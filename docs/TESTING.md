# Testing the Windows development preview

This is an owner/developer test build, not a production release. Windows 11 x64 is the target. A test on a 16 GB or more powerful PC qualifies only that configuration; it cannot qualify the primary 8 GB target. Czech, English and mixed-language quality remain unaccepted.

## Get a portable test build

1. Sign in to GitHub and open this repository's **Actions → Development build checks**.
2. Choose **Run workflow**, branch `main`, and enable **Upload a portable Windows test package after checks pass**. Ordinary pushes do not publish an application package.
3. Wait for the run to succeed. In **Artifacts**, download `JLVoice-Windows-x64-test`. It is a temporary development artifact, not a GitHub Release; retention is seven days.
4. Extract the entire ZIP into a new writable folder, for example `%LOCALAPPDATA%\JLVoice-Test`. Do not run from inside the ZIP or mix files from older builds. The extracted folder contains `JLVoice.exe`, `runtime`, `models`, `assets`, licenses, `BUILD-INFO.json` and this guide.
5. Optional integrity check using the Windows PowerShell already included with Windows: open PowerShell in the extracted folder and run `& .\Check-Package.ps1`. It verifies static files against `SHA256SUMS.json`; editable `settings.json` is checked for presence but excluded from hash comparison. This check detects missing/mixed files, not publisher authenticity. Follow your existing script policy; no security-policy change is required for launching the application.
6. Run `JLVoice.exe` normally. No administrator rights, Python, developer SDK, separate .NET installation or model download is required. This development executable is unsigned. `BUILD-INFO.json` identifies the source commit and packaged model; use artifacts only from the repository/run you selected.

Do not delete or move the model, runtime, fonts or DLLs independently. Keep the whole extracted folder together. The app starts in the tray with its idle bubble; double-click the tray icon to open its window. If it is already running, exit it from the tray before starting another build.

## If Windows blocks the speech worker

If the app opens but reports that an app control policy blocked `JLVoiceAsr.exe`, transcription cannot proceed on that machine. The current unsigned build is known to be blocked on the owner's Windows 11 PC with Smart App Control enabled. Do not disable protection or assume another unsigned download fixes the problem. Check [the signing status](https://github.com/beamj786-rgb/jl-voice/blob/main/docs/SIGNING.md); a trusted signed build is still needed. The updated UI shows the numeric Windows code without the long user-profile path.

## First functional check (about 10–15 minutes)

Use non-sensitive sample text. No audio or transcript history is saved by default. Do not send private dictation, clipboard contents or unrelated screenshots as a bug report.

| Check | Steps | Expected / record |
|---|---|---|
| Setup | Open Settings, select the microphone, explicitly select Czech and save. Ensure Windows microphone access for desktop apps is enabled. | Correct device selected; no runtime/model installation prompt. |
| Czech | Focus Notepad and say: “Příští středu pošlu aktualizovanou nabídku. Cena je tisíc dvě stě korun.” Hold Caps Lock to speak and release to transcribe, or use the bubble. Older saved configurations retain their chosen shortcut. | Record recognized text, diacritics, punctuation and perceived delay. Treat errors as findings. |
| English | Select English, save, focus Notepad and say: “Please open the project, review the latest changes, and send me the updated document.” | Evaluate separately from Czech. |
| Mixed | Try Czech, then English, then Automatic with: “Otevři Visual Studio Code, spusť build a zkontroluj pull request na GitHubu.” Also switch languages mid-recording. | Record each result separately. Automatic is experimental and detects the recording as a whole; usable within-recording switching is not established. Include very short utterances. |
| Caps Lock | Test with Caps Lock initially off and initially on; tap below 250 ms, hold above 250 ms, release, repeat, and cancel while holding. | Taps toggle capitalization; long holds preserve the previous state. Key repeat must not restart recording. Release after cancellation must not affect a later recording. Physical keyboard/hook behavior still requires this check. |
| Bubble and focus | Click the idle bubble with a caret in Notepad. Speak, then stop using its square button. Hover while idle. | Caret stays in the destination; microphone meter follows sound and flattens in silence. Shortcut/settings appear on hover. Idle is static. |
| Cancel | Cancel both recording and processing using the cross or Escape. Also cancel a recording shorter than half a second. | No insertion, microphone released, processing worker exits, bubble returns to idle. |
| Changed target | Stop recording and immediately switch focus to a different window before transcription finishes. | Insertion is refused and the result is retained in JL Voice. This does not prove safety between browser DOM fields sharing one native handle. |
| Everyday app | Repeat in your intended ordinary, non-elevated editor or browser field using disposable sample text. | Check caret, insertion, accents, line breaks and whether the app accepts every character. Avoid password fields; password detection is not implemented. |
| Last result | Open JL Voice after a transcription; try Copy and Clear. | Copy explicitly changes the clipboard; automatic paste temporarily uses it and attempts restoration; Clear removes the in-memory text. No permanent history is created. |
| Window lifecycle | Close/minimize the window, then reopen from the tray. Finally choose Exit in the tray menu. | Close/minimize keeps dictation available; Exit removes the bubble/tray icon and stops owned work. |
| Settings and scale | Try light/dark theme, Czech/English interface and your actual display scaling. | Readable fields, visible focus, no clipped actions. Report display scaling and monitor setup. |
| Offline | Exit, disconnect the network, then relaunch and repeat both language tests. | Recognition works with the packaged local model. First-time success on a clean offline Windows machine still needs to be demonstrated. |

