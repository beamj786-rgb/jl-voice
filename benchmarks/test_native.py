"""Run with JL_VOICE_WORKER pointing to the built native worker."""
import io
import os
import subprocess
import unittest
import wave

@unittest.skipUnless(os.environ.get('JL_VOICE_WORKER'), 'native worker not built')
class NativeInputTests(unittest.TestCase):
    def run_worker(self, audio, *options):
        return subprocess.run([os.environ['JL_VOICE_WORKER'], '--cpu-only', '--model', 'missing-model.bin', *options],
                              input=audio, capture_output=True, timeout=10)
    def test_rejects_non_wave(self):
        r = self.run_worker(b'not a WAV')
        self.assertNotEqual(r.returncode, 0)
        self.assertIn(b'Expected a PCM WAV', r.stderr)
    def test_requires_cpu_only(self):
        r = subprocess.run([os.environ['JL_VOICE_WORKER'], '--model', 'missing.bin'], capture_output=True, timeout=10)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn(b'--cpu-only', r.stderr)
    def test_bounded_threads(self):
        r = self.run_worker(b'', '--threads', '8')
        self.assertNotEqual(r.returncode, 0)
        self.assertIn(b'Threads must be 1 or 2', r.stderr)
    def test_pcm_format_boundary(self):
        output = io.BytesIO()
        with wave.open(output, 'wb') as f:
            f.setnchannels(2); f.setsampwidth(2); f.setframerate(44100); f.writeframes(bytes(44100 * 4))
        r = self.run_worker(output.getvalue())
        self.assertNotEqual(r.returncode, 0)
        self.assertIn(b'Expected mono 16 kHz', r.stderr)
    def test_declared_riff_length(self):
        r = self.run_worker(b'RIFF' + bytes(4) + b'WAVE')
        self.assertNotEqual(r.returncode, 0)
        self.assertIn(b'Invalid RIFF length', r.stderr)

if __name__ == '__main__': unittest.main()
