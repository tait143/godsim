"""Elven halls of learning and healing. Builder session 8."""
from math import cos, sin, pi
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish
from generators.buildings.elf_institution_forms import deck, drum, open_book, pod, stair, vault

WHITE = (1., 1., 1.)
TAU = 2*pi


class Grove(MeshBuilder):
    """A vocabulary of grown shells, folded leaves and bent silverwood ribs."""
    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.n = (14, 10, 6)[lod]
        self.era = ("ancient", "classical", "industrial", "future").index(spec.band)
        self.bark = ("timber", "trim", "metal", "ivory")[self.era]
        self.masonry = ("fieldstone", "wall", "brick", "wall")[self.era]
        self.light = "ascension" if self.era == 3 else "emissive"

    def lathe(self, x, z, rx, rz, rows, n=None, closed=True, bottom=False):
        # Rows give absolute height, radius multiplier and surface below the row.
        n = n or self.n
        rings = []
        for y, scale, role in rows:
            ring = [(x+rx*scale*cos(a), y, z+rz*scale*sin(a))
                    for a in (TAU*(i+.5)/n-pi/2 for i in range(n))]
            if rings:
                last = rings[-1]
                for i in range(n):
                    j = (i+1)%n
                    self.quad(last[j], last[i], ring[i], ring[j], WHITE, role)
            rings.append(ring)
        if closed:
            self.poly(rings[-1][::-1], WHITE, rows[-1][-1])
        if bottom:
            self.poly(rings[0], WHITE, rows[0][-1])

    def cargo(self, x, y, z, size, wrap="timber"):
        w,h,d=size
        self.box((x,y+.5*h,z),size,WHITE,material=wrap)
        if self.lod<2:
            for sign in (-1,1):
                self.box((x+sign*w*.30,y+h+.009,z),(.034,.018,d*1.02),WHITE,material="trim")
                self.box((x+sign*w*.30,y+h*.49,z-d*.50),(.034,h*.95,.020),WHITE,material="trim")

    def stem(self, points, radii, role, sides=None, cap=True):
        """Sweep a continuous rib, sharing the mitres at each bend."""
        sides = sides or (7, 5, 4)[self.lod]
        path = np.asarray(points, dtype=float)
        axis = path[-1]-path[0]
        ref = np.eye(3)[np.argmin(np.abs(axis))]
        rings = []
        for i, (pt, r) in enumerate(zip(path, radii)):
            tangent = path[min(i+1, len(path)-1)] - path[max(0, i-1)]
            tangent /= np.linalg.norm(tangent)
            u = np.cross(tangent, ref)
            if np.linalg.norm(u)<.001:
                u = np.cross(tangent, np.roll(ref,1))
            u /= np.linalg.norm(u)
            v = np.cross(tangent,u)
            rings.append([pt+r*(u*cos(TAU*j/sides)+v*sin(TAU*j/sides)) for j in range(sides)])
        for a,b in zip(rings,rings[1:]):
            for i in range(sides):
                j=(i+1)%sides
                # An elbow is nonplanar. Project its two facets independently.
                self.tri(a[i],a[j],b[j],WHITE,role)
                self.tri(a[i],b[j],b[i],WHITE,role)
        if cap:
            self.poly(rings[0][::-1],WHITE,role)
            self.poly(rings[-1],WHITE,role)

    def bar(self,a,b,r,role,end=None):
        self.stem((a,b),(r,r if end is None else end),role)

    def leaf(self, base, tip, width, role="leaf"):
        # Lanceolate, with a raised central vein and folded reverse.
        a,b=np.asarray(base),np.asarray(tip)
        d=b-a
        perp=np.array([-d[2],0,d[0]])
        if np.linalg.norm(perp)<.001:
            perp=np.array([1.,0,0])
        perp=perp/np.linalg.norm(perp)*width
        c=a+.47*d
        ridge=c+np.array([0,.40*width,0])
        underside=c-np.array([0,.24*width,0])
        ring=[a,c+perp,b,c-perp]
        if np.cross(ring[1]-a,b-a)[1]<0:
            ring.reverse()
        for i in range(4):
            j=(i+1)%4
            self.tri(ring[i],ring[j],ridge,WHITE,role)
            self.tri(ring[j],ring[i],underside,WHITE,role)

    def hood(self,x,y,z,rx,rz,h,role="roof",small=False):
        """Scalloped cornice gathers into an off-axis rolled leaf tip."""
        n=((12,8,6) if small else (20,12,8))[self.lod]
        sweep=(.23,-.19,.08,.29)[self.era]
        rows=[(1,0),(.76,.33),(.38,.78)] if self.lod==0 else [(1,0),(.55,.60)]
        if self.lod==2:
            rows=[(1,0)]
        rings=[]
        for r,t in rows:
            ring=[]
            for i in range(n):
                a=TAU*i/n-pi/2
                flute=(1+cos(4*(a+pi/2)))*.5
                radius=r*(.86+.14*flute)
                ring.append((x+rx*(radius*cos(a)+sweep*t*t),y+h*(t+.115*(1-t)*(1-flute)),
                             z+rz*(radius*sin(a)+.12*t*t)))
            rings.append(ring)
        tip=(x+rx*sweep,y+h,z+rz*.12)
        for i in range(n):
            j=(i+1)%n
            role_i = "leaf" if role=="roof" and i//(n//4)==2 else role
            p,q=rings[0][i],rings[0][j]
            s,t=(p[0],p[1]-.038,p[2]),(q[0],q[1]-.038,q[2])
            self.quad(t,s,p,q,WHITE,"accent")
            self.tri(s,t,(x,y-.03,z),WHITE,self.bark)
            for lower,upper in zip(rings,rings[1:]):
                self.tri(lower[j],lower[i],upper[i],WHITE,role_i)
                self.tri(lower[j],upper[i],upper[j],WHITE,role_i)
            self.tri(rings[-1][j],rings[-1][i],tip,WHITE,role_i)

    def portal(self,x,y,z,w,h,role="door"):
        # A thick pointed surround whose back projects into the curved facade.
        outline=[(-.5,0),(-.5,.51),(-.34,.79),(0,1),(.34,.79),(.5,.51),(.5,0)]
        if self.lod==2:
            outline=[outline[i] for i in (0,1,3,5,6)]
        outer=[(x+a*w,y+b*h,z) for a,b in outline]
        inner=[(x+a*w*.72,y+b*h*.83,z) for a,b in outline]
        for i in range(len(outer)-1):
            p,q=outer[i:i+2]
            self.quad(q,p,(p[0],p[1],z+.102),(q[0],q[1],z+.102),WHITE,self.bark)
            self.quad(p,q,inner[i+1],inner[i],WHITE,"accent")
        self.poly(inner,WHITE,role)

    def lamp(self,x,y,z,s):
        self.lathe(x,z,s,s,[(y,.43,self.bark),(y+.6*s,.78,self.bark),
                             (y+1.6*s,.46,self.light),(y+2*s,.15,"accent")],n=(7,5,4)[self.lod])

    def hall(self,x,z,rx,rz,h,rise,*,door=True,roof="roof",small=False):
        e=self.era
        h*=(.89,1,.94,1.09)[e]
        n=(10,8,6)[self.lod] if small else self.n
        rows=[(0,1.,self.masonry),(.10,1.015,self.masonry),(.15,.91,"accent")]
        if self.lod==2:
            rows=[(0,.96,self.masonry)]
        wall=("wall","wall","brick","glass")[e]
        rows.append((h*.73,.90,wall))
        if self.lod<2:
            rows.extend([(h*.84,.94,"glass" if e>=2 else "wall"),(h*.89,.965,"trim")])
        rows.append((h,.97,wall))
        self.lathe(x,z,rx,rz,rows,n=n,closed=False)
        self.hood(x,h,z,rx*1.12,rz*1.14,rise*(.90,.99,.78,1.18)[e],roof,small)
        if door:
            self.portal(x,0,z-rz*.97-.014,min(.36,rx*.87),min(.53,h*.77))
        if self.lod!=2:
            for a in ((-.64,.67) if small else (-.64,.67,2.0,3.74)):
                c,s=cos(a),sin(a)
                self.stem([(x+rx*.995*c,.047,z+rz*.995*s),
                           (x+rx*.93*c,h*.53,z+rz*.93*s),
                           (x+rx*1.02*c,h+.014,z+rz*1.02*s)],
                          [.039,.024,.035],self.bark,sides=(5,4)[self.lod])
        if door and self.lod==0:
            self.lamp(x+rx*.48,.034,z-rz*.90,.055)

    def tulip(self,x,y,z,r,h,role="flower"):
        self.lathe(x,z,r,r,[(y,.32,self.bark),(y+h*.4,.37,self.bark),
                             (y+h*.73,1,role),(y+h,.74,role)],n=(7,5,4)[self.lod])

    def basin(self,x,y,z,rx,rz,h,water="water"):
        self.lathe(x,z,rx,rz,[(y,.77,self.masonry),(y+h*.74,1,self.masonry),
                              (y+h,1,"accent"),(y+h*.98,.87,"ivory"),
                              (y+h*.74,.83,water)])

    def hoop(self,x,y,z,rx,ry,thickness,role,rotation=0):
        # Continuous hollow elliptical ring in a vertical plane.
        n=(18,12,8)[self.lod]
        with self.transform(translate=(x,y,z),rotate_y=rotation):
            outer=[(rx*cos(TAU*i/n),ry*sin(TAU*i/n)) for i in range(n)]
            inner=[((rx-thickness)*cos(TAU*i/n),(ry-thickness)*sin(TAU*i/n)) for i in range(n)]
            for i in range(n):
                j=(i+1)%n
                a,b=outer[i],outer[j]
                c,d=inner[j],inner[i]
                depth=thickness*.48
                front=[(*p,-depth) for p in (a,b,c,d)]
                back=[(*p,depth) for p in (a,b,c,d)]
                self.quad(*front[::-1],WHITE,role)
                self.quad(*back,WHITE,role)
                self.quad(front[0],front[1],back[1],back[0],WHITE,role)
                self.quad(front[2],front[3],back[3],back[2],WHITE,role)


