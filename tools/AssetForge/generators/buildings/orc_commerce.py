"""Orc exchange yards, counting houses and freight terminals."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_towncenters import Lodge, generate
from generators.buildings import orc_civic_trades
from generators.buildings.orc_learninghealth import build_lodge

PACKAGE = "orc_commerce"


def course(m, x, z, rings, n=None, flat=1):
    m.shell(x, z, rings, n or (12, 8, 5)[m.lod], flatten=flat)


def distant_lodge(m, x, z, size, base=0):
    course(m,x,z,[(base,size*.47,"wall"),(base+size*.49,size*.49,"roof"),
                  (base+size*.88,size*.072,"trim")],6,flat=.89)
    m.block("door",x,base,z-size*.356,size*.25,size*.32,size*.143)


def timber(m):
    return "timber" if m.era < 2 else "metal"


def crate(m, x, y, z, size):
    m.block("timber" if m.era < 3 else "metal", x, y, z, size, size*.76, size*.83, .045)
    if m.lod == 0:
        m.block("banner", x-.12*size, y+.01, z, size*.15, size*.77, size*.852)


def canopy(m, x, y, z, w, d, height):
    """A tensioned, scalloped trade canopy with a ridge and two eave rails."""
    base = timber(m)
    roof_roles = ("hide", "roof_tile", "metal", "plaster")
    for side in (-1, 1):
        for end in (-1, 1):
            m.block(base, x+side*w*.46, y, z+end*d*.43, .058, height*.83, .065)
        if m.lod < 2:
            m.post(base, (x+side*w*.46, y+height*.82, z-d*.44),
                   (x+side*w*.46, y+height*.995, z+.02*d), .036, sides=5)
            # Rear spar is let into the inner cheek of the front spar at the apex.
            rear_x = x+side*(w*.46-.019)
            m.post(base, (rear_x, y+height*.995, z+.02*d),
                   (rear_x, y+height*.82, z+d*.44), .032, sides=5)
    n = (7, 5, 3)[m.lod]
    for j in range(n):
        def line(i):
            t = i/n
            sag = .047*math.sin(math.pi*t)
            px = x+(t-.5)*w
            return [(px, y+height*k-sag, z+d*v) for v,k in
                    ((-.50,.80),(-.23,.85),(.035,1.),(.29,.9),(.5,.81))]
        a, b = line(j), line(j+1)
        light_band = ("ivory", "plaster", "ivory", "glass")[m.era]
        mat = "cloth" if j % 2 == 0 else light_band
        if m.lod == 2:
            a, b = a[::2], b[::2]
        for k in range(len(a)-1):
            m.panel(mat, (a[k],a[k+1],b[k+1],b[k]))
        # Thick valance with a tooth-tip sill, more substantial than a paper roof.
        mid = ((a[0][0]+b[0][0])/2, min(a[0][1],b[0][1])-.088, a[0][2]+.012)
        m.panel(mat, (a[0], b[0], mid))
    if m.lod < 2:
        m.post(roof_roles[m.era], (x-w*.505, y+height, z+.035*d),
               (x+w*.505, y+height, z+.035*d), .024, sides=5)


def market(m):
    # A rear merchant lodge opens onto a covered counter and a clear front aisle.
    h = (.97,1.06,.91,1.13)[m.era]
    m.hut(-.27,.39,.99,roof_material=("hide","roof_tile","metal","glass")[m.era])
    canopy(m, .11, 0, -.30, 1.53, .84, h)
    m.block("wall" if m.era==1 else "trim", .11, 0, -.49, 1.18, .29, .31, .055)
    m.block("hide" if m.era == 0 else "plaster", .11, .29, -.49, 1.26,.054,.37)
    for i, x in enumerate((-.29,.10,.49)):
        course(m,x,-.51,[(.344,.112,"timber"),(.443,.134,"ivory")],(8,6,4)[m.lod])
        if m.lod < 2:
            for j in range(3 if m.lod == 0 else 1):
                cx=x+.059*math.cos(j*2.1)
                cz=-.51+.048*math.sin(j*2.1)
                role=("leaf","gold","roof_tile")[i]
                m.dome((cx,.44,cz),.059,m.col(role),rings=1,segments=5,material=role)
    if m.lod < 2:
        m.tusk(-.60,h*.78,-.67,-1,.47)
        m.tusk(.82,h*.78,-.67,1,.42)
        if m.era >= 2:
            m.flag(.55,0,.49,1.04,.38)
        crate(m,.70,0,.22,.27)
        if m.lod == 0:
            crate(m,.73,.205,.22,.19)
    else:
        m.tusk(-.60,h*.79,-.67,-1,.47)
    # Exchanges grow permanent supports, delivery scales, then a lit price board.
    if m.era==1:
        for px in (-.595,.813):
            course(m,px,-.662,[(0,.085,"fieldstone"),(.11,.080,"plaster"),
                                (h*.74,.062,"gold"),(h*.81,.091,"trim")],(8,6,4)[m.lod])
    elif m.era==2:
        m.block("brick",-.69,0,-.14,.25,.44,.25,.08)
        m.block("metal",-.69,.44,-.14,.20,.72,.15,.15)
        m.block("metal",-.69,1.14,-.14,.26,.096,.19,.08)
        if m.lod<2:
            coin(m,-.69,1.01,-.22,.097)
    elif m.era==3:
        m.block("plaster",.70,0,.17,.20,1.13,.16,.10)
        m.block("trim",.67,1.05,.16,.44,.26,.12,.07)
        m.block("turquoise",.67,1.09,.09,.35,.16,.037,.04)
        if m.lod==0:
            for i in range(3):
                m.block("ivory",.56+i*.10,1.13,.065,.041,.065,.021,.05)


def inset_gable(m, w, d, wall, roof, rise, x=0., z=.08):
    # Thick roof fascia. Paired, painted sails flank a brighter ridge saddle.
    m.block("fieldstone",x,0,z,w*1.045,.153,d*1.035,.045)
    m.block(("hide","plaster","brick","glass")[m.era],x,.149,z,w,wall-.149,d)
    m.block("trim",x,wall-.038,z,w*1.025,.075,d*1.025)
    m.gable_roof((x,wall+.036,z),w*1.11,d*1.09,rise,m.col(roof),overhang=0,material=roof)
    if m.lod < 2:
        for side in (-1,1):
            for end in (-1,1):
                m.block(timber(m),x+side*w*.478,.09,z+end*d*.483,.086,wall-.05,.094)
        for side in (-1,1):
            mx=x+side*w*.36
            m.post("cloth",(mx,wall+.043,z-d*.54),(mx,wall+rise+.048,z),
                   .040,sides=5)
    if m.lod == 0:
        for s in (-1,1):
            m.block("door",x+s*w*.503,wall*.34,z,.039,wall*.40,d*.47)
            m.block("emissive" if m.era<2 else "glass",x+s*w*.524,wall*.39,z,.018,wall*.28,d*.385)


def coin(m, x, y, z, radius):
    with m.transform(translate=(x,y,z),rotate_x=-90):
        course(m,0,0,[(0.,radius*.96,"trim"),(.054,radius,"gold"),(.073,radius*.88,"gold")],
               (14,8,6)[m.lod])
        if m.lod < 2:
            m.block("accent",0,.073,0,radius*.30,.018,radius*1.2,.12)


def counting_tower(m):
    """A brick strongroom with a public pay arcade at the foot of its assay tower."""
    x,z=.37,.19
    m.block("fieldstone",.01,0,.035,1.73,.135,1.30,.055)
    m.block("brick",x,.135,z,.83,1.12,.86,.035)
    m.block("trim",x,1.255,z,.90,.105,.93,.045)
    m.block("metal",x,1.36,z,.81,.23,.83,.40)
    m.block("gold",x,1.59,z,.46,.048,.47,.10)
    # Tusks grow from individual iron shoes at the edge of the parapet.
    for side in (-1,1):
        sx=x+side*.325
        if m.lod<2:
            m.block("banner",sx,1.32,-.074,.151,.095,.166,.12)
        m.tusk(sx,1.407,-.074,side,.54)
    coin(m,x,.962,-.258,.22)
    m.entry(x,-.242,.135,.32,.47,2)
    for j in range((3,2,1)[m.lod]):
        n=(3,2,1)[m.lod]
        m.block("fieldstone",x,0,-.843+(j+.5)*.24/n,.50,.135*(j+1)/n,.24/n)

    # A low, iron-roofed counting hall opens onto the pay windows under a lean-to.
    hx=-.40
    m.block("wall",hx,.135,.11,.74,.55,.89)
    m.prism_x((hx,.685,.11),.84,1.01,.225,m.col("metal"),material="metal")
    m.block("door",hx,.346,-.342,.60,.234,.048)
    m.block("timber",hx,.328,-.404,.67,.052,.20)
    with m.transform(translate=(hx,.669,-.453),rotate_x=-14):
        m.block("banner",0,0,0,.85,.056,.37)
    for side in (-1,1):
        m.block("trim",hx+side*.36,.135,-.568,.068,.518,.076)
    if m.lod<2:
        for slot in (-.22,0,.22):
            m.block("gold",hx+slot,.379,-.374,.031,.206,.023)
        # The high security lights are aligned to the tapered tower wall.
        for side in (-1,1):
            with m.transform(translate=(x+side*.412,0,z),rotate_y=-90*side):
                m.block("trim",0,.79,-.003,.46,.26,.045)
                m.block("glass",0,.819,-.027,.38,.196,.019)
                for bar in (-.105,.105):
                    m.block("metal",bar,.809,-.039,.030,.215,.021)
    if m.lod==0:
        for side in (-1,1):
            m.post("trim",(x+side*.416,.14,-.206),(x+side*.378,.696,-.181),
                   .063,.052,sides=7)
            for k in range(2):
                course(m,hx+side*.20,-.431,[(.380+k*.020,.047,"gold"),(.400+k*.020,.047,"gold")],10)
        m.block("metal",x,.332,-.329,.25,.053,.031,.06)
        m.block("gold",x-.089,.335,-.350,.049,.044,.029,.12)
        m.block("banner",x,1.183,-.234,.58,.053,.047,.04)


def bank(m):
    if m.era==2:
        return counting_tower(m)
    # Broad low vault, flared ingot cap and mounted mint coin above the counting wicket.
    h=(.69,.74,.82,.91)[m.era]
    course(m,0,.13,[(0,.84,"fieldstone"),(.15,.87,"wall"),
                     (h,.78,"trim"),(h+.080,.86,"roof"),(h+.27,.63,"metal")],flat=.83)
    course(m,0,.13,[(h+.265,.614,"banner"),(h+.35,.56,"roof"),
                     (h+(.54,.64,.47,.70)[m.era],.20,"gold")],flat=.85)
    m.entry(0,-.54,0,.42,.47,m.era)
    m.block("fieldstone",0,0,-.73,.74,.083,.37,.085)
    for x in (-.33,.33):
        m.block("trim",x,.08,-.71,.123,.57,.14,.09)
        m.tusk(x,.61,-.71,1 if x>0 else -1,.53)
    m.block("banner",0,.598,-.66,.79,.105,.27,.065)
    coin(m,0,.80,-.701,.191)
    if m.lod < 2:
        for s in (-1,1):
            m.post(timber(m),(s*.72,.14,-.28),(s*.65,h+.045,-.26),.060,sides=6)
        m.flag(-.51,.12,.48,h+.55,.34)
    if m.lod < 2:
        # The mint evolves from masonry vault ribs through brick vents to luminous coin pylons.
        for s in (-1,1):
            if m.era==1:
                m.block("fieldstone",s*.72,.036,.13,.29,.49,.32,.26)
            else:
                k=h+.32+(.24 if m.era==3 else 0)
                course(m,s*.59,.46,[(0,.13,"brick" if m.era==2 else "glass"),
                                     (k,.105,"trim" if m.era==2 else "ascension"),
                                     (k+.072,.131,"metal")],(9,6)[m.lod])
    if m.lod == 0:
        crate(m,.68,0,-.53,.24)
        course(m,.68,-.53,[(.182,.083,"gold"),(.211,.083,"gold")],8)
        m.block("ivory",-.49,.32,-.51,.089,.13,.044,.15)
        m.block("timber",.52,0,-.57,.12,.22,.11)
        coin(m,.52,.253,-.619,.09)


def guild(m):
    h=(.60,.80,.76,.93)[m.era]
    roof=("hide","slate","metal","plaster")[m.era]
    inset_gable(m,1.28,.92,h,roof,(.36,.53,.39,.34)[m.era],z=.11)
    m.hut(.57,.23,.70,base=0,entry=False)
    m.entry(-.12,-.40,0,.43,.48,m.era)
    # Toolwrights' gate: heavy ivory arch, suspended clan cloth, crossed hammers.
    for s in (-1,1):
        px=-.12+s*.34
        m.block(timber(m),px,0,-.55,.11,.72,.131,.12)
        m.tusk(px,.65,-.55,s,.58)
    m.block("trim",-.12,.637,-.55,.81,.14,.15)
    m.block("cloth",-.12,.67,-.64,.44,.26,.045)
    for s in (-1,1):
        lower=(-.12-s*.115,.689,-.669-s*.012)
        upper=(-.12+s*.117,.88,-.669-s*.012)
        m.post("gold",lower,upper,.026,sides=5)
        with m.transform(translate=upper,rotate_z=35*s):
            m.block("ivory",0,-.028,0,.126,.053,.058,.07)
    if m.lod < 2:
        m.drum(-.60,0,-.52,.153,.24)
        if m.era>=2:
            course(m,-.43,.34,[(h*.6,.102,"metal"),(h+(.62 if m.era==2 else .51),.092,"banner"),
                                (h+(.70 if m.era==2 else .59),.117,"trim")],7)
    if m.lod==0:
        crate(m,.53,0,-.52,.235)
        m.flag(.57,.15,.25,h+.49,.30)


def deck(m,x,y,z,w,d):
    m.block("trim",x,y-.072,z,w,.085,d)
    # Ashlar on the customs quay changes from pale carved stone to warm ochre.
    surface=("timber","plaster","timber","metal")[m.era]
    if m.lod==0:
        for i in range(10):
            m.block(surface,x,y+.013,z+d*((i+.5)/10-.5),w*.99,.023,d*.095)
    else:
        m.block(surface,x,y+.013,z,w*.99,.023,d*.994)


def boat(m, x, z, w, d):
    with m.transform(translate=(x,0,z),scale=(w,1,d)):
        plan=[(0,-.95),(.34,-.69),(.47,-.27),(.43,.53),(.30,.84),(0,.93),
              (-.30,.84),(-.43,.53),(-.47,-.27),(-.34,-.69)]
        if m.lod==2:
            plan=[plan[i] for i in (0,2,3,5,7,8)]
        bottom=[(a*.59,.018+.047*b*b,b*.85) for a,b in plan]
        rim=[(a,.26+.13*b*b,b) for a,b in plan]
        inner=[(.81*a,.26+.13*b*b,b*.92) for a,b in plan]
        floor=[(.52*a,.13+.07*b*b,b*.76) for a,b in plan]
        for i in range(len(plan)):
            j=(i+1)%len(plan)
            for mat,face in (("wall" if m.era==1 else "metal",(bottom[j],bottom[i],rim[i],rim[j])),
                             ("ivory",(rim[j],rim[i],inner[i],inner[j])),
                             ("timber",(inner[j],inner[i],floor[i],floor[j]))):
                m.panel(mat,face)
        for i in range(len(plan)):
            j=(i+1)%len(plan)
            m.panel("timber", (floor[j],floor[i],(0,.128,0)))
        # Timber cargo pallet above the bilge. 
        m.block("timber",0,.129,0,.495,.08,.84)
        if m.lod<2:
            crate(m,0,.209,.12,.39)
            m.block("banner",0,.237,-.40,.62,.041,.15)
        with m.transform(translate=(0,.301,-.80),rotate_y=90):
            m.tusk(0,0,0,1,.515)
        if m.lod<2:
            # Bow paint follows the hull between the bilge and the gunwale.
            for i in (0,1,len(plan)-2,len(plan)-1):
                j=(i+1)%len(plan)
                def paint(k,t):
                    return tuple((a+t*(b-a))*(1.013 if dim!=1 else 1)
                                 for dim,(a,b) in enumerate(zip(bottom[k],rim[k])))
                m.panel("cloth",(paint(j,.56),paint(i,.56),paint(i,.74),paint(j,.74)))


def transfer_dock(m):
    # The freighter berths broadside underneath a rail-travelling portal crane.
    m.block("water",0,0,-.28,1.94,.043,1.20)
    m.block("wall",0,0,.55,1.89,.22,.68,.04)
    deck(m,0,.22,.55,1.91,.68)
    with m.transform(translate=(.03,0,-.15),rotate_y=90):
        boat(m,0,0,.77,.90)
    for x in (-.61,.15):
        for z in (-.64,.46):
            if m.lod<2:
                m.block("fieldstone",x,0,z,.23,.26,.20,.07)
            m.post("ivory",(x,.24 if m.lod<2 else .022,z),(x+.085,1.00,z*.88),.070,.055,
                   sides=(8,5,4)[m.lod])
        m.block("metal",x+.083,.975,-.083,.16,.13,1.21,.05)
        if m.lod<2:
            m.tusk(x+.08,1.08,.38,1,.36)
    m.block("turquoise",-.16,1.085,-.20,.94,.14,.24,.07)
    for x in ((-.30,-.07) if m.lod<2 else (-.185,)):
        m.post("metal",(x,1.10,-.20),(x,.64,-.20),.020,sides=4)
    crate(m,-.186,.41,-.20,.30)
    m.shell(.55,.56,[(.256,.27,"metal"),(.35,.28,"ivory"),(.57,.23,"glass"),
                       (.65,.29,"turquoise"),(.76,.15,"ivory")],(10,6,4)[m.lod],flatten=.73)
    m.entry(.55,.368,.255,.23,.25,3)
    if m.lod==0:
        for x in (.03,-.54):
            crate(m,x,.256,.62,.20)
        for x in (-.83,-.43,.04,.52):
            m.post("ivory",(x,.254,.83),(x,.45,.83),.024,sides=5)
        m.post("metal",(-.85,.45,.83),(.54,.45,.83),.021,sides=5)



def harbor(m):
    if m.era==3:
        return transfer_dock(m)
    if m.era==2:
        # A marine railway hauls the cargo boat stern-first into its ribbed
        # shed. The working quay stands on piles beside the wet end of the slip.
        m.block('water',.024,0,-.533,1.83,.044,.84)
        m.block('fieldstone',.02,0,.396,1.83,.123,1.01,.055)
        x=.112
        for s in (-1,1):
            m.block('metal',x+s*.195,.104,.091,.046,.041,1.54)
            if m.lod<2:
                for z in (-.31,.39):
                    m.block('timber',x+s*.177,.129,z,.112,.081,.158)
            else:
                m.block('timber',x+s*.177,.129,.04,.112,.081,.82)
        with m.transform(translate=(x,.167,-.126)):
            boat(m,0,0,.78,.777)
        for s in (-1,1):
            for z in (.071,.719):
                m.block('brick',x+s*.44,.119,z,.165,.265,.154,.09)
                m.post('metal',(x+s*.44,.379,z),(x+s*.417,.884,z),.061,.056)
                if m.lod<2:
                    m.post('trim',(x+s*.417,.637,z),(x+s*.286,.908,z),.041)
            m.block('metal',x+s*.411,.865,.392,.125,.108,.87)
        with m.transform(translate=(x,0,.391),rotate_y=90):
            m.gable_roof((0,.954,0),.97,1.19,.324,m.col('slate'),overhang=0,material='slate')
        m.shell(-.644,.574,[(.119,.117,'brick'),(.732,.089,'metal'),(.799,.112,'trim')])
        for z in (-.68,-.13):
            m.block('timber',-.625,0,z,.096,.19,.106)
        deck(m,-.629,.187,-.39,.33,.81)
        crate(m,-.63,.222,-.36,.207)
        if m.lod<2:
            for s in (-1,1):
                m.tusk(x+s*.394,.963,.078,s,.312)
            crate(m,-.641,.123,.235,.211)
        if m.lod==0:
            for z in (-.63,-.14):
                m.post('metal',(-.753,.222,z),(-.753,.419,z),.022)
            m.post('trim',(-.753,.416,-.642),(-.753,.416,-.121),.021)
        return
    m.block("water",.49,0,0,.93,.042,1.88)
    for x in (-.82,.035):
        for z in (-.77,.67):
            m.block(timber(m),x,0,z,.093,.218,.11)
    deck(m,-.39,.218,-.032,1.03,1.76)
    if m.lod == 2:
        distant_lodge(m,-.42,.39,.83,base=.254)
    else:
        m.hut(-.42,.39,.83,base=.254,roof_material="plaster" if m.era==1 else None)
    boat(m,.51,-.04,.81,.87)
    px,pz=-.42,-.43
    h=(.97,1.01,1.13,1.28)[m.era]
    course(m,px,pz,[(.254,.17,"fieldstone"),(.34,.151,"wall" if m.era==1 else timber(m)),(h,.117,"banner"),
                     (h+.08,.136,"trim")],8)
    m.post(timber(m),(px,h-.05,pz),(.49,h+.28,pz+.023),.072,.049,sides=(8,6,4)[m.lod])
    m.post(timber(m),(px,h-.36,pz),(.24,h+.182,pz+.02),.053,.045,sides=(7,5,4)[m.lod])
    m.post("trim",(.47,h+.27,pz+.023),(.47,.59,pz+.023),.021,sides=4)
    crate(m,.47,.423,pz+.023,.225)
    if m.lod<2:
        # Sheave and counterweight make the cargo derrick legible as a crane.
        m.post("gold",(.43,h+.246,pz-.06),(.43,h+.246,pz+.086),.082,sides=8)
        m.block("fieldstone",-.62,h-.07,pz,.31,.162,.21,.12)
        m.flag(-.72,.254,.055,.96,.31)
    if m.lod==0:
        crate(m,-.71,.254,-.67,.245)
        crate(m,-.70,.440,-.67,.169)


def railway(m):
    # The rails have a clear approach from both sides. The short tram sits at the platform.
    m.block("fieldstone",0,0,-.48,1.88,.057,.59,.06)
    for i in range((10,6,3)[m.lod]):
        n=(10,6,3)[m.lod]
        m.block("timber",-.89+1.78*i/(n-1),.057,-.48,.074,.044,.47)
    for z in (-.65,-.32):
        m.block("metal",0,.099,z,1.90,.048,.043)
    m.block("wall",0,0,.19,1.88,.20,.75,.045)
    m.block("ivory",0,.20,.19,1.89,.054,.758)
    if m.lod==2 or m.lod==1 and m.era==2:
        m.block("wall",.18,.254,.58,.81,.416,.60)
        m.prism_x((.18,.67,.58),.87 if m.lod==1 else .85,.64,.24,m.col("roof"),material="roof")
    else:
        inset_gable(m,.81,.60,.67,"slate" if m.era==2 else "glass",.24,x=.18,z=.58)
    if m.lod < 2:
        m.entry(.18,.24,.253,.29,.34,m.era)
    else:
        m.block("door",.18,.254,.269,.28,.32,.085)
    canopy(m,0,.254,.139,1.74,.49,.61)
    with m.transform(translate=(-.35,.147,-.486)):
        for j in (-1,1):
            if m.lod==2 or m.lod==1 and m.era==2:
                m.post("trim",(j*.23,.083,-.198),(j*.23,.083,.198),.083,sides=4)
            else:
                for s in (-1,1):
                    m.post("trim",(j*.23,.083,s*.144),(j*.23,.083,s*.199),.083,sides=(9,6)[m.lod])
        m.block("metal",0,.092,0,.81,.084,.39,.06)
        if m.era==2:
            m.post("metal",(-.30,.281,0),(.08,.281,0),.124,sides=(10,7,5)[m.lod])
            m.block("banner",.195,.175,0,.244,.266,.337)
            m.block("roof",.197,.442,0,.288,.047,.379)
            course(m,-.191,0,[(.38,.052,"metal"),(.54,.060,"trim"),(.58,.078,"door")],7)
        else:
            m.block("cloth",0,.176,0,.77,.14,.348,.12)
            m.block("glass",.015,.31,0,.65,.134,.30,.22)
            m.block("plaster",.015,.443,0,.55,.052,.267,.10)
    if m.lod<2:
        m.block("trim",.57,.254,.42,.07,.86,.085)
        m.block("banner",.57,.97,.40,.24,.24,.11,.09)
        if m.lod==0:
            coin(m,.57,1.09,.348,.111)
        else:
            m.post("ivory",(.57,1.09,.35),(.57,1.09,.325),.101,sides=6)
        m.tusk(.81,.73,-.041,1,.36)
        m.tusk(-.82,.73,-.041,-1,.36)
        if m.lod==0:
            m.flag(-.79,.254,.38,.91,.33)
            crate(m,.74,.254,.28,.18)


def aircraft(m):
    # A compact high-wing freight plane with a rear stabilizer, canopy and three-wheel stance.
    with m.transform(translate=(.33,0,-.25),rotate_y=-14):
        if m.lod==2:
            m.block("metal",0,.046,-.03,.23,.15,.42)
            m.post("ivory",(0,.20,-.56),(0,.263,.49),.105,.044,sides=5)
        for x,z in (() if m.lod==2 else ((-.17,.07),(.17,.07),(0,-.39))):
            m.block("metal",x,.048,z,.052,.142,.097,.10)
        if m.lod<2:
            m.post("ivory",(0,.207,-.57),(0,.215,-.32),.051,.118,sides=(10,7,5)[m.lod])
            m.post("plaster",(0,.215,-.321),(0,.235,.15),.118,.092,sides=(10,7,5)[m.lod])
            m.post("ivory",(0,.235,.149),(0,.28,.52),.092,.043,sides=(8,6,4)[m.lod])
        m.dome((0,.293,-.235),.095,m.col("glass"),rings=(3,2,1)[m.lod],segments=8,
               squash=.72,material="glass")
        for s in (-1,1):
            with m.transform(scale=(s,1,1)):
                for y,dz,span,chord,mat in ((.25,-.11,.49,.24,"roof"),(.284,.31,.242,.114,"cloth")):
                    p=[(.035,y,dz),(.10,y+.009,dz-.06), (span,y+.052,dz+.115),
                       (span*.97,y+.055,dz+.205),(.065,y,dz+chord)]
                    m.panel(mat,p[::-1])
                    q=[(a,b-.043,c) for a,b,c in p]
                    m.panel("metal",q)
                    for i in range(len(p)):
                        j=(i+1)%len(p)
                        m.panel(mat,(p[i],p[j],q[j],q[i]))
        m.post("banner",(0,.276,.40),(0,.47,.51),.07,.027,sides=4)


def airport(m):
    m.block("slate",.30,0,-.10,1.14,.046,1.69,.022)
    if m.lod<2:
        for i in range(4):
            m.block("ivory",.05+i*.23,.046,-.84,.114,.008,.070)
    for i in range((5,3,1)[m.lod]):
        m.block("ivory",.46,.046,-.78+i*.29,.033,.006,.15)
    aircraft(m)
    if m.lod == 2:
        distant_lodge(m,-.46,.29,.86)
    else:
        m.hut(-.46,.29,.86,entry=True)
    x,z=-.58,-.37
    course(m,x,z,[(0,.229,"wall"),(.12,.210,"plaster"),(.74,.122,"banner"),
                  (.85,.20,"glass"),(1.07,.25,"ivory"),(1.14,.27,"metal"),(1.23,.085,"gold")])
    for s in (-1,1):
        m.tusk(x+s*.15,1.09,z,s,.44)
    m.post("metal",(x,1.22,z),(x-.015,1.59,z+.01),.031,.013,sides=5)
    if m.lod<2:
        m.flag(.60,0,.70,.81,.28)
        m.block("metal",-.29,0,-.56,.25,.218,.22,.064)
    if m.lod==0:
        for k in (-1,1):
            course(m,.72,-.66+k*.11,[(.046,.036,"metal"),(.131,.031,"emissive")],6)


MODELS = {"Airport": airport, "Bank": bank, "Guildhall": guild, "Harbor": harbor,
          "Market": market, "RailStation": railway}


def build(spec, lod):
    if spec.building_type == "Bank" and spec.band == "future":
        return build_lodge(spec,lod,lambda m,t: orc_civic_trades.bank(m))
    if spec.building_type == "RailStation" and spec.band == "future":
        from generators.buildings.orc_era_forms import skyrail
        return build_lodge(spec,lod,lambda m,t: skyrail(m))
    m=Lodge(spec,lod)
    w,d=spec.footprint
    with m.transform(scale=(w*.49,min(w,d)*.49,d*.49)):
        MODELS[spec.building_type](m)
    return m


if __name__ == "__main__":
    generate(PACKAGE,build)
