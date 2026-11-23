"""The Deep Forge: paired magma vents cut into a granite shoulder above a vaulted foundry."""
from math import cos, sin, pi
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish

PACKAGE = "dwarf_wonders"
WHITE = (1., 1., 1.)


def stone_shoulder(m, x, z, width, depth, height, hand, lod):
    # The broken planes are strata in a single closed rock profile. The two outcrops are asymmetric.
    plan = [(-.5,-.20),(-.31,-.5),(.27,-.47),(.5,-.28),(.46,.22),(.23,.5),(-.35,.45),(-.5,.24)]
    if lod == 2:
        plan = [plan[i] for i in (0,1,2,3,5,6)]
    beds = [(0,1),(.25,.98),(.64,.84),(.94,.55)] if lod < 2 else [(0,1),(.91,.61)]
    last = None
    for layer,(y,scale) in enumerate(beds):
        ring = []
        for i,(px,pz) in enumerate(plan):
            high = y + (.047*cos(i*2.3+hand) if layer else 0)
            ring.append((x+width*px*scale + .055*hand*y, height*high, z+depth*pz*scale+.07*y))
        if last:
            for j in range(len(ring)):
                k = (j+1)%len(ring)
                role = "fieldstone" if (j+hand+layer)%4 else "slate"
                m.tri(last[k], last[j],ring[j],WHITE,role)
                m.tri(last[k],ring[j],ring[k],WHITE,role)
        last = ring
    top = (x+.08*hand,height*1.01,z+.10)
    for j in range(len(last)):
        m.tri(last[(j+1)%len(last)],last[j],top,WHITE,"fieldstone")


def crucible(m, x, z, radius, height, lod):
    # The top is a recessed vent, including its inside lip, throat and glowing lower surface.
    n = (12,8,6)[lod]
    p = [(0,1.20,"wall"),(.16,1.10,"wall"),(.23,1.12,"accent"),
         (.28,.95,"trim"),(.77,.78,"wall"),(.81,.87,"trim"),
         (.92,.92,"accent"),(1.,.88,"wall"),(1.,.66,"metal"),(.84,.60,"door")]
    if lod == 1:
        p = [p[i] for i in (0,2,4,5,7,8,9)]
    if lod == 2:
        p = [p[i] for i in (0,4,7,8,9)]
    last = None
    for y,r,role in p:
        ring = [(x+radius*r*cos(2*pi*i/n),height*y,z+radius*r*sin(2*pi*i/n)) for i in range(n)]
        if last:
            for j in range(n):
                k=(j+1)%n
                m.quad(last[k],last[j],ring[j],ring[k],WHITE,role)
        last = ring
    m.poly(list(reversed(last)),WHITE,"forge")
    if lod == 0:
        # Broad raised locking keys stand proud of the iron hoops.
        for i in range(0,n,3):
            a=2*pi*i/n
            with m.transform(translate=(x+radius*.85*cos(a),height*.875,z+radius*.85*sin(a)),
                             rotate_y=90-a*180/pi):
                m.box((0,0,0),(.088,.13,.052),WHITE,material="gold",taper=.12)


