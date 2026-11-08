# Far villages: what Unity draws round Ashcombe at 300 m, and the disc at 2.5 km

Branch `codex/far-villages` from `f7588a1` (written by Claude standing in for Codex). No Unity, no GPU work, no push or merge. The staged `Tools/Tests/FieldsVillages/*` files and the `make_garden_props.py` edit are someone else's and stay uncommitted.

## The cause

**No house is culled.** Unity draws every building Ashcombe's plan has. The village looks thin because of the plan and what stands on its plots outside a sector.

- *Evidence: hd180's frames.*
  - The harness fits RtsController's camera to hd180's 50 m shot. The focus is (-1883, -8387), and the shot's three houses are the plan's three B houses there.
  - It then projects the plan into the 300 m frame. 18 buildings fall in it: 11 in the open, 7 under the feed panel and the order bar (the church's spire shows above the bar).
  - All of them are in the shot, each within about 15 px of its predicted position: the 4 houses on the NE road, the old-core rows of 4 and 2, the church and the barn on the right.
  - `hd180-300m-shot-vs-harness.jpg` shows the shot above the harness's picture of the same frame.
- *The code path.* `SettlementHlod.Update` → `Draw` → `FarScatter.House` runs for every building in a block when two things hold: the block's centre is nearer the camera than `HouseReach` (1500 m), and the block's 180 m box is in the frustum.
  - At 300 m the camera is about 231 m up, so every block in view is near.
  - Every house kind loads (`models[36 + kind]`), and no budget limits houses.
- *The plan.* Ashcombe has 57 plots:
  - 22 built: 21 houses and the church;
  - 34 fringe: 21 paddocks, 8 rough, 2 orchards and 3 vegetable plots, 23 of them fenced.
  - The scatter drew 23 buildings and **no trees**.
  - In the 300 m frame, 16 of the 32 plots whose middle is in view are fringe plots.
- *Why the plots looked empty.*
  - **Painted fences.** `VillagePlot.Colour` painted post-and-rail (`boundary == 2`) as a 30 cm line of dark soil, so every fenced paddock had an outline, like an empty house plot. The sector builds that fence as 11 cm rails (`Features.Edge`). It drops them beyond 300 m (LOD1) and paints nothing on the ground.
  - **Missing trees.** The plan's garden, orchard and churchyard trees existed only in the sector (`World.Garden`, `World.Orchards`, the church's pair). Outside a sector, gardens were bare lawn in the 2 m tiles and orchards were empty painted rows.
- *The disc.* Two things made it:
  - `VillageOpen` kept an 8-20 m halo of meadow round every plot, fringe plots included. Round 57 plots that formed a soft blob.
  - A town's open ground was always pasture (`CoverColour`: Town → grass). So Brenmoor's cover blob showed as a soft green ring beyond its outer gardens.

## What changed

