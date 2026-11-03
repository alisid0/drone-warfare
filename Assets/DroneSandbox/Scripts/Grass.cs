using UnityEngine;

namespace DroneSandbox
{
    // Grass and wildflowers on the meadows, and reeds in the shallows, drawn by the terrain as instanced detail
    // meshes: each clump is a ring of cards leaning outward with a painted texture of blades (Resources/Materials/Grass,
    // Flowers and Reeds, made by Drone Sandbox > Configure Project). Grass is thick in patches and thin in
    // others, and kept off roads, fields, water, steep rock and the bare ground of the bases.
    public static class Grass
    {
        const int Resolution = 512;      // detail cells across the terrain (just under a metre each)
        public const float DrawDistance = 120f;

        public static void Plant(Terrain terrain)
        {
            var grassMaterial = Resources.Load<Material>("Materials/Grass");
            var flowerMaterial = Resources.Load<Material>("Materials/Flowers");
            var reedMaterial = Resources.Load<Material>("Materials/Reeds");
            if (terrain == null || grassMaterial == null || flowerMaterial == null || reedMaterial == null) return;

            var tussock = ClumpMesh(40f);
            var data = terrain.terrainData;
            data.detailPrototypes = new[]
            {
                Prototype(Holder("Grass Clump", tussock, grassMaterial, terrain.transform), 0.45f, 0.9f),
                Prototype(Holder("Flower Clump", tussock, flowerMaterial, terrain.transform), 0.4f, 0.75f),
                Prototype(Holder("Reed Clump", ClumpMesh(10f), reedMaterial, terrain.transform), 2.2f, 3.4f),
            };
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(Resolution, 32);

            var grass = new int[Resolution, Resolution];
            var flowers = new int[Resolution, Resolution];
            var reeds = new int[Resolution, Resolution];
            float size = Ground.Size, cell = size / Resolution;
            var roads = MapLayout.RoadField;
            var river = MapLayout.RiverField;
            for (int zi = 0; zi < Resolution; zi++)
            {
                for (int xi = 0; xi < Resolution; xi++)
                {
                    float x = -size / 2f + (xi + 0.5f) * cell, z = -size / 2f + (zi + 0.5f) * cell;
                    var p = new Vector2(x, z);
                    // Reeds in beds along the lake's edge and the riverbanks.
                    float lake = MapLayout.LakeDistance(x, z), bank = river.Distance(x, z) - MapLayout.RiverHalfWidth;
                    bool waterside = (lake > -3f && lake < 1f) || (bank > -1.5f && bank < 1f && !MapLayout.InGorge(p, 0f));
                    if (waterside && Mathf.PerlinNoise(x * 0.06f + 90f, z * 0.06f + 30f) > 0.5f && !MapLayout.NearDam(p, 2f))
                        reeds[zi, xi] = Mathf.PerlinNoise(x * 0.4f, z * 0.4f) > 0.45f ? 2 : 1;
                    if (data.GetSteepness((xi + 0.5f) / Resolution, (zi + 0.5f) / Resolution) > 30f) continue;
                    if (roads.Distance(x, z) < MapLayout.RoadHalfWidth + 0.3f) continue;
                    if (river.Distance(x, z) < MapLayout.RiverHalfWidth + 1f) continue;
                    if (MapLayout.LakeDistance(x, z) < 1f || MapLayout.NearDam(p, 0f)) continue;
                    if (MapLayout.NearFlatSite(p, -1f) || MapLayout.InField(p, 0.3f) || MapLayout.OnWorks(p, -2f)) continue;

                    // Clumped in patches 6-18 m across with open ground between (about two cells in five bare), so
                    // from above the meadow is calm, with tussocks where they gather rather than an even stipple.
                    float n = Mathf.PerlinNoise(x * 0.085f + 7f, z * 0.085f + 3f) * 0.75f + Mathf.PerlinNoise(x * 0.31f, z * 0.31f + 50f) * 0.25f;
                    grass[zi, xi] = n < 0.47f ? 0 : n < 0.6f ? 1 : 2;
                    float bloom = Mathf.PerlinNoise(x * 0.05f + 40f, z * 0.05f + 11f);
                    if (bloom > 0.62f && Mathf.PerlinNoise(x * 0.9f, z * 0.9f) > 0.55f) flowers[zi, xi] = 1;
                }
            }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, flowers);
            data.SetDetailLayer(0, 0, 2, reeds);
            terrain.detailObjectDistance = DrawDistance;
            terrain.detailObjectDensity = 1f;
        }

        static DetailPrototype Prototype(GameObject holder, float minScale, float maxScale)
        {
            return new DetailPrototype
            {
                usePrototypeMesh = true,
                prototype = holder,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = minScale, maxWidth = maxScale,
                minHeight = minScale, maxHeight = maxScale * 1.1f,
                noiseSpread = 0.35f,
                alignToGround = 0.7f,
                healthyColor = Color.white,
                dryColor = Color.white,
            };
        }

        // The terrain copies the mesh and material from this hidden object.
        static GameObject Holder(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, -500f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            return go;
        }

        // A tussock: five cards standing round the middle and leaning out (grass 40 degrees, reeds nearly upright),
        // 0.55 m wide and 0.55 m long, so from the game camera above you look onto the blades fanning out, not
        // along thin crossed cards.
        // Normals all point up, so the grass is lit like the ground it grows from instead of going dark on the side
        // facing away from the sun.
        static Mesh ClumpMesh(float leanDegrees)
        {
            const int cards = 5;
            const float width = 0.55f, length = 0.55f, inner = 0.06f;
            float lean = leanDegrees * Mathf.Deg2Rad;
            var vertices = new Vector3[cards * 4];
            var uvs = new Vector2[cards * 4];
            var sway = new Vector2[cards * 4]; // for the wind shader (WindFoliage): x a phase per card, y how far it moves
            var normals = new Vector3[cards * 4];
            var triangles = new int[cards * 6];
            for (int k = 0; k < cards; k++)
            {
                float a = k * 2f * Mathf.PI / cards;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var half = new Vector3(-outward.z, 0f, outward.x) * width * 0.5f;
                var foot = outward * inner;
                var top = outward * (inner + length * Mathf.Sin(lean)) + Vector3.up * length * Mathf.Cos(lean);
                int v = k * 4;
                vertices[v] = foot - half;
                vertices[v + 1] = foot + half;
                vertices[v + 2] = top + half * 1.3f; // a little wider at the top, as the blades spread
                vertices[v + 3] = top - half * 1.3f;
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);
                for (int i = 0; i < 4; i++)
                {
                    normals[v + i] = Vector3.up;
                    sway[v + i] = new Vector2(k * 0.29f, uvs[v + i].y); // roots stay put, tips sway
                }
                int t = k * 6;
                // Wound so the side facing up and in, the one the camera sees from above, is the front: the
                // shader lights a card's back with its normal flipped, which turned the grass black.
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Grass Clump", vertices = vertices, uv = uvs, uv2 = sway, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
