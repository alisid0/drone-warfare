# Forest and settlement HLOD handoff

Branch: `codex/forest-hlod`, based on `fc81c6f`. Unity was **not** opened or run.

The far forest now uses shared deterministic stands: LOD1 photo meshes within about 120 m, LOD2 crown cards to 700 m, then opaque canopy masses. Species, age, width and three gentle tint variants follow 30 m jittered stands. Glades and 15–35 m inward edge feathering affect both trees and canopy; margins favour birch. Camera distance includes altitude. Instances batch across cells, with a 511-instance limit for non-uniform crown scales. Both tree tier transforms are cached at cell creation; all nine after-view geometry captures stayed byte-identical after this optimization.

Canopy blocks are 2,048 m square, sampled at 16 m, with 64/128 m distant mesh LODs. Interior sample positions and the photo quilt are warped to avoid a visible lattice; shared block edges remain aligned. Heights combine stand age, crown bumps, glades and skirts to ground; normals come from the resulting surface. A normalized quilt derived from the existing top-view impostor photographs supplies crown detail. The palette uses ProjectSetup's exact impostor tints, measured crown coverage, dark ground gaps and a measured oblique-view correction. There is no alpha testing or shadow casting on the canopy. Row-by-row construction gets a 0.7 ms CPU slice, followed by a native mesh/texture upload; that upload is not time-capped.

Settlements use road-aligned 120 m blocks, 28 m plot spacing, 3–9 m setbacks and overlap rejection. The same plots become individual photo-material houses inside 1.5 km or 12/14-triangle roof/wall shells beyond it. Far blocks are combined into one mesh per settlement, with index ranges removed when their individual houses appear.

## Review artifacts

- [Forest before/after: above, 300 m, 2.5 km, 9 km](Review/forest-comparison.png)
- [Town before/after at the same four views](Review/town-comparison.png)
- [Near tree mesh check](Review/forest_near_after.png)
- [Matched-camera card/canopy luminance comparison](Review/calibration-comparison.png)
- [Derived crown atlas](Review/crown-atlas.png)
- [All per-tier counts and triangle/draw submissions](Review/counts.md)

Each comparison panel is 1600×900. These are **offline Blender renders, not Unity/HDRP captures**. The harness executes the current production scatter/mesh generators and the actual `fc81c6f` FarScatter source. Managed engine storage/submission substitutes and a conservative frustum replace Unity native calls. Model dimensions, materials and triangle counts come from the repository FBXs, including the two older houses without LOD groups. Blender uses those FBXs and project photo textures. The terrain is a sampled WorldPlan surface with the same baseline painting recipe on both sides; native Perlin noise, HDRP lighting, exposure, fog, mip coverage and occlusion are not reproduced exactly.

The review forest is seed 1 at `(16500, -4500)`, safely inside the world for the 9 km view. The town is seed 1's largest town. The new autotest shot independently finds the largest connected forest district on WorldPlan's kilometre grid.

## Validation and budget

The C# 9 assembly check passes with **zero errors and warnings** against installed Unity 6000.6.7f1 CoreModule and PhysicsModule. It includes the complete changed/new rendering sources, WorldPlan and the exact `WorldCheck` method extracted from AutoTest. The remaining game services are small compile fixtures; this is not a complete player build.

Three-seed execution checks pass: glades cover 8.10–8.23% of sampled woodland; sampled stand heights span 9.1–23.1 m within the 8–24 m limits; adjacent equal-resolution block heights agree within 1 mm; normals are finite and varied; generated roof faces are non-degenerate; sampled settlements have no overlapping proxy footprints. Repeat tree sampling, native-resource cleanup paths and origin changes are exercised. The near-flight check submits **258 real mesh trees and 2,787 cards**.

At a fixed 600 m camera range and 60° pitch, the central matched-camera ROI measures **−2.67% linear luminance** for the canopy against the same stand's cards. This meets the 10% target in the offline harness. Confirm it in HDRP with fixed exposure.

