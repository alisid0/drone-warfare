# Places that look used, second pass

Branch `codex/used-look-2` from `b8d8322`. No Unity import or run, no push or merge, no protected files. The staged `Tools/Tests/FieldsVillages/*` files and the `make_garden_props.py` edit are someone else's and are left uncommitted.

## What changed

**Soft surfaces (every landmark).** A `LandmarkSurface` can now have:
- a soft edge: a width per outline edge, 0.5-2 m (35-100% of a widest value, never more than 40% of its narrower half);
- a tone variant (lighter or darker);
- pours: a tone per 9-13 m bay along a concrete slab.

One rule, `WorldLandmarkLayout.Shade`, decides what shows: the top surface fades (smoothstep) through its soft edge into the flat tone of the surface below, or into verge grass where nothing lies below. `WorldFeatureMap.SurfaceAt` and `WorldPlan.FeatureSurfaceAt` both call it, so the tiles and the world picture blend the same way.

The slab mesh reproduces this (`LandmarkMesh`):
- Each piece is split into a full-strength core and one zone per nearby soft edge. Inside a zone the weight is linear, so vertices carry it exactly.
- The tone pair and the weight go into UVs on a 512x64 palette texture: eight texels per pair (top over below), 58 tones.
- Every surface of a landmark is now one submesh with one material (`Features.LandmarkSurfaceMaterial`, an HDRP Lit `BaseLit` copy with `mainTexture`, as `SettlementHlod` does). This is used near (sector) and distant (HLOD).
- Where the ground below changes along a soft edge, the zone is halved, up to twice, so each part fades to the ground it lies on.

Layering:
- Decals sit in lift tiers a layer gap apart, so they are drawn over one another instead of cutting each other into pieces: traffic 0.125, patches 0.162, marks 0.198.
- The 96-piece cap is unchanged; zones are split after the subtraction.
- `LayerGap` is unchanged at 3.5 cm.

The distant mesh is built by `LandmarkMesh.Builder`, about 1 ms a frame (`LandmarkHlod`). `Build` runs it to the end for the sector.

**Works.**
- Repairs are irregular blobs, close in tone to the slab: 10% newer, 45% older. They gather on the hall forecourts where lorries turn, not at random.
- Feathered stains, aprons, lobed aggregate spills and fence weeds; about a quarter of the weed runs are darker scrub.
- Continuous worn bands (`Route`, 4-5 m, soft) join each active loading door, the storage stacks, the shed, the tanker stand, the service track between the tank rows, the parked vans and the loader's track to their lanes, under the wheel lines.
- Each hall has a broad worn forecourt.
- Concrete is laid in pours; the gravel yards have large soft compacted and loose patches.
- Yards fade into their soil rims, and the rims fade onto the grass.
- Working corners:
  - skips and pallets beside the second hall;
  - a parked trailer bay on the gravel;
  - a weedy corner with scrub and an abandoned container.

  These reuse the existing yard props in two new spatial groups.

**Rail yards.**
- Each long siding's ballast is laid in 40-110 m runs, dusty, plain or oily, each fading in from the run before. Oil drips where wagons stand, and spilled load under the hoppers.
- Soil and weed tongues straddle the yard's ends and south rim, so the yard-grass edge is irregular.
- The gate road and the service road are worn: faded patches and broken, muddy edges. Worn bands lead from the three pads to the sheds, the gantry and the warehouse.
- Denser scrub along the fences.

**Village shadow blobs: the cause.** They were the forest floor. `Ground.PaintForestFloor` spreads litter and shaded damp grass round every logical stem and every planted tree:
- A lone field tree or a pair, or an orchard whose small trees fade out at range (`VillagePropLods`), left a dark disc with nothing over it.
- A copse's floor ran well past its crowns.

The fix:
- Each stem now weighs in by the woodland stems within 25 m: none for a group of three or fewer, a little for a copse, as before from about ten. Every wood's edge and corner keeps its floor.
- The floor also stops where the stems' own grid runs out.

**Village land use.** The fringe plan fills more spare pasture, on a 46 m grid reaching 15% past the houses:
- vegetable plots, up to three a village: soil beds between grass paths, a compost corner with a tool shed, half with rails;
- paddocks in two sizes, each grazed, hay (a paler uncut meadow, new ground code 6) or rough grazing;
- rough grass and orchards as before.

No hedges were added. Sector access roads and tracks no longer bend round fringe plots (`VillageAccess`); a crossed plot is left out in that sector. The bending had pushed two houses, a barn and the pump house off their plots in Sector 4.

## Numbers (managed harness, .NET 8, busy machine)

Landmarks: the ranges cover the four variants of each kind. The build times are mostly noise from Unity running alongside.

| | Works before | Works after | Rail before | Rail after |
|---|---|---|---|---|
| Surfaces | 194-222 | 243-317 | 183-185 | 323-329 |
| Surface triangles | 2.5-2.8k | 7.3-8.7k | 2.5-2.9k | 9.6-10.0k |
| Surface draws, sector | 11 | 1 | 12 | 1 |
| Submeshes, distant (with buildings) | 17-18 | 7-8 | 15-16 | 4-5 |
| Sector mesh build | 1.0-1.3 ms | 2.3-4.5 ms | 1.2-1.4 ms | 3.8-4.7 ms |
| Distant build | one stage, 1.1 ms | 3-4 slices of about 1 ms | one stage, 1.2-1.5 ms | 5-7 slices of about 1 ms |

