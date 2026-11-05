# World sector consumers: build handoff

Implemented on `codex/world-sector-hooks`, starting at `1016ddc`. Unity was not opened or run.

The campaign now allocates a world centre, sets FarTerrain's origin, and loads the corresponding crop. The reuse check includes the centre and world seed. Varrow retains its separate spot. An unavailable dry footprint falls back to the old generated plan and old spot, with one warning per session.

## What changed

- MapLayout uses disconnected world river lines, exact lake distance/level queries and world river widths. Prepared road segments retain half-widths of 3.5 m for highways, 2 m for roads and 1.5 m for tracks/access lanes. Dam exclusions use world dam endpoints.
- Ground uses absolute snapshot heights, world-coordinate roughness, each pad's own elevation and the existing controlling-site rule where pads overlap. World basins and channels are retained. Road/rail grading and local quarry/trench work precede the final edge fade. Actual min/max determine vertical storage and terrain placement.
- Water builds one mesh per river reach with interpolated widths/levels and independent tangents/UV distance. Boundary triangles are clipped with their heights/UVs. Each world lake has its own shoreline-grid surface and absolute level. Existing models span the world bridge/dam endpoints at their deck/crest levels. Rail bridges currently use the existing road bridge model.
- World.BuildWoods uses a world-aligned candidate lattice filtered by forest cover, retaining spacing, clearings, exclusions and budgets. The riverbank pass also respects reach breaks and forest cover. Other vegetation builders are unchanged.
- FieldParcels uses world farmland cover with a 5 m sample guard and predicts slopes from world heights.

`MapLayout.Bridges` is the legacy **rendering** list and is empty for world plans, preventing World.Build from making duplicate bridges. The adapter's `plan.bridges` remains intact for routing and placement. World.cs was edited only inside BuildWoods; Reservoir.cs and FarTerrain.cs are unchanged.

## Evidence

See the [six-crop comparison](consumer-renders/comparison-sheet.png) and [measurements](consumer-validation.json). Individual sheets:

| Crop | Centre (world X, Z) | Kind hint / sector seed | Review |
| --- | --- | --- | --- |
| River crossing | 2750, -4000 | RiverCrossing / 7100 | [sheet](consumer-renders/river-crossing.png) |
| Town edge | -13000, 17250 | TownOutskirts / 7101 | [sheet](consumer-renders/town-edge.png) |
| Forest | 3500, -6500 | DeepForest / 7102 | [sheet](consumer-renders/forest.png) |
| Reservoir | 10250, 15000 | Reservoir / 7103 | [sheet](consumer-renders/reservoir.png) |
| Farmland | 3500, -9500 | RollingFarmland / 7104 | [sheet](consumer-renders/farmland.png) |
| Confluence | -12000, 20000 | RiverValley / 7105 | [sheet](consumer-renders/confluence.png) |

- Maximum terrain boundary error is **0.0453 m**, against a target of 0.5 m. Measurements reconstruct stored normalized terrain heights at every terrain/world-grid knot, plus quarter-interval samples. Both edge profiles are piecewise linear, so the knots include the possible error maxima. The six sheets show the full 8 km perimeter. Comparison is against WorldPlan ground, not FarTerrain's canopy surface.
- No vertical clipping; normalization round-trip error is at most 0.000004 m in managed storage. Isolated pads reach their absolute targets; overlapping pads retain the documented controlling-site rule.
- All **170 fitting situations** generate successfully, with zero stationary units over exact water and zero two-point convoy fallbacks. Adapter determinism, roster allocation, all 29 non-Varrow kind hints and snapshot ownership checks remain enabled.
- Water vertices stay inside the square, triangles face up and each reach mesh remains within its own width. Bridge midpoint/span/deck/orientation and dam midpoint/crest/downstream orientation checks pass.
- All 16,228 woodland placements in these six crops stay in forest cover, with the shared tree limit respected. All 1,271 accepted parcels stay in farmland at 1 m validation spacing.
- Copied production LoadSector and PlayHotspotSoon bodies pass reuse, changed-centre, world/generated transitions, generation/origin call order, Varrow and repeated dry-footprint-failure tests. The exception is injected through a test service; production code has no test switch.
- Against `1016ddc`, Varrow plus generated RollingFarmland, Reservoir and DeepForest have identical hashes for normalized height data, terrain placement/range, water geometry, woodland model positions and parcel geometry. See [legacy comparison](legacy-consumer-validation.json).
- Complete Ground.cs, Water.cs, World.cs and planning sources compile directly against installed Unity 6000.6.7f1 CoreModule, TerrainModule, TerrainPhysicsModule, PhysicsModule and AnimationModule assemblies, with C# 9 and warnings as errors. The copied campaign methods compile there too. HDRP WaterSurface properties and adjacent game services use stand-ins; no new HDRP property API was introduced.

