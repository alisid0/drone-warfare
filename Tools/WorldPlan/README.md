# World plan, generator version 2

The 56 x 56 km plan is pure managed data. The stand-in's public names, types, enum ordinals, units and coordinate conventions are retained. `GeneratorVersion` is now 2; discard caches made with version 1. The five production files are `Assets/DroneSandbox/Scripts/WorldPlan*.cs`. No existing integration or gameplay files change.

Call `Generate(seed)` between worlds, before starting terrain workers, then share the resulting plan. `Ready` publishes completion through a volatile flag. Concurrent readers allocate nothing; regeneration must not overlap readers. The original public `readonly List<T>` fields are retained for compatibility, so consumers must not mutate the lists or their contents. This is an ownership contract, not a new immutable collection API.

`Height` and `RiverDistance` are bilinear 32 m grid samples; `CoverAt` is a nearest grid sample. Coordinates outside the square clamp to the exact edge. `Cell` returns a cached value for indices 0..55, with shares sampled at 32 m and a seed derived only from world seed, cell coordinates and generator version. `FrontAt` interpolates a cached 500 m polyline, north to south. Height is **ground/bed elevation**, not the water surface.

## Added data for the renderer and streaming

- `WorldRiver.waterLevels`: absolute water elevations paired with every point and half-width; downstream order, non-increasing levels, flat through lakes. Widths remain **half** widths, in metres. The Lissen's lower full width is 36 m; five tributaries grow from 3 m to 7-10 m full width.
- `Lakes`: two natural lakes and one reservoir. Each has an implicitly closed, simple `shore` polygon, absolute `level`, downstream `outlet`, name and `reservoir` flag.
- `Dams`: bank endpoints `a/b`, absolute `crest` and `tailwater`. The reservoir's water is level; the height drop occurs at its downstream dam.
- `WaterLevel(x,z)`: nearest raster water elevation, or NaN on dry samples. **Narrow streams need their river vectors**: a 3 m stream cannot be reliably represented by a 32 m cover raster.
- `Railways`: `WorldRoad` polylines in a separate list; one line through Halvard and Brenmoor, extending to the boundary. The road `kind` on a railway is not a new road class.
- `Bridges`: position, bank-to-bank endpoints, absolute deck elevation, river index, and route index. `railway` selects whether `route` indexes `Railways` or `Roads`. Decks and approaches still need detailed rendering/engineering.
- `Farms`: isolated farm positions, each connected to a track.
- `FarmableSquareKm` and `FarmedShare`: dry, open land under 180 m and 9% gradient, after woodland/wetland exclusion but before small settlement/site stamps. A regional fertility ranking assigns about 68% of that eligible land to farmland. These are districts, not local field parcels.

## Generation

1. Warped multi-scale relief makes western plains, central hills, eastern uplands, an incomplete northern range capped smoothly below 600 m, and three isolated landmark ridges.
2. A terrain-cost search chooses river corridors. Seeded lateral bends remove lattice-aligned straight reaches. Carve-then-flow grades each channel from its outlet upstream, cuts valleys, and joins tributary positions and elevations exactly to the trunk. Lake basins and shore earthworks are followed by a final sub-grid channel cut so interpolated terrain stays below water.
3. Four towns and twenty villages select dry, low-gradient footprints in valley/crossing catchments. A connected highway/village network uses 16-direction A* with intermediate slope samples and lake costs; local branches attach to existing segments. Paths retain exact endpoints and gain smooth bends. Rail has stronger slope and riverbank penalties. Final vector intersections export bridges, including streams too narrow for the raster.
4. Eight launch sites are selected together by a minimum-cost chain, checking 8-12 km spacing and 4-10 km westward depth from the local front. Their staggered arrangement fits the 56 km theatre. The airbase, front command and four depots get dry footprints and road access. Installations and farms avoid settlement footprints.
5. Regional woodland includes a roughly 12 km forest region; wetland follows low riparian ground, rock follows high/steep terrain. Settlement outlines are irregular. Cells use sampled cover and intersecting transport, water, settlement, front and installation features.

No `System.Linq`, packages, Unity objects, engine noise, engine random state or main-thread APIs are used. The only Unity API is `Vector2`'s public float fields and two-float constructor, checked against [Unity's reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Math/Vector2.cs). All noise and other arithmetic use managed .NET. Random values are consumed only in serial generation; parallel loops write disjoint grid rows.

## Standalone verification

From the worktree, without opening Unity:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/WorldPlan/run.ps1 -OutputDirectory C:\Users\ali10\AppData\Local\Temp\dwprev\codex\world
```

The script uses the installed .NET Framework C# compiler, optimized x64. It compiles the **actual production files**, a minimal Vector2 data stand-in, and the real `SectorKind` enum extracted from `Sector.cs` on each run. Passing this older compiler also checks the C# 9 ceiling. It adds no project dependencies. `-CheckOnly` skips images; `-Seeds` selects seed values when invoked within PowerShell.

Outputs per seed: a 2048 px atlas, separate 2048 px hillshade, vector JSON, metric JSON and check log. Seed 1 also gets two 2048 px / 8 km crops: Halvard and the reservoir. These show the full 32 m macro data; they do not fabricate buildings or the other session's field parcels. River widths use the exported widths with a 1.1 px minimum; transport and point symbols are enlarged and labelled accordingly. North is up.

Checks cover all height samples, cover shares and cell identities, dry/flat/connected placements, launch spacing and front offsets, downhill river levels, channel beds below water, exact confluences, simple lake shores, transport avoiding lakes, valid bridge references, out-of-world clamps, parallel reader equality, and a complete SHA-256 fingerprint after regeneration. Query benchmarks time a million calls separately from generation. Generation timing excludes compilation, checks, rendering and GC measurement. Retained memory is managed live data; peak working set includes the standalone process and generation scratch space, measured before rendering.

Unity import, Mono/IL2CPP compilation and performance, far-terrain seams/appearance, bridge meshes, detailed roads and the 20 km flight acceptance test remain for Claude's integration. This phase supplies macro geography; it does not build the streaming or flight systems.

Captured seed 1-3 numbers and the wider seed audit are in [validation-report.md](validation-report.md).
