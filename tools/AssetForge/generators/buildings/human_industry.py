"""Human workshops. The working face of each building opens onto the town street."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House

PACKAGE = "human_industry"
ERAS = ("ancient", "classical", "industrial", "future")


def band(m, mat, x, z, w, d, bottom, top, bevel=.04):
    m.course(mat, x, z, w, d, bottom, top, corner=bevel)


def pane(m, mat, x, y, z, w, h):
    m.face(mat, [(x+w/2,y-h/2,z),(x-w/2,y-h/2,z),
                 (x-w/2,y+h/2,z),(x+w/2,y+h/2,z)])


def turned(m, x, z, profile, divisions=(12,8,6), well=None):
    """Join profile rings into a single shell, including the dark recessed throat of a flue."""
    n = divisions[m.lod]
    def ring(y, radius):
        return [(x+radius*math.cos((i+.5)*2*math.pi/n), y,
                 z+radius*math.sin((i+.5)*2*math.pi/n)) for i in range(n)]
    rings = [ring(y,r) for y,r,_ in profile]
    for lower, upper, (_, _, mat) in zip(rings, rings[1:], profile[1:]):
        for i in range(n):
            j = (i+1)%n
            m.face(mat, (lower[j],lower[i],upper[i],upper[j]))
    y,r,mat = profile[-1]
    if well and m.lod != 2:
        inner = ring(y, r*.69)
        lower = ring(y-r*.32,r*.65)
        for i in range(n):
            j = (i+1)%n
            m.face(mat,(rings[-1][j],rings[-1][i],inner[i],inner[j]))
            m.face("door",(inner[j],inner[i],lower[i],lower[j]))
        m.face(well,lower[::-1])
    else:
        m.face(well or mat, rings[-1][::-1])


def flue(m, x, z, base, height, r, era):
    body = ("plaster","fieldstone","brick","ivory")[era]
    trim = "trim" if era<2 else "metal"
    profile = [(base,r,None),(base+height*.75,r*.70,body)]
    if m.lod<2:
        profile += [(base+height*.80,r*.72,trim),(base+height*.89,r*.68,body),
                    (base+height*.91,r*.86,trim)]
    profile += [(base+height,r*(.84 if m.lod<2 else .72),body)]
    turned(m,x,z,profile,(10,7,5),"door")


def roof(m, x, z, w, d, eave, rise, era, hip=False):
    with m.transform(translate=(x,0,z)):
        if era < 2:
            m.roof(w,d,eave,rise,"roof","wall",hip=d*.15 if hip else 0,
                   ridge_mat="trim" if m.lod<2 else None)
        elif era == 2:
            m.prism_x((0,eave,0),w,d,rise,m.color("roof"),material="roof")
            if m.lod < 2:
                m.block("metal",(0,eave+rise,0),(w+.012,.032,.048))
        else:
            band(m,"ivory",0,0,w,d,eave-.005,eave+.074,.076)
            band(m,"glass",0,.012,w*.86,d*.81,eave+.074,eave+rise*.84,.068)
            band(m,"roof",0,.012,w*.91,d*.87,eave+rise*.84,eave+rise,.067)


def sash(m,x,z,y,w,h,era):
    pane(m,"trim",x,y,z-.005,w+.035,h+.035)
    pane(m,"glass" if era>=2 else "emissive",x,y,z-.011,w,h)
    if m.lod<2:
        for u in ((-.25,0,.25) if m.lod==0 and w>.45 else (0,)):
            m.block("ivory" if era>=2 else "trim",(x+w*u,y,z-.017),(.018,h+.012,.018))
        if m.lod==0:
            m.block("banner" if era>=2 else "timber",(x,y-h*.53,z-.015),
                    (w+.071,.036,.048))


def hall(m,x,z,w,d,eave,rise,era,door_x,hip=False,window=True):
    front = z-d/2
    band(m,"fieldstone",x,z,w+.036,d+.026,.030,.11)
    band(m,"wall",x,z,w,d,.098,eave, .025 if era<3 else .069)
    roof(m,x,z,w+.085,d+.074,eave,rise,era,hip)
    m.entry(door_x,front,.17 if w<.8 else .23,.098,min(.34,eave*.62))
    if m.lod<2:
        if window:
            light_x=x+w*.25 if door_x<=x else x-w*.25
            sash(m,light_x,front,eave*.63,.16 if w<.8 else .245,.11,era)
        if era<2:
            for s in (-1,1):
                m.block("timber",(x+s*(w*.5-.045),(eave+.107)*.5,front-.01),
                        (.039,eave-.102,.043))
            # The ties stand proud of the lintel wherever their vertical courses meet.
            lintel_y=.107 + min(.34,eave*.62)
            tie_z=front-(.024 if abs(eave-.046-lintel_y)<.040 else .019)
            m.block("trim",(x,eave-.046,tie_z),(w,.039,.038))
        elif era==2:
            m.block("ivory",(x,eave-.072,front-.013),(w-.04,.054,.044))
        if m.lod==0:
            with m.transform(translate=(x+w/2,0,z),rotate_y=-90):
                sash(m,-.03,-.004,eave*.65,d*.42,.127,era)


def flag(m,x,z,y,h=.33):
    m.beam("trim",(x,y,z),(x,y+h,z),.023)
    tip=y+h-.018
    points=[(x,tip,z),(x+.08,tip-.012,z+.019),(x+.17,tip-.047,z-.004),
            (x+.081,tip-.097,z+.019),(x,tip-.095,z)]
    for ids in ((0,1,3,4),(1,2,3)):
        pts=[points[i] for i in ids]
        m.face("banner",pts[::-1])
        m.face("banner",[(a,b,c+.008) for a,b,c in pts])


def shield(m,x,y,z,r):
    """A blue maker's escutcheon with a raised gold rim and a folded face."""
    outline=[(-.79,.58),(-.67,.80),(.64,.80),(.80,.56),(.65,-.20),(0,-.86),(-.65,-.20)]
    outer=[(x+r*u,y+r*v,z) for u,v in outline]
    inner=[(x+r*.81*u,y+r*.81*v,z-.012) for u,v in outline]
    boss=(x,y+r*.15,z-.037)
    for i in range(len(outer)):
        j=(i+1)%len(outer)
        m.face("gold",(outer[i],outer[j],inner[j],inner[i]))
        m.face("banner",(inner[i],inner[j],boss))


