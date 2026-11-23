#include "whisper.h"
#include "ggml-backend.h"
#include "transcript_guard.h"
#include <algorithm>
#include <cmath>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <memory>
#include <sstream>
#include <cstdlib>
#include <stdexcept>
#include <string>
#include <vector>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#include <windows.h>
#include <psapi.h>
#else
#include <sys/resource.h>
#endif

namespace {
constexpr size_t max_pcm = 16000 * 2 * 90;
constexpr size_t max_wav = max_pcm + 65536;
uint16_t u16(const uint8_t * p) { return uint16_t(p[0]) | uint16_t(p[1]) << 8; }
uint32_t u32(const uint8_t * p) { return uint32_t(u16(p)) | uint32_t(u16(p + 2)) << 16; }

std::vector<float> read_audio() {
#ifdef _WIN32
    if (_setmode(_fileno(stdin), _O_BINARY) == -1) throw std::runtime_error("Cannot set binary audio input");
#endif
    std::vector<uint8_t> wav;
    uint8_t buffer[8192];
    for (size_t n; (n = std::fread(buffer, 1, sizeof buffer, stdin)) > 0;) {
        if (wav.size() + n > max_wav) throw std::runtime_error("Audio exceeds the 90-second preview limit");
        wav.insert(wav.end(), buffer, buffer + n);
    }
    if (std::ferror(stdin)) throw std::runtime_error("Cannot read audio");
    if (wav.size() < 12 || std::memcmp(wav.data(), "RIFF", 4) || std::memcmp(wav.data() + 8, "WAVE", 4))
        throw std::runtime_error("Expected a PCM WAV");
    if (uint64_t(u32(wav.data() + 4)) + 8 != wav.size()) throw std::runtime_error("Invalid RIFF length");
    bool format = false, have_data = false;
    size_t data_offset = 0, data_length = 0;
    size_t at = 12;
    for (; at + 8 <= wav.size();) {
        const uint8_t * chunk = wav.data() + at;
        const size_t length = u32(chunk + 4);
        at += 8;
        if (length > wav.size() - at) throw std::runtime_error("Truncated WAV chunk");
        if (!std::memcmp(chunk, "fmt ", 4)) {
            if (format || length < 16 || u16(wav.data() + at) != 1 || u16(wav.data() + at + 2) != 1 ||
                u32(wav.data() + at + 4) != 16000 || u32(wav.data() + at + 8) != 32000 ||
                u16(wav.data() + at + 12) != 2 || u16(wav.data() + at + 14) != 16)
                throw std::runtime_error("Expected mono 16 kHz 16-bit PCM WAV");
            format = true;
        } else if (!std::memcmp(chunk, "data", 4)) {
            if (have_data) throw std::runtime_error("Duplicate audio data");
            data_offset = at; data_length = length; have_data = true;
        }
        at += length + (length & 1);
        if (at > wav.size()) throw std::runtime_error("Missing WAV padding");
    }
    if (at != wav.size()) throw std::runtime_error("Truncated WAV chunk header");
    if (!format || !have_data || data_length < 16000 || data_length > max_pcm || data_length % 2)
        throw std::runtime_error("Expected 0.5 to 90 seconds of PCM audio");
    std::vector<float> samples(data_length / 2);
    for (size_t i = 0; i < samples.size(); ++i) {
        const auto value = u16(wav.data() + data_offset + i * 2);
        const int signed_value = value >= 32768 ? int(value) - 65536 : int(value);
        samples[i] = signed_value / 32768.0f;
    }
    return samples;
}

// Fixed, reviewed UTF-8 prompt presets. Arbitrary prompt text is not accepted
// (Windows argv is not UTF-8; free text could also inject content).
const char * prompt_preset(const std::string & id) {
    if (id == "cs-dictation") return /* Czech dictation-style sentence, UTF-8 escaped for MSVC */
        "Dobr\xc3\xbd" " den, tohle je diktov\xc3\xa1" "n\xc3\xad" " v \xc4\x8d" "e\xc5\xa1" "tin\xc4\x9b" " s diakritikou a interpunkc\xc3\xad" ".";
    throw std::runtime_error("Unknown prompt preset");
}

// Peak normalization toward -6 dBFS, gain capped at +30 dB so near-silence is not
// turned into loud noise. Returns the applied linear gain.
float normalize_peak(std::vector<float> & audio) {
    float peak = 0;
    for (float v : audio) peak = (std::max)(peak, std::abs(v));
    if (peak <= 0) return 1.0f;
    const float gain = (std::min)(0.5f / peak, 31.62f);
    if (gain <= 1.0f) return 1.0f;
    for (float & v : audio) v *= gain;
    return gain;
}

void process_usage(uint64_t & peak_rss, double & cpu_ms) {
    peak_rss = 0; cpu_ms = -1;
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS memory{};
    if (GetProcessMemoryInfo(GetCurrentProcess(), &memory, sizeof memory)) peak_rss = memory.PeakWorkingSetSize;
    FILETIME created, exited, kernel, user;
    if (GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user)) {
        ULARGE_INTEGER k{}, u{}; k.LowPart = kernel.dwLowDateTime; k.HighPart = kernel.dwHighDateTime;
        u.LowPart = user.dwLowDateTime; u.HighPart = user.dwHighDateTime;
        cpu_ms = (k.QuadPart + u.QuadPart) / 10000.0;
    }
#else
    rusage usage{};
    if (getrusage(RUSAGE_SELF, &usage) == 0) {
        peak_rss = uint64_t(usage.ru_maxrss) * 1024;
        cpu_ms = (usage.ru_utime.tv_sec + usage.ru_stime.tv_sec) * 1000.0 + (usage.ru_utime.tv_usec + usage.ru_stime.tv_usec) / 1000.0;
    }
#endif
}

