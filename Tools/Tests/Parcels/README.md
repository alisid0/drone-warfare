# Parcel comparison harness

Run `./Tools/Tests/Parcels/run.ps1` in this worktree. It uses the installed Unity **SDK compiler only**, never the editor or player, and adds no packages. Override `-Dotnet`, `-Python` and `-Output` on other machines. Outputs go to the requested `dwprev/codex/parcels` folder; generated projects stay under ignored `Temp/parcels`.

The baseline is read directly from `61757a3`. Both versions execute the complete production `SectorGenerator`, `Sector`, `PathField`, `MapLayout` and `Farmland` sources. Only engine math/graphics and other builders are substituted. An independent compilation uses the actual installed Unity 6000.6.7f1 CoreModule reference assembly and C# 9, with warnings treated as errors.

Seeds: Varrow's hand-authored plan (world 7 / fields 11); RiverValley 1000 and Marshland 1074 match AutoTest's sector tour. VillageCluster is absent from that tour: 1555 is the explicitly chosen reproducible sample (`1000 + 37 * SectorKind.VillageCluster`). DeepForest 1037 is an extra low-share regression. The checked-in Marshland recipe is **0.15**, not 0.3; the planner respects the generated `Plan.fields` value.

## Measurement limits

- Native `Mathf.PerlinNoise` cannot execute outside Unity. A fixed reference Perlin substitute is used identically before and after. Seeds and placement code are real; geometry and numbers are approximate, not a claim to reproduce the engine's 533-field Varrow exactly.
- Coverage is the existing `FieldCoverage()` survey: the central 1250 m square, excluding water, village circles and sites. It includes existing orchards. District occupancy separately divides crop area by sampled usable ground inside selected districts, including unplanted margins and pasture.
- Parcel areas and matching use geometry before the gentle warp. Shared-edge percentages are **length weighted**, handle partial overlaps/T-junctions, and allow a 2.3 m baulk tolerance. The report includes all perimeter edges as well as internal edges. Internal sampling excludes district exteriors and obstacle-facing edges within 12 m of the blocked mask; the baseline has no district model, so its interior survey uses the central 1250 m square. These definitions are deliberately exposed rather than treating all outer edges as shared.
- Baseline boundary counts mean field edges with an actual successful hedge placement; other field edges are open. They may double-count a neighbour's corresponding edge, just as the old builder did. Candidate counts mean unique shared edges; `Fence` includes the continuous low bank plus whatever existing fence panels its allowance buys. These are different ownership models.
- Renderer counts execute the real budget paths, including both hedge LOD renderers. Model costs use the existing source/model audit: trees have three renderers, fences/bales/bushes one. The stub emulates 3.2 m tree spacing, but cannot inspect Unity's imported prefab variants or global scenery state. Counts are standalone estimates; the production code uses `Art.ModelRendererCount` for actual assets.
- Layout time is the median of three `Prepare` calls; the cold first call is also retained in JSON. Farmland CPU time executes geometry/detail-texture math, but stubs terrain sampling, asset loading, GameObject creation, uploads and GPU work. Neither timing proves the sector's 15-second Unity budget.

## Checks

The harness checks repeatable field geometry/crops; empty-sector reset; indexed `InField` against exhaustive queries at margins 0, 2.2 and 5 m; sampled field non-overlap; sampled road, standing-water, village and site clearance; minimum nominal 500 m2 area; shared-edge owner references and duplicate removal; mesh index/winding sanity; far hedge shadows; renderer cap; and that the candidate Farmland leaves the supplied shared random stream untouched.

The old builder is instrumented only to observe successful hedge runs. This does not add random draws or alter placements. API stubs are isolated to this harness and are never included in the Unity Assets folder.

## Integration notes

No Ground, World, Art, Atmosphere, HUD or gameplay file is changed. Ditches are thin mesh damp lines, so no terrain-splat edit is needed. `FieldParcels.PlanningHeight` mirrors the **broad** part of `Ground.Hills` from 61757a3 because terrain does not exist when layout runs; if that relief recipe changes, keep the predictor aligned. It intentionally omits metre-scale roughness and uses a 16 m slope grid at approximately 15 degrees. It is not an exact final-terrain slope test.

Woods have no pre-layout footprints: World grows them later on `IsWild` ground. Whole unfarmed districts and connected pasture reserves preserve that ground; the planner cannot follow individual future wood outlines without a shared forest plan from World.

Farmland now owns a seeded stream (`worldSeed ^ 0x31a7`). This changes the old villages/woods random sequence once at integration; thereafter field count, textures and boundary-detail changes cannot advance the stream those builders share. Preserving the old sequence exactly would require replaying the removed planner and all old prop placement decisions.

Hedge shadow distance follows the 45-degree play camera and lodBias 1, like Art's existing tree LOD tuning. Photo-camera FOV and quality LOD bias can move that transition. Close-up hedge material appearance, terrain contact, imported model renderer totals and the full sector build time still need Claude's Unity review.

Unity API source checked for the new LOD usage: [LOD bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/LOD.bindings.cs) and [Mesh implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Graphics/Mesh.cs), in addition to the installed assembly compilation and existing Art/Farmland/World call sites.
