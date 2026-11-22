# Caps HOLD and STT quality sprint

## Audit of the starting implementation

Audited starting source: `f44fe7a3db1d7a0730026cab483e9193f0656e7d`. The owner's screenshot shows different text in Last transcript and the destination editor. This is evidence of an insertion discrepancy in that session, not proof of its exact cause. Recognition quality and text insertion must be evaluated separately.

| Boundary | Finding before this sprint | Action / proof boundary |
|---|---|---|
| Capture | WinMM `WaveInEvent`, explicitly mono 16 kHz signed PCM16, 50 ms callbacks; bounded 90 seconds; no application resampler | Kept. WAV serializer preserves sample values/order and finalizes lengths. Actual device conversion/selection still needs the in-app playback check. A clean Sound Recorder capture alone does not establish this path. |
| Worker input | Strict WAV format/RIFF bounds and signed-int16-to-float conversion | Kept, native boundary tests. Public English audio round-trips through the same NAudio serializer before native inference. |
| Runtime/model | Pinned whisper.cpp v1.9.5; quantized multilingual Base Q5_1 | Base retained as lightweight experimental profile. Comparison package also bundles Small Q5_1, selected in settings; no end-user dependency/model installation. Hash and byte-length checks use pinned upstream metadata. |
| Language | Explicit cs/en/auto; Auto detects one recording, not per-word switching | Kept and paired in benchmarks. No claim of reliable short/mixed Auto detection. |
| Decoding | Beam search 5; upstream defaults no_context=true, suppress_blank=true, temperature=0, fallback increment=0.2 | Explicitly pin these existing settings. No quality gain attributed to unchanged defaults. |
| VAD/segmentation | VAD off, no Silero model, utterance bounded by user key and 90-second cap; whisper internally segments | Kept off; no unmeasured VAD/filter introduced. Silence/noise hallucinations remain a quality obligation. |
| Postprocessing | Trim only, no LLM, grammar rewriting or diacritic stripping | Kept. WER/CER normalization is evaluation-only; punctuation review is separate. |
| Insertion | Large Unicode SendInput burst; foreground/native control check only before sending | Paced Unicode scalars, own event tag, target/modifier checks before each scalar and cancellation between scalars. Clipboard untouched. Hosted native edit-control checks added. Modern Notepad/browser failure is not claimed reproduced or fixed without owner retest. |
| Caps | Low-level hook, immediate start, no tap threshold; no system-session cancellation | Dedicated monotonic state machine, 250 ms default, configurable 100–1000 ms, tap toggle via tagged SendInput; repeat/generation guards, session/power cancellation. |
| Diagnostics | Event times only; no separate inference time or same-audio rerun | Worker reports duration, detected language, load/inference times, OS worker peak RAM/CPU when available. App shows model and separate times. Optional memory-only retained clip can be retranscribed with another model/language. Numeric/category errors only; no user audio/text in logs. |

## Caps lifecycle and races

`IDLE → PENDING → RECORDING → TRANSCRIBING → IDLE`. A physical Caps down is consumed, stores monotonic time and a cheap original-window/control snapshot, and posts lightweight pending setup to the UI. Even if the UI callback is delayed, switching windows cannot silently retarget capture; missing text focus does not suppress a short tap. A one-shot UI timer starts capture only while the same generation is held and the original native target remains focused. Hook callbacks never open the microphone or run STT. A physical Caps up is consumed; a short committed tap sends one tagged Caps down/up pair. Own injected events and other injected input pass through; unrelated physical keys are never suppressed.

Long holds send no Caps toggle, so the previous toggle state is not rewritten. Shift is not swallowed; insertion refuses held modifiers. Auto-repeat cannot create another pending/recording session. A completed stop is tied to the actual capture object, so a later rapid key press cannot drop that stop or stop a different recording. Cancellation invalidates timer/start generations and disarms the current physical hold until release. Session lock/disconnect, suspend, graceful shutdown, microphone/worker errors and cancellation clean up owned work; resume does not treat an existing held key as a fresh press.

At exactly the threshold, if key-up is processed first, the timer cannot start capture after release. A long press with a UI timer that never ran records nothing and does not toggle Caps. If the timer starts first, key-up ends that recording; recordings under 0.5 seconds return the existing short-recording error. This fail-safe is preferable to opening a microphone after release; physical scheduling/UX still needs testing. A key-up lost on the secure desktop may consume the first following press/release before readiness recovers.

