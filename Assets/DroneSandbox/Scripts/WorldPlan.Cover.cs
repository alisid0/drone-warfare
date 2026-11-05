using System;
using System.Threading.Tasks;
using UnityEngine;

namespace DroneSandbox
{
    // Cover is a regional land-use plan, leaving individual field parcels to the detailed cell generator.
    public static partial class WorldPlan
    {
        // Large woods occupy poor slopes; fertile open land is ranked into connected farming districts.
        static void PlanCover()
        {
            var settlementDistance = new float[RouteCount * RouteCount];
            for (int k = 0; k < settlementDistance.Length; k++)
            {
                Vector2 p = RoutePoint(k); float best = 20000f;
                foreach (var s in Settlements) best = Math.Min(best, Distance(p, s.center) - s.radius);
                settlementDistance[k] = best;
            }
            var scores = new ushort[cover.Length];
            Parallel.For(0, GridCount, j =>
            {
                float z = -Half + j * HeightStep;
                for (int i = 0; i < GridCount; i++)
                {
                    int k = j * GridCount + i;
                    if (cover[k] == Cover.Water) continue;
                    float x = -Half + i * HeightStep, h = heights[k], grade = Grade(x, z);
                    float warpX = 1800f * (Noise(x / 4700f, z / 4700f, 91) - 0.5f);
                    float warpZ = 1800f * (Noise(x / 4300f, z / 4300f, 92) - 0.5f);
                    float u = x + warpX, v = z + warpZ;
                    float block = Noise(u / 3600f, v / 3600f, 93) * 0.7f + Noise(u / 900f, v / 900f, 94) * 0.3f;
                    float settled = SampleRouteGrid(settlementDistance, x, z);
                    float dx = (u - deepForest.x) / 6500f, dz = (v - deepForest.y) / 6000f;
                    float deep = 1f - Smooth((dx * dx + dz * dz - 0.65f) / 0.8f);
                    float woods = block + Smooth((h - 90f) / 150f) * 0.24f + Smooth(grade / 0.14f) * 0.13f + deep * 0.65f
                        - (1f - Smooth(settled / 2000f)) * 0.42f;
                    if ((h > 320f && grade > 0.16f) || h > 490f) cover[k] = Cover.Rock;
                    else if (riverDistance[k] < 120f + 280f * block && grade < 0.025f && Noise(u / 520f, v / 520f, 98) > 0.46f) cover[k] = Cover.Wetland;
                    else if (woods > 0.65f) cover[k] = Cover.Forest;
                    else
                    {
                        cover[k] = Cover.Pasture;
                        if (h < 180f && grade < 0.09f)
                        {
                            float fertility = 0.72f * Noise(u / 2400f, v / 2400f, 95) + 0.16f * Noise(u / 750f, v / 750f, 96)
                                + 0.24f * (1f - Smooth(settled / 4500f));
                            scores[k] = (ushort)(1 + (int)(Clamp(fertility, 0f, 1f) * 1022f));
                        }
                    }
                }
            });
            // A histogram makes the 68% farming target stable across seeds, rather than hoping a noise cut fits.
            var histogram = new int[1024]; int eligible = 0;
            for (int k = 0; k < scores.Length; k++) if (scores[k] > 0) { histogram[scores[k]]++; eligible++; }
            int threshold = 1023, selected = 0;
            while (threshold > 1 && selected < eligible * 0.68f) selected += histogram[threshold--];
            for (int k = 0; k < cover.Length; k++) if (scores[k] > threshold) cover[k] = Cover.Farmland;
            FarmableSquareKm = eligible * HeightStep * HeightStep / 1000000f;
            FarmedShare = eligible > 0 ? selected / (float)eligible : 0f;
            foreach (var s in Settlements) StampSettlement(s);
            foreach (var farm in Farms) ClearGround(farm.position, 180f, Cover.Farmland);
            foreach (var site in Sites) if (site.kind != WorldSiteKind.Landmark) ClearGround(site.position, site.kind == WorldSiteKind.Airbase ? 650f : 140f, Cover.Pasture);
        }

        // Bilinear district influence is sufficient at 250 m and avoids millions of settlement searches.
        static float SampleRouteGrid(float[] grid, float x, float z)
        {
            float gx = Clamp((x + Half) / RouteStep, 0f, RouteCount - 1), gz = Clamp((z + Half) / RouteStep, 0f, RouteCount - 1);
            int i = Math.Min((int)gx, RouteCount - 2), j = Math.Min((int)gz, RouteCount - 2), k = j * RouteCount + i;
            return Lerp(Lerp(grid[k], grid[k + 1], gx - i), Lerp(grid[k + RouteCount], grid[k + RouteCount + 1], gx - i), gz - j);
        }

        // Occupied footprints have irregular edges and avoid flood channels; radius is a planning extent.
        static void StampSettlement(WorldSettlement s)
        {
            float reach = s.radius * 1.35f;
            for (int j = GridIndex(s.center.y - reach); j <= GridIndex(s.center.y + reach); j++)
                for (int i = GridIndex(s.center.x - reach); i <= GridIndex(s.center.x + reach); i++)
                {
                    int k = j * GridCount + i;
                    if (cover[k] == Cover.Water || cover[k] == Cover.Wetland) continue;
                    float x = -Half + i * HeightStep, z = -Half + j * HeightStep;
                    float dx = (x - s.center.x) * 0.85f, dz = (z - s.center.y) * 1.1f;
                    float radius = s.radius * (0.72f + 0.48f * Noise(x / 340f, z / 340f, 97));
                    if (dx * dx + dz * dz < radius * radius && Grade(x, z) < 0.08f) cover[k] = s.town ? Cover.Town : Cover.Village;
                }
        }

