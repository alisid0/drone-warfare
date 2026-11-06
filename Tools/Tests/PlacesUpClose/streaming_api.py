"""Compile the entire modified terrain streamer against Unity, replacing only unrelated game services."""
from pathlib import Path
import os
import subprocess
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
out=ROOT/'Temp/places-up-close/build'
scripts=ROOT/'Assets/DroneSandbox/Scripts'
project=ET.parse(out/'api.csproj'); items=project.getroot().find('ItemGroup')
reference=ET.SubElement(items,'Reference',Include='UnityEngine.IMGUIModule')
ET.SubElement(reference,'HintPath').text='C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/Managed/UnityEngine/UnityEngine.IMGUIModule.dll'
for item in list(items):
    if item.tag=='Compile' and Path(item.attrib['Include']).name in ('DetailStub.cs','ApiServices.cs'): items.remove(item)
services=(out/'ApiServices.cs').read_text()
start=services.index('    public static class FarTerrain'); end=services.index('    public static class Backdrop',start)
services=services[:start]+services[end:]
services+='''
namespace DroneSandbox {
public class Drone : UnityEngine.MonoBehaviour { public bool IsAlive=>true; }
public class DroneFeed { public static DroneFeed Instance; public Drone Source; }
}
'''
(out/'StreamingServices.cs').write_text(services)
sources=[scripts/'FarTerrain.cs',out/'StreamingServices.cs']+list(scripts.glob('FarDetail*.cs'))+list(scripts.glob('FarWorld*.cs'))
included={str(Path(x.attrib['Include'])) for x in items if x.tag=='Compile'}
for source in sources:
    if str(source) not in included: ET.SubElement(items,'Compile',Include=str(source))
project.write(out/'streaming.csproj')
env=dict(os.environ,DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_PROCESSOR_COUNT='2',MSBUILDDISABLENODEREUSE='1')
subprocess.run(['C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe','build',str(out/'streaming.csproj'),'-c','Release','--nologo','-m:1','--configfile',str(out/'NuGet.Config')],env=env,check=True)
