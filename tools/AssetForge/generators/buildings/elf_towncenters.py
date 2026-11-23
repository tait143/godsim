"""Ten council groves. The chamber, its living piers, and the public entrance survive every age."""
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish, style


class Grove:
    def __init__(self, model, lod):
        self.mesh = MeshBuilder(seed=model.id)
        self.age = model.ages[0]
        self.lod = lod
        self.n = (14, 10, 8)[lod]
        self.color = style.blended('elf', self.age, 'wall')
        self.bark = 'timber'
        self.moon = 'ivory'
        self.light = 'ascension' if self.age in ('information', 'ascension') else 'emissive'

    def drum(self, x, z, y, rx, rz, h, material, taper=1, n=None):
        with self.mesh.transform(translate=(x, y, z), scale=(rx, 1, rz)):
            segments = n or self.n
            with self.mesh.transform(rotate_y=90 - 180 / segments):
                self.mesh.cylinder((0, 0, 0), 1, h, self.color, segments, taper, material=material)

    def link(self, points, material, sides=None):
        """A continuous tapering tube. Transport cross sections along each growing bough."""
        n = sides or (6, 5, 4)[self.lod]
        xyz = np.array([p[:3] for p in points])
        rings = []
        for i, p in enumerate(points):
            direction = xyz[min(i + 1, len(points)-1)] - xyz[max(0, i-1)]
            direction /= np.linalg.norm(direction)
            reference = np.array([0., 0., 1.])
            if abs(direction @ reference) > .93:
                reference = np.array([1., 0., 0.])
            axis = np.cross(direction, reference)
            axis /= np.linalg.norm(axis)
            across = np.cross(direction, axis)
            rings.append([xyz[i] + p[3] * (math.cos(a)*axis + math.sin(a)*across)
                          for a in (math.tau*j/n for j in range(n))])
        for lower, upper in zip(rings, rings[1:]):
            for i in range(n):
                j = (i+1) % n
                self.mesh.quad(lower[i], lower[j], upper[j], upper[i], self.color, material)
        self.mesh.poly(list(reversed(rings[0])), self.color, material)
        self.mesh.poly(rings[-1], self.color, material)

    def pod(self, x, y, z, radius, height, material):
        with self.mesh.transform(translate=(x, y, z), scale=(radius, height, radius)):
            self.mesh.sphere((0, 0, 0), 1, self.color, rings=(4, 3, 2)[self.lod],
                             segments=(8, 6, 5)[self.lod], material=material)

    def leaf(self, material, vein, rx, length, height, curl, sweep=0, fold_height=None):
        """One sculpted blade, stem at the origin, pointed tip toward +Z.

        A folded midrib catches the light, the margins curl up at the tip. A thin
        underside closes the blade where it projects beyond the underlying dome.
        """
        fold_height = .035+rx*.28 if fold_height is None else fold_height
        if self.lod == 2:
            base, tip = (0,height,0), (sweep,0,length)
            mid_y = height*.40 + curl*.95
            left = (-rx+sweep*.36,mid_y,length*.60)
            right = (rx+sweep*.36,mid_y,length*.60)
            ridge = (sweep*.36,mid_y+fold_height,length*.60)
            keel = (sweep*.36,mid_y-.035,length*.60)
            for a,b in ((left,ridge),(ridge,right),(right,keel),(keel,left)):
                self.mesh.tri(base,a,b,self.color,material)
                self.mesh.tri(a,tip,b,self.color,material)
            return
        path = [(0, .035, .015), (.27, .64, .64), (.60, 1., 1.), (.84, .60, .66), (1., .018, .06)]
        if self.lod == 1:
            path = [path[0], (.37, .86, .85), (.73, .86, .89), path[-1]]
        sections = []
        for t, width, fold in path:
            x = sweep*t*t
            y = height*(1-t) + curl*math.sin(math.pi*t)
            width *= rx
            relief = fold_height*fold
            half_rib = max(.0018, width*.047)
            ridge = y+relief
            sections.append([(x-width, y, length*t), (x-half_rib, ridge, length*t),
                             (x+half_rib, ridge, length*t), (x+width, y, length*t),
                             (x, y-max(.009,width*.10), length*t)])
        if self.lod == 1:
            sections = [[s[0], tuple((a+b)/2 for a,b in zip(s[1],s[2])), s[3], s[4]] for s in sections]
            for a,b in zip(sections,sections[1:]):
                for i in range(4):
                    j=(i+1)%4
                    self.mesh.quad(a[i],b[i],b[j],a[j],self.color,material)
            self.mesh.poly(sections[0],self.color,material)
            self.mesh.poly(list(reversed(sections[-1])),self.color,material)
            return
        for a,b in zip(sections, sections[1:]):
            for left,right,mat in ((0,1,material),(1,2,vein),(2,3,material),
                                   (3,4,material),(4,0,material)):
                self.mesh.quad(a[left], b[left], b[right], a[right], self.color, mat)
        self.mesh.poly(sections[0], self.color, material)
        self.mesh.poly(list(reversed(sections[-1])), self.color, material)

    def canopy(self, x, z, eave, rx, rz, rise, petals, skin='leaf', rib='roof', twist=0, grafts=()):
        # The dome is weatherproof. The blades grow down over it and past the eaves.
        with self.mesh.transform(translate=(x, eave, z), scale=(rx, 1, rz)):
            self.mesh.dome((0,.014,0), .81, self.color, rings=(3,2,1)[self.lod],
                           segments=self.n, squash=rise*.75/.81, material=skin)
            for i in range(petals):
                with self.mesh.transform(rotate_y=360*i/petals+twist):
                    with self.mesh.transform(translate=(0,-.005,.025)):
                        surface = 'leaf' if i in grafts else skin
                        sweep = .12 if i % 2 else .07
                        self.leaf(surface, rib if self.lod < 2 else surface,
                                  min(.59, .98*math.sin(math.pi/petals)), .97-.035*(i%3),
                                  rise+.025, rise*.24, sweep,fold_height=rise*.15)

    def joined_canopy(self, x, z, eave, rx, rz, rise, petals, skin, grafts, twist):
        """Leaf grafts replace entire roof panels. Adjacent blades share their margins."""
        # The crease curls along the roof. Its seam, raised vein and scalloped hem
        # are geometry on a single shell at every LOD, including its underside.
        fractions = (-1,-.42,-.067,.067,.42,1) if self.lod == 0 else (-1,0,1)
        stations = ((.06,.32,.65,1),(.06,.46,1),(.06,.57,1))[self.lod]
        crown = (x,eave+rise+.015,z)
        soffit = (x,eave-.024,z)
        for i in range(petals):
            mat = 'leaf' if i in grafts else skin
            rings=[]
            for t in stations:
                ring=[]
                for frac in fractions:
                    lobe=1-abs(frac)
                    radius=t*(1-.21*abs(frac)**1.3*t*t)
                    angle=math.radians(twist)+(i+frac*.5)*math.tau/petals+.12*t*t
                    arch=rise*(1-t)**.70+rise*.11*math.sin(math.pi*t)
                    y=eave+arch+rise*.16*lobe*math.sin(math.pi*t)+.032*lobe*t*t
                    ring.append((x+rx*radius*math.sin(angle),y,z+rz*radius*math.cos(angle)))
                rings.append(ring)
            for j in range(len(fractions)-1):
                bead=self.lod==0 and j==2
                surface='roof' if bead and mat=='leaf' else mat
                self.mesh.tri(crown,rings[0][j],rings[0][j+1],self.color,surface)
                for a,b in zip(rings,rings[1:]):
                    self.mesh.quad(a[j],b[j],b[j+1],a[j+1],self.color,surface)
                a,b=rings[-1][j:j+2]
                la,lb=(a[0],a[1]-.031,a[2]),(b[0],b[1]-.031,b[2])
                self.mesh.quad(a,la,lb,b,self.color,mat)
                self.mesh.tri(soffit,lb,la,self.color,skin)

    def ogive(self, width, height, z, material):
        # A flat, pointed lancet. Ordered from the door's outside (-Z).
        w = width * .5
        path = [(-w,0),(-w,.62*height),(-.70*w,.82*height),(0,height),
                (.70*w,.82*height),(w,.62*height),(w,0)]
        self.mesh.poly([(x,y,z) for x,y in path], self.color, material)

    def facade(self, rx, rz, cz, floor, eave, window, entrance=True):
        h = eave-floor
        # The spiral roots leave the approach open on the negative-Z face.
        wall_z = cz-rz*math.cos(math.pi/self.n)
        with self.mesh.transform(translate=(0,floor,wall_z)):
            if entrance:
                self.ogive(.43, min(.64,h*.98), -.016, 'hide' if self.age=='dawn' else self.moon)
                self.ogive(.34, min(.55,h*.84), -.025, 'door')
                if self.lod == 0:
                    self.ogive(.016,min(.50,h*.76),-.031,'trim')
                    for side in (-1,1):
                        self.pod(side*.036,h*.32,-.034,.014,.020,'gold')
        if self.lod == 2:
            return
        for i in range(1,self.n):
            angle = math.tau*i/self.n
            # Leave breathing room around the main gate and use alternating bays.
            if i in (1,self.n-1) or i % 2 == 0:
                continue
            dx, dz = math.sin(angle), -math.cos(angle)
            face = math.cos(math.pi/self.n)
            normal = math.degrees(math.atan2(-dx/rx,-dz/rz))
            with self.mesh.transform(translate=(rx*dx*face, floor+h*.20, cz+rz*dz*face),rotate_y=normal):
                self.ogive(min(.20,rx*.34),h*.65,-.013,self.moon if self.age!='dawn' else 'trim')
                self.ogive(min(.145,rx*.25),h*.48,-.022,window)

    def trunk(self, x, z, h, reach, angle, fork=True):
        with self.mesh.transform(translate=(x,0,z), rotate_y=angle):
            path = [(0,.073,0,.110),(.018,.23*h,.035,.094),(.035,.53*h,.026,.073),
                    (.012,.76*h,-reach*.25,.065),(-.01,h,-reach,.046)]
            self.link(path if self.lod < 2 else [path[0],path[2],path[-1]],self.bark)
            if self.lod <= 1 and fork:
                self.link([path[2],(-.09,h*.72,.008,.05),(-.15,h*.93,-reach*.58,.032)],self.bark)
            if self.lod == 0:
                self.link([(0,.20,0,.076),(.072,.11,.055,.055),(.18,.066,.09,.026)],self.bark)

    def step(self, z, front, back, width, y, height, material):
        # Rounded nosing across a broad, straight entry flight.
        plan=[(-width,back),(width,back),(width,z),(width*.91,front+.044),
              (width*.71,front),(-width*.71,front),(-width*.91,front+.044),(-width,z)]
        lower=[(x,y,k) for x,k in plan]
        upper=[(x,y+height,k) for x,k in plan]
        self.mesh.poly(upper,self.color,material)
        for i in range(len(lower)):
            j=(i+1)%len(lower)
            self.mesh.quad(lower[j],upper[j],upper[i],lower[i],self.color,material)

    def civic(self, rx, rz, eave, skin, footing, window='emissive', cz=.035, roots=True):
        # Street-level entrance steps overlap the foundation. The large upper
        # platform is simultaneously a public floor and the base of the shell.
        floor = .18
        for k in range(3):
            front = -.924+.106*k
            self.step(front+.073,front,-.35,.307-.018*k,k*.062,.062,footing)
        self.drum(0,cz,0,rx+.092,rz+.092,.154,footing,taper=.96)
        self.drum(0,cz,.143,rx+.088,rz+.088,.05,footing)
        self.drum(0,cz,.185,rx,rz,eave-.185,skin)
        self.facade(rx,rz,cz,floor,eave,window)
        if roots:
            for angle in (-70,65,158,220):
                a=math.radians(angle)
                self.trunk(math.sin(a)*(rx+.028),cz+math.cos(a)*(rz+.035),
                           eave+.035,.14,angle, fork=self.lod==0 or angle>0)
        if self.lod < 2:
            self.lamps(rx,rz,eave,cz)
        if self.lod == 0:
            # The five corners of the council pennon represent its canopy houses.
            # Its two corners are pegged into the lip of the chamber wall.
            angle=math.radians(62)
            face=math.cos(math.pi/self.n)
            with self.mesh.transform(translate=(rx*math.sin(angle)*face,eave-.09,
                                                cz-rz*math.cos(angle)*face),rotate_y=-62):
                path=[(-.077,0),(.077,0),(.069,-.18),(-.002,-.25),(-.066,-.18)]
                self.mesh.poly([(x,y,-.040) for x,y in path],self.color,'banner')
                self.mesh.poly([(-.023,-.065,-.046),(0,-.031,-.046),(.023,-.065,-.046),
                                (0,-.135,-.046)],self.color,'ivory')
                for x in (-.061,.061):
                    self.pod(x,-.011,-.033,.013,.013,'gold')

    def lamps(self, rx, rz, eave, cz):
        for side in (-1,1):
            x,z = side*(rx*.62), cz-rz*.81
            y = min(.69, eave)
            self.link([(x,y*.50,z,.033),(x+side*.037,y-.04,z-.053,.028),
                       (x+side*.068,y,z-.098,.017),(x+side*.088,y-.047,z-.13,.013)],'trim',4)
            self.pod(x+side*.088,y-.112,z-.13,.051,.081,self.light)

    def bud(self, x, z, y, r, h, skin='leaf'):
        self.pod(x,y+h*.46,z,r,h*.48,skin)
        if self.lod < 2:
            self.drum(x,z,y+h*.19,r*.83,r*.83,h*.09,'gold',n=7)

    def bough(self, x, z, floor, height, size, angle):
        """A living graft branching off a load-bearing pier beyond the roof."""
        with self.mesh.transform(translate=(x,floor,z),rotate_y=angle):
            self.link([(0,0,0,.077),(-.024,height*.40,.065,.058),(.01,height*.79,.16,.04),
                       (.055,height,.20,.025)],self.bark)
            leaves=[(-70,.75,-17),(15,1,-6),(71,.83,-31)]
            if self.lod == 2:
                leaves=leaves[::2]
            for yaw,scale,tilt in leaves:
                with self.mesh.transform(translate=(.052,height-.018,.199),rotate_y=yaw,
                                         rotate_x=tilt,scale=scale):
                    self.leaf('leaf','roof',size*.41,size,0,.039,yaw*.0004)

    def annex(self,x,z,r,eave,rise,wall,roof):
        self.drum(x,z,0,r*.91,r*.94,.15,'fieldstone')
        self.drum(x,z,.145,r*.82,r*.85,eave-.14,wall)
        if self.lod < 2:
            for yaw in (-82,-25,48):
                with self.mesh.transform(translate=(x,.24,z), rotate_y=yaw):
                    self.ogive(r*.55,(eave-.24)*.82,-r*.834,'trim')
                    self.ogive(r*.39,(eave-.24)*.66,-r*.858,'emissive')
        self.canopy(x,z,eave,r,r,rise,5,roof,'trim',twist=17)


