// Unit tests for transcript_guard.h (no model required). Exit code 0 = all passed.
#include "transcript_guard.h"
#include <cstdio>

using namespace jlvoice;
static int failures = 0;
static void check(bool ok, const char * name) { std::printf("%s: %s\n", ok ? "PASS" : "FAIL", name); if (!ok) ++failures; }
static SegmentEvidence seg(const char * text, float no_speech = 0.05f, float logprob = -0.3f) { return SegmentEvidence{text, no_speech, logprob, 8}; }
static bool has(const GuardResult & r, const char * reason) { for (auto & x : r.reasons) if (x == reason) return true; return false; }

int main() {
    // Real Czech/English dictation-style sentences (and observed good outputs) must pass.
    for (const char * ok : {"Většina života hebrejské rodiny se odehrávala pod širým nebem.",
                            "Příští čtvrtek otevřu Visual Studio Code a dokončím nastavení aplikace.",
                            "Police said that the body appeared to have been there for about a day.",
                            "Nejneobhospodařovávatelnějšími pozemky jsou ty v horách.",
                            "Cena je 1000000 korun... a to je vše."}) {
        auto r = evaluate(true, 4.5, {seg(ok)});
        check(r.verdict == Verdict::Ok, ok);
    }
    check(evaluate(false, 0, {}).verdict == Verdict::NoSpeech, "VAD without speech is no_speech, never text");
    check(evaluate(true, 3.0, {seg("  ...  ")}).verdict == Verdict::NoSpeech, "punctuation-only output is no_speech");
    check(evaluate(true, 3.0, {}).verdict == Verdict::NoSpeech, "no segments is no_speech");
    // Observed failures from the 2026-11-23 experiment (docs/benchmark-evidence/2026-11-23-czech-decoding).
    auto loop = evaluate(true, 3.0, {seg("Titulky vytvořil Jirka Kvělecícícícícícícícícícícícícícícícícícícícícícícícícícící")});
    check(loop.verdict == Verdict::Suspicious && has(loop, "character_loop") && has(loop, "overlong_token"), "cs-1578 character loop is suspicious");
    auto words = evaluate(true, 3.0, {seg("Titulky vytvořilý vytvořil vytvořil vytvořil vytvořil.")});
    check(words.verdict == Verdict::Suspicious && has(words, "word_loop"), "cs-1578 word loop is suspicious");
    auto sparse = evaluate(true, 2.6, {seg("Konec.")});
    check(sparse.verdict == Verdict::Suspicious && has(sparse, "too_short_for_speech"), "one word for 2.6 s of speech is suspicious");
    check(evaluate(true, 0.8, {seg("Ano.")}).verdict == Verdict::Ok, "a genuinely short reply stays ok");
    auto phrase = evaluate(true, 6.0, {seg("Sperba a nunpřezalakaseng. Sperba a nunpřezalakaseng. Sperba a nunpřezalakaseng.")});
    check(phrase.verdict == Verdict::Suspicious && has(phrase, "word_loop"), "repeated 3-word phrase is suspicious");
    auto dense = evaluate(true, 1.0, {seg("Toto je velmi dlouhý text, který by nikdo neřekl za jednu sekundu řeči.")});
    check(dense.verdict == Verdict::Suspicious && has(dense, "too_long_for_audio"), "text far longer than the speech is suspicious");
    auto unsure = evaluate(true, 3.0, {seg("Titulky vytvořil JohnyX.", 0.8f, -1.4f)});
    check(unsure.verdict == Verdict::Suspicious && has(unsure, "low_confidence"), "low-confidence segment is suspicious");
    check(evaluate(true, 3.0, {seg("Dobrý den, jak se máte?", 0.2f, -0.9f)}).verdict == Verdict::Ok, "confidence just inside limits stays ok");
    check(evaluate(true, 3.0, {seg("abc\xff def")}).verdict == Verdict::Suspicious, "invalid UTF-8 is suspicious");
    check(max_unit_repeats(utf8_to_u32("haha")) == 2 && max_unit_repeats(utf8_to_u32("0000000")) == 1, "unit repeat counting ignores digits");
    check(evaluate(true, 4.0, {seg("Ne, ne. To ne.")}).verdict == Verdict::Ok, "two repeats are not a loop");
    std::printf("%d failure(s)\n", failures);
    return failures == 0 ? 0 : 1;
}
