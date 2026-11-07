"""Exercise the production upload state machine with a recording mesh API, not a timing simulation."""
from pathlib import Path
import os
import subprocess
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
out=ROOT/'Temp/places-up-close/upload';out.mkdir(parents=True,exist_ok=True)
sources=[HERE/'UploadCheck.cs',ROOT/'Assets/DroneSandbox/Scripts/FarChunkUpload.cs']
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>'+''.join(f'<Compile Include="{p}" />' for p in sources)+'</ItemGroup></Project>'
(out/'check.csproj').write_text(project)
env=dict(os.environ,DOTNET_CLI_HOME=str(out/'home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_PROCESSOR_COUNT='2',MSBUILDDISABLENODEREUSE='1')
dotnet='C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe'
subprocess.run([dotnet,'build',str(out/'check.csproj'),'-c','Release','--nologo','-m:1','--configfile',str(ROOT/'Temp/places-up-close/build/NuGet.Config')],env=env,check=True)
r=subprocess.run([dotnet,str(out/'bin/Release/net8.0/check.dll')],env=env,capture_output=True,text=True)
print(r.stdout+r.stderr)
(HERE/'Review/upload-check.txt').write_text(r.stdout+r.stderr)
r.check_returncode()