def terrace(m,x,z,rx,rz,start,end,levels):
    """One continuous, solid bank of raked seating, open toward the entrance."""
    n=(20,12,6)[m.lod]
    profiles=[(.55,.075)]
    for i in range(levels):
        r=.55+.42*i/levels
        t=(i+1)/levels
        profiles.extend([(r,.075+.40*t),(r+.42/levels,.075+.40*t)])
    profiles.extend([(1,.49),(1.035,.51),(1.035,.43),(.97,0),(.55,0)])
    angles=[start+(end-start)*i/n for i in range(n+1)]
    for j,(a,b) in enumerate(zip(profiles,profiles[1:])):
        ra,ya=a
        rb,yb=b
        role=("wall" if ya==yb else m.masonry) if j<2*levels else ("accent" if j==2*levels else m.bark)
        for aa,ab in zip(angles,angles[1:]):
            def point(r,y,ang):
                return (x+rx*r*cos(ang),y,z+rz*r*sin(ang))
            m.quad(point(ra,ya,aa),point(ra,ya,ab),point(rb,yb,ab),point(rb,yb,aa),WHITE,role)
    # End cheeks are populated strip by strip down to a shallow foundation.
    for k,ang in enumerate((start,end)):
        for (ra,ya),(rb,yb) in zip(profiles[:2*levels+1],profiles[1:2*levels+2]):
            if rb-ra<.00001 or min(ya,yb)<=0:
                continue
            v=[(x+rx*ra*cos(ang),0,z+rz*ra*sin(ang)),(x+rx*rb*cos(ang),0,z+rz*rb*sin(ang)),
               (x+rx*rb*cos(ang),yb,z+rz*rb*sin(ang)),(x+rx*ra*cos(ang),ya,z+rz*ra*sin(ang))]
            m.quad(*(v[::-1] if k==0 else v),WHITE,m.masonry)


