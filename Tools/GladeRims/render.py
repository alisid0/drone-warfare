"""Replay actual exported forest geometry with matched lighting; these are offline views, not HDRP captures."""
from pathlib import Path
import sys
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / 'Tools/CanopyRelief/render.py').read_text().split('args=sys.argv[')[0]
source = source.replace('Tools/CanopyRelief/Review', 'Tools/GladeRims/Review').replace('Temp/canopy-relief', 'Temp/glade-rims')
# Both revisions already include hd155's crown textures, tints and olive floor.
source = source.replace("if PHASE == 'after'", 'if True')
source = source.replace("        weight=op('MINIMUM'", "        if PHASE != 'before': return\n        weight=op('MINIMUM'")
exec(compile(source, str(ROOT / 'Tools/CanopyRelief/render.py'), 'exec'))

# Render the exported rotation, including edge lean, instead of the historical yaw-only replay.
tree_source = (ROOT / 'Tools/ForestPolish/render.py').read_text()
tree_source = tree_source[tree_source.index('def trees(data):'):tree_source.index('def surface(')]
tree_source = tree_source.replace(".96 + .04*t['shade']", ".90 + .09*t['shade']")
tree_source = tree_source.replace('rotation = np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]])', "rotation = np.array(t['rotation'])")
exec(compile(tree_source, __file__, 'exec'))

# The wood-edge view includes the real parcel footprints and shared filtered crop palette, not a blank meadow.
floor_source = (ROOT / 'Tools/ForestPolish/render.py').read_text()
floor_source = floor_source[floor_source.index('def smooth_floor('):floor_source.index('def trees(')]
floor_source = floor_source.replace('(.25,.28,.18)', '(49/255,70/255,45/255)')
floor_source = floor_source.replace('    pixels=np.concatenate', '''    if data.get('fields'):
        fields=np.array(data['fields']).reshape(n,n,4)
        rgb=rgb*(1-fields[:,:,3:])+fields[:,:,:3]*fields[:,:,3:]
    pixels=np.concatenate''')
exec(compile(floor_source, __file__, 'exec'))

# Use the same sun in both pictures, with physical cast shadows and no material emission.
render_source = (ROOT / 'Tools/ForestPolish/render.py').read_text()
render_source = render_source[render_source.index('def render_one('):render_source.index('args = sys.argv[')]
render_source = render_source.replace('Temp/forest-polish', 'Temp/glade-rims')
render_source = render_source.replace('scene = bpy.context.scene;', '''for m in bpy.data.materials:
        if m.use_nodes:
            bsdf=m.node_tree.nodes.get('Principled BSDF')
            if bsdf: bsdf.inputs['Emission Strength'].default_value=0
    scene = bpy.context.scene;''')
render_source = render_source.replace('sun.data.use_shadow = False', 'sun.data.use_shadow = True')
exec(compile(render_source, __file__, 'exec'))
args = sys.argv[sys.argv.index('--') + 1:]
PHASE = args[0]
render_one(PHASE, args[1])
