# Regional land-use handoff

Branch `codex/regional-masks`, based on integrated `f5a5877`. Unity was not launched and no Library was imported. The integrated `_FarGround...` names, separate normal sampler, shader error messages and sixth terrain splat layer are preserved. Ground.cs, Farmland.cs and Codex A's files are unchanged.

## Changes

- Existing far-terrain chunks now use the HDRP ground shader's regional variant. A fixed 9 x 9 array follows the main camera: 2,048 m pages, 4 m texels, 64 m gutters, three mips. Workers paint with `FarDetailData` and encode BC1; the main thread writes one staging texture in separate stages and copies completed mips into the array. There is no new mesh or draw pass.
- The tile macro colour blends into the same regional/background sampler over 900-1,300 m and over the last 32 m of each tile. Regional colour blends into the world picture over 7-8 km. Distances in the shader are camera-to-surface distances. Residency covers an 8 km horizontal circle. Pages fade in over 0.75 s; neighbouring page edges share availability weights while loading. Missing pages use the world picture. The main camera owns regional residency; a distant drone feed uses any resident pages and otherwise the picture beneath its existing tile cache.
- Cover memberships are bilinearly interpolated, with up to 6 m of displacement over 37/53 m wavelengths. Forest litter uses the exact `ForestPattern.Edge` formula, including its original 7/21 m ripple, against an immutable snapshot. It uses `Ground.LitterMean`; the grass/crop array indices are separate from terrain splat indices. Village coverage is interpolated too. River vectors retain their real widths.
- Farm districts vary strip width, length, hedge density and idle share by broad region, slope and proximity to settlement cover. Flat open districts occasionally have 8+ ha fields. Meadow/fallow targets vary from 10-20%; some village-adjacent parcels have orchard or market-garden ground patterns. These are ground patterns, not new tree/prop placement.
- Boundary parcels are conservatively subdivided against the smooth cover mask during planning. Accepted fragments retain the 600 m² / 18 m / 45° rules; rejected remnants stay meadow. The sector uses those same fragments and hedge choices. The collar preserves interpolated farm coverage rather than reverting to the raw cover grid.

Coarse painting now repeats ownership sampling only around field edges. A 3 MB immutable CPU mask skips noise in provably uniform cover; 100,000 comparisons verify equivalent memberships. Striped publication locks prevent duplicate first-touch district clipping on competing workers. Removing the old main-thread cover-noise grid also removes 66,564 native Perlin pairs per tile. Native height roughness is unchanged. Regional and tile uploads share `FarDetail`'s timer: a regional API stage is deferred on tile sampling/upload or collider frames. This scheduling bounds work; it cannot guarantee an individual driver call's duration.

## Review renders

| Same seed and world coordinates | 2.5 km | 6 km |
|---|---|---|
| Road farm | [Before / after](Review/road-farm-2500.png) | [Before / after](Review/road-farm-6000.png) |
| Forest edge | [Before / after](Review/forest-edge-2500.png) | [Before / after](Review/forest-edge-6000.png) |
| Farmland | [Before / after](Review/farmland-forest-2500.png) | [Before / after](Review/farmland-forest-6000.png) |
| Village | [Before / after](Review/village-2500.png) | [Before / after](Review/village-6000.png) |

[Tile-to-regional crossing animation](Review/crossing.webp) · [Crossing still](Review/crossing.png)

These are **CPU albedo renders, not Unity screenshots**: 45° vertical FOV, 1280 x 720, fixed seed 1200. Before samples a 4096² picture painted by f5a5877's code; after decodes the actual BC1 pages and filters their mips. The crossing samples actual 258² tile maps and regional pages while moving the eye 1.6 km across a fixed 3.6 km strip. It tests colour transitions, not moving geometry, normals, props, exposure, fog or HDRP lighting. Baseline native Perlin is represented by the same managed reference used in earlier handoffs. All 162 coarse garden-row checks preserve the analytic area mean. Coordinates are in [metrics](Review/after/metrics.json) and [forest location](Review/after/forest-location.json).

## Checks and budgets

| Check | Result | Limit / status |
|---|---|---|
| Regional GPU texture payload | 15,925,056 bytes, **15.19 MiB**, including staging | ≤64 MB; calculated from all resident compressed mips |
| Tile worker, four cases x five runs | medians **45-68 ms**, worst **70 ms** | Near 200 ms; offline CPU measurement |
| Regional page worker | median **179 ms**, worst **278 ms** | Includes painting, linear mips and BC1 encoding |
| World picture, three cold paints including snapshot | median **2.046 s before / 1.163 s after** | Growth ≤0.5 s; passes offline |
| World picture, three warm paints | median **2.040 s before / 0.991 s after** | Same executable settings |
| Combined main-thread commit | Timer includes regional work; stages mutually exclude tile uploads | **Unverified in Unity:** mean ≤0.5 ms, worst <2 ms |
| Extra GPU time | One regional sample on interior pixels, same chunk draws | **Unverified in Unity:** ≤0.25 ms |

