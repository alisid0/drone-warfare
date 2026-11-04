using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The country beyond the map, out to the horizon, so high and low views see land instead of the edge of the
    // world: a ring of rolling farmland and woods, then lines of ever higher ridges out to 12 km, starting exactly
    // at the heights of the terrain's edge so the two meet without a seam. Painted with a patchwork of irregular
    // fields edged with hedgerows, and woods (Resources/Textures/Patchwork, made by Drone Sandbox > Configure
    // Project from Patchwork() below; about 8 m a pixel), which the haze blends into the distance.
    public static class Backdrop
    {
        const int Segments = 512;
        const float Reach = 12000f;
        const float TextureSpan = 16384f; // metres the patchwork covers before it repeats
        static readonly float[] Steps = RingSteps();

        // How far out each ring of the mesh is: 8 m apart at the map's edge, then each 6% further than the last,
        // out to 12 km, so a far hill has about as many triangles across it on screen as a near one (with a
        // couple of dozen rings, far ridges showed as big flat facets).
        static float[] RingSteps()
        {
            var steps = new List<float> { 0f };
            for (float d = 0f; d < Reach;)
            {
                d = Mathf.Min(Reach, d + Mathf.Max(8f, d * 0.06f));
                steps.Add(d);
            }
            return steps.ToArray();
        }

        public static void Build(Transform parent)
        {
            float edge = Ground.Size / 2f - 0.5f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int rings = Steps.Length;
            for (int s = 0; s < Segments; s++)
            {
                float a = s * Mathf.PI * 2f / Segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // Where this direction leaves the square terrain, and how high the ground is there.
                float toEdge = edge / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
                Vector2 start = dir * toEdge;
                float edgeHeight = Ground.HeightAt(new Vector3(start.x, 0f, start.y));
                for (int r = 0; r < rings; r++)
                {
                    Vector2 p = start + dir * Steps[r];
                    float blend = Mathf.SmoothStep(0f, 1f, Steps[r] / 400f);
                    float h = Mathf.Lerp(edgeHeight, Hills(p, Steps[r]), blend);
                    if (r > 0) h -= 0.3f; // tucked just under the terrain's edge
                    vertices.Add(new Vector3(p.x, h, p.y));
                    uvs.Add(p);
                }
            }
            for (int s = 0; s < Segments; s++)
            {
                int a = s * rings, b = ((s + 1) % Segments) * rings;
                for (int r = 0; r < rings - 1; r++)
                    triangles.AddRange(new[] { a + r, b + r, a + r + 1, b + r, b + r + 1, a + r + 1 });
            }

            var mesh = new Mesh { name = "Backdrop", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            if (Vector3.Dot(mesh.normals[1], Vector3.up) < 0f)
            {
                for (int i = 0; i < triangles.Count; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject("Backdrop");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = Art.NewMaterial(Color.white);
            material.mainTexture = Resources.Load<Texture2D>("Textures/Patchwork") ?? Patchwork();
            material.mainTextureScale = Vector2.one / TextureSpan;
            material.mainTextureOffset = Vector2.one * 0.5f;
            material.SetFloat("_Smoothness", 0.05f);
            renderer.sharedMaterial = material;
        }

        // Rolling farmland near the map, rising through lines of ridges: about 80-180 m of relief at 1.5-3 km,
        // 150-350 m from 5 km out (they fade, cooler and paler, into the haze).
        static float Hills(Vector2 p, float distance)
        {
            float h = 6f + 26f * Mathf.PerlinNoise(p.x * 0.0022f + 31f, p.y * 0.0022f + 7f)
                         + 10f * Mathf.PerlinNoise(p.x * 0.009f + 3f, p.y * 0.009f + 51f);
            h += Mathf.SmoothStep(0f, 1f, (distance - 700f) / 1200f) * 140f * Ridge(p, 0.0016f, 9f);
            h += Mathf.SmoothStep(0f, 1f, (distance - 2500f) / 2500f) * 260f * Ridge(p, 0.0007f, 41f);
            h += Mathf.SmoothStep(0f, 1f, (distance - 6000f) / 4000f) * 320f * Ridge(p, 0.00035f, 77f);
            return h;
        }

        // Ridged noise: crests and soft valleys, like hill ranges. The crests are rounded off (a softened fold
        // rather than a sharp one), as weathered hills are.
        static float Ridge(Vector2 p, float scale, float seed)
        {
            float n = Mathf.PerlinNoise(p.x * scale + seed, p.y * scale + seed * 0.37f);
            float fold = n * 2f - 1f;
            float r = Mathf.Clamp01(1.1f - Mathf.Sqrt(fold * fold + 0.02f));
            return r * r * 0.7f + n * 0.3f;
        }

        // Fields in greens and wheat golds, darker woods, and the odd ploughed patch: irregular field shapes (the
        // cells of a jittered Voronoi pattern), hedgerows along their edges, and blobs of woodland over about a fifth
        // of the land. Tiles seamlessly.
        public static Texture2D Patchwork()
        {
            const int size = 2048;
            var rng = new System.Random(41);
            var colours = new[]
            {
                // Mostly pasture and growing crops in summer greens, a third ripening wheat and bare soil.
                new Color(0.33f, 0.42f, 0.17f), new Color(0.40f, 0.48f, 0.20f), new Color(0.52f, 0.50f, 0.24f),
                new Color(0.62f, 0.55f, 0.30f), new Color(0.28f, 0.36f, 0.15f), new Color(0.42f, 0.36f, 0.24f),
                new Color(0.36f, 0.45f, 0.18f), new Color(0.30f, 0.40f, 0.16f), new Color(0.45f, 0.50f, 0.22f),
            };
            const int cells = 96; // fields about 170 m across (the texture covers 16 km)
            var seeds = new Vector2[cells * cells];
            var cellColour = new Color[cells * cells];
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = new Vector2((i % cells) + 0.15f + 0.7f * (float)rng.NextDouble(), (i / cells) + 0.15f + 0.7f * (float)rng.NextDouble());
                cellColour[i] = colours[rng.Next(colours.Length)] * (0.9f + 0.2f * (float)rng.NextDouble());
            }
            var pixels = new Color[size * size];
            var wood = new Color(0.13f, 0.2f, 0.09f);
            float toCells = (float)cells / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Fields: the nearest and second-nearest seed among the nine neighbouring cells (wrapping round).
                    float fx = x * toCells, fy = y * toCells;
                    int gx = (int)fx, gy = (int)fy;
                    float best = float.MaxValue, second = float.MaxValue;
                    int owner = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int cx = gx + ox, cy = gy + oy;
                            int wx = (cx % cells + cells) % cells, wy = (cy % cells + cells) % cells;
                            Vector2 seed = seeds[wy * cells + wx] + new Vector2(cx - wx, cy - wy);
                            float d = (seed - new Vector2(fx, fy)).sqrMagnitude;
                            if (d < best)
                            {
                                second = best;
                                best = d;
                                owner = wy * cells + wx;
                            }
                            else if (d < second) second = d;
                        }
                    }
                    var c = cellColour[owner];
                    // Hedgerows where two fields meet, with gaps.
                    float edge = Mathf.Sqrt(second) - Mathf.Sqrt(best);
                    if (edge < 0.05f && Mathf.PerlinNoise(x * 0.05f + 7f, y * 0.05f) > 0.3f) c = Color.Lerp(c, wood, 0.85f);
                    // Woods: blobs of dark forest over about a fifth of the land.
                    float forest = Mathf.PerlinNoise(x * 0.004f + 11f, y * 0.004f + 23f) * 0.75f + Mathf.PerlinNoise(x * 0.02f, y * 0.02f + 5f) * 0.25f;
                    c = Color.Lerp(c, wood * (0.85f + 0.3f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f)), Mathf.SmoothStep(0f, 1f, (forest - 0.5f) * 8f));
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
