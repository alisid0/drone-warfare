# The world outside the sector at 300 m to 2.5 km

Branch `codex/regional-realism` from `5fb5060` (written by Claude standing in for Codex). No Unity import or run, no push or merge. The staged `Tools/Tests/FieldsVillages/*` files and the `make_garden_props.py` edit are someone else's and stay uncommitted. ChatGPT's critique 20 specs (relayed mid-task) are used as acceptance where they apply.

## What changed

**1. Settlements.**
- *The dark blob behind each house* was the far painter's yard colour. `VillagePlot.WorkAt` gives every house a soil oval behind it. The sector lays that oval as `Ground.Wear` (its light dirt layer at 85%), but `VillagePlot.Colour` painted it as the dark bed soil (sRGB 66,51,31) that the sector uses only for dug-bed meshes. Worn yards, shed standings and paths now paint as dirt over the lawn: mean yard (73,60,41) → (118,106,82), against lawn (109,121,82). Beds and town surfaces keep the dark soil.
- *Farms:* every farm plot's barn is now in the scatter and HLOD (`SettlementPattern.Barn`, model `Scenery_Barn`). It stands where `World.Garden` puts it in the sector.
- *Churches:* every village now has a church. It is reserved on the old street near the middle before the houses are placed (`VillagePlan.Church`), so the sector builds it too: 4 churches → 24.
- *Village plan:*
  - Old-core frontage is 15-18 m (was 12-14), newer lanes 20-32 m, farms 34 m.
  - About 12% of newer slots and 5% of old ones are left as unbuilt lots (rough grass or a paddock).
  - About 20% of newer houses stand gable-end to the street (`VillagePlot.gable`, one line in `World.cs`), 7% of all village houses.
  - Town streets are each set up to 18 m off their 120 m rhythm.
- *At range:* beyond the 2 m tiles (pixels over 2 m), garden and orchard plots and town ground blend toward a garden-mass tone (lawn, crowns, paving), so a settlement holds together round its roofs. A village's open ground is painted as grazed or mown pasture with paddocks, not one lawn. `Paddock` now counts village ground but skips gardens; the sector splat shares it.

**2. Field blocks (`FarFieldPlan`, `FarFieldSector`).** The 700 m Voronoi districts are gone:
- *Regions:* Voronoi regions about 4 km across. Each takes the main axis of the through-roads within 1.6 km (a length-weighted mean that repeats every quarter turn), else the old broad drift.
- *Blocks:* each region is cut into staggered rectangular blocks, 200-700 m a side, between 4 m tracks that meet in T-junctions.
- *Strips:* each block is cut from its own edges into whole strips and fields. Rows and columns divide it evenly, so no sliver is left.
- *Region edges:* a block cut by the next region tries both grid directions and both directions of the cutting edge, and keeps the layout with the most field.
- *Shape rules:*
  - Field sides are limited to 60-degree corners (`MaxSlope`).
  - A field must be at least 20 m wide at its narrow end and two fifths of its wide end.
  - A leftover joins its neighbour only if the union passes the same test; otherwise it stays meadow.
- *Sector and lookup:* the sector clips the same blocks (`Districts(min, max)`). `Get(x, z)` remains for the old harnesses. Lookups stay lock-free after first build.
- *Result:* in the 8 km square round the farmland block, field area with a corner under 60 degrees went from 24.8% to 0, wedges from 13.1% to 0 and slivers from 11.0% to 0.0% (4 fields).

**3. Land-use mix.**
- *Grassland blocks:* whole blocks are grazed (paddocks painted from the strip slots, about one slot in eight a hay meadow). The chance is 7% plus more on slopes, by water and round villages. They register no crop meshes, so the sector shows the same paddocks.
- *Copses:* 50-170 m woods, more likely on steep corners and along streams. They keep off roads, rail, settlements, farms, sites and landmarks (`WorldPlan.PlanCopses`). They are planned after `PlanCells`, so sector kinds and the campaign are unchanged. World forest went from 31.1% to 31.8%.
- *Crops:*
  - Crops are pulled 16% toward the five-crop mean (`FieldPalette.CropPull`), and per-field value is ±10% (was ±12%). Both are in `CropTint`, so the sector meshes and the painters agree.
  - The neighbouring-crop luma difference fell from 42.9 to 31.1.
  - Fewer ploughed and sunflower fields.

