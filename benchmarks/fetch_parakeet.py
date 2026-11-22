"""Developer-only fetch of an integrity-pinned sherpa conversion; never downloads code."""
import argparse
import hashlib
import pathlib
import tarfile
import urllib.request

URL = 'https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2'
SHA256 = '1495e91b59d7fc53ddca91f3431758ec99f8fa253fdb0e1e851be0f72b14b0f9'
BYTES = 487211443

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--output', required=True, type=pathlib.Path)
    args = parser.parse_args(); args.output.mkdir(parents=True,exist_ok=True)
    archive = args.output/'parakeet-v3-int8.tar.bz2'
    if not archive.exists():
        part = archive.with_suffix('.part')
        urllib.request.urlretrieve(URL, part)
        with part.open('rb') as file: digest = hashlib.file_digest(file,'sha256').hexdigest()
        if part.stat().st_size != BYTES or digest != SHA256: raise ValueError('Downloaded model archive integrity mismatch')
        part.replace(archive)
    with archive.open('rb') as file: digest = hashlib.file_digest(file,'sha256').hexdigest()
    if archive.stat().st_size != BYTES or digest != SHA256: raise ValueError('Cached model archive integrity mismatch')
    with tarfile.open(archive) as bundle: bundle.extractall(args.output,filter='data')
    print('Verified and extracted pinned Parakeet v3 INT8 model')

if __name__ == '__main__': main()
