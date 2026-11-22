# SignPath Foundation application — draft, not submitted

Prepared for owner review. **No form, email, GitHub issue or signing request has been sent.** This draft does not claim eligibility or approval. No account was created, provider terms accepted or repository integration installed.

## Public project details

- Project: JL Voice
- Repository: https://github.com/beamj786-rgb/jl-voice
- Documentation/download instructions: https://github.com/beamj786-rgb/jl-voice/blob/main/README.md
- Proposed signing policy: https://github.com/beamj786-rgb/jl-voice/blob/main/docs/CODE-SIGNING-POLICY.md
- Privacy/removal policy: https://github.com/beamj786-rgb/jl-voice/blob/main/docs/PRIVACY.md
- Original source license: MIT, owner approved 2026-11-22. Dependency/font licenses and reserved brand assets remain separate under LICENSE-SCOPE.md.
- Proposed project owner/approver account: `beamj786-rgb`; roles/MFA need owner/provider confirmation.
- Contact name/email: **owner must supply/approve directly for the application**. Do not infer a private contact or put identity documents/credentials in this draft.

## Project summary for review

JL Voice is a development-preview Windows 11 x64 WPF dictation utility. It targets ordinary 8 GB CPU-only PCs, with Czech/English recognition and experimental Automatic selection. Holding Caps Lock opens recording; release initiates local transcription and guarded Unicode insertion into the previously focused native control. Recognition runs in an owned below-normal-priority process with at most two CPU threads; the model exits/unloads after dictation. The portable bundle includes the .NET runtime and quantized multilingual model.

The application has no cloud speech API, app login, telemetry, default audio files or persistent transcript history. A bottom-centre bubble reports state without activation. Preferences are portable; removal is exit plus folder deletion.

This is not a qualified production release. Hosted CI verifies builds, basic CPU inference, state logic and native rendering. Representative 8 GB hardware, Czech/English/mixed speech quality and full insertion/offline acceptance remain incomplete. The first owner Windows 11 test reached a Smart App Control block on the unsigned native worker.

## Proposed signing scope — original binaries only

| File | Provenance |
|---|---|
| `JLVoice.exe` | .NET-generated app host for the original WPF app |
| `JLVoice.dll` | Original managed application |
| `runtime/JLVoiceAsr.exe` | Original C++ wrapper using the public whisper.cpp C API |

Product: `JL Voice`; product version `0.1.0`; file version `0.1.0.0`. Source identity is recorded separately in BUILD-INFO.json. No vendor DLL version/product attributes are relabelled.

## Questions that must be resolved with the provider

The full current inventory is documented in SIGNING.md. The last checked bundle had 17 unsigned files: three original-project binaries and 14 third-party binaries (NAudio, whisper and ggml/CPU backends). The Foundation's upstream restrictions matter here. We cannot assume those 14 files are eligible under our project identity, or that an own-binary signature alone solves enforced Smart App Control.

A proposed eligibility enquiry, **not sent**:

> We maintain JL Voice, a new MIT-licensed Windows dictation project with separately licensed dependencies and reserved brand artwork. Could the Foundation consider a development-preview project at this stage, and clarify eligible artifact/signing scope for our own wrapper/app versus unchanged upstream NAudio and whisper.cpp/ggml DLLs? We will not claim third-party files as original project code. We also need to clarify acceptable brand-asset distribution and the evidence expected before enrollment. Our current goal is a portable ZIP that can be tested with Smart App Control enabled; we do not claim that goal is already met.

## Readiness findings and next steps

- Repository created 2026-11-21. Public metadata checked 2026-11-22: no GitHub Releases, stars or forks. This is a new project; established reputation cannot be claimed. Foundation admission is discretionary.
- The Foundation asks for an already released/documented project. The existing preview artifact is a developer test ZIP, not a production release. Ask whether that stage can qualify; do not publish a release simply to appear eligible.
- Original code MIT permission is now approved. Branding/distribution and dependency signing coverage remain unresolved.
- The owned-binary metadata check is ready in `tests/signpath-readiness.ps1`. Its report is not admission/signing proof.
- Contact details, MFA and human approval roles remain unconfirmed. No provisioned Foundation organization, signing policy or API credential exists.
- The local `sign-package.ps1` helper is for a directly available CA-backed Windows certificate provider. Foundation uses its own approved remote build/signing integration; this must be configured only after acceptance and access approval. Returned bytes need the existing finalization/verification stages before any signed-ZIP claim.

Owner review of this draft and explicit authorization naming the Foundation/application are required before any enquiry or application is sent. The reviewed message must state these limitations rather than claim completed release/hardware acceptance or existing sponsorship.

Official references: https://signpath.org/terms · https://signpath.org/apply · https://docs.signpath.io/trusted-build-systems/github .
