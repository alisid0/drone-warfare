"""Downloads the CC0 Poly Haven textures the models use (colour + OpenGL normal map, 1K JPG) into
Assets/DroneSandbox/Textures/PBR/<name>_diff.jpg and _nor.jpg. Drone Sandbox > Configure Project then
turns them into URP materials named after the Blender materials (see ProjectSetup.PbrMaterials).

Run from the project root:  python Tools/fetch_textures.py
"""
import json
import os
import sys
import urllib.request

OUT = os.path.join("Assets", "DroneSandbox", "Textures", "PBR")

# local name -> Poly Haven asset id
TEXTURES = {
    "concrete": "concrete_floor_worn_001",
    "concrete_wall": "concrete_wall_004",
    "corrugated": "corrugated_iron_02",
    "worn_corrugated": "worn_corrugated_iron",
    "metal_plate": "metal_plate",
    "green_metal": "green_metal_rust",
    "rust_paint": "rusty_painted_metal",
    "plaster_white": "white_plaster_rough_01",
    "plaster_grey": "grey_plaster_02",
    "stone_wall": "plastered_stone_wall",
    "brick": "brick_wall_02",
    "roof_tiles": "clay_roof_tiles_02",
    "plank_siding": "weathered_plank_siding",
    "wood": "wood_planks_grey",
    "rough_wood": "rough_wood",
    "bark": "bark_brown_02",
    "oak_bark": "jolcham_oak_bark_01",
    "willow_bark": "bark_willow",
    "pine_bark": "pine_bark",
    "hessian": "hessian_230",
    "mossy_rock": "mossy_rock",
    "gravel": "gravel_floor",
    "asphalt": "asphalt_02",
}


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "DroneWarfare-texture-fetch"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def main():
    os.makedirs(OUT, exist_ok=True)
    only = set(sys.argv[1:])
    for name, asset in TEXTURES.items():
        if only and name not in only:
            continue
        files = json.loads(fetch(f"https://api.polyhaven.com/files/{asset}"))
        for kind, suffix in (("Diffuse", "diff"), ("nor_gl", "nor")):
            path = os.path.join(OUT, f"{name}_{suffix}.jpg")
            if os.path.exists(path):
                continue
            url = files[kind]["1k"]["jpg"]["url"]
            data = fetch(url)
            with open(path, "wb") as f:
                f.write(data)
            print(f"{name}_{suffix}: {asset} ({len(data) // 1024} KB)")
    fetch_skies(only)


# Extra skies the day/night sky blends to: local name -> Poly Haven HDRI (2K, sky only). The daytime sky.hdr
# is Kloofendal 48d Partly Cloudy (Pure Sky).
SKIES = {
    "sky_sunset": "qwantani_sunset_puresky",
    "sky_night": "qwantani_night_puresky",
}
SKY_DIR = os.path.join("Assets", "DroneSandbox", "Resources", "Sky")


def fetch_skies(only):
    for name, asset in SKIES.items():
        if only and name not in only:
            continue
        path = os.path.join(SKY_DIR, name + ".hdr")
        if os.path.exists(path):
            continue
        files = json.loads(fetch(f"https://api.polyhaven.com/files/{asset}"))
        data = fetch(files["hdri"]["2k"]["hdr"]["url"])
        with open(path, "wb") as f:
            f.write(data)
        print(f"{name}: {asset} ({len(data) // 1024} KB)")


main()
