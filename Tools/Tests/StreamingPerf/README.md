# Streaming performance handoff

Base: `6611380`. Branch: `codex/streaming-perf`. Unity was neither opened nor run.

Changes:

- Tile mips are generated on the data worker, uploaded one level per frame, and applied without native mip generation. Pooled textures are allocated before flight. Collider baking runs on a worker using Unity 6.6's `EntityId`; publication waits without blocking, then activation uses the cached bake with identical cooking options. Teardown waits for any native bake before destroying its mesh.
- Regional copies are at most 128 rows per frame, aligned to BC1 blocks. Invalidation, copying and publication have separate stages. Mesh uploads are explicit, timed stages. Tile arrays and roughness buffers are reused; worker closures no longer allocate during idle regional ticks or ordinary upload stages.
- One immutable snapshot is shared per world grid identity, including districts, cover index and villages. Sector coarse heights remain private. Same-seed regeneration invalidates the snapshot. Original chunk arrays are retained directly instead of read back from the native mesh.
- Startup shows a 256-square cover preview and an animated “Preparing distant landscape” indicator while the full 4096-square image paints with at most two workers. Replacing a world cancels and drains the old painter before allocating another full image. The same texture object is resized at publication, preserving Backdrop/material bindings. The current sector collar is painted before a single final Apply. Failed publication releases the result instead of retrying every frame.
- The field exclusion pass rejects non-farmland first, before shoreline, village, works and slope queries. Field geometry is unchanged.

Measured data-path comparison (one process at a time, .NET 8 harness, seed 1200, processor count 2; **not Unity/player timings**):

| Stage | Before | After |
| --- | ---: | ---: |
| Cold snapshot | 177.09 ms | 179.06 ms |
| Repeat snapshot | 40.59 ms | <0.01 ms |
| Picture on caller | 2607.06 ms, full paint | 6.03 ms, preview only |
| Full picture paint | 2607.06 ms, caller blocked | 4181.80 ms, background, explicit two-worker cap |
| Lake District layout, seed 1296 | 795.20 ms | 520.74 ms |
| Lake District exclusion map | 360 ms | 152 ms |
| Reservoir layout, seed 1333 | 1091.91 ms | 371.20 ms |
| Reservoir exclusion map | 621 ms | 15 ms |
| Regional paint + BC1 | 111.47 ms | 110.61 ms |
| Working set at picture sample | 198 MiB | 191 MiB |

The worker cap trades completion latency for player CPU capacity; the before version used unrestricted Parallel.For. Subsequent after runs varied (Lake layout 498–568 ms; Reservoir 369–389 ms). These measurements do not establish a new total sector build time or a player startup time under 3 seconds.

The exact 4096-square image hash is `24C7EF4116C25C0C` in both revisions. Lake District retained 358 fields, hash `2C94ADB5891A6987`; Reservoir retained zero fields, hash `CBF29CE484222325`. The preview constructs zero farm districts. Full painting constructs 3933 districts, measured at 442.66 ms summed construction CPU time, overlapping between workers. This does not account for the full old player startup delay by itself.

Code audit: this startup path has **no native PerlinNoise inside Parallel.For**, **no startup BC1 encoder**, and **no full texture readback**. Native Perlin remains in the bounded main-thread detail roughness sampler. The readable picture still requires one transient 64 MiB managed result and one CPU-to-native copy; neither is duplicated by the new scheduler. The collar reads only its small patch. New timers report the copy and native Apply separately, plus chunk geometry, mesh APIs, collider cooking, pool/array setup and snapshot costs.

Compile/checks: complete modified consumers compiled against installed Unity **6000.6.7f1** assemblies, **C# 9 / .NET Standard 2.1**, zero warnings/errors. Managed checks cover exact output hashes, same-seed snapshot invalidation, sector coarse-height ownership, recycled tile geometry/controls/mips, odd-size linear mip filtering, cancelled paint, all 81 regional pages, 811 block-aligned GPU-copy boundaries, denied commit frames, teleport during a partial upload, clearing in-flight work, serialized picture cancellation/replacement, and upload-failure cleanup. The engine wrappers validate lifecycle only; they cannot time native graphics or physics.

Reproduce sequentially, without Unity or Blender:

```powershell
py -3.13 -I Tools/Tests/StreamingPerf/prepare.py before --run
py -3.13 -I Tools/Tests/StreamingPerf/prepare.py after --run
```

All generated projects and baseline copies go under `Temp/streaming-perf`. No package downloads are needed. The harness reuses tracked scaffolding from LandmarkRealism/WorldLandmarks/WorldSectors and does not depend on the pre-existing staged FieldsVillages harness.

Exact log additions for Claude (no AutoTest or World edits included):

```csharp
log.WriteLine($"World build stages: {FarTerrain.BuildTimings}");
log.WriteLine($"World picture stages (pending {FarTerrain.PicturePending}): {FarTerrain.PictureTimings}");
log.WriteLine($"World detail stage maxima: {FarDetailTiming.Breakdown}; collider bake worker worst {FarDetail.WorstBakeMs:F3} ms");
log.WriteLine($"Field layout stages: {MapLayout.Timings}; {MapLayout.FieldTimings}");
```

Place the first/fourth after the corresponding sector build. Log picture stages again after `PicturePending` becomes false (the world-flight report is a suitable existing point). Place the detail stage line beside the existing low 4x flight report, after its existing `FarDetail.ResetMetrics()`. Keep the existing whole-frame mean/worst/over-2-ms test: the stage counters supplement it. Each stage reports its maximum and call count; GC frames mean overlapping collections, not proven GC causation. District CPU time is summed per shared world snapshot, not a main-thread duration.

Still requiring Claude's Unity run: identify the actual native stage behind hd144–hd148's aggregate spikes, verify worst commit <2 ms, check final picture publication/collar and spinner visually, and measure synchronous `world` plus asynchronous copy/Apply timings. Drivers can still stall an indivisible Apply or CopyTexture; source inspection and mock timings cannot guarantee the native limit. The 64 MiB final copy/Apply is a one-off main-thread publication and may still produce a measured hitch. Forest/tree files, Ground, Atmosphere, HUD/game flow, landmark slab caps and LOD fixes are untouched.

API references checked: [BakeMesh thread safety and reuse conditions](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/physics/bakemesh), [Unity physics binding source (EntityId)](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Modules/Physics/ScriptBindings/Physics.bindings.cs), [texture source (Reinitialize and SetPixelData)](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Runtime/Export/Graphics/Texture.cs), [mesh source (MarkDynamic and UploadMeshData)](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Runtime/Export/Graphics/Mesh.cs). The installed assembly compile is the authority for this project's 6.6 signatures.
