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
shader = shader.replace('Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitProperties.hlsl',
                        'Assets/DroneSandbox/Resources/Shaders/ForestLitProperties.hlsl')
shader = shader.replace('    HLSLINCLUDE', '''    HLSLINCLUDE
    // ForestFade needs position in opaque depth/shadow passes too, before LitDepthPass packs the varyings.
    #define VARYINGS_NEED_POSITION_WS''')
shader = shader.replace('    Properties\n    {', '''    Properties
    {
        _ForestEnabled("Forest fade enabled", Float) = 0
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

# Keep one constant-buffer layout in all raster passes, including DOTS material defaults.
package = root.parent / 'DroneWarfare/Library/PackageCache/com.unity.render-pipelines.high-definition@49ad9dd6ab9a'
properties = (package / 'Runtime/Material/Lit/LitProperties.hlsl').read_text()
assert 'CBUFFER_START(UnityPerMaterial)' in properties
properties = properties.replace('CBUFFER_START(UnityPerMaterial)', '''CBUFFER_START(UnityPerMaterial)
// Forest adapter additions; keep these outside keyword conditionals for SRP Batcher layout compatibility.
float4 _ForestCamera;
float4 _ForestDistances;
float _ForestTier;
float _ForestBuild;
float _ForestEnabled;''')
(shaders / 'ForestLitProperties.hlsl').write_bytes(('// Derived from installed HDRP 17.6 LitProperties; see ThirdPartyNotices.txt.\n' + properties).replace('\n', '\r\n').encode())


def metadata(path):
    """Stable references let serialized materials retain instancing/cutout variants in a player build."""
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'drone-warfare/dense-forest/' + path.relative_to(root).as_posix()).hex
        meta.write_bytes(('fileFormatVersion: 2\r\nguid: ' + guid + '\r\n').encode())
    return re.search(r'guid: (\w+)', meta.read_text())[1]


guid = metadata(shaders / 'ForestLit.shader')
metadata(shaders / 'ForestLitProperties.hlsl')
metadata(root / 'Assets/DroneSandbox/Scripts/ForestMaterials.cs')
materials = root / 'Assets/DroneSandbox/Resources/Materials/Forest'
materials.mkdir(exist_ok=True)
for name in ['BarkOak', 'LeavesOak', 'BarkBirch', 'LeavesBirch', 'BarkPine', 'LeavesSpruce', 'LeavesPine',
             'ImpostorOak', 'ImpostorBirch', 'ImpostorSpruce', 'ImpostorPine', 'Canopy']:
    source = root / ('Assets/DroneSandbox/Resources/Materials/BaseLit.mat' if name == 'Canopy' else 'Assets/DroneSandbox/Resources/Materials/PBR/' + name + '.mat')
    text = source.read_text()
    text = re.sub(r'm_Shader: \{[^}]+\}', 'm_Shader: {fileID: 4800000, guid: ' + guid + ', type: 3}', text)
    text = re.sub(r'm_EnableInstancingVariants: \d', 'm_EnableInstancingVariants: 1', text)
    text = re.sub(r'  m_Name: [^\n]+', '  m_Name: Forest ' + name, text)
    tier = 3 if name == 'Canopy' else 2 if name.startswith('Impostor') else 0
    text = text.replace('    m_Floats:', '    m_Floats:\n    - _ForestEnabled: 0\n    - _ForestBuild: 1\n    - _ForestTier: ' + str(tier))
    target = materials / (name + '.mat')
    text = '\n'.join(line.rstrip() for line in text.splitlines()) + '\n'
    target.write_bytes(text.replace('\n', '\r\n').encode()); metadata(target)
