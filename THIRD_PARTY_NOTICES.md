# Third-party components

This file is an inventory for development, not a completed redistribution clearance.

- .NET 10 / Windows Desktop runtime — Microsoft, MIT and accompanying third-party notices. Self-contained publish must preserve runtime notices. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms .
- NAudio.WinMM / NAudio.Core 2.2.1 — Mark Heath / contributors, MIT. Exact dependencies are recorded in `src/JLVoice/packages.lock.json`. `licenses/NAudio-MIT.txt` is from upstream v2.2.1, commit `b5d5ff83fd378f046398891fe5cd99426ce44732`. Source: https://github.com/naudio/NAudio .
- whisper.cpp v1.9.5, commit `d1be6fde11ac6e0407606b4e42fe72d34add8037`, including ggml — MIT; packaging copies upstream LICENSE. Source: https://github.com/ggml-org/whisper.cpp . Review any bundled backend dependency notices before release.
- Whisper model weights converted/quantized by ggerganov — pinned repository revision and hashes in `models/manifest.json`; the inspected model repository metadata declares MIT. `licenses/Whisper-model-MIT.txt` is upstream OpenAI Whisper's license at commit `86098128c0b4f24f0e2aa2994de830614b474227` (SHA-256 `b5d65a59060e68c4ff940e1eddfa6f94b2d68fdf58ed7f4dd57721c997e35e9d`). Source: https://github.com/openai/whisper . Complete redistribution audit remains a release gate.
- psutil 7.2.2 — BSD-3-Clause, developer benchmark dependency only, not bundled with the application.

No code or assets from Wispr Flow or `az9713/wispr-flow-rea-teardown` are included. No project-source redistribution license has been selected by the owner yet. Public GitHub visibility does not itself grant a source license.
