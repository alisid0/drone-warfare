# Seed 1200 validation

World-backed crops, tested without opening Unity. [Integration notes and exact Ground.cs changes](README.md) accompany the code; the protected consumers remain unchanged.

| Spot | World centre (x, z), m | PlanAt, ms | Road crossings | River crossings | Max road / river edge error, m |
|---|---|---:|---:|---:|---:|
| [River crossing](renders/river-crossing.png) | (2750, -4000) | 127.40 | 2 | 2 | 0.0000 / 0.0000 |
| [Town edge](renders/town-edge.png) | (-13000, 17250) | 37.74 | 3 | 0 | 0.0000 / 0.0000 |
| [Forest](renders/forest.png) | (3500, -6500) | 27.73 | 0 | 0 | 0.0000 / 0.0000 |
| [Reservoir](renders/reservoir.png) | (10250, 15000) | 76.37 | 4 | 2 | 0.0000 / 0.0000 |
| [Farmland](renders/farmland.png) | (3500, -9500) | 16.19 | 0 | 0 | 0.0000 / 0.0000 |
| [Confluence](renders/confluence.png) | (-12000, 20000) | 15.89 | 0 | 3 | 0.0000 / 0.0000 |

Road seam errors remain **0.0000 m after the unchanged MapLayout.Prepare**, including Smooth/Wander and village planning. Measurements independently intersect the original world segments and returned local segments with all four square edges, check counts, then compare both directions. The assertion tolerance is 0.05 m; these six captured results report exactly zero in float arithmetic. Cases with no crossing are explicitly counted as zero, not evidence of a tested crossing.

The river numbers include **all reaches in plan.world.rivers**. The legacy single plan.river retains only two of the confluence's three edge events. Its missing branch requires the documented RiverField/Water hooks; the diagram does not pretend the legacy renderer draws it.

The production-source harness passes **54,130 assertions**: repeated plan equality, aligned lists/river attributes, dry base/HQ/defence/site positions, connected base-HQ routes, height/cover agreement, edge detail suppression, 170 fitting situations on the six spots, all 29 procedural kind assignments/plans, snap/non-overlap/order/advance checks, and a snapshot that survives global world regeneration. The 29 additional kind crops took 4.6–60.3 ms. These are standalone .NET 8 timings; world generation (~1.6 s), MapLayout.Prepare, mission generation and rendering are excluded from PlanAt timing. The first timed crop includes its JIT cost. Unity Mono/IL2CPP timings remain unverified.

The separate, generated-copy water-hook probe passes **9,646 assertions**. Without the hooks there are six stationary placements over the reservoir's exact water footprint; with exact LakeDistance and all-reach RiverField inputs, all 170 situations have zero stationary units over exact river/lake water. Both runs have zero two-point convoy route fallbacks. This probe changes only Temp/world-sectors/MapLayout.probe.cs; **the game still needs Claude to apply those hooks**.

Production planning sources also compile against the installed Unity 6 CoreModule with C# 9, warnings as errors: zero warnings/errors. The executable harness uses managed substitutes for Unity math and excludes scene construction, terrain/water meshes, trees and native PerlinNoise. Unity import, full game compilation, autotest, playability and in-game seam appearance are Claude's verification work.

[Comparison sheet](renders/comparison-sheet.png) · [Measurements](validation.json) · [Water-hook probe](water-hook-probe.json). Full snapshots and final build/check log are retained in ignored Temp/world-sectors. No packages, Unity Library, branch pushes or main-checkout edits were made.
