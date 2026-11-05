from pathlib import Path
import re, subprocess, os
root=Path(__file__).resolve().parents[2]; scripts=root/'Assets/DroneSandbox/Scripts'; temp=root/'Temp/forest-hlod'
unity=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data');dotnet=unity/'DotNetSdk/dotnet.exe'
def write(p,text):p.write_bytes(text.replace('\r\n','\n').replace('\n','\r\n').encode())
def block(src,sig):
    a=src.index(sig);b=src.index('{',a);e=b+1;depth=1
    while depth:
        if src[e]=='{':depth+=1
        elif src[e]=='}':depth-=1
        e+=1
    return src[a:e]
math=(root/'Tools/Tests/SectorIdentity/UnityMath.cs').read_text().split('namespace DroneSandbox')[0]
math=math.replace('public static float Tan(', 'public static float Floor(float a)=>(float)Math.Floor(a); public static float Ceil(float a)=>(float)Math.Ceiling(a); public static float Exp(float a)=>(float)Math.Exp(a); public static float Pow(float a,float b)=>(float)Math.Pow(a,b);\n        public static float Tan(')
math=math.replace('public static Vector3 Cross(', 'public static Vector3 Scale(Vector3 a,Vector3 b)=>new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);\n        public static Vector3 Cross(')
math=math.replace('public static Vector2 zero', 'public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y; public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b); public override bool Equals(object o)=>o is Vector2 b && this==b; public override int GetHashCode()=>x.GetHashCode()^y.GetHashCode();\n        public static Vector2 zero')
write(temp/'Math.cs',math)
cmd=['git','-c','safe.directory='+str(root).replace('\\','/'),'show']
old=subprocess.check_output(cmd+['fc81c6f:Assets/DroneSandbox/Scripts/FarScatter.cs'],text=True)
write(temp/'BaselineFarScatter.cs',old.replace('class FarScatter','class BaselineFarScatter'))
far=(scripts/'FarTerrain.cs').read_text()
support='''using UnityEngine;namespace DroneSandbox { public static class FarTerrain {
public static bool Active=true,ForestCanopyHlod=true;public static Vector2 Origin;
public static float GroundAt(Vector2 p)=>WorldPlan.Height(p.x+Origin.x,p.y+Origin.y);
public static void Initialize(){BuildCanopy();PaintWorld();}
const float CanopyStep=64f,CanopyHeight=16f;const int TextureSize=4096;
static int canopyCount,textureSeed;static float[] canopy;static Texture2D worldTexture;
public static Texture2D WorldTexture=>worldTexture;
public static float CanopyAt(float x,float z)=>Canopy(x,z);
'''
for sig in ['static void BuildCanopy(', 'static float Canopy(', 'static void PaintWorld(', 'static Color32 Field(', 'static int Hash(', 'static void DrawLine(']:support+=block(far,sig)+'\n'
support+='''static Color32 Shade(Color32 c,float f)=>new Color32((byte)Mathf.Clamp(c.r*f,0,255),(byte)Mathf.Clamp(c.g*f,0,255),(byte)Mathf.Clamp(c.b*f,0,255),255);
}}'''
write(temp/'Services.cs',support)
files=list(scripts.glob('WorldPlan*.cs'))+[scripts/(x+'.cs') for x in ['FarScatter','ForestPattern','ForestPalette','ForestHlod','SettlementPattern','SettlementHlod']]+[temp/(x+'.cs') for x in ['SectorKind','Math','Services','BaselineFarScatter']]+[root/'Tools/ForestHlod'/x for x in ['Engine.cs','Harness.cs']]
includes=''.join('<Compile Include="'+str(p)+'" />' for p in files)
write(temp/'harness.csproj','<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>')
env=dict(os.environ,DOTNET_CLI_HOME=str(temp/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1')
subprocess.run([str(dotnet),'build',str(temp/'harness.csproj'),'-c','Release','--nologo','--configfile',str(temp/'NuGet.Config')],env=env,check=True)
subprocess.run([str(dotnet),str(temp/'bin/Release/net8.0/harness.dll'),str(root),str(temp/'exports')],check=True)
