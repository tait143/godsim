"""Mine railheads, quarry derricks, fuel pumps and log yards. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def outcrop(m,x,z,w,d,h,ore):
    # Offset each successive contour towards the face of the cliff. The broken bedding
    # planes stay continuous through the bends, including the visible mineral stratum.
    n=(9,7,5)[m.lod]
    rings=[]
    for y,g,dx,dz in ((0,1,0,0),(.40,.90,.04,.035),(.48,.83,.07,.06),(1,.49,-.06,.17)):
        ring=[]
        for j in range(n):
            a=2*math.pi*j/n
            k=1+.062*math.sin(3*a+.8)
            ring.append((x+w*(g*k*math.cos(a)+dx),h*y*(1+.085*math.sin(2*a+.3)),
                         z+d*(g*k*math.sin(a)+dz)))
        rings.append(ring)
    for i,mat in enumerate(("fieldstone",ore,"slate")):
        for j in range(n):
            k=(j+1)%n
            a,b,c,e=rings[i][k],rings[i][j],rings[i+1][j],rings[i+1][k]
            m.face(mat,[a,b,c])
            m.face(mat,[a,c,e])
    # The summit is a shallow asymmetric fan, never a horizontal polygon through uneven vertices.
    for j in range(n):
        m.face("fieldstone",[rings[-1][(j+1)%n],rings[-1][j],(x-.07*w,h*1.13,z+.16*d)])


def headframe(m,x,z,y,span,h,mat):
    for sx in (-1,1):
        for sz in (-1,1):
            m.beam(mat,(x+sx*span*.61,.12,z+sz*span*.49),
                   (x+sx*span*.31,y+h,z+sz*span*.24),span*.125)
    m.block("trim",x,y+h-.057,z,span*.89,.108,span*.67)
    if m.era>=2:
        m.vault("roof",x,y+h+.047,z,span*1.15,.122,span*.85)
    if m.lod<2:
        for s in (-1,1):
            m.beam("accent",(x+s*span*.48,y+h*.32,z-span*.40),
                   (x-s*span*.36,y+h*.79,z-span*.29),span*.065)
        # Head sheave and suspended guide cable drop through the center of the frame.
        wheel_z=z-span*.33
        m.block("trim",x,y+h*.77,wheel_z+.053,span*.20,h*.24,.096)
        with m.transform(translate=(x,y+h*.84,wheel_z+.125),rotate_x=-90):
            m.drum("accent",0,0,0,span*.085,.20)
        if m.lod==0:
            m.wheel(x,y+h*.84,wheel_z,span*.22,"metal",4)
        else:
            with m.transform(translate=(x,y+h*.84,wheel_z+.025),rotate_x=-90):
                m.drum("metal",0,0,0,span*.22,.051,n=8)
        m.beam("trim",(x+span*.19,y+h*.83,z-span*.26),(x+span*.19,y+h*.32,z-span*.26),.018)
    if m.era==3:
        m.block("glass",x,y+h+.152,z,span*.52,.162,span*.44)


def mine(m):
    e=m.era
    if e==1:
        return treadwheel_mine(m)
    if e==2:
        return incline_mine(m)
    if e==3:
        return shaft_mine(m)
    mineral="metal"
    height=.71
    m.slab("soil",0,0,0,1.87,.075,1.82,.12)
    outcrop(m,-.02,.16,.83,.66,height,mineral)
    m.vault("wall",-.16,.066,-.305,.78,.56,.52)
    # A dark, unobstructed adit sits inside three load-bearing portal stones.
    aperture=[(-.425,.075),(.103,.075),(.103,.395),(.012,.477),
              (-.319,.477),(-.425,.395)]
    if m.lod==2:
        m.face("door",[(x,y,-.582) for x,y in reversed(aperture)])
    else:
        m.relief("door",aperture,-.582,.063)
    frame="timber" if e==0 else "trim"
    if m.lod<2:
        for x in (-.459,.138):
            m.block(frame,x,.073,-.581,.090,.390,.109)
        m.vault(frame,-.16,.459,-.582,.703,.098,.131)
    else:
        m.block(frame,-.16,.459,-.582,.68,.095,.127)
    if m.lod<2:
        m.block("accent",-.16,.476,-.652,.12,.070,.021)
    # A disused ore wagon is shunted onto the right siding, with the main gate kept clear.
    for x in (-.33,.02):
        m.block("metal" if e else "timber",x,.077,-.68,.048,.032,.42)
    if m.lod<2:
        for z in (-.85,-.70,-.55):
            m.block("timber",-.155,.074,z,.49,.016,.059)
    headframe(m,-.12,.16,height*.69,.76,(.72,.86,1.04,.88)[e],"timber" if e<2 else "metal")
    x,z=.54,-.48
    for s in (-1,1):
        m.block("trim",x+s*.186,.078,z,.057,.031,.767)
    if m.lod<2:
        for t in (-1,0,1):
            m.block("timber",x,.074,z+t*.284,.49,.012,.066)
    m.block("trim",x,.151,z,.35,.078,.43)
    m.slab("metal" if e>=2 else "timber",x,.221,z,.405,.187,.46,.07)
    for j,(dx,dz,r) in enumerate(((-.085,-.07,.102),(.06,.05,.127),(.086,-.09,.073))):
        if j==2 and m.lod==2: continue
        m.dome((x+dx,.404,z+dz),r,m.color(mineral),rings=1,segments=(7,5,4)[m.lod],squash=.92,material=mineral)
    if m.lod<2:
        for s in (-1,1):
            for t in (-1,1):
                with m.transform(translate=(x+s*.153,.182,z+t*.135),rotate_z=-s*90):
                    m.drum("metal",0,0,0,.077,.069,n=10 if m.lod==0 else 6)
    else:
        for s in (-1,1):
            m.block("metal",x+s*.186,.106,z,.059,.066,.353)
    if m.lod==0:
        m.lamp(-.57,.074,-.48,.128)


def ore_heap(m,x,y,z,scale):
    stones=(((0,0,.191),) if m.lod==2 else
            ((-.114,-.051,.13),(.037,.074,.147),(.129,-.072,.087)))
    for dx,dz,r in stones:
        m.dome((x+dx*scale,y,z+dz*scale),r*scale,m.color("metal"),
               rings=1,segments=(7,5,4)[m.lod],squash=.91,material="metal")


def treadwheel_mine(m):
    # Hand-powered winch on a masonry bearing wall beside the rock-cut entrance.
    m.slab("soil",0,0,.021,1.90,.089,1.73,.17)
    outcrop(m,.325,.38,.543,.404,.605,"metal")
    m.vault("fieldstone",.316,.084,-.031,.897,.619,.681)
    m.entry(.315,.092,-.374,.469,.491)
    for z in (-.275,.197):
        m.slab("fieldstone",-.446,.082,z,.277,.563,.166,.125,.185)
    m.wheel(-.448,.642,-.023,.447,"timber",4 if m.lod==2 else 6)
    with m.transform(translate=(-.448,.644,.251),rotate_x=-90):
        m.drum("trim",0,0,0,.079,.609)
    if m.lod<2:
        for j in range((10,5)[m.lod]):
            a=math.tau*j/(10,5)[m.lod]
            with m.transform(translate=(-.448+.414*math.cos(a),.642+.414*math.sin(a),-.023),
                             rotate_z=math.degrees(a)):
                m.block("timber",0,-.025,0,.099,.052,.247)
    m.slab("timber",-.401,.083,-.568,.519,.134,.319,.066)
    ore_heap(m,-.403,.208,-.568,.98)
    if m.lod==0:
        for x in (.163,.457):
            m.block("timber",x,.09,-.571,.053,.034,.402)
        m.lamp(.689,.088,-.445,.111)
        # Rope drum on the back of the axle. Its cheeks keep the coils aligned.
        with m.transform(translate=(-.448,.644,.293),rotate_x=90):
            m.drum("timber",0,0,0,.124,.208)
            for y in (.015,.154):
                m.drum("trim",0,y,0,.143,.035)
        m.beam("trim",(-.331,.65,.383),(-.331,.24,.381),.022)


def incline_mine(m):
    # An inclined skipway runs from a low buttressed adit to a high ore tipple.
    m.slab("fieldstone",0,0,.012,1.88,.107,1.73,.125)
    outcrop(m,-.45,.361,.392,.391,.473,"metal")
    m.vault("brick",-.423,.099,-.048,.713,.502,.591)
    m.entry(-.423,.111,-.346,.382,.398)
    for x in (.317,.718):
        for z in ((.119,.544) if m.lod<2 else (.319,)):
            m.block("metal",x,.102,z,.098,.715,.098)
    m.slab("metal",.517,.711,.332,.457,.328,.453,.085,-.35)
    m.slab("brick",.517,1.031,.332,.625,.288,.619,.08)
    m.vault("metal",.517,1.313,.332,.678,.158,.684)
    m.beam("trim",(-.508,.199,-.531),(.481,1.087,.244),.194,.258)
    m.beam("metal",(-.183,.104,-.266),(-.183,.507,-.266),.086)
    if m.lod<2:
        for s in (-1,1):
            m.beam("metal",(-.499+s*.064,.227,-.531-s*.078),
                   (.465+s*.064,1.099,.243-s*.078),.034)
        for x in (.317,.718):
            m.beam("metal",(x,.172,.119),(x,.692,.546),.052)
    m.slab("timber",.537,.103,-.323,.523,.24,.508,.098)
    ore_heap(m,.54,.34,-.322,1.04)
    m.beam("metal",(.534,.743,.259),(.534,.393,-.226),.151)
    if m.lod==0:
        m.block("glass",.517,1.119,.016,.316,.128,.037)
        m.lamp(-.726,.11,-.354,.104)
        with m.transform(translate=(-.018,.633,-.14),rotate_y=52,rotate_x=-29):
            m.slab("metal",0,0,0,.261,.109,.355,.08,-.17)
            ore_heap(m,0,.105,0,.67)
        with m.transform(translate=(.419,1.132,.247),rotate_z=90):
            m.drum("metal",0,0,0,.108,.187)
            m.drum("accent",0,.177,0,.129,.035)


def shaft_mine(m):
    # A sealed lift pod rises inside a C-shaped shaft collar; the ore transfer rail reaches its mouth.
    m.slab("fieldstone",0,0,.015,1.91,.127,1.72,.203)
    x,z=-.253,.264
    m.drum("trim",x,.122,z,.544,.208,.528,n=(14,10,6)[m.lod])
    m.drum("door",x,.323,z,.453,.028,n=(14,10,6)[m.lod])
    # Segment the collar into three solid buttresses so the mouth remains visible.
    for a in (-69,0,69):
        rad=math.radians(a)
        px=x+.423*math.sin(rad); pz=z+.423*math.cos(rad)
        with m.transform(translate=(px,.326,pz),rotate_y=a):
            m.slab("ivory",0,0,0,.351,.763 if m.lod==2 else .664,.201,.135,.13)
            if m.lod<2:
                m.slab("glass",0,.658,0,.304,.105,.18,.12)
    m.slab("ivory",x,1.077,z+.187,.918,.129,.423,.187)
    m.block("trim",x,.649,z+.066,.075,.436,.075)
    m.drum("metal",x,.431,z+.061,.185,.236,.199)
    m.drum("accent",x,.662,z+.061,.203,.084,.121)
    m.slab("wall",.533,.121,-.091,.549,.452,.789,.15)
    m.slab("glass",.533,.569,-.091,.531,.115,.747,.14)
    m.vault("ivory",.533,.679,-.091,.594,.137,.844)
    m.entry(.532,.129,-.488,.306,.341)
    m.slab("metal",-.26,.119,-.416,.43,.115,.601,.095)
    ore_heap(m,-.262,.23,-.511,1.03)
    if m.lod==0:
        for x in (-.396,-.118):
            m.block("gold",x,.234,-.326,.034,.022,.289)
        m.rune(.531,.746,-.517,.149)
        with m.transform(translate=(-.253,1.247,.503),rotate_x=-90):
            m.drum("metal",0,0,0,.114,.26)
            m.drum("gold",0,.241,0,.124,.042)
        for px in (-.373,-.133):
            m.beam("trim",(px,1.19,.426),(px,1.265,.416),.062)
        m.drum("trim",-.253,.489,.325,.201,.043)


def coal_chunk(m,r):
    # Angular fracture planes and a narrow slate-coloured glint distinguish the
    # dark cargo across every palette, including the gold Ascension architecture.
    count=(7,6,5)[m.lod]
    lower=[]
    shoulder=[]
    for j in range(count):
        a=j*2*math.pi/count
        variation=1+.13*math.cos(3*a+.4)
        lower.append((r*variation*math.cos(a),0,r*variation*math.sin(a)))
        shoulder.append((r*(.67*variation*math.cos(a)-.09),r*(.48+.13*math.sin(2*a+.3)),
                         r*(.71*variation*math.sin(a)+.09)))
    peak=(-.20*r,.91*r,.14*r)
    for j in range(count):
        k=(j+1)%count
        if m.lod:
            m.face("slate" if j==2 else "door",[lower[k],lower[j],peak])
            continue
        m.face("door",[lower[k],lower[j],shoulder[j]])
        m.face("door",[lower[k],shoulder[j],shoulder[k]])
        m.face("slate" if j==2 else "door",[shoulder[k],shoulder[j],peak])


def coal_bunker(m):
    future=m.era==3
    if future:
        return shaft_loading_bay(m)
    m.slab("slate",0,0,0,1.91,.09,1.74,.125)
    x,z=.36,.22
    # The receiving bunker flares above a low discharge throat. A masonry engine
    # room occupies the front of the plinth and has its own ground-level entrance.
    m.slab("wall" if future else "brick",x,.085,.08,.81,.55,.91,.13)
    m.vault("metal" if future else "slate",x,.452,-.249,.75,.18,.46)
    m.entry(x,.093,-.388,.44,.383,"metal" if future else "fieldstone")
    m.block("fieldstone",x,.089,-.505,.49,.045,.19)
    m.slab("metal",x,.595,z,.56,.38,.55,.14,-.80)
    m.slab("wall" if future else "roof_tile",x,.956,z,1.02,.20,.98,.11)
    m.slab("metal",x,1.145,z,1.07,.06,1.01,.12)
    # Chipped facets rise from inside the lip, visibly heaped above the bucket.
    for dx,dz,r,h in ((.04,.04,.345,.65),(-.23,.15,.174,.99),(.26,-.15,.188,.88),
                      (-.10,-.235,.16,.74)):
        if m.lod==2 and dz<0: continue
        with m.transform(translate=(x+dx,1.196,z+dz),rotate_y=dx*143,scale=(1,h,.85)):
            coal_chunk(m,r)
    if m.lod<2:
        for dx in (-.33,0,.33):
            m.block("trim",x+dx,.97,z-.492,.053,.18,.027)
        for dz in (-.26,.22):
            m.block("trim",x+.513,.97,z+dz,.029,.18,.056)
        m.rune(x,1.06,z-.502,.132)
    # A continuous inclined belt reaches over the lip. The short end dips into a
    # feeder box and the intermediate trestle carries the return run.
    bx,bz=-.76,-.56
    dx,dz=.98,.78
    rise=1.21
    run=math.hypot(dx,dz)
    length=math.hypot(run,rise)
    with m.transform(translate=(bx,.22,bz),rotate_y=math.degrees(math.atan2(dx,dz))):
        m.block("fieldstone",0,-.132,.13,.41,.129,.42)
        m.block("wall" if future else "brick",0,-.129,run*.49,.27,.66,.20,taper=.18)
        with m.transform(rotate_x=-math.degrees(math.atan2(rise,run))):
            m.block("roof",0,-.055,length*.5,.302,.103,length)
            for s in (-1,1):
                m.block("turquoise" if future else "accent",s*.16,-.04,length*.5,
                        .043,.115,length+.026)
            if m.lod<2:
                for i in range(1,7):
                    m.block("metal",0,.044,i*length/7,.288,.024,.043)
            if future:
                for i in ((1,3) if m.lod==2 else (1,2,3)):
                    m.vault("glass",0,.050,(i-.26)*length/3.5,.382,.113,.36)
            elif m.lod==0:
                for i in (1,3,5):
                    m.dome((.013,.052,i*length/7+.085),.072,m.color("door"),
                           rings=1,segments=6,squash=.65,material="door")
    if future:
        # Dust separator with a wide cyclone chamber and a sealed upper neck.
        m.drum("metal",-.19,.089,.58,.145,.75)
        m.drum("ivory",-.19,.827,.58,.102,.402,top=.244)
        m.drum("glass",-.19,1.218,.58,.238,.239)
        m.drum("metal",-.19,1.447,.58,.253,.103,top=.151)
        if m.lod<2:
            m.beam("metal",(-.19,1.49,.58),(.24,1.243,.43),.095)
    else:
        m.stack(-.21,.088,.57,.158,1.47,"brick")
    if m.lod==0:
        m.lamp(.693,.09,-.413,.104)
        for dx in (-.31,.31):
            m.block("fieldstone",x+dx,.112,-.37,.078,.37,.10,taper=.15)


def shaft_loading_bay(m):
    m.slab("slate",0,0,.014,1.89,.086,1.72,.16)
    # A horizontal transfer floor borders a lower lift cage, bounded by the guide rails.
    for x in (-.692,.047):
        m.slab("wall",x,.083,.181,.22,.279,1.02,.11)
        m.block("metal",x,.355,.279,.099,.596,.111)
    m.slab("trim",-.323,.081,.633,.573,.271,.151,.09)
    m.slab("metal",-.323,.092,.199,.49,.094,.628,.085)
    for x in (-.538,-.114):
        m.block("accent",x,.18,.239,.036,.336,.049)
    m.block("trim",-.326,.512,.239,.461,.066,.084)
    m.slab("ivory",-.322,.928,.278,.933,.157,.386,.14)
    with m.transform(translate=(-.517,1.088,.264),rotate_z=-90):
        m.drum("metal",0,0,0,.137,.383,n=(12,8,5)[m.lod])
    m.beam("trim",(-.334,.548,.237),(-.334,.951,.237),.025)
    m.slab("wall",.518,.084,.169,.657,.313,1.07,.11)
    m.vault("metal",.518,.393,.44,.706,.247,.574)
    m.slab("metal",.062,.080,-.515,1.49,.155,.346,.095)
    for z in (-.613,-.407):
        m.block("trim",.065,.228,z,1.381,.043,.037)
    # Low ore tubs pass across the shaft mouth and straight into the loading gallery.
    for x in ((-.269,.373) if m.lod<2 else (.034,)):
        m.slab("metal",x,.267,-.505,.481,.181,.302,.08,-.09)
        for dx,dz,r in ((-.068,.005,.128),(.096,-.011,.10)):
            with m.transform(translate=(x+dx,.442,-.505+dz)):
                coal_chunk(m,r)
        if m.lod==0:
            for dx in (-.143,.143):
                with m.transform(translate=(x+dx,.266,-.645),rotate_x=90):
                    m.drum("trim",0,0,0,.062,.284,n=8)
    if m.lod<2:
        m.rune(.521,.554,.144,.157)
        for x in (-.442,-.236):
            with m.transform(translate=(x,.185,.15)):
                coal_chunk(m,.119)
    if m.lod==0:
        m.lamp(.738,.397,-.183,.102)
        m.block("glass",.515,.224,-.370,.331,.101,.035)


def wash_terraces(m):
    # Each catch bed is level. Water falls through a cut in the front curb to
    # the next bed; raised gold riffles retain the heavy fraction of the ore.
    e=m.era
    material=("timber","fieldstone","brick","ivory")[e]
    for i in range(3):
        x,z=-.164,-.583+i*.403
        w=1.22-i*.103
        h=.245+i*.205
        m.slab(material,x,.080+i*.001,z,w,h-.080-i*.001,.428,.09)
        if m.lod==2:
            a=(w-.147)*.5
            m.face("water",[(x-a,h+.017,z+.19),(x+a,h+.017,z+.19),
                            (x+a,h+.017,z-.19),(x-a,h+.017,z-.19)])
        else:
            m.block("water",x,h-.005,z,w-.147,.022,.379)
        for s in (-1,1):
            m.block("metal" if e>=2 else "timber",x+s*(w*.5-.037),h-.012,z,
                    .065,.083,.424)
        # Only the central spillway drops down the masonry riser.
        if i and m.lod==2:
            m.face("water",[(x-.143,h-.189,z-.228),(x-.143,h+.008,z-.228),
                            (x+.143,h+.008,z-.228),(x+.143,h-.189,z-.228)])
        elif i:
            m.block("water",x,h-.197,z-.216,.285,.205,.024)
        for j in ((0,) if m.lod else (-1,1)):
            if m.lod==2:
                a=w*.285
                m.face("gold",[(x-a,h+.038,z+.014),(x+a,h+.038,z+.014),
                               (x+a,h+.038,z-.014),(x-a,h+.038,z-.014)])
            else:
                m.block("gold",x,h+.014,z+j*.082,w*.57,.024,.026)
        if m.lod==0:
            for j in range(3):
                r=.046+.009*((i+j)%3)
                m.dome((x-.30+j*.25,h+.015,z+.115*(1 if j%2 else -1)),r,m.color("gold"),
                       rings=1,segments=5,squash=.79,material="gold")
    # A dry path alongside the beds leads to the vault where the concentrate is weighed.
    m.slab("fieldstone",.596,.084,-.177,.479,.38,.63,.13)
    m.vault(("hide","roof","metal","glass")[e],.596,.451,-.175,.532,.172,.676)
    if m.lod==2:
        m.face("door",[(.451,.091,-.503),(.451,.314,-.503),(.516,.384,-.503),
                       (.676,.384,-.503),(.741,.314,-.503),(.741,.091,-.503)])
    else:
        m.entry(.596,.09,-.485,.353,.32)
    if m.lod<2:
        m.block("slate",.596,.083,-.60,.397,.046,.177)
    if m.lod==0:
        m.lamp(.852,.086,-.54,.082)


def gold_mine(m):
    e=m.era
    m.slab("fieldstone",0,0,0,1.89,.086,1.82,.12)
    wash_terraces(m)
    x,z=-.14,.594
    if e==0:
        # Rock-cut headwater cistern, with a squared stone brow and projecting
        # copper spout. Timber yokes clamp the hand-hewn reservoir together.
        m.slab("wall",x,.081,z,.94,.82,.49,.21,taper=.15)
        m.slab("fieldstone",x,.889,z,.95,.14,.55,.19)
        m.slab("water",x,1.018,z,.68,.032,.34,.17)
        m.vault("timber",x,.827,.357,.68,.16,.213)
        m.relief("accent",[(x-.13,.785),(x,.697),(x+.13,.785),(x+.17,.934),
                           (x-.17,.934)],.215,.155)
        if m.lod<2:
            for s in (-1,1):
                m.block("timber",x+s*.306,.19,.386,.099,.68,.092,taper=.14)
                m.drum("fieldstone",x+s*.354,.998,z,.112,.139,top=.083)
        if m.lod==0:
            m.rune(x,.849,.202,.18,"gold")
            m.lamp(.418,.088,.504,.13)
    elif e==1:
        # The aqueduct crosses two clear, shoulder-cut arches. Wide springers
        # and individual keystones carry its copper-lined header channel.
        for dx in (-.60,0,.60):
            m.slab("wall",x+dx,.083,z,.19,.89,.315,.09,taper=.07)
        for dx in (-.30,.30):
            for s in (-1,1):
                with m.transform(translate=(x+dx,0,0),scale=(s,1,1)):
                    m.relief("fieldstone",[(.067,1.067),(.23,.869),(.23,1.13),(.067,1.13)],
                             z-.162,.324)
            m.block("accent",x+dx,1.035,z,.147,.11,.339)
        m.slab("roof_tile",x,1.122,z,1.49,.134,.415,.105)
        m.block("water",x,1.251,z,1.32,.025,.284)
        if m.lod<2:
            for dx in (-.6,0,.6):
                m.slab("accent",x+dx,.801,z,.233,.069,.346,.10)
            m.rune(x,.974,z-.181,.15)
        if m.lod==0:
            m.lamp(.654,.085,.62,.127)
    elif e==2:
        # A rotating, hooped trommel screens the gravel. Its axle rests in two
        # splayed saddles spanning the full width of the upper washing bed.
        for dx in (-.49,.49):
            m.slab("brick",x+dx,.081,z,.26,.84,.381,.11,taper=.18)
        with m.transform(translate=(x-.584,.995,z),rotate_z=-86):
            m.drum("metal",0,0,0,.302,1.17,n=(12,8,6)[m.lod])
            for t in ((.042,1.047) if m.lod==2 else (.042,.385,.72,1.047)):
                m.drum("accent",0,t,0,.321,.074,n=(12,8,6)[m.lod])
            m.drum("trim",0,1.16,0,.129,.117)
        if m.lod<2:
            m.wheel(x+.38,.753,z-.225,.154,"roof",4)
        if m.lod==0:
            m.lamp(.633,.086,.496,.13)
    else:
        # A pair of angled mineral scanners bracket the sorting chamber. The
        # upper separation prism and its gold contact plates sit in an open jaw.
        m.slab("metal",x,.083,z,.99,.70,.465,.17,taper=.14)
        m.slab("glass",x,.774,z,1.06,.145,.52,.17)
        for s in (-1,1):
            with m.transform(translate=(x,0,z),scale=(s,1,1)):
                m.relief("ivory",[(.296,.887),(.493,.899),(.608,1.19),(.434,1.199)],
                         -.178,.356,open_edges=(2,))
                m.relief("ivory",[(.434,1.199),(.608,1.19),(.482,1.445),(.316,1.484)],
                         -.178,.356,open_edges=(0,))
                m.relief("gold",[(.323,1.442),(.476,1.42),(.407,1.546),(.28,1.564)],
                         -.187,.373)
        m.drum("metal",x,.910,z,.17,.148,top=.116)
        m.drum("gold",x,1.049,z,.108,.161,top=.25,n=(8,7,5)[m.lod])
        m.drum("turquoise",x,1.201,z,.245,.26,top=.041,n=(8,7,5)[m.lod])
        if m.lod<2:
            m.rune(x,.837,z-.271,.145,"ivory")
        if m.lod==0:
            m.lamp(.603,.088,.548,.13)


def quarry(m):
    e=m.era
    if e == 1:
        return treadwheel_quarry(m)
    if e == 2:
        return wire_quarry(m)
    if e == 3:
        return bridge_quarry(m)
    m.slab("fieldstone",0,0,.012,1.86,.107,1.77,.14)
    # Successive quarry benches expose broad, pale saw cuts and stepped shade lines.
    for i in range(3):
        face_material="wall" if i!=1 else "fieldstone"
        m.slab(face_material,-.26,.099+i*.19,.14+i*.174,
               1.17-i*.093,.20,.96-i*.20,.09)
        if m.lod==0:
            for j in range(3):
                m.block("trim",-.64+j*.32,.14+i*.19,-.342+i*.274,.023,.103,.013)
    m.vault("wall",-.30,.106,-.243,.56,.34,.24)
    m.entry(-.30,.107,-.366,.44,.29)
    x,z=.48,.11
    m.drum("wall",x,.10,z,.26,.27,top=.22)
    h=.95
    m.block("timber",x,.357,z,.14,h-.357,.16)
    m.beam("timber",(x+.15,h-.06,z),(x-.55,h+.186,z-.19),.084)
    m.beam("trim",(x,.56,z),(x-.41,h+.13,z-.15),.059)
    m.beam("trim",(x-.51,h+.158,z-.18),(x-.51,.703,z-.18),.022)
    m.block("fieldstone",x-.51-.004,.554,z-.18,.268,.158,.227)
    if m.lod<2:
        m.wheel(x,.570,z-.085,.164,"accent",4)
        m.block("gold",x-.51,.565,z-.18,.073,.152,.238)
    if m.lod==0:
        for i in range(3):
            m.slab("fieldstone",.128+i*.283,.104,-.63,.261,.182,.234,.09)
        m.lamp(.70,.106,-.54,.105)


def treadwheel_quarry(m):
    m.slab("fieldstone",0,0,-.01,1.88,.125,1.64,.12)
    # Ashlar benches below the treadwheel leave a haul lane for the quarry sled.
    for j in range(2):
        m.slab("plaster",-.38,.12+j*.245,.23+j*.13,.90-j*.09,.25,.86-j*.32,.065)
    m.block("timber",-.45,.606,.35,.61,.127,.24)
    for z in (.20,.49):
        m.beam("timber",(-.73,.69,z),(-.42,1.14,z),.089)
        m.beam("timber",(-.10,.69,z),(-.42,1.14,z),.09)
    quarry_wheel(m,-.423,.975,.093,.333,"timber",5)
    m.beam("timber",(-.51,1.15,.343),(.53,1.33,.278),.11)
    m.beam("trim",(-.06,.70,.34),(.44,1.295,.287),.074)
    m.beam("trim",(.482,1.30,.28),(.482,.55,.28),.026)
    m.slab("plaster",.48,.391,.277,.40,.17,.29,.035)
    if m.lod<2:
        for x in (.42,.63):
            m.block("timber",x,.123,-.43,.062,.095,.58)
        m.slab("fieldstone",.525,.213,-.43,.45,.245,.47,.053)
    if m.lod==0:
        with m.transform(translate=(-.423,.975,.083),rotate_x=90):
            m.drum("trim",0,0,0,.075,.46,n=10)
        for q in range(8):
            ang=q*math.pi/4
            m.beam("timber",(-.423+.28*math.cos(ang),.975+.28*math.sin(ang),.073),
                    (-.423+.28*math.cos(ang),.975+.28*math.sin(ang),.252),.042)
        for x in (-.65,-.46,-.27,-.08):
            m.block("slate",x,.287,-.203,.014,.061,.018)
        m.rune(.477,.47,.126,.10)


def quarry_wheel(m,x,y,z,r,mat,spokes):
    if m.lod==2:
        with m.transform(translate=(x,y,z-r*.12),rotate_x=90):
            m.drum(mat,0,0,0,r,r*.24,n=7)
    elif m.lod==1:
        # Mitred rim sections share edges. Spokes end in the hub at the centre.
        for j in range(8):
            a,b=j*math.pi/4,(j+1)*math.pi/4
            outline=[(x+k*r*math.cos(t),y+k*r*math.sin(t))
                     for t,k in ((a,.77),(a,1),(b,1),(b,.77))]
            m.relief(mat,outline,z-r*.11,r*.22,open_edges=(0,2))
        for j in range(3):
            a=j*2*math.pi/3
            m.beam("trim",(x+r*.15*math.cos(a),y+r*.15*math.sin(a),z),
                   (x+r*.83*math.cos(a),y+r*.83*math.sin(a),z),r*.115)
        with m.transform(translate=(x,y,z-r*.15),rotate_x=90):
            m.drum("accent",0,0,0,r*.18,r*.31,n=6)
    else:
        m.wheel(x,y,z,r,mat,spokes)


def wire_quarry(m):
    m.slab("fieldstone",0,0,0,1.93,.123,1.76,.09)
    # A cut face spans the rear. Two wire sheaves on a floor rail pull the cutting loop.
    m.slab("wall",-.12,.119,.41,1.50,.57,.73,.07,.045)
    m.slab("slate",-.49,.68,.51,.77,.208,.51,.08)
    for x in (-.68,.39):
        m.block("metal",x,.122,-.264,.095,.073,.77)
    m.block("brick",-.14,.19,-.32,1.29,.252,.33)
    for x in (-.59,.36):
        quarry_wheel(m,x,.477,-.503,.201,"accent",4)
    for y in (.294,.662):
        m.beam("metal",(-.59,y,-.512),(.36,y,-.512),.029)
    m.stack(.65,.12,.45,.134,.86,"brick")
    if m.lod<2:
        for x in (-.62,-.35,.01,.36):
            m.block("slate",x,.261,.047,.021,.39,.020)
    if m.lod==0:
        m.slab("metal",.607,.121,-.26,.360,.346,.37)
        m.vault("metal",.607,.462,-.26,.37,.159,.405)
        m.rune(.607,.344,-.452,.126)


def bridge_quarry(m):
    m.slab("slate",0,0,0,1.87,.146,1.78,.18)
    # Twin bedrock shoulders carry a travelling bridge across a visible open cut.
    for s in (-1,1):
        x=s*.593
        m.slab("fieldstone",x,.14,.09,.60,.37,1.28,.105)
        m.slab("wall",s*.676,.505,.17,.38,.23,.95,.08)
        if m.lod==0:
            m.slab("metal",s*.69,.731,.17,.19,.069,1.07,.11)
        else:
            m.block("metal",s*.69,.731,.17,.19,.069,1.07)
        m.slab("ivory",s*.69,.793,.095,.25,.31,.37,.13,.22)
    m.slab("metal",0,1.098,.09,1.61,.18,.34,.14)
    m.slab("accent",.056,1.227,.091,.434,.14,.397,.11)
    m.drum("metal",.056,.63,.09,.123,.49,.102)
    m.drum("turquoise",.056,.302,.09,.056,.34,.085)
    if m.lod<2:
        for z in ((-.48,-.19,.13) if m.lod==0 else (-.33,.13)):
            m.slab("fieldstone",-.13,.143,z,.40,.137,.25,.06)
    if m.lod==0:
        m.rune(.055,1.176,-.084,.16)
        for x in (-.69,.69):
            for z in (-.256,.438):
                with m.transform(translate=(x,.828,z),rotate_z=-90):
                    m.drum("metal",0,-.105,0,.079,.207,n=10)
            m.block("gold",x,.651,-.304,.15,.058,.017)
        for x in (-.615,.615):
            m.block("ascension",x,.338,-.558,.304,.055,.017)


def log(m,x,y,z,r,length):
    with m.transform(translate=(x,y,z-length*.5),rotate_x=90):
        m.drum("timber",0,0,0,r,length)
        sides=(12,8,5)[m.lod]
        m.face("hide",[(r*math.cos(i*2*math.pi/sides),0,r*math.sin(i*2*math.pi/sides)) for i in range(sides)])
        if m.lod<2:
            for rad,t,mat in ((r*.57,-.003,"timber"),(r*.28,-.006,"hide"))[:2 if m.lod==0 else 1]:
                n=(12,8)[m.lod]
                m.face(mat,[(rad*math.cos(i*2*math.pi/n),t,rad*math.sin(i*2*math.pi/n)) for i in range(n)])


def timber_yard(m,sawmill):
    e=m.era
    if sawmill and e == 3:
        return bandsaw_mill(m)
    if not sawmill and e > 0:
        return (timber_lodge, steam_yarder, grapple_yard)[e-1](m)
    m.slab("fieldstone",0,0,0,1.82,.094,1.67,.11)
    m.hearth(-.26,.091,.30,(1.05,1.12,.95),roof_material="roof" if e==0 else None)
    for x,y,z in ((.48,.212,.22),(.712,.212,.254),(.596,.416,.285)):
        log(m,x,y,z,.119,1.03)
    if sawmill:
        m.block("wall",-.07,.093,-.47,.87,.23,.31)
        m.block("trim",-.07,.321,-.47,1.08,.068,.37)
        # A vertical gang blade breaks the table surface and the roof line.
        teeth=(15,10,7)[m.lod]
        outline=[]
        for j in range(teeth):
            for da,r in ((0,.264),(.23,.341)):
                a=2*math.pi*(j+da)/teeth
                outline.append((-.15+r*math.cos(a),.462+r*math.sin(a)))
        for i,point in enumerate(outline):
            other=outline[(i+1)%len(outline)]
            m.relief("metal",[(-.15,.462),point,other],-.645,.044,open_edges=(0,2))
        with m.transform(translate=(-.15,.465,-.636),rotate_x=-90):
            m.drum("accent",0,0,0,.058,.034)
        log(m,.32,.447,-.47,.084,.36)
    else:
        m.drum("timber",-.38,.092,-.54,.218,.219,top=.20)
        m.drum("hide",-.38,.308,-.54,.179,.010)
        # The wedge and its red-brown handle remain visibly embedded in the block.
        m.relief("metal",[(-.44,.31),(-.307,.37),(-.28,.517),(-.385,.49)],-.57,.099)
        m.beam("trim",(-.34,.46,-.52),(-.02,.684,-.49),.046)
    if m.lod==0:
        m.lamp(-.72,.095,-.20,.11)
        for i in range(3):
            with m.transform(translate=(-.04+i*.13,.099,-.66+.019*i),rotate_y=i*16):
                m.block("hide",0,0,0,.069,.029,.11)


def timber_lodge(m):
    m.slab("fieldstone",0,0,0,1.85,.086,1.69,.13)
    # An open-sided log store with cruck frames and a single sweeping slate roof.
    for z in (-.12,.56):
        for x in (-.71,.37):
            m.block("fieldstone",x,.082,z,.206,.16,.22)
            m.beam("timber",(x,.235,z),(-.17,.99,z),.107 if x<0 else .145,.121)
    m.hip_roof((-.17,.724,.229),1.30,.98,.39,m.color("slate"),overhang=.05,material="slate")
    for x,y,z in ((-.47,.228,.19),(-.16,.228,.216),(-.32,.459,.20))[:2 if m.lod==2 else 3]:
        log(m,x,y,z,.143,.85)
    # A two-handled broad axe stands in a separate chopping block.
    m.drum("timber",.548,.083,-.408,.203,.29,.181)
    m.relief("metal",[(.427,.356),(.57,.408),(.61,.581),(.464,.547)],-.48,.113)
    m.beam("timber",(.529,.511,-.43),(.762,.821,-.39),.055)
    if m.lod==0:
        for z in (-.12,.56):
            m.beam("trim",(-.62,.368,z),(.273,.368,z),.073)
        for x in (-.49,-.27,-.05,.17):
            m.block("slate",x,.739,-.279,.147,.060,.119)
        m.drum("trim",.548,.228,-.408,.193,.037)
        m.drum("timber",.596,.085,.32,.161,.177,.149,n=10)
        m.drum("hide",.596,.259,.32,.142,.012,n=10)
        for x in (-.52,-.27,.02):
            m.block("timber",x,.087,-.54,.158,.093,.23,taper=.09)


def steam_yarder(m):
    m.slab("soil",0,0,.02,1.88,.098,1.74,.16)
    m.slab("wall",-.27,.092,.16,.93,.214,1.14,.13)
    with m.transform(translate=(-.36,.519,-.09),rotate_x=90):
        m.drum("metal",0,0,0,.244,.69,n=(12,8,5)[m.lod])
    m.stack(-.36,.677,.38,.102,.402,"metal")
    # Steam donkey and a raking spar pull felled trunks up the skid road.
    for x in (.05,.54):
        m.beam("timber",(x,.104,.41),(.39,1.34,-.072),.115)
    m.beam("trim",(-.50,.31,-.095),(.39,1.34,-.072),.033)
    m.beam("trim",(.394,1.31,-.085),(.53,.32,-.404),.026)
    log(m,.50,.246,-.33,.147,.91)
    if m.lod<2:
        m.wheel(-.36,.519,-.137,.195,"accent",5)
        log(m,.75,.201,.19,.101,.77)
    if m.lod==0:
        m.slab("timber",.52,.321,-.44,.32,.072,.082)
        m.lamp(-.669,.308,-.212,.115)


def grapple_yard(m):
    m.slab("fieldstone",0,0,.01,1.82,.133,1.74,.19)
    m.slab("wall",-.42,.128,.283,.753,.341,.84,.18)
    m.slab("glass",-.46,.463,.374,.65,.275,.566,.17,.08)
    m.vault("ivory",-.46,.733,.374,.70,.143,.61)
    # An articulated grapple gathers whole trees across a bunk, accessible from either end.
    for z in (-.46,.36):
        m.block("metal",.38,.130,z,.83,.13,.113)
        for x in (.035,.74):
            m.beam("accent",(x,.24,z),(x+(.085 if x>.4 else -.085),.491,z),.069)
    for x,y,z in ((.22,.389,-.108),(.53,.389,-.044),(.38,.604,-.076))[:2 if m.lod==2 else 3]:
        log(m,x,y,z,.14,1.28)
    m.drum("metal",-.36,.462,-.052,.15,.136)
    m.beam("ivory",(-.36,.559,-.052),(-.071,1.13,-.12),.14)
    m.beam("metal",(-.071,1.13,-.12),(.382,.899,-.186),.112)
    for s in (-1,1):
        z=-.186+s*.013
        m.beam("accent",(.382,.893,z),(.382+s*.18,.699,z),.072)
        if m.lod<2:
            m.beam("metal",(.382+s*.18,.70,z),(.382+s*.12,.568,z),.057)
    if m.lod==0:
        m.rune(-.45,.594,.078,.161)
        for x,y in ((-.071,1.13),(.382,.899)):
            with m.transform(translate=(x,y,-.239),rotate_x=90):
                m.drum("metal",0,0,0,.087,.162,n=10)
        m.drum("gold",-.36,.515,-.052,.158,.039)
        m.block("glass",-.414,.293,-.147,.357,.112,.025)
        for x,y,z in ((-.27,.68,-.085),(-.009,1.01,-.165)):
            with m.transform(translate=(x,y,z),rotate_z=-48):
                m.drum("trim",0,0,0,.043,.209,n=9)
                m.drum("metal",0,.205,0,.029,.166,n=8)


def bandsaw_mill(m):
    m.slab("fieldstone",0,0,0,1.89,.129,1.79,.13)
    # Long bed passes through the saw portal; squared boards leave on a side rack.
    m.slab("metal",-.28,.124,-.02,.91,.209,1.46,.11)
    log(m,-.28,.487,-.36,.156,.785)
    for x in (-.67,.126):
        m.slab("wall",x,.33,.126,.232,.72,.504,.14,.08)
    m.vault("ivory",-.27,1.044,.126,1.113,.229,.584)
    m.block("metal",-.264,.499,.110,.030,.553,.144)
    if m.lod<2:
        m.block("turquoise",-.281,.551,.025,.028,.442,.027)
    for z in (-.38,.39):
        m.slab("trim",.547,.126,z,.553,.143,.165)
    for i in range((4,3,2)[m.lod]):
        for j in range(2 if m.lod==0 else 1):
            m.block("timber",.536+(j-.5)*.225 if m.lod==0 else .536,
                    .262+i*.109,-.015,.209 if m.lod==0 else .421,.094,1.20-i*.067)
    if m.lod==0:
        m.slab("glass",-.38,1.217,.12,.54,.126,.346)
        m.rune(-.275,1.142,-.176,.18)
        for z in (-.55,-.34,.43,.62):
            with m.transform(translate=(-.63,.339,z),rotate_z=-90):
                m.drum("metal",0,0,0,.057,.7,n=9)


def rotary_well(m):
    m.slab("fieldstone",0,0,-.017,1.85,.148,1.70,.18)
    m.slab("wall",-.34,.143,-.035,.91,.278,1.12,.115)
    # A vertical direct-drive rig; the moving head runs inside a diamond-braced mast.
    for x in (-.59,-.12):
        m.beam("ivory",(x,.41,.06),(-.355+(x+.355)*.69,1.43,.18),.11)
    m.vault("metal",-.355,1.416,.176,.512,.19,.45)
    m.slab("accent",-.355,.918,.085,.398,.262,.387,.14)
    m.drum("metal",-.355,.415,.051,.058,.51)
    m.drum("trim",-.355,.413,.051,.176,.09,.141)
    for x,z in ((.40,-.32),(.51,.37)):
        m.drum("metal",x,.143,z,.269,.105,.245)
        m.drum("wall",x,.243,z,.238,.448)
        m.dome((x,.688,z),.25,m.color("ivory"),rings=(3,2,1)[m.lod],
               segments=(12,8,5)[m.lod],squash=.52,material="ivory")
    if m.lod<2:
        m.beam("trim",(-.594,.528,.164),(-.216,.918,.23),.032)
        m.beam("trim",(-.167,.572,.186),(-.514,.898,.22),.032)
        m.beam("accent",(.44,.39,-.16),(.50,.39,.213),.059)
    if m.lod==0:
        m.rune(-.355,1.039,-.113,.166)
        m.slab("metal",-.33,.145,-.673,.57,.102,.19)
        for z in (-.30,.34):
            m.drum("accent",.45 if z<0 else .51,.511,z,.246,.038)


def pump(m):
    e=m.era
    if e == 3:
        return rotary_well(m)
    m.slab("fieldstone",0,0,0,1.85,.13,1.66,.13)
    m.hearth(-.52,.127,.38,(.66,.85,.79))
    x,z=.09,-.18
    for s in (-1,1):
        for t in (-1,1):
            m.beam("metal",(x+s*.315,.13,z+t*.22),(x+s*.056,.99,z+t*.13),.087)
    m.block("trim",x,.934,z,.273,.138,.389)
    m.beam("accent",(x-.72,.831,z),(x+.59,1.158,z),.132,.198)
    # Crescent horsehead and polished rod on the working end, crank on the short end.
    m.relief("roof",[(.59,.942),(.71,.80),(.836,.914),(.849,1.19),(.685,1.275),(.579,1.142)],
             z-.123,.246)
    m.beam("metal",(.773,1.04,z),(.773,.239,z),.024)
    m.drum("trim",.773,.12,z,.099,.193,top=.077)
    if m.lod<2:
        m.wheel(-.55,.385,z,.234,"roof",6)
    else:
        with m.transform(translate=(-.55,.385,z-.046),rotate_x=90):
            m.drum("roof",0,0,0,.234,.091,n=8)
    m.beam("trim",(-.63,.835,z),(-.635,.468,z),.059)
    for s in (-1,1):
        m.block("wall",-.55,.124,z+s*.135,.18,.27,.10,taper=.17)
    if m.lod<2:
        m.drum("wall",.52,.127,.49,.246,.32)
        m.drum("metal",.52,.442,.49,.25,.087,top=.162)
    if m.lod==0:
        m.lamp(.32,.128,-.59,.101)
        m.beam("metal",(.77,.22,-.15),(.59,.21,.36),.047)


def build(spec,lod):
    m=Stonework(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        kind=spec.building_type
        if kind=="Mine": mine(m)
        elif kind=="CoalMine": coal_bunker(m)
        elif kind=="GoldMine": gold_mine(m)
        elif kind=="StoneQuarry": quarry(m)
        elif kind in ("WoodcutterCamp","Sawmill"): timber_yard(m,kind=="Sawmill")
        else: pump(m)
    return m


if __name__=="__main__":
    publish_package("dwarf_extraction",build)
