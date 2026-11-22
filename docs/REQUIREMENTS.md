# Mandatory requirements and acceptance

This contract incorporates the owner's 2026-11-21 requirements update; it overrides conflicting assumptions in the original master prompt.

## Hardware and lifecycle

Primary: Windows 11 x64, 8 GB physical RAM, integrated graphics, modest multicore CPU, ordinary desktop applications running simultaneously. Secondary: 16 GB. CPU-only execution is the acceptance baseline. RTX 3060 and Ryzen 7 5700X results cannot qualify the primary target, and limiting GPU access is not a low-end CPU simulation.

Idle: near-zero CPU, minimal full-process-family RAM, no unnecessary GPU allocation, no permanent recording, no continuously running LLM. Bound CPU threads and inference resources. Measure lazy loading, retention and unloading rather than assuming that warm startup justifies permanent residency. The current preview unloads on every transcription; resident-model comparison is outstanding.

## Languages and model selection

Mandatory peers: Czech (`cs`), English (`en`) and mixed speech, including switching languages during a recording. Required language choices: Czech, English, Automatic. Test diacritics, punctuation in both languages, natural conversation, programming terms, product/software names and English technical terms inside Czech sentences. Automatic detection must be measured on short and mixed samples; availability of an `auto` flag is not evidence of accuracy.

Use quantized multilingual candidates and actual CPU inference. Choose based on Czech, English and mixed accuracy, inference latency, RAM, startup responsiveness, package size and background resource use. Never choose a larger model simply because the development machine can run it. A larger optional quality profile cannot be required for basic use, and English-only ASR is never the default.

## Reproducible evidence

Collect real recordings, immutable hashes, verbatim human-checked references, speaker/microphone/noise details and annotated language switches. Keep private audio out of Git and agent context. Separately report Czech WER, English WER and mixed quality; do not average away language failures. Review punctuation separately from normalized WER.

Measure actual CPU-only RTF, stop-to-text-insertion latency, cold and warm startup, peak/idle RAM, idle/transcription CPU and simultaneous desktop activity. Count all owned processes. Distinguish disk-cache warmth from model residency, dispatch from accepted text, sampled peaks from OS peak counters, synthetic tests from human recordings, and Linux/build-machine evidence from representative Windows hardware.

## Release gates — all currently unaccepted

- [ ] Reliable Czech dictation and punctuation, demonstrated with real recordings.
- [ ] Reliable English dictation and punctuation.
- [ ] Usable mixed speech, technical vocabulary and mid-recording switches.
- [ ] Short/mixed Automatic detection evaluated against explicit language modes.
- [ ] CPU-only runtime and no discrete GPU required, verified on Windows.
- [ ] Representative physical 8 GB low-end PC with integrated graphics tested during normal desktop work.
- [ ] Secondary 16 GB configuration evaluated.
- [ ] Portable clean-machine package with bundled runtime and model; no separate installation.
- [ ] Network-disabled operation tested; no accounts, usage limits or paid APIs.
- [ ] Microphone lifecycle and disconnect/cancel/shutdown recovery verified.
- [ ] Insertion verified in Notepad, VS Code, Chrome, Edge, ChatGPT/Claude fields and messaging apps; clipboard/focus preserved.
- [ ] Resource consumption, model residency trade-off and end-to-end latency documented with real results.
- [ ] Redistribution licenses and all packaged components reviewed.

If any gate is unmet, label the limitation and keep optimizing. No fabricated measurements or implied hardware qualification. Numerical quality/latency thresholds need explicit acceptance against the real corpus; previous aspirational idle CPU of 0–1% and unloaded idle RAM below 200 MB remain goals, not measured capabilities.

## Authorized NVIDIA evaluation exception — 2026-11-22

The owner explicitly authorized comparison of NVIDIA cloud endpoints and local Parakeet for speed and transcript quality. The owner subsequently authorized integrating this tested client into the WPF application as an explicit opt-in development mode, with an owner-supplied API key and consent before sending dictation or replay audio. It does not make cloud access, a user account, a paid API, Python, a GPU or telemetry a requirement of the JL Voice desktop app. Do not silently send ordinary dictation to the cloud or change the app's offline default. Cloud requests/latency must be measured, not inferred from mocks or local model results. Keys must never enter source, command-line arguments, CI logs, reports or chat.
