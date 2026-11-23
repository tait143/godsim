"""Delta fisheries, reed-rice gardens, clutch pastures and water-driven grain works."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

PACKAGE = "saurian_food"


def solar_badge(m,x,y,z,r):
    m.block("trim",x,y,z,.053,r*1.05,.058)
    m.disc(x,y+r*1.28,z,r)


def punt(m,x,z):
    # Hollow reed boat with a dished floor and upswept ends.
    outline=[(0,-.68),(.14,-.40),(.17,.27),(.105,.56),(0,.67),(-.105,.56),(-.17,.27),(-.14,-.40)]
    if m.lod==2:
        outline=outline[::2]
    def rail(scale_x,scale_z,y,upturn):
        return [(x+a*scale_x,y+abs(b)**2*upturn,z+b*scale_z) for a,b in outline]
    keel=rail(.54,.81,.035,.12)
    rim=rail(1,1,.166,.35)
    inside=rail(.74,.94,.166,.35)
    floor=rail(.49,.79,.071,.12)
    for i in range(len(rim)):
        j=(i+1)%len(rim)
        for role,points in (("timber",(keel[j],keel[i],rim[i],rim[j])),
                            ("gold",(rim[j],rim[i],inside[i],inside[j])),
                            ("hide",(inside[j],inside[i],floor[i],floor[j]))):
            m.face(role,points)
    m.face("timber",floor[::-1])
    if m.lod<2:
        for spot in (-.22,.17):
            m.block("trim",x,.147,z+spot,.260,.042,.069)
        if m.lod==0:
            m.rod("thatch",(x-.096,.20,z-.39),(x+.125,.25,z+.33),.019)


def fisher(m):
    if m.era:
        # A landing net, a roofed commercial slip and an offshore harvesting
        # station are three different methods of working the same tidal fishery.
        m.slab("water",0,0,0,1.90,.058,1.76,.97)
        if m.era==1:
            for x in (-.61,.09):
                for z in (-.17,.57):
                    m.drum("timber",x,.058,z,.071,.27,n=(8,5,4)[m.lod])
            m.slab("timber",-.26,.328,.20,1.02,.083,1.08,underside=True)
            works.pagoda(m,-.32,.34,.84,.76,.61,y=.411)
            # Four dog-leg poles gather the suspended square dip-net to a common hook.
            cx,cz=.49,-.32
            for x,z in ((.14,-.63),(.80,-.63),(.80,.00),(.14,.00)):
                m.rod("trim",(x,.32,z),(cx,1.03,cz),.027,n=(6,4,3)[m.lod])
            corners=[(.14,.32,-.63),(.80,.32,-.63),(.80,.32,0),(.14,.32,0)]
            for i,corner in enumerate(corners):
                m.face("hide",(corner,(cx,.163,cz),corners[(i+1)%4]))
            m.rod("timber",(-.03,.408,-.01),(.49,1.05,-.32),.060)
            if m.lod==0:
                for x in (-.61,.09):
                    m.drum("gold",x,.24,-.17,.076,.044,n=8)
        elif m.era==2:
            m.slab("fieldstone",-.40,.058,.08,.93,.195,1.56,.94)
            works.barrel(m,-.42,.41,.74,.79,.37,.32,y=.253)
            with m.transform(translate=(.48,0,-.16),scale=(1.4,1.15,1)):
                punt(m,0,0)
            for x in (-.26,.64):
                m.slab("metal",x,.058,.18,.11,.98,.13,.85)
            m.slab("metal",.19,1.038,.18,1.05,.11,.18,.95,underside=True)
            m.rod("trim",(.45,.54,.18),(.45,1.04,.18),.024)
            if m.lod<2:
                m.jar(-.54,.253,-.46,.129,"hide")
        else:
            # Circular tidal pen and pier-mounted boom feed the raised sorting pod.
            for x in (-.61,-.26):
                for z in (.09,.60):
                    m.slab("ivory",x,.058,z,.12,.38,.14,.74)
            m.slab("ivory",-.435,.438,.345,.80,.077,.87,underside=True)
            works.lens(m,-.435,.35,.33,.37,y=.515)
            with m.transform(translate=(.37,.258,-.24),rotate_x=90):
                m.ring("turquoise",0,0,0,.46,.378,.16,n=(18,10,6)[m.lod])
            for x,z in ((.02,-.48),(.73,-.48),(.36,.14)):
                m.drum("ivory",x,.058,z,.048,.24)
            m.rod("gold",(-.15,.67,.25),(.36,.82,-.24),.043)
            m.rod("metal",(.36,.23,-.24),(.36,.82,-.24),.025)
            if m.lod<2:
                m.mound("turquoise",.36,.19,-.24,.12,.10)
        return
    for x in (-.70,.18):
        for z in (-.52,.60):
            m.drum("timber",x,0,z,.075,.325,n=(8,6,4)[m.lod])
    m.slab("timber",-.25,.29,.03,1.24,.093,1.48,cut=.039,underside=True)
    m.house(-.25,.383,.26,1.02,.91,.77,role="metal" if m.era==0 else None)
    # The landing piles and the boat share the same waterline.
    m.slab("water",.48,0,-.025,.85,.058,1.65,.986,cut=.105)
    punt(m,.630,-.07)
    # Net spread on two poles by the landing; the lead line sags at the middle.
    for x in (-.79,-.38):
        m.rod("trim",(x,.38,-.50),(x,.79,-.50),.026)
    m.rod("trim",(-.82,.767,-.50),(-.35,.767,-.50),.020)
    net=[(-.38,.754,-.515),(-.79,.754,-.515),(-.74,.52,-.573),(-.54,.44,-.57),(-.42,.54,-.543)]
    m.face("hide",net[::-1])
    m.face("hide",[(x,y,z+.005) for x,y,z in net])
    if m.lod<2:
        m.disc(-.40,.773,-.50,.065)
        if m.lod==0:
            for x in (-.74,-.64,-.54):
                m.drum("ivory",x,.744,-.52,.027,.047,n=6)
    if m.era>=2:
        m.rod("metal",(.23,.38,.43),(.23,1.03,.43),.052)
        m.rod("metal",(.23,.99,.43),(.69,1.20,.31),.046)
        m.rod("trim",(.69,1.2,.31),(.69,.83,.31),.016)
    elif m.era==1:
        m.rooflet(-.25,1.22,.26,.40,.35,.18,"roof_tile")


def hut(m,hunting):
    if hunting and m.era==1:
        # The tracker can survey the reeds from an elevated, roofed blind.
        # Its skin-curing court sits at ground level beside the ladder.
        works.court(m,-.34,role="soil")
        for x in (-.61,-.07):
            for z in (-.03,.46):
                m.rod("timber",(x,.08,z),(x,.76,z),.062)
        m.slab("timber",-.34,.76,.215,.76,.09,.71,underside=True)
        works.shell(m,-.34,.22,.56,.49,.30,.85)
        m.rooflet(-.34,1.15,.22,.76,.70,.33,"roof")
        for x in (-.427,-.253):
            m.rod("trim",(x,.08,-.43),(x,.85,-.094),.024,n=4)
        for i in range((6,4,2)[m.lod]):
            u=(i+1)/((6,4,2)[m.lod]+1)
            m.block("timber",-.34,.08+.77*u,-.43+.336*u,.19,.028,.040)
        for z in (-.37,.37):
            m.rod("timber",(.23,.08,z),(.47,.77,z),.040)
            m.rod("timber",(.73,.08,z),(.47,.77,z),.040)
        m.rod("trim",(.47,.76,-.43),(.47,.76,.43),.045)
        if m.lod<2:
            for x in (.31,.63):
                m.rod("hide",(x,.35,-.33),(.47,.75,-.33),.035)
        with m.transform(translate=(.47,.73,0),rotate_z=22):
            m.box((0,-.205,0),(.046,.42,.45),m.color("hide"),material="hide",bottom=True)
        if m.lod<2:
            m.jar(.26,.08,-.54,.11,"turquoise")
        return
    m.slab("soil",0,0,.02,1.75,.07,1.63,.965)
    m.house(-.12,.07,.19,1.38,1.04,1.05,role="metal" if m.era==0 else None)
    solar_badge(m,-.12,1.23 if not m.era else 1.31,.19,.114)
    if hunting:
        # Drying rack carries stretched hides; forked posts project above its top rail.
        for x in (.06,.67):
            m.rod("timber",(x,.069,-.53),(x+.023,.74,-.54),.047)
        m.rod("trim",(.01,.657,-.535),(.73,.691,-.537),.038)
        leather=[(.64,.65,-.57),(.12,.63,-.57),(.16,.43,-.60),
                 (.25,.24,-.61),(.49,.31,-.608),(.61,.44,-.59)]
        m.face("hide",leather[::-1])
        m.face("hide",[(x,y,z+.006) for x,y,z in leather])
        if m.lod<2:
            m.jar(-.55,.07,-.48,.113)
            m.rod("ivory",(.63,.67,-.53),(.74,.82,-.49),.022)
        if m.era==1:
            m.rooflet(.49,.32,.32,.70,.61,.32,"slate")
            m.slab("wall",.49,.07,.32,.64,.25,.53)
    else:
        for x,z,r in ((.45,-.51,.19),(-.27,-.56,.143),(.68,.18,.149)):
            m.drum("thatch",x,.07,z,r*.69,r*1.32,r,n=(9,6,4)[m.lod])
            m.mound("leaf",x,.07+r*1.31,z,r*.95,r*.59)
            if m.lod==0:
                for a in (0,2.1,4.2):
                    m.blade("grass",(x,.07+r*1.44,z),(x+r*.91*math.cos(a),.07+r*2.25,z+r*.91*math.sin(a)),r*.26)


def rack_silo(m,x,z,large):
    r=.206 if large else .17
    h=.82 if large else .58
    m.drum("metal" if m.era>=2 else "timber",x,.059,z,r,h,n=(10,7,5)[m.lod])
    m.drum("roof",x,h+.059,z,r*1.13,r*.84,r*.35)
    if m.lod<2:
        for k in (.22,.68):
            m.drum("turquoise",x,.06+h*k,z,r*1.027,.037,n=(10,7)[m.lod])


def irrigated_delta(m):
    """Low contour beds follow the delta's waterline below a solar sluice house."""
    m.slab("water",0,.08,-.015,1.80,.033,1.69,.98,cut=.11)
    for row,(front,end) in enumerate(((-.65,.65),(-.29,.75),(.07,.70),(.43,.12))):
        start=-.79 if row < 3 else -.67
        depth=.117
        top=.21+row*.023
        # The planting strip follows a sweeping contour, with sloped shoulders
        # and a dark earth crown. The continuous water between berms is visible.
        sections=(7,5,3)[m.lod]
        def center(t):
            return start+(end-start)*t, front+.15*math.sin(math.pi*t)
        def course(t):
            x,z=center(t)
            # Bevel only the two ends of each bed so water can flow round them.
            breadth=depth*(.67 if t==0 or t==1 else 1)
            return [(x,.113,z-breadth),(x,top,z-breadth*.78),
                    (x,top,z+breadth*.78),(x,.113,z+breadth)]
        rings=[course(i/sections) for i in range(sections+1)]
        m.face("soil",rings[0][::-1])
        m.face("soil",rings[-1])
        for near,far in zip(rings,rings[1:]):
            for i,role in enumerate(("wall","soil","wall")):
                m.face(role,(near[i],near[i+1],far[i+1],far[i]))

        count=((6,4,3) if row<3 else (4,3,2))[m.lod]
        for j in range(count):
            x,z=center((j+.5)/count)
            # Paired folded leaves silhouette the crop at a distance. The seed
            # head leans towards open water, visible above the plant's crown.
            direction=1 if (row+j)%2 else -1
            crown=(x,top,z)
            for side in (-1,1):
                m.blade("leaf" if side==-1 else "grass",crown,
                        (x+side*.105,top+.122,z+side*.049),.060)
            if m.lod<2:
                m.blade("thatch" if row%2 else "gold",crown,
                        (x+.048*direction,top+.223,z-.036),.036)
            if m.lod==0:
                m.blade("leaf",crown,(x-.043,top+.149,z+.100),.050)
    # This small pump sits on dry ground at the head of the canal. Its gold
    # calendar faces the service court, with a horizontal solar collector above.
    x,z=.53,.58
    m.slab("fieldstone",x,.113,z,.59,.068,.45,.94)
    m.slab("ivory",x,.181,z,.42,.224,.31,.89)
    m.entry(x,.181,z-.155,.163,.176)
    with m.transform(translate=(x,.403,z),rotate_x=-11):
        m.slab("turquoise",0,0,0,.55,.048,.36,.94,cut=.11,underside=True)
        m.slab("glass",0,.048,0,.48,.015,.295,.96)
    m.rod("turquoise",(.72,.257,.54),(.837,.257,.36),.042)
    m.rod("ivory",(.837,.257,.36),(.837,.136,.31),.042)
    if m.lod<2:
        m.disc(x,.322,z-.169,.056,"gold",False)
        if m.lod==0:
            for i in (-1,0,1):
                m.block("turquoise",x+.206,.270,z+i*.064,.022,.080,.032)


