"""Elven public groves, performance courts and the crown arcology. Builder session 8."""
from pathlib import Path
import sys
from math import cos, sin, pi

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import TAU, generate, terrace


def canopy(m,x,z,w,h,angle=0):
    # Forks support a cupped sail on its raised outer edge.
    with m.transform(translate=(x,0,z),rotate_y=angle):
        for side in (-1,1):
            m.stem([(side*w*.74,.025,.10),(side*w*.81,h*.60,.12),(side*w*.68,h,.03)],
                   [.059,.037,.030],m.bark)
        m.bar((-w*.70,h,.03),(w*.70,h,.03),.036,"accent")
        m.leaf((-w*.98,h,.03),(w*.98,h+.056,.03),w*.47,"roof")
        if m.lod<2:
            m.lamp(-w*.67,h-.13,.032,.063)


def gathering(m,kind):
    e=m.era
    if kind=="Campfire":
        m.basin(0,0,.04,.43,.39,.15,"soil")
        for i in range(3):
            a=TAU*i/3
            m.stem([(.29*cos(a),.15,.04+.29*sin(a)),(.07*cos(a+.3),.41,.04+.07*sin(a+.3))],
                   [.074,.047],"timber")
        for x,z,h,role in [(-.09,.03,.41,"forge"),(.07,.05,.65,"emissive"),(.01,-.08,.38,"gold")]:
            m.lathe(x,z,.103,.087,[(.18,.81,role),(.26,1.13,role),(.18+h,.10,role)],n=(8,6,3)[m.lod])
        for i,a in enumerate((-.42,.84,2.10,3.54)):
            if m.lod==2 and i in (1,3):
                continue
            with m.transform(translate=(.65*cos(a),0,.64*sin(a)),rotate_y=-a*180/pi):
                rows=[(0,.77,"fieldstone"),(.15,1,m.bark),(.19,.92,"wall")]
                m.lathe(0,0,.15,.31,rows if m.lod==0 else rows[::2])
        if e:
            canopy(m,-.22,.58,.48,.74)
        else:
            m.stem([(-.65,.02,.38),(-.62,.49,.34),(-.46,.63,.34)],[.043,.031,.023],"timber")
            m.lamp(-.46,.46,.34,.085)
    elif kind=="Tavern":
        if e>=2:
            if e==2:
                # A horizontal cask carried on two saddles faces the serving yard.
                for z in (-.22,.46):
                    m.lathe(-.25,z,.33,.11,[(0,.96,"brick"),(.37,.82,"wall")])
                with m.transform(translate=(-.25,.54,.10),rotate_x=90):
                    m.lathe(0,0,.39,.38,[(-.53,.82,"timber"),(-.45,.93,"metal"),
                            (-.12,1,"timber"),(.33,.95,"timber"),(.48,.82,"trim")],bottom=True)
                    if m.lod==0:
                        for offset in (-.12,.12):
                            m.box((offset,-.539,0),(.023,.015,.48),(1,1,1),material="trim")
                m.hall(.44,.32,.28,.32,.49,.26,small=True)
                m.bar((-.25,.43,-.42),(-.25,.40,-.56),.046,"gold")
            else:
                # The taproom opens onto an elevated oval dining terrace.
                m.lathe(.06,.21,.57,.46,[(0,.68,"ivory"),(.56,.80,"glass"),(.68,1.29,"ivory")])
                m.hood(.03,.71,.45,.39,.34,.48,"glass",small=True)
                m.portal(.07,0,-.095,.26,.44)
                for x,z in (((-.37,.02),(.36,-.10)) if m.lod<2 else ()):
                    m.lathe(x,z,.11,.10,[(.68,.44,"trim"),(.82,.45,"trim"),(.84,1.14,"timber")])
                # The approach rises to a recessed bar. A tapped keg and handled
                # drinking cup sit on the counter, visible from the street.
                for j in range((5,3,2)[m.lod]):
                    steps=(5,3,2)[m.lod]
                    h=.669*(j+1)/steps
                    z=-.72+.48*(j+.5)/steps
                    m.box((.22,h*.5,z),(.32,h,.48/steps),(1,1,1),material="ivory")
                m.lathe(-.40,-.45,.29,.19,[(0,.88,"timber"),(.28,1.02,"trim")])
                m.lathe(-.51,-.43,.14,.14,[(.28,.77,"timber"),(.35,1,"trim"),
                        (.49,1.02,"timber"),(.55,.84,"gold")],n=(12,7,5)[m.lod])
                if m.lod<2:
                    m.lathe(-.25,-.43,.064,.066,[(.28,.80,"trim"),(.42,1,"gold")],n=(8,5)[m.lod])
                    m.hoop(-.18,.365,-.43,.039,.048,.014,"gold")
            if m.lod<2:
                m.lamp(.63,0,-.51,.095)
            return
        m.hall(-.20,.22,.49,.49,.83,.56)
        m.hall(.38,-.14,.38,.49,.43,.32,small=True)
        for x,z in [(-.38,-.43),(.05,-.48)]:
            m.lathe(x,z,.15,.14,[(0,.80,"timber"),(.08,.95,"trim"),(.24,1.09,"timber"),
                                  (.30,1,"metal"),(.38,.85,"timber")])
        m.lathe(-.64,-.72,.088,.10,[(0,.93,m.masonry),(.12,1,"accent")])
        m.stem([(-.64,.10,-.72),(-.64,.61,-.72),(-.71,.73,-.72),(-.88,.74,-.72)],
               [.043,.036,.028,.028],m.bark)
        m.bar((-.86,.74,-.72),(-.86,.62,-.72),.018,"accent")
        m.lathe(-.86,-.72,.114,.052,[(.38,.45,"accent"),(.44,1,"gold"),(.59,1,"gold"),
                                      (.64,.53,"accent")],n=(9,6,4)[m.lod])
    elif kind=="Theater":
        if e==2:
            # A raised proscenium and tall fly loft carry the indoor scenery machinery.
            loft=[(0,1,"brick"),(.19,.97,"trim"),(.83,.79,"wall"),(1.14,.83,"roof"),(1.21,.74,"metal")]
            profile=loft if m.lod==0 else [loft[i] for i in (0,2,4)]
            m.lathe(.04,.27,.66,.44,profile,n=(14,8,6)[m.lod])
            m.lathe(.04,-.17,.62,.37,[(0,.92,"wall"),(.22,1,"trim"),(.27,1,"timber")],
                    n=(14,8,6)[m.lod])
            for side in (-1,1):
                x=.04+side*.47
                m.stem([(x,.245,-.28),(x+side*.05,.72,-.23),(x-side*.12,1.03,-.18)],
                       [.10,.071,.077],m.bark)
            m.hood(.04,.99,-.145,.47,.26,.21,"roof",small=True)
            # Three open rows turn toward the stage, with a center aisle.
            for x in (-.36,.36):
                for j in range((3,2,1)[m.lod]):
                    row=2*j if m.lod==1 else j
                    z=-.43-.15*row
                    height=.115+.095*row
                    m.box((x,height/2,z),(.53,height,.135),(1,1,1),material="wall")
                    if m.lod<2:
                        m.box((x,height+.020,z),(.55,.04,.144),(1,1,1),material="banner")
            if m.lod<2:
                for x in (-.32,.40):
                    m.leaf((x,.88,-.245),(x+.04,.37,-.24),.13,"banner")
            return
        if e==3:
            # A shell of overlapping acoustic leaves opens forward over a broad public stage.
            m.lathe(0,.11,.52,.35,[(0,.89,"wall"),(.24,1,"ivory"),(.33,.96,"timber")])
            # Opposed audience banks rake away from the central thrust stage.
            for side in (-1,1):
                for j in range((3,2,1)[m.lod]):
                    x=side*(.36+j*.15)
                    h=.16+j*.11
                    m.lathe(x,-.32,.12,.44,[(0,.92,"wall"),(h,1,"banner")],n=(9,6,4)[m.lod])
            m.lathe(0,.27,.65,.33,[(0,.96,"wall"),(.45,1,"glass"),(.53,.91,"roof")])
            petals=(5,5,3)[m.lod]
            for j in range(petals):
                a=-.82+1.64*j/(petals-1)
                cx=.52*sin(a)
                if m.lod==2:
                    m.stem([(cx,.36,.28),(cx*1.25,1.04,.25)],[.063,.034],m.bark,sides=3)
                else:
                    m.stem([(cx,.36,.28),(cx*1.19,.84,.35),(cx*1.25,1.04,.25)],
                           [.063,.042,.034],m.bark)
                m.leaf((cx*1.25,1.00,.28),(1.04*sin(a),1.19+.26*cos(a),-.43*cos(a)),.26,"roof")
            if m.lod<2:
                for x in (-.72,.72):
                    m.lamp(x,.03,-.32,.105)
            return
        terrace(m,0,.06,.83,.78,-.90,4.04,(4,3,2)[m.lod])
        m.lathe(0,-.24,.45,.28,[(0,.94,"fieldstone"),(.12,1,"accent"),(.17,.96,"timber")])
        canopy(m,0,.51,.69,.86+.10*e)
        for x in (-.49,.49):
            m.lamp(x,.17,-.11,.09)
    elif kind in ("Arena","Stadium"):
        stadium=kind=="Stadium"
        terrace(m,0,.06,.87,.82,-1.04,4.18,(5,3,2)[m.lod])
        m.lathe(0,.06,.48,.42,[(0,1,"fieldstone"),(.052,1,"grass" if stadium else "soil")])
        if stadium:
            for x in (-.54,.54):
                canopy(m,x,.21,.46,.95+.12*(e-2),90 if x<0 else -90)
            for x in (-.37,.37):
                m.hoop(x,.18,.04,.068,.135,.025,"ivory",90)
        else:
            for x,z in [(-.53,.61),(.58,.55)]:
                m.lathe(x,z,.17,.15,[(0,1,m.masonry),(.46,.9,"wall"),(.73+.05*e,.74,"trim")])
                m.hood(x,.73+.05*e,z,.22,.21,.32,small=True)
            for x in (-.39,.39):
                m.lamp(x,0,-.65,.095)


