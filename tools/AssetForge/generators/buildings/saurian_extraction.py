"""Tidal ore galleries, quarry terraces, reedwood log camps and delta pumpworks."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

PACKAGE="saurian_extraction"


def boulder(m, role, x,y,z,r,h, twist=0):
    # Displaced hexagonal strata; the same rock keeps its silhouette at distance.
    n=(7,6,5)[m.lod]
    rings=[]
    for k,(rad,dy,ox) in enumerate(((.88,0,0),(1,.42,.02),(.60,.89,-.09),(.28,1,-.13))):
        if m.lod and k==2:
            continue
        ring=[]
        for i in range(n):
            a=2*math.pi*i/n+twist
            sway=1+.105*math.sin(3*a+1.1)
            ring.append((x+r*(ox+sway*rad*math.cos(a)),y+h*dy,z+r*.79*sway*rad*math.sin(a)))
        rings.append(ring)
    for low,high in zip(rings,rings[1:]):
        for i in range(n):
            j=(i+1)%n
            m.face(role,[low[j],low[i],high[i],high[j]])
    m.face(role,rings[-1][::-1])


def pulley(m,x,y,z,r):
    m.ring("metal" if m.era>=2 else "timber",x,y,z,r,r*.65,r*.45,n=(12,8,5)[m.lod])
    for i in range((5,4,3)[m.lod]):
        a=i*math.tau/(5,4,3)[m.lod]
        m.rod("accent",(x+r*.16*math.cos(a),y+r*.16*math.sin(a),z+r*.22),
              (x+r*.75*math.cos(a),y+r*.75*math.sin(a),z+r*.22),r*.095,4)
    m.disc(x,y,z+r*.08,r*.30,"gold",False)


def ore_belt(m,x,z,w,length,low,angle):
    """Inclined belt, its two abutments and deep girder cheeks."""
    a=math.radians(angle)
    middle=low+length*math.sin(a)*.5
    for side in (-1,1):
        offset=side*length*.39
        zz=z+offset*math.cos(a)
        h=middle+offset*math.sin(a)
        m.slab("trim",x,.08,zz,w*.68,h-.08,w*.47,.83)
    with m.transform(translate=(x,middle,z),rotate_x=-angle):
        m.box((0,0,0),(w,.065,length),m.color("metal"),material="metal",bottom=True)
        for s in (-1,1):
            m.block("turquoise",s*w*.47,.0325,0,w*.105,.07,length*.98)
        if m.lod==0:
            for i in range(5):
                m.block("trim",0,.0325,(i-2)*length*.16,w*.80,.029,.037)


def ore_chunk(m,role,x,y,z,r,h,twist):
    if m.lod<2:
        return boulder(m,role,x,y,z,r,h,twist)
    # A small nugget reads as one facet group. Keep the wider shoulder of
    # bedrock outcrops so the reduced adit still has its supporting overburden.
    def vertices(rad,height):
        return [(x+r*rad*math.cos(twist+k*math.tau/5),y+height,
                 z+r*.79*rad*math.sin(twist+k*math.tau/5)) for k in range(5)]
    base=vertices(.93,0)
    if r>=.30:
        shoulders=vertices(.89,h*.51)
        for i,b in enumerate(base):
            j=(i+1)%5
            m.face(role,[base[j],b,shoulders[i],shoulders[j]])
        base=shoulders
    tip=(x-r*.11,y+h,z+r*.053)
    for i,v in enumerate(base):
        m.face(role,[base[(i+1)%5],v,tip])


def mineral_bench(m):
    """Two cut faces with a continuous mineral seam and a walkable ledge between them."""
    sections=[]
    for x,front,low,high,back in ((-.86,-.04,.38,.81,.65),(-.43,.042,.47,1.03,.78),
                                 (.08,.001,.45,.89,.73),(.47,.095,.37,.76,.77),
                                 (.84,.177,.28,.51,.65)):
        profile=[(front,.08),(front+.011,low*.53),(front+.024,low*.64),
                 (front+.055,low),(front+.255,low+.012),
                 (front+.284,high*.77),(front+.298,high*.84),
                 (front+.333,high),(back,high-.085),(back,.08)]
        sections.append([(x,y,z) for z,y in profile])
    strata=("fieldstone","metal","fieldstone","slate","fieldstone","metal",
            "fieldstone","slate","fieldstone")
    for left,right in zip(sections,sections[1:]):
        for j,role in enumerate(strata):
            m.face(role,[right[j],left[j],left[j+1],right[j+1]])
    for side,edge in ((-1,sections[0]),(1,sections[-1])):
        for a,b in zip(edge,edge[1:]):
            if a[2]==b[2]:
                continue
            face=[(a[0],.08,a[2])]
            if a[1]>.08:
                face.append(a)
            face.extend([b,(b[0],.08,b[2])])
            m.face("fieldstone",face if side==1 else face[::-1])


def haul_track(m,z):
    # The long bottom run sits flat. The curved ends and road wheels are still
    # visible underneath the dump body from the two opposing isometric views.
    outline=[(-.54,.08),(.49,.08),(.61,.17),(.56,.29),(-.55,.29),(-.66,.19)]
    near=[(x,y,z-.068) for x,y in outline]
    far=[(x,y,z+.068) for x,y in outline]
    m.face("trim",near[::-1])
    m.face("trim",far)
    for i in range(len(near)):
        j=(i+1)%len(near)
        m.face("metal",[near[i],near[j],far[j],far[i]])
    if m.lod<2:
        for x in (-.47,-.15,.17,.47):
            with m.transform(translate=(x,.181,z),rotate_x=-90):
                m.drum("metal",0,-.078,0,.081,.156,n=(10,6)[m.lod],bottom=True)


def bench_hauler(m):
    z=-.400
    for side in (-1,1):
        haul_track(m,z+side*.183)
    m.slab("turquoise",-.01,.29,z,1.34,.088,.49,.94,underside=True)
    m.slab("ivory",-.466,.378,z,.40,.15,.43,.94)
    m.slab("glass",-.471,.528,z,.351,.232,.39,.81)
    m.slab("ivory",-.471,.760,z,.355,.045,.397,.93,underside=True)
    # A flared ore skip, with a solid rim and an open interior for its load.
    bottom=[(-.224,.378,z-.166),(.503,.378,z-.166),
            (.503,.378,z+.166),(-.224,.378,z+.166)]
    rim=[(-.247,.60,z-.275),(.70,.682,z-.275),(.70,.682,z+.275),(-.247,.60,z+.275)]
    inner=[(x+(.027 if x<0 else -.035),y,zg+(.033 if zg<z else -.033)) for x,y,zg in rim]
    floor=[(x*.94+.006,.409,z+(zg-z)*.90) for x,y,zg in bottom]
    for i in range(4):
        j=(i+1)%4
        m.face("turquoise",[bottom[j],bottom[i],rim[i],rim[j]])
        m.face("ivory",[rim[j],rim[i],inner[i],inner[j]])
        m.face("metal",[inner[j],inner[i],floor[i],floor[j]])
    m.face("slate",floor[::-1])
    for x,dz,r,h in ((-.087,-.018,.167,.25),(.13,.031,.207,.31),(.40,-.023,.21,.29)):
        ore_chunk(m,"fieldstone",x,.409,z+dz,r,h,.27+x)
    if m.lod<2:
        m.disc(-.466,.453,z-.215,.052,"gold",False)
        for x in (-.08,.21,.49):
            m.rod("ivory",(x,.40,z-.173),(x+.048,.602+(x+.16)*.07,z-.277),.020,n=4)


def surface_mine(m):
    mineral_bench(m)
    bench_hauler(m)
    # A short loading chute is built into the upper rock cut. Its lip spills
    # into the hauler from the side, leaving the ore exposed throughout.
    with m.transform(translate=(.34,.665,.125),rotate_x=-23):
        m.box((0,0,0),(.26,.051,.60),m.color("metal"),material="metal",bottom=True)
        for side in (-1,1):
            m.block("ivory",side*.12,.0255,0,.034,.081,.59)
        if m.lod==0:
            for zz in (-.13,.025,.17):
                m.block("metal",0,.0255,zz,.195,.021,.018)


def ore_gallery(m):
    works.court(m,role="soil")
    if m.era==1:
        # A timbered adit disappears between the two bedrock shoulders.
        # Ore carts run out onto the loading apron beside a heap of tailings.
        ore_chunk(m,"fieldstone",-.55,.08,.16,.34,.81,.41)
        ore_chunk(m,"fieldstone",.43,.08,.34,.45,.68,.84)
        ore_chunk(m,"fieldstone",-.18,.08,.46,.40,.98,.18)
        x=-.11
        m.block("door",x,.08,.075,.51,.40,.10)
        for side in (-1,1):
            m.rod("timber",(x+side*.26,.08,-.12),(x+side*.205,.52,-.12),.062,n=6)
        m.block("timber",x,.505,-.12,.68,.12,.22)
        ore_chunk(m,"fieldstone",x,.595,-.008,.31,.24,.33)
        for z in ((-.68,-.49,-.30,-.11) if m.lod<2 else (-.62,-.16)):
            m.block("timber",x,.08,z,.51,.027,.059)
        for sign in (-1,1):
            m.block("metal",x+sign*.146,.107,-.34,.036,.026,.86)
            for z in (-.54,-.30):
                with m.transform(translate=(x+sign*.146-.024,.206,z),rotate_z=-90):
                    m.drum("metal",0,0,0,.073,.048,n=(8,6,4)[m.lod],bottom=True)
        m.slab("timber",x,.228,-.42,.385,.21,.42,1.19,cut=.045,underside=True)
        ore_chunk(m,"metal",x-.063,.438,-.45,.135,.11,.37)
        ore_chunk(m,"fieldstone",x+.081,.438,-.37,.108,.087,.5)
        ore_chunk(m,"soil",.58,.08,-.31,.264,.26,.24)
        for xx,zz,r in ((.45,-.46,.093),(.73,-.32,.083)):
            ore_chunk(m,"metal",xx,.08,zz,r,r,.61)
        if m.lod<2:
            for sx,yy,zz in ((-.63,.50,-.015),(.55,.33,.027)):
                boulder(m,"metal",sx,yy,zz,.10,.105,.21)
    elif m.era==2:
        # Elevated ore tipple on column bearings empties onto a sloping belt.
        for x in (-.40,.05):
            for z in (.22,.62):
                m.block("brick",x,.08,z,.125,.69,.13)
        m.slab("metal",-.175,.77,.42,.53,.35,.47,1.36,underside=True)
        m.slab("brick",-.175,1.12,.42,.75,.22,.66)
        m.rooflet(-.175,1.34,.42,.80,.72,.22,"metal")
        ore_belt(m,-.18,-.25,.39,1.02,.19,24)
        works.barrel(m,.54,.14,.46,.75,.35,.13)
        works.cartouche(m,-.175,1.22,.087,.060)
    else:
        surface_mine(m)


def placer_works(m):
    works.court(m,-.46,role="soil")
    if m.era==1:
        # River wheel turns a line of timber stampers over a gravel-washing bed.
        works.basin(m,.14,-.22,1.18,.91)
        for zz in (-.25,.13):
            m.slab("wall",.27,.177,zz,.16,.47,.122,.79)
        m.rod("timber",(.27,.64,-.37),(.27,.64,.23),.064)
        works.wheel(m,.27,.64,-.139,.422,"timber")
        works.pagoda(m,-.57,.41,.56,.72,.59)
        m.slab("gold",-.21,.177,-.52,.31,.081,.19,.85)
    elif m.era==2:
        # A horizontal rotary wash drum discharges concentrate beside its furnace.
        for z in (-.23,.47):
            m.slab("brick",-.29,.08,z,.57,.31,.19,.76)
        with m.transform(translate=(-.29,.55,-.375),rotate_x=83):
            m.drum("metal",0,0,0,.255,1.08,.238,n=(16,10,6)[m.lod],bottom=True)
            for h in (.19,.83):
                m.drum("turquoise",0,h,0,.266,.062,n=(16,9,6)[m.lod])
        works.spire(m,.51,.46,.21,.98)
        ore_belt(m,.31,-.36,.45,.71,.17,21)
        if m.lod<2:
            m.disc(-.29,.55,-.390,.105)
    else:
        # Visible gold-bearing rock feeds the upper hopper; the sloping open
        # wash plant deposits concentrate at its low end under an optical sorter.
        ore_chunk(m,"fieldstone",-.58,.08,.35,.36,.77,.37)
        ore_chunk(m,"fieldstone",-.60,.08,-.09,.23,.42,.89)
        for x,y,z,r in ((-.56,.38,.085,.134),(-.43,.54,.24,.101),(-.65,.21,-.21,.086)):
            ore_chunk(m,"gold",x,y,z,r,r*.83,.42)
        for x in (.00,.40):
            m.slab("ivory",x,.08,.50,.14,.48,.21,.78)
        m.slab("metal",.20,.56,.50,.44,.25,.33,1.48,underside=True)
        ore_chunk(m,"fieldstone",.18,.81,.49,.236,.17,.21)
        ore_chunk(m,"gold",.32,.812,.42,.112,.097,.43)
        ore_belt(m,.20,-.04,.63,1.07,.22,25)
        # Freestanding optical bridge and underside scanner sit over the riffles.
        for x in (-.22,.62):
            m.slab("ivory",x,.08,-.12,.095,.65,.19,.78)
        m.slab("turquoise",.20,.73,-.12,.96,.13,.22,.91,underside=True)
        m.drum("glass",.20,.636,-.12,.084,.094,.104)
        m.slab("metal",.20,.08,-.65,.65,.14,.27,1.10)
        for x,z,r in ((.06,-.67,.125),(.31,-.61,.129),(.43,-.70,.087)):
            ore_chunk(m,"gold",x,.22,z,r,r*.73,.27)


def coal_reclaimer(m):
    works.court(m,.41,role="soil")
    # The bucket wheel cuts into the coal face and a belt carries it to the hopper.
    boulder(m,"door",-.46,.08,.27,.47,.36,.20)
    if m.lod<2:
        boulder(m,"fieldstone",-.67,.08,.43,.22,.43,.51)
    m.slab("ivory",.25,.08,-.06,.59,.25,.48,.79)
    m.rod("metal",(.25,.33,-.06),(-.36,.67,-.11),.085)
    m.rod("ivory",(.25,.33,-.06),(.32,.87,.13),.077)
    m.rod("gold",(.32,.87,.13),(-.38,.67,-.10),.043)
    works.wheel(m,-.38,.67,-.19,.35)
    for k in range((7,5,3)[m.lod]):
        a=k*math.tau/(7,5,3)[m.lod]
        with m.transform(translate=(-.38+.34*math.cos(a),.67+.34*math.sin(a),-.145),
                         rotate_z=math.degrees(a)):
            m.slab("ivory",0,-.063,0,.125,.14,.17,.81)
    ore_belt(m,.53,.21,.31,.88,.22,31)


def mine(m,kind):
    if kind=="Mine" and m.era:
        return ore_gallery(m)
    if kind=="GoldMine" and m.era:
        return placer_works(m)
    if kind=="CoalMine" and m.era==3:
        return coal_reclaimer(m)
    role="gold" if kind=="GoldMine" else "door" if kind=="CoalMine" else "metal"
    frame="timber" if m.era<2 else "metal"
    m.slab("soil",0,0,.01,1.88,.078,1.74,.97)
    # The hill is a half-buried, stratified shoulder surrounding the timber-lined adit.
    boulder(m,"fieldstone",-.20,.078,.29,.72,.68,.17)
    boulder(m,"fieldstone",-.65,.078,.05,.29,.34,.61)
    m.slab("trim",-.22,.077,-.17,.59,.46,.39,.96)
    m.entry(-.22,.079,-.365,.391,.382)
    # Rails and sleepers emerge towards the landing.
    for s in (-1,1):
        m.block(frame,-.22+s*.128,.083,-.593,.031,.030,.46)
    if m.lod<2:
        for z in (-.46,-.61,-.76):
            m.block("timber",-.22,.078,z,.37,.021,.039)
    x=.27
    span=(.29,.30,.32,.25)[m.era]
    h=(.97,1.13,1.34,1.49)[m.era]
    for s in (-1,1):
        for t in (-1,1):
            m.rod(frame,(x+s*span,.078,.18+t*.245),(x+s*.13,h,.18+t*.094),.050,n=(6,5,4)[m.lod])
    # Open central slot lets the sheave turn freely between its bearing beams.
    for rail_z in (.038,.270):
        m.block("trim",x,h-.04,rail_z,.49,.092,.097)
    for side in (-1,1):
        m.block(frame,x+side*.213,h-.04,.154,.059,.092,.135)
    pulley(m,x,h+.10,.112,.177)
    m.block(frame,x,h+.047,.251,.125,.075,.11)
    m.block(frame,x,h+.047,.051,.113,.075,.074)
    m.rod(frame,(x,h+.10,.021),(x,h+.10,.293),.049,n=(8,6,4)[m.lod])
    if m.era in (1,3):
        for s in (-1,1):
            m.block(frame,x+s*.19,h,.19,.041,.32,.059)
        m.rooflet(x,h+.32,.19,.54,.42,.18,m.skin)
    if m.lod<2:
        m.rod(frame,(x-span*.80,.38,-.032),(x+.158,h-.13,.073),.025,4)
        m.rod("trim",(x-.149,h+.12,.166),(x-.149,.35,.166),.017)
    if kind=="GoldMine":
        # Gold-washing sluice in front of the hoist.
        with m.transform(translate=(.51,.166,-.43),rotate_x=-11):
            m.slab("turquoise",0,0,0,.38,.05,.64,cut=.05,underside=True)
            for s in (-1,1):
                m.block("timber",s*.19,0,0,.049,.085,.65)
            if m.lod<2:
                for z in (-.19,-.02,.15):
                    m.block("gold",0,.048,z,.32,.036,.028)
        for z,hp in ((-.65,.146),(-.23,.235)):
            m.block("fieldstone",.51,.078,z,.24,hp-.078,.11)
    else:
        # A raised spoil bunker or mine tub, with its load protruding past the lip.
        w=.53 if kind=="CoalMine" else .39
        m.slab("metal" if m.era>=2 else "timber",.52,.078,-.45,w,.205,.44,1.13)
        boulder(m,role,.52,.278,-.45,w*.50,.15,.43)
    if m.lod==0:
        for i in range(3):
            boulder(m,role,-.47+i*.14,.48+(.09 if i==1 else .02),.01+i*.03,.087,.079,i*.53)
        m.jar(-.63,.078,-.39,.091,"turquoise")
        # A carved sun gauge on the adit and a triple measure on its jambs.
        m.disc(-.22,.473,-.357,.083)
        for s in (-1,1):
            for i in range(3):
                m.block("turquoise",-.22+s*.239,.17+i*.078,-.36,.056,.034,.047)
    if kind=="CoalMine":
        m.drum("brick" if m.era==2 else "glass",.65,.079,.45,.15,.67,.126)
        m.drum("metal",.65,.742,.45,.158,.088)


def quarry(m):
    if m.era==1:
        # A treadwheel crane on a broad masonry berth can drop blocks directly
        # on the low stone sled. The uncut face forms a separate back wall.
        works.court(m,role="soil")
        m.slab("fieldstone",-.08,.08,.53,1.53,.47,.38,.93)
        m.slab("wall",-.26,.55,.54,1.05,.21,.33,.95)
        for z in (-.32,.02):
            m.slab("wall",-.29,.08,z,.29,.46,.19,.65)
        m.rod("timber",(-.29,.52,-.40),(-.29,.52,.13),.055)
        works.wheel(m,-.29,.52,-.205,.337,"timber")
        m.rod("timber",(-.29,.52,-.03),(.59,1.05,-.18),.079)
        m.rod("trim",(-.48,.08,.08),(.38,.92,-.14),.039)
        m.rod("trim",(.59,1.05,-.18),(.59,.58,-.18),.021)
        for s in (-1,1):
            m.rod("trim",(.59,.58,-.18),(.59+s*.10,.40,-.18),.020,n=4)
        m.slab("fieldstone",.59,.28,-.18,.33,.175,.27,.91)
        m.slab("timber",-.08,.08,-.55,.64,.085,.33,.90)
        m.slab("wall",-.08,.165,-.55,.44,.16,.27,.92)
        if m.lod==0:
            for side in (-1,1):
                m.rod("timber",(-.46,.08,side*.16-.14),(-.29,.52,side*.16-.14),.037)
                m.block("gold",.59+side*.10,.272,-.18,.041,.012,.241)
                for j in (-.051,.051):
                    m.block("trim",.59+side*.10,.284,-.18+j,.032,.165,.031)
            m.disc(-.29,.52,-.405,.091)
        return
    if m.era>=2:
        works.footing(m,1.87,1.77,"soil")
        if m.era==2:
            # An engine drives a large circular quarry saw into a long stone bed.
            works.barrel(m,.44,.36,.72,.76,.38,.23)
            works.spire(m,.68,.58,.094,.82)
            for x in (-.69,-.25):
                m.slab("brick",x,.08,.07,.19,.51,.53,.90)
            m.rod("metal",(-.73,.57,.07),(-.14,.57,.07),.075)
            with m.transform(translate=(-.46,.57,.07),rotate_y=90):
                m.disc(0,0,0,.41,"metal",False)
                m.disc(0,0,-.105,.15,"gold",False)
            for x,z,w,d in ((-.48,-.42,.62,.42),(.33,-.44,.66,.30)):
                m.slab("fieldstone",x,.08,z,w,.29,d,.945,cut=.07)
        else:
            # A travelling cutter on twin rails straddles the excavation. Its
            # suspended head scores channels across the full width of each bench.
            for x in (-.64,.61):
                m.slab("fieldstone",x,.08,.05,.30,.23,1.48,.91)
                if m.lod<2:
                    m.block("metal",x,.31,.04,.11,.075,1.49)
                bearing=.385 if m.lod<2 else .31
                m.slab("ivory",x,bearing,.16,.24,1.105-bearing,.36,.67)
            m.slab("turquoise",-.015,1.105,.16,1.49,.17,.35,.89,underside=True)
            m.drum("ivory",-.18,.54,.16,.12,.59,.17)
            m.drum("metal",-.18,.32,.16,.035,.22,.113)
            for j in range(3):
                m.slab("fieldstone",-.35+j*.32,.08,-.27,.26,.19+j*.045,
                       .81-.11*j,.93,cut=.09)
            if m.lod<2:
                m.disc(.61,.89,-.029,.082)
        return
    m.slab("soil",0,0,.02,1.87,.07,1.76,.98)
    for j in range(3):
        m.slab("plaster" if m.era==0 else "fieldstone",-.22+j*.043,.07+j*.205,.05+j*.20,
               1.23-j*.19,.205,1.08-j*.21,.945,cut=.09)
    # A tripod derrick on the working bench passes stones down to the loading floor.
    frame="timber" if m.era<2 else "metal"
    h=(.95,1.12,1.31,1.45)[m.era]
    for x,z in ((.28,.48),(.75,.45),(.53,-.01)):
        m.rod(frame,(x,.071,z),(.54,h,.25),.051,6 if m.lod==0 else 4)
    m.rod(frame,(.54,h-.13,.25),(.40,h+.20,-.33),.064)
    m.rod("trim",(.40,h+.20,-.33),(.40,.735,-.33),.021)
    m.slab("fieldstone",.40,.390,-.33,.32,.21,.27)
    m.block("metal",.40,.360,-.33,.378,.035,.078)
    for s in (-1,1):
        x=.40+s*.18
        m.rod("gold",(x,.38,-.33),(x,.607,-.33),.017,4)
        m.rod("gold",(x,.607,-.33),(.40,.735,-.33),.018,4)
    m.disc(.54,h+.004,.21,.080)
    for i,(x,z) in enumerate(((-.64,-.56),(-.21,-.63),(.59,-.65))):
        if m.lod==2 and i==1:
            continue
        m.slab("fieldstone",x,.07,z,.32,.160+i*.02,.25,.97,cut=.06)
        if m.lod==0:
            m.block("turquoise",x,.14,z-.128,.19,.033,.012)
    if m.era>=2:
        m.slab("metal",.64,.07,.57,.46,.23,.42,.91)
        m.drum("glass",.64,.30,.57,.13,.09)
    if m.lod<2:
        for i in range(3):
            m.rod("trim",(-.62+i*.17,.46,.23),(-.59+i*.17,.54,.27),.026)
        if m.lod==0:
            m.jar(-.76,.07,-.29,.089,"thatch")


def log(m,x,y,z,length,r,angle=0):
    with m.transform(translate=(x,y,z),rotate_y=angle,rotate_x=-90):
        m.drum("timber",0,0,0,r,length,r*.88,n=(9,6,4)[m.lod],bottom=True)
        if m.lod<2:
            m.drum("thatch",0,length+.0002,0,r*.75,.010,n=(9,6)[m.lod])
        if m.lod==0:
            m.drum("trim",0,length+.0102,0,r*.28,.008,n=7)


def timberworks(m,sawmill):
    if m.era and (not sawmill or m.era==3):
        works.footing(m,role="soil")
        if sawmill:
            # The scanning saw is an open portal straddling the feed conveyor;
            # the glazed roof encloses the saw drive and dust separators.
            works.conveyor(m,.12,-.06,.63,1.43,.34)
            works.arch(m,.12,-.09,1.22,1.17,.17,.27)
            m.block("metal",.12,.72,-.09,.075,.33,.092)
            for x in (-.58,.70):
                works.vessel(m,x,.47,.18,.63,role="turquoise")
            log(m,.12,.51,.39,.87,.10)
            if m.lod<2:
                m.disc(.12,1.14,-.232,.09)
        elif m.era==1:
            # A radial log boom and open marked sorting court replace a fixed woodpile.
            works.pagoda(m,-.48,.45,.70,.63,.62)
            m.drum("wall",.24,.08,.04,.20,.28,.14)
            m.rod("timber",(.24,.36,.04),(.24,1.17,.04),.073)
            m.rod("timber",(-.19,.72,-.32),(.56,1.18,.30),.069)
            m.rod("trim",(-.19,.45,-.32),(-.19,.72,-.32),.026)
            for x,z,angle in ((-.37,-.27,54),(.48,.19,-20)):
                log(m,x,.19,z,.63,.113,angle)
            if m.lod==0:
                m.jar(.63,.08,-.57,.13,"turquoise")
        elif m.era==2:
            # Steam winch, winding drum and open iron logging arch straddle the haul road.
            works.barrel(m,-.46,.26,.70,.94,.34,.26)
            works.spire(m,-.57,.56,.10,1.00)
            works.arch(m,.36,-.06,.94,.88,.082,.12,"metal")
            m.drum("metal",.39,.08,.45,.18,.31)
            m.rod("metal",(.39,.36,.45),(.36,.85,-.06),.028)
            log(m,.36,.18,.30,.98,.109)
            if m.lod<2:
                log(m,-.23,.20,-.49,.47,.11,75)
        else:
            # The crawler's articulated harvesting head grips a horizontal bole.
            # Its pivot rests on a stepped service pod, with a small cut-stack at left.
            works.lens(m,.29,.31,.38,.33)
            m.drum("gold",.29,.78,.31,.091,.14)
            m.rod("ivory",(.29,.88,.31),(.30,1.33,-.06),.089)
            m.rod("turquoise",(.30,1.33,-.06),(.16,.66,-.46),.070)
            for x in (.06,.26):
                m.rod("metal",(x,.68,-.46),(x,.33,-.46),.039)
            log(m,.16,.408,-.15,.67,.116)
            for x,z in ((-.68,.50),(-.43,.44)):
                log(m,x,.19,z,.98,.117)
            if m.lod<2:
                log(m,-.55,.37,.43,.89,.112)
        return
    m.slab("soil",0,0,0,1.86,.068,1.79,.98)
    m.house(-.38,.068,.39,.91,.74,.76 if not sawmill else .91,role="metal" if m.era==0 and not sawmill else None)
    for i in range((3,3,2)[m.lod]):
        log(m,.24+i*.222,.177,.47,.99,.113,-5)
    log(m,.35,.357,.45,.96,.114,-5)
    if m.lod<2:
        log(m,.57,.356,.41,.93,.109,-5)
    if sawmill:
        m.slab("metal",-.36,.07,-.43,.78,.30,.48,.87)
        m.slab("timber",-.36,.37,-.43,.85,.057,.50,cut=.04)
        teeth=(12,9,6)[m.lod]
        rim=[]
        for i in range(teeth):
            for fraction,radius in ((0,.224),(.19,.294)):
                a=math.tau*(i+fraction)/teeth
                rim.append((-.36+radius*math.cos(a),.429+radius*math.sin(a)))
        for i,(ax,ay) in enumerate(rim):
            bx,by=rim[(i+1)%len(rim)]
            m.face("slate",((bx,by,-.430),(ax,ay,-.430),(-.36,.429,-.430)))
            m.face("slate",((ax,ay,-.387),(bx,by,-.387),(-.36,.429,-.387)))
            m.face("metal",((ax,ay,-.430),(bx,by,-.430),(bx,by,-.387),(ax,ay,-.387)))
        m.disc(-.36,.452,-.437,.078)
    else:
        m.drum("timber",-.42,.068,-.49,.224,.251,.178)
        m.drum("thatch",-.42,.317,-.49,.182,.027)
        for i in range((3,2,1)[m.lod]):
            m.blade("grass",(-.55,.33,-.50),(-.64+i*.09,.52+i*.036,-.45+i*.026),.047)
        if m.lod<2:
            m.jar(-.72,.068,-.22,.09,"turquoise")
    if m.era==1:
        m.rooflet(-.38,.95,.39,.41,.34,.16,"roof_tile")
    if m.era>=2:
        m.drum("brick" if m.era==2 else "glass",-.67,.55,.50,.084,.56,.072)
        m.drum("metal",-.67,1.10,.50,.085,.067)
    m.disc(-.38,.63 if not sawmill else .73,.046,.085)


def oil(m):
    if m.era==3:
        # An axial electric wellhead within a four-foot drilling mast. The crane
        # tip houses the travelling block, and the return line enters the separator.
        works.footing(m,1.90,1.68)
        x,z=-.25,.12
        for dx,dz in ((-.42,-.38),(.42,-.38),(-.42,.38),(.42,.38)):
            m.rod("ivory",(x+dx,.08,z+dz),(x+dx*.15,1.52,z+dz*.15),.068)
        m.slab("turquoise",x,1.50,z,.34,.17,.34,.71)
        m.rod("metal",(x,.41,z),(x,1.51,z),.043)
        m.drum("metal",x,.08,z,.19,.27,.144)
        m.drum("gold",x,.35,z,.14,.075)
        works.vessel(m,.53,-.17,.285,.70)
        works.shell(m,.51,.50,.52,.35,.38)
        m.slab("glass",.51,.46,.50,.57,.11,.42,.68,underside=True)
        m.rod("turquoise",(x,.24,z),(.52,.24,-.17),.050)
        if m.lod<2:
            m.disc(x,1.58,z-.164,.093)
            if m.lod==0:
                for side in (-1,1):
                    m.rod("metal",(x+side*.38,.21,z-.33),(x-side*.21,.81,z-.21),.027)
        return
    m.slab("fieldstone",0,0,.01,1.88,.115,1.49,.96)
    m.slab("wall",-.23,.115,.19,.68,.37,.62)
    m.entry(-.23,.116,-.119,.27,.30)
    for x in (-.50,.04):
        m.rod("metal",(x,.47,.20),(-.23,1.18,.20),.061)
    m.rod("metal",(-.23,.47,.47),(-.23,1.18,.20),.060)
    with m.transform(translate=(-.23,1.15,.20),rotate_z=12):
        m.slab("turquoise",.06,0,0,1.19,.13,.16,.97,cut=.05)
        m.slab("trim",.59,-.15,0,.16,.34,.21,.80)
        m.drum("metal",-.44,-.07,0,.132,.25)
    m.disc(-.23,1.19,.099,.108)
    m.rod("metal",(.39,.40,.20),(.39,1.18,.20),.023)
    m.drum("metal",.39,.115,.20,.133,.29,.090)
    for x in ((.32,.75) if m.era==3 else (.65,)):
        m.drum("metal" if m.era==2 else "glass",x,.115,-.41,.181,.51)
        m.mound("ivory",x,.625,-.41,.182,.105)
        if m.lod==0:
            m.drum("turquoise",x,.46,-.41,.186,.045)
    if m.lod<2:
        m.rod("metal",(.39,.26,.20),(.57,.26,-.41),.041)
    if m.lod==0:
        m.jar(-.67,.115,-.35,.093,"turquoise")


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        kind=spec.building_type
        if kind in ("Mine","GoldMine","CoalMine"):
            mine(m,kind)
        elif kind=="StoneQuarry":
            quarry(m)
        elif kind=="OilWell":
            oil(m)
        else:
            timberworks(m,kind=="Sawmill")
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
