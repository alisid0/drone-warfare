"""Run the real ForestDetail.hlsl on Direct3D 11's WARP software rasteriser (CPU, never the GPU Unity uses) and compare
ForestCrownDetail with Tools/Glades/crowns.py, the NumPy mirror behind the 'after' review pictures. A compute shader feeds
it explicit world derivatives and a macro cover with its screen derivatives, over flat top-down patches at several
footprints: dense wood (cover 1), a glade edge (cover ramping 0 to 1 across the patch) and an open floor (cover 0).
Reuses the COM bindings of Tools/CanopyRelief2/check_parity.py."""
from pathlib import Path
import ctypes as c
import sys
import numpy as np

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parent / 'CanopyRelief2'))
import render as R
import check_parity as W
from check_shader import blob_bytes, compile_shader
import importlib.util
# CanopyRelief2's modules already hold the name 'crowns' (the f7588a1 mirror); load this tool's mirror under its own.
spec = importlib.util.spec_from_file_location('glade_crowns', here / 'crowns.py')
G = importlib.util.module_from_spec(spec); spec.loader.exec_module(G)

ROOT = here.parents[1]
SIZE = W.SIZE
KERNEL = '''Texture2D _ForestDetail : register(t0);
SamplerState sampler_ForestDetail : register(s0);
#define SAMPLE_TEXTURE2D_GRAD(t,s,u,x,y) t.SampleGrad(s,u,x,y)
cbuffer Patch : register(b0) { float4 origin; float4 macro; float4 sun; float4 ramp; };
RWTexture2D<float4> result : register(u0);
''' + (ROOT / 'Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl').read_text() + '''
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float2 world = origin.xy + (float2(id.xy) + 0.5) * origin.z;
    // Cover ramps along x; its screen derivative is the ramp's slope per pixel wherever it is not clamped.
    float a = ramp.x + ramp.y * (float(id.x) + 0.5) * origin.z;
    float canopy = saturate(a);
    float2 canopyD = float2(a > 0.0 && a < 1.0 ? ramp.y * origin.z : 0.0, 0.0);
    float2 slope;
    float factor = ForestCrownDetail(world, float2(origin.z, 0.0), float2(0.0, origin.z), macro.rgb, canopy, canopyD, ramp.z, sun.xy, slope);
    result[id.xy] = float4(factor, slope, canopy);
}'''


