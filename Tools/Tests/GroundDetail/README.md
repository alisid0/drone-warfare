# Ground detail hand-off

Branch `codex/ground-detail`, based on `788df6f`. Unity was not launched and no Library was imported. Ground, Farmland, ForestHlod, ForestPattern and FarScatter are unchanged.

The tile mesh now samples the sector's photo textures and all five Farmland crop textures at their original physical scales. Crop direction is stored per field, independently of tile UVs. The 2 m colour map supplies area-filtered cover, boundaries, roads and banks; a separate discrete map selects the fine surface and row direction. Both maps have matching world-sampled border texels. Texture filtering blends evaluated surfaces rather than interpolating IDs or angles. Photo normals use the same sources as Ground, with actual mesh tangents. Hay windrows and paired tramlines have physical widths and footprint filtering. Detail fades to its mean between 1 and 3 m per screen pixel, avoiding distant sampling cost and shimmer.

This is a raster HDRP Lit adapter, generated from the installed **17.6** shader and LitData sources. It changes the surface inputs before HDRP applies decals, lighting and fog; it retains the standard raster passes. It adds no package. The checked-in generated sources include Unity's notice. The editor regenerates them from the package's resolved filesystem path, validates a standard Lit material first, then changes its shader and binds the arrays. There is no keyword validation after the swap. Ray tracing is not implemented for these tiles.

Ground photos are tinted exactly as Ground.BuildLayers. Palette values are measured in linear light from those photos and the actual Farmland.CropColor output. Editor setup rejects an imported/resampled source whose mean differs by more than 5%. Terrain mixtures retain continuous macro colour, while their fine texture/normal uses the dominant ground layer; this is not a full five-way TerrainLit splat at every fragment. Crop microtexture is shared with Farmland; the tile's procedural windrows/tramlines do not reproduce each sector field's entire growth/headland map.

The far picture uses the same painter at its own pixel footprint, including true vector widths. Finite-strip coverage prevents a narrow river from becoming half a pixel wide. A **256 m** collar starts from the actual sector edge splat weights, with the first 32 m held at the edge colour, then blends to the world picture. The old patch is restored before moving/rebuilding the sector. This needs a readable world texture: **64 MiB additional persistent CPU storage**, plus its existing GPU texture. Collar updates and shared array creation occur during sector/setup work, outside flight streaming.

## Render review

[Overview](Review/comparison-sheet.png) · [Sector edge](Review/sector-edge.png)

| Ground | 50 m equivalent | 300 m equivalent |
|---|---|---|
| Wheat | [Pair](Review/wheat-50m.png) | [Pair](Review/wheat-300m.png) |
| Ploughed soil | [Pair](Review/ploughed-50m.png) | [Pair](Review/ploughed-300m.png) |
| Village | [Pair](Review/village-50m.png) | [Pair](Review/village-300m.png) |
| River crossing | [Pair](Review/river-crossing-50m.png) | [Pair](Review/river-crossing-300m.png) |

These are **CPU albedo renders**, with a 45-degree vertical FOV, 16:9 aspect and a top-down footprint. They are not Unity screenshots. They use identical exposure and linear filtering. The reference side runs the actual WorldSectors/MapLayout layouts and Ground height/splat painters at 1025/2048 resolution. Crop colours, detail formulas and tint RNG come from Farmland. Fields are nearby examples at the same scale, not the same parcels. Reference water uses vector footprints; props, water lighting, geometry shadows and normal-map lighting are omitted. Managed reference Perlin substitutes for Unity native Perlin. The 300 m tile view is shifted slightly where necessary to keep its footprint inside the exported tile.

The edge comparison samples **8 m strips on all four sides**: actual terrain splat colour inside versus the final, bilinearly filtered world texture outside. Maximum relative linear RGB-channel differences are **0.63–0.85%**, below 5%; raw values are in [edge-metrics.json](Review/edge-metrics.json). This is a colour check, not proof that the sector is visually undetectable in an HDRP player. The overview still exposes the different parcel layouts and the grass margin around sector fields.

No Ground/Farmland change is needed for the measured palette agreement. For Claude, if the remaining parcel outline must disappear too: `MapLayout.PlanFields` currently obtains sector plots from `FieldParcels`, while the world uses `FarDetailData.FieldAt`'s district/strip partition. A shared world parcel plan must supply **the same boundaries, crop and orientation** to both; changing a tint in Ground or Farmland cannot reconcile them. `MapLayout.Edge = Ground.Size / 2 - 15` also deliberately stops sector crops inside the boundary. That work is outside the permitted files and has not been done.

