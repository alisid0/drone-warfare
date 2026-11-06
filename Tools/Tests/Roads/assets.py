"""Package the production C# material swatches and stable import metadata without running Unity."""
from pathlib import Path
import re
import uuid
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
resources = ROOT/'Assets/DroneSandbox/Resources'


def guid(path):
    """Stable asset IDs make repeat baking a clean, deterministic change."""
    return uuid.uuid5(uuid.NAMESPACE_URL,'drone-warfare/'+path.relative_to(ROOT).as_posix()).hex


def write(path, text):
    """Match the repository's CRLF convention in all generated text assets."""
    path.write_bytes(text.replace('\r\n','\n').replace('\n','\r\n').encode())


colors = {'RoadHighway':(1,1,1), 'RoadLane':(1,1,1), 'RoadTrack':(1,1,1), 'RoadJunction':(92/255,91/255,87/255),
          'RoadGravel':(132/255,128/255,113/255), 'RoadDitch':(64/255,70/255,49/255), 'RoadPuddle':(.23,.26,.25)}
for name,color in colors.items():
    path = resources/'Textures'/(name+'.png')
    if name in ('RoadHighway','RoadLane','RoadTrack'):Image.open(ROOT/'Temp/roads/review'/(name+'.ppm')).save(path)
    else:Image.new('RGB',(4,4),tuple(round(c*255) for c in color)).save(path)
    meta = (resources/'Textures/DetailNeutral.png.meta').read_text()
    meta = re.sub(r'guid: \w+', 'guid: '+guid(path), meta, count=1)
    meta = meta.replace('sRGBTexture: 0','sRGBTexture: 1').replace('filterMode: 1','filterMode: 2').replace('aniso: 1','aniso: 8').replace('nPOTScale: 1','nPOTScale: 0')
    meta = meta.replace('wrapU: 0','wrapU: 1').replace('textureCompression: 1','textureCompression: 0')
    write(Path(str(path)+'.meta'),meta)

for name,color in colors.items():
    path = resources/'Materials'/(name+'.mat')
    text = (resources/'Materials/BaseLit.mat').read_text().replace('m_Name: BaseLit','m_Name: '+name)
    tex = resources/'Textures'/(name+'.png')
    text = text.replace('- _BaseColorMap:\n        m_Texture: {fileID: 0}', '- _BaseColorMap:\n        m_Texture: {fileID: 2800000, guid: '+guid(tex)+', type: 3}')
    for prop in ('_BaseColor','_Color'):
        text = re.sub(r'(- '+prop+r': )\{[^}]+\}',r'\g<1>{r: 1, g: 1, b: 1, a: 1}',text)
    text = re.sub(r'(- _Smoothness: )[\d.]+',r'\g<1>'+('.88' if name=='RoadPuddle' else '.18'),text)
    write(path,text)
    write(Path(str(path)+'.meta'),'fileFormatVersion: 2\nguid: '+guid(path)+'\n')

for path in list((ROOT/'Assets/DroneSandbox/Scripts').glob('Road*.cs')):
    write(Path(str(path)+'.meta'),'fileFormatVersion: 2\nguid: '+guid(path)+'\n')
print('Packaged three road atlases, four tiny colour swatches, seven shared HDRP materials and stable source GUIDs.')
