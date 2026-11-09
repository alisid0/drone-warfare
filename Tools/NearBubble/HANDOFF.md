# Near-flight detail bubble outside sectors (task 24)

Branch `codex/near-bubble`, from `ed6a3eb`. Written by Claude standing in for Codex. Unity, Blender and the GPU were not used; the C# was compiled against the Unity 6000.6.7f1 assemblies and the shader hook was compiled with the editor's own D3D11 compiler.

## Approach, and why

The streamed tiles' shader already has the "cheap route" the brief describes. It has a world-space detail layer, driven by the control map:
- photo and crop arrays;
- crop rows along each field's angle;
- sprayer tramlines and hay windrows.

What blurs at 50 m is the data under that layer:
- the 2 m colour (field strips, hedges, road edges, ruts and gardens are 1-4 m wide);
- the 2 m control, which blends textures across 2 m at every boundary;
- each tile's outer 32 m, where colour falls back to the 4 m regional page and the detail layer fades out. That's a 64 m soft band every 512 m (`Review/tile-edge-100m-*`).

Procedural textures can't restore boundaries the 2 m data doesn't hold. But the painter is analytic, and every mask carries a pixel footprint, so running it at a finer footprint gives exact sharper ground. So I followed ChatGPT's spec, with these choices:

- **Pages.** A 4 by 4 toroidal array of 256 m pages follows a low camera. Each page holds:
  - 0.5 m colour: BC1, three mips, 8 m gutters (the regional pages' format and encoder);
  - 1 m control: RGBA32, the same format as the tile's control, and the sector splat's own resolution.
- **Workers.** Pages are painted by the tiles' own `Paint` and `Control`, on a worker.
- **Main thread.** Uploads use staging textures and `CopyTexture`, one stage per frame, and never Apply an array.
- **Shader.** The near layer sits in the existing hook. Colour and control blend by the page weight, and nothing switches.
  - Pages are tile-independent, so inside the bubble they run straight through tile edges. Chunks next to a live tile show the same near colour.
  - Pages fade out by screen footprint between 1.5 and 2.5 m, where the tile's full mip chain carries the same colour.

## What changed

- **`FarDetailNearData.cs` (new).** The page builder:
  - `FarDetailData.NearPixels` paints colour rows on two threads, as the world picture does.
  - It culls vectors once per page.
  - One instance is reused for every page, so a page allocates nothing on the worker.
- **`FarDetailNear.cs` (new).** The bubble:
  - **Window.** Centred up to 160 m toward the camera's ground focus. It moves a page at a time with a 0.625-page margin, so the centre stays at least 352 m inside.
  - **When it builds.** Only below 800 m camera height, and only while the camera's own tiles are all live (`CameraMissing == 0`). Nearest page first.
  - **Fades.** A page appears only when complete, then fades in over 0.6 s. A slot's old page fades out over 0.6 s, from its current weight, before the slot is overwritten. Nothing pops.
  - **Switch.** `FarDetailNear.Enabled` lets an experiment hide the bubble.
- **`FarDetail.cs`.**
  - Creates and clears the bubble.
  - Measures the camera's height above ground in `Focus`.
  - Arbitrates frames: a waiting near stage gets every third frame, but only once the camera's tiles are live, and the regional queue goes first. Each frame still runs at most one texture stage.
  - Exposes `NearCommitted` and `NearWorkerMs`.
- **`FarDetailTiming.cs`.** Adds the stages `NearPlan`, `NearPixels`, `NearApply`, `NearCopy` and `NearPublish`.
- **`FarDetailRegionData.cs`.** The BC1 encoder gains a form that writes into a caller's buffer. Regional output is unchanged.
- **`FarDetailRegional.hlsl`.** Adds the near globals, readiness with fade-in and fade-out, 16 m page-edge bands (as `FarRegionWeight`), footprint coverage and the colour sample. The sample borrows `sampler_FarRegionAlbedo`, so no pass gains a sampler.
- **`FarDetailSurface.hlsl`.**
  - `GroundDetail` holds the existing four-control bilinear surface blend.
  - The tile path blends the tile's detail (with its edge fade, as before) with the near detail (without it) by the page weight.
  - Under a full page, the edge band's regional sample is skipped.
  - The chunk path lerps in the near colour.
- **Not changed.** `FarDetailShaderSetup` doesn't regenerate either hook file, so no setup rerun is needed.

## Budget

- **Texture memory: 8.33 MB.**
  - Per page: 194,208 B of colour plus 295,936 B of control.
  - 16 pages plus one staging texture of each.
  - The cap was 64-96 MB.
- **GPU: not measured.** My estimate is +0.05 to 0.15 ms at 1080p. Against ps_5_0 with D3DCompiler_47 (`Review/shader-check.txt`):
  - The tile variant goes from 534 to 1068 instruction slots, because the detail block exists twice, as separate dynamic branches.
  - A pixel inside a ready page skips the tile's control block, and runs about 60-100 more instructions plus one BC1 sample.
  - The chunk variant goes from 116 to 258 slots. Pixels coarser than 2.5 m exit after the footprint test.
  - Samplers are unchanged: 5.
- **Main thread: no stage over 2 ms expected (not measured).** Each stage is one of:
  - `SetPixelData` (at most 296 KB);
  - `Apply` on a staging texture (194 KB or 296 KB);
  - one GPU-side `CopyTexture` per mip;
  - a 16-vector publish.
- **Worker: 122-256 ms per page** under .NET 8, against 65-103 ms for a full 2 m tile build at the same places (`Review/review.json`). Mono in the player will be slower; my guess is 2-3x.

## Seams and colour

- **Sector edge.** Pages exist only on tiles `FarDetail.Eligible` allows, so the sector edge and its 1.5 km ring are untouched.
- **Page edges.** Gutters make two ready neighbours identical at their shared edge. A missing neighbour gets a 16 m blend to the tile (`Review/page-edge-100m-neighbour-missing.jpg`).
- **Colour agreement.** I compared the page's 2 m mip, decoded from BC1, with the tile's 2 m colour at the same texel centres:
  - mean shift: under 0.7 sRGB levels per channel;
  - per texel: median 2 levels, 99th percentile 13-19;
  - the differences sit on 1-2 m strips, hedges and ruts, which the page resolves and the tile averages;
  - BC1's own error: 1.3-1.4 levels.

## Evidence (`Review/`)

- **Renders.** `<scene>-<50|100|300>m-<before|after>.jpg`, for a field, a road and a village edge.
  - Top-down, at the pixel size of a 1080-line, 45 degree camera.
  - Each is a CPU model of the hook: tile colour, regional edge band, detail arrays (the photos tinted as setup does, Farmland's crop maps), tramlines and windrows.
  - Albedo only: no lighting, normal maps, fog or TAA.
  - The scene windows avoid tile edge bands, so they compare 2 m with 0.5 m alone.
- **Other files:** `tile-edge-100m-*`, `page-edge-100m-neighbour-missing`, `sheet.jpg`, `review.json` and `shader-check.txt`.
- **Reproduce:**
  - `py -3.13 -I Tools/NearBubble/run.py` takes about 45 s, uses one process and no GPU.
  - `py -3.13 -I Tools/PredictiveStreaming/run.py api` gives the API compile, which passes with zero warnings.

## Build checklist (Claude)

1. Merge, then import. In the console, check that `DroneSandbox/GroundDetail` compiles both variants with no errors.
2. Fly outside a sector at 50, 100 and 300 m over a field, a road and a village. Compare with hd178's `91_flight_*_50m`.
   - Pages should fade in about 0.6 s after arriving.
   - There should be no line at page or tile edges inside the bubble.
   - Turning to look behind shouldn't pop: pages leaving the window fade out.
3. Log `FarDetail.NearCommitted` and `FarDetail.NearWorkerMs`, and the `Near*` entries in `FarDetailTiming.Breakdown`.
   - No stage should exceed 2 ms.
   - The worst-frame stage shouldn't be a `Near*` one.
4. GPU A/B on one frame: `FarDetailNear.Enabled = false`, then `true`. Expect at most 0.15 ms difference (budget 0.5).
5. Memory: about +8.3 MB of textures, all allocated at sector build.
6. At 4x, pages lag behind the drone and the 2 m tiles show. Tile prefetch should be unchanged: near stages yield whenever the camera's tiles are missing.

## Not done, or not verified

- **Grass and shrub geometry** (optional in the spec) is not done, given the GPU measurement under way. Streamed tiles are meshes, not terrain, so it needs its own instanced path.
- **The ring near sectors.** From reading the code: the first 1.5 km beyond a sector is Backdrop's ring, drawn with the 13.7 m world picture (`Backdrop.Build`). Tiles, and so pages, are ineligible there, so 50 m views near a sector stay soft. Bringing that ring onto the chunk material would need care at the collar.
- **The tile edge band outside the bubble** is unchanged.
- **Unverified:** HDRP look, GPU time, Mono worker time, and real upload and hitch costs.
