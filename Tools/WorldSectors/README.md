# World sectors handoff

`WorldSectors.PlanAt(center, kindHint, seed)` cuts a 2 km square, with a 128 m vector margin, from the already generated world. `SpotFor(hotspot)` allocates non-overlapping campaign squares in stable north-to-south roster order. Both are deterministic. Generate WorldPlan before timing crops; a cold call can generate `World.WorldSeed` itself. Varrow remains on its separate hand-made path.

The only SectorPlan addition is `WorldSectorData world`. No protected production consumer was changed. **Integration must read this field for water, woods and heights; calling PlanAt alone is not a finished world-backed renderer.**

## What the adapter supplies

- World roads/tracks, with their original classes and crossings. Splitting at the terrain boundary fixes the ends through MapLayout's existing Smooth/Wander. Collinear guard points prevent uniform Catmull-Rom overshoot at tiny clipped end segments. Railways are also cropped from the world.
- All disconnected river reaches, downstream ordered, with interpolated half-widths and absolute levels. `plan.river` is the longest in-square reach for old consumers; `plan.riverHalfWidth` is the maximum retained width for conservative old exclusions. **A confluence cannot fit in that one legacy polyline.** `world.RiverLines()` supplies separate, linearly sampled reaches for PathField.
- Exact local bridge endpoints, deck elevations, route IDs and railway flags in `world.bridges`; road bridge centres inside the square also populate `plan.bridges`.
- Complete local shoreline polygons of intersecting lakes, preserving water levels, outlets and reservoir flags. Keeping the shore beyond the crop avoids inventing a shore along the crop boundary. Exact dam geometry is in `world.dams`. Legacy `hasDam` stays false: its contract assumes the dam is river[0] and would carve a fictitious gorge in an upstream crop. Inscribed legacy ponds are compatibility approximations, not the authoritative lake shape.
- Settlement centres, names and farm positions; occupied town blocks also come from actual town cover, including when the settlement centre is outside the crop. Town cover selects MarketTown/TownOutskirts where appropriate.
- Area-weighted forest/field shares, world-aligned forest rectangles (`world.woods`), and exact nearest-sample `CoverAt`. The optional data owns a 32 m height/cover/water snapshot, aligned with the global grid. `Height` reproduces WorldPlan's bilinear ground height and survives regeneration of the global world.
- The existing generator's mission skeleton: base, HQ, defence identities and site models. Suitable positions stay put; unsuitable positions move to dry, low-relief pads, clear of world roads and village centres. HQ defences retain their compound offsets where safe. A local route tree reaches the HQ, crosses water only at exported road bridges and connects reachable world fragments. These access tracks end inside the sector, so they add no world-edge crossings.

No dry footprint or no dry base/HQ connection produces an explicit InvalidOperationException, not a base under water. Invalid/out-of-world centres are rejected. PlanAt deliberately rejects VarrowValley; use the existing Varrow plan and dam anchor. Special feature models still come from the mission recipe; this does not promote every installation into a persistent world entity.

SpotFor reserves the hand-made Varrow square too. Exact cell-kind matches take priority; named fallback families cover kinds WorldPlan does not classify (e.g. LoggingCamp → DeepForest/ForestEdge, IndustrialZone → RailLine). Within the best available kind it minimizes displacement from the theatre position and desired front depth. Rare airfields may consequently lie well behind the front. Cell centres already meet the 250 m snap. Allocation reads the complete current Theatre.Hotspots roster, never remembers call order, and removes current Advance from the baseline so a victory does not move an existing hotspot.

## Required consumer hooks for Claude

Keep all old branches when `plan.world == null`. The harness includes a **temporary-copy integration probe**, with no edit to the real MapLayout. It verifies these two exact substitutions:

```csharp
// MapLayout.Prepare: the first PathField constructor argument.
p.world != null ? p.world.RiverLines()
    : (p.river != null ? new[] { RiverLine } : new List<Vector2>[0])

// MapLayout.LakeDistance:
public static float LakeDistance(float x, float z) => Plan.world != null
    ? Plan.world.LakeDistance(new Vector2(x, z), out _)
    : Mathf.Min(ReservoirDistance(x, z), PondDistance(x, z, out _));
```

