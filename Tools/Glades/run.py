"""Replay production forest C# for the glades task without Unity, reusing Tools/CanopyRelief2 and Tools/CanopyCracks.

  run.py after cracks    CanopyCracks gap, exposure and pattern checks on the working tree
  run.py before cracks   the same checks on this branch's base (f7588a1)
  run.py after render    Harness.cs: canopy G-buffers for the 300 m, 1 km and 2.5 km glade views, plus glade statistics
  run.py before render   the same at f7588a1
The API compile is Tools/PredictiveStreaming/run.py api (every runtime script against the real Unity and package assemblies).
Output goes to Temp/glades/<phase>-<mode>. One build node and one process at a time; never starts Unity or uses the GPU.
"""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
mode = sys.argv[2] if len(sys.argv) > 2 else 'cracks'

# Newer runtime code (route prefetch, sliced uploads, shader priming, landmark palettes) needs a few more managed stubs.
ROUTE = """    public sealed class FarDetailRoute { public Vector2 Eye; public float Lead; public int PathCount; public Vector2 PathPoint(int i) => Eye; }
    public static class FarChunkUpload
    {
        public static long Bytes(int stage, int vertices, int indices) => 0;
        public static bool Step(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] triangles, Bounds bounds, bool release, ref int stage) => true;
    }
    public static class FarDetail
    {
        internal static FarDetailRoute Route => null;"""


def stubs(temp, write):
    """Patch the generated managed engine just before the review build."""
    storage = (temp / 'ProjectionStorage.cs').read_text()
    storage = storage.replace('public enum FilterMode { Trilinear }', 'public enum FilterMode { Trilinear, Bilinear }')
    storage = storage.replace('public class Camera : Component {', 'public class Camera : Component { public float nearClipPlane = 0.3f;', 1)
    write(temp / 'ProjectionStorage.cs', storage)
    engine = (temp / 'RuntimeEngine.cs').read_text()
    if 'Quaternion identity' not in engine:
        engine = engine.replace('public static Vector3 operator*(Quaternion q,Vector3 v)',
            'public static Quaternion identity=>new Quaternion{right=Vector3.right,up=Vector3.up,forward=new Vector3(0,0,1)}; public static Vector3 operator*(Quaternion q,Vector3 v)', 1)
    write(temp / 'RuntimeEngine.cs', engine)
    maths = (temp / 'RuntimeMath.cs').read_text()
    if 'Vector3 Min(' not in maths:
        maths = maths.replace('public static Vector3 right => new Vector3(1,0,0);', 'public static Vector3 right => new Vector3(1,0,0); '
            'public static Vector3 Min(Vector3 a,Vector3 b)=>new Vector3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z)); '
            'public static Vector3 Max(Vector3 a,Vector3 b)=>new Vector3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));', 1)
    write(temp / 'RuntimeMath.cs', maths)
    detail = (temp / 'DetailStub.cs').read_text()
    if 'FarDetailRoute' not in detail:
        detail = detail.replace('    public static class FarDetail\n    {', ROUTE, 1)
        assert 'FarDetailRoute Route' in detail
    write(temp / 'DetailStub.cs', detail)


def glades(driver):
    """Retarget the CanopyRelief2 driver: own temp folder, f7588a1 as 'before', newer stubs and this tool's harness."""
    driver = driver.replace('Temp/canopy-relief-2', 'Temp/glades')
    assert '2618d5a:' in driver
    driver = driver.replace('2618d5a:', 'f7588a1:')
    if mode == 'render':
        assert 'Tools/CanopyRelief2/Harness.cs' in driver
        driver = driver.replace('Tools/CanopyRelief2/Harness.cs', 'Tools/Glades/Harness.cs')
    build = "build('review', runtime, executable=True)"
    assert build in driver
    driver = driver.replace(build, 'stubs(temp, write)\n    ' + build)
    return driver


source = (root / 'Tools/CanopyRelief2/run.py').read_text()
hook = 'driver = patch(driver)\\n'
assert hook in source
source = source.replace(hook, 'driver = glades(patch(driver))\\n')
exec(compile(source, str(root / 'Tools/CanopyRelief2/run.py'), 'exec'))