def quill(m):
    # A tapered vein supports each notched vane. The broad faces look toward the approach.
    with m.transform(translate=(.53,.23,-.39),rotate_y=-25,rotate_z=-19):
        m.bar((0,0,0),(0,1.06,0),.027,"accent",.012)
        knots=[(.25,.018,.025),(.44,.16,.12),(.51,.094,.14),(.57,.18,.16),
               (.72,.17,.15),(.77,.12,.105),(.81,.14,.13),(.94,.084,.09),(1.08,.006,.006)]
        if m.lod==2:
            knots=knots[::2]
        for i,(a,b) in enumerate(zip(knots,knots[1:])):
            for side in (-1,1):
                left=(side*a[1 if side<0 else 2],a[0],.012)
                right=(side*b[1 if side<0 else 2],b[0],.012)
                for z,reverse in ((-.032,False),(.032,True)):
                    face=[(0,a[0],z),left,right,(0,b[0],z)]
                    m.quad(*(face[::-1] if (side>0)!=reverse else face),WHITE,
                           "ivory" if side<0 or i%3 else "accent")


def healing_plaque(m):
    m.lathe(0,0,.137,.043,[(-.17,.64,"trim"),(-.09,1.07,"leaf"),(.17,1.05,"leaf"),
                            (.23,.42,"leaf")],n=(8,6,4)[m.lod])
    m.box((0,.04,-.050),(.064,.226,.028),WHITE,material="ivory")
    for x in (-.066,.066):
        m.box((x,.04,-.050),(.068,.065,.028),WHITE,material="ivory")


def receiving_porch(m,high):
    # A pale apothecary cross is relief on a leaf-green plaque over the ambulance porch.
    for x in (-.33,.33):
        m.stem([(x,.022,-.64),(x,.39,-.62),(x*.86,.61,-.58)],
               [.049,.031,.044],m.bark,sides=(6,5,4)[m.lod])
    m.hood(0,.60,-.47,.42,.29,.16,"glass" if high else "roof",small=True)
    with m.transform(translate=(0,.80,-.62),rotate_y=-18):
        if high:
            m.hoop(0,.08,0,.236,.243,.047,"ivory")
        healing_plaque(m)


