"""Prepare the one required edit outside the allowed file list for Claude, without changing Canopy.cs."""
from pathlib import Path
import difflib
root = Path(__file__).resolve().parents[2]
path = 'Assets/DroneSandbox/Scripts/Canopy.cs'
before = (root / path).read_text()
addition = '''            // Logical woods have no LODGroup: retain the existing crown tests and drone height grid.
            foreach (var stem in SectorForest.Stems)
            {
                float radius = stem.stand.radius, height = stem.stand.height;
                Vector3 p = stem.position;
                float roof = p.y + height;
                int x0 = Index(p.x - radius * 0.8f), x1 = Index(p.x + radius * 0.8f);
                int z0 = Index(p.z - radius * 0.8f), z1 = Index(p.z + radius * 0.8f);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) top[x, z] = Mathf.Max(top[x, z], roof);
                bool conifer = stem.stand.species >= 2;
                var crown = new Crown { centre = p + Vector3.up * height * 0.675f,
                    radius = radius * (conifer ? 0.75f : 0.65f), halfHeight = height * 0.325f, cone = conifer };
                var key = CrownKey(crown.centre);
                if (!crowns.TryGetValue(key, out var list)) crowns[key] = list = new List<Crown>();
                list.Add(crown);
            }
'''
after = before.replace('            crowns.Clear();\n', '            crowns.Clear();\n' + addition)
assert after != before
patch = ''.join(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile='a/' + path, tofile='b/' + path))
(root / 'Tools/DenseForest/canopy-integration.patch').write_bytes(patch.replace('\n', '\r\n').encode())
(root / 'Temp/dense-forest/Canopy.cs').write_bytes(after.replace('\n', '\r\n').encode())
