using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneSandbox
{
    // The photo-scanned rocks, cliffs, deadwood and plants: CC0 scans from Poly Haven, fetched by
    // Tools/fetch_models.py and made into Resources/Models/Scan_* by Tools/Blender/import_scans.py. Grouped into
    // families so the builders can pick one at random, plus a way to stand one on the ground. Scans keep their
    // real-world size with the origin at the bottom centre (patches of ground cover: at the ground they grow from),
    // so each is sunk by a share of its own height rather than a fixed amount.
    public static class Scans
    {
        // Mossy boulders 2-3.4 m long, and a lichen-streaked one of 1.8 m.
        public static readonly string[] Boulders =
        {
            "Scan_MossRock_A", "Scan_MossRock_B", "Scan_MossRock_C", "Scan_MossRock_D", "Scan_MossRock_E", "Scan_MossRock_F",
            "Scan_Boulder",
        };

        // Mossy rocks 1-2 m long.
        public static readonly string[] Rocks =
        {
            "Scan_MossStone_A", "Scan_MossStone_B", "Scan_MossStone_C", "Scan_MossStone_D", "Scan_MossStone_E",
            "Scan_MossStone_F", "Scan_MossStone_G",
        };

        // A blocky 30 cm stone and a flat 15 cm one, scanned at a high enough resolution to be scaled up into
        // knee-high rocks and slabs at the water's edge.
        public const string Stone = "Scan_Stone", FlatStone = "Scan_FlatStone";

        // Gravel and stones in drifts about 1.6 m across.
        public static readonly string[] Pebbles = { "Scan_Pebbles_A", "Scan_Pebbles_B" };

        // Faces of layered rock (5 m and 2.7 m wide), facing their +Z and leaning back about 45 degrees.
        public static readonly string[] Cliffs = { "Scan_RockFace01", "Scan_RockFace02" };

        public static readonly string[] Stumps = { "Scan_Stump01", "Scan_Stump02" };
        public const string FallenTrunk = "Scan_FallenTrunk"; // 3 m long, lying along its X

        public static readonly string[] Ferns = { "Scan_Fern_A", "Scan_Fern_B", "Scan_Fern_C", "Scan_Fern_D" };
        public static readonly string[] Nettles = { "Scan_Nettles_A", "Scan_Nettles_B" };
        public static readonly string[] Moss = { "Scan_Moss_A", "Scan_Moss_B" };

        // Leafy bushes about 1-1.3 m tall, and wiry low shrubs 1.2-1.7 m.
        public static readonly string[] Bushes = { "Scan_Bush_A", "Scan_Bush_B" };
        public static readonly string[] Shrubs = { "Scan_Shrub_A", "Scan_Shrub_B", "Scan_Shrub_C", "Scan_Shrub_D" };

        // Low plants for the shore and the banks: white-flowered herbs, weeds with seed spikes, celandine, dandelions.
        public static readonly string[] Wildflowers =
        {
            "Scan_Herbs_A", "Scan_Herbs_B", "Scan_Weeds_A", "Scan_Weeds_B", "Scan_Celandine_A", "Scan_Celandine_B",
            "Scan_Dandelions_A", "Scan_Dandelions_B",
        };

        static readonly Dictionary<string, bool> imported = new Dictionary<string, bool>();

        // Whether Resources/Models has this model (the scans are only there once import_scans.py has run).
        public static bool Has(string model)
        {
            if (!imported.TryGetValue(model, out bool has))
            {
                has = Resources.Load<GameObject>("Models/" + model) != null;
                imported[model] = has;
            }
            return has;
        }

        // The family's model at roll (0-1), or null if it hasn't been imported.
        public static string Pick(string[] family, double roll)
        {
            string model = family[Mathf.Clamp((int)(roll * family.Length), 0, family.Length - 1)];
            return Has(model) ? model : null;
        }

        // A scanned rock from the family (picked by roll), or, without the scans, the procedural rock the map had
        // before at its old size and depth (World.Place then falls back to a stand-in if that's missing too).
        public static GameObject Rock(string[] family, double roll, Vector3 pos, float yaw, float scale, Transform parent,
            string oldModel, float oldScale, float oldSink, float sink = 0.22f)
        {
            string model = Pick(family, roll);
            return model != null ? Place(model, pos, yaw, scale, parent, sink)
                : World.Place(oldModel, pos, yaw, oldScale, oldSink, parent);
        }

        // Stands a scan on the ground at pos (y is ignored), turned yaw degrees, or returns null if it isn't imported.
        // sink: how much of its height goes into the ground. tilt: how far it leans with the slope (0 upright, like
        // a plant; 1 lying flush with the ground, like a drift of pebbles). footprint: how much of its half-width to
        // look under for the lowest ground, so an upright model doesn't float on the downhill side (0: the ground at
        // its centre, for models that follow the slope or are meant to stick out of it). Small ground cover can skip
        // casting shadows, which saves a shadow pass per object for something the sun barely darkens anyway.
        public static GameObject Place(string model, Vector3 pos, float yaw, float scale, Transform parent,
            float sink = 0.2f, float tilt = 0f, float footprint = 0.7f, bool shadows = true)
        {
            if (!Has(model)) return null;
            var root = new GameObject(model).transform;
            root.SetParent(parent, false);
            root.position = new Vector3(pos.x, 0f, pos.z);
            var turn = Quaternion.Euler(0f, yaw, 0f);
            root.rotation = tilt > 0f ? Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, Ground.NormalAt(pos)), tilt) * turn
                : turn;
            var go = Art.TryModel(model, root, scale);
            if (go == null)
            {
                Art.Discard(root.gameObject);
                return null;
            }
            var bounds = Bounds(go);
            float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * footprint, ground = Ground.HeightAt(pos);
            if (reach > 0.05f)
                foreach (var corner in new[] { new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f) })
                    ground = Mathf.Min(ground, Ground.HeightAt(pos + corner * (reach * 0.7071f)));
            root.position = new Vector3(pos.x, ground - bounds.size.y * sink, pos.z);
            if (!shadows)
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            return root.gameObject;
        }

        // World-space box round everything the object draws.
        public static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