1. **`SettlementPattern.Build`: the plan's trees.** They use the sector's own rules and poses:
   - two young trees in an untended garden, one in a third of the kept vegetable gardens (never on a farm's barn);
   - up to six fruit trees on each orchard's 7 m rows;
   - the churchyard's pair.

   They are `Tree` kind entries, so FarScatter draws them in the existing street-tree batch (Scenery_Tree LOD2 cards), and SettlementHlod adds them to the settlement's one mesh as crowns. Trees: Ashcombe 0 → 22, Brenmoor 316 → 944.
2. **`VillagePlot.Colour`: rails at their width.** Post-and-rail paints as the 11 cm rail, not a 30 cm soil line. Hedges and walls are unchanged.
3. **Painter: a settlement's open ground is the country round it.** This lives in `FarDetailPaint`, `FarDetailCover`, `VillagePlan` and `VillageFringe`.
   - `SettlementOpen` replaces `VillageOpen` and applies to towns too. The field plan's crops or meadow begin 2-6 m from any plot, so there is no halo.
   - Gaps a settlement closes round stay its grass. `VillagePlan.Openness` closes the gardens and town uses on a 16 m lattice: grow 32 m, flood the country in, grow back 48 m. That keeps the railway strip and block interiors free of crop patches inside Brenmoor.
   - Settled cells get the cover of the open land nearest them (`FarDetailCover.Farmed`, a breadth-first spread from the settlement's edge). A settlement in grazing land keeps pasture rather than growing a crop disc.
   - Fringe plots stand on the pasture's grass instead of the gardens' lawn.
   - Inside a plot the painter no longer evaluates the cover colour it then discards. The output is the same, and it pays for the new lookups.
   - The control map uses the same rule.

## Measurements (`Review/measure-*.json`; .NET 8 harness, two threads, best of 3)

| | f7588a1 | codex/far-villages |
|---|---|---|
| Ashcombe / Brenmoor trees in the scatter | 0 / 316 | 22 / 944 |
| HLOD triangles added (14 per tree) | | +308 / +8.8k (Brenmoor was 27.9k) |
| Brenmoor open ground 20-60 m from a plot, sRGB distance from the open land round it (colour spread) | 14.3 (5.7) | 1.4 (22.4) |
| Brenmoor open ground 60+ m from a plot | 13.0 (6.4) | 9.4 (22.8) |
| Ashcombe open ground 6-20 m from a plot | 14.8 (12.3) | 6.9 (21.8) |
| Fringe plots against the region's pasture cover | | (114,121,83) against (109,120,81) |
| Settlement plan pass, main thread (SettlementHlod's first step): Ashcombe / Brenmoor | 0.12 / 0.41 ms | 0.12 / 0.57 ms |
| 2 m tile: Ashcombe / Brenmoor | 121-123 / 181-183 ms | 120-122 / 172-174 ms |
| 4 m regional page: Ashcombe / Brenmoor | 253 / 359-373 ms | 250 / 340 ms |
| Village plan, all 24 settlements | 88-91 ms | 90-92 ms |
| Detail capture (world setup, once) | 230-236 ms | 238-262 ms |

Gardens keep their own lawn and, beyond the tiles, the garden mass. So they stay about 21-24 sRGB from the fields, by design.

## Renders (`Review/`)

- The flight test's strategy view at 1920×1080: RtsController's pitch for the zoom, 45° lens, yaw 30.
  - **Ashcombe** is at hd180's fitted focus. **Brenmoor** is at its market square.
  - Each is shown at 300 m and 2.5 km.
- Files: `*-before.jpg`, `*-after.jpg` and `*-pair.jpg` (before above after). Also `hd180-300m-shot-vs-harness.jpg`.
- How they are drawn:
  - *Ground:* the production `FarDetailData.Paint` on the 2 m tile lattice (300 m) or the 4 m regional texel (2.5 km), filtered bilinearly.
  - *Buildings:* the production `SettlementPattern` plan as SettlementHlod's shells. In the game, 300 m shows the instanced models instead.
  - *Trees:* low crowns in the HLOD palette. The game shows LOD2 cards.
  - *Shadows* are cast along the sun. *Woods* are tinted from cover.

## Budget

- **Draw calls:** no new batch. The trees go into the street-tree batch and the settlement's single HLOD draw. A village with no town in view now fills that tree batch: one instanced draw per submesh of Scenery_Tree's LOD2, which was skipped while empty.
- **GPU:** not measured. Over Ashcombe it adds 22 small card instances and 308 HLOD triangles.
- **Streaming:**
  - No streaming stage changed. Tile and page workers are a little cheaper.
  - The main-thread plan pass grows by 0.15 ms for Brenmoor in .NET 8. Mono will be slower.

## Build checklist for Claude

1. Merge. The changes:
   - `SettlementPattern.cs`: `Build`, plus new `Garden`, `Orchard` and `Plant`;
   - `VillageUse.cs`: one line in `Colour`;
   - `FarDetailCover.cs`: `Farmed`;
   - `FarDetailPaint.cs`: `Control`, `PaintPoint`, `CoverColour` and `SettlementOpen`;
   - `VillageFringe.cs`: the enclosure and `Openness`;
   - `VillagePlan.cs`: one call.

   No assets or metas.
2. Compile, then run the autotest. Look at `91_flight_1_village_300m` / `_2500m`:
   - trees in Ashcombe's orchards and some gardens;
   - paddocks without dark outlines;
   - fields up to the back fences, and no soft disc at 2.5 km.
3. Watch these numbers:
   - `HousesDrawn`: it counts trees too;
   - `InstanceDrawCalls` and GPU time over the village;
   - `worst commit`;
   - `DetailCaptureMs` at world setup.
4. Over Brenmoor, if a view has it: check there are no crop patches between its streets and that fields meet its outer gardens.

## Not verified (no Unity)

- HDRP look, GPU time, draw calls, and how the LOD2 cards read at 300 m.
- **Sector seam.** The sector's own splat keeps town and village land as pasture (`Paddock`). Where a settlement's cover crosses a sector seam, the far tiles' fields can stop at the seam. The towns pass noted the same for villages.
- **Small items.** Sheds, huts, log piles and muck heaps stay sector-only. The scatter has no hut kind, and one would be a new batch.
- **Ashcombe stays a small village**, 21 houses, with 60% of its plots fringe. A fuller village is a plan change (`VillageGrowth`), which this task left alone.

## Found in passing

- **SettlementHlod shares one set of near flags between cameras.** With the drone feed close to a settlement and the main camera 2.5 km out, the flags flip twice a frame. Each camera then rebuilds the town's index buffer (`SetTriangles`) every frame. Worth checking in the hitch hunt at stops. Not changed.
- **`FarScatter.House` recomputes the ground height and matrix per building, per camera, per frame.** The new trees add to that loop: Brenmoor's instances grow by about 37%.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/FarVillages/prepare.py --harness            # this branch's harness
py -3.13 -I Tools/Tests/FarVillages/prepare.py --before --harness   # the f7588a1 harness
py -3.13 -I Tools/Tests/FarVillages/run.py --render                  # add --phase=before for f7588a1
py -3.13 -I Tools/Tests/FarVillages/package.py
py -3.13 -I Tools/PredictiveStreaming/run.py api                     # every runtime script against Unity and its packages
```
