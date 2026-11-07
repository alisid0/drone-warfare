# Forest submissions against 788df6f

Offline production-source harness; 1600 x 900 review cameras. Triangle/draw submissions are not measured GPU time.

| View | Cards before / after | Forest triangles before / after | Total draws before / after | Forest texture MiB before / after |
|---|---:|---:|---:|---:|
| 300m | 3,263 / 2,998 | 131,752 / 79,647 | 20 / 14 | 5.42 / 5.51 |
| 2500m | 0 / 0 | 240,484 / 149,277 | 10 / 10 | 10.83 / 11.01 |
| 9000m | 0 / 0 | 18,650 / 18,621 | 37 / 37 | 0.75 / 0.85 |
| edge | 0 / 0 | 122,196 / 79,405 | 6 / 6 | 6.75 / 6.86 |

The same seed-1 interior forest is at (16500, -4500). Edge target: [14852, -6532].

Fine blocks: 32 m interiors, 16 m glade rims, 8 m external margins; 516 x 516 RGBA32 mipmapped crown texture (512 interior pixels plus a two-texel gutter). Distant blocks retain 64/128 m geometry; texture interiors are 128/64 pixels plus gutters. Each occupied block is one opaque draw, with no alpha test or shadow casting.

The builder retains the 0.7 ms main-thread slice and yields within sampling, stamping and skirt loops. Native upload and final array copies remain indivisible; profile these in HDRP.

Resident estimates in metrics.json assume 116 bytes/vertex (source, optional bubble copy, readable mesh and GPU mesh), 12 bytes/index (source/mesh/GPU) and RGBA mipmaps. They exclude managed object overhead, construction dictionaries/lists, driver overhead and temporarily overlapping old/new blocks.

Required Unity gates remain 114 fps sandbox, 121 fps wide, low-flight p95 <= 12.2 ms at 1600 x 900 on the RTX 3050 laptop. No new Unity FPS or GPU timing is claimed.
