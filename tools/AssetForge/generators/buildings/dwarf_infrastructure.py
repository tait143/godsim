"""Water, power and provisions for the mountain holds."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_learninghealth import Stonework, publish_package

PACKAGE = "dwarf_infrastructure"


def well(m):
    e=m.era
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


def aqueduct(m):
    e=m.era
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


def stores(m):
    e=m.era
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