        // Small dry clearings give installations and farms usable land without levelling regional terrain.
        static void ClearGround(Vector2 p, float radius, Cover ground)
        {
            for (int j = GridIndex(p.y - radius); j <= GridIndex(p.y + radius); j++)
                for (int i = GridIndex(p.x - radius); i <= GridIndex(p.x + radius); i++)
                {
                    float dx = -Half + i * HeightStep - p.x, dz = -Half + j * HeightStep - p.y;
                    int k = j * GridCount + i;
                    if (dx * dx + dz * dz < radius * radius && cover[k] != Cover.Water && cover[k] != Cover.Wetland && cover[k] != Cover.Town && cover[k] != Cover.Village) cover[k] = ground;
                }
        }

        // Classify cells once from their actual cover and intersecting features, not just their centre point.
        static void PlanCells()
        {
            cells = new WorldCell[CellCount * CellCount];
            var highway = new bool[cells.Length]; var rail = new bool[cells.Length]; var bridge = new bool[cells.Length];
            var roadCount = new int[cells.Length]; var lake = new byte[cells.Length]; var airfield = new bool[cells.Length];
            foreach (var road in Roads)
            {
                var visited = new bool[cells.Length];
                foreach (var p in road.points)
                {
                    int k = CellIndex(p);
                    if (!visited[k]) { roadCount[k]++; visited[k] = true; }
                    if (road.kind == RoadClass.Highway) highway[k] = true;
                }
            }
            foreach (var line in Railways) foreach (var p in line.points) rail[CellIndex(p)] = true;
            foreach (var b in Bridges) bridge[CellIndex(b.position)] = true;
            foreach (var l in Lakes)
            {
                foreach (var p in l.shore) lake[CellIndex(p)] = (byte)(l.reservoir ? 2 : 1);
                lake[CellIndex(l.outlet)] = (byte)(l.reservoir ? 2 : 1);
            }
            foreach (var s in Sites) if (s.kind == WorldSiteKind.Airbase) airfield[CellIndex(s.position)] = true;
            Parallel.For(0, CellCount, cz =>
            {
                for (int cx = 0; cx < CellCount; cx++)
                {
                    int index = cz * CellCount + cx, forest = 0, fields = 0, water = 0, town = 0, village = 0, wet = 0, total = 0;
                    float lo = float.MaxValue, hi = float.MinValue, river = 2000f;
                    for (float dz = 16f; dz < CellSize; dz += HeightStep)
                        for (float dx = 16f; dx < CellSize; dx += HeightStep)
                        {
                            float x = -Half + cx * CellSize + dx, z = -Half + cz * CellSize + dz;
                            int k = GridIndex(z) * GridCount + GridIndex(x);
                            Cover c = cover[k];
                            if (c == Cover.Forest) forest++; else if (c == Cover.Farmland) fields++; else if (c == Cover.Water) water++;
                            else if (c == Cover.Town) town++; else if (c == Cover.Village) village++; else if (c == Cover.Wetland) wet++;
                            lo = Math.Min(lo, heights[k]); hi = Math.Max(hi, heights[k]); river = Math.Min(river, riverDistance[k]); total++;
                        }
                    var cell = new WorldCell { x = cx, z = cz, forest = forest / (float)total, fields = fields / (float)total, water = water / (float)total };
                    unchecked { cell.seed = ((Seed * 73856093) ^ (cx * 19349663) ^ (cz * 83492791) ^ (GeneratorVersion * 1000003)) & 0x7fffffff; }
                    float x0 = -Half + (cx + 0.5f) * CellSize, z0 = -Half + (cz + 0.5f) * CellSize;
                    cell.kind = airfield[index] ? SectorKind.Airfield
                        : lake[index] == 2 ? SectorKind.Reservoir
                        : lake[index] == 1 || cell.water > 0.12f ? SectorKind.LakeDistrict
                        : town > total * 0.35f ? SectorKind.MarketTown
                        : town > 0 ? SectorKind.TownOutskirts
                        : bridge[index] ? SectorKind.RiverCrossing
                        : village > total * 0.05f ? (roadCount[index] >= 3 ? SectorKind.CrossroadsHamlet : SectorKind.VillageCluster)
                        : wet > total * 0.2f ? SectorKind.Marshland
                        : rail[index] ? SectorKind.RailLine
                        : Math.Abs(x0 - FrontAt(z0).x) < 400f ? SectorKind.BorderLine
                        : hi - lo > 55f && roadCount[index] > 0 ? SectorKind.RidgePass
                        : cell.forest > 0.78f ? SectorKind.DeepForest
                        : hi - lo > 60f ? SectorKind.HillCountry
                        : highway[index] ? SectorKind.HighwayCorridor
                        : river < 150f ? SectorKind.RiverValley
                        : cell.forest > 0.35f ? (lo < 70f ? SectorKind.BirchWoods : SectorKind.ForestEdge)
                        : hi > 240f ? SectorKind.Moorland : SectorKind.RollingFarmland;
                    cells[index] = cell;
                }
            });
        }

        // Feature vectors on the closed world edge belong to the last cell, never a fictitious cell 56.
        static int CellIndex(Vector2 p)
        {
            int x = Math.Max(0, Math.Min(CellCount - 1, (int)((p.x + Half) / CellSize)));
            int z = Math.Max(0, Math.Min(CellCount - 1, (int)((p.y + Half) / CellSize)));
            return z * CellCount + x;
        }
    }
}
