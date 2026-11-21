"""Unicode-aware word alignment. Diacritics remain significant."""
import re
import unicodedata

def tokens(text):
    return re.findall(r"[^\W_]+(?:['’][^\W_]+)*", unicodedata.normalize("NFC", text).casefold(), re.UNICODE)

def align(reference, hypothesis):
    ref, hyp = tokens(reference), tokens(hypothesis)
    # Tuples retain actual edit counts, with deterministic tie breaking.
    previous = [(j, 0, 0, j) for j in range(len(hyp) + 1)]
    for i, expected in enumerate(ref, 1):
        current = [(i, 0, i, 0)]
        for j, actual in enumerate(hyp, 1):
            if expected == actual:
                current.append(previous[j - 1])
            else:
                cost, s, d, ins = previous[j - 1]
                substitution = (cost + 1, s + 1, d, ins)
                cost, s, d, ins = previous[j]
                deletion = (cost + 1, s, d + 1, ins)
                cost, s, d, ins = current[j - 1]
                insertion = (cost + 1, s, d, ins + 1)
                current.append(min((substitution, deletion, insertion), key=lambda x: x[0]))
        previous = current
    errors, s, d, ins = previous[-1]
    return dict(words=len(ref), substitutions=s, deletions=d, insertions=ins,
                wer=errors / len(ref) if ref else None, hallucinated_words=len(hyp) if not ref else 0)
