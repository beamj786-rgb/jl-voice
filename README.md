# JL Voice

Offline Czech and English dictation for Windows 11 x64. Designed first for ordinary 8 GB PCs with integrated graphics and modest CPUs; 16 GB is the secondary target. Dedicated graphics are optional, never required.

**Status: development preview source, not a release.** No representative 8 GB Windows hardware, real microphone, destination application, or Czech/English/mixed speech corpus has been qualified. Building successfully does not establish dictation quality or minimum hardware support.

[Windows CI](https://github.com/beamj786-rgb/jl-voice/actions/runs/44950062466) passed at `af26d57`: self-contained CPU package build, ten tests, real CPU transcription of a public English fixture, and tray startup/idle-monitor smoke test. A hosted Windows runner is not representative Windows 11 low-end hardware.

## Implemented

- Windows Forms tray application without a browser engine; nonactivating recording overlay.
- Configurable F9–F12 shortcut: Ctrl+Alt+key toggles recording, or hold the selected key for push-to-talk.
- Microphone selection; mono 16 kHz PCM capture only during explicit recording, bounded to 90 seconds per recording in this preview.
- Czech, English, and experimental Automatic language selection.
- Isolated, below-normal-priority native whisper.cpp CPU worker with at most two inference threads, GPU explicitly disabled, five-minute timeout and cancellation.
- Audio sent through an anonymous pipe, no audio files or persistent transcript history by default. Last result stays in memory until the next recording or exit.
- Unicode keyboard insertion with an original-window check, without changing the clipboard. Input dispatch still needs real target-app acceptance testing.
- Worker exits after each dictation, releasing model memory. No prewarming, idle inference, or resident LLM.
- Portable settings, pinned model download hashes, Windows packaging script and developer benchmark tools.
- JL Automation native UI: bundled Funnel Display / Funnel Sans / Geist Mono, original Orbit assets, dark/light/system themes, Czech/English interface, overview and settings pages. See [UI design and review](docs/UI.md).

Automatic mode currently uses upstream language detection for the recording, **not verified language switching within one recording**. No candidate model has passed Czech/English quality acceptance; the small quantized base model is an experiment, not an accuracy claim.

## Developer build

Use .NET SDK 10.0.404 (or compatible patch), PowerShell 7, Git, CMake and Visual Studio C++ build tools on Windows. These are build-machine requirements, not end-user dependencies.

```powershell
dotnet build src/JLVoice/JLVoice.csproj -c Release
pwsh scripts/package.ps1
```

`artifacts/JLVoice` is a self-contained portable folder, including the CPU runtime and multilingual model. Do not distribute it until licenses, clean-machine/offline operation and the [acceptance checklist](docs/REQUIREMENTS.md) pass. The development package downloads dependencies only at build time. The application contains no model downloader, cloud ASR, account system or telemetry.

Launch `JLVoice.exe`, focus a text field and use Ctrl+Alt+F9. Double-click the tray icon to select language, microphone and hotkey. Use the tray menu to cancel or exit. Keep the portable folder writable for settings. Elevated applications and some input controls may reject synthesized input; inspect the retained result rather than blindly retrying partial insertion.

The three candidate models can be packaged with `-Model ggml-base-q5_1.bin`, `ggml-base-q8_0.bin`, or `ggml-small-q5_1.bin`. Packaging a larger candidate is an explicit developer choice, not an automatic RAM-based upgrade.

See [benchmarks](docs/BENCHMARKS.md), [architecture](docs/ARCHITECTURE.md), [research](docs/REVERSE_ENGINEERING.md) and [known issues](docs/KNOWN_ISSUES.md).
