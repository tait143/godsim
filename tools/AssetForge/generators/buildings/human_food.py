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
    divisions=4 if m.lod==0 else 2
    for side in (-1,1):
        for i in range(2*divisions-1):
            intercept=(i-divisions+1)/divisions
            v0=max(0.,-intercept)
            v1=min(1.,1.-intercept)
            a,b=intercept+v0,intercept+v1
            if side<0:
                a,b=1.-a,1.-b
            m.beam("hide",knot(a,v0,side*.003),knot(b,v1,side*.003),.008)


def shed(m, x, ground, z, size, era, fishing=False, simple_windows=False):
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
            m.light(.182,h*.62,-.285,.12,.102,shutters=era<2 and (m.lod==0 or not simple_windows))
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
        if m.lod==2 or m.lod==1 and era<2:
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
    if era==1:
        # Boat drawn bow-first into a little stone slip beside the net loft.
        for x in (-.37,-.02):
            for z in (-.28,.30):
                m.block('fieldstone',(x,.072,z),(.09,.144,.10),taper=.12)
        m.block('timber',(-.19,.145,.012),(.55,.061,.81))
        m.block('plaster',(-.21,.328,.201),(.41,.307,.35))
        m.entry(-.21,.026,.15,.177,.23)
        for x in (-.405,-.02):
            m.beam('timber',(x,.176,-.308),(x,.488,-.308),.040)
        with m.transform(translate=(-.21,0,.045)):
            m.roof(.55,.81,.482,.31,'roof','plaster',hip=.095,ridge_mat='trim')
        little_boat(m,.245,-.062,.85,era)
        if m.lod<2:
            for x in (-.39,-.285):
                m.beam('trim',(x,.197,-.336),(x,.456,-.31),.015)
            m.beam('timber',(-.397,.445,-.31),(-.274,.445,-.31),.025)
            with m.transform(translate=(-.123,.056,-.085),scale=.67):
                fishing_net(m)
        if m.lod==0:
            m.jar(-.05,-.14,.177,.052,.099)
            pennant(m,-.20,.77,.12,.12)
        return
    if era==2:
        # A drifter lies broadside to a piled tidal wharf. Its catch is being
        # lifted between the gunwale and the receiving door of the curing shed.
        m.course('water',0,0,.978,.954,0,.033,.081)
        m.course('fieldstone',-.06,.316,.81,.25,.033,.136,.034)
        for x in (-.363,.286):
            drum(m,'timber',x,.008,-.048,.041,.178,segments=(9,6,4)[m.lod])
        m.block('timber',(-.039,.16,.151),(.837,.059,.485))
        m.block('brick',(-.137,.323,.253),(.471,.274,.289))
        m.entry(-.195,.107,.161,.19,.212)
        with m.transform(translate=(-.137,0,.253)):
            m.roof(.537,.358,.458,.154,'slate','brick',ridge_mat='trim')
        m.flue(-.268,.308,.491,.664,.065)
        # A framed winch foundation and a crane with a deep jib give the
        # working front a clear outline even when the landing line disappears.
        x,z=.264,.096
        m.block('metal',(x,.228,z),(.152,.077,.145),taper=.10)
        m.beam('metal',(x,.255,z),(x,.645,z),.062)
        m.beam('metal',(x,.586,z),(.143,.566,-.206),.066)
        m.beam('gold',(x,.365,z),(.169,.563,-.151),.028)
        m.beam('trim',(.143,.561,-.206),(.143,.449,-.206),.021)
        with m.transform(translate=(.022,.020,-.259),rotate_y=70,scale=(1.11,.91,.98)):
            little_boat(m,0,0,.81,1)
            if m.lod==0:
                m.block('banner',(0,.12,-.226),(.169,.044,.057))
        # Each mesh row is offset half a diamond from the next one. The
        # slings and knotted belly remain a single loaded net at distance.
        nx,nz=.143,-.206
        count=(8,6,5)[m.lod]
        def knot(j,y,r,turn=0):
            a=2*math.pi*(j+turn)/count
            return (nx+r*math.cos(a),y,nz+r*.78*math.sin(a))
        for j in range(count):
            lip=knot(j,.363,.114)
            shoulder=knot(j,.276,.117,.5)
            toe=knot(j,.204,.064)
            if m.lod==2:
                m.face('hide',(lip,knot(j+1,.363,.114),shoulder))
                m.face('timber',(knot(j-.5,.276,.117),lip,shoulder,toe))
                m.face('hide',(toe,shoulder,knot(j+1,.204,.064)))
            else:
                m.beam('hide',lip,shoulder,.012)
                m.beam('hide',shoulder,knot(j+1,.363,.114),.010)
                m.beam('timber',shoulder,toe,.011)
                m.beam('hide',toe,knot(j-.5,.276,.117),.009)
            if m.lod<2 or j%2==0:
                m.beam('trim',lip,(nx,.449,nz),.012+j*.0002)
        with m.transform(translate=(nx,.278,nz),scale=(1,.82,.77)):
            m.sphere((0,0,0),.093,m.color('ivory'),rings=(4,3,2)[m.lod],
                     segments=(10,6,5)[m.lod],material='ivory')
        for sx in (-.329,.046):
            drum(m,'hide',sx,.19,-.011,.046,.073,.054,segments=(8,5,4)[m.lod])
        if m.lod<2:
            with m.transform(translate=(.10,0,.25),rotate_y=-90):
                m.light(0,.348,0,.137,.09,False)
            pennant(m,x,.645,z,.14)
            for bx in (-.329,.046):
                m.dome((bx,.263,-.011),.044,m.color('ivory'),rings=1,segments=6,squash=.48,material='ivory')
        if m.lod==0:
            for bx in (-.361,.289):
                drum(m,'trim',bx,.188,-.048,.034,.051)
                drum(m,'gold',bx,.239,-.048,.042,.018)
            m.herbs(.031,.321,.107,.10)
            for j in range(6):
                x=-.384+j*.14
                m.block('trim',(x,.191,.014),(.006,.006,.162))
        return
    if era==3:
        # Twin pontoons carry a catamaran net tender. A lift net hangs off its
        # working stern between the tips of a forked aluminum boom.
        for x in (-.267,.267):
            m.block('metal',(x,.016,.004),(.071,.032,.48),taper=.12)
            with m.transform(translate=(x,.011,0),scale=(.66,.82,.94)):
                little_boat(m,0,-.005,.96,era)
        m.block('metal',(0,.143,.025),(.692,.066,.635))
        m.course('ivory',-.063,.179,.43,.32,.168,.368,.055)
        m.course('glass',-.063,.179,.423,.313,.368,.485,.054,.023)
        m.course('ivory',-.063,.179,.458,.353,.485,.516,.062)
        m.entry(-.092,.019,.14,.17,.158,lintel='metal')
        for x in (-.237,.237):
            m.beam('metal',(x,.17,-.107),(x,.47,-.322),.037)
            m.beam('trim',(x,.469,-.322),(x,.23,-.355),.012)
        m.beam('metal',(-.244,.232,-.355),(.244,.232,-.355),.026)
        for i in range((9,5,3)[m.lod]):
            x=(i/((9,5,3)[m.lod]-1)-.5)*.451
            m.beam('banner',(x,.228,-.353),(x*.74,.094,-.299),.012)
        if m.lod<2:
            m.beam('ivory',(-.11,.512,.195),(-.11,.675,.195),.027)
            m.dome((-.11,.666,.195),.055,m.color('accent'),rings=2,segments=7,material='accent')
        if m.lod==0:
            with m.transform(translate=(.155,0,.179),rotate_y=-90):
                m.light(0,.268,0,.128,.089,False)
            m.block('metal',(-.062,.524,.244),(.134,.025,.078))
            for x in (-.234,.224):
                for z in (-.09,.015):
                    drum(m,'metal',x,.176,z,.042,.070,.03)
            for x in (-.325,.325):
                m.beam('gold',(x,.177,.18),(x,.244,.18),.017)
                m.beam('gold',(x,.177,-.094),(x,.244,-.094),.017)
                m.beam('gold',(x,.244,-.098),(x,.244,.184),.015)
        return
    # The landing and the boat stay outside the house envelope; timber piles support the deck.
    for x in (-.365,.153):
        for z in (-.307,.330):
            drum(m,"timber",x,0,z,.038,.155,segments=(7,5,4)[m.lod])
    m.block("timber",(-.10,.154,0),(.68,.072,.84))
    shed(m,-.105,.19,.095,.71,era,True,simple_windows=era<2)
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
        shed(m,.003,0,.085,.91,era,simple_windows=era==1)
        for x in (-.37,-.085):
            m.block("trim",(x,.1575,-.342),(.026,.315,.032))
        m.beam("trim",(-.40,.33,-.341),(-.06,.33,-.341),.030)
        hung_hide(m,[(-.194,.088,-.37),(-.310,.117,-.369),(-.343,.314,-.352),(-.11,.314,-.352)])
        if m.lod<2:
            # Carved stag saddle over the gable, with a branching tine on each side.
            m.block("timber",(0,.542,-.202),(.111,.117,.117),taper=.12)
            for s in (-1,1):
                m.beam("ivory",(0,.538,-.235),(s*.099,.645,-.227),.026)
                if era!=1 or m.lod==0:
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
    if era==3:
        # A solar orchard replaces the cereal rows. The arrays tip above
        # alternating lines of fruit trees with a produce cooler on the lane.
        m.course('grass',0,0,2.81,2.65,0,.055,.16)
        for j,(x,z) in enumerate(((-.89,-.78),(-.82,.02),(-.88,.84),
                                   (.13,-.84),(.21,-.06),(.17,.84))):
            h=.47+.06*(j%3)
            drum(m,'timber',x,.055,z,.088,h,.058,segments=(8,5,4)[m.lod])
            for s in (-1,1):
                m.beam('timber',(x,h*.69,z),(x+s*.144,h+.047,z+.023*s),.048)
                m.dome((x+s*.142,h+.015,z+.023*s),.226,
                       m.color('leaf' if s<0 else 'grass'),segments=(9,6,4)[m.lod],rings=(3,2,1)[m.lod],
                       squash=1.07,material='leaf' if s<0 else 'grass')
            if m.lod==0:
                for bx in (-.177,.13):
                    drum(m,'roof_tile',x+bx,h+.074,z-.173,.035,.044,.022,segments=5)
        for z in (-.49,.609):
            m.beam('metal',(.833,.055,z),(.833,1.025,z),.102)
            with m.transform(translate=(.83,1.028,z),rotate_z=-17):
                m.box((0,0,0),(.76,.071,.77),m.color('glass'),bottom=True,material='glass')
                if m.lod==0:
                    for u in (-.18,0,.18):
                        m.block('ivory',(u,.039,0),(.016,.013,.74))
        m.course('ivory',.923,-.933,.53,.48,.055,.411,.058)
        m.entry(.924,-1.173,.227,.065,.267,lintel='metal')
        if m.lod<2: pennant(m,.781,.399,-.892,.271)
        return
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
    if era==3:
        # The packing house turns harvest racks out toward the sun from a
        # central irrigation core. The open sides expose the planted shelves.
        m.course("fieldstone",0,0,2.72,2.56,0,.048,.17)
        m.course("ivory",.16,.30,.86,.80,.048,1.36,.12)
        for j in range(3):
            y=.35+j*.39
            x=-.26+.13*j
            if m.lod<2:
                m.course("metal",x,.27,1.95-.16*j,1.39-.12*j,y,y+.075,.15)
            else:
                m.block("metal",(x,y+.038,.27),(1.95-.16*j,.075,1.39-.12*j))
            if m.lod==2 and j==1:
                continue
            plants=(4,3,1)[m.lod]
            for k in range(plants):
                xx=x+(k-(plants-1)*.5)*1.26/max(1,plants-1)
                m.block("leaf",(xx,y+.13,-.08),(.19 if plants>1 else .85,.12,.43),taper=.23)
        if m.lod==2:
            m.block("roof",(.16,1.415,.30),(.99,.11,.91))
        else:
            m.course("roof",.16,.30,.99,.91,1.36,1.47,.14)
        m.course("glass",.51,-.61,.77,.64,.048,.37,.09)
        m.entry(.51,-.934,.26,.048,.27,lintel="metal")
        if m.lod<2:
            pennant(m,-.57,1.205,.62,.35)
        return
    m.course("fieldstone",0,0,2.77,2.61,0,.044,corner=.15)
    growing_range(m,-.64,.01,.96,1.70,era)
    growing_range(m,.46,.13,.81,1.46,era)
    # A common head house collects the harvest and supplies both growing ranges.
    m.block("wall",(-.16,.26,1.00),(1.93,.43,.36))
    m.hip_roof((-.16,.475,1.0),1.96,.41,.17,m.color("roof"),overhang=0,material="roof")
    drum(m,"metal",1.076,.044,.85,.161,.68)
    drum(m,"roof",1.076,.724,.85,.17,.066,.13)
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
    if era:
        m.course("grass",.06,-.03,2.69,2.55,0,.044,corner=.19)
        if era==1:
            # A timber byre forms one side of a triangular lambing fold.
            with m.transform(translate=(-.51,.044,.48),rotate_y=-23):
                m.block("wall",(0,.38,0),(.81,.76,1.11))
                m.roof(.96,1.25,.75,.47,"roof","wall",hip=.14,ridge_mat="trim")
                m.entry(0,-.562,.32,.035,.48)
            fence=[(-1.13,-.12),(-.53,-1.11),(.91,-.92),(1.14,.33),(.57,.92)]
            herd=[(.28,.29,-19),(.52,-.48,57),(-.14,-.60,109)]
        elif era==2:
            # Brick dairy, milk tower and long handling race.
            m.block("brick",(-.05,.405,.66),(1.72,.73,.70))
            with m.transform(translate=(-.05,0,.66)):
                m.roof(1.87,.84,.77,.28,"roof","wall",ridge_mat="trim")
            m.entry(-.31,.301,.36,.04,.53)
            drum(m,"metal",-.99,.044,.42,.236,1.04,.22)
            m.dome((-.99,1.08,.42),.225,m.color("roof"),segments=(12,8,5)[m.lod],rings=(3,2,1)[m.lod],material="roof")
            fence=[(-1.18,-.97),(.99,-.97),(1.19,-.17),(1.07,.54)]
            herd=[(-.58,-.38,31),(.16,-.31,-75),(.72,-.41,16)]
        else:
            # A sensor-equipped radial milking carousel shelters the herd beside an open exercise yard.
            cx,cz=.24,.33
            drum(m,"wall",cx,.044,cz,.69,.22,.76)
            drum(m,"glass",cx,.262,cz,.305,.86,.255)
            drum(m,"roof",cx,1.10,cz,.83,.18,.46)
            for j in range((7,5,3)[m.lod]):
                a=math.tau*j/(7,5,3)[m.lod]
                x,z=cx+.62*math.cos(a),cz+.62*math.sin(a)
                drum(m,"ivory",x,.245,z,.046,.884)
            m.entry(cx,-.005,.27,.264,.51)
            fence=[(-1.19,.84),(-1.21,-.91),(-.65,-1.15),(.69,-1.11),(1.20,-.70)]
            herd=[(-.72,.20,-31),(-.64,-.53,96),(.26,-.70,42)]
        for a,b in zip(fence,fence[1:]):
            rail(m,a,b,era,3)
        for x,z,turn in herd[:(3,2,1)[m.lod]]:
            ewe(m,x,z,turn)
        if m.lod<2:
            m.block("timber",(.86,.134,-.55),(.22,.18,.36))
            m.block("water",(.86,.228,-.55),(.16,.014,.29))
        return
    m.course("grass",0,0,2.76,2.64,0,.043,corner=.13)
    shed(m,-.56,.043,.68,1.39,era)
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


