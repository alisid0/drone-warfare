# Lived-in villages and fields

Branch: `codex/lived-in`, based on `cb49269`. No Unity import or run, push, merge or main-checkout edits. Existing unrelated staged `FieldsVillages` work and the existing Blender-script change were preserved.

## What changed

- `VillagePlan`, `VillageGrowth` and `VillageUse` now share an immutable age/use plan with the sector and distant painters. Villages follow one old route, then acquire two to four unequal side lanes. Old frontage is tighter and closer to the street; new frontage has larger setbacks. Lanes bend 2–6 m over 40–90 m. Farms occupy outer plots. Town street topology is retained.
- Independently shuffled property choices target 28% old buildings, 16% trapezoid/L-shaped plots, 43% cultivated gardens, 28% sheds, 15% log piles, 12% untended gardens and 14% roof changes. There are three or four wall palettes per settlement and uniform building scales of 0.89–1.11. Small populations require integer rounding. Trapezoids and L-shaped rear corners retain their original street frontage; no house is placed in a stream.
- Frontage, paths, working yards, beds and partial enclosures share property coordinates. Door paths are 1–1.8 m wide, drives 2.5–3.5 m. Barn/shed wear and farm circulation use existing deferred `Ground.Wear`/`Ground.Paint` calls and the existing flush. Whole lawn rectangles are no longer painted over the landscape.
- Yard/fence geometry is combined in 96 m bins. Fine geometry and small logs/saplings drop at 300 m; major hedge/wall boundaries remain. The existing house, barn, hut, machinery shed, log-pile and tree models supply the props; no additional models or packages were needed.
- Sector-generated mission access roads and tracks take short bounded detours around planned properties. Authored world roads keep their ownership and bridge routes. Road-width lookup now follows source ownership when mission access is inserted at index zero. This prevents the access road from deleting village frontage.
- `FieldAppearance` adds shared 20 m growth cells, sparse rough/damp areas, 1–2.5 m uncultivated margins and corner gate locations. Tiles, the regional ring and world picture use these same physical coordinates with pixel filtering. The parcel graph is unchanged. `FieldMargins` builds the sector margins and rough ground in 128 m bins when Claude applies the Farmland patch.

## Patches for Claude

The protected source files have not been edited. Apply both after merging this branch:

```powershell
git apply --check --ignore-space-change Tools/Tests/LivedIn/Claude-Farmland.patch
git apply --ignore-space-change Tools/Tests/LivedIn/Claude-Farmland.patch
git apply --check --ignore-space-change Tools/Tests/LivedIn/Claude-house-scale.patch
git apply --ignore-space-change Tools/Tests/LivedIn/Claude-house-scale.patch
```

`Claude-Farmland.patch` installs the margin build/release calls, shares world-space growth with the distant painter and uses the parent parcel's corner gate for clipped boundary fragments. `Claude-house-scale.patch` has only two matrix-scale changes, in `FarScatter.cs` and `SettlementHlod.cs`, so distant houses keep their sector scale. The forest implementation is untouched; coordinate that one-line FarScatter integration with Codex A. No Ground or Atmosphere patch is required.

## Review artifacts and validation

[Open all twelve renders](Review/index.html), or [compare the five villages](Review/villages-contact.jpg). Fixed random sample: Tolbeck (6), Grisham (7), Dunmere (10), Redwick (13), Sorrel (14), all world seed 1200. Each has a 150 m and a 400 m view. Town Outskirts is at 400 m; the field view is at 200 m. These labels are camera altitude above the focus ground, not slant range.

These are Blender 5.2 layout reviews, with actual accepted building transforms, garden meshes and the field-margin geometry. Shared production colour sampling supplies the ground. Lighting/material approximation, coarse review terrain, hedge silhouettes and the camera are harness-only. Crop shader detail, full-resolution terrain splats, native culling and HDRP appearance are not verified by these renders.

- C# 9 compile against the installed Unity 6000.6.7f1 Core/Terrain/TerrainPhysics/Physics/Animation assemblies: **0 warnings, 0 errors**. Includes the exact temporary patched Farmland and SettlementHlod sources; the FarScatter patch is a source-reviewed matrix-scale expression, not a compile of the whole protected forest implementation. The harness uses source-checked service contracts for unrelated systems.
- Twenty villages / 318 properties: **zero intersecting planned plots**, 90 old buildings, 53 shaped plots, 20 boundary farms, 138 cultivated gardens, 90 sheds, 51 log piles, 39 untended gardens and 46 changed roofs. Old-core and shaped-plot percentages pass in every village. Every planned property anchor, including the four towns, lies on an authored road or connected lane within 0.05 m. Width and scale bounds pass. Untended/cultivated flags do not overlap. [Counts](Review/checks.json).
- Repeated world seed 1200 produces the same property fingerprint; generating seed 1201 does not mutate the retained snapshot. [Determinism result](Review/determinism.txt).
- Sampled working/soil area is 20.6–28.3% of property area across those villages. This includes paths, yards, shed working ground and cultivated soil; it is not a measurement of final terrain-splat coverage or a yard-only denominator. Actual prop placement can reject a shed/log/tree for clearance or renderer budget, so the planned use mix needs the Unity visual check below.
- Scope audit: `World.cs` outside the village section is unchanged; Ground, Farmland, Atmosphere and restricted game files are unchanged. No LINQ or packages added. Source CRLF and both patch applicability checks pass.

