"""Prepare Claude's terrain-only patch and a compilation copy; never edit Ground.cs."""
from pathlib import Path
import difflib

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
SOURCE = ROOT / 'Assets/DroneSandbox/Scripts/Ground.cs'


def write(path, text):
    """Keep generated text in the worktree and match the project's CRLF convention."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


def prepare():
    """Apply narrow, asserted substitutions to a temporary copy for review and API checks."""
    original = SOURCE.read_text()
    text = original

    def replace(before, after):
        """Reject a stale integration context instead of producing a partial patch."""
        nonlocal text
        assert text.count(before) == 1, before[:120]
        text = text.replace(before, after)

    replace('        static Terrain terrain;', '        static Terrain terrain;\n        static WatercourseProfile watercourse;')
    replace('            terrainBottom = -Sink;\n', '''            // Real channel sizes and bend shelves, after levels are known. The dam gorge keeps its established shape.
            watercourse = new WatercourseProfile();
            if (MapLayout.HasRiver)
                System.Threading.Tasks.Parallel.For(0, HeightRes, zi =>
                {
                    for (int xi = 0; xi < HeightRes; xi++)
                    {
                        float x = -Size / 2f + xi * step, z = -Size / 2f + zi * step;
                        if (MapLayout.HasDam && MapLayout.NearDam(new Vector2(x, z), 60f)) continue;
                        if (!watercourse.Sample(x, z, out float distance, out float level, out float width,
                            out float margin, out float depth, out float slope) || distance > width + margin + 14f) continue;
                        if (MapLayout.LakeDistance(x, z) < 8f) continue;
                        float edge = distance - width;
                        // Preserve bridge approaches, graded roads and all flat building sites on dry ground.
                        if (edge > 0f && roads.Distance(x, z) < MapLayout.RoadWidthAt(x, z) + 3f) continue;
                        float shape = edge <= 0f ? level - depth
                            : level + .12f + Mathf.Min(edge, margin) * .045f + Mathf.Max(0f, edge - margin) * slope;
                        float weight = (1f - WaterAppearance.Smooth((edge - margin) / 14f)) * FlatMask(x, z, out _);
                        world[zi, xi] = Mathf.Lerp(world[zi, xi], shape, weight);
                    }
                });

            terrainBottom = -Sink;
''')
    replace('                Layer("litter", "litter", 6f, new Color(0.59f, 0.88f, 1.55f), new Color(0.30f, 0.23f, 0.15f), new Color(0.18f, 0.14f, 0.09f)),', '''                Layer("litter", "litter", 6f, new Color(0.59f, 0.88f, 1.55f), new Color(0.30f, 0.23f, 0.15f), new Color(0.18f, 0.14f, 0.09f)),
                // Reuse the dirt photo: wet mud and pale gravel occupy the two unused slots of HDRP's existing
                // eight-layer pass. Linear tints match WaterAppearance's filtered distant colours.
                Layer("wet mud", "dirt", 5f, new Color(.53001f, .66905f, .72861f), new Color(.34f, .32f, .26f), new Color(.27f, .25f, .20f)),
                Layer("shore gravel", "dirt", 4f, new Color(1.99341f, 2.47628f, 2.89197f), new Color(.62f, .59f, .50f), new Color(.54f, .51f, .43f)),''')
    replace('new float[SplatRes, SplatRes, 6]', 'new float[SplatRes, SplatRes, 8]')
    replace('                        floor *= 1f - Mathf.Clamp01((maps[j, i, 2] + maps[j, i, 3]) * 1.5f); // tracks, yards, rock',
            '''                        float shore = maps.GetLength(2) > 7 ? maps[j, i, 6] + maps[j, i, 7] : 0f;
                        floor *= 1f - Mathf.Clamp01((maps[j, i, 2] + maps[j, i, 3] + shore) * 1.5f); // preserve exposed banks too''')
    start = '                    // River banks: bare earth where the bank is cut back'
    a = text.index(start)
    b = text.index('                    // The lake: a stony shore', a)
    legacy = text[a:b]
    # Retain the existing gorge painting verbatim, then apply the new bank treatment to ordinary reaches.
    replace(legacy, legacy + '''                    float wetMud = 0f, gravel = 0f;
                    if (!(MapLayout.HasDam && MapLayout.NearDam(new Vector2(x, z), 60f)) && watercourse != null && watercourse.Sample(x, z, out float riverDistance,
                        out _, out float waterWidth, out float margin, out _, out _) && riverDistance < waterWidth + margin + 1f)
                    {
                        float edge = riverDistance - waterWidth;
                        float bank = 1f - WaterAppearance.Smooth((edge - margin + .5f) / 1f);
                        float wetFringe = WaterAppearance.WetEdge(edge);
                        // The submerged shelf is pale enough to read through shallow water; mud stays on land.
                        wetMud = bank * wetFringe * WaterAppearance.Smooth((edge + .3f) / .6f);
                        gravel = bank * (1f - wetMud) * (.75f + .25f * Mathf.PerlinNoise(x * .14f, z * .14f + 9f));
                    }
''')
    needle = '                    // A few worn patches.\n'
    replace(needle, '''                    // Draw-down and wet margins follow the signed shore, including islands and world lakes.
                    if (lake > -7f && lake < 7f)
                    {
                        bool reservoir = plan.world != null ? false : MapLayout.ReservoirDistance(x, z) <= lake + .01f;
                        if (plan.world != null)
                            foreach (var basin in plan.world.lakes)
                                if (basin.reservoir && WorldSectorData.ShoreDistance(new Vector2(x, z), basin.shore) <= lake + .01f)
                                { reservoir = true; break; }
                        float band = reservoir ? 5f : 2.5f;
                        float shore = 1f - WaterAppearance.Smooth((lake - band + .5f) / 1f);
                        // Keep the spillway abutments and their weathering exactly as authored.
                        float keep = MapLayout.NearDam(new Vector2(x, z), 12f) ? 0f : 1f;
                        float wetFringe = WaterAppearance.WetEdge(lake) * WaterAppearance.Smooth((lake + .3f) / .6f);
                        wetMud = Mathf.Max(wetMud, shore * wetFringe * keep);
                        gravel = Mathf.Max(gravel, shore * (1f - wetFringe) * keep);
                    }
                    // Roads, yards and field margins keep ownership of their dry ground.
                    float shoreKeep = bare || dRoad < roadHw + 1f || MapLayout.InField(new Vector2(x, z), 0f) ? 0f : 1f;
                    wetMud *= shoreKeep; gravel *= shoreKeep;
                    // A few worn patches.
''')
    replace('                    splat[zi, xi, 4] = w4 / sum;', '''                    splat[zi, xi, 4] = w4 / sum;
                    float shoreCover = Mathf.Clamp01(wetMud + gravel);
                    for (int layer = 0; layer < 6; layer++) splat[zi, xi, layer] *= 1f - shoreCover;
                    float shoreSum = Mathf.Max(.0001f, wetMud + gravel);
                    splat[zi, xi, 6] = shoreCover * wetMud / shoreSum;
                    splat[zi, xi, 7] = shoreCover * gravel / shoreSum;''')
    relative = SOURCE.relative_to(ROOT).as_posix()
    diff = ''.join(difflib.unified_diff(original.splitlines(True), text.splitlines(True), 'a/' + relative, 'b/' + relative))
    write(HERE / 'Claude-Ground-water.patch', diff)
    write(ROOT / 'Temp/water/Ground.patched.cs', text)

    # Grass is also read-only: this companion keeps reeds on the new waterline and grass off exposed bars.
    grass_path = ROOT / 'Assets/DroneSandbox/Scripts/Grass.cs'
    grass = grass_path.read_text()
    changed = grass.replace('            var river = MapLayout.RiverField;', '''            var river = MapLayout.RiverField;
            var watercourse = new WatercourseProfile();
            Vector2 waterOrigin = MapLayout.Plan.world != null ? MapLayout.Plan.world.center : Vector2.zero;''')
    before = '''                    float lake = MapLayout.LakeDistance(x, z), bank = river.Distance(x, z) - MapLayout.RiverWidthAt(x, z);
                    bool waterside = (lake > -3f && lake < 1f) || (bank > -1.5f && bank < 1f && !MapLayout.InGorge(p, 0f));'''
    after = '''                    float lake = MapLayout.LakeDistance(x, z), riverDistance = river.Distance(x, z);
                    float bank = 100000f, margin = 0f;
                    bool slow = false;
                    if (riverDistance < MapLayout.RiverHalfWidth + 6f && watercourse.Sample(x, z, out float distance,
                        out _, out float width, out margin, out _, out _))
                    {
                        bank = distance - width;
                        slow = margin > 2.3f && WaterAppearance.Pool(x + waterOrigin.x, z + waterOrigin.y) > .45f;
                    }
                    bool waterside = (lake > -3f && lake < 1f) || (slow && bank > -1.5f && bank < 1f && !MapLayout.InGorge(p, 0f));'''
    assert before in changed
    changed = changed.replace(before, after)
    changed = changed.replace('if (river.Distance(x, z) < MapLayout.RiverHalfWidth + 1f) continue;',
                              'if (riverDistance < MapLayout.RiverHalfWidth + 1f || bank < margin + .5f) continue;')
    changed = changed.replace('if (MapLayout.LakeDistance(x, z) < 1f || MapLayout.NearDam(p, 0f)) continue;',
                              'if (lake < (MapLayout.HasDam ? 5f : 2.5f) || MapLayout.NearDam(p, 0f)) continue;')
    relative = grass_path.relative_to(ROOT).as_posix()
    diff = ''.join(difflib.unified_diff(grass.splitlines(True), changed.splitlines(True), 'a/' + relative, 'b/' + relative))
    write(HERE / 'Claude-Grass-water.patch', diff)
    write(ROOT / 'Temp/water/Grass.patched.cs', changed)


if __name__ == '__main__':
    prepare()
