"""Water, power and reserve stores in the Elven settlements. Builder session 8."""
from pathlib import Path
import sys
from math import pi, cos, sin

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import WHITE, generate


def waterworks(m,kind):
    if kind=="Well":
        m.basin(0,0,-.04,.50,.47,.43)
        for side in (-1,1):
            m.stem([(side*.45,.11,-.02),(side*.47,.67,.00),(side*.34,1.03,.04)],
                   [.075,.047,.048],m.bark)
        m.hood(0,1.00,.04,.55,.43,.38+.04*m.era)
        m.bar((-.47,.71,0),(.49,.71,0),.046,"timber")
        m.bar((.075,.71,0),(.075,.38,-.01),.016,"trim")
        m.lathe(.075,-.01,.084,.083,[(.28,.78,"trim"),(.38,1,"metal")])
        if m.lod<2:
            m.bar((.48,.71,0),(.50,.61,-.068),.021,"accent")
            m.lamp(-.41,.66,-.07,.069)
        return
    # Paired spandrels frame each arch. The open channel and raised kerbs run
    # the actual length of the asset, including both terminal piers.
    e=m.era
    span=.72
    rise=.78+.10*(e-1)
    for center in (-.38,.38):
        n=(10,7,4)[m.lod]
        for side in (-1,1):
            x=side*.31
            points=[(x,.11+rise*sin(pi*i/n),center-span*.50*cos(pi*i/n)) for i in range(n+1)]
            m.stem(points,[.090]*(n+1),"metal" if e==3 else m.masonry,sides=(7,5,4)[m.lod])
            if m.lod<2:
                for i in (2,n-2):
                    _,y,z=points[i]
                    m.bar((x,y,z),(x,.99+.09*(e-1),z),.037,m.bark)
    for z in (-.77,0,.77):
        m.lathe(0,z,.49,.14,[(0,1,"fieldstone"),(.12,.95,m.masonry),
                              (.89+.09*(e-1),.66,m.masonry),(.99+.09*(e-1),.93,"accent")])
    h=.98+.09*(e-1)
    m.box((0,h+.045,0),(.77,.09,1.84),WHITE,material="roof" if e==3 else m.masonry)
    m.box((0,h+.097,0),(.56,.018,1.83),WHITE,material="water")
    for x in (-.345,.345):
        m.box((x,h+.15,0),(.073,.12,1.84),WHITE,material="roof" if e==3 else "accent")
    for z in (-.77,.77):
        m.lamp(-.32,h+.19,z,.093)


def storage(m,kind):
    if kind=="Granary":
        # Seed-shaped bins hold the grain above broad, rodent-resistant footings.
        for i,(x,z,r,h) in enumerate([(-.39,.18,.36,.83),(.34,.29,.36,1.04),(.13,-.40,.30,.64)]):
            rows=[(0,.54,m.masonry),(.13,.59,m.masonry),(.18,.81,"accent"),
                  (.30,.68,"trim"),(h*.67,1,"wall"),(h,.87,"wall")]
            m.lathe(x,z,r,r,rows if m.lod<2 else [rows[j] for j in (0,2,4,5)],closed=False)
            m.hood(x,h,z,1.05*r,1.04*r,(.29+.07*m.era)*(1-.09*i),small=i>0)
            if i==2:
                m.portal(x,0,z-r*.83,.23,.37)
            if m.lod==0:
                m.lamp(x+r*.47,.24,z-r*.81,.043)
    else:
        m.hall(-.17,.16,.60,.54,.59,.41)
        # Slatted delivery platform and barrelled reserve jars by the entrance.
        m.lathe(.33,-.36,.44,.34,[(0,.91,"fieldstone"),(.14,1,m.bark)])
        for i,(x,z,r,h) in enumerate([(.51,-.31,.135,.40),(.25,-.28,.14,.32),(.38,-.54,.12,.23)]):
            rows=[(.14,.73,"timber"),(.19,.95,"trim"),(.14+h*.64,1,"timber"),
                  (.14+h*.79,.95,"metal"),(.14+h,.75,"timber")]
            m.lathe(x,z,r,r,rows if m.lod<2 else rows[::2],n=(9,7,5)[m.lod])
        if m.era>=2:
            m.stem([(.58,.16,-.12),(.65,.70,-.11),(.46,.86,-.18)],
                   [.067,.043,.037],"metal")
            m.lamp(.46,.72,-.18,.065)


def power(m,kind):
    if kind=="FusionReactor":
        m.lathe(0,.10,.69,.63,[(0,.94,"wall"),(.18,1,"ivory"),(.27,.94,"glass"),
                              (.34,.87,"accent"),(.38,.83,"wall")])
        # Magnetic cage encloses a faceted light core. Both rings spring from the dais.
        for angle,role,radius,height in [(-35,"ivory",.48,.60),(54,"accent",.44,.64)]:
            m.hoop(0,.31+height,.10,radius,height,.071,role,angle)
            if m.lod==2:
                break
        m.lathe(0,.10,.20,.19,[(.35,.71,"trim"),(.59,.46,"metal"),(.67,.66,"accent"),
                              (.89,1.26,m.light),(1.11,.27,"glass")])
        m.hall(-.52,-.33,.23,.28,.39,.26,small=True)
    else:
        m.hall(-.31,.11,.46,.51,.55,.40)
        for i,(x,z,h,r) in enumerate([(.37,.31,1.13,.24),(.53,-.17,.86,.20)]):
            rows=[(0,1,m.masonry),(.19,1.04,m.masonry),(.25,.89,"trim"),
                  (h*.68,.62,"wall"),(h*.90,.90,"wall"),(h,.94,"accent"),
                  (h-.012,.72,"door"),(h*.87,.68,"door")]
            m.lathe(x,z,r,r,rows if m.lod<2 else [rows[j] for j in (0,3,5,7)])
            if m.era==3:
                m.hoop(x,h+.09,z,r*.65,.14,.030,"ascension",25+55*i)
        m.stem([(-.18,.23,.46),(-.15,.33,.67),(.35,.33,.67),(.40,.28,.47)],
               [.066,.060,.059,.061],"metal")
        m.lamp(-.55,.012,-.46,.09)


def draw(m,kind):
    if kind in ("Well","Aqueduct"):
        waterworks(m,kind)
    elif kind in ("Storehouse","Granary"):
        storage(m,kind)
    else:
        power(m,kind)


if __name__=="__main__":
    generate("elf_infrastructure",draw)
