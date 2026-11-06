# Canopy relief 2: individual crowns in the far canopy

Branch `codex/canopy-relief-2` from `b8d8322`.

## Commits

| Commit | Change |
| --- | --- |
| `df2068c` | Passes the sun's sideways shadow shift to forest draws. |
| `03ee787` | Shades individual canopy crowns in the material instead of the photo-map carpet. |
| `5eda3a4` | Keeps the 7-10 m crowns out to a 3.5 m footprint, and holds the mean tone with distance. |
| (this) | Adds the harness, review pictures, measurements and this handoff. |

## What changed and why

ChatGPT's critique 19 said the 2.5 km canopy read as a "bumpy green carpet". The cause was the 8 m photo crown map: it drew every crown at the same size and softness, everywhere. The new canopy shading in `ForestDetail.hlsl` changes four things.

- **Crown layout.** The shader draws world-stable crowns per pixel. It uses two nested jittered lattices:
  - a 15 m lattice and a 7.5 m one inside it;
  - both rotated 26.6° from the world grid, with a gentle warp.
- **Stands.** Each 60 m stand has its own share of emergent crowns: young (5-15%), mixed (35-63%) or old (75-90%).
  - Emergent crowns are 15-19 m across. The rest of the stand has 7-10 m crowns.
  - Stand edges follow whole crowns, so they never run in straight lines.
  - Each stand also sets highlight strength (±25%).
- **One crown.** Each crown is an elliptical cap.
  - Its slope gives HDRP a sunlit side and a shaded side.
  - It has its own shade (±16%), a brighter top, and a darker rim and gaps.
  - Emergent crowns cast a shadow onto lower crowns, displaced away from the sun. The shift comes from `RenderSettings.sun` through the unused `_ForestOrigin.zw`.
  - Shadows shorten as cloud dims the sun, and vanish at night.
- **Filtering.** Nothing is drawn below about three pixels.
  - Rims, and creases where two crowns meet, soften over 1.6 pixels.
  - The 7-10 m crowns fade out between 1.9 and 3.5 m footprints (long screen axis). Emergent crowns fade between 2.2 and 3.6 m.
  - Faded crowns settle to 0.989, the photo map's mip mean, so distant woods keep their calibrated tone.

The photo map survives only as 2.5 m foliage clumps inside crowns: one sample, offset per crown. `ForestDetailB.png` is no longer bound.

Nothing else changed:
- geometry, vertex data and the macro map;
- stand-scale (40-120 m) height and colour;
- glades;
- tree counts.

## Before and after (offline replay, same production geometry and macro maps)

The review pictures are in this folder:
- `compare_view*.png`: half-size full frames, then 1:1 crops at the centre and the top. Before is on the left, after on the right.
- full frames: `view400/1000/2500_{before,after}.png`;
- `topdown_240m_at_0.25m.png`.

The three views use the RTS camera at slant distances of 400 m, 1 km and 2.5 km, with yaw 35°. They look at the `88_world_forest_hlod` target.

Measurements are in `measurements.json`, in green sRGB levels:

| | 400 m | 1 km | 2.5 km |
| --- | --- | --- | --- |
| Mean colour change | ≤0.4 | ≤0.1 | +0.8 |
| Crown-scale contrast | 8.3 → 11.9 | 9.2 → 12.9 | 5.7 → 6.7 |
| Highlight above the median, per 60 m stand | 16 → 27 | 14 → 22 | 8.6 → 9.3 |
| Stand-to-stand highlight spread (half range) | 4.3 → 6.0 | 5.0 → 5.6 | 3.9 → 6.1 |
| Single vs 4-sample residual (aliasing) | 0.4-0.6 → 1.0-1.2 | 0.4-0.5 → 1.2 | 0.5-0.8 → 1.0/0.7/0.15/0 (1-2/2-3/3-4/>4 m footprints) |

Against ChatGPT's spec:

| Spec | Result |
| --- | --- |
| 8-20 m crown normals | 7-10 m and 15-19 m crowns |
| 40-90 m stands | 60 m stands with ragged edges |
| ±2-4 m relief | Emergent tops sit 3.5-6 m above the small crowns ±1 m; geometry keeps its ±4.1 m |
| ±5-8 levels of highlight spread | 5.6-6.1 |
| No new trees | None added |

