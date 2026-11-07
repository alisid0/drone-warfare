"""Town terraces, apartments, village houses, semis, old-core terraces and a filling station with repaint-compatible slots."""
import math

from common import material
from world_asset_common import door, rail, roof, window


def townhouse_row(k, m, lod):
    """Four terraced three-storey homes, 24 metres of frontage with party-wall chimneys and separate doors."""
    k.box(m['Concrete'],(24.4,10.4,.35),(0,0,.175))
    for i,x in enumerate((-9,-3,3,9)):
        wall = ('PlasterCream','Brick','PlasterWhite','PlasterGrey')[i]
        k.box(m[wall],(6,10,9),(x,0,4.85))
        roof(k,m['RoofSlate' if i%2 else 'RoofTile'],x,0,10,6,9.35,2.4,axis='x',over=.05)
        k.box(m['Brick'],(.65,.65,2.0),(x+2.5,.3,11.8))
        door(k,m,x-1.5,-5.1,1,2.2,lod,.35,'Wood')
        if lod<2:
            for floor in range(3):
                for s in (-1,1):
                    for dx in (-1.5,1.5):
                        if floor==0 and s==-1 and dx==-1.5:
                            continue
                        window(k,m,(x+dx,s*5.07,2+3*floor),1.2,1.65,lod)
            for z in (3.4,6.4,9.35):
                k.box(m['Concrete'],(6,.2,.14),(x,-5.08,z))
            k.box(m['Concrete'],(1.4,.7,.35),(x-1.5,-5.4,.175))
            if lod==0:
                k.box(m['RoofSlate'],(1.6,.8,.15),(x-1.5,-5.3,2.8))
                for xx in (x-2.8,x+2.8):
                    k.box(m['PlasterWhite'],(.15,.16,9),(xx,-5.1,4.85))
                for xx in (x+2.3,x+2.7):
                    k.cyl(m['RoofTile'],.12,.4,(xx,.3,13),n=8)
    if lod==2:
        for z in (2,5,8):
            for s in (-1,1):
                k.box(m['WindowGlass'],(22,.08,1.3),(0,s*5.05,z))


def apartment(k, m, lod):
    """A four-storey 30 by 12 metre block with two stair entrances, recessed window rhythm and balconies."""
    k.box(m['Concrete'],(30.4,12.4,.4),(0,0,.2))
    k.box(m['PlasterGrey'],(30,12,12),(0,0,6.4))
    k.box(m['RoofSlate'],(30.5,12.5,.4),(0,0,12.6))
    for z in (2.1,5.1,8.1,11.1):
        if lod==2:
            for s in (-1,1):
                k.box(m['WindowGlass'],(27,.09,1.5),(0,s*6.05,z))
        else:
            for s in (-1,1):
                for x in (-12,-8,-4,4,8,12):
                    window(k,m,(x,s*6.08,z),1.65,1.65,lod)
                for y in (-3,3):
                    window(k,m,(s*15.08,y,z),1.5,1.65,lod,'x')
    for x in (-6,6):
        k.box(m['PlasterWhite'],(2.3,.3,12),(x,-6.1,6.4))
        if lod<2:
            door(k,m,x,-6.3,1.4,2.4,lod,.4,'WindowGlass')
            k.box(m['Concrete'],(3,1.4,.2),(x,-6.5,3.1))
    if lod<2:
        for z in (3.5,6.5,9.5):
            k.box(m['Concrete'],(30.25,12.25,.12),(0,0,z))
        for x in (-12,12):
            for z in (3.9,6.9,9.9):
                k.box(m['Concrete'],(2.8,1.3,.18),(x,6.6,z))
                # Broad solid balcony fronts read well without excessive balusters.
                k.box(m['PlasterWhite'],(2.8,.15,.8),(x,7.2,z+.5))
                if lod==0:
                    rail(k,m['SteelDark'],(x-1.4,6.1,z),(x-1.4,7.2,z),lod)
                    rail(k,m['SteelDark'],(x+1.4,6.1,z),(x+1.4,7.2,z),lod)
        for x in (-8,8):
            k.box(m['Brick'],(.8,.8,1.4),(x,1,13.4))