def field(m,mechanical):
    if m.era and (not mechanical or m.era==3):
        works.footing(m,1.94,1.88,"soil")
        if mechanical:
            # Three walk-in hydroponic glasshouses share a circular feed reservoir.
            for x,z,depth in ((-.60,.09,1.35),(-.01,.04,1.54),(.58,.22,1.11)):
                m.slab("wall",x,.08,z,.52,.18,depth,.96)
                works.ridge(m,x,z,.54,depth,.65,y=.26)
                m.entry(x,.08,z-depth/2,.16,.158)
            works.vessel(m,.60,-.55,.214,.38,role="turquoise")
        elif m.era==1:
            # An upland gravity-fed terrace with paddies stepping back and up.
            for j in range(3):
                w=1.76-.17*j
                z=-.53+.45*j
                h=.145+.215*j
                m.slab("wall",-.015,.08,z,w,h,.43,.96)
                m.slab("water",-.015,.08+h,z,w*.93,.023,.348)
                for k in range((6,4,2)[m.lod]):
                    n=(6,4,2)[m.lod]; x=-.015+w*.81*((k+.5)/n-.5)
                    m.slab("leaf",x,.103+h,z,w*.64/n,.094,.224,.36,cut=.078)
            works.pagoda(m,.08,.57,.61,.49,.48,y=.655)
        elif m.era==2:
            # A steam irrigation wheel lifts the river into two long planting beds.
            works.barrel(m,-.47,.39,.74,.85,.39,.20)
            works.spire(m,-.70,.53,.098,1.02)
            m.slab("water",-.41,.08,-.45,.87,.064,.55)
            works.wheel(m,-.39,.49,-.34,.34)
            m.rod("metal",(-.39,.49,-.35),(-.39,.49,.21),.062)
            for x in (.24,.63):
                m.slab("soil",x,.08,-.03,.31,.12,1.45,.91)
                for k in range((7,4,2)[m.lod]):
                    n=(7,4,2)[m.lod];z=-.62+(k+.5)*1.18/n
                    m.slab("leaf",x,.20,z,.23,.16,1.03/n,.47)
        else:
            irrigated_delta(m)
        return
    m.slab("plaster" if m.era==0 else "soil",0,0,0,1.94,.06,1.87,.99,cut=.045)
    m.house(-.43,.06,.54,.84,.55,.59,role="metal" if m.era==0 else None)
    rack_silo(m,.262 if mechanical else .35,.55,mechanical)
    if mechanical:
        rack_silo(m,.75,.58,True)
    else:
        solar_badge(m,-.43,.72 if m.era<2 else .65,.54,.090)
    rows=(4,3,3)[m.lod]
    for j in range(rows):
        z=-.72+j*.739/(rows-1)
        m.slab("water",0,.061,z,1.75,.033,.243,cut=.035)
        count=(6,5,3)[m.lod]
        for i in range(count):
            x=-.84+(i+.5)*1.68/count
            role="thatch" if (j+m.era)%3==0 else "leaf"
            # Ridges of leafy crops are interrupted by glimpses of water.
            m.slab(role,x,.093,z,1.68/count*.83,.10+(i+2*j)%3*.018,.146,.40,cut=.07)
            if m.lod==0 and i%2==0:
                m.blade("grass",(x,.13,z),(x+.067,.252,z+.021),.030)
    if mechanical:
        # Traversing irrigator on legs with a visible crossfield spray pipe.
        for x in (-.86,.86):
            m.rod("metal",(x,.061,-.27),(x,.49,-.27),.032)
        m.rod("turquoise",(-.88,.48,-.27),(.88,.48,-.27),.034)
        if m.lod==0:
            for x in (-.57,-.19,.19,.57):
                m.drum("gold",x,.376,-.27,.045,.078,.018,n=6)
    elif m.era>=2:
        m.rod("metal",(.70,.06,.45),(.70,.53,.45),.070)
        m.slab("glass",.70,.53,.45,.39,.077,.30,.82,underside=True)


