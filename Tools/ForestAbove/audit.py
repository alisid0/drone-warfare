"""Check projected coverage and shader/material assets independently of Unity native execution."""
from pathlib import Path
import json
import math
import re
import struct
import zlib

ROOT = Path(__file__).resolve().parents[2]
out = ROOT / 'Tools/ForestAbove/Review'
out.mkdir(exist_ok=True)


def screen_coverage(scene):
    """Count occupied 200-by-112-pixel cells in the near half, comparing logical stems with submitted crowns."""
    eye, target = scene['eye'], scene['target']
    direction = [b-a for a,b in zip(eye,target)]; norm = math.sqrt(sum(v*v for v in direction)); direction = [v/norm for v in direction]
    right = [direction[2],0,-direction[0]]; norm = math.sqrt(sum(v*v for v in right)); right = [v/norm for v in right]
    up = [direction[1]*right[2], direction[2]*right[0]-direction[0]*right[2], -direction[1]*right[0]]
    logical, drawn = [0]*32, [0]*32
    for tree in scene['logical']:
        offset = [tree['p'][k]-eye[k] + (tree['height']*.75 if k==1 else 0) for k in range(3)]
        depth = sum(a*b for a,b in zip(offset,direction))
        if depth <= 0: continue
        x = sum(a*b for a,b in zip(offset,right))/(depth*math.tan(math.pi/8)*1600/900)
        y = sum(a*b for a,b in zip(offset,up))/(depth*math.tan(math.pi/8))
        if not (-1 <= x < 1 and -1 <= y < 0): continue
        k = int((y+1)*4)*8+int((x+1)*4)
        logical[k] += 1
        if tree['tier'] >= 0: drawn[k] += 1
    wooded = [k for k in range(32) if logical[k] >= 8]
    return dict(woodedCells=len(wooded),cellsWithAtLeastFourCrowns=sum(drawn[k]>=4 for k in wooded),logical=logical,drawn=drawn)


def shader_contract():
    """Validate serialized inputs, shared constant-buffer layout, and the independent coarse detail path."""
    folder = ROOT/'Assets/DroneSandbox/Resources/Shaders'
    shader = (folder/'ForestLit.shader').read_text()
    props = (folder/'ForestLitProperties.hlsl').read_text().split('CBUFFER_START(UnityPerMaterial)')[1].split('CBUFFER_END')[0]
    for name in ['Camera','Distances','Tier','Build','Enabled','Selection','Projection','Crown','Radius','Origin','Viewport']:
        assert '_Forest'+name+'(' in shader
        assert re.search(r'float4? _Forest'+name+r';',props)
    assert shader.index('#define VARYINGS_NEED_POSITION_WS') < shader.index('LitDepthPass.hlsl')
    data = (folder/'ForestLitData.hlsl').read_text()
    assert 'surfaceData.normalWS = normalize' in data and 'SAMPLE_TEXTURE2D(_ForestDetail' in data
    assert data.index('surfaceData.normalWS = normalize') < data.index('// Use bent normal to sample GI')
    meta = (ROOT/'Assets/DroneSandbox/Resources/Textures/ForestDetail.png.meta').read_text()
    assert 'sRGBTexture: 0' in meta and 'enableMipMap: 1' in meta and 'textureCompression: 0' in meta
    assert 'da29c1a119c84a899701210a1fb7b6e2' in (ROOT/'Assets/DroneSandbox/Resources/Materials/Forest/Canopy.mat').read_text()
    materials = list((ROOT/'Assets/DroneSandbox/Resources/Materials/Forest').glob('*.mat'))
    for path in materials: assert 'm_EnableInstancingVariants: 1' in path.read_text()
    return dict(serializedForestMaterials=len(materials),detailTextureBytes=(ROOT/'Assets/DroneSandbox/Resources/Textures/ForestDetail.png').stat().st_size,nativeShaderCompilerExecuted=False)


def luminance():
    """Measure the shared tile mean and mature-stand linear luminance against the fixed review pasture."""
    data=(ROOT/'Assets/DroneSandbox/Resources/Textures/ForestDetail.png').read_bytes()
    compressed=b'';at=8
    while at<len(data):
        count=struct.unpack('>I',data[at:at+4])[0]
        if data[at+4:at+8]==b'IDAT':compressed+=data[at+8:at+8+count]
        at+=12+count
    raw=zlib.decompress(compressed)
    mean=sum(raw[y*1537+1+x*3] for y in range(512) for x in range(512))/(512*512*255)
    def light(rgb):
        values=[c/255/12.92 if c/255<=.04045 else ((c/255+.055)/1.055)**2.4 for c in rgb]
        return sum(a*b for a,b in zip(values,[.2126,.7152,.0722]))
    pasture=light((105,126,86));floor=light((49,70,45));cover=1-math.exp(-math.pi*4*4*.0277)
    factor=(.5+mean)*(.82+.36*mean)
    palette=[(84,108,70),(89,112,73),(62,88,62),(66,92,63)]
    ratios=[round((floor*(1-cover)+light(c)*cover)*1.06*factor/pasture,3) for c in palette]
    assert all(.60<=v<=.75 for v in ratios[:2]) and all(.40<=v<=.60 for v in ratios[2:]),ratios
    return dict(detailMean=round(mean,4),matureStandToPastureLinearRatios=ratios,illuminationAndFogExcluded=True)


report = dict(shader=shader_contract(),luminance=luminance(),views={})
for path in (ROOT/'Temp/forest-above').glob('*-*/scene.json'):
    scene = json.loads(path.read_text())
    report['views'][path.parent.name] = screen_coverage(scene)
    report['views'][path.parent.name]['effectivePixels'] = [round(1000/v,2) if v else 0 for v in scene['thresholds']] if path.parent.name.startswith('after') else None
    if path.parent.name.startswith('after'):
        view = report['views'][path.parent.name]
        assert view['woodedCells'] > 0 and view['cellsWithAtLeastFourCrowns'] == view['woodedCells'], 'Near-half crown coverage'
    metrics = path.with_name('metrics.json')
    if metrics.exists():
        report['views'][path.parent.name]['metrics'] = json.loads(metrics.read_text())
        (out/(path.parent.name+'-metrics.json')).write_text(metrics.read_text()+'\n')
(out/'audit.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({name:{k:v for k,v in view.items() if k not in ('logical','drawn','metrics')} for name,view in report['views'].items()}))
