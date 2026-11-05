"""Unarmed long-range Norvane reconnaissance aircraft; -Y forward, metres, nominal CG at the origin."""
import math

import bpy
from mathutils import Vector

from kit import Kit, TO_BACK, TO_FWD, TO_X
from world_asset_common import mesh_from_kit


def ball(k, mat, centre, r, lod):
    """A camera or fairing sphere with explicit ring counts for each LOD."""
    rings, n = ((8,16),(5,10),(3,6))[lod]
    k.lathe(mat,[(-r*math.cos(math.pi*i/rings),r*math.sin(math.pi*i/rings)) for i in range(rings+1)],centre,n=n)


def wing(k, mat, side, lod):
    """A tapered high-aspect-ratio wing with cambered sections and slight outer dihedral."""
    stations = [(0,-.38,.66,.13),(.65,-.36,.6,.14),(2.35,-.15,.32,.22),(2.6,-.08,.19,.28)]
    if lod==1:
        stations.pop(1)
    if lod==2:
        stations = [stations[0],stations[-1]]
    foil = [(0,.0),(.08,.08),(.3,.12),(.65,.065),(1,0),(.7,-.025),(.3,-.035),(.07,-.02)]
    if lod==2:
        foil = [(0,0),(.3,.12),(1,0),(.3,-.035)]
    k.loft(mat,[[(side*x,y+u*ch,z+v*ch) for u,v in foil] for x,y,ch,z in stations])


def recon(k, m, lod):
    """A slender 5.2 m twin-boom pusher with an inverted-V tail and a large nose camera gimbal."""
    sections=[(-1.45,.015,.025,0),(-1.3,.19,.20,-.01),(-.9,.31,.32,0),
              (-.25,.34,.34,0),(.4,.29,.29,.015),(.85,.16,.17,.02),(.98,.09,.10,.02)]
    if lod==1:
        sections=[sections[i] for i in (0,1,3,4,6)]
    if lod==2:
        sections=[sections[i] for i in (0,3,6)]
    n=(20,12,6)[lod]
    k.loft(m['ShellGray'],[[(w/2*math.cos(2*math.pi*i/n),y,zc+h/2*math.sin(2*math.pi*i/n)) for i in range(n)] for y,w,h,zc in sections])
    for s in (-1,1):
        wing(k,m['ShellGray'],s,lod)
        k.bar(m['OliveDark'],(s*.65,.05,.06),(s*.65,2.12,.04),.04,n=(12,8,4)[lod])
        # Two canted tail planes meet above the booms; the prop disc is clear between them.
        k.plate(m['ShellGray'],[(s*.68,1.76,.04),(s*.68,2.3,.04),(0,2.42,.72),(0,2.08,.72)],(s*.7,0,.7),.035)
        if lod<2:
            # Norvane identification panels and visible aileron hinge lines on both wings.
            k.plate(m['TealMark'],[(s*1.95,-.14,.268),(s*2.12,-.12,.274),(s*2.12,.16,.27),(s*1.95,.18,.265)],(0,0,1),.002)
            k.bar(m['OliveDark'],(s*1,.19,.19),(s*2.36,.13,.235),.003,n=4)
    ball(k,m['ShellGray'],(0,-1.06,-.25),.16,lod)
    k.cyl(m['Lens'],.095,.03,(0,-1.203,-.28),TO_FWD,n=(20,12,6)[lod])
    if lod<2:
        k.cyl(m['SteelDark'],.065,.13,(0,-1.06,-.12),n=12)
        for s in (-1,1):
            k.box(m['OliveDark'],(.035,.11,.19),(s*.175,-1.06,-.19))
        k.cyl(m['Lens'],.035,.03,(.095,-1.176,-.23),TO_FWD,n=10)
        k.lathe(m['Galvanized'],[(-.008,.095),(.008,.095),(.008,.108),(-.008,.108)],(0,-1.212,-.28),TO_FWD,n=20,closed=True)
        k.cyl(m['SteelDark'],.095,.16,(0,.99,.02),TO_BACK,n=16)
        k.box(m['Olive'],(.21,.46,.016),(0,-.24,.174))
        k.bar(m['SteelDark'],(0,.25,.17),(0,.3,.42),.009,n=6)
        k.lathe(m['ShellGray'],[(0,.06),(.025,.06),(.04,0)],(0,-.5,.167),n=12)
    if lod==0:
        # Inspection covers, cooling slots and a protected belly skid are close-camera details only.
        for y in (-.4,-.25,-.1):
            k.box(m['SteelDark'],(.12,.015,.004),(0,y,.185))
        for s in (-1,1):
            for y in (.4,.46,.52,.58):
                k.box(m['SteelDark'],(.008,.035,.065),(s*.126,y,.03))
        k.bar(m['OliveDark'],(0,-.6,-.17),(0,.45,-.16),.018,n=8)


def propeller(m, lod, parent):
    """One independently pivoted Prop_ child per LOD; its shaft points along Blender +Y (Unity -Z)."""
    k=Kit()
    foil=[(-.5,0),(-.2,.1),(.3,.07),(.5,0),(.2,-.04),(-.3,-.03)] if lod<2 else [(-.5,0),(0,.1),(.5,0),(0,-.05)]
    stations=[(.06,.035,35),(.18,.075,25),(.35,.065,14),(.5,.035,8)] if lod<2 else [(.06,.04,25),(.5,.035,8)]
    for s in (-1,1):
        loops=[]
        for radius,chord,twist in stations:
            a=math.radians(twist)
            loops.append([(s*radius,u*chord*math.sin(a)+v*chord*math.cos(a),s*(u*chord*math.cos(a)-v*chord*math.sin(a))) for u,v in foil])
        k.loft(m['Prop'],loops)
    k.lathe(m['Prop'],[(-.04,.055),(.03,.055),(.10,0)],(0,0,0),TO_BACK,n=(16,10,6)[lod])
    obj=mesh_from_kit(k,f'Prop_0_LOD{lod}',parent)
    obj.location=(0,1.14,.02)
    bpy.context.view_layer.update()


BUILDS=[('Drone_Recon',recon)]
