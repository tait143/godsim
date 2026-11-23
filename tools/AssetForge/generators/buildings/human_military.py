"""Human town defences, garrison lodgings and signal stations. Builder session 10."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import ROOT, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.human_housesancient import House

PACKAGE = "human_military"


def post(m, role, x, z, bottom, top, w, d=None):
    m.block(role, (x, (bottom + top) / 2, z), (w, top - bottom, d or w))


def drum(m, role, x, z, bottom, top, r, end=None, n=None):
    m.cylinder((x, bottom, z), r, top - bottom, m.color(role),
               segments=n or (12, 8, 6)[m.lod], top_radius=end, material=role)


def hipped(m, x, z, w, d, y, rise, role="roof"):
    with m.transform(translate=(x, 0, z), rotate_y=90):
        m.roof(d, w, y, rise, role, "wall", hip=min(d, w) * .26,
               ridge_mat="trim" if m.lod < 2 else None)


def pennon(m, x, z, base, height):
    """A folded blue commission flag with the gold hem of the town guard."""
    with m.transform(translate=(x, base, z), scale=height):
        post(m, "trim", 0, 0, 0, 1, .04)
        pts = [(0, .99, 0), (.24, .97, -.056), (.49, .88, .015),
               (.24, .79, -.056), (0, .77, 0)]
        for part in ((0, 1, 3, 4), (1, 2, 3)):
            face = [pts[i] for i in part]
            m.face("banner", face[::-1])
            m.face("banner", [(a,b,c+.008) for a,b,c in face])
        if m.lod == 0:
            drum(m, "gold", 0, 0, .992, 1.06, .042, .015, 6)


def hanging(m, x, top, z, w, h):
    """A swallowtail over a timber arm, with a field division woven in ivory."""
    y = top - h
    m.block("trim", (x,top+.012,z+.004), (w*1.22,.031,.044))
    if m.lod==0:
        for side in (-1,1):
            m.block("trim",(x+side*w*.43,top+.011,z+.042),(.027,.034,.086))
    a,b,c,d,e = [(x-w/2,top,z),(x+w/2,top,z),(x+w/2,y,z-.012),
                 (x,y+h*.18,z-.024),(x-w/2,y,z-.012)]
    m.face("banner", [d,e,a,b])
    m.face("banner", [c,d,b])
    if m.lod == 0:
        m.face("ivory", [(x+w*.24,top-h*.33,z-.013),(x,top-h*.20,z-.012),
                         (x-w*.24,top-h*.33,z-.013),(x,top-h*.46,z-.020)][::-1])


def arched_entry(m, x, front, base, r, spring, role="fieldstone"):
    """A recessed boarded gate with an articulated, outward-facing arch ring."""
    y = base + spring
    t = r * .23
    n = (9, 6, 4)[m.lod]
    z = front-.014
    m.face("door", [(x+r,base,z),(x-r,base,z),(x-r,y,z),(x+r,y,z)])
    arc = [(x+r*math.cos(i*math.pi/n),y+r*math.sin(i*math.pi/n),z) for i in range(n+1)]
    m.face("door", arc[::-1])
    if m.lod == 2:
        return
    for sign in (-1, 1):
        post(m, role, x+sign*(r+t/2), z-.012, base, y, t, .05)
    for i in range(n):
        a,b = i*math.pi/n,(i+1)*math.pi/n
        outer = r+t*(1.23 if i == n//2 else 1)
        p = [(x+s*math.cos(angle),y+s*math.sin(angle),z-.040)
             for s,angle in ((r,a),(r,b),(outer,b),(outer,a))]
        m.face(role if i%2 == 0 else "wall", p)
        if m.lod == 0:
            m.face(role, [p[2],p[3],(p[3][0],p[3][1],z+.006),
                          (p[2][0],p[2][1],z+.006)])
    if m.lod == 0:
        for sign in (-1,1):
            m.block("metal",(x+sign*r*.50,base+spring*.65,z-.007),
                    (r*.82,.022,.014))
        post(m,"trim",x,z-.008,base,y+r*.92,.018,.018)


def wall_plate(m, x, y, z, w, h, role="glass"):
    m.face(role, [(x+w/2,y-h/2,z),(x-w/2,y-h/2,z),
                  (x-w/2,y+h/2,z),(x+w/2,y+h/2,z)])


def buttress(m, x, z, w, depth, height):
    if m.lod==2:
        m.block("wall",(x,height*.5,z),(w,height,depth),taper=.20)
        return
    m.block("fieldstone", (x,height*.24,z),(w,height*.48,depth),taper=.10)
    m.block("wall",(x,height*.73,z+.013),(w*.87,height*.50,depth*.78),taper=.12)
    if m.lod == 0:
        m.block("fieldstone",(x,height*.477,z),(w*1.04,.037,depth*.99),taper=.04)


def picket(m, x, h, r, n, wood):
    # The diagonal saw cut on each pole catches a different facet of light.
    pts = [(x+r*math.cos(i*2*math.pi/n),0,r*math.sin(i*2*math.pi/n)) for i in range(n)]
    neck = [(a,h-.14+.036*(a-x)/r,c) for a,_,c in pts]
    tip = (x+r*.18,h,0)
    for i in range(n):
        j = (i+1)%n
        m.face(wood,(pts[j],pts[i],neck[i],neck[j]))
        m.face("hide",(neck[j],neck[i],tip))


def palisade(m, era):
    h = .68 if era == 0 else .85
    wood = "roof" if era==0 else "timber"
    for side in (-1,1):
        cx = side*.895
        if m.lod == 2:
            m.block(wood,(cx,(h-.1)/2,.005),(1.12,h-.1,.125))
            n = 7
            for i in range(n):
                px = cx+1.12*((i+.5)/n-.5)
                m.pyramid((px,h-.102,.005),1.12/n,.128,.13+.016*(i%2),
                          m.color("hide"),material="hide")
        else:
            n=9
            for i in range(n):
                x=cx+1.12*((i+.5)/n-.5)
                picket(m,x,h+.022*math.sin(i*2.4+side),.071,(7,4)[m.lod],wood)
        for y in ((.22,.47) if m.lod<2 else (.25,)):
            m.block("trim",(cx,y,.064),(1.15,.073,.074))
        if era==0:
            # A broad metal binding clamps the round poles against their rear rails.
            # In the Bronze row its hammered copper contrasts with the Iron row's grey band.
            m.block("metal",(cx,.392,-.064),(1.122,.161,.059))
            if m.lod==0:
                for i in range(5):
                    m.block("trim",(cx+(i-2)*.216,.391,-.096),(.033,.036,.018))
        if m.lod < 2:
            m.beam("trim",(cx-.43,.08,.100),(cx+.43,h-.14,.100),.058)
        if m.lod == 0:
            for offset in (-.39, .29):
                m.beam("timber",(cx+offset,.02,.323),(cx+offset,h*.7,.089),.054)
                m.block("hide",(cx+offset,h*.51,-.064),(.11,.041,.016))
    for s in (-1,1):
        post(m,"fieldstone",s*.302,0,0,.20,.207,.236)
        post(m,"trim",s*.302,0,.18,h+.11,.135,.163)
        if m.lod < 2:
            m.block("metal",(s*.302,h*.82,0),(.143,.056,.171))
    m.block("timber",(0,h+.12,0),(.77,.16,.231))
    if era==0:
        m.block("metal",(0,h+.128,-.096),(.754,.124,.070))
    if era:
        hipped(m,0,0,.87,.40,h+.20,.20)
        if m.lod < 2:
            for s in (-1,1):
                m.beam("trim",(s*.30,h-.10,-.055),(s*.12,h+.095,-.055),.045)
    else:
        for s in (-1,1):
            m.beam("timber",(s*.33,h+.17,0),(s*.17,h+.36,0),.075)
        pennon(m,.28,0,h+.19,.33)
    if m.lod < 2:
        hanging(m,-.53,h-.075,-.081,.16,.23)


def stonewall(m, era):
    """A complete wall bay with a recessed foot entrance and a continuous parapet."""
    h = .66 if era==1 else .73
    m.block("fieldstone",(0,.10,0),(1,.20,.44),taper=.06)
    m.block("wall",(0,(h+.18)/2,0),(1,h-.18,.35))
    m.block("fieldstone",(0,h+.018,0),(.987,.052,.409))
    post(m,"wall",-.042 if era==1 else -.103,-.174,0,.371,.261 if era==1 else .278,.092)
    if era == 1:
        m.block("wall",(0,h+.09,-.143),(1,.108,.105))
        if m.lod<2:
            m.block("wall",(0,h+.08,.151),(1,.087,.07))
        for i in range(4):
            x = (i+.5)/4-.50
            m.block("wall",(x,h+.202,-.143),(.142,.126,.109),taper=.075)
        for x in (-.39,.39):
            buttress(m,x,-.145,.126,.218,h-.04)
        arched_entry(m,-.042,-.215,0,.091,.232)
        if m.lod<2:
            hanging(m,.186,h-.081,-.180,.135,.204)
    else:
        m.block("brick",(0,h+.099,-.090),(.979,.145,.257))
        m.block("metal",(0,h+.180,-.088),(1,.040,.286))
        for x in (-.38,.35):
            buttress(m,x,-.17,.132,.250,h+.08)
        m.entry(-.103,-.223,.195,.026,.332,lintel="metal")
        if m.lod<2:
            wall_plate(m,.157,.41,-.182,.105,.054,"door")
            hanging(m,.16,h-.062,-.185,.141,.163)
        if m.lod==0:
            m.block("metal",(-.38,h+.21,-.151),(.115,.11,.103))
            m.block("emissive",(-.38,h+.271,-.151),(.080,.047,.082))
    if m.lod==0:
        for x,y in ((-.235,.39),(.18,.30),(.30,.51),(-.32,.55)):
            m.block("fieldstone",(x,y,-.177),(.103,.046,.016),taper=.08)
        for x in (-.29,.03,.26):
            m.block("fieldstone",(x,h-.039,.184),(.128,.06,.034))


def lookout(m, era):
    """Iron timber trestle, stone belfry and a brick signal tower."""
    h=(.83,1.06,1.14)[era]
    m.course("fieldstone",0,.015,.67,.63,0,.076,.064)
    if era==0:
        for x in (-.216,.216):
            for z in (-.177,.209):
                m.beam("trim",(x*1.25,.075,z*1.24),(x,h,z),.091)
        for z in ((-.190,.219) if m.lod<2 else (-.190,)):
            m.beam("timber",(-.244,.18,z),(.214,h-.06,z),.059)
            if m.lod<2:
                m.beam("timber",(.239,.19,z+.022),(-.215,h-.07,z+.022),.046)
        if m.lod<2:
            for s in (-1,1):
                m.beam("timber",(s*.244,.21,-.193),(s*.215,h-.07,.207),.051)
        def ladder_rail(x1,y1,z1,x2,y2,z2,w):
            if m.lod==0:
                m.beam("timber",(x1,y1,z1),(x2,y2,z2),w)
            else:
                # Twigs viewed at village scale resolve to the width of a single face.
                dx,dy=x2-x1,y2-y1
                length=math.hypot(dx,dy)
                tx,ty=w*.5*dy/length,-w*.5*dx/length
                m.face("timber",[(x1+tx,y1+ty,z1),(x1-tx,y1-ty,z1),(x2-tx,y2-ty,z2),(x2+tx,y2+ty,z2)])
        for s in (-1,1):
            ladder_rail(s*.063,.007,-.35,s*.063,h,-.213,.032)
        for i in range((7,5,3)[m.lod]):
            u=(i+1)/((7,5,3)[m.lod]+1)
            ladder_rail(-.063,.007+(h-.007)*u,-.35+.137*u,
                        .063,.007+(h-.007)*u,-.35+.137*u,.023)
    else:
        m.course("wall" if era==1 else "brick",0,.01,.48,.46,.075,h,.043,.027)
        if m.lod<2:
            m.block("fieldstone",(0,.29,.01),(.491,.073,.47))
        arched_entry(m,0,-.231,.064,.085,.209,"wall" if era==1 else "metal")
        if m.lod<2:
            wall_plate(m,0,.72,-.211,.075,.157,"door")
            with m.transform(translate=(.223,0,.018),rotate_y=-90):
                wall_plate(m,0,.81,-.002,.061,.123,"door")
    if m.lod<2 or era!=0:
        m.block("trim",(0,h+.016,.01),(.647,.111,.609))
    guard = h+.229
    cage_bottom=h-.024 if m.lod==2 and era==0 else h+.062
    m.block("wall" if era else "timber",(0,(cage_bottom+guard)/2,.01),(.605,guard-cage_bottom,.551))
    if m.lod<2:
        m.block("trim",(0,guard+.004,.01),(.635,.034,.58))
    for s in (-1,1):
        for t in (-1,1):
            post(m,"trim",s*.257,.01+t*.230,guard-.003,h+.51,.047)
    if era==2:
        # A glazed lantern gallery shelters the signal crew above the catwalk.
        m.block("glass",(0,h+.366,.01),(.487,.235,.425))
        m.block("metal",(0,h+.511,.01),(.68,.063,.621))
        m.pyramid((0,h+.54,.01),.60,.55,.19,m.color("roof"),material="roof")
        pennon(m,0,.01,h+.718,.26)
    else:
        m.box((0,h+.490,.01),(.746,.049,.666),m.color("trim"),
              material="trim",bottom=True)
        hipped(m,0,.01,.76,.68,h+.51,.36 if era==1 else .26)
        pennon(m,0,.01,h+.51+(.36 if era==1 else .26)-.016,.245)
    if m.lod<2:
        hanging(m,.13,h+.204,-.286,.161,.22)
        for x in (-.234,.229):
            m.beam("trim",(x,h-.190,-.174),(x,h+.009,-.283),.051)
    if m.lod==0:
        for z in (-.178,.178):
            for side in (-1,1):
                m.beam("trim",(side*.217,h-.190,z),(side*.304,h+.009,z),.047)
        if era != 0:
            m.herbs(-.112,.50,-.222,.117)


def muster_drum(m,x,z,era):
    # The guard assembles at the regimental drum, raised on a broad wooden foot.
    with m.transform(translate=(x,.085,z)):
        if m.lod==2:
            drum(m,"banner",0,0,0,.237,.138,.146,n=6)
            drum(m,"hide" if era<2 else "ivory",0,0,.237,.268,.153,n=6)
            return
        drum(m,"trim",0,0,0,.034,.152,.151)
        drum(m,"banner",0,0,.033,.246,.136,.146)
        drum(m,"accent",0,0,.229,.258,.152)
        drum(m,"hide" if era<2 else "ivory",0,0,.258,.268,.145)
        if m.lod==0:
            for i in range(6):
                a=i*math.tau/6
                b=(i+.5)*math.tau/6
                c=(i+1)*math.tau/6
                upper=(.146*math.cos(b),.238,.146*math.sin(b))
                for angle in (a,c):
                    m.beam("ivory",(.137*math.cos(angle),.038,.137*math.sin(angle)),upper,.013)
            for offset in (-.064,.032):
                m.beam("timber",(-.103,.279,offset),(.102,.279,offset+.031),.015)


def bunks(m, era):
    # A high transverse hall is paired with a projecting front gatehouse.
    hall_h=(.59,.71,.62,.67)[era]
    m.course("fieldstone",0,.065,1.67,1.37,0,.086,.085)
    m.block("wall" if era!=2 else "brick",(0,(hall_h+.085)/2,.255),
            (1.55,hall_h-.085,.86))
    if era<2:
        hipped(m,0,.255,1.72,1.00,hall_h,.44 if era==0 else .52)
    elif era==2:
        hipped(m,0,.255,1.71,1.00,hall_h,.255)
        m.block("glass",(0,hall_h+.257,.255),(.80,.101,.17))
        hipped(m,0,.255,.916,.261,hall_h+.306,.087)
    else:
        m.course("glass",0,.255,1.59,.89,hall_h-.12,hall_h+.105,.061)
        m.course("ivory",0,.255,1.69,.96,hall_h+.102,hall_h+.168,.076)
        for x,width in ((-.362,.696),(.344,.474)):
            m.wedge((x,hall_h+.168,.248),width,.608,.184,m.color("glass"),material="glass")
    wingz=-.335
    wy=.535 if era<2 else .44
    m.block("wall",(-.21,(wy+.084)/2,wingz),(.58,wy-.084,.54))
    if era<2:
        with m.transform(translate=(-.21,0,wingz)):
            m.roof(.69,.68,wy,.35,"roof","wall",ridge_mat="trim" if m.lod<2 else None)
    else:
        m.block("metal",(-.21,wy+.024,wingz),(.69,.066,.68))
        if era==3:
            wall_plate(m,-.21,wy-.081,wingz-.278,.455,.081,"glass")
    if era==1:
        arched_entry(m,-.21,wingz-.271,.083,.113,.213)
    else:
        m.entry(-.21,wingz-.271,.208,.083,.316 if era<3 else .246)
    # The parade square banners and entrance plinth stay separate from the roof.
    pennon(m,.578,-.378,.085,.837)
    muster_drum(m,.245,-.461,era)
    if m.lod<2:
        for x in (-.633,.272,.606):
            m.light(x,hall_h*.65,-.176,.133,.140,shutters=era<2)
        with m.transform(translate=(.777,0,.25),rotate_y=-90):
            for x in (-.21,.18):
                m.light(x,hall_h*.66,-.002,.164,.131,shutters=era<2)
        hanging(m,-.431,wy-.039,wingz-.292,.105,.169)
    if m.lod==0:
        if era<2:
            m.flue(-.561,.374,hall_h+.09,hall_h+.52,.132)
            for x in (-.713,-.516,.137,.46,.723):
                post(m,"trim",x,-.180,.091,hall_h,.046)
            m.bent_rail("trim",[(-.469,wy+.025),(-.21,wy+.314),(.049,wy+.025)],
                        wingz-.32,.031)
        else:
            for x in (-.593,.308):
                m.block("metal",(x,.14,-.246),(.24,.105,.107),taper=.1)
            for z in (.21,.42):
                m.block("glass",(.782,hall_h*.69,z),(.017,.133,.146))
        m.block("fieldstone",(.578,.142,-.378),(.229,.116,.209),taper=.11)
    # Broad seated treads span the edge of the raised parade yard.
    m.block("fieldstone",(-.21,.030,-.686),(.339,.06,.205),taper=.035)
    m.block("fieldstone",(-.21,.075,-.65),(.288,.040,.142),taper=.035)


def castle_tower(m,x,z,r,y,roof=True):
    n=(12,8,6)[m.lod]
    if m.lod==2:
        drum(m,"wall",x,z,.064,y+.20,r*1.08,r*.97,n)
        if roof:
            m.cone((x,y+.20,z),r*1.26,r*1.61,m.color("roof"),segments=n,material="roof")
        else:
            for i in range(4):
                a=i*math.pi/2
                post(m,"wall",x+r*.84*math.cos(a),z+r*.84*math.sin(a),y+.199,y+.331,.13)
        return
    drum(m,"fieldstone",x,z,.064,.325,r*1.12,r,n)
    drum(m,"wall",x,z,.323,y,r,r*.92,n)
    drum(m,"fieldstone",x,z,y-.037,y+.064,r*.93,r*1.075,n)
    drum(m,"wall",x,z,y+.064,y+.20,r*1.075,None,n)
    if roof:
        m.cone((x,y+.20,z),r*1.26,r*1.61,m.color("roof"),segments=n,material="roof")
    else:
        for i in range((7,5,4)[m.lod]):
            a = i*math.tau/(7,5,4)[m.lod]
            with m.transform(translate=(x+r*.93*math.cos(a),y+.20,z+r*.93*math.sin(a)),
                             rotate_y=-math.degrees(a)):
                m.block("wall",(0,.065,0),(.10,.13,.124))
    if m.lod<2:
        wall_plate(m,x,y-.23,z-r*.98-.005,.049,.148,"door")
    if m.lod==0:
        with m.transform(translate=(x+r*.983,0,z),rotate_y=-90):
            wall_plate(m,0,y-.325,-.005,.048,.142,"door")


def castle(m):
    m.course("fieldstone",0,.015,2.65,2.52,0,.12,.238)
    # The upper ward rises behind a lower gate court, leaving the gate visible.
    m.course("wall",-.10,.37,1.23,1.04,.113,1.39,.085)
    m.course("fieldstone",-.10,.37,1.29,1.09,1.24,1.36,.071)
    m.block("plaster",(-.10,1.49,.37),(1.33,.266,1.14))
    hipped(m,-.10,.37,1.56,1.35,1.617,.60)
    pennon(m,-.12,.37,2.206,.40)
    for side in (-1,1):
        m.block("wall",(side*.922,.448,.010),(.193,.676,1.69))
        m.block("fieldstone",(side*.922,.813,.01),(.232,.072,1.71))
        for i in range((5,4,3)[m.lod]):
            z=-.70+1.37*i/((5,4,3)[m.lod]-1)
            m.block("wall",(side*.922,.90,z),(.212,.107,.147))
    m.block("wall",(0,.469,.878),(1.84,.70,.187))
    m.block("wall",(0,.499,-.835),(1.74,.762,.25))
    m.block("fieldstone",(0,.889,-.835),(1.77,.065,.291))
    for x in (-.64,-.37,0,.37,.64):
        m.block("wall",(x,.981,-.858),(.158,.121,.150))
    arched_entry(m,0,-.969,.105,.240,.375)
    for x,z,r,h,roof in ((-.91,-.795,.238,.94,True),(.91,-.795,.246,1.00,False),
                         (.91,.865,.252,1.20,True),(-.91,.865,.218,1.16,False)):
        castle_tower(m,x,z,r,h,roof)
    m.course("fieldstone",0,-1.061,.69,.45,0,.108,.052)
    if m.lod<2:
        hanging(m,-.48,.804,-.977,.206,.345)
        hanging(m,.48,.804,-.977,.206,.345)
        for x in (-.516,.309):
            m.light(x,1.490,-.203,.132,.135,shutters=True)
        for x in (-.715,-.32,.119,.501):
            post(m,"trim",x,-.208,1.366,1.622,.048)
        m.block("trim",(-.1,1.615,-.202),(1.36,.046,.043))
    if m.lod==0:
        for x in (-.511,.30):
            m.light(x,1.104,-.158,.107,.180,shutters=False)
        for side in (-1,1):
            m.beam("trim",(-.10+side*.262,1.370,-.229),
                   (-.10+side*.078,1.610,-.229),.043)
        m.flue(-.43,.497,1.625,2.234,.152)
        for z in (.061,.38,.67):
            with m.transform(translate=(.569,0,z),rotate_y=-90):
                m.light(0,1.495,-.002,.135,.122)
        for x in (-.310,.310):
            post(m,"fieldstone",x,-1.152,.107,.222,.096,.223)


def reflector(m, x, z, base, size):
    """An offset parabolic signal receiver turned towards the public approach."""
    with m.transform(translate=(x,base,z), scale=size):
        drum(m,"metal",0,0,0,.28,.168,.11)
        with m.transform(translate=(0,.31,0),rotate_x=-46):
            n=(16,10,7)[m.lod]
            def circle(radius,y):
                return [(radius*math.cos(i*math.tau/n),y,radius*math.sin(i*math.tau/n))
                        for i in range(n)]
            inner, rim, rear = circle(.26,.065),circle(.51,.234),circle(.51,.204)
            for i in range(n):
                j=(i+1)%n
                m.face("glass",[(0,.018,0),inner[j],inner[i]])
                m.face("ivory",[inner[i],inner[j],rim[j],rim[i]])
                m.face("metal",[rear[j],rear[i],rim[i],rim[j]])
                m.face("metal",[(0,-.037,0),rear[i],rear[j]])
            if m.lod<2:
                for i in (1,n//2,n-3):
                    p=rim[i]
                    m.beam("metal",p,(0,.389,0),.021)
                drum(m,"accent",0,0,.37,.435,.041,.061,6)


def signal_base(m, future):
    m.course("fieldstone",0,0,2.78,2.53,0,.086,.179)
    z=.39
    hall_h=.79 if future else .57
    m.course("wall",-.305,z,1.65,1.01,.084,hall_h,.097)
    if future:
        m.course("glass",-.305,z,1.72,1.06,hall_h-.245,hall_h+.078,.135)
        m.course("ivory",-.305,z,1.80,1.15,hall_h+.077,hall_h+.158,.158)
        m.course("glass",-.551,.51,.79,.61,hall_h+.151,hall_h+.437,.131)
        m.course("wall",-.551,.51,.85,.66,hall_h+.431,hall_h+.496,.143)
        reflector(m,-.55,.51,hall_h+.493,.875)
    else:
        hipped(m,-.305,z,1.82,1.14,hall_h,.258)
        m.course("brick",.823,.58,.53,.69,.085,.917,.04)
        m.block("metal",(.823,.942,.58),(.65,.071,.77))
        reflector(m,.823,.58,.974,.91)
    if future:
        m.course("metal",.90,.49,.50,.65,.084,.696,.082,.037)
        m.course("glass",.90,.49,.58,.71,.694,.905,.107)
        m.course("wall",.90,.49,.61,.753,.903,.968,.112)
        pennon(m,.90,.50,.967,.406)
    # A blue sentry canopy and a single raised barrier identify the reception court.
    m.block("wall",(-.343,.301,-.682),(.386,.43,.46))
    m.block("glass",(-.343,.524,-.682),(.389,.104,.463))
    m.block("metal",(-.343,.602,-.682),(.54,.072,.60))
    if m.lod==0:
        # Threshold lamp and shelter ribs are carried on the gatekeeper’s booth.
        m.block("metal",(-.501,.401,-.918),(.046,.073,.047))
        m.block("emissive",(-.501,.405,-.941),(.033,.044,.014))
        for sx in (-.523,-.163):
            post(m,"metal",sx,-.906,.503,.57,.021)
    m.entry(-.343,-.913,.170,.086,.287,lintel="metal")
    m.block("banner",(.12,.190,-.791),(.124,.208,.16))
    m.beam("ivory",(.13,.254,-.790),(.496,.655,-.79),.041)
    m.entry(-.436,-.12,.235,.085,.335,lintel="metal")
    pennon(m,-.905,-.423,.086,.797)
    if m.lod<2:
        for x in (-.925,.045,.30):
            m.light(x,hall_h*.596,-.12,.159,.119,shutters=False)
        if not future:
            for z in (.40,.71):
                with m.transform(translate=(1.09,0,z),rotate_y=-90):
                    wall_plate(m,0,.735,-.004,.145,.139)
        for x in (.58,.86,1.15):
            m.block("ivory",(x,.153,-.705),(.19,.14,.161),taper=.28)
        for x in (-1.00,-.78):
            m.block("fieldstone",(x,.174,-.803),(.162,.176,.221),taper=.15)
        hanging(m,-.311,.531,-.916,.092,.116)
    if m.lod == 0:
        for x in (-.906,-.730):
            m.block("fieldstone",(x,.115,-.445),(.162,.057,.153))
        for x in (.30,.75):
            m.block("ivory",(x,.089,-.364),(.19,.006,.047))
        if future:
            for x in (-.91,-.65,-.08,.209):
                m.block("ivory",(x,hall_h-.08,-.148),(.031,.282,.026))
        else:
            m.flue(-.953,.617,.61,1.003,.116)


def build(spec,lod):
    m=House(spec,lod)
    era=("ancient","classical","industrial","future").index(spec.band)
    if spec.building_type=="Palisade":
        palisade(m,era)
    elif spec.building_type=="StoneWall":
        stonewall(m,era)
    elif spec.building_type=="Watchtower":
        lookout(m,era)
    elif spec.building_type=="Barracks":
        bunks(m,era)
    elif spec.building_type=="Castle":
        castle(m)
    else:
        signal_base(m,era==3)
    return m


def preview_sheets(entries):
    folder = (ROOT/entries[0]["preview"]).parent
    names = [e["building_type"]+" / "+e["id"].split("_")[-2] for e in entries]
    contact_sheet([ROOT/e["preview"] for e in entries],folder/"contact-models.png",
                  cols=4,cell=180,labels=names)
    views = [(e["building_type"]+" / "+age,ROOT/path) for e in entries
             for age,path in e["age_previews"].items()]
    # The 64-pixel images show actual village-camera scale beside their age labels.
    from PIL import Image, ImageDraw
    cols, width, height = 5, 143, 87
    canvas=Image.new("RGB",(cols*width,((len(views)+cols-1)//cols)*height),(245,243,238))
    draw=ImageDraw.Draw(canvas)
    for i,(label,path) in enumerate(views):
        x,y=(i%cols)*width,(i//cols)*height
        with Image.open(path) as source:
            canvas.paste(source.resize((64,64),Image.Resampling.LANCZOS),(x+(width-64)//2,y))
        draw.text((x+4,y+66),label,fill=(40,40,40))
    canvas.save(folder/"contact-ages.png")


if __name__=="__main__":
    entries=[]
    for spec in package_models(PACKAGE):
        hi,mid,low=(build(spec,lod) for lod in range(3))
        result=publish(hi,spec.metadata(),PACKAGE,lods=(mid,low),preview_size=224)
        print(spec.id,result["lod_triangles"],flush=True)
        entries.append(result)
    preview_sheets(entries)
