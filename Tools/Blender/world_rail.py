"""Rail yard buildings and freight stock matching the existing 1.435 m track gauge and rail height."""
import math

from kit import TO_BACK, TO_X, yprism
from world_asset_common import door, rail, roof, stairs, window

RAIL_TOP = .592
AXLE_Z = RAIL_TOP + .46
RAIL_X = .7535


def signal_box(k, m, lod):
    """A raised glazed signal cabin on a brick base with an external stair and tiled roof."""
    k.box(m['Concrete'],(8.4,5.4,.3),(0,0,.15))
    k.box(m['Brick'],(8,5,3.2),(0,0,1.9))
    k.box(m['PlasterCream'],(8.5,5.5,2.7),(0,0,4.85))
    roof(k,m['RoofTile'],0,0,8.5,5.5,6.2,1.8,over=.35)
    for s in (-1,1):
        if lod==2:
            k.box(m['WindowGlass'],(7.7,.08,1.5),(0,s*2.79,5.1))
        else:
            for x in (-3,-1,1,3):
                window(k,m,(x,s*2.79,5.1),1.6,1.5,lod)
            window(k,m,(s*4.29,0,5.1),3.6,1.5,lod,'x')
    k.box(m['Concrete'],(2,1.5,.2),(5,-1.75,3.4))
    stairs(k,m,5,-8,1.4,5.5,3.3,lod)
    if lod<2:
        door(k,m,0,-2.56,1.1,2.2,lod,.3,'Wood')
        k.box(m['Brick'],(.6,.6,2.3),(-2.4,.4,7))
        for z in (3.4,6.15):
            k.box(m['Concrete'],(8.7,5.7,.16),(0,0,z))
        rail(k,m['Galvanized'],(4.2,-2.5,3.5),(5.8,-2.5,3.5),lod)
        if lod==0:
            for x in (-3,3):
                window(k,m,(x,-2.55,1.8),.7,1.2,lod)


def freight_shed(k, m, lod):
    """A 40 by 15 metre freight shed with raised loading platforms and a broad track-side canopy."""
    k.box(m['Concrete'],(40,19,1.2),(0,0,.6))
    k.box(m['Brick'],(40,15,5.5),(0,0,3.95))
    roof(k,m['RoofSlate'],0,0,15,40,6.7,3,axis='x',over=.4)
    for s in (-1,1):
        k.box(m['RoofMetal'],(40.5,2.4,.2),(0,s*8.6,5.5),rot=(s*.08,0,0))
        for x in (-15,-5,5,15):
            door(k,m,x,s*7.6,4.5,3.7,lod,1.2,'Wood')
    if lod==2:
        return
    for s in (-1,1):
        for x in (-19,-10,0,10,19):
            k.box(m['SteelDark'],(.16,.16,4.2),(x,s*9.4,3.3))
            k.bar(m['SteelDark'],(x,s*9.4,4.4),(x,s*7.7,5.5),.065,n=4)
        for x in (-19.5,19.5):
            k.box(m['Concrete'],(.5,.3,5.5),(x,s*7.55,3.95))
    stairs(k,m,18,-12,2,3,1.2,lod)
    if lod==0:
        for x in range(-18,19,3):
            for s in (-1,1):
                k.bar(m['Galvanized'],(x,s*7.8,6.75),(x,0,9.75),.04,n=4)
        for x in (-18,-12,-8,-2,2,8,12,18):
            k.box(m['Concrete'],(.25,.4,5.4),(x,7.65,3.9))
        for s in (-1,1):
            window(k,m,(s*20.05,0,7.5),3,1.2,lod,'x')


def gantry_crane(k, m, lod):
    """A 16 metre portal spanning two tracks with a top-running trolley, cab and hanging hook."""
    for s in (-1,1):
        k.box(m['SteelDark'],(.6,10,.6),(s*7,0,.65))
        for y in (-4,4):
            k.box(m['RustySteel'],(.65,.65,11),(s*7,y,6.2))
            if lod<2:
                k.cyl(m['SteelDark'],.4,.5,(s*7,y,.4),TO_X,n=12 if lod==0 else 8)
        if lod<2:
            for y in (-4,4):
                k.bar(m['RustySteel'],(s*7,0,6),(s*7,y,11.7),.14,n=6)
                k.bar(m['RustySteel'],(s*7,0,6),(s*7,y,1),.14,n=6)
    for y in (-4,4):
        k.box(m['RustySteel'],(16,.65,1.1),(0,y,12.1))
        if lod<2:
            rail(k,m['Galvanized'],(-8,y,12.7),(8,y,12.7),lod)
    k.box(m['SteelDark'],(3,8.5,.4),(1,0,12.85))
    k.box(m['OliveDark'],(2,2,1.2),(1,0,13.6))
    k.box(m['WindowGlass'],(1.8,2,1.8),(-5,-3.5,10.9))
    k.box(m['RoofMetal'],(2.1,2.3,.2),(-5,-3.5,11.9))
    k.bar(m['SteelDark'],(1,0,12.7),(1,0,5.5),.05,n=4)
    k.box(m['SteelDark'],(.6,.5,.8),(1,0,5.3))
    if lod<2:
        k.path(m['SteelDark'],[(1,0,5),(1,0,4.6),(1.4,0,4.4),(1.65,0,4.7)],.09,n=6)
    if lod==0:
        for z in [1+i*.3 for i in range(36)]:
            k.bar(m['Galvanized'],(-7.4,-4.2,z),(-6.6,-4.2,z),.025,n=4)
        for x in (-7.4,-6.6):
            k.bar(m['Galvanized'],(x,-4.2,.8),(x,-4.2,12),.04,n=6)
        for y in (-4,4):
            for x in range(-7,8,2):
                k.bar(m['SteelDark'],(x,y,11.6),(x+1,y,12.6),.045,n=4)
        for x in (-5.85,-4.15):
            k.box(m['Galvanized'],(.08,2.1,1.8),(x,-3.5,10.9))


