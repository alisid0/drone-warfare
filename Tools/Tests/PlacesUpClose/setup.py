"""Derive the standalone check and licensed place shader without importing Unity."""
from pathlib import Path
import uuid
import re

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent

out = ROOT/'Assets/DroneSandbox/Resources/Shaders'
package = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit')
s = (package/'Lit.shader').read_text().replace('Shader "HDRP/Lit"', 'Shader "DroneSandbox/PlaceLit"')
s = s.replace('    Properties\n    {', '''    Properties
    {
        _PlaceCamera("Place camera", Vector) = (0,0,0,0)
        _PlaceProjection("Place projection", Vector) = (1,-1,0,0)
        _PlaceRange("Place LOD and proxy range", Vector) = (0,1100,850,1100)''')
s = s.replace('    HLSLINCLUDE', '    HLSLINCLUDE\n    #define VARYINGS_NEED_POSITION_WS')
for name in ('LitData', 'LitProperties'):
    s = s.replace('Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/'+name+'.hlsl', 'Assets/DroneSandbox/Resources/Shaders/Place'+name+'.hlsl')
p = (package/'LitProperties.hlsl').read_text().replace('CBUFFER_START(UnityPerMaterial)', 'CBUFFER_START(UnityPerMaterial)\nfloat4 _PlaceCamera;\nfloat4 _PlaceProjection;\nfloat4 _PlaceRange;')
d = (package/'LitData.hlsl').read_text()
marker = 'RAY_TRACING_OPTIONAL_PARAMETERS)\n{'
assert marker in d
d = d.replace(marker, marker+'''
    // Use complementary coverage in depth, colour and shadow passes, retaining all the original PBR shading.
    float3 placeWorld = GetAbsolutePositionWS(input.positionRWS);
    float placeDistance = _PlaceProjection.y >= 0 ? _PlaceProjection.y : distance(placeWorld, _PlaceCamera.xyz) * _PlaceProjection.x;
    float placeNoise = frac(52.9829189 * frac(dot(floor(input.positionSS.xy), float2(0.06711056, 0.00583715))));
    float nearWeight = 1 - smoothstep(_PlaceRange.z, _PlaceRange.w, placeDistance);
    if (_PlaceProjection.z > 0.5) clip(placeNoise - nearWeight);
    else
    {
        clip(nearWeight - placeNoise - 0.00001);
        // Adjacent tiers share the same mask across the ten-percent transition band.
        if (_PlaceRange.x > 0) clip(placeNoise - (1 - smoothstep(_PlaceRange.x * 0.9, _PlaceRange.x, placeDistance)));
        if (_PlaceRange.y < _PlaceRange.w) clip(1 - smoothstep(_PlaceRange.y * 0.9, _PlaceRange.y, placeDistance) - placeNoise - 0.00001);
    }
''')
for name, text in [('PlaceLit.shader', s), ('PlaceLitProperties.hlsl', p), ('PlaceLitData.hlsl', d)]:
    (out/name).write_bytes(('// Derived from installed HDRP 17.6 Lit; see ThirdPartyNotices.txt.\n'+text).replace('\r\n','\n').replace('\n','\r\n').encode())

# New runtime assets have stable GUIDs without invoking Unity's importer.
for path in list((ROOT/'Assets/DroneSandbox/Scripts').glob('Landmark*.cs')) + list((ROOT/'Assets/DroneSandbox/Scripts').glob('VillageTown*.cs')) + [ROOT/'Assets/DroneSandbox/Scripts/FarChunkUpload.cs'] + list(out.glob('Place*')):
    if path.suffix == '.meta': continue
    meta = Path(str(path)+'.meta')
    if not meta.exists(): meta.write_bytes(('fileFormatVersion: 2\r\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'drone-warfare/'+path.relative_to(ROOT).as_posix()).hex+'\r\n').encode())

# Serialized instancing/keyword witnesses prevent player stripping of runtime-cloned material variants.
shader_guid=re.search(r'guid: (\w+)',(out/'PlaceLit.shader.meta').read_text())[1]
variants={}
matroot=ROOT/'Assets/DroneSandbox/Resources/Materials'
for source in [matroot/'BaseLit.mat']+sorted((matroot/'PBR').glob('*.mat')):
    text=source.read_text()
    keywords=text.split('  m_ValidKeywords:')[1].split('  m_InvalidKeywords:')[0]
    variants.setdefault(keywords,text)
for index,text in enumerate(variants.values()):
    text=re.sub(r'm_Shader: \{[^}]+\}', 'm_Shader: {fileID: 4800000, guid: '+shader_guid+', type: 3}',text)
    text=re.sub(r'm_EnableInstancingVariants: \d','m_EnableInstancingVariants: 1',text)
    path=matroot/('PlaceVariant'+str(index)+'.mat')
    path.write_bytes(text.replace('\r\n','\n').replace('\n','\r\n').encode())
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_bytes(('fileFormatVersion: 2\r\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'drone-warfare/'+path.relative_to(ROOT).as_posix()).hex+'\r\n').encode())
print('Retained place shader keyword sets:',len(variants))