def gristmill(m,era):
    if era==2:
        # Steam roller mill: flour descends through a tall processing block.
        # The boiler's flywheel and sack chute are exposed to the loading yard.
        m.course('fieldstone',0,.03,1.82,1.69,0,.078,.10)
        m.course('brick',-.15,.275,1.03,.89,.078,1.17,.058)
        m.prism_x((-.15,1.17,.275),1.16,1.01,.30,m.color('slate'),material='slate')
        m.entry(-.31,-.17,.30,.082,.44)
        drum(m,'brick',.60,.078,.47,.167,1.47,.105)
        drum(m,'trim',.60,1.53,.47,.126,.091)
        m.block('metal',(.53,.28,-.15),(.41,.40,.56),taper=.09)
        from generators.buildings.human_extraction import wheel
        wheel(m,.524,.332,-.481,.239,'metal',.087)
        m.beam('timber',(.17,.749,-.163),(.17,.339,-.619),.118,.106)
        if m.lod<2:
            for x in (-.49,-.16,.172):
                m.light(x,.863,-.175,.169,.261,False)
            for z in (.094,.47):
                with m.transform(translate=(.37,0,z),rotate_y=-90):
                    m.light(0,.823,0,.17,.255,False)
        if m.lod==0:
            for x,z in ((-.43,-.619),(-.62,-.55),(.03,-.669)):
                m.jar(x,z,.079,.084,.165)
            pennant(m,-.53,1.31,.27,.26)
        return
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
