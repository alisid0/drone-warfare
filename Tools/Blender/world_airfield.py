"""Airfield landmarks at real-world scale, with hand-authored silhouette-preserving LODs."""
import math

from kit import TO_BACK, TO_X, yprism
from world_asset_common import door, rail, roof, stairs, window


def control_tower(k, m, lod):
    """Four occupied levels, an overhanging glazed cab and a roof antenna make a clear airfield landmark."""
    k.box(m['Concrete'], (10, 10, .4), (0, 0, .2))
    k.box(m['PlasterGrey'], (7, 7, 11.8), (0, 0, 6.3))
    k.box(m['Concrete'], (10, 10, .45), (0, 0, 12.1))
    k.box(m['WindowGlass'], (9, 9, 2.6), (0, 0, 13.6))
    k.box(m['RoofMetal'], (10.4, 10.4, .45), (0, 0, 15.1))
    k.cyl(m['SteelDark'], .1, 3.5, (0, 0, 17), n=6)
    if lod == 2:
        return
    for z in (3.5, 6.5, 9.5):
        k.box(m['Concrete'], (7.15, 7.15, .16), (0, 0, z))
        for s in (-1, 1):
            for x in (-2, 0, 2):
                window(k, m, (x, s*3.55, z-1.2), .9, 1.4, lod)
                window(k, m, (s*3.55, x, z-1.2), .9, 1.4, lod, 'x')
    for s in (-1, 1):
        for t in (-4.5, -2.25, 0, 2.25, 4.5):
            k.box(m['Galvanized'], (.10, .16, 2.65), (t, s*4.55, 13.6))
            k.box(m['Galvanized'], (.16, .10, 2.65), (s*4.55, t, 13.6))
        rail(k, m['Galvanized'], (-4.8, s*4.8, 12.35), (4.8, s*4.8, 12.35), lod)
        rail(k, m['Galvanized'], (s*4.8, -4.8, 12.35), (s*4.8, 4.8, 12.35), lod)
    door(k, m, 0, -3.58, 1.4, 2.4, lod, .4)
    stairs(k, m, 0, -5, 2, 1.4, .4, lod)
    if lod == 0:
        for x in (-2.4, 2.4):
            k.box(m['SteelDark'], (1.5, 1, .7), (x, 1, 15.7))
        k.bar(m['Galvanized'], (-1, 0, 17.6), (1, 0, 17.6), .04, n=6)
        for y in (-1, 0, 1):
            k.bar(m['Galvanized'], (-1, y*.4, 17.6), (1, y*.4, 17.6), .025, n=6)


def aircraft_shelter(k, m, lod):
    """A 25 by 35 metre concrete barrel vault with a deep open doorway and heavy sliding doors."""
    n = (32, 16, 8)[lod]
    # Annular half arch, closed at both ends, with the inner surface visible through the mouth.
    outer = [(12.5*math.cos(math.pi*i/n), 2 + 8.5*math.sin(math.pi*i/n)) for i in range(n+1)]
    inner = [(11.3*math.cos(math.pi*i/n), 2 + 7.3*math.sin(math.pi*i/n)) for i in reversed(range(n+1))]
    k.loft(m['ConcreteStained'], yprism(outer + inner, -17.5, 17.5))
    for s in (-1, 1):
        k.box(m['Concrete'], (1.2, 35, 2), (s*11.9, 0, 1))
    k.box(m['Asphalt'], (22.6, 35, .12), (0, 0, .06))
    k.loft(m['Concrete'], yprism([(-11.3, 0), (11.3, 0)] + [(11.3*math.cos(math.pi*i/n), 2+7.3*math.sin(math.pi*i/n)) for i in range(n+1)], 17.2, 17.5))
    for s in (-1, 1):
        k.box(m['OliveDark'], (5.6, .5, 7), (s*8.3, -17.3, 3.5))
    if lod == 2:
        return
    for y in (-17.3, 0, 17.2):
        rib_outer = [(12.57*math.cos(math.pi*i/n), 2+8.57*math.sin(math.pi*i/n)) for i in range(n+1)]
        k.loft(m['Concrete'], yprism(rib_outer + list(reversed(outer)), y-.12, y+.12))
    for s in (-1, 1):
        for x in (5.8, 8.3, 10.8):
            k.box(m['SteelDark'], (.14, .12, 6.7), (s*x, -17.6, 3.5))
        for z in (1, 3.5, 6):
            k.box(m['SteelDark'], (5.4, .12, .12), (s*8.3, -17.6, z))
    k.box(m['SteelDark'], (24, .45, .3), (0, -17.6, 7.2))
    if lod == 0:
        for y in range(-15, 17, 4):
            for i in range(n):
                a, b = outer[i], outer[i+1]
                k.bar(m['Concrete'], (a[0]*1.002, y, a[1]+.025), (b[0]*1.002, y, b[1]+.025), .035, n=4)
        for x in (-10, 10):
            k.cyl(m['Galvanized'], .3, .8, (x, 13, 8), n=12)


