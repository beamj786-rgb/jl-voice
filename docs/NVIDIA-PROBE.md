# NVIDIA model probes — developer preview

This is a separate diagnostic kit, not a replacement JL Voice application or a production release. The desktop app and default offline behavior are unchanged. The owner authorized comparison of NVIDIA cloud/local speech models. No API key is embedded or collected by CI; no cloud audio request is made during build.

## Windows cloud test, no Python or runtime install

1. Extract the entire `JLVoice-NVIDIA-Cloud-Test-Windows-x64` ZIP to a new writable folder on the Windows PC that permits development executables.
2. Run `JLVoice.NvidiaProbe.exe`. The console explains that it will upload the six included public FLEURS Czech/English recordings to NVIDIA. No microphone or private dictation is read.
3. Paste your own NVIDIA Developer API key at the hidden prompt and press Enter. Input is deliberately invisible. Escape cancels. Never send the key in chat, place it on the command line, commit it or include it in a report.
4. The tool sends each integrity-checked fixture once, with `cs-CZ` or `en-GB`, and prints the reference, returned text and total request time. It stops on failure; there are no automatic retries or unlimited credit consumption.
5. Inspect the new `nvidia-results-*.json` file. It contains recognized public text, reference text, audio hashes, per-sample WER/CER and measured request wall time. It does not contain your key. Send that report back for comparison; a partial run has `complete=false` and cannot be accepted as a completed benchmark.

The official HTTP endpoint is fixed to:
`https://2b940e91-a70e-4483-a958-d50408b96589.invocation.api.nvcf.nvidia.com/v1/audio/transcriptions`

Source: https://build.nvidia.com/nvidia/parakeet-tdt-0_6b/api . This is the hosted multilingual TDT deployment, not the English-only v2 endpoint. Exact server weights/version are not attested, and it must not be described as byte-identical to the local INT8 conversion. Cloud services can reject/limit requests or rotate deployments. API access/availability, Czech locale acceptance and real cloud latency remain unverified until a successful real request.

The API key is held in process memory only and put only in the Authorization header to the fixed HTTPS destination. Redirects are disabled, requests/body reads are time-bounded, and response sizes are bounded. Error reports expose HTTP status only, not raw response bodies, headers or credentials. This does not promise erasure from OS paging/crash dumps. The owner supplies an existing account/key; the kit does not create accounts, accept terms or change billing. NVIDIA's development/trial terms and account limits apply.

If Windows blocks the unsigned development executable, report the block and stop; do not disable Smart App Control or other protection. The existing trusted-signing limitation still applies.

## Timing and quality interpretation

Cloud request time includes upload, network, queue, inference and receiving the response. Server inference time, GPU/CPU use and peak RAM are unavailable and remain null. This is not end-of-recording-to-insertion time; the kit does not insert text into another app. A first request may include TLS/cache warmup. It is not valid to compare cloud request wall time directly to inference-only local timing.

WER/CER preserve Czech diacritics and normalize NFC/case/word punctuation. Six short read-speech recordings (three per language), repeated runs and formatting variants are not representative quality acceptance. Mixed speech, natural longer dictation and silence/noise need separate samples. The public samples include quiet recordings and uncommon names; listen to the included WAVs and review actual text, not only aggregate metrics.

## Local CPU Parakeet benchmark

`benchmarks/fetch_parakeet.py` verifies the official sherpa-onnx conversion archive's SHA-256 and byte length before extraction. `benchmarks/parakeet.py` uses sherpa-onnx 1.12.40, explicit CPU provider, two below-normal-priority threads, greedy transducer decode, mono 16 kHz PCM16 input, and the exact public fixture hashes from the prior Whisper comparison. It reports load, feature/decode inference, complete worker-process wall time, CPU, OS peak RSS and transcript scores separately. Each sample reloads the model; repetitions warm disk caches only. Language is automatic; explicit forcing and mixed-language behavior are not established.

Python/sherpa-onnx are developer benchmark dependencies only; they are not installed by the Windows cloud test kit or added to the JL Voice app.

## Licensing and attribution

Parakeet TDT 0.6B v3: NVIDIA and contributors, CC-BY-4.0, https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 . Local conversion: k2-fsa/sherpa-onnx, https://k2-fsa.github.io/sherpa/onnx/pretrained_models/offline-transducer/nemo-transducer-models.html . sherpa-onnx runtime is Apache-2.0; its benchmark wheels are not included in the cloud kit. Cloud NIM deployment terms are separate from downloaded model weights.