def healing(m,kind):
    if kind=="Herbalist":
        if m.era:
            if m.era==1:
                # A cloister garden wraps a broad, raised circular medicinal bed.
                m.hall(-.34,.38,.34,.29,.52,.31,small=True)
                m.basin(.18,0,-.18,.57,.47,.22,"soil")
                for x,z in ((.18,-.36),(.42,-.12),(-.06,-.10))[:(3,3,1)[m.lod]]:
                    m.tulip(x,.202,z,.105,.31,"flower")
                for x in (-.41,.60):
                    m.stem([(x,.024,.11),(x,.48,.17),(x*.69,.74,.16)],
                           [.077,.050,.041],m.bark)
                m.hood(.10,.73,.17,.53,.22,.19,"roof",small=True)
            elif m.era==2:
                # A drying loft and a pair of distillation vessels supply the apothecary.
                m.hall(-.40,.26,.32,.40,.83,.235,small=True)
                for x,z,r,h in ((.28,-.21,.31,.81),(.62,.30,.21,.49)):
                    m.lathe(x,z,r,r,[(0,.76,"wall"),(.13,1,"metal"),(.40,.89,"metal"),
                            (h,.25,"trim"),(h+.13,.20,"metal")])
                m.stem([(.28,.91,-.21),(.37,1.01,-.18),(.60,.85,.21),(.62,.59,.30)],
                       [.043,.042,.035,.034],"trim")
                if m.lod<2:
                    m.basin(-.37,0,-.39,.23,.19,.13,"soil")
                    m.tulip(-.37,.113,-.39,.12,.27,"leaf")
            else:
                # Grow shelves spiral around the glazed conservatory trunk above the dispensary.
                m.hall(-.34,-.24,.37,.32,.42,.21,small=True)
                x,z=.20,.18
                m.lathe(x,z,.32,.30,[(0,1,"wall"),(.18,.83,"ivory"),(1.04,.68,"glass"),
                                      (1.18,1.02,"roof"),(1.22,.83,"glass")])
                m.tulip(x,1.19,z,.245,.37,"flower")
                for j in range(3):
                    if j==1 and m.lod==2:
                        continue
                    a=j*TAU/3
                    u,v=x+.31*cos(a),z+.31*sin(a)
                    y=.24+.28*j
                    m.stem([(x,y-.10,z),(u,y+.04,v)],[.082,.061],m.bark)
                    m.basin(u,y,v,.27,.22,.10,"soil")
                    m.tulip(u,y+.083,v,.096,.18,"leaf")
            return
        m.hall(-.16,.14,.52,.52,.66,.54)
        beds=[(.39,-.45,.29),(.64,.07,.22)]
        for i,(x,z,r) in enumerate(beds):
            m.basin(x,0,z,r,r*.82,.14,"soil")
            for j in range(5 if m.lod<2 else 3):
                a=TAU*j/5+i
                m.leaf((x,.105,z),(x+r*.88*cos(a),.26+.07*(j%2),z+r*.88*sin(a)),r*.26)
            m.tulip(x,.13,z,.090,.31,"flower" if i==0 else "ivory")
    elif kind=="Bathhouse":
        m.hall(-.38,.16,.43,.46,.62,.44)
        m.basin(.23,0,-.41,.53,.38,.23)
        m.basin(.49,0,.27,.32,.27,.35)
        m.stem([(.56,.07,.02),(.56,.48,.05),(.48,.58,-.08),(.40,.53,-.17)],
               [.055,.043,.033,.030],m.bark)
    elif kind=="MedicalCenter":
        # Two leaf-shaped ward towers rise above a sheltered, through-running triage court.
        for x,z,h in ((-.46,.22,1.18),(.46,.30,.89)):
            m.lathe(x,z,.31,.51,[(0,.99,"wall"),(.14,1,"wall"),(.44,.83,"ivory"),
                    (h-.17,.75,"glass"),(h,.90,"roof")])
            m.tulip(x,h-.015,z,.296,.337,"roof")
        m.lathe(0,-.032,.58,.27,[(.511,.94,"ivory"),(.57,1,"roof"),(.768,.94,"glass"),
                                   (.789,1,"ivory")])
        m.hall(0,-.30,.31,.33,.404,.151,small=True)
        for s in (-1,1):
            m.stem([(s*.41,.01,-.53),(s*.42,.301,-.48),(s*.28,.54,-.19)],
                   [.054,.044,.039],m.bark)
        m.lathe(0,-.30,.52,.37,[(.501,.79,"wall"),(.543,1,"ivory"),(.578,.94,"roof")])
        with m.transform(translate=(0,.777,-.508),rotate_y=-20,scale=(1.65,1.45,1.40)):
            healing_plaque(m)
        if m.lod<2:
            for x in (-.605,.615):
                m.tulip(x,.17,-.21,.074,.19,"emissive")
        if m.lod==0:
            m.basin(-.59,0,-.64,.159,.122,.097,"leaf")
            m.tulip(-.59,.095,-.64,.075,.13,"flower")
    else:
        # A low pair of patient wards flanks the tall clerestory.
        m.hall(-.02,.21,.40,.50,.83,.55,roof="roof")
        for x,z,h in [(-.54,-.03,.43),(.52,-.13,.54)]:
            m.hall(x,z,.28,.54,h,.28,door=False,small=True)
        m.portal(.52,0,-.67,.24,.34,"glass")
        receiving_porch(m,False)


def written_arts(m,kind):
    if kind=="Library":
        m.hall(-.08,.17,.65,.51,.91,.46)
        # Stacks of volumes in a sheltered public lending niche.
        m.lathe(.36,-.46,.38,.23,[(0,.91,"trim"),(.14,1,"wall"),(.18,1,"accent")])
        for i,role in enumerate(("trim","banner","roof")):
            with m.transform(translate=(.36,.179+i*.068,-.47),rotate_y=(-8,8,-4)[i]):
                m.box((0,.028,0),(.45,.056,.22),WHITE,material="ivory")
                m.box((0,.061,0),(.48,.015,.24),WHITE,material=role)
    elif kind=="Scriptorium":
        m.hall(-.13,.14,.57,.55,.64,.43)
        # Quill and ink vessel by the scribe's arcaded workshop.
        m.lathe(.53,-.39,.19,.22,[(0,.90,"fieldstone"),(.18,1,"glass"),(.26,.53,"trim")])
        quill(m)
    else:
        m.hall(-.28,.22,.50,.48,.63,.42)
        # Screw press with a real lintel, squeeze platen and offset lever.
        x,z=.34,-.30
        m.lathe(x,z,.40,.35,[(0,.94,"fieldstone"),(.19,1,"wall"),(.24,1,"trim")])
        for dx in (-.25,.25):
            m.bar((x+dx,.18,z),(x+dx,.85,z),.070,m.bark)
        m.bar((x-.31,.81,z),(x+.31,.81,z),.077,m.bark)
        m.bar((x,.42,z),(x,1.04,z),.048,"metal")
        m.bar((x-.19,.97,z-.04),(x+.18,1.06,z+.04),.037,"accent")
        m.lathe(x,z,.24,.20,[(.39,.87,"trim"),(.46,1,"trim")])
        if m.lod<2:
            m.box((x,.259,z-.09),(.39,.034,.29),WHITE,material="ivory")