Lean managed comparison against `cb49269`, one scene per process:

| Scene | Layout before / after | Village build before / after | Yard/fence triangles near before / after | Yard/fence triangles far before / after | Houses before / after |
|---|---:|---:|---:|---:|---:|
| Town Outskirts | 751.2 / 769.2 ms | 99.5 / 71.6 ms | 141,422 / 67,116 | 75,900 / 6,510 | 128 / 133 |
| River Valley | 685.2 / 722.9 ms | 51.1 / 46.2 ms | 22,468 / 3,984 | 12,772 / 474 | 28 / 10 |

Town resident prop renderers increased from 262 to 445 (including hidden LODs and smaller culling blocks). The two River Valley layouts contain different village populations; its village timing improvement is not an equal-population speedup. Layout time increased in this final sample, while village construction decreased. These single-run figures exclude the full splat build/flush, Farmland crop generation, forests and all HDRP rendering. They cannot establish the 0.4 ms GPU or complete-sector budgets. Peak managed comparison processes were 234–262 MiB; review export processes were 241–279 MiB. The separate field-use build took 76.3 ms for 233 blocks / 65,534 triangles, and its renderers are deducted from Farmland's existing allowance by the patch. Blender ran one job at a time with two threads. [Before](Review/benchmark-before.log), [after](Review/benchmark-after.log), [field geometry result](Review/Field.log).

## Reproduce without Unity

From this worktree, with the installed Python 3.13, .NET SDK and Blender 5.2 paths used by the scripts:

```powershell
py -3.13 -I Tools/Tests/LivedIn/prepare.py
& 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe' Temp/lived-in/build/bin/Release/net8.0/harness.dll Checks Tools/Tests/LivedIn/Review
py -3.13 -I Tools/Tests/LivedIn/run.py --renders
py -3.13 -I Tools/Tests/LivedIn/benchmark.py
py -3.13 -I Tools/Tests/LivedIn/package_review.py
py -3.13 -I Tools/Tests/LivedIn/audit.py
```

The lean harness deliberately replaces full splat allocation/flush with bounded storage. It retains the production terrain-height, wear-index, village placement, shared plan and geometry code. Scripts depend on the existing Water/StreamingPerf harness and WorldLandmarks model-reader helpers. Temporary projects/exports live under ignored `Temp/lived-in`.

## Unity build checklist for Claude

- [ ] Apply both patches, compile/build, run autotest, and confirm zero errors. Preserve Codex A's forest changes when integrating the FarScatter scale expression.
- [ ] Capture the five villages at 150 m and 400 m in HDRP. Check old street density, different branch arrangements, visibly narrower setbacks in the old core, farm edge location, nonrectangular gardens, palette/roof/scale variation and actual shed/log/untended distribution.
- [ ] Follow every visible house door path/drive to its street. Check mission-road detours, track endpoints, road-width joins and sector entry/exit; reserved mission sites may legitimately suppress individual planned buildings.
- [ ] Inspect barn/shed clearances, 1–3 m working aprons, farm gate-to-yard-to-barn circulation and 15–30% visibly worn yard area on the real terrain splats. Confirm cultivated patches and neglected plots remain distinct.
- [ ] At 200 m over crops, check margin width, 10–30 m growth drifts, rough/wet patches and tractor gate openings. Check sloping margin meshes for crop intersection or flicker. Cross a sector clipping seam: no new internal margin or invented gate should appear.
- [ ] Compare sector, tile, regional ring and world picture for matching field drift/margin/gate placement and village footprints/scale. Check 250–400 m transitions for popping or small clutter remaining visible.
- [ ] Measure GPU over a village at 300 m against hd152: increase **at most 0.4 ms**. Check active renderers/material passes as well as triangle counts; resident prop renderer count increased.
- [ ] Full Town Outskirts build **no worse than 16.3 s**, River Valley **no worse than 17.6 s**, on the same machine/run conditions as hd152. The lean measurements above do not certify these limits.
- [ ] Run the flight test and compare p95 with hd152's **14.8 ms**. Recheck the grey-blue river and untouched dam; this branch makes no water or forest changes.
