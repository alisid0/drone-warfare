"""Compile real production sources against Unity, and a separate managed data harness without starting the editor."""
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = Path(sys.argv[1]).resolve()
UNITY = Path(sys.argv[2])
PHASE = sys.argv[3]
base = HERE.parent / 'GroundDetail' / 'prepare.py'
# Reuse wrappers only. The historical script's shader generator predates the integrated HDRP name fix.
source = base.read_text().split('# Generate the raster adapter')[0]
exec(compile(source, str(base), 'exec'), {'__file__': str(base), '__name__': '__prepare__'})

def write(path, text):
    """Generated projects and wrappers always stay under Temp."""
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())

scripts = ROOT / 'Assets/DroneSandbox/Scripts'
extras = list(scripts.glob('FarField*.cs')) + [scripts / (name + '.cs') for name in ('FarDetailCover', 'FarDetailRegionData', 'FarDetailRegional')]
for mode in ('api', 'harness'):
    path = OUT / (mode + '.csproj')
    text = path.read_text()
    for extra in extras:
        if PHASE == 'before' and extra.name in ('FarDetailCover.cs', 'FarDetailRegionData.cs', 'FarDetailRegional.cs'):
            continue
        text = text.replace('</ItemGroup>', f'<Compile Include="{extra}" /></ItemGroup>')
    if mode == 'harness':
        text = text.replace('</PropertyGroup>', '<StartupObject>RegionalHarness</StartupObject>' + ('<DefineConstants>BEFORE</DefineConstants>' if PHASE == 'before' else '') + '</PropertyGroup>')
        text = text.replace('</ItemGroup>', f'<Compile Include="{HERE / "Harness.cs"}" /></ItemGroup>')
        text = text.replace('</ItemGroup>', f'<Compile Include="{scripts / "ForestPattern.cs"}" /></ItemGroup>')
    if PHASE == 'before':
        # Freeze every production input at the actual integrated base, without changing the active branch.
        for path_string in re.findall(r'<Compile Include="([^"]+)"', text):
            src = Path(path_string)
            if scripts not in src.parents:
                continue
            dest = OUT / (src.stem + '.before.cs')
            data = subprocess.check_output(['git', '-c', 'safe.directory=' + str(ROOT), 'show', 'f5a5877:' + src.relative_to(ROOT).as_posix()], cwd=ROOT)
            dest.write_bytes(data)
            text = text.replace(str(src), str(dest))
    write(OUT / (mode + '.csproj'), text)

stub = OUT / 'EngineStubs.cs'
text = stub.read_text().replace('public struct Color {', 'public struct Color { public Color linear=>new Color(DroneSandbox.FarDetailPalette.Linear(r),DroneSandbox.FarDetailPalette.Linear(g),DroneSandbox.FarDetailPalette.Linear(b),a);')
text = text.replace('public enum TextureFormat { RGBA32 }', 'public enum TextureFormat { RGBA32, DXT1 }')
text = text.replace('public Texture2D(){}', '''public Texture2D(int w,int h,TextureFormat f,int m,bool l){width=w;height=h;}
public Color GetPixelBilinear(float u,float v)=>new Color(.1f,.1f,.1f);
public Texture2D(){}''')
text = text.replace('public class Material : Object {', 'public class Material : Object { public void EnableKeyword(string s){}')
text = text.replace('pixels = (Color32[])(object)p.Clone();', 'if (typeof(T)==typeof(Color32)) pixels = (Color32[])(object)p.Clone();')
text += '''
namespace UnityEngine {
public class Texture2DArray : Object { public string name; public TextureWrapMode wrapMode; public FilterMode filterMode; public int anisoLevel;
public Texture2DArray(int w,int h,int d,TextureFormat f,int m,bool l){} public void Apply(bool a,bool b){} }
public static class Shader { public static void SetGlobalFloat(string n,float v){} public static void SetGlobalVector(string n,Vector4 v){}
public static void SetGlobalVectorArray(string n,Vector4[] v){} public static void SetGlobalTexture(string n,Object v){} }
public static class SystemInfo { public static bool supports2DArrayTextures=>true; public static bool SupportsTextureFormat(TextureFormat f)=>true;
public static UnityEngine.Rendering.CopyTextureSupport copyTextureSupport=>UnityEngine.Rendering.CopyTextureSupport.DifferentTypes; }
public static class Graphics { public static void CopyTexture(Object a,int b,int c,Object d,int e,int f){} }
}
namespace UnityEngine.Rendering { [System.Flags] public enum CopyTextureSupport { DifferentTypes=1 } }
namespace DroneSandbox { public static class ForestPalette { public static readonly float[] RadiusPerHeight={.2f,.2f,.2f,.2f}; } }
'''
write(stub, text)
services = OUT / 'Services.cs'
write(services, services.read_text().replace('public static class Undergrowth\n    {', 'public static class Undergrowth\n    { public static readonly System.Collections.Generic.List<UnityEngine.Vector2> Trees = new System.Collections.Generic.List<UnityEngine.Vector2>();'))
write(stub, stub.read_text().replace('public static class Debug {', 'public static class Debug { public static void LogWarning(string s){}')
    .replace('public static class FarTerrain\n    {', 'public static class FarTerrain\n    { public static Texture2D WorldTexture=>null;'))
math = OUT / 'Math.cs'
write(math, math.read_text().replace('public const float PI', 'public static float Exp(float x)=>(float)System.Math.Exp(x); public const float PI'))
if PHASE == 'after':
    project = OUT / 'harness.csproj'
    write(project, project.read_text().replace('</ItemGroup>', f'<Compile Include="{HERE / "Streaming.cs"}" /></ItemGroup>'))
