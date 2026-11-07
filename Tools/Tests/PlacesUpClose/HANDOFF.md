# Places up close

Branch `codex/places-up-close`, based on `9667d1f`. No Unity import/run, push, merge or main-checkout source edits. The pre-existing staged FieldsVillages review and garden-script edit remain separate.

Landmarks now submit the actual FBX meshes, imported child transforms and photo materials with `Graphics.RenderMeshInstanced`. Buildings retain their shared recipe positions, yaw and scale. The HDRP Lit adapter adds complementary dither between real buildings and HLOD at 850–1100 m, accounting for FOV, orthographic projection and quality LOD bias. Surfaces remain unchanged. Building tiers use 65/250 m; the sector's equipment tiers and 275 m cull remain in effect for clutter, wagons and cranes. Only full building tiers cast shadows. Owned material copies are released on origin/seed changes. No FarScatter or forest source changed.

Chunk replacement now separates mesh creation, clearing, buffer allocation, bounded attribute/index copies, submesh setup and driver upload. Each copy is at most 12 KiB. Up to six copies may share a 0.2 ms window; allocations and driver uploads remain separate. The existing atomic publication, cancellation and original-mesh restoration paths remain intact. This limits individual work units; native driver stalls and the **2 ms maximum are not yet measured**.

Village cores have 12–14.2 m frontages, roofs roughly 2–6 m behind the road edge, and rear sheds. Slot-centred placement avoids discarding alternate houses when adjacent frontage widths differ. This covers both world villages and legacy Operations. Newer plots retain their loose spacing and gardens. Twenty villages contain 539 planned properties with zero overlaps; access, core setbacks, repeatability and retained-snapshot checks pass. Tolbeck's review has 26 accepted houses, 20 planned core plots. The additional Varrow review exercises the legacy layout path.

Town edges reserve community and working ground before fields are fitted: an allotment grid, a marked playing field with goals, cemetery rows and walls, roadside warehouses/depot yards with distribution poles and wires, ribbon houses and larger farmyards. Existing huts, freight groups, barns and machinery sheds are reused; markings, goals and gravestones are batched geometry. Reservations and painting share the same world plan. The reviewed Town Outskirts has 135 houses and planned counts of 1 allotment site, 1 playing field, 1 cemetery, 3 depots and 3 farmyards. Native placement may reject obstructed individual structures.

[Nine review images](Review/index.html): works, airfield and rail yard before/after at a **300 m slant range**, plus world and legacy villages at **300 m altitude**, and Town Outskirts at **400 m altitude**. These are Blender geometry reviews, not HDRP screenshots. The landmark baseline is the unchanged HLOD mesh from the starting layout; the after view uses its real FBXs with representative LOD selection. The review does not emulate temporal dither. Terrain/lighting are approximate; coarse review terrain is lowered locally beneath community surfaces to avoid false intersections. Woods and native crop rendering are omitted.

The full-compound FBX selection audit at the landmark camera, including selected transition tiers and without frustum rejection, counts:

| Compound | Submitted triangles | Material slots |
|---|---:|---:|
| Works | 9,048 | 40 |
| Airfield | 996 | 20 |
| Rail yard | 3,000 | 29 |

These exclude unchanged HLOD yard surfaces and are **not a 0.5 ms GPU certification**. Details are in [mesh-budgets.json](Review/mesh-budgets.json). C# 9 compilation against Unity 6000.6.7f1 assemblies, including the complete modified streamer, passes with zero warnings/errors. Four interrupted mesh-upload cases preserve every attribute/index and keep publication deferred. Managed export processes peaked at 252–309 MiB; Blender jobs ran serially with two threads. No new models, packages or unapplied patches are needed.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/PlacesUpClose/setup.py
py -3.13 -I Tools/Tests/PlacesUpClose/verify.py
py -3.13 -I Tools/Tests/PlacesUpClose/run.py Checks
py -3.13 -I Tools/Tests/PlacesUpClose/run.py --renders
py -3.13 -I Tools/Tests/PlacesUpClose/run.py VillageLegacy --renders
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --threads 2 --python-exit-code 1 --python Tools/Tests/PlacesUpClose/budgets.py
py -3.13 -I Tools/Tests/PlacesUpClose/package.py
```

## Claude's build checklist

- [ ] Review/integrate the commits; import and compile in the integration checkout. Confirm PlaceLit shader/instancing variants compile in the player, including normal-mapped materials.
- [ ] Run autotest and compare `89_landmark_*` at 300/2500 m against hd154. Check every imported mesh part, texture, root, yaw and scale; ensure active-sector objects are not duplicated at either ownership boundary.
- [ ] Fly through 65/150/250/275 m equipment transitions and 850–1100 m building handover. Check dither/depth, no pop, no holes, both cameras, zoom, quality changes and orthographic views. Inspect 2.5 km masses unchanged.
- [ ] Measure **fully visible works at 300 m ≤0.5 ms GPU**, recording actual draw/pass counts. The offline geometry budget does not establish this limit.
- [ ] Repeat cold flight, reversals, teleport and tile eviction. Require **every World detail stage ≤2 ms**, verify tile readiness latency, no terrain holes, correct colliders and no retained meshes after world reset.
- [ ] Capture the village at 300–400 m; verify close old street lines, 2–8 m ordinary roof gaps, junction breaks and rear outbuildings. Check legacy Varrow as well as world villages.
- [ ] Capture Town Outskirts at 400 m. Verify accepted community sites, goals/headstones, depot yards, ribbon development, power lines and farmsteads; check native surface/terrain intersections, crop exclusion, sector-edge painting and renderer/GPU cost.
