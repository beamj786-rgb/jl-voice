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
    args = parser.parse_args()
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
            if len(selected) == 3: break
        wanted = {r[1]:r for r in selected}
        with urllib.request.urlopen(base+'/audio/dev.tar.gz', timeout=120) as response, tarfile.open(fileobj=response, mode='r|gz') as archive:
            for member in archive:
                name = pathlib.PurePosixPath(member.name).name
                if name not in wanted: continue
                if not member.isfile() or member.size > 16*1024*1024: raise ValueError('Invalid fixture member')
                row = wanted.pop(name); source = archive.extractfile(member).read(); data = pcm16(source)
                path = args.output/(language+'-'+name); path.write_bytes(data)
                rows.append(dict(id=language+'-'+row[0], language=language, audio=path.name, reference=row[2],
                    audio_sha256=hashlib.sha256(data).hexdigest(), source_sha256=hashlib.sha256(source).hexdigest(), source_file=name))
                print('Fetched public fixture', language, name, flush=True)
                if not wanted: break
        if wanted: raise ValueError('Selected source recordings missing')
    (args.output/'corpus.json').write_text(json.dumps(dict(dataset='google/fleurs', revision=REVISION, split='dev', license='CC-BY-4.0',
        attribution='FLEURS: Few-shot Learning Evaluation of Universal Representations of Speech, Google and contributors; https://huggingface.co/datasets/google/fleurs',
        conversion='16 kHz mono IEEE float32 -> PCM16; round(value*32768), clamp to signed int16; no resampling',
        selection='3 shortest unique prompt IDs per language with >=3 seconds; narrow read-speech probe', samples=rows), ensure_ascii=False, indent=2), encoding='utf-8')

if __name__ == '__main__': main()
