# Streaming offline dictation: obstacles in the current architecture

Status: analysis only. Nothing here is implemented. Goal (a performance target, not a guarantee): dictation of 30–60 s with at most 3 s from releasing Caps Lock to the inserted text on the target hardware (8 GB RAM, integrated graphics, modest CPU, other applications running).

## The first obstacle is speed, not code

Streaming only helps when a segment is transcribed faster than it is spoken, otherwise the backlog grows during dictation and the 3 s end latency is impossible. Measured whole-worker real-time factors (hosted Windows runner, 2 threads, below-normal priority, model load included; public read speech; **not** an 8 GB machine, expect slower on weak CPUs):

| Model | RTF on 17–45 s recordings (wall time / audio time) | Keeps up with real time? |
|---|---|---|
| Small Q5_1 | 0.74–0.98 | Marginal; no headroom while the UI, the capture thread and other applications compete for two cores |
| Turbo Q5_0, bounded context | 1.74–2.2 | No: a 45 s dictation needs about 100 s of compute |
| Base Q5_1 | not measured on long recordings | Probably yes, but Czech WER ~83% on the public set |

So with today's models a chunked pipeline can reduce the tail (only the last segment remains after release) but cannot reach 3 s for Czech-quality models on a slow CPU unless segments are processed in parallel with speech and the CPU has spare capacity. This must be measured on the target 8 GB hardware before any design is chosen; the hosted runner numbers only show the order of magnitude.

## Code-level obstacles (where, and what is missing)

| # | Area | Today | Needed for streaming |
|---|---|---|---|
| 1 | Worker protocol (`native/worker.cpp` `read_audio`, `AsrEngine.TranscribeAsync`) | One process per recording; the whole RIFF/WAV is read from stdin until EOF, strict RIFF length, 0.5–90 s; model loaded per process | A long-lived worker for the duration of one dictation (still unloaded afterwards) with a framed stdin protocol (audio chunk, flush, cancel) and framed stdout results; today there is no framing and exit code = result |
| 2 | Capture (`AudioCapture`) | Every PCM buffer is appended to one `MemoryStream`; a second consumer would need a thread-safe chunk queue; `MaxSeconds = 90` is hard-coded in the capture, worker and `EncodePcm` | A bounded chunk queue fed from the NAudio callback that never blocks the callback; 180 s limits in three places; ring/segment storage that is wiped on completion |
| 3 | VAD (`run_vad`) | Silero is run once over the complete audio and only as a gate | Streaming VAD with preserved LSTM state (`whisper_vad_detect_speech_no_reset`, `whisper_vad_reset_state`), pause-based segment boundaries, a maximum segment length (Whisper window 30 s), start/end padding so word onsets and endings are not cut |
| 4 | Language context (`params.no_context = true`) | Every transcription starts without context; arbitrary prompt text is rejected on purpose (Windows argv is not UTF-8, free text on a command line) | The previous segment's text as `initial_prompt`, which requires passing UTF-8 text over stdin, a bound on prompt length, and a measured check that carried context does not increase repetition/hallucination |
| 5 | Segment joining | One transcript from one decode | Overlap between segments and deduplication of repeated words at the seam; ordering guarantees when a late segment finishes after a later one; word-level timestamps or a conservative text-overlap rule |
| 6 | Guard (`transcript_guard.h`) | Evaluates one finished transcript; the words-per-second and length rules assume the full audio | Per-segment evaluation plus a whole-dictation check; a suspicious segment must withhold the whole insertion or mark the segment, never insert partial text silently |
| 7 | Backpressure and CPU | Transcription starts after capture ends, so recording and inference never overlap | Inference during capture competes with the capture thread and the UI; a lag limit (stop accepting audio or warn, never silently drop it), thread count and priority policy, behaviour when inference falls behind, timeout per segment instead of one 5-minute timeout |
| 8 | Memory | Model resident only for ~one transcription (Turbo ~0.9 GB peak, Small ~0.55 GB) | Model resident for the whole dictation (up to 3 minutes); acceptable on 16 GB, to be measured on 8 GB with the browser etc. open; idle must still hold no model |
| 9 | Latency of insertion | Single paste after the full result; cancellation, focus and clipboard checks happen once | Insert only once, after the last segment (partial insertion into other applications would reintroduce the corruption and cancel/undo problems); the user sees progress in the app, not in the target field |
| 10 | Replay, retention and cancellation | Replay uses one retained buffer; cancel kills one process | The retained audio must be the complete recording; cancel must stop capture, discard queued chunks, kill the worker and release buffers; Caps Lock cancel/lock/sleep handling must cover a worker that is already running |
| 11 | Diagnostics | One result record | Per-segment timing (queue wait, inference), lag, dropped/withheld segments; no text or audio in diagnostics |
| 12 | Cloud path | One HTTPS request with the whole WAV, 120 s timeout | A cloud streaming API (not used by the current hosted endpoint) or chunked requests with the same consent, no automatic retry and no fallback rules |

## Design constraints that follow from the requirements

- Idle: no microphone, no worker, no model. The worker would start at the hold threshold and exit after the last segment, so the resident-model cost applies only while dictating.
- Audio loss is the worst failure: the capture path must stay independent of inference. If inference lags, show it and let the user finish; never overwrite or drop queued audio.
- Quality first: segment boundaries come from VAD pauses, not fixed chunks, with a measured policy for overlap. A fixed-interval split would cut words and is not acceptable.
- Pre-conditions before implementing: (a) RTF/RAM/CPU of the candidate model on the owner's real 8 GB machine, (b) a dictation set of 30–60 s recordings with references for before/after WER, (c) the decision whether Czech quality requires a model that cannot run faster than real time on that machine, in which case cloud or a smaller scope is the honest answer.

## What can be prepared without committing to streaming

1. Chunk-based capture queue behind the existing capture class, still delivering the whole buffer at the end (verifies thread safety and memory behaviour without changing the pipeline).
2. A framed worker protocol that accepts one chunk and the final flush, so the present behaviour is the degenerate case.
3. Per-segment evaluation in `transcript_guard.h` with the same single-segment results.
4. A benchmark that simulates streaming on recorded audio (feed chunks at real-time speed, measure end latency and WER versus the one-shot result) so the design is judged on data before it reaches the application.
