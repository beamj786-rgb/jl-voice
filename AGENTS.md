# JL Voice engineering contract

The user's low-end-first requirements take precedence over earlier master-prompt assumptions. English source/docs/commits; Czech owner communication. Work only in the dedicated JL Voice repo. Do not place the project in `/workspace`; temporary build/checkouts may use `/tmp`.

- Windows 11 x64, 8 GB RAM, integrated graphics, modest CPU and concurrent desktop activity are the primary acceptance target. 16 GB is secondary. Never extrapolate qualification from a high-end CPU or disabled GPU.
- CPU-only baseline; use multilingual ASR. The worker must set `use_gpu=false` and reject English-only models. Czech, English and mixed speech are equally mandatory. Preserve diacritics, technical vocabulary and product names. Test short Automatic detection and within-recording language changes.
- No end-user runtime/model installation, accounts, paid APIs, telemetry or cloud transcription. Package the runtime and default model. Offline/clean-machine tests are mandatory.
- Idle must have no microphone capture, inference, GPU allocation or resident LLM. Model residency decisions need measured evidence. Count all owned worker processes in resource measurements; bounded threads and low worker priority protect desktop responsiveness.
- Never claim quality or performance without real evidence. Missing metrics are null / unverified, never zero. Keep WER for Czech, English and mixed recordings separate. A source build or mock never proves microphone, UI input, low-end performance or offline package acceptance.
- No release until representative 8 GB hardware, both languages, mixed speech, offline portable operation, resource consumption and insertion latency have passed acceptance. Do not publish releases or deploy without explicit owner approval.
- Reverse-engineer read-only within the master prompt boundaries. Do not copy code from the community teardown (no root license observed). Do not copy proprietary source/assets or inspect private credentials/content.
- Honor cancellation and cleanup; do not insert into a changed foreground window. Do not persist raw audio/transcripts without explicit opt-in. Preserve clipboard contents.
- Verify changes with `dotnet build ...`, self-contained Windows publish and relevant benchmark unit tests. Windows runtime testing remains separate. Do not introduce Electron or an always-loaded model without measured justification.

Living requirement owner: `docs/REQUIREMENTS.md`. Known gaps: `docs/KNOWN_ISSUES.md`. Do not turn an unmeasured model candidate into a release default.
