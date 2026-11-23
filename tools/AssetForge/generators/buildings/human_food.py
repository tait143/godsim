"""Human food production, from basket shelters and eel boats to stacked glass kitchen gardens."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House

PACKAGE = "human_food"
ERAS = ("ancient", "classical", "industrial", "future")


def drum(m, role, x, y, z, radius, height, top=None, segments=None):
    m.cylinder((x,y,z),radius,height,m.color(role),segments=segments or (9,7,5)[m.lod],
               top_radius=top,material=role)


def pennant(m, x, y, z, height):
    m.block("trim",(x,y+height*.5,z),(.023,height,.023))
    tip = y+height
    a,b,c,d,e = [(x,tip,z),(x+.084,tip-.012,z-.014),(x+.158,tip-.055,z+.002),
                 (x+.084,tip-.081,z-.014),(x,tip-.077,z)]
    # A fold along the middle seam gives the cloth two differently lit facets.
    for face in ((a,b,d,e),(b,c,d)):
        m.face("banner",face)
        m.face("banner",[(u,v,w+.004) for u,v,w in reversed(face)])


def hung_hide(m, front):
    back = [(x,y,z+.009) for x,y,z in front]
    m.face("hide",front)
    m.face("hide",back[::-1])
    for i in range(len(front)):
        j=(i+1)%len(front)
        m.face("hide",(front[j],front[i],back[i],back[j]))


def fishing_net(m):
    # Knot two sets of diagonals between a cork line and a weighted foot rope.
    def knot(u,v,layer=0):
        left=-.344-.046*v
        width=.137+.073*v
        return (left+width*u,.257+.188*v+.018*u*(1-v),-.369+.019*v+layer)
    for u in (0,1):
        m.beam("timber",knot(u,0),knot(u,1),.011)
    m.beam("timber",knot(0,0),knot(1,0),.012)
    divisions=4 if m.lod==0 else 3
    for side in (-1,1):
        for i in range(2*divisions-1):
            intercept=(i-divisions+1)/divisions
            v0=max(0.,-intercept)
            v1=min(1.,1.-intercept)
            a,b=intercept+v0,intercept+v1
            if side<0:
                a,b=1.-a,1.-b
            m.beam("hide",knot(a,v0,side*.003),knot(b,v1,side*.003),.008)


def shed(m, x, ground, z, size, era, fishing=False):
    # The four eras replace the frame, roof profile and upper floors of the outbuilding.
    with m.transform(translate=(x,ground,z),scale=size):
        h=(.35,.43,.47,.50)[era]
        m.course("fieldstone",0,0,.76,.63,0,.075,corner=.055)
        m.block("wall",(0,(h+.073)/2,0),(.70,h-.073,.57))
        if era==0:
            m.roof(.84,.71,h,.36,"roof","wall",hip=.08,ridge_mat="timber")
        elif era==1:
            m.roof(.83,.70,h,.46,"roof","wall",hip=.045,ridge_mat="trim")
            # A limewashed working porch looks cream in Classical and warm ochre in Renaissance.
            for sx in (-.323,.322):
                m.block("timber",(sx,.245,-.406),(.042,.49,.047))
            with m.transform(translate=(0,.488,-.386),rotate_x=-14):
                m.box((0,0,0),(.743,.045,.306),m.color("plaster"),material="plaster",bottom=True)
        elif era==2:
            m.gable_roof((0,h,0),.72,.60,.22,m.color("roof"),overhang=.047,material="roof")
            m.block("roof",(-.14,h+.221,0),(.275,.092,.18),taper=.1)
        else:
            m.block("trim",(0,h+.020,0),(.79,.065,.64))
            m.wedge((-.02,h+.053,.026),.725,.57,.159,m.color("glass"),material="glass")
            m.block("accent",(0,h+.035,-.312),(.72,.027,.023))
        m.entry(-.09,-.285,.18,.077,h*.59)
        if m.lod < 2:
            m.light(.182,h*.62,-.285,.12,.102,shutters=era<2)
            if m.lod==0:
                for side in (-1,1):
                    m.block("trim",(side*.342,h*.52,-.282),(.038,h*.94,.045))
            if era==1:
                m.beam("trim",(-.338,.09,-.303),(-.195,h-.015,-.303),.025)
            if not fishing and era==0 and m.lod==0:
                m.herbs(.181,h*.62-.084,-.285,.18)
        if m.lod==0 and era>0:
            m.rear_vent(0,.292,h*.58,.16,.10)
            if era==2:
                m.flue(.21,.10,.44,.85,.095)


def little_boat(m, x, z, size, era):
    # A shallow, open clinker dinghy with pointed ends and two thwarts.
    with m.transform(translate=(x,.008,z),scale=size):
        plan=[(0,-.48),(.137,-.26),(.166,.13),(.092,.39),(0,.44),(-.092,.39),(-.166,.13),(-.137,-.26)]
        if m.lod==2:
            plan=[plan[i] for i in (0,2,4,6)]
        bottom=[(.65*a,.01,.79*b) for a,b in plan]
        sheer=[(a,.12+abs(b)*.16,b) for a,b in plan]
        inner=[(.83*a,.12+abs(b)*.16,.91*b) for a,b in plan]
        floor=[(.59*a,.043,.72*b) for a,b in plan]
        for j in range(len(plan)):
            k=(j+1)%len(plan)
            m.face("timber" if era<2 else "metal",(bottom[k],bottom[j],sheer[j],sheer[k]))
            m.face("ivory",(sheer[k],sheer[j],inner[j],inner[k]))
            m.face("timber",(inner[k],inner[j],floor[j],floor[k]))
        m.face("timber",floor[::-1])
        if m.lod<2:
            for sz in (-.17,.14):
                m.block("trim",(0,.115,sz),(.245,.026,.047))


def fisher(m,era):
    # The landing and the boat stay outside the house envelope; timber piles support the deck.
    for x in (-.365,.153):
        for z in (-.307,.330):
            drum(m,"timber",x,0,z,.038,.155,segments=(7,5,4)[m.lod])
    m.block("timber",(-.10,.154,0),(.68,.072,.84))
    shed(m,-.105,.19,.095,.71,era,True)
    little_boat(m,.337,-.008,.83,era)
    if m.lod<2:
        m.block("trim",(.18,.264,-.33),(.047,.15,.053))
        m.beam("timber",(.185,.32,-.33),(.324,.172,-.215),.014)
        if era<2:
            # Corks and the diamond of drying net flank the approach, clear of the front door.
            for x in (-.39,-.18):
                m.block("timber",(x,.326,-.35),(.02,.28,.026))
            m.beam("timber",(-.39,.46,-.35),(-.18,.46,-.35),.018)
            fishing_net(m)
            if m.lod==0:
                for x in (-.35,-.29,-.23):
                    drum(m,"ivory",x,.436,-.362,.014,.023,segments=6)
        else:
            m.block("metal",(-.36,.329,-.32),(.087,.28,.081))
            m.beam("metal",(-.36,.45,-.32),(-.17,.60,-.32),.040)
            m.beam("trim",(-.17,.60,-.32),(-.17,.35,-.32),.014)
    if m.lod==0:
        with m.transform(translate=(-.3,.19,-.238),scale=.62):
            m.basket(0,0,.075,.085)


def forest_hut(m,kind,era):
    # Shelters are biased to the rear so the rack and baskets sit in the working yard.
    if kind=="GathererHut":
        # An open sorting porch and a closed cupboard share a low shed roof. The
        # sweep of the lean-to and its loaded baskets distinguish it from a residence.
        m.course("fieldstone",0,.02,.81,.72,0,.055,corner=.062)
        m.block("wall",(0,.22,.178),(.65,.33,.30))
        for x in (-.305,.305):
            m.block("trim",(x,.198,-.235),(.055,.287,.061))
            m.block("trim",(x,.276,.27),(.047,.448,.060))
        m.beam("timber",(-.342,.323,-.235),(.342,.323,-.235),.045)
        with m.transform(translate=(0,.434,.085),rotate_x=-17):
            m.box((0,0,0),(.85,.075,.637),m.color("roof"),material="roof",bottom=True)
        m.entry(.105,.026,.17,.057,.236)
        m.block("timber",(-.043,.135,-.307),(.34,.18,.097),taper=.10)
        m.block("timber",(-.045,.239,-.307),(.374,.034,.145))
        if m.lod<2:
            m.beam("trim",(.294,.259,-.234),(.294,.374,.003),.035)
            for mx,mz,mr in ((-.141,-.320,.041),(-.061,-.298,.057),(.026,-.324,.037)):
                drum(m,"ivory",mx,.256,mz,mr*.35,.038,segments=5)
                m.dome((mx,.294,mz),mr,m.color("cloth"),rings=2 if m.lod==0 else 1,
                       segments=6 if m.lod==0 else 4,squash=.67,material="cloth")
        for x,z,r in ((-.293,-.345,.090),(.247,-.332,.079)):
            if m.lod==0:
                m.basket(x,z,r,.103)
                for i in range(4):
                    a=i*math.pi/2
                    m.dome((x+.036*math.cos(a),.106,z+.03*math.sin(a)),.038,
                           m.color("flower"),rings=1,segments=5,material="flower")
            else:
                drum(m,"hide",x,0,z,r*.76,.10,r,segments=5)
                m.dome((x,.10,z),r*.83,m.color("flower"),rings=1,segments=5,material="flower")
        if m.lod==0:
            m.beam("timber",(.33,.03,-.16),(.33,.43,.08),.026)
    else:
        shed(m,.003,0,.085,.91,era)
        for x in (-.37,-.085):
            m.block("trim",(x,.1575,-.342),(.026,.315,.032))
        m.beam("trim",(-.40,.33,-.341),(-.06,.33,-.341),.030)
        hung_hide(m,[(-.194,.088,-.37),(-.310,.117,-.369),(-.343,.314,-.352),(-.11,.314,-.352)])
        if m.lod<2:
            # Carved stag saddle over the gable, with a branching tine on each side.
            m.block("timber",(0,.542,-.202),(.111,.117,.117),taper=.12)
            for s in (-1,1):
                m.beam("ivory",(0,.538,-.235),(s*.099,.645,-.227),.026)
                m.beam("ivory",(s*.068,.611,-.23),(s*.071,.683,-.222),.020)
        if m.lod==0:
            with m.transform(translate=(.28,0,-.31),scale=.64):
                m.jar(0,0,0,.11,.16)


def furrow(m,x,z,length,width,era,ripe):
    m.prism_x((x,.035,z),length,width,.114,m.color("soil"),material="soil")
    count=(8,3 if era==1 else 4,1)[m.lod]
    for i in range(count):
        cx=x+length*((i+.5)/count-.5)
        w=length/count*.89
        h=(.164 if ripe else .114)+.012*math.sin(i*1.7)
        m.hip_roof((cx,.105,z),w,width*.66,h,m.color("thatch" if ripe else "leaf"),
                   overhang=0,material="thatch" if ripe else "leaf")
        if m.lod==0 and era==0 and i%3==1:
            m.beam("grass",(cx,.11,z-.017),(cx+.049,.268,z-.061),.019)


def crops(m,era):
    yard = ("plaster","fieldstone","soil","soil")[era]
    m.course(yard,0,-.05,2.82,2.72,0,.04,corner=.12)
    shed(m,-.72,.04,.75,1.19,era)
    for i in range(3 if m.lod==2 else 4):
        z=-1.02+i*(.38 if m.lod==2 else .32)
        furrow(m,0,z,2.53,.24 if m.lod==2 else .206,era,ripe=(i+era)%3==0)
    if era>=2:
        for x in (.39,.93):
            height=.74
            drum(m,"metal",x,.04,.71,.196,height)
            m.cone((x,.04+height,.71),.212,.175,m.color("roof"),segments=(10,7,5)[m.lod],material="roof")
            if m.lod==0:
                for y in (.23,.52,.76):
                    if y<height:
                        drum(m,"trim",x,y,.71,.201,.038)
        if era==3:
            m.beam("metal",(.39,.87,.71),(.93,.87,.71),.065)
        if m.lod<2:
            m.block("metal",(.69,.18,.21),(.50,.27,.20),taper=.15)
            for s in (-1,1):
                drum(m,"accent",.69+s*.145,.306,.21,.049,.048)
    else:
        drum(m,"timber",.64,.04,.73,.25,.39)
        m.cone((.64,.43,.73),.31,.35,m.color("thatch"),segments=(10,7,5)[m.lod],material="thatch")
        if m.lod<2:
            # The straw sentinel stands at the edge of the furrows in its work smock.
            m.block("timber",(1.05,.31,.25),(.05,.55,.05))
            m.beam("timber",(.906,.466,.25),(1.204,.501,.25),.034)
            m.cone((1.05,.32,.25),.093,.17,m.color("banner"),segments=5,material="banner")
            m.sphere((1.05,.54,.25),.063,m.color("hide"),rings=2,segments=6,material="hide")
            m.cone((1.05,.58,.25),.094,.063,m.color("thatch"),segments=7,material="thatch")
    if m.lod==0 and era>=1:
        pennant(m,-.69,.58,.85,.61)


def growing_range(m,x,z,width,length,era):
    half=width/2
    front=z-length/2
    m.block("wall",(x,.172,z),(width-.035,.26,length-.04))
    segments=(8,6,4)[m.lod]
    sections=(4,2,1)[m.lod]
    def point(a,depth,r):
        return (x+r*math.cos(a),.297+r*math.sin(a)*(.90 if era==3 else .70),depth)
    for i in range(segments):
        a,b=math.pi*i/segments,math.pi*(i+1)/segments
        for j in range(sections):
            z0,z1=front+length*j/sections,front+length*(j+1)/sections
            m.face("glass" if (i+j)%4 else "roof",
                   (point(a,z0,half),point(b,z0,half),point(b,z1,half),point(a,z1,half)))
        for k in range(1,sections):
            end=front+length*k/sections
            # A raised structural glazing bar spans across the curve, joint to joint.
            m.face("metal",(point(a,end-.012,half+.012),point(b,end-.012,half+.012),
                            point(b,end+.012,half+.012),point(a,end+.012,half+.012)))
    front_face=[point(math.pi*i/segments,front,half) for i in range(segments+1)]
    m.face("glass",front_face[::-1])
    m.face("glass",[(u,v,w+length) for u,v,w in front_face])
    m.block("trim",(x,.158,front-.022),(.28,.28,.055))
    m.entry(x,front-.051,.189,.054,.236,lintel="metal")
    if m.lod<2:
        for s in (-1,1):
            m.block("accent",(x+s*(half-.011),.28,z),(.044,.056,length+.036))


def industrial_farm(m,era):
    m.course("fieldstone",0,0,2.77,2.61,0,.044,corner=.15)
    growing_range(m,-.64,.01,.96,1.70,era)
    growing_range(m,.46,.13,.81,1.69 if era==3 else 1.46,era)
    # A common head house collects the harvest and supplies both growing ranges.
    m.block("wall",(-.16,.26,1.00),(1.93,.43,.36))
    m.hip_roof((-.16,.475,1.0),1.96,.41,.17,m.color("roof"),overhang=0,material="roof")
    drum(m,"metal",1.076,.044,.85,.161,.68)
    drum(m,"roof",1.076,.724,.85,.17,.066,.13)
    if era==3:
        for s in (-1,1):
            m.block("metal",(.58+s*.265,.276,-.967),(.048,.46,.05))
        with m.transform(translate=(.58,.539,-.967),rotate_x=-14):
            m.box((0,0,0),(.68,.063,.33),m.color("glass"),material="glass",bottom=True)
    if m.lod<2:
        for x,z in ((-.93,-1.07),(-.28,-1.06),(.012,-.92)):
            m.block("hide",(x,.125,z),(.21,.16,.19),taper=.09)
            m.block("leaf",(x,.207,z),(.18,.042,.16),taper=.25)
    if m.lod==0:
        pennant(m,-.71,.51,1.00,.36)
        m.beam("metal",(.74,.42,1.011),(1.06,.57,.85),.065)
        for z in (.41,.66):
            m.block("ivory",(-1.14,.18,z),(.058,.17,.11))


def rail(m,a,b,era,extent):
    x,z=a
    dx,dz=b[0]-x,b[1]-z
    for i in range(extent):
        u=i/extent
        if m.lod==2 and i != 0:
            continue
        m.block("trim",(x+dx*u,.192,z+dz*u),(.072,.38,.072),taper=.11)
    levels=(.18,.31) if m.lod==0 else (.245,)
    length=math.hypot(dx,dz)
    inset_x,inset_z=dx*.028/length,dz*.028/length
    for y in levels:
        m.beam("metal" if era>=2 else "timber",(x+inset_x,y,z+inset_z),
               (x+dx-inset_x,y,z+dz-inset_z),.048)


def ewe(m,x,z,turn):
    with m.transform(translate=(x,.04,z),rotate_y=turn):
        for s in (-1,1):
            for t in (-1,1):
                m.block("trim",(s*.072,.069,t*.082),(.039,.138,.042))
        m.sphere((0,.183,0),.153,m.color("ivory"),rings=3 if not m.lod else 2,
                 segments=7 if not m.lod else 5,squash=.72,material="ivory")
        m.sphere((0,.221,-.139),.077,m.color("trim"),rings=2,segments=6,squash=.85,material="trim")
        if m.lod==0:
            for s in (-1,1):
                m.block("trim",(s*.065,.237,-.136),(.059,.026,.044))


def pasture(m,era):
    m.course("grass",0,0,2.76,2.64,0,.043,corner=.13)
    shed(m,-.56,.043,.68,1.39,era)
    if era==1:
        # Gravel path leading from the stock gate into the sheltered shearing yard.
        with m.transform(translate=(-.32,.046,-.42),rotate_y=-17):
            m.block("fieldstone",(0,0,0),(.37,.010,1.63))
    corners=[(-1.27,-1.19),(-.44,-1.19)]
    rail(m,corners[0],corners[1],era,2)
    rail(m,(.14,-1.19),(1.27,-1.19),era,3)
    rail(m,(1.27,-1.19),(1.27,1.19),era,4)
    rail(m,(1.27,1.19),(-1.27,1.19),era,4)
    rail(m,(-1.27,1.19),(-1.27,-1.19),era,4)
    m.block("trim",(-.44,.192,-1.19),(.072,.38,.072),taper=.11)
    if m.lod<2:
        herd=((.55,.27,49),(.14,-.49,-32),(-.71,-.50,84))
        for x,z,a in herd[:3 if m.lod==0 else 2]:
            ewe(m,x,z,a)
        m.block("timber",(.92,.105,-.64),(.25,.14,.47),taper=.13)
        m.block("water",(.92,.179,-.64),(.18,.018,.39))
    else:
        for x,z,a in ((.55,.27,49),(-.71,-.50,84)):
            with m.transform(translate=(x,.043,z),rotate_y=a):
                m.block("ivory",(0,.116,0),(.246,.232,.294),taper=.20)
                m.block("trim",(0,.188,-.159),(.115,.118,.115),taper=.14)
    if m.lod==0:
        m.beam("banner",(-.42,.30,-1.18),(-.28,.50,-1.17),.055)
    if era>=2:
        with m.transform(translate=(.64,.042,.73)):
            for s in (-1,1):
                m.block("metal",(s*.31,.38,0),(.072,.76,.078))
            m.wedge((0,.76,0),.83,.54,.13,m.color("glass"),material="glass")


def gristmill(m,era):
    h=1.10
    m.course("fieldstone",0,.11,1.06,.96,0,.15,corner=.15)
    drum(m,"wall",0,.15,.11,.457,h,.309)
    m.hip_roof((0,h+.15,.11),.77,.71,.37,m.color("roof"),overhang=.014,material="roof")
    m.block("fieldstone",(0,.218,-.265),(.269,.381,.14))
    m.entry(0,-.335,.207,.075,.32)
    cy,cz=1.075,-.40
    with m.transform(translate=(0,cy,cz+.248),rotate_x=-90):
        drum(m,"metal",0,0,0,.088,.346)
    for i in range(4):
        with m.transform(translate=(0,cy,cz),rotate_z=24+90*i):
            m.block("trim",(0,.406,0),(.059,.708,.041))
            m.block("ivory",(-.069,.526,-.028),(.183,.424,.022))
            if m.lod==0:
                for y in (.34,.46,.58,.70):
                    m.block("timber",(-.070,y,-.045),(.19,.017,.015))
    if m.lod<2:
        m.block("fieldstone",(-.206,.68,-.168),(.180,.167,.168),taper=.05)
        m.light(-.206,.68,-.252,.106,.113)
        with m.transform(translate=(.41,0,-.30),scale=.84):
            m.jar(0,0,0,.128,.224)
    if era==2:
        shed(m,.526,0,.36,.76,2)
    elif m.lod==0:
        m.flue(.20,.25,1.105,1.67,.096)


def hydroponics(m,era):
    # Crops grow in open planter galleries around a glazed seed vault, with a roof collector.
    m.course("wall",0,0,1.38,1.29,0,.14,corner=.23)
    drum(m,"glass",0,.14,0,.48,1.69,.30)
    levels=(.30,.72,1.14,1.56)
    for i,y in enumerate(levels):
        r=.735-.072*i
        drum(m,"roof",0,y,0,r*.74,.09,r)
        drum(m,"turquoise",0,y+.09,0,r,.037,r*.994)
        drum(m,"grass",0,y+.127,0,r*.965,.029)
        if m.lod<2:
            count=(9,5)[m.lod]
            for j in range(count):
                a=2*math.pi*(j+i*.32)/count
                m.dome((r*.76*math.cos(a),y+.155,r*.76*math.sin(a)),r*.128,
                       m.color("leaf"),rings=1,segments=5,material="leaf")
    m.dome((0,1.83,0),.36,m.color("roof"),rings=(3,2,1)[m.lod],
           segments=(10,7,5)[m.lod],squash=.74,material="roof")
    m.block("ivory",(0,.242,-.548),(.542,.384,.338))
    m.entry(0,-.717,.239,.071,.277,lintel="metal")
    with m.transform(translate=(0,0,-.548)):
        m.roof(.619,.438,.431,.208,"banner","ivory",hip=.018,ridge_mat="trim")
    if m.lod==0:
        for j in range(3):
            a=2*math.pi*j/3+math.pi/2
            m.beam("gold",(.45*math.cos(a),.14,.45*math.sin(a)),
                   (.29*math.cos(a),1.84,.29*math.sin(a)),.048)
        pennant(m,.16,1.95,.05,.27)


def build(spec,lod):
    m=House(spec,lod)
    era=ERAS.index(spec.band)
    t=spec.building_type
    if t in ("GathererHut","HuntingLodge"):
        forest_hut(m,t,era)
    elif t=="FishingHut":
        fisher(m,era)
    elif t=="Farm":
        crops(m,era)
    elif t=="IndustrialFarm":
        industrial_farm(m,era)
    elif t=="Pasture":
        pasture(m,era)
    elif t=="Mill":
        gristmill(m,era)
    else:
        hydroponics(m,era)
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        print(spec.id,entry["lod_triangles"])
