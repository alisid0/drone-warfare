"""Compare cb49269 and the current production village/layout code with the same bounded managed storage."""
from pathlib import Path
import os
import subprocess
import xml.etree.ElementTree as ET

HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[2]
DOTNET=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
source=ROOT/'Temp/lived-in/build'
original=[Path(n.attrib['Include']) for n in ET.parse(source/'harness.csproj').iter('Compile')]
env=dict(os.environ,DOTNET_PROCESSOR_COUNT='2',DOTNET_TieredCompilation='0',MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
for phase in ('before','after'):
    out=ROOT/'Temp/lived-in'/('benchmark-'+phase);out.mkdir(parents=True,exist_ok=True)
    files=[]
    for p in original:
        if p.name=='Harness.cs':continue
        if phase=='before' and ROOT/'Assets/DroneSandbox/Scripts' in p.parents:
            rel=p.relative_to(ROOT).as_posix()
            result=subprocess.run(['git','-c','safe.directory='+ROOT.as_posix(),'show','cb49269:'+rel],cwd=ROOT,capture_output=True)
            if result.returncode:continue
            target=out/p.name;target.write_bytes(result.stdout);files.append(target)
        else:files.append(p)
    files.append(HERE/'Benchmark.cs')
    includes=''.join(f'<Compile Include="{p}" />' for p in files)
    (out/'benchmark.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>')
    env['DOTNET_CLI_HOME']=str(out/'dotnet-home')
    subprocess.run([str(DOTNET),'build',str(out/'benchmark.csproj'),'-c','Release','--nologo','-m:1','--configfile',str(source/'NuGet.Config')],env=env,check=True)
    logs=[]
    for scene in ('TownOutskirts','RiverValley'):
        r=subprocess.run([str(DOTNET),str(out/'bin/Release/net8.0/benchmark.dll'),scene],env=env,check=True,capture_output=True,text=True)
        logs.append(r.stdout);print(phase,r.stdout,flush=True)
    (HERE/'Review'/('benchmark-'+phase+'.log')).write_bytes(''.join(logs).replace('\r\n','\n').replace('\n','\r\n').encode())
