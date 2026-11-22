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
