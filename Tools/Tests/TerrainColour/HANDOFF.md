# Cultivated land at 300-600 m

Branch `codex/terrain-colour` from `d3778ed`. No Unity import or run, no push or merge, no forest, atmosphere, village or protected files; one line of `MapLayout.cs` at the coordinator's request. Albedo means are unchanged (crop means within 3 sRGB levels of before); every colour this pass adds is in `FieldPalette.cs`, as mixes of `FarDetailPalette.Means` or multipliers around them.

## What changed

**Margins (no more pale frame).** The splat's 2.2 m rough ring round every field is gone: the meadow runs to the crop and the strip between two fields is the same varied grass. Each parcel edge keeps its own use (`FieldEdge`), in the sector meshes and the painters alike:
- grass strip: pasture with damp grass, 1-2 m, wandering over 13-60 m;
- rough (Wide, 15% of non-hedge edges): browner and darker, 3-6 m;
- track: grass with two soil wheel lines;
- bare: crop right to the boundary (was a 0.35 m sliver).
Measured at the tiles' 2 m footprint round sectors 4 and 9 (`Review/sector-*.json`):
- the far painter's grass between parcels was pale dry meadow (122,124,87); it is now the local meadow (106,119,79);
- strips go from (119,122,83) to (102,116,74);
- rough margins go from (117,121,84) to (106,114,77), browner and darker than the meadow (107,120,80).

**Damp patches.** The hard-edged grey-green contour meshes in `FieldMargins` and the control map's switch to rough grass are gone. A damp hollow is the same crop, darker and a little greener (`DampCrop`), soft-edged and rare: about 4% of sector crop area (the sector's own hollows, `Ground.LowAt`), 0.5-2% in the painters (valley floors from the world heights, `FarDetailData.Low`). Wheat's light "lodged" patches went with them.

**Pasture.** Wetness follows drainage: hollows weigh 0.75, broad noise 0.3 (was 0.7, plus a fine noise term). Paddocks reuse the farm districts' strip slots (`District.Paddock`): grazed (brighter), hay meadow (paler) or rough grazing (browner, darker, with thistle patches), faded to a neutral fence line at each edge. 12-35 m grazing drifts are shared by the splat and the painters through world coordinates. Open-pasture luma spread at 2 m: 1.2-1.8 before, 4.1 after; below 64 m: 0.7-0.9 before, 2.6 after.

**Crops.** One hue and value per field, shared exactly by the sector material and the painters (`FarFieldPlan.Shade` from the parcel, registered per parcel in `FarFieldSurface`): wheat greener to riper, green crops bluish to yellowish, ploughland dark brown to grey-brown with a 0.4-1.3 km soil band, value ±12%. The old random sector tint (±20%, not shared) is replaced; its two random draws are kept so bales and hedge trees stay put. Growth drifts are two octaves (24 m and 11 m). Whole-field luma spread per crop: 0.9-2.0 before, 1.8-6.3 after.

**Stair-steps.**
- Sector splat: the field ring and the soil under crops thresholded `InField` per 1 m texel; they now feather a signed field distance from an 8 m index (four `InField` calls per texel became one lookup).
- The big 15-30 m staircases where crops meet open grass (hd162 `31_sector_04`, `31_sector_03`): `MapLayout.MapBlocked` clipped fields to the world cover's nearest 32 m sample. It now uses the smooth outline the painters already draw, `FarDetailCover.Farm`. This is a one-line change outside the brief's file list, made at the coordinator's request.
- Field pieces: a road or track crossing a whole field used to halve it into a staircase. `FarFieldSector.Cut` now:
  - splits along a fitted line through the crossing, or just short of a cross-road;
  - trims a slanting outline's slivers off a field's ends, so the slant becomes a fitted side;
  - steps a side back 0.65 m only where it was fitted to an obstacle.
- Margins: widths are taken at both ends of each 3 m step, so their inner edge no longer saws.
- Not changed: BC1 quantisation still dots thin lines slightly on the regional pages.

## Evidence

`Review/` holds top-down JPEGs, before and after, at the pixel size a 45 degree, 1080-line camera sees straight down from 300 m (0.23 m) and 600 m (0.46 m), plus 1 m overviews:
- `sector-*`: the sector as built. That means the terrain splat, Farmland's meshes with their tints and real detail maps (HDRP's overlay), `FieldMargins` and hedges. The `meadow` view is the most open pasture in the sector.
- `tiles-*`: the far painter at the 2 m tile lattice, bilinear.
- `layers-9-*`: tile, regional page (BC1 decoded) and world picture over one window.
- `timing.json` and `sector-*.json` hold the numbers.

All are albedo with a mild hillshade, and deterministic. The villages are not built in this harness.

The API compile passes for every changed file (C# 9, netstandard2.1, Unity 6000.6.7f1 modules, warnings as errors).

## Not verified (no Unity)

HDRP look, GPU time (no shader or material count changes, fewer margin triangles), streaming stages and the real sector build.

Managed harness, best of three alternating runs, sectors 4 / 9 (`Review/timing.json`):

| Stage | Before (ms) | After (ms) |
|---|---|---|
| Splat | 490 / 653 | 323 / 474 |
| Farmland build | 519 / 525 | 467 / 453 |
| FieldMargins (part of the Farmland build) | 51 / 52 | 17 / 14 |
| Layout | 233 / 273 | 236 / 274 |
| 512 m tile | 54 | 55 |
| Regional page (worker) | 185 / 192 | 205 / 216 |
| World 256-square block (worker) | 1293 / 1210 | 1325 / 1254 |

The smooth-outline blocked map came after that series. Single runs, not repeated, put it at 41 → 45 ms and 44 → 49 ms.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/TerrainColour/prepare.py              # API compile and harness
py -3.13 -I Tools/Tests/TerrainColour/prepare.py --before --harness
py -3.13 -I Tools/Tests/TerrainColour/run.py 4 9 --render       # add --phase=before for d3778ed
```
