"""Orc sanctuaries: ancestor masks, offering bowls, drum choirs and bell courts."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_towncenters import Lodge, generate

PACKAGE="orc_religion"


def mask(m,x,y,z,s,clad=False):
    """Carved wooden ancestor face. A folded brow and long central nose catch side light."""
    with m.transform(translate=(x,y,z),scale=s):
        rim=[(0,.38),(-.27,.31),(-.32,.09),(-.22,-.19),(0,-.38),(.22,-.19),(.32,.09),(.27,.31)]
        if m.lod==2:
            rim=[rim[i] for i in (0,2,4,6)]
        for j,(u,v) in enumerate(rim):
            a,b=rim[(j+1)%len(rim)]
            tone=("metal" if clad else "hide") if j in (0,1,2,7) else "roof"
            m.panel(tone,[(u,v,0),(0,-.028,-.14),(a,b,0)])
            m.panel("timber",[(a,b,.089),(0,0,.089),(u,v,.089)])
            m.panel("trim",[(u,v,0),(a,b,0),(a,b,.089),(u,v,.089)])
        for side in (-1,1):
            # The painted eyebrows slope down to a common bridge.
            p=[(side*.264,.19,-.057),(side*.062,.12,-.133),(side*.066,.053,-.143),(side*.236,.097,-.081)]
            m.panel("ivory",p if side<0 else p[::-1])
            if m.lod==0:
                m.block("trim",side*.142,.042,-.11,.088,.043,.044,.08)
        if m.lod<2:
            m.post("banner",(0,.088,-.120),(0,-.105,-.17),.076,.036,sides=4)
        for side in (-1,1):
            m.tusk(side*.222,.238,.041,side,.65)


def offering(m,x,z,r,base=0):
    # Copper bowl cradled on a squat octagonal altar, with warm coals visible through its lip.
    m.shell(x,z,[(base,r*.72,"fieldstone"),(base+r*.36,r*.77,"banner"),(base+r*.47,r*.60,"metal"),
                 (base+r*.81,r,"gold"),(base+r*.92,r*.95,"forge")],(10,7,5)[m.lod])
    if m.lod==0:
        for j in (0,1,2):
            t=j*2*math.pi/3
            m.post("forge",(x+r*.27*math.sin(t),base+r*.90,z+r*.27*math.cos(t)),
                   (x+r*.36*math.sin(t)+.01,base+r*(1.25+.09*j),z+r*.31*math.cos(t)),r*.12,0,sides=4)


def idol(m):
    m.shell(0,.13,[(0,.58,"fieldstone"),(.114,.55,"wall"),(.173,.41,"timber")],(10,7,5)[m.lod],flatten=.80)
    m.shell(0,.12,[(.17,.223,"timber"),(.48,.166,"metal"),(.61,.196,"roof"),(1.12,.211,"cloth"),
                    (1.22,.231,"metal"),(1.39,.182,"roof")],(9,7,5)[m.lod])
    mask(m,0,1.16,-.060,1.39,clad=True)
    m.post("timber",(-.47,.80,.125),(.47,.80,.125),.065)
    for side in (-1,1):
        m.tusk(side*.413,.77,.125,side,.70)
    offering(m,0,-.48,.23)
    if m.lod<2:
        m.drum(.42,0,-.28,.158,.25)
        m.drum(-.45,0,-.234,.126,.18)
    if m.lod==0:
        for side in (-1,1):
            m.post("hide",(side*.37,.76,.12),(side*.37,.61,.12),.021,sides=4)
            m.shell(side*.37,.12,[(.51,.049,"gold"),(.61,.030,"metal")],6)


def shrine(m):
    e=m.era
    m.shell(0,.075,[(0,.71,"fieldstone"),(.097,.708,"wall"),(.153,.62,"plaster")],flatten=.79)
    m.block("plaster" if e>=1 else "timber",0,.15,-.162,.53,.67,.33,.13)
    mask(m,0,.63,-.354,.76)
    for side in (-1,1):
        m.post(("timber","plaster","metal","metal")[e],(side*.46,.15,-.09),(side*.41,1.025,-.059),.073,.062)
        m.post(("timber","plaster","metal","metal")[e],(side*.46,.15,.40),(side*.41,1.025,.38),.064,.054)
    roof=("metal","roof_tile","metal","roof")[e]
    h=(.41,.55,.35,.49)[e]
    m.shell(0,.255,[(1.005,.689,"trim"),(1.059,.715,roof),(1.11+h*.34,.436,roof),
                    (1.05+h,.064,"gold")],flatten=.64,panels=True)
    m.tusk(0,1.027+h,.255,1,.28)
    offering(m,0,-.637,.169)
    if m.lod<2:
        m.flag(-.45,.154,.31,1.01,.21)
        m.drum(.51,0,-.42,.155,.223)
    if e in (2,3):
        m.block("ascension" if e==3 else "metal",0,.323,-.162,.572,.079,.335)
    if m.lod==0:
        for side in (-1,1):
            m.shell(side*.452,-.083,[(.392,.067,"banner"),(.455,.066,"timber")],6)


def bell(m,x,z,foot,h,s):
    """An open belfry with a three-lobed bronze bell and a cutout below its roof."""
    e=m.era
    with m.transform(translate=(x,foot,z),scale=s):
        wall=("wall","fieldstone","brick","glass")[e]
        m.shell(0,0,[(0,.273,"fieldstone"),(.118,.26,wall),(h*.59,.208,"banner"),
                     (h*.63,.227,"wall")],(8,6,5)[m.lod])
        for side in (-1,1):
            m.block("trim" if e<2 else "metal",side*.169,h*.624,0,.054,h*.31,.12)
        m.post("metal",(0,h*.81,0),(0,h*.97,0),.033)
        m.shell(0,0,[(h*.66,.139,"metal"),(h*.71,.140,"gold"),(h*.85,.084,"gold"),
                     (h*.87,.047,"metal")],(8,6,4)[m.lod])
        m.shell(0,0,[(h*.925,.25,"trim"),(h*.97,.258,"roof"),(h+.21,.041,"gold")],(8,6,5)[m.lod])
        if m.lod<2:
            m.tusk(0,h+.201,0,1,.31)


def cathedral_of_ribs(m):
    # Deep, sequential white vaults step up toward the faceted apse. Their open portals
    # frame a ceremonial approach through a luminous shell, with no flanking bell turrets.
    m.block("fieldstone",0,0,.03,1.46,.12,1.66,.08)
    m.shell(0,.43,[(.12,.57,"metal"),(.30,.61,"ivory"),(.76,.51,"glass"),
                    (1.00,.43,"turquoise"),(1.26,.30,"glass"),(1.56,.17,"ascension"),
                    (1.80,.024,"ivory")],(14,8,5)[m.lod],flatten=.69)
    for i,(z,h,w) in enumerate(((-.53,.96,1.17),(-.16,1.17,1.32),(.19,1.37,1.41))):
        with m.transform(translate=(0,.12,z)):
            memorial_arch(m,0,0,h,w,.17,"ivory" if i!=1 else "turquoise")
        if m.lod<2 and i!=1:
            for side in (-1,1):
                m.tusk(side*w*.429,.48+.15*i,z,side,.49)
    m.entry(0,.04,.12,.41,.57,3)
    for s in (-1,1):
        m.shell(s*.45,-.64,[(.12,.14,"metal"),(.19,.15,"gold"),(.28,.10,"ivory")],(8,6,4)[m.lod])
        m.tusk(s*.45,.25,-.64,s,.76)
    mask(m,0,1.06,.118,.54,clad=True)
    if m.lod==0:
        for s in (-1,1):
            offering(m,s*.54,-.19,.115,.12)



def temple(m,cathedral):
    if cathedral and m.era==3:
        return cathedral_of_ribs(m)
    e=m.era
    stone=("wall","plaster","brick","glass")[e]
    roof=("roof","roof_tile","metal","roof")[e]
    m.block("fieldstone",0,0,.10,1.16,.123,1.38,.06)
    m.block("wall",0,.12,.11,1.075,.095,1.32,.036)
    h=.68 if cathedral else .50
    m.block(stone,0,.21,.13,1.016,h,1.225,.03)
    top=.21+h
    # The tall five-sided nave runs front to back and ends in a deep sculpted pediment.
    with m.transform(translate=(0,top,.13),rotate_y=90):
        m.prism_x((0,0,0),1.40,1.18,.53 if cathedral else .45,m.col(roof),material=roof)
    ridge=top+(.53 if cathedral else .45)
    m.block("metal" if e>=2 else "timber",0,ridge-.024,.13,.08,.054,1.43)
    m.entry(0,-.488,.11,.398,h*.80,e)
    m.block("fieldstone",0,0,-.659,.65,.065,.30,.05)
    m.block("wall",0,.065,-.584,.574,.055,.23,.04)
    mask(m,0,top+.17,-.576,.65 if cathedral else .60)
    for side in (-1,1):
        if cathedral:
            bell(m,side*.654,-.118,0,1.27 if e<2 else 1.37,1.0)
        else:
            m.post("plaster" if e==3 else "timber",(side*.548,.024,.42),(side*.514,top+.011,.42),.065)
            m.tusk(side*.48,top-.01,.43,side,.53)
    if not cathedral and e==1:
        # A shallow open porch carries the carved lintel above the pilgrims’ stair.
        for side in (-1,1):
            m.shell(side*.264,-.747,[(.054,.082,"fieldstone"),(.104,.087,"plaster"),
                                      (.505,.061,"plaster"),(.559,.083,"accent")],(8,6,4)[m.lod])
        m.block("plaster",0,.554,-.639,.689,.078,.344,.035)
        m.hip_roof((0,.63,-.64),.729,.387,.123,m.col("roof"),overhang=0,material="roof")
    if e==2:
        m.shell(0,.54,[(ridge-.23,.16,"metal"),(ridge+.10,.118,"emissive"),
                        (ridge+.19,.12,"slate"),(ridge+.37,.024,"metal")],(9,7,4)[m.lod])
    if e==3:
        m.shell(0,.35,[(ridge-.092,.154,"metal"),(ridge+.088,.124,"ascension"),(ridge+.25,.05,"gold")],(8,7,4)[m.lod])
    if m.lod<2:
        for side in (-1,1):
            offering(m,side*.48,-.650,.125)
        for k in (-.19,.35):
            for side in (-1,1):
                m.block("emissive" if e<3 else "ascension",side*.503,.37,k,.042,.18,.115)
    if m.lod==0:
        m.drum(.69,0,.44,.16,.232)
        for side in (-1,1):
            m.post("banner",(side*.482,top+.092,.077),(side*.077,ridge-.033,.077),.027,sides=4)


def cloister(m):
    e=m.era
    wall=("wall","plaster","brick","glass")[e]
    roof=("roof","roof_tile","metal","roof")[e]
    m.block("fieldstone",0,0,0,1.83,.071,1.69,.045)
    m.block("grass",0,.071,-.028,1.43,.036,1.37,.028)
    # Three inhabited ranges enclose a planted court, with an axial front gate and small gong.
    for x in (-.66,.66):
        m.block(wall,x,.071,.014,.34,.365,1.29)
        with m.transform(translate=(x,.435,.014),rotate_y=90):
            m.hip_roof((0,0,0),1.38,.428,.215,m.col(roof),overhang=0,material=roof)
    m.block(wall,0,.071,.585,1.40,.46,.38)
    m.hip_roof((0,.53,.585),1.45,.479,.29,m.col(roof),overhang=0,material=roof)
    if m.lod<2:
        m.entry(0,.383,.10,.245,.33,e)
    else:
        m.block("door",0,.10,.380,.245,.33,.039)
    for side in (-1,1):
        m.block("metal" if e>=2 else "timber",side*.215,.071,-.648,.085,.572,.112)
    m.block("banner",0,.615,-.649,.586,.095,.147)
    mask(m,0,.68,-.733,.48)
    m.post("trim",(0,.48,-.648),(0,.62,-.648),.020)
    m.post("gold",(0,.415,-.685),(0,.415,-.605),.095,sides=(10,7,5)[m.lod])
    # Stepping stones lead from the gate to the court's planted censer.
    for z in ((-.49,-.28,.25) if m.lod<2 else ()):
        m.block("plaster",0,.105,z,.242,.017,.133,.06)
    if m.lod<2:
        offering(m,0,-.019,.186,.107)
    else:
        m.shell(0,-.019,[(.106,.120,"fieldstone"),(.249,.17,"gold")],5)
    if m.lod<2:
        for side in (-1,1):
            m.shell(side*.35,.05,[(.105,.115,"timber"),(.194,.128,"soil")],7)
            m.dome((side*.35,.192,.05),.126,m.col("leaf"),rings=(2,1)[m.lod],segments=(8,5)[m.lod],material="leaf")
    if e==3:
        # A raised octagonal light well brings daylight to the rear meditation range.
        m.shell(0,.582,[(.68,.27,"metal"),(.764,.291,"glass"),(.972,.237,"trim"),
                        (1.015,.286,"roof"),(1.166,.061,"ascension")],(10,7,4)[m.lod],flatten=.81)
    if e==2:
        if m.lod<2:
            m.block("metal",.49,.532,.605,.251,.236,.247,.08)
        else:
            m.block("metal",.49,.532,.605,.251,.306,.247,.15)
            return
        m.shell(.49,.60,[(.75,.139,"glass" if e==3 else "metal"),(.845,.118,"metal")],(8,6,4)[m.lod])
    if m.lod==0:
        for side in (-1,1):
            m.block("banner",side*.655,.256,-.64,.095,.19,.04)


def dressed_tower(m,x,z,h,r):
    rows=[(0,r*1.11,"fieldstone"),(.12*h,r*1.06,"brick"),(.69*h,r*.77,"brick"),
          (.71*h,r*.86,"trim"),(.76*h,r*.82,"brick"),(.85*h,r*.78,"brick"),
          (.88*h,r*.96,"metal"),(.95*h,r*.99,"roof"),(h,r*.60,"trim")]
    if m.lod==2:
        rows=[rows[i] for i in (0,1,5,6,8)]
    m.shell(x,z,rows,(12,7,4)[m.lod])


def memorial_arch(m,x,z,h,w,depth,mat):
    # A continuous inverted U. The piers and arched soffit enclose a real opening.
    n=(12,8,4)[m.lod]
    radius=w/2
    inner=.63*radius
    spring=h-radius
    for side in (-1,1):
        m.block(mat,x+side*.5*(radius+inner),0,z,radius-inner,spring,depth)
    def p(r,i,d):
        angle=math.pi*i/n
        return (x+r*math.cos(angle),spring+r*math.sin(angle),z+d)
    for i in range(n):
        a,b,c,d=[p(r,k,-depth/2) for r,k in ((inner,i),(inner,i+1),(radius,i+1),(radius,i))]
        e,f,g,hp=[(u,v,q+depth) for u,v,q in (a,b,c,d)]
        m.panel(mat,(a,b,c,d))
        m.panel(mat,(hp,g,f,e))
        m.panel("trim",(d,c,g,hp))
        m.panel(mat,(b,a,e,f))


def pilgrim_shrine(m):
    e=m.era
    if e==1:
        # A stone triumphal niche frames the ancestor tablet, with the bowl on the processional axis.
        memorial_arch(m,0,.13,1.29,1.18,.36,"plaster")
        m.block("fieldstone",0,0,.39,.88,.15,.48,.08)
        m.block("wall",0,.15,.39,.46,.58,.23,.14)
        mask(m,0,.59,.236,.76)
        for s in (-1,1):
            m.tusk(s*.49,.78,.12,s,.60)
        offering(m,0,-.34,.22)
        if m.lod<2:
            m.drum(-.51,0,-.31,.14,.17)
    elif e==2:
        # A brick devotional furnace with a wrought canopy over a cinder altar.
        dressed_tower(m,-.34,.25,1.09,.25)
        dressed_tower(m,.39,.38,1.45,.205)
        m.block("brick",.02,0,.14,.85,.63,.53,.08)
        m.prism_x((.02,.63,.14),.99,.71,.23,m.col("metal"),material="metal")
        mask(m,.02,.53,-.155,.72,clad=True)
        offering(m,.02,-.54,.237)
        if m.lod<2:
            for s in (-1,1):
                m.post("metal",(s*.39,.12,-.45),(s*.29,.68,-.04),.048,sides=5)
    else:
        # The ancestor face is inset into a faceted crystal, guarded by two great tusks.
        m.shell(0,.19,[(0,.47,"fieldstone"),(.12,.55,"metal"),(.23,.38,"ivory"),
                        (.46,.22,"turquoise"),(.84,.36,"glass"),(1.10,.27,"ascension"),
                        (1.37,.047,"ivory")],(10,7,4)[m.lod],flatten=.71)
        mask(m,0,.71,-.039,.61,clad=True)
        for s in (-1,1):
            m.shell(s*.45,-.02,[(0,.18,"fieldstone"),(.12,.21,"metal"),(.23,.12,"gold")],(8,5,4)[m.lod])
            m.tusk(s*.45,.20,-.02,s,1.07)
        offering(m,.02,-.58,.19)
        if m.lod==0:
            for s in (-1,1):
                m.post("metal",(s*.40,.14,.015),(s*.22,.41,.16),.044,sides=6)


def high_temple(m):
    e=m.era
    if e==1:
        # A pyramidal terrace raises the sanctuary above a line of processional stairs.
        for k,(w,d,h) in enumerate(((1.55,1.11,.15),(1.24,.90,.18),(.96,.69,.21))):
            y=(0,.15,.33)[k]
            m.block("fieldstone" if k==0 else "plaster",0,y,.20,w,h,d,.08)
        m.block("wall",0,.54,.19,.72,.47,.58)
        m.entry(0,-.12,.54,.29,.35,e)
        m.pyramid((0,1.01,.19),.96,.83,.42,m.col("roof_tile"),material="roof_tile")
        mask(m,0,.95,-.169,.47)
        for j in range((5,4,3)[m.lod]):
            n=(5,4,3)[m.lod]
            z=-.55+j*.49/n
            m.block("wall",0,0,z,.49+.06*(1-j/n),.54*(j+1)/n,.49/n)
        for s in (-1,1):
            m.tusk(s*.60,.154,.20,s,.69)
            if m.lod<2:
                offering(m,s*.48,-.46,.14)
    elif e==2:
        # A bell and metal lantern crown a compact cruciform hall with low transepts.
        for s in (-1,1):
            x=s*.55
            m.block("brick",x,0,.20,.54,.43,.72,.055)
            m.hip_roof((x,.43,.20),.64,.81,.24,m.col("metal"),overhang=0,material="metal")
            if m.lod<2:
                m.tusk(x+s*.12,.45,.18,s,.51)
        m.block("fieldstone",0,0,-.17,.75,.12,1.17,.03)
        m.block("brick",0,.115,-.17,.69,.52,1.11)
        with m.transform(translate=(0,.635,-.17),rotate_y=90):
            m.prism_x((0,0,0),1.20,.80,.26,m.col("metal"),material="metal")
        bell(m,0,.25,0,1.31,1.06)
        m.entry(0,-.75,.04,.33,.44,e)
        mask(m,0,.69,-.773,.48,clad=True)
        if m.lod==0:
            offering(m,-.63,-.44,.169)
    else:
        # Three exterior buttresses carry a faceted luminous spire over a broad congregation drum.
        m.shell(0,.19,[(0,.72,"fieldstone"),(.12,.77,"ivory"),(.23,.68,"metal"),
                        (.55,.60,"glass"),(.64,.70,"ivory"),(.73,.55,"turquoise"),
                        (.82,.39,"metal"),(1.11,.26,"glass"),(1.46,.15,"ascension"),
                        (1.68,.032,"ivory")],(12,7,5)[m.lod],flatten=.81)
        m.entry(0,-.375,.03,.34,.43,e)
        for angle in (-64,64,180):
            with m.transform(translate=(0,0,.19),rotate_y=angle):
                foot=.65 if angle==180 else .74
                m.block("fieldstone",0,0,-foot,.22,.12,.24,.08)
                m.post("ivory",(0,.08,-foot),(0,.83,-.40),.12,.065,sides=(7,5,4)[m.lod])
                m.post("turquoise",(0,.81,-.41),(0,1.41,-.16),.072,.029,sides=(7,5,4)[m.lod])
                if m.lod==0:
                    m.tusk(.04,.32,-.65,1,.42)
        mask(m,0,.66,-.356,.50,clad=True)
        if m.lod<2:
            offering(m,-.40,-.50,.155)


def monastic_order(m):
    if m.era==2:
        # The communal refectory joins a lantern tower to the low walled herb garth.
        m.block("brick",-.27,0,.43,1.12,.57,.64)
        m.prism_x((-.27,.57,.43),1.24,.76,.29,m.col("metal"),material="metal")
        m.entry(-.45,.080,0,.32,.43,m.era)
        bell(m,.45,.33,0,1.18,1.20)
        for s in (-1,1):
            m.block("brick",s*.60,0,-.30,.15,.28,.78)
            m.block("plaster",s*.60,.28,-.30,.18,.067,.80,.05)
        m.block("grass",0,0,-.30,1.05,.048,.76,.06)
        offering(m,.03,-.29,.195,.048)
        mask(m,-.27,.68,.046,.49)
        if m.lod==0:
            for s in (-1,1):
                m.drum(s*.57,0,-.72,.107,.18)
    else:
        # Pods of individual cells cluster round a raised gong on its open frame.
        for x,z,h,r in ((-.58,.19,.62,.31),(-.30,.54,.88,.30),(.27,.56,.79,.32),(.59,.12,.55,.30)):
            m.shell(x,z,[(0,r,"fieldstone"),(.13*h,r*1.10,"metal"),(.52*h,.94*r,"glass"),
                         (.65*h,1.18*r,"ivory"),(.73*h,1.15*r,"turquoise"),
                         (h,.28*r,"metal")],(10,7,4)[m.lod],flatten=.85)
            if m.lod<2:
                m.block("door",x,0,z-r*.78,r*.55,.35*h,.052)
            if m.lod==0:
                m.tusk(x+r*.72,.63*h,z,1,.35)
        memorial_arch(m,0,-.27,1.14,1.00,.16,"ivory")
        m.post("gold",(0,.71,-.27),(0,1.07,-.27),.026,sides=5)
        m.post("gold",(0,.66,-.326),(0,.66,-.211),.232,sides=(14,9,6)[m.lod])
        m.post("trim",(0,.66,-.344),(0,.66,-.322),.069,sides=(9,6,4)[m.lod])
        for s in (-1,1):
            m.shell(s*.47,-.36,[(0,.13,"fieldstone"),(.12,.15,"metal"),(.22,.092,"ivory")],(9,6,4)[m.lod])
            m.tusk(s*.47,.18,-.36,s,.49)
        offering(m,.02,-.60,.17)


def build(spec,lod):
    m=Lodge(spec,lod)
    t=spec.building_type
    with m.transform(scale=spec.footprint[0]/2):
        if t=="SpiritTotem":
            idol(m)
        elif t=="Shrine" and m.era>0:
            pilgrim_shrine(m)
        elif t=="Temple" and m.era>0:
            high_temple(m)
        elif t=="Monastery" and m.era>1:
            monastic_order(m)
        elif t=="Shrine":
            shrine(m)
        elif t=="Monastery":
            cloister(m)
        else:
            temple(m,t=="Cathedral")
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