def armory(m,era):
    # Secure vaulted stockroom behind a shielded gate and paired masonry piers.
    band(m,"fieldstone",0,.015,1.81,.83,0,.065,.10)
    h=(.47,.59,.48,.55)[era]
    hall(m,-.08,.055,1.29,.64,h,(.32,.44,.18,.18)[era],era,-.07,False,False)
    for s in (-1,1):
        x=s*.665
        z=-.025 if s>0 else .09
        top=h+(.075 if s>0 else -.03)
        band(m,"fieldstone" if era<2 else "wall",x,z,.32,.59,.063,top,.063)
        if era<2:
            m.pyramid((x,top,z),.395,.66,.30 if era==1 else .23,m.color("roof"),material="roof")
        else:
            band(m,"trim",x,z,.354,.62,top,top+.062)
            band(m,"metal" if era==2 else "glass",x,z,.284,.48,top+.062,top+.145)
        if m.lod!=2:
            sash(m,x,z-.295,top*.63,.097,.14,era)
    shield_y=h+(.108 if era<2 else -.008)
    shield(m,-.075,shield_y,-.306 if era<2 else -.318,.169)
    if era<2:
        m.block("timber",(-.075,shield_y,-.281),(.218,.128,.084))
    else:
        m.block("trim",(-.075,shield_y,-.282),(.198,.114,.063))
    if m.lod < 2:
        for x in (-.395,.235):
            pane(m,"banner",x,h*.60,-.273,.09,.22)
            m.block("gold",(x,h*.60+.12,-.278),(.12,.023,.029))
        if era>=2:
            sash(m,.16,.055-.32,h*.57,.095,.10,era)
    if m.lod==0:
        if era<2:
            flag(m,.667,-.024,top+.19,.25)
            # Give the large gable its own truss and a short king post behind the guild arms.
            rise=.32 if era==0 else .44
            for side in (-1,1):
                m.beam("timber",(-.08+side*.59,h+.012,-.29),
                       (-.08+side*.07,h+rise*.85,-.29),.041)
            m.beam("timber",(-.08,h+.01,-.288),(-.08,h+rise*.87,-.288),.043)
        else:
            # The side annexes are secure ventilated stores; their hoods cover the slit.
            for x,z in ((.665,-.025),(-.665,.09)):
                m.block("trim",(x,h*.70+.05,z-.307),(.178,.026,.076))
        with m.transform(translate=(.52,.064,-.331),rotate_x=-11):
            m.block("timber",(0,.019,0),(.352,.038,.088))
            for i in (-1,1):
                m.block("timber",(i*.111,.081,0),(.028,.126,.039))
                shield(m,i*.094,.116,-.029,.104)
        for x in (-.29,.145):
            m.block("metal",(x,.174,-.31),(.059,.22,.053),taper=.18)
        m.crate((-.80,.067,-.258),.13)


