"""Compile production C# 9 against Unity, or replay HEAD/working forest geometry serially; never start Unity.

  run.py api            compile the forest sources against the installed Unity 6000.6.7f1 assemblies
  run.py before cracks  replay this branch's base (2618d5a) forest sources through Harness.cs
  run.py after cracks   replay the working tree's forest sources through Harness.cs
  check_shader.py       D3DCompile ForestFade/ForestDetail with the per-pixel refinement signature

Harness.cs builds eight canopy levels for the 88_world_forest_hlod block and its east/north neighbours, counts
internal and shared-edge gaps for all 128 level pairs, then rasterises exposure at 2 m. Review/exposure-before-after.png
shows 1 km of that block: bootstrap above, refined32 below, before left, after right; red = hole, yellow = below
the 62.5 m far chunk, blue = below the 4 m detail floor, green = canopy (darker = glade), grey = outside the wood mask.
"""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
driver = (root / 'Tools/GladeRims/run.py').read_text()
driver = driver.replace('Temp/glade-rims', 'Temp/canopy-cracks')
# 'before' replays this branch's base; the landmark instancer arrived after the glade-rims harness was written.
driver = driver.replace('2d99a10:', '2618d5a:')
driver = driver.replace("['LandmarkClutter', 'WaterAppearance',", "['LandmarkClutter', 'LandmarkInstances', 'LandmarkLod', 'WaterAppearance',")
# The real streamer samples a rough 4 m detail surface; the visible far chunks interpolate 62.5 m heights.
harness = "runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/GladeRims/Harness.cs']"
assert harness in driver
driver = driver.replace(harness, "write(temp / 'DetailStub.cs', (root / 'Tools/CanopyCracks/DetailStub.cs').read_text())\n"
                        "runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/CanopyCracks/Harness.cs']")
exec(compile(driver, __file__, 'exec'))
