"""Write Claude's exact terrain hook patch without modifying the other session's terrain files."""
from pathlib import Path
import difflib
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[2]
changes=[]
patched={}


def changed(name, replacements):
    """Assert context before producing a reviewable unified diff against the current integration base."""
    path=root/'Assets/DroneSandbox/Scripts'/name
    before=path.read_text();after=before
    for old,new in replacements:
        assert old in after, (name,old)
        after=after.replace(old,new,1)
    rel=path.relative_to(root).as_posix()
    changes.extend(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/'+rel,tofile='b/'+rel,n=1))
    patched[name]=after


changed('FarTerrain.cs',[
    ('            worldTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)', '''            // Paint permanent runways, aprons, yards and siding ballast after the ordinary road strokes.
            System.Threading.Tasks.Parallel.For(0, TextureSize, y =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float wx = -WorldPlan.Half + (x + 0.5f) * pixel, wz = -WorldPlan.Half + (y + 0.5f) * pixel;
                    if (WorldPlan.FeatureSurfaceAt(wx, wz, out Color feature)) pixels[y * TextureSize + x] = feature;
                }
            });
            worldTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)'''),
    ('        public static Color CanopyGroundColour(float x, float z)\n        {', '''        public static Color CanopyGroundColour(float x, float z)
        {
            if (WorldPlan.FeatureSurfaceAt(x, z, out Color feature)) return feature.linear;''')])
changed('FarDetailData.cs',[
    ('        readonly Cover[] covers;', '        readonly Cover[] covers;\n        readonly WorldFeatureMap features;'),
    ('            this.seed = seed; this.heights = heights; this.covers = covers; this.count = count;', '''            this.seed = seed; this.heights = heights; this.covers = covers; this.count = count;
            features = WorldPlan.CaptureFeatureMap(); // immutable worker-owned surfaces and grade reservations'''),
    ('            return Mix(CoarseHeight(x, z), Height(x, z) + roughness, Smooth(edge / 128f));', '''            float detail = Mix(CoarseHeight(x, z), Height(x, z) + roughness, Smooth(edge / 128f));
            return Mix(Height(x, z), detail, features.DetailWeight(x, z));'''),
    ('        public Color32 Paint(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null)\n        {', '''        public Color32 Paint(float x, float z, Vector2 coverPosition, Segment[] lanes = null, Segment[] streams = null)
        {
            if (features.SurfaceAt(x, z, out Color feature)) return feature; // unwarped world metres, sRGB''')])
(Path(__file__).parent/'terrain-hooks.patch').write_bytes(''.join(changes).replace('\n','\r\n').encode())

if '--check-api' in sys.argv:
    # Compile temporary copies only; the other session's source files are never changed.
    temp=root/'Temp/world-landmarks'
    copies=temp/'terrain-hooks'
    copies.mkdir(parents=True,exist_ok=True)
    for name,source in patched.items():
        (copies/name).write_bytes(source.replace('\n','\r\n').encode())
    project=ET.parse(temp/'render-api.csproj')
    for item in project.findall('.//Compile'):
        if Path(item.attrib['Include']).name=='FarTerrain.cs':
            item.set('Include',str(copies/'FarTerrain.cs'))
    ET.SubElement(project.find('ItemGroup'),'Compile',Include=str(copies/'FarDetailData.cs'))
    path=temp/'terrain-hooks-api.csproj'
    project.write(path,encoding='utf-8')
    env=dict(os.environ,DOTNET_CLI_HOME=str(temp/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1')
    dotnet='C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe'
    subprocess.run([dotnet,'build',str(path),'-c','Release','--nologo','--configfile',str(temp/'NuGet.Config')],env=env,check=True)
