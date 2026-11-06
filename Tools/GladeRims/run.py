"""Compile production C# 9 against Unity or replay it with recording storage; never start Unity."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
phase = sys.argv[1] if len(sys.argv) > 1 else 'api'
sector = sys.argv[2] if len(sys.argv) > 2 else 'glade'
setup = (root / 'Tools/WoodsBuild/run.py').read_text()
setup = setup[:setup.index("runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/WoodsBuild/Harness.cs']")]
setup = setup.replace('Temp/woods-build', 'Temp/glade-rims')
exec(compile(setup, str(root / 'Tools/WoodsBuild/run.py'), 'exec'))
env['DOTNET_PROCESSOR_COUNT'] = '2'
env['DOTNET_GCHeapHardLimit'] = '0x60000000'

# The old recorder stored only yaw. Preserve all three axes for the new leaning crowns and clearance checks.
engine = (temp / 'RuntimeEngine.cs').read_text()
start = engine.index('    public struct Quaternion')
end = engine.index('    public enum PrimitiveType', start)
engine = engine[:start] + '''    public struct Quaternion
    {
        public Vector3 right, up, forward;
        public static Quaternion LookRotation(Vector3 d)
        {
            var f=d.normalized; var r=Vector3.Cross(Vector3.up,f).normalized;
            return new Quaternion { right=r, up=Vector3.Cross(f,r), forward=f };
        }
        public static Vector3 operator*(Quaternion q,Vector3 v)=>q.right*v.x+q.up*v.y+q.forward*v.z;
        public static Quaternion operator*(Quaternion a,Quaternion b)=>new Quaternion {right=a*b.right,up=a*b.up,forward=a*b.forward};
        public static Quaternion Euler(float x,float y,float z)
        {
            x*=Mathf.Deg2Rad;y*=Mathf.Deg2Rad;z*=Mathf.Deg2Rad;
            float cx=Mathf.Cos(x),sx=Mathf.Sin(x),cy=Mathf.Cos(y),sy=Mathf.Sin(y),cz=Mathf.Cos(z),sz=Mathf.Sin(z);
            var rx=new Quaternion{right=Vector3.right,up=new Vector3(0,cx,sx),forward=new Vector3(0,-sx,cx)};
            var ry=new Quaternion{right=new Vector3(cy,0,-sy),up=Vector3.up,forward=new Vector3(sy,0,cy)};
            var rz=new Quaternion{right=new Vector3(cz,sz,0),up=new Vector3(-sz,cz,0),forward=new Vector3(0,0,1)};
            return ry*rx*rz;
        }
    }
''' + engine[end:]
write(temp / 'RuntimeEngine.cs', engine)
runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/GladeRims/Harness.cs']
if phase == 'before':
    for name in ['ForestPattern', 'ForestPalette', 'SectorForest', 'ForestHlod', 'FarScatter', 'Canopy']:
        source = subprocess.check_output(['git', '-c', 'safe.directory=' + root.as_posix(), 'show', '2d99a10:Assets/DroneSandbox/Scripts/' + name + '.cs'], text=True)
        write(temp / ('Before' + name + '.cs'), source)
        runtime = [temp / ('Before' + name + '.cs') if p.stem == name else p for p in runtime]

# Reuse the small serial compiler driver, including its actual installed Unity module references.
driver = (root / 'Tools/WoodsBuild/run.py').read_text()
driver = driver[driver.index("refs = ''.join"):driver.index("if phase == 'api':")]
driver = driver.replace('<LangVersion>9</LangVersion>', '<LangVersion>9</LangVersion><DefineConstants>' + ('CANDIDATE' if phase != 'before' else '') + '</DefineConstants>')
exec(compile(driver, __file__, 'exec'))
if phase == 'api':
    api = list(files) + [scripts / (name + '.cs') for name in ['LandmarkClutter', 'WaterAppearance', 'FarDetailWater', 'FarDetailRegionData'] if scripts / (name + '.cs') not in files]
    write(temp / 'HdrpContract.cs', (temp / 'HdrpStubs.cs').read_text())
    api = [temp / 'HdrpContract.cs' if p.name == 'HdrpStubs.cs' else p for p in api]
    build('api', api, refs)
else:
    build('review', runtime, executable=True)
    result = subprocess.run([str(dotnet), str(temp / 'bin/Release/net8.0/review.dll'), str(temp / (phase + '-' + sector)), sector, phase] + sys.argv[3:], env=env, capture_output=True, text=True)
    write(temp / (phase + '-' + sector + ('-geometry' if '--geometry' in sys.argv else '') + '.log'), result.stdout + result.stderr)
    print(result.stdout + result.stderr)
    result.check_returncode()
