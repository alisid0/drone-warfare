"""Derive a separate forest adapter from the repository's licensed HDRP Lit source; never edit ground files."""
from pathlib import Path
import re
import uuid
root = Path(__file__).resolve().parents[2]
shaders = root / 'Assets/DroneSandbox/Resources/Shaders'
shader = (shaders / 'FarDetailLit.shader').read_text()
shader = shader.replace('DroneSandbox/GroundDetail', 'DroneSandbox/ForestLit')
shader = '\n'.join(line for line in shader.split('\n') if '_FarGround' not in line)
shader = shader.replace('FarDetailLitData.hlsl', 'ForestLitData.hlsl')
shader = shader.replace('    Properties\n    {', '''    Properties
    {
        _ForestTier("Forest tier", Float) = 0
        _ForestBuild("Forest refinement", Float) = 1
        _ForestCamera("Forest camera", Vector) = (0,0,0,0)
        _ForestDistances("Forest distances", Vector) = (45,125,350,700)''')
data = (shaders / 'FarDetailLitData.hlsl').read_text()
data = data.replace('#include "FarDetailSurface.hlsl"', '#include "ForestFade.hlsl"')
data = data.replace('    FarDetailSurface(input, surfaceData, normalTS);', '')
marker = 'RAY_TRACING_OPTIONAL_PARAMETERS)\n{'
assert marker in data
data = data.replace(marker, marker + '\n    ForestFade(input.positionRWS);')
for name, text in [('ForestLit.shader', shader), ('ForestLitData.hlsl', data)]:
    text = text.replace('by FarDetailShaderSetup.', 'for forest tiers by Tools/DenseForest/make_shader.py.')
    text = '\n'.join(line.rstrip() for line in text.splitlines()) + '\n'
    (shaders / name).write_bytes(text.replace('\n', '\r\n').encode())


def metadata(path):
    """Stable references let serialized materials retain instancing/cutout variants in a player build."""
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'drone-warfare/dense-forest/' + path.relative_to(root).as_posix()).hex
        meta.write_bytes(('fileFormatVersion: 2\r\nguid: ' + guid + '\r\n').encode())
    return re.search(r'guid: (\w+)', meta.read_text())[1]


guid = metadata(shaders / 'ForestLit.shader')
materials = root / 'Assets/DroneSandbox/Resources/Materials/Forest'
materials.mkdir(exist_ok=True)
for name in ['BarkOak', 'LeavesOak', 'BarkBirch', 'LeavesBirch', 'BarkPine', 'LeavesSpruce', 'LeavesPine',
             'ImpostorOak', 'ImpostorBirch', 'ImpostorSpruce', 'ImpostorPine', 'Canopy']:
    source = root / ('Assets/DroneSandbox/Resources/Materials/BaseLit.mat' if name == 'Canopy' else 'Assets/DroneSandbox/Resources/Materials/PBR/' + name + '.mat')
    text = source.read_text()
    text = re.sub(r'm_Shader: \{[^}]+\}', 'm_Shader: {fileID: 4800000, guid: ' + guid + ', type: 3}', text)
    text = re.sub(r'm_EnableInstancingVariants: \d', 'm_EnableInstancingVariants: 1', text)
    text = re.sub(r'  m_Name: [^\n]+', '  m_Name: Forest ' + name, text)
    target = materials / (name + '.mat')
    text = '\n'.join(line.rstrip() for line in text.splitlines()) + '\n'
    target.write_bytes(text.replace('\n', '\r\n').encode()); metadata(target)
