# Architecture

## Selected development approach

The preview uses .NET 10 WPF/XAML with native Windows APIs and NAudio.WinMM capture, plus a small native `JLVoiceAsr` process using whisper.cpp's C API. Self-contained folder publishing bundles the .NET runtime. The UI has no Windows Forms dependency, browser engine, Python runtime, account or local LLM. Tray integration uses `Shell_NotifyIcon` on the WPF window's message loop.

The owner selected WPF for a custom minimalist Windows interface. It replaces the provisional Windows Forms UI; audio capture, ASR, settings format, hotkeys and insertion interop retain their existing owners. Tauri was considered but would add WebView2 and a different UI/backend boundary. No framework choice establishes low-end acceptance.

`RenderOptions.ProcessRenderMode = SoftwareOnly` avoids a GPU rendering context for this small utility. The transparent bubble is a small WPF layered window; processing feedback uses a bounded 100 ms timer only when Windows client animations are enabled. Idle uses neither that timer nor capture/model work. Measure CPU rendering, memory and desktop responsiveness on representative hardware before changing this policy or claiming minimum support.

## Ownership

`MainWindow` serializes recording/transcription on the UI thread; `AudioCapture` owns the live microphone and bounded PCM buffer; `AsrEngine` owns exactly one below-normal worker; `Settings` owns portable configuration; `WindowsInput` owns hotkeys/Unicode dispatch; the nonactivating `VoiceBubble` projects controller state. Its idle capsule has no active timer, capture or inference. Only recording updates the RMS meter; processing and transient feedback own bounded timers. Window close/minimize hides to the tray; explicit Exit cancels and awaits owned work. WPF `ShowActivated=false`, nonfocusable bubble buttons, native no-activation styles and z-order preserve the destination focus. `Diagnostics` only records phase timestamps after an explicit `--diagnostics NEW_FILE` invocation.

The worker receives an in-memory WAV through stdin, validates mono 16 kHz 16-bit PCM and the 0.5–90 second duration bound, sets `use_gpu=false`, refuses English-only models and accepts only 1–2 threads. Package builds disable OpenMP and BLAS; CPU backend variants use upstream runtime dispatch instead of build-machine-native optimization. The worker exits after every dictation. Cancellation kills only the owned process tree; exit awaits current work before disposing the tray.

The initial CLI experiment revealed that upstream's `-f -` infers stdout-output mode and suppresses ordinary segment printing; moreover, Windows text-file stdout handling opens `CON` instead of the redirected pipe. Rather than encode platform-specific CLI output workarounds, the owned worker writes UTF-8 text to its normal stdout directly. No transcript file is required. A Windows active-code-page manifest supports UTF-8 model paths; actual Unicode-path execution still needs Windows verification.

Current language handling is whole-recording upstream detection or explicit `cs` / `en`. Segmented detection/code switching is **not implemented or accepted**. Text is preserved rather than passed through an unvalidated formatter. Clipboard is untouched. Foreground-window and native focused-control-handle checks reduce wrong-destination insertion, but cannot distinguish browser DOM fields sharing a native handle or prevent focus races during `SendInput`; Windows acceptance must cover these cases.

Privacy defaults: no transcript history, no audio files, no diagnostic logs. One last result remains in memory for recovery after failed input dispatch. Temporary PCM and WAV buffers are cleared on normal cleanup. No promises are made about OS paging, crash dumps or cryptographic erasure.

## Hold shortcut

Fresh packages use physical Caps Lock as a hold-only binding. The low-level keyboard hook consumes its down/up events to preserve the existing Windows letter-case state. It ignores injected events. F9–F12 retain hold and Ctrl+Alt toggle modes; saved settings keep their existing valid binding. Caps Lock forces hold mode in the UI and settings validation rejects Caps Lock toggle mode.

`HoldKeyState` emits one start/stop pair per accepted hold, ignores auto-repeat, does not arm presses during busy work, and disarms cancellation without allowing a held key to restart. Cancellation/rebinding also invalidates queued UI intents. Capture stopping/device failure disarms the old hold so a later release cannot stop a new recording. Deterministic checks cover this state logic; native key-hook/LED/physical keyboard behavior still needs acceptance on Windows.
