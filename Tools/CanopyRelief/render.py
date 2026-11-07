"""Replay exported production meshes with matched offline lighting; these are not Unity/HDRP captures."""
from pathlib import Path
import sys
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
original = ROOT / 'Tools/ForestPolish/render.py'
source = original.read_text().split('args = sys.argv[')[0]
source = source.replace('Tools/ForestPolish/Review', 'Tools/CanopyRelief/Review').replace('Temp/forest-polish', 'Temp/canopy-relief')
source = source.replace(".96 + .04*t['shade']", "(.90 + .09*t['shade']) if PHASE == 'after' else (.96 + .04*t['shade'])")
source = source.replace("        m.surface_render_method = 'DITHERED'", "        if PHASE == 'after': relief_material(m, d, v, data, geometry)\n        m.surface_render_method = 'DITHERED'")
source = source.replace("(.25,.28,.18)", "((49/255,70/255,45/255) if PHASE == 'after' else (66/255,58/255,42/255))")
exec(compile(source, str(original), 'exec'))


def filtered(image, u, v):
    """Bilinear repeat matches the crown sampler; Blender generates the eventual view-dependent mip levels."""
    h, w = image.shape[:2]
    x, y = (u % 1)*w-.5, (v % 1)*h-.5
    i, j = np.floor(x).astype(int), np.floor(y).astype(int)
    a, b = (x-i)[...,None], (y-j)[...,None]
    return ((image[j % h,i % w]*(1-a)+image[j % h,(i+1) % w]*a)*(1-b)
            +(image[(j+1) % h,i % w]*(1-a)+image[(j+1) % h,(i+1) % w]*a)*b)


def detail_at(x, z, a, b, maps):
    """Reproduce ForestBlockDetail's uint hash, transform, map choice and world-space slope rotation."""
    mask = np.uint64(0xffffffff)
    h = ((a.astype(np.int64).astype(np.uint64)*73856093) ^ (b.astype(np.int64).astype(np.uint64)*19349663)) & mask
    h ^= h >> 16; h = (h*0x7feb352d) & mask; h ^= h >> 15; h = (h*0x846ca68b) & mask; h ^= h >> 16
    angle = (h & 65535) * (6.2831853/65536)
    sn, cs = np.sin(angle), np.cos(angle)
    u = (cs*x-sn*z)/128+((h >> 16) & 255)/256
    v = (sn*x+cs*z)/128+((h >> 24) & 255)/256
    value = np.where(((h & 65536)==0)[...,None], filtered(maps[0],u,v), filtered(maps[1],u,v))
    sx, sz = value[...,1]*2-1, value[...,2]*2-1
    return np.stack([value[...,0], cs*sx+sn*sz, -sn*sx+cs*sz],axis=-1)