def lecture_campus(m):
    if m.era==2:
        # A vaulted assembly hall and two reading galleries frame the college green.
        vault(m,-.23,.32,.49,.37,.42,.43,roof="roof")
        for x,z in ((.54,.12),(-.54,-.38)):
            vault(m,x,z,.18,.31,.24,.19,roof="metal",ribs=False)
        deck(m,.08,0,-.41,.30,.245,.17,"timber")
        drum(m,.09,-.43,.13,.13,[(.17,1,"wall"),(.34,.70,"wall"),(.40,1,"trim")])
        open_book(m,.09,.40,-.43,.89)
        if m.lod==0:
            for x in (-.50,-.13):
                m.lamp(x,.02,-.08,.068)
        return
    # Classrooms radiate from the mast of a sky college. Its glazed bridge leads
    # to a shaded ground-level reading court and a broad, forward-facing entrance.
    drum(m,-.30,.19,.27,.26,[(0,1.03,"wall"),(.25,.80,"wall"),(1.09,.67,"glass"),
                             (1.27,1.08,"trim"),(1.43,.54,"ivory")])
    m.leaf((-.30,1.30,.18),(-.22,1.74,.32),.125,"accent")
    pod(m,.36,.13,.36,.31,.58,.43,role="glass")
    m.box((.015,.760,.17),(.42,.14,.19),WHITE,material="ivory")
    if m.lod<2:
        m.box((.015,.868,.17),(.40,.081,.15),WHITE,material="glass")
    vault(m,-.29,-.20,.276,.25,.30,.14,roof="ivory",ribs=False)
    deck(m,.27,0,-.46,.32,.25,.126,"roof")
    drum(m,.27,-.45,.125,.11,[(.126,.93,"wall"),(.31,.73,"ivory"),(.35,1,"accent")])
    open_book(m,.27,.35,-.46,.79)


def science(m,kind):
    if kind=="University":
        if m.era>=2:
            lecture_campus(m)
            return
        m.hall(-.40,.28,.36,.42,1.01,.61)
        # Half-round outdoor lecture court. The ranks face an open folio at the dais.
        with m.transform(translate=(.29,0,-.04),scale=(.77,.80,1.0)):
            terrace(m,0,0,.68,.65,-.57,3.87,(4,3,2)[m.lod])
            m.lathe(0,-.34,.235,.21,[(0,.90,"wall"),(.17,.82,"trim"),(.42,.62,"wall"),
                                    (.48,1,"accent")])
            for side in (-1,1):
                with m.transform(translate=(side*.104,.52,-.34),rotate_z=side*15,rotate_x=15):
                    m.box((0,0,0),(.219,.047,.26),WHITE,material="ivory",bottom=True)
    elif kind=="Observatory":
        m.lathe(-.08,.10,.65,.58,[(0,.97,"fieldstone"),(.17,1,"wall"),(.25,.87,"trim"),
                                  (.65,.84,m.masonry),(.73,1,"accent"),(.83,.96,"roof")])
        m.lathe(-.08,-.434,.222,.135,[(0,.96,m.masonry),(.42,.98,"wall"),
                                       (.53,.79,"accent"),(.56,.44,"wall")])
        m.portal(-.08,0,-.569,.33,.50)
        # Open armillary with a tilted polar axis and a solid central sighting globe.
        m.hoop(-.08,1.16,.10,.46,.45,.052,"accent",-22)
        m.hoop(-.08,1.16,.10,.40,.41,.046,"trim",62)
        m.bar((-.32,.80,.09),(.19,1.58,.11),.035,"metal")
        m.lathe(-.08,.10,.125,.125,[(1.02,.36,"accent"),(1.15,1,"glass"),(1.28,.36,"glass")])
        m.hall(.53,-.25,.25,.32,.37,.27,door=False)
    elif kind=="DataCenter":
        for x,z,h,r in [(-.42,.27,1.22,.34),(.35,.32,1.02,.34),(.04,-.35,.66,.38)]:
            rows=[(0,1,"wall"),(.14,.86,"wall")]
            for i in range(3):
                y=(h-.14)/3
                rows.extend([(.14+(i+.80)*y,.90,"glass"),(.14+(i+1)*y,.97,"ivory")])
            if m.lod==2:
                rows=[rows[0],rows[-3],rows[-1]]
            m.lathe(x,z,r,r,rows,closed=False)
            m.hood(x,h,z,r*1.05,r*1.04,.24)
        m.portal(.04,0,-.72,.29,.43)
        # Deep louvered cooling spine crosses the service stack on the camera-facing flank.
        with m.transform(translate=(.39,0,-.36),rotate_y=-67):
            m.box((0,.38,0),(.55,.71,.16),WHITE,material="metal",taper=.08)
            m.box((0,.39,-.081),(.43,.54,.030),WHITE,material="door")
            for i in range((6,4,2)[m.lod]):
                y=.18+.40*i/((6,4,2)[m.lod]-1)
                with m.transform(translate=(0,y,-.103),rotate_x=-18):
                    m.box((0,0,0),(.41,.041,.062),WHITE,material="ivory",bottom=True)
    else:
        m.hall(-.30,.05,.49,.56,.68,.42,roof="glass")
        for x,z,h,r in [(.45,.31,1.08,.21),(.48,-.30,.73,.19)]:
            m.lathe(x,z,r,r,[(0,.89,"wall"),(.18,1,"trim"),(.21,.81,"ivory"),
                             (h*.72,.81,"glass"),(h*.82,.97,m.light),(h,.49,"metal")])
            m.hood(x,h,z,r*.57,r*.58,.12)
        m.bar((.45,.64,.31),(.48,.61,-.30),.043,"metal")


