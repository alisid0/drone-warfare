# Field topology hand-off

Branch `codex/field-topology`, based on `e45f400`. Unity was not launched; no Library was imported. Ground.cs, Farmland.cs and Codex A's files are unchanged. The regional land-use-mask task has not been started.

## Changes

`FarFieldPlan` retains the 700 m jittered district sites, but clips finite, workable trapezoids inside them. District boundaries have a 10 m meadow corridor; corners are bevelled inward by 8 m. Thin tips are shortened or left as meadow. Accepted parcels have at least 600 m², 18 m edges and 24 m end widths, with side slopes bounded to give at least 45° interior angles. This also exceeds the requested 12 m minimum width. The broad angle field correlates neighbouring districts; a through-road within 500 m of the district site supplies the nearest parallel/perpendicular axis. The slightly wider search reaches farms whose land, rather than their centre, is within 300 m of a road. Angles use the same 16-bit grid as the tile controls.

Districts are constructed only when touched, published immutably with compare-exchange, and then sampled by district/strip/slot. There is no global polygon pass in WorldPlan.Generate and no native engine call in a worker. The tile, world picture and harness all call this implementation. A 2 m tile pixel uses analytic edge coverage once; the coarse world picture retains four stratified samples. No new texture allocation or shader sampler was added.

World-cut sectors clip these same polygons into FieldPlot trapezoids, preserving the crop and axis. The existing exclusion map still cuts roads, settlement streets, water, sites, orchards, slopes, `OnWorks` and reserved features. Remnants created by those cuts keep their parent's crop. World fields reach the terrain boundary; only legacy sectors retain the old setback and local field warp. The splat-only collar now preserves farmland pixels so it cannot paint over the continuing crops. Farm cover uses the original cover grid on both sides; the existing coarse land-use outline remains visible.

Village ground uses the nearest real world road's cumulative distance and side. Staggered frontages, individual garden depths, yards and side/rear hedges follow the street, replacing the x/z lattice. This follows WorldPlan's roads; it does not reproduce sector-only generated side lanes or add streets to WorldPlan.

## Required Farmland integration

**Claude must apply [farmland-world-rows.patch](farmland-world-rows.patch).** The file is deliberately not applied here because Farmland.cs is read-only for this task. `git apply --check` passes against this checkout.

The existing Farmland mesh UV calculation uses a fragment's tapered parameter coordinates. That can fan its fine crop rows, and its per-fragment detail map adds a headland at an artificial sector cut. `FarFieldSurface` now retains each fragment's original parcel. The patch changes only `BuildField`: use physical world-metre UVs for registered world parcels, generate the detail map from their original parcel, and offset that map into the same world coordinate frame. Legacy UVs and detail maps retain their existing behavior. The production helper and the proposed patched Farmland source compile against the Unity assemblies with zero warnings/errors; its coordinate mapping is tested. The patch still needs Claude's game build and visual check. Random per-renderer tint and the tile's procedural tramlines still differ from Farmland's full growth/detail shader.

**No Ground.cs edit is needed for parcel geometry.** Ground already reads `MapLayout.InField`; its existing splat painter is not replaced by this work. The sector-edge figures below demonstrate polygon ownership, not full terrain/material continuity.

## Review images

[Comparison sheet](Review/comparison-sheet.png) · [Sector overview](Review/sector-edge.png) · [Sector-edge close-up](Review/sector-edge-close.png)

| Fixed world area | 300 m | 2.5 km |
|---|---|---|
| Wheat reference | [Before/after](Review/wheat-300.png) | [Before/after](Review/wheat-2500.png) |
| Ploughed reference | [Before/after](Review/ploughed-300.png) | [Before/after](Review/ploughed-2500.png) |
| Road-aligned farm | [Before/after](Review/road-farm-300.png) | [Before/after](Review/road-farm-2500.png) |
| Village | [Before/after](Review/village-300.png) | [Before/after](Review/village-2500.png) |

These are CPU albedo/geometry views of identical locations at seed 1200, with a 45° vertical FOV and 16:9 aspect. The 300 m views use the production 2 m coverage painter; 2.5 km views bilinearly sample the actual 4096² world picture in linear light. Crop microtextures, normals, props, HDRP lighting and exposure are not simulated. The sector panels use actual clipped FieldPlots on a neutral meadow inside the cut, and the world painter/picture outside. The dashed line in the close-up marks the sector cut. Managed reference Perlin replaces native Unity Perlin on both sides. These are not Unity screenshots.

## Measurements

The [500-district audit](Review/after/topology.json) measures actual resulting world parcel polygons. It found **0 tips, 0 slivers and 0 convergences**; minimum angle about **45°**, shortest edge **18.01 m**, minimum width **18.01 m**, minimum area about **621 m²**. All 18,080 parcels passed the limits before cover/feature masks.

