"""Write the exact integration patch and compile copy; the owner's Ground.cs remains read-only."""
from pathlib import Path
import difflib

ROOT = Path(__file__).resolve().parents[2]
path = ROOT / 'Assets/DroneSandbox/Scripts/Ground.cs'
before = path.read_text()
after = before.replace('''            for (int t = 0; t < trees.Count; t++)
            {
                Spread(canopy, trees[t], 0.5f);''', '''            for (int t = 0; t < trees.Count; t++)
            {
                // Varrow's decorative bank/garden trees cull independently; only logical woods keep a distant floor.
                if (MapLayout.Plan.kind == SectorKind.VarrowValley && !SectorForest.HasFloorCoverage(trees[t])) continue;
                Spread(canopy, trees[t], 0.5f);''')
assert after != before, 'Floor input contract changed'
after = after.replace('// dull grey-brown litter seen between trees from the air (66, 58, 42); warmer read as rust halos (hd141).',
                      '// shadowed olive understorey seen between crowns (49, 70, 45), shared with ForestPalette.Ground.')
after = after.replace('new Color(0.59f, 0.88f, 1.55f), new Color(0.30f, 0.23f, 0.15f), new Color(0.18f, 0.14f, 0.09f)',
                      'new Color(0.337f, 1.280f, 1.775f), new Color(0.21f, 0.30f, 0.19f), new Color(0.17f, 0.25f, 0.16f)')
after = after.replace('new Vector3(0.0539f, 0.0421f, 0.0229f)', 'new Vector3(0.03071f, 0.06125f, 0.02624f)')
out = ROOT / 'Temp/canopy-relief'
out.mkdir(parents=True, exist_ok=True)
(out / 'Ground.patched.cs').write_bytes(after.replace('\n', '\r\n').encode())
patch = ''.join(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                 'a/Assets/DroneSandbox/Scripts/Ground.cs', 'b/Assets/DroneSandbox/Scripts/Ground.cs'))
(ROOT / 'Tools/CanopyRelief/ground-integration.patch').write_bytes(patch.replace('\n', '\r\n').encode())
