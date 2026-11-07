using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // Small civilian equipment shares three materials and spatial LOD batches; no units or mission targets are spawned.
    public sealed class LandmarkClutter : MonoBehaviour
    {
        readonly List<Mesh> owned = new List<Mesh>();

        // Existing pipes, cranes and wagon models keep their import settings but share the yard's short fade range.
        // Aged roofs get the same photo material as the instanced and distant copies of that building.
        public static void Tune(GameObject placed, Site site)
        {
            var world = MapLayout.Plan.world;
            if (world == null || placed == null) return;
            foreach (var feature in world.features) foreach (var building in feature.buildings)
            {
                if (building.model != site.model || Vector2.Distance(building.position - world.center, site.position) > .01f) continue;
                if (building.roof > 0) Art.Swap(placed, "RoofMetal", LandmarkMesh.RoofMaterial(building.roof));
                if (!building.detail) return;
                var groups = placed.GetComponentsInChildren<LODGroup>(true);
                if (groups.Length == 0)
                {
                    var group = placed.AddComponent<LODGroup>();
                    group.SetLODs(new[] { new LOD(.01f, placed.GetComponentsInChildren<Renderer>(true)) });
                    group.RecalculateBounds(); groups = new[] { group };
                }
                foreach (var group in groups)
                {
                    var lods = group.GetLODs(); if (lods.Length == 0) continue;
                    float projection = group.size * site.scale / (2f * Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad));
                    for (int i = 0; i < lods.Length; i++)
                    {
                        float distance = i == lods.Length - 1 ? 275f : i == 0 ? 65f : 150f;
                        lods[i].screenRelativeTransitionHeight = Mathf.Clamp(projection / distance, .0001f, .95f - i * .01f);
                        lods[i].fadeTransitionWidth = .25f;
                    }
                    Descending(lods);
                    group.fadeMode = LODFadeMode.CrossFade; group.animateCrossFading = false; group.SetLODs(lods);
                }
                return;
            }
        }

        // Only new readable low-poly assets are combined; existing building and forest import settings stay untouched.
        public static void Build(Transform parent)
        {
            var world = MapLayout.Plan.world;
            if (world == null) return;
            var go = new GameObject("Landmark equipment"); go.transform.SetParent(parent, false);
            var owner = go.AddComponent<LandmarkClutter>();
            foreach (var feature in world.features)
            {
                var groups = new Dictionary<int, List<LandmarkProp>>();
                foreach (var prop in feature.props)
                {
                    Vector2 p = prop.position - world.center;
                    if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > MapLayout.Edge - 20f) continue;
                    if (!groups.TryGetValue(prop.group, out var list)) { list = new List<LandmarkProp>(); groups.Add(prop.group, list); }
                    list.Add(prop);
                }
                foreach (var group in groups) owner.Group(feature.id, group.Key, group.Value, world.center);
            }
        }

        // Each functional cluster is at most three material draws, with two light LODs and a dithered exit near 275 m.
        void Group(int feature, int group, List<LandmarkProp> props, Vector2 origin)
        {
            var root = new GameObject("Yard " + feature + " group " + group); root.transform.SetParent(transform, false);
            var lods = new LOD[2];
            for (int level = 0; level < 2; level++)
            {
                var pieces = new Dictionary<Material, List<CombineInstance>>();
                foreach (var prop in props)
                {
                    var prefab = Resources.Load<GameObject>("Models/" + prop.model);
                    if (prefab == null) { Debug.LogWarning("Missing landmark prop " + prop.model); continue; }
                    Vector2 p = prop.position - origin;
                    Vector3 at = Ground.OnGround(new Vector3(p.x, 0f, p.y));
                    var placement = Matrix4x4.TRS(at, Quaternion.Euler(0f, Mathf.Atan2(prop.facing.x, prop.facing.y) * Mathf.Rad2Deg, 0f), prop.scale);
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!filter.name.EndsWith("_LOD" + (level + 1))) continue;
                        Mesh mesh = filter.sharedMesh;
                        if (mesh == null || !mesh.isReadable) { Debug.LogWarning("Unreadable landmark prop " + prop.model); continue; }
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer == null) continue;
                        var materials = renderer.sharedMaterials;
                        for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                        {
                            var source = materials[sub]; if (source == null) continue;
                            // Shared fallback colours unify identical exported slots across the three FBXs.
                            Material material = Art.Shared(source.color);
                            if (!pieces.TryGetValue(material, out var list)) { list = new List<CombineInstance>(); pieces.Add(material, list); }
                            list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub,
                                transform = placement * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                        }
                    }
                }
                var renderers = new List<Renderer>();
                foreach (var pair in pieces)
                {
                    var mesh = new Mesh { name = "Batched yard equipment", indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true); owned.Add(mesh);
                    var part = new GameObject("Equipment LOD" + level); part.transform.SetParent(root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pair.Key;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderers.Add(renderer);
                }
                lods[level] = new LOD(.5f / (level + 1), renderers.ToArray()) { fadeTransitionWidth = .25f }; // placeholder, set below
            }
            var lod = root.AddComponent<LODGroup>(); lod.SetLODs(lods); lod.RecalculateBounds();
            float projection = lod.size / (2f * Mathf.Tan(Scale.CameraFov * .5f * Mathf.Deg2Rad));
            lods[0].screenRelativeTransitionHeight = Mathf.Clamp(projection / 150f, .002f, .95f);
            lods[1].screenRelativeTransitionHeight = Mathf.Min(lods[0].screenRelativeTransitionHeight * .9f, projection / 275f);
            Descending(lods);
            lod.fadeMode = LODFadeMode.CrossFade; lod.animateCrossFading = false; lod.SetLODs(lods);
        }

        // Unity refuses a LOD whose screen size isn't strictly below the one before it (hd148 logged 21 SetLODs
        // errors: middle levels shared a distance, and a third level kept its placeholder). Each level is kept
        // at most 0.9 of the previous, and above a floor that still falls level by level.
        static void Descending(LOD[] lods)
        {
            for (int i = 0; i < lods.Length; i++)
            {
                float floor = .0001f * (lods.Length - i);
                float value = Mathf.Max(lods[i].screenRelativeTransitionHeight, floor);
                if (i > 0) value = Mathf.Max(Mathf.Min(value, lods[i - 1].screenRelativeTransitionHeight * .9f), floor);
                lods[i].screenRelativeTransitionHeight = value;
            }
        }

        // Sector unload owns combined meshes, while source meshes and shared materials remain resource assets.
        void OnDestroy()
        {
            foreach (var mesh in owned) Art.Discard(mesh);
            owned.Clear();
        }
    }
}
