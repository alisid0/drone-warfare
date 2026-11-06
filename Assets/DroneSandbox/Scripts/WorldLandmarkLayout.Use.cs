using System;
using UnityEngine;
using Random = System.Random;

namespace DroneSandbox
{
    // Use-driven wear: dirt, tyre tracks, stains, repairs and weeds go where vehicles and people actually work. Patches
    // have irregular outlines and soft edges (LandmarkSurface.soft), so they read as accumulated wear, not cut shapes.
    public static partial class WorldLandmarkLayout
    {
        // Decals ride at least this high, above every graded slab, so LandmarkMesh can layer them without cutting.
        public const float DecalLift = .145f;
        // Broad traffic wear lies a layer gap above the slabs and the lane shoulders and a gap below the aprons, stains and
        // wheel lines, so those are drawn over it rather than cutting it into pieces.
        const float TrafficLift = .125f;
        // Spills, repairs, aprons, weeds and pads lie a gap above the traffic; stains and wheel lines a gap above those.
        const float PatchLift = .162f, MarkLift = .198f;

        // Works: worn routes joining the gate, loading doors, storage and equipment; repairs where lorries turn; spills,
        // stains, small working corners and the fence line.
        static void WorksUse(WorldFeature f, Random rng)
        {
            // Forecourts in front of each hall's doors carry the most traffic; repairs gather where lorries turn on them.
            foreach (var b in f.buildings)
            {
                if (b.model != "Scenery_FactoryHall") continue;
                Local(f, b.position, out float hu, out float hv);
                float front = hv - b.size.z * .5f, depth = Math.Min(17f, front + 116.5f);
                Blob(f, rng, hu, front - depth * .5f - .5f, 1f, 0f, b.size.x * .95f, depth, LandmarkSurfaceKind.Traffic, TrafficLift, 2f);
                if (hv < -80f) Tint(f, 2); // the hall on the gravel: compacted, darker than the slab's traffic polish
                Repairs(f, rng, hu - b.size.x * .45f, hu + b.size.x * .45f, front - depth - 2f, front - 4f, 3);
            }
            Repairs(f, rng, -200f, 40f, -36f, 20f, 3);
            Repairs(f, rng, 50f, 180f, 33f, 50f, 2);
            // Spilled aggregate spreads around each stockpile; a third pile is fed from the rail spur.
            Spill(f, rng, -166f, 3f, 1f, 0f, 33f, 27f);
            Spill(f, rng, -147f, 21f, .8f, .6f, 19f, 15f);
            Spill(f, rng, -173f, 69f, 1f, .2f, 23f, 18f);
            Prop(f, "Scenery_AggregatePile", -173f, 69f, 7, rng, new Vector3(.75f, .7f, .7f));
            // The loader shuttles between the piles, the spur and the side lane on a churned soil track.
            Route(f, 5f, LandmarkSurfaceKind.Rut, 0, -190f, 30f, -172f, 22f, -158f, 10f, -150f, -8f);
            Ruts(f, MarkLift, -190f, 30f, -172f, 22f, -158f, 10f, -150f, -8f);
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
            Blob(f, rng, -36f, 60.5f, 1f, 0f, 38f, 8f, LandmarkSurfaceKind.Rut, PatchLift, 2f);
            for (int i = 0; i < 3; i++) Stain(f, rng, -52f + i * 15f + Range(rng, -3f, 3f), 59f + Range(rng, -1.5f, 1.5f), Range(rng, 3f, 6f), Range(rng, 2f, 3.5f));
            // Storage traffic leaves the top lane for the freight stacks and the shed's road side, on worn bands.
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, -150f, 133f, -152f, 121f, -141f, 113f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, -95f, 133f, -97f, 122f, -86f, 115f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, -30f, 133f, -32f, 110f, -38f, 91f);
            Ruts(f, MarkLift, -150f, 134f, -152f, 121f, -141f, 113f);
            Ruts(f, MarkLift, -95f, 134f, -97f, 122f, -86f, 115f);
            Ruts(f, MarkLift, -30f, 134f, -32f, 110f, -38f, 91f);
            Blob(f, rng, -97f, 106f, 1f, 0f, 142f, 11f, LandmarkSurfaceKind.Rut, PatchLift, 2f);
            // Tankers leave the middle lane for the pipe racks, and a service track runs between the tank rows.
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 0, 92f, 47f, 86f, 35f, 70f, 31.5f);
            Route(f, 4f, LandmarkSurfaceKind.Traffic, 0, 60f, 47f, 64f, 76f, 150f, 77f);
            Ruts(f, MarkLift, 92f, 44f, 86f, 35f, 72f, 31.5f);
            Stain(f, rng, 71f, 30f, 16f, 6f);
            // Parked vans drip where they wait, at the end of a short worn turn-in from the lane.
            Route(f, 4f, LandmarkSurfaceKind.Traffic, 0, 53f, 133f, 53f, 118f);
            Route(f, 4f, LandmarkSurfaceKind.Traffic, 0, -119f, -117.5f, -119f, -103f);
            Stain(f, rng, 53f, 114f, 6f, 3.5f);
            Stain(f, rng, -119f, -106f, 6f, 3.5f);
            WorkCorners(f, rng);
        }

        // Small irregular working corners: skips and pallets beside the second hall, a parked trailer bay on the gravel,
        // and a weedy unused corner with an abandoned container. Props join two spatial groups of their own.
        static void WorkCorners(WorldFeature f, Random rng)
        {
            Blob(f, rng, 17f, -104f, 1f, .15f, 22f, 15f, LandmarkSurfaceKind.Gravel, PatchLift, 1.8f); Tint(f, 2);
            Stain(f, rng, 12f, -101f, 5f, 3f);
            Route(f, 4f, LandmarkSurfaceKind.Traffic, 0, 17f, -117.5f, 17f, -108f);
            Prop(f, "Scenery_FreightGroup", 11f, -104f, 8, rng, new Vector3(.8f, .55f, .6f));
            Prop(f, "Scenery_FreightGroup", 22f, -103f, 8, rng, new Vector3(.8f, .55f, .6f));
            Blob(f, rng, 166f, -45f, 0f, 1f, 36f, 30f, LandmarkSurfaceKind.Gravel, PatchLift, 2f); Tint(f, 2);
            Route(f, 5f, LandmarkSurfaceKind.Traffic, 2, 193f, -45f, 178f, -46f);
            for (int i = 0; i < 2; i++)
            {
                Prop(f, "Scenery_UtilityVehicles", 158f + i * 11f, -45f, 9, rng);
                Stain(f, rng, 158f + i * 11f + Range(rng, -2f, 2f), -45f + Range(rng, -4f, 4f), Range(rng, 3f, 5f), Range(rng, 2f, 3f));
            }
            Blob(f, rng, 176f, -139f, 1f, .1f, 44f, 20f, LandmarkSurfaceKind.Overgrown, PatchLift, 2f);
            Blob(f, rng, 186f, -142f, 1f, -.2f, 20f, 11f, LandmarkSurfaceKind.Scrub, PatchLift, 2f);
            Blob(f, rng, 163f, -135f, .7f, .7f, 14f, 9f, LandmarkSurfaceKind.Soil, PatchLift, 1.5f);
            Prop(f, "Scenery_FreightGroup", 170f, -139f, 9, rng);
        }

        // Rail yard use: platform loading, road-served sheds and a warehouse, parked vans, an idle south margin, a worn
        // access road and scrubby fences.
        static void RailUse(WorldFeature f, Random rng)
        {
            Repairs(f, rng, -250f, 10f, 4f, 12f, 4);
            // Forklifts work the platform at three loading spots, beside the gantry and opposite the middle shed.
            foreach (float u in new[] { -118f, -62f, 150f, 188f })
            {
                Blob(f, rng, u, -1f, 1f, 0f, Range(rng, 11f, 15f), 7f, LandmarkSurfaceKind.Rut, .165f, 2f);
                Stain(f, rng, u + Range(rng, -3f, 3f), Range(rng, -2.5f, .5f), Range(rng, 2.5f, 4.5f), Range(rng, 1.5f, 2.5f));
                Prop(f, "Scenery_FreightGroup", u, -1f, 8, rng);
            }
            // Lorries turn on each pad and run along the apron to the shed doors, the gantry and the warehouse.
            // The worn bands start beside each pad's lane, so the asphalt itself stays dark.
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 0, -225f, 10f, -214f, 8f, -182f, 7f, -160f, 8f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 0, 20f, 11f, 6f, 13f, -28f, 14.5f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, 30f, 10f, 45f, 8f, 100f, 7f, 123f, 8.5f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, 180f, 10f, 170f, 7.5f, 152f, 5f);
            Route(f, 4.5f, LandmarkSurfaceKind.Traffic, 2, 190f, 10f, 210f, 7f, 245f, 6f, 256f, 14f);
            Ruts(f, MarkLift, -230f, 17f, -214f, 7.5f, -182f, 6.5f, -160f, 8f);
            Ruts(f, MarkLift, 25f, 17f, 6f, 13f, -28f, 14.5f);
            Ruts(f, MarkLift, 25f, 17f, 45f, 8f, 100f, 7f, 123f, 8.5f);
            Ruts(f, MarkLift, 185f, 17f, 170f, 7.5f, 152f, 5f);
            Ruts(f, MarkLift, 185f, 17f, 210f, 7f, 245f, 6f, 256f, 14f);
            foreach (var b in f.buildings)
                if (b.model == "Scenery_FreightShed")
                {
                    Local(f, b.position, out float u, out float v);
                    float front = v - b.size.z * .5f;
                    Blob(f, rng, u, front - 3.5f, 1f, 0f, b.size.x * .85f, 8f, LandmarkSurfaceKind.Rut, PatchLift, 2f);
                    for (int i = 0; i < 3; i++)
                        Stain(f, rng, u + Range(rng, -16f, 16f), front - Range(rng, 1.5f, 5f), Range(rng, 2.5f, 5.5f), Range(rng, 1.5f, 3f));
                }
            Stain(f, rng, -65f, 53f, 6f, 3.5f);
            Stain(f, rng, 88f, 53f, 6f, 3.5f);
            Spill(f, rng, 311f, 42f, 1f, .3f, 28f, 22f);
            // The access road from the gate is broken at its edges, patched, and muddy where it meets the yard lane.
            WornRoad(f, rng, 0f, 90f, 0f, 62f, 8f);
            WornRoad(f, rng, -297f, 62f, 340f, 62f, 7f);
            // Beyond the outer siding: a maintenance track, scrubby unused ground and weedy fences.
            Track(f, -300f, -47f, 330f, -47f);
            for (int i = 0; i < 9; i++)
                Blob(f, rng, -330f + i * 80f + Range(rng, -20f, 20f), Range(rng, -57f, -38f), 1f, Range(rng, -.15f, .15f),
                    Range(rng, 20f, 55f), Range(rng, 6f, 13f), i % 3 == 0 ? LandmarkSurfaceKind.Soil : LandmarkSurfaceKind.Overgrown, PatchLift, 2f);
            // The yard's grass edge is not a clean line: soil and weed tongues straddle the ends and the south rim.
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Blob(f, rng, side * Range(rng, 378f, 392f), Range(rng, -55f, 20f), Range(rng, -.3f, .3f), 1f, Range(rng, 14f, 30f), Range(rng, 7f, 12f),
                    i < 3 ? LandmarkSurfaceKind.Overgrown : LandmarkSurfaceKind.Soil, PatchLift, 2f);
            }
            for (float u = -360f + Range(rng, 0f, 30f); u < 360f; u += Range(rng, 40f, 90f))
                Blob(f, rng, u, -60f + Range(rng, -2f, 2f), 1f, Range(rng, -.1f, .1f), Range(rng, 12f, 34f), Range(rng, 5f, 9f),
                    rng.NextDouble() < .4 ? LandmarkSurfaceKind.Soil : LandmarkSurfaceKind.Overgrown, PatchLift, 2f);
            FenceWeeds(f, rng, -374f, -84.5f, 374f, -84.5f, 0f, 1f, 6f);
            FenceWeeds(f, rng, -374f, 84f, -8f, 84f, 0f, -1f, 3.5f);
            FenceWeeds(f, rng, 8f, 84f, 374f, 84f, 0f, -1f, 3.5f);
        }

        // Ballast is not one even grey: a long straight siding at v is laid in runs that are dustier or oilier, each fading
        // in from the run before, with oil dripped where wagons stand and spilled load under the hoppers.
        static void Ballast(WorldFeature f, Random rng, float u0, float u1, float v, float rakeStart, float rakeEnd, bool hoppers)
        {
            for (float at = u0; at < u1;)
            {
                float run = Math.Min(u1 - at, Range(rng, 40f, 110f)), from = at > u0 ? at - 3f : at;
                f.surfaces.Add(new LandmarkSurface { area = new FeatureArea { center = f.Area.Point((from + at + run) * .5f, v), along = f.along,
                    halfLength = (at + run - from) * .5f, halfWidth = 2.1f }, kind = LandmarkSurfaceKind.Rail, lift = 0.16f });
                if (at > u0)
                {
                    int index = f.surfaces.Count - 1; var s = f.surfaces[index];
                    s.soft = new[] { 0f, 0f, 0f, 3f }; s.shade = (byte)rng.Next(3); f.surfaces[index] = s;
                }
                at += run;
            }
            for (float u = rakeStart + Range(rng, 2f, 8f); u < rakeEnd - 4f; u += Range(rng, 9f, 22f))
            {
                if (hoppers && rng.NextDouble() < .5)
                {
                    Blob(f, rng, u, v, 1f, 0f, Range(rng, 6f, 14f), Range(rng, 3.2f, 4.2f), LandmarkSurfaceKind.Aggregate, MarkLift, 1.2f);
                    Tint(f, 2); // spilled load, dulled by the ballast dust
                }
                else Blob(f, rng, u, v, 1f, 0f, Range(rng, 3f, 7f), Range(rng, 1.4f, 2f), LandmarkSurfaceKind.Stain, MarkLift, .7f);
            }
        }

        // A worn paved road: faded and patched asphalt in places, mud and broken edges where the verge has been driven.
        static void WornRoad(WorldFeature f, Random rng, float u0, float v0, float u1, float v1, float width)
        {
            Vector2 a = new Vector2(u0, v0), d = new Vector2(u1 - u0, v1 - v0); float length = d.magnitude; d /= Math.Max(.01f, length);
            Vector2 side = new Vector2(-d.y, d.x);
            for (float at = Range(rng, 2f, 12f); at < length - 2f; at += Range(rng, 14f, 40f))
            {
                Vector2 c = a + d * at;
                Blob(f, rng, c.x + side.x * Range(rng, -1f, 1f), c.y + side.y * Range(rng, -1f, 1f), d.x, d.y, Range(rng, 3f, 9f), Range(rng, 2.5f, width * .8f),
                    LandmarkSurfaceKind.Asphalt, PatchLift, 1f);
                Tint(f, 1); // faded, older surfacing and patches
                float edge = rng.NextDouble() < .5 ? -1f : 1f;
                Vector2 e = c + side * (edge * (width * .5f + Range(rng, -.4f, .6f))) + d * Range(rng, -6f, 6f);
                Blob(f, rng, e.x, e.y, d.x, d.y, Range(rng, 4f, 11f), Range(rng, 1.6f, 3f), LandmarkSurfaceKind.Soil, PatchLift, 1f);
                Tint(f, 2);
            }
        }

        // Large, faint tone patches over a gravel yard: compacted and darker where vehicles stand, paler where loose.
        static void Mottle(WorldFeature f, Random rng, float u, float v, float length, float width, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Blob(f, rng, u + Range(rng, -.4f, .4f) * length, v + Range(rng, -.4f, .4f) * width, 1f, Range(rng, -.3f, .3f),
                    Range(rng, .15f, .3f) * length, Range(rng, .25f, .5f) * width, LandmarkSurfaceKind.Gravel, .105f, 2f);
                Tint(f, 1 + i % 2);
            }
        }

        // Slab repairs in older or newer concrete: irregular, soft-edged and mostly close in tone to the slab, so they
        // read as patching that built up over the years rather than placed tiles.
        static void Repairs(WorldFeature f, Random rng, float u0, float u1, float v0, float v1, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float length = Range(rng, 4f, 13f), width = Range(rng, 3f, 7f);
                float u = Range(rng, u0 + length * .5f, u1 - length * .5f), v = Range(rng, v0 + width * .5f, v1 - width * .5f);
                Blob(f, rng, u, v, 1f, Range(rng, -.08f, .08f), length, width, LandmarkSurfaceKind.Patch, PatchLift, Range(rng, .8f, 2f));
                double roll = rng.NextDouble();
                if (roll < .1) Tint(f, 1); else if (roll < .55) Tint(f, 2);
            }
        }

        // A broad worn band along a local polyline, under the wheel lines: the visibly used way between two places.
        // Segments overlap at bends and each fades into the one before, so the band reads as one continuous route.
        static void Route(WorldFeature f, float width, LandmarkSurfaceKind kind, int shade, params float[] uv)
        {
            for (int i = 2; i + 1 < uv.Length; i += 2)
            {
                Vector2 a = new Vector2(uv[i - 2], uv[i - 1]), b = new Vector2(uv[i], uv[i + 1]), d = (b - a).normalized;
                a -= d * (i == 2 ? 0f : width * .5f); b += d * (i + 2 < uv.Length ? width * .5f : 0f);
                Strip(f, a.x, a.y, b.x, b.y, width, kind, TrafficLift);
                Soften(f, Math.Min(2f, width * .45f));
                if (shade != 0) Tint(f, shade);
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

        // Each door gets a working chance; active ones have a dirty apron, a drip stain and a worn lorry route to the lane.
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
                // Lorries reverse to the door, so the route swings to the lane's travel direction before meeting it.
                float swing = rng.NextDouble() < .5 ? -1f : 1f;
                Vector2 start = door + forward, mid, end;
                if (Math.Abs(forward.y) > .7f)
                {
                    end = new Vector2(start.x + swing * 10f, laneV);
                    mid = new Vector2(start.x + swing * 2.5f, (start.y + laneV) * .5f);
                }
                else
                {
                    mid = start + forward * 7f;
                    end = new Vector2(mid.x + forward.x * 4f, laneV);
                }
                Route(f, 4.6f, LandmarkSurfaceKind.Traffic, bv < -80f ? 2 : 0, start.x, start.y, mid.x, mid.y, end.x, end.y);
                Blob(f, rng, apron.x, apron.y, right.x, right.y, Range(rng, 8f, 12f), Range(rng, 7f, 10f), LandmarkSurfaceKind.Rut, PatchLift, 2f);
                Vector2 drip = door + forward * Range(rng, 2f, 6f) + right * Range(rng, -2.5f, 2.5f);
                Blob(f, rng, drip.x, drip.y, right.x, right.y, Range(rng, 2.5f, 5f), Range(rng, 1.8f, 3f), LandmarkSurfaceKind.Stain, MarkLift, .9f);
                Ruts(f, MarkLift, start.x, start.y, mid.x, mid.y, end.x, end.y);
            }
        }

        // Drips gather on the side of a tank where its valves and the tanker hose stand.
        static void TankStain(WorldFeature f, Random rng, LandmarkBuilding b)
        {
            Local(f, b.position, out float u, out float v);
            double angle = rng.NextDouble() * Math.PI * 2.0;
            float r = b.size.x * .5f + Range(rng, 1.5f, 3f);
            Vector2 d = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            Blob(f, rng, u + d.x * r, v + d.y * r, -d.y, d.x, Range(rng, 5f, 8f), Range(rng, 2.5f, 4f), LandmarkSurfaceKind.Stain, MarkLift, 1.2f);
        }

        // Spilled aggregate round a stockpile: one soft patch with two smaller lobes laid over it, so the spill's outline
        // is lobed rather than one convex shape.
        static void Spill(WorldFeature f, Random rng, float u, float v, float du, float dv, float length, float width)
        {
            Blob(f, rng, u, v, du, dv, length, width, LandmarkSurfaceKind.Aggregate, PatchLift, 2f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Blob(f, rng, u + side * du * length * Range(rng, .2f, .38f) - dv * width * Range(rng, -.25f, .25f),
                    v + side * dv * length * Range(rng, .2f, .38f) + du * width * Range(rng, -.25f, .25f), dv * side + du, du - dv * side,
                    length * Range(rng, .35f, .55f), width * Range(rng, .45f, .7f), LandmarkSurfaceKind.Aggregate, MarkLift, 2f);
                if (i == 1) Tint(f, 2);
            }
        }

        // An oil or fuel stain: soft-edged and irregular, fresh and dark or faded.
        static void Stain(WorldFeature f, Random rng, float u, float v, float length, float width)
        {
            Blob(f, rng, u, v, 1f, Range(rng, -.3f, .3f), length, width, LandmarkSurfaceKind.Stain, MarkLift, Math.Min(1.2f, width * .45f));
            if (rng.NextDouble() < .35) Tint(f, rng.NextDouble() < .5 ? 1 : 2);
        }

        // Coarse growth along a fence: uneven runs with gaps, set just inside the posts and never wider than the verge;
        // about one run in three has grown into darker scrub.
        static void FenceWeeds(WorldFeature f, Random rng, float u0, float v0, float u1, float v1, float iu, float iv, float maxWidth)
        {
            Vector2 a = new Vector2(u0, v0), d = new Vector2(u1 - u0, v1 - v0), inward = new Vector2(iu, iv);
            float length = d.magnitude, at = Range(rng, 0f, 12f);
            d /= Math.Max(.01f, length);
            while (at < length - 6f)
            {
                float run = Math.Min(length - at, Range(rng, 14f, 46f)), width = Range(rng, Math.Min(1.5f, maxWidth), maxWidth);
                Vector2 c = a + d * (at + run * .5f) + inward * (.4f + width * .5f);
                bool scrub = rng.NextDouble() < .25;
                Blob(f, rng, c.x, c.y, d.x, d.y, run, scrub ? width + 1f : width, scrub ? LandmarkSurfaceKind.Scrub : LandmarkSurfaceKind.Overgrown, PatchLift, 2f);
                at += run + Range(rng, 2f, 12f);
            }
        }

        // An irregular, soft-edged decal on any local axis, flat at its own lift above the slabs.
        static void Blob(WorldFeature f, Random rng, float u, float v, float du, float dv, float length, float width, LandmarkSurfaceKind kind, float lift, float soft)
        {
            Vector2 axis = f.along * du + f.Area.Across * dv;
            axis = axis.sqrMagnitude > .0001f ? axis.normalized : f.along;
            var area = new FeatureArea { center = f.Area.Point(u, v), along = axis, halfLength = length * .5f, halfWidth = width * .5f };
            f.surfaces.Add(new LandmarkSurface { area = area, outline = Irregular(area, rng), kind = kind, lift = lift });
            Soften(f, soft);
        }

        // Feature-local coordinates of a world point, matching FeatureArea.Point.
        static void Local(WorldFeature f, Vector2 p, out float u, out float v)
        {
            Vector2 d = p - f.center;
            u = Vector2.Dot(d, f.along); v = Vector2.Dot(d, f.Area.Across);
        }
    }
}
