# Roads and tracks

`codex/roads`, based on `codex/places-up-close` (`25ec74e`). Work is confined to this worktree. No Unity import/run, push, merge, forest changes or main-checkout source changes. The pre-existing staged FieldsVillages files and garden-prop edit are preserved.

Highways are 7 m wide, ordinary roads 4 m, and track envelopes vary from 2.5–3.5 m. Shared metre-based appearance gives tarmac a lighter crown, darker wheel paths, faded 3 m / 9 m centre dashes and edge lines on highways, repairs and edge cracks. Tracks have two 0.4–0.6 m ruts and a grass centre. Rough verges have a mown strip; farmland roads gain narrow damp drains. Junction corners splay without painted lines across the crossing. Actual hedge openings supply worn entrances, fading field ruts and occasional gravel lay-bys. Bridge road ribbons follow the measured deck and blend into the approaches; wider highways enlarge the existing bridge model's width to fit.

The same linear-colour profile supplies the sector's baked road atlases and the 2 m detail tiles, 4 m regional pages and 13.67 m world picture. Narrow features are footprint filtered, including the 25% dash duty cycle. Ordinary roads previously became dirt in both distant painting and sector splats. Authored world roads now retain their exact source polyline and chainage through sector clipping. Private 3 m rear lanes stay tracks. The collar no longer replaces roads with the terrain's average grass/dirt colour. Gateway paint and surface-control masks share the original parcel gates.

Road surfaces use at most seven opaque, non-shadowing renderers and ordinary HDRP Lit materials. Three 128×768 baked atlases and four 4×4 colour swatches avoid runtime texture generation, decals and extra terrain shader samplers. Roadside poplars, gates and the new nine-metre telegraph pole use `Graphics.RenderMeshInstanced`, imported child transforms and the existing LODs. Pole spacing is 40–60 m, with exclusions at water, buildings, gates and bridges. Limits are 96 trees and 96 poles, paid from the **existing Farmland avenue allowance**, including hidden LOD renderers; trees also consume `World.TreeLimit` and Undergrowth spacing. The new pole has 84 / 72 / 24 triangles, one material and direct-child meshes. No package or shader was added.

Shallow puddle quads are restricted to low, gently graded points in track ruts, capped at 48 pairs. They appear during rain and linger for three minutes afterwards. This is a small reflective material surface, not an additional water simulation. Distant road maps retain the compacted-soil mean rather than streaming a new world picture on each weather change. Furniture and reflective puddles are active-sector detail; coarse road paint persists outside it.

## Integration patch

[Claude-integration.patch](Claude-integration.patch) is **unapplied**. Review and integrate all three hunks with the feature:

- `Ground.cs`: replace the old all-dirt road treatment with shared class/rut/verge splat masks. The detailed asphalt itself is the road ribbon.
- `Farmland.cs`: subtract road-surface renderers from its existing 5,400 cap; replace the old avenue builder with the instanced roadside builder using the already reserved allowance; preserve the gateway opening. Do not run both avenue builders.
- `Atmosphere.cs`: call the rain-state hook from the existing weather switch.

Protected originals remain byte-for-byte unchanged. Ground and Farmland temporary patched copies were compiled with the feature. The Atmosphere hook targets its existing `Weather.Rain` call; full HDRP weather integration still belongs to Claude's build.

## Evidence and limits

[Sixteen paired review views](Review/index.html) show a main road, junction, farm track and village street at 150 m and 300 m **altitude above the target**. They are Blender geometry/material reviews, not HDRP frames. Before uses the original sector road-splat equations on the same prepared layout, rather than reconstructing the old random avenue placements. After includes the new instance selections. Native crop shading and woods are omitted, terrain is sampled at 5 m and lowered 8 cm in the review, and lighting/LOD appearance is approximate. Road geometry and UVs come from the production builder. The managed furniture loader assumes three imported LOD renderers per existing model; native accounting reads the actual prefabs.

C# 9 compilation against Unity 6000.6.7f1 modules and .NET Standard 2.1 passes with zero warnings/errors. The managed harness also compiles cleanly. Checks cover dash energy at negative chainage, track/rut dimensions, the grass centre, finite endpoints, splayed junctions, immutable inputs, spatial-bucket boundaries, valid upward road triangles, renderer caps and clipped-sector/world alignment. The worst filtered colour difference against independent 32×32 quadrature is 0.00856 in linear RGB distance at 2 / 4 / 13.67 m footprints. Shared centreline error is at most 0.002 m; chainage error is at most 0.054 m in the three reviewed sectors.