def furnace(m,x,z,scale,era,short=False):
    """A ring of fired masonry around a charging shaft with its tap arch toward the yard."""
    with m.transform(translate=(x,.045,z),scale=scale):
        body=("plaster","fieldstone","brick","metal")[era]
        lip="gold" if era==3 else "trim"
        h=(.78,.99,1.16,1.05)[era] * (.72 if short else 1)
        profile=[(0,.25,None),(.27*h,.285,body),(.61*h,.21,body)]
        if m.lod<2:
            profile += [(.64*h,.221,lip),(.69*h,.207,body)]
        profile += [(.87*h,.142,body),(h,.155,lip)]
        turned(m,0,0,profile,(12,8,6),"forge")
        band(m,body,0,-.16,.38,.33,.002,.245*h,.047)
        front=-.327
        width=.117
        for mat,k,offset in (("door",1.16,0),("forge",.85,-.009)):
            r=width*k
            points=[(r,.039,front+offset),(-r,.039,front+offset),(-r,.135*h,front+offset),
                    (-r*.72,.215*h,front+offset),(0,.245*h,front+offset),
                    (r*.72,.215*h,front+offset),(r,.135*h,front+offset)]
            m.face(mat,points)
        if m.lod<2:
            m.block("metal",(0,.038,front-.027),(.256,.039,.118),taper=.22)
            if m.lod==0:
                for side in (-1,1):
                    m.beam(lip,(side*.251,.13,.034),(side*.166,.71*h,.038),.030)
                for y in (.34,.51):
                    m.block(lip,(0,y*h,-(.269-y*.11)),(.087,.024,.053))


def anvil(m,x,z,scale,era):
    with m.transform(translate=(x,.059,z),scale=scale):
        turned(m,0,0,[(0,.117,None),(.16,.102,"timber" if era<2 else "fieldstone")],(8,6,4))
        for y,w,d,h in ((.178,.208,.155,.040),(.210,.098,.122,.030),(.254,.231,.14,.060)):
            m.block("metal",(0,y,0),(w,h,d))
        # A forged tapered nose extends from the flat crown.
        with m.transform(translate=(.113,.255,0),rotate_z=-90):
            m.cylinder((0,0,0),.054,.109,m.color("metal"),segments=5,top_radius=.005,material="metal")


def ingots(m,x,z,era,scale=1):
    with m.transform(translate=(x,.049,z),scale=scale):
        for i in range(3 if m.lod==0 else 2):
            m.block("metal",((i-1)*.100,.036,0),(.086,.066,.198),taper=.21)
        if m.lod==0:
            m.block("accent",(-.045,.091,.005),(.083,.049,.179),taper=.20)