[Before timings](Review/before/world-timings.json), [after timings](Review/after/world-timings.json), [tile timings](Review/after/tile-controls.json), [page timings and seams](Review/after/seams.json). Host load varies; these are not player frame-time measurements. Earlier runs ranged up to 189 ms for tiles and 361 ms for regional pages under higher host load. Cold picture timings include snapshot construction; warm timings reuse it. A cold regional cache fills progressively, nearest pages first; do not capture comparisons before it is warm.

The [500-district sample audit](Review/after/audit.json) measures **post-cover** geometry (deterministic random district draws with replacement): 4,627 parcels, minimum area 602.08 m², edge 18.11 m, angle 45° within floating-point tolerance. All pass. Of those parcels, 739 (15.97%) are meadow/fallow, 44 are 8-25 ha, and 22 have orchard/garden ground patterns. District strip widths span 42.5-323 m; hedge probabilities span 0.14-0.81. Across 5,630,955 probes, no accepted parcel escapes the smooth farm mask or enters the forest fringe. Across 100,000 samples, the snapshot forest function agrees exactly with `ForestPattern.Edge`.

[Seams](Review/after/seams.json): 137,088 compressed border comparisons match byte-for-byte across all three mips; repeated pages are deterministic. 2,880 circle probes cover the full 8 km radius at positive/negative cell phases. [Sector agreement](Review/after/sector.json): all 3,042 samples retain crop and polygon ownership. This does not certify every fragment from all possible roads, reserved features or artificial sector cuts.

[Streaming](Review/after/streaming.json): the production state machine fills 81 pages, loads 17 after a diagonal cell move, defers 524 simulated blocked frames, and survives an interrupted upload and reset without publishing stale work. Engine calls use managed wrappers in this test, so its timings are deliberately not reported.

The complete modified C# helpers, consumers and editor setup compile against installed **Unity 6000.6.7f1 assemblies**, C# 9, warnings as errors. Unrelated services use wrappers; this is not a full game build. The [shader check](Review/shader-check.json) compiles tile/chunk colour and normal-only hooks without diagnostics and checks new names against macros in 523 installed HDRP/core HLSL files, including the known sky macro. It does **not** substitute for real HDRP pass compilation. Texture APIs were checked against Unity's [reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.2/Runtime/Export/Graphics/Texture.cs) and the installed assemblies.

## Read-only integration notes

No Ground.cs change is required for the regional ring. It still paints the detailed sector's forest floor from actual tree occupancy and moss, while regional litter follows `ForestPattern.Edge`. Compare their collar in the player; matching their interior forest-floor recipes would require a separate Ground change.

The applied Farmland world-row patch remains in use. Farmland still renders its existing crop materials: fallow is represented by ploughed crop and orchard/garden parcels by young crop inside the playable sector. Their more specific macro ground patterns currently exist in the tile/regional/world painters. For exact material continuity of these three special uses, Farmland would need a world-parcel use/material hook, with the original world row coordinates retained; real orchard props remain Features/Codex A's scope. Do not change legacy Operations layouts to obtain those patterns.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Phase before
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Phase before -Mode forest
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Mode forest
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Mode stream
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Mode tiles
# Run the two benchmarks sequentially, without image encoding or other harnesses in parallel.
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Phase before -Mode benchmark
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/RegionalMasks/run.ps1 -Mode benchmark
& 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe' -I Tools/Tests/RegionalMasks/render.py
& 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe' -I Tools/Tests/RegionalMasks/shader_check.py
```

`-CompileOnly` builds without running data/render checks. `-Mode audit` runs geometry, ownership, canopy and seam checks. `-Mode crossing` regenerates only the crossing. Generated projects and raw PPMs are ignored. Preparation never rewrites production shaders and never opens Unity.

## Claude's build checklist

1. Review and integrate the commits; run the existing project setup so the saved ground material and arrays are current. Compile all HDRP raster passes/variants in a real player build, including normal-only, depth, deferred and forward paths. Check every shader diagnostic, SRP batching, and BC1 texture-copy support.
2. Warm the ring, then repeat the fixed 2.5 km and 6 km views and fly through 900-1,300 m, a 2,048 m page edge and 7-8 km. Check colour, normals, mip filtering, fog and exposure. Test page arrival with missing neighbours, then a distant drone feed.
3. Measure `FarDetail.TotalCommitMs / FarDetail.CommitFrames` and `WorstCommitMs` during cold fill, ordinary flight, 4x flight, turns, teleport and sector travel. Require mean ≤0.5 ms and worst <2 ms. Inspect `RegionalCommitted`, `RegionalWorkerMs`, `BudgetOverruns` and native texture upload/copy markers.
4. Compare GPU timing with `_FarRegionEnabled` set to 0 and 1 after warming, at the same camera and resolution. Require extra GPU ≤0.25 ms and verify texture residency against the 64 MB cap. The normal-only variant should not retain albedo samplers.
5. Cross all four sector edges over farm/forest/river land and inspect the collar. Review special-use material continuity from the note above, WorldPlan landmarks/reservations after Codex A's integration, legacy Operations, and the normal autotest. Record actual Unity captures and profiler budgets before considering the runtime gates closed.
