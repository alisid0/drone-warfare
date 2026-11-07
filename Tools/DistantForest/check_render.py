"""Run the production forest scheduler with recording engine storage; no claim to measure Unity uploads or GPU work."""
from pathlib import Path
import subprocess
import sys

script = Path(__file__).parent / 'run.py'
source = script.read_text()
exec(compile(source[:source.index("includes = ''.join")], str(script), 'exec'))
runtime = [p for p in runtime if p.name not in {'CanopyBake.cs', 'Harness.cs', 'Checks.cs', 'CrownChecks.cs', 'ReferenceCanopy.cs'}]
stub = remove_class((temp / 'RuntimeServices.cs').read_text(), 'public class FarScatter')
stub = stub.replace('public static bool Active => false;', 'public static bool Active = true;')
stub += '''
namespace DroneSandbox {
public sealed class SettlementHlod { public void Clear(){} public void Update(UnityEngine.Camera c,UnityEngine.Plane[] p,FarScatter s){} }
public sealed class LandmarkHlod { public void Clear(){} public void Update(UnityEngine.Camera c,UnityEngine.Plane[] p){} }
}
'''
write(temp / 'RuntimeServices.cs', stub)
engine = (temp / 'RuntimeEngine.cs').read_text()
for signature in ['public class Shader', 'public class Material', 'public static class Resources', 'public struct Bounds', 'public struct Plane', 'public struct Vector4']:
    engine = remove_class(engine, signature)
engine = engine.replace('public class Component : Object', 'public class MonoBehaviour : Component { } public class Component : Object')
engine = engine.replace('public Vector3 position, localScale, up;', 'public Matrix4x4 localToWorldMatrix=>Matrix4x4.identity; public Matrix4x4 worldToLocalMatrix=>Matrix4x4.identity; public Vector3 position, localScale, up;')
engine = engine.replace('public GameObject(string name)', 'public T[] GetComponentsInChildren<T>() where T:new()=>new T[0]; public GameObject(string name)')
engine = engine.replace('public Bounds bounds; public bool', 'public Bounds bounds; public bool')
engine = engine.replace('public Rendering.IndexFormat indexFormat;', 'public Bounds bounds=new Bounds(new Vector3(0,10,0),new Vector3(12,20,12)); public void UploadMeshData(bool b){} public uint GetIndexCount(int sub)=>(uint)(triangles?.Length??0); public Rendering.IndexFormat indexFormat;')
engine = engine.replace('public void RecalculateBounds() { }', 'public void RecalculateBounds() { if(vertices==null||vertices.Length==0)return; bounds=new Bounds(vertices[0],Vector3.zero);foreach(var p in vertices)bounds.Encapsulate(p); }')
engine = engine.replace('public TextureWrapMode wrapMode;', 'public FilterMode filterMode; public TextureWrapMode wrapMode;')
engine = engine.replace('public enum IndexFormat { UInt32 }', 'public enum IndexFormat { UInt16, UInt32 }')
engine = engine.replace('public enum ShadowCastingMode { Off }', 'public enum ShadowCastingMode { Off, On }')
engine = engine.replace('public LOD[] GetLODs()=>new LOD[0];', 'public LOD[] lods; public LOD[] GetLODs()=>lods??new LOD[0];')
write(temp / 'RuntimeEngine.cs', engine)
math = (temp / 'RuntimeMath.cs').read_text().replace('public struct Vector3\n    {', 'public struct Vector3\n    { public static Vector3 Scale(Vector3 a,Vector3 b)=>new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);')
write(temp / 'RuntimeMath.cs', math)
runtime += [scripts / (name + '.cs') for name in ['FarScatter', 'ForestHlod', 'ForestMaterials', 'SettlementPattern']]
runtime += [root / 'Tools/DistantForest/RenderStorage.cs', root / 'Tools/DistantForest/RenderChecks.cs']
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in runtime)
write(temp / 'render-checks.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'render-checks.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
subprocess.run([str(dotnet), str(temp / 'bin/Release/net8.0/render-checks.dll'), str(temp / 'render-checks.json')], env=env, check=True)