def smith(m,era):
    band(m,"fieldstone",0,0,1.85,.87,0,.058,.085)
    hall(m,-.415,.057,.82,.62,(.421,.52,.52,.54)[era],(.30,.36,.15,.16)[era],era,-.50,True)
    furnace(m,.375,.202,.96,era,short=True)
    # Open-sided shoeing bay in front of the fire, with a deep practical hood.
    cover=("hide","roof","metal","glass")[era]
    h=(.42,.50,.50,.54)[era]
    for x in (-.007,.755):
        m.block("timber" if era<2 else "metal",(x,(h+.056)*.5,-.244),(.050,h-.056,.062))
    for x in (-.007,.755):
        m.block("trim",(x,(h+.10)*.5,.075),(.045,h-.012,.052))
    with m.transform(translate=(.372,h,-.109),rotate_x=-13):
        m.box((0,0,0),(.882,.048,.421),m.color(cover),material=cover,bottom=True)
    anvil(m,.39,-.293,.95,era)
    if m.lod<2:
        for x in (-.007,.755):
            m.beam("trim",(x,h-.12,-.24),(x,h+.018,-.105),.031)
        if era>=2:
            flue(m,-.66,.23,.42,.48,.089,era)
        if m.lod==0:
            ingots(m,.73,-.09,era,.63)
            turned(m,.085,-.327,[(.058,.065,None),(.141,.083,"timber"),(.163,.083,"metal")],
                   (8,6,4),"water")
            m.block("timber",(.29,.339,-.295),(.119,.024,.025))
            m.block("metal",(.241,.35,-.295),(.05,.05,.05))


def smelter(m,era):
    band(m,"fieldstone",0,0,1.86,.88,0,.048,.085)
    furnace(m,.286,.04,1.13,era)
    hall(m,-.47,.07,.66,.53,.42,.22 if era<2 else .115,era,-.51,True,False)
    if era>=1:
        flue(m,-.738,.233,.28,.61+era*.115,.079 if era<3 else .098,era)
    if era>=2:
        for x in (.68,.81):
            m.block("metal",(x,.223,.22),(.054,.35,.066))
        m.block("metal",(.745,.426,.22),(.233,.082,.218))
        m.beam("metal",(.71,.442,.22),(.46,.67,.12),.076)
    if m.lod<2:
        ingots(m,-.057,-.288,era,.82)
        # Poker and charging bucket occupy the dry side of the tap apron.
        turned(m,.744,-.121,[(.051,.072,None),(.201,.089,"metal")],(9,6,4),"door")
        if m.lod==0:
            m.beam("timber",(.74,.16,-.12),(.81,.40,-.072),.024)
            flag(m,-.72,.024,.396,.27)


def dyed_web(m,x,z,width,top,bottom,mat):
    # A hanging bolt wraps from a fixed roller, turning a visible corner at its weighted hem.
    points=[(-.5,top,z),(.5,top,z),(.5,bottom+.023,z-.035),(-.5,bottom,z-.057)]
    front=[(x+width*u,y,v) for u,y,v in points]
    m.face(mat,front)
    m.face(mat,[(a,b,c+.008) for a,b,c in front[::-1]])
    if m.lod<2:
        stripe=[(a,b+.031,c-.002) if i>=2 else None for i,(a,b,c) in enumerate(front)]
        m.face("ivory",[front[3],stripe[3],stripe[2],front[2]])