| View | Before | After |
|---|---|---|
| Forest, 300 m | 88,501 cards; 1,901 draws; 708,008 tris | 3,263 cards; 20 draws; 157,856 tris |
| Forest, 2.5 km | 76,835 cards; 1,598 draws; 614,680 tris | 0 trees; 10 canopy blocks/draws; 240,484 tris |
| Town, 2.5 km | 2,692 houses + 403 cards; 2,953 draws; 2,327,320 tris | 0 instances; 186 town blocks + 2 forest blocks; 3 draws; 24,742 tris |
| Forest region, 9 km | 0 scatter draws | 37 mass draws; 20,288 tris |
| Town region, 9 km | 0 scatter draws | 19 mass draws; 30,318 tris |

Counts are API submissions before GPU clipping, excluding terrain and the detailed sector. HDRP depth and other passes can multiply backend draw work. Full data is in `Review/metrics.json`.

**Estimated GPU allowance, not measured:** roughly 0.2–0.6 ms for the 2.5 km forest masses, 0.05–0.2 ms for the town masses, and 0.1–0.3 ms for the macro masses at 1600×900 on the target RTX 3050 class hardware. These are rough planning ranges; profile the actual HDRP passes. A fine block holds about 17k vertices, at most 33.8k triangles, a 512² mipmapped texture and CPU data; allow roughly 4 MB per populated fine block including readable mesh/copies. Distant blocks are much smaller.

**The 97 fps wide / 85 fps sandbox gates are unverified.** The baseline already suppresses individual scatter at 9 km. The new macro geometry adds GPU work there, despite lower managed submission overhead; a speedup cannot be claimed. Static managed harness timings exclude native uploads/rendering and are not game frame timings.

## Required integration and build checklist for Claude

1. Keep the single `FarScatter.GroundAt(Vector2 local)` integration point. Redirect it to the other session's `FarDetail.HeightAt` when that work is merged; canopy and settlement placement already call through it.
2. **Preserve flight height:** `Ground.HeightAt` currently calls `FarTerrain.SurfaceAt`. Both `SurfaceAt` and `Elevation` remain unchanged. The new `VisualSurfaceAt` hook removes the smooth shell only for mesh builders, and FarTerrain chunks already use it.
3. **Legacy Backdrop ring needs coordination:** its existing builder still calls the flight-height method. Apply [the prepared one-line Backdrop integration patch](backdrop-integration.patch), or the equivalent in FarDetail's replacement builder, so that visible ring also uses `VisualSurfaceAt`/the shared bare-ground source. `Backdrop.cs` was outside the permitted files, so this patch is supplied but **not applied**. Until then, the old smooth canopy can still obscure near trees in the 1.5 km join ring. Set `ForestCanopyHlod` before rebuilding visual terrain.
4. Compile/build the merged project, then run WorldCheck. It logs mesh trees, cards, houses, forest/town blocks, triangles, draw submissions and forest build time, and adds `88_world_forest_hlod` at a 2.5 km camera range over the largest forest district.
5. Compare identical 1600×900 settings/cameras against the 97/85 fps baselines. Record steady GPU time and activation/upload spikes, including during a continuous 40 m/s crossing. Check the 120 m tree switch, 560–700 m canopy handover, 1.5 km town handover and 3.2/8.5 km canopy mesh LOD boundaries. Mesh/card and house/proxy switches are discrete; there is no temporal dithering shader in this change.
6. Inspect fixed-exposure luminance at the handover, road-facing blocks, glade/edge skirts and terrain rebases. Retain the existing Scout/Swift clearance checks. Verify a sector change releases the old meshes/textures and does not leave duplicated scenery.

## Reproduce without Unity

Run `Tools/ForestHlod/run.ps1`, or add `-SkipRender` for API/geometry/count checks only. It requires the already-installed Python, Blender and Unity-bundled .NET SDK; it adds no packages and writes intermediate data to `Temp/forest-hlod`. The installed Python needs permission to execute from this sandbox.

If source FBXs, impostor photos or tints change, first run `inspect_models.py` with Blender headlessly, then `python -I Tools/ForestHlod/make_palette.py`. The latter regenerates `ForestPalette.cs`, `ForestCrown.bytes` and the atlas. `run.ps1` intentionally does not regenerate production assets. Individual render PNGs are reproducible and ignored; the committed full-resolution sheets retain every requested view.

API behavior was also checked in Unity's primary source: [Graphics/RenderParams](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/Graphics.cs), [Mesh](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/Mesh.cs), and [GeometryUtility](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Geometry/GeometryUtility.cs). The installed assemblies are the version-specific compile authority.
