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


def well_basin(m,x,z,rx,rz,height):
    if m.lod<2:
        m.basin(x,0,z,rx,rz,height)
    else:
        m.lathe(x,z,rx,rz,[(0,.79,m.masonry),(height,1,"accent"),
                            (height*.86,.84,"water")],n=5)


def draw(m,kind):
    e=m.era
    if kind=="Aqueduct" and e>=2:
        if e==2:
            # A single riveted bow supports a suspended water trough by hangers.
            for x in (-.27,.27):
                bays=(8,6,4)[m.lod]
                pts=[(x,.08+1.21*sin(pi*i/bays),-.79+1.58*i/bays) for i in range(bays+1)]
                m.stem(pts,[.064]*len(pts),"metal",sides=(8,5,3)[m.lod])
                for i in ((bays//4,bays//2,3*bays//4) if m.lod<2 else (bays//2,)):
                    px,py,pz=pts[i]
                    m.bar((px,.57,pz),(px,py,pz),.024,"accent")
            m.box((0,.53,0),(.62,.093,1.74),WHITE,material="brick")
            m.box((0,.585,0),(.48,.017,1.73),WHITE,material="water")
            for x in (-.266,.266):
                m.box((x,.63,0),(.055,.097,1.74),WHITE,material="trim")
            for z in (-.77,.77):
                m.lathe(0,z,.42,.15,[(0,1,"wall"),(.12,.96,"brick")])
        else:
            # A pressure conduit bends over its open valve chamber and returns
            # below the opposite embankment. Its collars expose the pipe joints.
            for z,h in ((-.62,.40),(.06,.96),(.67,.46)):
                m.lathe(0,z,.32,.21,[(0,.96,"wall"),(h,.62,"ivory")])
            pts=[(0,.13,-.86),(0,.56,-.62),(0,1.05,.04),(0,.61,.66),(0,.23,.86)]
            m.stem(pts,[.15,.18,.19,.17,.15],"glass",sides=(14,8,4)[m.lod])
            for z,h in ((-.60,.56),(.05,1.05),(.67,.61)):
                if m.lod<2 or z==.05:
                    m.hoop(0,h,z,.22,.22,.053,"ivory")
            if m.lod<2:
                m.lamp(.32,0,-.36,.11)
    elif kind=="Well" and e:
        if e==1:
            well_basin(m,-.09,-.25,.50,.40,.30)
            m.lathe(.19,.32,.26,.21,[(0,1,"wall"),(.61,.67,"trim"),(.89,.54,"wall")])
            m.bar((-.55,.57,-.29),(.44,1.05,.48),.062,"timber")
            m.bar((-.51,.59,-.26),(-.51,.29,-.26),.017,"trim")
            if m.lod<2:
                m.tulip(-.50,.21,-.26,.090,.12,"metal")
        elif e==2:
            m.hall(-.44,.27,.32,.32,.45,.27,small=True)
            m.lathe(.26,.05,.25,.25,[(0,.96,"brick"),(.57,.76,"metal"),(.76,.59,"gold")])
            m.stem([(.26,.73,.05),(.24,.83,-.14),(.18,.78,-.38),(.18,.61,-.44)],
                   [.084,.082,.073,.063],"metal")
            m.basin(.18,0,-.48,.33,.27,.22)
        else:
            if m.lod==1:
                m.lathe(-.12,-.29,.47,.39,[(0,.77,"wall"),(.205,1,"wall"),
                        (.22,.86,"ivory"),(.18,.83,"water")],n=8)
            else:
                well_basin(m,-.12,-.29,.47,.39,.22)
            m.lathe(.24,.21,.26,.30,[(0,.94,"ivory"),(.54,.63,"glass"),(1.13,.55,"glass"),
                                       (1.24,.86,"accent")],n=(14,8,6)[m.lod])
            m.leaf((.24,1.2,.21),(-.33,.68,-.29),.20,"ivory")
            m.bar((-.32,.69,-.29),(-.21,.19,-.31),.031,"water")
        if m.lod<2:
            m.lamp(-.65,0,-.29,.085)
    elif kind=="Storehouse" and e:
        if e==1:
            # Three raised bins sit behind a covered inspection desk.
            for x,z,r,h in ((-.48,.32,.29,.60),(.18,.42,.30,.75),(.47,-.15,.26,.47)):
                m.lathe(x,z,r,r*.82,[(0,.46,"fieldstone"),(.20,1,"timber"),(h,.91,"wall")])
                m.hood(x,h,z,r*1.10,r*.93,.21,small=True)
            m.portal(-.47,.14,.07,.24,.30)
            m.box((-.06,.08,-.40),(.87,.16,.51),WHITE,material="timber")
            m.cargo(-.27,.16,-.39,(.36,.24,.32))
            m.cargo(.09,.16,-.38,(.27,.27,.26),"hide")
            m.cargo(-.23,.40,-.38,(.29,.19,.26),"cloth")
        elif e==2:
            m.lathe(-.08,.22,.72,.46,[(0,1,"brick"),(.57,.91,"wall")])
            m.hood(-.08,.57,.22,.80,.52,.28,"metal")
            m.box((-.08,.25,-.245),(.68,.50,.10),WHITE,material="door")
            m.box((-.08,.53,-.257),(.76,.07,.12),WHITE,material="metal")
            for x in (-.52,.38):
                m.bar((x,0,-.41),(x,.42,-.41),.055,"metal")
            m.bar((-.51,.42,-.41),(.39,.42,-.41),.046,"metal")
            m.lathe(.35,-.55,.21,.20,[(0,.91,"timber"),(.29,1,"trim")])
            m.box((-.17,.055,-.47),(.63,.11,.50),WHITE,material="timber")
            m.cargo(-.32,.11,-.47,(.26,.24,.24))
            m.cargo(-.08,.11,-.46,(.18,.17,.21),"cloth")
        else:
            # A service lift shares its mast with three cantilever storage pods.
            m.lathe(-.04,.14,.25,.23,[(0,1,"wall"),(1.18,.70,"ivory")])
            for x,z,y,r in ((-.39,.04,.32,.32),(.30,.22,.63,.34),(-.28,.21,.95,.30)):
                m.lathe(x,z,r,r*.86,[(y,.64,"ivory"),(y+.13,1,"glass"),(y+.28,.90,"trim")])
            m.hall(.35,-.35,.29,.24,.31,.21,small=True)
            m.box((-.39,.075,-.43),(.50,.15,.51),WHITE,material="metal")
            for x,z,size in ((-.52,-.48,.21),(-.29,-.41,.17)):
                m.cargo(x,.15,z,(size,size*.85,size),"cloth")
            m.cargo(-.489,.329,-.475,(.18,.14,.18),"ivory")
            for x,z,y in ((-.39,-.23,.48),(.30,-.02,.79)):
                m.box((x,y,z),(.27,.16,.033),WHITE,material="door")
            m.stem([(-.59,.14,-.32),(-.57,.65,-.24),(-.35,.77,-.36)],
                   [.049,.038,.036],"ivory")
            m.bar((-.35,.77,-.36),(-.35,.43,-.36),.019,"gold")
        if m.lod<2:
            m.lamp(-.59,0,-.46,.089)
    elif kind=="PowerPlant" and e==3:
        # Turbine petals sweep round a tall hub above the substation.
        m.lathe(.26,.20,.25,.26,[(0,1,"ivory"),(.69,.52,"metal"),(1.32,.33,"ivory")])
        for j in range(4):
            a=pi*j/2+.21
            m.leaf((.26,1.26,.20),(.26+.57*cos(a),1.33+.24*sin(a),.20+.57*sin(a)),.15,"glass")
        m.hall(-.40,-.18,.38,.37,.39,.22,small=True)
        if m.lod<2:
            m.stem([(-.27,.22,-.07),(-.08,.31,.22),(.24,.32,.23)],[.052]*3,"accent")
    elif kind in ("Well","Aqueduct"):
        waterworks(m,kind)
    elif kind in ("Storehouse","Granary"):
        storage(m,kind)
    else:
        power(m,kind)


if __name__=="__main__":
    generate("elf_infrastructure",draw)
