"""Re-import exported FBXs, verify the delivery contract and build the review sheet and measured table.

blender --background --factory-startup --python-exit-code 1 --python Tools/Blender/check_world_assets.py -- [previews]
This is Blender validation only; Unity import, material replacement, LODGroups and gameplay remain integration checks.
"""
import html
import json
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import reset_scene
from world_asset_common import PROJECT, bounds, tris


def validate(reports):
    """Checks re-imported geometry, names, UV layers, material slots, pivots and LOD counts against the export manifest."""
    checks=[]
    for name, row in sorted(reports.items()):
        reset_scene()
        bpy.ops.import_scene.fbx(filepath=str(PROJECT / row['path']))
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
        assert len(meshes)==(6 if name=='Drone_Recon' else 3), (name, len(meshes))
        counts=[sum(tris(o) for o in meshes if o.name.endswith(f'_LOD{i}')) for i in range(3)]
        assert counts==row['triangles'], (name,counts,row['triangles'])
        assert counts[0]>counts[1]>counts[2] and counts[2]<300, (name,counts)
        materials=sorted({m.name for o in meshes for m in o.data.materials})
        assert materials==row['materials'], (name,materials)
        flat={'WindowGlass','WindowFrame','Interior','TealMark','ShellGray','Prop','Lens','SignalOrange'}
        for mat in materials:
            assert mat in flat or (PROJECT/'Assets/DroneSandbox/Resources/Materials/PBR'/(mat+'.mat')).exists(), (name,mat)
        for lod in range(3):
            parts=[o for o in meshes if o.name.endswith(f'_LOD{lod}')]
            lo,hi=bounds(parts)
            assert max(abs(float(v)-expected) for actual,wanted in zip((lo,hi),row['lod_bounds'][lod]) for v,expected in zip(actual,wanted))<.002, (name,lod,list(lo),list(hi))
        for o in meshes:
            assert o.parent is not None and o.parent.name==name, (name,o.name)
            assert len(o.data.uv_layers)==1 and o.data.uv_layers[0].name=='UVMap', o.name
            uv=[tuple(v.uv) for v in o.data.uv_layers[0].data]
            assert all(math.isfinite(c) for p in uv for c in p), o.name
            assert len(set(uv))>3, (o.name,'collapsed UVs')
            assert all(m.name in row['materials'] for m in o.data.materials)
        if name in ('Scenery_House_C','Scenery_House_D'):
            for o in meshes:
                assert {'RoofTile','PlasterWhite'} <= {m.name for m in o.data.materials}, o.name
        if name=='Drone_Recon':
            for lod in range(3):
                obj=next(o for o in meshes if o.name==f'Prop_0_LOD{lod}')
                assert (obj.matrix_world.translation-Vector((0,1.14,.02))).length<.002, obj.name
                lo,hi=bounds([obj])
                assert hi.y-lo.y<.2 and hi.x-lo.x>.98, (obj.name,'prop axis')
            assert abs(row['dimensions'][0]-5.2)<.001
        if name=='Scenery_PipeRack':
            assert abs(row['bounds'][0][1]+10)<.001 and abs(row['bounds'][1][1]-10)<.001
        checks.append(dict(name=name,triangles=counts,status='PASS',mesh_depth=1))
        print('ROUNDTRIP PASS',name,counts,flush=True)
    return checks


def text_object(body, x, y, size, color=(.8,.86,.9,1)):
    """Places a flat emission label in the contact sheet, which requires no installed font packages."""
    curve=bpy.data.curves.new('Label','FONT')
    curve.body,curve.size=body,size
    obj=bpy.data.objects.new('Label',curve)
    bpy.context.collection.objects.link(obj)
    obj.location=(x,y,.05)
    mat=bpy.data.materials.get('Label'+str(color)) or bpy.data.materials.new('Label'+str(color))
    mat.use_nodes=True
    nodes=mat.node_tree.nodes
    bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value=color
    bsdf.inputs['Emission Color'].default_value=color
    bsdf.inputs['Emission Strength'].default_value=1
    curve.materials.append(mat)


