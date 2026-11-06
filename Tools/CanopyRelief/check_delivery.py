"""Check the review links, retained image sizes, file scope and line endings before handoff."""
from pathlib import Path
from html.parser import HTMLParser
import struct
import subprocess

ROOT=Path(__file__).resolve().parents[2]
review=ROOT/'Tools/CanopyRelief/Review'


class Images(HTMLParser):
    """Collect the actual gallery image references so a missing capture cannot pass by filename count alone."""
    names=[]

    def handle_starttag(self,tag,attrs):
        """Read image sources without needing a browser or modifying the captures."""
        if tag=='img':self.names.append(dict(attrs)['src'])


parser=Images();parser.feed((review/'index.html').read_text())
assert len(parser.names)==18
for name in parser.names:
    data=(review/name).read_bytes()
    assert data[:8]==b'\x89PNG\r\n\x1a\n'
    assert struct.unpack('>II',data[16:24])==(1600,900),name
git=['git','-c','safe.directory='+str(ROOT).replace('\\','/')]
assert not subprocess.check_output(git+['diff','0542d98','--','Assets/DroneSandbox/Scripts/Ground.cs'],cwd=ROOT)
changed=subprocess.check_output(git+['diff','0542d98','--name-only'],cwd=ROOT,text=True).splitlines()
for name in changed:
    assert name.startswith(('Assets/DroneSandbox/Scripts/Forest','Assets/DroneSandbox/Scripts/SectorForest',
                            'Assets/DroneSandbox/Resources/Shaders/Forest','Assets/DroneSandbox/Resources/Materials/Forest/',
                            'Assets/DroneSandbox/Resources/Textures/ForestDetailB','Tools/CanopyRelief/')),name
files={ROOT/name for name in changed}|set((ROOT/'Tools/CanopyRelief').rglob('*'))
for path in files:
    if not path.is_file() or path.suffix=='.png':continue
    assert b'\n' not in path.read_bytes().replace(b'\r\n',b''),str(path)+' must be CRLF'
subprocess.run(git+['diff','--check','0542d98'],cwd=ROOT,check=True)
subprocess.run(git+['apply','--check','Tools/CanopyRelief/ground-integration.patch'],cwd=ROOT,check=True)
print('18 matched 1600x900 captures; Ground unchanged; scope, CRLF and integration patch verified.')
