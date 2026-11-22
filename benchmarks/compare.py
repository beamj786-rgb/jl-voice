"""Developer-only paired CPU model comparison; no model download or default change."""
import argparse
import json
import pathlib
import statistics
import subprocess
import sys

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', required=True, type=pathlib.Path)
    parser.add_argument('--model', required=True, action='append', type=pathlib.Path)
    parser.add_argument('--corpus', required=True, type=pathlib.Path)
    parser.add_argument('--output', required=True, type=pathlib.Path)
    parser.add_argument('--repeats', type=int, default=2)
    parser.add_argument('--threads', type=int, choices=[1,2], default=2)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    results, paired = [], None
    for index, model in enumerate(args.model):
        output = args.output / f'model-{index}.json'
        subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name('run.py')), '--runtime', str(args.runtime), '--model', str(model), '--corpus', str(args.corpus), '--output', str(output), '--repeats', str(args.repeats), '--threads', str(args.threads)], check=True)
        report = json.loads(output.read_text(encoding='utf-8'))
        identity = [(r['id'], r['mode'], r['repeat'], r['audio_sha256']) for r in report['samples']]
        if paired is not None and identity != paired: raise RuntimeError('Model runs did not use the same audio/modes/repeats')
        paired = identity
        for group, scores in report['groups'].items():
            rows = [r for r in report['samples'] if f"{r['group']}/{r['mode']}" == group]
            results.append(dict(model=model.name, group=group, **scores,
                median_inference_ms=statistics.median(r['inference_ms'] for r in rows),
                median_worker_wall_ms=1000*statistics.median(r['transcription_wall_seconds'] for r in rows),
                peak_sampled_rss_bytes=max(r['sampled_peak_family_rss_bytes'] for r in rows),
                os_peak_worker_rss_bytes=max((r['os_peak_worker_rss_bytes'] for r in rows if r['os_peak_worker_rss_bytes'] is not None), default=None),
                mean_sampled_cpu_percent_one_core=statistics.mean(r['sampled_cpu_percent_one_core'] for r in rows)))
    (args.output/'comparison.json').write_text(json.dumps(dict(results=results, cpu_only=True, minimum_hardware_accepted=False,
        limitations=['Paired recordings, not a representative user-quality acceptance set.', 'RSS/CPU sampled; short peaks may be missed.', 'Sequential models and repeats have uncontrolled OS file-cache state.', 'No microphone, insertion, clean offline Windows, desktop responsiveness or 8 GB acceptance.']), indent=2),encoding='utf-8')

if __name__ == '__main__': main()
