# Code signing policy — proposed Foundation route

**Status: preparation only. No Foundation application has been submitted or approved, and no Foundation-signed artifact exists.**

The proposed signing identities are maintained through SignPath Foundation, not a private release key exported to this repository. If accepted, the certificate publisher would be SignPath Foundation; the app remains named JL Voice. No API token/private key is stored in source or chat.

Proposed responsible account: repository owner [`beamj786-rgb`](https://github.com/beamj786-rgb), as maintainer/reviewer and human signing approver, subject to that person's confirmation and Foundation acceptance. Required GitHub/SignPath MFA and role setup have not been verified. No claim is made that the account is already enrolled or configured.

Only builds attributable to a reviewed canonical source commit may be requested for signing. Each signing request requires human approval under the provider's configured policy. Signing does not authorize a production release; the existing hardware, language-quality, offline and insertion acceptance gates still apply.

The proposed original-project binary scope is `JLVoice.exe`, `JLVoice.dll` and `runtime/JLVoiceAsr.exe`. These are metadata-normalized as JL Voice version 0.1.0. Dependency product/version metadata is preserved. Third-party DLLs are **not** silently claimed as our own software or submitted under our project scope. The known unsigned dependencies must be discussed with the Foundation; signing the three original binaries alone is not proof that this ZIP runs under Smart App Control.

Attribution required **if the provider approves and signing is actually in use**: “Free code signing provided by SignPath.io, certificate by SignPath Foundation”. This is a proposed attribution, not a claim of sponsorship or current service.

The [privacy/removal policy](PRIVACY.md) describes local audio processing, optional diagnostics and portable removal. The [license scope](../LICENSE-SCOPE.md) preserves existing dependency/font terms and separately reserved branding. These scopes require review before a whole-package eligibility claim.

The application draft is in [SIGNPATH-APPLICATION.md](SIGNPATH-APPLICATION.md). Its contact fields and unresolved eligibility questions must be completed/approved before sending it to the Foundation. Installing a GitHub integration, granting access, accepting provider terms or submitting a signing request are separate actions; none has been performed by this preparation.
