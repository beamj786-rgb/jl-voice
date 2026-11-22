"""Developer-only real CPU benchmark. Never installs models or fabricates samples."""
import argparse
import hashlib
import json
import pathlib
import platform
import subprocess
import threading
import time
import wave
import psutil
from score import align

def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def run_sample(executable, model, audio, language, threads):
    with wave.open(str(audio)) as f:
        if (f.getnchannels(), f.getframerate(), f.getsampwidth()) != (1, 16000, 2):
            raise ValueError(f'{audio}: expected mono 16 kHz 16-bit PCM WAV')
        duration = f.getnframes() / f.getframerate()
        if duration <= 0:
            raise ValueError('Empty WAV')
    command = [str(executable), '--model', str(model), '--language', language,
               '--threads', str(threads), '--cpu-only']
    started = time.perf_counter()
    p = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    process = psutil.Process(p.pid)
    try:
        process.nice(psutil.BELOW_NORMAL_PRIORITY_CLASS if platform.system() == 'Windows' else 5)
    except psutil.Error:
        p.kill(); p.communicate()
        raise RuntimeError('Could not apply below-normal worker priority')
    peak_rss, cpu_seconds = 0, 0.0
    done = threading.Event()
    def monitor():
        nonlocal peak_rss, cpu_seconds
        while not done.is_set():
            try:
                family = [process] + process.children(recursive=True)
                peak_rss = max(peak_rss, sum(x.memory_info().rss for x in family))
                cpu_seconds = max(cpu_seconds, sum(sum(x.cpu_times()[:2]) for x in family))
            except psutil.Error:
                pass
            done.wait(0.02)
    watcher = threading.Thread(target=monitor, daemon=True)
    watcher.start()
    try:
        output, error = p.communicate(input=audio.read_bytes(), timeout=300)
    except BaseException:
        p.kill(); p.communicate()
        raise
    finally:
        done.set(); watcher.join()
    elapsed = time.perf_counter() - started
    if p.returncode:
        raise RuntimeError(f'ASR exited {p.returncode}; no benchmark result accepted')
    metrics = [json.loads(line[16:]) for line in error.decode('utf-8').splitlines() if line.startswith('JLVOICE_METRICS ')]
    if len(metrics) != 1: raise RuntimeError('Missing or ambiguous worker inference metrics')
    return dict(text=output.decode('utf-8').strip(), audio_seconds=duration,
                inference_ms=metrics[0]['inference_ms'], model_load_ms=metrics[0]['load_ms'],
                detected_language=metrics[0]['detected_language'],
                os_peak_worker_rss_bytes=metrics[0].get('peak_rss_bytes'),
                worker_cpu_ms=metrics[0].get('process_cpu_ms'),
                transcription_wall_seconds=elapsed, rtf=elapsed / duration,
                sampled_peak_family_rss_bytes=peak_rss,
                sampled_cpu_seconds=cpu_seconds,
                sampled_cpu_percent_one_core=100 * cpu_seconds / elapsed,
                end_recording_to_insertion_ms=None)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', required=True, type=pathlib.Path)
    parser.add_argument('--model', required=True, type=pathlib.Path)
    parser.add_argument('--corpus', default=str(pathlib.Path(__file__).with_name('corpus.json')), type=pathlib.Path)
    parser.add_argument('--threads', type=int, choices=(1, 2), default=2)
    parser.add_argument('--repeats', type=int, default=2)
    parser.add_argument('--output', required=True, type=pathlib.Path)
    args = parser.parse_args()
    if args.repeats < 1: parser.error('--repeats must be positive')
    executable, model = args.runtime.resolve(strict=True), args.model.resolve(strict=True)
    if '.en' in model.name: parser.error('English-only models cannot be evaluated as JL Voice defaults')
    corpus = json.loads(args.corpus.read_text(encoding='utf-8'))
    for sample in corpus['samples']:
        (args.corpus.parent / sample['audio']).resolve(strict=True)
    rows = []
    for sample in corpus['samples']:
        audio = (args.corpus.parent / sample['audio']).resolve()
        # Auto is tested alongside the matching explicit language; mixed tests all modes.
        modes = ['auto', sample['language']] if sample['language'] in ('cs', 'en') else ['auto', 'cs', 'en']
        for mode in modes:
            for repeat in range(args.repeats):
                row = run_sample(executable, model, audio, mode, args.threads)
                row.update(id=sample['id'], group=sample['language'], mode=mode, repeat=repeat,
                           audio_sha256=sha256(audio), score=align(sample['reference'], row['text']))
                if sample.get('audio_sha256') and sample['audio_sha256'] != row['audio_sha256']:
                    raise RuntimeError('Corpus audio hash mismatch')
                rows.append(row)
    groups = {}
    for row in rows:
        key = f"{row['group']}/{row['mode']}"
        summary = groups.setdefault(key, dict(words=0, errors=0, characters=0, character_errors=0))
        summary['words'] += row['score']['words']
        summary['errors'] += sum(row['score'][k] for k in ('substitutions', 'deletions', 'insertions'))
        summary['characters'] += row['score']['characters']
        summary['character_errors'] += row['score']['character_errors']
    for group in groups.values():
        group['wer'] = group['errors'] / group['words'] if group['words'] else None
        group['cer'] = group['character_errors'] / group['characters'] if group['characters'] else None
    report = dict(schema=1, platform=platform.platform(), cpu=platform.processor(),
                  logical_cpus=psutil.cpu_count(), physical_ram_bytes=psutil.virtual_memory().total,
                  cpu_only=True, threads=args.threads, model_sha256=sha256(model),
                  executable_sha256=sha256(executable), groups=groups, samples=rows,
                  minimum_hardware_accepted=False,
                  limitations=['ASR worker only, not full app memory or insertion latency.',
                               'Family RSS/CPU sampled every 20ms; short peaks/exit CPU may be missed. OS worker peak/CPU counters are captured by the worker just after inference when available.',
                               'Each run reloads the model. Repeats warm filesystem caches, not a resident worker.',
                               'Cold boot, idle app resources, desktop activity and physical hardware qualification require separate runs.',
                               'WER ignores punctuation; punctuation and mixed-language usability need human review.'])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

if __name__ == '__main__':
    main()
