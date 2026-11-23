"""The Orc house kit and the hide tents, kiln lodges and stone longhouses of the early clans."""
import math
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import MeshBuilder, publish
from forge.render import contact_sheet
from forge.atlas import palette

PACKAGE = "orc_housesancient"


class Lodge(MeshBuilder):
    def __init__(self, spec, lod):
        super().__init__(seed=spec.id)
        self.lod = lod
        self.v = spec.variant
        self.age = spec.ages[0]
        self.palette = palette(self.age)[0]

    def colour(self, role):
        return self.palette[role if role in self.palette else 'orc/' + role]

    def face(self, role, points):
        self.poly(points, self.colour(role), material=role)

    def block(self, role, center, size, taper=0, bottom=False):
        self.box(center, size, self.colour(role), material=role, taper=taper, bottom=bottom)

    def path(self, stations, roles, sides=6, cap=True):
        """Carve connected cross-sections perpendicular to a centre line (horns, bindings, poles)."""
        centers = np.array([p[:3] for p in stations], dtype=float)
        axis = centers[-1] - centers[0]
        guide = np.eye(3)[np.argmin(np.abs(axis))]
        rings = []
        for i, (_, _, _, radius) in enumerate(stations):
            direction = centers[min(i + 1, len(centers)-1)] - centers[max(0, i-1)]
            u = np.cross(guide, direction)
            u /= np.linalg.norm(u)
            v = np.cross(direction, u)
            v /= np.linalg.norm(v)
            rings.append([centers[i] + radius * (u*math.cos(a) + v*math.sin(a))
                          for a in (2*math.pi*j/sides for j in range(sides))])
        for i, (lower, upper) in enumerate(zip(rings, rings[1:])):
            for j in range(sides):
                k = (j+1) % sides
                if stations[i+1][-1] == 0:
                    self.face(roles[i], (lower[j], lower[k], centers[i+1]))
                else:
                    self.face(roles[i], (lower[j], lower[k], upper[k], upper[j]))
        if cap and stations[-1][-1] > 0:
            self.face(roles[-1], rings[-1])

    def pole(self, role, a, b, radius, top=None, sides=5):
        self.path([(*a, radius), (*b, radius if top is None else top)], [role], sides)

    def round(self, role, x, y, z, radius, height, top=None, sides=8):
        self.cylinder((x,y,z), radius, height, self.colour(role), top_radius=top,
                      segments=sides, material=role)

    def shell(self, courses, roles, cut=.43, marked=None, lid=True):
        """An octagonal loft. A chevron is sewn into the face, without coplanar paint decals."""
        plan = [(-1+cut,-1),(1-cut,-1),(1,-1+cut),(1,1-cut),
                (1-cut,1),(-1+cut,1),(-1,1-cut),(-1,-1+cut)]
        rings = [[np.array((x+a*w,y,z+b*d)) for a,b in plan] for y,w,d,x,z in courses]
        for i, (base, top) in enumerate(zip(rings, rings[1:])):
            for j in range(8):
                k = (j+1)%8
                a,b,c,d = base[k],base[j],top[j],top[k]
                if marked and marked[0] == i and j in marked[1]:
                    # Piece the V directly into the panel. Boundary vertices interpolate both edges.
                    e,f = b*.48+c*.52, a*.48+d*.52
                    mid = (a+b)*.43+(c+d)*.07
                    self.face(roles[i], (a,b,mid))
                    self.face(roles[i], (b,e,mid))
                    self.face(roles[i], (mid,f,a))
                    self.face(marked[2], (e,c,mid))
                    self.face(marked[2], (c,d,mid))
                    self.face(marked[2], (d,f,mid))
                else:
                    self.face(roles[i], (a,b,c,d))
        if lid:
            self.face(roles[-1], rings[-1][::-1])

    def horn(self, x, y, z, s, size=1, skin='ivory', socket='timber'):
        points = [(0,0,0,.055),(.055,.030,-.004,.051),(.104,.085,-.012,.036),
                  (.123,.157,-.017,.018),(.111,.211,-.014,0)]
        roles = [socket,skin,skin,skin]
        if self.lod == 2:
            points = [points[i] for i in (0,2,4)]
            roles = [skin,skin]
        self.path([(x+s*a*size,y+b*size,z+c*size,r*size) for a,b,c,r in points],
                  roles,(6,5,4)[self.lod])

    def doorway(self,x,front,y,h,width,body='timber',lining='door',horns=True):
        """Built-out entry surround. The rear embeds in the dwelling, the cut face faces south."""
        door_shape = [(-.5,0),(-.5,.73),(-.24,.98),(.24,.98),(.5,.73),(.5,0)]
        def portal(w, high, z, bottom):
            return [(x+w*a,bottom+high*b,z) for a,b in door_shape]
        outer = portal(width,h,front,y)
        inner = portal(width-.058,h-.030,front-.002,y+.003)
        inset = [(a,b,c+.026) for a,b,c in inner]
        rear = [(a,b,c+.135) for a,b,c in outer]
        for i in range(5):
            self.face(body,[outer[i],outer[i+1],inner[i+1],inner[i]])
            self.face('trim',[inner[i],inner[i+1],inset[i+1],inset[i]])
            self.face(body,[outer[i+1],outer[i],rear[i],rear[i+1]])
        self.face(lining,inset)
        if horns:
            for s in (-1,1):
                self.horn(x+s*(width*.34),y+h*.80,front+.038,s,.81)
        if self.lod==0:
            self.pole('accent',(x+width*.21,y+h*.37,front+.022),
                      (x+width*.21,y+h*.37,front+.009),.011,sides=6)

    def drum(self,x,z,r,h,base=0,head='hide',body='cloth'):
        n=(8,6,4)[self.lod]
        self.round(body,x,base,z,r*.73,h*.71,r*.96,n)
        self.round('trim',x,base+h*.71,z,r*.96,h*.13,r*1.04,n)
        self.round(head,x,base+h*.84,z,r*1.04,h*.16,r,n)
        if self.lod==0:
            for i in range(0,n,2):
                a=2*math.pi*i/n
                b=a+.50
                self.pole('thatch',(x+.75*r*math.cos(b),base+h*.09,z+.75*r*math.sin(b)),
                          (x+r*math.cos(a),base+h*.79,z+r*math.sin(a)),r*.085,sides=4)