std::string number_or_null(bool present, double value) {
    if (!present || !std::isfinite(value)) return "null";
    std::ostringstream out; out.precision(6); out << value; return out.str();
}

// Result of the Silero VAD gate (null fields when no VAD model was given).
struct VadResult {
    bool ran = false, speech = true;
    int segments = 0;
    double speech_seconds = 0, first_s = 0, last_s = 0, ms = 0;
};

// vad_normalize: the VAD (only) sees a peak-normalized copy (gain capped at +30 dB); Whisper never does.
VadResult run_vad(const std::string & path, const std::vector<float> & original, int threads, float threshold, bool vad_normalize) {
    VadResult result; result.ran = true;
    std::vector<float> copy;
    if (vad_normalize) { copy = original; normalize_peak(copy); }
    const std::vector<float> & audio = vad_normalize ? copy : original;
    const auto start = std::chrono::steady_clock::now();
    auto context_params = whisper_vad_default_context_params();
    context_params.use_gpu = false;   // CPU-only contract.
    context_params.n_threads = threads;
    std::unique_ptr<whisper_vad_context, decltype(&whisper_vad_free)> vad(
        whisper_vad_init_from_file_with_params(path.c_str(), context_params), &whisper_vad_free);
    if (!vad) throw std::runtime_error("Cannot load voice activity model");
    auto params = whisper_vad_default_params();   // threshold 0.5, min speech 250 ms, min silence 100 ms
    params.threshold = threshold;
    std::unique_ptr<whisper_vad_segments, decltype(&whisper_vad_free_segments)> segments(
        whisper_vad_segments_from_samples(vad.get(), params, audio.data(), int(audio.size())), &whisper_vad_free_segments);
    if (!segments) throw std::runtime_error("Voice activity detection failed");
    result.segments = whisper_vad_segments_n_segments(segments.get());
    for (int i = 0; i < result.segments; ++i) {
        const double t0 = whisper_vad_segments_get_segment_t0(segments.get(), i) / 100.0;  // centiseconds
        const double t1 = whisper_vad_segments_get_segment_t1(segments.get(), i) / 100.0;
        if (i == 0) result.first_s = t0;
        result.last_s = t1;
        result.speech_seconds += (std::max)(0.0, t1 - t0);
    }
    result.speech = result.segments > 0 && result.speech_seconds > 0;
    result.ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
    return result;
}