Notes on the landmark numbers:
- Airfields go from 7 surface draws to 1, and from 12-13 to 6-7 distant submeshes.
- Best of seven builds (`Profile`) for Rail 20 / Works 10: hard 1.39 / 1.06 ms, all soft 3.73 / 2.89 ms. Almost all the extra comes from soft decals (3.27 / 2.22 ms alone); slabs add 0.4 ms.
- After a warm-up, the dearest single surface takes 0.2-0.5 ms. The few slices over 2 ms (2.1, 6.4 and 8.5 ms once each, and one 11 ms) match GC pauses in this harness and do not repeat run to run.
- Mesh/painter agreement:
  - Query disagreement stays 0.
  - Visible points that differ by more than 5 sRGB levels go from 3-13 to 105-364 per 42-62k (0.2-0.8%).
  - All of these are inside soft bands, where the ground below changes across the band: the mesh keeps one tone below per zone piece, while the painter reads it per point.
- GPU: about 7k more triangles per visible compound, 6-11 fewer draws per compound near and far, and one 128 KiB RGBA32 texture without mips.

Villages (whole world plan, `Villages`):

| | Before | After |
|---|---|---|
| Paddocks | 292 | 411 |
| Vegetable plots | 0 | 72 |
| Orchards | 48 | 47 |
| Rough grass | 369 | 395 |
| Hedges (share of field-edge length) | 12.2% | 12.2% |

Sectors 4 / 9:
- 29 / 25 houses before and after.
- Resident renderers 124 / 107 → 124 / 108, inside the `8 + 4 x houses` cap.
- Village build 64.8 / 54.5 → 56.9 / 54.6 ms.

Forest floor (synthetic test, `Floor4`):
- Isolated floor (more than 12 m from a real stand) 54 → 0 m².
- The copse's and the pair's discs are gone; the wood's floor is identical.
- Floor area 8,547 → 8,253 m².
- Building the floor grids takes 2.9 → 5.6 ms on this small layout. Counting stops at ten neighbours, so dense woods stay cheap.

## Evidence

`Review/` holds:
- `view-*`: before/after views framed like the autotest's `89_landmark_*_300m` (1280x720, 45 degree lens, 50.5 degree pitch, yaw 35). They show the sector slab mesh decoded through the palette as the GPU filters it, the shared painter beyond it, and buildings as boxes.
- `landmark-*`: top-down views of the painter at 0.5 m.
- `village-view-9`: framed like `02d_village`. The painter only; the sector splat is not built here.
- `floor-*`: a synthetic floor test on sector terrain: a wood, a 7-stem copse, a pair, a lone tree and a 6-tree orchard. Red dots are groups of three or fewer.
- JSON results for everything above.

Every scene is deterministic. The API compile (C# 9, netstandard2.1, Unity 6000.6.7f1 modules, warnings as errors) passes for every changed file. `--before` builds the same harness from b8d8322, now including its own `Ground.cs`.

## Not verified (no Unity here)

- HDRP look and GPU time. The palette material uses `_BaseColorMap` with UV0 and no new keywords; `BaseLit` has 1/0 tiling.
- Z-fighting of the new tiers on the HLOD at 1-20 km.
- Streaming stage and sector build times in Mono.
- The real sector splat for the new fringe plots and the forest floor.
- The forest code is stubbed in the harness, so the floor was tested on a synthetic layout, not the 02d_village wood.
- No new models: the skips are scaled `Scenery_FreightGroup`s and the trailer bay holds `Scenery_UtilityVehicles` flatbeds. A real skip and semi-trailer would read better.

## Claude's checklist

- [ ] Compile, then run the autotest. Compare `89_landmark_IndustrialZone_300m`, `89_landmark_RailJunction_300m`, `31_sector_04/05` and `02d_village` with hd162.
- [ ] Check that the yard surfaces are not pink or white. If they are, the palette texture or UVs are not reaching `_BaseColorMap`.
- [ ] Check the soft edges at 300 m and on the HLOD at 1-20 km: no z-fighting between the traffic, patch and mark tiers. If any appears, raise the tier lifts or `LayerGap`.
- [ ] Profile:
  - GPU no more than +0.2 ms at low flight;
  - `LandmarkHlod.DrawCalls` should fall (one surface draw per landmark instead of 7-12);
  - no streaming stage over 2 ms on first sight of a works or rail yard;
  - sector build: the landmark slab mesh takes about +2-3 ms per compound in .NET.
- [ ] Look at a village from 200-300 m:
  - no dark disc without a tree;
  - copses keep their trees' shadows;
  - woods' floors and edges unchanged;
  - vegetable plots and paddocks read in the open grass.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/UsedLook2/prepare.py                       # API compile and harness
py -3.13 -I Tools/Tests/UsedLook2/prepare.py --before --harness    # b8d8322 baseline
py -3.13 -I Tools/Tests/UsedLook2/run.py Checks --render           # Landmarks, Hedges, Villages (+ pictures); --phase=before
py -3.13 -I Tools/Tests/UsedLook2/run.py Sector4 Sector9 Floor4 --render
py -3.13 -I Tools/Tests/UsedLook2/run.py Profile                   # mesh build cost: plain, soft, decals, slabs, both
py -3.13 -I Tools/Tests/UsedLook2/package.py                       # copy results and pairs into Review/
```