def doorway(m, lod):
    # Each voussoir has four masonry faces, a front cut face and a rear face. Tiny joints separate stones.
    n = (9,7,5)[lod]
    cy, z0, z1 = .38,-.888,-.57
    for i in range(n):
        gap=.012 if lod == 0 else 0
        a,b=i*pi/n+gap,(i+1)*pi/n-gap
        inner,outer=.34,.525+(.06 if i==n//2 else 0)
        x0,x1=inner*cos(a),inner*cos(b)
        y0,y1=cy+inner*sin(a),cy+inner*sin(b)
        X0,X1=outer*cos(a),outer*cos(b)
        Y0,Y1=cy+outer*sin(a),cy+outer*sin(b)
        front=[(x0,y0,z0),(x1,y1,z0),(X1,Y1,z0),(X0,Y0,z0)]
        back=[(x,y,z1) for x,y,_ in front]
        role="accent" if i==n//2 else "wall"
        m.quad(*front,WHITE,role)
        for j in range(4):
            k=(j+1)%4
            m.quad(front[k],front[j],back[j],back[k],WHITE,"trim" if j==0 else role)
    for side in (-1,1):
        m.box((side*.433,.16,-.724),(.18,.32,.32),WHITE,material="wall")
        m.box((side*.435,.35,-.728),(.215,.06,.34),WHITE,material="accent")
    # Recessed furnace door with a raised lintel; the grille gives scale to the opening.
    m.box((0,.316,-.579),(.67,.632,.10),WHITE,material="door")
    m.box((0,.258,-.634),(.49,.38,.025),WHITE,material="forge")
    if lod < 2:
        for x in (-.17,-.055,.06,.175):
            m.box((x,.27,-.657),(.030,.419,.026),WHITE,material="metal")


def anvil(m,lod):
    # A smith's anvil forms the ridge crest. Its bick and square heel are visible above the forecourt.
    m.box((0,1.137,-.28),(.43,.085,.24),WHITE,material="trim",taper=.26)
    m.box((0,1.22,-.28),(.215,.12,.155),WHITE,material="metal")
    m.box((.035,1.32,-.28),(.42,.095,.212),WHITE,material="metal")
    with m.transform(translate=(-.17,1.324,-.28),rotate_z=90):
        m.cone((0,0,0),.094,.252,WHITE,segments=7 if lod==0 else 5,material="accent")


def build(spec,lod):
    m=MeshBuilder(seed=spec.id)
    for side,z,w,d,h in ((-1,.11,1.11,2.00,1.14),(1,.28,1.07,1.81,.98)):
        stone_shoulder(m,side*.82,z,w,d,h,side,lod)
    m.box((0,.41,.12),(1.48,.82,1.34),WHITE,material="wall",taper=.05)
    m.box((0,.870,.12),(1.57,.10,1.42),WHITE,material="trim",bottom=lod < 2)
    m.box((0,.992,.11),(1.32,.145,1.21),WHITE,material="roof",taper=.065)
    m.box((0,1.08,-.20),(.69,.058,.53),WHITE,material="wall")
    # The operators enter beside the tapping floor, protected from the molten run.
    m.box((-.89,.235,-.916),(.41,.47,.21),WHITE,material="wall",taper=.09)
    m.box((-.89,.18,-1.029),(.215,.36,.018),WHITE,material="door")
    m.box((-.89,.403,-.929),(.45,.077,.245),WHITE,material="trim")
    if lod < 2:
        for x in (-1.025,-.755):
            m.box((x,.193,-1.026),(.046,.385,.048),WHITE,material="accent")
        m.box((-.89,.026,-1.099),(.35,.052,.144),WHITE,material="fieldstone")
    for side in (-1,1):
        x=side*.62
        m.wedge((x,0,-.756),.31,.78,.805,WHITE,material="wall")
        if lod==0:
            for j in range(2):
                z = -.954+j*.24
                surface_y = (z+.756+.39)*.805/.78
                with m.transform(translate=(x,surface_y+.015,z),rotate_x=-46):
                    m.box((0,0,0),(.30,.035,.07),WHITE,material="accent")
    doorway(m,lod)
    crucible(m,-.47,.53,.285,1.90,lod)
    crucible(m,.49,.65,.24,1.58,lod)
    anvil(m,lod)
    # Taphole and ingot runnel are continuous with the mouth of the furnace.
    m.box((0,.028,-1.02),(.32,.055,.75),WHITE,material="slate")
    m.box((0,.062,-1.02),(.166,.015,.70),WHITE,material="forge")
    for side in (-1,1):
        m.box((side*.138,.070,-1.03),(.05,.058,.711),WHITE,material="wall")
    if lod < 2:
        m.box((0,.033,-1.407),(.39,.066,.15),WHITE,material="metal")
        for x in (-.113,0,.113):
            m.box((x,.071,-1.41),(.081,.012,.105),WHITE,material="forge")
    if lod < 2:
        # A worn four-spoke valve handwheel sits against the left gate jamb.
        n=(14,8)[lod]
        cx,cy,cz=-.56,.46,-.955
        m.box((cx,cy,cz+.135),(.125,.128,.35),WHITE,material="metal")
        with m.transform(translate=(cx,cy,cz),rotate_x=90):
            m.cylinder((0,-.03,0),.039,.06,WHITE,segments=n,material="metal",cap_bottom=True)
            for i in range(4):
                with m.transform(rotate_y=i*90):
                    m.box((.083,0,0),(.106,.027,.031),WHITE,material="accent")
            rings = [[(radius*cos(2*pi*i/n),y,radius*sin(2*pi*i/n)) for i in range(n)]
                     for radius,y in ((.16,-.022),(.16,.022),(.127,.022),(.127,-.022))]
            for i in range(n):
                j=(i+1)%n
                for k,base in enumerate(rings):
                    top=rings[(k+1)%4]
                    m.quad(base[j],base[i],top[i],top[j],WHITE,"accent")
        # Three piled ingots are one deliberate, human-scaled detail beside a very large door.
        for x,y,z in ((.98,.04,-1.263),(.98,.04,-1.137),(.98,.12,-1.20)):
            m.box((x,y,z),(.29,.08,.112),WHITE,material="metal",taper=.18)
    if lod == 0:
        # Paired lamps sit on the cut ramparts and cast amber colour into the stone approach.
        for x in (-.94,.92):
            m.box((x,.49,-.49),(.20,.98,.23),WHITE,material="wall",taper=.16)
            m.cylinder((x,.98,-.49),.118,.07,WHITE,segments=8,top_radius=.14,material="trim")
            m.dome((x,1.05,-.49),.098,WHITE,rings=2,segments=8,squash=.68,material="emissive")
    return m


if __name__ == "__main__":
    for model in package_models(PACKAGE):
        a,b,c=(build(model,i) for i in range(3))
        entry=publish(a,model.metadata(),PACKAGE,lods=(b,c),preview_size=288)
        print(model.id,entry["lod_triangles"])
