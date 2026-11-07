using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // The dam and everything around the lake that isn't terrain or water: the dam model (Tools/Blender/make_dam.py)
    // with lamps that light up at night and weathering decals (DamWeathering), spray where the spillway hits the
    // river and mist lying on the lake (both volumetric fog, so they glow in the sun and cast shafts),
    // boulders and rock faces in the gorge below the dam, rocks along the shore, forest on the slopes and pines on
    // the island.
    // The lake's shape and level come from MapLayout; Ground shapes the basin and Water draws the surface.
    public static class Reservoir
    {
        const float ModelLakeLevel = 12.9f; // the lake level the dam model was drawn for, above its foot

        public static void Build(Transform parent)
        {
            MapLayout.Prepare();
            if (!MapLayout.HasDam) return; // a sector without a reservoir
            var root = new GameObject("Reservoir").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(23);

            Vector2 dam = MapLayout.DamPoint, down = MapLayout.DamDown;
            var model = new GameObject("Dam").transform;
            model.SetParent(root, false);
            model.position = new Vector3(dam.x, MapLayout.LakeLevel - ModelLakeLevel, dam.y);
            model.rotation = Quaternion.LookRotation(new Vector3(down.x, 0f, down.y)); // the model's +Z points downstream
            if (Art.TryModel("Scenery_Dam", model) != null)
            {
                model.gameObject.AddComponent<SpillwayFlow>();
                DamWeathering.Add(model);
                foreach (var t in model.GetComponentsInChildren<Transform>())
                {
                    if (!t.name.StartsWith("LampPoint")) continue;
                    var lamp = DayNight.AddNightLight(t, Vector3.zero, Vector3.zero, LightType.Spot, new Color(1f, 0.82f, 0.55f), 9f, 24f, 115f);
                    lamp.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // straight down onto the road
                }
            }

            // Spray where the spillway pours into the river, and thin mist lying on the lake.
            Vector3 foot = Ground.OnGround(new Vector3(dam.x + down.x * 9f, 0f, dam.y + down.y * 9f));
            float river = MapLayout.RiverField.values[0];
            Fog(root, "Spray", new Vector3(foot.x, river + 4f, foot.z), new Vector3(26f, 10f, 20f), down, 9f, 0.35f);
            foreach (var middle in new[] { dam - down * 45f, dam - down * 80f + MapLayout.DamAcross * 60f })
                Fog(root, "Lake Mist", new Vector3(middle.x, MapLayout.LakeLevel + 2f, middle.y), new Vector3(95f, 5f, 75f), down, 90f, 0.45f);

            // The scanned rocks are picked with the roll the procedural ones used (or a separate generator where
            // there was none), so rng runs as before and the lake woods stay where they were.
            var variety = new System.Random(29);
            GorgeBoulders(root, rng, variety);
            ShoreRocks(root, rng, variety);
            Forest(root, rng);
            GorgeCliffs(root);
        }

        // Boulders along both banks and in the water for the first stretch below the dam: the photo-scanned ones
        // (see Scans) when they've been imported, else the procedural rocks.
        static void GorgeBoulders(Transform root, System.Random rng, System.Random variety)
        {
            var field = MapLayout.RiverField;
            var line = field.points;
            float hw = MapLayout.RiverHalfWidth, travelled = 0f;
            for (int i = 1; i < line.Count && travelled < 70f; i++)
            {
                travelled += Vector2.Distance(line[i - 1], line[i]);
                if (travelled < 9f) continue; // under the dam
                Vector2 dir = (line[i] - line[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                foreach (float sign in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() > 0.55) continue;
                    Vector2 p = line[i] + side * sign * (hw + Range(rng, -1.5f, 3.5f));
                    float scale = Range(rng, 0.9f, 2.4f);
                    double roll = rng.NextDouble();
                    Scans.Rock(Scans.Boulders, roll, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.6f + 0.3f * scale, root,
                        roll < 0.5 ? "Scenery_Rock" : "Scenery_Rock_B", scale, 0.25f * scale);
                }
                if (rng.NextDouble() < 0.18)
                {
                    // A boulder in the stream, standing proud of the water.
                    Vector2 p = line[i] + side * Range(rng, -hw * 0.6f, hw * 0.6f);
                    float scale = Range(rng, 0.8f, 1.4f);
                    var rock = Scans.Rock(Scans.Rocks, variety.NextDouble(), new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), scale, root,
                        "Scenery_Rock_B", scale, 0f, sink: 0f);
                    if (rock == null) continue;
                    var pos = rock.transform.position;
                    float height = Scans.Bounds(rock).size.y;
                    rock.transform.position = new Vector3(pos.x, Mathf.Max(pos.y, field.values[i] - 0.4f * height), pos.z);
                }
            }
        }

        // Faces of bare rock (scanned rock faces) on the steep walls of the gorge below the dam, turned to face the
        // river, so the walls read as a rocky gorge rather than steep grass. Two rows a side, low and high on the
        // wall, with gaps; only where the wall is steep, and never down in the water. Its own random numbers, so
        // nothing else on the map moves.
        static void GorgeCliffs(Transform root)
        {
            if (!Scans.Has(Scans.Cliffs[0])) return;
            var cliffs = new GameObject("Gorge Cliffs").transform;
            cliffs.SetParent(root, false);
            var rng = new System.Random(31);
            Vector2 dam = MapLayout.DamPoint, down = MapLayout.DamDown, across = MapLayout.DamAcross;
            float water = MapLayout.RiverField.values[0];
            for (float x = 16f; x <= 44f; x += 4f)
            {
                foreach (float sign in new[] { -1f, 1f })
                {
                    foreach (var (near, far) in new[] { (14.5f, 18f), (20f, 26f) })
                    {
                        Vector2 p = dam + down * (x + Range(rng, -1.5f, 1.5f)) + across * sign * Range(rng, near, far);
                        var at = new Vector3(p.x, 0f, p.y);
                        Vector3 normal = Ground.NormalAt(at);
                        bool steep = normal.y < 0.92f; // more than about 23 degrees
                        if (rng.NextDouble() < 0.25 || !steep || Ground.HeightAt(at) < water + 0.8f) continue;
                        // The face looks downhill, which on a gorge wall is towards the river.
                        float yaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg + Range(rng, -15f, 15f);
                        string model = Scans.Pick(Scans.Cliffs, rng.NextDouble());
                        // Set by the ground at its middle and sunk deep enough that its downhill foot is buried too;
                        // the top stands out of the slope like an outcrop.
                        if (model != null) Scans.Place(model, at, yaw, Range(rng, 0.9f, 1.4f), cliffs, sink: 0.4f, footprint: 0f);
                    }
                }
            }
        }

        // Rocks scattered along the waterline, and a ring of them round the island.
        static void ShoreRocks(Transform root, System.Random rng, System.Random variety)
        {
            Vector2 dam = MapLayout.DamPoint, down = MapLayout.DamDown, across = MapLayout.DamAcross;
            var lobes = MapLayout.Plan.lakeLobes;
            for (int k = 0; k < 640 && lobes.Length > 0; k++)
            {
                var lobe = lobes[k % lobes.Length];
                float angle = k * 2.399963f; // a golden-angle walk samples every lobe without repeating spokes
                Vector2 center = dam + down * lobe.center.x + across * lobe.center.y;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float reach = Mathf.Max(lobe.halfLength, lobe.halfWidth) * 2.5f + Scale.D(14f);
                float r = 0f;
                // Step coarsely across the enlarged lake, then refine the last wet/dry interval to centimetres.
                while (r < reach && MapLayout.ReservoirDistance(center.x + dir.x * r, center.y + dir.y * r) < 0f) r += 4f;
                if (r == 0f || r >= reach) continue;
                float low = r - 4f, high = r;
                for (int j = 0; j < 7; j++)
                {
                    float mid = (low + high) * 0.5f;
                    if (MapLayout.ReservoirDistance(center.x + dir.x * mid, center.y + dir.y * mid) < 0f) low = mid;
                    else high = mid;
                }
                Vector2 p = center + dir * (high + Range(rng, -0.5f, 1.5f));
                if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge
                    || MapLayout.DamLocal(p.x, p.y).x > -12f || rng.NextDouble() > 0.5) continue;
                float scale = Range(rng, 0.6f, 1.9f);
                double roll = rng.NextDouble();
                Scans.Rock(Scans.Rocks, roll, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 0.5f + 0.4f * scale, root,
                    roll < 0.5 ? "Scenery_Rock" : "Scenery_Rock_B", scale, 0.3f * scale);
            }
            for (int i = 0; i < MapLayout.IslandCount; i++)
            {
                Vector2 island = MapLayout.IslandCenter(i);
                float radius = MapLayout.IslandRadius(i);
                for (int k = 0; k < 10; k++)
                {
                    float angle = k * Mathf.PI * 2f / 10f + Range(rng, -0.2f, 0.2f);
                    Vector2 p = island + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * Range(rng, 0.85f, 1.05f);
                    if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) continue;
                    float scale = Range(rng, 0.8f, 1.7f);
                    // Big boulders and smaller rocks mixed, a third of them boulders as before.
                    Scans.Rock(k % 3 == 0 ? Scans.Boulders : Scans.Rocks, variety.NextDouble(), new Vector3(p.x, 0f, p.y),
                        Range(rng, 0f, 360f), k % 3 == 0 ? 0.45f + 0.3f * scale : 0.5f + 0.4f * scale, root,
                        k % 3 == 0 ? "Scenery_Rock" : "Scenery_Rock_B", scale, 0.3f * scale);
                }
            }
        }

        // Conifers and birches over the slopes round the lake, thinning out further from the water, and a clump of
        // pines on the island.
        static void Forest(Transform root, System.Random rng)
        {
            var woods = new GameObject("Lake Woods").transform;
            woods.SetParent(root, false);
            Rect bounds = Water.LakeBounds(Scale.D(75f));
            var candidates = new List<Vector2>();
            const float spacing = 5.6f;
            for (float z = bounds.yMin; z <= bounds.yMax; z += spacing)
                for (float x = bounds.xMin; x <= bounds.xMax; x += spacing)
                {
                    var p = new Vector2(x + Range(rng, -1.2f, 1.2f), z + Range(rng, -1.2f, 1.2f));
                    float shore = MapLayout.ReservoirDistance(p.x, p.y);
                    if (shore < 4f || shore > Scale.D(75f) || !MapLayout.IsWild(p, 0.5f)) continue;
                    // Broad stands with open meadow between them, rather than scattering trees over all the land.
                    if (Mathf.PerlinNoise(p.x / Scale.D(40f) + 5f, p.y / Scale.D(40f) + 9f) < 0.5f) continue;
                    if (rng.NextDouble() > 0.85f - shore / Scale.D(150f)) continue;
                    candidates.Add(p);
                }
            int startCount = World.TreeCount;
            for (int i = candidates.Count - 1; i >= 0 && World.TreeCount - startCount < 2200; i--)
            {
                int pick = rng.Next(i + 1);
                Vector2 p = candidates[pick];
                candidates[pick] = candidates[i];
                double roll = rng.NextDouble();
                string model = roll < 0.4 ? "Scenery_Spruce" : roll < 0.65 ? "Scenery_Pine" : roll < 0.8 ? "Scenery_Pine_B"
                    : roll < 0.92 ? "Scenery_Birch" : "Scenery_Tree";
                World.Place(model, new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.25f), 0.25f, woods);
            }
            // Islands are already included in the signed shore distance above; plant their centres too, with
            // the same real spacing and within the same 2,400-tree allowance for the whole reservoir.
            for (int i = 0; i < MapLayout.IslandCount; i++)
            {
                Vector2 island = MapLayout.IslandCenter(i);
                float radius = MapLayout.IslandRadius(i) - 3f;
                for (float z = -radius; z <= radius && World.TreeCount - startCount < 2400; z += spacing)
                    for (float x = -radius; x <= radius && World.TreeCount - startCount < 2400; x += spacing)
                    {
                        Vector2 offset = new Vector2(x, z) + new Vector2(Range(rng, -1f, 1f), Range(rng, -1f, 1f));
                        Vector2 p = island + offset;
                        if (offset.sqrMagnitude > radius * radius || !MapLayout.IsWild(p, 0.5f)) continue;
                        World.Place(rng.NextDouble() < 0.3 ? "Scenery_Spruce" : "Scenery_Pine", new Vector3(p.x, 0f, p.y),
                            Range(rng, 0f, 360f), Range(rng, 0.8f, 1.15f), 0.25f, woods);
                    }
            }
        }

        // A box of local volumetric fog, softened towards its edges (fade: the fraction of each side that fades).
        // meanFreePath is how far you see into it, in metres: small is thick.
        static void Fog(Transform root, string name, Vector3 at, Vector3 size, Vector2 down, float meanFreePath, float fade)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(down.x, 0f, down.y));
            var fog = go.AddComponent<LocalVolumetricFog>();
            var parameters = fog.parameters;
            parameters.albedo = Color.white;
            parameters.meanFreePath = meanFreePath;
            parameters.size = size;
            parameters.positiveFade = Vector3.one * fade;
            parameters.negativeFade = Vector3.one * fade;
            parameters.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
            parameters.distanceFadeStart = 800f;
            parameters.distanceFadeEnd = 1200f;
            fog.parameters = parameters;
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }

    // Moves the water down the dam's spillway: the streaks of the falling sheets, the churned foam where they land
    // and the ripples of the water on the treads scroll along the water's UVs, which run downhill in metres.
    public class SpillwayFlow : MonoBehaviour
    {
        Material foam, sheet, flow;

        void Start()
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.name.StartsWith("WhiteWater")) foam = m;
                    if (m.name.StartsWith("SpillSheet")) sheet = m;
                    if (m.name.StartsWith("SpillWater")) flow = m;
                }
        }

        void Update()
        {
            // 3.5 m/s for the falling sheets (their texture repeats every 3 m), the landing foam churning slowly
            // (1 m/s, swaying), 2 m/s for the ripples (every 4 m).
            if (sheet != null) sheet.SetTextureOffset("_DetailMap", new Vector2(0f, -Time.time * 3.5f / 3f));
            if (foam != null) foam.SetTextureOffset("_BaseColorMap", new Vector2(Mathf.Sin(Time.time * 0.7f) * 0.05f, -Time.time * 1f / 1.2f));
            if (flow != null) flow.SetTextureOffset("_BaseColorMap", new Vector2(0f, -Time.time * 2f / 4f));
        }
    }
}