def relief_material(mat, mesh, vertices, scene, geometry):
    """Bake only shader detail in narrow bands; production geometry, normals and macro colour remain unchanged."""
    maps=[]
    for name in ('ForestDetail', 'ForestDetailB'):
        im=bpy.data.images.load(str(ROOT/'Assets/DroneSandbox/Resources/Textures'/(name+'.png')),check_existing=True)
        im.colorspace_settings.name='Non-Color'
        maps.append(np.array(im.pixels[:],dtype=np.float32).reshape(im.size[1],im.size[0],4)[...,:3])
    lo=vertices[:,[0,2]].min(axis=0); span=2048; n=1024
    result=np.empty((n,n,4),dtype=np.float32)
    for row in range(0,n,16):
        x,z=np.meshgrid(lo[0]+scene['origin'][0]+(np.arange(n)+.5)*span/n,
                        lo[1]+scene['origin'][1]+(np.arange(row,min(row+16,n))+.5)*span/n)
        gridx,gridz=x/96,z/96; a,b=np.floor(gridx),np.floor(gridz); fx,fz=gridx-a,gridz-b
        cx=(fx>fz).astype(int); cz=1-cx
        weights=np.stack([1-np.maximum(fx,fz),np.abs(fx-fz),np.minimum(fx,fz)],axis=-1)**2
        weights/=weights.sum(axis=-1)[...,None]
        value=(detail_at(x,z,a,b,maps)*weights[...,0,None]
              +detail_at(x,z,a+cx,b+cz,maps)*weights[...,1,None]
              +detail_at(x,z,a+1,b+1,maps)*weights[...,2,None])
        result[row:row+16,:,:3]=np.stack([value[...,0],value[...,1]*.5+.5,value[...,2]*.5+.5],axis=-1)
        result[row:row+16,:,3]=1
    im=bpy.data.images.new(mat.name+' stochastic relief',width=n,height=n)
    im.colorspace_settings.name='Non-Color'; im.pixels.foreach_set(result.reshape(-1))
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    bsdf=nodes.get('Principled BSDF')
    # The old function linked this first texture to base colour before adding its legacy detail nodes.
    base=next(node for node in nodes if node.type=='TEX_IMAGE')
    split=nodes.new('ShaderNodeSeparateXYZ'); links.new(geometry.outputs['Position'],split.inputs[0])
    uv=nodes.new('ShaderNodeCombineXYZ')
    def op(kind,a,b):
        node=nodes.new('ShaderNodeMath');node.operation=kind
        for i,value in enumerate((a,b)):
            if isinstance(value,(int,float,np.floating)):node.inputs[i].default_value=float(value)
            else:links.new(value,node.inputs[i])
        return node.outputs[0]
    links.new(op('DIVIDE',op('SUBTRACT',split.outputs[0],float(lo[0])),span),uv.inputs[0])
    links.new(op('DIVIDE',op('SUBTRACT',op('MULTIPLY',split.outputs[1],-1),float(lo[1])),span),uv.inputs[1])
    tex=nodes.new('ShaderNodeTexImage');tex.image=im;tex.extension='EXTEND';links.new(uv.outputs[0],tex.inputs[0])
    channels=nodes.new('ShaderNodeSeparateColor');links.new(tex.outputs[0],channels.inputs[0])
    colour=nodes.new('ShaderNodeMixRGB');colour.blend_type='MULTIPLY';colour.inputs[0].default_value=1
    links.new(base.outputs['Color'],colour.inputs[1]);links.new(op('ADD',1,op('MULTIPLY',op('SUBTRACT',channels.outputs[0],.5),1.6)),colour.inputs[2]);links.new(colour.outputs[0],bsdf.inputs['Base Color'])
    slope=nodes.new('ShaderNodeCombineXYZ')
    for axis,sign in ((0,1),(1,-1)):
        links.new(op('MULTIPLY',op('SUBTRACT',op('MULTIPLY',channels.outputs[axis+1],2),1),.65*sign),slope.inputs[axis])
    normal=nodes.new('ShaderNodeVectorMath');normal.operation='ADD';links.new(geometry.outputs['Normal'],normal.inputs[0]);links.new(slope.outputs[0],normal.inputs[1])
    normalize=nodes.new('ShaderNodeVectorMath');normalize.operation='NORMALIZE';links.new(normal.outputs[0],normalize.inputs[0]);links.new(normalize.outputs[0],bsdf.inputs['Normal'])
    if 'cover' in mesh:
        # The export's macro alpha retains understorey through the same close-view canopy fade as HDRP.
        import base64
        cover=np.frombuffer(base64.b64decode(mesh['cover']),dtype=np.uint8) if isinstance(mesh['cover'],str) else np.array(mesh['cover'])
        size=int(np.sqrt(cover.size));rgba=np.ones((size,size,4),dtype=np.float32);rgba[:,:,:3]=(cover.reshape(size,size)[::-1,:,None]/255)
        mask=bpy.data.images.new(mat.name+' canopy cover',width=size,height=size);mask.colorspace_settings.name='Non-Color';mask.pixels.foreach_set(rgba.reshape(-1))
        masktex=nodes.new('ShaderNodeTexImage');masktex.image=mask;masktex.extension='EXTEND'
        crown=op('MINIMUM',1,op('MAXIMUM',0,op('DIVIDE',op('SUBTRACT',masktex.outputs['Color'],.1),.6)))
        crown=op('MULTIPLY',op('MULTIPLY',crown,crown),op('SUBTRACT',3,op('MULTIPLY',crown,2)))
        links.new(op('ADD',1,op('MULTIPLY',op('MULTIPLY',op('SUBTRACT',channels.outputs[0],.5),1.6),crown)),colour.inputs[2])
        for axis,sign in ((0,1),(1,-1)):
            links.new(op('MULTIPLY',op('MULTIPLY',op('SUBTRACT',op('MULTIPLY',channels.outputs[axis+1],2),1),.65*sign),crown),slope.inputs[axis])
        weight=op('MINIMUM',1,op('MAXIMUM',0,op('DIVIDE',op('SUBTRACT',masktex.outputs['Color'],.35),.5)))
        smooth=op('MULTIPLY',op('MULTIPLY',weight,weight),op('SUBTRACT',3,op('MULTIPLY',weight,2)))
        previous=bsdf.inputs['Alpha'].links[0].from_socket
        links.new(op('SUBTRACT',1,op('MULTIPLY',op('SUBTRACT',1,previous),smooth)),bsdf.inputs['Alpha'])


args=sys.argv[sys.argv.index('--')+1:]
PHASE=args[0]
render_one(PHASE,args[1])
