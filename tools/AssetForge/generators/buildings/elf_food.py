"""Elven food-producing buildings."""
import math
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish
from forge.atlas import palette
from forge.registry import PREVIEWS
from forge.render import contact_sheet

PACKAGE = "elf_food"
ERAS = ("ancient", "classical", "industrial", "future")
TAU = 2 * math.pi


class Grove(MeshBuilder):
    """Curved timber, fitted petal roofs and lantern-pod glazing for the elven trades."""

    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.era = ERAS.index(spec.band)
        self.colors = palette(spec.ages[0])[0]

    def color(self, role):
        return self.colors.get(role) or self.colors["elf/" + role]

    def face(self, role, points):
        self.poly(points, self.color(role), material=role)

    def disk(self, role, x, y, z, rx, rz, height, top=1, sides=None):
        n = sides or (12, 8, 5)[self.lod]
        with self.transform(translate=(x,y,z), scale=(rx,1,rz)):
            with self.transform(rotate_y=90+180/n):
                self.cylinder((0,0,0),1,height,self.color(role),segments=n,
                              top_radius=top,material=role)

    def lump(self, role, point, size, sides=None):
        with self.transform(translate=point,scale=size):
            self.sphere((0,0,0),1,self.color(role),rings=(4,3,2)[self.lod],
                        segments=sides or (8,6,4)[self.lod],material=role)

    def limb(self, role, points, radii, sides=None):
        """A continuous tapered sweep. Adjacent spans share the complete bend ring."""
        path = np.asarray(points)
        n = sides or (6,5,3)[self.lod]
        axis = path[-1]-path[0]
        ref = np.eye(3)[np.argmin(np.abs(axis))]
        rings = []
        for i,(p,r) in enumerate(zip(path,radii)):
            direction = path[min(i+1,len(path)-1)]-path[max(i-1,0)]
            direction = direction / np.linalg.norm(direction)
            u = np.cross(direction,ref)
            u /= np.linalg.norm(u)
            v = np.cross(direction,u)
            rings.append([p+r*(math.cos(TAU*j/n)*u+math.sin(TAU*j/n)*v) for j in range(n)])
        for a,b in zip(rings,rings[1:]):
            for j in range(n):
                k = (j+1)%n
                # At bends a ruled facet may be twisted. Project each triangle independently.
                self.face(role,[a[j],a[k],b[k]])
                self.face(role,[a[j],b[k],b[j]])
        self.face(role,rings[0][::-1])
        self.face(role,rings[-1])

    def leaf(self, role, root, tip, width, arch):
        """A lance-shaped blade with a keeled underside and a raised central vein."""
        a,b = np.array(root),np.array(tip)
        d = b-a
        side = np.array([d[2],0,-d[0]]) / math.hypot(d[0],d[2])
        up = np.array([0,1,0])
        slices = ((.28,.65),(.58,1),(.80,.63)) if self.lod==0 else ((.52,1),)
        rings = []
        for t,w in slices:
            c = a + d*t + up*arch*math.sin(math.pi*t)
            rings.append([c+up*.32*width*w,c-side*width*w,
                          c-up*.13*width*w,c+side*width*w])
        # Cross-section seen looking towards the tip is counterclockwise.
        for j in range(4):
            k=(j+1)%4
            self.face(role,[a,rings[0][k],rings[0][j]])
            for left,right in zip(rings,rings[1:]):
                self.face(role,[left[j],left[k],right[k]])
                self.face(role,[left[j],right[k],right[j]])
            self.face(role,[rings[-1][j],rings[-1][k],b])

    def crown(self,x,y,z,rx,rz,h,role="roof",lobes=6):
        """Scalloped roof of overlapping visual lobes, with one continuous weather shell."""
        n = lobes*(4,2,1)[self.lod]
        rings=[]
        bands=[(1,0),(.78,.32),(.45,.65),(.15,.87)]
        if self.lod==1:
            bands=[bands[i] for i in (0,2,3)]
        elif self.lod==2:
            bands=[bands[i] for i in (0,2)]
        for r,v in bands:
            ring=[]
            for j in range(n):
                a=TAU*j/n-math.pi/2
                scallop=.5+.5*math.cos(lobes*a+lobes*math.pi/2)
                scale=r*(1-.12*(1-scallop))
                ring.append((x+rx*(scale*math.cos(a)+.12*v*v),
                             y+h*(v+.19*(1-scallop)*r),
                             z+rz*(scale*math.sin(a)+.18*v*v)))
            rings.append(ring)
        tip=(x+.15*rx,y+h*1.12,z+.19*rz)
        for j in range(n):
            k=(j+1)%n
            mat="leaf" if role=="roof" and (j*lobes//n)%3==1 else role
            for a,b in zip(rings,rings[1:]):
                self.face(mat,[a[k],a[j],b[j]])
                self.face(mat,[a[k],b[j],b[k]])
            self.face(mat,[rings[-1][k],rings[-1][j],tip])
            self.face("trim",[(x,y+.035*h,z),rings[0][j],rings[0][k]])

    def portal(self,x,y,z,w,h,role="door"):
        # A hollow-looking pointed arch with a structural reveal and proud inner door.
        coords=[(-.5,0),(-.5,.53),(-.37,.77),(0,1),(.37,.77),(.5,.53),(.5,0)]
        for span,height,depth,mat in ((1,1,0,"trim"),(.71,.82,-.012,role)):
            self.face(mat,[(x+u*w*span,y+v*h*height,z+depth) for u,v in coords])
        if self.lod==0:
            self.lump("gold",(x+.16*w,y+.31*h,z-.014),(.033*w,.034*h,.024*w),5)

    def hall(self,x,y,z,rx,rz,h,cap,role="wall"):
        n=(12,8,6)[self.lod]
        self.disk("fieldstone",x,y,z,rx*1.05,rz*1.05,.095*h,.97,n)
        self.disk(role,x,y+.085*h,z,rx,rz,.92*h,.995,n)
        self.crown(x,y+.985*h,z,rx*1.21,rz*1.23,cap)
        # Flat front landing is tangent to the drum. Both reveal and tread meet the wall.
        front=z-rz*math.cos(math.pi/n)
        self.disk("trim",x,y+.004*h,front,.40*rx,.18*rz,.066*h,.98,8)
        self.portal(x,y+.069*h,front-.007,.66*rx,.72*h)
        if self.lod<2:
            for s in (-1,1):
                self.limb("timber" if self.era==0 else "trim",
                          [(x+s*rx*.92,y+.025*h,z-.38*rz),(x+s*rx*.84,y+.60*h,z-.52*rz),
                           (x+s*rx*.58,y+1.04*h,z-.80*rz)],
                          [rx*.12,rx*.082,rx*.055])
                self.lump("emissive",(x+s*rx*.75,y+.57*h,z-.66*rz),
                          (rx*.10,h*.131,rz*.103))
        if self.lod==0:
            # A bent fruiting shoot above the finial.
            self.leaf("leaf",(x+rx*.141,y+h+cap*1.06,z+.21*rz),
                      (x+rx*.49,y+h+cap*1.24,z+.36*rz),rx*.105,.06*cap)

    def pot(self,x,y,z,r,produce="flower"):
        self.disk("hide",x,y,z,r*.71,r*.71,r*.99,1.34)
        self.disk("trim",x,y+r*.91,z,r,r,r*.16)
        if self.lod<2:
            for a in (0,2.2,4.4):
                self.lump(produce,(x+r*.40*math.cos(a),y+r*1.10,z+r*.40*math.sin(a)),
                          (r*.39,r*.38,r*.39),5)


def farm(m):
    if m.era in (1,3):
        planted_fields(m)
        return
    m.disk("plaster" if m.era==0 else "soil",0,0,0,1.48,1.45,.055,.99, (20,12,8)[m.lod])
    m.hall(-.38,.054,.47,.48,.39,.55+.05*m.era,.42+.035*m.era)
    # Three curved planting beds follow the sweep of the boundary.
    for row in range(3):
        z=-.19-.34*row
        span=(1.19,1.13,.87)[row]
        mat=("leaf","thatch","grass")[row]
        for i in range((7,4,2)[m.lod]):
            count=(7,4,2)[m.lod]
            x=span*((i+.5)*2/count-1)
            bend=z+.19*(x/span)**2
            m.leaf(mat,(x-span/count,.049,bend),(x+span/count,.050,bend+.04),
                   (.13,.13,.14)[m.lod],.14+.04*(row==1))
    if m.era<2:
        m.disk("timber",.50,.053,.46,.21,.21,.37,.89)
        m.crown(.50,.414,.46,.30,.30,.32,"thatch",5)
        if m.lod==0:
            m.pot(.73,.055,.04,.096,"gold")
    else:
        for i in range(1+(m.era==3)):
            x=.48+.34*i
            m.disk("metal",x,.054,.42,.20,.19,.62+.12*i,.84)
            m.crown(x,.67+.12*i,.42,.18,.18,.18,"glass",4)
        if m.lod<2:
            m.limb("metal",[(.48,.47,.44),(.34,.47,.55),(.03,.28,.56)],[.045,.043,.041])


def planted_fields(m):
    from generators.buildings.elf_trade_forms import arch, block
    if m.era==1:
        # Three rising vineyard terraces, each with a loaded vine and a dry sidewall.
        for j in range(3):
            z=-.72+j*.62
            h=.14+.135*j
            width=2.10-.21*j
            block(m,"fieldstone",(0,h/2,z),(width,h,.54),.06)
            block(m,"soil",(0,h+.016,z),(width*.90,.032,.46))
            for s in (-1,1):
                block(m,"timber",(s*.37*width,h+.178,z),(.058,.34,.06))
            m.limb("timber",[(-.38*width,h+.319,z),(.38*width,h+.321,z+.008)],[.028,.028],4)
            clusters=(6,4,2)[m.lod]
            for i in range(clusters):
                x=width*.74*((i+.5)/clusters-.5)
                reach=.42*width/clusters
                m.leaf("leaf",(x-reach,h+.31,z),(x+reach,h+.31,z+.03),.10,.068)
                if m.lod==0 or (m.lod==1 and i%2==0):
                    m.lump("flower",(x,h+.235,z-.05),(.055,.083,.052),5)
        arch(m,0,.394,.58,.62,.85,.17,"trim")
        return
    # A solar rake spans two long beds. The small drive carriage stays beside the rows.
    m.disk("soil",0,0,-.01,1.34,1.20,.071,.99,(18,12,7)[m.lod])
    for j in range(4):
        x=-.76+j*.47
        length=1.45+.19*(j in (1,2))
        block(m,"wall",(x,.108,.02),(.33,.082,length))
        block(m,"grass",(x,.153,.02),(.24,.016,length*.96))
        blades=(7,5,3)[m.lod]
        for i in range(blades):
            z=.02+length*((i+.5)/blades-.5)
            m.leaf("leaf" if j%2==0 else "thatch",(x-.123,.157,z-.047),(x+.105,.22,z+.055),.11,.12)
    for x in (-.91,.85):
        block(m,"metal",(x,.30,.33),(.081,.47,.103))
    block(m,"trim",(-.03,.548,.33),(1.84,.084,.14))
    for x in (-.41,.35):
        with m.transform(translate=(x,.601,.32),rotate_x=-12):
            block(m,"glass",(0,0,0),(.68,.053,.38))
    m.disk("metal",.82,.069,-.27,.17,.19,.25,.87)
    m.crown(.82,.31,-.27,.18,.20,.10,"ivory",4)


def deer(m,x,z,angle):
    with m.transform(translate=(x,.052,z),rotate_y=angle):
        m.lump("hide",(0,.24,0),(.12,.133,.219),6)
        m.limb("hide",[(0,.25,-.13),(0,.39,-.216),(0,.46,-.241)],[.093,.062,.061])
        m.lump("ivory",(0,.456,-.264),(.069,.067,.095),6)
        for s in (-1,1):
            for end in (-1,1):
                m.limb("timber",[(.071*s,.008,.147*end),(.070*s,.222,.12*end)],[.023,.036],4)
            m.leaf("trim",(s*.04,.485,-.23),(s*.102,.576,-.175),.024,.009)
        if m.lod==0:
            m.lump("ivory",(0,.261,.201),(.045,.049,.04),5)


def pasture(m):
    if m.era:
        stockyard(m)
        return
    m.disk("plaster" if m.era==0 else "grass",0,0,0,1.44,1.40,.055,1, (20,14,10)[m.lod])
    if m.era==0:
        m.disk("grass",-.033,.055,-.053,1.06,1.02,.009,1,(16,10,6)[m.lod])
    m.hall(-.37,.053,.52,.44,.36,.48+.05*m.era,.39+.068*m.era)
    # Open south gate. Every rail stops inside a post.
    count=(8,8,7)[m.lod]
    prev=None
    for i in range(count):
        a=-math.pi/2+.26+(TAU-.52)*i/(count-1)
        x,z=1.32*math.cos(a),1.25*math.sin(a)
        m.disk("trim",x,.043,z,.044,.044,.295,.64,5)
        if prev:
            for y in ((.155,.266) if not m.lod else (.218,)):
                m.limb("timber" if m.era<2 else "metal",[(prev[0],y,prev[1]),(x,y+.008,z)],
                       [.023,.024],4)
        prev=(x,z)
    for x,z,a in ((.52,-.36,65),(-.37,-.51,-35))[:2 if m.lod<2 else 1]:
        deer(m,x,z,a)
    if m.lod<2:
        m.disk("timber",.62,.055,.32,.23,.11,.109,1.04)
        m.disk("water",.62,.164,.32,.188,.084,.008)
    if m.era>=2:
        m.disk("metal",.42,.055,.86,.16,.15,.45,1)
        m.lump("glass",(.42,.503,.86),(.151,.124,.146),6)


def stockyard(m):
    from generators.buildings.elf_trade_forms import arch, block, gable
    m.disk("grass",0,0,0,1.33,1.23,.055,.98,(18,12,7)[m.lod])
    if m.era==1:
        # An almond-shaped stone byre shelters one side of the grazing court.
        block(m,"wall",(-.45,.37,.34),(.70,.64,.65),.09)
        gable(m,-.45,.67,.34,.96,1.02,.46)
        m.portal(-.45,.055,.005,.38,.48)
        arch(m,.59,.055,-.48,.58,.47,.091,"timber")
        animals=[(.24,-.47,24),(-.56,-.65,-54)]
    elif m.era==2:
        # An open feed barn has a long steel spine, paired stanchions and a hayloft.
        for x in (-.70,.64):
            for z in (.10,.68):
                block(m,"metal",(x,.39,z),(.077,.67,.077))
        gable(m,-.03,.72,.39,1.72,1.01,.45,"metal")
        block(m,"timber",(-.03,.238,.62),(1.31,.367,.16))
        for x in (-.48,.16,.48):
            m.lump("thatch",(x,.23,.35),(.15,.18,.15),6)
        # The deer face the feeder, with an open lane between the stock and the barn.
        animals=[(-.29,-.30,164),(.53,-.39,194)]
        block(m,"timber",(-.03,.19,-.01),(1.02,.25,.16),.12)
        if m.lod<2:
            for x in (-.40,.34):
                block(m,"trim",(x,.275,-.015),(.072,.42,.18))
    else:
        # A high weather sail protects an open nursery. The stalk and roots carry the canopy.
        m.disk("wall",-.38,.053,.41,.32,.31,.18,.74)
        for a in (.21,2.18,4.12):
            dx,dz=math.cos(a),math.sin(a)
            m.limb("ivory",[(-.38+.38*dx,.08,.41+.33*dz),(-.38+.12*dx,.54,.41+.12*dz),
                            (-.38+.22*dx,1.10,.41+.19*dz)],[.11,.09,.072])
        m.crown(-.38,1.04,.41,.79,.63,.29,"glass",5)
        m.disk("trim",.65,.056,.23,.22,.37,.24,.93)
        m.disk("water",.65,.294,.23,.18,.31,.014)
        # A smaller calf rests in the lee of the sail. An adult drinks at the trough.
        animals=[(.65,-.27,179),(-.31,.22,79)]
        for x in (-.74,.13):
            m.disk("ivory",x,.054,-.56,.12,.11,.20,.58)
            m.lump("emissive",(x,.265,-.56),(.075,.065,.075),6)
    if m.lod==2:
        x,z,angle=animals[0]
        with m.transform(translate=(x,.05,z),rotate_y=angle):
            m.lump("hide",(0,.25,0),(.13,.13,.22),5)
            m.limb("hide",[(0,.22,-.13),(0,.42,-.24),(0,.43,-.30)],[.09,.072,.043],4)
            for z in (-.13,.13):
                block(m,"timber",(0,.105,z),(.13,.21,.043))
        return
    for i,(x,z,a) in enumerate(animals):
        with m.transform(translate=(x,0,z),scale=(.72,.72,.72) if i and m.era==3 else (1,1,1)):
            deer(m,0,0,a)
    if m.era!=1:
        return
    for sign in (-1,1):
        points=[(sign*1.01,.14,-.45),(sign*1.12,.14,.10),(sign*.94,.14,.55)]
        for x,y,z in points:
            m.disk("trim",x,.043,z,.045,.043,.22,.78,5)
        m.limb("timber" if m.era==1 else "metal",points,[.035]*3,4)


def hull(m,x,y,z,length,width):
    with m.transform(translate=(x,y,z),scale=(width,1,length)):
        plan=[(0,-.5),(.45,-.26),(.5,.13),(.33,.34),(0,.5),(-.33,.34),(-.5,.13),(-.45,-.26)]
        if m.lod==2:
            plan=plan[::2]
        def ring(s,base,bow):
            return [(a*s,base+bow*abs(b)**2,b*s) for a,b in plan]
        a,b,c,d=ring(.65,0,.08),ring(1,.085,.22),ring(.80,.086,.21),ring(.60,.024,.09)
        for i in range(len(a)):
            j=(i+1)%len(a)
            for r,t,mat in ((a,b,"timber"),(b,c,"accent"),(c,d,"timber")):
                m.face(mat,[r[j],r[i],t[i]])
                m.face(mat,[r[j],t[i],t[j]])
        m.face("hide",d[::-1])
        if m.lod<2:
            for depth in (-.15,.16):
                m.limb("trim",[(-.40,.071,depth),(.40,.071,depth)],[.022,.022],4)


def fishing(m):
    if m.era == 3:
        tidal_fishery(m)
        return
    if m.era == 1:
        net_house(m)
        return
    m.disk("timber",-.116,.083,.02,.338,.406,.068,1)
    for s in (-1,1):
        for t in (-1,1):
            m.disk("timber",-.116+.207*s,0,.02+.222*t,.043,.043,.099,.86,5)
    m.hall(-.12,.151,.115,.238,.215,.235+.018*m.era,.228+.012*m.era)
    hull(m,.306,.0,-.026,.81,.203)
    # A shore-mounted lift-net has a curved derrick, cable and gathered net/basket.
    h=.37+.067*m.era
    m.limb("trim",[(.014,.143,-.233),(.01,h,-.238),(.095,h+.132,-.246),(.277,h+.16,-.246)],
           [.032,.024,.020,.014])
    m.limb("timber",[(.275,h+.161,-.246),(.275,.238,-.246)],[.008,.008],4)
    m.disk("hide",.275,.17,-.246,.034,.034,.075,2.1, (9,6,4)[m.lod])
    if m.lod<2:
        m.lump("emissive",(-.29,.196,-.196),(.03,.048,.03),5)
        m.disk("trim",-.29,.142,-.196,.025,.025,.055)
    if m.era>=2:
        m.disk("metal",.022,.15,-.221,.062,.061,.114)
        if m.lod==0:
            m.lump("glass",(.024,.269,-.22),(.051,.035,.049))


def tidal_fishery(m):
    from generators.buildings.elf_trade_forms import block

    # A shore stair reaches the observation cabin independently of the tidal pen.
    # Its load-bearing shell sweeps up into a pointed leaf keel at the stern.
    x = -.259
    for px,pz in ((-.354,.264),(-.157,.268),(-.322,-.118)):
        m.disk("wall",px,0,pz,.063,.066,.191,.77,(8,6,4)[m.lod])
    m.disk("trim",x,.158,.079,.213,.332,.052,.98,(12,8,6)[m.lod])
    for i in range(3):
        h=.064*(i+1)
        block(m,"wall",(x,h*.5,-.403+.089*i),(.207,h,.100),.07)
    stations=[(-.169,.118,.284,.361),(-.063,.191,.368,.434),
              (.127,.198,.421,.547),(.271,.148,.434,.678),(.387,.067,.361,.748)]
    if m.lod==2:
        stations=[stations[i] for i in (0,2,4)]
    sections=[]
    for z,width,sill,ridge in stations:
        sections.append([(x+u*width,y,z) for u,y in
                         ((-1,.234),(-.78,.205),(.78,.205),(1,.234),
                          (.92,sill),(.74,sill+.024),(0,ridge),
                          (-.74,sill+.024),(-.92,sill))])
    m.face("wall",sections[0][::-1])
    m.face("roof",sections[-1])
    zones=("wall","wall","wall","glass","accent","roof","leaf","trim","glass")
    for fore,aft in zip(sections,sections[1:]):
        for i,role in enumerate(zones):
            j=(i+1)%len(zones)
            m.face(role,[fore[i],fore[j],aft[j]])
            m.face(role,[fore[i],aft[j],aft[i]])
    m.portal(x,.204,-.172,.135,.140)
    if m.lod<2:
        for i in (1,2):
            s=sections[i]
            for a,b in ((s[0],s[-1]),(s[3],s[4])):
                m.limb("trim",[a,b],[.010,.008],4)

    # A fixed, round lift-net stays open to the tide. Its hoop rides on three
    # guide piles. The drawn-in lower edge and diagonal ropes form the basket.
    cx,cz=.181,-.059
    rx,rz=.267,.308
    n=(16,12,8)[m.lod]
    def ring_point(a,scale,y):
        return (cx+rx*scale*math.cos(a),y,cz+rz*scale*math.sin(a))
    rings=[[ring_point(TAU*j/n,scale,y) for j in range(n)]
           for scale,y in ((.907,.208),(.924,.246),(1,.253),(1.024,.217))]
    for i,role in enumerate(("trim","ivory","metal","trim")):
        a,b=rings[i],rings[(i+1)%4]
        for j in range(n):
            k=(j+1)%n
            m.face(role,[a[j],a[k],b[k],b[j]])
    rim_y=.216
    base_y=.047
    for i in range(0,n,2 if m.lod==2 else 1):
        a=TAU*i/n
        b=TAU*(i+1)/n
        top=ring_point(a,.958,rim_y)
        heel=ring_point(b,.645,base_y)
        m.limb("metal",[top,heel],[.007,.007],(4,3,3)[m.lod])
        if m.lod<2:
            m.limb("trim",[top,ring_point(a-TAU/n,.645,base_y)],[.006,.006],3)
    for i in (0,1,2):
        a=(.41,2.43,4.74)[i]
        u,_,v=ring_point(a,.986,0)
        m.disk("wall",u,0,v,.033,.031,.227,.85,(8,5,4)[m.lod])
        if m.lod<2:
            m.disk("trim",u,.223,v,.030,.028,.054,.91,6)
    # The perforated well has a weighted bottom and a waterline inside the mesh.
    for height,scale,role in ((.046,.660,"trim"),(.128,.804,"water")):
        rim=[ring_point(TAU*j/n,scale,height) for j in range(n)]
        m.face(role,rim[::-1])

    # A larger catch and a fingerling sit at the surface below the retractable guide.
    school=((.014,.038,.225,-19),(-.038,-.089,.170,26))
    for dx,dz,length,angle in (school[:1] if m.lod==2 else school):
        with m.transform(translate=(cx+dx,.121,cz+dz),rotate_y=angle,scale=length):
            outline=[(.48,0),(.21,.115),(-.05,.137),(-.31,.052),
                     (-.32,-.046),(-.08,-.123),(.17,-.124)]
            pts=[(u,.018,v) for u,v in outline]
            dorsal=(.085,.172,.001)
            for j,p in enumerate(pts):
                m.face("ivory",[pts[(j+1)%len(pts)],p,dorsal])
            m.face("trim",pts)
            for sign in (-1,1):
                m.leaf("accent",(-.245,.054,0),(-.512,.084,sign*.182),.070,.023)
    # Its hollow collection arm links the wet well to the shore-side cabin.
    m.limb("wall",[(-.119,.302,.133),(-.038,.363,.149),
                   (.055,.357,.170),(.102,.257,.198)],[.037,.032,.026,.024])
    if m.lod<2:
        m.lump("emissive",(.045,.365,.170),(.028,.018,.024),6)


def net_house(m):
    from generators.buildings.elf_trade_forms import arch, block, gable, shaft

    # A boat can enter between the two pile jetties and tie up under the lift net.
    for x,end in ((-.314,-.445),(.308,-.387)):
        for z in (end+.055,.296):
            m.disk("timber",x,0,z,.042,.042,.222,.85)
        block(m,"timber",(x,.209,(.374+end)/2),(.124,.046,.374-end))
        if not m.lod:
            m.disk("trim",x,.219,end+.054,.030,.030,.050,.8)
    block(m,"trim",(-.021,.226,.218),(.746,.068,.329))
    block(m,"wall",(-.101,.384,.225),(.405,.275,.255),.07)
    gable(m,-.10,.516,.22,.52,.349,.171)
    m.portal(-.105,.253,.093,.167,.218)
    arch(m,-.105,.253,.090,.205,.222,.035,"timber")
    for x in (-.266,.253):
        shaft(m,"timber",(x,.211,-.284),(x,.624,-.273),.031,.022)
        shaft(m,"trim",(x,.239,-.13),(x,.518,-.274),.018,.014)
    shaft(m,"trim",(-.302,.611,-.272),(.284,.612,-.272),.025)
    # Solid knotted mouth and hanging pocket give the open mesh a silhouette at distance.
    count=(6,4,2)[m.lod]
    for i in range(count+1):
        x=-.22+.42*i/count
        u=i/count
        bottom=(x*.63,.345+.104*(2*u-1)**2,-.229)
        shaft(m,"hide",(x,.601,-.272),bottom,.0065)
        if i<count:
            shaft(m,"thatch",(x+.42/count,.597,-.268),bottom,.0053)
    m.limb("hide",[(-.143,.445,-.229),(-.078,.357,-.232),(.009,.331,-.218),
                   (.128,.433,-.229)],[.014,.021,.024,.013])
    hull(m,.03,.001,-.16,.483,.151)
    if m.lod<2:
        m.pot(-.289,.235,-.015,.052,"hide")


def lodge(m):
    from generators.buildings.elf_trade_forms import arch, block, gable, shaft, textile

    block(m,"fieldstone",(-.121,.040,.046),(.46,.080,.701),.045)
    block(m,"timber",(-.121,.218,.053),(.422,.326,.632),.07)
    gable(m,-.127,.379,.05,.511,.776,.226)
    m.portal(-.12,.073,-.266,.215,.268)
    arch(m,-.12,.070,-.283,.246,.297,.057,"trim")
    if m.lod<2:
        for z in (-.187,.190):
            shaft(m,"timber",(-.329,.089,z),(-.314,.376,z),.019,.015)
    # Shallow-pitched curing gallery along the east side of the longhouse.
    for z in (-.227,.043,.334):
        shaft(m,"timber",(.34,.003,z),(.318,.277,z),.027,.022)
    with m.transform(translate=(.202,.316,.056),rotate_z=-19):
        block(m,"leaf",(0,0,0),(.336,.037,.643))
    for z in ((-.156,.111) if m.lod < 2 else (0,)):
        with m.transform(translate=(.35,.251,z),rotate_y=90):
            textile(m,0,0,0,.168,.154,"hide")
    block(m,"trim",(-.114,.036,-.319),(.237,.072,.098),.035)
    # A trophy skull fixes the spreading antlers to the gable face.
    m.lump("ivory",(-.126,.429,-.344),(.051,.061,.036))
    for sign in (-1,1):
        def p(dx,dy,dz):
            return (-.126+sign*dx,dy,-.344+dz)
        m.limb("ivory",[p(.024,.461,0),p(.090,.516,-.018),p(.148,.557,-.012),
                        p(.17,.619,.005)],[.017,.016,.012,.006])
        shaft(m,"ivory",p(.079,.506,-.012),p(.084,.568,-.034),.014,.005)
        if m.lod<2:
            shaft(m,"ivory",p(.133,.547,-.013),p(.12,.594,-.032),.011,.004)
    if not m.lod:
        m.pot(.312,0,-.317,.062,"hide")


def small_hut(m,hunt):
    if hunt and m.era == 1:
        lodge(m)
        return
    m.hall(-.031,0,.098,.303,.263,.36+.063*m.era,.32+.095*m.era)
    if hunt:
        # Forked stag crest above the porch lintel and a hide-stretching frame in the yard.
        m.limb("timber",[(0,.291,-.151),(0,.359,-.228),(0,.421,-.278)],[.035,.029,.023])
        for s in (-1,1):
            pts=[(0,.418,-.278),(s*.090,.49,-.291),(s*.168,.572,-.270)]
            m.limb("ivory",pts,[.026,.021,.010],5)
            if m.lod<2:
                m.limb("ivory",[pts[1],(s*.066,.563,-.294)],[.014,.005],4)
        for x in (.032,.337):
            m.limb("timber",[(x,0,-.334),(x,.037,-.334),(x+.009,.281,-.32)],[.024,.024,.018],5)
        m.limb("trim",[(.021,.27,-.32),(.364,.275,-.32)],[.019,.02],5)
        m.leaf("hide",(.054,.26,-.329),(.33,.252,-.325),.080,-.062)
        m.pot(-.267,0,-.24,.077,"hide")
    else:
        # Covered sorter's table with baskets of berries below a broad, asymmetric leaf.
        m.limb("timber",[(-.32,0,-.213),(-.32,.254,-.21),(-.199,.379,-.243)],[.035,.024,.02])
        m.leaf("leaf",(-.205,.376,-.237),(.236,.283,-.246),.129,.10)
        for x in (-.27,.133):
            m.disk("trim",x,.003,-.22,.032,.032,.144)
        m.disk("timber",-.067,.143,-.22,.251,.09,.027)
        m.pot(.242,0,-.21,.082,"flower")
        if m.lod<2:
            m.pot(-.198,.17,-.219,.050,"gold")


def mill(m):
    h=1.05 if m.era==1 else 1.21
    m.hall(.065,0,.16,.42,.40,h,.44)
    cy,cz=.97,-.43
    m.limb("metal",[(.065,cy,.10),(.065,cy,cz-.045)],[.067,.059])
    for i in range(5):
        with m.transform(translate=(.065,cy,cz),rotate_z=13+i*72):
            m.limb("trim",[(0,.04,0),(0,.72,0)],[.031,.015],4)
            # Folded flax sails taper to a pointed outer tip and narrow heel.
            m.face("ivory",[(0,.16,-.024),(-.195,.52,-.042),(-.074,.73,-.027)])
            m.face("ivory",[(0,.16,-.024),(-.074,.73,-.027),(.009,.68,-.021)])
            m.face("hide",[(.009,.68,-.012),(-.074,.73,-.018),(-.195,.52,-.033),(0,.16,-.015)])
    if m.era==2:
        m.hall(-.49,0,.29,.257,.24,.41,.25,"brick")
    if m.lod<2:
        m.pot(.47,0,-.14,.116,"thatch")
        m.pot(.62,0,.02,.082,"thatch")


def glasshouse(m,x,z,rx,rz,h):
    m.disk("wall",x,.044,z,rx,rz,.19)
    with m.transform(translate=(x,.232,z),scale=(rx, h, rz)):
        m.dome((0,0,0),1,m.color("glass"),rings=(3,2,2)[m.lod],
               segments=(12,10,6)[m.lod],material="glass")
    if m.lod<2:
        for a in (0,math.pi/2,math.pi,3*math.pi/2):
            rings=(3,2)[m.lod]
            pts=[(x+(rx+.013)*math.cos(a)*math.cos(b),.232+(h+.016)*math.sin(b),
                  z+(rz+.013)*math.sin(a)*math.cos(b))
                 for b in [j*math.pi/(2*rings) for j in range(rings)]+[1.44]]
            m.limb("accent",pts,[.022]*len(pts),4)
    front=z-rz
    m.disk("wall",x,.04,front,.176,.115,.205)
    m.portal(x,.046,front-.106,.241,.192)


def greenhouse(m):
    if m.era==3:
        # The grower moves from paired ground tunnels to an orchard of elevated planting trays.
        for x,z,r,height in ((-.53,.35,.62,1.53),(.67,-.09,.45,.99)):
            m.disk("wall",x,0,z,r*.65,r*.72,.22,.93)
            m.disk("glass",x,.213,z,r*.46,r*.49,height-.20,.76)
            for tier in range(2):
                y=height*(.38+.42*tier)
                m.disk("roof",x,y,z,r*.60,r*.67,.14,1.54)
                m.disk("grass",x,y+.135,z,r*.89,r*.99,.062)
                if m.lod<2:
                    for j in range((5,3)[m.lod]):
                        a=j*TAU/(5,3)[m.lod]
                        u,v=x+r*.72*math.cos(a),z+r*.78*math.sin(a)
                        with m.transform(translate=(u,y+.245,v),scale=(.092,.113,.087)):
                            m.sphere((0,0,0),1,m.color("leaf"),rings=2,segments=5,material="leaf")
            m.crown(x,height,z,r*.52,r*.53,.23,"glass",5)
        m.hall(-.17,0,-.64,.36,.30,.40,.245)
        return
    m.disk("fieldstone",0,0,0,1.48,1.44,.052,.98, (20,14,8)[m.lod])
    glasshouse(m,-.55,-.12,.53,.92,.50)
    glasshouse(m,.51,.03,.43,.83,.64 if m.era==3 else .43)
    m.hall(-.09,.051,.83,.37,.30,.50,.32)
    if m.era==3:
        m.limb("trim",[(.60,.049,.60),(.56,.80,.54),(.22,1.06,.61)],[.06,.039,.022])
        m.leaf("glass",(.23,1.05,.61),(-.27,1.12,.49),.19,.092)
    if m.lod<2:
        for x in (.41,.66):
            m.pot(x,.052,-.89,.088,"leaf")


def hydro(m):
    m.disk("wall",0,0,0,.55,.51,.16,.96)
    m.disk("glass",0,.151,0,.366,.357,1.84,.68)
    for j in range(4):
        y=.34+j*.42
        r=.73-j*.074
        m.disk("roof",0,y,0,r*.67,r*.67,.145,1.49)
        m.disk("leaf",0,y+.143,0,r*.982,r*.982,.055,.96)
        for i in range((7,5,3)[m.lod]):
            n=(7,5,3)[m.lod]
            a=TAU*i/n+.7*j
            m.leaf("grass",(.37*r*math.cos(a),y+.19,.37*r*math.sin(a)),
                   (r*.94*math.cos(a),y+.27,r*.94*math.sin(a)),.127*r,.098)
    m.crown(0,1.98,0,.42,.43,.42,"roof")
    m.disk("wall",0,.04,-.389,.216,.161,.282)
    m.portal(0,.041,-.542,.233,.234)
    if m.lod<2:
        for a in (0,2.4,4.4):
            pts=[((.43-.064*j)*math.cos(a+j*.52),.155+j*.445,
                  (.43-.064*j)*math.sin(a+j*.52)) for j in range(5)]
            m.limb("accent",pts,[.044,.042,.036,.029,.022])


def build(spec,lod):
    m=Grove(spec,lod)
    t=spec.building_type
    if t=="FishingHut":
        fishing(m)
    elif t in ("HuntingLodge","GathererHut"):
        small_hut(m,t=="HuntingLodge")
    else:
        {"Farm":farm,"Mill":mill,"Pasture":pasture,"HydroponicsTower":hydro,
         "IndustrialFarm":greenhouse}[t](m)
    return m


def publish_package(package,build_model):
    from building_scope import AGES, ROOT

    rows=[]
    for spec in package_models(package):
        levels=[build_model(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=224)
        rows.append(entry)
        print(spec.id,entry["lod_triangles"],flush=True)
    directory=Path(PREVIEWS)/rows[0]["category"]
    for age in AGES:
        models=[e for e in rows if age in e["ages"]]
        if models:
            contact_sheet([ROOT/e["age_previews"][age] for e in models],directory/("sheet_"+age+".png"),
                          cols=min(5,len(models)),cell=160,labels=[e["building_type"] for e in models])


if __name__=="__main__":
    publish_package(PACKAGE,build)