def wagon_frame(k, m, lod, length=14):
    """Four axles use the existing rail centres and .592 m rail top; buffers retain the stock coupling height."""
    k.box(m['SteelDark'],(2.8,length,.3),(0,0,1.55))
    if lod==2:
        for y in (-4.5,4.5):
            # Single bogie silhouette, avoiding round wheels that cost pixels at RTS scale.
            k.box(m['SteelDark'],(1.8,2.7,.85),(0,y,RAIL_TOP+.425))
        return
    for yc in (-4.5,4.5):
        for y in (yc-.9,yc+.9):
            k.bar(m['SteelDark'],(-.9,y,AXLE_Z),(.9,y,AXLE_Z),.08,n=8)
            for s in (-1,1):
                k.cyl(m['SteelDark'],.46,.14,(s*RAIL_X,y,AXLE_Z),TO_X,n=16 if lod==0 else 8)
                if lod==0:
                    k.cyl(m['RustySteel'],.49,.025,(s*(RAIL_X-.08),y,AXLE_Z),TO_X,n=16)
                    k.box(m['SteelDark'],(.18,.28,.24),(s*.97,y,AXLE_Z),ch=.025)
        for s in (-1,1):
            k.box(m['SteelDark'],(.18,2.5,.25),(s*.98,yc,1.2))
    for e in (-1,1):
        for x in (-.875,.875):
            k.cyl(m['SteelDark'],.10,.4,(x,e*(length/2+.2),1.652),TO_BACK,n=8)
            k.cyl(m['SteelDark'],.2,.06,(x,e*(length/2+.42),1.652),TO_BACK,n=8)
        k.box(m['SteelDark'],(.18,.6,.16),(0,e*(length/2+.2),1.652))
    if lod==0:
        for y in range(-6,7,2):
            k.box(m['RustySteel'],(2.75,.12,.15),(0,y,1.72))
        k.cyl(m['RustySteel'],.16,1,(-.5,0,1.25),TO_BACK,n=12)


def hopper(k, m, lod):
    """A covered grain hopper: sloped discharge bays under tall side walls and a narrow roof catwalk."""
    wagon_frame(k,m,lod)
    k.box(m['Galvanized'],(3,12.5,1.9),(0,0,3.5))
    roof(k,m['RoofMetal'],0,0,3,12.5,4.45,.5,over=.03)
    for y in (-4,0,4):
        k.loft(m['Galvanized'],[[(x,yy,z) for x,yy in ((-w,y-d),(w,y-d),(w,y+d),(-w,y+d))]
                               for w,d,z in ((.45,1,1.75),(1.5,2,2.55))])
        k.box(m['SteelDark'],(.9,1.6,.15),(0,y,1.72))
    if lod<2:
        for s in (-1,1):
            for y in range(-6,7,2):
                k.box(m['SteelDark'],(.10,.10,2.25),(s*1.52,y,3.35))
        k.box(m['SteelDark'],(.55,12,.08),(0,0,5))
        for y in (-4,0,4):
            k.cyl(m['Galvanized'],.42,.12,(0,y,5.06),n=12)
        if lod==0:
            for z in [1.9+i*.3 for i in range(11)]:
                k.bar(m['SteelDark'],(-.4,-6.4,z),(.4,-6.4,z),.025,n=4)
            for x in (-.4,.4):
                k.bar(m['SteelDark'],(x,-6.4,1.7),(x,-6.4,5.15),.03,n=6)
            rail(k,m['SteelDark'],(-.4,-6,5),(-.4,6,5),lod,height=.5)


def container_wagon(k, m, lod):
    """A flat wagon carrying a 40 ft container, with corrugated sides and end locking bars."""
    wagon_frame(k,m,lod)
    k.box(m['Wood'],(2.65,13.6,.12),(0,0,1.76))
    k.box(m['OliveDark'],(2.438,12.192,2.591),(0,0,3.1155))
    if lod<2:
        for s in (-1,1):
            for y in (-6.03,6.03):
                k.box(m['SteelDark'],(.15,.15,2.6),(s*1.2,y,3.12))
            for z in (1.9,4.33):
                k.box(m['SteelDark'],(.1,12.192,.1),(s*1.23,0,z))
        for x in (-.8,-.35,.35,.8):
            k.bar(m['Galvanized'],(x,-6.15,2),(x,-6.15,4.25),.025,n=6)
    if lod==0:
        for i in range(38):
            y=-5.7+i*.3
            for s in (-1,1):
                k.box(m['Olive'],(.045,.065,2.25),(s*1.24,y,3.12))
            k.box(m['Olive'],(2.25,.065,.04),(0,y,4.43))
        for x in (-1.1,1.1):
            for y in (-6,6):
                k.box(m['RustySteel'],(.25,.25,.16),(x,y,1.83))


BUILDS = [('Scenery_SignalBox',signal_box),('Scenery_FreightShed',freight_shed),
          ('Scenery_GantryCrane',gantry_crane),('Scenery_RailHopper',hopper),('Scenery_RailContainerWagon',container_wagon)]
