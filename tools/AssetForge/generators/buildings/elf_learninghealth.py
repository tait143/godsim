"""Elven halls of learning and healing. Builder session 8."""
from math import cos, sin, pi
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish

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

    def lathe(self, x, z, rx, rz, rows, n=None, closed=True):
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


def science(m,kind):
    if kind=="University":
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


def draw(m,kind):
    if kind in ("Bathhouse","Herbalist","Hospital","MedicalCenter"):
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
