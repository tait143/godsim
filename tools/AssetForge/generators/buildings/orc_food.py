"""Orc food camps: smoke racks, coracles, hardy gardens and irrigated seed drums."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_towncenters import Lodge, generate

PACKAGE="orc_food"


def basket(m,x,y,z,r,fruit=True):
    m.shell(x,z,[(y,r*.72,"hide"),(y+r*.88,r,"timber"),(y+r,r,"soil")],(9,6,4)[m.lod])
    if fruit:
        for j in range((5,3,1)[m.lod]):
            a=2.4*j
            distance=.53*r if j else 0
            m.dome((x+distance*math.cos(a),y+r,z+distance*math.sin(a)),r*.43,
                   m.col("leaf" if j%2 else "roof_tile"),rings=1,segments=5,material="leaf" if j%2 else "roof_tile")


def hide_rack(m,x,z,s):
    with m.transform(translate=(x,0,z),scale=s):
        for side in (-1,1):
            m.post("timber",(side*.32,.012,.08),(side*.28,.64,0),.037)
        m.post("trim",(-.365,.57,0),(.365,.594,0),.034)
        # Split lower edge and raised fold keep the drying hide from reading as a plank.
        p=[(-.25,.58,-.022),(.02,.57,-.063),(.245,.58,-.024),(.215,.22,-.016),
           (.123,.16,-.032),(.018,.22,-.049),(-.14,.14,-.027),(-.206,.25,-.018)]
        for ids in ((0,1,5,6,7),(1,2,3,4,5)):
            f=[p[i] for i in ids]
            m.panel("hide",f)
            m.panel("hide",[(u,v,w+.014) for u,v,w in reversed(f)])
        if m.lod<2:
            for side in (-1,1):
                m.tusk(side*.282,.56,0,side,.35)


def forage(m,hunter):
    m.hut(.03,.20,1.49 if hunter else 1.40,roof_material="metal" if m.era==0 else None)
    if hunter:
        hide_rack(m,-.43,-.66,1.14)
        m.drum(.49,0,-.42,.166,.27)
        if m.lod<2:
            m.post("timber",(.44,.274,-.52),(.54,.289,-.36),.018)
    else:
        basket(m,-.45,0,-.50,.236)
        basket(m,.45,0,-.52,.171)
        if m.lod<2:
            basket(m,-.66,0,-.13,.125)
            m.flag(-.79,0,-.18,1.04,.29)


def coracle(m,era):
    # Stretched skin hull on two slipway runners; curled stem and benches stay inside the gunwale.
    with m.transform(translate=(.54,.058,-.08),rotate_y=-12,scale=(.36,.8,.90)):
        n=(12,8,6)[m.lod]
        outline=[(math.sin(2*math.pi*j/n),math.cos(2*math.pi*j/n)) for j in range(n)]
        def ring(rx,rz,y):
            return [(x*rx,y+.095*z*z,z*rz) for x,z in outline]
        keel,rim,inner,floor=ring(.37,.60,0),ring(.83,.83,.22),ring(.70,.76,.22),ring(.36,.60,.068)
        for j in range(n):
            k=(j+1)%n
            for mat,face in (("metal" if era>=2 else "hide",(keel[j],keel[k],rim[k],rim[j])),
                             ("ivory",(rim[j],rim[k],inner[k],inner[j])),
                             ("timber",(inner[j],inner[k],floor[k],floor[j]))):
                m.panel(mat,face)
        m.panel("timber",floor)
        if m.lod<2:
            for z in (-.26,.25):
                m.block("timber",0,.199,z,1.28,.041,.12)
        m.tusk(0,.25,-.74,1,.235)


def fisher(m):
    for x in (-.72,.08):
        m.block("timber",x,0,-.025,.111,.21,1.35)
    m.block("roof" if m.era==3 else "timber",-.32,.19,-.028,1.19,.084,1.66)
    roof = {0: "metal", 3: "roof"}.get(m.era)
    m.hut(-.34,.23,1.16 if m.era==3 else 1.08,base=.274,roof_material=roof)
    for z in (-.43,.31):
        m.block("timber",.55,0,z,.64,.092,.101)
    coracle(m,m.era)
    if m.era==3:
        # A cantilever davit hauls the catch from the coracle alongside the quay.
        m.shell(.13,-.31,[(.274,.116,"roof"),(.41,.088,"metal"),(.47,.068,"accent")])
        m.post("roof",(.13,.40,-.31),(.20,1.01,-.31),.072,.055)
        m.post("metal",(.18,.93,-.31),(.55,1.17,-.34),.070,.047)
        m.post("ivory",(.55,1.14,-.34),(.53,.56,-.34),.016,sides=4)
        m.tusk(.51,.49,-.34,1,.18)
        if m.lod<2:
            m.post("banner",(.18,.64,-.31),(.44,1.09,-.337),.039,.025)
    if m.lod<2:
        m.post("timber",(-.75,.273,-.72),(-.75,.94,-.72),.032)
        m.post("trim",(-.75,.91,-.72),(-.19,.80,-.68),.025)
        pts=[(-.72,.897,-.73),(-.21,.791,-.697),(-.405,.453,-.742),(-.63,.504,-.76)]
        m.panel("cloth",pts)
        m.panel("cloth",[(x,y,z+.013) for x,y,z in reversed(pts)])
        if m.lod==0:
            basket(m,-.425,.274,-.49,.126,False)
            for i in range(3):
                x=-.66+i*.14
                m.shell(x,-.73+i*.009,[(.84-i*.03,.03,"ivory"),(.89-i*.03,.036,"ivory")],6)


def furrows(m):
    m.block("soil",0,0,0,1.87,.08,1.84,.028)
    for row in range(4):
        z=-.715+.259*row
        color="thatch" if row in (0,2) else "leaf"
        m.prism_x((0,.08,z),1.74,.21,.09,m.col("soil"),material="soil")
        pieces=(6,4,1)[m.lod]
        for k in range(pieces):
            x=(k+.5)*1.69/pieces-.845
            m.hip_roof((x,.139,z),1.66/pieces,.166,.112+.024*math.cos(row+k),
                       m.col(color),overhang=0,material=color)
        if m.lod==0:
            for x in (-.57,-.11,.40):
                m.post("grass",(x,.15,z),(x+.052,.31,z-.031),.018,0,sides=4)
    m.hut(-.46,.50,.79,base=.08,roof_material="metal" if m.era==0 else None)
    if m.era>=2:
        for i,x in enumerate((.30,.65)):
            h=.52+.12*i
            m.shell(x,.55,[(.08,.135,"metal"),(.16,.162,"glass" if m.era==3 else "metal"),
                           (h,.16,"banner"),(h+.047,.171,"roof"),(h+.17,.046,"trim")])
        if m.lod<2:
            m.post("metal",(.30,.58,.55),(.65,.65,.55),.038)
            # A gravity irrigation header with visible downpipe into the first row.
            m.post("metal",(.64,.40,.41),(.83,.40,.14),.034)
            m.post("metal",(.83,.40,.14),(.83,.12,.14),.034)
    else:
        m.shell(.49,.55,[(.08,.20,"timber"),(.46,.18,"trim"),(.49,.244,"thatch"),(.75,.040,"gold")])
        if m.lod<2:
            m.flag(.72,.08,.28,.51,.215)


def harvester(m):
    # A grain combine on the threshing yard. The open cutter reel spans the axle track.
    m.block("soil",0,0,0,1.88,.055,1.91,.023)
    m.block("trim",0,.30,.15,.72,.26,1.10,.05)
    m.block("cloth",0,.49,.15,.81,.25,1.19,.08)
    for side in (-1,1):
        for z,r in ((-.20,.276),(.51,.21)):
            height=.055+r
            m.post("door",(side*.30,height,z),(side*.56,height,z),r,sides=(12,8,6)[m.lod])
            if m.lod<2:
                m.post("metal",(side*.55,height,z),(side*.576,height,z),r*.57,
                       sides=(10,6)[m.lod])
            if m.lod==0:
                m.post("banner",(side*.57,height,z),(side*.59,height,z),r*.24,sides=8)

    # Glazed lookout and flaring bulk hopper behind it, filled with threshed grain.
    m.block("glass",0,.72,-.16,.63,.36,.49,.12)
    m.block("cloth",0,1.06,-.155,.66,.075,.535,.08)
    m.shell(0,.435,[(.65,.21,"gold"),(.80,.31,"hide"),(1.07,.43,"trim"),
                    (1.13,.44,"thatch"),(1.22,.23,"thatch"),(1.25,.071,"thatch")],
            (10,8,5)[m.lod],flatten=.88)
    m.post("metal",(.29,.60,.55),(.61,1.19,.64),.083,.069)
    m.post("gold",(.58,1.13,.63),(.84,1.20,.61),.077,.066)
    for side in (-1,1):
        m.post("cloth",(side*.29,.48,-.29),(side*.58,.25,-.61),.075,.055,sides=5)
        m.tusk(side*.34,.73,-.31,side,.40)

    m.block("metal",0,.118,-.69,1.67,.072,.34,.055)
    m.post("thatch",(-.81,.296,-.65),(.81,.296,-.65),.108,sides=(10,7,5)[m.lod])
    for x in ((-.76,0,.76) if m.lod<2 else (-.76,.76)):
        m.post("cloth",(x-.029,.30,-.65),(x+.029,.30,-.65),.191,sides=(10,7,5)[m.lod])
    if m.lod<2:
        for y,z in ((.442,-.703),(.313,-.803)):
            m.post("gold",(-.78,y,z),(.78,y,z),.029,sides=5)
        for x in (-.68,-.34,0,.34,.68):
            m.post("ivory",(x,.152,-.77),(x,.092,-.92),.042,0,sides=4)
        m.post("trim",(0,.746,-.41),(0,1.058,-.38),.027,sides=4)
    for x in (-.73,.73):
        pieces=(4,3,1)[m.lod]
        for i in range(pieces):
            z=-.30+(i+.5)*1.16/pieces
            m.hip_roof((x,.056,z),.19,1.12/pieces,.16,m.col("thatch"),overhang=0,material="thatch")
    if m.lod==0:
        for z in (-.10,.22,.55):
            for x in (-.74,.72):
                m.post("grass",(x,.11,z),(x+.045,.26,z+.026),.03,0,sides=4)


def glasshouse(m):
    # Folded glazing is continuous from the sill to the ridge. Solid braces cross
    # the panes and hold a row of climate vents clear of the growing benches.
    m.block("fieldstone",0,0,.035,1.74,.13,1.80,.09)
    m.block("roof",-.10,.112,.19,1.34,.255,1.32,.045)
    profile=[(-.75,.35),(-.73,.68),(-.49,1.04),(-.10,1.225),
             (.29,1.04),(.53,.68),(.55,.35)]
    front,back=-.447,.83
    for i,((x,y),(x1,y1)) in enumerate(zip(profile,profile[1:])):
        mat="glass" if i in (1,2,3,4) else "roof"
        m.panel(mat,[(x,y,front),(x,y,back),(x1,y1,back),(x1,y1,front)])
    m.panel("roof",[(x,y,front) for x,y in profile])
    m.panel("glass",[(x,y,back) for x,y in reversed(profile)])
    for z in ((-.43,-.02,.39,.80) if m.lod==0 else ((-.43,.17,.80) if m.lod==1 else (-.43,.80))):
        for (x,y),(x1,y1) in zip(profile,profile[1:]):
            m.post("ivory",(x,y+.012,z),(x1,y1+.012,z),.033,sides=(6,4,3)[m.lod])
    for side in (-1,1):
        m.tusk(-.10+side*.48,.44,-.493,side,.71)
    m.entry(-.10,front-.042,.13,.39,.46,3)
    # Repeating leaf benches outside the glazing keep the enclosed crop visible.
    for x in (-.52,-.13,.26,.63):
        m.block("roof",x,.124,-.674,.33,.13,.26,.06)
        for dx in ((-.078,.078) if m.lod<2 else (0,)):
            m.dome((x+dx,.25,-.674),.104,m.col("leaf"),rings=(2,1,1)[m.lod],
                   segments=(7,5,4)[m.lod],squash=.95,material="leaf")
    for z in ((-.22,.19,.61) if m.lod<2 else (.19,)):
        m.shell(-.10,z,[(1.20,.089,"roof"),(1.28,.090,"ascension"),
                        (1.34,.064,"ascension")],(8,6,4)[m.lod])
    m.shell(.675,.53,[(.128,.147,"metal"),(.22,.159,"roof"),(.76,.135,"accent"),
                      (.79,.149,"glass"),(.90,.060,"metal")],(10,7,5)[m.lod])


def boar(m,x,z,rot):
    with m.transform(translate=(x,.04,z),rotate_y=rot):
        for side in (-1,1):
            for end in (-1,1):
                m.block("trim",side*.071,0,end*.103,.042,.117,.046)
        with m.transform(translate=(0,.17,0),scale=(1.,.80,1.42)):
            m.sphere((0,0,0),.14,m.col("hide"),rings=(3,2,2)[m.lod],segments=(8,6,5)[m.lod],material="hide")
        m.block("roof",0,.089,-.176,.158,.141,.150,.15)
        if m.lod<2:
            for s in (-1,1):
                m.tusk(s*.064,.117,-.196,s,.126)
                m.post("trim",(s*.060,.217,-.146),(s*.092,.272,-.125),.031,0,sides=4)


def pasture(m):
    if m.era==0:
        m.block("roof",0,0,0,1.87,.042,1.83,.015)
        grazing=[(-.75,.05,-.60),(-.71,.05,.50),(.42,.05,.67),(.70,.05,.41),
                 (.70,.05,-.57),(.34,.05,-.69),(-.37,.05,-.70)]
        m.panel("grass",grazing)
        m.hut(-.32,.38,1.15,base=.045,roof_material="metal")
    else:
        m.block("grass",0,0,0,1.87,.045,1.83,.015)
        m.hut(-.35,.44,.91,base=.045)
    for a,b,count in (((-.84,-.81),(-.24,-.81),2),((.19,-.81),(.84,-.81),2),
                      ((.84,-.81),(.84,.80),4),((.84,.80),(-.84,.80),4),((-.84,.80),(-.84,-.81),4)):
        for i in range(count if m.lod<2 else 1):
            t=i/count
            x=a[0]+(b[0]-a[0])*t
            z=a[1]+(b[1]-a[1])*t
            m.block("timber" if m.era<2 else "metal",x,.04,z,.059,.26,.060,.08)
        for h in ((.14,.239) if m.lod==0 else (.20,)):
            m.post("timber",(a[0],h,a[1]),(b[0],h,b[1]),.024,sides=4)
    m.block("timber",-.24,.04,-.81,.068,.28,.069)
    for p in ((.39,-.32,-28),(-.30,-.34,57),(.48,.26,100))[:(3,2,1)[m.lod]]:
        boar(m,*p)
    if m.lod<2:
        m.shell(.6,.55,[(.045,.136,"timber"),(.19,.15,"water")],7)
        m.tusk(-.24,.30,-.81,-1,.181)


def mill(m):
    h=1.01
    m.shell(0,.15,[(0,.48,"fieldstone"),(.13,.47,"wall"),(h,.29,"banner"),(h+.051,.385,"roof"),
                   (h+.33,.084,"ivory")],(12,8,4)[m.lod])
    m.entry(0,-.239,0,.25,.35,m.era)
    hub=(0,1.00,-.276)
    m.post("metal",(0,1,.07),(0,1,-.316),.073)
    for j in range(4):
        with m.transform(translate=hub,rotate_z=j*90+26):
            m.block("trim",0,.052,0,.048,.60,.046)
            m.block("cloth" if j%2 else "ivory",-.077,.272,-.021,.184,.399,.047)
            if m.lod==0:
                for y in (.30,.43,.56):
                    m.block("timber",-.069,y,-.050,.19,.019,.026)
    m.hut(.51,.35,.68)
    if m.lod<2:
        basket(m,.50,0,-.27,.156,False)
        m.tusk(-.29,.48,.044,-1,.43)


def grow_tower(m):
    m.shell(0,.04,[(0,.56,"plaster"),(.14,.56,"glass"),(1.52,.245,"gold"),(1.61,.295,"roof"),
                    (1.80,.067,"ascension")])
    for i,y in enumerate((.25,.64,1.03,1.39)):
        r=.75-.093*i
        m.shell(0,.04,[(y,.61*r,"roof"),(y+.103,r,"cloth"),(y+.145,r,"grass")])
        sprouts=(9,6,3)[m.lod]
        for j in range(sprouts):
            a=(j+.32*i)*2*math.pi/sprouts
            m.dome((.82*r*math.sin(a),y+.144,.04+.82*r*math.cos(a)),r*.154,m.col("leaf"),
                   rings=(2,1,1)[m.lod],segments=(7,5,4)[m.lod],squash=.88,material="leaf")
    m.entry(0,-.51,0,.33,.28,3)
    for side in (-1,1):
        m.tusk(.09*side,1.57,.04,side,.48)
    if m.lod<2:
        m.drum(.56,0,-.38,.116,.20)


def build(spec,lod):
    m=Lodge(spec,lod)
    t=spec.building_type
    scale=spec.footprint[0]/2
    with m.transform(scale=scale):
        if t in ("GathererHut","HuntingLodge"):
            forage(m,t=="HuntingLodge")
        elif t=="FishingHut":
            fisher(m)
        elif t=="Farm":
            furrows(m)
        elif t=="IndustrialFarm":
            (glasshouse if m.era==3 else harvester)(m)
        elif t=="Mill":
            mill(m)
        elif t=="Pasture":
            pasture(m)
        else:
            grow_tower(m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
