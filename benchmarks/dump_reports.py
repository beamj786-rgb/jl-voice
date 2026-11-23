"""Audit helper: print experiment reports (public corpus only) as ASCII JSON lines for log-based review."""
import json
import pathlib
import sys

def main():
    for root in map(pathlib.Path, sys.argv[1:]):
        for path in sorted(root.rglob('*.json')):
            data = json.loads(path.read_text(encoding='utf-8'))
            label = f'{root.name}/{path.relative_to(root)}'
            if 'samples' in data and isinstance(data['samples'], list) and data['samples'] and 'score' in data['samples'][0]:
                print('AUDIT_REPORT ' + json.dumps(dict(file=label, model=data.get('model'), model_sha256=data.get('model_sha256'),
                      options=data.get('options'), groups=data.get('groups'), cpu=data.get('cpu'), logical_cpus=data.get('logical_cpus'))))
                for row in data['samples']:
                    print('AUDIT_SAMPLE ' + json.dumps(dict(file=label, **{k: row.get(k) for k in (
                        'id', 'group', 'mode', 'audio_seconds', 'audio_ctx', 'applied_gain', 'prompt', 'detected_language',
                        'model_load_ms', 'inference_ms', 'transcription_wall_seconds', 'rtf', 'os_peak_worker_rss_bytes', 'audio_sha256', 'text', 'score')})))
            elif 'samples' in data and data.get('dataset'):
                for row in data['samples']:
                    print('AUDIT_REFERENCE ' + json.dumps(dict(file=label, **row)))
            else:
                print('AUDIT_OTHER ' + json.dumps(dict(file=label, keys=sorted(data)))[:2000])

if __name__ == '__main__': main()