Included public audio/reference text: FLEURS, Google and contributors, CC-BY-4.0, https://huggingface.co/datasets/google/fleurs . Revision, prompt IDs, original/converted audio hashes and conversion details are preserved in `public-corpus/corpus.json`. These samples retain their source license and are not relicensed under JL Voice's original-code MIT license. .NET self-contained runtime retains its distributed notices. No owner recordings or credentials are bundled.

## Measured results and current conclusion

[Windows CI run 45059057919](https://github.com/beamj786-rgb/jl-voice/actions/runs/45059057919), source `13455ab8c09781ca4bdd1b8828d2ebe042dabf0b`, passed real local Parakeet CPU inference and self-contained cloud-kit publish/contract checks. Cloud API tests in CI were mocked: **no real NVIDIA API request or cloud benchmark result is claimed**. [Raw measurements, returned text, corpus hashes and provenance](benchmark-evidence/2026-11-22-nvidia/).

Hosted Windows Server, AMD Family 25 Model 17, four logical CPUs, approximately 16 GB RAM; CPU provider, two threads, below-normal priority. Three unique clips per language, two fresh-process runs per clip (20 Czech and 31 English unique reference words). Repeated runs are not additional independent speech coverage. WER/CER lower is better. Inference includes feature preparation/decode, not model load or process startup. Peak RAM is the entire benchmark worker, excluding the WPF app.

| Language | WER | CER | Median inference | Median model load | Median worker total | Peak worker RAM |
|---|---:|---:|---:|---:|---:|---:|
| Czech | 60.0% | 25.8% | 0.188 s | 1.759 s | 2.128 s | 760.0 MiB |
| English | 48.4% | 34.2% | 0.192 s | 1.795 s | 2.150 s | 759.9 MiB |

Mean process CPU utilization was approximately 109%/108% of one logical core, including model loading and worker lifetime. The earlier Whisper Windows probe used another hosted CPU model; these timings do not establish a controlled speedup over Whisper. Local warm model residency could avoid repeated initialization but would retain substantial RAM; that trade-off is not implemented or accepted for 8 GB everyday use.

Actual returned text illustrates both outcomes: the Czech Moldovan-religion sentence is mostly recognizable with inflection/spelling errors; a Czech clip with foreign names was transcribed as Polish-looking text. One English clip is good apart from formatting, while a very quiet clip produces unrelated words. Parakeet therefore is **not a quality-qualified replacement**. Explicit CZ/EN forcing and mixed-language usability are still unverified for this local TDT adapter.

### Separate gain experiment

The public English clips have very low PCM amplitude (roughly 0.0004–0.0008 full-scale RMS). A separate Linux probe used the exact same source clips with experimental peak normalization to 0.8 full scale, preserving durations/rates; source/transformed hashes and gain factors are archived. With the same Parakeet model, English WER changed from 48.4% to 22.6% and CER from 34.2% to 5.2%; Czech WER changed from 50% to 55%. These are tiny-set exploratory results, not a universal gain rule or a fair comparison to unmodified input. No gain normalization was added to JL Voice. This is evidence that input level matters and that a different model alone is not sufficient to diagnose the owner's capture.

### Windows probe failure and repair

The initial Windows benchmark failed before producing an accepted report: redirected Python stdout used cp1252 and could not encode Czech `ř`. The failure was confirmed in Windows logs and reproduced locally with `PYTHONIOENCODING=cp1252`. Worker protocol now uses ASCII JSON escapes; UTF-8 report files restore the exact Unicode text. A protocol regression check covers Czech diacritics and emoji across a cp1252 pipe. This fixes the developer benchmark's transport, not an asserted cause of prior JL Voice WPF/native insertion errors. No failed-run scores are included above.

The cloud kit can be downloaded from [the successful Windows run](https://github.com/beamj786-rgb/jl-voice/actions/runs/45059057919/artifacts/14768831898). The earlier independently built kit is the same cloud client and remains usable; the later change repaired only the local Python benchmark protocol. The app itself was not rebuilt/replaced or given a cloud default. No release/deployment occurred.
