"""Reuse the production ground harness and freeze an e45f400 comparison without changing branches."""
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = Path(sys.argv[1]).resolve()
UNITY = Path(sys.argv[2])
PHASE = sys.argv[3]
base = HERE.parent / 'GroundDetail' / 'prepare.py'
exec(compile(base.read_text(), str(base), 'exec'), {'__file__': str(base), '__name__': '__prepare__'})
stub = OUT / 'EngineStubs.cs'
text = stub.read_text().replace('public struct Color {', 'public struct Color { public Color linear=>new Color(DroneSandbox.FarDetailPalette.Linear(r),DroneSandbox.FarDetailPalette.Linear(g),DroneSandbox.FarDetailPalette.Linear(b),a);')
text = text.replace('public class Texture2D :', 'public class Texture2D :')
text = text.replace('public Texture2D(){}', '''public Color GetPixelBilinear(float u,float v){
float x=Mathf.Clamp(u*width-.5f,0,width-1),y=Mathf.Clamp(v*height-.5f,0,height-1);int i=(int)x,j=(int)y,i1=Math.Min(i+1,width-1),j1=Math.Min(j+1,height-1);
return Color.Lerp(Color.Lerp(pixels[j*width+i],pixels[j*width+i1],x-i),Color.Lerp(pixels[j1*width+i],pixels[j1*width+i1],x-i),y-j);}
public Texture2D(){}''')
stub.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())
scripts = ROOT / 'Assets/DroneSandbox/Scripts'
for mode in ('api', 'harness'):
    p = OUT / (mode + '.csproj')
    text = p.read_text()
    if PHASE == 'after':
        for source in scripts.glob('FarField*.cs'):
            text = text.replace('</ItemGroup>', f'<Compile Include="{source}" /></ItemGroup>')
    if mode == 'harness':
        text = text.replace('</PropertyGroup>', '<StartupObject>TopologyHarness</StartupObject>' + ('<DefineConstants>BEFORE</DefineConstants>' if PHASE == 'before' else '') + '</PropertyGroup>')
        text = text.replace('</ItemGroup>', f'<Compile Include="{HERE / "Harness.cs"}" /></ItemGroup>')
        text = text.replace('</ItemGroup>', f'<Compile Include="{HERE / "TopologyChecks.cs"}" /></ItemGroup>')
    if PHASE == 'before':
        # Every changed production source comes from the requested baseline, including later reruns.
        for name in ('FarDetailData', 'FarDetailPaint', 'FarDetailEdge', 'MapLayout', 'FieldParcels'):
            dest = OUT / (name + '.before.cs')
            source = subprocess.check_output(['git', '-c', 'safe.directory=' + str(ROOT), 'show', 'e45f400:Assets/DroneSandbox/Scripts/' + name + '.cs'], cwd=ROOT)
            dest.write_bytes(source)
            text = text.replace(str(scripts / (name + '.cs')), str(dest))
    p.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())

# The reused shader setup copies its notice with source-package LF endings; keep the repository byte convention.
notice = ROOT / 'Assets/DroneSandbox/Resources/Shaders/ThirdPartyNotices.txt'
notice.write_bytes(notice.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))

if PHASE == 'after':
    # Compile Claude's exact proposed Farmland edit in Temp, never in the read-only production file.
    patch_script = HERE / 'handoff_patch.py'
    scope = {'__file__': str(patch_script)}
    exec(compile(patch_script.read_text(), str(patch_script), 'exec'), scope)
    (OUT / 'PatchedFarmland.cs').write_bytes(scope['after'].replace('\n', '\r\n').encode())
    services = (OUT / 'Services.cs').read_text()
    services = services.replace('    public static class Farmland { public static Texture2D CropTexture(Crop c)=>null; public static void Build(Transform t, System.Random r) { } }', '')
    services = services.replace('public static class Art\n    {', 'public static class Art\n    {\n        public static Material Shared(Color c, bool glow = false) => null;')
    (OUT / 'FarmlandServices.cs').write_bytes(services.replace('\n', '\r\n').encode())
    project = (OUT / 'api.csproj').read_text().replace(str(OUT / 'Services.cs'), str(OUT / 'FarmlandServices.cs'))
    project = project.replace('</ItemGroup>', f'<Compile Include="{OUT / "PatchedFarmland.cs"}" /></ItemGroup>')
    (OUT / 'farmland-api.csproj').write_bytes(project.replace('\n', '\r\n').encode())