def animal(m,x,z,angle):
    # A patient, low-slung, four-legged marsh grazer with a broad tail and a dorsal crest.
    with m.transform(translate=(x,.065,z),rotate_y=angle):
        for s in (-1,1):
            for t in (-1,1):
                m.block("trim",s*.075,0,t*.111,.054,.108,.065)
        m.slab("hide",0,.085,0,.23,.147,.37,.80,cut=.20)
        m.slab("ivory",0,.143,-.19,.15,.093,.16,.81,cut=.24)
        m.slab("thatch",0,.145,-.28,.118,.059,.099,.67,cut=.19)
        m.blade("leaf",(0,.143,.13),(.06,.079,.40),.074)
        if m.lod==0:
            for dz in (.015,.102):
                m.blade("ivory",(0,.213,dz),(.002,.296,dz+.06),.038)


def pasture(m):
    if m.era:
        works.footing(m,1.93,1.86,"grass")
        if m.era==1:
            # Crescent dry-stone paddock leaves an open pasture gate in front.
            works.pagoda(m,-.42,.44,.78,.63,.59)
            center=(.23,-.16)
            count=(13,8,4)[m.lod]
            for i in range(count):
                a,b=[math.radians(-60+(290*j/count)) for j in (i,i+1)]
                A=(center[0]+.57*math.cos(a),.24,center[1]+.56*math.sin(a))
                B=(center[0]+.57*math.cos(b),.24,center[1]+.56*math.sin(b))
                m.rod("timber",A,B,.039,n=4)
                m.drum("wall",A[0],.08,A[2],.057,.25,.047,n=(7,5,4)[m.lod])
            with m.transform(translate=(0,.015,0)):
                animal(m,.18,-.13,-42)
        elif m.era==2:
            works.barrel(m,-.31,.31,1.12,.98,.39,.34)
            works.vessel(m,.56,.48,.24,.78)
            # Fenced handling chute leads to the open shed.
            for x in (-.64,-.07):
                for z in (-.68,-.19):
                    m.block("timber",x,.08,z,.069,.32,.07)
                m.rod("metal",(x,.34,-.69),(x,.34,-.17),.032)
            if m.lod==0:
                m.disc(-.31,.42,-.183,.088)
            with m.transform(translate=(0,.015,0)):
                animal(m,.43,-.32,22)
        else:
            # Free-range cooling canopy: two ridge frames throw shade onto a
            # low grazing yard. The drinking reservoir sits outside the pen.
            works.ridge(m,-.25,.25,1.24,1.15,1.03)
            works.basin(m,.60,-.13,.39,.81)
            with m.transform(translate=(0,.015,0)):
                animal(m,-.24,-.13,-14)
                if m.lod<2:
                    animal(m,-.09,.47,77)
            for x in (-.74,.23):
                m.slab("ivory",x,.08,-.61,.15,.26,.17,.77)
            if m.lod==0:
                m.disc(.23,.28,-.70,.055)
        return
    m.slab("plaster" if m.era==0 else "grass",0,0,0,1.92,.066,1.84,cut=.07)
    if m.era==0:
        m.slab("grass",-.023,.066,-.058,1.55,.009,1.34,cut=.15)
    m.house(-.39,.066,.48,.93,.65,.61,role="roof" if m.era==0 else None)
    # A clear gate in the south fence.
    runs=[((-.86,-.77),(-.31,-.77)),((.20,-.77),(.86,-.77)),
          ((.86,-.77),(.86,.79)),((.86,.79),(-.86,.79)),((-.86,.79),(-.86,-.77))]
    for (x,z),(u,v) in runs:
        m.block("trim",x,.063,z,.066,.31,.069)
        if m.lod<2 and math.dist((x,z),(u,v))>1:
            m.block("trim",(x+u)/2,.063,(z+v)/2,.066,.31,.069)
        for y in ((.169,.298) if m.lod==0 else (.24,)):
            m.rod("timber" if m.era<2 else "metal",(x,y,z),(u,y,v),.025,n=4)
    m.block("trim",-.31,.063,-.77,.066,.31,.069)
    for x,z,a in ((-.35,-.24,58),(.39,-.16,-34),(.55,.41,144))[:(3,2,1)[m.lod]]:
        animal(m,x,z,a)
    if m.era>=2:
        m.drum("metal",.38,.066,.64,.17,.37)
        m.mound("glass",.38,.436,.64,.17,.11)
    if m.lod<2:
        solar_badge(m,.20,.35,-.77,.063)