def build_model(spec,lod,draw):
    m=Grove(spec,lod)
    w,d=spec.footprint
    scale=(w*.49,min(w,d)*.47+.10,d*.49)
    with m.transform(scale=scale):
        draw(m,spec.building_type)
    return m


def thermal_baths(m):
    if m.era==2:
        vault(m,-.27,.24,.46,.58,.46,.40,roof="glass")
        vault(m,.43,.36,.24,.37,.31,.18,door=False)
        drum(m,.53,.49,.17,.16,[(0,1,"brick"),(.19,1.13,"trim"),(.94,.62,"brick"),
                                  (1.35,.61,"brick"),(1.39,.79,"accent"),(1.44,.77,"door")])
        m.basin(.29,0,-.47,.49,.32,.24)
        m.stem([(.55,.30,.03),(.55,.35,-.13),(.51,.36,-.25),(.50,.28,-.29)],
               [.064,.05,.05,.048],"metal")
    else:
        # Three connected pools step down from the changing pavilion to the open garden.
        for x,z,r,d,h in ((-.26,.44,.46,.38,.76),(.20,-.04,.46,.34,.44),(.28,-.54,.51,.30,.16)):
            deck(m,x,0,z,r*1.09,d*1.09,h,"ivory")
            m.basin(x,h-.011,z,r,d,.135)
        for x,top,bottom,z1,z2 in ((-.12,.86,.55,.19,-.01),(.22,.54,.26,-.31,-.51)):
            # The spill broadens into a sheet below a stone lip.
            banks=[(x,top,z1,.103),(x+.018,top-.029,z1-.09,.103),(x+.04,bottom,z2,.125)]
            for a,b in zip(banks,banks[1:]):
                ax,ay,az,aw=a
                bx,by,bz,bw=b
                m.quad((ax-aw,ay,az),(ax+aw,ay,az),(bx+bw,by,bz),(bx-bw,by,bz),WHITE,"water")
            if m.lod<2:
                for sign in (-1,1):
                    m.bar((x+sign*.108,top,z1+.045),(x+.018+sign*.108,top-.027,z1-.09),.022,"ivory")
        pod(m,-.63,.05,.24,.34,.41,.34,door=True)
        m.leaf((-.46,.83,.64),(-.65,1.23,.80),.18,"glass")


def treatment(m):
    if m.era==2:
        for x,h in ((-.52,.35),(.52,.40)):
            vault(m,x,.16,.24,.63,h,.20,roof="roof")
        vault(m,-.01,.39,.30,.41,.60,.27,roof="glass",door=False)
        for z in (.18,.49):
            drum(m,-.01,z,.095,.10,[(.78,.85,"metal"),(.98,.64,"metal"),(1.02,1.10,"trim"),
                                      (1.08,.52,"roof")])
        for x in (-.31,.31):
            m.bar((x,.018,-.57),(x,.43,-.55),.044,"metal")
        deck(m,0,.39,-.39,.42,.32,.095,"roof")
        with m.transform(translate=(0,.63,-.60),scale=(1.26,1.1,1.0)):
            healing_plaque(m)
    else:
        deck(m,0,0,-.11,.72,.61,.26)
        for x,z,w,d,b,h in ((-.44,.28,.36,.38,.59,.58),(.43,.40,.32,.38,.83,.56),
                             (.51,-.38,.28,.32,.47,.43)):
            pod(m,x,z,w,d,b,h)
        deck(m,0,.63,.31,.50,.125,.115,"glass")
        deck(m,0,.245,-.53,.36,.27,.47,"glass")
        m.portal(0,.245,-.77,.35,.38,"door")
        stair(m,0,-.86,.064,.26,.13,.073)
        with m.transform(translate=(-.02,.86,-.54),scale=(1.30,1.23,1)):
            healing_plaque(m)


