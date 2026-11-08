"""D3DCompile ForestFade + ForestDetail as the canopy pixel shader uses them, at f7588a1 and in the working tree, and count
instructions with D3DDisassemble. Raster and ray-tracing branches both compile; the glade-floor-only path (lattice
skipped) is counted separately. HDRP pass/variant compilation and GPU timing remain Claude's Unity build check;
instruction counts are only a proxy for cost. Reuses Tools/CanopyRelief2/check_shader.py's compiler bindings."""
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'CanopyRelief2'))
import check_shader as C

ROOT = Path(__file__).resolve().parents[2]
# The tier-3 block of GetSurfaceAndBuiltinData in the working tree: macro cover and its derivatives replace the weight.
AFTER = '''float4 main(float4 p:SV_Position,float2 uv:TEXCOORD0,float3 world:TEXCOORD1,float4 macro:TEXCOORD2,float3 n:NORMAL):SV_Target {
ForestFade(world,uv,(uint2)p.xy);
float2 forestWorld = world.xz + _ForestOrigin.xy;
float alpha = macro.a;
float2 worldDx = float2(8.0, 0.0), worldDy = float2(0.0, 8.0), coverD = 0.0;
#if !defined(SHADER_STAGE_RAY_TRACING)
worldDx = ddx(forestWorld); worldDy = ddy(forestWorld); coverD = float2(ddx(alpha), ddy(alpha));
#endif
float2 slope;
float crownFactor = ForestCrownDetail(forestWorld, worldDx, worldDy, macro.rgb, alpha, coverD, normalize(n).y, _ForestOrigin.zw, slope);
float3 base = macro.rgb * crownFactor;
return float4(base * saturate(dot(normalize(n + float3(slope.x, 0.0, slope.y)), float3(0.46,0.59,-0.66))), 1); }'''
# The open-floor path: the cover is zero and flat, so the crown lattice is skipped and only the meadow runs.
FLOOR = AFTER.replace('float alpha = macro.a;', 'float alpha = 0.0;').replace('normalize(n).y', '1.0')
# The dense-wood path: full flat cover, so the compiler drops the floor branch, as dense pixels skip it at run time.
DENSE = AFTER.replace('float alpha = macro.a;', 'float alpha = 1.0;').replace('normalize(n).y', '1.0')

if __name__ == '__main__':
    base = subprocess.check_output(['git', '-c', 'safe.directory=' + ROOT.as_posix(), 'show', 'f7588a1:Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl'],
                                   cwd=ROOT, text=True)
    detail = (ROOT / 'Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl').read_text()
    C.check('before', base, C.AFTER)
    C.check('after', detail, AFTER)
    C.check('after, dense wood', detail, DENSE)
    C.check('after, open floor', detail, FLOOR)
