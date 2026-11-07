"""Compile the forest and terrain integration against installed Unity assemblies, without starting Unity."""
from pathlib import Path

root = Path(__file__).resolve().parents[2]
script = root / 'Tools/DistantForest/prepare.py'
source = script.read_text().replace("temp = root / 'Temp/distant-forest'", "temp = root / 'Temp/forest-polish'")
source = source.replace("refs = ''.join", "files += [scripts / (n + '.cs') for n in ['VillagePlan', 'VillageLayout', 'VillageScenery', 'WorldLandmarkLayout.Yards', 'LandmarkClutter', 'FarDetailMips']]\nrefs = ''.join")
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
