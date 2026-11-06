"""Build API/data harnesses from production sources without launching Unity or importing a Library."""
from pathlib import Path
import re
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = Path(sys.argv[1]).resolve()
UNITY = Path(sys.argv[2])
SCRIPTS = ROOT / 'Assets/DroneSandbox/Scripts'
legacy = HERE.parent / 'FarDetail'

# Reuse the established API wrappers, not a second independent set of engine signatures.
exec(compile((legacy / 'prepare.py').read_text(), str(legacy / 'prepare.py'), 'exec'), {'__file__': str(legacy / 'prepare.py'), '__name__': '__prepare__'})

def write(name, text):
    """Generated wrappers remain outside the tracked tree."""
    (OUT / name).write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())

def method(text, anchor):
    """Extract an unchanged source method, including local functions."""
    a = text.index(anchor)
    b = text.index('{', a) + 1
    depth = 1
    while depth:
        depth += (text[b] == '{') - (text[b] == '}')
        b += 1
    return text[a:b]

helpers = ['FarDetailPalette', 'FarDetailPaint', 'FarDetailMaterial', 'FarDetailEdge']
for mode in ['api', 'harness']:
    p = (OUT / (mode + '.csproj')).read_text()
    p = p.replace('</ItemGroup>', ''.join(f'<Compile Include="{SCRIPTS / (n + ".cs")}" />' for n in helpers) + '</ItemGroup>')
    if mode == 'api':
        p = p.replace(f'<Compile Include="{SCRIPTS / "FarScatter.cs"}" />', '')
        p = p.replace(f'<Compile Include="{OUT / "WorldCheck.cs"}" />', '')
        # Compile the setup helper too: all Texture2DArray, editor, GL and GPU-copy calls use real assemblies.
        for dll in ['ImageConversionModule', 'IMGUI', 'InputLegacyModule']:
            path = UNITY / ('UnityEngine.' + dll + '.dll')
            if path.exists():
                p = p.replace('</ItemGroup>', f'<Reference Include="UnityEngine.{dll}"><HintPath>{path}</HintPath></Reference></ItemGroup>')
        editor = UNITY.parent / 'UnityEditor.dll'
        p = p.replace('</ItemGroup>', f'<Reference Include="UnityEngine"><HintPath>{UNITY / "UnityEngine.dll"}</HintPath></Reference></ItemGroup>')
        p = p.replace('</ItemGroup>', f'<Reference Include="UnityEditor"><HintPath>{editor}</HintPath></Reference><Compile Include="{ROOT / "Assets/DroneSandbox/Editor/FarDetailShaderSetup.cs"}" /></ItemGroup>')
    else:
        p = p.replace(str(legacy / 'EngineStubs.cs'), str(OUT / 'EngineStubs.cs'))
        p = p.replace(str(legacy / 'Harness.cs'), str(OUT / 'LegacyHarness.cs'))
        p = p.replace(str(legacy / 'StreamChecks.cs'), str(OUT / 'StreamChecks.cs'))
        p = p.replace('</ItemGroup>', f'<Compile Include="{HERE / "Harness.cs"}" /><Compile Include="{OUT / "CropReference.cs"}" /></ItemGroup>')
        p = p.replace(f'<Compile Include="{OUT / "SectorKind.cs"}" />', '')
        sector_files = ['Sector', 'SectorGenerator', 'SectorIdentity', 'MapLayout', 'PathField', 'FieldParcels', 'WorldSectorData', 'WorldSectors', 'WorldSectors.Geometry', 'WorldSectors.Access']
        p = p.replace('</ItemGroup>', ''.join(f'<Compile Include="{SCRIPTS / (n + ".cs")}" />' for n in sector_files) + f'<Compile Include="{OUT / "GroundReference.cs"}" /><Compile Include="{OUT / "Enums.cs"}" /><Compile Include="{HERE / "SectorReference.cs"}" /></ItemGroup>')
    write(mode + '.csproj', p)

services = (OUT / 'Services.cs').read_text()
services = services.replace('public static class Farmland {', 'public static class Farmland { public static Texture2D CropTexture(Crop c)=>null;')
services += '''
namespace DroneSandbox { public class FarScatter : UnityEngine.MonoBehaviour { public void Clear(){} } }
namespace DroneSandbox.EditorTools {
public static class HdrpSetup { public static UnityEngine.Shader Lit=>null;
public static UnityEngine.Material LoadOrCreate(string p,UnityEngine.Shader s)=>null;
public static void Surface(UnityEngine.Material m,float a,float b){}
public static void Finish(UnityEngine.Material m,bool instancing=true){} }
}
'''
write('Services.cs', services)

