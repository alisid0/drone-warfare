using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Kinds 5-8 stay unused, as the HLOD building materials once shared these numbers; every surface now draws through
    // one palette material (LandmarkMesh.SurfaceSlot), so a new kind costs a palette row, not a draw call.
    public enum LandmarkSurfaceKind : byte { Gravel, Concrete, Asphalt, Paint, Rail, Soil = 9, Wear, Overgrown, Patch, Stain, TrackBed, Aggregate, Rut, Traffic, Scrub }

    // A real model's root and approximate outer volume, in world metres with its original yaw and scale.
    public struct LandmarkBuilding
    {
        public string model;
        public Vector2 position, facing;
        public Vector3 size;
        public float flat;
        public float scale;
        public bool detail;
        public int shape; // 0 pitched roof, 1 tank, 2 chimney, 3 flat roof, 4 cooling tower
        public int roof;  // 0 original sheeting, 1 weathered, 2 rusted, 3 recently replaced
    }

    // Small, non-interactive civilian dressings are grouped by work area for bounded near-only batches.
    public struct LandmarkProp
    {
        public string model;
        public Vector2 position, facing;
        public Vector3 scale;
        public int group;
    }

    // Flat surfaces share coordinates with the world grade and never acquire mission-seeded offsets.
    public struct LandmarkSurface
    {
        public FeatureArea area;
        public LandmarkSurfaceKind kind;
        public float lift;
        public Vector2[] outline;
        public Vector2 fall;
        // Soft edge width in metres per outline edge (the rectangle's four sides when there is no outline); null is hard.
        public float[] soft;
        // 0 the kind's own tone, 1 lighter, 2 darker; bays > 0 instead picks a tone per pour of that length along the area.
        public byte shade;
        public float bays;

        // The polygon is convex and counterclockwise; the bounding rectangle rejects most painter queries.
        public bool Contains(Vector2 p)
        {
            if (!area.Contains(p, .004f)) return false;
            if (outline == null) return true;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], d = outline[(i + 1) % outline.Length] - a, q = p - a;
                if (d.x * q.y - d.y * q.x < -.004f * d.magnitude) return false;
            }
            return true;
        }

        // How far inside its soft edge p lies: 0 on the outline, 1 once past every edge's width. The nearest edge in
        // width units wins, so the weight is piecewise linear and LandmarkMesh reproduces it exactly with vertices.
        public float Weight(Vector2 p)
        {
            if (soft == null) return 1f;
            float t = 1f;
            if (outline == null)
            {
                Vector2 d = p - area.center;
                float u = Vector2.Dot(d, area.along), v = Vector2.Dot(d, area.Across);
                if (soft[0] > 0f) t = Math.Min(t, (v + area.halfWidth) / soft[0]);
                if (soft[1] > 0f) t = Math.Min(t, (area.halfLength - u) / soft[1]);
                if (soft[2] > 0f) t = Math.Min(t, (area.halfWidth - v) / soft[2]);
                if (soft[3] > 0f) t = Math.Min(t, (u + area.halfLength) / soft[3]);
            }
            else for (int i = 0; i < outline.Length; i++)
            {
                if (soft[i] <= 0f) continue;
                Vector2 a = outline[i], d = outline[(i + 1) % outline.Length] - a;
                float length = d.magnitude;
                if (length < .001f) continue;
                t = Math.Min(t, (d.x * (p.y - a.y) - d.y * (p.x - a.x)) / (length * soft[i]));
            }
            return t < 0f ? 0f : t;
        }

        // The palette tone at p: a pour's own shade along a bay-laid slab, otherwise the surface's fixed shade.
        public int Tone(Vector2 p)
        {
            int variant = shade;
            if (bays > 0f)
            {
                int bay = (int)Math.Floor((Vector2.Dot(p - area.center, area.along) + area.halfLength) / bays);
                uint h = unchecked((uint)bay * 2654435761u ^ (uint)Mathf.RoundToInt(area.center.x) * 73856093u ^ (uint)Mathf.RoundToInt(area.center.y) * 19349663u);
                h = unchecked((h ^ (h >> 15)) * 2246822519u); h ^= h >> 13;
                int roll = (int)(h % 10u);
                variant = roll < 5 ? 0 : roll < 8 ? 1 : 2;
            }
            return (int)kind * 3 + variant;
        }
    }

    // One recipe runs once per world feature; SectorIdentity and LandmarkHlod consume these same placements.
    public static partial class WorldLandmarkLayout
    {
        // A saved variant changes building counts and yard equipment without consuming the mission RNG.
        public static void Build(WorldFeature f)
        {
            var rng = new Random(f.variantSeed);
            if (f.kind == WorldFeatureKind.Airfield) Airfield(f, rng);
            else if (f.kind == WorldFeatureKind.IndustrialZone) Industry(f, rng);
            else Railway(f, rng);
        }

        // Hangar doors face the working apron, with separate fuel, dispersal and road-service areas.
        static void Airfield(WorldFeature f, Random rng)
        {
            Region(f, rng, -120f, 85f, 330f, 135f, LandmarkSurfaceKind.Gravel);
            Region(f, rng, 188f, 99f, 220f, 102f, LandmarkSurfaceKind.Soil);
            Region(f, rng, -125f, 61f, 310f, 98f, LandmarkSurfaceKind.Concrete);
            Region(f, rng, 175f, 50f, 210f, 65f, LandmarkSurfaceKind.Concrete);
            Region(f, rng, -350f, 110f, 88f, 65f, LandmarkSurfaceKind.Gravel);
            Surface(f, 0f, -65f, 880f, 30f, LandmarkSurfaceKind.Asphalt);
            Surface(f, 0f, 0f, 820f, 14f, LandmarkSurfaceKind.Asphalt);
            foreach (float u in new[] { -380f, 380f }) Surface(f, u, -32f, 14f, 65f, LandmarkSurfaceKind.Asphalt);
            foreach (float u in new[] { -215f, -90f, 170f }) Surface(f, u, 20f, 16f, 40f, LandmarkSurfaceKind.Concrete);
            Building(f, "Scenery_HangarLarge", -215f, 94f, false, new Vector3(41.08f, 15.3f, 51.075f), 36f);
            Building(f, "Scenery_HangarLarge", -90f, 94f, false, new Vector3(41.08f, 15.3f, 51.075f), 36f);
            for (int i = 0; i < 3; i++)
            {
                float u = 95f + i * 67f;
                Region(f, rng, u, 77f, 48f, 51f, LandmarkSurfaceKind.Concrete, false);
                Building(f, "Scenery_AircraftShelter", u, 99f, false, new Vector3(24f, 9f, 30f), 22f);
            }
            Building(f, "Scenery_ControlTower", 312f, 112f, false, new Vector3(10.4f, 18.75f, 10.4f), 9f, 3);
            Building(f, "Scenery_FuelFarm", -350f, 111f, false, new Vector3(25f, 6.535f, 19f), 18f, 1);
            Building(f, "Scenery_OfficeBlock", 35f, 122f, false, new Vector3(24.2f, 11f, 15.735f), 18f, 3);
            Lane(f, 0f, 160f, 0f, 137f, 8f);
            Lane(f, -365f, 137f, 330f, 137f, 7f);
            Lane(f, -317f, 137f, -317f, 68f, 6f);
            Lane(f, 0f, 137f, 0f, 50f, 6f);
            // A gravel inspection circuit stays beyond the runway clearways and joins the service gate.
            Track(f, -480f, -132f, 480f, -132f); Track(f, -480f, -132f, -480f, 151f);
            Track(f, 480f, -132f, 480f, 151f); Track(f, -480f, 151f, 480f, 151f);
            for (int i = 0; i < 8; i++) Prop(f, "Scenery_FreightGroup", -273f + (i % 4) * 17f, 118f + (i / 4) * 10f, i / 4, rng);
            Vehicles(f, rng, -300f, 93f, 2); Vehicles(f, rng, 63f, 126f, 3);
            Prop(f, "Scenery_AggregatePile", 360f, 111f, 4, rng, new Vector3(.65f, .6f, .8f));
            Weather(f, rng, -140f, 43f, 225f, 26f, 18);
            Corners(f, rng); Perimeter(f, 492f, -146f, 158f, false);
            for (float u = -400f; u < 400f; u += 24f) Surface(f, u, -65f, 12f, 0.6f, LandmarkSurfaceKind.Paint);
            foreach (float v in new[] { -79f, -51f }) Surface(f, 0f, v, 872f, 0.4f, LandmarkSurfaceKind.Paint);
            foreach (float end in new[] { -1f, 1f }) for (float v = -76f; v <= -54f; v += 3f)
                Surface(f, end * 422f, v, 18f, 1.6f, LandmarkSurfaceKind.Paint);
        }

        // A freight spine separates production from storage; a loop connects every loading court to the gate.
        static void Industry(WorldFeature f, Random rng)
        {
            // Retain the old inventory rolls so decoration changes cannot silently inflate a variant's draw budget.
            var inventory = new Random(f.variantSeed);
            int warehouses = inventory.Next(3), tanks = 4 + inventory.Next(3);
            Region(f, rng, -77f, -56f, 253f, 167f, LandmarkSurfaceKind.Concrete);
            Region(f, rng, 114f, -83f, 140f, 131f, LandmarkSurfaceKind.Gravel);
            Region(f, rng, -80f, 96f, 252f, 84f, LandmarkSurfaceKind.Gravel);
            Region(f, rng, 115f, 80f, 138f, 99f, LandmarkSurfaceKind.Concrete);
            Region(f, rng, -167f, 5f, 64f, 65f, LandmarkSurfaceKind.Soil);
            Region(f, rng, 28f, 120f, 56f, 51f, LandmarkSurfaceKind.Asphalt);
            Building(f, "Scenery_FactoryHall", -141f, -64f, false, new Vector3(60.35f, 11.573f, 33.335f), 48f, 0, 1.45f);
            Building(f, "Scenery_FactoryHall", -33f, -68f, false, new Vector3(60.35f, 11.573f, 33.335f), 43f, 0, 1.25f);
            Building(f, "Scenery_FactoryHall", 97f, -89f, false, new Vector3(60.35f, 11.573f, 33.335f), 38f);
            Building(f, "Scenery_Warehouse", -141f, 74f, true, new Vector3(18f, 9f, 30f), 22f);
            if (warehouses > 0) Building(f, "Scenery_Warehouse", -91f, 74f, true, new Vector3(18f, 9f, 30f), 22f);
            Building(f, "Scenery_FreightShed", -36f, 76f, false, new Vector3(40.8f, 9.787f, 21.839f), 26f);
            Building(f, "Scenery_OfficeBlock", 29f, 112f, false, new Vector3(24.2f, 11f, 15.735f), 18f, 3);
            Building(f, "Scenery_CoolingTower", 165f, 92f, true, new Vector3(38f, 48f, 38f), 26f, 4);
            for (int i = 0; i < tanks; i++) Building(f, "Scenery_StorageTank", 78f + (i % 3) * 28f, 62f + (i / 3) * 30f,
                true, new Vector3(20.48f, 15.3f, 20.48f), 16f, 1);
            foreach (float u in new[] { -97f, 10f }) Building(f, "Scenery_Chimney", u, -29f, true, new Vector3(2.6f, 28f, 2.6f), 5f, 2);
            for (int i = 0; i < 2; i++) Building(f, "Scenery_PipeRack", 61f + i * 20f, 27f, true, new Vector3(4f, 5.5f, 20f), 0f, 3, 1f, true);
            // Keep 7 m lanes and 24 m turning courts outside door aprons and rail loading clearance.
            Lane(f, 0f, 165f, 0f, 138f, 8f); Lane(f, -190f, 138f, 199f, 138f, 7f);
            Lane(f, 199f, 138f, 199f, -123f, 7f); Lane(f, -190f, 138f, -190f, 42f, 7f);
            Lane(f, -190f, 42f, 199f, 42f, 7f); Lane(f, 199f, -123f, -182f, -123f, 7f);
            Lane(f, 35f, -123f, 35f, 42f, 7f); Lane(f, -182f, -123f, -182f, -100f, 7f);
            foreach (float u in new[] { -180f, 35f, 194f }) Region(f, rng, u, -119f, 24f, 24f, LandmarkSurfaceKind.Gravel, false);
            Track(f, -190f, 42f, -164f, 8f); Track(f, 199f, -20f, 163f, -20f);
            // The spur reaches the loading doors; its outside approach is admitted only after a dry-route check.
            WorksRail(f);
            for (int i = 0; i < 12; i++)
            {
                bool loading = i < 7;
                Prop(f, "Scenery_FreightGroup", loading ? -163f + (i % 7) * 22f : 71f + (i % 3) * 30f,
                    loading ? 101f + (i % 2) * 9f : -43f + (i / 10) * 11f, loading ? i / 4 : 3, rng);
            }
            Vehicles(f, rng, 53f, 114f, 4); Vehicles(f, rng, -119f, -106f, 5);
            Prop(f, "Scenery_AggregatePile", -169f, 2f, 7, rng, new Vector3(1.3f, 1.2f, 1f));
            Prop(f, "Scenery_AggregatePile", -148f, 21f, 7, rng, new Vector3(.7f, .6f, .8f));
            Corners(f, rng); WorksUse(f, rng); Perimeter(f, 212f, -154f, 158f, f.rails.Count > 0);
        }

        // Four 530-590 m sidings at 5.5 m spacing join the actual railway; the yard faces its road gate.
        static void Railway(WorldFeature f, Random rng)
        {
            var inventory = new Random(f.variantSeed); // rake counts remain independent of new surface wear rolls
            Region(f, rng, 0f, -29f, 775f, 61f, LandmarkSurfaceKind.Gravel);
            Region(f, rng, -124f, 32f, 286f, 54f, LandmarkSurfaceKind.Concrete);
            Region(f, rng, 175f, 28f, 263f, 58f, LandmarkSurfaceKind.Gravel);
            Region(f, rng, 301f, 60f, 130f, 53f, LandmarkSurfaceKind.Soil);
            var a = f.Area;
            Vector2 throat = WorldPlan.FeatureRailPoint(a.Point(-390f, -62f), out _);
            // A rust-brown formation and lighter shoulders go down first; the dark ballast strips are laid over them.
            var lines = new Vector2[4][];
            for (int track = 0; track < 4; track++)
            {
                float v = -30f + track * 5.5f, begin = -290f + track * 20f;
                lines[track] = new[] { throat, a.Point(-350f + track * 12f, -62f), a.Point(begin, v), a.Point(300f, v) };
                for (int i = 1; i < lines[track].Length; i++) Formation(f, lines[track][i - 1], lines[track][i]);
            }
            foreach (float v in new[] { -34.6f, -8.9f }) Strip(f, -235f, v, 302f, v, 2.4f, LandmarkSurfaceKind.Aggregate, DecalLift);
            // One siding stands idle and one holds a short rake, so the yard is never a full diagram of wagons.
            int idle = (f.variantSeed >> 4) & 3, shortRake = (idle + 1 + ((f.variantSeed >> 7) & 1)) & 3;
            var rakeEnd = new float[4];
            for (int track = 0; track < 4; track++)
            {
                float v = -30f + track * 5.5f;
                var line = lines[track];
                f.rails.Add(DenseRail(line));
                for (int i = 1; i < line.Length; i++) RailSurface(f, line[i - 1], line[i]);
                int count = 5 + inventory.Next(5);
                if (track == idle) count = 0;
                else if (track == shortRake) count = Math.Max(2, count / 2);
                float start = -125f + track * 43f;
                for (int i = 0; i < count; i++) Building(f, track % 2 == 0 ? "Scenery_RailContainerWagon" : "Scenery_RailHopper",
                    start + i * 16.6f, v, true, new Vector3(3.14f, track % 2 == 0 ? 4.45f : 5.535f, 15f), 0f, 3, 1f, true);
                rakeEnd[track] = count > 0 ? start + count * 16.6f : -290f + track * 20f;
            }
            // Weeds take the idle siding along its length and the gaps between sidings beyond the standing rakes.
            for (float u = -280f + idle * 20f + Range(rng, 0f, 15f); u < 290f;)
            {
                float run = Math.Min(296f - u, Range(rng, 18f, 55f));
                Blob(f, rng, u + run * .5f, -30f + idle * 5.5f, 1f, 0f, run, Range(rng, 4.6f, 6.2f), LandmarkSurfaceKind.Overgrown, .162f);
                u += run + Range(rng, 3f, 14f);
            }
            for (int gap = 0; gap < 3; gap++)
            {
                float from = Math.Max(rakeEnd[gap], rakeEnd[gap + 1]) + Range(rng, 8f, 20f);
                if (from < 290f) Strip(f, from, -27.25f + gap * 5.5f, 297f, -27.25f + gap * 5.5f, 1.2f, LandmarkSurfaceKind.Overgrown, .162f);
            }
            Blob(f, rng, 312f, -21.75f, 1f, 0f, 30f, 26f, LandmarkSurfaceKind.Overgrown, .162f);
            for (int i = 0; i < 3; i++) Building(f, "Scenery_FreightShed", -179f + i * 151f, 23f + (i % 2) * 8f, false, new Vector3(40.8f, 9.787f, 21.839f), 26f);
            Building(f, "Scenery_SignalBox", -270f, 28f, true, new Vector3(10.6f, 8.15f, 11.135f), 10f, 3);
            Building(f, "Scenery_Warehouse", 232f, 21f, true, new Vector3(18f, 9f, 30f), 22f);
            Building(f, "Scenery_GantryCrane", 170f, -21.75f, true, new Vector3(18f, 13f, 15f), 0f, 3, 1f, true);
            Surface(f, 20f, -1f, 560f, 7f, LandmarkSurfaceKind.Concrete);
            Lane(f, 0f, 90f, 0f, 62f, 8f); Lane(f, -297f, 62f, 340f, 62f, 7f);
            foreach (float u in new[] { -230f, 25f, 185f })
            { Lane(f, u, 62f, u, 9f, 6f); Region(f, rng, u, 10f, 24f, 23f, LandmarkSurfaceKind.Gravel, false); }
            Track(f, -340f, 76f, 358f, 76f);
            for (int i = 0; i < 12; i++) Prop(f, "Scenery_FreightGroup", -208f + (i % 6) * 85f, 43f + (i / 6) * 10f, i % 6, rng);
            Vehicles(f, rng, -65f, 53f, 6); Vehicles(f, rng, 88f, 53f, 7);
            Prop(f, "Scenery_AggregatePile", 311f, 42f, 5, rng, new Vector3(1.1f, 1f, .8f));
            Corners(f, rng); RailUse(f, rng); Perimeter(f, 377f, -87f, 87f, true);
        }

        // The rust-stained formation is wider than the ballast, showing between neighbouring sidings and at their ends.
        static void Formation(WorldFeature f, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = (a + b) / 2f, along = d.normalized,
                halfLength = d.magnitude / 2f + 1.5f, halfWidth = 3.4f }, kind = LandmarkSurfaceKind.TrackBed, lift = DecalLift });
        }

        // Short collinear neighbours stop MapLayout's Catmull-Rom pass overshooting a long siding at the throat.
        static Vector2[] DenseRail(Vector2[] corners)
        {
            var points = new System.Collections.Generic.List<Vector2> { corners[0] };
            for (int i = 1; i < corners.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(corners[i - 1], corners[i]) / 12f));
                for (int j = 1; j <= steps; j++) points.Add(Vector2.Lerp(corners[i - 1], corners[i], j / (float)steps));
            }
            return points.ToArray();
        }

        // The facing convention is identical to SectorIdentity.Site and the FBX roots used by Features.
        static void Building(WorldFeature f, string model, float u, float v, bool along, Vector3 size, float flat, int shape = 0, float scale = 1f, bool detail = false)
        {
            f.buildings.Add(new LandmarkBuilding { model = model, position = f.Area.Point(u, v),
                facing = along ? f.along : -f.Area.Across, size = size * scale, flat = flat, shape = shape, scale = scale, detail = detail,
                roof = detail || shape == 1 || shape == 2 || shape == 4 ? 0 : RoofAge(f.variantSeed, u, v) });
        }

        // Roofs were re-sheeted at different times: about 40% original, then weathered, rusted and new, by position only.
        static int RoofAge(int seed, float u, float v)
        {
            uint h = unchecked((uint)seed * 2654435761u ^ (uint)Mathf.RoundToInt(u * 4f) * 73856093u ^ (uint)Mathf.RoundToInt(v * 4f) * 19349663u);
            h = unchecked((h ^ (h >> 15)) * 2246822519u); h ^= h >> 13;
            int roll = (int)(h % 20u);
            return roll < 8 ? 0 : roll < 13 ? 1 : roll < 16 ? 2 : 3;
        }

        // Lift prevents z fighting in both the detailed and HLOD meshes; surface queries follow this order.
        static void Surface(WorldFeature f, float u, float v, float length, float width, LandmarkSurfaceKind kind)
        {
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = f.Area.Point(u, v), along = f.along,
                halfLength = length / 2f, halfWidth = width / 2f }, kind = kind, lift = kind == LandmarkSurfaceKind.Paint ? 0.19f : 0.12f + f.surfaces.Count * 0.0001f });
        }

        // Broad ballast strips remain legible at kilometres; steel pairs are supplied by the detailed railway.
        static void RailSurface(WorldFeature f, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = (a + b) / 2f, along = d.normalized,
                halfLength = d.magnitude / 2f, halfWidth = 2.1f }, kind = LandmarkSurfaceKind.Rail, lift = 0.16f });
        }

        // Keep colours centralized for terrain hooks, local surfaces, HLOD geometry and the offline renderer.
        public static Color SurfaceColor(LandmarkSurfaceKind kind) => ToneColor((int)kind * 3);

        // Three tones per kind (its own, lighter, darker) and one verge tone for soft edges that run out onto grass.
        public const int KindCount = 19, ToneCount = KindCount * 3 + 1, VergeTone = ToneCount - 1;
        static readonly Color[] tones =
        {
            new Color(.42f, .40f, .36f), new Color(.47f, .45f, .40f), new Color(.36f, .345f, .31f),    // gravel
            new Color(.55f, .55f, .51f), new Color(.565f, .56f, .52f), new Color(.525f, .52f, .48f),   // concrete
            new Color(.15f, .15f, .15f), new Color(.19f, .19f, .18f), new Color(.12f, .12f, .12f),     // asphalt
            new Color(.85f, .85f, .82f), new Color(.85f, .85f, .82f), new Color(.70f, .70f, .66f),     // paint
            new Color(.21f, .20f, .19f), new Color(.28f, .26f, .23f), new Color(.16f, .15f, .14f),     // rail ballast: dusty, oily
            new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f),     // 5-8 unused
            new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f),
            new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f),
            new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f), new Color(.42f, .40f, .36f),
            new Color(.32f, .29f, .23f), new Color(.38f, .34f, .27f), new Color(.27f, .24f, .19f),     // soil
            new Color(.24f, .25f, .23f), new Color(.30f, .30f, .28f), new Color(.20f, .20f, .19f),     // wear
            new Color(.32f, .37f, .25f), new Color(.38f, .40f, .27f), new Color(.27f, .33f, .21f),     // overgrown: dry, lush
            new Color(.46f, .46f, .42f), new Color(.575f, .575f, .535f), new Color(.41f, .41f, .38f),   // repair: newer, older
            new Color(.22f, .21f, .19f), new Color(.30f, .28f, .25f), new Color(.17f, .16f, .15f),     // stain: faded, fresh
            new Color(.36f, .26f, .19f), new Color(.40f, .31f, .23f), new Color(.31f, .22f, .16f),     // track bed
            new Color(.60f, .57f, .50f), new Color(.64f, .61f, .54f), new Color(.52f, .49f, .43f),     // aggregate
            new Color(.33f, .30f, .25f), new Color(.38f, .35f, .30f), new Color(.28f, .25f, .21f),     // rut
            new Color(.40f, .39f, .35f), new Color(.45f, .44f, .40f), new Color(.34f, .33f, .29f),     // traffic-worn slab
            new Color(.27f, .32f, .19f), new Color(.31f, .35f, .21f), new Color(.23f, .28f, .17f),     // scrub
            new Color(.40f, .44f, .29f),                                                               // verge grass
        };

        // sRGB colour of one palette tone.
        public static Color ToneColor(int tone) => tones[tone];

        // Soft edges ease in and out: smoothstep sampled at eight points and joined linearly, exactly what the GPU's
        // bilinear filter makes of the palette's eight texels per tone pair.
        public static float Profile(float t)
        {
            float x = Math.Max(0f, Math.Min(1f, t)) * 7f;
            int k = Math.Min(6, (int)x);
            float a = k / 7f, b = (k + 1) / 7f;
            float sa = a * a * (3f - 2f * a), sb = b * b * (3f - 2f * b);
            return sa + (sb - sa) * (x - k);
        }

        // What a viewer sees at p when surface top is the highest there: its tone, fading through its soft edge into the
        // flat tone of the next surface down (or verge grass). Painters, queries and the slab mesh all use this rule.
        public static Color Shade(IList<LandmarkSurface> surfaces, int top, Vector2 p)
        {
            var s = surfaces[top];
            Color colour = tones[s.Tone(p)];
            if (s.soft == null) return colour;
            float w = s.Weight(p);
            if (w >= 1f) return colour;
            Color under = tones[VergeTone];
            for (int i = top - 1; i >= 0; i--)
                if (surfaces[i].Contains(p)) { under = tones[surfaces[i].Tone(p)]; break; }
            return Color.Lerp(under, colour, Profile(w));
        }
    }
}
