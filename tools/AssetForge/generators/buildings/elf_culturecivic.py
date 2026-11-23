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