stubs = (legacy / 'EngineStubs.cs').read_text()
stubs = stubs.replace('public void SetNormals', 'public void SetTangents(Vector4[] t) {}\n        public void SetNormals')
stubs = stubs.replace('public class Material : Object {', 'public class Material : Object { public Material(){} public Material(Material other){} public void SetTexture(string n,Texture2D t){} public void SetTextureOffset(string n,Vector2 p){}')
stubs = stubs.replace('public struct Color { public static Color white => default; }', '''public struct Color {
public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
public static Color white=>new Color(1,1,1);
public static Color Lerp(Color a,Color b,float t)=>a*(1-Mathf.Clamp01(t))+b*Mathf.Clamp01(t);
public static Color operator*(Color a,float t)=>new Color(a.r*t,a.g*t,a.b*t,a.a*t);
public static Color operator+(Color a,Color b)=>new Color(a.r+b.r,a.g+b.g,a.b+b.b,a.a+b.a);
public static implicit operator Color32(Color c)=>new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r)*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(c.g)*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(c.b)*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(c.a)*255));
public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
}''')
stubs = stubs.replace('public enum TextureWrapMode { Clamp }', '''public enum TextureWrapMode { Clamp, Repeat }
public enum FilterMode { Point, Trilinear }
public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} }
public static class Resources { public static T Load<T>(string name) where T:new()=>new T(); }
public class Terrain { public TerrainData terrainData=new TerrainData(); }
public class TerrainData { public int alphamapResolution=128,alphamapLayers=5;
public float[,,] GetAlphamaps(int x,int y,int w,int h) { var a=new float[h,w,5];for(int j=0;j<h;j++)for(int i=0;i<w;i++)a[j,i,0]=1;return a;} }
''')
stubs = stubs.replace('public string name; public TextureWrapMode', 'public int width,height; public FilterMode filterMode; public string name; public TextureWrapMode')
stubs = stubs.replace('public Texture2D(int w, int h, TextureFormat f, bool mip) { }', '''public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false){width=w;height=h;}
public Color[] GetPixels(int x,int y,int w,int h){var a=new Color[w*h];for(int j=0;j<h;j++)for(int i=0;i<w;i++)a[j*w+i]=pixels[(y+j)*width+x+i];return a;}
public void SetPixels(int x,int y,int w,int h,Color[] a){for(int j=0;j<h;j++)for(int i=0;i<w;i++)pixels[(y+j)*width+x+i]=a[j*w+i];}
''')
ground = (SCRIPTS / 'Ground.cs').read_text()
stubs = stubs.replace('public static class Ground {', 'public static class Ground { public static Terrain Current;\n' + method(ground, 'internal static float[] Lattice(') + '\n' + method(ground, 'internal static float SampleLattice('))
write('EngineStubs.cs', stubs)

