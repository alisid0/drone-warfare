using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Road furniture borrows Farmland's existing avenue allowance and submits shared FBX parts as instances.
    public sealed class Roadside : MonoBehaviour
    {
        public static int Trees { get; private set; }
        public static int Poles { get; private set; }
        public static int Gates { get; private set; }
        public static int ChargedRenderers { get; private set; }
        readonly List<Model> models = new List<Model>();
        readonly List<Material> owned = new List<Material>();
        readonly Matrix4x4[] batch = new Matrix4x4[511];

        // Imported transforms, submeshes and all LOD parts remain intact, including the existing tree billboard.
        sealed class Part
        {
            public Mesh mesh;
            public Matrix4x4 local;
            public Material[] materials;
        }

        // Placements are immutable; one reused instance array is filtered separately for each actual camera.
        sealed class Model
        {
            public readonly List<Matrix4x4> placements = new List<Matrix4x4>();
            public readonly List<Vector3> positions = new List<Vector3>();
            public Part[][] tiers;
            public int cost;
            public bool tree;
        }

        // The caller passes the already reserved avenue budget, so roadside detail never expands a renderer cap.
        public static void Build(Transform parent, ref int allowance)
        {
            Trees = Poles = Gates = ChargedRenderers = 0;
            if (allowance <= 0) return;
            var go = new GameObject("Instanced roadside"); go.transform.SetParent(parent, false);
            var owner = go.AddComponent<Roadside>();
            var pole = owner.Load("Scenery_TelegraphPole", false);
            var tree = owner.Load("Scenery_Poplar", true);
            var gate = owner.Load("Prop_Gate", false);
            int before = allowance;
            foreach (var s in MapLayout.Roads.segments)
            {
                if (s.kind == (int)RoadClass.Track) continue;
                Vector2 a = new Vector2(s.ax, s.az), b = new Vector2(s.bx, s.bz), dir = (b - a).normalized;
                float length = (b - a).magnitude;
                if (length < .01f) continue;
                float side = RoadAppearance.Value(s.route, 5) > .5f ? -1f : 1f;
                Vector2 normal = new Vector2(-dir.y, dir.x) * side;
                float pitch = 40f + 20f * RoadAppearance.Value(s.route, 71);
                float first = (float)Math.Ceiling(s.walked / pitch) * pitch;
                if (s.kind == (int)RoadClass.Highway || RoadAppearance.Value(s.route, 17) > .55f)
                    for (float v = first; v < s.walked + length && Poles < 96; v += pitch)
                    {
                        Vector2 p = a + dir * (v - s.walked) + normal * (s.widthA + 2.4f);
                        if (Clear(p, false) && owner.Place(pole, p, dir, 1f, ref allowance)) Poles++;
                    }
                first = (float)Math.Ceiling(s.walked / 12f) * 12f;
                for (float v = first; v < s.walked + length && Trees < 96; v += 12f)
                {
                    if (RoadAppearance.Value(s.route, (int)Math.Floor(v / 240f)) < .64f) continue;
                    Vector2 p = a + dir * (v - s.walked) - normal * (s.widthA + 3f);
                    if (!Clear(p, true) || tree == null || allowance < tree.cost || !World.ReserveRoadTree(p)) continue;
                    if (owner.Place(tree, p, dir, .85f + .2f * RoadAppearance.Value(s.route, (int)v), ref allowance)) Trees++;
                }
            }
            foreach (var entry in RoadSurface.Entrances)
            {
                // A swung-open leaf sits along the field margin, leaving the shared four-metre tractor opening clear.
                Vector2 p = entry.gate - entry.along * 2f;
                if (owner.Place(gate, p, entry.into, 1f, ref allowance)) Gates++;
            }
            ChargedRenderers = before - allowance;
        }

        // Furniture stays off junctions, crop meshes, building plots, bridges and both banks of standing water.
        static bool Clear(Vector2 p, bool tree)
        {
            if (Math.Max(Math.Abs(p.x), Math.Abs(p.y)) > MapLayout.Edge - 5f || MapLayout.NearFlatSite(p, 3f)
                || MapLayout.NearVillage(p, tree ? 8f : 1f) || MapLayout.InField(p, tree ? 1.2f : .2f)) return false;
            var road = MapLayout.Roads.At(p.x, p.y);
            if (!road.found || road.edge < 1.4f || road.junction > 0f || MapLayout.LakeDistance(p.x, p.y) < 4f) return false;
            if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverWidthAt(p.x, p.y) + 5f) return false;
            foreach (var gate in RoadSurface.Entrances) if ((p - gate.gate).sqrMagnitude < 7f * 7f) return false;
            foreach (var bridge in MapLayout.Bridges) if ((p - bridge).sqrMagnitude < 32f * 32f) return false;
            return true;
        }

        // Budget charges include hidden LOD renderers, matching the old non-instanced avenue accounting.
        bool Place(Model model, Vector2 p, Vector2 direction, float scale, ref int allowance)
        {
            if (model == null || allowance < model.cost) return false;
            Vector3 at = Ground.OnGround(new Vector3(p.x, 0f, p.y)) - Vector3.up * .08f;
            model.positions.Add(at);
            model.placements.Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 0f), Vector3.one * scale));
            allowance -= model.cost; return true;
        }

        // Load each actual FBX once; no GameObjects, colliders or duplicated meshes are created for its placements.
        Model Load(string name, bool tree)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            var group = prefab.GetComponent<LODGroup>();
            var lods = group != null ? group.GetLODs() : new LOD[0];
            var model = new Model { tiers = new Part[3][], tree = tree, cost = Math.Max(1, prefab.GetComponentsInChildren<Renderer>(true).Length) };
            var cache = new Dictionary<Material, Material>();
            for (int tier = 0; tier < 3; tier++)
            {
                var renderers = lods.Length > 0 ? lods[Math.Min(tier, lods.Length - 1)].renderers : prefab.GetComponentsInChildren<Renderer>(true);
                var parts = new List<Part>();
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] == null) continue;
                        var photo = Resources.Load<Material>("Materials/PBR/" + slots[i].name);
                        var source = photo != null ? photo : slots[i];
                        if (!cache.TryGetValue(source, out var material))
                        { material = new Material(source) { enableInstancing = true }; cache.Add(source, material); owned.Add(material); }
                        slots[i] = material;
                    }
                    parts.Add(new Part { mesh = filter.sharedMesh, materials = slots, local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix });
                }
                model.tiers[tier] = parts.ToArray();
            }
            models.Add(model); return model;
        }

        // HDRP invokes this for strategy and drone cameras; only nearby instances of the selected tier are submitted.
        void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;

        // Disabling a sector immediately removes its submissions, before deferred object destruction finishes.
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        // Camera distance selects existing LODs without allocation or per-instance scene updates.
        void Draw(ScriptableRenderContext context, Camera camera)
        {
            Vector3 eye = camera.transform.position;
            foreach (var model in models) for (int tier = 0; tier < 3; tier++) foreach (var part in model.tiers[tier])
            {
                int count = 0; var bounds = new Bounds();
                for (int i = 0; i < model.positions.Count; i++)
                {
                    Vector3 p = model.positions[i]; float distance = (p - eye).magnitude;
                    if (distance > 1000f || (distance < 65f ? 0 : distance < 250f ? 1 : 2) != tier) continue;
                    batch[count++] = model.placements[i] * part.local;
                    if (count == 1) bounds = new Bounds(p, Vector3.one * 28f); else bounds.Encapsulate(new Bounds(p, Vector3.one * 28f));
                }
                if (count == 0) continue;
                for (int sub = 0; sub < part.mesh.subMeshCount && part.materials.Length > 0; sub++)
                {
                    var material = part.materials[Math.Min(sub, part.materials.Length - 1)]; if (material == null) continue;
                    var rp = new RenderParams(material) { camera = camera, worldBounds = bounds, receiveShadows = true,
                        shadowCastingMode = model.tree && tier == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off };
                    Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, count);
                }
            }
        }

        // Borrowed FBX geometry stays loaded; only this sector's instancing material copies need disposal.
        void OnDestroy()
        {
            foreach (var material in owned) Art.Discard(material);
            owned.Clear(); models.Clear();
        }
    }
}
