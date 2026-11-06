"""D3DCompile ForestFade + ForestDetail as the canopy pixel shader uses them, before (b8d8322) and after, and count
instructions with D3DDisassemble. Raster and ray-tracing branches both compile. HDRP pass/variant compilation and
GPU timing remain Claude's Unity build check; instruction counts are only a proxy for cost."""
from pathlib import Path
import ctypes as c
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
folder = ROOT / 'Assets/DroneSandbox/Resources/Shaders'
compiler = c.WinDLL('d3dcompiler_47.dll')
compile_shader = compiler.D3DCompile
compile_shader.argtypes = [c.c_void_p, c.c_size_t, c.c_char_p, c.c_void_p, c.c_void_p, c.c_char_p, c.c_char_p, c.c_uint,
                           c.c_uint, c.POINTER(c.c_void_p), c.POINTER(c.c_void_p)]
compile_shader.restype = c.c_long
disassemble = compiler.D3DDisassemble
disassemble.argtypes = [c.c_void_p, c.c_size_t, c.c_uint, c.c_char_p, c.POINTER(c.c_void_p)]
disassemble.restype = c.c_long


def blob_bytes(blob):
    """Read a compiler-owned ID3DBlob through its documented COM methods."""
    table = c.cast(blob, c.POINTER(c.POINTER(c.c_void_p))).contents
    pointer = c.WINFUNCTYPE(c.c_void_p, c.c_void_p)(table[3])(blob)
    size = c.WINFUNCTYPE(c.c_size_t, c.c_void_p)(table[4])(blob)
    return c.string_at(pointer, size)


PREFIX = '''Texture2D _ForestDetail, _ForestDetailB, _BaseColorMap;
SamplerState sampler_ForestDetail, sampler_BaseColorMap;
#define SAMPLE_TEXTURE2D_GRAD(t,s,u,x,y) t.SampleGrad(s,u,x,y)
#define SAMPLE_TEXTURE2D_LOD(t,s,u,l) t.SampleLevel(s,u,l)
float4 _ForestCamera,_ForestDistances,_ForestProjection,_ForestSelection,_ForestCrown,_ForestRadius,_ForestViewport,_ForestOrigin;
float _ForestTier,_ForestEnabled,_ForestBuild;
float4x4 GetObjectToWorldMatrix(){return float4x4(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);}
float3 GetAbsolutePositionWS(float3 p){return p;}
'''
# The tier-3 block of GetSurfaceAndBuiltinData at each revision, fed a macro colour, cover and vertex normal.
BEFORE = '''float4 main(float4 p:SV_Position,float2 uv:TEXCOORD0,float3 world:TEXCOORD1,float4 macro:TEXCOORD2,float3 n:NORMAL):SV_Target {
ForestFade(world,uv,(uint2)p.xy);
float3 detail = ForestCrownDetail(world.xz + _ForestOrigin.xy);
float crownWeight = smoothstep(0.1, 0.7, macro.a);
float3 base = macro.rgb * lerp(1.0, 1.0 + (detail.r - 0.5) * 1.6, crownWeight);
float2 slope = detail.gb * (0.65 * crownWeight);
return float4(base * saturate(dot(normalize(n + float3(slope.x, 0.0, slope.y)), float3(0.46,0.59,-0.66))), 1); }'''
AFTER = '''float4 main(float4 p:SV_Position,float2 uv:TEXCOORD0,float3 world:TEXCOORD1,float4 macro:TEXCOORD2,float3 n:NORMAL):SV_Target {
ForestFade(world,uv,(uint2)p.xy);
float2 forestWorld = world.xz + _ForestOrigin.xy;
float crownWeight = smoothstep(0.1, 0.7, macro.a);
float2 worldDx = float2(8.0, 0.0), worldDy = float2(0.0, 8.0);
#if !defined(SHADER_STAGE_RAY_TRACING)
worldDx = ddx(forestWorld); worldDy = ddy(forestWorld);
#endif
float2 slope;
float crownFactor = ForestCrownDetail(forestWorld, worldDx, worldDy, macro.rgb, crownWeight, _ForestOrigin.zw, slope);
float3 base = macro.rgb * crownFactor;
return float4(base * saturate(dot(normalize(n + float3(slope.x, 0.0, slope.y)), float3(0.46,0.59,-0.66))), 1); }'''


def check(name, detail, body):
    """Compile both branches; return the raster disassembly's instruction counts."""
    source = PREFIX + detail + (folder / 'ForestFade.hlsl').read_text() + body
    counts = {}
    for branch, define in (('raster', ''), ('ray-helper', '#define SHADER_STAGE_RAY_TRACING 1\n')):
        code = (define + source).encode()
        compiled, error = c.c_void_p(), c.c_void_p()
        result = compile_shader(code, len(code), name.encode(), None, None, b'main', b'ps_5_0', 1 << 18, 0, c.byref(compiled), c.byref(error))
        messages = blob_bytes(error).decode(errors='replace') if error else ''
        assert result >= 0, messages
        bytecode = blob_bytes(compiled)
        text = c.c_void_p()
        assert disassemble(bytecode, len(bytecode), 0, None, c.byref(text)) >= 0
        listing = blob_bytes(text).decode(errors='replace')
        lines = [l.strip() for l in listing.splitlines() if l.strip() and not l.strip().startswith(('//', 'dcl_', 'ps_', '{', '}'))]
        samples = sum(1 for l in lines if l.startswith('sample'))
        slots = re.search(r'Approximately (\d+) instruction slots', listing)
        counts[branch] = (len(lines), samples, int(slots.group(1)) if slots else None)
        warnings = [l for l in messages.splitlines() if 'warning' in l]
        print('%s %s: compiled, %d instructions (%d texture samples), %s slots%s' % (name, branch, len(lines), samples,
              counts[branch][2], '; ' + '; '.join(warnings) if warnings else ''))
    return counts


if __name__ == '__main__':
    base = subprocess.check_output(['git', '-c', 'safe.directory=' + ROOT.as_posix(), 'show', 'b8d8322:Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl'],
                                   cwd=ROOT, text=True)
    check('before', base, BEFORE)
    check('after', (folder / 'ForestDetail.hlsl').read_text(), AFTER)
