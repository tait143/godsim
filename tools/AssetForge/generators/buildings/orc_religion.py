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


def temple(m,cathedral):
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


def build(spec,lod):
    m=Lodge(spec,lod)
    t=spec.building_type
    with m.transform(scale=spec.footprint[0]/2):
        if t=="SpiritTotem":
            idol(m)
        elif t=="Shrine":
            shrine(m)
        elif t=="Monastery":
            cloister(m)
        else:
            temple(m,t=="Cathedral")
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
