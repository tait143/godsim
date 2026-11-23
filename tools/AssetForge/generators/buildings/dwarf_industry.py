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
    m.slab("fieldstone",0,0,0,1.86,.103,1.69,.10)
    m.hearth(-.32,.099,.31,(1.01,1.16,.98),roof_material="roof" if e==0 else None)
    x,z=.37,-.38
    mat="timber" if e<2 else "metal"
    for s in (-1,1):
        m.block(mat,x+s*.318,.104,z,.089,.72,.114)
        m.block("trim",x+s*.318,.104,z,.127,.075,.40)
    m.block(mat,x,.765,z,.77,.084,.10)
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
    if e>=2:
        m.block("metal",.74,.104,-.02,.187,.41,.26)
        m.beam("accent",(.74,.405,-.05),(.69,.41,z),.047)


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
        sheds(m)
        for x in (-.49,.03):
            m.stack(x,.571,.463,.137,(.59 if x<0 else .45),"brick" if e==2 else "metal")
        if e==3:
            m.block("trim",.40,.556,.03,.44,.190,.31)
            m.vault("glass",.40,.743,.03,.50,.111,.35)
    elif kind=="Foundry":
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
        if e==3:
            m.block("trim",-.45,1.110,.32,.41,.063,.53)
            m.slab("glass",-.45,1.168,.32,.39,.102,.50,.11)
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