**4. Works.**
- *Cover:* `StampFeature` no longer turns the 88 m ring round a compound into pasture. Farmland and pasture stay; only woods leave the grading reserve.
- *Painter:* draws a 5-15 m worn margin outside the footprint, wandering over 40 m. It is the landmark verge grass worn through to soil and gravel in patches (`CompoundMargin`, `CompoundWear`), and the control map's dirt layer follows the wear.
- *Sector:* fields stop at the same margin (`MapLayout.FieldWorks`) instead of the 64 m reserve.

## Evidence (`Review/`)

- Top-down pictures, `*-before.jpg` (5fb5060) and `*-after.jpg`, plus `*-pair.jpg` (before above after), for:
  - Ashcombe (`village`), the hd166 village;
  - Brenmoor (`town`), hd165's town;
  - IndustrialZone 13 (`works`);
  - a farmland block (`farmland`).
- Each place is shown at:
  - `300m`: 0.5 m pixels; the 2 m tile lattice, filtered bilinearly;
  - `2500m`: 4 m pixels; the regional page texel.
- Painter: production `FarDetailData.Paint`.
- Houses: the production `SettlementPattern` plan, drawn as shaded roof boxes in the HLOD palette, with landmark buildings as grey boxes.
- Woods: tinted from the cover's forest mask. This stands in for the canopy and is not the production renderer.
- `measure-*.json` holds the numbers above.

| Worker cost, best of 3 | Before | After |
|---|---|---|
| 2 m tile: town / village / works / farmland | 173 / 91 / 269 / 57 ms | 176 / 101 / 269 / 61 ms |
| 512-square world picture, cold | 450 ms | 361 ms |
| District/block construction | 432 ms (5275) | 261 ms (16715 blocks) |
| `WorldPlan.Generate` (harness) | 920-1370 ms | 908 ms |

## Build checklist for Claude

1. Merge, compile, then run the autotest. Watch for:
   - `Continuous flight` frames;
   - `worst commit` staying under 2 ms;
   - the world build and world picture stages;
   - `91_flight_*` shots at 300 m and 2.5 km of a village, a works and farmland.
2. In the sector:
   - fields still fill Quenfield/Harby (district occupancy in the build log);
   - no crop meshes stand inside a works' margin;
   - fields meet the far tiles at the sector seam.
3. Village sectors:
   - each village builds its church (`VillageRoom` must accept it) and its barns;
   - gable-end houses sit inside their plots;
   - unbuilt lots show as rough grass or paddocks.
4. Far scatter: the barn model loads at `models[46]`. It adds its submeshes as one more instanced batch while a barn is within 1.5 km: a few draw calls in total, not per settlement.
5. Copses: trees and canopy appear in them, and none stands on a road.

## Not verified (no Unity)

- HDRP look, GPU time, streaming stage timings and the real sector build.
- Whether `VillageRoom` accepts every new church.
- The barn prefab's LOD 1.
- The canopy on copses.
- The town is still a regular street grid of sparse plots. It reads as a mass at 2.5 km now, but its plan was not redesigned.
- Median field width grew from 138 m to 218 m (estate blocks, and strips divided evenly across smaller blocks).
- ChatGPT's 70-85% road-facing share is not met: every plan house is on a road-facing plot, and 7% stand gable-end.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/RegionalRealism/prepare.py                       # API compile and harness
py -3.13 -I Tools/Tests/RegionalRealism/prepare.py --before --harness
py -3.13 -I Tools/Tests/RegionalRealism/run.py --render                   # add --phase=before for 5fb5060
py -3.13 -I Tools/Tests/RegionalRealism/package.py
```