def dawn(g):
    g.civic(.68,.44,.65,'hide','fieldstone',window='door')
    g.joined_canopy(0,.038,.67,.98,.68,.46,7,'thatch',(1,2,6),3)
    g.bough(-.30,.20,.46,.76,.32,-43)
    # The branches of the two gate trees carry the assembly's seed standard.
    # Its broad green field is visible from both approaches to the grove.
    for side in (-1,1):
        g.link([(side*.36,.075,-.45,.083),(side*.40,.55,-.49,.080),
                (side*.35,1.05,-.50,.069),(side*.235,1.51,-.50,.048)],'timber')
    g.link([(-.266,1.48,-.50,.035),(-.04,1.53,-.50,.033),(.266,1.48,-.50,.035)],'timber')
    border=[(-.218,1.494),(.216,1.494),(.215,1.276),(.025,1.153),(-.198,1.256)]
    front=[(x,y,-.533) for x,y in border]
    rear=[(x,y,-.480) for x,y in border]
    g.mesh.poly(front,g.color,'banner')
    g.mesh.poly(list(reversed(rear)),g.color,'banner')
    for i in range(len(front)):
        j=(i+1)%len(front)
        g.mesh.quad(front[j],front[i],rear[i],rear[j],g.color,'grass')
    for z in (-.538,-.475):
        mark=[(-.065,1.380,z),(.012,1.440,z),(.066,1.373,z),(.008,1.275,z)]
        g.mesh.poly(mark if z<-.50 else list(reversed(mark)),g.color,'ivory')


