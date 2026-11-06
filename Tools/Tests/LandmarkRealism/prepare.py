"""Build an isolated base/current harness and real Unity API check without importing a Unity project."""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
out = ROOT / 'Temp/landmark-realism' / phase
out.mkdir(parents=True, exist_ok=True)
legacy = ROOT / 'Tools/WorldLandmarks/prepare.py'
source = legacy.read_text().split("for mode in (")[0]
source = source.replace("root / 'Temp/world-landmarks'", repr(out.as_posix()))
source = source.replace("temp = ", "temp = Path(", 1).replace("\ntemp.mkdir", ")\ntemp.mkdir", 1)
context = {'__file__': str(legacy), '__name__': '__prepare__'}
exec(compile(source, str(legacy), 'exec'), context)
files, scripts, unity, dotnet = [context[k] for k in ('files', 'scripts', 'unity', 'dotnet')]


def write(path, text):
    """Normalize generated and review text without touching source inputs."""
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


files += list(scripts.glob('FarField*.cs'))
files += [scripts / (n + '.cs') for n in ('FarDetailData', 'FarDetailPaint', 'FarDetailCover', 'FarDetailPalette')]
if phase != 'before': files += [scripts / 'WorldLandmarkLayout.Yards.cs']
services = (out / 'Services.cs').read_text()
services = services.replace('public static class FarTerrain\n    {', 'public static class FarTerrain\n    { public static Vector2 Origin => Vector2.zero;')
services = services.replace('public static class Undergrowth\n    {', 'public static class Undergrowth\n    { public static readonly List<Vector2> Trees = new List<Vector2>();')
write(out / 'Services.cs', services)
engine = (out / 'Engine.cs').read_text()
engine = engine.replace('public struct Vector4 {', 'public struct Vector4 { public Vector4(float x,float y,float z,float w){}')
engine = engine.replace('public static implicit operator Color32(Color c)=>new Color32();', 'public static implicit operator Color32(Color c)=>new Color32((byte)Math.Round(Math.Max(0,Math.Min(1,c.r))*255),(byte)Math.Round(Math.Max(0,Math.Min(1,c.g))*255),(byte)Math.Round(Math.Max(0,Math.Min(1,c.b))*255),255);')
write(out / 'Engine.cs', engine)
harness = (ROOT / 'Tools/WorldLandmarks/Harness.cs').read_text()
harness = harness.replace('Temp/world-landmarks/results', (out / 'results').as_posix())
harness = harness.replace('Directory.CreateDirectory(Output);', 'Directory.CreateDirectory(Output); if (args.Length > 2 && args[2] == "benchmark") { PictureBench.Run(Output); return 0; }')
harness = harness.replace('Campaign(seed, render);', 'Campaign(seed, false);')
harness = harness.replace('            Build(SectorPlan.Varrow());', '')
harness = harness.replace('7400 + f.id); Build(p);', '7400 + f.id); var buildTimer = Stopwatch.StartNew(); Build(p); double buildMs = buildTimer.Elapsed.TotalMilliseconds;')
harness = harness.replace('var surfaces = LandmarkMesh.Build', 'var meshTimer = Stopwatch.StartNew(); var surfaces = LandmarkMesh.Build')
harness = harness.replace('var far = LandmarkMesh.Build(f, f.center, false, false);', 'var far = LandmarkMesh.Build(f, f.center, false, false); double meshMs = meshTimer.Elapsed.TotalMilliseconds;')
harness = harness.replace('Save(f.kind + ".json", new {', 'Save(f.kind + ".json", new { buildMs, meshMs,')
harness = harness.replace('int m = 0; m < 5; m++', 'int m = 0; m < LandmarkMesh.MaterialCount; m++')
harness = harness.replace('var t = data.triangles[m]; Vector3', 'if (m >= 5 && m < 9) continue; var t = data.triangles[m]; Vector3')
harness = harness.replace('        Campaign(seed, false);', '        YardChecks.Run();\n        Campaign(seed, false);') if phase != 'before' else harness
if phase != 'before':
    harness = harness.replace('scale = XYZ(item.mesh.transform.localScale) });', 'scale = XYZ(item.mesh.transform.localScale), nearOnly = f.buildings.Exists(b => b.detail && b.model == item.model && Vector2.Distance(b.position - f.center, new Vector2(item.root.position.x, item.root.position.z)) < .01f) });')
    harness = harness.replace('var surfaces = LandmarkMesh.Build', '''foreach (var prop in f.props) {
            Vector2 point = prop.position - f.center;
            placements.Add(new { model = prop.model, position = new[] { point.x, f.elevation, point.y }, facing = new[] { prop.facing.x, 0f, prop.facing.y }, scale = XYZ(prop.scale), clutter = true });
        }
        var surfaces = LandmarkMesh.Build''')
    harness = harness.replace('Save(f.kind + ".json", new {', '''var fences = new List<object>(); foreach (var fence in f.fences) { var line = new List<float[]>(); foreach (var q in fence) line.Add(XY(q - f.center)); fences.Add(line); }
        var fenceMeshes = new List<object>(); foreach (var mesh in Mesh.All) if (mesh.name == "Compound fence") {
            var vertices = new List<float[]>(); foreach (var vertex in mesh.vertices) vertices.Add(XYZ(vertex));
            fenceMeshes.Add(new { vertices, triangles = new[] { mesh.triangles }, colors = new[] { new[] { .36f, .38f, .37f } } });
        }
        var props = new List<object>(); foreach (var prop in f.props) props.Add(new { prop.model, prop.group });
        Save(f.kind + ".json", new { props, fences, fenceMeshes,''')
    harness = harness.replace('foreach (var s in f.surfaces) text.Append', '''foreach (var prop in f.props) text.Append(JsonSerializer.Serialize(new { prop.model, at = XY(prop.position), facing = XY(prop.facing), scale = XYZ(prop.scale), prop.group }));
            foreach (var fence in f.fences) text.Append(JsonSerializer.Serialize(Line(fence)));
            foreach (var s in f.surfaces) if (s.outline != null) text.Append(JsonSerializer.Serialize(Line(s.outline)));
            foreach (var s in f.surfaces) text.Append''')
