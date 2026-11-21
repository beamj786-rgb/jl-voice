# Known gaps and limitations

- Hosted Windows CI passes native CPU package creation, ten tests, public-English-fixture inference and tray startup. No real microphone, low-end physical hardware or representative language-quality acceptance has run.
- No complete portable runtime/model package has been executed on a clean offline Windows 11 machine. The packaging script passes on a developer-tool-equipped hosted Windows runner; that does not prove clean-machine acceptance.
- Base q5_1 is an unaccepted experimental candidate. Czech, English, punctuation, silence hallucinations and mixed-language accuracy are unknown. Single-recording language switching is not specifically implemented.
- Recording is bounded to 90 seconds, not arbitrary long-form dictation. Safe chunking and switch-aware processing remain needed.
- Worker-per-recording unloads the model but adds startup latency. Resident model trade-off has not been measured.
- Input dispatch checks foreground window and native focused-control handles, but browser DOM fields can share a native handle and focus can still race. Partial input may occur. Password-field detection/UI Automation targeting, elevated targets and supported-app behavior need implementation/verification before release.
- Push-to-talk hook, rapid press/release, microphone failure and recording shutdown have source implementations but need Windows stress/recovery tests.
- Microphone list is captured at startup. Devices disconnected/reordered while the app is running need better recovery and stable device identity.
- Settings changes are explicit and portable; saving requires a writable folder. Shortcut conflicts and malformed settings are reported. Startup registration, dictionary, themes, optional history and formatting profiles are not implemented.
- No default history, model prewarming, persistent worker, cloud fallback or LLM. No grammar/postprocessing quality claims.
- Full-app monitor supplies sampled working-set/CPU and input-dispatch timings; OS peak metrics, verified insertion, cold-cache control and desktop responsiveness require additional evidence.
- No signed executable, completed redistribution audit, production release, or benchmark-qualified minimum system requirements.
- Upstream MSVC builds emit warnings, including pointer/bool conversions in ggml backward-gradient helpers and ignored GCC pragmas. Source inspection places the reported conversion sites in gradient-building code; the inference smoke test passes, but this is not a comprehensive upstream warning audit. Review before release; no upstream patch is carried.