## What Claude must verify in Unity

1. **Compile and run the autotest after merging.** The harness cannot execute native TerrainData upload, TerrainCollider, FBX imports or HDRP. Inspect the native height range and placement in the elevated reservoir crop (roughly Y=145 to 211 m) and confirm bases/units remain on their pads.
2. **Inspect all six crop edges from an oblique camera.** Look for cracks, exposed terrain skirts, canopy steps or a visible Backdrop transition. FarTerrain and Backdrop's existing radial blend and forest canopy were retained. The numeric seam check covers the detailed terrain against world ground; it does not render that distant mesh/texture transition.
3. **Inspect water in the confluence, reservoir and town-edge crops.** Both confluence reaches must render independently, with downstream flow and no diagonal ribbon between endpoints. Lakes must meet their actual shores and continue to the terrain edge without legacy pond duplicates. Check HDRP custom meshes at different absolute elevations. Check narrow channels for terrain occlusion: the requested world branch retains the 32 m macro bed and intentionally does not recarve it locally.
4. **Inspect structure contacts.** Bridge decks should meet bank approaches with the correct span/yaw, including any rail crossings. The reservoir dam should span its banks, with its crest at the exported elevation and its spillway facing downstream. Models are scaled approximations; native renderer bounds, collider contacts and animated spillway effects are not validated here. The world dam does not acquire Reservoir's legacy spray/lights/weathering setup.
5. **Inspect forest and field rendering.** The forest crop must fill its actual cover while retaining the mission clearing. Farmland must stop at cover boundaries, roads, water and sites. Verify native imported tree spacing/LODs and Farmland's crop meshes at the new absolute elevations.
6. **Measure full player load time.** After initial world generation, test these six crops and compare generated sectors on the same machine: target no slower than the existing 8–13 s, and under 15 s. Offline PlanAt + Prepare + Ground + Water + BuildWoods totals were about **0.58–1.70 s** per crop; Ground alone was 0.24–0.96 s. These exclude native uploads/HDRP, far terrain, Farmland meshes, villages, features, grass and bases, so they do not certify the player budget. Use World.Timings to identify any regression.
7. **Exercise transitions in play.** Load a campaign hotspot, return to a Varrow operation, and reopen the hotspot. Verify same kind/seed at another centre rebuilds, identical centre reuses, and a forced InvalidOperationException from PlanAt restores the old hotspot plan/origin with one warning rather than a broken loading screen.

The old translation-only Varrow/world-reservoir orientation mismatch is unchanged; see README's Varrow orientation section. This task does not reshape the global reservoir or change operations.

## Reproduce without Unity

```powershell
powershell -ExecutionPolicy Bypass -File Tools/WorldSectors/run.ps1
```

`-CheckOnly` skips PNG rendering. Python 3.13 runs with `-I` for harness preparation in both modes. No packages, Unity import or paid generation are used. Generated projects, baseline source copies, raw mesh/height JSON and binaries stay under ignored `Temp/world-sectors`. The executable uses the existing reference noise implementation and managed engine storage. Its images show produced geometry/placements, without native shading, terrain occlusion or physics.