def bronze(g):
    g.civic(.63,.54,.74,'wall','brick')
    g.canopy(0,.035,.75,.86,.74,.28,8,'leaf','gold',twist=7)
    # A second public chamber grew on the rear half of the clay vault.
    g.drum(.23,.18,.77,.33,.31,.53,'wall',taper=.94)
    g.drum(.23,.18,1.23,.33,.31,.062,'gold')
    g.canopy(.23,.18,1.30,.47,.44,.32,6,'roof','gold',twist=19,grafts=(3,4))
    g.bud(.23,.18,1.59,.061,.16,'gold')


def iron(g):
    g.civic(.58,.53,.86,'fieldstone','fieldstone',window='door')
    g.canopy(-.035,.04,.855,.80,.77,.68,7,'leaf','metal',twist=15)
    g.annex(-.60,.09,.34,.71,.36,'fieldstone','leaf')
    g.annex(.59,-.17,.36,.85,.44,'fieldstone','slate')
    g.bough(-.15,.28,.97,.59,.24,-32)


def classical(g):
    g.civic(.48,.40,.86,'wall','fieldstone',window='glass',roots=False)
    g.drum(0,.035,.012,.76,.64,.101,'fieldstone')
    for a in (-53,53,104,-104,157,-157):
        ang=math.radians(a)
        x,z=.63*math.sin(ang),.035-.52*math.cos(ang)
        if g.lod == 2:
            g.drum(x,z,.097,.075,.075,.80,'ivory',n=5)
        else:
            g.drum(x,z,.097,.095,.095,.097,'ivory',n=(8,6)[g.lod])
            g.drum(x,z,.187,.074,.074,.60,'ivory',taper=.85,n=(8,6)[g.lod])
            g.drum(x,z,.777,.082,.082,.12,'ivory',taper=1.22,n=(8,6)[g.lod])
    g.drum(0,.035,.866,.76,.64,.115,'ivory')
    g.canopy(0,.035,.955,.86,.75,.275,9,'leaf','gold',twist=15)
    for x,ang in ((-.42,-47),(.42,48)):
        g.bough(x,.29,.76,.60,.26,ang)
    g.bud(0,.035,1.19,.072,.13,'gold')


