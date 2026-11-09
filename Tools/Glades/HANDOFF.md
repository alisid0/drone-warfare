# Glades that read as open woodland (task 25)

Branch `codex/glades`, from `f7588a1`. Written by Claude standing in for Codex. Unity, Blender and the GPU were not used. The C# was compiled against the Unity 6000.6.7f1 assemblies. The shader was compiled with D3DCompile and executed on D3D11 WARP, which runs on the CPU.

## Commits

| Commit | Change |
| --- | --- |
| `3c5bf9d` | `ForestPattern.cs`: lobed, ragged glade outlines, a crown-sized edge step with younger fringes, lone trees and scrub clumps, and scrub hummocks on the floor. |
| `c810081` | `ForestPalette.cs`: sunlit grass and scrub floors, and no dark ring in the macro map. |
| `8240ce2` | `ForestDetail.hlsl` and `ForestLitData.hlsl`: crowns thin out one by one into a glade, a textured floor shows in the gaps, and steep edge faces stay foliage. |
| (this) | `Tools/Glades/`: harness, before/after renders, measurements, checks and this handoff. |

## Why the glades read as craters

There were four causes:
- **Shape.** Each glade was one warped ellipse, so every opening was a smooth oval.
- **Profile.** Canopy height followed `Rim(margin) = margin²` across a 10-25 m margin. That makes a steep upper lip and a rounded bowl down to the apron.
- **Floor.** Crown detail is weighted by the macro cover, so it switched off in glades. The floor was one flat colour.
- **Rim colour.** `Linear(stand)` shrank crown coverage as the glade margin fell, which let the dark shadowed-ground colour through. On top of that, the crown shader drew dark gaps at half weight round the rim.

## What changed

### `ForestPattern.Glade` (geometry, cover and trees)
- **Outline.**
  - Each glade is a warped main ellipse plus 1-3 circular lobes on its rim, at 0.8-1.1 of the radius, with radii of 0.4-0.7 of the radius.
  - A second warp octave (17-19 m) is added to the 43-47 m one.
  - Radii are 22-50 m, up from 18-44 m. This keeps the glade share level now that the margin is narrower.
- **Ragged edge.** 6.5 m noise (±3 m) is added to the edge distance, so single crowns stand out or leave notches.
- **Two outputs.** `Glade(x, z, out rise)` returns cover and canopy rise from one edge distance in metres. `Glade(x, z)` still returns cover for its existing callers.
  - **Cover** reaches 1 within 3 m of the outline.
  - **Mature edges** reach full height within 5 m.
  - **Younger edges** cover about a third of the outline (41 m noise). They hold a 42% fringe for 5-11 m before the mature step.
- **Lone trees and scrub.** Glades of radius 26 m or more get one or two islands:
  - a lone tree: radius 3.5-5.5 m, full height;
  - or a scrub clump: radius 5-8 m, cover 0.6, rise 0.25.

  Real stems grow there through the existing `Tree()` density.
- **`At()`.**
  - Height is `H * min(Rim(edge), rise)`, so field, track and water edges keep the convex `Rim`.
  - Tree radius also follows `rise`, so the young fringe has smaller crowns.
- **Floor.** `Canopy()` lifts the apron by up to half (scrub hummocks, 13 m noise). It is never lowered, so exposure is unchanged.
- **Cost.** Points beyond `2.6 r + 36 m` from every glade centre skip the new edge noise.

### `ForestPalette` (macro map)
- **Coverage.** `Linear()` takes coverage from the crown size at the wood mask alone. Glade edges keep full crown colour, then meadow, with no dark ring.
- **Floor colour.** Floors mix grass (100,124,76) and bracken/scrub (80,99,60) in 15-40 m patches. They are brighter than the crowns.
- **Floor share.** A 16 m texel in an opening shows 95% floor, up from 80%. A 64 m texel still shows 55%.

