# Third-party components

This file is an inventory for development, not a completed redistribution clearance.

- .NET 10 / Windows Desktop runtime — Microsoft, MIT and accompanying third-party notices. Self-contained publish must preserve runtime notices. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf .
- NAudio.WinMM / NAudio.Core 2.2.1 — Mark Heath / contributors, MIT. Exact dependencies are recorded in `src/JLVoice/packages.lock.json`. `licenses/NAudio-MIT.txt` is from upstream v2.2.1, commit `b5d5ff83fd378f046398891fe5cd99426ce44732`. Source: https://github.com/naudio/NAudio .
- whisper.cpp v1.9.5, commit `d1be6fde11ac6e0407606b4e42fe72d34add8037`, including ggml — MIT; packaging copies upstream LICENSE. Source: https://github.com/ggml-org/whisper.cpp . Review any bundled backend dependency notices before release.
- Whisper model weights converted/quantized by ggerganov — pinned repository revision and hashes in `models/manifest.json`; the inspected model repository metadata declares MIT. `licenses/Whisper-model-MIT.txt` is upstream OpenAI Whisper's license at commit `86098128c0b4f24f0e2aa2994de830614b474227` (SHA-256 `b5d65a59060e68c4ff940e1eddfa6f94b2d68fdf58ed7f4dd57721c997e35e9d`). Source: https://github.com/openai/whisper . Complete redistribution audit remains a release gate.
- Google FLEURS public development-set speech/reference text — CC-BY-4.0, Google and contributors, https://huggingface.co/datasets/google/fleurs . Pinned dataset revision, selected prompt IDs, audio hashes, conversion and attribution are preserved in `docs/benchmark-evidence/2026-11-22/corpus.json`. Benchmark references and derived transcripts retain dataset attribution; no dataset audio is bundled with the app.
- psutil 7.2.2 — BSD-3-Clause, developer benchmark dependency only, not bundled with the application.
- Funnel Display, Funnel Sans and Geist Mono — SIL Open Font License 1.1. Original static brand font files and license texts are bundled under `assets/fonts`; provenance and roles are in `docs/UI.md`.
- JL Automation Orbit marks/app icon — owner-supplied brand assets from the owner's agency repository, pinned source commit documented in `assets/brand/source.json`. SVGs are preserved; PNG/ICO derivatives retain their colours and shape. Brand assets are not relicensed as application source.

No code or assets from Wispr Flow or `az9713/wispr-flow-rea-teardown` are included. The owner approved MIT for the original JL Voice source on 2026-11-22; see LICENSE and LICENSE-SCOPE.md. Existing third-party/font licenses and reserved brand artwork remain separate. This does not assert whole-bundle Foundation eligibility.

NVIDIA evaluation tools only: Parakeet TDT 0.6B v3 weights (NVIDIA/contributors, CC-BY-4.0) and sherpa-onnx 1.12.40 (Apache-2.0). The integrity-pinned INT8 archive is fetched by developer tooling and is not included in the cloud test kit or ordinary JL Voice package. Hosted NVIDIA NIM model/API terms are separate; see docs/NVIDIA-PROBE.md.