def weaver(m,era):
    band(m,"fieldstone",0,0,.91,.88,0,.045,.065)
    hall(m,-.172,.09,.465,.60,.42,.27 if era<2 else .15,era,-.25,era==0,False)
    if era==1:
        band(m,"wall",-.19,.20,.16,.21,.59,.71,.018)
        m.hip_roof((-.19,.71,.20),.22,.25,.109,m.color("roof"),overhang=0,material="roof")
        if m.lod<2:
            pane(m,"door",-.19,.672,.09,.087,.053)
    # Raised outdoor drying frame and two distinct finished bolts form the street sign.
    mat="timber" if era<2 else "metal"
    high=(.57,.68,.72,.72)[era]
    for x in (.052,.421):
        m.beam(mat,(x,.04,-.189),(x,high,-.123),.037)
        if m.lod<2:
            m.beam(mat,(x,.05,.22),(x,high,-.123),.028)
    m.beam(mat,(.025,high,-.123),(.448,high,-.123),.04)
    dyed_web(m,.143,-.143,.139,high-.005,high*.38,"banner")
    dyed_web(m,.316,-.142,.125,high-.004,high*.25,"cloth")
    if m.lod!=2:
        # Comb teeth and the shuttle sit beneath the short web on an angled weaving bed.
        for u in (.07,.213):
            m.beam(mat,(u,.093,-.346),(u,high*.44,-.178),.022)
            for z,top in ((-.346,.095),(-.178,high*.44)):
                m.block(mat,(u,(.044+top)*.5,z),(.028,top-.044,.027))
        for i in range(5 if m.lod==0 else 3):
            x=.082+i*(.12/(4 if m.lod==0 else 2))
            m.beam("ivory",(x,.102,-.336),(x,high*.43,-.181),.009)
        m.beam("timber",(.059,.122,-.326),(.227,.132,-.322),.025)
    if era>=2:
        band(m,"metal",.235,.149,.33,.29,.045,.33)
        turned(m,.242,.15,[(.33,.103,None),(.38,.111,"trim"),(.46,.087,"ivory")],(9,6,4))
    if m.lod<2:
        # The dyer's vat is a squat, open coopered vessel.
        turned(m,-.020,-.304,[(.044,.063,None),(.125,.083,"timber"),(.16,.080,"trim")],(9,7,5),"turquoise")
        if m.lod==0:
            for x in (.15,.30):
                turned(m,x,-.259,[(.046,.052,None),(.068,.057,"timber"),(.075,.045,"timber"),
                                    (.143,.044,"cloth" if x>.2 else "ivory"),(.151,.052,"timber")],(8,6,4))
            m.beam("trim",(-.033,.12,-.30),(.018,.243,-.27),.012)


def wheel(m,x,y,z,r,era):
    n=(12,8,6)[m.lod]
    mat="timber" if era==1 else "metal"
    for i in range(n):
        a,b=math.tau*i/n,math.tau*(i+1)/n
        def p(angle,k,depth):
            return (x+r*k*math.cos(angle),y+r*k*math.sin(angle),z+depth)
        l,h=-.027,.027
        m.face(mat,(p(a,1,l),p(a,1,h),p(b,1,h),p(b,1,l))[::-1])
        m.face(mat,(p(a,.78,l),p(b,.78,l),p(b,.78,h),p(a,.78,h))[::-1])
        m.face(mat,(p(a,1,l),p(b,1,l),p(b,.78,l),p(a,.78,l))[::-1])
        m.face(mat,(p(a,1,h),p(b,1,h),p(b,.78,h),p(a,.78,h)))
    count=6 if m.lod==0 else 4
    for i in range(count):
        angle=math.tau*i/count+.27
        dx,dy=math.cos(angle),math.sin(angle)
        m.beam(mat,(x+.050*dx,y+.050*dy,z),(x+r*.82*dx,y+r*.82*dy,z),.036)
    with m.transform(translate=(x,y,z+.036),rotate_x=-90):
        turned(m,0,0,[(0,.064,None),(.083,.058,"accent")],(7,6,4))


def workshop(m,era):
    band(m,"fieldstone",0,.025,1.75,1.63,0,.062,.12)
    hall(m,-.26,.266,.98,.84,.62,.39 if era==1 else .22,era,-.43,True)
    # A wheelwright's gantry carries the wheel spindle and the hoist over the workbench.
    mat="timber" if era==1 else "metal"
    for x in (.28,.71):
        m.beam(mat,(x,.062,-.44),(x,.98,-.34),.069)
        if m.lod<2:
            m.beam(mat,(x,.062,.25),(x,.98,-.34),.062)
    m.beam(mat,(.18,.987,-.34),(.82,.987,-.34),.09)
    m.beam("trim",(.59,.947,-.34),(.59,.54,-.34),.025)
    m.block(mat,(.466,.144,-.383),(.48,.166,.25))
    m.block("timber",(.463,.256,-.383),(.56,.066,.29))
    if m.lod<2:
        wheel(m,.38,.327,-.541,.245,era)
    else:
        with m.transform(translate=(.38,.327,-.512),rotate_x=-90):
            m.cylinder((0,0,0),.245,.056,m.color(mat),segments=8,material=mat)
            m.cylinder((0,.056,0),.095,.013,m.color("trim"),segments=6,material="trim")
    if era==2:
        flue(m,-.50,.53,.39,.72,.096,era)
    if m.lod<2:
        ingots(m,-.16,-.49,era,1)
        if m.lod==0:
            m.crate((-.65,.063,-.39),.203)
            # Two braces hold the hoist truss square and a treenail box sits under the spindle.
            for px in (.28,.71):
                end=px+(.15 if px<.5 else -.15)
                m.beam(mat,(px,.77,-.36),(end,.988,-.34),.043)
            m.block("timber",(.692,.105,-.632),(.15,.085,.17),taper=.09)
            flag(m,-.63,.17,.633,.32)


