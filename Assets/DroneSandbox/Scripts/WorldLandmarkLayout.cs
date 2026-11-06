using System;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Surfaces are ordered bottom to top, so the ground query and both mesh builders agree at overlaps.
    public enum LandmarkSurfaceKind : byte { Gravel, Concrete, Asphalt, Paint, Rail }

    // A real model's root and approximate outer volume, in world metres with its original yaw and scale.
    public struct LandmarkBuilding
    {
        public string model;
        public Vector2 position, facing;
        public Vector3 size;
        public float flat;
        public int shape; // 0 pitched roof, 1 tank, 2 chimney, 3 flat roof
    }

    // Flat surfaces share coordinates with the world grade and never acquire mission-seeded offsets.
    public struct LandmarkSurface
    {
        public FeatureArea area;
        public LandmarkSurfaceKind kind;
        public float lift;
    }

    // One recipe runs once per world feature; SectorIdentity and LandmarkHlod consume these same placements.
    public static class WorldLandmarkLayout
    {
        // A saved variant changes building counts and yard equipment without consuming the mission RNG.
        public static void Build(WorldFeature f)
        {
            var rng = new Random(f.variantSeed);
            if (f.kind == WorldFeatureKind.Airfield) Airfield(f, rng);
            else if (f.kind == WorldFeatureKind.IndustrialZone) Industry(f, rng);
            else Railway(f, rng);
        }

        // An 880 x 30 m runway has sixty metre end clearways, a parallel taxiway and three apron hangars.
        static void Airfield(WorldFeature f, Random rng)
        {
            Surface(f, 0f, -65f, 880f, 30f, LandmarkSurfaceKind.Asphalt);
            Surface(f, 0f, 0f, 820f, 14f, LandmarkSurfaceKind.Asphalt);
            Surface(f, 0f, 78f, 540f, 108f, LandmarkSurfaceKind.Concrete);
            foreach (float u in new[] { -380f, 380f }) Surface(f, u, -32f, 14f, 65f, LandmarkSurfaceKind.Asphalt);
            foreach (float u in new[] { -190f, 0f, 190f }) Surface(f, u, 22f, 16f, 44f, LandmarkSurfaceKind.Concrete);
            int count = 2 + rng.Next(3);
            for (int i = 0; i < count; i++) Building(f, "Scenery_HangarLarge", -180f + i * 110f, 99f, false, new Vector3(41.08f, 15.3f, 51.075f), 36f);
            Building(f, "Scenery_ControlTower", 300f, 110f, false, new Vector3(10.4f, 18.75f, 10.4f), 9f, 3);
            Building(f, "Scenery_FuelFarm", -315f, 112f, false, new Vector3(25f, 6.535f, 19f), 18f, 1);
            Surface(f, 0f, 147f, 16f, 28f, LandmarkSurfaceKind.Asphalt);
            for (float u = -400f; u < 400f; u += 24f) Surface(f, u, -65f, 12f, 0.6f, LandmarkSurfaceKind.Paint);
            foreach (float v in new[] { -79f, -51f }) Surface(f, 0f, v, 872f, 0.4f, LandmarkSurfaceKind.Paint);
            foreach (float end in new[] { -1f, 1f }) for (float v = -76f; v <= -54f; v += 3f)
                Surface(f, end * 422f, v, 18f, 1.6f, LandmarkSurfaceKind.Paint);
        }

        // Six to eight halls and warehouses, four to six tanks and two chimneys fill a 450 x 350 m works.
        static void Industry(WorldFeature f, Random rng)
        {
            Surface(f, 0f, 0f, 440f, 340f, LandmarkSurfaceKind.Gravel);
            Surface(f, 0f, 12f, 405f, 250f, LandmarkSurfaceKind.Concrete);
            for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++)
                Building(f, "Scenery_FactoryHall", -145f + col * 100f, -85f + row * 83f, false, new Vector3(60.35f, 11.573f, 33.335f), 38f);
            int warehouses = rng.Next(3);
            for (int i = 0; i < warehouses; i++) Building(f, "Scenery_Warehouse", 160f, -85f + i * 70f, true, new Vector3(18f, 9f, 30f), 22f);
            Building(f, "Scenery_OfficeBlock", 85f, 115f, false, new Vector3(24.2f, 11f, 15.735f), 18f, 3);
            int tanks = 4 + rng.Next(3);
            for (int i = 0; i < tanks; i++) Building(f, "Scenery_StorageTank", -170f + i * 34f, 102f, true, new Vector3(20.48f, 15.3f, 20.48f), 16f, 1);
            foreach (float u in new[] { -80f, 20f }) Building(f, "Scenery_Chimney", u, -42f, true, new Vector3(2.6f, 28f, 2.6f), 5f, 2);
            Surface(f, 0f, 150f, 16f, 40f, LandmarkSurfaceKind.Asphalt);
        }

        // Four 530-590 m sidings at 5.5 m spacing join the actual railway; the yard faces its road gate.
        static void Railway(WorldFeature f, Random rng)
        {
            Surface(f, 0f, 0f, 790f, 190f, LandmarkSurfaceKind.Gravel);
            Surface(f, 40f, 48f, 570f, 60f, LandmarkSurfaceKind.Concrete);
            var a = f.Area;
            Vector2 throat = WorldPlan.FeatureRailPoint(a.Point(-390f, -62f), out _);
            for (int track = 0; track < 4; track++)
            {
                float v = -30f + track * 5.5f, begin = -290f + track * 20f;
                var line = new[] { throat, a.Point(-350f + track * 12f, -62f), a.Point(begin, v), a.Point(300f, v) };
                f.rails.Add(line);
                for (int i = 1; i < line.Length; i++) RailSurface(f, line[i - 1], line[i]);
                int count = 5 + rng.Next(5);
                for (int i = 0; i < count; i++) Building(f, track % 2 == 0 ? "Scenery_RailContainerWagon" : "Scenery_RailHopper",
                    -40f + i * 19f, v, true, new Vector3(3.14f, track % 2 == 0 ? 4.45f : 5.535f, 15f), 0f, 3);
            }
            for (int i = 0; i < 3; i++) Building(f, "Scenery_FreightShed", -145f + i * 160f, 48f, false, new Vector3(40.8f, 9.787f, 21.839f), 26f);
            Building(f, "Scenery_SignalBox", -270f, 28f, true, new Vector3(10.6f, 8.15f, 11.135f), 10f, 3);
            Surface(f, 20f, -1f, 560f, 7f, LandmarkSurfaceKind.Concrete);
            Surface(f, 0f, 84f, 16f, 32f, LandmarkSurfaceKind.Asphalt);
        }

        // The facing convention is identical to SectorIdentity.Site and the FBX roots used by Features.
        static void Building(WorldFeature f, string model, float u, float v, bool along, Vector3 size, float flat, int shape = 0)
        {
            f.buildings.Add(new LandmarkBuilding { model = model, position = f.Area.Point(u, v),
                facing = along ? f.along : -f.Area.Across, size = size, flat = flat, shape = shape });
        }

        // Lift prevents z fighting in both the detailed and HLOD meshes; surface queries follow this order.
        static void Surface(WorldFeature f, float u, float v, float length, float width, LandmarkSurfaceKind kind)
        {
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = f.Area.Point(u, v), along = f.along,
                halfLength = length / 2f, halfWidth = width / 2f }, kind = kind, lift = kind == LandmarkSurfaceKind.Paint ? 0.14f : 0.08f + (int)kind * 0.01f });
        }

        // Broad ballast strips remain legible at kilometres; steel pairs are supplied by the detailed railway.
        static void RailSurface(WorldFeature f, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = (a + b) / 2f, along = d.normalized,
                halfLength = d.magnitude / 2f, halfWidth = 2.1f }, kind = LandmarkSurfaceKind.Rail, lift = 0.16f });
        }

        // Keep colours centralized for terrain hooks, local surfaces, HLOD geometry and the offline renderer.
        public static Color SurfaceColor(LandmarkSurfaceKind kind)
        {
            switch (kind)
            {
                case LandmarkSurfaceKind.Concrete: return new Color(0.55f, 0.55f, 0.51f);
                case LandmarkSurfaceKind.Asphalt: return new Color(0.15f, 0.15f, 0.15f);
                case LandmarkSurfaceKind.Paint: return new Color(0.85f, 0.85f, 0.82f);
                case LandmarkSurfaceKind.Rail: return new Color(0.27f, 0.26f, 0.24f);
                default: return new Color(0.42f, 0.40f, 0.36f);
            }
        }
    }
}
