"""Plot actual consumer output over the direct world crop; these are harness diagrams, not Unity screenshots."""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

SIZE = 660
PALETTE = [(158, 173, 130), (193, 184, 122), (48, 87, 65), (105, 143, 134),
           (75, 139, 166), (177, 151, 132), (193, 169, 141), (154, 153, 147)]


def font(n, bold=False):
    """Use a fixed installed font so labels stay reproducible."""
    return ImageFont.truetype('C:/Windows/Fonts/' + ('arialbd.ttf' if bold else 'arial.ttf'), n)


def point(p):
    """Keep north up and both panels at the same 2 km scale."""
    return ((p[0] + 1000) * SIZE / 2000, (1000 - p[1]) * SIZE / 2000)


def panel(data, consumer=None):
    """Overlay produced fields, tree placements and triangles on independently sampled world cover."""
    img = Image.new('RGB', (200, 200))
    img.putdata([PALETTE[data['cover'][(199-z)*200+x]] for z in range(200) for x in range(200)])
    if consumer:
        img = Image.blend(img, Image.new('RGB', img.size, (235, 235, 218)), .55)
    img = img.resize((SIZE, SIZE), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(img)
    if consumer:
        for f in consumer['fields']:
            draw.polygon([point(p) for p in f], fill=(200, 176, 91), outline=(133, 118, 64))
        for mesh in consumer['water']:
            vertices, tris = mesh['vertices'], mesh['triangles']
            pts = [point((v[0], v[2])) for v in vertices]
            for i in range(0, len(tris), 3):
                draw.polygon([pts[tris[j]] for j in range(i, i+3)], fill=(37, 128, 179))
        for p in consumer['trees']:
            x, y = point(p)
            draw.ellipse((x-1.3, y-1.3, x+1.3, y+1.3), fill=(25, 74, 46))
    else:
        for lake in data['lakes']:
            draw.polygon([point(p) for p in lake['shore']], fill=(37, 128, 179))
        for river in data['rivers']:
            for i in range(1, len(river['points'])):
                draw.line([point(river['points'][i-1]), point(river['points'][i])], fill=(37, 128, 179),
                          width=max(1, round((river['halfWidths'][i-1]+river['halfWidths'][i])*SIZE/2000)))
    for road in data['roads']:
        if len(road) > 1:
            draw.line([point(p) for p in road], fill=(246, 220, 171), width=2)
    for dam in data['dams']:
        draw.line([point(dam['a']), point(dam['b'])], fill=(80, 70, 70), width=5)
    draw.rectangle((0, 0, SIZE-1, SIZE-1), outline=(40, 60, 60), width=2)
    return img


def trace(draw, values, box, low, high, color, width=2):
    """Draw a shared-axis profile without smoothing away a seam error."""
    x, y, w, h = box
    draw.line([(x+i*w/(len(values)-1), y+h-(v-low)*h/(high-low)) for i, v in enumerate(values)], fill=color, width=width)


def render(path, target):
    """One review sheet per crop shows exact world geography, consumer output and the full boundary profile."""
    d = json.loads(path.read_text())
    c, s = d['consumer'], d['summary']
    img = Image.new('RGB', (1450, 1240), (247, 247, 239))
    draw = ImageDraw.Draw(img)
    draw.text((42, 22), s['name'].replace('-', ' ').title() + ' / world consumer hooks', font=font(29, True), fill=(25, 49, 50))
    draw.text((42, 65), f"Seed 1200 | centre ({s['center'][0]:,.0f}, {s['center'][1]:,.0f}) m | north up | 2 km square", font=font(19), fill=(55, 70, 68))
    draw.text((42, 110), 'WorldPlan: direct cover and water', font=font(21, True), fill=(25, 49, 50))
    draw.text((748, 110), 'Produced water / woods / fields over world cover', font=font(21, True), fill=(25, 49, 50))
    img.paste(panel(d['world']), (42, 145))
    img.paste(panel(d['world'], c), (748, 145))
    draw.text((42, 820), f"{len(c['trees']):,} woodland placements | {len(c['fields'])} parcels | {len(c['water'])} separate water meshes | {s['missions']} situations", font=font(19, True), fill=(25, 49, 50))
    draw.text((42, 852), 'Blue: produced water triangles   Green dots: placed woods   Gold: accepted parcels   Pale background: world cover', font=font(17), fill=(55, 70, 68))
    low, high = min(c['edgeWorld'])-1, max(c['edgeWorld'])+1
    box = (80, 916, 1320, 125)
    draw.text((42, 885), f"Boundary height: WorldPlan (dark) / stored Terrain (orange) | {low:.1f} to {high:.1f} m absolute Y", font=font(18, True), fill=(25, 49, 50))
    for side in range(5):
        x = box[0] + side*box[2]/4
        draw.line((x, box[1], x, 1145), fill=(202, 209, 200))
        if side < 4:
            draw.text((x+8, 1044), ['South >', 'East >', 'North >', 'West >'][side], font=font(15), fill=(55, 70, 68))
    trace(draw, c['edgeWorld'], box, low, high, (25, 65, 70), 4)
    trace(draw, c['edgeTerrain'], box, low, high, (235, 134, 42), 2)
    errors = [a-b for a, b in zip(c['edgeTerrain'], c['edgeWorld'])]
    draw.text((42, 1071), f"Error, scale +/-0.5 m | maximum including every terrain/world grid knot: {c['maxBoundary']:.4f} m", font=font(18, True), fill=(25, 49, 50))
    draw.line((80, 1123, 1400, 1123), fill=(171, 185, 180))
    trace(draw, errors, (80, 1101, 1320, 44), -.5, .5, (211, 91, 32), 2)
    draw.text((42, 1164), 'Harness output: actual consumer C# with managed engine storage and reference noise. No HDRP shading, occlusion or native physics.', font=font(17), fill=(55, 70, 68))
    draw.text((42, 1193), f"Ground {c['groundMs']:.0f} ms | Water {c['waterMs']:.0f} ms | Woods {c['woodsMs']:.0f} ms | native player load time still needs Unity", font=font(17), fill=(55, 70, 68))
    img.save(target / (path.stem + '.png'))
    return img, dict(name=s['name'], **{k: c[k] for k in ['maxBoundary', 'maxStored', 'boundaryChecks', 'bottom', 'range', 'groundMs', 'waterMs', 'woodsMs']},
                     trees=len(c['trees']), fields=len(c['fields']), waterMeshes=len(c['water']), missions=s['missions'], unitsOverWater=s['unitsOverWater'])


def main():
    """Keep large raw meshes in ignored Temp; commit only review sheets and compact measurements."""
    source = Path(sys.argv[1])
    target = Path(__file__).parent / 'consumer-renders'
    target.mkdir(exist_ok=True)
    names = ['river-crossing', 'town-edge', 'forest', 'reservoir', 'farmland', 'confluence']
    sheet = Image.new('RGB', (1450, 1860), (247, 247, 239))
    metrics = []
    for i, name in enumerate(names):
        img, result = render(source / (name + '.json'), target)
        metrics.append(result)
        sheet.paste(img.resize((725, 620), Image.Resampling.LANCZOS), ((i % 2)*725, (i // 2)*620))
    sheet.save(target / 'comparison-sheet.png')
    (target.parent / 'consumer-validation.json').write_text(json.dumps(metrics, indent=2) + '\n')
    print('Rendered six consumer sheets and comparison-sheet.png')


if __name__ == '__main__':
    main()
