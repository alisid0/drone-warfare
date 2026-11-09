using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Weathering the dam's concrete texture can't carry, projected onto the model as HDRP decals:
    // - moss and grime along the foot, wherever the dam meets the ground;
    // - the spillway's side walls wet from the spray;
    // - dark slime on the spillway's steps under the water;
    // - the stained band at the waterline on the lake side.
    // The materials are made by Drone Sandbox > Configure Project (Resources/Materials/Decals). The positions come
    // from the sizes in Tools/Blender/make_dam.py.
    public static class DamWeathering
    {
        // From make_dam.py: the underside of the deck, how far the downstream face leans out per metre down, the
        // arch, the bays and the spillway, and the lake level the model was drawn for.
        const float DeckBottom = 14f, Slope = 6.5f / 14f, Sag = 5f, HalfLength = 36f, Bay = 6f, SpillHalf = 6f, Lake = 12.9f;
        const float DrawDistance = 700f;

        public static void Add(Transform model)
        {
            var moss = Resources.Load<Material>("Materials/Decals/DecalMoss");
            var damp = Resources.Load<Material>("Materials/Decals/DecalDamp");
            var algae = Resources.Load<Material>("Materials/Decals/DecalAlgae");
            var waterline = Resources.Load<Material>("Materials/Decals/DecalWaterline");
            if (moss == null || damp == null || algae == null || waterline == null) return;
            var root = new GameObject("Weathering").transform;
            root.SetParent(model, false);
            Vector3 upstream = Direction(0f, 1f, 0f), downstream = -upstream, down = Direction(0f, 0f, -1f);

            for (float x = -HalfLength + Bay / 2f; x < HalfLength; x += Bay)
            {
                if (Mathf.Abs(x) < SpillHalf) continue;
                float a = Arch(x);
                // Moss along the foot: find where the ground meets the leaning face (bays buried in the hillside,
                // where the arcade stands in front, get none).
                Vector3 guess = model.TransformPoint(Local(x, a - 9.5f, 0f));
                float ground = model.InverseTransformPoint(new Vector3(guess.x, Ground.HeightAt(guess), guess.z)).y;
                if (ground < 6f)
                {
                    float face = a - 3.5f - Slope * (DeckBottom - ground);
                    Decal(root, moss, Local(x, face + 0.5f, ground + 1.5f), upstream, new Vector3(Bay + 2f, 4f, 4f));
                }
                // The waterline on the lake side (the box spans the arch's curve across the bay). Each bay's box
                // overlaps the next, where both fade out, so the band runs on unbroken.
                Decal(root, waterline, Local(x, a + 3.2f, Lake + 0.1f), downstream, new Vector3(Bay + 2f, 1.8f, 3.6f));
            }

            // The spillway: its side walls wet from the spray, and slime on its steps.
            float crest = Arch(SpillHalf / 2f);
            for (int side = -1; side <= 1; side += 2)
                Decal(root, damp, Local(side * 5.4f, crest - 5.8f, 5.5f), Direction(side, 0f, 0f), new Vector3(10.5f, 13f, 1.6f));
            Decal(root, algae, Local(0f, crest - 5.6f, 6f), down, new Vector3(11.2f, 10.2f, 16f));
        }

        static void Decal(Transform parent, Material material, Vector3 at, Vector3 forward, Vector3 size)
        {
            var go = new GameObject(material.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            // Pictures stand upright on walls; looking down, their "up" runs downstream.
            Vector3 up = Mathf.Abs(forward.y) > 0.9f ? Direction(0f, -1f, 0f) : Vector3.up;
            go.transform.localRotation = Quaternion.LookRotation(forward, up);
            var decal = go.AddComponent<DecalProjector>();
            decal.material = material;
            decal.size = size;
            decal.pivot = Vector3.zero; // the box is centred on the projector
            decal.drawDistance = DrawDistance;
            decal.fadeScale = 0.85f;
        }

        // make_dam.py's coordinates (x along the dam, y towards the lake, z up) in the model's space, as the FBX
        // export and import turn them.
        static Vector3 Local(float x, float y, float z) => new Vector3(-x, z, -y);
        static Vector3 Direction(float x, float y, float z) => new Vector3(-x, z, -y);

        // How far the dam's centre line bows towards the lake at x along it.
        static float Arch(float x)
        {
            x = Mathf.Clamp(x, -HalfLength, HalfLength) / HalfLength;
            return Sag * (1f - x * x);
        }
    }
}
