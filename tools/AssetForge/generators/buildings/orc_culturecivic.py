"""Orc public architecture: hearth courts, drum theaters and horned grandstands."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.orc_towncenters import Lodge

PACKAGE = "orc_culturecivic"


def bench_arc(m, cx, cz, sx, sz, start, stop, section, sectors, striped=False):
    """Turn a seat profile through an open sector. Radial ends are stepped separately."""
    def vertex(degrees, radius, y):
        a = math.radians(degrees)
        return (cx + sx * radius * math.sin(a), y, cz - sz * radius * math.cos(a))

    for i in range(sectors):
        a = start + (stop - start) * i / sectors
        b = start + (stop - start) * (i + 1) / sectors
        for j in range(len(section)):
            r, y, role = section[j]
            nr, ny, _ = section[(j + 1) % len(section)]
            if y == ny == 0:
                continue
            mat = "cloth" if striped and role == "hide" and i % 5 == 2 else role
            m.panel(mat, [vertex(a,r,y), vertex(b,r,y), vertex(b,nr,ny), vertex(a,nr,ny)])
    if stop - start == 360:
        return
    # Each terrace's end is a convex strip dropping down to its base.
    for a, reverse in ((start, True), (stop, False)):
        for (r,y,_), (nr,ny,_) in zip(section,section[1:]):
            if nr <= r or min(y,ny) <= 0:
                continue
            pts = [vertex(a,r,0), vertex(a,nr,0), vertex(a,nr,ny), vertex(a,r,y)]
            m.panel("wall", pts[::-1] if reverse else pts)


def skin(m, x, z, w, d, base, rise, mat, *, saddle=False):
    """A pegged, eight-panel canopy with a broad ridge and slightly upturned ends."""
    edge = [(-.76,-1),(.76,-1),(1,-.68),(1,.68),(.76,1),(-.76,1),(-1,.68),(-1,-.68)]
    def ring(height, wx, dz):
        return [(x + a*wx, base + height + (.07*rise*abs(a) if saddle else 0), z + b*dz)
                for a,b in edge]
    rims = [ring(0,w*.97,d*.97), ring(.09*rise,w,d),
            ring(.60*rise,w*.68,d*.46), ring(rise,w*.41,d*.081)]
    for j,(lo,hi) in enumerate(zip(rims,rims[1:])):
        for i in range(8):
            k = (i+1)%8
            role = "trim" if j == 0 else ("cloth" if i in (1,5) else mat)
            m.panel(role,[lo[k],lo[i],hi[i],hi[k]])
    m.panel("trim",rims[-1][::-1])
    m.panel("trim",rims[0])


def material(m):
    return ("timber","fieldstone","brick","plaster")[m.era]


def roof(m):
    return ("hide","roof_tile","metal","glass")[m.era]


def lozenge(m,x,y,z,w,h):
    m.panel("ivory",[(x,y-h,z),(x-w,y,z),(x,y+h,z),(x+w,y,z)])


def carved_post(m,x,y,z,h,r,side, *, horn=True):
    body = "timber" if m.era < 2 else "metal"
    if m.lod == 2:
        m.post(body,(x,y,z),(x,y+h,z),r,r*.76,4)
    else:
        m.shell(x,z,[(y,r*1.16,material(m)),(y+h*.13,r*1.16,body),
                     (y+h*.68,r*.90,"cloth"),(y+h*.78,r*.94,body),
                     (y+h,r*.79,"gold")],(8,6)[m.lod])
    if horn:
        m.tusk(x,y+h-.01,z,side,r*3.55)
    if m.lod == 0:
        lozenge(m,x,y+h*.50,z-r*.98,r*.54,r*.88)


def entry_gate(m, x, z, w, h, *, emblem=False):
    r=.09*w
    for sign in (-1,1):
        carved_post(m,x+sign*w*.49,0,z,h,r,sign)
    m.block("trim",x,h-.037,z,w*1.21,.105*w,.164*w)
    m.block("cloth",x,h+.062*w,z,w*.59,.075*w,.15*w,.12)
    if emblem and m.lod<2:
        with m.transform(translate=(x,h+.079*w,z-.077*w),rotate_x=-90):
            m.shell(0,0,[(0,.092*w,"trim"),(.052*w,.114*w,"hide")],(9,6)[m.lod])
    if m.lod==0:
        for s in (-1,1):
            m.post("ivory",(x+s*.44*w,h-.159*w,z-.018),(x+s*.31*w,h-.025*w,z-.018),.025*w,sides=5)


def hearth(m):
    e, lod = m.era,m.lod
    z=.050
    n=(12,9,7)[lod]
    if e==0:
        # A broad, weathered hearth floor: raw planks, ochre pavers, then faceted stone.
        # The age-responsive wall role changes surface pattern as well as color.
        m.shell(0,.018,[(0,.433,"trim"),(.018,.437,"wall")],(12,9,7)[lod],flatten=.85)
    # Fire stones enclose a charcoal bed, with a clear approach between the seats.
    m.shell(0,z,[(0,.242,"soil"),(.034,.257,"door")],n,flatten=.88)
    if lod<2:
        for i in range((11,8)[lod]):
            a=math.tau*i/(11,8)[lod]
            x,sz=.230*math.sin(a),z-.198*math.cos(a)
            with m.transform(translate=(x,0,sz),rotate_y=-math.degrees(a)+3):
                m.block("fieldstone" if i%4 else "wall",0,0,0,.103,.065+.009*(i%3),.081,.20)
    for i,d in enumerate((48,-48,5) if lod<2 else (48,-48)):
        with m.transform(translate=(0,.071+i*.026,z),rotate_y=d):
            m.post("timber",(-.166,0,0),(.167,0,0),.033,sides=(7,5,4)[lod])
    for x,sz,r,h,dx in ((-.041,.008,.108,.34,.14),(.080,-.028,.065,.243,-.16),(-.108,.033,.055,.20,.08)):
        if lod==2 and x<-.08:
            continue
        m.shell(x,z+sz,[(.101,r*.55,"forge"),(.152,r,"forge"),
                        (.101+h*.58,r*.56,"emissive"),(.101+h,0,"gold")],
                (7,5,4)[lod],flatten=.87,dx=dx)
    for side in (-1,1):
        with m.transform(translate=(side*.338,0,-.048),rotate_y=side*19):
            if e==0:
                for sz in (-.137,.132):
                    m.block("fieldstone",0,0,sz,.076,.072,.084,.18)
                m.post("roof",(0,.086,-.195),(0,.093,.192),.053,sides=(8,6,4)[lod])
                # One low plank behind each log seat protects the gathering from wind.
                m.block("wall",side*.047,.047,0,.041,.146,.340,.055)
                m.block("roof",side*.047,.190,0,.051,.022,.355,.04)
            else:
                m.block("fieldstone",0,0,0,.108,.108,.39,.15)
                m.block("plaster",0,.101,0,.12,.045,.40,.035)
    if e==0:
        carved_post(m,-.215,0,.317,.342,.048,-1)
        if lod==0:
            m.drum(.238,0,.283,.076,.107)
    else:
        for s in (-1,1):
            carved_post(m,s*.182,0,.344,.286,.048,s)
        m.block("trim",0,.278,.345,.40,.048,.073)
        if lod==0:
            for side in (-1,1):
                m.block("gold",side*.182,.185,.344,.098,.022,.096)
        if lod<2:
            m.block("cloth",0,.172,.344,.102,.124,.042)
            lozenge(m,0,.24,.322,.025,.039)


def cantilever_stadium(m):
    # A rectangular pitch with two open end zones replaces the older oval arena.
    m.block("fieldstone",0,0,.03,2.62,.085,2.42,.07)
    m.block("grass",0,.085,.03,1.39,.031,2.11)
    for s in (-1,1):
        # Taller home terrace to the left, low visitor bleachers on the opposite side.
        levels=(4,3,2)[m.lod] if s<0 else (2,2,1)[m.lod]
        h=.68 if s<0 else .41
        for j in range(levels):
            x=s*(.73+(j+.5)*.52/levels)
            m.block("ivory" if j%2 else "metal",x,.085,.02,.52/levels,h*(j+1)/levels,
                    1.95-.05*j)
        if m.lod<2:
            for z in (-.97,.99):
                m.tusk(s*.89,.29,z,s,.47)
        # Broad goal mouths at both ends, facing the long axis of the field.
        z=s*.88
        for x in (-.27,.27):
            m.block("ivory",x,.116,z,.035,.19,.046)
        m.block("ivory",0,.306,z,.57,.038,.046)
    for z in (-.72,.68):
        m.post("metal",(-1.21,.09,z),(-1.16,.88,z),.095,.067,sides=(8,5,4)[m.lod])
        m.post("ivory",(-1.16,.85,z),(-.76,1.08,z),.067,.045,sides=(8,5,4)[m.lod])
    with m.transform(translate=(-1.02,1.025,.03),rotate_y=90,rotate_x=9):
        skin(m,0,0,1.10,.357,0,.19,"glass",saddle=True)
    # Single elevated scoreboard behind the far end, clear of the open entrance.
    for x in (-.42,.42):
        m.post("metal",(x,.083,1.10),(x,.63,1.10),.069,.049,sides=(7,5,4)[m.lod])
    m.block("metal",0,.59,1.09,1.13,.35,.15,.10)
    m.block("glass",0,.635,1.011,.95,.25,.035)
    if m.lod==0:
        for x in (-.33,0,.33):
            m.block("turquoise",x,.72,.991,.15,.059,.016)
        for z in (-.62,.20,.69):
            m.block("cloth",1.247,.18,z,.036,.17,.34)
            m.post("metal",(-1.27,.093,z),(-1.14,.68,z+.07),.049,sides=6)
            m.post("trim",(1.26,.09,z),(1.23,.37,z+.085),.042,sides=5)



def public_ring(m, stadium):
    if stadium and m.era==3:
        return cantilever_stadium(m)
    lod,e=m.lod,m.era
    levels=(3,3,2)[lod] if stadium else (4,3,2)[lod]
    n=(18,13,8)[lod]
    wall=material(m)
    sx,sz=(1.37,1.14) if stadium else (1.235,1.235)
    field="grass" if stadium else ("soil","soil","hide","slate")[e]
    m.shell(0,.045,[(0,.76,"trim"),(.026,.76,field)],(20,14,9)[lod],flatten=.89 if stadium else 1)
    section=[(.575,0,wall)]
    height=((.43,.53,.64,.69)[e] if not stadium else (.55 if e==2 else .69))
    for row in range(levels):
        radius=.575+.355*row/levels
        top=height*(row+1)/levels
        section.extend([(radius,top,"hide" if row==levels-1 else ("wall" if row%2 else "timber")),
                        (.575+.355*(row+1)/levels,top,wall)])
    section.extend([(.97,height+.072,"trim"),(1,height+.068,wall),(1,0,wall)])
    # The gap in the horseshoe forms the public entrance.
    bench_arc(m,0,.045,sx,sz,17,343,section,n,True)
    entry_gate(m,0,.045-sz*.92,.58,.50 if stadium else .43,emblem=True)
    # A wall-walk crowned by two pairs of broad ivory prongs reads above the stands.
    # Their sockets straddle the rim, with a hanging crimson apron over its outside face.
    for angle in (-53,53,-135,135):
        if lod==2 and abs(angle)>90:
            continue
        a=math.radians(angle)
        with m.transform(translate=(sx*.969*math.sin(a),height+.032,.045-sz*.969*math.cos(a)),rotate_y=-angle):
            s=1 if angle>0 else -1
            if not stadium or abs(angle)<90:
                m.tusk(0,0,0,s,.505 if not stadium else .47)
            if lod<2:
                m.block("cloth",0,-.27,-.041,.151,.279,.077,.08)
                if lod==0:
                    lozenge(m,0,-.139,-.081,.041,.070)
    if stadium:
        # Each sideline has a cantilever roof backed by its own two raking masts.
        for side in (-1,1):
            for z in (-.49,.49):
                bx=side*1.155
                m.post("metal",(bx,.008,z),(side*1.18,height+.43,z),.057,sides=(6,5,4)[lod])
                if lod<2:
                    m.post("trim",(bx,height*.77,z),(side*.77,height+.329,z),.046,sides=5)
            with m.transform(translate=(side*1.045,height+.30,0),rotate_y=90,rotate_x=side*7):
                skin(m,0,0,.66 if e==2 else .76,.29,0,.17,roof(m),saddle=True)
        if lod<2:
            # Two short goal frames and a pale painted center stripe.
            for x in (-.532,.532):
                for z in (-.16,.16):
                    m.block("ivory",x,.026,z,.024,.18,.025)
                m.block("ivory",x,.204,0,.026,.024,.345)
            m.block("ivory",0,.026,.035,.024,.004,1.23)
    else:
        # The champion's raised box faces the entrance and sits on the rim wall.
        if e==1:
            with m.transform(translate=(0,height+.045,1.12),scale=(1,1,.70)):
                for x in (-.27,.27):
                    m.block("timber",x,0,0,.081,.258,.10)
                skin(m,0,0,.405,.254,.246,.234,"roof_tile",saddle=True)
        elif e==2:
            for side in (-1,1):
                carved_post(m,side*.77,0,.920,height+.26,.094,side,horn=False)
                if lod<2:
                    m.flag(side*.77,height+.20,.920,.33,.22)
        else:
            for side in (-1,1):
                m.post("ivory",(side*.70,height-.19,.99),(side*.46,height+.33,1.075),.077,.042)
            with m.transform(translate=(0,height+.32,1.065)):
                skin(m,0,0,.57,.19,0,.107,"glass")
    if lod==0:
        for side in (-1,1):
            for a in (52,94,141):
                rad=math.radians(a*side)
                dx,dz=math.sin(rad),-math.cos(rad)
                with m.transform(translate=(sx*.98*dx,0,.045+sz*.98*dz),rotate_y=-a*side):
                    m.block("trim",0,0,0,.12,height*.82,.135,.19)
                    m.block("cloth",0,height*.57,-.016,.12,.061,.136)


def theater(m):
    lod,e=m.lod,m.era
    # Audience faces an open performance deck and a large vertical, hide-faced chorus drum.
    wall=material(m)
    steps=(3,2,1)[lod]
    profile=[(.50,0,wall)]
    for s in range(steps):
        y=.255*(s+1)/steps
        r=.50+s*.41/steps
        profile += [(r,y,"hide"),(.50+(s+1)*.41/steps,y,wall)]
    profile += [(.96,.28,"trim"),(.99,.249,wall),(.99,0,wall)]
    for angle in (-82,12):
        bench_arc(m,0,-.01,.92,.92,angle,angle+(70 if angle<0 else 70),profile,(7,5,3)[lod],True)
    m.block(wall,0,0,.384,1.26,.205,.69,.07)
    m.block("timber",0,.198,.387,1.30,.056,.71,.025)
    for x in (-.59,.59):
        carved_post(m,x,.249,.568,.594,.061,1 if x>0 else -1)
    with m.transform(translate=(0,.678,.591),rotate_x=-90):
        r=(.33,.37,.32,.39)[e]
        n=(14,10,6)[lod]
        m.panel("hide" if e<3 else "metal",[(r*.94*math.sin(math.tau*(i+.5)/n),-.065,
                -r*.94*math.cos(math.tau*(i+.5)/n)) for i in range(n)])
        m.shell(0,0,[(-.065,r*.94,"trim"),(-.009,r*1.05,"gold"),
                     (.065,r,"hide" if e<3 else "glass")],(14,10,6)[lod])
    m.block("trim",0,.25,.62,.21,.67,.13)
    if lod<2:
        for side in (-1,1):
            m.post("timber",(side*.59,.769,.574),(side*.29,.796,.593),.037,sides=5)
        if lod==0:
            lozenge(m,0,.67,.521,.091,.144)
            for sign in (-1,1):
                x=sign*.152
                m.panel("cloth",[(x-.024,.623,.520),(x-.065,.779,.520),
                                 (x-.011,.753,.520),(x+.037,.610,.520)])
    if e in (1,3):
        # Two small, parted curtains leave the central soundboard visible.
        for s in (-1,1):
            with m.transform(translate=(s*.611,0,.51),rotate_y=s*12):
                skin(m,0,0,.216,.299,.791,.223,"hide" if e==1 else "glass")
                if lod<2:
                    m.block("cloth",s*.096,.362,.019,.137,.488,.074,.09)
    else:
        for s in (-1,1):
            m.post("metal",(s*.57,.78,.565),(s*.43,.977,.54),.046,.03)
            m.block("emissive",s*.426,.931,.515,.098,.09,.092,.1)
    if lod<2:
        m.drum(-.391,.253,.316,.097,.137)
        m.block("trim",.344,.253,.265,.15,.118,.123,.07)


def tavern(m):
    e,lod=m.era,m.lod
    with m.transform(translate=(-.051,0,.133),scale=(1.54,1.12,1.38)):
        m.hut(0,0,1,e,0)
    for s in (-1,1):
        # Paired jawbone finials are socketed into the high shoulder of the conical roof.
        m.tusk(-.051+s*.277,(.99,.99,.81,.81)[e],.13,s,.465)
    # Keg over the serving porch, with a projecting tap and sunward window.
    for x in (-.34,.13):
        m.block("trim",x,0,-.549,.074,.443,.080)
    skin(m,-.101,-.497,.348,.192,.410,.167,"hide" if e==1 else roof(m))
    if lod<2:
        m.post("trim",(.381,.01,-.455),(.381,.708,-.455),.039,sides=6)
        m.post("trim",(.358,.68,-.455),(.664,.684,-.455),.043,sides=6)
        m.block("gold",.541,.563,-.455,.027,.124,.022)
        with m.transform(translate=(.541,.483,-.446),rotate_x=-90):
            m.shell(0,0,[(-.047,.090,"trim"),(-.006,.104,"hide"),(.053,.093,"gold")],(9,7)[lod])
        m.block("glass",-.46,.126,-.301,.155,.136,.10)
    flue=(.295,.40)
    if e==1:
        m.shell(*flue,[(.08,.126,"fieldstone"),(.85,.118,"fieldstone"),(1.18,.094,"trim"),(1.236,.124,"door")],(8,6,5)[lod],dx=.046)
    elif e==2:
        m.shell(*flue,[(0,.153,"brick"),(.85,.127,"brick"),(1.15,.113,"metal"),(1.27,.10,"door")],(10,7,5)[lod])
    else:
        m.shell(*flue,[(0,.142,"wall"),(.77,.128,"glass"),(.94,.151,"ascension"),(1.02,.128,"metal")],(9,6,5)[lod])
    if lod==0:
        for x,z,r in ((.51,-.335,.10),(.687,-.206,.09)):
            m.shell(x,z,[(0,r*.80,"trim"),(.034,r*.96,"timber"),(.147,r,"metal"),(.17,r*.94,"timber"),(.215,r*.85,"hide")],8)


def iron_moot(m):
    # An industrial assembly hall backs a public speaking platform; its clock overlooks the square.
    m.block("fieldstone",-.18,0,.20,2.04,.13,1.12,.045)
    m.block("brick",-.32,.13,.48,1.52,.50,.47,.04)
    m.prism_x((-.32,.63,.48),1.65,.61,.32,m.col("metal"),material="metal")
    m.entry(-.45,.20,.13,.31,.39,2)
    m.block("brick",.78,0,.28,.51,.94,.54,.08)
    m.block("metal",.78,.94,.27,.58,.096,.60,.06)
    m.block("wall",.78,1.033,.28,.46,.28,.47)
    m.post("ivory",(.78,1.18,.044),(.78,1.18,.014),.117,sides=(12,7,4)[m.lod])
    m.pyramid((.78,1.312,.28),.58,.59,.32,m.col("metal"),material="metal")
    # Two sloped rows face the orator's rostrum. Leave room to approach the lectern.
    for side in (-1,1):
        with m.transform(translate=(side*.72,0,-.43),rotate_y=-side*34):
            m.block("fieldstone",0,0,0,.63,.17,.33,.08)
            m.block("timber",0,.17,.089,.62,.10,.12)
        m.tusk(side*.88,.135,-.22,side,.47)
    m.shell(-.02,-.06,[(.13,.26,"trim"),(.21,.30,"metal"),(.26,.25,"wall")],(11,7,5)[m.lod])
    m.block("trim",-.02,.25,-.01,.16,.20,.13,.1)
    if m.lod<2:
        m.drum(-.93,.13,.085,.144,.22)
    if m.lod==0:
        for x in (-.87,.15):
            m.block("glass",x,.32,.239,.17,.20,.030)
            m.post("trim",(x-.10,.64,.24),(x-.16,.80,.34),.033,sides=6)
            m.post("trim",(x+.10,.64,.24),(x+.16,.80,.34),.033,sides=6)
        m.post("trim",(.78,1.18,-.008),(.78,1.263,-.008),.015,sides=5)
        m.post("trim",(.78,1.18,-.010),(.829,1.146,-.010),.014,sides=5)
        m.flag(.73,1.50,.26,.38,.25)


def signal_moot(m):
    # An open circular council chamber faces a broadcasting dais with a broad canopy
    # shaped like a war shield held upright. The gap in the seats is the public aisle.
    m.shell(0,.07,[(0,.68,"fieldstone"),(.10,.73,"ivory"),(.16,.68,"metal")],
            (16,10,6)[m.lod],flatten=.75)
    for side in (-1,1):
        section=[(.62,0,"metal"),(.62,.19,"hide"),(.75,.19,"ivory"),(.75,.36,"cloth"),
                 (.91,.36,"metal"),(.98,.42,"turquoise"),(1,.38,"ivory"),(1,0,"metal")]
        if m.lod==2:
            section=[(.62,0,"metal"),(.62,.24,"ivory"),(.96,.35,"metal"),(1,0,"metal")]
        bench_arc(m,0,.035,1.18,.86,31 if side>0 else 206,153 if side>0 else 329,
                  section,(9,6,4)[m.lod])
        m.tusk(side*.91,.34,.23,side,.60)
    m.shell(0,.37,[(.15,.32,"metal"),(.31,.23,"glass"),(.66,.135,"turquoise"),
                    (.81,.24,"ivory")],(10,7,4)[m.lod],flatten=.68)
    with m.transform(translate=(0,1.09,.35),rotate_x=-13):
        pts=[(0,.41),(-.44,.27),(-.53,.06),(-.29,-.26),(0,-.39),(.29,-.26),(.53,.06),(.44,.27)]
        for i,(x,y) in enumerate(pts):
            u,v=pts[(i+1)%len(pts)]
            m.panel("ivory" if i%3==0 else "glass",[(u,v,0),(x,y,0),(0,.023,-.11)])
            m.panel("metal",[(x,y,.075),(u,v,.075),(0,.023,.075)])
            m.panel("trim",[(x,y,0),(u,v,0),(u,v,.075),(x,y,.075)])
    m.block("trim",0,.16,-.04,.16,.21,.14,.10)
    with m.transform(translate=(0,.37,-.05),rotate_x=-13):
        m.block("turquoise",0,0,0,.26,.043,.22)
    if m.lod==0:
        m.drum(.40,.16,.08,.105,.146)



def forum(m):
    if m.era==2:
        return iron_moot(m)
    if m.era==3:
        return signal_moot(m)
    lod,e=m.lod,m.era
    # Three-sided moot court with a covered rear gallery and open speaking circle.
    wall=material(m)
    m.block("fieldstone",0,0,.10,2.54,.116,1.51,.044)
    m.block("wall",0,.11,.120,2.44,.056,1.40)
    rise=(0,.68,.79,.89)[e]
    for side in (-1,1):
        for j,z in enumerate((-.372,.074,.503)):
            x=side*(1.078-.046*j)
            if lod==2 and j==1:
                continue
            carved_post(m,x,.161,z,rise-.16,.088,side,horn=False)
        with m.transform(translate=(side*1.024,0,.072),rotate_y=90):
            skin(m,0,0,.62,.19,rise-.018,.188,roof(m),saddle=True)

    for side in (-1,1):
        m.tusk(side*.967,rise+.105,-.36,side,.554)
    if e==1:
        skin(m,0,.508,1.06,.267,rise-.011,.30,"roof_tile")
    elif e==2:
        skin(m,0,.508,1.08,.267,rise-.011,.208,"metal")
        if lod<2:
            m.flag(-.52,rise+.115,.506,.34,.31)
    else:
        skin(m,0,.508,1.08,.267,rise-.011,.164,"glass")
        for x in (-.39,.39):
            m.post("ivory",(x,.163,.52),(x*.74,rise+.09,.52),.056,.034)
    m.block(wall,0,.16,.597,1.98,rise-.165,.19)
    m.block("cloth",0,.375,.478,.46,.28,.086,.055)
    if lod<2:
        lozenge(m,0,.525,.432,.098,.094)
    m.shell(0,-.202,[(.16,.268,"trim"),(.262,.27,"hide"),(.315,.245,"wall")],(11,8,5)[lod],flatten=.86)
    m.block("trim",0,.309,-.137,.127,.152,.107,.12)
    with m.transform(translate=(0,.453,-.145),rotate_x=-14):
        m.block("gold",0,0,0,.194,.036,.162)
    # Keep the full flight length when coarsening the risers. Its last tread always
    # enters the raised court, and every lower tread abuts the next riser.
    steps=(3,2,1)[lod]
    for i in range(steps):
        run=.32/steps
        m.block("fieldstone",0,0,-.924+(i+.5)*run,.70-.05*i,.145*(i+1)/steps,run)
    if lod==0:
        m.drum(.524,.165,-.372,.13,.225)
        m.shell(-.535,-.377,[(.165,.097,"trim"),(.251,.062,"gold"),(.302,.121,"trim"),
                            (.358,.134,"forge"),(.374,.108,"forge"),(.490,0,"emissive")],9)


def skyhold(m):
    lod=m.lod
    # Three inhabited galleries grow from a single shield-shaped hill fort. Their flared
    # balconies act as the heel sockets for two continuous rising ivory ribs.
    for j,(y,r,h) in enumerate(((0,.96,.64),(.62,.78,.62),(1.22,.54,.51))):
        rows=[(y,r*.94,"wall"),(y+.11,r*.90,"glass"),(y+h*.67,r*.86,"ivory"),
              (y+h*.79,r*1.07,"gold"),(y+h*.88,r*1.102,"wall"),(y+h,r*1.08,"grass")]
        m.shell(0,.09,rows if lod<2 else [rows[i] for i in (0,1,3,5)],(14,10,7)[lod],flatten=.84)
        if lod==0:
            for i in range(7):
                a=math.tau*i/7
                sx,sz=math.sin(a),math.cos(a)*.84
                m.post("ivory",(r*.92*sx,y+.097,.09+r*.92*sz),
                       (r*.90*sx,y+h*.70,.09+r*.90*sz),.025,sides=4)
    for side in (-1,1):
        m.shell(side*.83,.12,[(0,.274,"wall"),(.196,.277,"gold"),(.30,.215,"ivory")],(9,7,5)[lod])
        # Ribs rake towards the roof crown, leaving an open silhouette on either side.
        m.post("ivory",(side*.84,.233,.12),(side*1.04,1.07,.125),.17,.12)
        m.post("ivory",(side*1.04,1.047,.125),(side*.77,1.73,.107),.125,.076)
        m.post("gold",(side*.77,1.707,.107),(side*.53,1.96,.095),.080,.037)
    m.shell(0,.09,[(1.716,.43,"gold"),(1.80,.375,"glass"),(2.05,.234,"glass"),(2.18,.083,"ascension")],(12,8,5)[lod])
    crown=[(.45,1.955,"gold"),(.45,2.009,"ascension"),(.55,2.017,"gold"),(.55,1.975,"gold")]
    bench_arc(m,0,.09,1,1,0,360,crown,(14,10,7)[lod])
    m.entry(0,-.65,0,.484,.468,3)
    # A clan standard, suspended from the sky-garden parapet. Its taper clears the
    # ground gate; large tusks rise from ferrules fixed to the lower balcony.
    for side in (-1,1):
        m.block("gold",side*.256,1.153,-.585,.083,.112,.35)
        if lod<2:
            m.shell(side*.383,-.666,[(.51,.117,"gold"),(.635,.113,"cloth")],(8,6,4)[lod])
        m.tusk(side*.383,.60,-.666,side,.88)
    m.post("gold",(-.345,1.231,-.761),(.345,1.231,-.761),.046,sides=(8,6,4)[lod])
    contour=[(-.31,1.226),(-.278,.789),(0,.652),(.278,.789),(.31,1.226)]
    face=[(x,y,-.787) for x,y in contour]
    back=[(x,y,-.737) for x,y in contour]
    m.panel("cloth",face[::-1])
    m.panel("cloth",back)
    for i in range(len(face)):
        k=(i+1)%len(face)
        m.panel("cloth",(face[i],face[k],back[k],back[i]))
    lozenge(m,0,.996,-.789,.112,.145)
    if lod<2:
        m.block("glass",0,.468,-.635,.67,.104,.326,.10)


def build(spec,lod):
    if spec.building_type == "Theater" and spec.band in ("industrial","future"):
        from generators.buildings.orc_learninghealth import build_lodge
        from generators.buildings.orc_era_forms import performance_hall
        return build_lodge(spec,lod,lambda m,t: performance_hall(m))
    m=Lodge(spec,lod)
    kind=spec.building_type
    if kind in ("Arena","Stadium"):
        public_ring(m,kind=="Stadium")
    else:
        {"Campfire":hearth,"Theater":theater,"Forum":forum,"Tavern":tavern,"Arcology":skyhold}[kind](m)
    return m


if __name__=="__main__":
    published=[]
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        published.append((spec,entry))
        print(spec.id,entry["lod_triangles"],flush=True)
    folder=ROOT/"tools/AssetForge/previews/Buildings/Orc/CultureCivic"
    contact_sheet([ROOT/e["preview"] for _,e in published],folder/"contact_sheet.png",cols=5,cell=192,
                  labels=[s.building_type+" "+s.band for s,_ in published])
    for age in AGES:
        rows=[(s,e) for s,e in published if age in s.ages]
        if rows:
            contact_sheet([ROOT/e["age_previews"][age] for _,e in rows],folder/("sheet_"+age+".png"),
                          cols=min(5,len(rows)),cell=160,labels=[s.building_type+" "+age for s,_ in rows])