def civic(m,kind):
    if kind=="Forum":
        if m.era>=2:
            if m.era==2:
                # The speaker stands on a balcony under a radial fan of roof ribs.
                m.lathe(.02,.40,.53,.35,[(0,.91,"brick"),(.39,1,"wall"),(.50,1.04,"trim")])
                for x in (-.39,.43):
                    m.bar((x,.49,.40),(x,.92,.41),.067,"metal")
                m.hood(.02,.92,.38,.57,.40,.28,"metal")
                for j in range((3,2,2)[m.lod]):
                    h=.13+.072*j
                    m.box((.01,h/2,-.26-.18*j),(1.16-.09*j,h,.13),(1,1,1),material="timber")
                m.lathe(.05,.26,.14,.10,[(.50,.74,"trim"),(.67,.67,"wall"),(.74,1.12,"ivory")],
                        n=(14,8,4)[m.lod])
                for j in range((4,3,2)[m.lod]):
                    n=(4,3,2)[m.lod]
                    h=.50*(j+1)/n
                    m.box((-.55+.28*(j+.5)/n,h*.5,.29),(.28/n,h,.20),(1,1,1),material="brick")
            else:
                # A circular debating chamber surrounds a freestanding lectern.
                terrace(m,.01,.035,.76,.67,-.75,3.87,(3,2,1)[m.lod])
                m.lathe(-.05,-.26,.23,.21,[(0,.78,"ivory"),(.36,.83,"glass"),(.42,1.20,"ivory")])
                for x in (-.55,.53):
                    m.stem([(x,.37,.41),(x*1.09,.96,.37),(x*.60,1.22,.34)],
                           [.073,.052,.033],"ivory")
                m.bar((-.33,1.21,.34),(.32,1.21,.34),.037,"gold")
            if m.lod<2:
                m.lamp(.52,0,-.53,.08)
            return
        m.hall(-.10,.31,.51,.38,.68,.43)
        for side in (-1,1):
            for j in range(3 if m.lod<2 else 2):
                z=.12-.28*j
                x=side*(.67-.045*j)
                m.stem([(x,.03,z),(x-.03*side,.43,z),(x-.12*side,.63,z+.035)],
                       [.082,.046,.057],m.bark)
            m.hood(side*.60,.62,-.16,.20,.51,.19,small=True)
        m.lathe(0,-.48,.25,.18,[(0,.81,m.masonry),(.18,.84,"wall"),(.30,1,"accent")])
    else:
        # Continuous inhabited terraces widen into sky gardens, then contract up the spire.
        x,z=-.07,.12
        for i,(y,r,h,dx) in enumerate([(0,.63,.65,0),(.59,.49,.62,.05),(1.15,.34,.48,.10)]):
            cx=x+dx
            rows=[(y,.91,"ivory"),(y+h*.20,.86,"trim"),(y+h*.80,.79,"glass"),
                  (y+h*.91,1.11,"ivory"),(y+h,1.09,"leaf")]
            m.lathe(cx,z,r,r*.81,rows if m.lod<2 else rows[::2])
        m.hood(.03,1.63,z,.33,.30,.49,"glass")
        m.portal(x,0,-.36,.38,.49)
        for side in (-1,1):
            m.stem([(side*.68,.05,-.02),(side*.62,.49,.09),(side*.27,1.27,.15)],
                   [.10,.069,.047],"ivory")
        m.lamp(.54,0,-.44,.135)


def draw(m,kind):
    if kind in ("Forum","Arcology"):
        civic(m,kind)
    else:
        gathering(m,kind)


if __name__=="__main__":
    generate("elf_culturecivic",draw)
