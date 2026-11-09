"""Write reviewable integration patches and temporary compile copies; never edit protected source files."""
from pathlib import Path
import difflib

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
out = ROOT / 'Temp/roads/patched'
out.mkdir(parents=True, exist_ok=True)
parts = []
for name in ('Ground', 'Farmland', 'Atmosphere'):
    path = ROOT / f'Assets/DroneSandbox/Scripts/{name}.cs'
    before = path.read_text()
    after = before
    if name == 'Ground':
        a = after.index('                    // Dirt roads:')
        b = after.index('                    // Gravel railway beds', a)
        after = after[:a] + '''                    // Shared class widths reserve the same surface as the road ribbon and distant painter.
                    float dRoad = roads.Query(x, z, out _, out int roadSegment);
                    float roadHw = MapLayout.RoadWidth(roadSegment);
''' + after[b:]
        a = after.index('                    // And along the roads, in stretches:')
        b = after.index('                    // Each layer covers', a)
        after = after[:a] + '''                    // Bake mown shoulders and the two narrow ruts after the natural grass mix.
                    RoadLayout.Splat(x, z, Size / SplatRes, ref dirt, ref wet, ref rough);

''' + after[b:]
    elif name == 'Farmland':
        after = after.replace('RendererLimit - MapLayout.Fields.Count - useRenderers', 'RendererLimit - MapLayout.Fields.Count - useRenderers - RoadSurface.Renderers')
        after = after.replace('            PoplarRows(root, rng, ref rows);', '            Roadside.Build(root, ref rows); // same avenue allowance, shared FBX instances')
        after = after.replace('            if (Mathf.Abs(t - gate) * length < 2f + extra) return true;', '''            if (Mathf.Abs(t - gate) * length < 2f + extra) return true;
            foreach (var entry in RoadSurface.Entrances)
                if ((edge.Point(t) - entry.gate).sqrMagnitude < (2f + extra) * (2f + extra)) return true;''')
    else:
        after = after.replace('            Rain(weather == Weather.Rain);', '            Rain(weather == Weather.Rain);\n            RoadSurface.SetRain(weather == Weather.Rain);')
    assert before != after, name
    (out/(name+'.cs')).write_bytes(after.replace('\n','\r\n').encode())
    rel = path.relative_to(ROOT).as_posix()
    parts.extend(difflib.unified_diff(before.splitlines(True), after.splitlines(True), 'a/'+rel, 'b/'+rel))
(HERE/'Claude-integration.patch').write_bytes(''.join(parts).replace('\n','\r\n').encode())
print('Wrote Ground/Farmland/Atmosphere patch and temporary compile copies; originals unchanged.')
