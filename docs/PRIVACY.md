# Privacy and portable-app removal

JL Voice is a local Windows dictation application. The default offline mode does not initiate network requests, require an account or send speech to a cloud API. There is no app telemetry. Optional NVIDIA cloud mode is described below. Developer build tools obtain dependencies/models from their documented upstream sources; GitHub/Windows/signature services have their own platform/network behavior outside the dictation application.

The microphone opens only during user-requested recording. A bounded audio buffer is held in memory. Offline mode passes it through a local pipe to the CPU-only recognition worker. Raw audio files and persistent transcript history are not saved by default. One last transcript remains in memory until the next recording, Clear or exit. Copy writes to the clipboard only when the user explicitly requests it; automatic text insertion does not overwrite the clipboard.

An optional developer checkbox can retain one last audio buffer in memory for repeated transcription with the selected provider. It is off after relaunch; unchecking it, replacing the buffer or exiting clears the owned buffer. Replay never inserts text into another app. Optional audio playback also stays in memory. These checks are not enabled by diagnostic logging alone.

The configured global shortcut is used to start/stop dictation. Other keystrokes are not logged. Text insertion checks the original foreground window/native focused-control identity. Known limitations include browser fields sharing a native handle, focus races and elevated applications; password-field detection is not implemented, so use non-sensitive sample text during development testing.

Portable preferences are saved in `settings.json` next to the app. Developer diagnostic phase/timing logging is opt-in and omits transcript/audio contents and target-window titles. It may include the model filename, selected/detected language, audio duration, inference/process timings, exception category and numeric worker/start error code. These statements do not promise erasure from Windows paging, clipboard history, crash dumps or third-party application storage.

To remove the portable app, choose **Exit JL Voice** in its tray menu, wait for owned recording/transcription work to stop, then delete its extracted folder. This also removes its portable preferences. Remove any separately chosen diagnostic files yourself if enabled. JL Voice does not install an always-running service, an OS font, an auto-start entry or a root certificate.

No signing-service account or signing client is required for end users of a future approved signed ZIP. SignPath would be publisher/build infrastructure; Foundation signing has not yet been approved or activated.

## Optional NVIDIA cloud development mode

Selecting NVIDIA, choosing Czech or English, entering an API key, accepting the audio-sharing checkbox and saving enables cloud dictation. Each requested transcription/replay sends the WAV audio and selected language to the fixed NVIDIA HTTPS endpoint documented in NVIDIA-APP-TEST.md, with the key in the Authorization header. Saving settings alone sends nothing. Automatic language selection is currently offline only. No automatic cloud fallback or retry occurs; the request timeout is 120 seconds. Cancellation cannot retract audio already uploaded. Provider processing/retention and limits follow your NVIDIA account terms; JL Voice makes no provider-side deletion promise.

The key is masked in the UI and kept in process memory by default. The optional Remember checkbox stores it in Windows Credential Manager for the current user, never in settings.json or diagnostic logs. Forget key clears the session and removes that credential. Before deleting the portable folder, use Forget key, or remove JLVoice.NVIDIA.APIKey in Windows Credential Manager afterward; deleting the folder alone does not remove it. Managed strings cannot promise secure erasure from process memory. Request failures log only a category, without response bodies.
