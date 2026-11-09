"""Large factories and tanks give industrial sectors their own roofscape and skyline."""
import math

from kit import yprism
from world_asset_common import door, rail, ring_rail, stairs, window


def factory_hall(k, m, lod):
    """A 60 by 30 metre factory with six sawtooth roof bays and a row of loading doors."""
    k.box(m['Concrete'], (60,30,.4), (0,0,.2))
    k.box(m['Brick'], (60,30,7.6), (0,0,4.2))
    for i in range(6):
        x = -30+i*10
        k.loft(m['RoofMetal'], yprism([(x,8),(x+10,8),(x+10,11.5)], -15.3,15.3))
        k.box(m['WindowGlass'], (.08,29,2.8), (x+10.05,0,9.65))
    for x in (-24,-12,0,12,24):
        door(k,m,x,-15.12,4.5,4.8,lod,.4)
    if lod == 2:
        return
    for x in range(-30,31,6):
        for s in (-1,1):
            k.box(m['Concrete'], (.35,.3,7.8), (x,s*15.1,4.2))
    for x in range(-27,28,6):
        for s in (-1,1):
            window(k,m,(x,s*15.14,6.5),3.6,1.3,lod)
    k.box(m['Concrete'], (59,2,.7),(0,-16,.35))
    for x in (-28,28):
        stairs(k,m,x,-18,1.5,2,.7,lod)
    if lod == 0:
        for i in range(6):
            x = -20+i*10
            for y in range(-14,15,2):
                k.box(m['Galvanized'], (.12,.08,3), (x+.1,y,9.65))
            for y in range(-14,15,3):
                k.bar(m['Galvanized'], (x-10,y,8.04), (x,y,11.54), .035,n=4)
        for x in (-25,-5,15):
            k.cyl(m['SteelDark'],.4,1.5,(x,9,10.6),n=12)
            k.cyl(m['Galvanized'],.65,.15,(x,9,11.4),n=12)


def storage_tank(k, m, lod):
    """An 18 metre tank with a conical cap, circular guardrail and a real helical access stair."""
    n = (48,24,12)[lod]
    k.cyl(m['Concrete'],9.5,.5,(0,0,.25),n=n)
    k.lathe(m['Galvanized'],[(.5,9),(13.5,9),(15,1),(15,0)],(0,0,0),n=n)
    if lod == 2:
        return
    ring_rail(k,m['SteelDark'],8.9,13.65,lod,n=24 if lod==0 else 12)
    steps = 72 if lod == 0 else 24
    for i in range(steps):
        a, b = -math.pi + 1.75*math.pi*i/steps, -math.pi + 1.75*math.pi*(i+1)/steps
        z = .5+13*(i+1)/steps
        pts = [(r*math.cos(t),r*math.sin(t),z) for r,t in ((9.1,a),(10.2,a),(10.2,b),(9.1,b))]
        k.plate(m['SteelDark'],pts,(0,0,1),.1)
        if i%3 == 0:
            p=(10.2*math.cos(a),10.2*math.sin(a),z)
            k.bar(m['Galvanized'],p,(p[0],p[1],z+1),.035,n=4)
        k.bar(m['Galvanized'],(10.2*math.cos(a),10.2*math.sin(a),z+1),(10.2*math.cos(b),10.2*math.sin(b),z+1+13/steps),.04,n=4)
    k.box(m['SteelDark'],(2.2,2,.18),(-6.8,6.8,13.6))
    k.cyl(m['SteelDark'],.5,.3,(0,0,15.15),n=12)
    if lod == 0:
        for z in (3.6,6.8,10):
            k.lathe(m['SteelDark'],[(z,9.01),(z+.055,9.01)],(0,0,0),n=n,caps=False)
        k.bar(m['RustySteel'],(0,-9,1),(0,-10.2,1),.25,n=12)
        for a in (0,math.pi/2,math.pi,3*math.pi/2):
            k.cyl(m['SteelDark'],.1,.5,(9.2*math.cos(a),9.2*math.sin(a),.6),n=8)