Lit radiance against footprint stays close to monotonic (`checks.txt`):
- after: 0.97 near, rising to 1.00 far, within 1%;
- before: 0.95 → 0.99.

## Checks (all in `checks.txt`)

- **Gap check** (`run.py after cracks`, the CanopyCracks harness): internal 0 and shared 0 over 128 level pairs; exposure 0% at every level. The canopy CPU build path is unchanged. The 24-block build totals 1260 ms here, against 1243 ms for the same code in the predecessor's run.
- **API compile** (`run.py api`): passes with zero warnings against the Unity 6000.6.7f1 assemblies. This includes the `ForestMaterials` use of `RenderSettings.sun`, `Light.isActiveAndEnabled` and `Light.intensity`.
- **Shader compile** (`check_shader.py`): D3DCompile `ps_5_0` succeeds for both the raster and ray-tracing branches.
- **Parity** (`check_parity.py`): the real `ForestDetail.hlsl` runs as a compute shader on D3D11 WARP, on the CPU only. It matches the NumPy mirror `crowns.py` that drew the after pictures: mean factor error 0.0006, 99.9th percentile 0.005, at footprints of 0.25 to 3 m.

## Cost (proxy only)

| Path | DXBC instructions, including ForestFade | Texture samples |
| --- | --- | --- |
| Before | 301 | 3 anisotropic executed (6 static) |
| After, crowns resolved (footprint < 3.6 m) | about 880 | 1 |
| After, far canopy, glades and ray tracing | 155 | 0 |

- In the three review views, 84-86% of canopy pixels take the crown path. That works out to about 470 more instructions per canopy pixel, or roughly 0.8 G instructions per full-canopy 1080p frame. I estimate +0.1-0.2 ms on a mid-range GPU.
- Low flight should cost less:
  - the canopy near the camera is mostly replaced by real trees;
  - the canopy past them has coarse footprints and takes the cheaper path.
- There are no new draws, keywords, passes or vertex streams.
- If the p95 budget is exceeded, two knobs cut the cost:
  - lower 3.6 (the branch test and fade ends);
  - drop the emergent-shadow loop (about 60 instructions).

## Not verified

- **Unity.** Nothing was checked in Unity or HDRP:
  - no pass or variant compile;
  - no Vulkan or DX12 compile;
  - no TAA behaviour;
  - no GPU timing or low-flight p95;
  - no draw-call count.
- **Shimmer.** Only the 4-sample metric above stands in for it. The after is somewhat noisier than the before at 0-2 m footprints; TAA should hide this, but please watch a slow pan at 1-2.5 km.
- **Offline lighting.** It is a sun-plus-sky approximation, fitted per channel to hd162's `88_world_forest_hlod` statistics. It uses the default sun Euler(36, -35), not DayNight's 15:07 position.
- **Sector views (`31_*`).** Near sectors, the 4 m recipe canopy already has geometric crown bumps, and these crowns add to them. Check that it doesn't look busy.
- **Glades.** They look unchanged and still read as smooth ovals; they were out of scope.
- **Leftovers.** These are now unused or stale:
  - `ForestDetailB.png` (can be deleted);
  - `Canopy.mat`'s stale `_ForestDetailB` entry (harmless);
  - the old `Tools/CanopyRelief` and `Tools/CanopyCracks` `check_shader.py` scripts, which call the removed `ForestCrownDetail(world)` signature. `Tools/CanopyRelief2/check_shader.py` replaces them.

## Re-running (Python 3.13 with -I; never starts Unity)

```
py -3.13 -I Tools/CanopyRelief2/run.py after cracks   # gap/exposure checks
py -3.13 -I Tools/CanopyRelief2/run.py api            # Unity-assembly compile
py -3.13 -I Tools/CanopyRelief2/run.py after render   # G-buffers -> Temp/canopy-relief-2/after-render
py -3.13 -I Tools/CanopyRelief2/render.py             # before/after PNGs, then: sheets, measure
py -3.13 -I Tools/CanopyRelief2/check_shader.py
py -3.13 -I Tools/CanopyRelief2/check_parity.py
py -3.13 -I Tools/CanopyRelief2/calibrate.py ramp     # also: patch, means
```
