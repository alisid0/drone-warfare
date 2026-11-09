# World plan validation - 2026-11-05

Generator version 2, production commit `2b80cbe` (unchanged stand-in: `3aa9982`). Optimized standalone x64 .NET Framework harness; production WorldPlan sources, real SectorKind enum, Vector2 data stand-in. Unity was not opened.

| Seed | Generate (s) | Live managed (MiB) | Peak process (MiB) | Height (m) | Launch spacing (km) | Roads H/R/T | Bridges |
|---|---:|---:|---:|---|---|---|---:|
| 1 | 2.78 | 38.32 | 96.07 | 6.5-491.2 | 8.37-10.97 | 5/30/56 | 20 |
| 2 | 2.26 | 38.32 | 93.25 | 6.5-527.6 | 8.59-10.31 | 5/28/56 | 19 |
| 3 | 2.53 | 38.30 | 96.82 | 6.5-544.2 | 8.19-9.62 | 5/29/56 | 15 |

Every seed has 4 towns, 20 villages, 48 farms, 8 launch sites, 1 airbase, 1 enemy headquarters, 4 depots, 3 landmark ridges, 2 natural lakes, 1 reservoir/dam and 1 railway through two towns. Launch nearest-neighbour ranges are 8.37-9.77, 8.59-9.41 and 8.19-9.48 km. All sites have road access; launch depth stays within 4-10 km west of the front.

## Cover (% of the complete grid)

| Cover | Seed 1 | Seed 2 | Seed 3 |
|---|---:|---:|---:|
| Pasture | 22.900 | 21.298 | 23.890 |
| Farmland | 41.967 | 39.369 | 43.245 |
| Forest | 33.473 | 37.535 | 30.666 |
| Wetland | 0.874 | 0.916 | 0.752 |
| Water | 0.288 | 0.269 | 0.277 |
| Town | 0.265 | 0.270 | 0.319 |
| Village | 0.193 | 0.245 | 0.230 |
| Rock | 0.040 | 0.097 | 0.621 |

Farmland occupies 68.11%, 68.14% and 68.02% of eligible open farming land, respectively (1955.19, 1836.76 and 2018.73 square km before small settlement/site stamps). Narrow streams are exported as vectors as well as the 32 m raster.

## River lengths (km)

| River | Seed 1 | Seed 2 | Seed 3 |
|---|---:|---:|---:|
| Lissen | 62.74 | 62.67 | 64.98 |
| Venn | 38.73 | 34.76 | 34.64 |
| Callow | 12.01 | 15.27 | 8.88 |
| Oster | 38.78 | 27.46 | 34.00 |
| Sorrel Beck | 12.48 | 18.17 | 14.19 |
| Alder | 33.73 | 36.36 | 30.67 |

## Validation and artifacts

All 16 seeds tested (0 through 12, -1, int.MinValue and int.MaxValue) pass. Each seed passes more than 3.09 million invariant checks, a complete data fingerprint after regeneration, and parallel query comparisons. Seeds 1-3 have zero unjoined river crossings, self-intersecting shorelines, or transport vertices inside lakes. Their worst sampled riverbeds remain 2.70, 2.09 and 2.59 m below the water surface. Final rail ground grades outside bridge spans are at most 2.59%, 1.64% and 1.85%.

A million height queries took 22-41 ms; a million cover queries took 28-31 ms in the captured runs. Timings vary with other work on this PC. Three concurrent stress processes reached 4.33 s for seed 12; the follow-up solo run took 1.29 s with 96.61 MiB peak working set. The generation numbers above exclude compilation, checks, images and GC measurement. Live memory is retained managed data; peak process working set includes runtime and generation scratch space, sampled before image rendering.

Saved in `C:\Users\ali10\AppData\Local\Temp\dwprev\codex\world\`: `seed-{1,2,3}-atlas.png`, `seed-{1,2,3}-hillshade.png`, `seed-1-detail-8km.png`, `seed-1-reservoir-8km.png`, per-seed metric/vector JSON and check logs. All eight PNGs are 2048 x 2048. The crops show the 32 m macro plan, not buildings or local field parcels.

Not verified here: Unity import/compilation, Mono/IL2CPP timing, far-terrain rendering and seams, detailed bridge/road approaches, streaming and the long-range flight acceptance test. Claude owns integration. Full data contracts and rerun instructions are in `Tools/WorldPlan/README.md`.