def mill(m):
    if m.era==2:
        works.footing(m)
        # The elevator dominates the new steam roller mill. Two grain bins
        # discharge into the low winding house through a covered transfer chute.
        works.barrel(m,.29,-.27,1.08,.86,.41,.25)
        for x,z,r,h in ((-.45,.32,.31,1.14),(.23,.48,.27,.88)):
            m.drum("wall",x,.08,z,r,h)
            m.drum("metal",x,.08+h,z,r*1.05,.18,r*.20)
            if m.lod<2:
                m.drum("trim",x,.08+h*.72,z,r*1.016,.046)
        m.rod("metal",(-.45,1.09,.32),(.24,.63,-.23),.081)
        if m.lod<2:
            works.wheel(m,-.51,.39,-.48,.22)
            m.slab("brick",-.51,.08,-.31,.33,.35,.25,.88)
        return
    m.house(-.34,0,.24,.88,.85,.72)
    m.slab("wall",.29,0,.18,.67,.84,.77,.81)
    m.rooflet(.29,.84,.18,.78,.87,.34,m.skin)
    # Head pond and overshot wheel. The axle enters the tower along Z.
    m.slab("water",.29,0,-.39,1.08,.072,.56)
    for x in (-.251,.832):
        m.block("fieldstone",x,0,-.39,.069,.112,.64)
    m.block("fieldstone",.20,0,-.678,.91,.109,.059)
    # A raised leat delivers water at the upper rim and drains through the gap in the bank.
    for z in (-.055,.37):
        m.block("trim",.661,0,z,.082,.88+.17*z,.081)
    with m.transform(translate=(.661,.87,.105),rotate_x=-10):
        m.block("water" if m.lod==2 else "timber",0,0,0,.178,.051,.78)
        if m.lod<2:
            m.block("water",0,.051,0,.136,.025,.78)
            for x in (-.083,.083):
                m.block("trim",x,.051,0,.029,.064,.78)
    cx,cy,cz=.29,.53,-.29
    r=.448
    m.ring("timber" if m.era==1 else "metal",cx,cy,cz,r,r*.78,.107,n=(16,10,5)[m.lod])
    m.rod("trim",(cx,cy,cz-.038),(cx,cy,.20),.076)
    for i in range((8,6,3)[m.lod]):
        a=2*math.pi*i/(8,6,3)[m.lod]
        m.rod("trim",(cx+.05*math.cos(a),cy+.05*math.sin(a),cz+.054),
              (cx+r*.88*math.cos(a),cy+r*.88*math.sin(a),cz+.054),.030,n=4)
        if m.lod==0:
            with m.transform(translate=(cx+r*math.cos(a),cy+r*math.sin(a),cz+.058),rotate_z=math.degrees(a)):
                m.block("gold",0,-.037,0,.091,.074,.159)
    m.disc(cx,cy,cz-.038,.106)
    if m.era==2:
        m.drum("brick",-.56,.47,.39,.104,.70,.083)
        m.drum("metal",-.56,1.16,.39,.11,.061)