def sheds(m,x,z,w,d,base,height,count,era):
    # North lights have a solid pitched back and a near-vertical glazed face toward the street.
    for i in range(count):
        left=x-w/2
        right=x+w/2
        near=z-d/2+i*d/count
        far=near+d/count
        crest=near+(far-near)*.17
        y=base+height
        m.face("glass",[(right,base,near),(left,base,near),
                                                (left,y,crest),(right,y,crest)])
        m.face("roof",[(left,y,crest),(left,base,far),(right,base,far),(right,y,crest)])
        for end in (left,right):
            pts=[(end,base,near),(end,y,crest),(end,base,far)]
            m.face("wall",pts if end==right else pts[::-1])
        if m.lod<2:
            m.block("metal" if era==2 else "ivory",(x,y,crest),(w+.025,.033,.05))
        if m.lod==0:
            for u in (-.3,0,.3):
                m.beam("ivory",(x+w*u,base,near-.005),(x+w*u,y,crest-.005),.022)


def loading(m,x,z,w,h,era):
    pane(m,"trim",x,.082+h/2,z-.006,w+.06,h+.051)
    pane(m,"metal",x,.082+h/2,z-.012,w,h)
    if m.lod<2:
        for k in range(1,5 if m.lod==0 else 3):
            y=.082+h*k/(5 if m.lod==0 else 3)
            pane(m,"trim",x,y,z-.016,w-.012,.015)
        m.block("accent",(x,.1+h,z-.035),(w+.091,.045,.072))


def factory(m,era):
    band(m,"fieldstone",0,0,2.70,2.58,0,.086,.13)
    x,z=.09,.16
    w,d=1.70,1.76
    h=.72 if era==2 else .78
    band(m,"wall",x,z,w,d,.086,h,.052)
    sheds(m,x,z,w+.08,d+.059,h,.34 if era==2 else .24,3,era)
    front=z-d/2
    for dx in (-.38,.39):
        loading(m,x+dx,front,.44,.43,era)
    if m.lod<2:
        with m.transform(translate=(x+w/2,0,z),rotate_y=-90):
            for v in (-.55,0,.55):
                sash(m,v,-.005,h*.64,.37,.25,era)
    # A gatehouse and boiler house share the main building's services.
    hall(m,-.80,-.44,.64,.68,.49,.20 if era==2 else .16,era,-.88,True,False)
    band(m,"brick" if era==2 else "metal",-.83,.58,.64,.84,.086,.48)
    for i,depth in enumerate((.40,.79)):
        flue(m,-.85,depth,.48,1.12-i*.25 if era==2 else .80-i*.21,.13,era)
    if m.lod<2:
        m.crate((.82,.087,-.97),.205)
        if m.lod==0:
            m.crate((.56,.087,-1.01),.157)
            flag(m,-.56,-.63,.50,.39)
            for x in (-.27,.04):
                m.block("accent",(x,.166,-.96),(.046,.155,.058))