int run(int argc, char ** argv) {
    std::string model, language = "auto", prompt, vad_model;
    int threads = 2;
    float vad_threshold = 0.5f;
    bool cpu_only = false, normalize = false, short_context = false, vad_only = false, vad_normalize = false;
    for (int i = 1; i < argc; ++i) {
        const std::string key = argv[i];
        if (key == "--cpu-only") cpu_only = true;
        else if (key == "--normalize") normalize = true;
        else if (key == "--short-context") short_context = true;
        else if (key == "--vad-only") vad_only = true;
        else if (key == "--vad-normalize") vad_normalize = true;
        else if (i + 1 < argc && key == "--vad-model") vad_model = argv[++i];
        else if (i + 1 < argc && key == "--vad-threshold") {
            const std::string value = argv[++i];
            char * end = nullptr; const double parsed = std::strtod(value.c_str(), &end);
            if (end == value.c_str() || *end || !(parsed >= 0.05 && parsed <= 0.95)) throw std::runtime_error("VAD threshold must be 0.05..0.95");
            vad_threshold = float(parsed);
        }
        else if (i + 1 < argc && key == "--prompt-preset") prompt = prompt_preset(argv[++i]);
        else if (i + 1 < argc && key == "--model") model = argv[++i];
        else if (i + 1 < argc && key == "--language") language = argv[++i];
        else if (i + 1 < argc && key == "--threads") {
            const std::string value = argv[++i];
            if (value != "1" && value != "2") throw std::runtime_error("Threads must be 1 or 2");
            threads = value == "1" ? 1 : 2;
        } else throw std::runtime_error("Unknown or incomplete worker option");
    }
    if (!cpu_only || (model.empty() && !vad_only) || (language != "cs" && language != "en" && language != "auto"))
        throw std::runtime_error("Required: --cpu-only --model PATH [--language cs|en|auto] [--threads 1|2] [--vad-model PATH]");
    if (vad_only && vad_model.empty()) throw std::runtime_error("--vad-only requires --vad-model");
    auto audio = read_audio();
    const double audio_seconds = audio.size() / 16000.0;
    ggml_backend_load_all();
    // Layer 1: the VAD sees the original (never normalized) audio. It only gates; audio is not trimmed.
    VadResult vad;
    if (!vad_model.empty()) vad = run_vad(vad_model, audio, threads, vad_threshold, vad_normalize);
    const float gain = normalize ? normalize_peak(audio) : 1.0f;

    std::vector<jlvoice::SegmentEvidence> evidence;
    std::string transcript, detected;
    bool whisper_ran = false;
    double load_ms = 0, inference_ms = 0;
    int audio_ctx = 0;
    if (vad.speech && !vad_only) {
        const auto load_start = std::chrono::steady_clock::now();
        auto context_params = whisper_context_default_params();
        context_params.use_gpu = false;
        std::unique_ptr<whisper_context, decltype(&whisper_free)> context(
            whisper_init_from_file_with_params(model.c_str(), context_params), &whisper_free);
        if (!context) throw std::runtime_error("Cannot load multilingual model");
        if (!whisper_is_multilingual(context.get())) throw std::runtime_error("English-only models are not supported");
        const auto decode_start = std::chrono::steady_clock::now();
        auto params = whisper_full_default_params(WHISPER_SAMPLING_BEAM_SEARCH);
        params.beam_search.beam_size = 5;
        params.n_threads = threads;
        params.language = language.c_str();
        params.translate = false;
        // Explicit pinned defaults, not an unmeasured decoding-profile change.
        params.no_context = true;
        params.vad = false;
        params.suppress_blank = true;
        params.temperature = 0.0f;
        params.temperature_inc = 0.2f;
        if (!prompt.empty()) params.initial_prompt = prompt.c_str();
        // Encoder frames are 50/s; a bounded margin keeps short dictation from paying for 30 s.
        if (short_context) params.audio_ctx = (std::min)(1500, int(audio_seconds * 50) + 128);
        params.print_realtime = false;
        params.print_progress = false;
        params.print_timestamps = false;
        params.print_special = false;
        if (whisper_full(context.get(), params, audio.data(), int(audio.size())) != 0)
            throw std::runtime_error("Speech recognition failed");
        const auto decode_end = std::chrono::steady_clock::now();
        whisper_ran = true;
        load_ms = std::chrono::duration<double, std::milli>(decode_start - load_start).count();
        inference_ms = std::chrono::duration<double, std::milli>(decode_end - decode_start).count();
        audio_ctx = params.audio_ctx;
        detected = whisper_lang_str(whisper_full_lang_id(context.get()));
        const whisper_token eot = whisper_token_eot(context.get());
        for (int i = 0; i < whisper_full_n_segments(context.get()); ++i) {
            jlvoice::SegmentEvidence segment;
            segment.text = whisper_full_get_segment_text(context.get(), i);
            segment.no_speech_prob = whisper_full_get_segment_no_speech_prob(context.get(), i);
            double sum = 0;
            for (int t = 0; t < whisper_full_n_tokens(context.get(), i); ++t) {
                const auto token = whisper_full_get_token_data(context.get(), i, t);
                if (token.id >= eot) continue;   // timestamps/special tokens are not text
                sum += token.plog; ++segment.tokens;
            }
            segment.avg_logprob = segment.tokens ? float(sum / segment.tokens) : 0.0f;
            transcript += segment.text;
            evidence.push_back(segment);
        }
    }
    // Layers 2 and 3: decoder confidence and loop/length plausibility.
    const double speech_seconds = vad.ran ? vad.speech_seconds : audio_seconds;
    const auto guard = jlvoice::evaluate(vad.speech, speech_seconds, evidence);
    uint64_t peak_rss; double cpu_ms;
    process_usage(peak_rss, cpu_ms);
    std::ostringstream m;
    m << "JLVOICE_METRICS {\"load_ms\":" << number_or_null(whisper_ran, load_ms)
      << ",\"inference_ms\":" << number_or_null(whisper_ran, inference_ms)
      << ",\"audio_seconds\":" << audio_seconds
      << ",\"applied_gain\":" << gain << ",\"prompt\":" << (prompt.empty() ? "false" : "true")
      << ",\"audio_ctx\":" << audio_ctx
      << ",\"detected_language\":" << (whisper_ran ? "\"" + detected + "\"" : std::string("null"))
      << ",\"vad\":" << (vad.ran ? "true" : "false")
      << ",\"vad_normalize\":" << (vad_normalize ? "true" : "false")
      << ",\"vad_threshold\":" << number_or_null(vad.ran, vad_threshold)
      << ",\"vad_ms\":" << number_or_null(vad.ran, vad.ms)
      << ",\"vad_segments\":" << (vad.ran ? std::to_string(vad.segments) : "null")
      << ",\"vad_speech_seconds\":" << number_or_null(vad.ran, vad.speech_seconds)
      << ",\"vad_first_s\":" << number_or_null(vad.ran && vad.speech, vad.first_s)
      << ",\"vad_last_s\":" << number_or_null(vad.ran && vad.speech, vad.last_s)
      << ",\"verdict\":\"" << (vad_only ? (vad.speech ? "speech" : "no_speech") : jlvoice::verdict_name(guard.verdict)) << "\",\"reasons\":[";
    for (size_t i = 0; i < guard.reasons.size(); ++i) m << (i ? "," : "") << '"' << guard.reasons[i] << '"';
    m << "],\"segments\":[";
    for (size_t i = 0; i < evidence.size(); ++i)
        m << (i ? "," : "") << "{\"no_speech\":" << number_or_null(true, evidence[i].no_speech_prob)
          << ",\"avg_logprob\":" << number_or_null(evidence[i].tokens > 0, evidence[i].avg_logprob) << ",\"tokens\":" << evidence[i].tokens << '}';
    m << "],\"peak_rss_bytes\":" << (peak_rss ? std::to_string(peak_rss) : "null")
      << ",\"process_cpu_ms\":" << (cpu_ms >= 0 ? std::to_string(cpu_ms) : "null") << "}\n";
    std::cerr << m.str();
    // The transcript is printed even when suspicious (the app shows it for manual review);
    // nothing is printed when no speech was detected.
    if (guard.verdict != jlvoice::Verdict::NoSpeech) std::cout << transcript;
    std::cout << '\n';
    if (!std::cout) throw std::runtime_error("Cannot write transcript");
    return 0;
}
}

int main(int argc, char ** argv) {
    try { return run(argc, argv); }
    catch (const std::exception & e) { std::cerr << "JL Voice ASR: " << e.what() << '\n'; return 1; }
}
