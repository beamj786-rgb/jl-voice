import json
import unittest
from parakeet import encode_result

class ParakeetProtocolTests(unittest.TestCase):
    def test_unicode_survives_legacy_windows_pipe(self):
        text = 'Příští čtvrtek: příliš žluťoučký kůň. Hello 😀'
        wire = encode_result({'text':text}).encode('cp1252')
        self.assertTrue(wire.isascii())
        self.assertEqual(json.loads(wire)['text'], text)

if __name__ == '__main__': unittest.main()