Automatic insertion temporarily uses the clipboard and restores its supported original formats when no newer copy occurred. Elevated apps may block simulated input. If insertion is blocked or partial, inspect the destination and retained result before retrying.

## Diagnose garbled recognition before comparing models

If Windows Sound Recorder is clear but JL Voice produces broken words or repeated characters, open **Settings → Advanced options → Play recording before transcription (audio check)**. In Czech: **Nastavení → Pokročilé možnosti → Před přepisem přehrát nahrávku (test zvuku)**. This is opt-in for the current app session only and is off after relaunch. Save any microphone/language changes as usual, return to Notepad, then dictate one non-sensitive sentence normally.

After releasing the dictation key, the microphone stops. JL Voice plays the exact in-memory WAV it is about to send to the model through the default Windows playback device, then transcribes it using the same worker/model/settings as normal dictation. No audio file is saved or uploaded. Escape cancels playback/transcription; owned playback is disposed on cancellation or exit. The normal microphone and model are not opened while idle. Playback adds the recording's duration to end-to-end latency; disable the check for normal latency measurements.

Compare (1) the audible recording, (2) **Last transcript** inside JL Voice and (3) the destination text. Clear playback with a poor retained transcript isolates recognition from insertion; a correct retained transcript with damaged destination text points to insertion. Garbled playback points to capture/device selection, even if another recorder sounds clear. These observations narrow the diagnosis but do not establish the exact root cause. Report the three observations and the selected microphone/language; sharing audio is optional.

## Resource and responsiveness check

Record Windows version, CPU model, installed RAM, display scaling, app source commit and model from `BUILD-INFO.json`. Specify whether other desktop applications are running. A discrete GPU must not be required; ASR and this preview's WPF rendering are configured for CPU execution.

- Leave the app idle for at least a minute. In Task Manager, observe CPU and memory for `JLVoice`; the microphone must be off and no `JLVoiceAsr` worker should remain.
- Dictate roughly 10, 30 and 60 seconds. Record duration, delay from stopping to text appearing, visible UI stalls, memory and whether you can continue typing/using other apps. Check that the ASR worker exits afterward.
- Repeat after a fresh app launch and during a second/warm dictation. Record observations separately; OS disk cache is not controlled by these manual steps.
- Repeat while normal desktop apps are active. Do not use your powerful PC with its GPU disabled as a substitute for representative 8 GB hardware.

Task Manager observations are a functional smoke test, not precise WER, RTF, peak-memory or minimum-hardware qualification. Developers can use the reproducible tools in `docs/BENCHMARKS.md` for formal measurements. There is no end-user Python dependency.

## Report a problem

Copy the following into a GitHub issue or this conversation. Share only your own non-sensitive test text; audio is not required.

```text
Source commit / model (BUILD-INFO.json):
Windows version / CPU / RAM:
Display scaling / monitors:
Microphone:
Chosen speech language / toggle or hold mode:
Destination application:
Steps to reproduce:
Expected behavior:
Actual behavior / exact error:
Recording duration / delay to insertion:
Idle CPU and memory / desktop responsiveness observations:
Offline test result:
```

Keep unmeasured values “not measured”. Separate Czech, English and mixed-language findings. Minimum 8 GB hardware acceptance and production release remain blocked until the requirements in `docs/REQUIREMENTS.md` pass.

## Caps HOLD and model-comparison sprint

See STT-CAPS-SPRINT.md for the audit and proof boundaries. In Settings → Advanced options, choose Base Q5 or Small Q5 and save; both ship in the comparison ZIP. The hold threshold defaults to 250 ms and accepts 100–1000 ms. Speak only once the bubble indicates recording. If a delayed timer loses the exact-threshold race to key-up, no microphone starts afterward and no Caps toggle occurs for that long press. A hold that records less than 0.5 seconds reports the minimum-duration error rather than transcribing an empty clip.

