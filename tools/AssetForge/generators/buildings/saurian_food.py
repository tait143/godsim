"""Delta fisheries, reed-rice gardens, clutch pastures and water-driven grain works."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE = "saurian_food"


def solar_badge(m,x,y,z,r):
    m.block("trim",x,y,z,.053,r*1.05,.058)
    m.disc(x,y+r*1.28,z,r)


def punt(m,x,z):
    # Hollow reed boat with a dished floor and upswept ends.
    outline=[(0,-.68),(.14,-.40),(.17,.27),(.105,.56),(0,.67),(-.105,.56),(-.17,.27),(-.14,-.40)]
    if m.lod==2:
        outline=outline[::2]
    def rail(scale_x,scale_z,y,upturn):
        return [(x+a*scale_x,y+abs(b)**2*upturn,z+b*scale_z) for a,b in outline]
    keel=rail(.54,.81,.035,.12)
    rim=rail(1,1,.166,.35)
    inside=rail(.74,.94,.166,.35)
    floor=rail(.49,.79,.071,.12)
    for i in range(len(rim)):
        j=(i+1)%len(rim)
        for role,points in (("timber",(keel[j],keel[i],rim[i],rim[j])),
                            ("gold",(rim[j],rim[i],inside[i],inside[j])),
                            ("hide",(inside[j],inside[i],floor[i],floor[j]))):
            m.face(role,points)
    m.face("timber",floor[::-1])
    if m.lod<2:
        for spot in (-.22,.17):
            m.block("trim",x,.147,z+spot,.260,.042,.069)
        if m.lod==0:
            m.rod("thatch",(x-.096,.20,z-.39),(x+.125,.25,z+.33),.019)


def fisher(m):
    for x in (-.70,.18):
        for z in (-.52,.60):
            m.drum("timber",x,0,z,.075,.325,n=(8,6,4)[m.lod])
    m.slab("timber",-.25,.29,.03,1.24,.093,1.48,cut=.039,underside=True)
    m.house(-.25,.383,.26,1.02,.91,.77,role="metal" if m.era==0 else None)
    # The landing piles and the boat share the same waterline.
    m.slab("water",.48,0,-.025,.85,.058,1.65,.986,cut=.105)
    punt(m,.630,-.07)
    # Net spread on two poles by the landing; the lead line sags at the middle.
    for x in (-.79,-.38):
        m.rod("trim",(x,.38,-.50),(x,.79,-.50),.026)
    m.rod("trim",(-.82,.767,-.50),(-.35,.767,-.50),.020)
    net=[(-.38,.754,-.515),(-.79,.754,-.515),(-.74,.52,-.573),(-.54,.44,-.57),(-.42,.54,-.543)]
    m.face("hide",net[::-1])
    m.face("hide",[(x,y,z+.005) for x,y,z in net])
    if m.lod<2:
        m.disc(-.40,.773,-.50,.065)
        if m.lod==0:
            for x in (-.74,-.64,-.54):
                m.drum("ivory",x,.744,-.52,.027,.047,n=6)
    if m.era>=2:
        m.rod("metal",(.23,.38,.43),(.23,1.03,.43),.052)
        m.rod("metal",(.23,.99,.43),(.69,1.20,.31),.046)
        m.rod("trim",(.69,1.2,.31),(.69,.83,.31),.016)
    elif m.era==1:
        m.rooflet(-.25,1.22,.26,.40,.35,.18,"roof_tile")


def hut(m,hunting):
    m.slab("soil",0,0,.02,1.75,.07,1.63,.965)
    m.house(-.12,.07,.19,1.38,1.04,1.05,role="metal" if m.era==0 else None)
    solar_badge(m,-.12,1.23 if not m.era else 1.31,.19,.114)
    if hunting:
        # Drying rack carries stretched hides; forked posts project above its top rail.
        for x in (.06,.67):
            m.rod("timber",(x,.069,-.53),(x+.023,.74,-.54),.047)
        m.rod("trim",(.01,.657,-.535),(.73,.691,-.537),.038)
        leather=[(.64,.65,-.57),(.12,.63,-.57),(.16,.43,-.60),
                 (.25,.24,-.61),(.49,.31,-.608),(.61,.44,-.59)]
        m.face("hide",leather[::-1])
        m.face("hide",[(x,y,z+.006) for x,y,z in leather])
        if m.lod<2:
            m.jar(-.55,.07,-.48,.113)
            m.rod("ivory",(.63,.67,-.53),(.74,.82,-.49),.022)
        if m.era==1:
            m.rooflet(.49,.32,.32,.70,.61,.32,"slate")
            m.slab("wall",.49,.07,.32,.64,.25,.53)
    else:
        for x,z,r in ((.45,-.51,.19),(-.27,-.56,.143),(.68,.18,.149)):
            m.drum("thatch",x,.07,z,r*.69,r*1.32,r,n=(9,6,4)[m.lod])
            m.mound("leaf",x,.07+r*1.31,z,r*.95,r*.59)
            if m.lod==0:
                for a in (0,2.1,4.2):
                    m.blade("grass",(x,.07+r*1.44,z),(x+r*.91*math.cos(a),.07+r*2.25,z+r*.91*math.sin(a)),r*.26)


def rack_silo(m,x,z,large):
    r=.206 if large else .17
    h=.82 if large else .58
    m.drum("metal" if m.era>=2 else "timber",x,.059,z,r,h,n=(10,7,5)[m.lod])
    m.drum("roof",x,h+.059,z,r*1.13,r*.84,r*.35)
    if m.lod<2:
        for k in (.22,.68):
            m.drum("turquoise",x,.06+h*k,z,r*1.027,.037,n=(10,7)[m.lod])


def field(m,mechanical):
    m.slab("plaster" if m.era==0 else "soil",0,0,0,1.94,.06,1.87,.99,cut=.045)
    m.house(-.43,.06,.54,.84,.55,.59,role="metal" if m.era==0 else None)
    rack_silo(m,.262 if mechanical else .35,.55,mechanical)
    if mechanical:
        rack_silo(m,.75,.58,True)
    else:
        solar_badge(m,-.43,.72 if m.era<2 else .65,.54,.090)
    rows=(4,3,3)[m.lod]
    for j in range(rows):
        z=-.72+j*.739/(rows-1)
        m.slab("water",0,.061,z,1.75,.033,.243,cut=.035)
        count=(6,5,3)[m.lod]
        for i in range(count):
            x=-.84+(i+.5)*1.68/count
            role="thatch" if (j+m.era)%3==0 else "leaf"
            # Ridges of leafy crops are interrupted by glimpses of water.
            m.slab(role,x,.093,z,1.68/count*.83,.10+(i+2*j)%3*.018,.146,.40,cut=.07)
            if m.lod==0 and i%2==0:
                m.blade("grass",(x,.13,z),(x+.067,.252,z+.021),.030)
    if mechanical:
        # Traversing irrigator on legs with a visible crossfield spray pipe.
        for x in (-.86,.86):
            m.rod("metal",(x,.061,-.27),(x,.49,-.27),.032)
        m.rod("turquoise",(-.88,.48,-.27),(.88,.48,-.27),.034)
        if m.lod==0:
            for x in (-.57,-.19,.19,.57):
                m.drum("gold",x,.376,-.27,.045,.078,.018,n=6)
    elif m.era>=2:
        m.rod("metal",(.70,.06,.45),(.70,.53,.45),.070)
        m.slab("glass",.70,.53,.45,.39,.077,.30,.82,underside=True)


def animal(m,x,z,angle):
    # A patient, low-slung, four-legged marsh grazer with a broad tail and a dorsal crest.
    with m.transform(translate=(x,.065,z),rotate_y=angle):
        for s in (-1,1):
            for t in (-1,1):
                m.block("trim",s*.075,0,t*.111,.054,.108,.065)
        m.slab("hide",0,.085,0,.23,.147,.37,.80,cut=.20)
        m.slab("ivory",0,.143,-.19,.15,.093,.16,.81,cut=.24)
        m.slab("thatch",0,.145,-.28,.118,.059,.099,.67,cut=.19)
        m.blade("leaf",(0,.143,.13),(.06,.079,.40),.074)
        if m.lod==0:
            for dz in (.015,.102):
                m.blade("ivory",(0,.213,dz),(.002,.296,dz+.06),.038)


def pasture(m):
    m.slab("plaster" if m.era==0 else "grass",0,0,0,1.92,.066,1.84,cut=.07)
    if m.era==0:
        m.slab("grass",-.023,.066,-.058,1.55,.009,1.34,cut=.15)
    m.house(-.39,.066,.48,.93,.65,.61,role="roof" if m.era==0 else None)
    # A clear gate in the south fence.
    runs=[((-.86,-.77),(-.31,-.77)),((.20,-.77),(.86,-.77)),
          ((.86,-.77),(.86,.79)),((.86,.79),(-.86,.79)),((-.86,.79),(-.86,-.77))]
    for (x,z),(u,v) in runs:
        m.block("trim",x,.063,z,.066,.31,.069)
        if m.lod<2 and math.dist((x,z),(u,v))>1:
            m.block("trim",(x+u)/2,.063,(z+v)/2,.066,.31,.069)
        for y in ((.169,.298) if m.lod==0 else (.24,)):
            m.rod("timber" if m.era<2 else "metal",(x,y,z),(u,y,v),.025,n=4)
    m.block("trim",-.31,.063,-.77,.066,.31,.069)
    for x,z,a in ((-.35,-.24,58),(.39,-.16,-34),(.55,.41,144))[:(3,2,1)[m.lod]]:
        animal(m,x,z,a)
    if m.era>=2:
        m.drum("metal",.38,.066,.64,.17,.37)
        m.mound("glass",.38,.436,.64,.17,.11)
    if m.lod<2:
        solar_badge(m,.20,.35,-.77,.063)


def mill(m):
    m.house(-.34,0,.24,.88,.85,.72)
    m.slab("wall",.29,0,.18,.67,.84,.77,.81)
    m.rooflet(.29,.84,.18,.78,.87,.34,m.skin)
    # Head pond and overshot wheel. The axle enters the tower along Z.
    m.slab("water",.29,0,-.39,1.08,.072,.56)
    for x in (-.251,.832):
        m.block("fieldstone",x,0,-.39,.069,.112,.64)
    m.block("fieldstone",.20,0,-.678,.91,.109,.059)
    # A raised leat delivers water at the upper rim and drains through the gap in the bank.
    for z in (-.055,.37):
        m.block("trim",.661,0,z,.082,.88+.17*z,.081)
    with m.transform(translate=(.661,.87,.105),rotate_x=-10):
        m.block("water" if m.lod==2 else "timber",0,0,0,.178,.051,.78)
        if m.lod<2:
            m.block("water",0,.051,0,.136,.025,.78)
            for x in (-.083,.083):
                m.block("trim",x,.051,0,.029,.064,.78)
    cx,cy,cz=.29,.53,-.29
    r=.448
    m.ring("timber" if m.era==1 else "metal",cx,cy,cz,r,r*.78,.107,n=(16,10,5)[m.lod])
    m.rod("trim",(cx,cy,cz-.038),(cx,cy,.20),.076)
    for i in range((8,6,3)[m.lod]):
        a=2*math.pi*i/(8,6,3)[m.lod]
        m.rod("trim",(cx+.05*math.cos(a),cy+.05*math.sin(a),cz+.054),
              (cx+r*.88*math.cos(a),cy+r*.88*math.sin(a),cz+.054),.030,n=4)
        if m.lod==0:
            with m.transform(translate=(cx+r*math.cos(a),cy+r*math.sin(a),cz+.058),rotate_z=math.degrees(a)):
                m.block("gold",0,-.037,0,.091,.074,.159)
    m.disc(cx,cy,cz-.038,.106)
    if m.era==2:
        m.drum("brick",-.56,.47,.39,.104,.70,.083)
        m.drum("metal",-.56,1.16,.39,.11,.061)


def hydro(m):
    m.slab("ivory",0,0,0,1.47,.148,1.42,.89)
    m.drum("glass",0,.147,.06,.42,1.38,.265)
    for i in range(3):
        y=.32+i*.44
        r=.77-.092*i
        m.drum("roof",0,y,.06,r*.77,.116,r)
        m.drum("turquoise",0,y+.114,.06,r,.049)
        if m.lod<2:
            m.drum("soil",0,y+.163,.06,r*.94,.033)
        for j in range((8,5,3)[m.lod]):
            a=2*math.pi*(j+.31*i)/(8,5,3)[m.lod]
            px,pz=r*.75*math.cos(a),.06+r*.75*math.sin(a)
            root=(px,y+(.193 if m.lod<2 else .160),pz)
            m.blade("leaf",root,(px+.145*math.cos(a+.50),y+.395,pz+.145*math.sin(a+.50)),.096)
            if m.lod==0:
                m.blade("grass",root,(px+.141*math.cos(a-.61),y+.331,pz+.141*math.sin(a-.61)),.079)
    m.slab("wall",0,.14,-.458,.42,.29,.33)
    m.entry(0,.14,-.623,.245,.22)
    m.mound("roof",0,1.52,.06,.31,.30)
    solar_badge(m,0,1.79,.06,.122)


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        name=spec.building_type
        if name=="FishingHut":
            fisher(m)
        elif name in ("GathererHut","HuntingLodge"):
            hut(m,name=="HuntingLodge")
        elif name in ("Farm","IndustrialFarm"):
            field(m,name=="IndustrialFarm")
        elif name=="Pasture":
            pasture(m)
        elif name=="Mill":
            mill(m)
        else:
            hydro(m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
