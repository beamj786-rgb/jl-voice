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
| Caps Lock | Test with Caps Lock initially off and initially on; hold, release, repeat, and cancel while holding. | Previous letter-case state is preserved. Key repeat must not restart recording. Release after cancellation must not affect a later recording. Physical keyboard/hook behavior still requires this check. |
| Bubble and focus | Click the idle bubble with a caret in Notepad. Speak, then stop using its square button. Hover while idle. | Caret stays in the destination; microphone meter follows sound and flattens in silence. Shortcut/settings appear on hover. Idle is static. |
| Cancel | Cancel both recording and processing using the cross or Escape. Also cancel a recording shorter than half a second. | No insertion, microphone released, processing worker exits, bubble returns to idle. |
| Changed target | Stop recording and immediately switch focus to a different window before transcription finishes. | Insertion is refused and the result is retained in JL Voice. This does not prove safety between browser DOM fields sharing one native handle. |
| Everyday app | Repeat in your intended ordinary, non-elevated editor or browser field using disposable sample text. | Check caret, insertion, accents, line breaks and whether the app accepts every character. Avoid password fields; password detection is not implemented. |
| Last result | Open JL Voice after a transcription; try Copy and Clear. | Copy changes the clipboard only when explicitly clicked; Clear removes the in-memory text. No permanent history is created. |
| Window lifecycle | Close/minimize the window, then reopen from the tray. Finally choose Exit in the tray menu. | Close/minimize keeps dictation available; Exit removes the bubble/tray icon and stops owned work. |
| Settings and scale | Try light/dark theme, Czech/English interface and your actual display scaling. | Readable fields, visible focus, no clipped actions. Report display scaling and monitor setup. |
| Offline | Exit, disconnect the network, then relaunch and repeat both language tests. | Recognition works with the packaged local model. First-time success on a clean offline Windows machine still needs to be demonstrated. |

Automatic insertion preserves the existing clipboard. Elevated apps may block simulated input. If insertion is blocked or partial, inspect the destination and retained result before retrying.

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
