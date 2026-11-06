"""Bounded C# 9/Unity assembly check and one-scene managed review; never launches Unity."""
from pathlib import Path
import os
import subprocess
import sys
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
out=ROOT/'Temp/lived-in/build'; out.mkdir(parents=True,exist_ok=True)
base=HERE.parent/'Water/prepare.py'
setup=base.read_text().split('# The terrain patch')[0]
setup=setup.replace("out = ROOT / 'Temp/water' / phase", 'out = Path('+repr(str(out))+')')
scope={'__file__':str(base)}
exec(compile(setup,str(base),'exec'),scope)
files,scripts,unity,dotnet,write,body=[scope[k] for k in ('files','scripts','unity','dotnet','write','body')]
files+=list(scripts.glob('Village*.cs'))+[scripts/'FieldAppearance.cs',scripts/'FieldMargins.cs',scripts/'SettlementPattern.cs']
subprocess.run([sys.executable,'-I',str(HERE/'field_patch.py')],check=True)
subprocess.run([sys.executable,'-I',str(HERE/'house_patch.py')],check=True)
files=list(dict.fromkeys(files))
engine=(out/'Engine.cs').read_text().replace('public struct LOD { public LOD(float h,Renderer[] r){} }', 'public struct LOD { public float screenRelativeTransitionHeight; public LOD(float h,Renderer[] r){screenRelativeTransitionHeight=h;} }').replace('public float size=100f;', 'public float size=100f; public LOD[] GetLODs()=>new LOD[0];')
engine=engine.replace('public class Transform : Component', 'public class Transform : Component').replace('public Vector3 position, localScale, up;', 'public Vector3 position, localScale, up; public Vector3 lossyScale=>localScale;')
write(out/'Engine.cs',engine)
text=(scripts/'Ground.cs').read_text()
text=text.replace(body(text,'        static float[,,] BuildSplat('),'        static float[,,] BuildSplat(TerrainData data) => new float[1,1,8];')
text=text.replace(body(text,'        public static void FlushWear('),'        public static void FlushWear() { pending.Clear(); }')
write(out/'Ground.review.cs',text)

env=dict(os.environ,DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_PROCESSOR_COUNT='2',DOTNET_TieredCompilation='0',MSBUILDDISABLENODEREUSE='1')
for mode in ('api','harness'):
 sources=list(files); refs=''
 if mode=='api':
  services=(out/'Services.cs').read_text()
  services=services.replace(body(services,'    public static class Farmland'), '')
  services=services.replace('public void Clear(){} public static bool InSector', 'public const float HouseReach=1500f; public void House(SettlementPattern.House h){} public static float GroundAt(Vector2 p)=>0f; public void Clear(){} public static bool InSector')
  write(out/'ApiServices.cs',services)
  sources=[out/'ApiServices.cs' if p.name=='Services.cs' else p for p in sources]
  sources += [ROOT/'Temp/lived-in/Farmland.patched.cs', ROOT/'Temp/lived-in/patched-SettlementHlod.cs']
  sources += [scripts / n for n in ('LandmarkClutter.cs','LandmarkHlod.cs','FarDetailEdge.cs')]
  for m in ('CoreModule','TerrainModule','TerrainPhysicsModule','PhysicsModule','AnimationModule'):
   refs+=f'<Reference Include="UnityEngine.{m}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine."+m+".dll")}</HintPath><Private>false</Private></Reference>'
 else:
  sources=[out/'Ground.review.cs' if p.name=='Ground.cs' else p for p in sources]
  sources += [out/n for n in ('ConsumerMath.cs','Engine.cs','ClutterStub.cs')]+[HERE/'Harness.cs']
 includes=''.join(f'<Compile Include="{p}" />' for p in sources)
 write(out/(mode+'.csproj'),f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Library" if mode=="api" else "Exe"}</OutputType><TargetFramework>{"netstandard2.1" if mode=="api" else "net8.0"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
 subprocess.run([str(dotnet),'build',str(out/(mode+'.csproj')),'-c','Release','--nologo','-m:1','--configfile',str(out/'NuGet.Config')],env=env,check=True)
