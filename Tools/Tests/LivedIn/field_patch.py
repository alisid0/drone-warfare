"""Write Claude's exact Farmland patch and a temporary compile input; never edit the read-only source."""
from pathlib import Path
import difflib
ROOT=Path(__file__).resolve().parents[3]
p=ROOT/'Assets/DroneSandbox/Scripts/Farmland.cs'
a=p.read_text();b=a.replace('            details.Clear();','            details.Clear();\n            FieldMargins.Release();')
b=b.replace('            int remaining = Mathf.Max(0, RendererLimit - MapLayout.Fields.Count);', '            int useRenderers = FieldMargins.Build(root);\n            int remaining = Mathf.Max(0, RendererLimit - MapLayout.Fields.Count - useRenderers);')
old='''                    growth[y * nw + x] = new Vector2(
                        Mathf.PerlinNoise(across * 0.03f + seed, along * 0.03f + seed * 0.5f) * 0.65f
                            + Mathf.PerlinNoise(across * 0.11f + seed * 2f, along * 0.11f + 7f) * 0.35f,
                        plot.crop == Crop.Wheat ? Mathf.PerlinNoise(across * 0.09f + seed * 3f, along * 0.09f + 40f) : 0f);'''
new='''                    // Match the physical world coordinates of the distant painter, including tapered parcels.
                    Vector2 point = plot.center + plot.along * (along - length * .5f) + plot.Across * (across - width * .5f);
                    if (MapLayout.Plan.world != null) point += MapLayout.Plan.world.center;
                    else point += FieldPlot.Warp(point);
                    growth[y * nw + x] = new Vector2(FieldAppearance.Growth(point.x, point.y),
                        plot.crop == Crop.Wheat ? FieldAppearance.Patch(point.x, point.y) : 0f);'''
assert old in b;b=b.replace(old,new)
b=b.replace('float patchy = crop == Crop.Ploughed ? 0.5f : crop == Crop.YoungCrop ? 0.34f : crop == Crop.MownHay ? 0.2f : 0.28f;', 'float patchy = .55f;')
b=b.replace('float gate = 0.25f + Mathf.Repeat(edge.a.x * 0.017f + edge.a.y * 0.031f, 0.5f);', '''// Keep tractor access near a gate corner; the shared painter uses this same physical gap.
            Vector2 opening = edge.hasGate ? edge.gate : FieldAppearance.Gate(edge.a, edge.b);
            float gate = Vector2.Dot(opening-edge.a, edge.b-edge.a) / Mathf.Max(.01f, length*length);''')
out=ROOT/'Temp/lived-in/Farmland.patched.cs';out.parent.mkdir(parents=True,exist_ok=True)
out.write_bytes(b.replace('\n','\r\n').encode())
patch=''.join(difflib.unified_diff(a.splitlines(True),b.splitlines(True),fromfile='a/Assets/DroneSandbox/Scripts/Farmland.cs',tofile='b/Assets/DroneSandbox/Scripts/Farmland.cs'))
(Path(__file__).parent/'Claude-Farmland.patch').write_bytes(patch.replace('\n','\r\n').encode())
