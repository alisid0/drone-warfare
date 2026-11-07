"""Compile the town sources as C# 9 against the installed Unity 6 modules, then build the town review harness.

Never launches Unity. Reuses the RegionalRealism scaffold (and through it UsedLook, PlacesUpClose, Water and
StreamingPerf) with this folder's Review.cs; --before builds the same harness from the 0d54a9d sources.
"""
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
base = HERE.parent / 'RegionalRealism/prepare.py'
text = base.read_text()
text = text.replace("'Temp/regional-realism/build-before' if BEFORE else 'Temp/regional-realism/build'",
                    "'Temp/towns/build-before' if BEFORE else 'Temp/towns/build'")
text = text.replace("setup.replace(\"'fe0df56:'\", \"'5fb5060:'\").replace(\"'fe0df56'\", \"'5fb5060'\")",
                    "setup.replace(\"'fe0df56:'\", \"'0d54a9d:'\").replace(\"'fe0df56'\", \"'0d54a9d'\")")
text = text.replace("assert '5fb5060' in setup and 'regional-realism' in setup", "assert '0d54a9d' in setup and 'towns' in setup")
assert 'Temp/towns/build' in text and "'0d54a9d:'" in text
scope = {'__file__': str(HERE / 'prepare.py'), '__name__': '__main__'}
exec(compile(text, str(base), 'exec'), scope)
