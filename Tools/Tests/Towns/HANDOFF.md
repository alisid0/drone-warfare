# Towns that read as small central-European towns at 300 m and 2.5 km

Branch `codex/towns` from `0d54a9d` (written by Claude standing in for Codex). No Unity import or run, no push or merge. The staged `Tools/Tests/FieldsVillages/*` files and the `make_garden_props.py` edit are someone else's and stay uncommitted.

## What changed

**1. Plan (`VillageTown.cs`, the town branch of `VillagePlan`).** The 120 m grid is gone. Each town is planned in a fixed order from its own seeded stream:
- *Old core:* a 62-80 m paved market square opens off the main road at the town's middle, with the church set back across it. Within 150-240 m of the middle, streets are 85-130 m apart with more wander, and they are lined with attached houses built to the pavement: the new old-core terrace, the three-storey row and corner shops. A narrow shop fills a gap that a terrace can't. Attached houses also line the square's two sides.
- *Streets:* streets branch off the through-roads, then off each other, for five generations. Cross streets are 95-140 m apart (two gardens deep); the rest are 110-180 m apart.
  - A street grows in 12 m steps. It joins the first street it meets in a T, stops beside a parallel street rather than leave a thin block, and stops at the outline, water, woods or reserved land.
  - A side already joined by a street from the next block gets no second one.
  - A street may cross the railway at a level crossing but never runs along it. Houses keep 5.9 m off its centre line.
- *Ring (to 0.62 of the reach):* semi-detached pairs (the new semi), villas (house B, house C) and some house A, on 17-24 m frontage behind front gardens.
- *Outskirts:* detached houses on 20-31 m frontage, with a few unbuilt lots.
- *Gardens:* gardens run back to meet the next street's half way across the block (at most 58 m, 40 m in the core). Where a block is deep, or at the town's edge, a backland building (workshop, small house) stands behind instead.
- *Estates:* one or two estates of four-storey apartment blocks, 0.4-0.8 of the way out. Each is 3-4 columns by 3-4 rows of blocks in green space, with parking along the street, paths to the stair doors, one car-park cell and one play-area cell. Apartment blocks appear nowhere else.
- *Working edge:* along the main road where it leaves town: a filling station, warehouses, an office block and a depot, each with a paved yard.
- *Edge uses:* the existing allotments, playing field, cemetery, depot, edge farms and ribbon houses (`TownEdge`), now from 0.9 of the reach outward.
- *Avenues:* trees on the verges of most through-roads (between the core and the outskirts) and of one first-generation street in four, plus a row round the square (`VillagePlan.trees`).

**2. Ground.**
- *New plot uses:*
  - `estate`: lawn, parking, paths and a play area.
  - `yard`: a paved forecourt with lawn strips.
  - `attached`: a paved court with worn patches, then beds or lawn.
  - These are painted the same way in the 2 m tiles, the regional ring, the world picture and the sector, as every plot use is (`VillagePlot.UseAt`). Streets are lanes, so the road painter draws them.
- *Town land outside plots:* it is now ordinary grazed or mown land with paddocks (`Paddock` counts `Town`), and the sector splat shares this. The town-wide garden-mass blend, the pale disc, is removed. Estates and yards don't take the garden mass at range.
- *Village land:* beyond 20 m from any plot (gardens, paddocks or orchards), village land is painted as the field plan's own crops or pasture (`VillageOpen`, `VillagePlan.Clearance`), so fields run to the back fences. The control map follows the same rule.

**3. Buildings.**
- *New models* (via `world_town.py` and `make_world_assets.py`, two levels deep, LOD tris LOD0/1/2):
  - `Scenery_House_Semi`: 2016/396/132.
  - `Scenery_Townhouse_Old`: 1952/464/212. Three burgher houses of different width, height, plaster and roof.
  - `Scenery_FillingStation`: 408/276/204.
- *Scatter:* `SettlementPattern.Models` is now the single kind list, with house C, apartment, shop, the three new models and the street tree added. FarScatter loads it, drawing street trees as their LOD2 crossed cards.
- *Roofs:*
  - Each plot has a roof tone (`VillagePlot.RoofTone`): villages keep their old rule, and town roofs are 45% tile, 20% dark, 20% warm and 15% slate.
  - FarScatter draws a model's `RoofTile` submesh once per tone in use, at most three more draws per kind.
  - The sector's `Repaint` and the HLOD palette (`SettlementPattern.Palette`, 14 texels) use the same tone.
- *HLOD:* `SettlementHlod` keeps one mesh per town with every building as a roof shell in its own colour, so the street pattern reads beyond 1.5 km.

**4. Sector.**
- *Plots and roads:* a Town Outskirts sector builds the same world plots (`VillageLayout.Project`). Each street now becomes one road line instead of one line per segment. Backland and estate plots get no fake street line.
- *Buildings:* `World` has rectangular footprints and radii for the new and large models. Terraces count as houses for gardens and paths.
- *Street trees:* the sector plants the avenues inside its crop (`World.StreetTrees`, `MapLayout.VillageTrees`), posed as the scatter poses them.

