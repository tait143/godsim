"""The World Tree: a root-door, two bough chapels, and the seed of the next tree."""
import math
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, package_models
from forge import MeshBuilder, publish
from forge.atlas import palette
from forge.render import contact_sheet

PACKAGE = "elf_wonders"
TAU = 2 * math.pi


class Tree(MeshBuilder):
    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.colours = palette(spec.ages[0])[0]

    def face(self, points, role):
        colour = self.colours.get(role) or self.colours['elf/' + role]
        self.poly(points, colour, material=role)

    def skin(self, rings, roles, closed=False):
        # Rings are clockwise viewed from above, starting at +X and going toward +Z.
        for index, (bottom, top) in enumerate(zip(rings, rings[1:])):
            role = roles[index] if not isinstance(roles, str) else roles
            for j in range(len(bottom)):
                k = (j + 1) % len(bottom)
                self.face([bottom[k], bottom[j], top[j], top[k]], role)
        if closed:
            self.face(rings[0], roles[0] if not isinstance(roles, str) else roles)
            self.face(rings[-1][::-1], roles[-1] if not isinstance(roles, str) else roles)

    def vessel(self, x, z, profile, roles, sides, depth=1., phase=0., bottom=False):
        rings = [[(x + r*math.cos(a), y, z + depth*r*math.sin(a))
                  for a in (phase + TAU*j/sides for j in range(sides))]
                 for y, r in profile]
        self.skin(rings, roles)
        if bottom:
            self.face(rings[0], roles[0])
        self.face(rings[-1][::-1], roles[-1])

    def limb(self, stations, role='timber', sides=None, section=1.0):
        # Carry an orthogonal section along the curve, including nearly horizontal boughs.
        sides = sides or (8, 5, 4)[self.lod]
        xyz = np.array([s[:3] for s in stations])
        tangents = np.array([xyz[min(j+1, len(xyz)-1)] - xyz[max(0, j-1)]
                             for j in range(len(xyz))])
        tangents /= np.linalg.norm(tangents, axis=1)[:, None]
        ref = np.eye(3)[np.argmin(np.max(abs(tangents), axis=0))]
        rings = []
        for j, station in enumerate(stations):
            tangent = tangents[j]
            u = np.cross(ref, tangent)
            u /= np.linalg.norm(u)
            v = np.cross(u, tangent)
            ring = [xyz[j] + station[3]*(u*math.cos(TAU*i/sides) +
                                         section*v*math.sin(TAU*i/sides)) for i in range(sides)]
            rings.append(ring)
        self.skin(rings, role, closed=True)


