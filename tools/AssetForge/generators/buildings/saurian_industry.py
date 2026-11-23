"""Sun furnaces, reed looms, shell armories and ceramic manufacturing courts."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

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


def anvil(m,x,z,h):
    m.slab("fieldstone",x,.08,z,.46,h*.66,.37,.72)
    m.slab("metal",x,.08+h*.66,z,.32,h*.34,.27,1.18)
    m.slab("ivory",x,.08+h,z,.47,.072,.29,.92)
    with m.transform(translate=(x+.22,.113+h,z),rotate_z=-90):
        m.drum("metal",0,0,0,.081,.19,0,n=4)


def cast_yard(m,smith):
    if m.era:
        works.footing(m)
        if smith:
            if m.era==1:
                # Outdoor anvil court below a stepped smiths' guild hall.
                works.pagoda(m,-.34,.36,1.02,.85,.75)
                m.drum("wall",.49,.08,.33,.25,.38,.21)
                m.mound("roof",.49,.46,.33,.21,.23)
                firemouth(m,.49,.09,.075,.25,.29)
                anvil(m,.12,-.44,.33)
            elif m.era==2:
                # A power hammer, its box slide and a brick boiler share the work floor.
                works.barrel(m,-.52,.32,.66,.94,.38,.25)
                works.spire(m,-.62,.54,.12,1.18)
                m.slab("metal",.33,.08,.18,.35,.83,.42,.71)
                m.slab("metal",.33,.91,-.032,.49,.18,.83,.94,underside=True)
                m.drum("trim",.33,.51,-.305,.105,.40)
                m.slab("metal",.33,.43,-.305,.25,.12,.22)
                anvil(m,.33,-.30,.26)
            else:
                # The solar induction coil surrounds the heated billet. Its two
                # contacts lead down to the ceramic supply chamber at the rear.
                works.lens(m,-.37,.40,.40,.38)
                works.conveyor(m,.32,-.16,.57,1.12,.28,"metal")
                works.arch(m,.32,-.18,.89,.99,.12,.20,"turquoise")
                m.block("forge",.32,.393,-.20,.14,.063,.59)
                if m.lod<2:
                    m.rod("gold",(-.02,.35,.32),(.36,.34,.38),.057)
        elif m.era==1:
            # Paired domed bloomery kilns share a charcoal store and a tapping yard.
            for x,z,r,h in ((-.37,.24,.42,.45),(.47,.39,.29,.34)):
                m.drum("wall",x,.08,z,r,h,r*.94)
                m.mound("roof",x,.08+h,z,r*.94,r*.72)
                works.spire(m,x,z,r*.21,.20,.08+h+r*.63,"wall")
                m.block("fieldstone",x,.08,z-r*.94,r*1.12,h*.82,.15)
                firemouth(m,x,.08,z-r*.94-.078,r*.84,h*.69)
            m.slab("door",-.36,.08,-.47,.60,.08,.33,.92)
            if m.lod<2:
                m.jar(.48,.08,-.39,.15,"timber")
        elif m.era==2:
            # A blast furnace fed from a sloping skip; moulds line the tapping floor.
            x,z=.14,.24
            m.drum("brick",x,.08,z,.42,.56,.35)
            m.drum("metal",x,.64,z,.35,.29,.22)
            works.spire(m,x,z,.22,.48,.93)
            m.block("brick",x,.08,-.14,.45,.32,.22)
            firemouth(m,x,.08,-.252,.30,.27)
            m.rod("metal",(-.69,.15,-.37),(x,.90,z),.10)
            if m.lod<2:
                m.rod("turquoise",(.41,.30,.19),(.44,.79,.19),.045)
                if m.lod==0:
                    m.disc(.36,.52,-.04,.087)
            for dx in (-.36,.34):
                m.slab("metal",dx,.08,-.52,.29,.087,.34,.91)
                m.slab("forge",dx,.167,-.52,.18,.029,.225,.80)
        else:
            # Reflecting concentrator converges over an open ceramic melt bowl.
            m.drum("ivory",-.05,.08,-.16,.28,.28,.37)
            m.drum("turquoise",-.05,.36,-.16,.37,.14,.51)
            m.drum("forge",-.05,.50,-.16,.43,.025)
            for x in (-.63,.54):
                m.rod("ivory",(x,.08,.48),(x*.63,1.05,.40),.088)
            with m.transform(translate=(-.046,1.09,.38),rotate_x=-31):
                m.ring("gold",0,0,0,.53,.35,.13,n=(20,12,7)[m.lod])
                for side in (-1,1):
                    m.rod("turquoise",(side*.38,-.17,.065),(0,.10,.065),.031)
                m.disc(0,.10,.041,.09,"glass",False)
            if m.lod<2:
                works.shell(m,.57,-.26,.38,.46,.28)
        return
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
    if m.era:
        works.footing(m)
        if m.era==1:
            # A terraced arsenal towers above two ceremonial shield stelae.
            peak=works.pagoda(m,.02,.31,1.35,.99,.86)
            for x in (-.52,.56):
                m.slab("wall",x,.08,-.43,.29,.40,.24,.79)
                shield(m,x,.47,-.56,.23,"turquoise")
            if m.lod<2:
                m.drum("trim",.02,peak,.31,.065,.14)
                m.disc(.02,peak+.19,.30,.12)
        elif m.era==2:
            # Munitions are stored in a low vaulted earthwork beside an armored
            # inspection turret. A broad shield marks the magazine door.
            works.barrel(m,-.28,-.06,1.10,1.36,.39,.38)
            m.drum("brick",.54,.08,.35,.30,.65,.24)
            m.drum("door",.54,.73,.35,.24,.067)
            m.drum("metal",.54,.797,.35,.32,.16,.16)
            shield(m,-.28,.69,-.783,.18,"turquoise")
        else:
            works.lens(m,-.13,.29,.54,.36)
            for side in (-1,1):
                x=side*.57
                m.slab("ivory",x,.08,-.14,.26,.49,.61,.76)
                shield(m,x,.85,-.29,.43,"turquoise")
                m.rod("gold",(x,.41,-.12),(x,1.05,-.23),.047)
            if m.lod<2:
                m.slab("turquoise",-.13,.08,-.53,.54,.075,.27,.87)
        return
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
    if m.era:
        works.footing(m)
        if m.era==1:
            # Broad portico is the loom: warp threads drop from its eave to the lower beam.
            works.pagoda(m,-.07,.34,1.30,.81,.81)
            for x in (-.65,.56):
                m.drum("wall",x,.08,-.37,.076,.79)
            m.rooflet(-.045,.87,-.22,1.40,.52,.22,"roof")
            for j in range((8,5,3)[m.lod]):
                x=-.49+.90*j/max(1,(8,5,3)[m.lod]-1)
                m.rod("ivory",(x,.40,-.37),(x,.88,-.37),.018,n=4)
            m.block("turquoise",-.04,.24,-.37,1.04,.19,.041)
            m.rod("timber",(-.64,.23,-.37),(.55,.23,-.37),.049)
            if m.lod==0:
                m.disc(-.63,.73,-.433,.080)
        elif m.era==2:
            works.northlights(m,-.04,.31,1.52,.78,.49,.31,bays=3)
            for x in (-.58,.50):
                m.slab("metal",x,.08,-.37,.14,.48,.31,.88)
            for y,z in ((.46,-.37),(.24,-.52)):
                m.rod("cloth",(-.57,y,z),(.49,y,z),.095)
            m.block("turquoise",-.04,.30,-.44,.95,.08,.23)
            if m.lod<2:
                works.wheel(m,.59,.48,-.53,.186)
        else:
            # Two vertical bobbins feed a high free-standing jacquard frame.
            for x in (-.60,.60):
                if m.lod==2:
                    m.drum("cloth",x,.08,.15,.21,.89,.16,n=5)
                else:
                    m.drum("ivory",x,.08,.15,.24,.14)
                    m.drum("cloth",x,.22,.15,.18,.68,.16)
                    m.drum("turquoise",x,.90,.15,.24,.071)
                    if m.lod==0:
                        for y in (.37,.52,.67):
                            m.drum("turquoise",x,y,.15,.19,.025,n=10)
            works.arch(m,0,-.21,1.42,1.43,.103,.17)
            for i in range((9,6,3)[m.lod]):
                x=-.33+i*.66/((9,6,3)[m.lod]-1)
                m.rod("ivory",(x,.59,-.21),(x,1.31-.35*abs(x),-.21),.018,n=4)
            m.slab("turquoise",0,.08,-.21,.78,.52,.12,.89)
            if m.lod==0:
                for j in range(3):
                    m.block("gold",0,.19+j*.105,-.272,.58,.026,.018)
        return
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
    if m.era==2:
        works.footing(m)
        # Updraught pottery kiln with throat and stoking arch, beside the
        # covered wheel room and its loading trolley of completed vessels.
        works.barrel(m,-.49,.31,.71,.91,.37,.23)
        m.drum("brick",.34,.08,.25,.40,.60,.34)
        m.drum("metal",.34,.68,.25,.34,.29,.15)
        works.spire(m,.34,.25,.15,.24,.97)
        m.block("brick",.34,.08,-.106,.43,.36,.22)
        firemouth(m,.34,.08,-.218,.28,.27)
        works.conveyor(m,-.34,-.45,.60,.51,.25)
        m.jar(-.34,.363,-.45,.16,"turquoise")
        return
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
    if m.era==3 and kind in ("Factory","Foundry"):
        works.footing(m)
        if kind=="Factory":
            # Split clean production bays flank a central pick-and-place cell.
            for x in (-.56,.56):
                works.barrel(m,x,.22,.62,1.11,.38,.29)
            works.conveyor(m,0,-.27,.42,1.07,.34,"metal")
            m.drum("metal",-.51,.08,-.54,.17,.27)
            m.rod("ivory",(-.51,.35,-.54),(-.55,.93,-.32),.081)
            m.rod("turquoise",(-.55,.93,-.32),(.01,.73,-.36),.060)
            m.rod("metal",(.01,.73,-.36),(.01,.51,-.36),.034)
            if m.lod<2:
                m.slab("gold",0,.453,-.61,.17,.057,.17,.72)
        else:
            # Tall tilt-pour crucible on pedestal bearings and a casting track.
            for x in (-.54,.50):
                m.slab("ivory",x,.08,.19,.26,.84,.56,.62)
            m.rod("gold",(-.56,.82,.19),(.52,.82,.19),.083)
            with m.transform(translate=(-.02,.79,.19),rotate_x=-24):
                m.drum("metal",0,-.35,0,.28,.60,.43)
                m.drum("forge",0,.25,0,.34,.025)
                m.slab("ivory",0,.183,-.40,.21,.069,.26,.82)
            works.conveyor(m,-.02,-.43,.57,.68,.27,"metal")
            for z in (-.62,-.30):
                m.slab("forge",-.02,.383,z,.27,.035,.16,.82)
            if m.lod<2:
                works.vessel(m,.56,.55,.18,.47)
        return
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
