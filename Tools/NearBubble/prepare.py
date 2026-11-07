"""Build the near-bubble review harness: the production painter, near page builder and BC1 encoder as plain C# 9.

Never launches Unity and uses no GPU. The engine and scene services are the managed substitutes the terrain-colour
harness already used (vendored in Scaffold/, with Texture2D keeping SetPixels32 data so Farmland's crop maps can be
read). The photo ground textures are resampled to the shader arrays' 512 square with PIL, as setup's Blit does.
Generated files go under Temp/near-bubble; the build runs on one node.
"""
from pathlib import Path
import os
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SCRIPTS = ROOT / 'Assets/DroneSandbox/Scripts'
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
OUT = ROOT / 'Temp/near-bubble'
NAMES = '''Scale Sector SectorGenerator SectorIdentity PathField MapLayout FieldParcels Ground Water World WorldLandmarkLayout
LandmarkMesh LandmarkCheck ForestPattern ForestPalette Features FeatureScenery Situations Missions WorldPlan.Cover WorldPlan
WorldPlan.Features WorldPlan.Places WorldPlan.Relief WorldPlan.Water WorldSectorData WorldSectors.Access WorldSectors
WorldSectors.Geometry WorldSectors.Hotspots WorldSectors.Landmarks WorldFeatureMap FarFieldPlan FarFieldSector FarFieldSurface
FarDetailData FarDetailPaint FarDetailCover FarDetailPalette WorldLandmarkLayout.Yards WorldLandmarkLayout.Use FarDetailMips
WaterAppearance WatercourseProfile FarDetailWater FarDetailRegionData FarDetailNearData FieldAppearance FieldMargins
SettlementPattern FieldPalette Farmland'''.split()


def textures():
    """The five photo layers at the arrays' 512 square, as raw sRGB bytes."""
    from PIL import Image
    folder = OUT / 'textures'
    folder.mkdir(parents=True, exist_ok=True)
    for name in ('meadow', 'lush', 'dirt', 'rock'):
        image = Image.open(ROOT / f'Assets/DroneSandbox/Resources/Textures/Terrain/{name}_diff.jpg').convert('RGB')
        (folder / (name + '.rgb')).write_bytes(image.resize((512, 512), Image.BILINEAR).tobytes())


def build():
    """One serial Release build of the harness executable."""
    sources = [SCRIPTS / (n + '.cs') for n in NAMES]
    sources += sorted(SCRIPTS.glob('Village*.cs')) + sorted(p for p in SCRIPTS.glob('Road*.cs') if p.name != 'Roadside.cs')
    sources += sorted((HERE / 'Scaffold').glob('*.cs')) + [HERE / 'Review.cs']
    missing = [p for p in sources if not p.exists()]
    assert not missing, missing
    includes = ''.join(f'<Compile Include="{p}" />' for p in dict.fromkeys(sources))
    (OUT / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    (OUT / 'harness.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize>'
        '<TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>'
        '<ServerGarbageCollection>false</ServerGarbageCollection><ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>'
        f'</PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>')
    env = dict(os.environ, DOTNET_CLI_HOME=str(OUT / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1',
               DOTNET_GENERATE_ASPNET_CERTIFICATE='false', MSBUILDDISABLENODEREUSE='1', DOTNET_TieredCompilation='0')
    result = subprocess.run([str(DOTNET), 'build', str(OUT / 'harness.csproj'), '-c', 'Release', '--nologo', '-m:1',
                             '--configfile', str(OUT / 'NuGet.Config')], env=env, capture_output=True, text=True)
    (OUT / 'build.log').write_text(result.stdout + result.stderr)
    errors = list(dict.fromkeys(line.strip() for line in result.stdout.splitlines() if ': error ' in line or ': warning ' in line))
    if result.returncode or errors:
        print('\n'.join(errors[:40]))
        raise SystemExit('harness: compile failed')
    print('harness: compile passed (zero warnings/errors)')


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    textures()
    build()