def shop(k, m, lod):
    """A two-storey corner shop with a chamfered entrance, wraparound display glazing and striped awnings."""
    plan=[(-6,-5),(4,-5),(6,-3),(6,5),(-6,5)]
    k.loft(m['PlasterCream'],[[(x,y,z) for x,y in plan] for z in (.2,6.5)])
    k.loft(m['Concrete'],[[(x*1.02,y*1.02,z) for x,y in plan] for z in (0,.25)])
    k.loft(m['RoofTile'],[[(x*scale,y*scale,z) for x,y in plan] for scale,z in ((1.06,6.5),(.55,8.5))])
    k.box(m['WindowGlass'],(8.5,.1,2.4),(-1,-5.08,1.9))
    k.box(m['WindowGlass'],(.1,6,2.4),(6.08,1,1.9))
    k.box(m['TealMark'],(9.5,.2,.65),(-1,-5.16,3.5))
    k.box(m['TealMark'],(.2,7,.65),(6.16,1,3.5))
    k.box(m['WindowGlass'],(1.6,.16,2.6),(5.06,-4.06,1.5),rot=(0,0,math.pi/4))
    if lod==2:
        return
    for x in (-4,-1,2):
        window(k,m,(x,-5.07,5.2),1.2,1.5,lod)
    for y in (-1,2):
        window(k,m,(6.07,y,5.2),1.2,1.5,lod,'x')
    for x in (-4,0,4):
        window(k,m,(x,5.07,5.2),1.2,1.5,lod)
    k.box(m['Brick'],(.7,.7,1.6),(-3,2,8.1))
    if lod==0:
        for x in (-5,-3,-1,1,3):
            k.box(m['WindowFrame'],(.08,.16,2.5),(x,-5.14,1.9))
        for y in (-2,0,2,4):
            k.box(m['WindowFrame'],(.16,.08,2.5),(6.14,y,1.9))
        for i in range(18):
            k.box(m['TealMark' if i%2 else 'PlasterWhite'],(.5,1.2,.08),(-5.25+i*.5,-5.65,3.05),rot=(.15,0,0))
        for i in range(14):
            k.box(m['TealMark' if i%2 else 'PlasterWhite'],(1.2,.5,.08),(6.65,-2.25+i*.5,3.05),rot=(0,.15,0))
        for x in (-4.8,3):
            k.bar(m['SteelDark'],(x,-5.1,2.5),(x,-6.1,3),.03,n=4)


def house_c(k, m, lod):
    """An L-shaped two-storey farmhouse; RoofTile and PlasterWhite stay present at every LOD for Repaint."""
    k.box(m['ConcreteStained'],(14.2,7.2,.4),(0,2.5,.2))
    k.box(m['ConcreteStained'],(6.2,8.2,.4),(-4,-5,.2))
    k.box(m['PlasterWhite'],(14,7,6),(0,2.5,3.4))
    k.box(m['PlasterWhite'],(6,8,6),(-4,-5,3.4))
    roof(k,m['RoofTile'],0,2.5,7,14,6.4,2.6,axis='x',over=.3)
    roof(k,m['RoofTile'],-4,-5,6,8,6.4,2.6,over=.3)
    for x,y in ((3,2.5),(-4,-5)):
        k.box(m['Brick'],(.65,.65,2),(x,y,9.2))
    door(k,m,2,-1.12,1.2,2.3,lod,.4,'Wood')
    if lod==2:
        return
    for z in (2,5):
        for x in (-5,-1,3,5.5):
            window(k,m,(x,6.08,z),1.1,1.4,lod)
        for x in (0,4.8):
            window(k,m,(x,-1.08,z),1.1,1.4,lod)
        for x in (-5.3,-2.7):
            window(k,m,(x,-9.08,z),1.1,1.4,lod)
        for y in (-6.8,-3.4):
            for x in (-7.08,-.92):
                window(k,m,(x,y,z),1.1,1.4,lod,'x')
        for y in (1,4):
            window(k,m,(7.08,y,z),1.1,1.4,lod,'x')
    k.box(m['RoofTile'],(3,1.6,.18),(2,-1.6,2.9),rot=(.1,0,0))
    k.box(m['Concrete'],(2.2,1,.4),(2,-1.5,.2))
    if lod==0:
        for x in (.7,3.3):
            k.box(m['Wood'],(.12,.12,2.6),(x,-2.2,1.6))
        for x,y in ((3,2.5),(-4,-5)):
            k.box(m['Concrete'],(.85,.85,.15),(x,y,10.25))


