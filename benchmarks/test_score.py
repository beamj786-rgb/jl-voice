import unittest
from score import align

class ScoreTests(unittest.TestCase):
    def test_diacritics_count(self):
        self.assertEqual(align('příští čtvrtek', 'pristi čtvrtek')['wer'], 0.5)
    def test_canonical_unicode(self):
        self.assertEqual(align('příští', 'pr\u030ci\u0301s\u030cti\u0301')['wer'], 0)
    def test_edits(self):
        score = align('one two three', 'one four three five')
        self.assertEqual((score['substitutions'], score['insertions'], score['deletions']), (1, 1, 0))
    def test_empty_reference(self):
        self.assertIsNone(align('', 'phantom')['wer'])
        self.assertEqual(align('', 'phantom')['hallucinated_words'], 1)
    def test_case_punctuation(self):
        self.assertEqual(align('Hello, world!', 'hello world')['wer'], 0)
    def test_character_errors(self):
        self.assertEqual(align('kůň', 'kun')['cer'], 2/3)
        self.assertEqual(align('Hello, world!', 'hello world')['cer'], 0)
        self.assertIsNone(align('', 'phantom')['cer'])

if __name__ == '__main__':
    unittest.main()