If hook registration fails, the localized error directs the user to the bubble or Ctrl+Alt+F9 fallback. No security settings, keyboard registry mapping or permanent Caps disable is installed. Other global-hook conflicts and silent hook removal by Windows remain real-desktop checks.

## Paired quality methodology

`benchmarks/fetch_public.py` fetches a pinned Google FLEURS development subset (CC-BY-4.0, Google and contributors): three shortest distinct prompts per cs_cz/en_us with at least three seconds, selected before evaluating output. These are short read-speech samples, some with unusual names; they are **not representative Czech/English acceptance**. Source and converted audio hashes, prompt IDs, references, revision and conversion are recorded. Float32 WAV is converted deterministically to PCM16 at the same 16 kHz rate; no resampling. Nothing is synthesized or labeled as the owner's recording.

`benchmarks/compare.py` runs all models on exactly the same hashes, modes and repeats with CPU-only inference and two below-normal-priority threads. Reports contain word-weighted WER and character-weighted CER separately for cs/en and Auto/explicit mode; diacritics matter, NFC/case normalization ignores punctuation. They also include true worker inference-only time, model load time, process wall time, RTF, sampled family RSS/CPU and OS worker peak/CPU counters captured immediately after inference when available. An unavailable value is null. Samples and model/runtime hashes remain in each raw report. Sequential runs have uncontrolled OS cache state. Input dispatch, physical microphone, clean offline Windows, desktop interference, cold-cache startup and 8 GB acceptance are not measured by this probe.

```powershell
python benchmarks/fetch_public.py --output benchmarks/audio/public
python benchmarks/compare.py --runtime artifacts/JLVoice/runtime/JLVoiceAsr.exe --model artifacts/JLVoice/models/ggml-base-q5_1.bin --model artifacts/JLVoice/models/ggml-small-q5_1.bin --corpus benchmarks/audio/public/corpus.json --threads 2 --repeats 2 --output benchmarks/results/paired
```

Python is a developer benchmark dependency only; the portable app includes .NET, native runtime and both models. Medium Q5_0 is listed in the pinned manifest as benchmark-only, excluded from the app model selector/package. The generic comparison harness accepts a separately integrity-verified multilingual Medium path if an adequately resourced test machine and time budget are available. Medium is not a default or requirement; no Medium quality/performance claim is made without a run.

## Verification status