def house_d(k, m, lod):
    """A small single-storey cottage with a steep tile roof, dormer, chimney and timber porch."""
    k.box(m['ConcreteStained'],(7.2,6.2,.35),(0,0,.175))
    k.box(m['PlasterWhite'],(7,6,2.8),(0,0,1.75))
    roof(k,m['RoofTile'],0,0,6,7,3.15,2.6,axis='x',over=.35)
    k.box(m['Brick'],(.6,.6,1.8),(2,.4,5.7))
    k.box(m['PlasterWhite'],(1.8,1.4,1.2),(-1,-1.5,4.35))
    roof(k,m['RoofTile'],-1,-1.5,1.8,1.4,4.95,.65,over=.15)
    door(k,m,1,-3.1,1,2.1,lod,.35,'Wood')
    roof(k,m['RoofTile'],1,-3.6,2,1.5,2.5,.6,over=.1)
    if lod==2:
        return
    for x in (-2.3,-.2):
        window(k,m,(x,-3.08,1.8),1,1.15,lod)
    for x in (-2,0,2):
        window(k,m,(x,3.08,1.8),.9,1.15,lod)
    for s in (-1,1):
        for y in (-1.4,1.4):
            window(k,m,(s*3.58,y,1.8),.8,1.1,lod,'x')
    window(k,m,(-1,-2.23,4.4),1, .8,lod)
    for x in (.1,1.9):
        k.box(m['Wood'],(.12,.12,2.5),(x,-4.25,1.25))
    k.box(m['Concrete'],(1.6,1.3,.35),(1,-3.65,.175))
    if lod==0:
        k.box(m['Concrete'],(.85,.85,.16),(2,.4,6.65))
        for x in (-2.3,-.2):
            for dx in (-.72,.72):
                k.box(m['Wood'],(.3,.09,1.2),(x+dx,-3.12,1.8))
        for s in (-1,1):
            k.box(m['Galvanized'],(7.6,.12,.1),(0,s*3.35,3.12))


def extra(m):
    """Adds the plaster and roof tones the old-core terrace uses; their names match the project's photo materials."""
    for name, colour in (('PlasterOchre', (.60, .46, .26)), ('PlasterPink', (.62, .50, .46)),
                         ('RoofTileDark', (.20, .06, .03)), ('RoofTileWarm', (.36, .12, .05))):
        if name not in m:
            m[name] = material(name, colour, roughness=.72)
    return m


def house_semi(k, m, lod):
    """A two-storey semi-detached pair, 12.4 m of frontage under one tile roof, with a party-wall chimney and two doors."""
    k.box(m['Concrete'], (12.6, 9.2, .4), (0, 0, .2))
    k.box(m['PlasterWhite'], (12.4, 9, 5.8), (0, 0, 3.1))
    roof(k, m['RoofTile'], 0, 0, 9, 12.4, 6, 3, axis='x', over=.35)
    k.box(m['Brick'], (.8, .8, 2.2), (0, .6, 8.2))
    for s in (-1, 1):
        door(k, m, s * 4.1, -4.55, 1, 2.2, lod, .4, 'Wood')
        roof(k, m['RoofTile'], s * 4.1, -5.1, 1.2, 2, 2.75, .45, axis='x', over=.1)
    if lod == 2:
        for z in (1.8, 4.6):
            for side in (-1, 1):
                k.box(m['WindowGlass'], (11, .08, 1.2), (0, side * 4.55, z))
        return
    for s in (-1, 1):
        for z in (1.9, 4.7):
            for x in ((1.2, 2.9) if z < 3 else (1.2, 3.0, 4.6)):
                window(k, m, (s * x, -4.54, z), 1.1, 1.3, lod)
            for x in (1.4, 4.2):
                window(k, m, (s * x, 4.54, z), 1.1, 1.3, lod)
        window(k, m, (s * 6.24, 0, 4.7), 1, 1.2, lod, 'x')
    if lod == 0:
        k.box(m['Concrete'], (.9, .9, .15), (0, .6, 9.35))
        for s in (-1, 1):
            k.box(m['Concrete'], (1.6, 1.2, .3), (s * 4.1, -5.1, .15))
            k.box(m['Galvanized'], (.12, .12, 5.6), (s * 6.15, -4.45, 3.2))