def medieval(g):
    g.civic(.55,.51,.94,'wall','fieldstone')
    g.canopy(0,.035,.947,.80,.74,.40,7,'slate','leaf',twist=10)
    # A slim, many-voiced belfry sits inside the crown of the lower slate roof.
    g.drum(-.12,.11,1.14,.27,.25,.50,'wall')
    if g.lod < 2:
        for a in (0,90,180,270):
            with g.mesh.transform(translate=(-.12,1.36,.11),rotate_y=a):
                g.ogive(.145,.235,-.268,'door')
    g.canopy(-.12,.11,1.63,.405,.39,.58,6,'leaf','slate',twist=23)
    g.bud(-.12,.11,2.17,.04,.14,'gold')
    g.annex(.60,-.10,.35,.72,.35,'fieldstone','slate')


def renaissance(g):
    g.civic(.63,.54,.88,'plaster','fieldstone')
    g.drum(0,.034,.841,.68,.595,.10,'ivory')
    with g.mesh.transform(translate=(0,.916,.035),scale=(.70,.57,.60)):
        g.mesh.dome((0,0,0),1,g.color,rings=(5,3,2)[g.lod],segments=g.n,material='roof_tile')
    g.drum(0,.035,.913,.721,.623,.046,'gold',taper=.995)
    g.drum(0,.035,1.43,.14,.14,.18,'ivory',n=8)
    g.canopy(0,.035,1.60,.25,.24,.16,5,'leaf','gold',twist=10)
    g.annex(.59,-.11,.37,.67,.24,'plaster','leaf')
    g.bough(.48,.20,.70,.51,.28,21)


