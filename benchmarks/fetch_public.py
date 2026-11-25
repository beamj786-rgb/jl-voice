"""Fetch a small attributed, pinned FLEURS read-speech probe. Developer-only."""
import argparse
import csv
import hashlib
import io
import json
import math
import pathlib
import struct
import tarfile
import urllib.request
import wave

REVISION = '70bb2e84b976b7e960aa89f1c648e09c59f894dd'

def pcm16(source):
    if source[:4] != b'RIFF' or source[8:12] != b'WAVE': raise ValueError('Expected FLEURS WAV')
    at, fmt, samples = 12, None, None
    while at + 8 <= len(source):
        kind, size = source[at:at+4], struct.unpack_from('<I', source, at+4)[0]
        at += 8
        if at + size > len(source): raise ValueError('Truncated fixture')
        if kind == b'fmt ': fmt = struct.unpack_from('<HHIIHH', source, at)
        if kind == b'data': samples = source[at:at+size]
        at += size + (size & 1)
    if fmt != (3, 1, 16000, 64000, 4, 32) or samples is None or len(samples) % 4:
        raise ValueError('Expected mono 16 kHz float32 source; no implicit resampling')
    pcm = bytearray()
    for (value,) in struct.iter_unpack('<f', samples):
        if not math.isfinite(value): raise ValueError('Non-finite fixture sample')
        pcm.extend(struct.pack('<h', max(-32768, min(32767, round(value * 32768)))))
    output = io.BytesIO()
    with wave.open(output, 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000); wav.writeframes(pcm)
    return output.getvalue()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True, type=pathlib.Path)
    parser.add_argument('--count', type=int, default=3, help='clips per language (shortest unique prompts >= 3 s)')
    parser.add_argument('--controls', action='store_true', help='add synthetic silence/noise clips with empty references')
    parser.add_argument('--durations', default='', help='comma-separated target seconds (for example 5,15,30,45,60): concatenations of the selected clips; needs --count >= 16 for 60 s')
    parser.add_argument('--long', action='store_true', help='add ~20 s and up to 85 s concatenations of the selected clips per language')
    args = parser.parse_args()
    if not 1 <= args.count <= 40: parser.error('--count must be 1..40')
    targets = [float(x) for x in args.durations.split(',') if x.strip()]
    if any(not 3 <= t <= 85 for t in targets): parser.error('--durations values must be 3..85 seconds')
    args.output.mkdir(parents=True, exist_ok=True)
    rows = []
    for language, config in [('cs','cs_cz'), ('en','en_us')]:
        base = f'https://huggingface.co/datasets/google/fleurs/resolve/{REVISION}/data/{config}'
        with urllib.request.urlopen(base+'/dev.tsv', timeout=60) as response:
            tsv = response.read().decode('utf-8')
        selected, ids = [], set()
        for row in sorted(csv.reader(tsv.splitlines(), delimiter='\t'), key=lambda r:int(r[5])):
            if row[0] in ids or int(row[5]) < 48000: continue
            ids.add(row[0]); selected.append(row)
            if len(selected) == args.count: break
        wanted = {r[1]:r for r in selected}
        with urllib.request.urlopen(base+'/audio/dev.tar.gz', timeout=120) as response, tarfile.open(fileobj=response, mode='r|gz') as archive:
            for member in archive:
                name = pathlib.PurePosixPath(member.name).name
                if name not in wanted: continue
                if not member.isfile() or member.size > 16*1024*1024: raise ValueError('Invalid fixture member')
                row = wanted.pop(name); source = archive.extractfile(member).read(); data = pcm16(source)
                path = args.output/(language+'-'+name); path.write_bytes(data)
                rows.append(dict(id=language+'-'+row[0], language=language, audio=path.name, reference=row[2],
                    audio_sha256=hashlib.sha256(data).hexdigest(), source_sha256=hashlib.sha256(source).hexdigest(), source_file=name,
                    tier='short' if row in selected[:3] else 'extra'))
                print('Fetched public fixture', language, name, flush=True)
                if not wanted: break
        if wanted: raise ValueError('Selected source recordings missing')
        if targets:
            # Duration set for cloud/latency experiments: the selected clips in selection order, joined with 0.4 s digital
            # silence until the target is reached (within about +/- 4 s, actual length recorded). Reference = joined references.
            order = {f'{language}-{r[0]}': i for i, r in enumerate(selected)}
            clips = sorted((r for r in rows if r['language'] == language and r.get('tier') in ('short', 'extra')), key=lambda r: order[r['id']])
            gap = bytes(int(16000 * 0.4) * 2)
            for target in targets:
                pcm, refs, seconds = bytearray(), [], 0.0
                for clip in clips:
                    with wave.open(str(args.output / clip['audio'])) as f: frames = f.readframes(f.getnframes())
                    length = len(frames) / 32000
                    extra = 0.4 if refs else 0.0
                    if refs and seconds + extra + length > target + 4.0: break
                    if refs: pcm.extend(gap)
                    pcm.extend(frames); seconds += extra + length; refs.append(clip['reference'])
                    if seconds >= target - 1.0: break
                if seconds < target - 5.0: raise ValueError(f'Not enough clips for a {target:g} s sample; raise --count')
                output = io.BytesIO()
                with wave.open(output, 'wb') as wav:
                    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000); wav.writeframes(pcm)
                data = output.getvalue(); audio = f'{language}-d{target:g}.wav'; (args.output / audio).write_bytes(data)
                rows.append(dict(id=f'{language}-d{target:g}', language=language, group=f'{language}-dur', tier='duration', audio=audio,
                    reference=' '.join(refs), seconds=round(seconds, 2), parts=len(refs), target_seconds=target,
                    audio_sha256=hashlib.sha256(data).hexdigest(), source_file='concatenation of selected clips'))
        if args.long:
            # Same speech as the short clips, joined in selection order with 0.4 s digital silence,
            # to check omissions/repetitions on longer dictation. Reference = joined references.
            clips = [r for r in rows if r['language'] == language and 'group' not in r]
            gap = bytes(int(16000 * 0.4) * 2)
            for name, limit in (('long20', 20.0), ('longmax', 85.0)):
                pcm, refs, seconds = bytearray(), [], 0.0
                for clip in clips:
                    with wave.open(str(args.output / clip['audio'])) as f: frames = f.readframes(f.getnframes())
                    length = len(frames) / 32000
                    if refs and seconds + 0.4 + length > limit: break
                    if refs: pcm.extend(gap); seconds += 0.4
                    pcm.extend(frames); seconds += length; refs.append(clip['reference'])
                    if name == 'long20' and seconds >= 18.0: break
                output = io.BytesIO()
                with wave.open(output, 'wb') as wav:
                    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000); wav.writeframes(pcm)
                data = output.getvalue(); audio = f'{language}-{name}.wav'; (args.output / audio).write_bytes(data)
                rows.append(dict(id=f'{language}-{name}', language=language, group=f'{language}-long', audio=audio,
                    reference=' '.join(refs), seconds=round(seconds, 2), parts=len(refs),
                    audio_sha256=hashlib.sha256(data).hexdigest(), source_file='concatenation of selected clips'))
    if args.controls:
        # Deterministic synthetic controls: any words recognized here are hallucinations.
        import random
        generator = random.Random(20261122)
        for name, amplitude in (('control-silence.wav', 0.0), ('control-noise-60dbfs.wav', 0.001)):
            pcm = bytearray()
            for _ in range(16000 * 3):
                pcm.extend(struct.pack('<h', round(generator.gauss(0, amplitude) * 32767) if amplitude else 0))
            output = io.BytesIO()
            with wave.open(output, 'wb') as wav:
                wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000); wav.writeframes(pcm)
            data = output.getvalue(); (args.output/name).write_bytes(data)
            rows.append(dict(id=name[:-4], language='cs', group='control', audio=name, reference='',
                audio_sha256=hashlib.sha256(data).hexdigest(), source_file='synthetic'))
    (args.output/'corpus.json').write_text(json.dumps(dict(dataset='google/fleurs', revision=REVISION, split='dev', license='CC-BY-4.0',
        attribution='FLEURS: Few-shot Learning Evaluation of Universal Representations of Speech, Google and contributors; https://huggingface.co/datasets/google/fleurs',
        conversion='16 kHz mono IEEE float32 -> PCM16; round(value*32768), clamp to signed int16; no resampling',
        selection=f'{args.count} shortest unique prompt IDs per language with >=3 seconds; read speech, not dictation' + ('; plus synthetic silence/noise controls' if args.controls else '') + ('; plus long concatenations' if args.long else ''), samples=rows), ensure_ascii=False, indent=2), encoding='utf-8')

if __name__ == '__main__': main()
