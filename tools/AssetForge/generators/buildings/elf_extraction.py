"""Elven quarries, shaft houses, oil pumps and timber yards."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_food import Grove, TAU, publish_package
from generators.buildings.elf_trade_forms import arch, block, gable, shaft

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


def washing_mine(m):
    # A freestanding stone wheelhouse spans the stair of wash trays. Its mill wheel
    # and descending, water-filled launders remain visible through the central arch.
    cx=-.165
    arch(m,cx,0,.16,.99,1.16,.30)
    for side in (-1,1):
        m.disk("fieldstone",cx+side*.406,0,.167,.158,.24,.124,.92)
    wheel(m,cx,.651,-.014,.293,role="timber")
    shaft(m,"metal",(cx,.652,-.09),(cx,.652,.435),.061)
    for x in (cx-.27,cx+.27):
        shaft(m,"trim",(x,.485,.178),(cx,.646,.362),.035,.046)
    # Each progressively lower launder discharges into the next one.
    for i in range(3):
        z=-.092-i*.235
        y=.374-i*.109
        block(m,"fieldstone",(cx,y*.5,z),(.263,y,.272),.10)
        block(m,"water",(cx,y+.008,z),(.175,.018,.247))
        if m.lod<2:
            for s in (-1,1):
                block(m,"trim",(cx+s*.111,y+.022,z),(.037,.083,.265))
    m.disk("metal",cx,.0,-.777,.193,.174,.106,1.10)
    m.disk("gold",cx,.103,-.777,.177,.161,.026)
    cliff(m,-.586,.494,.346,.295,.47,2,"fieldstone")
    block(m,"wall",(.514,.14,.385),(.397,.28,.346),.08)
    gable(m,.514,.275,.385,.521,.463,.213)
    if m.lod<2:
        m.portal(.514,.006,.208,.176,.216)
        shaft(m,"timber",(.696,.051,.223),(.687,.301,.236),.027,.019)
        ore_cart(m,.570,-.262,"gold")
    else:
        m.disk("timber",.569,0,-.262,.138,.166,.183,1.18)
        m.lump("gold",(.569,.183,-.262),(.125,.095,.133),5)
    if m.lod<2:
        for i in (-1,0,1):
            m.lump("gold",(cx+i*.068,.132,-.791+.030*(i==0)),(.043,.028,.049),5)


def adit(m):
    # A deep pointed entry sits below the stepped rock face. Stone steps and
    # an offset ore yard give the winding engine its own distinct route.
    cliff(m,.016,.426,.637,.420,.711,3,"fieldstone")
    arch(m,.151,0,-.045,.847,.746,.346)
    m.portal(.151,.033,.018,.47,.572)
    for i in range(3):
        block(m,"fieldstone",(.151,.036+i*.031,-.529+.146*i),
              (.59-.057*i,.072+.062*i,.153))
    m.disk("fieldstone",-.589,0,-.121,.251,.347,.104,1)
    for x in (-.724,-.415):
        block(m,"timber",(x,.229,-.155),(.070,.29,.224),.13)
    shaft(m,"timber",(-.773,.355,-.153),(-.364,.355,-.153),.123)
    for x in (-.751,-.396):
        shaft(m,"trim",(x-.016,.355,-.153),(x+.016,.355,-.153),.172)
    if m.lod<2:
        for x in (-.637,-.567,-.497):
            shaft(m,"hide",(x-.014,.355,-.153),(x+.014,.355,-.153),.137)
        shaft(m,"hide",(-.552,.463,-.154),(.136,.431,.019),.013)
    with m.transform(translate=(.624,.022,-.175),rotate_y=-19):
        block(m,"timber",(0,.035,0),(.386,.11,.707))
        with m.transform(translate=(0,.09,0),scale=.86):
            if m.lod<2:
                ore_cart(m,0,0,"metal")
            else:
                m.disk("timber",0,0,0,.145,.16,.184,1.18)
                m.lump("metal",(0,.183,0),(.122,.088,.130),5)
    if not m.lod:
        m.lump("emissive",(-.23,.394,-.216),(.037,.060,.038))


def skip_mine(m):
    # The winding house is lifted on a stout brick base. An exposed haulage skip
    # travels in a separate braced steel frame, above the ore bunker and cart.
    block(m,"brick",(-.36,.48,.216),(.521,.96,.637),.09)
    block(m,"glass",(-.36,1.064,.216),(.535,.221,.647),.035)
    block(m,"trim",(-.36,.954,.216),(.575,.055,.667),.02)
    if m.lod<2:
        for dx in (-.19,0,.19):
            shaft(m,"metal",(-.36+dx,.974,-.111),(-.36+dx,1.167,-.101),.018)
        for dz in (-.18,.16):
            shaft(m,"metal",(-.633,.976,.216+dz),(-.618,1.169,.216+dz),.020)
    gable(m,-.36,1.170,.216,.652,.811,.26)
    m.portal(-.36,.015,-.109,.291,.449)
    for s in (-1,1):
        shaft(m,"trim",(-.36+s*.247,.199,-.112),(-.36+s*.228,.927,-.082),.035,.027)
    tx,tz=.332,-.079
    for side in (-1,1):
        x=tx+side*.165
        for z in (tz-.121,tz+.121):
            shaft(m,"metal",(x,0,z),(x,1.40,z),.047,.032)
        if m.lod<2:
            shaft(m,"trim",(x,.151,tz-.117),(x,.659,tz+.117),.027)
            shaft(m,"trim",(x,.647,tz+.117),(x,1.18,tz-.117),.027)
    block(m,"roof",(tx,1.39,tz),(.452,.092,.359),.08)
    wheel(m,tx,1.448,tz-.017,.157)
    shaft(m,"trim",(-.13,1.097,.047),(tx-.038,1.554,tz-.02),.021)
    shaft(m,"trim",(tx+.144,1.459,tz-.054),(tx+.144,.69,tz-.054),.015)
    block(m,"metal",(tx,.573,tz),(.220,.268,.161),.07)
    # The sloped discharge chute emerges from the bunker above the pickup point.
    m.disk("brick",.653,0,.290,.225,.211,.165)
    m.disk("metal",.653,.155,.290,.129,.135,.354,2.12)
    m.disk("slate",.653,.506,.290,.252,.258,.074,.54)
    shaft(m,"trim",(.65,.261,.226),(.686,.172,-.264),.068,.08)
    if m.lod<2:
        with m.transform(translate=(.60,0,-.458),scale=.82):
            ore_cart(m,0,0,"metal")


def conduit_mine(m):
    # A rotary cutter at the foot of a low reinforced extraction hood feeds a
    # sloping transparent ore tube. Twin pylons support the receiving head.
    x,z=-.493,.071
    m.disk("fieldstone",x,0,z,.411,.379,.143,.94)
    m.disk("wall",x,.137,z,.351,.32,.415,.89)
    m.crown(x,.544,z,.371,.358,.205,"roof",5)
    with m.transform(translate=(x,.306,-.212),rotate_x=-76):
        m.disk("metal",0,0,0,.243,.231,.097,.89)
        m.disk("emissive",0,.094,0,.168,.159,.073,.75)
        m.disk("metal",0,.163,0,.105,.101,.089,.12)
    for side in (-1,1):
        m.limb("wall",[(.502+side*.265,.019,.232),(.502+side*.210,.523,.213),
                        (.502+side*.095,1.101,.181)],[.097,.069,.080])
        m.disk("fieldstone",.502+side*.262,0,.232,.127,.129,.087,.93)
    m.disk("wall",.502,.924,.181,.228,.236,.327,1.21)
    m.disk("glass",.502,1.238,.181,.262,.27,.149,.79)
    m.crown(.502,1.374,.181,.271,.261,.189,"roof",5)
    a=(-.458,.458,.051)
    b=(.500,1.089,.145)
    shaft(m,"glass",a,b,.108)
    for t in ((.18,.40,.63,.84) if m.lod<2 else (.27,.71)):
        pts=[tuple(f+(g-f)*u for f,g in zip(a,b)) for u in (t-.023,t+.023)]
        shaft(m,"trim",*pts,.127)
    m.disk("metal",.482,0,-.306,.219,.207,.137,1.19)
    for i in range((4,3,1)[m.lod]):
        ang=1.63*i
        m.lump("metal",(.476+.098*math.cos(ang),.146,-.305+.092*math.sin(ang)),
               (.088,.067,.084),5)
    m.limb("accent",[(.501,.948,.172),(.496,.709,.078),(.480,.441,-.249)],
           [.085,.064,.061])


def stamp_mine(m):
    # The adit opens beside a steam stamp battery. Three falling shoes grind the
    # ore in a common mortar, which discharges into the riffled gold launder.
    cliff(m,-.51,.36,.38,.37,.57,1 if m.lod==2 else 2,"fieldstone")
    arch(m,-.535,0,.052,.508,.498,.205,"wall")
    block(m,"door",(-.535,.142,.074),(.335,.284,.073))
    if m.lod<2:
        for x in (-.634,-.437):
            block(m,"metal",(x,.021,-.202),(.027,.041,.545))
        for z in (-.38,-.229,-.089):
            block(m,"timber",(-.538,.011,z),(.305,.022,.047))

    # The round boiler house and bonded brick flue occupy the back of the works.
    m.disk("brick",.347,0,.395,.365,.293,.398,.97)
    m.crown(.347,.387,.395,.401,.323,.146,"roof",5)
    m.disk("brick",.421,.418,.495,.144,.141,.806,.69)
    if m.lod<2:
        m.disk("trim",.421,1.210,.495,.102,.100,.087,1.27)
        m.disk("metal",.421,1.294,.495,.130,.127,.065,.99)
        m.disk("door",.421,1.355,.495,.096,.092,.007,.98)
    else:
        m.disk("metal",.421,1.210,.495,.102,.100,.149,1.23)
    if not m.lod:
        for y,r in ((.721,.133),(1.004,.115)):
            m.disk("metal",.421,y,.495,r,r*.98,.023,.99)

    # Curved cheeks spring from separate piers and carry the raised guide beam.
    for x in (-.30,.389):
        block(m,"brick",(x,.116,-.159),(.159,.232,.27),.08)
        feet=(x,.217,-.152)
        shoulder=(x-.066,.900,-.187)
        if m.lod<2:
            m.limb("trim",[feet,(x,.515,-.146),(x-.033,.831,-.160),shoulder],
                   [.067,.052,.045,.043],(8,5)[m.lod])
        else:
            m.limb("trim",[feet,shoulder],[.067,.043],4)
    if m.lod<2:
        block(m,"metal",(.014,.493,-.148),(.863,.102,.083),.05)
    block(m,"trim",(-.013,.862,-.168),(.813,.097,.095),.035)
    block(m,"fieldstone",(.048,.128,-.299),(.763,.256,.258),.055)
    if m.lod<2:
        block(m,"metal",(.048,.253,-.295),(.730,.074,.235),.025)
    for i,x in enumerate((-.167,.038,.242)):
        shoe=(.313,.349,.301)[i]
        block(m,"metal",(x,(shoe+.984)*.5,-.221),(.058,.984-shoe,.064))
        block(m,"slate",(x,shoe,-.245),(.123,.104,.124),.035)
        if m.lod<2:
            block(m,"gold",(x,.906,-.222),(.097,.057,.096),.06)
    shaft(m,"metal",(-.359,.51,-.127),(.596,.51,-.127),.046)
    wheel(m,.547,.510,-.127,.296,axis="x",role="metal")
    if m.lod<2:
        shaft(m,"metal",(.543,.452,.001),(.48,.315,.28),.032)
        # The connecting rod runs into the steam cylinder on the boiler saddle.
        shaft(m,"trim",(.49,.315,.256),(.452,.304,.40),.073)

    # Paired timber bents carry the wash box down to a clean-up trough.
    for z,y in ((-.415,.23),(-.742,.123)):
        block(m,"timber",(.083,y*.5,z),(.35,y,.059),.07)
    with m.transform(translate=(.083,.182,-.581),rotate_x=-18):
        block(m,"timber",(0,0,0),(.422,.049,.459))
        if m.lod<2:
            block(m,"water",(0,.027,0),(.348,.013,.443))
        else:
            m.face("water",[(-.174,.031,-.221),(-.174,.031,.221),
                            (.174,.031,.221),(.174,.031,-.221)])
        for s in (-1,1):
            block(m,"trim",(s*.196,.044,0),(.038,.071,.461))
        if m.lod<2:
            for z in (-.13,-.017,.094):
                block(m,"gold",(0,.038,z),(.341,.014,.018))
    m.disk("trim",.085,0,-.820,.207,.126,.077,1.04)
    m.disk("gold",.085,.074,-.820,.193,.115,.023,.96)
    if m.lod<2:
        for x,z,r in ((.032,-.843,.048),(.11,-.859,.060),(.151,-.807,.038)):
            m.lump("gold",(x,.104,z),(r,r*.60,r*.72),5)


def mine(m,kind):
    if kind=="GoldMine" and m.era==2:
        stamp_mine(m)
        return
    if kind in ("GoldMine","CoalMine") and m.era==3:
        sorting_works(m,kind)
        return
    if kind == "GoldMine" and m.era == 1:
        washing_mine(m)
        return
    if kind == "Mine" and m.era:
        (adit,skip_mine,conduit_mine)[m.era-1](m)
        return
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


def sorting_works(m, kind):
    if kind=="CoalMine":
        # An enclosed incline runs over the portal to the open coal loading pocket.
        cliff(m,-.34,.28,.37,.31,.68,1 if m.lod==2 else 2,"slate")
        arch(m,-.33,0,-.067,.46,.47,.22,"ivory")
        block(m,"door",(-.33,.145,-.06),(.27,.29,.085))
        for x,z,h in ((.035,.28,.66),(.47,-.31,.37)):
            block(m,"metal",(x,h/2,z),(.10,h,.10))
        m.limb("trim",[(.028,.67,.28),(.27,.55,-.02),(.50,.37,-.33)],
               [.119,.116,.11],(9,6,4)[m.lod])
        m.disk("metal",.45,0,-.42,.28,.25,.25,1.15)
        heap=[(.43,-.40,.36),(.31,-.47,.30),(.56,-.47,.32)]
        for x,z,h in (heap[:1] if m.lod==2 else heap):
            m.lump("door",(x,.245,z),(.14,h-.12,.13),6)
        m.disk("ivory",-.025,.58,.26,.18,.18,.25,.89)
        m.crown(-.025,.81,.26,.22,.20,.20,"roof",4)
        if m.lod<2:
            m.leaf("accent",(-.14,.78,.25),(-.31,1.09,.35),.07,.05)
        return
    # A split rock cut feeds a bank of descending centrifugal gold pans.
    cliff(m,-.33,.39,.34,.29,.56,1 if m.lod==2 else 2,"fieldstone")
    arch(m,-.32,0,.14,.46,.38,.20,"wall")
    block(m,"door",(-.32,.11,.14),(.27,.22,.08))
    for i,(x,z,r,h) in enumerate(((.32,.32,.33,.72),(.27,-.13,.25,.42),(.12,-.52,.19,.23))):
        m.disk("ivory",x,0,z,r*.45,r*.48,h*.69,.87,(12,8,4)[m.lod])
        m.disk("metal",x,h*.67,z,r*.52,r*.52,h*.28,1.86)
        m.disk("gold",x,h*.94,z,r*.97,r*.97,h*.065,1.02)
        if m.lod<2 or i==0:
            m.disk("water",x,h*1.004,z,r*.80,r*.81,.013,.99)
        if i<2 and m.lod<2:
            m.limb("water",[(x,h*.94,z-r*.80),(x-.06,h*.66,z-r-.07)],
                   [.042,.037],5)
    jib(m,-.56,0,-.31,.37,.64)
    if m.lod==0:
        for x in (-.37,-.23):
            m.lump("gold",(x,.071,-.43),(.08,.07,.09),6)


def oilwell(m):
    if m.era==3:
        # An open three-legged rotary rig drives the well below the crown block.
        x,z=-.28,.10
        for j in range(3):
            a=TAU*j/3-.70
            dx,dz=math.cos(a),math.sin(a)
            m.disk("wall",x+.43*dx,0,z+.43*dz,.13,.13,.14)
            m.limb("metal",[(x+.43*dx,.085,z+.43*dz),(x+.28*dx,.91,z+.28*dz),
                            (x+.075*dx,1.69,z+.075*dz)],[.079,.053,.050])
        m.disk("roof",x,1.61,z,.18,.16,.155,.78)
        m.disk("metal",x,.12,z,.040,.040,1.58)
        m.disk("trim",x,0,z,.208,.18,.23,.70)
        for j in range(2):
            tx,tz=.50,.33-.49*j
            m.disk("wall",tx,0,tz,.215,.20,.45+.15*j,.92)
            m.crown(tx,.44+.15*j,tz,.22,.20,.12,"metal",4)
            m.limb("trim",[(tx,.23,tz),(tx-.21,.23,tz),(x,.13,z)],[.037,.039,.035])
        m.hall(-.12,0,-.48,.24,.20,.33,.16)
        return
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