# Run Ground's actual height and splat painters with managed array storage. Only engine creation/query code is replaced.
a = ground.index('        public static float HeightAt(')
b = ground.index('        // ---- Heights', a)
bootstrap = '''
        public static float HeightAt(Vector3 p)=>Sample(worldHeights,new Vector2(p.x,p.z));
        public static Terrain Build(Transform parent) {
            MapLayout.Prepare(); sites.Clear(); sites.AddRange(MapLayout.FlatSites);
            if(MapLayout.Plan.world!=null) for(int i=0;i<sites.Count;i++){var site=sites[i];site.center.y=MapLayout.Plan.world.Height(site.center.x,site.center.z);sites[i]=site;}
            IndexSites(); ClearWorn(); FieldPlot.Warp(Vector2.zero);
            BuildHeights();
            lieHeights=BoxAverage(worldHeights,Mathf.RoundToInt(40f/(Size/(HeightRes-1))));
            var data=new TerrainData{alphamapResolution=SplatRes};
            data.SetAlphamaps(0,0,BuildSplat(data)); terrain=new Terrain{terrainData=data}; return terrain;
        }
'''
write('GroundReference.cs', ground[:a] + bootstrap + ground[b:])
a = stubs.index('public static class Ground {')
b = stubs.index('public static class FarTerrain', a)
stubs = stubs[:a] + stubs[b:]
a = stubs.index('public class TerrainData {')
b = stubs.index('    public class Texture2D', a)
stubs = stubs[:a] + '''public class TerrainData {
public int alphamapResolution=2048,alphamapLayers=5; public int alphamapWidth=>alphamapResolution; public int alphamapHeight=>alphamapResolution;
public float[,,] maps;
public void SetAlphamaps(int x,int y,float[,,] a){if(maps==null){maps=a;return;}for(int j=0;j<a.GetLength(0);j++)for(int i=0;i<a.GetLength(1);i++)for(int l=0;l<5;l++)maps[j+y,i+x,l]=a[j,i,l];}
public float[,,] GetAlphamaps(int x,int y,int w,int h){var a=new float[h,w,5];for(int j=0;j<h;j++)for(int i=0;i<w;i++)for(int l=0;l<5;l++)a[j,i,l]=maps[j+y,i+x,l];return a;}
}
public class TerrainLayer { public string name; public Texture2D diffuseTexture,normalMapTexture; public Vector2 tileSize; public Vector4 diffuseRemapMin,diffuseRemapMax; }
''' + stubs[b:]
stubs = stubs.replace('public Texture2D(int w,int h,', 'public Texture2D(){} public Texture2D(int w,int h,')
stubs = stubs.replace('public void Apply(bool mip, bool unreadable)', 'public void SetPixels(Color[] a){} public void Apply(bool mip=true, bool unreadable=false)')
stubs = stubs.replace('public struct Vector4 {', 'public struct Vector4 { public static Vector4 zero=>default; public static Vector4 one=>new Vector4(1,1,1,1); public static explicit operator Vector4(Color c)=>new Vector4(c.r,c.g,c.b,c.a);')
stubs += '\nnamespace DroneSandbox { public static class World { public static int WorldSeed=1200; } }\n'
write('EngineStubs.cs', stubs)
math = (OUT / 'Math.cs').read_text().replace('public static Vector2 zero', 'public static Vector2 Min(Vector2 a,Vector2 b)=>new Vector2(Math.Min(a.x,b.x),Math.Min(a.y,b.y)); public static Vector2 Max(Vector2 a,Vector2 b)=>new Vector2(Math.Max(a.x,b.x),Math.Max(a.y,b.y)); public static Vector2 zero')
math = math.replace('public static float Abs(', 'public static float Pow(float a,float b)=>(float)Math.Pow(a,b); public static float Abs(')
write('Math.cs', math)

picture = (OUT / 'FarPicture.cs').read_text()
picture = re.sub(r'public static Color32 ExactField[^\n]+', 'public static Color32 ExactField(float x,float z)=>WorldPlan.CaptureDetail(null).Field(x,z);', picture)
write('FarPicture.cs', picture)
old = (legacy / 'Harness.cs').read_text().replace('static int Main(', 'static int LegacyMain(')
for name in ['Coarse', 'Rough', 'CheckRays', 'CheckClip', 'InspectTile']:
    old = re.sub(r'(    )static ([^\n]+\b' + name + r'\()', r'\1public static \2', old)
# The old texture-comparison loops assumed 256 samples; adapt only its sampler and PPM writer.
a = old.index('    static Vector2[] CoverPositions(')
b = old.index('    // Index validity', a)
old = old[:a] + '    static Vector2[] CoverPositions(int x,int z)=>GroundHarness.Cover(x,z);\n\n' + old[b:]
write('LegacyHarness.cs', old)
stream = (legacy / 'StreamChecks.cs').read_text()
stream = stream.replace('static class StreamChecks\n{', 'static class StreamChecks\n{\n    static long maximumChunkBytes;')
stream = stream.replace('            maximumLive = maximum,', '            maximumLive = maximum,\n            maximumClippedChunkPayloadBytes = maximumChunkBytes,')
stream = stream.replace('        maximum = Math.Max(maximum, FarDetail.Live); frames++;', '''
        long payload=0;
        foreach(var c in FarDetailChunks.All) foreach(var mesh in new[]{c.front,c.back})
            if(mesh!=null) payload+=(mesh.vertices?.Length??0)*32L+(mesh.triangles?.Length??0)*4L;
        maximumChunkBytes=Math.Max(maximumChunkBytes,payload);
        maximum = Math.Max(maximum, FarDetail.Live); frames++;
''')
write('StreamChecks.cs', stream)

