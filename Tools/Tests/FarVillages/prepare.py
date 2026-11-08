"""Compile the far-village sources as C# 9 against the installed Unity 6 modules, then build the far-village review harness.

Never launches Unity. Reuses the RegionalRealism scaffold (and through it UsedLook, PlacesUpClose, Water and
StreamingPerf) with this folder's Review.cs; --before builds the same harness from the f7588a1 sources.
"""
from pathlib import Path

HERE = Path(__file__).resolve().parent
base = HERE.parent / 'RegionalRealism/prepare.py'
text = base.read_text()
text = text.replace("'Temp/regional-realism/build-before' if BEFORE else 'Temp/regional-realism/build'",
                    "'Temp/far-villages/build-before' if BEFORE else 'Temp/far-villages/build'")
text = text.replace("setup.replace(\"'fe0df56:'\", \"'5fb5060:'\").replace(\"'fe0df56'\", \"'5fb5060'\")",
                    "setup.replace(\"'fe0df56:'\", \"'f7588a1:'\").replace(\"'fe0df56'\", \"'f7588a1'\")")
text = text.replace("assert '5fb5060' in setup and 'regional-realism' in setup", "assert 'f7588a1' in setup and 'far-villages' in setup")
assert 'Temp/far-villages/build' in text and "'f7588a1:'" in text
scope = {'__file__': str(HERE / 'prepare.py'), '__name__': '__main__'}
exec(compile(text, str(base), 'exec'), scope)
