"""Developer-only CPU Parakeet TDT v3 INT8 probe. Never used by the shipped app."""
import argparse
import hashlib
import json
import pathlib
import platform
import statistics
import struct
import subprocess
import sys
import time
import wave
import psutil
from score import align

FILES = ('encoder.int8.onnx', 'decoder.int8.onnx', 'joiner.int8.onnx', 'tokens.txt')

def sha256(path):
    with path.open('rb') as file: return hashlib.file_digest(file, 'sha256').hexdigest()

def worker(model, audio):
    process = psutil.Process()
    process.nice(psutil.BELOW_NORMAL_PRIORITY_CLASS if platform.system() == 'Windows' else 5)
    import sherpa_onnx
    with wave.open(str(audio)) as wav:
        if (wav.getnchannels(), wav.getframerate(), wav.getsampwidth()) != (1, 16000, 2):
            raise ValueError('Expected mono 16 kHz PCM16')
        duration = wav.getnframes()/16000
        if not 0.5 <= duration <= 90: raise ValueError('Audio outside preview duration bound')
        samples = [sample[0]/32768 for sample in struct.iter_unpack('<h', wav.readframes(wav.getnframes()))]
    start = time.perf_counter()
    recognizer = sherpa_onnx.OfflineRecognizer.from_transducer(
        encoder=str(model/FILES[0]), decoder=str(model/FILES[1]), joiner=str(model/FILES[2]), tokens=str(model/FILES[3]),
        num_threads=2, provider='cpu', model_type='nemo_transducer', decoding_method='greedy_search', debug=False)
    loaded = time.perf_counter()
    stream = recognizer.create_stream()
    stream.accept_waveform(16000, samples)
    recognizer.decode_stream(stream)
    decoded = time.perf_counter()
    memory = process.memory_info()
    if platform.system() == 'Windows': peak = memory.peak_wset
    else:
        import resource
        peak = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024
    return dict(text=stream.result.text, audio_seconds=duration, model_load_ms=1000*(loaded-start),
                inference_ms=1000*(decoded-loaded), os_peak_worker_rss_bytes=peak,
                process_cpu_seconds=sum(process.cpu_times()[:2]))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True, type=pathlib.Path)
    parser.add_argument('--corpus', type=pathlib.Path)
    parser.add_argument('--output', type=pathlib.Path)
    parser.add_argument('--worker-audio', type=pathlib.Path)
    parser.add_argument('--repeats', type=int, default=2, choices=[1,2,3])
    args = parser.parse_args()
    if args.worker_audio:
        print(json.dumps(worker(args.model, args.worker_audio), ensure_ascii=False)); return
    if not args.corpus or not args.output: parser.error('Need --corpus and --output')
    corpus = json.loads(args.corpus.read_text(encoding='utf-8'))
    rows = []
    for sample in corpus['samples']:
        audio = (args.corpus.parent/sample['audio']).resolve(strict=True)
        digest = sha256(audio)
        if digest != sample['audio_sha256']: raise ValueError('Corpus hash mismatch')
        for repeat in range(args.repeats):
            start = time.perf_counter()
            result = subprocess.run([sys.executable, __file__, '--model', str(args.model), '--worker-audio', str(audio)], capture_output=True, timeout=180)
            wall_ms = 1000*(time.perf_counter()-start)
            if result.returncode: raise RuntimeError('Parakeet worker failed; no quality result accepted')
            row = json.loads(result.stdout)
            row.update(id=sample['id'], group=sample['language'], mode='auto', repeat=repeat, audio_sha256=digest,
                worker_wall_ms=wall_ms, rtf=wall_ms/1000/row['audio_seconds'], score=align(sample['reference'],row['text']))
            rows.append(row)
            print(sample['id'], repeat, 'completed', flush=True)
    groups = {}
    for language in sorted({r['group'] for r in rows}):
        group = [r for r in rows if r['group']==language]
        words = sum(r['score']['words'] for r in group); chars = sum(r['score']['characters'] for r in group)
        groups[language] = dict(words=words, characters=chars,
            wer=sum(sum(r['score'][key] for key in ('substitutions','deletions','insertions')) for r in group)/words if words else None,
            cer=sum(r['score']['character_errors'] for r in group)/chars if chars else None,
            median_inference_ms=statistics.median(r['inference_ms'] for r in group),
            median_load_ms=statistics.median(r['model_load_ms'] for r in group),
            median_worker_wall_ms=statistics.median(r['worker_wall_ms'] for r in group),
            peak_worker_rss_bytes=max(r['os_peak_worker_rss_bytes'] for r in group),
            mean_cpu_percent_one_core=statistics.mean(100000*r['process_cpu_seconds']/r['worker_wall_ms'] for r in group))
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(dict(engine='sherpa-onnx',version='1.12.40',model='nemo-parakeet-tdt-0.6b-v3-int8',
        model_hashes={name:sha256(args.model/name) for name in FILES}, platform=platform.platform(), cpu=platform.processor(),
        logical_cpus=psutil.cpu_count(), physical_ram_bytes=psutil.virtual_memory().total, threads=2,cpu_only=True,
        samples=rows,groups=groups,minimum_hardware_accepted=False,
        limitations=['Short public read-speech probe; no representative quality acceptance.',
            'Automatic language mode only. Explicit language forcing and mixed speech not established.',
            'Each worker reloads its model; repeats only warm filesystem caches.',
            'Wall time includes Python/import overhead; inference includes feature preparation and decode.',
            'No actual Windows 11 8 GB hardware, idle app, microphone or insertion acceptance.']),ensure_ascii=False,indent=2),encoding='utf-8')

if __name__ == '__main__': main()
