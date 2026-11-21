# Benchmark protocol and evidence

## Current evidence

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
