# Detail generation measurements

Seed 1200; Release .NET 8, optimized JIT (`DOTNET_TieredCompilation=0`). One warm build and four timed worker builds per area. Heights, normals, skirts, 16 m collision data and the full 2 m texture are included. Native-noise input sampling, main-thread upload and physics cooking are excluded.

| Area | Generation median (ms) | Max (ms) | Chunk clipping (ms) | Edge error (m) |
| --- | ---: | ---: | ---: | ---: |
| farmland | 18.89 | 19.99 | 25.36 | 0.00000 |
| village | 35.82 | 37.20 | 1.44 | 0.00000 |
| river-crossing | 41.64 | 42.66 | 1.60 | 0.00000 |
| forest-edge | 15.38 | 18.70 | 3.82 | 0.00000 |

Each tile: 17,157 vertices, 34,816 triangles, 1,266,604 bytes of managed arrays, and an estimated 1,107,445 bytes of GPU payload (32-byte vertices, 16-bit indices and RGBA32 mip chain). Neighboring detail-edge sample error was zero.

737,955 data invariants passed. Streaming simulation: 12,351 virtual frames, maximum 25 live tiles, 279 publications before the reset, and 0 missing camera-tile frames on the warmed flight.

Chunk timings include the first call/JIT and are not upload timings. State-machine tests synchronize workers between virtual frames. The reference Perlin implementation substitutes for native Unity noise in both image painters. These measurements do not certify Unity commit time, GPU time, actual VRAM, collider cooking, visual activation quality or player frame-time p95. See the parent checklist for the remaining gates.
