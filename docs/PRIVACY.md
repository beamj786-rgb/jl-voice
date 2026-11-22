# Privacy and portable-app removal

JL Voice is a local Windows dictation application. The application itself does not initiate network requests, require an account, send speech to a cloud API or collect app telemetry. Developer build tools obtain dependencies/models from their documented upstream sources; GitHub/Windows/signature services have their own platform/network behavior outside the dictation application.

The microphone opens only during user-requested recording. A bounded audio buffer is held in memory and passed through a local pipe to the CPU-only recognition worker. Raw audio files and persistent transcript history are not saved by default. One last transcript remains in memory until the next recording, Clear or exit. Copy writes to the clipboard only when the user explicitly requests it; automatic text insertion does not overwrite the clipboard.

The configured global shortcut is used to start/stop dictation. Other keystrokes are not logged. Text insertion checks the original foreground window/native focused-control identity. Known limitations include browser fields sharing a native handle, focus races and elevated applications; password-field detection is not implemented, so use non-sensitive sample text during development testing.

Portable preferences are saved in `settings.json` next to the app. Developer diagnostic phase/timing logging is opt-in and omits transcript/audio contents and target-window titles. These statements do not promise erasure from Windows paging, clipboard history, crash dumps or third-party application storage.

To remove the portable app, choose **Exit JL Voice** in its tray menu, wait for owned recording/transcription work to stop, then delete its extracted folder. This also removes its portable preferences. Remove any separately chosen diagnostic files yourself if enabled. JL Voice does not install an always-running service, an OS font, an auto-start entry or a root certificate.

No signing-service account or signing client is required for end users of a future approved signed ZIP. SignPath would be publisher/build infrastructure; Foundation signing has not yet been approved or activated.
