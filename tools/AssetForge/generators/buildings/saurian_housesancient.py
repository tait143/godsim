"""Saurian houses of the Dawn, Bronze and Iron ages."""

import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import MeshBuilder, publish
from forge.atlas import palette
from forge.render import contact_sheet
from generators.buildings import saurian_delta_dwellings as homes

PACKAGE = 'saurian_housesancient'


class Dwelling(MeshBuilder):
    """Small-scale Saurian joinery, planar roof inlays and open undercrofts."""
    def __init__(self, spec, lod):
        super().__init__(seed=spec.id)
        self.lod = lod
        self.variant = spec.variant
        self.age = spec.ages[0]
        self.colors = palette(self.age)[0]

    def color(self, role):
        return self.colors.get(role) or self.colors['saurian/' + role]

    def face(self, role, points):
        self.poly(points, self.color(role), material=role)

    def block(self, role, x, z, y0, y1, width, depth, bottom=False):
        self.box((x, (y0+y1)/2, z), (width, y1-y0, depth), self.color(role),
                 material=role, bottom=bottom)

    def drum(self, role, base, radius, height, segments, top=None, bottom=False):
        self.cylinder(base, radius, height, self.color(role), segments=segments,
                      top_radius=top, material=role, cap_bottom=bottom)

    def rail(self, role, a, b, width, depth=None):
        dx, dy, dz = (j-i for i, j in zip(a, b))
        length = math.dist(a, b)
        with self.transform(translate=tuple((i+j)/2 for i,j in zip(a,b)),
                            rotate_x=0, rotate_y=math.degrees(math.atan2(dx,dz))):
            with self.transform(rotate_x=math.degrees(math.atan2(math.hypot(dx,dz),dy))):
                self.box((0,0,0), (width,length,depth or width),self.color(role),
                         material=role, bottom=True)

    def course(self, role, x,z, y0,y1, w,d, tw,td, bevel=.10, rim=None, underside=False):
        """Loft between clipped rectangular rings. A clipped eave catches the sun."""
        if self.lod == 2:
            bevel = 0
        def ring(y, width, depth):
            a,b = width/2,depth/2
            c = min(width,depth)*bevel
            plan = [(-a+c,-b),(a-c,-b),(a,-b+c),(a,b-c),(a-c,b),(-a+c,b),
                    (-a,b-c),(-a,-b+c)] if c else [(-a,-b),(a,-b),(a,b),(-a,b)]
            return [(x+u,y,z+v) for u,v in plan]
        lo,hi=ring(y0,w,d),ring(y1,tw,td)
        for i in range(len(lo)):
            j=(i+1)%len(lo)
            self.face(rim if rim and len(lo)==8 and i%2 else role,
                      [lo[j],lo[i],hi[i],hi[j]])
        self.face(role,hi[::-1])
        if underside:
            self.face(role,lo)

    def eye(self,x,y,z,r, role='gold'):
        # Face-on solar seal with a physical rim and an inset turquoise cabochon.
        with self.transform(translate=(x,y,z),rotate_x=-90):
            self.drum(role,(0,0,0),r,.018,(12,8,6)[self.lod],top=r*.89,bottom=True)
            if self.lod == 0:
                self.drum('turquoise',(0,.018,0),r*.42,.005,8,top=r*.35)

    def door_panel(self, x,z,y,w,h, surround='trim', angular=True):
        # Dark recessed leaf and a sloping-headed frame share a threshold at deck level.
        def leaf(role, width, height, plane, base):
            a=width/2
            head=height-.042 if angular else height
            pts=[(-a,base),(-a,head)]
            if angular:
                pts.extend([(-a*.54,height),(a*.54,height)])
            pts.extend([(a,head),(a,base)])
            self.face(role,[(x+u,y+v,plane) for u,v in pts])
        leaf(surround,w+.046,h+.027,z-.005,0)
        leaf('door',w,h,z-.010,.001)
        if self.lod==0:
            self.block('gold',x+w*.26,z-.014,y+h*.43,y+h*.50,.014,.012)

    def opening(self,x,y,z,w,h,side=False,light='emissive'):
        # Glazed vents and their sill are set against the real wall plane.
        with self.transform(translate=(x,y,z),rotate_y=-90 if side else 0):
            self.face('trim',[(w*.56,-h*.59,-.004),(-w*.56,-h*.59,-.004),
                              (-w*.56,h*.59,-.004),(w*.56,h*.59,-.004)])
            self.face(light,[(w*.46,-h*.44,-.008),(-w*.46,-h*.44,-.008),
                             (-w*.46,h*.44,-.008),(w*.46,h*.44,-.008)])
            if self.lod==0:
                self.block('trim',0,-.011,-h*.48,h*.48,.014,.014)
                self.block('timber',0,-.010,-h*.65,-h*.51,w*1.18,.04)

    def stairs(self, x, zback, width, height, run, role):
        count=(4,3,1)[self.lod]
        left,right=x-width/2,x+width/2
        front=zback-run
        if count==1:
            self.wedge((x,0,(front+zback)/2),width,run,height,
                       self.color(role),material=role)
            return
        for i in range(count):
            z=front+run*i/count
            end=front+run*(i+1)/count
            y=height*(i+1)/count
            prior=height*i/count
            self.face(role,[(right,prior,z),(left,prior,z),(left,y,z),(right,y,z)])
            self.face(role,[(left,y,end),(right,y,end),(right,y,z),(left,y,z)])
            self.face(role,[(right,0,end),(right,0,z),(right,y,z),(right,y,end)])
            self.face(role,[(left,0,z),(left,0,end),(left,y,end),(left,y,z)])
        self.face(role,[(left,0,zback),(right,0,zback),(right,height,zback),(left,height,zback)])

    def floor(self, xdoor, height, deck, support):
        underside=height-.055
        for x in (-.32,.32):
            for z in (-.255,.30):
                self.block(support,x,z,0,underside,.095,.105)
        self.course(deck,0,.012,underside,height,.85,.72,.87,.73,
                    bevel=.085,underside=True)
        self.stairs(xdoor,.012-.73/2,.22,height,.139,deck)

    def roof_lozenge(self,x,z,y,depth,top_depth,rise,width,role='turquoise'):
        # Two woven diamonds following the pitch of the front apron.
        def pt(u,t):
            return (x+u,y+rise*t+.0035,z-depth/2+(depth-top_depth)*t/2-.004)
        self.face(role,[pt(0,.12),pt(-width/2,.50),pt(0,.88),pt(width/2,.50)])
        if self.lod==0:
            self.face('gold',[(a,b+.002,c-.003) for a,b,c in
                              [pt(0,.34),pt(-width*.17,.5),pt(0,.66),pt(width*.17,.5)]])

    def pot(self,x,z,y,r=.045):
        n=(7,5,4)[self.lod]
        self.drum('hide',(x,y,z),r*.58,r*1.13,n,r)
        self.drum('hide',(x,y+r*1.13,z),r,r*.48,n,r*.70)
        self.drum('accent',(x,y+r*1.61,z),r*.72,r*.14,n)
        self.drum('soil',(x,y+r*1.75,z),r*.48,.002,n)

    def woven_mat(self,x,y,z,w,h):
        # A folded striped drying mat, suspended over a projecting timber rod.
        self.block('timber',x,z-.012,y+h-.010,y+h+.008,w+.032,.038)
        for a,b,role in [(0,.12,'gold'),(.12,.22,'turquoise'),(.22,.83,'banner'),
                         (.83,.92,'turquoise'),(.92,1,'banner')]:
            def at(u,t):
                return (x+u,y+t*h,z-.030-.012*(1-t))
            self.face(role,[at(w/2,a),at(-w/2,a),at(-w/2,b),at(w/2,b)])


