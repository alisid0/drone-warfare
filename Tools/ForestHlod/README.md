# Natural forest handoff

Branch `codex/forest-natural`, based on the merged `788df6f`. Unity was not opened or run. FarDetail and the main checkout were not edited.

The forest now uses deterministic, minimum-distance tree placement with clustered density and warped stand ages. Cards, meshes and the HLOD texture consume the same tree sampler, including position, species, height, radius, tint and yaw. Species follow warped 240 m districts instead of changing every 30 m. Individual crowns span 2-7 m in effective opaque radius and 8-24 m in height. The three atlas variants per species are asymmetrical derivatives of the existing species photograph, not three independently photographed trees.

Glades have variable size, rotation, position and warped boundaries; their previous regular spacing contributed to the dotted appearance. Cover is interpolated before 7/21 m boundary noise. Small young trees can extend a few metres into pasture/farmland; water, wetland and built covers reject them. Both canopy height and crown size diminish at margins. A ground band meets the existing world picture through the new `FarTerrain.CanopyGroundColour` hook. This hook does not replace or sample FarDetail's independently changing textures.

The canopy uses 32 m interior geometry, 16 m glade rims and stitched 8 m external margins, with shared world-space normals and skirts between block resolutions. Uncovered vertices sink two metres below the sampled ground. A non-repeating crown bake uses the exact individual tree list, per-tree rotation/offset, photo variants and overlapping crown heights. A small area filter and two-texel texture gutters suppress pinpricks and block seams. The 64/128 m far meshes average relief and species colour instead of sparsely sampling crowns or individual glades. Geometry describes the stand envelope; individual crown geometry still belongs to the card/mesh tiers.

## Renders and measurements

- [Before/after sheet: 300 m, 2.5 km, 9 km and farmland edge](Review/forest-comparison.png)
- [300 m before](Review/forest_300m_before.png) / [after](Review/forest_300m_after.png)
- [2.5 km before](Review/forest_2500m_before.png) / [after](Review/forest_2500m_after.png)
- [9 km before](Review/forest_9000m_before.png) / [after](Review/forest_9000m_after.png)
- [Farmland-edge before](Review/forest_edge_before.png) / [after](Review/forest_edge_after.png)
- [Matched-camera luminance](Review/calibration-comparison.png), [near mesh tier](Review/forest_near_after.png), [crown atlas](Review/crown-atlas.png)
- [Counts and budgets](Review/counts.md), [raw submissions and memory estimates](Review/metrics.json)

All panels are 1600 x 900 offline Blender renders of production-source submissions, not Unity/HDRP captures. `before` compiles the four exact forest sources from `788df6f`; no checkout switch is needed. The interior forest is seed 1 at `(16500, -4500)`, so the complete 9 km frame remains inside the world. The edge target is the nearest forest/farmland boundary. Cameras, lighting, terrain and project photo models are held fixed between revisions. The largest-forest autotest view is near the world boundary and remains available unchanged in Unity.

The harness replaces native engine storage/submission, uses a conservative frustum and samples WorldPlan ground. It does not reproduce FarDetail streaming geometry, native Perlin noise, HDRP exposure/fog, GPU clipping, uploads or pass multiplication. The comparison does not prove the FPS gates or an exact colour match to the other session's new ground textures.

Validation: the C# 9 assembly check passes with zero warnings/errors against installed Unity 6000.6.7f1 CoreModule and PhysicsModule. Complete rendering sources and WorldPlan compile; unrelated game services are fixtures, so this is not a full player build. Three-seed checks pass: full glades occupy 8.43-9.03% of sampled woodland, heights span 8-24 m, radii stay 2-7 m, and sampled minimum tree spacing is 4.14-4.20 m. Dense woodland samples contain 5,628-5,766 trees/km2. Neighbouring species agree 91.3-93.3% of the time at 20 m. Reverse traversal preserves tree membership/positions; adjacent fine blocks agree within 1 mm; normals are finite; faces are non-degenerate; near meshes and resource cleanup are exercised. See the committed seed JSON files and `luminance.json` for the measured handover difference (within 10%).

The 2.5 km forest submits 149,277 triangles versus 240,484 at `788df6f`, with the same 10 draws. Fine texture residency increases about 1.6% for gutters. The 0.7 ms construction slice, opaque rendering and tree distance limits remain; final copies/native uploads are indivisible. Triangle counts improve, but target-machine frame time must still be measured.

## Build checklist for Claude

1. Integrate the commits into `environment-and-asset-hunt`, compile/build and run WorldCheck. No packages or generated Unity Library are needed in this worktree.
2. Compare hd139 at the same 1600 x 900 settings: sandbox >= 114 fps, wide >= 121 fps, low-flight p95 <= 12.2 ms. Record steady CPU/GPU time and first-build/upload spikes during a 40 m/s forest-edge crossing.
3. Inspect the unchanged `88_world_forest_hlod` shot plus these matched review cameras. Check the farmland-facing margin against the other session's FarDetail textures. The new FarTerrain colour hook matches the old world picture; adapt that integration only if the ground work changes its palette.
4. With fixed exposure, check card/canopy luminance within 10% and silhouette continuity at 560-700 m. Also inspect the 120 m mesh/card switch and 3.2/8.5 km mass switches, including sideways camera movement across block seams. Tier geometry is discrete; this change does not add temporal dithering.
5. Check forest edges, glade floors, narrow roads, scrub beside fields, origin rebases and sector changes. Confirm the existing Scout/Swift clearance checks and that old meshes/textures are released.

## Reproduce

Run `Tools/ForestHlod/run.ps1` (or `-SkipRender` for compilation, captures and seed checks). It uses installed Python 3.13 with `-I`, headless Blender 5.2 and Unity's bundled .NET SDK. Outputs stay under `Temp/forest-hlod` and `Tools/ForestHlod/Review`. The baseline is read with `git show 788df6f`; no branch switch, Unity launch, network or package install occurs.

`python -I Tools/ForestHlod/make_palette.py` regenerates the embedded atlas and measured palette from existing impostor photographs. The old `ForestCrown.bytes` resource is retained for baseline reproduction; the revised runtime no longer loads it. Unity API usage follows the existing FarTerrain/FarScatter source, with the installed Unity assembly check as the version-specific authority.
