"""Run the real ForestDetail.hlsl on Direct3D 11's WARP software rasteriser (CPU, never the GPU Unity uses) and compare
ForestCrownDetail with crowns.py, the NumPy mirror behind the review pictures. A compute shader feeds it the same
explicit world derivatives a pixel shader would get from ddx/ddy, over flat top-down patches at several footprints."""
from pathlib import Path
import ctypes as c
import sys
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import render as R
import crowns as C
from check_shader import blob_bytes, compile_shader

ROOT = Path(__file__).resolve().parents[2]
SIZE = 256
HR = c.HRESULT


def method(obj, index, *types):
    """Bind COM vtable entry `index` of `obj`."""
    table = c.cast(obj, c.POINTER(c.POINTER(c.c_void_p))).contents
    return c.WINFUNCTYPE(*types)(table[index])


def check(hr, what):
    assert hr >= 0, '%s failed: 0x%08x' % (what, hr & 0xffffffff)


class TextureDesc(c.Structure):
    _fields_ = [('Width', c.c_uint), ('Height', c.c_uint), ('MipLevels', c.c_uint), ('ArraySize', c.c_uint), ('Format', c.c_uint),
                ('Count', c.c_uint), ('Quality', c.c_uint), ('Usage', c.c_uint), ('BindFlags', c.c_uint), ('CPUAccessFlags', c.c_uint),
                ('MiscFlags', c.c_uint)]


class SubresourceData(c.Structure):
    _fields_ = [('pSysMem', c.c_void_p), ('SysMemPitch', c.c_uint), ('SysMemSlicePitch', c.c_uint)]


class BufferDesc(c.Structure):
    _fields_ = [('ByteWidth', c.c_uint), ('Usage', c.c_uint), ('BindFlags', c.c_uint), ('CPUAccessFlags', c.c_uint),
                ('MiscFlags', c.c_uint), ('StructureByteStride', c.c_uint)]


class SamplerDesc(c.Structure):
    _fields_ = [('Filter', c.c_uint), ('AddressU', c.c_uint), ('AddressV', c.c_uint), ('AddressW', c.c_uint), ('MipLODBias', c.c_float),
                ('MaxAnisotropy', c.c_uint), ('ComparisonFunc', c.c_uint), ('BorderColor', c.c_float * 4), ('MinLOD', c.c_float), ('MaxLOD', c.c_float)]


class Mapped(c.Structure):
    _fields_ = [('pData', c.c_void_p), ('RowPitch', c.c_uint), ('DepthPitch', c.c_uint)]


KERNEL = '''Texture2D _ForestDetail : register(t0);
SamplerState sampler_ForestDetail : register(s0);
#define SAMPLE_TEXTURE2D_GRAD(t,s,u,x,y) t.SampleGrad(s,u,x,y)
cbuffer Patch : register(b0) { float4 origin; float4 macro; float4 sun; };
RWTexture2D<float4> result : register(u0);
''' + (ROOT / 'Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl').read_text() + '''
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float2 world = origin.xy + (float2(id.xy) + 0.5) * origin.z;
    float2 slope;
    float factor = ForestCrownDetail(world, float2(origin.z, 0.0), float2(0.0, origin.z), macro.rgb, macro.a, sun.xy, slope);
    result[id.xy] = float4(factor, slope, 1.0);
}'''


