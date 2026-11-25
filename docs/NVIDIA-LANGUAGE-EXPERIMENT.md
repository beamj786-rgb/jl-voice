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

- `minimal` (**recommended first run**): one Czech clip (`cs-1652`, "Hlavním náboženstvím Moldavska je ortodoxní křesťanství.") and one English clip (`en-1554`) — the two clips the owner already ran through the CLI probe, so earlier results are directly comparable — with three variants: `cs-CZ`, `en-GB` (deliberately wrong for Czech speech) and the field omitted. **6 requests.**
- `short`: the three shortest Czech and English clips (about 3–5 s) x six variants (`cs-CZ`, `cs`, `multi`, `en-GB`, `en-US`, omitted) = 36 requests.
- `duration`: concatenations of the selected clips (0.4 s digital silence between them) of about 6.9 / 15.4 / 29.9 / 45.2 / 61.0 s (Czech) and 7.3 / 15.5 / 33.0 / 47.0 / 62.1 s (English), six variants = 60 requests. The "5 s" sample is about 7 s because FLEURS clips are at least 3 s.
- `all`: `short` + `duration` = 96 requests.

Requests are sequential with a 1.5 s pause and no retries. Whether the endpoint accepts 45–60 s requests is also an experiment result (a hosted demo page is said to limit clips to 5 minutes; not verified).

Reading the minimal result: if `cs-CZ`, `en-GB` and the omitted field give the **same** text on both clips, the field is not used for these inputs; if the Czech clip changes under `en-GB`, the field influences decoding (not necessarily that it is enforced). The Czech clip was transcribed perfectly in the owner's earlier run, so a degradation under `en-GB` would be visible.

## Run it (Windows, owner's key)

Nothing is sent unless `--send` is given. With `--send` the key is typed into a **hidden console prompt** inside the program (input is not echoed, Escape cancels). It is not a command-line argument, not an environment variable and not a file.

```powershell
# 1. Dry run (no key, nothing sent): audio files, hashes and the exact number of requests
.\JLVoice.NvidiaProbe.exe --language-experiment --set minimal

# 2. Real run: 6 requests, counts against your NVIDIA account limits
.\JLVoice.NvidiaProbe.exe --language-experiment --set minimal --send

# 3. Only if the minimal result justifies it
.\JLVoice.NvidiaProbe.exe --language-experiment --set short --send       # 36 requests
.\JLVoice.NvidiaProbe.exe --language-experiment --set duration --send    # 60 requests
```

Useful limits: `--max-requests N` (default 100, hard cap 200), `--params cs-CZ,omit`, `--pause-ms 3000`. The run stops at once on HTTP 401, 403 or 429 (no retry, no more credits) and after three consecutive network failures; a partial run is marked `complete: false`. For scripted use only, `--key-from-env` reads `NVIDIA_API_KEY` instead (see below for why the prompt is preferred).

### Key handling, honestly

- The key is held as a .NET string for the lifetime of the process. .NET strings are immutable and cannot be zeroed, so **memory is not scrubbed**; the key disappears when the program exits (a crash dump or the Windows page file could in principle contain it, as for any program that handles a credential).
- The hidden prompt avoids the two avoidable leaks of the earlier idea: no environment variable (visible to child processes and `Get-ChildItem Env:`) and no `SecureString` → `BSTR` conversion. If you ever script the `--key-from-env` route, `Marshal.SecureStringToBSTR` allocates unmanaged memory that **must** be released with `Marshal.ZeroFreeBSTR`, and the converted string again becomes an ordinary managed string — so it adds nothing over the prompt.
- The key goes only into the `Authorization` header of HTTPS requests to the fixed endpoint (redirects and cookies disabled). The report and console output never contain it (the report writer refuses to write if it finds the key; the self-test checks this, and that rejection/401 responses never echo it).
- Do not paste the key into chat, an issue, a screenshot or a report. Rotate it in your NVIDIA account if you suspect exposure.

The result is `nvidia-language-experiment-<utc>-<id>.json` beside the executable: public reference texts, transcripts, HTTP statuses, WER/CER, request times, extra response fields and the analysis, never the key. It contains only public FLEURS text, so it is safe to share.

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