def hydro(m):
    m.slab("ivory",0,0,0,1.47,.148,1.42,.89)
    m.drum("glass",0,.147,.06,.42,1.38,.265)
    for i in range(3):
        y=.32+i*.44
        r=.77-.092*i
        m.drum("roof",0,y,.06,r*.77,.116,r)
        m.drum("turquoise",0,y+.114,.06,r,.049)
        if m.lod<2:
            m.drum("soil",0,y+.163,.06,r*.94,.033)
        for j in range((8,5,3)[m.lod]):
            a=2*math.pi*(j+.31*i)/(8,5,3)[m.lod]
            px,pz=r*.75*math.cos(a),.06+r*.75*math.sin(a)
            root=(px,y+(.193 if m.lod<2 else .160),pz)
            m.blade("leaf",root,(px+.145*math.cos(a+.50),y+.395,pz+.145*math.sin(a+.50)),.096)
            if m.lod==0:
                m.blade("grass",root,(px+.141*math.cos(a-.61),y+.331,pz+.141*math.sin(a-.61)),.079)
    m.slab("wall",0,.14,-.458,.42,.29,.33)
    m.entry(0,.14,-.623,.245,.22)
    m.mound("roof",0,1.52,.06,.31,.30)
    solar_badge(m,0,1.79,.06,.122)


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        name=spec.building_type
        if name=="FishingHut":
            fisher(m)
        elif name in ("GathererHut","HuntingLodge"):
            hut(m,name=="HuntingLodge")
        elif name in ("Farm","IndustrialFarm"):
            field(m,name=="IndustrialFarm")
        elif name=="Pasture":
            pasture(m)
        elif name=="Mill":
            mill(m)
        else:
            hydro(m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
