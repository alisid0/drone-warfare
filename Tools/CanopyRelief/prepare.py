"""Compile the forest and terrain integration against installed Unity assemblies, without starting Unity."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
subprocess.run([sys.executable, '-I', str(root / 'Tools/CanopyRelief/ground_patch.py')], check=True)
script = root / 'Tools/DistantForest/prepare.py'
source = script.read_text().replace("temp = root / 'Temp/distant-forest'", "temp = root / 'Temp/canopy-relief'")
contract = '''
files += [scripts / (n + '.cs') for n in ['VillagePlan', 'VillageLayout', 'VillageScenery', 'WorldLandmarkLayout.Yards', 'LandmarkClutter', 'FarDetailMips', 'WatercourseProfile', 'WaterAppearance', 'FarDetailWater', 'FarDetailRegionData']]
hdrp = (root / 'Tools/WorldSectors/HdrpStubs.cs').read_text().replace('public WaterSurfaceType surfaceType;', "public enum FadeMode { Custom } public FadeMode ripplesFadeMode; public float ambientScattering, heightScattering, ripplesFadeStart, ripplesFadeDistance, tessellationFactorFadeStart, tessellationFactorFadeRange; public bool caustics; public WaterSurfaceType surfaceType;")
write(temp / 'HdrpContract.cs', hdrp)
files = [temp / 'HdrpContract.cs' if p.name == 'HdrpStubs.cs' else p for p in files]
files = [temp / 'Ground.patched.cs' if p.name == 'Ground.cs' else p for p in files]
'''
source = source.replace("refs = ''.join", contract + "\nrefs = ''.join")
source = source.replace("complete += [scripts / 'FarTerrain.cs']", "complete += [scripts / 'FarTerrain.cs', scripts / 'FarWorldPicture.cs']")
source = source.replace("'AnimationModule']", "'AnimationModule', 'IMGUIModule']")
exec(compile(source, str(script), 'exec'))

# Check the changed decorative LOD policy against real Unity Renderer/LODGroup APIs too.
art_services = remove_class(services, 'public static class Art')
art_services += '''
namespace DroneSandbox { public static class DayNight { public static UnityEngine.Material WindowMaterial => null; } }
namespace UnityEngine.Rendering.HighDefinition { public static class HDMaterial { public static void ValidateMaterial(UnityEngine.Material m) {} } }
'''
write(temp / 'ArtServices.cs', art_services)
art_files = [temp / 'ArtServices.cs' if p.name == 'Services.cs' else p for p in files] + [scripts / 'Art.cs']
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in art_files)
write(temp / 'art-api.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + refs + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'art-api.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