Those are necessary placement hooks: with unchanged consumers the six sample plans generate 170 fitting situations without exceptions or two-point convoy fallbacks, but six stationary reservoir placements fall in the exact lake outside the inscribed legacy ponds. With the two query hooks, all 170 situations have **zero stationary units over exact river/lake water** in the harness. This is evidence for the query integration, not a substitute for Claude's Unity autotest or all possible seeds.

The rendering hooks additionally needed:

1. Use `world.RiverDistance(q, out width, out level)` for local width/absolute surface level. Do not shrink that width with RiverWidthAt's old noise at the edge. Preserve each line break in Water.RiverMesh and its tangents/UV distance: the current mesh joins every successive field point and would otherwise draw a false ribbon between reaches. Populate river field values as described below.
2. Build each standing-water surface over its shoreline bounds at `WorldLake.level`, selecting grid cells with the signed polygon distance. Existing PondMesh loops approximate ponds and cannot acquire exact shorelines just from the LakeDistance hook. Route WaterLevelAt to the exact query too. Use `world.dams` and `world.bridges` for orientation, span and absolute elevations; the old dam model and single river-width bridge builder cannot infer these.
3. In World.BuildWoods, use `world.woods` or a world-aligned candidate lattice filtered by `world.CoverAt(x,z) == Cover.Forest`, retaining existing spacing, tree budget, clearing and site exclusions. Merely changing `plan.forest` still puts random woods in the wrong places. FieldParcels should reject parcels/samples outside `Cover.Farmland`; its scalar fields share is supplied, but is not a spatial mask. Road classes in `world.roads` are available for highway/track widths; current MapLayout uses one width.
4. In PlayHotspotSoon, generate the world, get the centre from SpotFor, set FarTerrain's origin, and call PlanAt with that same centre before LoadSector. Keep the existing separate Varrow path. Do not call ScaleLayout on a world crop: it is already in 2 km local metres.

## Exact Ground.cs work

Ground's current height path is incompatible in four places: it invents broad Hills/ridges, multiplies ground by FlatMask to flatten pads to zero, recomputes water levels from those hills, and clamps absolute heights into the old fixed `[-Sink, HeightRange-Sink]` vertical range. Changing hills/ridges amplitudes alone cannot fix the relief.

For a world-backed plan, make these changes in Ground; retain every existing branch for non-world plans:

1. In Build, after copying MapLayout.FlatSites to `sites`, copy each struct back with `center.y = plan.world.Height(center.x, center.z)`. Pads must flatten to their own absolute regional elevation. IndexSites can stay unchanged.
2. In the first BuildHeights parallel loop, replace `Hills(...) * FlatMask(...)` with `source.Height(x,z)` plus only the existing real-size roughness terms. Evaluate noise using `wx = x + source.center.x`, `wz = z + source.center.y`. Apply pad flattening by interpolating toward the controlling site's `center.y` with its existing radius/blend mask, instead of multiplying the whole elevation by that mask. Concretely, reuse FlatMask's indexed-site loop, retain the smallest `m` and that site's Y, then return `Mathf.Lerp(siteY, h, mask)` (return `h` when there is no influencing site).

```csharp
var source = MapLayout.Plan.world;
float wx = x + source.center.x, wz = z + source.center.y;
float detail = (Mathf.PerlinNoise(wx * 0.024f + 17.3f, wz * 0.024f + 63.1f) - 0.5f) * 1.6f
             + (Mathf.PerlinNoise(wx * 0.09f + 5.5f, wz * 0.09f + 91.2f) - 0.5f) * 0.6f;
float h = source.Height(x, z);
// Suppress detail on water; banks and beds are already present in the macro height grid.
if (source.DryDistance(new Vector2(x, z)) <= 0f) detail = 0f;
world[zi, xi] = FlattenToSiteElevation(x, z, h + detail);
```

`FlattenToSiteElevation` is the small helper described above, not an existing method. The final edge blend below applies once to all local changes, including detail and pads.

