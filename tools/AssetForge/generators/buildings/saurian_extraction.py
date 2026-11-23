"""Tidal ore galleries, quarry terraces, reedwood log camps and delta pumpworks."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE="saurian_extraction"


def boulder(m, role, x,y,z,r,h, twist=0):
    # Displaced hexagonal strata; the same rock keeps its silhouette at distance.
    n=(7,6,5)[m.lod]
    rings=[]
    for k,(rad,dy,ox) in enumerate(((.88,0,0),(1,.42,.02),(.60,.89,-.09),(.28,1,-.13))):
        if m.lod and k==2:
            continue
        ring=[]
        for i in range(n):
            a=2*math.pi*i/n+twist
            sway=1+.105*math.sin(3*a+1.1)
            ring.append((x+r*(ox+sway*rad*math.cos(a)),y+h*dy,z+r*.79*sway*rad*math.sin(a)))
        rings.append(ring)
    for low,high in zip(rings,rings[1:]):
        for i in range(n):
            j=(i+1)%n
            m.face(role,[low[j],low[i],high[i],high[j]])
    m.face(role,rings[-1][::-1])


def pulley(m,x,y,z,r):
    m.ring("metal" if m.era>=2 else "timber",x,y,z,r,r*.65,r*.45,n=(12,8,5)[m.lod])
    for i in range((5,4,3)[m.lod]):
        a=i*math.tau/(5,4,3)[m.lod]
        m.rod("accent",(x+r*.16*math.cos(a),y+r*.16*math.sin(a),z+r*.22),
              (x+r*.75*math.cos(a),y+r*.75*math.sin(a),z+r*.22),r*.095,4)
    m.disc(x,y,z+r*.08,r*.30,"gold",False)


def mine(m,kind):
    role="gold" if kind=="GoldMine" else "door" if kind=="CoalMine" else "metal"
    frame="timber" if m.era<2 else "metal"
    m.slab("soil",0,0,.01,1.88,.078,1.74,.97)
    # The hill is a half-buried, stratified shoulder surrounding the timber-lined adit.
    boulder(m,"fieldstone",-.20,.078,.29,.72,.68,.17)
    boulder(m,"fieldstone",-.65,.078,.05,.29,.34,.61)
    m.slab("trim",-.22,.077,-.17,.59,.46,.39,.96)
    m.entry(-.22,.079,-.365,.391,.382)
    # Rails and sleepers emerge towards the landing.
    for s in (-1,1):
        m.block(frame,-.22+s*.128,.083,-.593,.031,.030,.46)
    if m.lod<2:
        for z in (-.46,-.61,-.76):
            m.block("timber",-.22,.078,z,.37,.021,.039)
    x=.27
    span=(.29,.30,.32,.25)[m.era]
    h=(.97,1.13,1.34,1.49)[m.era]
    for s in (-1,1):
        for t in (-1,1):
            m.rod(frame,(x+s*span,.078,.18+t*.245),(x+s*.13,h,.18+t*.094),.050,n=(6,5,4)[m.lod])
    # Open central slot lets the sheave turn freely between its bearing beams.
    for rail_z in (.038,.270):
        m.block("trim",x,h-.04,rail_z,.49,.092,.097)
    for side in (-1,1):
        m.block(frame,x+side*.213,h-.04,.154,.059,.092,.135)
    pulley(m,x,h+.10,.112,.177)
    m.block(frame,x,h+.047,.251,.125,.075,.11)
    m.block(frame,x,h+.047,.051,.113,.075,.074)
    m.rod(frame,(x,h+.10,.021),(x,h+.10,.293),.049,n=(8,6,4)[m.lod])
    if m.era in (1,3):
        for s in (-1,1):
            m.block(frame,x+s*.19,h,.19,.041,.32,.059)
        m.rooflet(x,h+.32,.19,.54,.42,.18,m.skin)
    if m.lod<2:
        m.rod(frame,(x-span*.80,.38,-.032),(x+.158,h-.13,.073),.025,4)
        m.rod("trim",(x-.149,h+.12,.166),(x-.149,.35,.166),.017)
    if kind=="GoldMine":
        # Gold-washing sluice in front of the hoist.
        with m.transform(translate=(.51,.166,-.43),rotate_x=-11):
            m.slab("turquoise",0,0,0,.38,.05,.64,cut=.05,underside=True)
            for s in (-1,1):
                m.block("timber",s*.19,0,0,.049,.085,.65)
            if m.lod<2:
                for z in (-.19,-.02,.15):
                    m.block("gold",0,.048,z,.32,.036,.028)
        for z,hp in ((-.65,.146),(-.23,.235)):
            m.block("fieldstone",.51,.078,z,.24,hp-.078,.11)
    else:
        # A raised spoil bunker or mine tub, with its load protruding past the lip.
        w=.53 if kind=="CoalMine" else .39
        m.slab("metal" if m.era>=2 else "timber",.52,.078,-.45,w,.205,.44,1.13)
        boulder(m,role,.52,.278,-.45,w*.50,.15,.43)
    if m.lod==0:
        for i in range(3):
            boulder(m,role,-.47+i*.14,.48+(.09 if i==1 else .02),.01+i*.03,.087,.079,i*.53)
        m.jar(-.63,.078,-.39,.091,"turquoise")
        # A carved sun gauge on the adit and a triple measure on its jambs.
        m.disc(-.22,.473,-.357,.083)
        for s in (-1,1):
            for i in range(3):
                m.block("turquoise",-.22+s*.239,.17+i*.078,-.36,.056,.034,.047)
    if kind=="CoalMine":
        m.drum("brick" if m.era==2 else "glass",.65,.079,.45,.15,.67,.126)
        m.drum("metal",.65,.742,.45,.158,.088)


def quarry(m):
    m.slab("soil",0,0,.02,1.87,.07,1.76,.98)
    for j in range(3):
        m.slab("plaster" if m.era==0 else "fieldstone",-.22+j*.043,.07+j*.205,.05+j*.20,
               1.23-j*.19,.205,1.08-j*.21,.945,cut=.09)
    # A tripod derrick on the working bench passes stones down to the loading floor.
    frame="timber" if m.era<2 else "metal"
    h=(.95,1.12,1.31,1.45)[m.era]
    for x,z in ((.28,.48),(.75,.45),(.53,-.01)):
        m.rod(frame,(x,.071,z),(.54,h,.25),.051,6 if m.lod==0 else 4)
    m.rod(frame,(.54,h-.13,.25),(.40,h+.20,-.33),.064)
    m.rod("trim",(.40,h+.20,-.33),(.40,.735,-.33),.021)
    m.slab("fieldstone",.40,.390,-.33,.32,.21,.27)
    m.block("metal",.40,.360,-.33,.378,.035,.078)
    for s in (-1,1):
        x=.40+s*.18
        m.rod("gold",(x,.38,-.33),(x,.607,-.33),.017,4)
        m.rod("gold",(x,.607,-.33),(.40,.735,-.33),.018,4)
    m.disc(.54,h+.004,.21,.080)
    for i,(x,z) in enumerate(((-.64,-.56),(-.21,-.63),(.59,-.65))):
        if m.lod==2 and i==1:
            continue
        m.slab("fieldstone",x,.07,z,.32,.160+i*.02,.25,.97,cut=.06)
        if m.lod==0:
            m.block("turquoise",x,.14,z-.128,.19,.033,.012)
    if m.era>=2:
        m.slab("metal",.64,.07,.57,.46,.23,.42,.91)
        m.drum("glass",.64,.30,.57,.13,.09)
    if m.lod<2:
        for i in range(3):
            m.rod("trim",(-.62+i*.17,.46,.23),(-.59+i*.17,.54,.27),.026)
        if m.lod==0:
            m.jar(-.76,.07,-.29,.089,"thatch")


def log(m,x,y,z,length,r,angle=0):
    with m.transform(translate=(x,y,z),rotate_y=angle,rotate_x=-90):
        m.drum("timber",0,0,0,r,length,r*.88,n=(9,6,4)[m.lod],bottom=True)
        if m.lod<2:
            m.drum("thatch",0,length+.0002,0,r*.75,.010,n=(9,6)[m.lod])
        if m.lod==0:
            m.drum("trim",0,length+.0102,0,r*.28,.008,n=7)


def timberworks(m,sawmill):
    m.slab("soil",0,0,0,1.86,.068,1.79,.98)
    m.house(-.38,.068,.39,.91,.74,.76 if not sawmill else .91,role="metal" if m.era==0 and not sawmill else None)
    for i in range((3,3,2)[m.lod]):
        log(m,.24+i*.222,.177,.47,.99,.113,-5)
    log(m,.35,.357,.45,.96,.114,-5)
    if m.lod<2:
        log(m,.57,.356,.41,.93,.109,-5)
    if sawmill:
        m.slab("metal",-.36,.07,-.43,.78,.30,.48,.87)
        m.slab("timber",-.36,.37,-.43,.85,.057,.50,cut=.04)
        teeth=(12,9,6)[m.lod]
        rim=[]
        for i in range(teeth):
            for fraction,radius in ((0,.224),(.19,.294)):
                a=math.tau*(i+fraction)/teeth
                rim.append((-.36+radius*math.cos(a),.429+radius*math.sin(a)))
        for i,(ax,ay) in enumerate(rim):
            bx,by=rim[(i+1)%len(rim)]
            m.face("slate",((bx,by,-.430),(ax,ay,-.430),(-.36,.429,-.430)))
            m.face("slate",((ax,ay,-.387),(bx,by,-.387),(-.36,.429,-.387)))
            m.face("metal",((ax,ay,-.430),(bx,by,-.430),(bx,by,-.387),(ax,ay,-.387)))
        m.disc(-.36,.452,-.437,.078)
    else:
        m.drum("timber",-.42,.068,-.49,.224,.251,.178)
        m.drum("thatch",-.42,.317,-.49,.182,.027)
        for i in range((3,2,1)[m.lod]):
            m.blade("grass",(-.55,.33,-.50),(-.64+i*.09,.52+i*.036,-.45+i*.026),.047)
        if m.lod<2:
            m.jar(-.72,.068,-.22,.09,"turquoise")
    if m.era==1:
        m.rooflet(-.38,.95,.39,.41,.34,.16,"roof_tile")
    if m.era>=2:
        m.drum("brick" if m.era==2 else "glass",-.67,.55,.50,.084,.56,.072)
        m.drum("metal",-.67,1.10,.50,.085,.067)
    m.disc(-.38,.63 if not sawmill else .73,.046,.085)


def oil(m):
    m.slab("fieldstone",0,0,.01,1.88,.115,1.49,.96)
    m.slab("wall",-.23,.115,.19,.68,.37,.62)
    m.entry(-.23,.116,-.119,.27,.30)
    for x in (-.50,.04):
        m.rod("metal",(x,.47,.20),(-.23,1.18,.20),.061)
    m.rod("metal",(-.23,.47,.47),(-.23,1.18,.20),.060)
    with m.transform(translate=(-.23,1.15,.20),rotate_z=12):
        m.slab("turquoise",.06,0,0,1.19,.13,.16,.97,cut=.05)
        m.slab("trim",.59,-.15,0,.16,.34,.21,.80)
        m.drum("metal",-.44,-.07,0,.132,.25)
    m.disc(-.23,1.19,.099,.108)
    m.rod("metal",(.39,.40,.20),(.39,1.18,.20),.023)
    m.drum("metal",.39,.115,.20,.133,.29,.090)
    for x in ((.32,.75) if m.era==3 else (.65,)):
        m.drum("metal" if m.era==2 else "glass",x,.115,-.41,.181,.51)
        m.mound("ivory",x,.625,-.41,.182,.105)
        if m.lod==0:
            m.drum("turquoise",x,.46,-.41,.186,.045)
    if m.lod<2:
        m.rod("metal",(.39,.26,.20),(.57,.26,-.41),.041)
    if m.lod==0:
        m.jar(-.67,.115,-.35,.093,"turquoise")


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        kind=spec.building_type
        if kind in ("Mine","GoldMine","CoalMine"):
            mine(m,kind)
        elif kind=="StoneQuarry":
            quarry(m)
        elif kind=="OilWell":
            oil(m)
        else:
            timberworks(m,kind=="Sawmill")
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
