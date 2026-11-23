"""Clan craft halls and heavier industry. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def kiln(m,x,z,r,h):
    """Battered smelting pot with a clinker arch, glowing throat and metal tap."""
    e=m.era
    m.drum("fieldstone",x,.105,z,r*1.05,.136)
    m.drum("brick" if e<3 else "wall",x,.234,z,r,h*.52,r*.82)
    m.drum("roof",x,.23+h*.52,z,r*.84,h*.27,r*.50)
    m.stack(x,.222+h*.79,z,r*.50,h*.30,"brick" if e<3 else "metal")
    with m.transform(translate=(x,.109,z-r*.84),scale=r):
        m.vault("trim",0,0,0,1.28,.76,.50)
        m.relief("forge",[(-.43,.05),(.43,.05),(.40,.37),(.21,.55),(-.21,.55),(-.40,.37)],-.261,.035)
        if m.lod<2:
            for t in (-.22,.16): m.block("door",t,.061,-.281,.061,.33,.051)
    if m.lod==0:
        m.drum("accent",x,.28+h*.23,z,r*.96,.038)
        m.drum("trim",x,.20+h*.57,z,r*.85,.045)


def ingots(m,x,y,z):
    for i in range(3 if m.lod==0 else 1):
        m.block("metal",x+(i%2)*.15,y+(i//2)*.072,z+i*.012,.141,.075,.252,taper=.26)


def furnace(m):
    m.slab("fieldstone",0,0,.01,1.86,.119,1.66,.12)
    m.hearth(-.41,.114,.21,(.91,1.14,1.05),roof_material="roof" if m.era==0 else None)
    kiln(m,.41,.17,.377,(.83,.93,1.04,.91)[m.era])
    m.block("trim",.41,.114,-.44,.24,.10,.49)
    m.block("forge",.41,.210,-.438,.137,.018,.45)
    if m.lod<2:
        ingots(m,-.59,.12,-.52)
        m.slab("wall",.02,.114,-.56,.29,.17,.34,.10)
    if m.era>=2:
        m.beam("metal",(-.43,.59,.26),(.39,.70,.28),.117)
    if m.lod==0: m.lamp(-.78,.116,.05,.113)


def anvil(m,x,y,z,scale):
    with m.transform(translate=(x,y,z),scale=scale):
        m.drum("timber",0,0,0,.28,.25,top=.254)
        # Two convex forgings form the horn, crown, narrow waist and flared feet.
        m.relief("metal",[(-.30,.246),(.29,.246),(.17,.365),(-.15,.365)],-.138,.276)
        m.relief("metal",[(-.147,.36),(.169,.36),(.288,.506),(-.29,.506)],-.126,.252)
        m.relief("trim",[(-.32,.50),(.27,.50),(.30,.615),(-.32,.615)],-.145,.29)
        m.relief("metal",[(-.324,.50),(-.316,.612),(-.56,.59)],-.09,.18)
        if m.lod==0:
            m.drum("trim",0,.073,0,.28,.045)


def blacksmith(m):
    e=m.era
    m.slab("fieldstone",0,0,.03,1.88,.112,1.63,.12)
    m.hearth(-.35,.109,.33,(1.05,1.13,.95),roof_material="roof" if e==0 else None)
    kiln(m,.48,.43,.268,(.69,.79,.87,.77)[e])
    anvil(m,.28,.111,-.38,.92)
    if m.lod<2:
        m.drum("timber",-.64,.112,-.46,.162,.202)
        m.drum("water",-.64,.312,-.46,.132,.011)
    if m.lod==0:
        m.beam("timber",(.29,.697,-.39),(.44,.693,-.54),.040)
        m.block("metal",.28,.673,-.38,.121,.064,.068)


def shield(m,x,y,z,w,h,mat):
    def face(s,shift,col):
        a=w*s*.5
        b=h*s
        m.relief(col,[(x,y+.01+(h-b)*.5),(x+a,y+.01+(h-b)*.5+b*.39),
                      (x+a*.92,y+.01+(h-b)*.5+b),(x-a*.92,y+.01+(h-b)*.5+b),
                      (x-a,y+.01+(h-b)*.5+b*.39)],z+shift,.049)
    face(1,0,"accent")
    face(.80,-.013,mat)
    if m.lod<2:
        m.rune(x,y+h*.57,z-.036,w*.48)


def armory(m):
    e=m.era
    m.slab("fieldstone",0,0,.04,1.87,.134,1.60,.13)
    m.hearth(0,.13,.12,(1.54,1.24,1.29),roof_material="roof" if e==0 else None)
    # Standards sit on two gate piers; broad, pointed shields identify the clan armory.
    for s in (-1,1):
        m.slab("wall",s*.60,.13,-.36,.382,.58,.44,.13,.10)
        m.slab("trim",s*.60,.705,-.36,.411,.083,.472)
        shield(m,s*.60,.238,-.596,.356,.415,"banner" if e<2 else "metal")
    crest_base=.13+1.24*((.47,.54,.52,.55)[e]-.015+(.264,.21,.145,.18)[e])
    m.block("trim",0,crest_base-.012,.15,.80,.082,.73)
    m.vault("roof",0,crest_base+.066,.15,.89,.167,.80)
    if m.lod==0:
        for s in (-1,1):
            m.lamp(s*.60,.787,-.529,.092)
    if e>=2: m.block("glass",0,crest_base+.225,.15,.40,.098,.55)


def loom(m):
    e=m.era
    if e == 1:
        return dyers_guild(m)
    if e == 2:
        return power_loom(m)
    if e == 3:
        return filament_loom(m)
    m.slab("fieldstone",0,0,0,1.86,.103,1.69,.10)
    m.hearth(-.32,.099,.31,(1.01,1.16,.98),roof_material="roof")
    x,z=.37,-.38
    for s in (-1,1):
        m.block("timber",x+s*.318,.104,z,.089,.72,.114)
        m.block("trim",x+s*.318,.104,z,.127,.075,.40)
    m.block("timber",x,.765,z,.77,.084,.10)
    # The warp hangs from the breast beam and rolls back over the front cloth beam.
    m.block("banner",x,.307,z,.564,.310,.044)
    for j in range((7,5,3)[m.lod]):
        dx=(j/((7,5,3)[m.lod]-1)-.5)*.51
        m.block("ivory",x+dx,.609,z,.020,.165,.019)
    m.block("trim",x,.284,z,.691,.070,.127)
    m.block("cloth",x,.141,z-.056,.48,.169,.058)
    if m.lod<2:
        for j in range(3):
            dx=(j-1)*.146
            m.rune(x+dx,.483,z-.032,.132,"gold")
        m.block("ivory",x,.560,z-.023,.556,.031,.026)
    if m.lod==0:
        for i in range(3):
            sx=-.70+i*.17
            m.drum("trim",sx,.104,-.50,.082,.031)
            m.drum(("ivory","banner","gold")[i],sx,.131,-.50,.065,.15,top=.058)
            m.drum("trim",sx,.28,-.50,.078,.023)


def roll(m, material, x, y, z, radius, width):
    """Cloth or sheet gathered onto a horizontal spindle between its bearings."""
    with m.transform(translate=(x-width/2,y,z),rotate_z=-90):
        m.drum(material,0,0,0,radius,width,n=(12,7,4)[m.lod])
        if m.lod == 0:
            for along in (-.017,width-.002):
                m.drum("trim",0,along,0,radius*1.11,.022,n=10)


def dyers_guild(m):
    m.slab("fieldstone",0,0,.02,1.83,.095,1.65)
    m.slab("wall",0,.088,.37,1.54,.50,.79,.085)
    m.hip_roof((0,.581,.37),1.57,.83,.38,m.color("roof_tile"),overhang=.035,material="roof_tile")
    m.entry(.46,.095,-.032,.36,.42)
    # Finished lengths hang over a covered drying rail above the dye vats.
    for x in (-.70,.19):
        m.block("timber",x,.093,-.415,.082,.66,.084)
    m.block("trim",-.255,.733,-.415,.985,.069,.103)
    for i,mat in enumerate(("cloth","banner","ivory")):
        x=-.53+i*.272
        height=(.38,.46,.33)[i]
        m.block(mat,x,.763-height,-.471,.212,height,.036)
        if m.lod==0:
            m.rune(x,.797-height,-.493,.125)
    for x,z in ((-.47,-.57),(.41,-.43))[:1 if m.lod==2 else 2]:
        m.drum("timber",x,.096,z,.199,.19,.204)
        m.drum("banner",x,.283,z,.165,.015)
        if m.lod==0:
            m.drum("metal",x,.144,z,.202,.032)
            m.beam("timber",(x-.07,.26,z),(x+.14,.46,z+.05),.039)


def power_loom(m):
    m.slab("fieldstone",0,0,.01,1.89,.12,1.52,.07)
    m.slab("brick",-.12,.114,.38,1.40,.51,.59,.09)
    m.wedge((-.12,.617,.38),1.49,.69,.23,m.color("metal"),material="metal")
    m.stack(.61,.120,.42,.168,.94,"brick")
    m.block("glass",-.14,.609,.708,.91,.15,.04)
    # A cast-iron power loom: triangular end frames hold two beams and the beating reed.
    for x in (-.55,.38):
        m.slab("wall",x,.117,-.30,.185,.38,.69,.12,.22)
        m.beam("accent",(x,.417,-.48),(x,.78,-.13),.084)
    roll(m,"banner",-.085,.442,-.493,.139,1.01)
    roll(m,"timber",-.085,.768,-.126,.084,1.01)
    with m.transform(translate=(-.085,.60,-.31),rotate_x=-47):
        m.block("cloth",0,-.021,0,.83,.042,.44)
    if m.lod<2:
        with m.transform(translate=(.50,.374,-.29),rotate_y=90):
            m.wheel(0,0,0,.235,"metal",4)
    if m.lod==0:
        for x in (-.41,-.24,-.07,.10,.27):
            m.beam("ivory",(x,.487,-.48),(x,.759,-.134),.021)
        m.entry(-.50,.126,.078,.32,.39)


def filament_loom(m):
    m.slab("wall",0,0,.035,1.67,.15,1.74,.21)
    # Three cartridges feed a broad translating print head over the take-up roll.
    for x in (-.50,0,.50):
        m.drum("trim",x,.145,.43,.189,.102)
        m.drum("ivory" if x else "banner",x,.239,.43,.151,.51,.132)
        m.drum("metal",x,.741,.43,.192,.085,.128)
        if m.lod==0:
            for y in (.315,.419,.523,.627):
                m.drum("accent",x,y,.43,.156-(y-.30)*.04,.022,n=8)
    for x in (-.63,.63):
        m.slab("wall",x,.144,-.19,.23,.70,.61,.14,.20)
    m.vault("metal",0,.806,-.16,1.53,.24,.52)
    m.slab("glass",-.08,.986,-.125,.66,.17,.34)
    m.block("banner",0,.422,-.299,.92,.365,.067)
    roll(m,"cloth",0,.416,-.38,.139,1.14)
    if m.lod<2:
        for x in (-.31,0,.31):
            m.block("ascension",x,.511,-.336,.032,.238,.016)
    if m.lod==0:
        m.rune(-.08,.942,-.433,.195)


def assembly_works(m):
    m.slab("fieldstone",0,0,.012,1.90,.153,1.69,.20)
    m.slab("wall",-.04,.147,.36,1.57,.51,.85,.20,.07)
    m.slab("glass",-.04,.652,.36,1.49,.175,.79,.19)
    m.slab("ivory",-.04,.819,.36,1.65,.12,.91,.23,.05)
    m.vault("metal",-.37,.935,.39,.59,.251,.67)
    m.entry(.38,.153,-.066,.41,.39)
    m.slab("metal",.05,.149,-.50,1.54,.18,.40,.16)
    m.block("door",.05,.321,-.50,1.34,.034,.29)
    # Opposed articulated pick-and-place arms reach over a conveyor.
    for s in (-1,1):
        x=s*.60
        m.drum("trim",x,.151,-.225,.16,.276,.125)
        base=(x,.419,-.225)
        joint=(s*.71,.781,-.32)
        wrist=(s*.38,.70,-.51)
        m.beam("accent",base,joint,.143)
        m.beam("ivory",joint,wrist,.106)
        m.block("metal",wrist[0],.551,wrist[2],.092,.15,.089)
        if m.lod<2:
            roll(m,"metal",joint[0],joint[1],joint[2],.088,.167)
            m.block("trim",wrist[0],.514,wrist[2],.164,.053,.081)
    if m.lod==0:
        for x in (-.43,.03,.44):
            m.slab("metal",x,.351,-.495,.21,.113,.19,.16,.20)
        m.rune(-.37,1.065,.041,.171)


def arc_foundry(m):
    m.slab("fieldstone",0,0,0,1.91,.138,1.72,.17)
    # Three electrodes descend from a cantilever over the open refractory vessel.
    m.drum("wall",-.18,.132,-.116,.42,.22,.49)
    m.drum("metal",-.18,.348,-.116,.49,.292,.442)
    m.drum("trim",-.18,.635,-.116,.468,.106,.48)
    m.drum("forge",-.18,.735,-.116,.406,.014)
    m.slab("wall",.593,.132,.22,.40,.91,.73,.17,.14)
    m.slab("glass",.593,1.037,.22,.35,.14,.63,.14)
    m.slab("ivory",.22,1.167,.065,1.13,.158,.45,.14)
    for x,z in ((-.34,-.10),(-.08,-.12),(-.19,.13)):
        m.drum("trim",x,.786,z,.062,.412,.071)
        if m.lod==0:
            m.drum("accent",x,1.049,z,.089,.064)
    m.block("trim",.035,.158,-.639,.50,.18,.295)
    m.beam("metal",(-.18,.608,-.48),(.034,.345,-.615),.145)
    if m.lod<2:
        ingots(m,.30,.14,-.58)
        m.beam("forge",(-.18,.638,-.50),(.034,.364,-.64),.069)
    if m.lod==0:
        m.rune(.594,.66,-.154,.209)
        for z in (.39,.58):
            m.slab("metal",-.43,.137,z,.69,.173,.15)


def workshop(m):
    m.slab("fieldstone",0,0,0,1.81,.116,1.69,.14)
    m.hearth(-.27,.113,.25,(1.12,1.36,1.15))
    # The drill press has a worktable, a belt flywheel, bearings and a visible quill.
    x,z=.49,-.33
    m.slab("trim",x,.113,z,.53,.128,.47,.12)
    m.block("wall",x+.151,.235,z,.16,.69,.23)
    m.block("metal",x-.023,.837,z,.51,.147,.274)
    m.block("trim",x-.136,.476,z,.122,.374,.10)
    m.block("timber",x-.096,.36,z,.372,.084,.261)
    m.block("metal",x-.096,.238,z,.141,.13,.147)
    m.wheel(x+.079,.91,z-.15,.206,"accent",4 if m.lod==2 else 6)
    if m.lod==0:
        ingots(m,-.61,.114,-.54)
        m.lamp(.13,.117,.59,.099)
    if m.era==2: m.stack(-.53,.599,.41,.114,.56,"brick")


def sheds(m):
    e=m.era
    m.slab("fieldstone",0,0,0,1.88,.12,1.76,.11)
    m.slab("brick" if e==2 else "wall",0,.116,.04,1.55,.426,1.36,.06)
    for i in range(3):
        z=-.416+i*.46
        m.wedge((0,.54,z),1.63,.46,.204,m.color("roof"),material="roof")
        if m.lod<2:
            m.block("glass",0,.565,z+.233,1.31,.148,.026)
    m.entry(-.31,.117,-.646,.44,.375)
    if m.lod<2:
        for s in (-1,1):
            for z in (-.34,.26):
                m.block("trim",s*.779,.19,z,.040,.26,.29)
                m.block("glass",s*.803,.218,z,.021,.184,.235)
    if m.lod==0:
        for x in (.16,.39,.62):
            m.block("metal",x,.247,-.65,.128,.139,.031)
        m.lamp(-.62,.12,-.59,.103)


def large(m,kind):
    e=m.era
    if kind=="Factory":
        if e == 3:
            return assembly_works(m)
        sheds(m)
        for x in (-.49,.03):
            m.stack(x,.571,.463,.137,(.59 if x<0 else .45),"brick")
    elif kind=="Foundry":
        if e == 3:
            return arc_foundry(m)
        m.slab("fieldstone",0,0,0,1.88,.115,1.76,.12)
        m.hearth(-.45,.111,.32,(.81,1.41,1.06))
        kiln(m,.376,.33,.41,1.02)
        # Tapping ladle in its trunnion fork and channel below the furnace mouth.
        x,z=.27,-.409
        m.drum("trim",x,.144,z,.177,.322,top=.346)
        m.drum("forge",x,.461,z,.295,.014)
        for s in (-1,1):
            m.block("metal",x+s*.33,.113,z,.095,.298,.16)
            m.beam("accent",(x+s*.184,.343,z),(x+s*.34,.343,z),.071)
        # A dedicated tap above the tuyere pours downslope into the ladle.
        m.block("trim",.376,.564,-.019,.185,.118,.127)
        m.beam("trim",(.376,.61,-.032),(.291,.512,-.284),.102,.116)
        m.beam("forge",(.376,.628,-.043),(.289,.529,-.287),.056,.071)
        if m.lod<2: ingots(m,-.67,.113,-.39)
        if m.lod==0: m.lamp(-.09,.116,-.67,.111)
    else:
        m.slab("fieldstone",0,0,0,1.85,.12,1.73,.14)
        m.slab("wall",0,.116,.04,1.59,.40,1.38,.15)
        m.slab("glass",0,.51,.04,1.62,.149,1.41,.15)
        m.slab("ivory",0,.656,.04,1.68,.086,1.47,.15)
        m.entry(0,.121,-.653,.46,.362)
        # The capped ceramic insulators flank a dark, ribbed processing chamber.
        m.vault("metal",0,.737,.10,.73,.28,.82)
        for x in (-.55,.55):
            for z in (-.33,.35):
                m.drum("trim",x,.739,z,.074,.262)
                for i in range(3 if m.lod<2 else 1):
                    m.drum("turquoise",x,.78+i*.07,z,.107,.039,top=.066)
        if m.lod<2:
            for x in (-.59,.59):
                m.beam("accent",(x,.745,-.25),(x,.745,.39),.029)
        if m.lod==0:
            for z in (-.16,.08,.32): m.block("trim",0,.997,z,.407,.046,.062)
            m.lamp(.48,.119,-.636,.11)


def build(spec,lod):
    m=Stonework(spec,lod)
    w,d=spec.footprint
    with m.transform(scale=(w/2,(.58*w+.42*d)/2,d/2)):
        kind=spec.building_type
        if kind=="Smelter": furnace(m)
        elif kind=="Armory": armory(m)
        elif kind=="Blacksmith": blacksmith(m)
        elif kind=="Weaver": loom(m)
        elif kind=="Workshop": workshop(m)
        else: large(m,kind)
    return m


if __name__=="__main__":
    publish_package("dwarf_industry",build)
