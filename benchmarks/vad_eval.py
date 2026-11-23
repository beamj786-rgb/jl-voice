"""Developer-only no-speech protection benchmark. Never used by the app.

Builds deterministic conditions from the public FLEURS corpus (fetch_public.py) and,
when downloadable, the DEMAND ambient-noise corpus (Zenodo record 1227121, CC BY-SA 3.0):
  no-speech : digital silence, white/pink noise at several levels, real ambient noise
  speech    : original clips, attenuated -20/-30 dB, mixed with noise at SNR 10/5/0 dB
  babble    : cafeteria noise (contains background voices; reported separately)
Runs the real worker in --vad-only mode at several thresholds and writes a corpus.json
of the generated conditions so the full ASR + guard can be evaluated on the same files.
Missing noise sources are reported as unavailable, never synthesized as substitutes.
"""
import argparse
import hashlib
import io
import json
import math
import pathlib
import random
import struct
import subprocess
import urllib.request
import wave
import zipfile
import zlib

DEMAND = {  # environment: (zip name, category)
    'office': ('OOFFICE_16k.zip', 'no_speech'),
    'living': ('DLIVING_16k.zip', 'no_speech'),
    'kitchen': ('DKITCHEN_16k.zip', 'no_speech'),
    'cafeteria': ('PCAFETER_16k.zip', 'babble'),
}

def read_pcm(path):
    with wave.open(str(path)) as f:
        if (f.getnchannels(), f.getsampwidth(), f.getframerate()) != (1, 2, 16000): raise ValueError(f'{path}: unexpected format')
        data = f.readframes(f.getnframes())
    return list(struct.unpack(f'<{len(data)//2}h', data))