def main():
    d3d = c.WinDLL('d3d11.dll')
    device, context, level = c.c_void_p(), c.c_void_p(), c.c_uint()
    check(d3d.D3D11CreateDevice(None, 5, None, 0, None, 0, 7, c.byref(device), c.byref(level), c.byref(context)), 'D3D11CreateDevice(WARP)')
    code = KERNEL.encode(); blob, error = c.c_void_p(), c.c_void_p()
    hr = compile_shader(code, len(code), b'parity', None, None, b'main', b'cs_5_0', 1 << 18, 0, c.byref(blob), c.byref(error))
    assert hr >= 0, blob_bytes(error).decode(errors='replace')
    bytecode = blob_bytes(blob)
    shader = c.c_void_p()
    check(method(device, 18, HR, c.c_void_p, c.c_void_p, c.c_size_t, c.c_void_p, c.POINTER(c.c_void_p))(device, bytecode, len(bytecode), None, c.byref(shader)), 'CreateComputeShader')
    # The photo crown map with the same box mips the review replay uses, quantised to 8 bits as Unity stores them.
    levels = R.pyramid(R.TEXTURES / 'ForestDetail.png')
    data = [np.ascontiguousarray(np.concatenate([np.round(l * 255), np.full(l.shape[:2] + (1,), 255.0)], axis=-1).astype(np.uint8)) for l in levels]
    init = (SubresourceData * len(data))(*[SubresourceData(d.ctypes.data, d.shape[1] * 4, 0) for d in data])
    texture, view = c.c_void_p(), c.c_void_p()
    check(method(device, 5, HR, c.c_void_p, c.POINTER(TextureDesc), c.c_void_p, c.POINTER(c.c_void_p))(
        device, c.byref(TextureDesc(512, 512, len(data), 1, 28, 1, 0, 0, 8, 0, 0)), init, c.byref(texture)), 'CreateTexture2D(map)')
    check(method(device, 7, HR, c.c_void_p, c.c_void_p, c.c_void_p, c.POINTER(c.c_void_p))(device, texture, None, c.byref(view)), 'CreateShaderResourceView')
    sampler = c.c_void_p()
    check(method(device, 23, HR, c.c_void_p, c.POINTER(SamplerDesc), c.POINTER(c.c_void_p))(
        device, c.byref(SamplerDesc(0x55, 1, 1, 1, 0.0, 4, 1, (c.c_float * 4)(), 0.0, 3.4e38)), c.byref(sampler)), 'CreateSamplerState')
    output, staging, uav = c.c_void_p(), c.c_void_p(), c.c_void_p()
    create_texture = method(device, 5, HR, c.c_void_p, c.POINTER(TextureDesc), c.c_void_p, c.POINTER(c.c_void_p))
    check(create_texture(device, c.byref(TextureDesc(SIZE, SIZE, 1, 1, 2, 1, 0, 0, 0x80, 0, 0)), None, c.byref(output)), 'CreateTexture2D(out)')
    check(create_texture(device, c.byref(TextureDesc(SIZE, SIZE, 1, 1, 2, 1, 0, 3, 0, 0x20000, 0)), None, c.byref(staging)), 'CreateTexture2D(staging)')
    check(method(device, 8, HR, c.c_void_p, c.c_void_p, c.c_void_p, c.POINTER(c.c_void_p))(device, output, None, c.byref(uav)), 'CreateUnorderedAccessView')
    set_resources = method(context, 67, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    set_uavs = method(context, 68, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p), c.c_void_p)
    set_shader = method(context, 69, None, c.c_void_p, c.c_void_p, c.c_void_p, c.c_uint)
    set_samplers = method(context, 70, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    set_constants = method(context, 71, None, c.c_void_p, c.c_uint, c.c_uint, c.POINTER(c.c_void_p))
    dispatch = method(context, 41, None, c.c_void_p, c.c_uint, c.c_uint, c.c_uint)
    copy = method(context, 47, None, c.c_void_p, c.c_void_p, c.c_void_p)
    map_ = method(context, 14, HR, c.c_void_p, c.c_void_p, c.c_uint, c.c_uint, c.c_uint, c.POINTER(Mapped))
    unmap = method(context, 15, None, c.c_void_p, c.c_void_p, c.c_uint)
    create_buffer = method(device, 3, HR, c.c_void_p, c.POINTER(BufferDesc), c.POINTER(SubresourceData), c.POINTER(c.c_void_p))
    set_shader(context, shader, None, 0)
    set_resources(context, 0, 1, c.byref(view)); set_samplers(context, 0, 1, c.byref(sampler)); set_uavs(context, 0, 1, c.byref(uav), None)
    R.MAPS = [levels]
    macro = np.array([0.055, 0.100, 0.040])
    broadleaf = float(C.smoothstep(0.50, 0.58, macro[0] / macro[1]))
    sun = R.SUN
    shift = (-sun[0] / sun[1], -sun[2] / sun[1])
    worst = 0.0
    for pixel, origin in ((0.25, (21500.0, 18500.0)), (1.0, (-7310.0, 2210.0)), (2.0, (12000.0, -16384.0)), (3.0, (21500.0, 18500.0))):
        constants = np.array([origin[0], origin[1], pixel, 0, *macro, 1.0, shift[0], shift[1], 0, 0], dtype=np.float32)
        buffer = c.c_void_p()
        check(create_buffer(device, c.byref(BufferDesc(48, 0, 4, 0, 0, 0)), c.byref(SubresourceData(constants.ctypes.data, 0, 0)), c.byref(buffer)), 'CreateBuffer')
        set_constants(context, 0, 1, c.byref(buffer))
        dispatch(context, SIZE // 8, SIZE // 8, 1)
        copy(context, staging, output)
        mapped = Mapped()
        check(map_(context, staging, 0, 1, 0, c.byref(mapped)), 'Map')
        rows = np.ctypeslib.as_array(c.cast(mapped.pData, c.POINTER(c.c_float)), shape=(SIZE, mapped.RowPitch // 4)).copy()
        unmap(context, staging, 0)
        gpu = rows[:, :SIZE * 4].reshape(SIZE, SIZE, 4)
        x, z = np.meshgrid(origin[0] + (np.arange(SIZE) + 0.5) * pixel, origin[1] + (np.arange(SIZE) + 0.5) * pixel)
        full = np.full(x.shape, pixel)
        factor, sx, sz = C.pattern((x, z), (full, np.zeros(x.shape)), (np.zeros(x.shape), full), full, np.full(x.shape, broadleaf),
                                   np.ones(x.shape), lambda *a: R.sample_grad(levels, *a), sun)
        df = np.abs(gpu[..., 0] - factor); ds = np.maximum(np.abs(gpu[..., 1] - sx), np.abs(gpu[..., 2] - sz))
        # Hash, branch or lattice slips show as large errors on whole crowns; texture filtering only as small ones.
        print('footprint %.2f m: factor mean %.4f vs %.4f, |diff| mean %.5f p99.9 %.4f max %.4f; slope |diff| mean %.5f p99.9 %.4f; pixels over 0.02: %.3f%%'
              % (pixel, gpu[..., 0].mean(), factor.mean(), df.mean(), np.percentile(df, 99.9), df.max(), ds.mean(), np.percentile(ds, 99.9),
                 100 * (df > 0.02).mean()))
        worst = max(worst, float(np.percentile(df, 99.9)))
    assert worst < 0.05, 'HLSL and crowns.py disagree'
    print('PARITY OK: WARP-executed ForestDetail.hlsl matches crowns.py (99.9th percentile factor error %.4f)' % worst)


if __name__ == '__main__':
    main()
