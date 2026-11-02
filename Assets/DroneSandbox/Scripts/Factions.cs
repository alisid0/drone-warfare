using UnityEngine;

namespace DroneSandbox
{
    // The game's made-up setting: the two sides, their colours and emblems, and the names of places on the map.
    // Everything here is fictional on purpose, so the game borrows how drone warfare works without standing in
    // for any real country or army. Rename anything here; nothing else hard-codes these names.
    public static class Factions
    {
        public const string Player = "Norvane Defence Force";
        public const string PlayerShort = "Norvane";
        public const string Enemy = "Dravin Coalition";
        public const string EnemyShort = "Dravin";
        public const string Region = "the Varrow Valley";
        public const string River = "Lissen River";
        public const string PlayerBase = "Forward Base Kestrel";
        public const string EnemyBase = "Dravin Command Post";

        // In the same order as MapLayout.Villages.
        public static readonly string[] Villages = { "Tolbeck", "Esterhay", "Marrowfield", "Grisham Farm" };

        public static readonly Color PlayerColor = new Color(0.10f, 0.52f, 0.58f);  // teal
        public static readonly Color EnemyColor = new Color(0.92f, 0.46f, 0.12f);   // orange
        static readonly Color EnemyDark = new Color(0.09f, 0.09f, 0.10f);

        static Texture2D playerEmblem, enemyEmblem, playerFlag, enemyFlag;
        static Material playerDecal, enemyDecal, playerFlagMaterial, enemyFlagMaterial;

        // ---- Emblems and flags (drawn in code, so there are no image files to keep) ----

        // Norvane: a white chevron in a teal hexagon with a white rim.
        public static Texture2D PlayerEmblem => playerEmblem != null ? playerEmblem : (playerEmblem = Paint(256, 256, p =>
        {
            if (!InHexagon(p, 0.48f)) return Color.clear;
            if (!InHexagon(p, 0.42f)) return Color.white;
            return InChevron(p) ? Color.white : PlayerColor;
        }));

        // Dravin: an orange triangle in a black diamond with an orange rim.
        public static Texture2D EnemyEmblem => enemyEmblem != null ? enemyEmblem : (enemyEmblem = Paint(256, 256, p =>
        {
            float d = Mathf.Abs(p.x - 0.5f) + Mathf.Abs(p.y - 0.5f);
            if (d > 0.48f) return Color.clear;
            if (d > 0.42f) return EnemyColor;
            return InTriangle(p) ? EnemyColor : EnemyDark;
        }));

        // Norvane flag: teal with a white band along the bottom, the emblem in the middle.
        public static Texture2D PlayerFlag => playerFlag != null ? playerFlag : (playerFlag = Paint(320, 200, p =>
        {
            Color emblem = EmblemOnFlag(p, 320f / 200f, false);
            if (emblem.a > 0.5f) return emblem;
            return p.y < 0.16f ? Color.white : PlayerColor;
        }));

        // Dravin flag: charcoal with an orange diagonal band, the emblem in the middle.
        public static Texture2D EnemyFlag => enemyFlag != null ? enemyFlag : (enemyFlag = Paint(320, 200, p =>
        {
            Color emblem = EmblemOnFlag(p, 320f / 200f, true);
            if (emblem.a > 0.5f) return emblem;
            float band = Mathf.Abs((p.y - 0.5f) - (p.x - 0.5f) * 0.62f);
            return band < 0.1f ? EnemyColor : new Color(0.16f, 0.16f, 0.17f);
        }));

        static Color EmblemOnFlag(Vector2 p, float aspect, bool enemy)
        {
            // The emblem sits in a square of 70% of the flag's height, centred.
            float size = 0.7f;
            var q = new Vector2((p.x - 0.5f) * aspect / size + 0.5f, (p.y - 0.5f) / size + 0.5f);
            if (q.x < 0f || q.x > 1f || q.y < 0f || q.y > 1f) return Color.clear;
            if (enemy)
            {
                float d = Mathf.Abs(q.x - 0.5f) + Mathf.Abs(q.y - 0.5f);
                if (d > 0.48f) return Color.clear;
                if (d > 0.42f) return EnemyColor;
                return InTriangle(q) ? EnemyColor : EnemyDark;
            }
            if (!InHexagon(q, 0.48f)) return Color.clear;
            if (!InHexagon(q, 0.42f)) return Color.white;
            return InChevron(q) ? Color.white : PlayerColor;
        }

        // Materials for painted emblems (cut out round their edges) and flags (both sides drawn).
        public static Material DecalMaterial(bool player)
        {
            if (player) return playerDecal != null ? playerDecal : (playerDecal = Textured(PlayerEmblem, false));
            return enemyDecal != null ? enemyDecal : (enemyDecal = Textured(EnemyEmblem, false));
        }

        public static Material FlagMaterial(bool player)
        {
            if (player) return playerFlagMaterial != null ? playerFlagMaterial : (playerFlagMaterial = Textured(PlayerFlag, true));
            return enemyFlagMaterial != null ? enemyFlagMaterial : (enemyFlagMaterial = Textured(EnemyFlag, true));
        }

        // A copy of the Decal material (alpha cut-out URP Lit, made by Configure Project) with this texture.
        static Material Textured(Texture2D texture, bool doubleSided)
        {
            var template = Resources.Load<Material>("Materials/Decal");
            var m = template != null ? new Material(template) : Art.NewMaterial(Color.white);
            m.mainTexture = texture;
            m.color = Color.white;
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            return m;
        }

        // Paints a small emblem flat onto a surface: a quad facing outward along `facing` (local space of parent).
        public static void Paint(Transform parent, Vector3 localPosition, Vector3 facing, float size, bool player)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Emblem";
            Art.Discard(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            // A Quad shows its face toward -Z, so point -Z along the outward direction.
            Vector3 upHint = Mathf.Abs(facing.normalized.y) > 0.9f ? Vector3.forward : Vector3.up; // flat on a roof
            go.transform.localRotation = Quaternion.LookRotation(-facing, upHint);
            go.transform.localScale = Vector3.one * size;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = DecalMaterial(player);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- Shape tests on the unit square ----

        static bool InHexagon(Vector2 p, float radius)
        {
            float x = Mathf.Abs(p.x - 0.5f), y = Mathf.Abs(p.y - 0.5f);
            return x <= radius * 0.866f && x * 0.5f + y * 0.866f <= radius * 0.866f;
        }

        static readonly Vector2[] Chevron =
        {
            new Vector2(0.5f, 0.74f), new Vector2(0.76f, 0.44f), new Vector2(0.62f, 0.44f),
            new Vector2(0.5f, 0.58f), new Vector2(0.38f, 0.44f), new Vector2(0.24f, 0.44f),
        };

        static readonly Vector2[] Triangle = { new Vector2(0.31f, 0.6f), new Vector2(0.69f, 0.6f), new Vector2(0.5f, 0.3f) };

        static bool InChevron(Vector2 p) => InPolygon(p, Chevron);

        static bool InTriangle(Vector2 p) => InPolygon(p, Triangle);

        static bool InPolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // Fills a texture from a colour-per-point function on the unit square, 2x2 samples per pixel.
        static Texture2D Paint(int width, int height, System.Func<Vector2, Color> colourAt)
        {
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            sum += colourAt(new Vector2((x + 0.25f + 0.5f * sx) / width, (y + 0.25f + 0.5f * sy) / height));
                    pixels[y * width + x] = sum / 4f;
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