## Measurements and reproduction

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/GroundDetail/run.ps1`. It uses Python 3.13 with `-I`, the installed Unity .NET SDK, and Windows' D3D11 compiler. It installs nothing. Generated compilation scaffolding stays in `Temp/ground-detail`; large raw render inputs are ignored. `-CompileOnly` checks assemblies; `-ReferencesOnly` rerenders existing cases after reference-only edits.

- Runtime helpers, their terrain consumers, and the new editor setup helper compile against Unity 6000.6.7f1 assemblies with **C# 9, warnings as errors, zero warnings/errors**. Unrelated services and HDRP setup calls use wrappers; this is not a complete game build.
- The actual surface sampler compiles as D3D11 `ps_5_0` with no diagnostics. Full HDRP ShaderLab pass/variant compilation still requires Unity.
- The production tile workers pass gutter identity, tangent basis, index/raycast, clipping and finite-width coverage checks. Crop means are checked against newly generated reference textures. Repainting the same sector restores the patch and produces identical bytes.
- The production streaming state-machine simulation covers **12,351 frames, 274 commits, 25 maximum live tiles, zero camera-tile misses**. It synchronizes worker completion between virtual frames, so it is not a frame-time/throughput benchmark.
- Warm worker generation: wheat **125 ms**, ploughed **126 ms**, village **136 ms**, river crossing **193 ms** median; worst measured case **195 ms**. World-picture painting took about **2.2 seconds** with managed reference Perlin. See [metrics.json](Review/metrics.json).
- Per-tile estimated GPU payload is **1.58 MiB**, including tangents, both maps and colour mipmaps. All 26 pooled slots plus the shared 512-square arrays total **61.0 MiB**. The simulation's peak clipped-chunk payload is **3.42 MiB**, giving about **64.4 MiB incremental payload** on that route. This excludes driver overhead, retained native buffer capacity and existing far-world resources; it is not measured VRAM or a universal worst case.
- Streaming retains its 0.8 ms work budget, 25-live cap and 16 m collider. Tangents and the extra control-map SetPixelData/Apply are separate upload stages; no new object/texture allocations occur on publication. Largest new upload is about 268 KiB, below the existing index upload. **Main-thread mean/worst and frame-time p95 are unmeasured here.** hd139's numbers are the baseline, not results for this branch.

## Claude's build checklist

1. In the integration checkout, run `ProjectSetup.Configure`, or run `FarDetailShaderSetup.Build` after the normal project setup. This must precede the player build: it writes `Materials/FarDetailLit.mat`, `Textures/GroundAlbedo.asset` and `Textures/GroundNormal.asset`. Plain `BuildTools.BuildWindows` does not call setup. Missing material setup fails explicitly at tile initialization.
2. Confirm the palette validation passes; shader import has no errors; generated raster passes compile in the player. Check GBuffer, Forward, depth/normal and motion-vector passes, photo normal orientation, sRGB array handling and distant mip colour. The adapter uses the installed HDRP version; review its guarded generation if HDRP changes.
3. Run the normal autotest at the recorded hd139 settings/resolution/GPU. Recheck **mean <=1 ms, worst <2 ms, p95 <16.7 ms**, 25 live tiles, and **ring VRAM <200 MB** over the same 15 km flight at 4x. Record Apply, mesh uploads, collider cooking, GC, GPU sample cost and actual texture/buffer allocation separately.
4. Capture Unity 50 m and 300 m views of wheat, ploughed ground, village ground and a river crossing. Inspect row direction, texture/normal scale, road and hedge coverage, 512 m boundaries and tile arrival/eviction. Compare the detailed sector at identical exposure and sun angle. The CPU pairs do not validate lighting or normal maps.
5. At 2.5 km, compare matching inside/outside edge strips in the HDRP capture. Preserve the measured colour difference and the full view: report parcel-layout visibility separately. Test a wet/rocky sector too, not only the farmland case measured here.
6. Rebuild the same sector, travel to a new sector and return, then regenerate the world seed. Confirm the old collar is restored, no colour stamps accumulate, native resources are released, and the extra readable world texture is acceptable in the CPU-memory capture.