farmland = (SCRIPTS / 'Farmland.cs').read_text()
reference = '''using System; using UnityEngine; namespace DroneSandbox {
public static class CropReference {
const float Headland=9f,TramSpacing=24f;
public static Color32 Pixel(Crop c,float x,float y,float a,float b,float d)=>CropColor(c,x,y,a,b,d);
public static float Detail(Crop c,float x,float y,float w,float l,Vector2 noise)=>Brightness(c,x,y,w,l,17f,0f,noise);
public static float MapValue(FieldPlot f,float across,float along) {
float seed=Mathf.Repeat(f.center.x*12.9898f+f.center.y*78.233f,100f), offset=Mathf.Repeat(f.center.x*3.7f+f.center.y*1.3f,24f);
Vector2 Noise(float x,float y)=>new Vector2(Mathf.PerlinNoise(x*.03f+seed,y*.03f+seed*.5f)*.65f+Mathf.PerlinNoise(x*.11f+seed*2,y*.11f+7f)*.35f, f.crop==Crop.Wheat?Mathf.PerlinNoise(x*.09f+seed*3f,y*.09f+40f):0f);
float x0=Mathf.Floor(across/2f)*2f,y0=Mathf.Floor(along/2f)*2f;
Vector2 noise=Vector2.Lerp(Vector2.Lerp(Noise(x0,y0),Noise(x0+2,y0),(across-x0)/2f),Vector2.Lerp(Noise(x0,y0+2),Noise(x0+2,y0+2),(across-x0)/2f),(along-y0)/2f);
return Mathf.RoundToInt(DetailValue(Brightness(f.crop,across,along,2*f.halfWidth,2*f.halfLength,seed,offset,noise))*255)/255f;
}
'''
reference += method(farmland, 'static Color32 CropColor(') + '\n' + method(farmland, 'static float Brightness(') + '\n' + method(farmland, 'static float DetailValue(') + '\n} }'
write('CropReference.cs', reference)

# Generate the raster adapter from the exact installed HDRP sources, matching the editor setup operation.
packages = UNITY.parents[1] / 'Resources/PackageManager/BuiltInPackages'
lit = packages / 'com.unity.render-pipelines.high-definition/Runtime/Material/Lit'
shaders = ROOT / 'Assets/DroneSandbox/Resources/Shaders'
shader = (lit / 'Lit.shader').read_text()
data = (lit / 'LitData.hlsl').read_text()
call = 'float alpha = GetSurfaceData(input, layerTexCoord, surfaceData, normalTS, bentNormalTS);'
assert call in data
data = data.replace('void GetSurfaceAndBuiltinData(', '#include "FarDetailSurface.hlsl"\n\nvoid GetSurfaceAndBuiltinData(')
data = data.replace(call, call + '\n#if defined(VARYINGS_NEED_TEXCOORD0) && defined(VARYINGS_NEED_TANGENT_TO_WORLD)\n    FarDetailSurface(input, surfaceData, normalTS);\n#endif')
second = shader.index('    SubShader', shader.index('    SubShader') + 1)
shader = shader[:second] + '    CustomEditor "Rendering.HighDefinition.LitGUI"\n}\n'
shader = shader.replace('Shader "HDRP/Lit"', 'Shader "DroneSandbox/GroundDetail"')
pos = shader.index('{', shader.index('Properties')) + 1
shader = shader[:pos] + '\n        _GroundControl("Surface and direction", 2D) = "black" {}\n        _GroundAlbedo("Shared ground albedo", 2DArray) = "" {}\n        _GroundNormal("Shared ground normals", 2DArray) = "" {}\n' + shader[pos:]
shader = shader.replace('Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl', 'Assets/DroneSandbox/Resources/Shaders/FarDetailLitData.hlsl')
notice = '// Generated from the installed HDRP 17.6 Lit by FarDetailShaderSetup. Unity source: see ThirdPartyNotices.txt.\n'
for name, content in [('FarDetailLit.shader', shader), ('FarDetailLitData.hlsl', data)]:
    (shaders / name).write_bytes((notice + content).replace('\r\n', '\n').replace('\n', '\r\n').encode())
palette = (SCRIPTS / 'FarDetailPalette.cs').read_text().split('public static readonly Vector3[] Means = {')[1].split('};')[0]
means = re.findall(r'new Vector3\(([^)]+)\)', palette)
text = '// Generated from FarDetailPalette; linear means, in the same order as the shared texture arrays.\nstatic const float GroundMetres[10] = {7,6,5,12,8,8,8,8,8,8};\nstatic const float3 GroundMean[10] = {\n'
text += '\n'.join('float3(' + m.replace('f', '') + '),' for m in means) + '\n};\n'
(shaders / 'FarDetailPalette.hlsl').write_bytes(text.replace('\n', '\r\n').encode())
(shaders / 'ThirdPartyNotices.txt').write_bytes((packages / 'com.unity.render-pipelines.high-definition/LICENSE.md').read_bytes())
