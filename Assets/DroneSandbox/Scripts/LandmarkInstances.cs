using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Actual imported FBX pieces share instanced submissions, photo materials and the sector's immutable placements.
    public sealed class LandmarkInstances
    {
        // Each tier retains all imported child transforms and submeshes, never assuming a single mesh per FBX.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 local;
        }

        // Placements are selected per camera into reused arrays; no scene object or mesh copy is needed.
        sealed class Model
        {
            public Part[][] tiers;
            public readonly List<Matrix4x4>[] roots = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            public bool detail, prop;
        }

        readonly Dictionary<(string name, bool detail, bool prop), Model> models = new Dictionary<(string, bool, bool), Model>();
        readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        readonly Matrix4x4[] matrices = new Matrix4x4[511];
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        public int DrawCalls { get; private set; }
        public int Triangles { get; private set; }

        // Borrowed FBX meshes remain resource-owned; only material adapters belong to this cache.
        public void Clear()
        {
            foreach (var material in materials.Values) Art.Discard(material);
            materials.Clear(); models.Clear();
        }

        // Keep all PBR texture slots, UVs and shader keywords while adding only the complementary dither.
        public static Material Adapt(Material source)
        {
            var shader = Resources.Load<Shader>("Shaders/PlaceLit");
            if (shader == null) return null;
            var result = new Material(source) { shader = shader, enableInstancing = true };
            return result;
        }

        // Surfaces keep their existing shader; only the four proxy-building materials fade to imported models.
        public static void SetView(MaterialPropertyBlock block, Camera camera, bool proxy)
        {
            Vector2 projection = LandmarkLod.Projection(camera);
            block.SetVector("_PlaceCamera", camera.transform.position);
            block.SetVector("_PlaceProjection", new Vector4(projection.x, projection.y, proxy ? 1f : 0f, 0f));
            block.SetVector("_PlaceRange", new Vector4(0f, LandmarkLod.Far, LandmarkLod.Near, LandmarkLod.Far));
        }

        // Batch one visible feature at a time so its bounds and budget never include off-screen compounds.
        public void Draw(WorldFeature feature, Camera camera, Bounds bounds)
        {
            DrawCalls = Triangles = 0;
            Vector2 projection = LandmarkLod.Projection(camera);
            foreach (var model in models.Values) foreach (var list in model.roots) list.Clear();
            foreach (var b in feature.buildings)
                Add(b.model, b.position, b.facing, Vector3.one * b.scale, feature.elevation - .05f,
                    b.size.magnitude * .5f, b.detail, false, camera, projection);
            foreach (var p in feature.props)
                Add(p.model, p.position, p.facing, p.scale, feature.elevation, 16f, true, true, camera, projection);
            SetView(properties, camera, false);
            foreach (var model in models.Values)
                for (int tier = 0; tier < model.tiers.Length; tier++)
                {
                    var roots = model.roots[tier]; if (roots.Count == 0) continue;
                    Vector2 range = LandmarkLod.Range(tier, model.detail, model.prop);
                    properties.SetVector("_PlaceRange", new Vector4(range.x, range.y, LandmarkLod.Near, LandmarkLod.Far));
                    foreach (var part in model.tiers[tier])
                        for (int start = 0; start < roots.Count; start += matrices.Length)
                        {
                            int count = Mathf.Min(matrices.Length, roots.Count - start);
                            for (int i = 0; i < count; i++) matrices[i] = roots[start + i] * part.local;
                            for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                            {
                                var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                                if (material == null) continue;
                                var rp = new RenderParams(material) { camera = camera, worldBounds = bounds, matProps = properties,
                                    shadowCastingMode = tier == 0 && !model.detail ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
                                Graphics.RenderMeshInstanced(rp, part.mesh, sub, matrices, count);
                                DrawCalls++; Triangles += (int)part.mesh.GetIndexCount(sub) / 3 * count;
                            }
                        }
                }
        }

        // Selection includes both tiers across a ten-percent fade interval; exact clipping is evaluated per pixel.
        void Add(string name, Vector2 world, Vector2 facing, Vector3 scale, float y, float radius, bool detail, bool prop, Camera camera, Vector2 projection)
        {
            Vector2 p = world - FarTerrain.Origin;
            if (prop ? Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) <= MapLayout.Edge - 20f : LandmarkLod.InSector(p)) return;
            Vector3 at = new Vector3(p.x, y, p.y);
            float distance = projection.y >= 0f ? projection.y : Vector3.Distance(at, camera.transform.position) * projection.x;
            float pad = projection.y >= 0f ? 0f : radius * projection.x;
            if (distance - pad > (detail ? 275f : LandmarkLod.Far)) return;
            var key = (name, detail, prop);
            if (!models.TryGetValue(key, out var model)) { model = Load(name, detail, prop); models.Add(key, model); }
            var root = Matrix4x4.TRS(at, Quaternion.Euler(0f, Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, 0f), scale);
            // Bounds cover large hangars without submitting wholly clipped higher tiers across the compound.
            for (int tier = 0; tier < model.tiers.Length; tier++)
            {
                Vector2 range = LandmarkLod.Range(tier, detail, prop);
                if (distance + pad >= range.x * .9f && distance - pad <= range.y) model.roots[tier].Add(root);
            }
        }

        // Name-based LOD fallback supports imported assets whose LODGroup has not been authored.
        Model Load(string name, bool detail, bool prop)
        {
            var result = new Model { detail = detail, prop = prop, tiers = new Part[prop ? 2 : 3][] };
            var prefab = Resources.Load<GameObject>("Models/" + name);
            var group = prefab != null ? prefab.GetComponent<LODGroup>() : null;
            var lods = group != null ? group.GetLODs() : null;
            for (int tier = 0; tier < result.tiers.Length; tier++)
            {
                var parts = new List<Part>(); int level = prop ? tier + 1 : tier;
                if (prefab != null)
                {
                    Renderer[] renderers = lods != null && lods.Length > 0 ? lods[Mathf.Min(level, lods.Length - 1)].renderers : prefab.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null || (lods == null || lods.Length == 0) && !renderer.name.EndsWith("_LOD" + level)) continue;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null || renderer.sharedMaterials.Length == 0) continue;
                        var slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if (slots[i] == null) continue;
                            var photo = Resources.Load<Material>("Materials/PBR/" + slots[i].name);
                            var source = photo != null ? photo : slots[i];
                            if (!materials.TryGetValue(source, out var material)) { material = Adapt(source); materials.Add(source, material); }
                            slots[i] = material;
                        }
                        parts.Add(new Part { mesh = filter.sharedMesh, materials = slots,
                            local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix });
                    }
                }
                result.tiers[tier] = parts.ToArray();
            }
            return result;
        }
    }
}