def reed_roof(m,x,z,y,w,d,wing=False):
    rise=.17 if wing else (.24 if m.variant else .255)
    m.course('roof',x,z,y,y+rise,w,d,w*.46,d*.43,rim='roof',underside=True)
    m.roof_lozenge(x,z,y,d,d*.43,rise,w*.26)
    m.course('thatch',x,z,y+rise,y+rise+.10,w*.52,d*.48,w*.26,d*.17,
             rim='roof')
    m.block('timber',x,z,y+rise+.10,y+rise+.123,w*.28,d*.18)
    if not wing:
        # The two splayed ridge poles clamp the reed bundle against the wind.
        for sign in (-1,1):
            m.rail('timber',(x+sign*w*.094,y+rise+.115,z),
                   (x+sign*w*.209,y+rise+.161,z+.010),.032,.041)
            if m.lod==0:
                m.block('thatch',x+sign*w*.070,z,y+rise+.102,y+rise+.132,.022,d*.19)


def build(spec,lod):
    m=Dwelling(spec,lod)
    if m.age in ("bronze","iron"):
        {"bronze":homes.bronze,"iron":homes.iron}[m.age](m)
        return m
    variant=m.variant
    x, w = (-.138,.50) if variant else (0,.68)
    z,d=.063,.54
    base=.164
    head=base+(.266 if variant else .286)
    dx=x+(.025 if variant else -.07)
    front=z-d/2
    m.floor(dx,base,'timber','timber')
    wall='hide'
    m.block(wall,x,z,base,head,w,d)
    reed_roof(m,x,z,head,w+.115,d+.115)
    m.door_panel(dx,front,base,.128,.193)
    if variant:
        wingx,wingz=.226,.105
        wy=base+.183
        m.block(wall,wingx,wingz,base,wy,.26,.398)
        reed_roof(m,wingx,wingz,wy,.345,.495,wing=True)
        if lod<2:
            m.opening(wingx,base+.096,wingz-.199,.131,.087)
            m.opening(wingx+.13,base+.094,wingz,.155,.086,side=True)
    elif lod<2:
        m.opening(x+w/2,base+.155,z+.025,.207,.114,side=True)
        m.opening(x+.173,base+.155,front,.099,.093)
    if lod<2:
        m.eye(dx,base+.238,front-.008,.037)
        for u in (x-w/2+.030,x+w/2-.030):
            m.block('timber',u,front-.008,
                    base,head,.042,.044)
        if not variant:
            m.woven_mat(x-.242,base+.071,front,.080,.133)
    if lod==0:
        # A rain jar gathers water at the lower eave, safely off the approach.
        m.pot(.327 if variant else .292,-.267,base,.045)
        if variant:
            m.woven_mat(dx-.152,base+.061,front,.070,.111)
        else:
            m.rail('timber',(.394,base+.006,.235),(.34,base+.24,.294),.033)
            m.rail('timber',(.325,.023,-.228),(.30,base-.055,-.072),.032)
        with m.transform(translate=(x-w/2-.002,base+.140,z),rotate_y=90):
            m.opening(0,0,0,.137,.090,light='door')
    return m


def publish_houses(package, builder):
    paths,labels=[],[]
    for spec in sorted(package_models(package),key=lambda s:(AGES.index(s.ages[0]),s.variant)):
        meshes=[builder(spec,i) for i in range(3)]
        row=publish(meshes[0],spec.metadata(),package,lods=meshes[1:],preview_size=288)
        paths.append(ROOT/row['preview'])
        labels.append(f"{spec.ages[0].title()}  /  {spec.variant+1}")
        print(spec.id,row['lod_triangles'],flush=True)
    contact_sheet(paths,paths[0].parent/'contact.png',cols=2,cell=192,labels=labels)


if __name__=='__main__':
    publish_houses(PACKAGE, build)
