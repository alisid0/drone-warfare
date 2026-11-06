"""Replay production forest C# for canopy-relief-2 without Unity, reusing Tools/CanopyCracks.

  run.py after cracks   CanopyCracks gap, exposure and pattern checks on the working tree
  run.py after render   Harness.cs: canopy G-buffers for the 400 m, 1 km and 2.5 km review views
  run.py api            compile the forest sources against the installed Unity 6000.6.7f1 assemblies
"""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
mode = sys.argv[2] if len(sys.argv) > 2 else 'cracks'
# Roads, use and fringe code merged after the CanopyCracks driver was written; World/MapLayout now need them.
extra = ['RoadNetwork', 'RoadGatePlan', 'RoadLayout', 'RoadAppearance', 'VillageFringe', 'WorldLandmarkLayout.Use', 'FieldPalette']
# Road ribbons only build Unity objects: the API check compiles them for real, the offline replay stubs them.
unity_only = ['RoadSurface', 'Roadside']


def patch(driver):
    """Add the newer production sources, and swap in this tool's render harness when asked."""
    driver = driver.replace('Temp/canopy-cracks', 'Temp/canopy-relief-2')
    harness = "runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/CanopyCracks/Harness.cs']"
    assert harness in driver
    added = ("[root / 'Tools/CanopyRelief2/RoadStub.cs'] + [scripts / (n + '.cs') for n in " + repr(extra)
             + " if (scripts / (n + '.cs')) not in runtime]")
    tool = 'Tools/CanopyRelief2/Harness.cs' if mode == 'render' else 'Tools/CanopyCracks/Harness.cs'
    # ForestMaterials now reads the sun for canopy shadows; the managed engine gains just enough to compile it.
    engine = '''
engine = (temp / 'RuntimeEngine.cs').read_text()
engine = engine.split('\\nnamespace UnityEngine { public class Light')[0]
engine += '\\nnamespace UnityEngine { public class Light : Component { public bool isActiveAndEnabled => false; public float intensity; } public static class RenderSettings { public static Light sun; } }'
write(temp / 'RuntimeEngine.cs', engine)
maths = (temp / 'RuntimeMath.cs').read_text()
if 'ClampMagnitude' not in maths:
    maths = maths.replace('public static float Distance(Vector2 a, Vector2 b)', 'public static Vector2 ClampMagnitude(Vector2 v, float m) => v.magnitude > m ? v.normalized * m : v; public static float Distance(Vector2 a, Vector2 b)', 1)
write(temp / 'RuntimeMath.cs', maths)
'''
    driver = driver.replace(harness, engine + "runtime += [temp / 'ProjectionStorage.cs', root / '" + tool + "'] + " + added)
    api = "api = list(files) + ["
    assert api in driver
    driver = driver.replace(api, "api = list(files) + [scripts / (n + '.cs') for n in " + repr(extra + unity_only)
                            + " if (scripts / (n + '.cs')) not in files] + [")
    return driver


source = (root / 'Tools/CanopyCracks/run.py').read_text()
exec_line = "exec(compile(driver, __file__, 'exec'))"
assert exec_line in source
source = source.replace(exec_line, "driver = patch(driver)\n" + exec_line)
exec(compile(source, str(root / 'Tools/CanopyCracks/run.py'), 'exec'))
