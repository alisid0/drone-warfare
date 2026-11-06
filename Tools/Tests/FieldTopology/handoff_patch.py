"""Write Claude's exact Farmland change without modifying the read-only production file."""
from pathlib import Path
import difflib

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
path = 'Assets/DroneSandbox/Scripts/Farmland.cs'
before = (ROOT / path).read_text()
after = before
replacements = [
    ('        static void BuildField(FieldPlot plot, Transform parent, System.Random rng)\n        {',
     '        static void BuildField(FieldPlot plot, Transform parent, System.Random rng)\n        {\n            bool worldParcel = FarFieldSurface.TryParent(plot, out var surface);'),
    ('                    uvs[k] = new Vector2(v / Tile, u / Tile); // texture rows run along the field',
     '                    // World rows use physical metres, independent of taper, clipping and sector translation.\n                    uvs[k] = worldParcel ? FarFieldSurface.RowUv(surface, p, Tile) : new Vector2(v / Tile, u / Tile);'),
    ('''                material.SetTexture("_DetailMap", FieldDetail(plot));
                material.SetTextureScale("_DetailMap", new Vector2(Tile / (2f * plot.halfWidth), Tile / (2f * plot.halfLength)));
                material.SetTextureOffset("_DetailMap", new Vector2(0.5f, 0.5f));''',
     '''                material.SetTexture("_DetailMap", FieldDetail(surface));
                material.SetTextureScale("_DetailMap", new Vector2(Tile / (2f * surface.halfWidth), Tile / (2f * surface.halfLength)));
                Vector2 offset = new Vector2(0.5f, 0.5f);
                if (worldParcel)
                {
                    Vector2 phase = FarFieldSurface.RowUv(surface, surface.center, 1f);
                    offset -= new Vector2(phase.x / (2f * surface.halfWidth), phase.y / (2f * surface.halfLength));
                }
                material.SetTextureOffset("_DetailMap", offset);''')
]
for old, new in replacements:
    assert after.count(old) == 1, old
    after = after.replace(old, new)
patch = f'diff --git a/{path} b/{path}\n' + ''.join(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile='a/' + path, tofile='b/' + path))
(HERE / 'farmland-world-rows.patch').write_bytes(patch.replace('\n', '\r\n').encode())
