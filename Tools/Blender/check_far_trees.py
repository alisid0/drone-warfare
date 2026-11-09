"""Checks regenerated trees against saved FBXs and renders their LOD2 at RTS scale.

Run in Blender with -- <baseline directory with Models/ and Textures/> <preview directory>.
The baseline must be copied before make_trees.py runs. No Unity import is needed.
"""
import hashlib
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import reset_scene


def mesh_record(obj):
    """Fingerprints mesh data and transforms, including UVs and custom normals used by the near crowns."""
    mesh = obj.data
    digest = hashlib.sha256()
    for values in ([tuple(v.co) for v in mesh.vertices],
                   [tuple(p.vertices) for p in mesh.polygons],
                   [p.material_index for p in mesh.polygons],
                   [[tuple(uv.uv) for uv in layer.data] for layer in mesh.uv_layers],
                   [tuple(n.vector) for n in mesh.corner_normals],
                   [tuple(row) for row in obj.matrix_world]):
        digest.update(repr(values).encode())
    return dict(hash=digest.hexdigest(), vertices=len(mesh.vertices),
                triangles=sum(len(p.vertices) - 2 for p in mesh.polygons),
                materials=[m.name.split(".")[0] for m in mesh.materials])


def load(path):
    """Imports one FBX in a fresh scene so name suffixes cannot conceal a hierarchy change."""
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(path))
    return {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}


def preview(obj, texture, path, distance=None):
    """Renders the far mesh without shadows, with alpha clipping, at 55 degrees down and 45 degrees FOV."""
    scene = bpy.context.scene
    for o in scene.objects:
        if o.type == "MESH":
            o.hide_render = o != obj
    mat = bpy.data.materials.new("PreviewCutout")
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.abspath(texture))
    clip = nodes.new("ShaderNodeMath")
    clip.operation = "GREATER_THAN"
    clip.inputs[1].default_value = 0.45
    links.new(tex.outputs["Alpha"], clip.inputs[0])
    light = nodes.new("ShaderNodeEmission")
    # All production impostor normals point up. An emission preview isolates silhouette and the baked light.
    links.new(tex.outputs["Color"], light.inputs["Color"])
    light.inputs["Strength"].default_value = 0.82
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    mix = nodes.new("ShaderNodeMixShader")
    links.new(clip.outputs[0], mix.inputs[0])
    links.new(transparent.outputs[0], mix.inputs[1])
    links.new(light.outputs[0], mix.inputs[2])
    out = nodes.new("ShaderNodeOutputMaterial")
    links.new(mix.outputs[0], out.inputs["Surface"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.material_index = 0
    corners = [obj.matrix_world @ Vector(v) for v in obj.bound_box]
    center = sum(corners, Vector()) / 8
    elev, azimuth = math.radians(55), math.radians(22)
    direction = Vector((math.sin(azimuth) * math.cos(elev), -math.cos(azimuth) * math.cos(elev), math.sin(elev)))
    up = Vector((0, 0, 1)) - direction * direction.z
    up.normalize()
    span = max(v.dot(up) for v in corners) - min(v.dot(up) for v in corners)
    calibrate = distance is None
    distance = distance or span * 512 / (2 * math.tan(math.radians(22.5)) * 20)
    cam = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
    scene.collection.objects.link(cam)
    cam.data.type = "PERSP"
    cam.data.angle = math.radians(45)
    cam.data.clip_end = 4000
    cam.location = center + direction * distance
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = 512 if calibrate else 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    if calibrate:
        # Bounding boxes include the cards' transparent corners. Measure the actual old silhouette instead,
        # then use exactly that camera for both versions, at a normal 1080-pixel viewport height.
        best = None
        for attempt in range(7):
            shot = bpy.data.images.load(path, check_existing=False)
            shot.reload()  # the same PNG is overwritten between calibration renders
            w, h = shot.size
            pixels = np.empty(w * h * 4, dtype=np.float32)
            shot.pixels.foreach_get(pixels)
            rows = np.where((pixels.reshape(h, w, 4)[:, :, 3] > 0.5).any(axis=1))[0]
            assert len(rows), "Empty far-tree silhouette"
            size = rows[-1] - rows[0] + 1
            bpy.data.images.remove(shot)
            if h == 1080 and (best is None or abs(size - 20) < best[0]):
                with open(path, "rb") as f:
                    best = (abs(size - 20), distance, f.read())
            if h == 1080 and abs(size - 20) <= 1:
                break
            if attempt == 6:
                break
            distance *= size / 20 * 1080 / h
            cam.location = center + direction * distance
            scene.render.resolution_x = scene.render.resolution_y = 1080
            bpy.ops.render.render(write_still=True)
        # Thin trunks can lose a whole pixel between two distances. Keep the closest measured frame.
        distance = best[1]
        with open(path, "wb") as f:
            f.write(best[2])
    return distance


def main():
    """Checks every tree variant, records building costs, and leaves paired old/new PNGs for review."""
    args = sys.argv[sys.argv.index("--") + 1:]
    baseline, output = map(os.path.abspath, args)
    os.makedirs(output, exist_ok=True)
    models = "Assets/DroneSandbox/Resources/Models"
    textures = "Assets/DroneSandbox/Textures/PBR"
    report = dict(trees={}, buildings={})
    for file in sorted(os.listdir(os.path.join(baseline, "Models"))):
        name = file[:-4]
        old = load(os.path.join(baseline, "Models", file))
        before = {n: mesh_record(o) for n, o in old.items()}
        kind = before[name + "_LOD2"]["materials"][0].replace("Impostor", "").lower()
        distance = preview(old[name + "_LOD2"], os.path.join(baseline, "Textures", f"impostor_{kind}.png"),
                           os.path.join(output, name + "_old.png"))
        new = load(os.path.join(models, file))
        after = {n: mesh_record(o) for n, o in new.items()}
        unchanged = all(before[name + suffix] == after[name + suffix] for suffix in ("_LOD0", "_LOD1"))
        depth = 0
        for o in new.values():
            d, p = 0, o
            while p.parent:
                d, p = d + 1, p.parent
            depth = max(depth, d)
        report["trees"][name] = dict(near_unchanged=unchanged, depth=depth, old=before, new=after, distance=distance)
        assert before[name + "_LOD2"]["triangles"] == 6
        assert after[name + "_LOD2"]["triangles"] == 8
        assert len(after[name + "_LOD2"]["materials"]) == 1
        assert depth <= 2
        preview(new[name + "_LOD2"], os.path.join(textures, f"impostor_{kind}.png"),
                os.path.join(output, name + "_new.png"), distance)
        print(f"CHECK {name}: near unchanged={unchanged}, depth={depth}, 6 -> 8 triangles", flush=True)
    for name in ("House_A", "House_B", "Church", "Barn", "Hut", "Silo", "Fence", "LogPile"):
        objects = load(os.path.join(models, "Scenery_" + name + ".fbx"))
        verts = [o.matrix_world @ v.co for o in objects.values() for v in o.data.vertices]
        report["buildings"][name] = dict(renderers=len(objects),
            triangles=sum(mesh_record(o)["triangles"] for o in objects.values()),
            material_slots=sum(len(o.data.materials) for o in objects.values()),
            radius=max(math.hypot(v.x, v.y) for v in verts),
            bounds=[[min(v[k] for v in verts), max(v[k] for v in verts)] for k in range(3)])
    with open(os.path.join(output, "checks.json"), "w") as f:
        json.dump(report, f, indent=2)
    assert all(t["near_unchanged"] for t in report["trees"].values()), "LOD0/1 changed; inspect checks.json"


if __name__ == "__main__":
    main()
