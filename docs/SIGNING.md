# Windows code trust and the blocked ASR worker

## Observed blocker — 2026-11-22

The owner tested the portable Windows build on a personal Windows 11 PC with Smart App Control enabled. JL Voice opened, but Windows rejected `Process.Start` for `runtime/JLVoiceAsr.exe` with the localized message “An app control policy has blocked this file.” No microphone-to-text result was produced. This is a real clean-machine distribution blocker, not evidence of an ASR accuracy failure.

The previously delivered ZIP was also inspected read-only: the worker PE security-directory offset and size are both zero, confirming it has no embedded Authenticode certificate table.

The owner currently has no code-signing certificate or signing-service account. The development build is unsigned; hosted CI success does not demonstrate acceptance by Smart App Control. `BUILD-INFO.json` now records the worker's observed Authenticode status and explicitly keeps Smart App Control qualification false.

## Supported resolution

Microsoft recommends signing application code with a certificate issued by a provider in the Microsoft Trusted Root Program, or using its hosted signing service. This requires publisher identity verification and a suitable certificate/service. A locally generated self-signed certificate is not equivalent to public publisher trust. A checksum only proves file integrity relative to its manifest; it is not a publisher signature.

Before claiming support:

1. Select and provision a trusted signing service/certificate for the owner's verified publisher. No account, purchase or certificate enrollment has been authorized or performed by this change. Never put a private signing key/password in source or a chat.
2. Inventory all shipped PE code. Include the app host, managed application assembly, owned worker and locally built ggml/CPU backend DLLs. Preserve and validate existing vendor signatures instead of blindly replacing them.
3. Sign applicable publisher-owned/distributed code, timestamp signatures and verify the resulting chain using Windows signing tools.
4. Generate the final file hashes only **after** signing; signing changes file bytes. Preserve signature/build provenance in the developer artifact.
5. Test the completed package on a clean Windows 11 machine with Smart App Control still enabled. Confirm that the worker and its DLLs load, CPU transcription completes and insertion succeeds. Also test the offline deployment requirements. A valid signature is not proof of universal policy acceptance or recognition quality.

Do not rename/repackage the worker, move inference into another host merely to evade policy, add antivirus exclusions, disable Smart App Control, weaken global execution policy or describe a fresh unsigned ZIP as a fix. Enterprise policies, if present, require the policy owner's legitimate approval; the reported machine is personal.

## Diagnostics and application behavior

The owned worker-start wrapper preserves the Win32 error code. Codes 4551 (system integrity policy), 1260 (policy restriction) and 577 (image/signature verification) get a concise localized blocked-module notice with the module basename and numeric code in collapsed details. Ordinary access denied is not automatically classified as Smart App Control. The user's profile path is omitted from this notice. Unexpected exception messages use bounded body text rather than a large display heading.

For a real blocked build, the relevant Windows log is **Event Viewer → Applications and Services Logs → Microsoft → Windows → CodeIntegrity → Operational**. Only inspect events relating to the blocked JL Voice module. No registry/policy changes are needed just to inspect the existing log. Do not share unrelated event logs or private paths as a default bug report.

The CI policy-error views are explicitly labelled simulations of Win32 4551. They verify error rendering and classification, not actual enforcement, signing or restored dictation on the owner's PC.

## Official references

Verified 2026-11-22:

- [Smart App Control developer overview](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview)
- [Signing for Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
- [App Control troubleshooting and logs](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/appcontrol-debugging-and-troubleshooting)

## Prepared signing and packaging tools

- `scripts/package-state.ps1` inventories every shipped EXE/DLL: relative path, SHA-256, Authenticode status, public signer thumbprint and timestamp presence. It does not read/export private keys.
- `scripts/finalize-package.ps1 -PackageDirectory PATH` writes `SIGNATURES.json`, records verification status in `BUILD-INFO.json` and regenerates file hashes. It excludes the hash manifest from its own entries. Re-run it only after all intended signing changes.
- `scripts/sign-package.ps1` uses a chosen CurrentUser/My certificate and Windows SDK SignTool. The certificate's private key stays with its Windows hardware/cloud provider. The script rejects missing, expired, self-signed or non-code-signing identities and untrusted local chains; preserves valid vendor signatures; refuses to overwrite invalid signatures; verifies integrity before signing; signs unsigned package code with SHA-256 and an RFC 3161 timestamp; then verifies signatures and regenerates final hashes. It is not configured with a real signing identity yet.
- `Check-Package.ps1` remains optional for end users and supports Windows PowerShell 5.1. Its stricter developer option `-RejectUnlistedFiles` rejects files absent from the manifest before signing. A manifest is not independent proof of source authenticity: build/sign only an explicitly reviewed canonical commit.

Developer command, **after** obtaining an approved identity and reviewing the package/source provenance:

```powershell
pwsh ./scripts/sign-package.ps1 -PackageDirectory ./artifacts/JLVoice -CertificateThumbprint YOUR_40_HEX_CERTIFICATE_THUMBPRINT
```

Do not export a private key or paste credentials into this command, source control or chat. Hardware/cloud providers can require interactive approval on the signing host. The current GitHub build has no signing credential and produces an explicitly unqualified development build.

CI verifies actual inventory of the unsigned worker, rejection by the signed-package gate, refusal of a missing identity and refusal of a temporary self-signed test leaf. That leaf exists only in the hosted runner's CurrentUser/My store, is never trusted in Root or used to sign files, and is removed with its key. CI also checks that negative signing attempts leave package integrity intact and that finalization can be repeated without hashing the manifest into itself. Positive public-trust signing, provider approval and enforced SAC acceptance are **not tested** without the real identity.

`all_code_signatures_valid=true` means Windows on the verification host accepted the PE signatures. It does not establish Microsoft Trusted Root Program eligibility, publisher enrollment, Store acceptance, recognition quality or Smart App Control acceptance. `smart_app_control_qualified` deliberately remains false. No production release is authorized by these scripts.

## Owner decision needed: private individual publisher

The owner has confirmed a private individual publisher without a business registration. No enrollment, purchase, license change, Store submission or public release has been performed. Assuming Czech residence, the currently researched choices are:

| Route | Verified terms | Consequence for JL Voice |
|---|---|---|
| Azure Artifact Signing Public Trust | Basic listed at USD 9.99/month, but individual publishers are limited to the US/Canada. Organization eligibility is broader, including the EU. | Not a suitable Czech private-individual route; do not create a billable account assuming eligibility. Private Trust is not a replacement for public distribution trust. |
| Individual CA certificate, e.g. Certum Standard Code Signing in the Cloud | Shop lists **from EUR 209**; actual variant, period, tax and final checkout total must be confirmed before purchase. Certum describes Standard individual identity/address verification. | Preserves portable ZIP distribution. Requires the owner's chosen paid certificate/provider and identity verification; not ordered. A verified legal individual name may appear as publisher. |
| Microsoft Store **MSIX** submission | Microsoft documents free individual registration in the current flow and Store signing after certification. ID verification, agreement acceptance and package review are required. Store EXE/MSI submissions do not receive the same re-signing treatment. | A potential no-certificate-fee route with an installer/Store channel, not the existing portable ZIP. Requires explicit owner choice and MSIX work: writable settings must move out of the protected install directory, capabilities/hotkeys/worker/offline operation need testing, and only an approved private test submission could precede release acceptance. Not implemented or submitted. |
| SignPath Foundation OSS | OSI-approved license, accepted project/team/policy requirements and provider approval are required; upstream signing has additional restrictions. | The repo has no owner-selected source license. Do not silently relicense the app or promise that the native dependency DLLs would all qualify. |

Signing services are publisher/build infrastructure. They do not add cloud speech recognition, app accounts, paid ASR APIs or an end-user SDK/runtime requirement. A Store route would be a separate distribution decision; the application must still meet its offline and hardware requirements.

Additional official sources checked 2026-11-22:

- [Artifact Signing eligibility](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Artifact Signing pricing tiers](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku)
- [Certum Standard Code Signing in the Cloud](https://shop.certum.eu/standard-code-signing-in-the-cloud.html?___store=certum_en)
- [Certum required documents](https://support.certum.eu/en/code-signing-required-documents/)
- [Microsoft Store individual registration](https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-individual-developer)
- [Microsoft Store MSIX signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [SignPath Foundation terms](https://signpath.org/terms)

## Verified preparation — 2026-11-22

[Windows CI run 45028112050](https://github.com/beamj786-rgb/jl-voice/actions/runs/45028112050), source `f740166c1dec108c8dcdf0af564e9652787a9201`, passed packaging, the Windows PowerShell 5.1 integrity helper, signing-preparation checks, the existing core/native tests, CPU fixture and UI review. The archived inventory reports **412 PE files: 395 Valid, 17 NotSigned**. The ASR worker is NotSigned; all-code validity and SAC qualification remain false.

Unsigned files in this build:

```text
JLVoice.dll
JLVoice.exe
NAudio.Core.dll
NAudio.WinMM.dll
runtime/ggml-base.dll
runtime/ggml-cpu-alderlake.dll
runtime/ggml-cpu-cannonlake.dll
runtime/ggml-cpu-cascadelake.dll
runtime/ggml-cpu-haswell.dll
runtime/ggml-cpu-icelake.dll
runtime/ggml-cpu-sandybridge.dll
runtime/ggml-cpu-skylakex.dll
runtime/ggml-cpu-sse42.dll
runtime/ggml-cpu-x64.dll
runtime/ggml.dll
runtime/JLVoiceAsr.exe
runtime/whisper.dll
```

CI explicitly rejected the unsigned package, a missing certificate and a temporary self-signed test identity, verified unchanged package integrity after rejection, and repeated finalization with consistent hashes and no manifest self-entry. This proves the preparation and negative checks only. There is no signed, SAC-qualified Windows ZIP yet. The owner’s distribution/certificate choice and real publisher enrollment remain required.