def archives(m):
    if m.era==2:
        vault(m,-.31,.03,.39,.69,.56,.24)
        vault(m,-.31,.15,.23,.51,.19,.17,base=.70,roof="glass",door=False)
        vault(m,.37,.28,.26,.43,.61,.23,turn=81,door=False)
        deck(m,.36,0,-.43,.32,.24,.21)
        open_book(m,.36,.24,-.42,.75)
    else:
        for x,z,r,h in ((-.44,.21,.34,1.28),(.40,.38,.30,.89)):
            drum(m,x,z,r,r*.93,[(0,.73,"wall"),(.26,.82,"ivory"),(.44,1,"trim"),
                                   (.79*h,.89,"glass"),(h,1.07,"accent"),(h+.12,.67,"roof")])
            if m.lod<2:
                deck(m,x,.59*h,z,r*1.07,r,.065,"ivory")
        vault(m,0,.30,.12,.36,.12,.09,turn=78,base=.73,door=False,roof="glass")
        deck(m,0,0,-.31,.70,.43,.26)
        drum(m,.04,-.34,.26,.23,[(.25,.82,"trim"),(.39,.66,"glass"),(.44,1,"ivory")])
        open_book(m,.04,.47,-.36,1.04)
        stair(m,0,-.78,.055,.36,.15,.078)


def telescope(m):
    if m.era==2:
        x,z=-.23,.17
        drum(m,x,z,.54,.53,[(0,1,"brick"),(.13,1.03,"accent"),(.51,.90,"brick"),
                               (.59,.99,"trim"),(.65,.97,"metal")])
        # Two cut hemisphere shutters bound the open optical slot on either side.
        panels, rings=(8,5,4)[m.lod],(5,3,2)[m.lod]
        for side in (-1,1):
            def shell(i,j):
                a=pi*(j/panels-.5)
                t=1.34*i/rings
                return (x+side*.51*cos(t)*cos(a)+side*.048,
                        .636+.51*sin(t),z+.50*cos(t)*sin(a))
            for i in range(rings):
                for j in range(panels):
                    pts=[shell(i,j),shell(i+1,j),shell(i+1,j+1),shell(i,j+1)]
                    m.quad(*(pts if side>0 else pts[::-1]),WHITE,"metal")
        # A recessed instrument chamber remains visible below the sliding shutters.
        with m.transform(translate=(x,.636,z),scale=(.90,1,.93)):
            m.dome((0,0,0),.495,WHITE,rings=(4,3,2)[m.lod],segments=(14,9,6)[m.lod],
                   squash=.994,material="metal")
        with m.transform(translate=(x,.84,z+.01),rotate_x=-48):
            drum(m,0,0,.095,.095,[(0,.74,"metal"),(.23,1,"metal"),(.52,.91,"metal"),
                                   (.562,1.28,"trim"),(.60,1.17,"glass")])
        vault(m,.49,-.11,.24,.44,.35,.20,roof="glass")
        m.portal(x,0,z-.54,.31,.43)
    else:
        deck(m,-.02,0,.01,.78,.60,.24,"ivory")
        pod(m,-.47,.22,.29,.37,.19,.38,door=True)
        # Two separated optical assemblies ride on parallel rails over a forked azimuth yoke.
        drum(m,.23,-.11,.30,.28,[(.19,.77,"metal"),(.32,1,"trim"),(.49,.58,"ivory")])
        with m.transform(translate=(.22,.84,-.03),rotate_z=-26,rotate_x=-21):
            for dx in (-.23,.23):
                m.bar((dx,-.34,.38),(dx,.31,-.44),.048,"ivory")
            for y,z,r in ((-.34,.38,.29),(.31,-.44,.215)):
                with m.transform(translate=(0,y,z),rotate_x=38):
                    m.hoop(0,0,0,r,r,.062,"accent")
                    if m.lod<2 or z>0:
                        with m.transform(rotate_x=-90):
                            m.lathe(0,0,r*.87,r*.87,[(-.023,.88,"glass"),(0,1,"glass"),
                                                      (.017,.89,"glass")],n=(10,7,5)[m.lod],bottom=True)
            if m.lod==0:
                for dx in (-.23,.23):
                    m.stem([(dx,-.28,.36),(dx*.63,-.13,.08),(dx,.21,-.39)],
                           [.025,.016,.022],"trim",sides=5)
        for x in (.01,.45):
            m.stem([(x,.32,-.14),(x,.62,-.05),(x,.85,.04)],[.074,.055,.060],"metal")


