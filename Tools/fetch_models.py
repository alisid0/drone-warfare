"""Downloads the CC0 Poly Haven photo-scanned models (rocks, cliffs, plants, stumps) as glTF into
Tools/scans/<id>/: the .gltf, its .bin and its textures, plus the separate alpha map for plants (Poly Haven's
glTF colour map is a JPG, so the leaves' cut-out comes as its own picture). Tools/scans is kept out of git and
out of Assets; Tools/Blender/import_scans.py turns the downloads into Unity models and textures.

Run from the project root:  python Tools/fetch_models.py [id ...]   (no ids: all of them)
"""
import json
import os
import sys
import urllib.request

OUT = os.path.join("Tools", "scans")

# Poly Haven asset id -> resolution of the textures. Rocks and cliffs get 2K since they're big and the camera can
# get close; plants and clutter are small on screen, so 1K is plenty.
MODELS = {
    "rock_moss_set_01": "2k",
    "rock_moss_set_02": "2k",
    "boulder_01": "2k",
    "rock_07": "2k",
    "rock_09": "2k",
    "rock_face_01": "2k",
    "rock_face_02": "2k",
    "fern_02": "1k",
    "nettle_plant": "1k",
    "shrub_01": "1k",
    "shrub_02": "1k",
    "shrub_03": "1k",
    "shrub_04": "1k",
    "tree_stump_01": "1k",
    "tree_stump_02": "1k",
    "dead_tree_trunk": "1k",
    "moss_01": "1k",
    "celandine_01": "1k",
    "dandelion_01": "1k",
}


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "DroneWarfare-model-fetch"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def save(folder, relative, url):
    """Writes url to folder/relative unless it's there already. The relative path comes from the API, so make
    sure it can't climb out of the asset's folder."""
    path = os.path.normpath(os.path.join(folder, relative))
    if not path.startswith(os.path.normpath(folder) + os.sep):
        raise ValueError(f"refusing to write outside {folder}: {relative}")
    if os.path.exists(path):
        return 0
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = fetch(url)
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


def main():
    only = set(sys.argv[1:])
    unknown = only - set(MODELS)
    if unknown:
        print("unknown ids: " + ", ".join(sorted(unknown)))
    for asset, res in MODELS.items():
        if only and asset not in only:
            continue
        folder = os.path.join(OUT, asset)
        try:
            files = json.loads(fetch(f"https://api.polyhaven.com/files/{asset}"))
        except Exception as e:  # a removed or renamed asset shouldn't stop the rest
            print(f"{asset}: skipped ({e})")
            continue
        gltf = files["gltf"].get(res) or files["gltf"][sorted(files["gltf"])[0]]
        main_file = gltf["gltf"]
        total = save(folder, os.path.basename(main_file["url"]), main_file["url"])
        for relative, info in main_file["include"].items():
            total += save(folder, relative, info["url"])
        # Plants: the cut-out mask, at the same resolution as the colour map.
        if "Alpha" in files:
            alpha = files["Alpha"].get(res) or files["Alpha"][sorted(files["Alpha"])[0]]
            pick = alpha.get("png") or alpha.get("jpg")
            total += save(folder, os.path.join("textures", os.path.basename(pick["url"])), pick["url"])
        print(f"{asset}: {res} ({total // 1024} KB downloaded)")


main()
