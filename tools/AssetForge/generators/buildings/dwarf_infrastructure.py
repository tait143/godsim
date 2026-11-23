"""Water, power and provisions for the mountain holds."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_learninghealth import Stonework, publish_package

PACKAGE = "dwarf_infrastructure"


def beam_pump(m):
    """A flywheel drives the walking beam above a public, stone-lined water well."""
    bx,bz=-.478,-.129
    n=(20,12,6)[m.lod]
    m.drum("fieldstone",bx,0,bz,.481,.105,.453,n=n)
    m.drum("brick",bx,.105,bz,.405,.199,.427,n=n)
    m.ring("metal",bx,.304,bz,.445,.350,.081,n=n)
    m.drum("water",bx,.305,bz,.393,.027,n=n)
    m.slab("fieldstone",.343,.098,.990,.795,0,.137,.112,.024)
    m.slab("brick",.389,.119,.785,.572,.137,.298,.073)
    # Twin bearing cheeks leave daylight around the central trunnion.
    for z in (-.035,.275):
        m.slab("wall",.097,z,.36,.148,.137,.961,.039,.050)
        m.slab("metal",.097,z,.262,.166,.961,1.053,.035)
    with m.transform(translate=(.096,.998,.358),rotate_x=-90):
        m.drum("gold",0,0,0,.084,.502,n=(12,8,5)[m.lod])

    # The cast girder is deeper above the bearing and tapers to each rod socket.
    outline=[(-.664,.785),(-.497,.875),(.076,1.135),(.219,1.126),
             (.714,1.207),(.73,1.101),(.212,.950),(.055,.901),(-.508,.772),(-.630,.702)]
    for z in (.044,.194):
        # Split the slightly concave side into vertical strips.
        for j in range(4):
            if z==.044:
                points=[outline[j],outline[j+1],outline[8-j],outline[9-j]]
            else:
                points=[outline[9-j],outline[8-j],outline[j+1],outline[j]]
            m.face("metal",[(x,y,z) for x,y in points])
    for j,(x,y) in enumerate(outline):
        u,v=outline[(j+1)%len(outline)]
        m.face("trim",[(x,y,.194),(u,v,.194),(u,v,.044),(x,y,.044)])
    # The well end works a polished vertical piston, with its delivery spout bent over the water.
    m.beam("gold",(-.563,.403,.119),(-.563,.817,.119),.042)
    m.drum("metal",-.563,.297,.119,.079,.246,.070,n=(12,7,5)[m.lod])
    m.beam("metal",(-.562,.448,.118),(-.516,.439,-.184),.066)
    m.beam("metal",(-.517,.449,-.182),(-.516,.401,-.193),.069)
    m.drum("water",-.516,.328,-.194,.024,.088,.021,n=(7,5,4)[m.lod])
    # Steam cylinder and eccentric share the engine bed at the drive end.
    m.drum("metal",.629,.298,.122,.118,.300,.112,n=(12,8,5)[m.lod])
    m.drum("gold",.629,.598,.122,.124,.060,.113,n=(12,8,5)[m.lod])
    m.beam("metal",(.629,.646,.119),(.656,1.139,.119),.051)
    wx,wy=.451,.493
    with m.transform(translate=(wx,wy,-.165),rotate_x=-90):
        m.ring("trim",0,0,0,.286,.231,.071,n=(22,12,7)[m.lod])
        m.drum("gold",0,-.031,0,.065,.124,n=(12,8,5)[m.lod])
        spoke_count=(6,5,3)[m.lod]
        for j in range(spoke_count):
            with m.transform(rotate_y=360*j/spoke_count):
                m.block("metal",(.149,.033,0),(.202,.041,.042),taper=.12)
    if m.lod<2:
        # A crank and connecting rod link the rim to the drive-end pin.
        m.beam("gold",(wx+.156,wy+.149,-.235),(.669,1.147,-.113),.043)
        with m.transform(translate=(.673,1.144,.123),rotate_x=-90):
            m.drum("trim",0,0,0,.051,.246,n=(9,6)[m.lod])
    if m.lod==0:
        m.rune(.275,.225,-.174,.105)
        for y in (.35,.49):
            m.drum("trim",.629,y,.122,.121,.035,n=12)
        m.ring("gold",bx,.200,bz,.422,.405,.023,n=n)


def well(m):
    e=m.era
    if e==2:
        return beam_pump(m)
    if e==3:
        return service_well(m)
    r=.49
    m.drum("fieldstone",0,0,0,.66,.115,.62)
    m.drum("wall",0,.115,0,r*.91,.225,r,n=(14,10,6)[m.lod])
    m.ring("roof" if e==0 else "wall",0,.34,0,r,r*.71,.115)
    m.ring("trim" if e!=3 else "metal",0,.455,0,r*1.05,r*.73,.064)
    m.drum("water",0,.34,0,r*.76,.024)
    # Two stone cheeks carry an axle. Later holds motorise the hoist, then add a collector.
    h=(.98,1.10,.95,.83)[e]
    for s in (-1,1):
        m.slab("wall" if e==0 else "fieldstone",s*.425,.048,.19,.25,.17,h,.04,.025)
    with m.transform(translate=(-.45,h-.105,.05),rotate_z=-90):
        m.drum("timber" if e<2 else "metal",0,0,0,.085,.99)
        m.drum("gold",0,.32,0,.13,.26)
    m.beam("trim",(.061,.41,-.062),(.061,h-.086,-.062),.025)
    if e==0:
        m.beam("timber",(-.57,h+.018,.054),(.57,h+.039,.054),.087)
    elif e==1:
        m.slab("trim",0,.05,1.18,.59,h,h+.078,.082)
        m.slab("roof",0,.05,1.21,.62,h+.078,h+.27,.11,.149)
    elif e==2:
        m.drum("metal",.425,h,.049,.161,.16,.142)
        m.flue(-.43,.047,h,.24,.092)
    else:
        m.slab("glass",0,.052,1.20,.66,h,h+.18,.12,.12)
        m.slab("ivory",0,.052,.99,.49,h+.18,h+.225,.08)
    if m.lod<2:
        with m.transform(translate=(.47,h-.105,.05),rotate_z=-90):
            m.ring("gold",0,0,0,.184,.146,.059)
            for a in range(0,360,60):
                with m.transform(rotate_y=a):
                    m.block("metal",(.11,.025,0),(.115,.030,.025),taper=.12)
        # Riveted water pail with a squared bail at the front of the stone apron.
        m.drum("metal",-.38,0,-.54,.095,.175,.135)
        m.ring("gold",-.38,.175,-.54,.139,.109,.026)
        m.drum("water",-.38,.175,-.54,.115,.008)
        if m.lod==0:
            m.bent_rail("trim",[(-.499,.197),(-.47,.301),(-.30,.309),(-.262,.197)],-.54,.023)
            m.rune(.426,.64,-.08,.095)


def service_well(m):
    m.slab("fieldstone",.005,0,1.72,1.47,0,.122,.146)
    # The open inspection basin is fed from an offset vertical pump, with a separate return manifold.
    m.drum("slate",-.284,.116,-.087,.462,.192,.436,n=(14,10,6)[m.lod])
    m.ring("metal",-.284,.301,-.087,.448,.321,.093,n=(14,10,6)[m.lod])
    m.drum("water",-.284,.304,-.087,.354,.027,n=(14,10,6)[m.lod])
    m.slab("trim",.454,.185,.49,.74,.120,.282,.095)
    m.drum("wall",.454,.276,.184,.203,.589,.173)
    m.drum("glass",.454,.861,.184,.179,.142,.173)
    m.crown("ivory",.454,.997,.184,.212,.53)
    m.beam("metal",(.454,.781,.184),(-.187,.78,.085),.099)
    m.drum("accent",-.186,.532,.086,.080,.282,.075)
    m.drum("water",-.186,.328,.086,.042,.207,.047)
    if m.lod<2:
        m.beam("metal",(.455,.374,.189),(.546,.382,-.397),.083)
        m.slab("ivory",.554,-.466,.365,.263,.123,.343,.062)
    if m.lod==0:
        for y in (.411,.573,.736):
            m.drum("trim",.454,y,.184,.205-(y-.411)*.05,.041)
        m.rune(.554,.247,-.605,.144)
        for x in (.10,.243,.386):
            m.block("metal",(x,.129,-.558),(.092,.018,.211))


def spiral_aqueduct(m):
    # One open flume spirals around a quarried core. Both ends join their
    # straight reaches tangentially, so the water and parapets run uninterrupted.
    cx,cz,radius=-.033,.705,.302
    sweep=2.5*math.pi
    start,drop=1.445,1.175
    segments=(36,20,10)[m.lod]
    # Across-channel offsets and elevations, traced across the water and
    # back along the underside. End faces are split at the two parapets.
    profile=[(-.133,-.090),(-.133,.066),(-.101,.066),(-.101,0),
             (.101,0),(.101,.066),(.133,.066),(.133,-.090)]
    surfaces=("ivory","gold","slate","water","slate","gold","ivory","metal")
    def section(x,y,z,nx,nz):
        return [(x+a*nx,y+b,z+a*nz) for a,b in profile]

    stations=[section(-.412,start+.018,cz+radius,0,1)]
    for i in range(segments+1):
        t=i/segments
        angle=sweep*(1-t)
        nx,nz=math.cos(angle),math.sin(angle)
        stations.append(section(cx+radius*nx,start-drop*t,cz+radius*nz,nx,nz))
    stations.append(section(cx+radius,.158,-1.387,1,0))
    for first,last in zip(stations,stations[1:]):
        for i,mat in enumerate(surfaces):
            j=(i+1)%len(profile)
            m.face(mat,(first[i],first[j],last[j],last[i]))
    for row,reverse in ((stations[0],True),(stations[-1],False)):
        # Frame ends meet the bed along edges only.
        for indices in ((0,1,2,3),(0,3,4,7),(4,5,6,7)):
            points=[row[i] for i in indices]
            m.face("trim",points[::-1] if reverse else points)

    m.drum("fieldstone",cx,0,cz,.336,.120,.286)
    m.drum("wall",cx,.120,cz,.174,1.240,.138,n=(10,8,6)[m.lod])
    m.drum("trim",cx,1.360,cz,.176,.106,.155,n=(10,8,6)[m.lod])
    for quarter in range(6):
        angle=quarter*math.pi/2
        height=start-drop*(1-angle/sweep)-.119
        with m.transform(translate=(cx,height,cz),rotate_y=-math.degrees(angle)):
            m.block("metal",(.209,0,0),(.271,.088,.103),taper=.14)
            if m.lod==0 and quarter>0:
                m.beam("trim",(.118,-.190,0),(.309,-.034,0),.051,.060)
    # A triangular bracket transfers the inlet apron back into the core.
    m.beam("trim",(cx,.935,cz),(-.353,1.362,cz+radius),.102)
    for z in (-1.202,-.553,.075):
        top=.158+.112*(z+1.387)/(cz+1.387)-.083
        m.slab("fieldstone",cx+radius,z,.355,.198,0,top,.034,.018)
    for side in (-1,1):
        m.block("trim",(-.361,1.528,cz+radius+side*.115),(.076,.182,.031))
    m.block("metal",(-.361,1.640,cz+radius),(.102,.048,.292))
    if m.lod<2:
        for height in (.339,.685,1.039):
            m.drum("metal",cx,height,cz,.175-height*.021,.046,n=(12,8)[m.lod])
    if m.lod==0:
        # Expansion plates bind the outside fascia of each cast flume section.
        for i in range(12):
            t=(i+.5)/12
            angle=sweep*(1-t)
            with m.transform(translate=(cx,start-drop*t,cz),rotate_y=-math.degrees(angle)):
                m.block("metal",(radius+.131,-.012,0),(.016,.153,.028))
        for z in (-1.202,-.553,.075):
            for side in (-1,1):
                m.block("gold",(cx+radius+side*.150,.056,z),(.031,.038,.125))


def aqueduct(m):
    e=m.era
    if e==3:
        # The flume section is authored in tile units to keep its circular plan.
        with m.transform(scale=(2,1,2/3)):
            return spiral_aqueduct(m)
    # Spandrels carry a continuous channel. Each bay is a true clear arch in the YZ plane.
    count=3 if e!=2 else 2
    height=(0,.78,.94,.86)[e]
    end=.91
    length=2*end/count
    thick=.38 if e!=3 else .32
    bottom=.135
    for i in range(count):
        center=-end+(i+.5)*length
        spring=.31 if e==1 else .36
        radius=length*.395
        rise=height-spring-.12
        # Subdivide the arch into transverse stone voussoirs. Split top/bottom
        # joints for crisp structural bands at close range.
        n=(10,5,2)[m.lod]
        for j in range(n):
            a=math.pi*j/n
            b=math.pi*(j+1)/n
            rad=.070 if e<3 else .046
            def section(theta,side):
                return ((side*thick/2,spring+rise*math.sin(theta),center-radius*math.cos(theta)),
                        (side*(thick/2+.009),spring+(rise+rad)*math.sin(theta),
                         center-(radius+rad*.53)*math.cos(theta)))
            # Side finish and the return under the arch.
            L0,U0=section(a,-1)
            L1,U1=section(b,-1)
            R0,V0=section(a,1)
            R1,V1=section(b,1)
            stone="plaster" if e==1 else "fieldstone"
            m.face(stone,(L1,L0,U0,U1)[::-1])
            m.face(stone,(R0,R1,V1,V0)[::-1])
            m.face("trim",(L0,R0,R1,L1))
            for side,A,B in ((-1,U0,U1),(1,V0,V1)):
                high0=(A[0],height,A[2])
                high1=(B[0],height,B[2])
                face=(A,B,high1,high0)
                m.face("wall",face if side<0 else face[::-1])
        if m.lod<2:
            m.block("gold" if e!=3 else "ascension",(0,height-.042,center),
                    (thick+.048,.119,.060),taper=.18)
    # End piers and interior piers exactly meet their adjacent arch springs.
    for j in range(count+1):
        z=-end+j*length
        pier=length*.21
        span=pier if 0<j<count else pier*.50
        center=z if 0<j<count else z+(1 if j==0 else -1)*span/2
        m.block("wall",(0,(height+bottom)/2,center),(thick+.020,height-bottom,span))
        m.slab("fieldstone",0,center,thick+.19,span+.075,0,bottom,.038,.025)
        if m.lod==0:
            for s in (-1,1):
                m.block("fieldstone",(s*(thick/2+.009),.285,center),(.076,.31,span*.84),taper=.21)
    width=thick+.13
    m.block("plaster" if e==1 else "roof",(0,height+.047,0),(width,.094,end*2+.035))
    m.block("water",(0,height+.103,0),(width-.113,.018,end*2+.012))
    for s in (-1,1):
        m.block(("wall" if e==1 else "trim") if e<3 else "ivory",(s*(width/2-.021),height+.142,0),
                (.055,.096,2*end+.041))
    if e==3:
        for z in (-.60,0,.60):
            m.slab("glass",0,z,width+.072,.15,height+.19,height+.30,.035,.043)
    if m.lod==0:
        m.rune(0,height*.53,-end-.007,.112)


def bins(m):
    e=m.era
    # Belly drums taper toward the foot, with a conical rain cap and a little cupola.
    silos=([(-.34,.17,.42,.64),(.42,.25,.31,.49)] if e==0 else
           [(-.39,.23,.35,.78),(.37,.21,.38,.91)] if e==1 else
           [(-.46,.32,.29,1.02),(.20,.32,.31,1.23),(.62,-.18,.20,.76)] if e==2 else
           [(-.37,.22,.39,.91),(.42,.09,.35,1.14)])
    for x,z,r,h in silos:
        m.drum("fieldstone",x,0,z,r*.81,.14,r*.87)
        m.drum("wall",x,.14,z,r*.82,h,r)
        m.drum("trim",x,.14+h,z,r*1.07,.058,r*1.05)
        if e==3:
            m.drum("roof",x,.198+h,z,r*1.10,r*.45,r*.47)
            m.slab("metal",x,z,.242,.23,.198+h+r*.44,1.62,.041,.014)
        else:
            m.drum("roof",x,.198+h,z,r*1.13,r*.79,r*.19)
            m.drum("gold",x,.198+h+r*.79,z,r*.22,.065,r*.19)
        if m.lod<2:
            for f in (.25,.70):
                radius=r*(.82+.18*f)+.017
                m.drum("trim",x,.14+h*f,z,radius,.040)
        if m.lod==0:
            for j in range(5):
                a=j*2*math.pi/5
                m.beam("timber" if e<2 else "metal",
                       (x+r*.84*math.cos(a),.15,z+r*.84*math.sin(a)),
                       (x+r*.99*math.cos(a),h+.115,z+r*.99*math.sin(a)),.024)
    m.hall(-.12,-.43,.80,.55,.32+.057*e,windows=1)
    m.stairway(-.12,-.705,.33,.085,.12)
    if e==3:
        with m.transform(translate=(.024,1.62,.155),rotate_y=9.3):
            m.slab("metal",0,0,1.13,.32,0,.063,.045)
            m.slab("glass",0,0,1.04,.273,.063,.219,.036)
            m.slab("ivory",0,0,1.13,.33,.219,.271,.054,.017)
            if m.lod<2:
                for x in (-.38,0,.38):
                    m.block("gold",(x,.141,0),(.034,.158,.286))
    if m.lod==0:
        m.rune(.10,.27,-.714,.108)


def hoist_store(m):
    # The tall left-hand loft hoists sealed goods from a lower open counting yard.
    x,z=-.432,.086
    m.slab("fieldstone",x,z,.815,1.47,0,.176,.101)
    m.slab("wall",x,z,.728,1.30,.176,.969,.082,.018)
    m.slab("timber",x,z,.803,1.36,.969,1.056,.070)
    m.hip_roof((x,1.056,z),.824,1.44,.39,m.color("roof"),overhang=.032,material="roof")
    m.portal(x,.166,-.565,.446,.545)
    m.slab("fieldstone",.394,.11,.896,1.29,0,.119,.063)
    # Beam projects over the yard, with a diagonal corbel at the warehouse wall.
    m.block("timber",(.244,.903,.157),(.766,.10,.145))
    m.beam("trim",(-.087,.492,.157),(.435,.876,.157),.076)
    m.block("metal",(.549,.627,.157),(.034,.483,.037))
    m.block("timber",(.549,.333,.157),(.30,.201,.317),taper=.09)
    if m.lod<2:
        for u,v,h in ((.218,-.354,.243),(.549,.481,.248)):
            m.drum("timber",u,.119,v,.174,h,.169)
            m.drum("trim",u,.119+h*.70,v,.18,.033)
        m.plaque("door",x,.842,-.568,.284,.153)
        m.rune(x,.855,-.572,.113)
    if m.lod==0:
        m.block("gold",(.549,.384,.157),(.061,.067,.325))
        for v in (-.365,.525):
            m.block("trim",(x-.362,.565,v),(.069,.78,.093),taper=.12)
        m.stairway(x,-.65,.366,.176,.247)


def stores(m):
    e=m.era
    if e==1:
        return hoist_store(m)
    h=.48+.086*e
    m.hall(-.11,.14,1.55,1.07,h,door=False,windows=1)
    m.portal(-.11,.12,-.405,.65,h*.84)
    m.stairway(-.11,-.612,.57,.14,.24)
    m.slab("timber" if e<2 else "metal",-.11,-.495,.98,.28,0,.138,.050)
    y=h+.10
    if e==0:
        m.slab("timber",-.13,.16,1.31,.93,y,y+.16,.13,.09)
        m.slab("roof",-.13,.16,1.18,.78,y+.16,y+.27,.14,.12)
    elif e==1:
        m.vault(-.14,.14,1.31,.89,y,.33,"roof","wall",(10,6,4)[m.lod])
    elif e==2:
        for x in (-.48,.23):
            m.wedge((x,y,.16),.65,.79,.28,m.color("roof"),material="roof")
            m.plaque("glass",x,y+.15,.559,.48,.115)
    else:
        m.slab("glass",-.15,.16,1.36,.77,y,y+.24,.14,.06)
        m.slab("ivory",-.15,.16,1.31,.72,y+.24,y+.31,.09)
    if m.lod<2:
        for x,z,w,d,height in ((-.68,-.60,.285,.28,.21),(.62,-.49,.31,.37,.26),
                                (.53,-.36,.18,.19,.44)):
            m.block("timber",(x,height/2,z),(w,height,d),taper=.05)
            for s in (-1,1):
                m.block("metal",(x+s*w*.36,(height+.007)/2,z),(w*.116,height+.007,d+.009))
        m.rune(-.11,h*.89,-.42,.114)
    if m.lod==0:
        m.foliate(.735,0,.70,.109)


def cooling_tower(m,x,z,r,h):
    # Continuous flared shell and return at the throat, open down to the interior basin.
    sections=[(.00,.91),(.10,1.0),(.36,.68),(.60,.60),(.81,.68),(.97,.91),(1,.94)]
    if m.lod==2:
        sections=[sections[i] for i in (0,1,3,5,6)]
    n=(12,9,6)[m.lod]
    def ring(y,radius):
        return [(x+r*radius*math.cos(j*2*math.pi/n),y*h,z+r*radius*math.sin(j*2*math.pi/n)) for j in range(n)]
    def belt(role,lower,upper,inside=False):
        for j in range(n):
            k=(j+1)%n
            face=(lower[k],lower[j],upper[j],upper[k])
            m.face(role,face[::-1] if inside else face)
    for (a,r0),(b,r1) in zip(sections,sections[1:]):
        role="fieldstone" if a==0 else "ivory" if a>.9 else "metal" if a>=.6 else "wall"
        belt(role,ring(a,r0),ring(b,r1))
    lip=ring(1,.82)
    outer=ring(1,.94)
    lower=ring(.69,.53)
    for j in range(n):
        k=(j+1)%n
        m.face("ivory",(outer[j],lip[j],lip[k],outer[k]))
    belt("slate",lower,lip,inside=True)
    m.face("door",lower[::-1])


def power(m):
    e=m.era
    h=.54 if e==2 else .64
    if e==2:
        m.hall(.14,-.16,1.24,.92,h,windows=3)
        m.stairway(.14,-.62,.39,.09,.18)
    else:
        m.hall(-.13,-.35,.96,.64,.49,windows=2)
        m.stairway(-.13,-.67,.36,.09,.16)
    if e==2:
        # A pair of stepped brick exhaust stacks rise from the boiler gallery.
        m.slab("brick",-.06,.42,1.14,.54,0,.36,.075)
        for x,H,r in ((-.35,1.14,.179),(.26,.83,.142)):
            m.flue(x,.43,.36,H,r)
    else:
        # Cooling towers have an inward waist and flared mouths, with recessed dark throats.
        cooling_tower(m,.505,.18,.417,1.49)
        cooling_tower(m,-.32,.49,.327,1.19)
    # Transformer pots on their own foundation, ceramic ribs join the copper crowns.
    m.slab("fieldstone",-.72,-.30,.36,.82,0,.09,.046)
    for i in range(2 if m.lod else 3):
        z=-.54+i*(.43 if m.lod else .22)
        m.drum("metal",-.72,.09,z,.109,.28,.084)
        if m.lod<2:
            for k in range(3):
                m.drum("ivory",-.72,.37+k*.042,z,.083-.010*k,.04,.052)
            m.drum("gold",-.72,.494,z,.04,.057)
    if m.lod==0:
        if e==2:
            m.rune(.14,h*.89,-.632,.124)
        else:
            m.rune(-.13,.413,-.701,.097)


def fusion(m):
    m.drum("fieldstone",0,0,.09,.68,.17,.74)
    m.drum("wall",0,.17,.09,.70,.33,.52)
    m.drum("metal",0,.50,.09,.50,.14,.44)
    m.drum("ascension",0,.64,.09,.42,.31,.35)
    m.crown("glass",0,.95,.09,.36,.74)
    # Field coils rest on four massive radial cradle arms. Open bore shows the central core.
    for j in range(4):
        with m.transform(translate=(0,0,.09),rotate_y=90*j+45):
            m.slab("wall",.60,0,.25,.27,0,.37,.065,.025)
            m.beam("metal",(.60,.34,0),(.59,.74,0),.126)
            if m.lod==0:
                m.block("gold",(.595,.55,0),(.151,.069,.147),taper=.09)
    m.ring("gold",0,.72,.09,.73,.52,.14)
    m.ring("metal",0,.86,.09,.69,.51,.067)
    m.hall(0,-.59,.58,.44,.39)
    m.stairway(0,-.81,.29,.089,.12)
    if m.lod<2:
        for x in (-.51,.51):
            m.drum("metal",x,0,-.51,.132,.32)
            m.crown("ascension",x,.32,-.51,.126,.44)
    if m.lod==0:
        m.rune(0,.365,-.825,.085)


def build(spec,lod):
    m=Stonework(spec,lod)
    w,d=spec.footprint
    scale=(w/2,min(1.14,(w+d)/4),d/2)
    with m.transform(scale=scale):
        {"Well":well,"Aqueduct":aqueduct,"PowerPlant":power,"FusionReactor":fusion,
         "Granary":bins,"Storehouse":stores}[spec.building_type](m)
    return m


if __name__=="__main__":
    publish_package(PACKAGE,build)
