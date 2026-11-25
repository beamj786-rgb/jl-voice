// Layered no-speech / hallucination guard for one dictation result.
// Pure functions without whisper.cpp dependencies so they can be unit-tested.
// Thresholds are fixed, reviewed constants; any change needs paired measurements.
#pragma once
#include <algorithm>
#include <cstdint>
#include <string>
#include <vector>

namespace jlvoice {

struct SegmentEvidence {
    std::string text;
    float no_speech_prob = 0;  // whisper_full_get_segment_no_speech_prob
    float avg_logprob = 0;     // mean log-probability of non-special tokens
    int tokens = 0;
};

enum class Verdict { Ok, NoSpeech, Suspicious };

struct GuardResult {
    Verdict verdict = Verdict::Ok;
    std::vector<std::string> reasons;
};

// OpenAI Whisper defaults, also used inside whisper.cpp for window skipping.
constexpr float kNoSpeechThreshold = 0.6f;
constexpr float kLowLogprob = -1.0f;
// Generous upper bound for real speech; Czech/English dictation is ~10-20 chars/s.
constexpr double kMaxCharsPerSecond = 30.0;
constexpr size_t kMaxTokenChars = 40;   // Longer than any common Czech/English word.
constexpr int kMaxUnitRepeats = 5;      // e.g. "cícícícící" (unit of 1-6 code points)
constexpr int kMaxWordRepeats = 3;      // the same word/2-3-word phrase 3x consecutively
constexpr double kMinWordsPerSpeechSecond = 0.5;  // 2-3 words/s is normal speech
constexpr double kMinSpeechForSparseCheck = 1.5;  // seconds

inline std::vector<std::u32string> words_of(const std::u32string & text) {
    std::vector<std::u32string> words;
    std::u32string current;
    for (char32_t c : text) {
        const bool separator = c == U' ' || c == U'\t' || c == U'\n' || c == U'\r';
        if (separator) { if (!current.empty()) { words.push_back(current); current.clear(); } }
        else current.push_back(c);
    }
    if (!current.empty()) words.push_back(current);
    return words;
}

inline std::u32string lower_letters(const std::u32string & word) {
    std::u32string out;
    for (char32_t c : word) {
        if (c == U'.' || c == U',' || c == U'!' || c == U'?' || c == U';' || c == U':' || c == U'"' || c == U'\'' ) continue;
        if (c >= U'A' && c <= U'Z') c = c - U'A' + U'a';
        out.push_back(c);
    }
    return out;
}

// Decode UTF-8 permissively; invalid bytes become U+FFFD (they are themselves suspicious).
inline std::u32string utf8_to_u32(const std::string & s) {
    std::u32string out;
    for (size_t i = 0; i < s.size();) {
        const unsigned char c = s[i];
        int n = c < 0x80 ? 1 : (c >> 5) == 6 ? 2 : (c >> 4) == 14 ? 3 : (c >> 3) == 30 ? 4 : 0;
        if (n == 0 || i + n > s.size()) { out.push_back(0xFFFD); ++i; continue; }
        char32_t cp = n == 1 ? c : n == 2 ? (c & 0x1F) : n == 3 ? (c & 0x0F) : (c & 0x07);
        bool valid = true;
        for (int k = 1; k < n; ++k) {
            const unsigned char cc = s[i + k];
            if ((cc & 0xC0) != 0x80) { valid = false; break; }
            cp = (cp << 6) | (cc & 0x3F);
        }
        if (!valid) { out.push_back(0xFFFD); ++i; continue; }
        out.push_back(cp); i += n;
    }
    return out;
}

// Longest consecutive repetition count of any unit of 1..6 code points.
inline int max_unit_repeats(const std::u32string & text) {
    int best = 1;
    for (size_t unit = 1; unit <= 6; ++unit) {
        for (size_t start = 0; start + unit <= text.size(); ++start) {
            int count = 1;
            size_t at = start + unit;
            while (at + unit <= text.size() && text.compare(at, unit, text, start, unit) == 0) { ++count; at += unit; }
            if (count > best) {
                // Only units containing a letter count: "....", "    " or "0000" are not decoder loops of speech.
                bool meaningful = false;
                for (size_t k = 0; k < unit; ++k) {
                    const char32_t c = text[start + k];
                    if ((c >= U'a' && c <= U'z') || (c >= U'A' && c <= U'Z') || (c > 0x7F && c != 0xFFFD)) meaningful = true;
                }
                if (meaningful) best = count;
            }
        }
    }
    return best;
}

// Longest consecutive repetition of an n-word phrase (n = 1..3), ignoring case/punctuation.
inline int max_phrase_repeats(const std::vector<std::u32string> & raw) {
    std::vector<std::u32string> w;
    for (const auto & x : raw) { auto y = lower_letters(x); if (!y.empty()) w.push_back(y); }
    int best = 1;
    for (size_t n = 1; n <= 3; ++n) {
        for (size_t start = 0; start + n <= w.size(); ++start) {
            int count = 1;
            size_t at = start + n;
            while (at + n <= w.size()) {
                bool same = true;
                for (size_t k = 0; k < n; ++k) if (w[at + k] != w[start + k]) { same = false; break; }
                if (!same) break;
                ++count; at += n;
            }
            if (count > best) best = count;
        }
    }
    return best;
}

// speech_detected: false only when a VAD ran and found no speech; true when VAD found speech
// or no VAD was used (then speech_seconds = audio length).
inline GuardResult evaluate(bool speech_detected, double speech_seconds, const std::vector<SegmentEvidence> & segments) {
    GuardResult result;
    if (!speech_detected) { result.verdict = Verdict::NoSpeech; result.reasons.push_back("vad_no_speech"); return result; }
    std::string joined;
    bool low_confidence = false;
    for (const auto & s : segments) {
        joined += s.text;
        if (s.tokens > 0 && (s.no_speech_prob > kNoSpeechThreshold || s.avg_logprob < kLowLogprob)) low_confidence = true;
    }
    const auto text = utf8_to_u32(joined);
    const auto words = words_of(text);
    size_t letters = 0;
    for (const auto & w : words) letters += lower_letters(w).size();
    if (letters == 0) { result.verdict = Verdict::NoSpeech; result.reasons.push_back("empty_transcript"); return result; }
    auto flag = [&](const char * reason) { result.verdict = Verdict::Suspicious; result.reasons.push_back(reason); };
    if (low_confidence) flag("low_confidence");
    for (char32_t c : text) if (c == 0xFFFD) { flag("invalid_text"); break; }
    size_t longest = 0;
    for (const auto & w : words) longest = (std::max)(longest, w.size());
    if (longest > kMaxTokenChars) flag("overlong_token");
    if (max_unit_repeats(text) >= kMaxUnitRepeats) flag("character_loop");
    if (max_phrase_repeats(words) >= kMaxWordRepeats) flag("word_loop");
    const double seconds = speech_seconds > 0.1 ? speech_seconds : 0.1;
    if (letters >= 20 && letters / seconds > kMaxCharsPerSecond) flag("too_long_for_audio");
    if (speech_seconds >= kMinSpeechForSparseCheck && words.size() / speech_seconds < kMinWordsPerSpeechSecond) flag("too_short_for_speech");
    return result;
}

inline const char * verdict_name(Verdict v) {
    return v == Verdict::Ok ? "ok" : v == Verdict::NoSpeech ? "no_speech" : "suspicious";
}

}  // namespace jlvoice