def hangar_large(k, m, lod):
    """A 40 by 50 metre steel hangar, broad sliding door, roof lights and repeated structural bays."""
    k.box(m['Concrete'], (40, 50, .45), (0, 0, .225))
    k.box(m['RoofMetal'], (40, 50, 11), (0, 0, 5.8))
    roof(k, m['RoofMetal'], 0, 0, 40, 50, 11.3, 4, over=.35)
    door(k, m, 0, -25.12, 32, 9.4, lod, .45, 'Galvanized')
    if lod == 2:
        # Strong longitudinal roof-light strips survive the far model.
        for x in (-10, 10):
            k.box(m['WindowGlass'], (3, 42, .12), (x, 0, 13.35), rot=(0, math.copysign(.1974, x), 0))
        return
    for x in (-10, 10):
        for y in range(-20, 21, 8):
            k.box(m['WindowGlass'], (3, 5.5, .12), (x, y, 13.35), rot=(0, math.copysign(.1974, x), 0))
    for s in (-1, 1):
        k.box(m['Concrete'], (.25, 50, 1.2), (s*20, 0, .6))
        for y in range(-24, 25, 6):
            k.box(m['SteelDark'], (.24, .28, 10.8), (s*20.1, y, 5.8))
        for x in (-16, -8, 0, 8, 16):
            k.box(m['SteelDark'], (.16, .25, 9.3), (x, -25.24, 5.1))
    door(k, m, 15, 25.1, 2, 2.6, lod, .45)
    if lod == 0:
        for s in (-1, 1):
            for y in range(-24, 25):
                k.box(m['Galvanized'], (.07, .055, 9.4), (s*20.16, y, 6.4))
            k.box(m['Galvanized'], (.25, 50.5, .2), (s*20.35, 0, 11.3))
            for y in (-23, 23):
                k.cyl(m['Galvanized'], .09, 10.6, (s*20.45, y, 5.5), n=8)
        for x in range(-18, 19):
            k.box(m['RoofMetal'], (.055, .06, 9), (x, 25.17, 6))
        k.box(m['SteelDark'], (34, .65, .6), (0, -25.4, 10.5))


def windsock(k, m, lod):
    """A six metre mast with an open, striped taper, readable by shape even at the lowest LOD."""
    n = (12, 8, 5)[lod]
    k.box(m['Concrete'], (1.1, 1.1, .18), (0, 0, .09))
    k.cyl(m['Galvanized'], .065, 6, (0, 0, 3.1), n=n)
    for j in range(5):
        x0, x1 = .6*j, .6*(j+1)
        r0, r1 = .45-.065*j, .45-.065*(j+1)
        loops = [[(x, r*math.cos(2*math.pi*i/n), 5.8-.18*x+r*math.sin(2*math.pi*i/n)) for i in range(n)] for x, r in ((x0,r0),(x1,r1))]
        k.loft(m['SignalOrange' if j%2==0 else 'PlasterWhite'], loops, caps=False)
    if lod < 2:
        k.lathe(m['SteelDark'], [(-.025,.43),(.025,.43),(.025,.47),(-.025,.47)], (0,0,5.8), TO_X, n=n, closed=True)
        for y in (-.4,.4):
            k.bar(m['SteelDark'], (0,0,6.1), (0,y,5.8), .015, n=4)


def fuel_farm(k, m, lod):
    """Four horizontal tanks in a concrete spill bund, with shared filling manifold and access deck."""
    k.box(m['ConcreteStained'], (25, 19, .25), (0, 0, .125))
    for s in (-1, 1):
        k.box(m['Concrete'], (25, .45, 1.2), (0, s*9.25, .6))
        k.box(m['Concrete'], (.45, 18.1, 1.2), (s*12.25, 0, .6))
    n = (24, 12, 6)[lod]
    for x in (-9, -3, 3, 9):
        profile = [(-6.4,0),(-6.2,1.4),(-5.5,2),(5.5,2),(6.2,1.4),(6.4,0)] if lod < 2 else [(-6.4,0),(-5.8,2),(5.8,2),(6.4,0)]
        k.lathe(m['Galvanized'], profile, (x,0,3.1), TO_BACK, n=n)
        if lod < 2:
            for y in (-4,4):
                k.box(m['Concrete'], (3.4, .7, 1.4), (x,y,.95))
            k.cyl(m['SteelDark'], .35,.3,(x,0,5.15),n=12)
            k.bar(m['RustySteel'], (x,-6,2.4), (x,-7.3,2.4), .14, n=8)
            if lod == 0:
                for y in (-4,4):
                    k.lathe(m['SteelDark'], [(-.1,2.01),(.1,2.01)], (x,y,3.1), TO_BACK, n=n, caps=False)
    if lod < 2:
        k.box(m['SteelDark'], (23,1,.15),(0,0,5.35))
        for y in (-.5,.5):
            rail(k,m['Galvanized'],(-11.5,y,5.45),(11.5,y,5.45),lod)
        k.bar(m['RustySteel'],(-10,-7.3,2.4),(10,-7.3,2.4),.18,n=8)
        if lod == 0:
            stairs(k,m,11,-8,1,7.5,5.3,lod)
            for x in (-9,-3,3,9):
                k.cyl(m['SignalOrange'],.22,.07,(x,-7.3,2.9),n=10)


BUILDS = [('Scenery_ControlTower', control_tower), ('Scenery_AircraftShelter', aircraft_shelter),
          ('Scenery_HangarLarge', hangar_large), ('Scenery_Windsock', windsock), ('Scenery_FuelFarm', fuel_farm)]
