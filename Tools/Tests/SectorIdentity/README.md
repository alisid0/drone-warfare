# Sector identity handoff

Branch: `codex/sector-identity`, based on `596e874`. No Unity editor/player was launched. No packages, asset models, terrain implementation, HUD, mission or unit files were changed. `World.cs` and Claude's street-village builder are unchanged.

The generator now reserves each dominant land use in **final world metres**, before MapLayout creates village streets and field parcels. The existing `OnWorks` query protects the complete reservation from fields and ordinary woods, including the gaps between structures. `plan.sites` contains actual models; separate data describes yards, streets, fences and terrain levelling. A five-node route planner takes access roads around the perimeter when the gate faces away from the nearest road. New forestry-track crossings register bridges.

| Kind | Dominant feature |
| --- | --- |
| DeepForest | 680 x 640 m reserve, a rounded woodland core, two glades and a forestry track. Tour seed places 6,049 core trees; nominal crown coverage is 73.1% in the reserved rectangle, versus 8.2% at the same location before. |
| IndustrialZone | 320 x 280 m fenced compound: two FactoryHalls, three StorageTanks, OfficeBlock, Warehouse and CoolingTower; five joined PipeRacks, concrete/gravel yards, gated road access and a rail spur with four tank wagons. Eight large structures inside the compound. |
| Airfield | 560 x 24 m runway, parallel 520 x 12 m taxiway, connecting strips and a concrete apron. HangarLarge, three AircraftShelters, ControlTower, FuelFarm and Windsock. The old runway was approximately 958 x 30 m after scaling. |
| RailJunction | 420 m working track inside a 440 x 150 m reserve, three fanned sidings and 27 wagons mixing container, hopper and existing stock. FreightShed, GantryCrane across two tracks, SignalBox and an existing Station aligned to its platform side. |
| TownOutskirts / MarketTown | 350 x 310 m reserve with six streets and 38 buildings: 36 street-facing residential/commercial buildings, plus Warehouse and OfficeBlock at the industrial edge. Townhouse_Row, Apartment and Shop occupy the centre; houses A-D occupy the outer rows. Ordinary villages remain separate. |
| Marshland | Approximately 480 m of connected stream corridor with a 124 m reservation width, bank ponds, wet soil and batched reeds. The tour seed has three pools along that corridor; roads and unit sites can suppress an individual pool without breaking the continuous wetland reserve. |

The existing 11,500-tree shared limit is enforced by `World.Place`. The core has an additional cap of 6,800 trees / 20,400 all-LOD renderers and is planted before ordinary woods consume the remaining shared allowance. Yards share one mesh, the compound fence one mesh, and wetland soil/reeds two meshes. Existing farmland and village-decoration budgets are unchanged.

## Plan renders

[Before/after contact sheet](Review/comparison.png) · [Unlabelled candidate sheet](Review/unlabelled_after.png)

| Kind / seed | Complete 2 km map | Same-location detail |
| --- | --- | --- |
| DeepForest / 1037 | [Before / after](Review/DeepForest_before_after.png) | [Detail](Review/DeepForest_detail.png) |
| IndustrialZone / 1148 | [Before / after](Review/IndustrialZone_before_after.png) | [Detail](Review/IndustrialZone_detail.png) |
| Airfield / 1222 | [Before / after](Review/Airfield_before_after.png) | [Detail](Review/Airfield_detail.png) |
| RailJunction / 1185 | [Before / after](Review/RailJunction_before_after.png) | [Detail](Review/RailJunction_detail.png) |
| TownOutskirts / 1111 | [Before / after](Review/TownOutskirts_before_after.png) | [Detail](Review/TownOutskirts_detail.png) |
| Marshland / 1074 | [Before / after](Review/Marshland_before_after.png) | [Detail](Review/Marshland_detail.png) |
| MarketTown / 1629 | [Before / after](Review/MarketTown_before_after.png) | [Detail](Review/MarketTown_detail.png) |

Six seeds are the exact `AutoTest.SectorTour` values (`1000 + 37 * tour index`). MarketTown is absent from the tour: 1629 is the explicit additional sample, `1000 + 37 * SectorKind.MarketTown`. Unlabelled sheet order is DeepForest, IndustrialZone, Airfield across the first row, then RailJunction, TownOutskirts, Marshland.

