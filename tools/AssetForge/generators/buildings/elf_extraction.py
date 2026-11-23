"""Elven quarries, shaft houses, oil pumps and timber yards."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_food import Grove, TAU, publish_package

PACKAGE="elf_extraction"


def wheel(m,x,y,z,r,axis="z",role="metal"):
    """A rim with open spoke bays and an axle through its hub."""
    n=(16,10,6)[m.lod]
    with m.transform(translate=(x,y,z),rotate_y=90 if axis=="x" else 0):
        for i in range(n):
            a,b=TAU*i/n,TAU*(i+1)/n
            def vertex(phi,rad,d):
                return (math.cos(phi)*rad,math.sin(phi)*rad,d)
            c=[vertex(a,r,-.17*r),vertex(b,r,-.17*r),vertex(b,.76*r,-.17*r),vertex(a,.76*r,-.17*r)]
            d=[(u,v,.17*r) for u,v,_ in c]
            m.face(role,c[::-1])
            m.face(role,d)
            for j in (0,2):
                k=j+1
                m.face(role,[c[j],c[k],d[k],d[j]])
        for i in range(3 if m.lod<2 else 2):
            a=math.pi*i/(3 if m.lod<2 else 2)
            v=(r*.87*math.cos(a),r*.87*math.sin(a))
            m.limb("trim",[(-v[0],-v[1],0),(v[0],v[1],0)],[.078*r,.078*r],4)
        m.limb("gold",[(0,0,-.23*r),(0,0,.23*r)],[.16*r,.16*r],(8,6,4)[m.lod])


def log(m,x,y,z,r,length,angle=0):
    with m.transform(translate=(x,y,z),rotate_y=angle):
        m.limb("timber",[(0,0,-length/2),(0,0,length/2)],[r,r*.90],(9,6,4)[m.lod])
        # Fresh end grain is inset in the bark; the darker heart records the cut.
        def end(role,depth,radius):
            n=(9,6,4)[m.lod]
            m.face(role,[(radius*math.cos(-TAU*j/n),radius*math.sin(-TAU*j/n),depth)
                         for j in range(n)])
        end("thatch",-length/2-.001,r*.81)
        if not m.lod:
            end("hide",-length/2-.002,r*.39)


def timber_stack(m):
    # Fit the three tapered bolts in cross section at each LOD. A square bolt
    # sits lower between its neighbours than a nine-sided one of the same radius.
    n=(9,6,4)[m.lod]
    outline=[(-math.sin(TAU*j/n),math.cos(TAU*j/n)) for j in range(n)]
    spacing=.055/.057
    heights=[]
    for (x0,y0),(x1,y1) in zip(outline,outline[1:]+outline[:1]):
        nx,ny=y1-y0,x0-x1
        if ny<0:
            nx,ny=-nx,-ny
        if ny>1e-6:
            projected=[nx*u+ny*v for u,v in outline]
            heights.append((max(projected)-min(projected)-abs(nx)*spacing)/ny)
    rise=min(heights)-.009
    with m.transform(translate=(-.166,.071,-.223),rotate_y=9):
        for side,shift in ((-1,0),(1,.004),(0,.012)):
            rings=[]
            for z in (-.1625+shift,.1625+shift):
                r=.057*(1-.10*(z+.1625)/.325)
                cx=side*spacing*r
                cy=rise*r if side==0 else 0
                rings.append([(cx+r*u,cy+r*v,z) for u,v in outline])
            near,far=rings
            for j in range(n):
                k=(j+1)%n
                m.face("timber",[near[j],near[k],far[k],far[j]])
            m.face("timber",near[::-1])
            m.face("timber",far)
            x=sum(p[0] for p in near)/n
            y=sum(p[1] for p in near)/n
            for ratio,d,role in ((.81,.001,"thatch"),(.39,.002,"hide")):
                if m.lod and role=="hide":
                    continue
                m.face(role,[(x+(u-x)*ratio,y+(v-y)*ratio,z-d) for u,v,z in reversed(near)])


def camp(m):
    # A retained pollard at the back carries the workers' split-leaf shelter.
    m.hall(-.135,0,.184,.22,.184,.247+.017*m.era,.21+.029*m.era)
    m.limb("timber",[(.187,0,.178),(.187,.045,.178),(.174,.28,.162),(.198,.48,.159),
                     (.266,.59+.028*m.era,.142)],[.094,.077,.062,.045,.035])
    m.leaf("leaf",(.203,.47,.157),(.11,.61,.06),.065,.061)
    m.leaf("roof",(.239,.541,.153),(.382,.631,.204),.087,.049)
    for z in (-.325,-.125):
        m.box((-.166,.018,z),(.277,.036,.041),m.color("trim"),material="trim")
    timber_stack(m)
    m.disk("timber",.21,0,-.177,.12,.112,.149,.91)
    m.disk("thatch",.21,.148,-.177,.102,.094,.009)
    # A grounded chopping block, tool secured in its kerf.
    if m.lod<2:
        m.limb("trim",[(.203,.161,-.173),(.273,.336,-.133)],[.015,.012],5)
        m.lump("metal",(.205,.181,-.176),(.052,.034,.020),5)
    if m.era==1:
        m.limb("trim",[(.16,.23,.163),(-.03,.447,.112),(-.20,.403,.106)],[.029,.021,.016])
    if m.era>=2:
        wheel(m,.256,.263,-.147,.104,role="metal")
        m.limb("metal",[(.23,.139,-.126),(.256,.263,-.13)],[.029,.024])
        if m.era==3:
            m.leaf("glass",(.175,.329,.165),(.034,.409,-.03),.093,.038)


def cliff(m,x,z,rx,rz,h,tiers,role):
    # Unequal ledges give the working face a stepped, quarried profile.
    n=(13,9,6)[m.lod]
    ring=[]
    for i in range(n):
        a=TAU*i/n
        r=1+.060*math.sin(3*a+.7)+.035*math.cos(5*a)
        ring.append((rx*r*math.cos(a),rz*r*math.sin(a)))
    low=[(x+a,.0,z+b) for a,b in ring]
    for j in range(tiers):
        scale=1-.15*j
        high=[(x+a*scale+.047*j,h*(j+1+.13*math.sin(i*2+j)+.13*math.cos(TAU*i/n-.8))/tiers,z+b*scale+.045*j)
              for i,(a,b) in enumerate(ring)]
        for i in range(n):
            k=(i+1)%n
            m.face(role,[low[k],low[i],high[i]])
            m.face(role,[low[k],high[i],high[k]])
        if j+1<tiers:
            low=[(x+(u-x)*.88+.02,v+.008,z+(w-z)*.88+.024) for u,v,w in high]
            for i in range(n):
                k=(i+1)%n
                m.face("fieldstone",[high[k],high[i],low[i]])
                m.face("fieldstone",[high[k],low[i],low[k]])
        else:
            apex=(x+.06*j,h+.044,z+.04*j)
            for i in range(n):
                m.face(role,[high[(i+1)%n],high[i],apex])


def jib(m,x,y,z,reach,h):
    role="timber" if m.era<2 else "metal"
    tip=(x+reach,y+h,z-.046)
    base=(x,y,z)
    elbow=(x+reach*.20,y+h*1.15,z)
    m.limb(role,[base,(x,y+.052,z),(x-.035,y+h*.64,z),elbow,tip],[.078,.070,.054,.039,.025])
    if m.lod<2:
        m.limb("trim",[(x-.02,y+h*.46,z),(x+reach*.45,y+h*1.09,z-.014)],[.033,.024])
    m.limb("trim",[tip,(tip[0],y+h*.45,tip[2])],[.011,.011],4)
    m.disk("metal" if m.era>=2 else "hide",tip[0],y+h*.30,tip[2],.076,.074,h*.16,1.25, (9,6,4)[m.lod])


def quarry(m):
    cliff(m,-.20,.245,.59,.48,.60+.08*m.era,2+(m.era%2),"fieldstone")
    m.hall(-.53,0,-.321,.22,.21,.231+.044*m.era,.22)
    jib(m,.64,0,-.18,-.36,.89+.087*m.era)
    # Blocks emerging from the face are sawn, with a distinct dressed upper course.
    for i,(x,z) in enumerate(((-.04,-.68),(.32,-.71),(.63,-.62))):
        if m.lod==2 and i==2:
            continue
        m.disk("wall",x,.0,z,.185,.13,.111+.047*i,1,4)
        if m.lod<2:
            m.disk("fieldstone",x+.014,.108+.047*i,z+.004,.131,.108,.078,1,4)
    if m.lod<2:
        for x,z,h in ((-.56,.52,.53),(-.49,.69,.44),(-.72,.42,.29)):
            m.lump("fieldstone",(x,h*.50,z),(.18,h*.50,.16),5)
    if m.era>=2:
        wheel(m,.28,.278,-.403,.224)
        m.limb("metal",[(.43,0,-.32),(.43,.047,-.32),(.28,.276,-.383)],[.060,.051,.048])
    if m.lod==0:
        m.pot(-.80,0,-.13,.081,"ivory")


def ore_cart(m,x,z,role):
    with m.transform(translate=(x,0,z)):
        for d in (-.20,.17):
            m.box((0,.014,d),(.33,.028,.047),m.color("timber"),material="timber")
        for s in (-1,1):
            m.limb("metal" if m.era else "timber",[(s*.12,.026,-.27),(s*.12,.027,.30)],[.023,.024],4)
            for d in ((-.096,.099) if m.lod<2 else (0,)):
                m.limb("trim",[(s*.093,.082,d),(s*.142,.082,d)],[.052,.051],(8,6,4)[m.lod])
        m.disk("timber" if m.era<2 else "metal",0,.083,0,.16,.17,.157,1.21, (8,6,5)[m.lod])
        for j,(u,v,r) in enumerate(((-.067,.00,.079),(.058,-.043,.093),(.008,.07,.068))):
            if m.lod==2 and j>0:
                continue
            m.lump(role,(u,.244,v),(r,r*.87,r*.87),5)


def mine(m,kind):
    price=kind=="GoldMine"
    coal=kind=="CoalMine"
    ore="gold" if price else "door" if coal else "metal"
    cliff(m,-.165,.25,.569,.41,.455,2,"slate" if coal else "fieldstone")
    # The headframe straddles the portal. Sheave axle runs between its two side cheeks.
    h=(.85,1.02,1.17,1.34)[m.era]+(.14 if coal else 0)
    width=.31 if price else .26
    for s in (-1,1):
        top=(s*.11,h,-.368)
        m.limb("timber" if m.era<2 else "trim",[(s*width,0,-.462),(s*width,.069,-.462),
               (s*(width-.053),h*.56,-.408),top],[.067,.060,.047,.035])
        m.limb("trim",[(s*.32,0,.29),(s*.32,.072,.29),top],[.065,.061,.046])
    m.limb("metal",[(-.15,h,-.368),(.15,h,-.368)],[.044,.044])
    radius=.143 if m.era<2 else .183
    if m.lod<2:
        wheel(m,0,h,-.368,radius,"x")
    else:
        m.limb("metal",[(-.037,h,-.368),(.036,h,-.368)],[radius,radius],8)
    drop=-.368-radius
    m.limb("trim",[(0,h+.012,drop),(0,.584,drop)],[.014,.014],4)
    m.limb("trim",[(-.057,.516,drop),(0,.584,drop),(.057,.516,drop)],[.011,.012,.011],4)
    m.disk("hide" if m.era<2 else "metal",0,.425,drop,.061,.058,.095,1.20,6)
    # A pointed doorway and its solid collar project out from the rock.
    m.disk("wall",-.10,0,-.088,.256,.247,.35,1)
    m.crown(-.10,.345,-.088,.30,.28,.263,"roof",5)
    m.portal(-.10,.002,-.334,.317,.309)
    ore_cart(m,-.11,-.59,ore)
    if price:
        # An inclined washing trough supported at the head and tail, with a gold catch pan.
        for z,hp in ((-.47,.133),(.26,.351)):
            m.disk("trim",.561,.0,z,.074,.072,hp)
        for s in (-1,1):
            m.limb("timber",[(.561+s*.079,.133,-.47),(.561+s*.079,.358,.26)],[.032,.031],4)
        m.leaf("water",(.561,.13,-.48),(.561,.353,.272),.083,.020)
        m.disk("gold",.561,0,-.58,.115,.118,.083,1.10)
    elif coal:
        m.disk("metal",.53,.0,.12,.107,.106,.17)
        m.disk("metal",.53,.163,.12,.112,.112,.38,2.0)
        m.disk("door",.53,.54,.12,.206,.202,.086,.32)
    else:
        jib(m,.55,0,.13,-.197,.60+.05*m.era)
    if m.lod==0:
        for x,z,r in ((-.56,-.04,.096),(-.64,-.18,.075),(-.49,-.13,.066)):
            m.lump(ore,(x,.061,z),(r,.060,r*.9),5)
    if m.era>=2:
        height=.37+.09*(m.era==3)
        m.disk("brick" if m.era==2 else "glass",-.61,0,.008,.175,.182,height,.92)
        m.crown(-.61,height-.004,.008,.194,.193,.166,"roof",4)
        if m.lod<2:
            m.portal(-.61,.005,-.169,.139,.201)
    elif m.lod<2:
        m.lump("emissive",(-.287,.264,-.263),(.035,.058,.036),5)


def oilwell(m):
    # Walking beam balances on the column; a broad curved horsehead guides the polished rod.
    h=.89 if m.era==2 else 1.10
    for s in (-1,1):
        m.limb("trim",[(s*.41,0,.115),(s*.41,.084,.115),(s*.19,h*.67,.092),(0,h,.042)],
               [.098,.086,.058,.059])
    m.limb("metal",[(-.58,h-.155,.042),(-.07,h+.04,.042),(.45,h+.23,.042)],[.103,.082,.067])
    m.limb("roof",[(.43,h+.225,.042),(.56,h+.134,.041),(.59,h-.113,.038)],[.133,.117,.066])
    m.limb("metal",[(.59,h-.124,.038),(.59,.153,.038)],[.023,.023],5)
    m.disk("metal",.59,0,.038,.123,.121,.211,.8)
    wheel(m,-.50,.275,-.03,.223)
    m.limb("accent",[(-.554,.387,-.065),(-.558,h-.13,-.028)],[.034,.036])
    m.hall(-.41,0,.20,.292,.242,.369,.218,"brick" if m.era==2 else "glass")
    m.disk("wall",.337,0,-.384,.229,.197,.32)
    m.crown(.337,.318,-.384,.239,.217,.132,"metal",5)
    if m.lod<2:
        m.limb("metal",[(.59,.150,.038),(.64,.175,-.326),(.44,.176,-.386)],[.04,.038,.037])
    if m.era==3:
        m.leaf("glass",(-.08,h+.052,.052),(-.39,h+.19,.187),.098,.049)


def sawmill(m):
    m.hall(-.30,0,.31,.446,.372,.55+.14*(m.era==3),.4)
    # Logs feed directly into the toothed blade along the front of the cutting gallery.
    for s in (-1,1):
        for j in (-1,1):
            m.disk("trim",.25+s*.147,.0,-.18+j*.38,.061,.061,.221,1)
    for z,rz in ((-.377,.332),(.203,.166)):
        m.disk("trim",.25,.21,z,.250,rz,.072,1,8)
    log(m,.234,.351,-.347,.083,.59)
    r=.266
    cx,cy,cz=.230,.409,-.005
    # A single saw plate with a toothed rim and distinct center boss.
    n=(20,12,8)[m.lod]
    pts=[(cx+r*(1 if i%2==0 else .83)*math.cos(TAU*i/n),
          cy+r*(1 if i%2==0 else .83)*math.sin(TAU*i/n)) for i in range(n)]
    for i in range(n):
        j=(i+1)%n
        a,b=pts[i],pts[j]
        m.face("metal",[(cx,cy,cz-.018),(*b,cz-.018),(*a,cz-.018)])
        m.face("metal",[(cx,cy,cz+.018),(*a,cz+.018),(*b,cz+.018)])
        m.face("accent",[(*a,cz-.018),(*b,cz-.018),(*b,cz+.018),(*a,cz+.018)])
    m.limb("trim",[(cx,cy,cz-.039),(cx,cy,cz+.17)],[.046,.048])
    m.disk("wall",cx,0,cz+.151,.106,.105,.438,.76)
    for z in (-.55,-.245):
        m.box((-.47,.022,z),(.49,.044,.057),m.color("trim"),material="trim")
    for i in range(3 if m.lod<2 else 2):
        log(m,-.62+.15*i,.091,-.397,.072,.58,7)
    if m.era==3:
        m.leaf("glass",(-.35,.82,.29),(.18,.75,.19),.19,.14)


def build(spec,lod):
    m=Grove(spec,lod)
    t=spec.building_type
    if t in ("Mine","GoldMine","CoalMine"):
        mine(m,t)
    else:
        {"WoodcutterCamp":camp,"StoneQuarry":quarry,"OilWell":oilwell,"Sawmill":sawmill}[t](m)
    return m


if __name__=="__main__":
    publish_package(PACKAGE,build)