def foundry(m,era):
    band(m,"fieldstone",0,0,2.69,2.57,0,.077,.15)
    # Tall charge vessel, enclosed tap floor and heat-recovery stove share a raised cast house.
    furnace(m,.30,.26,(2.02,1.38,1.72) if era==2 else (2.09,1.56,1.72),era)
    # The hot-blast main leaves the shaft shoulder and rises to the recuperator.
    m.beam("metal",(.06,.94,.29),(-.47,1.19,.56),.103)
    m.beam("metal",(-.47,1.19,.56),(-.73,1.19,.72),.103)
    hall(m,-.66,.36,.83,1.25,.72,.275 if era==2 else .175,era,-.83,False,False)
    flue(m,-.73,.72,.54,1.02,.139,era)
    if era==3:
        flue(m,.958,.57,.068,1.19,.12,era)
    # Overhead travelling crane and its squat pouring ladle.
    for x in (-.34,.97):
        m.beam("metal",(x,.077,-.78),(x,.92,-.72),.078)
        m.beam("metal",(x,.077,-.32),(x,.92,-.72),.066)
    m.block("metal",(.313,.936,-.72),(1.46,.115,.128))
    m.block("accent",(.54,.881,-.72),(.215,.084,.157))
    for side in (-1,1):
        m.beam("metal",(.54+side*.04,.855,-.721),(.54+side*.17,.45,-.721),.024)
    turned(m,.54,-.72,[(.147,.127,None),(.218,.177,"metal"),(.431,.187,"metal"),
                         (.463,.20,"trim")],(12,8,6),"forge")
    if m.lod<2:
        # A row of sand moulds on a low casting bench fills the pouring aisle.
        m.block("trim",(1.025,.137,.09),(.33,.12,.58))
        for z in ((-.10,.075,.25) if m.lod==0 else (-.07,.21)):
            m.block("hide",(1.025,.233,z),(.277,.074,.136),taper=.10)
            m.block("door",(1.025,.273,z),(.153,.012,.052),taper=.11)
        ingots(m,-.59,-.684,era,1.4)
        flag(m,-.91,.17,.68,.40)
        if m.lod==0:
            for x in (-.34,.97):
                m.beam("metal",(x,.64,-.72),(x+(.20 if x<0 else -.20),.922,-.72),.043)


def electronics(m,era):
    band(m,"fieldstone",0,0,2.67,2.50,0,.078,.17)
    # Recessed curtain wall between pearl-white cleanroom slabs and a filtered service penthouse.
    band(m,"ivory",-.10,.06,1.86,1.60,.078,.25,.15)
    band(m,"glass",-.10,.06,1.79,1.53,.25,.74,.13)
    band(m,"wall",-.10,.06,1.95,1.67,.74,.877,.15)
    band(m,"metal",-.20,.18,1.40,.99,.877,.932,.135)
    for s in (-1,1):
        x=-.20+s*.34
        turned(m,x,.25,[(.932,.245,None),(1.01,.23,"ivory"),(1.065,.208,"metal")],(14,10,6),"door")
        if m.lod==0:
            for lateral in (-.093,-.031,.031,.093):
                m.block("metal",(x,1.066,.25+lateral),(.324,.009,.022))
    hall(m,-.53,-.795,.82,.47,.439,.12,3,-.633,True,False)
    for i,z in enumerate((-.39,.23,.78)):
        x=1.015
        turned(m,x,z,[(.078,.17,None),(.19,.19,"ivory"),(.64+i*.085,.18,"metal"),
                      (.70+i*.085,.142,"ivory"),(.76+i*.085,.078,"trim")],(10,7,5))
        if m.lod!=2:
            m.beam("metal",(x,.49+i*.072,z),(.74,.49+i*.072,z),.054)
    if m.lod<2:
        for u in (-.67,-.31,.10,.49):
            m.block("ivory",(u,.49,-.714),(.035,.49,.041))
        if m.lod==0:
            flag(m,-.82,-.73,.48,.40)
            m.block("metal",(.36,.152,-.879),(.55,.15,.17),taper=.1)
            m.block("leaf",(.36,.241,-.879),(.51,.052,.13),taper=.20)


BUILDERS={"Smelter":smelter,"Blacksmith":smith,"Armory":armory,"Weaver":weaver,
          "Workshop":workshop,"Factory":factory,"Foundry":foundry,"ElectronicsPlant":electronics}


def build(model,lod):
    mesh=House(model,lod)
    BUILDERS[model.building_type](mesh,ERAS.index(model.band))
    return mesh


if __name__=="__main__":
    for model in package_models(PACKAGE):
        high,medium,low=(build(model,i) for i in range(3))
        entry=publish(high,model.metadata(),PACKAGE,lods=(medium,low),preview_size=224)
        print(model.id,entry["lod_triangles"],flush=True)
