# World sectors handoff

`WorldSectors.PlanAt(center, kindHint, seed)` cuts a 2 km square, with a 128 m vector margin, from the already generated world. `SpotFor(hotspot)` allocates non-overlapping campaign squares in stable north-to-south roster order. Both are deterministic. Generate WorldPlan before timing crops; a cold call can generate `World.WorldSeed` itself. Varrow remains on its separate hand-made path.

The optional `WorldSectorData world` is now consumed by MapLayout, Ground, Water, World.BuildWoods, FieldParcels and campaign loading. See the [consumer build handoff](CONSUMERS.md), [six consumer renders](consumer-renders/comparison-sheet.png) and [consumer measurements](consumer-validation.json). Legacy null-world branches remain in place.

## What the adapter supplies

- World roads/tracks, with their original classes and crossings. Splitting at the terrain boundary fixes the ends through MapLayout's existing Smooth/Wander. Collinear guard points prevent uniform Catmull-Rom overshoot at tiny clipped end segments. Railways are also cropped from the world.
- All disconnected river reaches, downstream ordered, with interpolated half-widths and absolute levels. `plan.river` is the longest in-square reach for old consumers; `plan.riverHalfWidth` is the maximum retained width for conservative old exclusions. **A confluence cannot fit in that one legacy polyline.** `world.RiverLines()` supplies separate, linearly sampled reaches for PathField.
- Exact local bridge endpoints, deck elevations, route IDs and railway flags in `world.bridges`; road bridge centres inside the square also populate `plan.bridges`.
- Complete local shoreline polygons of intersecting lakes, preserving water levels, outlets and reservoir flags. Keeping the shore beyond the crop avoids inventing a shore along the crop boundary. Exact dam geometry is in `world.dams`. Legacy `hasDam` stays false: its contract assumes the dam is river[0] and would carve a fictitious gorge in an upstream crop. Inscribed legacy ponds are compatibility approximations, not the authoritative lake shape.
- Settlement centres, names and farm positions; occupied town blocks also come from actual town cover, including when the settlement centre is outside the crop. Town cover selects MarketTown/TownOutskirts where appropriate.
- Area-weighted forest/field shares, world-aligned forest rectangles (`world.woods`), and exact nearest-sample `CoverAt`. The optional data owns a 32 m height/cover/water snapshot, aligned with the global grid. `Height` reproduces WorldPlan's bilinear ground height and survives regeneration of the global world.
- The existing generator's mission skeleton: base, HQ, defence identities and site models. Suitable positions stay put; unsuitable positions move to dry, low-relief pads, clear of world roads and village centres. HQ defences retain their compound offsets where safe. A local route tree reaches the HQ, crosses water only at exported road bridges and connects reachable world fragments. These access tracks end inside the sector, so they add no world-edge crossings.

No dry footprint or no dry base/HQ connection produces an explicit InvalidOperationException, not a base under water. Invalid/out-of-world centres are rejected. PlanAt deliberately rejects VarrowValley; use the existing Varrow plan and dam anchor. Special feature models still come from the mission recipe; this does not promote every installation into a persistent world entity.

SpotFor reserves the hand-made Varrow square too. Exact cell-kind matches take priority; named fallback families cover kinds WorldPlan does not classify (e.g. LoggingCamp â†’ DeepForest/ForestEdge, IndustrialZone â†’ RailLine). Within the best available kind it minimizes displacement from the theatre position and desired front depth. Rare airfields may consequently lie well behind the front. Cell centres already meet the 250 m snap. Allocation reads the complete current Theatre.Hotspots roster, never remembers call order, and removes current Advance from the baseline so a victory does not move an existing hotspot.

## Consumer integration status

The previously required consumer hooks and five-step Ground branch are implemented. The exact water queries drive placement; river reaches retain their breaks; standing water uses complete world shorelines; woods and fields use world cover. Ground stores absolute elevations and fades all local shaping back to world height at the square boundary. Campaign loading uses the same allocated centre for FarTerrain and PlanAt, with a generated-sector fallback.

[CONSUMERS.md](CONSUMERS.md) records implementation details, native Unity checks still needed, all six seed-1200 crop coordinates, performance limits and validation results. World bridge models are built in Water; MapLayout suppresses only the legacy rendering list to avoid duplicates while retaining `plan.bridges` for mission routing.

## Varrow orientation

The requested placement `world dam centre - SectorPlan.Varrow().damPoint` is untouched. Varrow's immediate lake basin runs along negative DamDown; its second lobe bends toward positive DamAcross. Seed 1200's global reservoir runs in a different direction: aligning Varrow's second-lobe vector to the global dam-to-inlet vector would rotate Varrow approximately **100.43 degrees clockwise in X/Z**. This is an orientation measurement, not a proposed change to the agreed translation-only placement.

To align the **world** to the unchanged Varrow, constrain Venn's reservoir reach during PlanDrainage: keep the dam centre, give its downstream tangent Varrow's DamDown, and route the upstream arm toward `DamDown * lakeLobes[1].center.x + DamAcross * lakeLobes[1].center.y`. Rebuild MakeLake's shore/dam endpoints from that constrained reach before CarveDrainage/CarveLakes, then regenerate downstream roads, bridges and cover. A shore-only rotation after generation would detach the river, beds and bridge approaches. Rotation alone also cannot make the 4 km world reservoir match Varrow's shorter two-lobe shore and narrower dam. Exact seam agreement needs a deliberately shaped Varrow footprint in the global plan, continued upstream outside its square.

## Validation and renders

The current consumer validation is in [CONSUMERS.md](CONSUMERS.md), [comparison sheet](consumer-renders/comparison-sheet.png), [measurements](consumer-validation.json) and [legacy regression hashes](legacy-consumer-validation.json). These are harness diagrams of actual consumer output, not Unity screenshots.

The earlier adapter-only [validation](validation.md), [renders](renders/comparison-sheet.png), [measurements](validation.json) and [temporary water-hook probe](water-hook-probe.json) remain as historical evidence from before the consumers were integrated.

Run from this worktree, without opening Unity or installing packages:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WorldSectors/run.ps1
```

Use `-CheckOnly` to skip PNG rendering. Python with `-I` still prepares the harness. Generated projects, baseline copies and raw render snapshots live under ignored `Temp/world-sectors`. Production Ground, Water, World and planning sources compile directly against the installed Unity assemblies with C# 9 and warnings as errors. Executable checks use managed engine storage and the existing reference noise because native engine functions cannot execute outside Unity. The final run also compares legacy outputs with `1016ddc` and checks the copied campaign loading methods.