def main():
    d3d = c.WinDLL('d3d11.dll')
    device, context, level = c.c_void_p(), c.c_void_p(), c.c_uint()
    W.check(d3d.D3D11CreateDevice(None, 5, None, 0, None, 0, 7, c.byref(device), c.byref(level), c.byref(context)), 'D3D11CreateDevice(WARP)')
    code = KERNEL.encode(); blob, error = c.c_void_p(), c.c_void_p()
    hr = compile_shader(code, len(code), b'parity', None, None, b'main', b'cs_5_0', 1 << 18, 0, c.byref(blob), c.byref(error))
    assert hr >= 0, blob_bytes(error).decode(errors='replace')
    bytecode = blob_bytes(blob)
    method, HR = W.method, W.HR
    shader = c.c_void_p()
    W.check(method(device, 18, HR, c.c_void_p, c.c_void_p, c.c_size_t, c.c_void_p, c.POINTER(c.c_void_p))(device, bytecode, len(bytecode), None, c.byref(shader)), 'CreateComputeShader')
    levels = R.pyramid(R.TEXTURES / 'ForestDetail.png')
    data = [np.ascontiguousarray(np.concatenate([np.round(l * 255), np.full(l.shape[:2] + (1,), 255.0)], axis=-1).astype(np.uint8)) for l in levels]
    init = (W.SubresourceData * len(data))(*[W.SubresourceData(d.ctypes.data, d.shape[1] * 4, 0) for d in data])
    texture, view = c.c_void_p(), c.c_void_p()
    W.check(method(device, 5, HR, c.c_void_p, c.POINTER(W.TextureDesc), c.c_void_p, c.POINTER(c.c_void_p))(
        device, c.byref(W.TextureDesc(512, 512, len(data), 1, 28, 1, 0, 0, 8, 0, 0)), init, c.byref(texture)), 'CreateTexture2D(map)')
    W.check(method(device, 7, HR, c.c_void_p, c.c_void_p, c.c_void_p, c.POINTER(c.c_void_p))(device, texture, None, c.byref(view)), 'CreateShaderResourceView')
    sampler = c.c_void_p()
    W.check(method(device, 23, HR, c.c_void_p, c.POINTER(W.SamplerDesc), c.POINTER(c.c_void_p))(
        device, c.byref(W.SamplerDesc(0x55, 1, 1, 1, 0.0, 4, 1, (c.c_float * 4)(), 0.0, 3.4e38)), c.byref(sampler)), 'CreateSamplerState')
    output, staging, uav = c.c_void_p(), c.c_void_p(), c.c_void_p()
    create_texture = method(device, 5, HR, c.c_void_p, c.POINTER(W.TextureDesc), c.c_void_p, c.POINTER(c.c_void_p))
    W.check(create_texture(device, c.byref(W.TextureDesc(SIZE, SIZE, 1, 1, 2, 1, 0, 0, 0x80, 0, 0)), None, c.byref(output)), 'CreateTexture2D(out)')
    W.check(create_texture(device, c.byref(W.TextureDesc(SIZE, SIZE, 1, 1, 2, 1, 0, 3, 0, 0x20000, 0)), None, c.byref(staging)), 'CreateTexture2D(staging)')
    W.check(method(device, 8, HR, c.c_void_p, c.c_void_p, c.c_void_p, c.POINTER(c.c_void_p))(device, output, None, c.byref(uav)), 'CreateUnorderedAccessView')
    set_resources = method(context, 67, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    set_uavs = method(context, 68, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p), c.c_void_p)
    set_shader = method(context, 69, None, c.c_void_p, c.c_void_p, c.c_void_p, c.c_uint)
    set_samplers = method(context, 70, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    set_constants = method(context, 71, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    dispatch = method(context, 41, None, c.c_void_p, c.c_uint, c.c_uint, c.c_uint)
    copy = method(context, 47, None, c.c_void_p, c.c_void_p, c.c_void_p)
    map_ = method(context, 14, HR, c.c_void_p, c.c_void_p, c.c_uint, c.c_uint, c.c_uint, c.POINTER(W.Mapped))
    unmap = method(context, 15, None, c.c_void_p, c.c_void_p, c.c_uint)
    create_buffer = method(device, 3, HR, c.c_void_p, c.POINTER(W.BufferDesc), c.POINTER(W.SubresourceData), c.POINTER(c.c_void_p))
    set_shader(context, shader, None, 0)
    set_resources(context, 0, 1, c.byref(view)); set_samplers(context, 0, 1, c.byref(sampler)); set_uavs(context, 0, 1, c.byref(uav), None)
    macro = np.array([0.055, 0.100, 0.040])
    broadleaf = float(G.smoothstep(0.50, 0.58, macro[0] / macro[1]))
    sun = R.SUN
    shift = (-sun[0] / sun[1], -sun[2] / sun[1])
    worst = 0.0
    patches = ((0.25, (21500.0, 18500.0)), (1.0, (-7310.0, 2210.0)), (2.0, (12000.0, -16384.0)), (3.0, (21500.0, 18500.0)))
    # 'side' is the glade edge on a steep face (normal height 0.78), which keeps foliage instead of floor.
    for case, (start, rate, upright) in (('dense', (1.0, 0.0, 1.0)), ('edge', (-0.15, 1.3, 1.0)), ('side', (-0.15, 1.3, 0.78)), ('open', (0.0, 0.0, 1.0))):
        for pixel, origin in patches:
            # The edge ramp crosses the patch whatever the footprint: cover -0.15 to 1.15.
            slope_per_m = rate / (SIZE * pixel)
            constants = np.array([origin[0], origin[1], pixel, 0, *macro, 1.0, shift[0], shift[1], 0, 0, start, slope_per_m, upright, 0], dtype=np.float32)
            buffer = c.c_void_p()
            W.check(create_buffer(device, c.byref(W.BufferDesc(64, 0, 4, 0, 0, 0)), c.byref(W.SubresourceData(constants.ctypes.data, 0, 0)), c.byref(buffer)), 'CreateBuffer')
            set_constants(context, 0, 1, c.byref(buffer))
            dispatch(context, SIZE // 8, SIZE // 8, 1)
            copy(context, staging, output)
            mapped = W.Mapped()
            W.check(map_(context, staging, 0, 1, 0, c.byref(mapped)), 'Map')
            rows = np.ctypeslib.as_array(c.cast(mapped.pData, c.POINTER(c.c_float)), shape=(SIZE, mapped.RowPitch // 4)).copy()
            unmap(context, staging, 0)
            gpu = rows[:, :SIZE * 4].reshape(SIZE, SIZE, 4)
            x, z = np.meshgrid(origin[0] + (np.arange(SIZE) + 0.5) * pixel, origin[1] + (np.arange(SIZE) + 0.5) * pixel)
            # The same float32 ramp the kernel evaluates.
            a = (np.float32(start) + np.float32(slope_per_m) * ((np.arange(SIZE, dtype=np.float32) + np.float32(0.5)) * np.float32(pixel)))
            a = np.broadcast_to(a.astype(np.float64), x.shape)
            canopy = np.clip(a, 0.0, 1.0)
            canopy_dx = np.where((a > 0) & (a < 1), float(np.float32(slope_per_m) * np.float32(pixel)), 0.0)
            full = np.full(x.shape, pixel)
            factor, sx, sz = G.pattern((x, z), (full, np.zeros(x.shape)), (np.zeros(x.shape), full), full, np.full(x.shape, broadleaf),
                                       canopy, (canopy_dx, np.zeros(x.shape)), np.full(x.shape, float(np.float32(upright))),
                                       lambda *q: R.sample_grad(levels, *q), sun)
            df = np.abs(gpu[..., 0] - factor); ds = np.maximum(np.abs(gpu[..., 1] - sx), np.abs(gpu[..., 2] - sz))
            # Hash, branch, threshold or lattice slips show as large errors on whole crowns; filtering only as small ones.
            print('%-5s footprint %.2f m: factor mean %.4f vs %.4f, |diff| mean %.5f p99.9 %.4f max %.4f; slope |diff| mean %.5f p99.9 %.4f; pixels over 0.02: %.3f%%'
                  % (case, pixel, gpu[..., 0].mean(), factor.mean(), df.mean(), np.percentile(df, 99.9), df.max(), ds.mean(),
                     np.percentile(ds, 99.9), 100 * (df > 0.02).mean()), flush=True)
            worst = max(worst, float(np.percentile(df, 99.9)))
    assert worst < 0.05, 'HLSL and crowns.py disagree'
    print('PARITY OK: WARP-executed ForestDetail.hlsl matches Tools/Glades/crowns.py (99.9th percentile factor error %.4f)' % worst)


if __name__ == '__main__':
    main()
