"""Developer-only paired Czech/English decoding experiment on identical audio.

Each configuration runs the same corpus through the real CPU worker. Writes raw
per-configuration reports and a summary; never changes app defaults.
"""
import argparse
import json
import pathlib
import statistics
import subprocess
import sys

CONFIGS = [  # (name, model, run.py flags)
    ('base', 'ggml-base-q5_1.bin', []),
    ('base+norm+prompt', 'ggml-base-q5_1.bin', ['--normalize', '--czech-prompt']),
    ('small', 'ggml-small-q5_1.bin', []),
    ('small+norm', 'ggml-small-q5_1.bin', ['--normalize']),
    ('small+prompt', 'ggml-small-q5_1.bin', ['--czech-prompt']),
    ('small+norm+prompt', 'ggml-small-q5_1.bin', ['--normalize', '--czech-prompt']),
    ('small+short', 'ggml-small-q5_1.bin', ['--short-context']),
    ('small+norm+prompt+short', 'ggml-small-q5_1.bin', ['--normalize', '--czech-prompt', '--short-context']),
    ('turbo+short', 'ggml-large-v3-turbo-q5_0.bin', ['--short-context']),
    ('turbo+norm+prompt+short', 'ggml-large-v3-turbo-q5_0.bin', ['--normalize', '--czech-prompt', '--short-context']),
    ('turbo', 'ggml-large-v3-turbo-q5_0.bin', []),
    ('turbo+norm+short', 'ggml-large-v3-turbo-q5_0.bin', ['--normalize', '--short-context']),
]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', required=True, type=pathlib.Path)
    parser.add_argument('--models', required=True, type=pathlib.Path)
    parser.add_argument('--corpus', required=True, type=pathlib.Path)
    parser.add_argument('--output', required=True, type=pathlib.Path)
    parser.add_argument('--only', action='append', help='run only these configuration names')
    parser.add_argument('--vad-model', type=pathlib.Path, help='run every configuration with the VAD gate and guard')
    parser.add_argument('--label', default='', help='suffix for output files and log lines')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    summary, identity = [], None
    for name, model, flags in CONFIGS:
        if args.only and name not in args.only: continue
        path = args.models / model
        if not path.exists():
            summary.append(dict(config=name, model=model, status='model unavailable; no result')); continue
        out = args.output / f'{name}{args.label}.json'
        try:
            subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name('run.py')), '--runtime', str(args.runtime),
                            '--model', str(path), '--corpus', str(args.corpus), '--output', str(out), '--repeats', '1',
                            '--threads', '2', '--modes', 'explicit', *flags,
                            *(['--vad-model', str(args.vad_model)] if args.vad_model else [])], check=True, timeout=3600)
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
            summary.append(dict(config=name, model=model, status=f'failed: {type(error).__name__}; no result')); continue
        report = json.loads(out.read_text(encoding='utf-8'))
        ids = [(r['id'], r['mode'], r['audio_sha256']) for r in report['samples']]
        if identity is not None and ids != identity: raise RuntimeError('Configurations did not use identical audio')
        identity = ids
        speech = [r for r in report['samples'] if r['group'] in ('cs', 'en')] or [r for r in report['samples'] if r['reference']]
        summary.append(dict(config=name, model=model, status='complete', groups=report['groups'],
            median_inference_ms=statistics.median(r['inference_ms'] for r in speech),
            median_rtf=statistics.median(r['rtf'] for r in speech),
            max_worker_wall_s=max(r['transcription_wall_seconds'] for r in speech),
            os_peak_worker_rss_bytes=max((r['os_peak_worker_rss_bytes'] or 0) for r in report['samples']) or None,
            detected_language_mismatches=sum(r['detected_language'] not in (None, r['mode']) for r in speech),
            verdicts={g: {v: sum(1 for r in report['samples'] if r['group'] == g and r.get('verdict') == v) for v in ('ok', 'no_speech', 'suspicious', None)}
                      for g in sorted({r['group'] for r in report['samples']})},
            samples=[dict(id=r['id'], mode=r['mode'], audio_seconds=round(r['audio_seconds'], 2), inference_ms=round(r['inference_ms']),
                          wall_s=round(r['transcription_wall_seconds'], 2), rtf=round(r['rtf'], 3), audio_ctx=r.get('audio_ctx'),
                          peak_rss_bytes=r['os_peak_worker_rss_bytes'], detected=r['detected_language'],
                          words=r['score']['words'], sub=r['score']['substitutions'], dele=r['score']['deletions'], ins=r['score']['insertions'],
                          chars=r['score']['characters'], char_errors=r['score']['character_errors'], text=r['text'],
                          group=r['group'], verdict=r.get('verdict'), reasons=r.get('reasons'), vad_speech_seconds=r.get('vad_speech_seconds'),
                          segments=r.get('segments')) for r in report['samples']]))
    meta = json.loads(args.corpus.read_text(encoding='utf-8'))
    (args.output / f'summary{args.label}.json').write_text(json.dumps(dict(corpus=dict((k, meta[k]) for k in ('dataset', 'revision', 'split', 'selection')),
        references={s['id']: s['reference'] for s in meta['samples']}, results=summary, cpu_only=True, threads=2,
        minimum_hardware_accepted=False,
        limitations=['Public read speech, not owner dictation or microphone audio.', 'Hosted CI CPU, not 8 GB low-end hardware.',
                     'One run per clip; latency comparisons share one machine but not controlled cache state.',
                     'No mixed Czech/English public reference is included; mixed speech remains unmeasured.']),
        ensure_ascii=False, indent=2), encoding='utf-8')
    for row in summary:
        groups = row.get('groups', {})
        fmt = lambda g, k: f"{groups[g][k]:.3f}" if g in groups and groups[g].get(k) is not None else 'n/a'
        print(f"{row['config']:28} cs WER {fmt('cs/cs','wer')} CER {fmt('cs/cs','cer')} | en WER {fmt('en/en','wer')} CER {fmt('en/en','cer')} "
              f"| cs-long WER {fmt('cs-long/cs','wer')} CER {fmt('cs-long/cs','cer')} | en-long WER {fmt('en-long/en','wer')} "
              f"| ctrl halluc {groups.get('control/cs', {}).get('hallucinated_words', 'n/a')} | med inf {row.get('median_inference_ms', 'n/a')} ms | {row['status']}")
    # Content-free metrics plus public-corpus/control texts, printed because artifact storage may be unreachable.
    for row in summary:
        print('EXPERIMENT_DETAIL ' + json.dumps(dict(label=args.label, **{k: row.get(k) for k in ('config', 'status', 'median_inference_ms', 'median_rtf', 'max_worker_wall_s',
              'os_peak_worker_rss_bytes', 'detected_language_mismatches', 'groups', 'verdicts')})))  # ASCII: Windows console is not UTF-8
        for sample in row.get('samples', []):
            print('EXPERIMENT_SAMPLE ' + json.dumps(dict(config=row['config'], label=args.label, **sample)))

if __name__ == '__main__': main()