- Local WPF and desktop-check project compilation: passed during implementation.
- Deterministic PCM/hold/settings/error checks: passed during implementation, including threshold edges, duplicate release, 100 rapid taps, repeat, busy tap/hold, configured threshold, cancellation generation and recovery.
- Native input boundary and WER/CER scoring checks: passed locally with the actual Linux worker.
- [Hosted Windows run 45051175776](https://github.com/beamj786-rgb/jl-voice/actions/runs/45051175776), source `3ccc8aa39e1bac283a7ca8f0918cd24ba7c058d1`: package/publish, 34 core checks, 11 scoring/native boundary tests, native edit-control exact Unicode/clipboard/focus/cancellation checks, tagged Caps toggle checks, paired CZ/EN probe, idle monitoring, and model-selector/retention/layout UI checks passed. This is a hosted Windows Server runner with four logical CPUs and approximately 16 GB RAM; it is not physical Windows 11 or 8 GB qualification. Later source changes require their own build gate.
- **Not performed:** physical keyboard/hook timing, Caps+Shift with actual devices, Notepad/browser acceptance, global-hook conflicts, session lock/sleep on a user desktop, microphone device faults, clean offline Windows 11 and representative 8 GB hardware. No production release qualification.

## Remaining limitations

Base/Small remain experimental. Automatic language switching, Czech punctuation, natural/mixed speech and silence/noise need broader real recordings. Native target guards cannot distinguish browser DOM fields sharing a native HWND. Paced insertion adds about 4 ms per Unicode scalar plus scheduling overhead; focus changes or cancellation may leave partial text. No clipboard paste fallback or password-field detection is added. Memory-only developer replay explicitly retains one bounded clip until replaced, unchecked or exit and is off on relaunch.

Smart App Control trust is unchanged: original binaries and some vendor DLLs remain unsigned. Win32 4551 is handled without asking users to weaken protection. A trusted signed chain and actual enabled-policy test are still needed for blocked PCs; Foundation eligibility enquiry is pending. No production build is replaced and no release/deployment is created.

## Measured Windows paired probe

Raw [reports and corpus provenance](benchmark-evidence/2026-11-22/) preserve the exact runtime/model/audio hashes and source BUILD-INFO. Six distinct recordings, three per language, one run per model/mode, CPU-only, two worker threads, below-normal priority. WER/CER are error rates: lower is better. RAM is OS-reported worker peak working set after inference; it excludes the WPF process. Times below are median inference only, excluding model load, playback and insertion.

| Model | Mode | WER | CER | Median inference | Peak worker RAM |
|---|---|---:|---:|---:|---:|
| ggml-base-q5_1.bin | cs/auto | 90.0% | 44.7% | 2.63 s | 213.2 MiB |
| ggml-base-q5_1.bin | cs/cs | 90.0% | 44.7% | 1.60 s | 212.2 MiB |
| ggml-base-q5_1.bin | en/auto | 35.5% | 12.9% | 2.28 s | 212.1 MiB |
| ggml-base-q5_1.bin | en/en | 35.5% | 12.9% | 1.32 s | 211.9 MiB |
| ggml-small-q5_1.bin | cs/auto | 80.0% | 29.5% | 8.20 s | 477.5 MiB |
| ggml-small-q5_1.bin | cs/cs | 80.0% | 29.5% | 4.40 s | 477.9 MiB |
| ggml-small-q5_1.bin | en/auto | 25.8% | 11.0% | 8.21 s | 477.9 MiB |
| ggml-small-q5_1.bin | en/en | 25.8% | 11.0% | 4.36 s | 477.6 MiB |

Small improves these error rates but remains poor on the selected Czech clips and costs more CPU time/memory. This does not justify changing the default or claiming usable Czech dictation. Auto added latency on these short clips and had no aggregate WER improvement. Only 20 Czech and 31 English reference words were evaluated; unusual names, short-clip selection and tokenization (e.g. WiFi versus Wi-Fi) make this probe unsuitable as a general accuracy estimate. Broader natural/mixed speech and silence/noise evaluation remain open. No Medium run or minimum-hardware acceptance is claimed.

## Changed files

- `.github/workflows/build.yml`
- `README.md`
- `benchmarks/compare.py`
- `benchmarks/fetch_public.py`
- `benchmarks/run.py`
- `benchmarks/score.py`
- `benchmarks/test_score.py`
- `docs/PRIVACY.md`
- `docs/STT-CAPS-SPRINT.md`
- `docs/TESTING.md`
- `models/manifest.json`
- `native/worker.cpp`
- `scripts/package.ps1`
- `src/JLVoice/AsrEngine.cs`
- `src/JLVoice/CapsHoldState.cs`
- `src/JLVoice/Diagnostics.cs`
- `src/JLVoice/MainWindow.Caps.cs`
- `src/JLVoice/MainWindow.Replay.cs`
- `src/JLVoice/MainWindow.Review.cs`
- `src/JLVoice/MainWindow.xaml`
- `src/JLVoice/MainWindow.xaml.cs`
- `src/JLVoice/Program.cs`
- `src/JLVoice/Settings.cs`
- `src/JLVoice/WindowsInput.cs`
- `tests/JLVoice.CoreChecks/JLVoice.CoreChecks.csproj`
- `tests/JLVoice.CoreChecks/Program.cs`
- `tests/JLVoice.DesktopChecks/JLVoice.DesktopChecks.csproj`
- `tests/JLVoice.DesktopChecks/Program.cs`
- `docs/benchmark-evidence/2026-11-22/`: raw paired reports, corpus provenance and build identity.

## Additional decoding probe

A temporary Small build with `params.no_timestamps=true` was run on the same six audio hashes on Linux (two-thread CPU quota, 8 GiB container memory limit; not representative physical hardware). Czech WER remained 80% and English WER was 35.5% in that probe. Raw results and the exact one-line experimental configuration/source hash are retained with the evidence. This supplied no basis for changing the production decoding defaults; the experimental flag is not shipped. Windows and Linux latency figures are not combined.