| Metric | Baseline strip skeleton | Shared parcel geometry |
|---|---:|---:|
| Parcels | 22,523 | 18,080 |
| Tips below 35° | 2,740 | 0 |
| Slivers (any area/width/edge failure) | 4,491 | 0 |
| Edges below 18 m | 4,132 | 0 |
| Areas below 600 m² | 1,778 | 0 |
| Widths below 12 m | 1,073 | 0 |
| Three districts' crops within 12 m of their common vertex | 909 | 0 |

The [baseline audit](Review/before/topology.json) reconstructs the old affine strip skeleton and its crop insets. Its 30 m strip warp and nonlinear distance-difference district margin are omitted. These baseline numbers are **an analytic approximation**, not an exact census of e45f400's painted pixels. Both geometry audits exclude subsequent road/water/cover masks; artificial sector clipping fragments are parts of their parent field, not newly cultivated parcels. Thus this audit does not certify minimum-size visible remnants produced by every external mask.

[Sector agreement](Review/after/agreement.json) checks 4,392 points in the actual clipped FieldPlots, including 942 within 25 m of the terrain edge, plus 12,152 crop points on a 0.5 m survey of all four cuts. Crop, row-axis, world-polygon and cover ownership agree; no sampled crop lies outside the sector, in works or in vector water. The parent material mapping is also checked in physical metres. [Legacy hashes](Review/after/legacy.json) match all field and boundary geometry/crops/kinds for Varrow and a generated non-world Operations layout with [e45f400](Review/before/legacy.json).

[Before timings](Review/before/metrics.json) and [after timings](Review/after/metrics.json) include five tile workers and the real field planner. The latest after medians are **94–177 ms**, worst **205 ms** on the river case; field planning is **505 ms** (273 ms exclusions + 232 ms parcels). Host load varied substantially: an earlier run measured 62–80 ms medians / 86.5 ms worst and 114 ms fields. The river worst case therefore needs Claude's confirmation against the approximate 200 ms limit. An isolated three-run cold/warm picture comparison is recorded separately: [before](Review/before/world-timings.json), [after](Review/after/world-timings.json). Cold medians were **2.090 s before / 2.027 s after**, within the 0.5 s increase budget; the full harness runs include larger host-load outliers. Cold includes first-touch district construction; CaptureDetail snapshot copying is outside this timer. No Unity main-thread, upload, GPU or frame-time measurement is claimed.

## Reproduce

Run sequentially, with no Unity instance launched by these scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/FieldTopology/run.ps1 -Phase before
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/FieldTopology/run.ps1 -Phase after
& 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe' -I Tools/Tests/FieldTopology/render.py
```

`-CompileOnly` builds the game helpers/consumers against installed Unity 6000.6.7f1 assemblies, C# 9, warnings as errors. Unrelated game services use wrappers; this is not a full game build. The executable harness substitutes managed engine storage/math. Both builds get their production code from the actual sources; the baseline uses `git show e45f400`, without switching branches. Raw render inputs and compilation scaffolding are ignored.

For isolated picture timing, set `DOTNET_TieredCompilation=0` and run each built `Temp/field-topology/<phase>/bin/Release/net8.0/harness.dll` with arguments `Tools/Tests/FieldTopology/Review/<phase> benchmark`, using the installed Unity .NET SDK. `handoff_patch.py` regenerates the unapplied Farmland patch from the read-only source.

## Claude's build checklist

1. Review the commits and apply `farmland-world-rows.patch` in the integration checkout. Build normally with the existing GroundDetail material/array setup. Confirm the new `.meta` files import and there are no C# or shader errors.
2. Capture the three farm areas and village at 300 m and 2.5 km in Unity. Check bevels, junction meadows, road alignment and street plots. The fixed coordinates are in the metrics JSON.
3. Fly across all four sides of the world-sector boundary at 50–300 m. Check crop outlines, fine row direction/phase, parent headlands, tint, lifts and the collar. Then travel and return, checking the parent mapping and restored world picture.
4. Check sectors with rivers, villages and dominant reserved landmarks after Codex A's integration. Verify no field covers `OnWorks`, water, sites or settlement plots. Inspect external-mask remnants separately from the base partition audit.
5. Confirm the real worker tile budget (~200 ms), picture-paint increase (≤0.5 s), field planning (≤0.8 s), and the normal flight/autotest budgets. Offline timings do not replace player profiling.
6. Check Varrow and a non-world Operations sector against their existing layouts. Regional land-use masks and the 13.7 m picture resolution remain the next task.