## Measurements (`Review/measure-*.json`, harness, best of 3)

| | 0d54a9d | codex/towns |
|---|---|---|
| Brenmoor buildings (scatter, excluding trees) | 914 | 1676 (+316 street trees) |
| Halvard buildings | 647 | 1334 (+242 trees) |
| Road-facing share (Brenmoor / Halvard) | 100% / 100% | 87% / 86% |
| Attached share | 0% | 6% / 5% |
| Irregular or empty parcels | 16% | 16-17% |
| Frontage p10/50/90 | 25/30/30 m | 18/23/29 m |
| Block faces in 80-180 m (median) | 92% (110 m, a lattice) | 70-76% (118-126 m) |
| HLOD triangles, Brenmoor | 12.8k | 27.9k |
| `VillagePlan` (all 24 settlements) | 207-235 ms | 115-130 ms |
| Detail capture | 345-400 ms | 300-320 ms |
| 2 m tile: Brenmoor / Halvard / Ashcombe | 244-254 / 218-240 / 130-140 ms | 218-249 / 194-268 / 148-154 ms |
| 512-square world picture | 508-521 ms | 520-567 ms |
| Villages' plans | unchanged | unchanged (plots, buildings and kinds identical for all 20) |

## Renders (`Review/`)

- Top-down pictures at 300 m (0.5 m pixels; the 2 m tile lattice, bilinear) and 2.5 km (4 m pixels; the regional texel), for:
  - Brenmoor, the flight test's town;
  - Halvard;
  - Ashcombe, a village, for the disc.
- Files are `*-before.jpg`, `*-after.jpg` and `*-pair.jpg` (before above).
- The painter is the production `FarDetailData.Paint`. Buildings are the production `SettlementPattern` plan, drawn as shaded roofs in the HLOD palette. Woods are tinted from cover.
- Previews of the three new models are in `Scenery_*.jpg`.

## Build checklist for Claude

1. Merge, keeping `FarScatter.Prime`. My FarScatter edits are in the header, the per-frame clear, `House`, `Draw`'s part loop, a new `DrawRoofs` and `LoadModel(s)`. `Prime` loops `models` and their `parts`, so it primes the new kinds. The extra roof materials share the tile shader.
2. Let Unity import the three FBXs (their metas use the world-asset template) and `VillageTown.cs.meta`. Check for prefab LOD groups and that the material names map to the PBR set (`PlasterOchre`, `PlasterPink`, `RoofTileDark`, `RoofTileWarm`).
3. Compile, then run the autotest. Watch:
   - `91_flight_3_village_300m` / `_2500m` over Brenmoor;
   - `worst commit` under 2 ms, the world build and world picture stages, and `Continuous flight` frames near the town;
   - `HousesDrawn`, `InstanceDrawCalls` and GPU time with the town in view (budget +0.3 ms).
4. In a town view check:
   - roofs vary in tone;
   - street trees render as cards;
   - semis, old-core terraces and the filling station sit facing their streets;
   - nothing stands on a street or the railway.
5. If a Town Outskirts sector is in the campaign, check that the houses, estate and yards build, that the avenues plant, and that roads join at the town streets' junctions.
6. Village sectors are unchanged in plan. Their open ground beyond 20 m from plots shows crops in the far tiles only (see below).

## Not verified (no Unity)

- HDRP look, GPU time, draw calls and streaming-stage timings. The scatter draws about twice as many town buildings as before (LOD1, 276-1052 triangles each), plus street-tree cards.
- Whether the new FBXs import with the expected materials and LOD groups, and how the street-tree impostor (LOD2) looks from low altitude.
- The sector's handling of the new models (footprints are estimated from the model bounds) and of joined street lines in `RoadLayout`.
- The mainline railway is not drawn outside sectors. Through Brenmoor's middle it shows as an empty strip, about 12 m wide, between back gardens and streets that cross it.
- *Village ground:* only the far painters and the control map treat village land beyond the plots as fields. The sector's own crops still need `Farmland` cover. So where a village's cover crosses a sector seam, crops can stop at the seam.
- ChatGPT's spec, by item:
  - *Road-facing share:* 86-87% (target 70-85%).
  - *Block faces:* 70-76% within 80-180 m.
  - *Old core:* has some open block interiors.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/Towns/prepare.py                         # API compile and harness
py -3.13 -I Tools/Tests/Towns/prepare.py --before --harness      # the 0d54a9d harness
py -3.13 -I Tools/Tests/Towns/run.py --render                     # add --phase=before for 0d54a9d
py -3.13 -I Tools/Tests/Towns/package.py
py -3.13 -I Tools/PredictiveStreaming/run.py api                  # every runtime script against Unity and its packages
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/Blender/make_world_assets.py -- Assets/DroneSandbox/Resources/Models Temp/towns/previews Scenery_House_Semi,Scenery_Townhouse_Old,Scenery_FillingStation
```
`TOWN_RAIL=1` before `run.py` overlays the railways and world roads on the pictures.