def sheet(reports, folder, suffix='', title='WORLD ASSETS / 21 MODELS'):
    """Arranges all rendered models with names, measured dimensions, budgets and their 1.8 m figures."""
    reset_scene()
    ordered=sorted(reports)
    cols=4
    rows=math.ceil(len(ordered)/cols)
    width,height=24,rows*5.1+1.5
    for i,name in enumerate(ordered):
        path=folder/(name+suffix+'.png')
        assert path.exists(), path
        x,y=(i%cols)*6+3,-(i//cols)*5.1-2.25
        bpy.ops.mesh.primitive_plane_add(size=2,location=(x,y,0))
        plane=bpy.context.object
        plane.scale=(2.95,2.2125,1)
        mat=bpy.data.materials.new(name)
        mat.use_nodes=True
        nodes,links=mat.node_tree.nodes,mat.node_tree.links
        nodes.clear()
        out=nodes.new('ShaderNodeOutputMaterial')
        emission=nodes.new('ShaderNodeEmission')
        tex=nodes.new('ShaderNodeTexImage')
        tex.image=bpy.data.images.load(str(path))
        links.new(tex.outputs['Color'],emission.inputs['Color'])
        links.new(emission.outputs[0],out.inputs['Surface'])
        plane.data.materials.append(mat)
        short=name.replace('Scenery_','').replace('Drone_','')
        dims=' x '.join(f'{v:.1f}' for v in reports[name]['dimensions'])+' m'
        counts=' / '.join(str(v) for v in reports[name]['triangles'])+' tris'
        text_object(short,x-2.88,y-2.46,.21)
        text_object(dims+'   |   '+counts,x-2.88,y-2.7,.125)
    text_object(title,.15,.7,.38)
    note='45-degree close views; each tile auto-framed. Orange figure = 1.8 m. Dimensions = width x depth x height.'
    if suffix=='_RTS':
        note='Same scale in every tile: 600 m overhead, 1.5 px/metre in source images. A 40 m building is 60 px wide.'
    elif suffix=='_LOD2':
        note='Authored last LODs, all below 300 triangles. Same framing as the close views; orange figure = 1.8 m.'
    text_object(note,.15,.3,.18)
    bpy.ops.object.camera_add(location=(width/2,1.05-height/2,10))
    cam=bpy.context.object
    cam.data.type='ORTHO'
    cam.data.ortho_scale=height
    scene=bpy.context.scene
    scene.camera=cam
    scene.render.engine='CYCLES'
    scene.cycles.samples=1
    scene.render.resolution_x=int(width*120)
    scene.render.resolution_y=int(height*120)
    scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('SheetBackground')
    scene.world.color=(.022,.028,.035)
    scene.view_settings.view_transform='Standard'
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(folder/('contact_sheet'+suffix+'.png'))
    bpy.ops.render.render(write_still=True)


def report(reports, checks, folder):
    """Writes the complete handoff table and local review page, including a playable recon turntable."""
    lines=['# World assets handoff','',
           '21 models, built with Blender 5.2 in factory-startup headless sessions. No game C# changes.', '',
           'Dimensions below are measured Blender X width × Y depth × Z height in metres, including stairs, eaves, balconies and antennas. In Unity these become X width × Z depth × Y height. One UV unit is one metre; the existing PBR materials supply their own tiling. All meshes are direct children of their FBX root.', '',
           '| Model | Dimensions (m) | LOD0 / LOD1 / LOD2 triangles | Materials | Export path |',
           '|---|---|---|---|---|']
    for name,row in sorted(reports.items()):
        dims=' × '.join(f'{v:.2f}' for v in row['dimensions'])
        counts=' / '.join(str(v) for v in row['triangles'])
        path='../../'+row['path']
        lines.append(f"| {name} | {dims} | {counts} | {', '.join(row['materials'])} | [{name}.fbx]({path}) |")
    lines += ['', '## Placement and integration', '',
              '- All models face Blender -Y / Unity +Z, with Z up in Blender and real metre dimensions. Scenery uses ground z=0; structural footings may sink slightly into terrain. Building roots are the centres of their primary structures, so asymmetric stairs, awnings and farmhouse wings extend beyond them.',
              '- `Drone_Recon`: 5.2 m span, slender unarmed airframe, twin booms, inverted-V tail and nose gimbal. Origin is the nominal centre of gravity at the wing root. Grey-green `ShellGray` finish with existing `Olive`, `OliveDark` and `TealMark` slots. Flight speed, endurance, camera behaviour and damage are left to Claude.',
              '- Recon moving parts are direct children `Prop_0_LOD0`, `Prop_0_LOD1`, `Prop_0_LOD2`, pivot Blender (0, 1.14, 0.02), Unity (0, 0.02, -1.14). They match Drone.cs `StartsWith("Prop")`. The current animation rotates around `body.up`; Claude must rotate the pusher around `body.forward` instead. No blur disc is included.',
              '- The `_LOD0`, `_LOD1`, `_LOD2` mesh suffixes follow make_trees.py. The recon airframe and propeller form two matching LOD series; verify Unity groups both renderers per level. All delivered LOD2 totals include the propeller and are below 300 triangles.',
              '- `Scenery_PipeRack`: tile along local Y in Blender / local Z in Unity at 20 m intervals. Pipe ends are open and coplanar at ±10 m; do not scale neighbouring runs differently.',
              '- Rail wagons match make_sectors.py: rail top 0.592 m, wheel tread radius 0.46 m, axle z=1.052 m, wheel centres ±0.7535 m. Place at the same ground datum as Scenery_RailTrack. The crane has 14 m between leg centrelines, enough for two tracks at 5 m spacing.',
              '- Factory body 60 × 30 m; hangar body 40 × 50 m; shelter shell 25 × 35 m; freight shed body 40 × 15 m; apartment body 30 × 12 m; town row frontage 24 m; storage tank diameter 18 m. Measured bounds in the table include exterior fittings.',
              '- House_C and House_D retain `RoofTile` and `PlasterWhite` in every LOD for World.Repaint. Glass, frames, markings, lens and prop materials are existing flat-slot conventions; `SignalOrange` is a flat windsock/valve colour. No new texture or material assets are required.', '',
              '## Verification', '',
              f'- {len(checks)} FBXs re-imported successfully into Blender: exact triangles per LOD, measured bounds within 2 mm, expected materials, non-collapsed finite UVs, direct-child hierarchy, village repaint slots and recon propeller pivot/axis.',
              '- Export checks also reject zero-area faces, non-decreasing LOD counts and exceeded triangle budgets. All photo slots match files in Resources/Materials/PBR.',
              '- Close, last-LOD and top-down preview images were generated for every model. The source RTS images are 720 × 540 at exactly 1.5 px/m with the camera at 600 m. The contact sheet rescales those thumbnails; inspect individual RTS files for the requested pixel scale.',
              '- Preview diffuse textures and tiling are read from the existing Unity material assets after FBX export. Lighting is a Blender review setup, not a prediction of HDRP appearance.',
              '- Not verified: Unity import/LODGroup creation, runtime PBR swaps, pusher animation, collisions, sector placement, HDRP appearance or GPU performance. Unity was not launched in this worktree.', '',
              '## Rebuild', '', '```powershell',
              '& "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python-exit-code 1 --python Tools/Blender/make_world_assets.py -- Assets/DroneSandbox/Resources/Models C:/Users/ali10/AppData/Local/Temp/dwprev/codex/assets',
              '& "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python-exit-code 1 --python Tools/Blender/check_world_assets.py -- C:/Users/ali10/AppData/Local/Temp/dwprev/codex/assets',
              '```', '', 'Use `-` as the preview argument to skip rendering. An optional final comma-separated argument selects model names or groups (`airfield,industrial,rail,town,recon`). The validator writes the table, validation JSON, three contact sheets and an HTML review page with a 24-frame playable turntable.', '']
    (PROJECT/'Tools/Blender/world_assets_report.md').write_bytes('\r\n'.join(lines).encode('utf-8'))
    (PROJECT/'Tools/Blender/world_assets_validation.json').write_bytes((json.dumps(checks,indent=2)+'\n').replace('\n','\r\n').encode())
    cards=[]
    for name,row in sorted(reports.items()):
        cards.append(f'<article><h2>{html.escape(name)}</h2><a href="{name}.png"><img src="{name}.png"></a><p>{row["dimensions"]} m · {row["triangles"]} triangles</p><a href="{name}_RTS.png">RTS, 1.5 px/m</a> · <a href="{name}_LOD2.png">Last LOD</a></article>')
    page='''<!doctype html><meta charset="utf-8"><title>Drone Warfare world assets</title>
<style>body{background:#101820;color:#e0e8ed;font:16px system-ui;margin:32px}h1{font-size:32px}h2{font-size:17px}a{color:#8fced4}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:24px}img{max-width:100%}article{background:#1d2933;padding:16px}button,input{padding:8px;margin:8px}</style>
<h1>World assets / 21 models</h1><p>45-degree review views, 1.8 m orange scale figure. Each close view is auto-framed.</p>
<p><a href="contact_sheet.png">Contact sheet</a> · <a href="contact_sheet_RTS.png">RTS sheet</a> · <a href="contact_sheet_LOD2.png">Last LOD sheet</a></p>
<h2>Recon turntable</h2><p><a href="Drone_Recon_turntable.mp4">Download MP4</a></p><img id="orbit" width="720" src="Drone_Recon_turntable/000.png"><br>
<button id="play">Pause</button><input id="frame" type="range" min="0" max="23" value="0"><span id="angle">0°</span>
<script>let f=0,playing=true; const frames=Array.from({length:24},(_,i)=>{let a=new Image();a.src='Drone_Recon_turntable/'+String(i).padStart(3,'0')+'.png';return a;});
function show(){document.getElementById('orbit').src=frames[f].src;document.getElementById('frame').value=f;document.getElementById('angle').textContent=(f*15)+'°';}
document.getElementById('play').onclick=()=>{playing=!playing;document.getElementById('play').textContent=playing?'Pause':'Play'};
document.getElementById('frame').oninput=e=>{f=+e.target.value;show()};setInterval(()=>{if(playing){f=(f+1)%24;show()}},140);</script><main>'''+''.join(cards)+'</main>'
    (folder/'index.html').write_text(page,encoding='utf-8')


def main():
    """Runs the checks before generating any handoff that claims they passed."""
    args=sys.argv[sys.argv.index('--')+1:]
    folder=Path(args[0]).resolve()
    reports=json.loads((PROJECT/'Tools/Blender/world_assets_manifest.json').read_text())
    assert len(reports)==21,len(reports)
    checks=validate(reports)
    ffmpeg=shutil.which('ffmpeg')
    if ffmpeg:
        subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-y','-framerate','8',
                        '-i',str(folder/'Drone_Recon_turntable/%03d.png'),'-c:v','libx264','-pix_fmt','yuv420p',
                        '-movflags','+faststart',str(folder/'Drone_Recon_turntable.mp4')],check=True)
    report(reports,checks,folder)
    sheet(reports,folder)
    sheet(reports,folder,'_RTS','WORLD ASSETS / RTS SCALE')
    sheet(reports,folder,'_LOD2','WORLD ASSETS / LAST LODS')


if __name__=='__main__':
    main()