write(out / 'Harness.cs', harness)
env = dict(os.environ, DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false')

for mode in (('harness',) if '--harness-only' in sys.argv else ('api', 'harness')):
    sources = list(files)
    if mode == 'api' and phase != 'before': sources += [scripts / 'LandmarkClutter.cs', scripts / 'LandmarkHlod.cs']
    if mode == 'harness':
        sources += [out / 'ConsumerMath.cs', out / 'Engine.cs', out / 'Harness.cs', HERE / 'PictureBench.cs']
        if phase != 'before':
            write(out / 'ClutterStub.cs', 'namespace DroneSandbox { public static class LandmarkClutter { public static void Build(UnityEngine.Transform t) {} public static void Tune(UnityEngine.GameObject p,Site s) {} } }')
            sources += [out / 'ClutterStub.cs', HERE / 'YardChecks.cs']
    if phase == 'before':
        for i, path in enumerate(sources):
            if scripts not in path.parents: continue
            dest = out / (path.stem + '.before.cs')
            dest.write_bytes(subprocess.check_output(['git', '-c', 'safe.directory=' + ROOT.as_posix(), 'show', '6a1ed17:' + path.relative_to(ROOT).as_posix()], cwd=ROOT))
            sources[i] = dest
    refs = ''
    if mode == 'api':
        for name in ('CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'):
            refs += f'<Reference Include="UnityEngine.{name}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine." + name + ".dll")}</HintPath></Reference>'
    include = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{'Exe' if mode == 'harness' else 'Library'}</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>{'<StartupObject>Harness</StartupObject>' if mode == 'harness' else ''}</PropertyGroup><ItemGroup>{include}{refs}</ItemGroup></Project>''')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
