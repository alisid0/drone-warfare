"""Check both surface variants and installed HDRP macro collisions; this does not replace Unity's full shader build."""
from pathlib import Path
import re
import runpy
import json

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SHADERS = ROOT / 'Assets/DroneSandbox/Resources/Shaders'
PACKAGE = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/Resources/PackageManager/BuiltInPackages')
compile_shader = runpy.run_path(str(HERE.parent / 'GroundDetail/shader_check.py'))['compile_shader']
source = (SHADERS / 'FarDetailSurface.hlsl').read_text()
for include in ('FarDetailPalette.hlsl', 'FarDetailRegional.hlsl'):
    source = source.replace(f'#include "{include}"', (SHADERS / include).read_text())

# Check actual installed macros, including PhysicallyBasedSkyCommon.hlsl's ground-albedo alias.
symbols = set(re.findall(r'\b(?:_Far\w+|sampler_Far\w+|FarRegion\w+)\b', source))
collisions = []
files = 0
for package in ('com.unity.render-pipelines.core', 'com.unity.render-pipelines.high-definition'):
    for path in (PACKAGE / package).rglob('*.hlsl'):
        macros = set(re.findall(r'^\s*#\s*define\s+(\w+)', path.read_text(errors='replace'), re.M))
        for symbol in macros & symbols:
            collisions.append((symbol, str(path)))
        files += 1
assert not collisions, collisions
prefix = '''
#define TEXTURE2D(n) Texture2D<float4> n
#define TEXTURE2D_ARRAY(n) Texture2DArray<float4> n
#define SAMPLER(n) SamplerState n
#define SAMPLE_TEXTURE2D_ARRAY_GRAD(t,s,u,i,x,y) t.SampleGrad(s,float3(u,i),x,y)
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
#define SAMPLE_TEXTURE2D_GRAD(t,s,u,x,y) t.SampleGrad(s,u,x,y)
#define LOAD_TEXTURE2D(t,c) t.Load(int3(c,0))
struct FragInputs { float4 texCoord0; float3x3 tangentToWorld; float3 positionRWS; };
struct SurfaceData { float3 baseColor; float perceptualSmoothness; };
Texture2D<float4> _BaseColorMap; SamplerState sampler_BaseColorMap; float4 _BaseColorMap_ST;
float3 _WorldSpaceCameraPos;
float3 GetAbsolutePositionWS(float3 p) {return p + _WorldSpaceCameraPos;}
// Keep the integration failure's real macro active in the syntax test too.
#define _GroundAlbedo _GroundAlbedo_PlanetRadius.xyz
'''
suffix = '''
float4 Check(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target {
FragInputs input; input.texCoord0=float4(uv,0,0); input.positionRWS=float3(uv*5000,0);
input.tangentToWorld=float3x3(1,0,0,0,0,1,0,1,0);
SurfaceData s; s.baseColor=0.15; s.perceptualSmoothness=0; float3 gradient=0;
FarDetailSurface(input,s,gradient); return float4(s.baseColor + gradient * 0.001,1);
}
'''
for mode in ('tile', 'chunk'):
    for output in ('colour', 'normal'):
        print(mode, output)
        ending = suffix if output == 'colour' else suffix.replace('s.baseColor + gradient * 0.001', 'gradient')
        compile_shader(('#define _FarRegionChunk 1\n' if mode == 'chunk' else '') + prefix + source + ending)
(HERE / 'Review/shader-check.json').write_text(json.dumps({'installedHlslFiles': files, 'checkedSymbols': sorted(symbols), 'macroCollisions': collisions,
    'variants': ['tile colour', 'tile normal', 'chunk colour', 'chunk normal'], 'limit': 'D3D syntax and installed macro census only. Real HDRP pass/variant compilation must run in Claude integration.'}, indent=2))