3. Skip Reservoir, Ponds and BelowDam in this branch: WorldPlan already supplies their basin/gorge and fixed levels. Keep local road grading, railway beds, Quarry and Trench; their working values are already absolute when sampled from `world`. For each `MapLayout.RiverField.points[i]`, set `river.values[i]` from `source.RiverDistance(point, out _, out level)`. Skip the old lowest-bank derivation, SmoothValues and MakeFalling for world rivers. Their copied levels already fall independently downstream; applying MakeFalling across unrelated reaches is wrong. Initially skip the old river recarve too. If adding sub-grid channel detail later, use each reach's width/level and the same edge fade.
4. Immediately before assigning `worldHeights`, computing Slopes and normalizing, apply this to **every** final sample. This makes the terrain's actual boundary equal the world ground, after all local earthworks:

```csharp
world[zi, xi] = Mathf.Lerp(source.Height(x, z), world[zi, xi], source.DetailWeight(x, z));
```

`DetailWeight` is 0 at ±1000 m and 1 from 100 m inside the boundary. Compare against WorldPlan.Height, not FarTerrain.Elevation, which includes a forest canopy above the ground.

5. Replace fixed vertical normalization for this branch. After all shaping/edge blending, scan actual `world` min/max. Set `terrainBottom = min - 8f` and `terrainRange = Mathf.Max(32f, max - terrainBottom + 8f)`. Normalize as `(world[z,x] - terrainBottom) / terrainRange`. BuildHeights must run **before** assigning `data.size`; then assign `data.size = new Vector3(Size, terrainRange, Size)`, SetHeights, and place the Terrain at `(-Size/2, terrainBottom, -Size/2)`. For old plans use `terrainBottom = -Sink`, `terrainRange = HeightRange`. Do not subtract the sector centre height from world heights: FarTerrain uses absolute world Y.

These changes are scoped to Ground's initial surface, pad targets, water branch, final edge blend and vertical storage. Ground.HeightAt, NormalAt, slope sampling, roads, collider, splat painting and the old Varrow behavior can retain their existing APIs. Runtime terrain, water meshes and canopy blending still need Claude's render/play validation.

## Varrow orientation

The requested placement `world dam centre - SectorPlan.Varrow().damPoint` is untouched. Varrow's immediate lake basin runs along negative DamDown; its second lobe bends toward positive DamAcross. Seed 1200's global reservoir runs in a different direction: aligning Varrow's second-lobe vector to the global dam-to-inlet vector would rotate Varrow approximately **100.43 degrees clockwise in X/Z**. This is an orientation measurement, not a proposed change to the agreed translation-only placement.

To align the **world** to the unchanged Varrow, constrain Venn's reservoir reach during PlanDrainage: keep the dam centre, give its downstream tangent Varrow's DamDown, and route the upstream arm toward `DamDown * lakeLobes[1].center.x + DamAcross * lakeLobes[1].center.y`. Rebuild MakeLake's shore/dam endpoints from that constrained reach before CarveDrainage/CarveLakes, then regenerate downstream roads, bridges and cover. A shore-only rotation after generation would detach the river, beds and bridge approaches. Rotation alone also cannot make the 4 km world reservoir match Varrow's shorter two-lobe shore and narrower dam. Exact seam agreement needs a deliberately shaped Varrow footprint in the global plan, continued upstream outside its square.

## Validation and renders

See [validation.md](validation.md), [comparison sheet](renders/comparison-sheet.png), [raw measurements](validation.json), and [water-hook probe measurements](water-hook-probe.json). The diagrams compare direct WorldPlan queries with the returned SectorPlan and its optional world inputs; they are not Unity screenshots and do not claim the unmodified woods/water renderer already consumes the new inputs.

Run from this worktree, without Unity or packages:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WorldSectors/run.ps1
```

Use `-CheckOnly` to compile/check without Python. All generated projects, raw render snapshots and the temporary MapLayout probe live under ignored `Temp/world-sectors`; production files are compiled directly. The executable harness uses the existing managed math substitutes because native PerlinNoise cannot run outside Unity. A separate compile checks production planning sources against the installed UnityEngine.CoreModule assembly with C# 9 and warnings as errors. The Python renderer uses `-I` and installed Pillow. It does not generate or edit photographic assets.