def pipe_rack(k, m, lod):
    """A 20 metre rack along Y, with coplanar open pipe ends that butt at +/-10 metres."""
    n = (10,7,5)[lod]
    for x,r,z in ((-1.1,.35,4.9),(0,.25,4.9),(1,.45,5)):
        k.bar(m['Galvanized' if x<0 else 'RustySteel'],(x,-10,z),(x,10,z),r,n=n,caps=False)
    for y in (-8,0,8) if lod<2 else (-8,8):
        for x in (-1.9,1.9):
            k.box(m['SteelDark'],(.18,.2,4.6),(x,y,2.3))
            if lod<2:
                k.box(m['Concrete'],(.75,.75,.35),(x,y,.175))
        k.box(m['SteelDark'],(4,.22,.25),(0,y,4.5))
        if lod==0:
            for s in (-1,1):
                k.bar(m['SteelDark'],(s*1.9,y,3.6),(s*.7,y,4.4),.065,n=4)
    if lod==0:
        for x,r,z in ((-1.1,.35,4.9),(0,.25,4.9),(1,.45,5)):
            for y in (-8,8):
                # Thin flange rings, away from the open end planes so repeated runs remain seamless.
                from kit import TO_BACK
                k.lathe(m['SteelDark'],[(-.055,r*1.02),(.055,r*1.02),(.055,r*1.18),(-.055,r*1.18)],(x,y,z),TO_BACK,n=10,closed=True)


def office_block(k, m, lod):
    """A three-storey administrative block, horizontal window bands and a projecting entrance canopy."""
    k.box(m['Concrete'],(24,14,.4),(0,0,.2))
    k.box(m['PlasterCream'],(23.6,13.6,9.3),(0,0,5.05))
    k.box(m['RoofSlate'],(24.2,14.2,.35),(0,0,9.85))
    for z in (2,5.1,8.2):
        for s in (-1,1):
            if lod==2:
                k.box(m['WindowGlass'],(21,.08,1.35),(0,s*6.85,z))
            else:
                for x in range(-10,11,2):
                    window(k,m,(x,s*6.85,z),1.35,1.45,lod)
                for y in (-4,0,4):
                    window(k,m,(s*11.85,y,z),1.5,1.45,lod,'x')
    k.box(m['Concrete'],(5,2,.3),(0,-7.6,2.9))
    if lod<2:
        door(k,m,0,-6.95,2.8,2.5,lod,.4,'WindowGlass')
        for z in (3.55,6.65,9.7):
            k.box(m['Concrete'],(24,14,.14),(0,0,z))
        stairs(k,m,0,-8.6,4,1.6,.4,lod)
        for x in (-2,2):
            k.box(m['Galvanized'],(.12,.12,2.7),(x,-8.2,1.5))
        if lod==0:
            for x in (-7,7):
                k.box(m['SteelDark'],(2,3,1),(x,2,10.5))


def cooling_tower(k, m, lod):
    """A 48 metre hyperboloid shell, open at the top, on raked concrete legs."""
    n = (48,24,12)[lod]
    levels = [(3,18),(8,16),(17,12.6),(28,10.5),(37,10.8),(48,13)]
    if lod==2:
        levels = [(3,18),(27,10.5),(48,13)]
    # Closed shell profile includes an inner wall and lip, leaving an actual open throat.
    profile = levels + [(z,r-.6) for z,r in reversed(levels)]
    k.lathe(m['ConcreteStained'],profile,(0,0,0),n=n,closed=True)
    k.cyl(m['Interior'],16,.1,(0,0,.2),n=n)
    legs = (24,12,6)[lod]
    for i in range(legs):
        a = 2*math.pi*i/legs
        k.bar(m['Concrete'],(19*math.cos(a),19*math.sin(a),0),(17.8*math.cos(a+.04),17.8*math.sin(a+.04),3.2),.45,n=4)
    if lod<2:
        ring_rail(k,m['Galvanized'],13,48,lod,n=24 if lod==0 else 12)
    if lod==0:
        for z,r in levels[1:-1]:
            k.lathe(m['Concrete'],[(z-.06,r+.03),(z+.06,r+.03)],(0,0,0),n=n,caps=False)


BUILDS = [('Scenery_FactoryHall',factory_hall),('Scenery_StorageTank',storage_tank),
          ('Scenery_PipeRack',pipe_rack),('Scenery_OfficeBlock',office_block),('Scenery_CoolingTower',cooling_tower)]
