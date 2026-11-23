"""Sun furnaces, reed looms, shell armories and ceramic manufacturing courts."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE="saurian_industry"


def flue(m,x,y,z,r,h,role):
    n=(10,7,5)[m.lod]
    m.drum(role,x,y,z,r,h,r*.73,n=n)
    m.drum("trim",x,y+h-.035,z,r*.91,.069,n=n)
    m.drum("door",x,y+h+.034,z,r*.66,.009,n=n)
    if m.lod<2:
        m.drum("turquoise",x,y+h*.63,z,r*.852,h*.047,n=n)


def firemouth(m,x,y,z,w,h):
    profile=[(.5,0),(-.5,0),(-.5,.63),(-.27,1),(.27,1),(.5,.63)]
    for role,width,height,depth in (("trim",w,h,0),("forge",w*.76,h*.76,-.008),("door",w*.45,h*.49,-.016)):
        m.face(role,[(x+i*width,y+j*height,z+depth) for i,j in profile])
    if m.lod<2:
        m.block("gold",x,y+h*.97,z,w*.59,h*.10,.061)


def furnace(m,x,z,r,h):
    body=("wall","fieldstone","brick","ivory")[m.era]
    m.slab("fieldstone",x,.075,z,r*2.30,.122,r*2.25,.93)
    m.slab(body,x,.197,z,r*2.02,h*.50,r*2.01,.83)
    # Projecting gate collar keeps the fire mouth in a flat, south-facing plane.
    m.block(body,x,.195,z-r*.94,r*1.18,h*.40,r*.37)
    firemouth(m,x,.196,z-r*1.125-.003,r*.94,h*.34)
    y=.197+h*.50
    m.drum("trim",x,y,z,r*.88,.062,r*.83)
    m.drum(body,x,y+.061,z,r*.83,h*.27,r*.47)
    flue(m,x,y+.06+h*.27,z,r*.48,h*(.35,.52,.76,.67)[m.era],body)


def cast_yard(m,smith):
    m.slab("soil" if m.era<2 else "fieldstone",0,0,.01,1.92,.077,1.79,.98)
    if smith:
        m.house(-.39,.077,.37,1.00,.91,.81)
        furnace(m,.52,.30,.249,.74)
        # Waisted anvil with a wide heel and long triangular working horn.
        m.drum("timber",.16,.077,-.46,.197,.244,.17)
        m.slab("metal",.16,.321,-.46,.30,.105,.246,.63)
        m.slab("metal",.16,.426,-.46,.20,.127,.176,1.57)
        m.slab("ivory",.13,.553,-.46,.43,.054,.235,.98,cut=.037)
        with m.transform(translate=(.34,.577,-.46),rotate_z=-90):
            m.drum("metal",0,0,0,.081,.217,0,n=4)
        if m.lod<2:
            m.jar(-.58,.077,-.44,.121,"turquoise")
            m.rod("timber",(-.03,.606,-.42),(.08,.606,-.50),.020)
            m.block("trim",.088,.605,-.507,.061,.039,.052)
    else:
        furnace(m,-.26,.15,.42,1.00)
        m.house(.53,.077,.40,.61,.70,.57)
        for x,z in ((.37,-.39),(.65,-.39)):
            m.slab("metal",x,.077,z,.206,.11,.37,.89)
            m.slab("gold" if m.era==0 else "accent",x,.187,z,.15,.055,.281,.78)
        if m.lod<2:
            m.jar(-.64,.077,-.49,.117,"turquoise")
    if m.lod==0:
        m.disc(-.39,.54,.37-.91*.455+.004,.061) if smith else m.disc(-.26,.70,-.195,.064)


def shield(m,x,y,z,r,role):
    # Scaphid scale, with an almond-shaped outline and a raised central boss.
    outline=[(0,-1),(-.70,-.30),(-.91,.32),(-.51,.78),(0,1),(.51,.78),(.91,.32),(.70,-.30)]
    p=[(x+i*r,y+j*r,z) for i,j in outline]
    front=(x,y+r*.12,z-r*.36)
    for i,a in enumerate(p):
        m.face(role,(a,p[(i+1)%len(p)],front))
    m.face("trim",p[::-1])
    if m.lod<2:
        m.disc(x,y+r*.09,z-r*.32,r*.215,"gold",False)


def armory(m):
    m.slab("fieldstone",0,0,0,1.92,.077,1.75,.98)
    m.house(0,.077,.20,1.65,1.00,.89)
    top=.077+.89*(.70+(.42,.51,.28,.32)[m.era]*1.11)
    m.block("trim",0,top-.02,.20,.10,.19,.12)
    shield(m,0,top+.178,.18,.196,"turquoise")
    for side in (-1,1):
        x=side*.57
        m.block("trim",x,.075,-.55,.083,.43,.087)
        m.block("timber" if m.era<2 else "metal",x,.342,-.556,.38,.072,.089)
        shield(m,x,.36,-.59,.228,"turquoise" if side==1 else "roof")
        if m.lod<2:
            for i in (-1,1):
                m.rod("trim",(x+i*.118,.080,-.50),(x+i*.118,.66,-.50),.023,n=4)
                m.drum("accent",x+i*.118,.64,-.50,.041,.122,0,n=4)
    if m.lod==0:
        m.jar(-.24,.077,-.42,.075,"hide")


def loom(m):
    m.slab("wall",0,0,0,1.86,.077,1.74,.98)
    m.house(-.22,.077,.38,1.23,.85,.78)
    # The working face turns towards the sun on the east side of the clear entrance path.
    with m.transform(translate=(.464,0,-.027),rotate_y=-12,scale=(.66,1,1)):
        loom_frame(m)
    if m.lod==0:
        for x,z in ((-.45,-.53),(-.71,-.62)):
            m.drum("trim",x,.077,z,.094,.27,.073,n=7)
            m.drum("cloth",x,.107,z,.113,.174,n=9)
    if m.era>=2:
        m.drum("metal",-.70,.077,-.25,.164,.251)
        m.drum("turquoise",-.70,.328,-.25,.139,.049)


def loom_frame(m):
    for x in (-.43,.50):
        m.block("trim",x,.077,-.46,.083,.88,.088)
    for h in (.196,.900):
        m.rod("timber" if m.era<2 else "metal",(-.49,h,-.46),(.56,h,-.46),.051)
    width=.81
    for j,role in enumerate(("turquoise","gold","turquoise","banner")):
        y=.218+j*.085
        m.block(role,.035,y,-.46,width,.085,.047)
    if m.lod<2:
        for i in range(6 if m.lod==0 else 4):
            x=-.315+i*(.70/(5 if m.lod==0 else 3))
            m.rod("ivory",(x,.552,-.46),(x,.887,-.46),.023,n=4)
        m.disc(.50,.807,-.503,.068)
    else:
        for x in (-.26,.035,.33):
            m.block("ivory",x,.558,-.46,.043,.335,.022)


def workshop(m):
    m.slab("fieldstone",0,0,0,1.92,.077,1.81,.98)
    m.house(-.27,.077,.32,1.14,.96,.86)
    # Potter's flywheel and a second pot being glazed on the loading bench.
    m.drum("trim",.47,.077,-.27,.224,.10)
    m.drum("metal",.47,.175,-.27,.075,.17)
    m.drum("timber",.47,.34,-.27,.242,.055)
    m.jar(.47,.395,-.27,.131,"turquoise")
    for x in (-.72,-.16):
        m.block("trim",x,.077,-.48,.106,.26,.26)
    m.slab("timber",-.44,.332,-.48,.77,.085,.35,cut=.04,underside=True)
    m.jar(-.49,.417,-.48,.132,"hide")
    if m.lod<2:
        m.jar(-.24,.417,-.45,.070,"turquoise")
    if m.era==2:
        flue(m,-.60,.57,.52,.103,.62,"brick")


def factory(m,kind):
    m.slab("fieldstone",0,0,.04,1.93,.072,1.78,.98)
    m.slab("wall",-.05,.072,.20,1.65,.477,1.09,cut=.055)
    m.entry(-.38,.074,-.345,.39,.366)
    if m.lod<2:
        m.block("glass",.35,.263,-.351,.57,.15,.023)
        for x in (.16,.35,.54):
            m.block("trim",x,.252,-.365,.026,.17,.028)
        # A sun calendar and splayed brackets break up the east end wall.
        with m.transform(translate=(.771,.31,.18),rotate_y=-90):
            m.disc(0,0,0,.142)
        for z in (-.135,.50):
            m.slab("trim",.768,.073,z,.10,.346,.082,.62,cut=.06)
    if kind=="Factory":
        # Three northlight bays lean towards the sun. A low transfer gallery serves the loading door.
        for i in range(3):
            x=-.57+i*.52
            with m.transform(translate=(x,.549,.2),rotate_y=90):
                height=(.224 if m.era==2 else .168+.070*(i==1))
                m.wedge((0,0,0),1.12,.515,height,m.color("glass" if m.era==3 else "roof"),material="glass" if m.era==3 else "roof")
        if m.era==3:
            m.slab("ivory",-.05,.605,.22,.32,.253,.65,.69)
            m.slab("turquoise",-.05,.858,.22,.25,.044,.50,.87)
        flue(m,.57,.55,.49,.115,.62,"brick" if m.era==2 else "metal")
        m.slab("trim",-.35,.072,-.49,.66,.112,.38,.98)
        for i in range(3):
            m.block("wall",-.35,.072,-.808+i*.051,.42,.037*(i+1),.051)
        if m.lod==0:
            for x in (.24,.53):
                m.slab("timber",x,.072,-.54,.223,.19,.22,.98,cut=.04)
    elif kind=="Foundry":
        m.rooflet(-.05,.549,.20,1.73,1.16,.23,m.skin)
        for x,h in ((-.53,.78),(.46,1.04)):
            flue(m,x,.58,.36,.137,h,"brick" if m.era==2 else "ivory")
        if m.era==3:
            m.rod("turquoise",(-.53,1.17,.36),(.46,1.38,.36),.065)
            m.drum("metal",.46,1.50,.36,.163,.109,.12)
        # A broad casting ladle sits on trunnions; the sunlit metal shows through the open rim.
        for x in (.10,.63):
            m.slab("metal",x,.072,-.640,.113,.33,.21,.84)
        m.rod("trim",(.094,.36,-.640),(.67,.36,-.640),.043)
        m.drum("metal",.368,.218,-.640,.18,.26,.261)
        m.drum("forge",.368,.478,-.640,.213,.016)
    else:
        # Glass clean-room roof and a set of pale insulating ceramic pylons.
        m.slab("glass",-.05,.549,.20,1.62,.23,1.04,.76,cut=.12)
        m.slab("ivory",-.05,.779,.20,1.29,.074,.83,.94)
        for x in (-.53,.45):
            m.drum("metal",x,.85,.21,.104,.29,.079)
            for j in range(3 if m.lod<2 else 2):
                m.drum("ivory",x,.874+j*.086,.21,.158-.016*j,.049,.105,n=(9,6,4)[m.lod])
            m.drum("gold",x,1.13,.21,.052,.051)
        if m.lod<2:
            m.rod("turquoise",(-.53,1.16,.21),(.45,1.16,.21),.019)
    if m.lod==0:
        m.disc(-.04,.413,-.345,.077)
        m.jar(-.77,.072,-.57,.080,"turquoise")


def build(spec,lod):
    m=Delta(spec,lod)
    w,d=spec.footprint
    # Machinery retains a round section wherever the work list grants a square yard.
    with m.transform(scale=(w/2,min(w,d)/2 if w==d else .77,d/2)):
        name=spec.building_type
        if name in ("Smelter","Blacksmith"):
            cast_yard(m,name=="Blacksmith")
        elif name=="Armory":
            armory(m)
        elif name=="Weaver":
            loom(m)
        elif name=="Workshop":
            workshop(m)
        else:
            factory(m,name)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