These are **plan drawings**, not screenshots of Unity or model rasterizations. Fields, roads, railways, new sites and successful tree placements come from executed production code. Building footprints are schematic; village buildings use generic symbols, ponds use their nominal radii, and the marsh tint shows its reserved corridor. The nominal canopy survey unions source-sized circular crowns at 0.5 m resolution, including gaps and glades; it does not measure leaf alpha coverage in HDRP.

## Numbers

See [metrics.json](Review/metrics.json) for the before/after building totals, all-LOD renderer estimates, trees, fields, both coverage definitions and CPU harness build times. The table below is generated alongside the renders in [measurements.md](Review/measurements.md).

Building counts include industrial structures, churches and garden huts; wagons, cranes, pipes, fences and trees are props. The town's 38 planned buildings are distinct from its whole-sector total, which includes ordinary villages. IndustrialZone's planned-site count includes the separate pre-existing station.

`coverage` is the existing central 1250 m square survey, excluding water, village footprints and flat sites. Its denominator changes when large flat sites are introduced. `wholeMapFields` is the nominal parcel area divided by the fixed 4 km2 map area, making that effect visible. Changing the recipe also changes its roads, village candidates and selected farm districts; coverage does not simply subtract the feature's area from the old map.

Renderer estimates execute **World, Features and Farmland**, including all tree/model LODs, meshes and power-line pieces. The new FBX models and trees cost three renderers, old joined scenery models one, as in their Blender source. Counts omit terrain, water, grass, backdrop, scanned undergrowth, bases and gameplay objects, whose builders are substituted. They are resident scenery-renderer estimates, not visible renderer counts, draw calls or GPU timings.

Harness build time includes `Generate`, `MapLayout.Prepare`, village/wood placement and feature/field mesh loops. It substitutes zero-height terrain, engine objects, imports/uploads and the other builders. Native `Mathf.PerlinNoise` is also unavailable: both versions use the same reference Perlin implementation inherited from the parcel harness. These results **do not prove a Unity sector build below 15 seconds**.

## Validation and reproduction

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/SectorIdentity/run.ps1
```

The default output is `Temp/sector-identity/review`. Override `-Output`, `-Dotnet` or `-Python` on another machine. Use `-Only after -SkipRender` for a focused C# iteration. The script reads baseline sources directly with `git show 596e874:...`; it never checks out another branch, starts Unity or downloads packages. Run Python with `-I`.

Passed:

- C# 9 compilation of both executable harnesses; independent compilation of all affected game files against the installed Unity 6000.6.7f1 CoreModule assembly. Zero warnings/errors.
- Fresh same-seed generation and complete placement fingerprints for all seven candidate examples.
- 448 additional seed layouts (64 per kind): reservations, access availability, footprint-sized building flats and street/building clearance.
- Dense field sampling against reservations, ordinary-wood exclusion, complete flat-yard coverage, runway dimensions, successful core planting and the shared tree cap.
- Mesh index checks, existing field-mesh winding and hedge LOD checks, plus empty-sector reset.

The harness found a baseline cache-order bug: `PlanVillages` consulted the previous sector's `RailField`. The candidate prepares current railways before villages. The untouched baseline logs state-dependent first transitions for IndustrialZone, RailJunction and TownOutskirts; its saved comparison uses the first build in the documented harness order, rather than silently repairing baseline code. Candidate placement fingerprints agree across fresh regenerations.

## Still for Claude

Run the normal Unity compilation, autotest and SectorTour after integration. Inspect imported FBX LODs/materials, ground contact and terrain transitions, reed/mud appearance, actual canopy coverage, and native label-free recognition at the tour's cameras. Measure complete sector load time, renderer/SetPass counts and GPU/frame time on the target laptop.

Reservations participate in `OnWorks` and flat-site clearance, so existing mission placement keeps out of their occupied land-use footprints. No synthetic yard markers were added to `plan.sites`; station/building lookups still find actual models. Mission placement and usable target positions around the larger features need the integration autotest; this harness does not execute combat or mission recipes.
