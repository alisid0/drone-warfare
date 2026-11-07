# Predictive streaming handoff (task 23)

Branch `codex/predictive-streaming`, from `e666e0f`. Unity was not opened or run. Every runtime script compiles against
the installed Unity 6000.6.7f1 engine modules plus the HDRP/Core RP/Input System/UI package assemblies (copied from the
main checkout's `Library/ScriptAssemblies`), C# 9, zero warnings: `py -3.13 -I Tools/PredictiveStreaming/run.py api`.

## What changed

**FarDetailRoute.cs (new, pure data).** The detail streamer's planner:
- The 600 m ready zone round the camera's ground focus is due now, and so is the camera eye's 500 m neighbourhood.
- The followed drone's 600 m zone is walked every 64 m along its route: `MoveTarget`, then `Waypoints`.
  - It reaches 2-3 km: `clamp(speed x time multiplier x (p99 generation + 3 s), 2 km, 3 km)`. At 4x that gives 2 km.
  - Each tile is due at the real second the zone first covers it, minus one 64 m step.
- Then the old 1.5 km camera capsule (2), then 1.5 km round the camera (3).
- Sorting is by deadline, then class, so camera tiles win ties.
- An unobserved drone (feed hidden, and away from the camera) sorts after everything the camera wants.
- The list is capped at 25. The planner also records the followed eye's needs, for ReadyAhead.

**FarDetail.cs.**
- **Two prepare lanes.** Each samples roughness within the frame budget and builds its tile on a worker, into a
  hidden slot's own arrays, while the previous tile commits. The commit worker now only cuts far chunks.
- **Packed uploads.** Upload stages run back to back while the next stage's learned cost still fits the same 0.35 ms
  budget. The learned cost is a decaying maximum, and a stage's first use always runs alone.
  - These still take a frame of their own: Apply, mesh upload, chunk buffer allocation, the bake dispatch and
    publication.
- **Eviction.** Passed tiles are kept for 20 s. Under the 25-tile cap, the least recently wanted tile goes first.
- **Regional pages.** A waiting regional page takes every third frame. An idle regional queue can start its worker on
  any frame (`FarDetailRegional.Pending`, and `Next`).
- **FarChunkUpload.** Gains `IsAllocation`, `Bytes`, and a general sliced `Step(Mesh, ...)`. The chunk path's stages
  are unchanged.

**ForestHlod.cs.**
- **Canopy prefetch.** With every visible block covered, it builds missing coarse blocks within 4 km of the followed
  eye, and within 2 km of its route out to the prefetch lead plus a block. The nearest along the route goes first,
  before refinement, and it never cancels one. Blocks in that zone are not evicted.
- **Reused build scratch.** The flight builder reuses one scratch (arrays, lists and maps, allocated with the sector),
  marking entries with a generation stamp. Before, each refinement allocated about 4 MB of new arrays.
- **Sliced mesh upload.** Canopy meshes fill through FarChunkUpload's 1024-vertex slices, with bounds taken from the
  data. This replaces the old single-slice upload (`mesh.triangles =` and `RecalculateBounds`, hd167's worst was
  3.3 ms). Index buffers are now always 32-bit.

**FarScatter.cs (scheduling and timing only; house code untouched).**
- Each camera reuses its tree-selection buffers and a pool of full-size draw batches:
  - planes, the cell snapshot, the visible list, the bins and the instance lists;
  - the batches return to the pool when their selection is replaced.
- The cell builder reuses one tree list.
- My estimate from reading the code: about 0.5 MB of garbage per selection, about 50 selections a second in hd167's
  flight (2728 in 52 s). This wasn't measured.

## The `FarDetail.ReadyAhead` contract

`public static float ReadyAhead` is updated every `FarDetail.LateUpdate`, and is valid from the next frame.

- **What it measures.** The real seconds before the followed eye's 600 m ready zone reaches the nearest eligible tile
  that isn't live. The tile counts if it is within the zone now, or on the eye's projected route within the prefetch
  lead.
- **Followed eye.** `DroneFeed.Source`, that is the selected drone, if it is alive. Otherwise the camera's ground
  focus.
- **Route.** The drone's move target and queued waypoints. With no route, the line of its velocity (for the camera
  focus, the camera's velocity).
- **Speed.** The eye's current real-time ground speed, smoothed over about 4 frames.
- **Values.**
  - 0 means a needed tile in the zone is missing now.
  - +infinity means everything needed is live, or the eye is still with nothing missing in its zone, or no world is
    streaming.
  - Route tiles count only while the eye moves faster than 0.5 m/s.
- **Precision.** Distances are measured from the last 0.2 s plan, so the value errs low by up to one 64 m step.
  Changing the followed drone, or a camera jump of over 200 m, replans at once.
- **Ceiling.** Within the 25-tile cap, the replay's steady 4x value is about 6 s, and about 25 s at 1x. A throttle
  threshold of 1.5 s (halve the multiplier) and 0.5 s (drop to 1x), restoring above 3 s, worked in the replay.

## New per-frame readings (for the spike log)

**FarDetail**
- `LastFrameMs`: the last LateUpdate's main-thread time.
- `UploadBytes`, `WorstUploadBytes`: bytes handed to Mesh/Texture2D (Set* copies plus Apply/UploadMeshData).
- `StagesThisFrame`.
- `FollowedReady`, `CameraMissing`.
- `FollowedFrames`, `FollowedMissingFrames`, `CameraFrames`, `CameraMissingFrames`: since `ResetMetrics`, for the 99.9%
  gate.
- `GenerationP99Ms`: start to finished data, over the last 64 tiles.
- `Preparing`, `PrefetchMetres`.

**ForestHlod**
- `LastFrameMs`: every camera, this frame.
- `UploadBytes`, `PrefetchMissing`, `PrefetchBuilds`.

**FarScatter**
- `PlacesMs`: the landmark and settlement passes this frame. These run outside `MainThreadMs`.
- hd167's 80 ms and 55 ms frames had scatter at only 0.6 and 1.2 ms, so log this to place them.

Suggested AutoTest lines (not edited here), in `ContinuousFlight`:

```csharp
// in the over-25 ms append, after the scatter figure:
$" detail {FarDetail.LastFrameMs:F2} ms/{FarDetail.StagesThisFrame} stages/{FarDetail.UploadBytes / 1024} KB, canopy {ForestHlod.LastFrameMs:F2} ms/{ForestHlod.UploadBytes / 1024} KB, places {FarScatter.PlacesMs:F1} ms"
// after the flight:
log.WriteLine($"Continuous flight readiness: followed tile missing {FarDetail.FollowedMissingFrames}/{FarDetail.FollowedFrames} frames, " +
              $"camera 600 m zone gaps {FarDetail.CameraMissingFrames}/{FarDetail.CameraFrames} frames, generation p99 {FarDetail.GenerationP99Ms:F0} ms, " +
              $"prefetch {FarDetail.PrefetchMetres:F0} m, worst upload {FarDetail.WorstUploadBytes / 1024} KB/frame, canopy prefetch builds {ForestHlod.PrefetchBuilds}");
```

## Offline replay

Run it with `py -3.13 -I Tools/PredictiveStreaming/run.py sim`. The full output is in `Results.txt`.

**What it flies.** hd167's route at 12.5 ms frames:
- base, then the wood and the rail junction at 1x;
- the works and the village at 4x;
- the autotest's 50 m, 300 m and 2.5 km stop views at each stop.

**What runs.**
- After: the production `FarDetailRoute`.
- Before: a port of e666e0f's planner.
- Both lane/commit/upload state machines are ports of FarDetail, with fake timed work and the regional queue.

Readings exclude the stop views and 2 s after each stop.

| Scenario | Scheduler | Drone tile ready (4x) | 600 m zone complete (4x) | 250 m core gap frames (4x) |
| --- | --- | ---: | ---: | ---: |
| A. Brief: generation p99 ~391 ms | before | 100% | 34.3% | 727 / 2470 |
| A | after | 100% | 100% | 0 |
| B. hd167-calibrated: mean 400, p99 ~597 ms | before | 84.9% | 18.8% | 1484 / 2470 |
| B | after | 100% | 100% | 0 |
| C. B at 8x (stress) | before | 24.8% | 4.9% | 927 / 1072 |
| C | after | 100% | 98.2% | 0 |
| C | after + illustrative throttle | 100% | 100% | 0 |

**Readings for B.**
- At 1x, both schedulers are 100%.
- Mean frames from a transaction's start to its publication: 82 before, 24 after.
- Modelled streaming main-thread mean: 0.065 ms before, 0.083 ms after. The worst is 0.38 ms in both, within the
  budget plus one stage.
- Prepared tiles discarded when they dropped out of the cap: 57 of 236.
- No regional page was starved.

**Calibration.** B's "before" reproduces hd167's failure: the drone's own tile missing in about 15% of 4x frames, against
9 of 191 one-second samples, mostly at 4x. Scenario A, the brief's latency, is kinder than hd167 was.

## Budget and memory

- `MaxTiles` stays 25. The pool grows from 26 to 28 slots: about 1.2 MB more textures, plus their meshes once used.
- The canopy scratch is about 6.4 MB, allocated once with the sector. Before, about 4 MB was allocated per refinement.
- Per-camera batch pools hold about 2-4 MB.
- Canopy prefetch keeps about 20-30 extra coarse blocks cached, about 100 KB each.

## Not verified

None of this was run in Unity or HDRP. Each of the following still needs measuring:
- **Per-frame costs.** p95/p99, the worst commit under 2 ms, `meanCommit` under 1 ms and FarScatter's mean under 1 ms.
  Packing uses more of the 0.35 ms budget per frame, and canopy prefetch uses its 0.20 ms cruise budget more often.
- **Rendering.** Draw counts, GPU time, and that sliced canopy meshes and pooled tree batches render exactly as before.
- **Worker CPU.** Two tile builds now run at once, beside the regional and picture workers. Check this on the laptop.
- **GC.** The selection and scratch reuse should cut collections, but that isn't measured.
- **Replay assumptions.**
  - The upload costs per stage are my assumptions; the per-stage maxima come from hd167.
  - The origin is taken as the base.
  - Collider-toggle frames aren't modelled.
- **The 80 and 55 ms frames are not fixed.** They are outside the systems measured so far.
  - **Likely causes:** first-use PlaceLit material variants in `LandmarkInstances.Load`/`Adapt`, which are created on
    first sight; `LandmarkHlod.Upload`, a single-frame compound mesh with RecalculateBounds; and `SettlementHlod`
    (the other session's file).
  - **Next step:** log `PlacesMs` to confirm, then prewarm those materials and slice that upload.
- **Remaining garbage.** `FarDetailChunks.Cut` still allocates about 1 MB per tile on its worker.
