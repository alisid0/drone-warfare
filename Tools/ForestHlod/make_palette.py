from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parents[2]
out=root/'Tools/ForestHlod/Review'
out.mkdir(parents=True,exist_ok=True)
species=['oak','birch','spruce','pine']
tints=[(.80,.86,.64),(.82,.85,.63),(.74,.84,.69),(.82,.86,.72)]
def linear(c): return np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)
def srgb(c): return np.where(c<=.0031308,c*12.92,1.055*np.maximum(c,0)**(1/2.4)-.055)
means=[]
areas=[]
import json
models=json.loads((out/"models.json").read_text())
model_names=["Scenery_Tree","Scenery_Birch","Scenery_Spruce","Scenery_Pine"]
for s,t in zip(species,tints):
    a=np.asarray(Image.open(root/f'Assets/DroneSandbox/Textures/PBR/impostor_{s}.png').convert('RGBA'),dtype=float)/255
    a=a[:,512:,:]
    c=linear(a[:,:,:3])*linear(np.array(t))
    means.append(srgb(c[a[:,:,3]>.45].mean(axis=0)))
    box=models[model_names[len(areas)]][2]
    width=box['hi'][0]-box['lo'][0];height=box['hi'][1]-box['lo'][1]
    areas.append(float((a[:,:,3]>.45).mean())*(width/height)**2)
# A seamless quilt of top-view photo crowns; dark seams stay in the average, not in alpha overdraw.
a=np.asarray(Image.open(root/'Assets/DroneSandbox/Textures/PBR/impostor_oak.png').convert('RGBA').crop((520,8,1016,504)).resize((32,32)),dtype=float)/255
l=linear(a[:,:,:3])@np.array([.2126,.7152,.0722]); mask=a[:,:,3]>.45
l=l/max(.001,l[mask].mean()); l=np.where(mask,np.clip(l,.6,1.5),.55)
rng=np.random.default_rng(617)
field=np.full((128,128),.55)
for y in range(-32,160,24):
    for x in range(-32,160,24):
        tile=np.rot90(l,int(rng.integers(4)))
        sx=x+int(rng.integers(-5,6)); sy=y+int(rng.integers(-5,6))
        for j in range(32):
            for i in range(32):
                field[(sy+j)%128,(sx+i)%128]=max(field[(sy+j)%128,(sx+i)%128],tile[j,i])
field/=field.mean()
raw=np.clip(np.round(field*128),0,255).astype('uint8')
resource=root/'Assets/DroneSandbox/Resources/Textures/ForestCrown.bytes'
resource.write_bytes(raw.tobytes())
rows=[]
canvas=Image.new('RGB',(1024,310),(22,28,30)); draw=ImageDraw.Draw(canvas)
for i,(s,c) in enumerate(zip(species,means)):
    rows.append('            new Color32('+', '.join(str(int(round(v*255))) for v in c)+', 255),')
    img=Image.fromarray(np.uint8(np.clip(srgb(linear(c)*field[:,:,None]),0,1)*255)).resize((256,256))
    canvas.paste(img,(i*256,30)); draw.text((i*256+12,8),s,fill='white')
canvas.save(out/'crown-atlas.png')
source='''using UnityEngine;

namespace DroneSandbox
{
    // Linear-light averages of opaque top-view impostor pixels, multiplied by ProjectSetup's exact tints.
    // Regenerate with Tools/ForestHlod/make_palette.py when the source pictures or those tints change.
    public static class ForestPalette
    {
        public static readonly Color32[] Colours =
        {
'''+ '\n'.join(rows)+'''
        };

        // The ground between cards belongs in the brightness reference too, especially for narrow conifers.
        public static readonly Color Ground = new Color(54f / 255f, 70f / 255f, 42f / 255f).linear;
        static readonly float[] AreaPerHeight = { AREA_VALUES };

        // Expected opaque crown coverage on the jittered 12 m grid, allowing for overlapping crowns.
        public static Color Linear(ForestPattern.Stand stand)
        {
            float area = AreaPerHeight[stand.species] * stand.height * stand.height * stand.width * stand.width /
                (ForestPattern.TreeStep * ForestPattern.TreeStep);
            float coverage = 1f - Mathf.Exp(-area);
            Color crown = ((Color)Colours[stand.species]).linear * Mathf.Pow(ForestPattern.Tint(stand.shade), 2.2f);
            // Crossed side cards add light beyond their top footprint; the 60-degree harness view measures this correction.
            return Color.Lerp(Ground, crown, coverage) * 1.15f;
        }
    }
}
'''
source=source.replace('AREA_VALUES', ', '.join(f'{a:.8f}f' for a in areas))
(root/'Assets/DroneSandbox/Scripts/ForestPalette.cs').write_bytes(source.replace('\n','\r\n').encode())
import json
(out/'palette.json').write_text(json.dumps(dict(species=species,tints=tints,srgb_means=[x.tolist() for x in means],area_per_height=areas,quilt_mean=float(raw.mean()/128)),indent=2))
print('Generated canopy palette and normalized photo quilt:',means)
