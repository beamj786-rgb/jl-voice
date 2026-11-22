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