def townhouse_old(k, m, lod):
    """Three attached old-core burgher houses of unequal width and height, eaves to the street, each its own plaster and
    roof; RoofTile and PlasterWhite stay on the middle house for Repaint."""
    extra(m)
    units = ((-6.3, 5.6, 6.4, 3.6, 'PlasterOchre', 'RoofTileDark'), (-.1, 6.8, 8.8, 3.4, 'PlasterWhite', 'RoofTile'),
             (6.2, 5.8, 7.2, 3.2, 'PlasterPink', 'RoofTileWarm'))
    k.box(m['Concrete'], (18.4, 11.2, .3), (0, 0, .15))
    for x, w, eave, rise, wall, tile in units:
        k.box(m[wall], (w, 11, eave), (x, 0, eave / 2 + .2))
        roof(k, m[tile], x, 0, 11, w, eave + .2, rise, axis='x', over=.04)
        k.box(m['Brick'], (.6, .6, 1.6), (x + w * .3, 1.5, eave + rise * .7))
    # The middle house's dormer, a gable breaking the roofline toward the street.
    k.box(m['PlasterWhite'], (2.2, 1.6, 1.6), (-.1, -4.3, 9.9))
    roof(k, m['RoofTile'], -.1, -4.3, 2.2, 1.9, 10.7, .9, over=.12)
    door(k, m, -7.5, -5.56, 1, 2.2, lod, .3, 'Wood')
    door(k, m, 7.4, -5.56, 1, 2.2, lod, .3, 'Wood')
    k.box(m['WindowGlass'], (4.6, .1, 2.2), (-.1, -5.55, 1.6))
    if lod == 2:
        for x, w, eave, _, _, _ in units:
            for z in [z for z in (3.8, 6.6) if z < eave - .8]:
                k.box(m['WindowGlass'], (w - 1.4, .08, 1.1), (x, -5.55, z))
        return
    k.box(m['TealMark'], (5.4, .2, .5), (-.1, -5.62, 3.05))
    for x, w, eave, _, _, _ in units:
        floors = [z for z in (3.9, 6.6) if z < eave - .9]
        for z in floors:
            for dx in (-w * .25, w * .25):
                window(k, m, (x + dx, -5.54, z), 1, 1.35, lod)
                window(k, m, (x + dx, 5.54, z), 1, 1.35, lod)
        if x != -.1:
            window(k, m, (x + (1.2 if x < 0 else -1.2), -5.54, 1.7), 1, 1.3, lod)
    if lod == 0:
        for x, w, eave, _, _, _ in units:
            k.box(m['Concrete'], (w, .25, .18), (x, -5.6, eave + .05))
            k.box(m['Galvanized'], (.1, .1, eave), (x + w / 2 - .12, -5.62, eave / 2 + .2))


def filling_station(k, m, lod):
    """A small filling station: a canopy on four posts over two pump islands, and a flat-roofed kiosk behind."""
    k.box(m['Concrete'], (16.4, 9.4, .25), (0, -3.5, .125))
    k.box(m['Concrete'], (16, 9, .7), (0, -3.5, 5.05))
    k.box(m['TealMark'], (16.1, 9.1, .3), (0, -3.5, 4.85))
    for x in (-5.8, 5.8):
        for y in (-6.5, -.5):
            k.box(m['SteelDark'], (.35, .35, 4.6), (x, y, 2.5))
    for x in (-3, 3):
        k.box(m['Concrete'], (1.2, 4.6, .25), (x, -3.5, .37))
        for y in (-4.8, -2.2):
            k.box(m['SteelDark'], (.6, .45, 1.6), (x, y, 1.3))
    k.box(m['PlasterWhite'], (8.6, 6, 3.4), (0, 6, 1.95))
    k.box(m['Concrete'], (8.9, 6.3, .3), (0, 6, 3.8))
    k.box(m['WindowGlass'], (6.2, .1, 2.2), (-.6, 2.97, 1.55))
    k.box(m['TealMark'], (8.7, .15, .5), (0, 2.95, 3.35))
    if lod == 2:
        return
    door(k, m, 3.2, 2.93, 1.2, 2.2, lod, .25, 'WindowGlass')
    k.box(m['SteelDark'], (.25, .25, 6), (7.5, -8, 3))
    k.box(m['TealMark'], (1.6, .3, 1.6), (7.5, -8, 6.6))
    if lod == 0:
        for x in (-3, 3):
            for y in (-4.8, -2.2):
                k.box(m['WindowGlass'], (.4, .47, .5), (x, y, 1.6))
        k.box(m['Galvanized'], (1.5, .9, 2), (-3.5, 9.4, 1.2))
        k.box(m['SteelDark'], (2.4, .6, 1.4), (2.5, 9.3, .9))


BUILDS = [('Scenery_Townhouse_Row',townhouse_row),('Scenery_Apartment',apartment),('Scenery_Shop',shop),
          ('Scenery_House_C',house_c),('Scenery_House_D',house_d),('Scenery_House_Semi',house_semi),
          ('Scenery_Townhouse_Old',townhouse_old),('Scenery_FillingStation',filling_station)]
