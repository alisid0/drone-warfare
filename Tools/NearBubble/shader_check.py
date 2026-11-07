"""Compile FarDetailSurface.hlsl, before and after, with the D3D11 shader compiler Unity ships (D3DCompiler_47, ps_5_0).

CPU only: no device is created. HDRP's FragInputs/SurfaceData are reduced to the fields the hook reads; the texture
macros are Core RP's own D3D11.hlsl from the installed editor. Both variants (streamed tile, _FarRegionChunk) are
compiled with Unity's optimisation level and their instruction counts reported from the compiler's disassembly.
  py -3.13 -I Tools/NearBubble/shader_check.py [before-revision]
"""
from pathlib import Path
import ctypes
import re
import subprocess
import sys
from ctypes import wintypes

ROOT = Path(__file__).resolve().parents[2]
EDITOR = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data')
CORE = EDITOR / 'Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.core/ShaderLibrary/API/D3D11.hlsl'
SHADERS = 'Assets/DroneSandbox/Resources/Shaders/'
OUT = ROOT / 'Temp/near-bubble/shader-check'
STUB = '''#define SHADER_API_D3D11 1
#include "{core}"
struct FragInputs {{ float4 texCoord0; float3 positionRWS; float3x3 tangentToWorld; }};
struct SurfaceData {{ float3 baseColor; float perceptualSmoothness; }};
TEXTURE2D(_BaseColorMap); SAMPLER(sampler_BaseColorMap); float4 _BaseColorMap_ST; float3 _WorldSpaceCameraPos;
float3 GetAbsolutePositionWS(float3 p) {{ return p + _WorldSpaceCameraPos; }}
#include "{surface}"
float4 main(float4 uv : TEXCOORD0, float3 p : TEXCOORD1, float3 t : TEXCOORD2, float3 b : TEXCOORD3, float3 n : TEXCOORD4) : SV_Target
{{
    FragInputs input; input.texCoord0 = uv; input.positionRWS = p; input.tangentToWorld = float3x3(t, b, n);
    SurfaceData s; s.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv.xy).rgb; s.perceptualSmoothness = 0.5;
    float3 gradient = 0;
    FarDetailSurface(input, s, gradient);
    return float4(s.baseColor + gradient, s.perceptualSmoothness);
}}
'''


class Macro(ctypes.Structure):
    _fields_ = [('Name', ctypes.c_char_p), ('Definition', ctypes.c_char_p)]


d3d = ctypes.WinDLL(str(EDITOR / 'Tools/D3DCompiler_47.dll'))
compile_file = d3d.D3DCompileFromFile
compile_file.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(Macro), ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                         wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
compile_file.restype = ctypes.c_long
disassemble = d3d.D3DDisassemble
disassemble.argtypes = [ctypes.c_void_p, ctypes.c_size_t, wintypes.UINT, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
disassemble.restype = ctypes.c_long


def blob(pointer):
    """The bytes of an ID3DBlob, through its vtable (GetBufferPointer, GetBufferSize), then Release."""
    if not pointer:
        return b''
    table = ctypes.cast(ctypes.cast(pointer, ctypes.POINTER(ctypes.c_void_p))[0], ctypes.POINTER(ctypes.c_void_p))
    data = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(table[3])(pointer)
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(table[4])(pointer)
    result = ctypes.string_at(data, size)
    ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(table[2])(pointer)
    return result


def build(folder, chunk):
    """Compile one variant; returns (ok, messages, instruction counts)."""
    source = folder / ('check-chunk.hlsl' if chunk else 'check-tile.hlsl')
    source.write_text(STUB.format(core=CORE.as_posix(), surface=(folder / 'FarDetailSurface.hlsl').as_posix()))
    macros = (Macro * 2)(Macro(b'_FarRegionChunk', b'1'), Macro(None, None)) if chunk else (Macro * 1)(Macro(None, None))
    code, errors = ctypes.c_void_p(), ctypes.c_void_p()
    flags = 1 << 15  # D3DCOMPILE_OPTIMIZATION_LEVEL3, as Unity compiles
    hr = compile_file(str(source), macros, ctypes.c_void_p(1), b'main', b'ps_5_0', flags, 0, ctypes.byref(code), ctypes.byref(errors))
    messages = blob(errors.value).decode(errors='replace').strip()
    counts = {}
    if hr >= 0:
        binary = blob(code.value)
        text = ctypes.c_void_p()
        disassemble(binary, len(binary), 0, None, ctypes.byref(text))
        listing = blob(text.value).decode(errors='replace')
        (folder / (source.stem + '.asm')).write_text(listing)
        slots = re.search(r'Approximately (\d+) instruction slots used', listing)
        counts = {'slots': int(slots.group(1)) if slots else None,
                  'sample': len(re.findall(r'^\s*sample', listing, re.M)), 'load': len(re.findall(r'^\s*ld', listing, re.M))}
    return hr >= 0, messages, counts


def prepare(name, revision):
    """The hook and its includes from the working tree, or from a revision."""
    folder = OUT / name
    folder.mkdir(parents=True, exist_ok=True)
    for file in ('FarDetailSurface.hlsl', 'FarDetailRegional.hlsl', 'FarDetailPalette.hlsl'):
        path = SHADERS + file
        text = (ROOT / path).read_bytes() if revision is None else subprocess.check_output(['git', 'show', revision + ':' + path], cwd=ROOT)
        (folder / file).write_bytes(text)
    return folder


failed = False
for name, revision in (('before', sys.argv[1] if len(sys.argv) > 1 else 'ed6a3eb'), ('after', None)):
    folder = prepare(name, revision)
    for chunk in (False, True):
        ok, messages, counts = build(folder, chunk)
        print(f'{name} {"chunk" if chunk else "tile"}: {"compiled" if ok else "FAILED"} {counts}')
        if messages:
            print('  ' + messages.replace('\n', '\n  '))
        failed |= not ok
raise SystemExit(1 if failed else 0)
