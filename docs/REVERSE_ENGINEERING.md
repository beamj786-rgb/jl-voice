# Research evidence

## Environment boundary

The available execution machine is Linux, not the owner's Windows workstation. No installed Wispr Flow executable, real Windows desktop, microphone or low-end physical PC was accessible. Consequently there is no locally reproduced Wispr Flow runtime behavior, Windows ASR measurement or application input verification.

REA 6.10.1 was built from `morluto/rea` commit `204434e` in the preceding installation task. CLI version, help and a static JavaScript fixture analysis passed there. REA is usable via CLI; an installation does **not** establish an active Codex MCP registration. No new Wispr Flow analysis is claimed in this milestone.

## Secondary sources reviewed

Source: https://github.com/az9713/wispr-flow-rea-teardown (root inventory and README reviewed 2026-11-21).

| Claim | Evidence | Confidence / limit | Implication |
|---|---|---|---|
| The repository contains teardown, Ghidra and development-journey reports, plus a FlowLite directory. | Root file listing and README links. | VERIFIED as repository contents only. | Useful research entry points; not Windows runtime evidence. |
| The author describes inspection of Wispr Flow 1.6.1102 and a hold-F9 local rebuild. | README text. | VERIFIED as author's statement; application behavior remains UNKNOWN here. | Independently implement the ergonomic pattern; verify Windows focus/audio/input ourselves. |
| The root listing contained no LICENSE file. | Root contents query. | VERIFIED for the inspected listing; comprehensive license audit not performed. | No source/assets copied into JL Voice. |

Upstream whisper.cpp v1.9.5 (`d1be6fde11ac6e0407606b4e42fe72d34add8037`) source confirms CPU controls and stdin WAV support. A real Linux CLI test exposed suppressed stdout when piping audio; source inspection also found Windows stdout text-file handling opens `CON`. JL Voice therefore owns a small C-API worker rather than relying on the CLI output mechanism. The owned worker builds and a public English fixture passes on Linux; this is not Czech, mixed-language or Windows recognition evidence.

Next research requires caller-provided, non-sensitive Windows application artifacts or execution on the owner's workstation. Keep observations, derivations, inferred mechanisms and unknown server-side behavior separate. Do not bypass licensing/authentication or read private application content.