def ground(m):
    n = (20, 12, 8)[m.lod]
    # A low, bevelled precinct of dressed stone. The front processional treads
    # have a narrower radial extent, so they lead into the door rather than encircling it.
    m.vessel(0, .075, [(0, 1.18), (.054, 1.26), (.13, 1.25), (.168, 1.205)],
             ['fieldstone', 'wall', 'trim'], n, depth=.84)
    m.vessel(0, .075, [(.167, 1.17), (.188, 1.16)], ['grass'], n, depth=.835)
    for j in range(3):
        m.vessel(0, -.83 + j*.032, [(0.003+j*.061, .426-j*.025),
                                     (.063+j*.061, .425-j*.025)], ['wall'],
                 (14, 10, 8)[m.lod], depth=1.30-j*.20)
    # A long threshold covers the joint with the lawn, graded up to the root door.
    m.vessel(0, -.64, [(.182, .29), (.209, .275)], ['fieldstone'], n//2, depth=1.06)


def root(m, angle, length, twist):
    a = math.radians(angle)
    # Each root spreads over the lawn like a buttress, with a tall shoulder and a broad foot.
    sections = ((.19, .86, .19, .28), (.42, .48, .215, .44),
                (.64, .289, .18, .32), (.84, .218, .118, .15), (1., .207, .035, .014))
    if m.lod == 1:
        sections = (sections[0], sections[1], sections[3], sections[-1])
    elif m.lod == 2:
        sections = (sections[0], sections[2], sections[-1])
    rings = []
    for d, y, w, peak in sections:
        t = a + twist*d
        x, z = length*d*math.cos(t), .07 + length*d*.85*math.sin(t)
        dx, dz = -math.sin(t), math.cos(t)
        rings.append([(x+dx*w, y-.026, z+dz*w),
                      (x+dx*w*.45, y+peak*.65, z+dz*w*.45),
                      (x-dx*w*.10, y+peak, z-dz*w*.10),
                      (x-dx*w*.75, y+peak*.35, z-dz*w*.75),
                      (x-dx*w, y-.025, z-dz*w)])
    if m.lod==2:
        rings=[[r[0],r[2],r[-1]] for r in rings]
    m.skin(rings, 'timber', closed=True)


def trunk(m):
    n = (18, 10, 7)[m.lod]
    sections = [(0,.13,.09,.62), (.03,.39,.105,.50), (.095,.80,.115,.38),
                (.017,1.22,.15,.30), (-.135,1.64,.15,.279), (-.119,2.04,.14,.228),
                (.055,2.39,.12,.167), (.12,2.72,.087,.128), (-.173,3.252,-.009,.065)]
    if m.lod == 1:
        sections=[sections[i] for i in (0,2,3,4,5,7,8)]
    elif m.lod == 2:
        sections = [sections[i] for i in (0,2,4,5,7,8)]
    rings = []
    for j, (x,y,z,r) in enumerate(sections):
        pts = []
        for i in range(n):
            a = TAU*i/n + .15*math.sin(1.06*y)
            flute = 1 + .11*math.cos(5*a-1.4*y) + .04*math.sin(3*a+.8*y)
            pts.append((x+r*flute*math.cos(a), y, z+r*flute*.94*math.sin(a)))
        rings.append(pts)
    m.skin(rings, 'timber')
    # The flutes make these sections concave. Their radial construction gives
    # an interior centre so the cap triangles cannot cross a neighbouring flute.
    for j in range(n):
        k=(j+1)%n
        m.face((sections[0][:3], rings[0][j], rings[0][k]), 'timber')
        m.face((sections[-1][:3], rings[-1][k], rings[-1][j]), 'timber')
    for angle, length, twist in ((-37,1.12,.21),(13,1.14,-.07),(78,1.10,.18),
                                 (151,1.12,-.14),(211,1.05,.16),(-115,.85,-.16)):
        root(m, angle, length, twist)


def portal(m):
    # A pointed vault has full depth, sloping soffits and a recessed door leaf.
    # The rear shell extends into the bole. The reveal also has a visible threshold.
    outline = [(-1,0),(-1,.34),(-.90,.59),(-.61,.80),(0,1),(.61,.80),
               (.90,.59),(1,.34),(1,0)]
    if m.lod == 2:
        outline = [outline[i] for i in (0,2,4,6,8)]
    def arch(w,h,z):
        return [(w*u,.203+h*v,z+.050*v*v) for u,v in outline]
    shell = arch(.43,1.14,-.237)
    outer = arch(.348,1.02,-.652)
    mid = arch(.319,.962,-.681)
    inner = arch(.253,.865,-.671)
    recess = arch(.219,.791,-.542)
    for rear, front, role in ((shell,outer,'wall'),(outer,mid,'trim'),
                              (mid,inner,'accent'),(inner,recess,'trim')):
        for j in range(len(front)-1):
            m.face([rear[j],rear[j+1],front[j+1],front[j]], role)
    m.face(recess, 'door')
    if m.lod < 2:
        m.limb([(0,.215,-.557,.014),(0,.76,-.53,.012),(0,.978,-.503,.009)],
               'gold', sides=4)
        # A seed-shaped luminous transom lives in the arch, above the door.
        with m.transform(translate=(0,1.038,-.632), rotate_x=-7):
            m.face([(0,-.078,-.011),(-.050,0,-.011),(0,.085,-.011),(.05,0,-.011)],
                   'emissive')


def canopy(m, center, size, phase, role):
    # One interlocking crown per scaffold limb. Each consists of overlapping
    # lobes cut into a continuous volume, with light on the shoulder and pointed leaf tips.
    x,y,z = center
    w,h,d = size
    n = (20, 10, 7)[m.lod]
    rings = []
    profiles = ((.32,-.74),(.78,-.48),(1.0,-.01),(.90,.48),(.53,.88))
    if m.lod == 1:
        profiles = (profiles[1], profiles[2], profiles[-1])
    elif m.lod == 2:
        profiles = (profiles[2], profiles[-1])
    for level, (radius, elevation) in enumerate(profiles):
        pts = []
        for i in range(n):
            a = TAU*i/n + phase
            lobe = .09*math.cos(4*a-phase) + .064*math.cos(7*a+2*phase)
            tip = (1 if i%2 else -.7)*(1-abs(elevation))*.058
            rr = radius*(1+lobe+tip)
            height = elevation + (.15*math.sin(3*a+phase) + .12*math.cos(5*a)) * radius
            pts.append((x + w*(rr*math.cos(a)-.115*elevation),
                        y + h*height + .06*w*math.cos(a),
                        z + d*(rr*math.sin(a)+.08*elevation)))
        rings.append(pts)
    m.skin(rings, role)
    for i in range(n):
        k=(i+1)%n
        m.face([rings[0][i], rings[0][k], (x+.045*w,y-.9*h,z-.10*d)], role)
        m.face([rings[-1][k], rings[-1][i], (x-.20*w,y+1.07*h,z+.14*d)], role)
    # Broad folded leaves grow out of the shoulder and break the skirt into
    # pointed groups. A ridge and a real underside keep each readable from below.
    leaf_count = (8, 4, 0)[m.lod]
    for j in range(leaf_count):
        a=phase+TAU*j/leaf_count
        ca,sa=math.cos(a),math.sin(a)
        def pt(length,width,height):
            return (x+w*(length*ca-width*sa),y+h*height,z+d*(length*sa+width*ca))
        root_pt=pt(.30,-.07,.83)
        left=pt(.82,-.24,.34)
        spine=pt(.79,.0,.64)
        right=pt(.80,.24,.37)
        tip=pt(1.06+.105*math.sin(3*a),.13,-.38-.14*math.cos(a))
        lower=pt(.77,.0,.12)
        leaf_role='grass' if j%4==0 else role
        for face in ((root_pt,spine,left),(root_pt,right,spine),
                     (left,spine,tip),(spine,right,tip),
                     (root_pt,left,lower),(root_pt,lower,right),(left,tip,lower),(lower,tip,right)):
            m.face(face,leaf_role)


def chapel(m, x, z, y, radius, mirror):
    # A closed scriptorium and an open lantern shrine rest in different branch forks.
    n = (10, 8, 5)[m.lod]
    with m.transform(translate=(x,y,z), rotate_y=-15*mirror):
        r = radius
        m.vessel(0,0,[(-.10,.48*r),(-.038,.92*r),(.018,r)],
                 ['trim','wall'], n, depth=.91, bottom=True)
        wall = .29 if mirror==1 else .37
        rings=[[(rr*math.cos(TAU*j/n),yy,.89*rr*math.sin(TAU*j/n))
                for j in range(n)] for yy,rr in ((.016,r*.66),(.097,r*.76),(wall,r*.69))]
        m.skin(rings[:2], 'wall')
        for j in range(n):
            k=(j+1)%n
            bl,br=np.array(rings[1][k]),np.array(rings[1][j])
            tl,tr=np.array(rings[2][k]),np.array(rings[2][j])
            if j%2:
                m.face((bl,br,tr,tl),'wall')
                continue
            if m.lod == 2:
                m.face((bl,br,tr,tl),'glass' if mirror==1 else 'emissive')
                continue
            # Pointed windows share the wall plane and replace the corresponding masonry.
            def panel(u,v):
                return (1-v)*((1-u)*bl+u*br)+v*((1-u)*tl+u*tr)
            a,b=panel(.09,0),panel(.91,0)
            c,d,e=panel(.91,.53),panel(.5,.95),panel(.09,.53)
            m.face((bl,a,e,tl),'wall')
            m.face((b,br,tr,c),'wall')
            m.face((tl,e,d),'wall')
            m.face((tl,d,tr),'wall')
            m.face((d,c,tr),'wall')
            m.face((a,b,c,d,e),'glass' if mirror==1 else 'emissive')
        m.vessel(0,0,[(wall-.02,r*.73),(wall+.022,r*.91)],
                 ['accent'],n, depth=.93, bottom=True)
        # Curved lamina above each room: a bell with an upturned stem rather than a cone.
        profiles = [(1.10, .026),(.75,.112),(.38,.235),(.105,.35)]
        if m.lod == 2:
            profiles.pop(1)
        rings=[]
        for width, hh in profiles:
            ring=[]
            for i in range(n):
                a=TAU*i/n
                tip = .046*(i%2) if width>1 else 0
                ring.append((r*width*math.cos(a)+mirror*hh**2*.8, wall+hh-tip,
                             r*width*.94*math.sin(a)+hh**2*.35))
            rings.append(ring)
        m.skin(rings, 'roof')
        m.face(rings[0], 'trim')
        m.face(rings[-1][::-1],'gold')
        if m.lod==0:
            m.vessel(mirror*.103,.044,[(wall+.343,.037),(wall+.391,.046),
                                       (wall+.444,.009)], ['gold','gold'], 6, bottom=True)


def crescent(m):
    # A hammered moon cup cradles a luminous seed on a short visible pedicel.
    # Ends are separate tapered horns, leaving a large wedge of sky around the seed.
    with m.transform(translate=(-.115,3.605,.029), rotate_y=-31, rotate_z=-11):
        pieces = (16, 12, 8)[m.lod]
        outer, inner = [], []
        for i in range(pieces+1):
            a=math.radians(-145+289*i/pieces)
            thick=.018+.112*math.sin(math.pi*i/pieces)**.8
            outer.append((.425*math.sin(a),-.414*math.cos(a)))
            inner.append(((.425-thick)*math.sin(a),-(.414-thick)*math.cos(a)))
        def at(p,z): return (p[0],p[1],z)
        for i in range(pieces):
            j=i+1
            front=[at(outer[j],-.042),at(outer[i],-.042),at(inner[i],-.042),at(inner[j],-.042)]
            back=[at((p[0],p[1]),.042) for p in reversed(front)]
            m.face(front, 'accent')
            m.face(back, 'accent')
            m.face([at(outer[i],-.042),at(outer[j],-.042),at(outer[j],.042),at(outer[i],.042)], 'gold')
            m.face([at(inner[j],-.042),at(inner[i],-.042),at(inner[i],.042),at(inner[j],.042)], 'ivory')
        for i in (0,pieces):
            cap=[at(outer[i],.042),at(inner[i],.042),at(inner[i],-.042),at(outer[i],-.042)]
            m.face(cap if i==0 else cap[::-1], 'gold')
        m.vessel(0,0,[(-.31,.052),(-.19,.043),(-.114,.061)], ['gold','gold'],6)
        m.vessel(0,0,[(-.135,.050),(-.035,.134),(.06,.136),(.187,.014)],
                 ['gold','emissive','emissive'],(9,7,5)[m.lod],depth=.88,bottom=True)


def lamp(m, path):
    x,y,z = path[-1]
    m.limb([(*point, .023 if j==0 else .014) for j,point in enumerate(path)], 'trim', sides=5)
    m.vessel(x,z,[(y-.193,.009),(y-.103,.071),(y-.044,.057),(y+.008,.022)],
             ['emissive','emissive','gold'],(7,6,5)[m.lod],bottom=True)


def gallery(m, stations, rail):
    # Two rising paths follow the broad forks. Cross sections are perpendicular
    # to the route in plan, so inner and outer parapets turn through each bend.
    edges=[]
    if m.lod==2:
        stations=stations[::2] if len(stations)%2 else stations[::2]+stations[-1:]
    for j,(x,y,z) in enumerate(stations):
        a=stations[max(0,j-1)]
        b=stations[min(j+1,len(stations)-1)]
        dx,dz=b[0]-a[0],b[2]-a[2]
        length=math.hypot(dx,dz)
        u,v=-dz/length*.09,dx/length*.09
        edges.append(((x+u,y,z+v),(x-u,y,z-v)))
    for (a,b),(c,d) in zip(edges,edges[1:]):
        m.face((a,c,d,b),'trim')
        aa,bb,cc,dd=[(x,y-.058,z) for x,y,z in (a,b,c,d)]
        m.face((aa,bb,dd,cc),'timber')
        m.face((aa,cc,c,a),'wall')
        m.face((b,d,dd,bb),'wall')
    for j in (0,len(edges)-1):
        a,b=edges[j]
        cap=(a,b,(b[0],b[1]-.058,b[2]),(a[0],a[1]-.058,a[2]))
        m.face(cap if j==0 else cap[::-1],'timber')
    if not rail or m.lod==2:
        return
    points=[]
    for j,(edge,_) in enumerate(edges):
        x,y,z=edge
        h=.143 if j%2==0 else .105
        if j%2==0 or j==len(edges)-1:
            m.limb([(x,y-.034,z,.024),(x,y+h,z,.020)], 'trim', sides=4)
        points.append((x,y+h,z,.022))
    m.limb(points,'accent',sides=5 if m.lod==0 else 4)


def paths(m):
    gallery(m,[(-.19,.218,-.60),(-.44,.34,-.33),(-.50,.48,-.02),
               (-.45,.63,.31),(-.24,.78,.53),(.09,.91,.56),
               (.40,1.035,.43),(.61,1.117,.25),(.80,1.196,.078)],False)
    gallery(m,[(.70,1.201,-.113),(.43,1.25,-.083),(.19,1.30,-.152),
               (-.065,1.354,-.192),(-.32,1.414,-.230),(-.63,1.526,-.218)],True)
    if m.lod<2:
        m.limb([(.20,.92,.12,.068),(.25,1.10,.035,.052),(.29,1.272,-.11,.040)],'timber')
        m.limb([(-.16,1.13,.13,.070),(-.27,1.27,-.06,.051),(-.34,1.403,-.21,.041)],'timber')


def herb_bed(m,x,z,turn):
    # Fans of medicinal leaves and one pale, five-petalled starflower in each bed.
    with m.transform(translate=(x,.177,z),rotate_y=turn):
        for j in range((7,4,0)[m.lod]):
            a=TAU*j/(7,4,1)[m.lod]
            h=.19+.068*math.cos(3*a)
            with m.transform(rotate_y=math.degrees(a)):
                root=(0,0,0)
                tip=(.235,h,-.042)
                spine=(.096,h*.73,0)
                left=(.108,h*.49,-.056)
                right=(.118,h*.51,.052)
                lower=(.119,h*.43,-.003)
                for f in ((root,spine,left),(root,right,spine),(left,spine,tip),(spine,right,tip),
                          (root,left,lower),(root,lower,right),(left,tip,lower),(lower,tip,right)):
                    m.face(f,'banner' if j%3 else 'leaf')
        if m.lod==0:
            m.limb([(0,0,0,.016),(-.034,.16,.032,.015),(-.026,.272,.041,.012)],'leaf',sides=4)
            with m.transform(translate=(-.026,.268,.041),rotate_x=-16):
                for j in range(5):
                    with m.transform(rotate_y=72*j):
                        rim=(.081,.026,.013)
                        left,right=(.041,.016,-.032),(.038,.018,.037)
                        core=(.016,0,0)
                        m.face((core,right,rim,left),'ivory')
                        m.face((left,rim,right,(.016,-.012,0)),'flower')
                m.vessel(0,0,[(-.004,.030),(.019,.021)],['gold'],5)


def build(spec,lod):
    m=Tree(spec,lod)
    ground(m)
    trunk(m)
    portal(m)
    # Each branch ends inside its own crown, with spandrels under the two occupied boughs.
    boughs = [
        [(.00,.76,.12,.255),(.38,.99,.15,.182),(.65,1.16,.14,.153),(.84,1.52,.15,.120),
         (.98,1.92,.24,.082),(.97,2.06,.26,.063)],
        [(.0,1.23,.16,.237),(-.31,1.40,.08,.186),(-.65,1.48,-.04,.145),
         (-.86,1.76,-.05,.109),(-.91,2.15,.03,.091),(-.88,2.37,.03,.052)],
        [(-.12,1.71,.17,.189),(-.33,1.99,.48,.139),(-.58,2.25,.61,.095),(-.63,2.52,.66,.071)],
        [(-.08,1.83,.19,.194),(.26,2.11,.44,.152),(.43,2.44,.61,.112),(.47,2.69,.66,.063)],
        [(.08,2.38,.13,.116),(.26,2.50,-.10,.109),(.48,2.62,-.25,.080),(.55,2.74,-.32,.048)],
        [(.11,2.63,.09,.121),(-.16,2.68,-.075,.101),(-.39,2.78,-.18,.067),(-.48,2.88,-.20,.048)]]
    for bough in boughs:
        if lod==2:
            bough = [bough[0],bough[len(bough)//2],bough[-1]]
        m.limb(bough)
    perches=[(.86,-.122,1.18,.351,1),(-.79,-.28,1.51,.322,-1)]
    for x,z,y,r,sign in perches:
        m.limb([(x*.56,y-.233,z+.24,.113),(x*.78,y-.145,z+.103,.114),
                (x,y-.045,z,.096)], sides=(8,6,4)[lod])
        chapel(m,x,z,y,r,sign)
    paths(m)
    crowns=[
        ((.942,2.15,.258),(.444,.27,.47),.36,'leaf'),
        ((-.94,2.37,.05),(.477,.296,.452),.11,'leaf'),
        ((-.59,2.56,.637),(.69,.286,.52),.42,'roof'),
        ((.46,2.77,.618),(.72,.347,.542),.78,'leaf'),
        ((.55,2.66,-.29),(.55,.260,.429),.3,'leaf'),
        ((-.45,2.90,-.19),(.59,.297,.51),.64,'leaf')]
    for centre,size,phase,role in crowns:
        canopy(m,centre,size,phase,role)
    crescent(m)
    if lod < 2:
        lamp(m,[(.80,1.90,.16),(1.026,1.864,-.105),(1.133,1.79,-.267),(1.133,1.63,-.267)])
        lamp(m,[(-.87,2.15,.0),(-1.086,2.112,-.105),(-1.142,2.06,-.219),(-1.142,1.88,-.219)])
        herb_bed(m,.58,-.44,22)
        herb_bed(m,-.65,-.43,97)
    return m


def main():
    for spec in package_models(PACKAGE):
        levels=[build(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=336)
        root=Path(__file__).resolve().parents[4]
        ages=[age for age in AGES if age in spec.ages]
        images=[root/entry['age_previews'][age] for age in ages]
        contact_sheet(images,images[0].parent/'elf_wonders_ages.png',cols=3,cell=160,labels=ages)
        print(spec.id,entry['lod_triangles'])


if __name__=='__main__':
    main()