### `ForestDetail.hlsl` (canopy material)
- **Crown thinning.** The caller now passes the macro alpha, its `ddx`/`ddy` and the vertex normal's height. Each crown compares its own threshold with the cover extrapolated to its centre (through the world gradient, capped at 0.15/m). Whole crowns drop out one by one towards an opening.
  - Emergent crowns survive furthest: thresholds 0.03-0.58.
  - Small crowns: thresholds 0.08-0.92.
  - In full cover every crown stays, so dense woods are unchanged.
- **Floor in the gaps.** Gaps in an opening show a floor instead of the dark understorey: 9 m bracken and scrub patches with 3.7 m tussocks.
  - Its mean factor is 1, so the far floor keeps the macro tone.
  - Emergent crowns shade it.
  - It fades out between 1.8 and 3.5 m footprints.
- **Steep faces.** Faces with a normal height under 0.88 are the sides of edge trees. They stay foliage, even where the 16 m macro map has blended towards the floor, and their caps are flattened by up to 60%.
- **Branches.** The lattice is skipped deep in openings, and the floor branch is skipped in dense wood.

## Evidence (`Review/`)

- **Renders.** `view300|view1000|view2500|topdown _ before|after .png`, plus `compare_*.jpg` and `sheet.jpg`.
  - **Before** is f7588a1's geometry and macro maps, shaded by `Tools/CanopyRelief2/crowns.py`, the f7588a1 shader mirror.
  - **After** is this branch, shaded by `Tools/Glades/crowns.py`.
  - The RTS camera is at 300 m, 1 km and 2.5 km slant distance (yaw 35°), plus a 512 m top-down at 0.5 m per pixel.
  - The target is the glade-richest point near the largest forest, at world (21244, 17860). It is picked only from fields this task doesn't touch, so both phases look at the same place.
  - The pictures are canopy only: no individual trees, terrain, HDRP lighting, AO or TAA.
- **Measurements.** `measurements.json` holds the numbers below; the 2 km survey is centred on that target.

| | Before | After |
| --- | --- | --- |
| Opening share (cover < 0.5), standard CanopyCracks block | 6.03% | 6.40% |
| Opening share, glade-rich 2 km survey | 7.82% | 8.44% |
| Outline roundness (4πA/P²), p10 / p50 / p90 | 0.71 / 0.86 / 0.95 | 0.41 / 0.53 / 0.70 |
| Edge drop, 10-90% of canopy relief, p10 / p50 / p90 | 6.5 / 11.5 / 22.5 m | 1.5 / 6.5 / 25 m (p90 = young two-step edges) |
| Edge crossings dropping within 8 m | 21% | 57% |
| Trees in the central km² | 30387 | 30292 (−0.3%) |
| Lone trees (≥ 10 m tall, open ground 9 m away on 3+ sides) | 0 | 7 |
| Floor minus dense-wood luma, 300 m / 1 km / 2.5 km | 22.3 / 20.9 / 14.2 | 26.1 / 23.9 / 16.9 |
| Floor texture (4 px high-pass RMS, levels) | 0.35 / 0.70 / 1.74 | 3.04 / 4.28 / 4.99 |
| Rim minus the darker of wood and floor (negative would be a dark ring) | 9.9 / 5.6 / 3.9 | 9.8 / 8.4 / 6.1 |
| Glade green minus dense, macro map, 16 / 32 / 64 m texels | 15.6 / 11.8 / 4.7 | 19.1 / 14.7 / 6.6 |

Dense-wood luma is identical in every view (104.0/105.3/108.5 before, 103.9/105.3/108.5 after).

## Checks (`Review/checks.txt`)

- **Gap check** (`run.py after cracks`, the CanopyCracks harness): internal 0 and shared 0 over 128 level pairs. There are no flipped, degenerate or vertical faces. Exposure is 0% at every level: holes, below the far chunk and below the detail floor.
- **Build cost**, in the same session, over the 24 block-levels:
  - 1245 → 1260 ms (+1.2%);
  - triangles 763,723 → 727,577 (−4.7%), because narrower margins trigger fewer 8 m rim refinements.
  - Review-view builds: 633/585/467 → 635/593/474 ms.
