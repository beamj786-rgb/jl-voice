# Decisions

- **2026-11-21 — CPU-only is the baseline.** The owned native worker sets `use_gpu=false`, requires `--cpu-only` and is built without GPU backends. Maximum two inference threads; below-normal worker priority. Hardware qualification is outstanding.
- **Multilingual candidates, no accuracy claim.** Start comparisons with base q5_1, base q8_0 and small q5_1. Pin upstream revision and SHA-256. The first candidate is not an accepted default. RAM alone does not select a larger model.
- **Unload every recording initially.** Easiest to bound idle memory without an always-on inference service. Measure the cold-latency penalty and a retained-model alternative before accepting a residency policy.
- **Native Windows controls, self-contained folder.** Prefer an initially maintainable microphone pipeline over a custom renderer; revisit runtime overhead based on measured full-app memory. Single-file distribution is not worth extra extraction/startup costs without evidence.
- **No automatic formatting yet.** Preserve identifiers and meaning; no LLM or speculative grammar rewriting. Optional Natural/Professional/Code postprocessing is still planned.
- **No community implementation copied.** Public research is a secondary source with platform/version limits; no license was observed at its root.
- **Own the pipe boundary.** A real upstream CLI smoke test and source inspection exposed stdout behavior incompatible with redirected Windows use. A small C++ C-API worker now owns WAV validation, bounded CPU-only arguments and direct UTF-8 output; no transcript/audio temporary files or upstream patch are needed.
