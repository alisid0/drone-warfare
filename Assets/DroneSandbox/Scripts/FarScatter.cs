using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Streams a small tree bubble and batches photo models by species, tier and stand tint. Larger shapes belong
    // to ForestHlod and SettlementHlod, so a high camera never submits thousands of sub-pixel tree cards.
    public class FarScatter : MonoBehaviour
    {
        public const float MeshReach = 120f, TreeReach = 700f, HouseReach = 1500f;
        const float CellSize = 120f;
        const int Batch = 511, CellsPerFrame = 4; // non-uniform crown scales also need inverse instance matrices
        static readonly string[] Trees = { "Scenery_Tree", "Scenery_Birch", "Scenery_Spruce", "Scenery_Pine" };
        static readonly string[] Houses = { "Scenery_House_A", "Scenery_House_B", "Scenery_House_C", "Scenery_House_D",
            "Scenery_Townhouse_Row", "Scenery_Apartment", "Scenery_Shop" };

        // A model may have several renderers at one LOD; retain every piece and its imported transform.
        sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 local;
        }

        // The model's full height normalizes both real meshes and cards to the stand's same 8-24 m trees.
        sealed class Model
        {
            public Part[] parts;
            public float height;
            public readonly List<Matrix4x4> instances = new List<Matrix4x4>();
        }

        // Root transforms stay immutable when the camera changes tiers; the world grid owns the trees.
        struct Tree
        {
            public Vector3 position;
            public ForestPattern.Stand stand;
            public Matrix4x4 mesh, card;
        }

        // Small cells bound generation work and culling without creating scenery GameObjects.
        sealed class Cell
        {
            public Bounds bounds;
            public Tree[] trees;
        }

        Model[] models;
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly List<long> stale = new List<long>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly Matrix4x4[] batch = new Matrix4x4[Batch];
        readonly Plane[] planes = new Plane[6];
        readonly ForestHlod forest = new ForestHlod();
        readonly SettlementHlod settlements = new SettlementHlod();
        readonly LandmarkHlod landmarks = new LandmarkHlod();
        Vector2 builtOrigin;
        int builtSeed = -1;

        public static int MeshTreesDrawn { get; private set; }
        public static int CardTreesDrawn { get; private set; }
        public static int TreesDrawn => MeshTreesDrawn + CardTreesDrawn;
        public static int HousesDrawn { get; private set; }
        public static int InstanceDrawCalls { get; private set; }

        // Release generated resources on sector/seed changes, not just their dictionary entries.
        public void Clear()
        {
            cells.Clear(); forest.Clear(); settlements.Clear(); landmarks.Clear();
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
        }

        // Also release cloned instancing materials when the persistent terrain object is destroyed.
        void OnDestroy()
        {
            Clear();
            foreach (var material in ownedMaterials) Art.Discard(material);
            ownedMaterials.Clear();
        }

        // Keep the ground lookup in one place for FarDetail's independent height-source change.
        // On the streamed detail tile's surface where one is (or will be), else the far terrain's ground. The tiered
        // trees stand on the ground itself (no canopy lift), so this is the bare-ground form of FarDetail's lookup.
        internal static float GroundAt(Vector2 local) => FarDetail.ScatterHeight(local, false);

        // 3D distance includes camera altitude, so the 2.5/9 km views submit no individual trees or houses.
        void LateUpdate()
        {
            MeshTreesDrawn = CardTreesDrawn = HousesDrawn = InstanceDrawCalls = 0;
            var cam = Camera.main;
            if (cam == null || !FarTerrain.Active) return;
            if (models == null) LoadModels();
            if (builtSeed != WorldPlan.Seed || builtOrigin != FarTerrain.Origin)
            {
                Clear(); builtSeed = WorldPlan.Seed; builtOrigin = FarTerrain.Origin;
            }
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            foreach (var model in models) if (model != null) model.instances.Clear();
            Vector3 position = cam.transform.position;
            Vector2 local = new Vector2(position.x, position.z), world = local + FarTerrain.Origin;
            float altitude = position.y - GroundAt(local);
            if (altitude < TreeReach + 150f)
            {
                int ci = Mathf.FloorToInt(world.x / CellSize), cj = Mathf.FloorToInt(world.y / CellSize), built = 0;
                int reach = Mathf.CeilToInt(TreeReach / CellSize);
                for (int ring = 0; ring <= reach && built < CellsPerFrame; ring++)
                    for (int dz = -ring; dz <= ring && built < CellsPerFrame; dz++)
                        for (int dx = -ring; dx <= ring && built < CellsPerFrame; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                            long key = Key(ci + dx, cj + dz);
                            if (cells.ContainsKey(key)) continue;
                            cells.Add(key, Build(ci + dx, cj + dz)); built++;
                        }
            }
            stale.Clear();
            foreach (var pair in cells)
            {
                var cell = pair.Value;
                if (cell.bounds.SqrDistance(position) > 1100f * 1100f) { stale.Add(pair.Key); continue; }
                if (!GeometryUtility.TestPlanesAABB(planes, cell.bounds)) continue;
                foreach (var tree in cell.trees)
                {
                    float distance = Vector3.Distance(position, tree.position + Vector3.up * tree.stand.height * 0.7f);
                    if (distance > TreeReach) continue;
                    int tier = distance <= MeshReach ? 0 : 1;
                    int kind = tier * 12 + tree.stand.species * 3 + tree.stand.shade;
                    var model = models[kind];
                    if (model == null) continue;
                    model.instances.Add(tier == 0 ? tree.mesh : tree.card);
                    if (tier == 0) MeshTreesDrawn++; else CardTreesDrawn++;
                }
            }
            foreach (long key in stale) cells.Remove(key);
            forest.Update(cam, planes);
            landmarks.Update(cam, planes);
            settlements.Update(cam, planes, this);
            Draw(cam);
        }

        // Settlement blocks call this only when their corresponding combined roof group is hidden.
        internal void House(SettlementPattern.House house)
        {
            var model = models[24 + house.kind];
            if (model == null) return;
            Vector2 p = house.world - FarTerrain.Origin;
            model.instances.Add(Matrix4x4.TRS(new Vector3(p.x, GroundAt(p) - 0.2f, p.y),
                Quaternion.Euler(0f, house.yaw, 0f), Vector3.one));
            HousesDrawn++;
        }

        // Pack across cells: one draw per populated model/submesh/tint batch, not one per tiny cell.
        void Draw(Camera cam)
        {
            var bounds = new Bounds(cam.transform.position, Vector3.one * 3600f);
            foreach (var model in models)
            {
                if (model == null || model.instances.Count == 0) continue;
                foreach (var part in model.parts)
                    for (int start = 0; start < model.instances.Count; start += Batch)
                    {
                        int count = Mathf.Min(Batch, model.instances.Count - start);
                        for (int i = 0; i < count; i++) batch[i] = model.instances[start + i] * part.local;
                        for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                        {
                            var material = part.materials[Mathf.Min(sub, part.materials.Length - 1)];
                            if (material == null) continue;
                            var rp = new RenderParams(material) { camera = cam, worldBounds = bounds,
                                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
                            Graphics.RenderMeshInstanced(rp, part.mesh, sub, batch, count);
                            InstanceDrawCalls++;
                        }
                    }
            }
        }

        // A unique signed pair avoids collisions on either side of the world's origin.
        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        // The globally aligned candidate cells fit exactly in a 120 m cell, preventing duplicate boundary trees.
        Cell Build(int ci, int cj)
        {
            var trees = new List<Tree>();
            var center = new Vector2((ci + 0.5f) * CellSize, (cj + 0.5f) * CellSize) - FarTerrain.Origin;
            var bounds = new Bounds(new Vector3(center.x, GroundAt(center), center.y), new Vector3(CellSize + 40f, 240f, CellSize + 40f));
            int count = Mathf.RoundToInt(CellSize / ForestPattern.TreeStep);
            for (int j = cj * count; j < (cj + 1) * count; j++)
                for (int i = ci * count; i < (ci + 1) * count; i++)
                {
                    if (!ForestPattern.Tree(i, j, out Vector2 world, out ForestPattern.Stand stand)) continue;
                    Vector2 p = world - FarTerrain.Origin;
                    if (InSector(p)) continue;
                    var at = new Vector3(p.x, GroundAt(p), p.y);
                    var tree = new Tree { position = at, stand = stand };
                    var rotation = Quaternion.Euler(0f, ForestPattern.Unit(i, j, 44) * 360f, 0f);
                    // Cache both roots once: flight changes the tier, not thousands of TRS matrices each frame.
                    for (int tier = 0; tier < 2; tier++)
                    {
                        var model = models[tier * 12 + stand.species * 3 + stand.shade];
                        if (model == null) continue;
                        float size = stand.height / model.height;
                        var matrix = Matrix4x4.TRS(at, rotation, new Vector3(size * stand.width, size, size * stand.width));
                        if (tier == 0) tree.mesh = matrix; else tree.card = matrix;
                    }
                    trees.Add(tree);
                    bounds.Encapsulate(at); bounds.Encapsulate(at + Vector3.up * 26f);
                }
            return new Cell { trees = trees.ToArray(), bounds = bounds };
        }

        // The detailed sector continues to own its existing scenery, including a small overlap margin.
        internal static bool InSector(Vector2 local) =>
            Mathf.Abs(local.x) < Ground.Size / 2f + 10f && Mathf.Abs(local.y) < Ground.Size / 2f + 10f;

        // Keep the photo material path used by Art, with three reusable tint variants for each tree tier.
        void LoadModels()
        {
            models = new Model[31];
            for (int tier = 0; tier < 2; tier++)
                for (int species = 0; species < 4; species++)
                    for (int shade = 0; shade < 3; shade++)
                        models[tier * 12 + species * 3 + shade] = LoadModel(Trees[species], tier == 0 ? 1 : 2, ForestPattern.Tint(shade));
            for (int i = 0; i < Houses.Length; i++) models[24 + i] = LoadModel(Houses[i], 1, 1f);
        }

        // Read every renderer of the selected LOD and use its transformed bounds to normalize tree height.
        Model LoadModel(string name, int lod, float tint)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) return null;
            Renderer[] renderers = null;
            var group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0) renderers = lods[Mathf.Min(lod, lods.Length - 1)].renderers;
            }
            if (renderers == null || renderers.Length == 0) renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            var parts = new List<Part>();
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || renderer.sharedMaterials.Length == 0) continue;
                var materials = renderer.sharedMaterials;
                var local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    var photo = Resources.Load<Material>("Materials/PBR/" + materials[i].name);
                    var material = new Material(photo != null ? photo : materials[i]) { enableInstancing = true };
                    var colour = material.color; colour.r *= tint; colour.g *= tint; colour.b *= tint;
                    material.color = colour; materials[i] = material; ownedMaterials.Add(material);
                }
                var box = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = local.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                    bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
                }
                parts.Add(new Part { mesh = filter.sharedMesh, materials = materials, local = local });
            }
            return parts.Count == 0 ? null : new Model { parts = parts.ToArray(), height = Mathf.Max(1f, top - bottom) };
        }
    }
}
