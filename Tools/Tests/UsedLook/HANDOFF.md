# Places that look used

Branch `codex/used-look` from `fe0df56`. No Unity import or run, no push or merge, no forest changes, and no edits to protected files. The staged `Tools/Tests/FieldsVillages/*` files and the `make_garden_props.py` edit are someone else's and are left uncommitted.

## What changed

**Works.** The random dark wear patches are gone. Wear now follows use:
- active loading doors (about 60% of them) get a dirty apron, a drip stain and a lorry track that swings out to the lane;
- lorry tracks also run to the freight stacks, the shed's road side and the tanker stand at the pipe racks;
- stains at tank valves, under parked vans and where wagons stand at the rail shed;
- rectangular slab repairs in a darker shade, aligned to the yard;
- aggregate spills around three stockpiles (one new pile by the spur);
- broken runs of weeds inside the fence.

Roofs carry an age (original, weathered, rusted or new, about 40/25/15/20%). It swaps `RoofMetal` for `RoofBarn`, `RustySteel` or `Galvanized` on the sector models (`LandmarkClutter.Tune`) and on the instanced models (`LandmarkInstances`). The HLOD proxies use matching tints, in materials 17-19.

**Rail yards.**
- A rust-brown formation, with lighter shoulders, under darker ballast.
- One idle siding and one short rake, so not every siding is full.
- Weeds on the idle siding, and between sidings beyond the standing wagons.
- Freight groups and forklift wear on the platform.
- Lorry tracks from the three road pads to the shed doors, the gantry and the warehouse.
- Dirty shed aprons, a maintenance track, a scrubby south margin and weedy fences.

All new ground uses `WorldLandmarkLayout.Surface*`. That means the sector slabs, the HLOD, `WorldFeatureMap.SurfaceAt` and therefore the tiles and the world picture all agree. Raised decals (at least 3.5 cm above a slab) are now layered on top instead of cutting holes in it. The piece cap went from 32 to 96. Together these drop the visible mesh/painter disagreement from 76 to 10 points per 48k samples, and keep the build time flat.

**Villages and farmyards.**
- Boundaries:
  - 30% of gardens have none.
  - Most others leave runs out: open frontages, rear runs into the land behind, and side runs dropped at shared street corners, so neighbours merge.
  - Of 3,043 plots, 621 are fully bounded, down from 2,425.
- Drives widen at the street, and three in five have a car's drip stain.
- Some gardens let a back corner go, and shed owners wear a path across the lawn.
- Farm plots:
  - mud at the yard gate, in front of the barn and in hollows;
  - a muck heap with mud around it;
  - tractor ruts out of the back of the yard.
- Standalone farmsteads get a muddy yard and a muck heap.
- Villages with 14 or more houses get a transformer kiosk, and 28 or more also a pump house. These are placed before garden clutter, so it can't spend their budget.
- Fringe ground, planned after every settlement's houses:
  - Spare pasture and village ground (never crops or wood) becomes paddocks with rails and a trodden gate, irregular rough grass, and up to two small orchards of up to six trees.
  - These are `VillagePlan` plots, so the far painter and the sector agree. `VillagePlan.Near` ignores them, so woods, scrub and placement rules still treat them as open ground.
- Distant rough village ground now uses the sector's dry-meadow layer colour.

**Hedges.**
- Each parcel edge picks its own use:
  - a hedge at the district's woody share: 5-15% in most districts, more near villages and in small-strip country, with pasture and orchards enclosed more often;
  - otherwise bare crop to the boundary, a grass strip, a wide uncultivated margin, or a field track with two ruts.
- Results:
  - Woody share of edge length: 55.8% before, 12.2% after. District p10/median/p90 is 5.9/11.4/18.1%, and 32.5% near villages.
  - In-sector hedges: 677 → 267 (Sector 4) and 341 → 110 (Sector 9).
- Sector hedges, gateways (`RoadGatePlan`), distant hedge paint, margins (`FieldMargins`) and the control map all read the same packed edge list.
- Hedge paint now sits on the parcel edge, where Farmland builds the hedge.
- Legacy Operation boundaries drop from 48% to 14% hedges.

**New models.** `Scenery_MuckHeap`, `Scenery_TransformerKiosk` and `Scenery_PumpHouse`, from `Tools/Blender/make_used_props.py`. Each has three LODs (288/140/56, 192/108/60 and 120/52/32 triangles), with one mesh per LOD directly under the root.

## Evidence

`Review/` holds top-down pictures from the production painter (before/after) and the JSON results:
- The works and rail yard pictures show building footprints darkened and props as red dots.
- `sector-*.json` are full production sector builds (`World.BuildVillages`, `FieldMargins`) in the managed harness.
- Every scene is deterministic: two runs give identical JSON.

The API compile (C# 9, netstandard2.1, Unity 6000.6.7f1 modules, warnings as errors) passes. It covers every changed file except `make_used_props.py`, which was run in Blender 5.2.

**Not verified (no Unity here):** HDRP look at 300 m, GPU time, streaming-stage times and real sector build time. Managed numbers (.NET 8, noisy machine):
- Landmark mesh build: 0.5-0.8 ms → 0.9-1.2 ms per compound. `LandmarkHlod` now meshes at most one new compound per frame, so a sector change no longer meshes every yard at once.
- Village build: about +4 ms per sector.
- `FieldMargins`: 43 → 48 ms best of four.
- Farmland should save more than this, since it builds 40-70% fewer hedges, but that is not measured here.
- Village resident renderers: 75 → 104 and 89 → 124, still inside the existing `8 + 4 × houses` cap. These come from paddock-rail blocks, orchard trees and the kiosk.
- Field-margin blocks with tracks gain a third, soil, submesh: 87 of 250 blocks in Sector 4.

## Claude's checklist

- [ ] Import the three models (check LODs, photo materials and orientation). Compile, then run the autotest. No protected-file patch is needed.
- [ ] Compare hd160 `89_landmark_IndustrialZone_300m`, `89_landmark_RailJunction_300m`, `31_sector_04/05`, `02d_village` and `02e_farmland` at matching settings. At 300 m, a residential street, a farmyard, a working yard and a loading area should be distinguishable without labels.
- [ ] Look for z-fighting of the raised decals (3.5 cm or more above their slab) on the HLOD at 1-20 km. If any appears, raise `LandmarkMesh.LayerGap`.
- [ ] Check the roof swaps on sector, instanced and HLOD copies of the same hall.
- [ ] Profile: no more than +0.3 ms GPU at low flight (hd160 p95 is 12.76 ms). Every streaming stage must stay at 2 ms or less, including first sight of a works and the sector change (one compound per frame now). Sector build should be no slower than hd160, including the Farmland hedge savings.
- [ ] Walk a sector edge across a village fringe and a hedged/unhedged field edge, and check tile/sector agreement.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/UsedLook/prepare.py            # API compile and harness
py -3.13 -I Tools/Tests/UsedLook/prepare.py --before --harness
py -3.13 -I Tools/Tests/UsedLook/run.py Checks --render   # Landmarks, Hedges, Villages (+ PNGs)
py -3.13 -I Tools/Tests/UsedLook/run.py Sector4 Sector9   # add --phase=before for fe0df56
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --threads 2 --python-exit-code 1 --python Tools/Blender/make_used_props.py -- Assets/DroneSandbox/Resources/Models
```
