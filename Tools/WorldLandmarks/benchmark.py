"""Compare base-commit and current generation without touching another checkout or launching Unity."""
from pathlib import Path
import os
import subprocess
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[2]
temp=root/'Temp/world-landmarks'
dotnet=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
project=ET.parse(temp/'harness.csproj')
sources=[Path(item.attrib['Include']) for item in project.findall('.//Compile')]
env=dict(os.environ,DOTNET_CLI_HOME=str(temp/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1')
for phase in ['before','after']:
    folder=temp/('benchmark-'+phase);folder.mkdir(exist_ok=True)
    includes=[]
    for path in sources:
        if path.name=='Harness.cs':
            path=Path(__file__).parent/'Benchmark.cs'
        elif phase=='before' and path.parent==root/'Assets/DroneSandbox/Scripts':
            relative=path.relative_to(root).as_posix()
            result=subprocess.run(['git','-c','safe.directory='+root.as_posix(),'show','3c111af:'+relative],cwd=root,capture_output=True)
            if result.returncode:
                continue
            path=folder/path.name
            path.write_bytes(result.stdout.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n'))
        includes.append('<Compile Include="'+str(path)+'" />')
    path=folder/'benchmark.csproj'
    path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>'+''.join(includes)+'</ItemGroup></Project>')
    subprocess.run([str(dotnet),'build',str(path),'-c','Release','--nologo','--configfile',str(temp/'NuGet.Config')],env=env,check=True)
    subprocess.run([str(dotnet),str(folder/'bin/Release/net8.0/benchmark.dll'),str(temp/'results'/('benchmark-'+phase+'.json'))],env=env,check=True)
