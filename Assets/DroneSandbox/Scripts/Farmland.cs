using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The farm fields from MapLayout: each is a mesh laid over the terrain with a crop texture drawn in code
    // (ripe wheat in drill rows, rows of a young crop, ploughed furrows, sunflowers, mown hay) repeating every few
    // metres, and over it a detail map the size of the whole field with what shows from the air: tractor
    // tramlines and the headland track, growth patchy in drifts and thin at the edges, raked windrows on hay.
    // Plus round bales on the hay fields, hedgerows along some field edges and poplar rows by the roads.
    public static class Farmland
    {
        const float Tile = 8f;      // metres covered by one repeat of a crop texture
        const int TexSize = 512;
        const float Lift = 0.14f;   // fields float this far above the terrain so it never shows through
        const float DetailCell = 0.25f; // metres per texel of a field's detail map
        const float TramSpacing = 24f;  // sprayer width: one pair of wheel tracks every this many metres
        const float Headland = 9f;      // the strip at each end where the tractor turns

        static readonly Dictionary<Crop, Texture2D> textures = new Dictionary<Crop, Texture2D>();
        static Texture2D gravel;
        static Material fieldTemplate;
        static readonly List<Object> made = new List<Object>(); // each field's own material and detail map, to free

        // Frees the per-field materials and detail maps of the last farmland built (a new sector replaces it).
        public static void Release()
        {
            foreach (var o in made) if (o != null) Object.Destroy(o);
            made.Clear();
        }

        public static void Build(Transform parent, System.Random rng)
        {
            var root = new GameObject("Farmland").transform;
            root.SetParent(parent, false);
            foreach (var plot in MapLayout.Fields)
            {
                BuildField(plot, root, rng);
                if (plot.crop == Crop.MownHay) PlaceBales(plot, root, rng);
                Hedgerows(plot, root, rng);
            }
            PoplarRows(root, rng);
        }

        // ---- Fields ----

        static void BuildField(FieldPlot plot, Transform parent, System.Random rng)
        {
            int nu = Mathf.CeilToInt(plot.halfLength * 2f / 3f), nv = Mathf.CeilToInt(plot.halfWidth * 2f / 3f);
            var vertices = new Vector3[(nu + 1) * (nv + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int j = 0; j <= nv; j++)
            {
                for (int i = 0; i <= nu; i++)
                {
                    float u = -plot.halfLength + 2f * plot.halfLength * i / nu;
                    float v = -plot.halfWidth + 2f * plot.halfWidth * j / nv;
                    Vector2 p = plot.Point(u, v);
                    var world = new Vector3(p.x, 0f, p.y);
                    int k = j * (nu + 1) + i;
                    vertices[k] = new Vector3(p.x, Ground.HeightAt(world) + Lift, p.y);
                    normals[k] = Ground.NormalAt(world);
                    uvs[k] = new Vector2(v / Tile, u / Tile); // texture rows run along the field
                }
            }
            var triangles = new int[nu * nv * 6];
            int t = 0;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }
            var mesh = new Mesh { name = "Field " + plot.crop, vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            // Winding depends on which way 'across' points; make sure the field faces up.
            if (Vector3.Cross(vertices[nu + 1] - vertices[0], vertices[1] - vertices[0]).y < 0f)
            {
                System.Array.Reverse(triangles);
                mesh.triangles = triangles;
            }
            mesh.RecalculateBounds();

            var go = new GameObject("Field " + plot.crop);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Each field a slightly different shade, like real ones.
            float shade = 0.9f + (float)rng.NextDouble() * 0.18f;
            var tint = new Color(shade, shade, shade * (0.94f + (float)rng.NextDouble() * 0.08f));
            if (fieldTemplate == null) fieldTemplate = Resources.Load<Material>("Materials/FieldLit");
            var material = fieldTemplate != null ? new Material(fieldTemplate) { color = tint } : Art.NewMaterial(tint);
            if (Application.isPlaying) made.Add(material);
            material.mainTexture = CropTexture(plot.crop);
            material.SetFloat("_Smoothness", 0f);
            if (fieldTemplate != null)
            {
                // The detail map spans the field once: the UVs run in Tile-sized steps out from the middle.
                material.SetTexture("_DetailMap", FieldDetail(plot));
                material.SetTextureScale("_DetailMap", new Vector2(Tile / (2f * plot.halfWidth), Tile / (2f * plot.halfLength)));
                material.SetTextureOffset("_DetailMap", new Vector2(0.5f, 0.5f));
            }
            renderer.sharedMaterial = material;
        }

        // ---- What shows from the air ----

        // A field's detail map, one byte per 25 cm: HDRP darkens or lightens the crop by how far it is from a half
        // (FieldLit ignores the detail map's normal and smoothness, so the red channel is all it needs).
        // Its pattern is picked by where the field lies, not from the map's random numbers, so the rest of the map
        // (built from the same sequence) comes out as before.
        static Texture2D FieldDetail(FieldPlot plot)
        {
            float width = plot.halfWidth * 2f, length = plot.halfLength * 2f;
            int w = Mathf.Clamp(Mathf.CeilToInt(width / DetailCell), 4, 1024);
            int h = Mathf.Clamp(Mathf.CeilToInt(length / DetailCell), 4, 1024);
            float seed = Mathf.Repeat(plot.center.x * 12.9898f + plot.center.y * 78.233f, 100f);
            float tramOffset = Mathf.Repeat(plot.center.x * 3.7f + plot.center.y * 1.3f, TramSpacing);
            var bytes = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                float along = (y + 0.5f) / h * length;
                for (int x = 0; x < w; x++)
                {
                    float across = (x + 0.5f) / w * width;
                    float f = Brightness(plot.crop, across, along, width, length, seed, tramOffset);
                    bytes[y * w + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(DetailValue(f) * 255f), 0, 255);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.R8, true, true)
            {
                name = plot.crop + " field detail", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, filterMode = FilterMode.Trilinear,
            };
            tex.SetPixelData(bytes, 0);
            tex.Apply(true, true);
            if (Application.isPlaying) made.Add(tex);
            return tex;
        }

        // How much lighter (above 1) or darker the crop is at this spot, in metres across and along the field.
        static float Brightness(Crop crop, float across, float along, float width, float length, float seed, float tramOffset)
        {
            // Drifts of better and worse growth (soil, damp, how the seed fell), bigger and smaller.
            float drift = Mathf.PerlinNoise(across * 0.03f + seed, along * 0.03f + seed * 0.5f) * 0.65f
                        + Mathf.PerlinNoise(across * 0.11f + seed * 2f, along * 0.11f + 7f) * 0.35f;
            float patchy = crop == Crop.Ploughed ? 0.5f : crop == Crop.YoungCrop ? 0.34f : crop == Crop.MownHay ? 0.2f : 0.28f;
            float f = 1f + (drift - 0.5f) * patchy;

            // Thinner and trampled along the edges (ploughed soil turned up paler).
            float edge = Mathf.Min(Mathf.Min(across, width - across), Mathf.Min(along, length - along));
            f *= Mathf.Lerp(crop == Crop.Ploughed ? 1.06f : 0.84f, 1f, Mathf.SmoothStep(0f, 1f, edge / 2.5f));

            if (crop == Crop.Ploughed)
            {
                // The headlands, ploughed last and the other way, come out a shade lighter.
                if (along < Headland || along > length - Headland) f *= 1.07f;
                return f;
            }

            if (crop == Crop.MownHay)
            {
                // Raked windrows of drying hay every 7 m, a little paler than the stubble between.
                float row = Mathf.Abs(Mathf.Repeat(across + tramOffset, 7f) - 3.5f);
                f *= Mathf.Lerp(1.22f, 0.96f, Mathf.SmoothStep(0f, 1f, (row - 0.3f) / 0.5f));
            }

            // Tramlines: a pair of wheel tracks 1.8 m apart every 24 m, along the field and stopping at the
            // headland track, which runs across both ends (and round the sides on wheat).
            float dark = crop == Crop.Wheat ? 0.72f : crop == Crop.MownHay ? 0.85f : 0.7f;
            float Track(float d) => Mathf.Lerp(dark, 1f, Mathf.SmoothStep(0f, 1f, (Mathf.Abs(d - 0.9f) - 0.12f) / 0.2f));
            float headlandTrack = Headland * 0.5f;
            bool inside = along > headlandTrack - 1f && along < length - headlandTrack + 1f;
            float t = Mathf.Abs(Mathf.Repeat(across - tramOffset, TramSpacing) - TramSpacing * 0.5f);
            if (inside && t < 1.5f) f *= Track(t);
            float ends = Mathf.Min(Mathf.Abs(along - headlandTrack), Mathf.Abs(along - (length - headlandTrack)));
            if (ends < 1.5f && across > headlandTrack - 1f && across < width - headlandTrack + 1f) f *= Track(ends);
            if (crop == Crop.Wheat)
            {
                float sides = Mathf.Min(Mathf.Abs(across - headlandTrack), Mathf.Abs(across - (width - headlandTrack)));
                if (sides < 1.5f && inside) f *= Track(sides);
                // Patches knocked flat by wind and rain, which catch the light.
                float lodged = Mathf.PerlinNoise(across * 0.09f + seed * 3f, along * 0.09f + 40f);
                f *= 1f + 0.14f * Mathf.SmoothStep(0f, 1f, (lodged - 0.72f) * 8f);
            }
            return f;
        }

        // The detail map value for a brightness: HDRP blends toward black (or white) by the square of how far the
        // value is from a half, in square-root space, so this undoes that (lightening is about right for the
        // crops' mid-dark colours).
        static float DetailValue(float f)
        {
            if (f <= 1f) return 0.5f - 0.5f * Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Sqrt(f)));
            return 0.5f + 0.5f * Mathf.Sqrt(Mathf.Clamp01(0.82f * (Mathf.Sqrt(f) - 1f)));
        }

        static void PlaceBales(FieldPlot plot, Transform parent, System.Random rng)
        {
            int count = rng.Next(5, 13);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = plot.Point(Range(rng, -0.8f, 0.8f) * plot.halfLength, Range(rng, -0.8f, 0.8f) * plot.halfWidth);
                World.Place("Scenery_HayBale", new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), 1f, 0.05f, parent);
            }
        }

        // Bushes (and the odd tree) along some field edges, just outside the field.
        static void Hedgerows(FieldPlot plot, Transform parent, System.Random rng)
        {
            for (int edge = 0; edge < 4; edge++)
            {
                if (rng.NextDouble() > (edge % 2 == 0 ? 0.35 : 0.2)) continue;
                Vector2 normal = edge switch { 0 => plot.Across, 1 => plot.along, 2 => -plot.Across, _ => -plot.along };
                Vector2 tangent = new Vector2(-normal.y, normal.x);
                float halfSpan = edge % 2 == 0 ? plot.halfLength : plot.halfWidth;
                float offset = (edge % 2 == 0 ? plot.halfWidth : plot.halfLength) + 1.8f;
                for (float s = -halfSpan; s <= halfSpan; s += Range(rng, 2.4f, 3.4f))
                {
                    Vector2 p = plot.center + normal * offset + tangent * s + Random2(rng) * 0.5f;
                    if (!HedgeSpot(p)) continue;
                    var pos = new Vector3(p.x, 0f, p.y);
                    if (rng.NextDouble() < 0.12)
                        World.Place(rng.NextDouble() < 0.6 ? "Scenery_Tree" : "Scenery_Birch", pos, Range(rng, 0f, 360f), Range(rng, 0.75f, 1.0f), 0.2f, parent);
                    else
                        World.Place("Scenery_Bush", pos, Range(rng, 0f, 360f), Range(rng, 1.0f, 1.5f), 0.15f, parent);
                }
            }
        }

        static bool HedgeSpot(Vector2 p)
        {
            if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 1.2f) return false;
            if (MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 2f) return false;
            if (MapLayout.NearFlatSite(p, 2f) || MapLayout.NearVillage(p, -4f)) return false;
            return !MapLayout.InField(p, 0.3f);
        }

        // Lines of tall poplars beside stretches of road.
        static void PoplarRows(Transform parent, System.Random rng)
        {
            foreach (var line in MapLayout.RoadLines)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                bool planting = false;
                float runLeft = Range(rng, 20f, 60f), next = 0f, walked = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    float step = Vector2.Distance(line[i - 1], line[i]);
                    walked += step;
                    runLeft -= step;
                    if (runLeft <= 0f)
                    {
                        planting = !planting && rng.NextDouble() < 0.55;
                        runLeft = planting ? Range(rng, 40f, 90f) : Range(rng, 30f, 80f);
                        if (!planting) side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    }
                    if (!planting || walked < next) continue;
                    next = walked + Range(rng, 5.5f, 7f);
                    Vector2 dir = (line[i] - line[i - 1]).normalized;
                    Vector2 p = line[i] + new Vector2(-dir.y, dir.x) * side * (MapLayout.RoadHalfWidth + 2.6f);
                    if (Mathf.Abs(p.x) > 215f || Mathf.Abs(p.y) > 215f || !HedgeSpot(p)) continue;
                    World.Place("Scenery_Poplar", new Vector3(p.x, 0f, p.y), Range(rng, 0f, 360f), Range(rng, 0.85f, 1.1f), 0.2f, parent);
                }
            }
        }

        // ---- Textures ----

        public static Texture2D CropTexture(Crop crop)
        {
            if (textures.TryGetValue(crop, out var tex) && tex != null) return tex;
            var rng = new System.Random((int)crop + 17);
            float[] coarse = Ground.Lattice(16, rng), fine = Ground.Lattice(64, rng), grain = Ground.Lattice(128, rng);
            var pixels = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float mx = x * Tile / TexSize, my = y * Tile / TexSize; // metres across (x) and along (y) the rows
                    float n1 = Ground.SampleLattice(coarse, 16, x * 16f / TexSize, y * 16f / TexSize);
                    float n2 = Ground.SampleLattice(fine, 64, x * 64f / TexSize, y * 64f / TexSize);
                    float n3 = Ground.SampleLattice(grain, 128, x * 128f / TexSize, y * 128f / TexSize);
                    pixels[y * TexSize + x] = CropColor(crop, mx, my, n1, n2, n3);
                }
            }
            tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true)
            {
                name = crop + " field", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            textures[crop] = tex;
            return tex;
        }

        static Color32 CropColor(Crop crop, float mx, float my, float n1, float n2, float n3)
        {
            Color c;
            switch (crop)
            {
                case Crop.Wheat:
                {
                    // Ripe wheat with faint drill rows (the tramlines are in the field's detail map).
                    c = Color.Lerp(new Color(0.70f, 0.55f, 0.27f), new Color(0.84f, 0.70f, 0.40f), n1 * 0.6f + n3 * 0.4f);
                    float row = Mathf.Abs(Mathf.Sin(mx * Mathf.PI / 0.25f));
                    c *= 0.93f + 0.07f * row;
                    break;
                }
                case Crop.YoungCrop:
                {
                    // Green rows on brown soil, 0.8 m apart.
                    var soil = Color.Lerp(new Color(0.33f, 0.25f, 0.17f), new Color(0.44f, 0.34f, 0.24f), n2);
                    var leaf = Color.Lerp(new Color(0.22f, 0.36f, 0.10f), new Color(0.36f, 0.50f, 0.16f), n3);
                    float across = Mathf.Abs(Mathf.Repeat(mx, 0.8f) - 0.4f) / 0.4f; // 0 on a row, 1 between
                    float cover = Mathf.SmoothStep(0f, 1f, (0.62f + (n3 - 0.5f) * 0.35f + (n1 - 0.5f) * 0.2f - across) * 5f);
                    c = Color.Lerp(soil, leaf, cover);
                    break;
                }
                case Crop.Ploughed:
                {
                    float furrow = 0.5f + 0.5f * Mathf.Sin(mx * Mathf.PI * 2f / 0.4f);
                    c = Color.Lerp(new Color(0.24f, 0.17f, 0.11f), new Color(0.42f, 0.32f, 0.22f), furrow * 0.7f + n3 * 0.3f);
                    c *= 0.88f + 0.2f * n1;
                    break;
                }
                case Crop.Sunflower:
                {
                    // Yellow flower heads in rows over dark green leaves.
                    c = Color.Lerp(new Color(0.14f, 0.24f, 0.07f), new Color(0.26f, 0.38f, 0.12f), n3 * 0.7f + n2 * 0.3f);
                    float cx = Mathf.Repeat(mx, 0.8f) - 0.4f, cy = Mathf.Repeat(my + (n2 - 0.5f) * 0.25f, 0.4f) - 0.2f;
                    float r = Mathf.Sqrt(cx * cx * 1.2f + cy * cy * 4f);
                    if (r < 0.17f) c = r < 0.06f ? new Color(0.30f, 0.18f, 0.06f) : Color.Lerp(new Color(0.95f, 0.72f, 0.10f), new Color(0.85f, 0.58f, 0.06f), n3);
                    break;
                }
                default:
                {
                    // Mown hay: pale stubble in alternating 2 m swaths.
                    float swath = Mathf.Repeat(mx, 4f) < 2f ? 1f : 0.9f;
                    c = Color.Lerp(new Color(0.55f, 0.56f, 0.30f), new Color(0.70f, 0.68f, 0.40f), n2 * 0.5f + n3 * 0.5f) * swath;
                    break;
                }
            }
            c.a = 1f;
            return c;
        }

        // Grey gravel for your base's yard.
        public static Texture2D GravelTexture()
        {
            if (gravel != null) return gravel;
            var rng = new System.Random(99);
            float[] coarse = Ground.Lattice(8, rng), fine = Ground.Lattice(128, rng), grain = Ground.Lattice(256, rng);
            var pixels = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float n1 = Ground.SampleLattice(coarse, 8, x * 8f / TexSize, y * 8f / TexSize);
                    float n2 = Ground.SampleLattice(fine, 128, x * 128f / TexSize, y * 128f / TexSize);
                    float n3 = Ground.SampleLattice(grain, 256, x * 256f / TexSize, y * 256f / TexSize);
                    float v = 0.42f + 0.22f * n2 + 0.12f * n3 + 0.08f * n1;
                    var c = new Color(v, v * 0.98f, v * 0.94f);
                    if (n2 > 0.72f) c *= 0.75f; // darker stones
                    c.a = 1f;
                    pixels[y * TexSize + x] = c;
                }
            }
            gravel = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true)
            {
                name = "Gravel", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear,
            };
            gravel.SetPixels32(pixels);
            gravel.Apply(true, true);
            return gravel;
        }

        static Vector2 Random2(System.Random rng) => new Vector2((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
