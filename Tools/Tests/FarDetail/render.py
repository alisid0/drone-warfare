"""Render exact harness pixels as labelled side-by-side review sheets."""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

out = Path(sys.argv[1])
metrics = json.loads((out / 'metrics.json').read_text())
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 20)
small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 15)
sheets = []
for tile in metrics['tiles']:
    name = tile['name']
    sheet = Image.new('RGB', (1080, 640), '#172025')
    draw = ImageDraw.Draw(sheet)
    draw.text((22, 14), name.replace('-', ' ').title() + ' | 512 x 512 metres', font=font, fill='#f2f2e8')
    draw.text((22, 48), 'FarTerrain world picture | 13.67 m / pixel', font=small, fill='#bec8c9')
    draw.text((558, 48), 'Streamed ground texture | 2 m / pixel', font=small, fill='#bec8c9')
    for index, kind in enumerate(['far', 'detail']):
        image = Image.open(out / f'{name}-{kind}.ppm')
        sheet.paste(image.resize((500, 500), Image.Resampling.NEAREST), (22 + index * 536, 80))
        image.save(out / f'{name}-{kind}.png')
    draw.text((22, 594), f"Seed 1200 | world tile ({tile['tileX']}, {tile['tileZ']}) | north up | same footprint", font=small, fill='#d6dfdf')
    draw.text((22, 616), 'CPU harness, managed reference noise; these are texture previews, not Unity screenshots.', font=small, fill='#9daeb0')
    sheet.save(out / f'{name}.png')
    sheets.append(sheet)
sheet = Image.new('RGB', (2160, 1280), '#172025')
for i, panel in enumerate(sheets):
    sheet.paste(panel, ((i % 2) * 1080, (i // 2) * 640))
sheet.save(out / 'comparison-sheet.png')

lines = ['# Detail generation measurements', '',
         'Seed 1200; Release .NET 8, optimized JIT (`DOTNET_TieredCompilation=0`). One warm build and four timed worker builds per area. Heights, normals, skirts, 16 m collision data and the full 2 m texture are included. Native-noise input sampling, main-thread upload and physics cooking are excluded.', '',
         '| Area | Generation median (ms) | Max (ms) | Chunk clipping (ms) | Edge error (m) |',
         '| --- | ---: | ---: | ---: | ---: |']
for tile in metrics['tiles']:
    clip = tile['clipping']
    lines.append(f"| {tile['name']} | {tile['generationMedianMs']:.2f} | {tile['generationMaxMs']:.2f} | {clip['milliseconds']:.2f} | {clip['boundaryErrorMetres']:.5f} |")
streaming = metrics['streaming']
lines += ['', 'Each tile: 17,157 vertices, 34,816 triangles, 1,266,604 bytes of managed arrays, and an estimated 1,107,445 bytes of GPU payload (32-byte vertices, 16-bit indices and RGBA32 mip chain). Neighboring detail-edge sample error was zero.', '',
          f"{metrics['checks']:,} data invariants passed. Streaming simulation: {streaming['frames']:,} virtual frames, maximum {streaming['maximumLive']} live tiles, {streaming['committed']} publications before the reset, and {streaming['cameraTileMisses']} missing camera-tile frames on the warmed flight.", '',
          'Chunk timings include the first call/JIT and are not upload timings. State-machine tests synchronize workers between virtual frames. The reference Perlin implementation substitutes for native Unity noise in both image painters. These measurements do not certify Unity commit time, GPU time, actual VRAM, collider cooking, visual activation quality or player frame-time p95. See the parent checklist for the remaining gates.', '']
(out / 'measurements.md').write_bytes('\r\n'.join(lines).encode())
