# Benchmark protocol and evidence

## Latest paired probe

See [the Caps/STT sprint report](STT-CAPS-SPRINT.md) for the actual pinned public CZ/EN Base/Small CPU comparison, WER/CER, inference latency, worker peak RAM/CPU, source identity and raw evidence. This narrow probe does not qualify dictation quality or minimum hardware. `compare.py` enforces matching sample hashes/modes/repeats across models; `fetch_public.py` prepares the attributed public corpus.

## Earlier plumbing evidence

The Windows-targeted application builds and a self-contained win-x64 publish was produced on Linux during bootstrap. Ten tests pass: five scoring checks (edit counts, silence insertions, Czech diacritics, canonical Unicode, case/punctuation normalization) and five native input-boundary checks. The native CPU worker also builds on Linux and transcribes upstream's public 11-second `samples/jfk.wav` with the pinned multilingual base q5_1 model, through the real stdin/stdout adapter. This is a narrow English fixture smoke test, **not representative language quality, Windows runtime acceptance or minimum-hardware qualification**.

On 2026-11-21, [Windows hosted CI run 44950062466](https://github.com/beamj786-rgb/jl-voice/actions/runs/44950062466), source commit `af26d579be24d92dcd45f804241996207d457cb9`, passed the complete CPU package build, all ten scoring/native tests, actual CPU transcription of the public English fixture, and the 12-second tray startup/idle-monitor smoke test. This verifies native Windows build/runtime plumbing on the hosted runner. It does not test microphone recording, text insertion, network-disabled execution, Czech/mixed quality, Windows 11 target-device behavior, cold/warm performance, or representative 8 GB hardware. CI did not upload or publish a distributable package or performance report.

All primary-target Czech/English/mixed WER, RTF, app latency, memory, idle CPU, cold/warm startup and desktop-interference values are **unmeasured**. No representative 8 GB system is available in the current environment. Model residency comparisons and optional GPU measurements are also outstanding. Do not fill missing values with estimates.

The branded UI has a separate [hosted Windows CI idle probe and provenance](UI.md#verified-review--2026-11-21), with the [raw samples](ui-review/idle-ci.json). This 16 GB Server 2025 runner observation is not primary-target acceptance and includes no active speech or desktop-workload measurements.

## Reproducible ASR runs

On the developer/test machine only:

```powershell
python -m pip install -r benchmarks/requirements.txt
python -m unittest discover -s benchmarks -v
python benchmarks/run.py --runtime artifacts/JLVoice/runtime/JLVoiceAsr.exe --model artifacts/JLVoice/models/ggml-base-q5_1.bin --threads 2 --output benchmarks/results/base-q5-threads2.json
```

Record the phrases in `benchmarks/corpus.json` with actual Czech and English speakers. Supply mono 16 kHz 16-bit PCM WAVs at the listed paths, preserving actual spoken words in the reference. Include multiple speakers, short utterances, natural pauses, quiet/noisy rooms, accents, Czech diacritics, programming/product names, and annotated mid-recording switches. The seed phrases are a **smoke corpus specification, not recorded evidence or representative quality coverage**. Expand it before acceptance. Keep private recordings/results out of Git.

The harness refuses missing recordings, runs explicit CPU-only inference, measures process-family RSS/CPU every 20 ms and records model/runtime/audio hashes. Each process reloads its model. Repeats warm disk caches only. Groups report aggregate word-weighted WER separately for cs/en/mixed and selected language mode. Empty-reference silence reports hallucinated words, not a misleading zero WER. Failures never become successful scores. Normalization preserves diacritics and canonicalizes Unicode but ignores case/punctuation; review punctuation and software-name fidelity separately.

Repeat on actual primary 8 GB and secondary 16 GB devices, all three multilingual candidates and both 1/2-thread settings. Record CPU model, OS/build, power mode, physical RAM, microphone, app/runtime/model versions and competing activity. Do not reduce RAM or disable an RTX and call that a representative low-end CPU.

## Full application metrics

Close existing JL Voice instances before each run. Explicit diagnostics collect event names and timings only, not audio/text/window titles.

```powershell
python benchmarks/app.py --app artifacts/JLVoice/JLVoice.exe --seconds 30 --scenario idle --output benchmarks/results/app-idle.json
python benchmarks/app.py --app artifacts/JLVoice/JLVoice.exe --seconds 180 --scenario dictation-with-desktop-activity --output benchmarks/results/app-active.json
```

During the active run, dictate in Notepad/VS Code/browser fields while browsing, typing and scrolling. The monitor measures all owned processes, sampled RSS/CPU, launch-to-ready and stop-to-input-dispatch. A phase timestamp does not prove the receiving app inserted the text. Check visible text and insertion correctness separately. Summed working sets can double-count shared pages, sampling can miss peaks/exit CPU, and the report states these limitations. Use Windows performance tooling to collect OS peak counters and verify idle/active CPU measurements before acceptance.

Cold startup requires a documented reboot/cold-cache run. Warm startup is a separate repeated launch. Compare normal desktop activity with and without the app, including input/scroll latency and a repeatable foreground task. The monitor's scenario label is **not** a measurement of desktop interference. Record actual observations and timings separately. Compare unload-each-dictation with a measured experimental resident-worker implementation; the current implementation does not yet support the latter.

Before release, test with network disabled and on a clean Windows machine without development tools or additional runtimes. Inspect process modules and confirm no GPU backend/context is loaded. Test microphone removal, cancellation, short/silent/noisy recordings, shutdown, focus changes, held modifiers, elevated apps and rejected/partial text insertion.

## Czech decoding experiment (2026-11-23, run 45081113086, source ac5efc2)

Hosted Windows runner, CPU-only, 2 threads, below-normal priority, one fresh worker per recording (model load included in wall time). Public FLEURS dev read speech: 10 shortest Czech and 10 shortest English clips (3.0–4.7 s), plus concatenations of the same clips (cs 17.7 s / 45.2 s, en 17.1 s / 42.2 s) and two synthetic 3 s controls (digital silence, −60 dBFS noise; requested language cs). Not owner dictation, not mixed speech, not 8 GB hardware. This runner was ~2× slower than earlier runs on the same configs; WER also shifted slightly between runners (Small cs 49.5% → 51.6%), so compare configs only within one run.

Turbo = `ggml-large-v3-turbo-q5_0.bin`, ggerganov/whisper.cpp @5359861, 574 041 195 B, SHA-256 394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2. Short context = `audio_ctx = min(1500, floor(seconds×50)+128)` encoder frames (1500 = full 30 s window), so it has no effect on recordings ≥ ~27.4 s.

| Config | cs WER / CER (short) | en WER / CER (short) | cs-long WER / CER | en-long WER | Short clips: median / max wall, median RTF | 45 s cs wall (RTF) | Peak worker RAM |
|---|---|---|---|---|---|---|---|
| Small | 51.6% / 17.0% | 16.4% / 8.2% | 43.9% / 13.9% | 16.0% | 9.6 / 11.7 s, 2.41 | 37.8 s (0.83) | 553 MiB |
| Small + norm | 49.5% / 17.0% | 13.6% / 6.7% | 43.9% / 13.9% | 13.5% | 9.4 / 14.3 s, 2.37 | 37.4 s (0.83) | 552 MiB |
| Turbo + short | 25.3% / 10.5% | 15.5% / 7.2% | 18.9% / 6.8% | 15.4% | 9.9 / 17.0 s, 2.45 | 99.8 s (2.21) | 895 MiB |
| Turbo full | 24.2% / 8.5% | 12.7% / 5.8% | 18.2% / 6.7% | 15.4% | 42.6 / 43.6 s, 10.69 | 99.7 s (2.20) | 895 MiB |
| Turbo + norm + short | 26.3% / 10.5% | 16.4% / 8.4% | 18.2% / 6.7% | **39.1%** | 9.7 / 23.3 s, 2.45 | 98.7 s (2.18) | 895 MiB |

Controls (every config hallucinated; any word here is an error): Small/Small+norm: silence "[Mužické výstře]", noise "*zvukování*". Turbo+short: silence "Konec. Konec.", noise "Titulky vytvořil JohnyX.". Turbo full: "Titulky vytvořil JohnyX." for both. Turbo+norm+short: "Konec. Konec." / "Konec.".

Per-recording findings:
- Short context caused one complete miss: Czech clip cs-1578 (3.0 s) → "Konec." with short context, while full context produced (garbled) speech. Other Czech clips were identical or within one word between short and full context.
- Long recordings: turbo+short and turbo full produced the same long-form text (≥27.4 s uses the full window anyway); no repeated passages (max 1 inserted word). Small inserted 5 words on the 45 s Czech recording.
- Turbo + normalization dropped the first third of the 42 s English recording (50 deleted words, 1 substitution); the earlier run's CER 42.8% for this config was not inspected per sample (artifact unreachable) and did not reproduce here (CER 10.5%). Normalization is rejected: no Czech gain, one long-form omission.
- Czech prompt preset (earlier runs): small WER gain, 15+ hallucinated control words. Rejected.

Conclusion: turbo roughly halves Czech word errors vs Small on this public set, at ~1.6× Small's worker RAM and RTF ~2.2 on long recordings (45 s took ~100 s here); slower hardware would be slower still. It is an optional owner-test model only, not a default and not 8 GB-qualified. All configs insert text on silence/noise; a speech-presence guard is needed before release (not implemented in this round).