Test Caps initially on/off, short taps, long holds, rapid taps, auto-repeat, Caps+Shift, switching windows before/during recording, changing native controls, session lock, suspend/resume, microphone failure, worker failure, insertion failure, shortcut conflicts and recovery. Disarm on cancellation requires physical release before a new hold; a key-up lost on the secure desktop can consume the first following press/release. If hook registration fails, use the bubble or select Ctrl+Alt+F9; never weaken Windows security. Modern Notepad/browser behavior and actual lock/sleep tests remain manual acceptance until recorded as executed.

For an exact same-audio comparison, explicitly enable **Developer check: retain last audio in memory** (Czech: **Vývojový test: ponechat poslední zvuk v paměti**). Dictate a non-sensitive sentence once. Under Advanced options select a model and language, save, then click **Transcribe the same audio again**. Replay only updates Last transcript; it never inserts into the focused app. Compare Base/Small and CZ/EN/Auto against the same retained clip. Model, detected language, recording duration, inference-only time and total worker time appear separately below the replay control. Uncheck retention or exit to clear the buffer; it is off after restart. Playback time, model loading and paced insertion are not inference time.

Automatic insertion now uses a single Ctrl+V instead of VK_PACKET character events. It checks the original native target/modifiers and cancellation before dispatch, publishes plain Unicode text with clipboard-history/cloud-sync exclusion flags, waits 700 ms, and restores backed-up clipboard formats if unchanged. A new user copy must not be overwritten. Unsupported/oversized clipboard contents must remain intact and cause a visible refusal. A paste already dispatched cannot be recalled by cancellation or focus change. A very busy target may read after the fixed grace period; verify Notepad/browser on the owner PC. Inspect the destination before retrying after any error. Hosted native edit-control checks do not prove modern Notepad/browser acceptance.

## Regression: repeated letters in automatic insertion

The owner reported a heavily repeated/missing-character Notepad result while JL Voice retained a different coherent (but inaccurate) transcript. Explicit Copy → Ctrl+V reproduced the retained transcript exactly. This establishes an automatic-insertion failure independently of STT accuracy. The older paced VK_PACKET path is removed; changing ASR providers cannot fix this transport defect.

On a disposable test desktop, native checks seed text/RTF/custom-binary and bitmap clipboard fixtures; verify exact long CZ/EN/emoji paste, replacement of selected text, Caps-on capitalization, restoration, newer-copy precedence, pre-cancellation, changed focus and oversized clipboard refusal. These tests deliberately seed/clear the test desktop clipboard and must not run on a desktop with important clipboard contents. The current tests do not automate modern Windows 11 Notepad or browser DOM.

For owner acceptance: copy a harmless unrelated sentence first, dictate in Notepad, compare it character-for-character with Last transcript, and paste again elsewhere to check the original clipboard was restored. Repeat in a browser field and with Caps Lock already on. Report separately any STT inaccuracies. Enable memory-only audio retention to compare NVIDIA and offline Small on the same recording; replay updates JL Voice only. NVIDIA returning English for spoken Czech with selected cs remains an unresolved recognition issue; cs is a requested language, not proof of the server detecting it.

### Paste-fix build evidence — 2026-11-22

Source `1cc9ba2e02d63b6e051c7d52bebdffdaaadae510`: [Windows CI 45064455886](https://github.com/beamj786-rgb/jl-voice/actions/runs/45064455886) passed core checks, native desktop checks, self-contained package construction/integrity/signature inventory, worker/scoring tests, public English CPU inference, idle smoke and WPF review. The new native assertions cover exact long CZ/EN/emoji paste without VK_PACKET, text/RTF/custom-binary restoration, delayed-render OLE data, selection replacement, Caps-on state, changed focus, pre-cancellation, newer user copy preservation, bitmap pixels and oversized clipboard refusal. No actual NVIDIA request or physical modern Windows 11 Notepad/browser acceptance was performed.

[Development Windows ZIP](https://github.com/beamj786-rgb/jl-voice/actions/runs/45064455886/artifacts/14771120644): 311,382,143 bytes; unsigned, not a release; artifact retention seven days. Extract to a new folder. You may copy only your previous settings.json into the new folder to retain provider/language/microphone preferences; saved API keys live separately in Windows Credential Manager and are loaded only when RememberNvidiaKey was enabled. Otherwise set NVIDIA/Czech/consent/key again. Do not copy old executables or runtime DLLs over this package.

Owner test: compare Last transcript with inserted Notepad text exactly, repeat with a harmless pre-existing clipboard and a browser field. Czech recognition quality remains unresolved: do not use this input-transport fix as evidence that NVIDIA understands the recording correctly. For a same-audio comparison, opt into memory-only audio retention, record once in Czech, then save Offline/Small and replay without inserting; clear retention afterward. No private audio upload to developers is necessary.