def industrial(g):
    g.civic(.58,.50,.77,'brick','fieldstone',window='glass')
    g.joined_canopy(0,.035,.78,.83,.68,.32,6,'metal',(1,),17)
    # Chimneys are rooted in the connected boiler wing. Cast cuffs bind each shaft.
    g.drum(.48,.23,0,.35,.31,.80,'brick')
    g.drum(.48,.23,.77,.36,.32,.075,'metal')
    for x,z,height,r in ((.54,.24,1.58,.126),(.28,.42,1.29,.111)):
        g.drum(x,z,.57,r,r,height-.57,'brick',taper=.76,n=(10,8,6)[g.lod])
        g.drum(x,z,height-.07,r*.91,r*.91,.09,'metal',n=(10,8,6)[g.lod])
        g.drum(x,z,height+.015,r*.66,r*.66,.009,'door',n=(10,8,6)[g.lod])
        if g.lod == 0:
            for y in (.91,1.12):
                g.drum(x,z,y,r*.93,r*.93,.033,'metal',n=10)
    g.bough(-.42,-.03,.61,.55,.31,-62)


def modern(g):
    g.civic(.61,.52,.78,'glass','fieldstone',window='glass')
    for y in ((.31,.64) if g.lod<2 else (.64,)):
        g.drum(0,.035,y,.632,.54,.047,'wall')
    g.drum(.43,.08,0,.33,.30,.28,'wall',taper=.91)
    g.drum(.43,.08,.27,.45,.40,.38,'glass')
    g.drum(.43,.08,.64,.47,.43,.071,'ivory')
    g.canopy(0,.03,.798,.82,.67,.15,5,'ivory','metal',twist=35)
    # A sweeping council shell rises above the long glazed terrace.
    with g.mesh.transform(translate=(-.44,.81,.10),rotate_y=84):
        # The front steel wishbone straddles the high shell at the chamber's
        # glass terrace. Its fork actually meets the underside of the green sail.
        joint=(.20,.27,.70,.104)
        g.link([(.27,-.12,.58,.145),(.24,.12,.63,.119),joint,
                (.17,.48,.88,.081),(.10,.69,1.03,.061)],'metal')
        g.link([joint,(.15,.41,.52,.077),(.09,.55,.49,.059)],'metal')
        with g.mesh.transform(translate=(0,.03,.02),rotate_x=-32):
            g.leaf('leaf','ivory',.49,1.41,.02,.21,.08)


