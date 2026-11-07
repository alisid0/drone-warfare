"""Compile the new HLSL helpers with Windows' shader compiler; HDRP pass/variant compilation remains Claude's build check."""
from pathlib import Path
import ctypes as c

ROOT = Path(__file__).resolve().parents[2]
folder = ROOT / 'Assets/DroneSandbox/Resources/Shaders'
compiler = c.WinDLL('d3dcompiler_47.dll')
compile_shader = compiler.D3DCompile
compile_shader.argtypes = [c.c_void_p,c.c_size_t,c.c_char_p,c.c_void_p,c.c_void_p,c.c_char_p,c.c_char_p,c.c_uint,c.c_uint,c.POINTER(c.c_void_p),c.POINTER(c.c_void_p)]
compile_shader.restype = c.c_long


def blob_bytes(blob):
    """Read the compiler-owned blob through its documented ID3DBlob COM methods, without native runtime state."""
    table=c.cast(blob,c.POINTER(c.POINTER(c.c_void_p))).contents
    pointer=c.WINFUNCTYPE(c.c_void_p,c.c_void_p)(table[3])(blob)
    size=c.WINFUNCTYPE(c.c_size_t,c.c_void_p)(table[4])(blob)
    return c.string_at(pointer,size)


prefix = '''Texture2D _ForestDetail, _ForestDetailB, _BaseColorMap;
SamplerState sampler_ForestDetail, sampler_BaseColorMap;
#define SAMPLE_TEXTURE2D_GRAD(t,s,u,x,y) t.SampleGrad(s,u,x,y)
#define SAMPLE_TEXTURE2D_LOD(t,s,u,l) t.SampleLevel(s,u,l)
float4 _ForestCamera,_ForestDistances,_ForestProjection,_ForestSelection,_ForestCrown,_ForestRadius,_ForestViewport;
float _ForestTier,_ForestEnabled,_ForestBuild;
float4x4 GetObjectToWorldMatrix(){return float4x4(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);}
float3 GetAbsolutePositionWS(float3 p){return p;}
'''
source = prefix + (folder/'ForestDetail.hlsl').read_text() + (folder/'ForestFade.hlsl').read_text()
source += '''float4 main(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
ForestFade(p.xyz,uv);return float4(ForestCrownDetail(p.xz),1); }'''
for name, define in [('raster',''),('ray-helper','#define SHADER_STAGE_RAY_TRACING 1\n')]:
    code=(define+source).encode(); compiled=c.c_void_p(); error=c.c_void_p()
    result=compile_shader(code,len(code),b'forest-helpers',None,None,b'main',b'ps_5_0',1<<18,0,c.byref(compiled),c.byref(error))
    messages=blob_bytes(error).decode(errors='replace') if error else ''
    assert result>=0, messages
    print(name+': HLSL helpers compiled; '+str(len(blob_bytes(compiled)))+' byte bytecode')
