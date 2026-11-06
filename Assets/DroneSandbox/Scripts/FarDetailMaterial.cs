using UnityEngine;

namespace DroneSandbox
{
    // Shared, setup-built texture arrays keep a tile's upload to two small maps, independent of crop count.
    public static class FarDetailMaterial
    {
        static Material template;

        // The saved template retains the HDRP passes and variants; fail visibly rather than silently lose detail.
        public static Material Create()
        {
            if (template == null) template = Resources.Load<Material>("Materials/FarDetailLit");
            if (template == null) throw new System.InvalidOperationException("Run ProjectSetup: Materials/FarDetailLit is missing.");
            return new Material(template);
        }

        // Point-loaded controls contain discrete IDs and a 16-bit row direction; colour remains trilinear.
        public static Texture2D ControlMap()
        {
            return new Texture2D(FarDetailData.TextureSize, FarDetailData.TextureSize, TextureFormat.RGBA32, false, true)
            { name = "Ground surface and direction", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        }

        // World-aligned phases are based on the tile origin, so moving the sector cannot move the crop rows.
        public static void Bind(Material material, Texture2D colour, Texture2D control, int x, int z)
        {
            material.mainTexture = colour;
            material.SetTexture("_GroundControl", control);
            material.SetTextureOffset("_BaseColorMap", new Vector2(x * 512f, z * 512f));
        }
    }
}
