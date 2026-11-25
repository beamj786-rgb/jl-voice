# Sprint G — Windows acceptance checklist

Scope: settings integrity, active/draft configuration, build identity, second-instance message, diagnostics. Not in scope: speech quality, the microphone, insertion into other applications, performance.

**What this checklist is not.** Hosted CI has already shown that the code builds, the unit/contract checks pass and the WPF review (rendered on a hosted Windows Server) reports no issues. That is **not** evidence that the steps below work on a real Windows 11 desktop; they have not been run by anyone yet. Release acceptance (a representative 8 GB machine, offline portable operation, resource use, insertion latency, Czech/English/mixed quality) is a separate, still unmet requirement and is not covered here.

Use a **new empty folder** for the new ZIP. Never copy new files over an old extraction. Keep a copy of your current `settings.json` before starting (step C).

## A. Identity of the build

| # | Do | Expect |
|---|---|---|
| A1 | Open `BUILD-INFO.json` in the unpacked folder | `source_commit` is the commit of the ZIP you downloaded (CI run → "Commit" of the run page); `release` is `false` |
| A2 | Start `JLVoice.exe`, open it from the tray, look at the Overview | the line `Build: v0.1.0 · <8 hex digits> · <date> UTC` matches A1; the window title ends with the same 8 digits |
| A3 | Hold Caps Lock, say one sentence, release; open Advanced | below the transcript: `Backend: Offline · …`, `Requested language: …`, `detected: …`, `Build: …` (same build) |

## B. Fresh configuration

| # | Do | Expect |
|---|---|---|
| B1 | Delete `settings.json` (new folder only), start the app, Settings | `Active configuration: Offline · Base · Q5 · English · microphone: <your mic>`; `Source: built-in defaults (no settings.json yet)`; dictation language box shows English |
| B2 | Save without changes | `Saved. Active: …`; `settings.json` now exists |

## C. Migration of an existing `settings.json`

Close JL Voice completely (tray → Exit) before editing; edit with a plain text editor; keys are case-sensitive.

| # | Put in `settings.json` | Expect after starting |
|---|---|---|
| C1 | your **real old file** (copy from the previous test folder) | settings unchanged: same language/model/shortcut/theme; source `settings.json`; no dialog |
| C2 | `{ "Language": "cs", "Model": "ggml-small-q5_1.bin", "Key": 20 }` | Czech + Small shown as active — the new English default does **not** replace an explicit choice |
| C3 | `{ "Language": "auto", "Key": 20 }` | Automatic stays |
| C4 | `{ "Model": "ggml-base-q5_1.bin", "Key": 20 }` (no `Language`) | English (missing field takes the new default) |
| C5 | `{ "Language": "cs", "Model": "does-not-exist.bin" }` | a message that settings.json was invalid; **defaults** active (or `settings.json.bak` restored if one exists); the file `settings.invalid-<timestamp>.json` holds your original bytes; Settings shows the source line in bold terms (`DEFAULTS … saved aside`) and names the field `Model` in the message |
| C6 | after C5, change anything and Save | a new valid `settings.json`; `settings.invalid-*.json` is **still there**; `settings.json.bak` exists after a second save |
| C7 | `{ "Provider": "nvidia", "Language": "auto" }` | treated as invalid (NVIDIA needs consent and an explicit Czech/English language): defaults + preserved copy |

## D. Simulated write failures (the active configuration must not change)

Start from a valid file; note the active line in Settings. Run these in PowerShell in the app folder.

| # | Do | Expect |
|---|---|---|
| D1 | `attrib +R settings.json`, change the dictation language in Settings, Save | bold message **under the Save button**: `Settings were NOT saved…` with `Still active: …` (the old language); the file is unchanged (`Get-FileHash` before/after equal); Overview/bubble still use the old language. Then `attrib -R settings.json` and Save again → `Saved. Active: …`. (Windows is expected to refuse replacing a read-only file; if it does not, the file will simply be saved — report that, it is not a defect.) |
| D2 | `mkdir settings.json.new` (blocks the temporary file), change a setting, Save | same `NOT saved` message; `settings.json` unchanged. `rmdir settings.json.new`, Save again → saved |
| D3 | Save a valid change twice | `settings.json.bak` equals the previous content; no `settings.json.new` left |
| D4 | NVIDIA cloud selected with no key/consent, Save | `NOT saved`, reason visible **in Settings** (not only on the Overview); active backend still Offline |
| D5 | Hold threshold box = `abc`, Save | `The hold threshold must be a whole number from 100 to 1000 ms.`; nothing saved |

Power-loss during a write cannot be simulated safely by hand. The code writes `settings.json.new` write-through, then swaps it in atomically and keeps `settings.json.bak`; a stale `.new` is ignored. This is verified by unit checks that block the temporary file, not by cutting power.

## E. Draft versus active

| # | Do | Expect |
|---|---|---|
| E1 | Change language/provider/model/theme without saving | `Unsaved changes – still active: <active config>`; the top line `Active configuration:` is unchanged |
| E2 | Press **Back** (or Hide, or the window ✕) | dialog: Yes = save, No = discard, Cancel = stay; text names what is still active |
| E3 | Choose **No** | controls (including live-previewed theme and interface language) return to the active values; no message left |
| E4 | Change the language, press Back, choose **Cancel** | stays in Settings with the draft intact |
| E5 | Change a setting, **Save**, restart the app | same configuration after the restart |
| E6 | Close the app via tray → Exit with an unsaved draft | no prompt (drafts are not saved); next start shows the saved configuration |

## F. Second instance

| # | Do | Expect |
|---|---|---|
| F1 | With JL Voice running (tray), start `JLVoice.exe` from **another** folder (old test ZIP) | a message (Czech and English) lists the running folder and its `build <8 digits>` and this copy's folder/build; the new copy exits; nothing else changed |
| F2 | Start the same `JLVoice.exe` again | same message with `same build` |
| F3 | Tray → Exit, start again | normal start |

## G. Diagnostics (no secrets)

```powershell
.\JLVoice.exe --diagnostics "$env:TEMP\jlvoice-diag.jsonl"
```
Dictate once in Notepad, change a setting and Save, tray → Exit, then:

```powershell
Get-Content "$env:TEMP\jlvoice-diag.jsonl" | Select-String '"name":"(build|config_active|config_at_recording)"'
Select-String -Path "$env:TEMP\jlvoice-diag.jsonl" -Pattern 'nvapi-|Bearer|Users\\' -SimpleMatch   # must print nothing
```
Expect a `build` mark (version, full commit, built time, process id, executable name only), a `config_active` mark at start and after the save, and a `config_at_recording` mark at the recording. They list provider, language, model, threads, microphone index **and name**, hold threshold, settings source; they contain no API key, transcript text, audio, window title or folder path. Delete the file afterwards.

## H. Record

Write down for every failed row: build `source_commit`, Windows version, the exact action, what happened. Do not send audio, transcripts you consider private, or any key. Passing this checklist proves Sprint G on **your** PC only; it does not qualify the product for release (8 GB hardware, offline package acceptance, Czech quality, insertion latency remain open).
