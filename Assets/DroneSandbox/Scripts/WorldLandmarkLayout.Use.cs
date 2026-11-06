using System;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Use-driven wear: dirt, tyre tracks, stains, repairs and weeds go where vehicles and people actually work.
    public static partial class WorldLandmarkLayout
    {
        // Decals ride at least this high, above every graded slab, so LandmarkMesh can layer them without cutting.
        public const float DecalLift = .145f;

        // Works: slab repairs, loading doors, storage traffic, the tank farm, stockpiles, parked vans and the fence line.
        static void WorksUse(WorldFeature f, Random rng)
        {
            // Repairs come first so later traffic marks and stains lie over them in the painter and the slab mesh.
            Repairs(f, rng, -200f, 40f, -36f, 20f, 6);
            Repairs(f, rng, -200f, 40f, -118f, -92f, 5);
            Repairs(f, rng, 50f, 180f, 33f, 50f, 3);
            // Spilled aggregate spreads around each stockpile; a third pile is fed from the rail spur.
            Blob(f, rng, -166f, 3f, 1f, 0f, 31f, 25f, LandmarkSurfaceKind.Aggregate, .15f);
            Blob(f, rng, -147f, 21f, .8f, .6f, 17f, 13f, LandmarkSurfaceKind.Aggregate, .15f);
            Blob(f, rng, -173f, 69f, 1f, .2f, 21f, 16f, LandmarkSurfaceKind.Aggregate, .15f);
            Prop(f, "Scenery_AggregatePile", -173f, 69f, 7, rng, new Vector3(.75f, .7f, .7f));
            FenceWeeds(f, rng, -208f, -151f, 208f, -151f, 0f, 1f, 5f);
            FenceWeeds(f, rng, -208f, 155f, -9f, 155f, 0f, -1f, 4f);
            FenceWeeds(f, rng, 9f, 155f, 208f, 155f, 0f, -1f, 4f);
            FenceWeeds(f, rng, -209f, -146f, -209f, 46f, 1f, 0f, 5f);
            FenceWeeds(f, rng, -209f, 64f, -209f, 150f, 1f, 0f, 5f);
            FenceWeeds(f, rng, 209.5f, -146f, 209.5f, 150f, -1f, 0f, 2.6f);
            foreach (var b in f.buildings)
            {
                if (b.model == "Scenery_FactoryHall") Doors(f, rng, b, -117.5f, new[] { -.4f, -.2f, 0f, .2f, .4f });
                else if (b.model == "Scenery_Warehouse") Doors(f, rng, b, 47f, new[] { 0f });
                else if (b.model == "Scenery_StorageTank") TankStain(f, rng, b);
            }
            // Rail-fed freight shed: a dirty strip between its doors and the spur, with spills where wagons stand.
            Blob(f, rng, -36f, 60.5f, 1f, 0f, 36f, 6f, LandmarkSurfaceKind.Rut, .165f);
            for (int i = 0; i < 3; i++) Blob(f, rng, -52f + i * 15f + Range(rng, -3f, 3f), 59f + Range(rng, -1.5f, 1.5f), 1f, Range(rng, -.3f, .3f), Range(rng, 3f, 6f), Range(rng, 2f, 3.5f), LandmarkSurfaceKind.Stain, .175f);
            // Storage traffic leaves the top lane for the freight stacks and the shed's road side.
            Ruts(f, .165f, -150f, 134f, -152f, 121f, -141f, 113f);
            Ruts(f, .165f, -95f, 134f, -97f, 122f, -86f, 115f);
            Ruts(f, .165f, -30f, 134f, -32f, 110f, -38f, 91f);
            Blob(f, rng, -97f, 106f, 1f, 0f, 140f, 9f, LandmarkSurfaceKind.Rut, .163f);
            // Tankers stand at the pipe racks; drips and hose spills collect beneath.
            Ruts(f, .165f, 92f, 44f, 86f, 35f, 72f, 31.5f);
            Blob(f, rng, 71f, 30f, 1f, 0f, 16f, 6f, LandmarkSurfaceKind.Stain, .175f);
            // Parked vans drip where they wait.
            Blob(f, rng, 53f, 114f, 1f, .1f, 6f, 3.5f, LandmarkSurfaceKind.Stain, .175f);
            Blob(f, rng, -119f, -106f, 1f, -.1f, 6f, 3.5f, LandmarkSurfaceKind.Stain, .175f);
        }

        // Rail yard use: platform loading, road-served sheds and a warehouse, parked vans, an idle south margin and weedy fences.
        static void RailUse(WorldFeature f, Random rng)
        {
            Repairs(f, rng, -250f, 10f, 4f, 12f, 4);
            // Forklifts work the platform at three loading spots, beside the gantry and opposite the middle shed.
            foreach (float u in new[] { -118f, -62f, 150f, 188f })
            {
                Blob(f, rng, u, -1f, 1f, 0f, Range(rng, 9f, 13f), 6f, LandmarkSurfaceKind.Rut, .165f);
                Blob(f, rng, u + Range(rng, -3f, 3f), Range(rng, -2.5f, .5f), 1f, Range(rng, -.4f, .4f), Range(rng, 2.5f, 4.5f), Range(rng, 1.5f, 2.5f), LandmarkSurfaceKind.Stain, .175f);
                Prop(f, "Scenery_FreightGroup", u, -1f, 8, rng);
            }
            // Lorries turn on each pad and run along the apron to the shed doors, the gantry and the warehouse.
            Ruts(f, .165f, -230f, 17f, -214f, 7.5f, -182f, 6.5f, -160f, 8f);
            Ruts(f, .165f, 25f, 17f, 6f, 13f, -28f, 14.5f);
            Ruts(f, .165f, 25f, 17f, 45f, 8f, 100f, 7f, 123f, 8.5f);
            Ruts(f, .165f, 185f, 17f, 170f, 7.5f, 152f, 5f);
            Ruts(f, .165f, 185f, 17f, 210f, 7f, 245f, 6f, 256f, 14f);
            foreach (var b in f.buildings)
                if (b.model == "Scenery_FreightShed")
                {
                    Local(f, b.position, out float u, out float v);
                    float front = v - b.size.z * .5f;
                    Blob(f, rng, u, front - 3.5f, 1f, 0f, b.size.x * .8f, 6f, LandmarkSurfaceKind.Rut, .163f);
                    for (int i = 0; i < 3; i++)
                        Blob(f, rng, u + Range(rng, -16f, 16f), front - Range(rng, 1.5f, 5f), 1f, Range(rng, -.3f, .3f), Range(rng, 2.5f, 5.5f), Range(rng, 1.5f, 3f), LandmarkSurfaceKind.Stain, .175f);
                }
            Blob(f, rng, -65f, 53f, 1f, .1f, 6f, 3.5f, LandmarkSurfaceKind.Stain, .175f);
            Blob(f, rng, 88f, 53f, 1f, -.1f, 6f, 3.5f, LandmarkSurfaceKind.Stain, .175f);
            Blob(f, rng, 311f, 42f, 1f, .3f, 26f, 20f, LandmarkSurfaceKind.Aggregate, .15f);
            // Beyond the outer siding: a maintenance track, scrubby unused ground and weedy fences.
            Track(f, -300f, -47f, 330f, -47f);
            for (int i = 0; i < 9; i++)
                Blob(f, rng, -330f + i * 80f + Range(rng, -20f, 20f), Range(rng, -57f, -38f), 1f, Range(rng, -.15f, .15f),
                    Range(rng, 20f, 55f), Range(rng, 6f, 13f), i % 3 == 0 ? LandmarkSurfaceKind.Soil : LandmarkSurfaceKind.Overgrown, .158f);
            FenceWeeds(f, rng, -374f, -84.5f, 374f, -84.5f, 0f, 1f, 6f);
            FenceWeeds(f, rng, -374f, 84f, -8f, 84f, 0f, -1f, 3.5f);
            FenceWeeds(f, rng, 8f, 84f, 374f, 84f, 0f, -1f, 3.5f);
        }

        // Rectangular slab replacements in a slightly different shade, aligned with the yard's own joints.
        static void Repairs(WorldFeature f, Random rng, float u0, float u1, float v0, float v1, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float length = (float)Math.Round(Range(rng, 1f, 3.5f)) * 4f, width = (float)Math.Round(Range(rng, 1f, 2.4f)) * 3f;
                float u = Range(rng, u0 + length * .5f, u1 - length * .5f), v = Range(rng, v0 + width * .5f, v1 - width * .5f);
                Strip(f, u - length * .5f, v, u + length * .5f, v, width, LandmarkSurfaceKind.Patch, .15f);
            }
        }

        // Paired wheel lines along a local polyline; a short overlap at each bend keeps the marks continuous.
        static void Ruts(WorldFeature f, float lift, params float[] uv)
        {
            for (int i = 2; i + 1 < uv.Length; i += 2)
            {
                Vector2 a = new Vector2(uv[i - 2], uv[i - 1]), b = new Vector2(uv[i], uv[i + 1]), d = (b - a).normalized, side = new Vector2(-d.y, d.x);
                a -= d * .4f; b += d * .4f;
                foreach (float wheel in new[] { -.95f, .95f })
                    Strip(f, a.x + side.x * wheel, a.y + side.y * wheel, b.x + side.x * wheel, b.y + side.y * wheel, .6f, LandmarkSurfaceKind.Rut, lift);
            }
        }

        // Each door gets a working chance; active ones have a dirty apron, a drip stain and a lorry track to the lane.
        static void Doors(WorldFeature f, Random rng, LandmarkBuilding b, float laneV, float[] doors)
        {
            Local(f, b.position, out float bu, out float bv);
            Vector2 forward = new Vector2(Vector2.Dot(b.facing, f.along), Vector2.Dot(b.facing, f.Area.Across)), right = new Vector2(forward.y, -forward.x);
            Vector2 front = new Vector2(bu, bv) + forward * (b.size.z * .5f);
            int active = 0;
            for (int i = 0; i < doors.Length; i++)
            {
                if (rng.NextDouble() > .62 && !(i == doors.Length - 1 && active == 0)) continue;
                active++;
                Vector2 door = front + right * (doors[i] * b.size.x);
                Vector2 apron = door + forward * 4.5f;
                Blob(f, rng, apron.x, apron.y, right.x, right.y, Range(rng, 7f, 11f), Range(rng, 6f, 9f), LandmarkSurfaceKind.Rut, .163f);
                Vector2 drip = door + forward * Range(rng, 2f, 6f) + right * Range(rng, -2.5f, 2.5f);
                Blob(f, rng, drip.x, drip.y, right.x, right.y, Range(rng, 2.5f, 5f), Range(rng, 1.8f, 3f), LandmarkSurfaceKind.Stain, .175f);
                // Lorries reverse to the door, so the track swings to the lane's travel direction before meeting it.
                float swing = rng.NextDouble() < .5 ? -1f : 1f;
                Vector2 start = door + forward, end;
                if (Math.Abs(forward.y) > .7f)
                {
                    end = new Vector2(start.x + swing * 10f, laneV);
                    Vector2 mid = new Vector2(start.x + swing * 2.5f, (start.y + laneV) * .5f);
                    Ruts(f, .165f, start.x, start.y, mid.x, mid.y, end.x, end.y);
                }
                else
                {
                    Vector2 mid = start + forward * 7f;
                    end = new Vector2(mid.x + forward.x * 4f, laneV);
                    Ruts(f, .165f, start.x, start.y, mid.x, mid.y, end.x, end.y);
                }
            }
        }

        // Drips gather on the side of a tank where its valves and the tanker hose stand.
        static void TankStain(WorldFeature f, Random rng, LandmarkBuilding b)
        {
            Local(f, b.position, out float u, out float v);
            double angle = rng.NextDouble() * Math.PI * 2.0;
            float r = b.size.x * .5f + Range(rng, 1.5f, 3f);
            Vector2 d = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            Blob(f, rng, u + d.x * r, v + d.y * r, -d.y, d.x, Range(rng, 5f, 8f), Range(rng, 2.5f, 4f), LandmarkSurfaceKind.Stain, .175f);
        }

        // Coarse growth along a fence: uneven runs with gaps, set just inside the posts and never wider than the verge.
        static void FenceWeeds(WorldFeature f, Random rng, float u0, float v0, float u1, float v1, float iu, float iv, float maxWidth)
        {
            Vector2 a = new Vector2(u0, v0), d = new Vector2(u1 - u0, v1 - v0), inward = new Vector2(iu, iv);
            float length = d.magnitude, at = Range(rng, 0f, 12f);
            d /= Math.Max(.01f, length);
            while (at < length - 6f)
            {
                float run = Math.Min(length - at, Range(rng, 14f, 46f)), width = Range(rng, Math.Min(1.5f, maxWidth), maxWidth);
                Vector2 c = a + d * (at + run * .5f) + inward * (.4f + width * .5f);
                Blob(f, rng, c.x, c.y, d.x, d.y, run, width, LandmarkSurfaceKind.Overgrown, .158f);
                at += run + Range(rng, 4f, 24f);
            }
        }

        // A chamfered, slightly irregular decal on any local axis, flat at its own lift above the slabs.
        static void Blob(WorldFeature f, Random rng, float u, float v, float du, float dv, float length, float width, LandmarkSurfaceKind kind, float lift)
        {
            Vector2 axis = f.along * du + f.Area.Across * dv;
            axis = axis.sqrMagnitude > .0001f ? axis.normalized : f.along;
            var area = new FeatureArea { center = f.Area.Point(u, v), along = axis, halfLength = length * .5f, halfWidth = width * .5f };
            f.surfaces.Add(new LandmarkSurface { area = area, outline = Outline(area, rng), kind = kind, lift = lift });
        }

        // Feature-local coordinates of a world point, matching FeatureArea.Point.
        static void Local(WorldFeature f, Vector2 p, out float u, out float v)
        {
            Vector2 d = p - f.center;
            u = Vector2.Dot(d, f.along); v = Vector2.Dot(d, f.Area.Across);
        }
    }
}
