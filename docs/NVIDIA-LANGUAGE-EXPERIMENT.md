# NVIDIA language-parameter experiment

Question: does the `language` form field of the hosted NVIDIA endpoint used by JL Voice actually influence decoding, and which values does it accept? Observed so far: Czech speech with `language=cs-CZ` produced Polish-looking text. Nothing in this repository proves why. This experiment measures it with identical public audio. **It has not been run yet**; no NVIDIA request was made while preparing it.

## What is verified about NVIDIA's documentation (2026-11-25)

Primary NVIDIA pages (build.nvidia.com, docs.nvidia.com, Hugging Face) are not reachable from the analysis environment. The statements below come from search-result excerpts of those pages and are therefore **secondary evidence**; re-read the pages before relying on them. The three kinds of "availability" are different things:

| Kind | What it is | What was found |
|---|---|---|
| Downloadable model | Weights on Hugging Face/NGC | `nvidia/parakeet-tdt-0.6b-v3`: 25 European languages including Czech, automatic language detection, FLEURS Czech WER 11.01% (model card). `nvidia/canary-1b-v2`: 25 languages, FLEURS Czech 7.86%, language selectable. Licence is stated as CC-BY-4.0 by NGC/Hugging Face; build.nvidia.com mentions an NVIDIA open model licence for v3, so confirm on the model card. |
| Self-hosted NIM container | The same models served from your own machine | NIM documentation: Czech is `cs-CZ`; `multi` is the code for automatic language detection; per release notes, omit the language for auto-detection or pass a code "to constrain decoding"; the HTTP path is `/v1/audio/transcriptions`. |
| Hosted endpoint (what JL Voice calls) | `https://2b940e91-…invocation.api.nvcf.nvidia.com/v1/audio/transcriptions` | The only documented example (build.nvidia.com Parakeet TDT 0.6B API tab) sends `language=en-GB`. Accepted values, whether `cs-CZ`/`multi` are valid, whether the field constrains decoding, which exact model version is served, and whether the response reports a detected language are **not documented** in anything found. |

NIM documentation does not prove the behaviour of the hosted function. A search of the build.nvidia.com ASR catalogue (cached snippets) showed most ASR models as downloadable rather than hosted; the live "free endpoint" list was not verified.

## Hypotheses this experiment separates

1. **The field is ignored** (server auto-detects): output identical for every variant, including deliberately wrong ones (`en-GB` on Czech speech).
2. **The field constrains decoding**: output differs between variants; Czech audio with `en-*` gets English-like or degraded text and `cs-CZ` is best for Czech.
3. **Values are validated**: some values (for example `multi`) are rejected with HTTP 4xx.
4. **The response says which language was used**: extra response fields (for example `language`) are recorded when present.

The report states the finding in words, but "different output" only proves influence, not that the requested language is enforced, and "identical output" only proves the field is unused for those inputs.

## Audio

Public FLEURS dev read speech (CC-BY-4.0, pinned revision in `public-corpus/corpus.json`), no owner recordings:

- `short` (default): the three shortest Czech and three shortest English clips (about 3–5 s, the same six used by the earlier CLI probe).
- `duration`: concatenations of the selected clips (0.4 s of digital silence between them) targeting about 5, 15, 30, 45 and 60 s for Czech and English; the real length is recorded in the report (the 5 s sample is about 6–7 s because FLEURS clips are at least 3 s).
- `all`: both.

Each audio file is sent once per language variant (`cs-CZ`, `cs`, `multi`, `en-GB`, `en-US`, and the field omitted), sequentially with a 1.5 s pause, no retries. `short` = 36 requests, `duration` = 60, `all` = 96. Whether the endpoint accepts 45–60 s requests is also an experiment result (the hosted demo page is said to limit clips to 5 minutes; not verified).

## Run it (Windows, owner's key)

The command prints the plan and sends nothing unless `--send` is given. The key is read only from the `NVIDIA_API_KEY` environment variable of that PowerShell session; it is never printed, written to the report, put on the command line or stored.

```powershell
# 1. Dry run (no key, nothing sent): shows audio files, hashes and the exact number of requests
.\JLVoice.NvidiaProbe.exe --language-experiment --set short

# 2. Real run, short clips first (36 requests; counts against your NVIDIA account limits)
$k = Read-Host 'NVIDIA API key' -AsSecureString
$env:NVIDIA_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($k))
.\JLVoice.NvidiaProbe.exe --language-experiment --set short --send
Remove-Item Env:NVIDIA_API_KEY

# 3. Optional: duration samples (60 requests), re-using the same key setup
.\JLVoice.NvidiaProbe.exe --language-experiment --set duration --send
```

Useful limits: `--max-requests N` (default 100, hard cap 200), `--params cs-CZ,omit` (subset), `--pause-ms 3000`. The run stops at once on HTTP 401, 403 or 429 (no retry, no further credits) and after three consecutive network failures. A partial run is marked `complete: false`.

The result is `nvidia-language-experiment-<utc>-<id>.json` beside the executable. It contains public reference texts, transcripts, HTTP statuses, WER/CER, request times, extra response fields and the analysis, never the key. Review it before sharing: it contains only public FLEURS text.

## Reading the report

- `analysis.language_parameter_effect`: the one-sentence finding.
- `analysis.per_audio`: the outputs of every variant for each file.
- `analysis.per_group_and_variant`: WER/CER and median request time per language group and variant; `czech_only_letters` / `polish_only_letters` are a coarse hint only (letters ě ř ů versus ł ą ę ś ć ń ź ż), not language identification.
- `analysis.rejected_values`: values answered with HTTP 4xx (validation), with their statuses.
- `analysis.response_fields_besides_text` / `server_reports_detected_language`: whether the server tells which language it decoded.
- `requests[].http_status`, `outcome`, `error_category`: HTTP 200 with an empty transcript is `Response`, not a network failure.

Latency is request time (upload, queue, inference, response) and is comparable only within one run; server inference time and the exact model are unknown (`model` is reported as unknown).

## What the result would change

| Finding | Consequence |
|---|---|
| Field ignored, Czech output wrong | Hosted Parakeet cannot be forced to Czech: stop recommending it for Czech; look for another NVIDIA-hosted model or keep cloud English-only. |
| Field changes output, `cs-CZ` clearly better on Czech | The current contract is right; poor Czech is model quality or audio level; compare with offline on the same audio. |
| `multi` accepted | Cloud Auto could be evaluated (still disabled in the app until measured). |
| 45–60 s accepted with sane latency | Cloud is a candidate for the 30–60 s dictation target. |

Nothing in the application changes because of this experiment. The shared client `NvidiaSpeechClient` carries the experiment through a separate entry point (`TranscribeExperimentalAsync`) that accepts only the six fixed variants; the application path is unchanged.
