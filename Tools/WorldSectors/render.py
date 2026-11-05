"""Draw the actual world and sector snapshots; these are plan diagrams, not Unity renders."""
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

PALETTE = [(158, 173, 130), (193, 184, 122), (48, 87, 65), (105, 143, 134),
           (75, 139, 166), (177, 151, 132), (193, 169, 141), (154, 153, 147)]
SIZE = 660


def font(size, bold=False):
    """Use installed Windows text fonts for readable, reproducible labels."""
    return ImageFont.truetype("C:/Windows/Fonts/" + ("arialbd.ttf" if bold else "arial.ttf"), size)


def point(p):
    """North is up in both panels, with identical metre-to-pixel transforms."""
    return ((p[0] + 1000) * SIZE / 2000, (1000 - p[1]) * SIZE / 2000)


def panel(data, sector=False):
    """Cover, river, roads and settlements are layered in geographic order."""
    raster = Image.new("RGB", (200, 200))
    raster.putdata([PALETTE[data["cover"][(199 - z) * 200 + x]] for z in range(200) for x in range(200)])
    raster = raster.resize((SIZE, SIZE), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(raster)
    for lake in data["lakes"]:
        draw.polygon([point(p) for p in lake["shore"]], fill=PALETTE[4])
    for river in data["rivers"]:
        pts, widths = river["points"], river["halfWidths"]
        for i in range(1, len(pts)):
            draw.line([point(pts[i - 1]), point(pts[i])], fill=(38, 108, 159),
                      width=max(2, round((widths[i - 1] + widths[i]) * SIZE / 2000)))
    for road in data["roads"]:
        if len(road) > 1:
            draw.line([point(p) for p in road], fill=(91, 80, 65), width=4)
            draw.line([point(p) for p in road], fill=(244, 220, 172), width=2)
    for dam in data["dams"]:
        draw.line([point(dam["a"]), point(dam["b"])], fill=(211, 211, 215), width=7)
        draw.line([point(dam["a"]), point(dam["b"])], fill=(76, 79, 84), width=2)
    for village in data["villages"]:
        x, y = point(village["point"])
        if village["farm"]:
            draw.rectangle((x - 4, y - 4, x + 4, y + 4), fill=(248, 235, 212), outline=(56, 48, 41), width=1)
        else:
            draw.ellipse((x - 5, y - 5, x + 5, y + 5), fill=(88, 57, 45), outline=(249, 233, 204), width=1)
    if sector:
        for p in data["defences"]:
            x, y = point(p)
            draw.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(195, 74, 35))
        for p in data["bridges"]:
            x, y = point(p)
            draw.rectangle((x - 5, y - 5, x + 5, y + 5), outline=(246, 243, 222), width=2)
        for key, label, color in [("basePosition", "BASE", (6, 92, 110)), ("hq", "HQ", (169, 55, 31))]:
            x, y = point(data[key])
            draw.ellipse((x - 7, y - 7, x + 7, y + 7), fill=color, outline="white", width=2)
            draw.rounded_rectangle((x + 9, y - 11, x + 74, y + 12), radius=3, fill=(250, 249, 238))
            draw.text((x + 13, y - 9), label, font=font(17, True), fill=color)
    for m in [-500, 0, 500]:
        t = round((m + 1000) * SIZE / 2000)
        for a in range(0, SIZE, 12):
            draw.line((t, a, t, a + 2), fill=(220, 220, 199), width=1)
            draw.line((a, t, a + 2, t), fill=(220, 220, 199), width=1)
    draw.rectangle((0, 0, SIZE - 1, SIZE - 1), outline=(28, 44, 43), width=3)
    return raster


def render(path):
    """The legend explicitly distinguishes stored inputs from consumers awaiting integration."""
    data = json.loads(path.read_text())
    summary = data["summary"]
    canvas = Image.new("RGB", (1450, 945), (245, 245, 237))
    draw = ImageDraw.Draw(canvas)
    title = summary["name"].replace("-", " ").title()
    draw.text((42, 24), f"WORLD / SECTOR   ·   {title}", font=font(29, True), fill=(25, 49, 50))
    c = summary["center"]
    draw.text((42, 66), f"World seed 1200     centre ({c[0]:,.0f}, {c[1]:,.0f}) m     2 km × 2 km     north ↑", font=font(18), fill=(66, 79, 77))
    draw.text((42, 105), "WorldPlan · direct crop", font=font(21, True), fill=(25, 49, 50))
    draw.text((748, 105), "SectorPlan · layout + exact world inputs", font=font(21, True), fill=(25, 49, 50))
    canvas.paste(panel(data["world"]), (42, 143))
    canvas.paste(panel(data["sector"], True), (748, 143))
    roads, rivers, prepared = summary["roads"], summary["rivers"], summary["preparedRoads"]
    draw.text((42, 821), f"Edge mismatch: roads {roads['maxMetres']:.4f} m ({roads['worldCount']} crossings)   ·   rivers {rivers['maxMetres']:.4f} m ({rivers['worldCount']} crossings)", font=font(19, True), fill=(25, 49, 50))
    draw.text((42, 850), f"Unchanged MapLayout roads: {prepared['maxMetres']:.3f} m   ·   forest {summary['forest']:.0%}   ·   fields {summary['fields']:.0%}   ·   relief {summary['minHeight']:.0f}–{summary['maxHeight']:.0f} m", font=font(18), fill=(66, 79, 77))
    draw.text((42, 882), "Exact lake / cover / all-river inputs shown. Ground, water and woods consumer hooks still required; see README.", font=font(17), fill=(66, 79, 77))
    draw.text((42, 911), "Roads: cream   River/lake: blue   Woods: dark green   Village: brown circle   Farm: white square   Base: teal   HQ/defences: red", font=font(15), fill=(66, 79, 77))
    target = path.with_suffix(".png")
    canvas.save(target)
    return canvas


def main():
    """Produce individual review renders and a contact sheet from the validated snapshots."""
    output = Path(sys.argv[1])
    images = [render(p) for p in sorted(output.glob("*.json")) if p.name != "metrics.json"]
    sheet = Image.new("RGB", (1450, math.ceil(len(images) / 2) * 473), (245, 245, 237))
    for i, image in enumerate(images):
        sheet.paste(image.resize((725, 473), Image.Resampling.LANCZOS), ((i % 2) * 725, (i // 2) * 473))
    sheet.save(output / "comparison-sheet.png")
    print(f"Rendered {len(images)} comparison pairs")


if __name__ == "__main__":
    main()