Exact counts and bounded timing samples are in [scene-budgets.json](Review/scene-budgets.json) and the three `*-audit.json` files. The reviewed scenes use 4–6 road renderers, 8,268–17,444 road triangles, and 132–327 furniture renderer slots. Managed processes stay around 250–280 MiB; Blender runs serially with two threads. Straight two-metre segments are compacted only in the query index; the terrain-following render geometry retains its vertices. A 62,500-point before/after splat comparison is included, but it uses managed noise/terrain substitutes and is sensitive to other machine activity.

**Performance is not signed off.** Neither the ≤0.3 ms GPU increase, ≤2 ms worst streaming stage nor sector build ≤hd155 can be established without the native player. The road geometry pass is reported separately as `roads` in `World.Timings`. Existing streamer upload stages, texture sizes and shader code are unchanged; all additional distant painting stays in its existing workers. No Unity editor, player, autotest or native GPU profiler was run here.

## Reproduce

```powershell
py -3.13 -I Tools/Tests/Roads/prepare.py
py -3.13 -I Tools/Tests/Roads/run.py Checks Textures
py -3.13 -I Tools/Tests/Roads/assets.py
py -3.13 -I Tools/Tests/Roads/run.py Main River Village --renders
py -3.13 -I Tools/Tests/Roads/package.py
```

`--geometry-only` exports and audits without repainting the terrain. `--render-only` reuses scene captures. Compile before starting an export; keep those processes sequential. The build scaffold uses the already-present PlacesUpClose/Water harness helpers, including managed substitutes; it never starts Unity or creates a Library. To rebuild the pole:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --threads 2 --python-exit-code 1 --python Tools/Blender/make_road_pole.py -- Assets/DroneSandbox/Resources/Models Tools/Tests/Roads/Review/pole.json
```

## Claude's build checklist

- [ ] Review/integrate the commits and all three patch hunks in the integration checkout; import the pole, seven materials and seven texture assets. Compile and run the normal autotest.
- [ ] Compare hd155's `02c_bridge`, `02e_farmland` and `31_sector_00_RiverValley` at matching settings. Capture native main road, junction, track and village at 150/300 m. Check faded 3/9 markings, crown/wheel paths, cracked margins and grass between ruts.
- [ ] Inspect road/terrain intersections and z-fighting on steep slopes and crossing ribbons. Verify the 18 cm lift against terrain LOD, width-scaled bridge kerbs, deck height and both approaches. Confirm no road ribbon crosses water outside a real bridge.
- [ ] Inspect actual field gates: hedge gaps, crop/soil overlap, swung-open gate bounds, gravel lay-bys and the final 12 m rut fade. Check mown strips and ditch breaks at gates, junctions, villages and water.
- [ ] Cross sector boundaries and tile/regional/world handovers. Confirm continuous class widths, centreline, route phase, filtered markings and gateway paint; inspect private rear tracks and both sides of clipped world roads. Furniture remains active-sector scenery.
- [ ] Switch clear → rain → clear. Verify puddles occur only in low ruts, toggle without a rebuild, linger for three minutes, do not float on cross-slopes, and do not add a water simulation. Check the reflection at 100–400 m.
- [ ] Record actual importer LOD/renderer counts, ≤96 trees, ≤96 poles and 40–60 m pole spacing. Confirm `Roadside.ChargedRenderers` is inside the old avenue allowance, total Farmland renderers ≤5,400, total trees ≤11,500, no duplicate old avenues, both cameras, and cleanup on travel/regeneration.
- [ ] Profile the fully visible road/junction at 100/150/300/400 m: **additional GPU ≤0.3 ms**. Record surface/furniture draw calls, triangles, shadows and SSR cost. Offline counts are not a GPU certification.
- [ ] Compare cold and warm sector build times with **hd155 or faster**, including the separate road pass, Ground splats, shared snapshot/index construction and Farmland. Record **every streaming stage ≤2 ms**, including cold travel, reversal and teleport; distinguish worker painting latency from main-thread uploads. Check no stale mesh/material instances after repeated travel.
