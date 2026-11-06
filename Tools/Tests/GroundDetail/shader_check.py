"""Compile the unmodified sampler hook as a D3D11 pixel shader without starting Unity."""
from pathlib import Path
import ctypes as C
import re

ROOT = Path(__file__).resolve().parents[3]
SHADERS = ROOT / 'Assets/DroneSandbox/Resources/Shaders'

def compile_shader(source):
    """Call Windows' native shader compiler, returning diagnostic text and binary size."""
    compiler = C.WinDLL('d3dcompiler_47.dll')
    compiler.D3DCompile.argtypes = [C.c_void_p, C.c_size_t, C.c_char_p, C.c_void_p, C.c_void_p, C.c_char_p, C.c_char_p, C.c_uint, C.c_uint, C.POINTER(C.c_void_p), C.POINTER(C.c_void_p)]
    compiler.D3DCompile.restype = C.c_long
    binary, errors = C.c_void_p(), C.c_void_p()
    raw = source.encode()
    hr = compiler.D3DCompile(raw, len(raw), b'GroundDetail offline sampler', None, None, b'Check', b'ps_5_0', 1 << 15, 0, C.byref(binary), C.byref(errors))
    def blob(pointer):
        """Read ID3DBlob using its documented COM vtable."""
        if not pointer:
            return b''
        table = C.cast(pointer, C.POINTER(C.POINTER(C.c_void_p))).contents
        address = C.WINFUNCTYPE(C.c_void_p, C.c_void_p)(table[3])(pointer)
        size = C.WINFUNCTYPE(C.c_size_t, C.c_void_p)(table[4])(pointer)
        result = C.string_at(address, size)
        C.WINFUNCTYPE(C.c_ulong, C.c_void_p)(table[2])(pointer)
        return result
    messages, code = blob(errors), blob(binary)
    if hr < 0:
        raise RuntimeError(messages.decode(errors='replace'))
    print('D3D11 ps_5_0 sampler compile:', len(code), 'bytes;', messages.decode(errors='replace') or 'no diagnostics')

if __name__ == '__main__':
    prefix = '''
#define TEXTURE2D(n) Texture2D<float4> n
#define TEXTURE2D_ARRAY(n) Texture2DArray<float4> n
#define SAMPLER(n) SamplerState n
#define SAMPLE_TEXTURE2D_ARRAY_GRAD(t,s,u,i,x,y) t.SampleGrad(s,float3(u,i),x,y)
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
#define LOAD_TEXTURE2D(t,c) t.Load(int3(c,0))
struct FragInputs { float4 texCoord0; float3x3 tangentToWorld; };
struct SurfaceData { float3 baseColor; float perceptualSmoothness; };
Texture2D<float4> _BaseColorMap; SamplerState sampler_BaseColorMap; float4 _BaseColorMap_ST;
'''
    source = (SHADERS / 'FarDetailSurface.hlsl').read_text().replace('#include "FarDetailPalette.hlsl"', (SHADERS / 'FarDetailPalette.hlsl').read_text())
    suffix = '''
float4 Check(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target {
FragInputs input; input.texCoord0=float4(uv,0,0); input.tangentToWorld=float3x3(1,0,0,0,0,1,0,1,0);
SurfaceData s; s.baseColor=0; s.perceptualSmoothness=0; float3 gradient=0;
FarDetailSurface(input,s,gradient); return float4(s.baseColor + gradient * 0.001,1);
}
'''
    compile_shader(prefix + source + suffix)