def early_details(m,x,front,v):
    # Lantern peg on the jamb and family drum or a broad-shouldered storage pot beside the door.
    if m.lod == 1:
        m.round('hide',-.30 if v else .32,0,-.307,.060,.11,.073,6)
    if m.lod == 0:
        px=-.30 if v else .32
        m.drum(px,-.307,.078 if v else .070,.115,head='hide' if not v else 'thatch',
               body='cloth' if not v else 'timber')
        bx=x-.117
        m.block('timber',(bx,.21,front-.004),(.035,.051,.079))
        m.pole('emissive',(bx,.141,front-.045),(bx,.204,front-.045),.020,.025)
        m.round('trim',bx,.198,front-.045,.029,.015,sides=5)


def build(spec,lod):
    m=Lodge(spec,lod)
    age,v=m.age,m.v
    x=.055 if v else -.025
    z=.06 if v else .041
    w,d=(.353,.295) if v else (.345,.333)
    cut=(.19 if v else .50)
    wall={'dawn':.205,'bronze':.280,'iron':.335}[age] + (.035 if v else 0)
    h={'dawn':.64,'bronze':.67,'iron':.75}[age] - (.047 if v else 0)
    ground=.065 if age=='iron' else .046
    def band(y,ww,dd,cx=x,cz=z):
        return (y,ww,dd,cx,cz)
    if lod < 2:
        m.shell([band(0,w+.022,d+.020),band(ground,w+.013,d+.012)],
                ['fieldstone' if age!='dawn' else 'soil'],cut)
    else:
        ground=0
    wallrole={'dawn':'hide','bronze':'brick','iron':'fieldstone'}[age]
    m.shell([band(ground,w,d),band(wall,w*.969,d*.974)], [wallrole],cut)
    wr,dr=w+.051,d+.046
    roof={'dawn':('thatch' if not v else 'hide'),'bronze':('hide' if not v else 'roof_tile'),
          'iron':('slate' if not v else 'roof')}[age]
    ridge=.17 if v else .042
    if age=='dawn':
        courses=[band(wall-.018,wr,dr),band(wall+.020,wr*.99,dr*.99),
                 band(wall+.126,wr*.72,dr*.71),band(h,ridge,.045,x+(.035 if v else -.025),z+.019)]
        roles=['hide',roof,roof]
    elif age=='bronze' and not v:
        courses=[band(wall-.025,wr,dr),band(wall+.017,wr,dr),band(wall+.117,wr*.89,dr*.89),
                 band(h-.075,.164,.152),band(h,.067,.060)]
        roles=['trim','brick',roof,'hide']
    elif age=='bronze':
        courses=[band(wall-.016,wr,dr),band(wall+.041,.376,.301),band(wall+.084,.329,.255),
                 band(h-.069,.239,.109,x+.035,z+.01),band(h,ridge,.066,x+.041,z+.012)]
        roles=['timber','brick',roof,'hide']
    else:
        courses=[band(wall-.024,wr,dr),band(wall+.028,wr*.991,dr*.991),
                 band(wall+.118,wr*.75,dr*.78),band(h,ridge,.052,x+(.026 if v else -.019),z+.023)]
        roles=['trim',roof,roof]
    if lod==2:
        courses=[courses[0],courses[-2],courses[-1]]
        roles=[roof,roof]
    m.shell(courses,roles,cut,marked=(len(roles)-1,{0,2,4,6},'cloth'))
    # The crown is an open, dark smoke pot held between two bound roof spars.
    _,rw,rd,rx,rz=courses[-1]
    if lod<2 and ((age=='bronze' and v) or age=='iron'):
        m.shell([band(h-.002,rw*.94,rd*.90,rx,rz),band(h+.045,rw*.91,rd*.87,rx,rz)],
                ['metal' if age=='iron' else 'trim'],cut)
    if lod==2:
        m.pole('timber',(rx,h-.01,rz),(rx+.031,h+.085,rz+.006),.026,.016,4)
    elif v:
        for s in (-1,1):
            m.pole('timber',(rx+s*rw*.65,h-.054,rz),(rx+s*(rw+.045),h+.073,rz+.020),.026,.016)
    else:
        for s in (-1,1):
            m.pole('timber',(rx-s*.075,h-.132,rz-s*.018),(rx+s*.040,h+.091,rz+s*.012),.024,.014)
    ex=x+(-.085 if v else .0)
    front=z-d-.095
    m.doorway(ex,front,.016,wall+.085,.256 if v else .248)
    early_details(m,ex,front,v)
    if lod<2:
        # A lifted hide flap or paired shutters catch the light on the domestic side wall.
        with m.transform(translate=(x+w-.003,wall*.52,z-.007),rotate_y=-90):
            m.block('trim',(0,0,-.004),(.165,.095,.029))
            m.block('emissive',(0,.002,-.020),(.132,.069,.009))
            if lod==0:
                m.block('timber',(.009,.003,-.028),(.014,.072,.012))
                with m.transform(translate=(0,.054,-.029),rotate_x=-21):
                    m.block('hide' if age=='dawn' else 'roof',(0,0,0),(.181,.018,.080),bottom=True)
    if m.lod<2:
        # Stakes sit against the two nearest clipped corners, binding roof and sill together.
        for s in (-1,1):
            px=x+s*(w*(1-cut))
            m.pole('timber',(px,.012,z-d-.007),(px+s*.017,wall+.033,z-d+.005),.025,.022)
    if m.lod==0:
        if age=='dawn':
            # An extra sloped rafter and lashings behind the house suggest the tent's tension frame.
            for s in (-1,1):
                m.pole('timber',(x+s*(w+.026),.012,z+.061),(rx+s*ridge*.55,h+.012,rz),.015,.012)
        else:
            m.shell([(.013,.087,.033,ex,front-.023),(.034,.083,.031,ex,front-.023)],['fieldstone'])
            m.round('hide',-.405,0,.429,.056,.105,.065,7)
            m.round('thatch',-.405,.105,.429,.065,.025,.039,7)
    return m


def generate(package, builder):
    cards=[]
    for spec in package_models(package):
        levels=[builder(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=256)
        for age,path in entry['age_previews'].items():
            label=age.title() if spec.kind=='wonder' else f'{age.title()}  /  House {spec.variant+1}'
            cards.append((spec.variant,AGES.index(age),ROOT/path,label))
        print(spec.id, entry['lod_triangles'],flush=True)
    cards.sort()
    images=[c[2] for c in cards]
    labels=[c[3] for c in cards]
    columns=5 if len(cards)==10 else len(cards)//2
    contact_sheet(images,images[0].parent/'contact_sheet.png',cols=columns,cell=184,labels=labels)


if __name__=='__main__':
    generate(PACKAGE,build)
