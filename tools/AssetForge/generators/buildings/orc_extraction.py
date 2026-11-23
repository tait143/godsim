"""Orc logging yards, cut-stone benches and mineral hoists."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_towncenters import Lodge, generate

PACKAGE="orc_extraction"


def rock(m,x,z,y,r,h,mat,lean=.13):
    with m.transform(translate=(x,y,z),rotate_y=17):
        profile=[(0,r*.85,mat),(h*.33,r,mat),(h*.76,r*.77,mat),(h,r*.38,mat)]
        if m.lod:
            profile.pop(2)
        m.shell(0,0,profile,sides=(7,5,4)[m.lod],flatten=.78,dx=lean)


def wheel(m,x,y,z,r,mat="timber"):
    # The winding wheel is an open annulus with four spokes and a steel axle.
    if m.lod==2:
        m.post(mat,(x,y,z),(x,y,z+.065),r,sides=7)
        return
    n=(12,7)[m.lod]
    points=[(math.cos(2*math.pi*j/n)*r,math.sin(2*math.pi*j/n)*r) for j in range(n)]
    for j in range(n):
        k=(j+1)%n
        ax,ay=points[j]
        bx,by=points[k]
        f=[(x+ax,y+ay,z),(x+bx,y+by,z),(x+.71*bx,y+.71*by,z),(x+.71*ax,y+.71*ay,z)]
        m.panel(mat,f[::-1])
        back=[(u,v,w+.063) for u,v,w in f]
        m.panel(mat,back)
        m.panel(mat,[f[0],f[1],back[1],back[0]])
        m.panel(mat,[back[3],back[2],f[2],f[3]])
    for a in (0,math.pi/2):
        dx,dy=r*.78*math.cos(a+.27),r*.78*math.sin(a+.27)
        m.post("trim",(x-dx,y-dy,z+.03),(x+dx,y+dy,z+.03),r*.11,sides=4)
    m.post("gold",(x,y,z-.029),(x,y,z+.13),r*.20,sides=6)


def log(m,x,y,z,length,r):
    m.post("timber",(x,y,z-length*.5),(x,y,z+length*.5),r,sides=(9,7,5)[m.lod])
    if m.lod<2:
        m.post("hide",(x,y,z-length*.5-.006),(x,y,z-length*.5+.002),r*.86,sides=7)
        if m.lod==0:
            m.post("roof",(x,y,z-length*.5-.013),(x,y,z-length*.5-.004),r*.34,sides=6)


def lumber(m,saw):
    m.hut(-.31,.35,1.13 if saw else 1.21,roof_material="metal" if m.era==0 else None)
    for x,z in ((.35,-.22),(.68,-.20),(.52,-.24)):
        log(m,x,.13 if x!=.52 else .312,z,1.05,.125)
    if saw:
        m.block("wall",-.28,0,-.48,.67,.26,.64,.09)
        m.block("metal",-.28,.26,-.48,.76,.086,.72)
        # A partially submerged crosscut saw has a deep scalloped tooth profile.
        n=(22,14,9)[m.lod]
        ring=[]
        for j in range(n):
            a=2*math.pi*j/n
            for delta,r in ((0,.237),(.63,.286)):
                b=a+2*math.pi*delta/n
                ring.append((r*math.cos(b),r*math.sin(b)))
        for i,(u,v) in enumerate(ring):
            w,h=ring[(i+1)%len(ring)]
            a=(-.28+u,.351+v,-.46)
            b=(-.28+w,.351+h,-.46)
            d=(a[0],a[1],-.413)
            c=(b[0],b[1],-.413)
            m.panel("metal",[(-.28,.351,-.46),b,a])
            m.panel("metal",[(-.28,.351,-.413),d,c])
            m.panel("ivory",[a,b,c,d])
        m.post("gold",(-.28,.351,-.499),(-.28,.351,-.399),.071)
        if m.lod<2:
            m.post("trim",(-.54,.392,-.67),(.04,.392,-.67),.045)
    else:
        m.shell(-.44,-.48,[(0,.236,"timber"),(.245,.183,"hide")])
        # A broad wedge-shaped axe sunk in the chopping block, with its handle inclined.
        m.post("timber",(-.45,.28,-.48),(-.13,.71,-.45),.033)
        m.block("metal",-.49,.23,-.48,.202,.145,.065,.16)
        if m.lod<2:
            m.tusk(.69,.12,.25,1,.40)
    if m.era>=2:
        m.shell(.49,.59,[(0,.19,"wall"),(.24,.20,"glass" if m.era==3 else "metal"),
                          (.62 if m.era==3 else .44,.146,"roof"),(.74 if m.era==3 else .56,.06,"gold")])
    if m.lod==0:
        m.flag(-.58,.10,.49,.99,.26)


def spoil(m,kind):
    quarry=kind=="StoneQuarry"
    base={"CoalMine":"door","GoldMine":"fieldstone","Mine":"fieldstone","StoneQuarry":"fieldstone"}[kind]
    if quarry:
        for i in range(3):
            m.block("fieldstone",.41+i*.06,i*.11,-.14+i*.15,.72-i*.09,.12,.91-i*.16,.032)
        if m.lod<2:
            m.block("plaster",.45,.11,-.48,.33,.13,.22)
            m.block("fieldstone",.64,.11,-.15,.21,.16,.31)
        return
    for x,z,r,h in ((.47,.27,.341,.35),(.69,-.14,.195,.24),(.34,-.22,.182,.24))[:(3,3,2)[m.lod]]:
        rock(m,x,z,0,r,h,base)
    if kind=="GoldMine":
        for x,z,y,rad in ((.41,.16,.237,.132),(.47,-.22,.081,.097),(.65,-.21,.104,.109))[:(3,2,1)[m.lod]]:
            rock(m,x,z,y,rad,rad*1.63,"gold",.27)
    if kind=="CoalMine":
        # Battered stanchions support a coal hopper that narrows down to the discharge gate.
        for x in (.35,.75):
            m.block("metal",x,0,-.42,.080,.43,.29)
        m.shell(.55,-.42,[(.255,.123,"metal"),(.58,.302,"trim"),(.67,.302,"door")],(8,6,4)[m.lod])
        if m.lod<2:
            rock(m,.54,-.42,.65,.23,.17,"door")
        else:
            m.cone((.54,.665,-.42),.225,.16,m.col("door"),segments=5,material="door")
        if m.lod<2:
            m.wedge((.55,.07,-.63),.255,.42,.213,m.col("metal"),material="metal")


def portal(m,kind):
    mat=("timber","fieldstone","brick","plaster")[m.era]
    m.block("door",-.38,0,-.32,.425,.414,.30)
    for side in (-1,1):
        m.block(mat,-.38+side*.235,0,-.35,.116,.42,.38,.025)
    # Three lintel stones carry the bank, with a pendant ochre keystone.
    m.block(mat,-.38,.404,-.34,.626,.136,.397,.045)
    m.block("gold" if kind=="GoldMine" else "banner",-.38,.39,-.533,.124,.16,.069,.12)
    for side in (-1,1):
        m.tusk(-.38+side*.259,.34,-.52,side,.69)
    if m.lod==2:
        m.block("timber",-.38,0,-.64,.37,.023,.45)
        return
    for side in (-1,1):
        m.post("trim",(-.38+side*.128,.023,-.87),(-.38+side*.10,.023,-.43),.017,sides=4)
    for z in (-.80,-.64,-.49) if m.lod<2 else (-.74,):
        m.block("timber",-.38,0,z,.405,.022,.053)


def derrick(m,kind):
    gold=kind=="GoldMine"
    frame=("timber","timber","metal","plaster")[m.era]
    h=(.94,1.09,1.17,1.26)[m.era]+(.13 if gold else 0)
    for x in (-.66,-.12):
        for z in (-.124,.46):
            tx=-.39+(x+.39)*.48
            tz=.17+(z-.17)*.56
            m.post(frame,(x,.025,z),(tx,h,tz),.063,.047,sides=(6,5,4)[m.lod])
        if m.lod<2:
            m.post(frame,(x*.99,.19,-.10),(-.39+(x+.39)*.58,h*.84,.36),.035,sides=4)
    m.block("cloth",-.39,h-.068,.18,.54,.115,.46)
    if kind=="Mine" and m.era==0:
        m.post("metal",(-.60,.66,.09),(-.18,.66,.09),.137,sides=(10,7,4)[m.lod])
    wheel(m,-.39,h+.127,-.069,.186,"gold" if gold else "metal" if m.era>=2 else "timber")
    m.post("metal",(-.39,h+.128,-.074),(-.39,h+.128,.253),.046)
    m.post("trim",(-.222,.53,-.087),(-.222,h+.15,-.057),.013,sides=4)
    if m.era in (1,3):
        for x in (-.62,-.16):
            m.block(frame,x,h+.046,.20,.039,.34,.048)
        roof="glass" if m.era==3 else "hide"
        m.hip_roof((-.39,h+.38,.20),.58,.49,.18,m.col(roof),overhang=0,material=roof)
    if m.era>=2:
        m.block("brick" if m.era==2 else "glass",.26,0,.54,.58,.47,.48,.07)
        m.block("metal",.26,.46,.54,.60,.086,.55)
        if m.lod<2:
            m.shell(.39,.57,[(.52,.088,"metal"),(.77,.082,"cloth"),(.82,.097,"door")],7)
    if m.lod==0:
        m.flag(-.59,h,.20,.34,.196)


def mine(m,kind):
    # A retaining bank surrounds the entrance behind the timbered headframe.
    m.block("soil",-.34,0,.15,.93,.22,.88,.11)
    rock(m,-.56,.34,.17,.39,.38,"fieldstone",-.08)
    portal(m,kind)
    derrick(m,kind)
    spoil(m,kind)
    if m.lod<2:
        m.drum(.19,0,-.57,.122,.191)


def quarry(m):
    spoil(m,"StoneQuarry")
    m.hut(-.47,.46,.73,roof_material="metal" if m.era==0 else None)
    mat=("timber","trim","metal","plaster")[m.era]
    m.shell(-.50,-.32,[(0,.20,"fieldstone"),(.14,.174,"trim")])
    m.post(mat,(-.50,.13,-.32),(-.50,1.00,-.32),.073,.057)
    m.post(mat,(-.63,.95,-.32),(.41,1.16,-.22),.060,.041)
    m.post(mat,(-.50,.55,-.32),(.17,1.096,-.24),.038)
    m.post("trim",(.39,.37,-.216),(.39,1.155,-.216),.018,sides=4)
    m.block("fieldstone",.39,.238,-.216,.27,.16,.206,.04)
    m.tusk(-.50,.94,-.32,-1,.39)
    if m.lod<2:
        wheel(m,-.50,.38,-.436,.144,"timber")
    if m.era>=2:
        m.block("metal",-.52,.13,-.32,.259,.29,.27,.09)
    if m.lod==0:
        m.drum(-.72,0,-.51,.126,.219)


def separator(m):
    # A compact downhole pump feeds a production tree, a vertical separator and a horizontal tank.
    m.shell(-.37,.26,[(0,.37,"fieldstone"),(.12,.40,"metal"),(.25,.28,"ivory"),
                       (.43,.25,"metal"),(.78,.24,"glass"),(.85,.29,"turquoise"),
                       (1.13,.26,"metal"),(1.27,.20,"ivory"),(1.34,.10,"trim")],(14,8,5)[m.lod])
    for x in (-.62,-.12):
        m.post("metal",(x,.02,.28),(x,.59,.28),.066,.042,sides=(8,5,4)[m.lod])
        m.tusk(x,.52,.28,1 if x>-.3 else -1,.52)
    for z in (-.23,.40):
        m.block("fieldstone",.47,0,z,.51,.19,.22,.08)
    m.post("ivory",(.47,.40,-.44),(.47,.40,.62),.247,.247,sides=(14,8,5)[m.lod])
    for z in (-.38,.48):
        m.post("metal",(.47,.40,z),(.47,.40,z+.09),.258,.258,sides=(14,8,5)[m.lod])
    # The wellhead tree branches out to a pair of control wheels.
    m.shell(-.19,-.51,[(0,.24,"fieldstone"),(.11,.25,"metal"),(.17,.11,"trim"),
                         (.38,.10,"metal"),(.44,.15,"ivory"),(.58,.11,"metal"),
                         (.68,.15,"turquoise")],(10,7,4)[m.lod])
    m.post("metal",(-.19,.37,-.49),(-.37,.37,.21),.065,sides=(8,5,4)[m.lod])
    m.post("metal",(-.29,.44,.29),(.48,.44,.25),.075,sides=(8,5,4)[m.lod])
    m.post("trim",(-.40,.52,-.50),(.02,.52,-.50),.046,sides=(7,5,4)[m.lod])
    for s in (-1,1):
        cx=-.19+s*.21
        with m.transform(translate=(cx,.52,-.50),rotate_y=s*90):
            wheel(m,0,0,-.04,.11,"gold")
    if m.lod==0:
        m.flag(-.39,1.27,.24,.30,.17)



def pump(m):
    if m.era==3:
        return separator(m)
    m.block("fieldstone",-.15,0,.04,1.34,.125,1.24,.065)
    m.hut(-.45,.40,.74,base=.125)
    mat="ivory" if m.era==3 else "metal"
    for z in (-.46,-.06):
        for s in (-1,1):
            m.post(mat,(s*.30,.125,z),(.04+s*.045,.98,z),.053,.045)
    m.post("trim",(.04,.96,-.55),(.04,.96,.02),.077)
    m.post("banner",(-.63,.83,-.27),(.58,1.07,-.27),.081)
    m.block("metal",.56,.83,-.27,.206,.304,.223,.24)
    m.post("metal",(.61,.18,-.27),(.61,.94,-.27),.031)
    m.shell(.61,-.27,[(0,.147,"metal"),(.17,.135,"banner"),(.25,.088,"trim")])
    wheel(m,-.51,.372,-.42,.25,"banner")
    m.post("trim",(-.52,.35,-.27),(-.60,.83,-.27),.044)
    m.block("metal",-.51,.125,-.22,.29,.30,.26,.08)
    m.tusk(-.58,.79,-.27,-1,.40)
    if m.lod<2:
        m.shell(.47,.45,[(0,.26,"metal"),(.50,.241,"banner"),(.55,.258,"metal"),(.62,.198,"trim")])
        if m.lod==0:
            m.flag(.52,.59,.45,.37,.202)


def build(spec,lod):
    m=Lodge(spec,lod)
    name=spec.building_type
    with m.transform(scale=spec.footprint[0]/2):
        if name in ("WoodcutterCamp","Sawmill"):
            lumber(m,name=="Sawmill")
        elif name=="StoneQuarry":
            quarry(m)
        elif name=="OilWell":
            pump(m)
        else:
            mine(m,name)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
