#include "whisper.h"
#include "ggml-backend.h"
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
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

int run(int argc, char ** argv) {
    std::string model, language = "auto";
    int threads = 2;
    bool cpu_only = false;
    for (int i = 1; i < argc; ++i) {
        const std::string key = argv[i];
        if (key == "--cpu-only") cpu_only = true;
        else if (i + 1 < argc && key == "--model") model = argv[++i];
        else if (i + 1 < argc && key == "--language") language = argv[++i];
        else if (i + 1 < argc && key == "--threads") {
            const std::string value = argv[++i];
            if (value != "1" && value != "2") throw std::runtime_error("Threads must be 1 or 2");
            threads = value == "1" ? 1 : 2;
        } else throw std::runtime_error("Unknown or incomplete worker option");
    }
    if (!cpu_only || model.empty() || (language != "cs" && language != "en" && language != "auto"))
        throw std::runtime_error("Required: --cpu-only --model PATH [--language cs|en|auto] [--threads 1|2]");
    auto audio = read_audio();
    ggml_backend_load_all();
    auto context_params = whisper_context_default_params();
    context_params.use_gpu = false;
    std::unique_ptr<whisper_context, decltype(&whisper_free)> context(
        whisper_init_from_file_with_params(model.c_str(), context_params), &whisper_free);
    if (!context) throw std::runtime_error("Cannot load multilingual model");
    if (!whisper_is_multilingual(context.get())) throw std::runtime_error("English-only models are not supported");
    auto params = whisper_full_default_params(WHISPER_SAMPLING_BEAM_SEARCH);
    params.beam_search.beam_size = 5;
    params.n_threads = threads;
    params.language = language.c_str();
    params.translate = false;
    params.print_realtime = false;
    params.print_progress = false;
    params.print_timestamps = false;
    params.print_special = false;
    if (whisper_full(context.get(), params, audio.data(), int(audio.size())) != 0)
        throw std::runtime_error("Speech recognition failed");
    for (int i = 0; i < whisper_full_n_segments(context.get()); ++i)
        std::cout << whisper_full_get_segment_text(context.get(), i);
    std::cout << '\n';
    if (!std::cout) throw std::runtime_error("Cannot write transcript");
    return 0;
}
}

int main(int argc, char ** argv) {
    try { return run(argc, argv); }
    catch (const std::exception & e) { std::cerr << "JL Voice ASR: " << e.what() << '\n'; return 1; }
}
