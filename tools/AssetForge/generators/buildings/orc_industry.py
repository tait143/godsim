"""Orc production buildings, with working bays, dye looms and furnace lines."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_towncenters import Lodge, generate
from generators.buildings.orc_extraction import wheel

PACKAGE="orc_industry"


def furnace(m,x,z,s,high=False):
    """Sloped refractory belly, bright tap hole, collecting trough, and flared chimney."""
    e=m.era
    h=(.96,1.22,1.40,1.31)[e]+(.24 if high else 0)
    mantle=("wall","fieldstone","brick","plaster")[e]
    with m.transform(translate=(x,0,z),scale=s):
        m.shell(0,0,[(0,.36,"fieldstone"),(.105,.405,mantle),(.50,.35,"trim"),
                     (.57,.356,mantle),(.83,.174,"metal"),(h,.13,"banner"),
                     (h+.053,.167,"metal"),(h+.095,.156,"door")],(12,8,5)[m.lod])
        m.block("trim",0,.125,-.337,.338,.213,.156,.11)
        m.block("forge",0,.14,-.422,.217,.133,.022,.10)
        m.block("metal",0,.024,-.502,.251,.096,.367,.06)
        m.block("forge",0,.112,-.513,.162,.019,.302)
        if m.lod<2:
            for side in (-1,1):
                m.tusk(side*.207,.48,-.14,side,.391)
        if m.lod==0:
            for side in (-1,1):
                m.post("metal",(side*.28,.24,.14),(side*.145,.84,.10),.034,sides=5)
            m.shell(0,0,[(h-.091,.144,"ivory"),(h-.058,.145,"metal")],8)


def shelter(m,x,z,s,w=.84,d=.82):
    e=m.era
    with m.transform(translate=(x,0,z),scale=s):
        h=(.44,.54,.55,.62)[e]
        m.block("fieldstone",0,0,0,w+.035,.071,d+.028,.035)
        m.block(("wall","plaster","brick","glass")[e],0,.071,0,w,h-.07,d,.023)
        profile=("roof","roof_tile","metal","roof")[e]
        m.hip_roof((0,h,0),w+.17,d+.16,(.36,.45,.24,.29)[e],m.col(profile),overhang=0,material=profile)
        m.entry(-w*.13,-d*.50,.067,.25,h*.68,e)
        for side in (-1,1):
            m.tusk(side*w*.40,h-.01,-d*.32,side,.37)
        if m.lod<2:
            for side in (-1,1):
                m.post("timber" if e<2 else "metal",(side*w*.46,.07,-d*.486),(side*w*.46,h,-d*.486),.036)
            m.block("emissive",w*.29,h*.50,-d*.499,.136,.101,.034)
        if m.lod==0:
            m.block("trim",w*.29,h*.46,-d*.518,.165,.032,.052)
            m.flag(-.10,h+.24,.006,.40,.213)


def anvil(m,x,z,s):
    with m.transform(translate=(x,0,z),scale=s):
        m.shell(0,0,[(0,.20,"timber"),(.23,.173,"hide")])
        m.block("metal",0,.226,0,.29,.07,.213,.10)
        m.block("trim",0,.294,0,.172,.105,.140)
        m.block("metal",.012,.387,0,.330,.09,.181)
        m.post("metal",(-.153,.417,0),(-.321,.431,0),.052,0,sides=5)
        if m.lod==0:
            m.post("timber",(.01,.484,-.045),(.15,.499,.058),.014,sides=4)
            m.block("trim",.013,.473,-.044,.070,.041,.051)


def works(m,kind):
    smelt=kind=="Smelter"
    furnace(m,.46,.16,1.12 if smelt else .81)
    shelter(m,-.39,.24,1.0)
    if smelt:
        m.shell(-.27,-.46,[(0,.22,"metal"),(.25,.233,"trim"),(.31,.241,"forge")])
        if m.lod<2:
            m.post("metal",(-.55,.31,-.46),(.014,.31,-.46),.035,sides=5)
        if m.era>=2:
            m.post("metal",(.46,.91,.18),(-.40,.68,.21),.070,sides=6)
    else:
        anvil(m,.35,-.66,1.17)
        if m.lod<2:
            m.shell(-.71,-.49,[(0,.147,"timber"),(.162,.15,"water")],7)


def shield(m,x,y,z,s,paint):
    # Faceted display shield with a steel inner face and a sharply folded painted crest.
    with m.transform(translate=(x,y,z),scale=s):
        loop=[(0,.40),(-.27,.27),(-.245,-.11),(0,-.39),(.245,-.11),(.27,.27)]
        for i,(u,v) in enumerate(loop):
            a,b=loop[(i+1)%6]
            mat=paint if i in (0,3,4) else "ivory"
            m.panel(mat,[(u,v,0),(0,.034,-.093),(a,b,0)])
            m.panel("trim",[(a,b,.052),(0,.034,.052),(u,v,.052)])
            m.panel("metal",[(u,v,0),(a,b,0),(a,b,.052),(u,v,.052)])
        if m.lod<2:
            m.post("gold",(0,.033,-.059),(0,.033,-.125),.070,sides=6)


def armory(m):
    shelter(m,-.22,.23,1.27,w=.97)
    for x in (-.25,.70):
        m.block("timber" if m.era<2 else "metal",x,0,-.46,.086,.62,.108,.08)
    m.block("trim",.227,.435,-.456,1.07,.095,.117)
    for i,x in enumerate((-.16,.20,.56)):
        if m.lod==2 and i==0:
            continue
        shield(m,x,.376,-.546,.64 if i!=1 else .81,"cloth" if i!=1 else "roof")
    if m.lod<2:
        m.drum(-.69,0,-.45,.166,.33)
    if m.era>=2:
        m.block("metal",.64,0,.25,.38,.48,.55,.08)
        m.block("glass" if m.era==3 else "metal",.64,.47,.25,.37,.243,.45,.19)


def loom(m):
    roof={0:"metal",3:"roof"}.get(m.era)
    m.hut(-.426,.31,1.01,roof_material=roof)
    # The loom occupies a separate working bay, leaving the hut door and its approach open.
    with m.transform(translate=(.29,0,.105),rotate_y=25,scale=.83):
        weaving_frame(m)


def weaving_frame(m):
    for x in (-.29,.69):
        m.post("timber" if m.era<2 else "metal",(x,.031,-.52),(x,.92,-.30),.054)
        m.post("trim",(x,.032,-.12),(x,.56,-.39),.042)
    m.post("roof" if m.era==3 else "timber",(-.375,.87,-.316),(.776,.87,-.316),.056)
    m.post("timber",(-.37,.318,-.484),(.77,.318,-.484),.068)
    # Continuous woven strip with a contrasting central lozenge, rolling over the lower beam.
    knots=[(.89,-.343),(.32,-.553),(.202,-.52),(.17,-.455)]
    edges=(-.235,-.042,.452,.645)
    for i,(y,z) in enumerate(knots[:-1]):
        yy,zz=knots[i+1]
        for j,(x,xx) in enumerate(zip(edges,edges[1:])):
            pts=[(xx,y,z),(x,y,z),(x,yy,zz),(xx,yy,zz)]
            m.panel("cloth" if j==1 else "ivory",pts[::-1])
            m.panel("cloth" if j==1 else "ivory",[(u,v,w+.016) for u,v,w in pts])
    diamond=[(.205,.766,-.403),(.356,.60,-.459),(.205,.431,-.522),(.057,.60,-.459)]
    m.panel("gold",diamond)
    if m.lod<2:
        m.tusk(.69,.84,-.316,1,.27)
        for x,mat in ((.47,"cloth"),(.69,"gold")):
            m.shell(x,.13,[(0,.095,"timber"),(.05,.103,mat),(.24,.068,"ivory"),(.27,.09,"trim")],7)


def bench(m):
    shelter(m,-.32,.30,1.19)
    for x in (.0,.65):
        for z in (-.51,-.16):
            m.block("timber",x,0,z,.091,.42,.091,.10)
    m.block("timber",.32,.399,-.34,.84,.085,.50)
    m.block("metal",.51,.473,-.32,.17,.16,.17)
    wheel(m,.46,.703,-.416,.20,"metal")
    if m.lod<2:
        m.block("trim",-.024,.478,-.405,.217,.098,.138)
        m.drum(-.50,0,-.43,.162,.26)
    if m.lod==0:
        m.post("gold",(.22,.50,-.17),(.36,.55,-.35),.022,sides=5)


def saw_roof(m,x,z,w,d,y,teeth):
    # Northlight roofs have a bright vertical pane and an opaque inclined steel face per bay.
    for j in range(teeth):
        span=d/teeth
        cz=z-d/2+(j+.5)*span
        m.wedge((x,y,cz),w,span-.012,.175,m.col("metal"),material="metal")
        m.block("glass",x,y+.01,cz+span*.50-.008,w*.95,.143,.027)
        if m.lod==0:
            m.block("trim",x,y+.151,cz+span*.5-.009,w*.98,.03,.046)


def factory(m,electronic):
    e=m.era
    base="glass" if e==3 else "brick"
    m.block("fieldstone",-.10,0,.09,1.43,.11,1.35,.04)
    m.block(base,-.10,.105,.09,1.37,.514,1.30,.018)
    m.entry(-.29,-.575,.07,.38,.397,e)
    m.block("trim",-.10,.613,.09,1.47,.088,1.38)
    if electronic:
        # A finned clean-room core rises from a glazed production podium.
        for i in range(4):
            y=.696+i*.145
            m.block("glass",-.17,y,.12,.80-.044*i,.13,.83-.045*i)
            m.block("ivory",-.17,y+.112,.12,.91-.045*i,.046,.94-.048*i,.035)
        for side in (-1,1):
            m.tusk(-.17+side*.21,1.24,.13,side,.47)
        if m.lod<2:
            m.shell(.62,-.41,[(0,.17,"metal"),(.39,.16,"turquoise"),(.49,.149,"metal")])
    elif e==3:
        # A glazed process gallery and two broad recuperator cowls replace the old sawtooth bays.
        m.block("glass",-.38,.697,.105,.67,.235,1.13,.105)
        with m.transform(translate=(-.38,.924,.105),rotate_y=90):
            m.hip_roof((0,0,0),1.22,.74,.142,m.col("roof"),overhang=0,material="roof")
        for z,h in ((-.285,.995),(.37,1.19)):
            m.shell(.293,z,[(.70,.157,"metal"),(h-.054,.136,"metal"),(h+.056,.212,"banner"),
                            (h+.096,.210,"metal"),(h+.16,.183,"door")],(10,7,4)[m.lod])
        for side in (-1,1):
            m.tusk(-.21+side*.215,.87,-.368,side,.56)
    else:
        saw_roof(m,-.15,.12,1.32,1.15,.698,2 if m.lod==2 else 3)
        for x,z,h in ((.49,.51,1.34),(.18,.62,1.13)):
            m.shell(x,z,[(.70,.122,"brick" if e==2 else "metal"),(h,.088,"cloth"),
                         (h+.057,.097,"metal"),(h+.10,.097,"door")],(10,7,5)[m.lod])
        for s in (-1,1):
            m.tusk(-.28+s*.26,.54,-.612,s,.62)
    if m.lod<2:
        for j in range(3):
            m.block("roof",.10+j*.18,.25,-.552,.138,.19,.072)
        m.drum(-.60,0,-.67,.147,.26)
    if m.lod==0:
        if not electronic and e==2:
            m.flag(-.55,.692,-.473,.56,.25)
        elif not electronic:
            m.flag(-.38,1.048,.125,.49,.285)
        else:
            m.flag(-.61,.63,.31,.44,.218)


def foundry(m):
    furnace(m,-.41,.29,1.10,high=True)
    furnace(m,.29,.44,.78)
    shelter(m,.47,-.23,.78)
    # Crane suspended from a ground-mounted post aims at the mobile pour ladle.
    m.post("metal",(-.73,0,-.45),(-.73,.87,-.45),.068)
    m.post("metal",(-.79,.83,-.45),(-.06,.93,-.45),.055)
    m.post("metal",(-.73,.50,-.45),(-.23,.90,-.45),.036)
    m.post("trim",(-.10,.40,-.45),(-.10,.932,-.45),.021,sides=4)
    m.shell(-.10,-.45,[(.045,.167,"metal"),(.29,.224,"trim"),(.35,.222,"forge")])
    for side in (-1,1):
        m.post("metal",(-.10+side*.196,.28,-.45),(-.10,.41,-.45),.019,sides=4)
    if m.lod<2:
        m.drum(-.66,0,-.72,.139,.19)


def build(spec,lod):
    m=Lodge(spec,lod)
    w,d=spec.footprint
    name=spec.building_type
    with m.transform(scale=(w*.5,min(w,d)*.5 if w==d else .77,d*.5)):
        if name in ("Blacksmith","Smelter"):
            works(m,name)
        elif name=="Armory":
            armory(m)
        elif name=="Weaver":
            loom(m)
        elif name=="Workshop":
            bench(m)
        elif name=="Foundry":
            foundry(m)
        else:
            factory(m,name=="ElectronicsPlant")
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