- **API compile.** `Tools/PredictiveStreaming/run.py api` compiles every runtime script against Unity and the package assemblies with zero warnings. The managed replay also compiles with warnings as errors.
- **Shader compile** (`check_shader.py`, ps_5_0): both branches compile.
  - Raster: 1105 static instructions, against 879 before.
  - Dense wood: 885.
  - Open floor: 306, against 155 before.
  - Ray helper: 149.
  - Still one texture sample.
- **Parity** (`check_parity.py`): the real `ForestDetail.hlsl` on WARP matches `Tools/Glades/crowns.py`, with a 99.9th percentile factor error of 0.0051. The cases are dense, an edge ramp, an edge ramp on a steep face, and open floor, each at 0.25, 1, 2 and 3 m footprints. The dense numbers match f7588a1's parity run exactly.

## Build checklist (Claude)

1. Merge and import. Check the ForestLit variants and that the console has no shader errors. `ForestCrownDetail` gained three parameters; its only caller is `ForestLitData.hlsl`.
2. Repeat hd180's `91_flight_0_forest_300m` and `_2500m`, and the RTS `88_world_forest_hlod` shot. You should see:
   - no bowls, ragged lobed outlines, crowns standing into openings and a few lone crowns;
   - textured, lighter floors with no dark ring;
   - steep edge faces reading as foliage rather than bare walls.
3. Check transitions:
   - fly towards a glade until the near-camera card fade reveals the floor and the real trees, including the new lone trees and scrub clumps;
   - cross a block refinement, watching the 0.75 s crossfade between 64/32/16 m macro texels, since crown thinning depends on the texel size.
4. Watch a slow pan at 1-2.5 km for TAA shimmer at glade edges. Thresholds are world-stable, but the cover gradient comes from 2x2 quad derivatives.
5. Run the canopy occlusion and drone-clearance autotests. Edge trees now stand at full height closer to openings.
6. GPU A/B of the canopy. Only edge and glade pixels pay more. Dense wood is about the same.

## Not verified

- **No Unity or HDRP checks:**
  - no pass or variant compile, and no Vulkan or DX12;
  - no GPU timing;
  - no TAA behaviour;
  - no real lighting, AO or shadows.
- **The in-game dark ring.** The offline lighting never reproduced it. Its rims were already lighter than both sides before, so the "no dark ring" result rests on the causes removed above, not on a measurement of HDRP.
- **Individual trees at 300 m.** The offline views show only the canopy.
- **The 4 m sector recipe canopy** (`data.local`) was not rendered.
- **Floor colours** were judged under CanopyRelief2's offline sun-and-sky fit only.
- **Reserved logging glades** in `SectorForest` (mission clearings) still use their old 10-25 m rims.
- **Glades don't follow terrain or tracks.** Lobes are random. Orienting glades along hollows would cost terrain queries in the hot path.

## Re-running (Python 3.13 with -I; never starts Unity or uses the GPU)

```
py -3.13 -I Tools/Glades/run.py after cracks     # gap, exposure and pattern checks (also: before cracks)
py -3.13 -I Tools/Glades/run.py before render    # f7588a1 G-buffers and survey -> Temp/glades/before-render
py -3.13 -I Tools/Glades/run.py after render     # this branch -> Temp/glades/after-render
py -3.13 -I Tools/Glades/render.py               # PNGs, then: render.py sheets, render.py measure
py -3.13 -I Tools/Glades/check_shader.py
py -3.13 -I Tools/Glades/check_parity.py
py -3.13 -I Tools/PredictiveStreaming/run.py api
```

`Tools/Glades/run.py` wraps the CanopyRelief2 → CanopyCracks → GladeRims driver chain. It adds the managed stubs that newer runtime code needs: route prefetch, sliced mesh upload, shader priming and landmark palettes.

`Tools/CanopyRelief2/check_parity.py` and `check_shader.py` still target the old `ForestCrownDetail` signature. Use the `Tools/Glades` versions instead. `Tools/CanopyRelief2/crowns.py` now serves as the f7588a1 mirror for "before" renders.
