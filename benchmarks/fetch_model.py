"""Developer-only benchmark model fetch. Never used by the app or package.

Manifest models are verified against pinned SHA-256/size. Benchmark-only extras
(not in the manifest) are verified against the Hugging Face LFS SHA-256 header and
their resolved repository commit is recorded; they are not release candidates.
"""
import argparse
import hashlib
import json
import pathlib
import urllib.request

EXTRAS = {'ggml-large-v3-turbo-q5_0.bin'}

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs): return None

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True)
    parser.add_argument('--output', required=True, type=pathlib.Path)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parent.parent
    manifest = json.loads((root / 'models/manifest.json').read_text(encoding='utf-8'))
    entry = next((m for m in manifest['models'] if m['file'] == args.model), None)
    if entry is None and args.model not in EXTRAS: parser.error('Unknown model')
    revision = manifest['revision'] if entry else 'main'
    url = f"https://huggingface.co/{manifest['repository']}/resolve/{revision}/{args.model}"
    expected_sha, expected_bytes, commit = (entry or {}).get('sha256'), (entry or {}).get('bytes'), revision
    if entry is None:
        opener = urllib.request.build_opener(NoRedirect)
        try: response = opener.open(urllib.request.Request(url, method='HEAD'), timeout=60)
        except urllib.error.HTTPError as redirect: response = redirect
        expected_sha = (response.headers.get('X-Linked-Etag') or '').strip('"')
        expected_bytes = int(response.headers.get('X-Linked-Size') or 0) or None
        commit = response.headers.get('X-Repo-Commit') or 'unknown'
        if len(expected_sha) != 64: raise RuntimeError('No LFS SHA-256 published for benchmark-only model')
    args.output.mkdir(parents=True, exist_ok=True)
    target = args.output / args.model
    digest, size = hashlib.sha256(), 0
    with urllib.request.urlopen(url, timeout=600) as response, open(str(target) + '.part', 'wb') as out:
        for block in iter(lambda: response.read(1 << 20), b''):
            digest.update(block); size += len(block); out.write(block)
    if digest.hexdigest() != expected_sha or (expected_bytes and size != expected_bytes):
        raise RuntimeError('Model integrity mismatch')
    pathlib.Path(str(target) + '.part').replace(target)
    print(json.dumps(dict(model=args.model, sha256=expected_sha, bytes=size, repository_commit=commit,
                          manifest_pinned=entry is not None)))

if __name__ == '__main__': main()
