# Delivery state

## Implemented and checked

- Dedicated GitHub project, low-end bilingual requirements and candidate-model hash manifest.
- Windows-targeted build and self-contained application publish on Linux.
- WER scoring unit tests and Python syntax checks.
- Native CPU worker builds on Linux; ten scoring/boundary tests pass and the public upstream English fixture transcribes through the real adapter. This does not qualify Czech, mixed speech, Windows or low-end hardware.

## Implemented, awaiting Windows/runtime validation

- Tray, overlay, hotkeys, microphone selection/capture, bounded PCM pipeline, CPU ASR adapter and foreground-checked Unicode input.
- CPU native packaging script, process cleanup, cancellation, portable settings and opt-in timing diagnostics.
- ASR corpus runner and full-app resource monitor.

## Next work requiring real evidence

1. Build the full CPU package on Windows; run microphone-to-Notepad dictation offline.
2. Correct focus/control targeting and verify rapid hotkey/cancel/disconnect/shutdown paths.
3. Record and evaluate Czech/English/mixed corpus with multilingual base q5_1, base q8_0 and small q5_1 on representative 8 GB hardware.
4. Measure cold/warm startup, model residency alternatives and desktop interference; tune model/thread defaults from evidence.
5. Add safe long-form segmentation and validate language switching, short Automatic detection, punctuation and silence handling.
6. Complete dictionary, optional formatting/history, themes and startup behavior after the core pipeline is accepted.
7. Run clean-machine, offline, license and supported-app checks. Release only after every mandatory gate passes and the owner authorizes distribution.