def press_house(m):
    if m.era==2:
        # Three glazed northlights run across a shed behind the exposed rotary press.
        for i in range(3):
            x=-.54+.43*i
            outline=[(x-.20,.00),(x+.198,.00),(x+.198,.62),(x+.135,.94),(x-.20,.66)]
            for z,rev in ((-.08,True),(.57,False)):
                p=[(a,b,z) for a,b in outline]
                m.poly(p[::-1] if rev else p,WHITE,"brick")
            for j in range(1,5):
                a,b=outline[j],outline[(j+1)%5]
                m.quad((a[0],a[1],-.08),(b[0],b[1],-.08),(b[0],b[1],.57),
                       (a[0],a[1],.57),WHITE,("wall","glass","roof","brick")[j-1])
        deck(m,0,0,-.42,.79,.30,.16)
        for x in (-.47,0,.47):
            with m.transform(translate=(x,.39,-.23),rotate_x=-90):
                rows=[(0,.83,"metal"),(.09,1,"trim"),(.36,.97,"metal"),
                      (.40,1.10,"accent"),(.46,.80,"trim"),(.48,.29,"metal")]
                if m.lod==2:
                    rows=[rows[j] for j in (0,2,3,5)]
                m.lathe(0,0,.21,.21,rows,n=(12,7,5)[m.lod])
        if m.lod<2:
            m.bar((-.62,.53,-.30),(.60,.53,-.30),.032,"ivory")
        # Printed stock turns over the delivery roller and falls onto its catch tray.
        with m.transform(translate=(.46,.29,-.67),rotate_x=19):
            m.box((0,0,0),(.32,.035,.28),WHITE,material="ivory",bottom=True)
        if m.lod==0:
            for x in (-.64,.13):
                m.stem([(x,.15,-.65),(x,.40,-.66),(x+.06,.55,-.52)],
                       [.043,.033,.026],"metal",sides=6)
    else:
        deck(m,.04,0,-.14,.69,.72,.17,"metal")
        for x in (-.43,.48):
            m.stem([(x,.15,.38),(x*1.29,.57,.28),(x*1.08,1.12,.16),(x*.49,1.27,.07)],
                   [.137,.121,.105,.084],"ivory")
        drum(m,.01,.10,.36,.25,[(.68,.19,"metal"),(.85,.53,"glass"),
                                  (1.05,1,"trim"),(1.26,.94,"ivory"),(1.32,.74,"roof")])
        deck(m,.01,.16,-.26,.29,.68,.23,"ivory")
        for i in range((4,3,2)[m.lod]):
            m.box((.01+.007*i,.40+i*.018,-.54+i*.073),(.37-.009*i,.024,.28),WHITE,material="plaster")
        if m.lod<2:
            pod(m,-.57,.53,.20,.22,.17,.32)


def transcription(m):
    if m.era==1:
        vault(m,-.48,.33,.29,.34,.38,.20)
        deck(m,-.48,0,-.06,.29,.26,.17)
        for x,z in ((.0,.34),(.55,.34),(.0,-.37),(.55,-.37)):
            m.bar((x,0,z),(x,.68,z),.047,"trim",.036)
        m.hood(.28,.68,-.01,.47,.55,.22)
        for x,z in ((.04,-.37),(.52,-.29),(.31,.12)):
            drum(m,x,z,.15,.11,[(0,.73,"timber"),(.23,.59,"wall"),(.30,1,"trim")])
            open_book(m,x,.31,z,.37)
    elif m.era==2:
        vault(m,-.32,.19,.38,.47,.90,.27,roof="glass")
        vault(m,.41,.28,.23,.43,.38,.16,turn=12)
        m.portal(-.32,.56,-.296,.36,.40,"glass")
        for z in (-.03,.23,.49):
            if m.lod==2 and z==.23:
                continue
            with m.transform(translate=(.072,.48,z),rotate_y=-90):
                m.portal(0,0,0,.19,.32,"glass")
        with m.transform(translate=(.02,0,-.14),scale=(.78,.75,.78)):
            drum(m,.53,-.39,.18,.19,[(0,1,"wall"),(.18,1,"glass"),(.26,.55,"trim")])
            quill(m)
    else:
        for x,z,r,y,h in ((-.29,.29,.44,0,.44),(-.25,.32,.38,.40,.44),(-.14,.35,.31,.80,.43)):
            with m.transform(translate=(0,y,0)):
                pod(m,x,z,r,r*.91,0,h)
        deck(m,.37,0,-.30,.43,.42,.19,"ivory")
        vault(m,.13,-.01,.12,.22,.12,.09,turn=38,base=.24,door=False,roof="glass")
        # A standing inscription leaf faces a sloped copy lectern.
        m.stem([(.45,.17,-.22),(.50,.53,-.16),(.64,.94,-.12)],
               [.070,.044,.025],"accent")
        m.leaf((.49,.36,-.16),(.68,1.02,-.095),.194,"glass")
        drum(m,.20,-.54,.15,.13,[(.15,.81,"trim"),(.37,.66,"ivory"),(.41,1,"accent")])
        open_book(m,.20,.42,-.54,.49)


def draw(m,kind):
    advanced={"Bathhouse":thermal_baths, "Hospital":treatment, "Library":archives,
              "Observatory":telescope, "Printworks":press_house}
    if kind in advanced and m.era>=2:
        advanced[kind](m)
    elif kind=="Scriptorium" and m.era>=1:
        transcription(m)
    elif kind in ("Bathhouse","Herbalist","Hospital","MedicalCenter"):
        healing(m,kind)
    elif kind in ("Library","Printworks","Scriptorium"):
        written_arts(m,kind)
    else:
        science(m,kind)


def generate(package,draw):
    for spec in package_models(package):
        a,b,c=(build_model(spec,i,draw) for i in range(3))
        result=publish(a,spec.metadata(),package,lods=(b,c),preview_size=224)
        print(spec.id,result["lod_triangles"],flush=True)


if __name__=="__main__":
    generate("elf_learninghealth",draw)
