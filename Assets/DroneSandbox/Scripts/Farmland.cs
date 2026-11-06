using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // The farm fields from MapLayout: each is a mesh laid over the terrain with a crop texture drawn in code
    // (ripe wheat in drill rows, rows of a young crop, ploughed furrows, sunflowers, mown hay) repeating every few
    // metres, and over it a detail map the size of the whole field with what shows from the air: tractor
    // tramlines and the headland track, growth patchy in drifts and thin at the edges, raked windrows on hay.
    // Plus round bales, one boundary per shared parcel edge and poplar rows by the roads.
    public static class Farmland
    {
        const float Tile = 8f;      // metres covered by one repeat of a crop texture
        const int TexSize = 512;
        const int RendererLimit = 5400; // includes field surfaces and every hidden LOD of their props
        // Fields float this far above the terrain so it never shows through. The terrain simplifies its mesh with
        // distance (up to 2 px of error), which at 0.14 m pushed it up through the crops as green blotches from the
        // play camera; Ground also paints bare soil under every field, so what still shows through is earth.
        const float Lift = 0.35f;
        const float DetailCell = 0.5f; // metres per texel of a field's detail map (the fields are twice the old size)
        const float TramSpacing = 24f;  // sprayer width: one pair of wheel tracks every this many metres
        const float Headland = 9f;      // the strip at each end where the tractor turns

        static readonly Dictionary<Crop, Texture2D> textures = new Dictionary<Crop, Texture2D>();
        static Texture2D gravel;
        static Material fieldTemplate;
        static readonly List<Object> made = new List<Object>(); // each field's mesh, material and detail map, to free
        // One detail map per world parcel: a parcel cut into pieces by roads or the sector edge shares its parent's
        // map, which each piece used to paint again in full (fields 13.4 s in hd143's river valley).
        static readonly Dictionary<FieldPlot, Texture2D> details = new Dictionary<FieldPlot, Texture2D>();

        // Frees the per-field meshes, materials and detail maps when a new sector replaces them.
        public static void Release()
        {
            foreach (var o in made) Art.Discard(o);
            made.Clear();
            details.Clear();
        }

        // Builds every field surface, then shares the prop allowance between fields so a large farm count
        // cannot turn into tens of thousands of hedge renderers. Poplar avenues get a separate small allowance.
        public static void Build(Transform parent, System.Random rng)
        {
            Release();
            // Field count and boundary detail must not advance the stream villages and woods share. Switching to
            // this stream changes the old scenery sequence once; future farmland edits leave it alone.
            rng = new System.Random(MapLayout.Plan.worldSeed ^ 0x31a7);
            var root = new GameObject("Farmland").transform;
            root.SetParent(parent, false);
            foreach (var plot in MapLayout.Fields) BuildField(plot, root, rng);
            int remaining = Mathf.Max(0, RendererLimit - MapLayout.Fields.Count);
            int rows = Mathf.Min(480, remaining / 8);
            remaining -= rows;
            // Pay for every boundary's inexpensive silhouette first; fences and mature trees share what remains.
            foreach (var edge in MapLayout.Boundaries) BuildBoundary(edge, root, ref remaining);
            int decoration = remaining;
            int perEdge = decoration / Mathf.Max(1, MapLayout.Boundaries.Count);
            foreach (var edge in MapLayout.Boundaries)
            {
                int allowance = perEdge;
                BoundaryProps(edge, root, rng, ref allowance);
                remaining -= perEdge - allowance;
            }
            int perField = remaining / Mathf.Max(1, MapLayout.Fields.Count);
            foreach (var plot in MapLayout.Fields)
            {
                int allowance = perField;
                if (plot.crop == Crop.MownHay) PlaceBales(plot, root, rng, ref allowance);
            }
            PoplarRows(root, rng, ref rows);
        }

        // ---- Fields ----

        // One ground-following mesh and renderer per field; the 3 m grid keeps the real furrows on the ground.
        static void BuildField(FieldPlot plot, Transform parent, System.Random rng)
        {
            bool worldParcel = FarFieldSurface.TryParent(plot, out var surface);
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
                    // World rows use physical metres, independent of taper, clipping and sector translation.
                    uvs[k] = worldParcel ? FarFieldSurface.RowUv(surface, p, Tile) : new Vector2(v / Tile, u / Tile);
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
            var mesh = new Mesh { name = "Field " + plot.crop, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            // Winding depends on which way 'across' points; make sure the field faces up.
            if (Vector3.Cross(vertices[nu + 1] - vertices[0], vertices[1] - vertices[0]).y < 0f)
            {
                System.Array.Reverse(triangles);
                mesh.triangles = triangles;
            }
            mesh.RecalculateBounds();
            made.Add(mesh);

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
            made.Add(material);
            material.mainTexture = CropTexture(plot.crop);
            material.SetFloat("_Smoothness", 0f);
            if (fieldTemplate != null)
            {
                // The detail map spans the field once: the UVs run in Tile-sized steps out from the middle.
                if (!details.TryGetValue(surface, out var detail)) details[surface] = detail = FieldDetail(surface);
                material.SetTexture("_DetailMap", detail);
                material.SetTextureScale("_DetailMap", new Vector2(Tile / (2f * surface.halfWidth), Tile / (2f * surface.halfLength)));
                Vector2 offset = new Vector2(0.5f, 0.5f);
                if (worldParcel)
                {
                    Vector2 phase = FarFieldSurface.RowUv(surface, surface.center, 1f);
                    offset -= new Vector2(phase.x / (2f * surface.halfWidth), phase.y / (2f * surface.halfLength));
                }
                material.SetTextureOffset("_DetailMap", offset);
            }
            renderer.sharedMaterial = material;
        }

        // ---- What shows from the air ----

        // A field's detail map, one byte per 25 cm: HDRP darkens or lightens the crop by how far it is from a half
        // (FieldLit ignores the detail map's normal and smoothness, so the red channel is all it needs).
        // Low-frequency growth is sampled on a 2 m grid (about 64 times fewer noise calls); wheel tracks still
        // use 50 cm texels. Its pattern is picked by where the field lies, not from the map's random numbers, so the rest of the map
        // (built from the same sequence) comes out as before.
        static Texture2D FieldDetail(FieldPlot plot)
        {
            float width = plot.halfWidth * 2f, length = plot.halfLength * 2f;
            int w = Mathf.Clamp(Mathf.CeilToInt(width / DetailCell), 4, 1024);
            int h = Mathf.Clamp(Mathf.CeilToInt(length / DetailCell), 4, 1024);
            float seed = Mathf.Repeat(plot.center.x * 12.9898f + plot.center.y * 78.233f, 100f);
            float tramOffset = Mathf.Repeat(plot.center.x * 3.7f + plot.center.y * 1.3f, TramSpacing);
            int nw = Mathf.CeilToInt(width / 2f) + 1, nh = Mathf.CeilToInt(length / 2f) + 1;
            var growth = new Vector2[nw * nh];
            // Rows on all cores: a world parcel's map can be a quarter of a million texels, and a sector has hundreds.
            System.Threading.Tasks.Parallel.For(0, nh, y =>
            {
                for (int x = 0; x < nw; x++)
                {
                    float across = x * 2f, along = y * 2f;
                    growth[y * nw + x] = new Vector2(
                        Mathf.PerlinNoise(across * 0.03f + seed, along * 0.03f + seed * 0.5f) * 0.65f
                            + Mathf.PerlinNoise(across * 0.11f + seed * 2f, along * 0.11f + 7f) * 0.35f,
                        plot.crop == Crop.Wheat ? Mathf.PerlinNoise(across * 0.09f + seed * 3f, along * 0.09f + 40f) : 0f);
                }
            });
            var bytes = new byte[w * h];
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                float along = (y + 0.5f) / h * length;
                int gy = Mathf.Min((int)(along / 2f), nh - 2);
                float fy = along / 2f - gy;
                for (int x = 0; x < w; x++)
                {
                    float across = (x + 0.5f) / w * width;
                    int gx = Mathf.Min((int)(across / 2f), nw - 2), g = gy * nw + gx;
                    float fx = across / 2f - gx;
                    Vector2 noise = Vector2.Lerp(Vector2.Lerp(growth[g], growth[g + 1], fx),
                        Vector2.Lerp(growth[g + nw], growth[g + nw + 1], fx), fy);
                    float f = Brightness(plot.crop, across, along, width, length, seed, tramOffset, noise);
                    bytes[y * w + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(DetailValue(f) * 255f), 0, 255);
                }
            });
            var tex = new Texture2D(w, h, TextureFormat.R8, true, true)
            {
                name = plot.crop + " field detail", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, filterMode = FilterMode.Trilinear,
            };
            tex.SetPixelData(bytes, 0);
            tex.Apply(true, true);
            made.Add(tex);
            return tex;
        }

        // How much lighter (above 1) or darker the crop is at this spot, in metres across and along the field.
        static float Brightness(Crop crop, float across, float along, float width, float length, float seed, float tramOffset, Vector2 noise)
        {
            // Drifts of better and worse growth (soil, damp, how the seed fell), bigger and smaller.
            float drift = noise.x;
            float patchy = crop == Crop.Ploughed ? 0.5f : crop == Crop.YoungCrop ? 0.34f : crop == Crop.MownHay ? 0.2f : 0.28f;
            float f = 1f + (drift - 0.5f) * patchy;
            // A paddock too small for the big machines shows no headland or tramlines (drawn there they make a
            // stamped frame), and of the big fields only about two in three show them clearly.
            bool worked = length > 40f && width > 28f && Mathf.Repeat(seed * 7.13f, 1f) < 0.65f;

            // Thinner and trampled along the edges (ploughed soil turned up paler).
            float edge = Mathf.Min(Mathf.Min(across, width - across), Mathf.Min(along, length - along));
            f *= Mathf.Lerp(crop == Crop.Ploughed ? 1.06f : 0.84f, 1f, Mathf.SmoothStep(0f, 1f, edge / 2.5f));

            if (crop == Crop.Ploughed)
            {
                // The headlands, ploughed last and the other way, come out a shade lighter.
                if (worked && (along < Headland || along > length - Headland)) f *= 1.07f;
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
            if (worked && inside && t < 1.5f) f *= Track(t);
            float ends = Mathf.Min(Mathf.Abs(along - headlandTrack), Mathf.Abs(along - (length - headlandTrack)));
            if (worked && ends < 1.5f && across > headlandTrack - 1f && across < width - headlandTrack + 1f) f *= Track(ends);
            if (crop == Crop.Wheat)
            {
                float sides = Mathf.Min(Mathf.Abs(across - headlandTrack), Mathf.Abs(across - (width - headlandTrack)));
                if (worked && sides < 1.5f && inside) f *= Track(sides);
                // Patches knocked flat by wind and rain, which catch the light.
                float lodged = noise.y;
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

        // Round bales left where the baler dropped them: in a cluster or two, not sprinkled evenly.
        static void PlaceBales(FieldPlot plot, Transform parent, System.Random rng, ref int allowance)
        {
            int count = rng.Next(5, 9), clusters = rng.Next(1, 3);
            for (int k = 0; k < clusters; k++)
            {
                float cu = Range(rng, -0.6f, 0.6f) * plot.halfLength, cv = Range(rng, -0.6f, 0.6f) * plot.halfWidth;
                for (int i = 0; i < count / clusters; i++)
                {
                    float u = Mathf.Clamp(cu + Range(rng, -7f, 7f), -0.85f * plot.halfLength, 0.85f * plot.halfLength);
                    float v = Mathf.Clamp(cv + Range(rng, -5f, 5f), -0.85f * plot.halfWidth, 0.85f * plot.halfWidth);
                    Vector2 p = plot.Point(u, v);
                    PlaceProp("Scenery_HayBale", p, Range(rng, 0f, 360f), 1f, 0.05f, parent, ref allowance);
                }
            }
        }

        // An entrance is kept open for 4 m, and an existing track always has at least 4 m of clear width.
        static bool BoundaryGap(FieldBoundary edge, float t, float length, float extra = 0f)
        {
            float gate = 0.25f + Mathf.Repeat(edge.a.x * 0.017f + edge.a.y * 0.031f, 0.5f);
            if (Mathf.Abs(t - gate) * length < 2f + extra) return true;
            Vector2 p = edge.Point(t);
            return MapLayout.TrackField.Distance(p.x, p.y) < 2.2f + extra;
        }

        // One cheap, ground-following boundary mesh per shared edge. Hedges are lumpy five-sided volumes using the
        // existing leaf material; ditches are a dark damp ribbon, banks are a low ridge under fence runs.
        static void BuildBoundary(FieldBoundary edge, Transform parent, ref int allowance)
        {
            if (edge.kind == FieldBoundaryKind.Open) return;
            bool hedge = edge.kind == FieldBoundaryKind.Hedge, ditch = edge.kind == FieldBoundaryKind.Ditch;
            int cost = hedge ? 2 : 1;
            if (allowance < cost) return;
            float length = Vector2.Distance(edge.a, edge.b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length)), sides = ditch ? 3 : 5;
            var vertices = new Vector3[(steps + 1) * sides];
            var uvs = new Vector2[vertices.Length];
            var triangles = new List<int>(steps * (sides - 1) * 6);
            float seed = Mathf.Repeat(edge.a.x * 1.7f + edge.b.y * 2.3f, 100f);
            float width = ditch ? 0.65f + seed % 0.8f : hedge ? 0.95f + seed % 0.9f : 0.9f;
            float height = hedge ? 1.45f + seed % 0.7f : 0.35f;
            Vector2 dir = (edge.b - edge.a).normalized, normal = new Vector2(-dir.y, dir.x);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 p = edge.Point(t);
                float lump = 0.96f + 0.07f * Mathf.Sin(i * 2.1f + seed) + 0.03f * Mathf.Sin(i * 0.73f + seed);
                for (int j = 0; j < sides; j++)
                {
                    float angle = j * Mathf.PI / (sides - 1);
                    Vector2 q = p + normal * (Mathf.Cos(angle) * width * 0.5f * (hedge ? lump : 1f));
                    var at = new Vector3(q.x, 0f, q.y);
                    // A ditch's centre sits lower than its raised damp rims, all just above the terrain surface.
                    float rise = ditch ? 0.025f + 0.055f * Mathf.Abs(Mathf.Cos(angle)) : Mathf.Sin(angle) * height * lump;
                    vertices[i * sides + j] = new Vector3(q.x, Ground.HeightAt(at) + 0.04f + rise, q.y);
                    uvs[i * sides + j] = new Vector2(i * length / steps, j * width / (sides - 1));
                }
                if (i == 0 || BoundaryGap(edge, (i - 0.5f) / steps, length)) continue;
                for (int j = 0; j < sides - 1; j++)
                {
                    int a = (i - 1) * sides + j, b = i * sides + j;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            if (triangles.Count == 0) return;
            if (hedge)
                for (int i = 0; i <= steps; i++)
                {
                    bool before = i > 0 && !BoundaryGap(edge, (i - 0.5f) / steps, length);
                    bool after = i < steps && !BoundaryGap(edge, (i + 0.5f) / steps, length);
                    if (before == after) continue;
                    // Close the cut ends at gateways too, so a close view cannot look inside a hollow hedge.
                    int a = i * sides;
                    for (int j = 1; j < sides - 1; j++)
                    {
                        triangles.Add(a);
                        triangles.Add(a + (after ? j : j + 1));
                        triangles.Add(a + (after ? j + 1 : j));
                    }
                }
            var mesh = new Mesh { name = "Parcel " + edge.kind, vertices = vertices, uv = uvs, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            made.Add(mesh);
            var material = hedge ? Resources.Load<Material>("Materials/PBR/BushLeaves") : null;
            if (material == null) material = Art.Shared(hedge ? new Color(0.07f, 0.13f, 0.025f)
                : ditch ? new Color(0.105f, 0.13f, 0.085f) : new Color(0.22f, 0.24f, 0.13f));
            var go = new GameObject("Parcel " + edge.kind);
            go.transform.SetParent(parent, false);
            var near = BoundaryRenderer(mesh, material, go.transform, "Near", hedge);
            if (hedge)
            {
                var far = BoundaryRenderer(mesh, material, go.transform, "Far (no shadows)", false);
                var lod = go.AddComponent<LODGroup>();
                // A fixed proxy size gives even a 200 m hedge a valid transition height at 150 m, as in Art.TuneLods.
                lod.localReferencePoint = mesh.bounds.center;
                lod.size = 10f;
                float projected = 10f / (2f * Mathf.Tan(Scale.CameraFov * 0.5f * Mathf.Deg2Rad));
                lod.SetLODs(new[] { new LOD(projected / 150f, new Renderer[] { near }), new LOD(projected / 3000f, new Renderer[] { far }) });
            }
            allowance -= cost;
        }

        // Both hedge LODs share one mesh and material; only the nearby one casts shadows.
        static MeshRenderer BoundaryRenderer(Mesh mesh, Material material, Transform parent, string name, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        // Mature trees punctuate some hedges at real 35-80 m intervals. Fence runs use the existing 3 m model,
        // leaving the low bank readable along the whole edge even when the model allowance is exhausted.
        static void BoundaryProps(FieldBoundary edge, Transform parent, System.Random rng, ref int allowance)
        {
            bool tree = edge.kind == FieldBoundaryKind.Hedge && rng.NextDouble() < 0.4;
            if (!tree && edge.kind != FieldBoundaryKind.Fence) return;
            float length = Vector2.Distance(edge.a, edge.b);
            float step = tree ? Range(rng, 35f, 80f) : 3.1f;
            float start = tree ? Range(rng, 12f, 30f) : 1.6f;
            for (float s = start; s < length - 1.6f && allowance > 0; s += step)
            {
                if (BoundaryGap(edge, s / length, length, tree ? 3f : 1.6f)) continue;
                Vector2 p = edge.Point(s / length);
                if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 2f) continue;
                Vector2 dir = edge.Point(Mathf.Min(1f, (s + 1f) / length)) - p;
                float yaw = tree ? Range(rng, 0f, 360f) : -Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                PlaceProp(tree ? "Scenery_Tree" : "Scenery_Fence", p, yaw, tree ? Range(rng, 0.85f, 1.1f) : 1f, tree ? 0.2f : 0.02f, parent, ref allowance);
            }
        }

        // Pays for the actual model variant before creating it, including renderers in inactive LODs.
        static void PlaceProp(string model, Vector2 p, float yaw, float scale, float sink, Transform parent, ref int allowance)
        {
            var pos = new Vector3(p.x, 0f, p.y);
            int cost = Art.ModelRendererCount(model, pos);
            if (cost > allowance) return;
            if (World.Place(model, pos, yaw, scale, sink, parent) != null) allowance -= cost;
        }

        // Hedges stay inside the map and clear of roads, water, buildings and the crop itself.
        static bool HedgeSpot(Vector2 p)
        {
            if (Mathf.Abs(p.x) > MapLayout.Edge || Mathf.Abs(p.y) > MapLayout.Edge) return false;
            if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 1.2f) return false;
            if (MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 2f) return false;
            if (MapLayout.NearFlatSite(p, 2f) || MapLayout.NearVillage(p, -4f)) return false;
            return !MapLayout.InField(p, 0.3f);
        }

        // Lines of tall poplars beside stretches of road. Walk in real metres so avenue spacing is independent
        // of the road curve's vertex spacing, while the runs and gaps grow with the layout.
        static void PoplarRows(Transform parent, System.Random rng, ref int allowance)
        {
            foreach (var line in MapLayout.RoadLines)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                bool planting = false;
                float runLeft = Scale.D(Range(rng, 20f, 60f)), next = 0f, walked = 0f;
                for (int i = 1; i < line.Count && allowance > 0; i++)
                {
                    float length = Vector2.Distance(line[i - 1], line[i]);
                    if (length < 0.001f) continue;
                    Vector2 dir = (line[i] - line[i - 1]) / length;
                    while (next <= walked + length && allowance > 0)
                    {
                        Vector2 at = Vector2.Lerp(line[i - 1], line[i], (next - walked) / length);
                        float step = Range(rng, 5.5f, 7f);
                        next += step;
                        runLeft -= step;
                        if (runLeft <= 0f)
                        {
                            planting = !planting && rng.NextDouble() < 0.55;
                            runLeft = Scale.D(planting ? Range(rng, 40f, 90f) : Range(rng, 30f, 80f));
                            if (!planting) side = rng.NextDouble() < 0.5 ? -1f : 1f;
                        }
                        if (!planting) continue;
                        Vector2 p = at + new Vector2(-dir.y, dir.x) * side * (MapLayout.RoadHalfWidth + 2.6f);
                        if (!HedgeSpot(p)) continue;
                        PlaceProp("Scenery_Poplar", p, Range(rng, 0f, 360f), Range(rng, 0.85f, 1.1f), 0.2f, parent, ref allowance);
                    }
                    walked += length;
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
                    // Ripe wheat with faint drill rows (the tramlines are in the field's detail map): a dusty straw
                    // gold, around (171, 150, 94), not a saturated yellow.
                    c = Color.Lerp(new Color(0.62f, 0.53f, 0.31f), new Color(0.74f, 0.65f, 0.42f), n1 * 0.6f + n3 * 0.4f);
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


        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