def write_wav(path, samples):
    clipped = [max(-32768, min(32767, int(round(v)))) for v in samples]
    out = io.BytesIO()
    with wave.open(out, 'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(16000); f.writeframes(struct.pack(f'<{len(clipped)}h', *clipped))
    path.write_bytes(out.getvalue())
    return hashlib.sha256(out.getvalue()).hexdigest()

def rms(samples):
    return math.sqrt(sum(v * v for v in samples) / max(1, len(samples)))

def white(n, dbfs, seed):
    g = random.Random(seed); a = 32767 * 10 ** (dbfs / 20)
    return [g.gauss(0, a) for _ in range(n)]

def pink(n, dbfs, seed):
    # Voss-McCartney approximation, scaled to the requested RMS.
    g = random.Random(seed); rows = [0.0] * 16; total = 0.0; out = []
    for i in range(n):
        k = (i & -i).bit_length() - 1 if i else 0
        k = min(k, 15); total -= rows[k]; rows[k] = g.uniform(-1, 1); total += rows[k]
        out.append(total + g.uniform(-1, 1))
    scale = 32767 * 10 ** (dbfs / 20) / max(1e-9, rms(out))
    return [v * scale for v in out]

def demand_segments(env, zip_name, cache):
    url = f'https://zenodo.org/records/1227121/files/{zip_name}?download=1'
    target = cache / zip_name
    if not target.exists():
        with urllib.request.urlopen(url, timeout=600) as response, open(target, 'wb') as out:
            for block in iter(lambda: response.read(1 << 20), b''): out.write(block)
    with zipfile.ZipFile(target) as z:
        member = next(n for n in z.namelist() if n.lower().endswith('ch01.wav'))
        with z.open(member) as f, wave.open(io.BytesIO(f.read())) as w:
            if (w.getnchannels(), w.getsampwidth(), w.getframerate()) != (1, 2, 16000): raise ValueError('DEMAND format')
            data = w.readframes(w.getnframes())
    samples = list(struct.unpack(f'<{len(data)//2}h', data))
    return samples, hashlib.sha256(target.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', required=True, type=pathlib.Path)
    parser.add_argument('--vad-model', required=True, type=pathlib.Path)
    parser.add_argument('--corpus', required=True, type=pathlib.Path)
    parser.add_argument('--output', required=True, type=pathlib.Path)
    parser.add_argument('--cache', required=True, type=pathlib.Path)
    parser.add_argument('--thresholds', default='0.3,0.4,0.5,0.6')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True); args.cache.mkdir(parents=True, exist_ok=True)
    corpus = json.loads(args.corpus.read_text(encoding='utf-8'))
    speech = [s for s in corpus['samples'] if s.get('group') is None and s['language'] in ('cs', 'en')]
    rows, sources = [], {}

    def add(name, category, samples, reference='', language='cs', origin=''):
        digest = write_wav(args.output / f'{name}.wav', samples)
        rows.append(dict(id=name, language=language, group=category, audio=f'{name}.wav', reference=reference,
                         audio_sha256=digest, source_file=origin, seconds=round(len(samples) / 16000, 2),
                         rms_dbfs=round(20 * math.log10(max(1e-9, rms(samples) / 32768)), 1)))

    n3, n10 = 16000 * 3, 16000 * 10
    add('silence-3s', 'no_speech', [0] * n3, origin='synthetic')
    for db in (-60, -50, -40):
        add(f'white{db}-3s', 'no_speech', white(n3, db, 1000 - db), origin='synthetic')
    add('white-50-10s', 'no_speech', white(n10, -50, 7), origin='synthetic')
    add('pink-45-3s', 'no_speech', pink(n3, -45, 11), origin='synthetic')
    add('pink-35-10s', 'no_speech', pink(n10, -35, 12), origin='synthetic')
    # Clicks: short broadband impulses roughly like key presses, on a quiet floor.
    g = random.Random(5); clicks = white(n3, -65, 13)
    for t in range(2000, n3 - 400, 2600):
        for k in range(160): clicks[t + k] += g.gauss(0, 8000) * math.exp(-k / 30)
    add('clicks-3s', 'no_speech', clicks, origin='synthetic impulses')

    ambient = {}
    for env, (zip_name, category) in DEMAND.items():
        try:
            samples, digest = demand_segments(env, zip_name, args.cache)
            ambient[env] = samples; sources[env] = dict(zip=zip_name, sha256=digest, status='used', license='CC BY-SA 3.0',
                                                        source='DEMAND, Zenodo record 1227121, channel 1')
            for offset, length in ((30, n3), (120, n10)):
                part = samples[offset * 16000: offset * 16000 + length]
                add(f'{env}-{length // 16000}s', category, part, origin=f'DEMAND {zip_name} ch01 @{offset}s')
            part = samples[60 * 16000: 60 * 16000 + n3]
            gain = 32767 * 10 ** (-30 / 20) / max(1e-9, rms(part))
            add(f'{env}-loud-3s', category, [v * gain for v in part], origin=f'DEMAND {zip_name} ch01 @60s scaled to -30 dBFS')
        except Exception as error:  # noqa: BLE001 - reported, never substituted
            sources[env] = dict(zip=zip_name, status=f'unavailable: {type(error).__name__}')

    for clip in speech:
        pcm = read_pcm(args.corpus.parent / clip['audio'])
        for db in (0, -20, -30):
            add(f"{clip['id']}{'' if db == 0 else f'-att{-db}'}", 'speech', [v * 10 ** (db / 20) for v in pcm],
                reference=clip['reference'], language=clip['language'], origin=clip['audio'])
        noise_sets = [('white', white(len(pcm), -40, zlib.crc32(clip['id'].encode())))]
        if 'office' in ambient: noise_sets.append(('office', ambient['office'][200 * 16000: 200 * 16000 + len(pcm)]))
        if 'cafeteria' in ambient: noise_sets.append(('cafeteria', ambient['cafeteria'][200 * 16000: 200 * 16000 + len(pcm)]))
        for noise_name, noise in noise_sets:
            for snr in (10, 5, 0):
                scale = rms(pcm) / max(1e-9, rms(noise)) / 10 ** (snr / 20)
                add(f"{clip['id']}-{noise_name}-snr{snr}", 'speech_in_noise', [a + b * scale for a, b in zip(pcm, noise)],
                    reference=clip['reference'], language=clip['language'], origin=f"{clip['audio']} + {noise_name}")

    results = []
    variants = [(float(x), False) for x in args.thresholds.split(',')] + [(0.5, True)]
    for threshold, vad_normalize in variants:
        for row in rows:
            r = subprocess.run([str(args.runtime), '--cpu-only', '--vad-only', '--vad-model', str(args.vad_model),
                                '--vad-threshold', str(threshold), '--threads', '2', *(['--vad-normalize'] if vad_normalize else [])],
                               input=(args.output / row['audio']).read_bytes(), capture_output=True, timeout=120)
            if r.returncode: raise RuntimeError(f"VAD worker failed on {row['id']}")
            m = [json.loads(l[16:]) for l in r.stderr.decode('utf-8').splitlines() if l.startswith('JLVOICE_METRICS ')][0]
            results.append(dict(threshold=threshold, vad_normalize=vad_normalize, id=row['id'], group=row['group'], seconds=row['seconds'], rms_dbfs=row['rms_dbfs'],
                                speech=m['verdict'] == 'speech', vad_speech_seconds=m['vad_speech_seconds'], vad_segments=m['vad_segments'],
                                vad_first_s=m['vad_first_s'], vad_last_s=m['vad_last_s'], vad_ms=m['vad_ms']))
    summary = {}
    for res in results:
        key = f"{res['threshold']}{'+vadnorm' if res['vad_normalize'] else ''}/{res['group']}"
        s = summary.setdefault(key, dict(n=0, detected_speech=0))
        s['n'] += 1; s['detected_speech'] += res['speech']
    # Subset for the (slower) full ASR + guard run: every no-speech/babble file, and for 3 cs + 3 en clips
    # the original, -30 dB, and SNR 5 dB office (or white) / cafeteria mixtures.
    chosen = [c['id'] for c in speech if c['language'] == 'cs'][:3] + [c['id'] for c in speech if c['language'] == 'en'][:3]
    noise_name = 'office' if 'office' in ambient else 'white'
    wanted = {f'{c}{suffix}' for c in chosen for suffix in ('', '-att30', f'-{noise_name}-snr5', '-cafeteria-snr5')}
    asr_rows = [r for r in rows if r['group'] in ('no_speech', 'babble') or r['id'] in wanted]
    (args.output / 'asr-corpus.json').write_text(json.dumps(dict(dataset='vad-conditions subset', revision=corpus['revision'],
        split=corpus['split'], selection='no-speech/babble + 6 clips x (original, -30 dB, SNR 5 dB noise)', samples=asr_rows),
        ensure_ascii=False, indent=2), encoding='utf-8')
    (args.output / 'corpus.json').write_text(json.dumps(dict(dataset='vad-conditions (FLEURS + synthetic + DEMAND)', revision=corpus['revision'],
        split=corpus['split'], selection='generated by vad_eval.py', samples=rows), ensure_ascii=False, indent=2), encoding='utf-8')
    (args.output / 'vad-results.json').write_text(json.dumps(dict(sources=sources, summary=summary, results=results), indent=2), encoding='utf-8')
    print('VAD_SOURCES ' + json.dumps(sources))
    for key, s in sorted(summary.items()): print('VAD_SUMMARY ' + json.dumps(dict(key=key, **s)))
    for res in results: print('VAD_RESULT ' + json.dumps(res))

if __name__ == '__main__': main()
