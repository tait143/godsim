"""Hillside kitchens, terrace farms and water-driven mills. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def cask(m,x,y,z,r,h,mat="timber"):
    m.drum(mat,x,y,z,r*.8,h*.44,r)
    m.drum(mat,x,y+h*.438,z,r,h*.56,r*.84)
    if m.lod==0:
        for t in (.19,.73):
            m.drum("trim",x,y+h*t,z,r*.97,h*.082)


def mushroom(m,x,y,z,r,cap):
    m.drum("ivory",x,y,z,r*.30,r*.89,top=r*.22,n=(7,5,4)[m.lod])
    m.dome((x,y+r*.88,z),r,m.color(cap),rings=(2,1,1)[m.lod],
           segments=(8,6,4)[m.lod],squash=.62,material=cap)
    if m.lod==0:
        m.drum("ivory",x+r*.31,y+r*1.35,z-r*.19,r*.13,r*.022)


def fisher(m):
    e=m.era
    m.slab("fieldstone",-.27,0,.04,1.16,.16,1.36,.14)
    m.hearth(-.28,.156,.26,1.04,roof_material="roof" if e==0 else None)
    # A blackwater dory on its slip. The interior is modeled down to the bilge.
    x,z=.55,.04
    outline=[(0,-.83),(.21,-.49),(.255,.21),(.17,.59),(0,.73),(-.17,.59),(-.255,.21),(-.21,-.49)]
    if m.lod==2: outline=[outline[i] for i in (0,2,4,6)]
    def ring(sx,sz,h,bend):
        return [(x+u*sx,h+abs(v)**2*bend,z+v*sz) for u,v in outline]
    keel,rail,inner,floor=ring(.62,.88,.008,.031),ring(1,1,.225,.21),ring(.80,.92,.227,.21),ring(.52,.75,.09,.031)
    for i in range(len(rail)):
        j=(i+1)%len(rail)
        m.face(("timber","roof","metal","metal")[e],(keel[j],keel[i],rail[i],rail[j]))
        m.face("gold",(rail[j],rail[i],inner[i],inner[j]))
        m.face("timber",(inner[j],inner[i],floor[i],floor[j]))
    m.face("timber",floor[::-1])
    if m.lod<2:
        for t in (-.29,.22):
            m.block("trim",x,.202,z+t,.40,.043,.085)
        m.beam("timber",(x-.11,.27,z-.3),(x+.12,.35,z+.51),.037)
        m.block("timber",x+.11,.33,z+.48,.087,.039,.18)
    m.block("timber",-.26,.153,-.60,1.06,.078,.31)
    if m.lod<2:
        for sx in (-.68,.16):
            m.drum("timber",sx,0,-.64,.056,.31)
        m.beam("trim",(-.69,.18,-.36),(-.69,.67,-.36),.045)
        m.beam("trim",(-.69,.64,-.36),(-.45,.64,-.36),.041)
        m.beam("gold",(-.46,.63,-.36),(-.46,.40,-.36),.015)
    if m.lod==0: cask(m,.085,.235,-.59,.116,.19)


def woodland(m,hunt):
    m.hearth(.04,0,.19,(1.46,1.32,1.43),roof_material="roof" if m.era==0 else None)
    if hunt:
        # A pegged frame with a scalloped curing hide left of the lodge door.
        for x in (-.81,-.32):
            m.block("timber",x,0,-.60,.065,.71,.076)
        m.block("trim",-.565,.62,-.60,.58,.073,.073)
        m.relief("hide",[(-.75,.64),(-.72,.37),(-.62,.22),(-.46,.34),(-.40,.64)],-.633,.030)
        m.relief("trim",[(-.12,.77),(.04,.70),(.20,.77),(.20,.929),(-.12,.929)],-.469,.077)
        for s in (-1,1):
            m.beam("ivory",(.04,.819,-.49),(s*.19+.04,.946,-.487),.065)
            m.beam("ivory",(s*.18+.04,.94,-.487),(s*.30+.04,1.13,-.478),.051)
            if m.lod<2:
                m.beam("ivory",(s*.174+.04,.93,-.487),(s*.12+.04,1.05,-.478),.041)
        if m.lod==0: cask(m,.53,0,-.54,.139,.233)
    else:
        # Cave mushrooms dry on two stumps beside the rooted fruiting bed.
        for x,z,r in ((-.59,-.49,.21),(.48,-.59,.16)):
            m.drum("timber",x,0,z,r,.124,top=r*.89)
            mushroom(m,x,.12,z,r*.87,"roof_tile")
        if m.lod<2:
            mushroom(m,-.75,0,-.25,.125,"banner")
            mushroom(m,.67,0,-.40,.126,"roof_tile")
        if m.lod<2:
            cask(m,-.29,0,-.64,.084,.12,"hide")


def terrace(m):
    e=m.era
    m.slab("fieldstone",0,0,0,1.88,.095,1.84,.07)
    m.hearth(-.43,.09,.48,(.88,.86,.86),roof_material="roof" if e==0 else None)
    for row in range(3):
        z=-.71+row*.30
        y=.091+row*.026
        m.block("wall",.02,.085,z,1.62,y-.085+.058,.278)
        m.block("soil",.02,y+.054,z,1.55,.019,.24)
        count=(6,4,2)[m.lod]
        for j in range(count):
            x=-.72+1.48*(j+.5)/count
            m.vault("thatch" if row==1 else "leaf",x,y+.07,z,1.42/count,
                    .109+(.025 if row==1 else 0),.173)
        if m.lod<2:
            m.block("water",-.794,.092,z,.045,.029,.286)
    if e<2:
        # A stone-lined header tank feeds the three terraces.
        m.drum("wall",.50,.09,.46,.268,.25)
        m.drum("water",.50,.339,.46,.230,.008)
        if m.lod<2:
            m.block("trim",.50,.328,.46,.484,.055,.063)
            m.beam("timber",(.50,.367,.46),(.50,.65,.46),.043)
            m.beam("trim",(.34,.60,.46),(.70,.66,.46),.049)
        if m.lod==0: cask(m,.78,.092,.11,.084,.15)
    else:
        m.drum("metal",.53,.094,.47,.186,.37)
        m.drum("roof",.53,.46,.47,.203,.135,top=.064)
        if m.lod==0: m.drum("gold",.53,.32,.47,.191,.036)
        if e==3 and m.lod<2:
            m.block("trim",-.43,.704,.48,.43,.105,.46)
            m.vault("glass",-.43,.805,.48,.48,.11,.50)


def growing_house(m):
    # The clan grows mushrooms in the heated stone basement and vegetables under stepped glass.
    future = m.era==3
    m.slab("fieldstone",0,0,0,1.89,.115,1.80,.11)
    m.slab("wall",0,.105,.14,1.60,.420,1.42,.055)
    m.entry(0,.115,-.578,.37,.34)
    for side in (-1,1):
        m.slab("trim",side*.756,.115,-.42,.127,.432,.22,.15,.08)
    for i in range(2 if future else 3):
        count = 2 if future else 3
        depth = 1.4/count
        z = -.56+depth*(i+.5)
        h = (.325+.240*i if future else .247+.085*i)
        m.wedge((0,.525,z),1.57,depth-.012,h,m.color("glass"),material="glass")
        m.block("metal",0,.524+h-.010,z+depth*.5-.014,1.60,.028,.036)
        if m.lod<2:
            for x in ((-.52,-.26,0,.26,.52) if m.lod==0 else (-.43,.43)):
                m.beam("metal",(x,.529,z-depth*.5+.010),(x,.522+h,z+depth*.5-.014),.023)
    # Open sample trays and their outsized fruiting caps identify the building as a food producer.
    for side in (-1,1):
        x=side*.47
        m.slab("timber",x,.115,-.695,.48,.094,.213,.09)
        m.block("soil",x,.209,-.695,.427,.025,.163)
        for j in (range(3) if m.lod==0 else range(2) if m.lod==1 else range(1)):
            cx=x+(.13*(j-1) if m.lod==0 else .095*(2*j-1) if m.lod==1 else 0)
            mushroom(m,cx,.234,-.690,.062 if m.lod==0 else .073,"roof_tile" if side>0 else "thatch")
    if future:
        m.stack(.665,.526,.576,.094,.699,"metal")
        if m.lod<2:
            m.drum("turquoise",.665,1.175,.576,.095,.090,top=.075)
    else:
        m.stack(-.65,.526,.65,.102,.621,"brick")
    if m.lod==0:
        for x in (-.566,.566):
            m.rune(x,.400,-.579,.094,"gold")


def ram(m,x,z,turn):
    with m.transform(translate=(x,.061,z),rotate_y=turn):
        for b in (-1,1):
            if m.lod==2:
                m.block("trim",0,0,b*.104,.142,.139,.049)
            else:
                for a in (-1,1):
                    m.block("trim",a*.061,0,b*.104,.048,.139,.049)
        m.slab("ivory",0,.105,0,.223,.137,.36,.22,.10)
        m.block("trim",0,.156,-.178,.134,.098,.128,taper=.15)
        if m.lod<2:
            for s in (-1,1):
                m.beam("gold",(s*.048,.234,-.154),(s*.106,.289,-.096),.044)


def pasture(m):
    if m.era == 1:
        return stone_fold(m)
    if m.era == 2:
        return stockyard(m)
    if m.era == 3:
        return canopy_pasture(m)
    m.slab("grass",0,0,0,1.87,.065,1.84,.09)
    m.hearth(-.36,.06,.49,(1.02,.94,.89),roof_material="roof" if m.era==0 else None)
    for a,b in (((-.83,-.80),(-.31,-.80)),((.11,-.80),(.83,-.80)),
                ((.83,-.80),(.83,.80)),((.83,.80),(-.83,.80)),((-.83,.80),(-.83,-.80))):
        dx,dz=b[0]-a[0],b[1]-a[1]
        # The side rails butt against the inside faces of the front and rear rails.
        ax,az=a
        bx,bz=b
        half_rail=.0205
        if dz:
            az+=math.copysign(half_rail,dz)
            bz-=math.copysign(half_rail,dz)
        else:
            if abs(ax)==.83: ax+=math.copysign(half_rail,ax)
            if abs(bx)==.83: bx+=math.copysign(half_rail,bx)
        for y in ((.15,.265) if m.lod==0 else (.22,)):
            m.beam("timber" if m.era<2 else "metal",(ax,y,az),(bx,y,bz),2*half_rail)
        for f in ((.04,.50) if m.lod<2 and abs(dx)+abs(dz)>1 else (.06,)):
            m.block("trim",a[0]+f*dx,.05,a[1]+f*dz,.064,.271,.064)
    for x in (-.309,.105):
        m.block("fieldstone",x,.058,-.80,.075,.292,.082)
    m.block("timber",-.103,.126,-.80,.352,.129,.044)
    if m.lod<2:
        m.beam("accent",(-.267,.126,-.83),(.053,.257,-.83),.033)
    for x,z,ang in ((.38,-.34,35),(-.37,-.43,-43))[:1 if m.lod==2 else 2]:
        ram(m,x,z,ang)
    if m.lod==0:
        m.slab("wall",.50,.061,.18,.42,.13,.191,.12)
        m.block("water",.50,.188,.18,.34,.009,.115)


def stone_fold(m):
    m.slab("grass",0,0,.01,1.81,.063,1.78,.18)
    # A dry-stone fold shares its rear wall with a shepherd's corbel-roofed watch hut.
    for x in (-.716,.716):
        m.slab("fieldstone",x,.059,.03,.215,.255,1.30,.08,.14)
    m.slab("wall",-.13,.061,.642,1.17,.293,.19,.085,.10)
    m.slab("fieldstone",-.486,.057,-.626,.58,.24,.198,.10,.10)
    m.slab("fieldstone",.519,.0615,-.654,.45,.22,.18,.12,.13)
    m.drum("wall",-.412,.057,.384,.389,.45,.359,n=(10,7,5)[m.lod])
    m.drum("slate",-.412,.503,.384,.402,.173,.306,n=(10,7,5)[m.lod])
    m.drum("roof",-.412,.671,.384,.314,.209,.139,n=(10,7,5)[m.lod])
    m.entry(-.411,.065,.035,.296,.33)
    ram(m,.27,-.25,54)
    if m.lod<2:
        ram(m,-.30,-.28,-31)
    if m.lod==0:
        for x in (-.72,.72):
            for z in (-.41,-.02,.37):
                m.block("slate",x,.308,z,.156,.037,.19,taper=.12)
        m.block("timber",.052,.102,-.645,.49,.145,.047)
        m.rune(-.412,.565,-.003,.123)


def stockyard(m):
    m.slab("grass",0,0,.02,1.89,.062,1.71,.09)
    # A riveted header tank gravity-feeds the covered trough along the stock lane.
    for x in (-.67,-.27):
        for z in ((.25,.63) if m.lod<2 else (.44,)):
            m.beam("metal",(x,.06,z),(-.47+(x+.47)*.73,.705,.44+(z-.44)*.75),.071)
    m.drum("trim",-.47,.69,.44,.329,.092,.31)
    m.drum("metal",-.47,.779,.44,.308,.34,.301)
    m.drum("roof",-.47,1.114,.44,.337,.136,.101)
    m.slab("wall",.40,.059,.337,.74,.167,.47,.06)
    m.slab("water",.40,.22,.337,.61,.019,.36,.09)
    for z in ((.19,.50) if m.lod<2 else (.345,)):
        for x in (.088,.706):
            m.block("trim",x,.061,z,.061,.502,.067)
    m.vault("metal",.40,.554,.344,.835,.118,.46)
    for x in (-.84,.85):
        m.beam("metal",(x,.223,-.65),(x,.223,.52),.049)
        for z in (-.60,.44):
            m.block("trim",x,.06,z,.075,.263,.081)
    ram(m,.197,-.421,69)
    if m.lod<2:
        ram(m,-.427,-.255,-66)
        m.beam("metal",(-.21,.712,.39),(.20,.286,.408),.043)
    if m.lod==0:
        m.drum("accent",-.47,.916,.44,.312,.047)
        m.drum("trim",-.47,1.075,.44,.307,.037)
        for x in (-.556,-.40):
            m.beam("metal",(x,.082,.119),(x,.803,.135),.027)
        for i in range(6):
            m.block("metal",-.478,.162+i*.101,.126+i*.0018,.166,.022,.028)
        with m.transform(translate=(-.138,.485,.388),rotate_x=-90):
            m.drum("accent",0,0,0,.084,.059,n=8)
        for x in (-.68,-.50,.47,.67):
            m.block("timber",x,.076,-.695,.144,.256,.054)
        m.beam("trim",(.32,.23,-.699),(.78,.23,-.699),.044)


def canopy_pasture(m):
    m.slab("grass",0,0,-.028,1.90,.063,1.73,.21)
    # Broad cantilevered shade leaves the front of the grazing terrace open.
    for x in (-.60,.54):
        m.slab("wall",x,.058,.40,.302,.234,.49,.14)
        m.beam("ivory",(x,.284,.51),(x,.803,.24),.124)
    for s in (-1,1):
        with m.transform(translate=(-.031+s*.393,.838,.287),rotate_z=s*15):
            m.slab("glass",0,-.047,0,.758,.098,.825,.22)
            if m.lod==0:
                for z in (-.244,0,.244):
                    m.block("metal",0,-.082,z,.697,.042,.046)
    m.slab("trim",-.031,.711,.291,.132,.079,.79,.18)
    m.drum("metal",.484,.06,-.263,.199,.222,.154)
    m.drum("accent",.484,.274,-.263,.266,.105,.239)
    m.drum("wall",.484,.374,-.263,.13,.234,.079)
    # Low curved segments form a sorting corral around the feeder.
    for a,b in (((-.78,.23),(-.78,-.39)),((-.75,-.42),(-.52,-.70)),
                ((.76,-.43),(.77,.29))):
        m.beam("ivory",(a[0],.189,a[1]),(b[0],.189,b[1]),.075)
        m.block("trim",a[0],.06,a[1],.081,.18,.083)
    ram(m,-.34,-.427,-27)
    if m.lod<2:
        ram(m,.028,-.14,75)
    if m.lod==0:
        for x in (-.50,.45):
            m.block("accent",x,.156,.158,.12,.052,.025)
        m.rune(.483,.324,-.526,.119)
        for angle in (-.81,1.31,3.38):
            x=.484+.171*math.cos(angle)
            z=-.263+.171*math.sin(angle)
            m.drum("trim",x,.064,z,.10,.081,.126,n=9)
            m.drum("grass",x,.142,z,.105,.021,n=8)


def mill(m):
    # Water-driven undershot wheel against the front bearing wall; a stone race below it.
    m.slab("fieldstone",0,0,0,1.80,.12,1.61,.13)
    m.hearth(.17,.118,.21,(1.38,1.51,1.22))
    m.block("slate",0,.01,-.49,1.937,.174,.418)
    m.block("water",0,.182,-.49,1.949,.025,.370)
    for z in (-.704,-.278):
        m.block("fieldstone",0,.113,z,1.91,.132,.066)
    m.block("timber",.17,.240,-.49,.256,.045,.580)
    m.wheel(-.42,.617,-.437,.425,"timber" if m.era==1 else "metal",6 if m.lod==2 else 8)
    # Horizontal axle reaches into the mill and across the outer bearing.
    with m.transform(translate=(-.42,.617,-.60),rotate_x=90):
        m.drum("trim",0,0,0,.071,.60)
    m.block("fieldstone",-.42,.117,-.625,.16,.52,.12,taper=.15)
    if m.lod<2:
        for i in range(8):
            a=2*math.pi*i/8
            with m.transform(translate=(-.42+.417*math.cos(a),.617+.417*math.sin(a),-.437),
                             rotate_z=math.degrees(a)):
                m.block("timber",0,-.026,0,.103,.056,.217)
        if m.era==2: m.stack(.54,.62,.48,.123,.62,"brick")
    if m.lod==0: cask(m,.68,.116,.71,.094,.18,"hide")


def hydro(m):
    m.slab("wall",0,0,0,1.57,.17,1.52,.17)
    m.slab("glass",0,.16,.05,.76,1.35,.68,.22,.22)
    m.slab("wall",0,.145,-.59,.53,.203,.337,.12)
    m.entry(0,.012,-.755,.45,.29)
    for i in range(4):
        w=1.53-i*.17
        y=.30+i*.354
        m.slab("roof",0,y,0,w,.11,w*.77,.20)
        m.slab("leaf",0,y+.106,0,w*.94,.068,w*.72,.20)
        if m.lod==0 or (m.lod==1 and i==3):
            for s in (-1,1):
                m.vault("grass",s*w*.29,y+.17,0,w*.19,.103,w*.40)
        if m.lod==0:
            m.block("gold",0,y+.018,-w*.385,w*.46,.033,.023)
            for s in (-1,1):
                for t in (-1,1):
                    m.dome((s*w*.22,y+.168,t*w*.246),.094,m.color("grass"),
                           rings=1,segments=7,squash=.93,material="grass")
    m.vault("roof",0,1.515,.05,.65,.234,.71)
    for s in (-1,1): m.beam("accent",(s*.63,.145,.29),(s*.36,1.49,.24),.072)


def build(spec,lod):
    m=Stonework(spec,lod)
    scale=spec.footprint[0]/2
    with m.transform(scale=scale):
        kind=spec.building_type
        if kind in ("GathererHut","HuntingLodge"): woodland(m,kind=="HuntingLodge")
        elif kind=="FishingHut": fisher(m)
        elif kind=="Farm": terrace(m)
        elif kind=="IndustrialFarm": growing_house(m)
        elif kind=="Pasture": pasture(m)
        elif kind=="Mill": mill(m)
        else: hydro(m)
    return m


if __name__=="__main__":
    publish_package("dwarf_food",build)
