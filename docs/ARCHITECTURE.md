# Architecture

## Selected development approach

The preview uses .NET 10 Windows Forms with native Windows APIs and NAudio.WinMM capture, plus a small native `JLVoiceAsr` process using whisper.cpp's C API. Self-contained folder publishing bundles the .NET runtime. No browser engine, Python runtime, service, account or local LLM is part of the end-user application.

Considered alternatives:

1. Rust / native Win32 / whisper.cpp: leaner potential runtime distribution and direct lifecycle control, but more initial Windows UI/audio interop work and no measured memory advantage for this project yet.
2. Windows Forms / NAudio / whisper.cpp: native controls, maintainable capture cleanup and straightforward self-contained distribution. Runtime size and GC cost must be measured on the primary target. Selected provisionally to obtain a real capture-to-insertion pipeline without a web renderer; this is not proof it meets the memory target.

## Ownership

`VoiceForm` serializes recording/transcription on the UI thread; `AudioCapture` owns the live microphone and bounded PCM buffer; `AsrEngine` owns exactly one below-normal worker; `Settings` owns portable configuration; `WindowsInput` owns hotkeys/Unicode dispatch; the nonactivating `Overlay` reports state. `Diagnostics` only records phase timestamps after an explicit `--diagnostics NEW_FILE` invocation.

The worker receives an in-memory WAV through stdin, validates mono 16 kHz 16-bit PCM and the 0.5–90 second duration bound, sets `use_gpu=false`, refuses English-only models and accepts only 1–2 threads. Package builds disable OpenMP and BLAS; CPU backend variants use upstream runtime dispatch instead of build-machine-native optimization. The worker exits after every dictation. Cancellation kills only the owned process tree; exit awaits current work before disposing the tray.

The initial CLI experiment revealed that upstream's `-f -` infers stdout-output mode and suppresses ordinary segment printing; moreover, Windows text-file stdout handling opens `CON` instead of the redirected pipe. Rather than encode platform-specific CLI output workarounds, the owned worker writes UTF-8 text to its normal stdout directly. No transcript file is required. A Windows active-code-page manifest supports UTF-8 model paths; actual Unicode-path execution still needs Windows verification.

Current language handling is whole-recording upstream detection or explicit `cs` / `en`. Segmented detection/code switching is **not implemented or accepted**. Text is preserved rather than passed through an unvalidated formatter. Clipboard is untouched. A foreground top-level-window check reduces wrong-destination insertion, but cannot prove focused-control identity or prevent focus races during `SendInput`; Windows acceptance must cover these cases.

Privacy defaults: no transcript history, no audio files, no diagnostic logs. One last result remains in memory for recovery after failed input dispatch. Temporary PCM and WAV buffers are cleared on normal cleanup. No promises are made about OS paging, crash dumps or cryptographic erasure.