def information(g):
    g.civic(.51,.46,.72,'wall','plaster',window='glass')
    g.canopy(0,.03,.73,.75,.65,.21,6,'leaf','ivory',twist=10)
    tiers=[(-.075,.085,.86,.39,.31,.49),(-.105,.095,1.345,.315,.245,.44),
           (-.09,.10,1.78,.195,.18,.27)]
    for x,z,y,rx,rz,h in tiers:
        g.drum(x,z,y,rx,rz,h,'glass',taper=.92)
        g.drum(x,z,y+h-.06,rx*.956,rz*.956,.066,'ivory')
    g.drum(-.105,.095,1.695,.35,.285,.064,'metal',taper=1.09)
    g.drum(-.105,.095,1.753,.38,.308,.039,'turquoise')
    for side in (-1,1):
        g.link([(side*.52,.15,.23,.070),(side*.45,.68,.23,.066),
                (side*.32,1.19,.18,.053),(side*.15-.06,1.76,.14,.038),
                (side*.075-.09,2.15,.10,.018)],'ivory')
    g.bud(-.09,.10,2.00,.093,.24,'ascension')
    g.link([(-.09,2.15,.10,.022),(-.067,2.34,.103,.010)],'metal',5)


def ascension(g):
    g.civic(.53,.48,.80,'ivory','wall',window='ascension')
    g.canopy(0,.035,.815,.79,.68,.36,7,'ivory','gold',twist=17,grafts=(1,5))
    # A ring hangs on three living, load-bearing arms above a luminous seed.
    for degrees in (38,157,275):
        a=math.radians(degrees)
        def polar(radius, y, width):
            return (math.sin(a)*radius,y,.06+math.cos(a)*radius,width)
        g.link([polar(.46,.29,.115),polar(.355,.89,.10),polar(.23,1.23,.083),
                polar(.30,1.46,.070),polar(.54,1.708+.115*math.sin(a),.043)],'gold')
    n=(24,16,10)[g.lod]
    for i in range(n):
        a,b=math.tau*i/n,math.tau*(i+1)/n
        def hoop(t,r,y):
            return (r*math.cos(t),y+.115*math.cos(t),.06+r*math.sin(t))
        p,q=hoop(a,.567,1.68),hoop(b,.567,1.68)
        s,t=hoop(a,.567,1.77),hoop(b,.567,1.77)
        u,v=hoop(a,.486,1.68),hoop(b,.486,1.68)
        w,k=hoop(a,.486,1.77),hoop(b,.486,1.77)
        for face,material in (([q,p,s,t],'gold'),([t,s,w,k],'ivory'),
                              ([u,v,k,w],'gold'),([p,q,v,u],'gold')):
            g.mesh.quad(*face,g.color,material)
    g.drum(0,.06,.98,.158,.144,.55,'ivory',taper=.57)
    g.bud(0,.06,1.46,.145,.48,'ascension')
    for i in (0,120,240):
        with g.mesh.transform(translate=(0,1.09,.06),rotate_y=i):
            with g.mesh.transform(rotate_x=-61):
                g.leaf('leaf','gold',.15,.65,.015,.09)


BUILDERS = (dawn, bronze, iron, classical, medieval, renaissance, industrial, modern, information, ascension)


def build(model, lod):
    grove = Grove(model, lod)
    w, d = model.footprint
    with grove.mesh.transform(scale=(w/2,1,d/2)):
        BUILDERS[style.AGES.index(grove.age)](grove)
    return grove.mesh


if __name__ == '__main__':
    for model in package_models('elf_towncenters'):
        lod0, lod1, lod2 = (build(model, level) for level in range(3))
        result = publish(lod0,model.metadata(),model.package,lods=(lod1,lod2),preview_size=240)
        print(result['id'],result['lod_triangles'],flush=True)
