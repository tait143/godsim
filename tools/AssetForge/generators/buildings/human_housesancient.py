"""Six village households from the first reed harvest through the Iron Age."""
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish
from forge.atlas import palette


class House(MeshBuilder):
    def __init__(self, model, lod):
        super().__init__(model.id)
        self.lod = lod
        self.age = model.ages[0]
        self.colors = palette(model.ages[0])[0]

    def color(self, mat):
        return self.colors.get(mat) or self.colors[f'human/{mat}']

    def block(self, mat, center, size, *, taper=0):
        self.box(center, size, self.color(mat), material=mat, taper=taper)

    def face(self, mat, points):
        self.poly(points, self.color(mat), mat)

    def beam(self, mat, start, end, width, depth=None):
        # Two square end-grain faces and four lengthwise faces for an unsawn post or rafter.
        a, b = np.asarray(start), np.asarray(end)
        along = (b-a) / np.linalg.norm(b-a)
        ref = (0, 0, 1) if abs(along[2]) < .9 else (0, 1, 0)
        side = np.cross(along, ref)
        side /= np.linalg.norm(side)
        up = np.cross(along, side)
        s, t = side * width/2, up * (depth or width)/2
        lo = [a-s-t, a+s-t, a+s+t, a-s+t]
        hi = [p+b-a for p in lo]
        self.face(mat, lo[::-1])
        self.face(mat, hi)
        for i in range(4):
            j = (i+1) % 4
            self.face(mat, (lo[i],lo[j],hi[j],hi[i]))

    def bent_rail(self, mat, path, z, width):
        """Sweep a square section along a left-to-right path with mitered corners."""
        centres = np.asarray(path, dtype=float)
        spans = np.diff(centres, axis=0)
        tangents = spans / np.linalg.norm(spans, axis=1)[:, None]
        normals = np.column_stack((-tangents[:, 1], tangents[:, 0]))
        sections = []
        for i, centre in enumerate(centres):
            a, b = normals[max(i-1,0)], normals[min(i,len(normals)-1)]
            offset = (a+b) * (width/2 / (1 + np.dot(a,b)))
            upper, lower = centre+offset, centre-offset
            sections.append([(*upper,z-width/2),(*lower,z-width/2),
                             (*lower,z+width/2),(*upper,z+width/2)])
        self.face(mat, sections[0])
        self.face(mat, sections[-1][::-1])
        for left,right in zip(sections,sections[1:]):
            for i in range(4):
                j = (i+1)%4
                self.face(mat,(left[j],left[i],right[i],right[j]))

    def course(self, mat, x, z, w, d, base, top, corner=.035, inset=0):
        """Chamfered masonry, with its own foundation batter."""
        if self.lod == 2 or (self.lod == 1 and base > .001):
            self.block(mat,(x,(base+top)/2,z),(w,top-base,d),taper=inset*2/w)
            return
        def corners(y, cut):
            a, b = w/2-cut, d/2-cut
            k = min(corner, a*.5, b*.5)
            outline = [(-a+k,-b),(a-k,-b),(a,-b+k),(a,b-k),
                       (a-k,b),(-a+k,b),(-a,b-k),(-a,-b+k)]
            return [(x+u,y,z+v) for u,v in outline]
        lo, hi = corners(base,0), corners(top,inset)
        for i in range(8):
            j = (i+1) % 8
            self.face(mat, (lo[j],lo[i],hi[i],hi[j]))
        self.face(mat, hi[::-1])

    def roof(self, w, d, eaves, rise, mat, end_mat, hip=0, ridge_mat=None):
        """Longitudinal ridge above separately formed laps, eave ends and hip planes.

        A positive hip retracts both ridge ends into the plan. The eaves always
        reach the same wall plate through the three levels of detail.
        """
        a, b = w/2, d/2
        thatched = mat == 'thatch'
        shakes = self.age == 'iron' and mat == 'roof'
        if self.lod == 0 and thatched:
            profile = [(1,0),(.64,.32),(.643,.337),(.30,.68),(.303,.698),(0,1)]
        elif self.lod == 0 and shakes:
            profile = [(1,0),(.66,.313),(.678,.332),(.33,.634),(.348,.654),(0,1)]
        elif self.lod < 2:
            profile = [(1,0),(.54,.412),(0,1)]
        else:
            profile = [(1,0),(0,1)]
        length = [-1,-.36,.35,1] if self.lod == 0 and thatched else [-1,1]
        if self.lod == 0 and mat == 'slate':
            length = [-1,-.06,1]
        thick = .035 if thatched else .026

        def point(sign, u, v, station):
            sag = (1-abs(station)) * (.013 if thatched else .005)
            return (sign*a*u, eaves+rise*v-sag, station*(b-hip*v))

        for sign in (-1,1):
            for k in range(len(profile)-1):
                u,v = profile[k]
                nu,nv = profile[k+1]
                for s,t in zip(length,length[1:]):
                    polygon = [point(sign,u,v,s), point(sign,u,v,t),
                               point(sign,nu,nv,t), point(sign,nu,nv,s)]
                    self.face(mat, polygon[::-1] if sign == 1 else polygon)
                if self.lod == 0 and shakes and nu < u:
                    # Interrupted butt joints distinguish split shakes from continuous
                    # boards. Each course has a half-shingle offset from the next.
                    for s in ((-.62,.04,.66) if k%4 == 0 else (-.34,.34)):
                        x,y,z = point(sign,u,v,s)
                        x2,y2,z2 = point(sign,nu,nv,s)
                        stop = .48 if k == 0 else .63
                        ex,ey = x+(x2-x)*stop,y+(y2-y)*stop
                        joint = [(x+sign*.0015,y+.0015,z-.0024),
                                 (x+sign*.0015,y+.0015,z+.0024),
                                 (ex+sign*.0015,ey+.0015,z2)]
                        self.face('trim',joint[::-1] if sign == 1 else joint)
            for s,t in zip(length,length[1:]):
                p,q = point(sign,1,0,s),point(sign,1,0,t)
                low = lambda p: (p[0]-sign*.009,p[1]-thick,p[2])
                strip = [q,p,low(p),low(q)]
                self.face('hide' if thatched else 'trim', strip[::-1] if sign == 1 else strip)
        for station in (-1,1):
            if hip:
                for (u,v),(nu,nv) in zip(profile,profile[1:]):
                    band = [point(1,u,v,station),point(-1,u,v,station),
                            point(-1,nu,nv,station)]
                    if nu:
                        band.append(point(1,nu,nv,station))
                    self.face(mat, band if station < 0 else band[::-1])
                p,q = point(1,1,0,station),point(-1,1,0,station)
                strip = [q,p,(p[0]-.009,p[1]-thick,p[2]-station*.009),
                         (q[0]+.009,q[1]-thick,q[2]-station*.009)]
                self.face('hide' if thatched else 'trim', strip if station < 0 else strip[::-1])
            elif any(nu > u for (u,_),(nu,_) in zip(profile,profile[1:])):
                # Roof laps turn outward as they climb. Horizontal slices are disjoint
                # even at the projecting lips, unlike a fan from the centre of the loft.
                outline = [(a-.009,eaves-thick)]+[(a*u,eaves+rise*v) for u,v in profile]
                front, inset = station*b, station*(b-.021)
                for (radius,y),(upper,top) in zip(outline,outline[1:]):
                    panel = [(radius,y,inset),(-radius,y,inset),(-upper,top,inset)]
                    if upper:
                        panel.append((upper,top,inset))
                    self.face(end_mat, panel if station < 0 else panel[::-1])
                    for sign in (-1,1):
                        strip = [(sign*radius,y,front),(sign*(radius-thick),y,front),
                                 (sign*max(0,upper-thick),top,front)]
                        if upper:
                            strip.append((sign*upper,top,front))
                        self.face('hide' if thatched else 'trim',
                                  strip if sign*station < 0 else strip[::-1])
            else:
                z = station*(b-.021)
                # The inset front closes the loft. Bargeboards follow the roof edge.
                for sign in (-1,1):
                    for (u,v),(nu,nv) in zip(profile,profile[1:]):
                        p,q = point(sign,u,v,station),point(sign,nu,nv,station)
                        strip = [p,q,(q[0],q[1]-thick,q[2]),(p[0],p[1]-thick,p[2])]
                        self.face('hide' if thatched else 'trim',strip if sign*station > 0 else strip[::-1])
                    center = (0,eaves-.026,z)
                    for (u,v),(nu,nv) in zip(profile,profile[1:]):
                        gable = [(sign*a*u,eaves+rise*v-thick*.78,z),
                                 (sign*a*nu,eaves+rise*nv-thick*.78,z),center]
                        self.face(end_mat, gable if sign*station > 0 else gable[::-1])
        if ridge_mat:
            self.beam(ridge_mat,(0,eaves+rise-.006,-b+hip-.01),
                      (0,eaves+rise-.004,b-hip+.01),.038 if thatched else .030)

    def entry(self, x, front, w, base, h, lintel='trim', curtain=False):
        self.face('door',[(x+w/2,base,front-.006),(x-w/2,base,front-.006),
                          (x-w/2,base+h,front-.006),(x+w/2,base+h,front-.006)])
        if self.lod < 2:
            for edge in (-1,1):
                self.block(lintel,(x+edge*(w/2+.008),base+h/2,front-.012),(.029,h+.012,.034))
            self.block(lintel,(x,base+h+.009,front-.016),(w+.077,.038,.044))
        if curtain:
            self.face('banner',[(x+.45*w,base+.32*h,front-.016),(x+.06*w,base+.49*h,front-.021),
                                (x-.24*w,base+h,front-.020),(x+.45*w,base+h,front-.018)])
        if self.lod == 0 and not curtain:
            self.block('gold',(x-.31*w,base+h*.47,front-.019),(.023,.027,.019))
        self.block('fieldstone',(x,base*.48,front-.032),(w+.082,base*.96,.109),taper=.06)

    def light(self, x, y, front, w, h, shutters=True):
        # Warm light behind a deeply framed opening. All offsets point out of the wall.
        def pane(mat,cx,cy,z,w,h):
            self.face(mat,[(cx+w/2,cy-h/2,z),(cx-w/2,cy-h/2,z),
                           (cx-w/2,cy+h/2,z),(cx+w/2,cy+h/2,z)])
        pane('door',x,y,front-.005,w+.022,h+.022)
        pane('emissive',x,y,front-.010,w,h)
        if self.lod == 0:
            self.block('trim',(x,y,front-.016),(.014,h+.008,.016))
        if shutters and self.lod < 2:
            for edge in (-1,1):
                self.block('banner',(x+edge*(w*.5+.022),y,front-.010),
                           (.034,h*.96,.023))
            self.block('timber',(x,y-h/2-.012,front-.010),(w+.104,.026,.044))

    def jar(self, x, z, ground, r, h):
        n = 7 if self.lod == 0 else 5
        profile = [(0,.59),(.18,.87),(.57,1),(.82,.58),(.96,.56),(1,.71)]
        if self.lod:
            profile = [(0,.59),(.56,1),(.88,.52),(1,.66)]
        def ring(y,k):
            return [(x+r*k*math.cos(i*2*math.pi/n),ground+h*y,
                     z+r*k*math.sin(i*2*math.pi/n)) for i in range(n)]
        rings = [ring(a,b) for a,b in profile]
        for a,b in zip(rings,rings[1:]):
            for i in range(n):
                j = (i+1)%n
                self.face('roof_tile',(a[j],a[i],b[i],b[j]))
        inner = ring(1,.46)
        for i in range(n):
            j = (i+1)%n
            self.face('hide',(rings[-1][i],inner[i],inner[j],rings[-1][j]))
        self.face('door',inner[::-1])

    def basket(self, x, z, r, h):
        self.cylinder((x,0,z),r*.74,h,self.color('hide'),segments=7,
                      top_radius=r,material='hide')
        self.cylinder((x,h-.008,z),r*1.06,.010,self.color('thatch'),segments=7,
                      material='thatch',top=self.color('soil'))
        self.cylinder((x,h+.0025,z),r*.82,.002,self.color('soil'),segments=7,
                      material='soil')
        self.bent_rail('timber',[(x-r*.82,h-.002),(x-r*.46,h+r*.85),
                                 (x+r*.46,h+r*.85),(x+r*.82,h-.002)],z,.010)

    def rear_vent(self, x, z, y, w, h, slats=2):
        if self.lod == 2:
            return
        # Rear opening and slats face +Z. A different offset at every layer keeps them legible.
        self.face('door',[(x-w/2,y-h/2,z),(x+w/2,y-h/2,z),
                          (x+w/2,y+h/2,z),(x-w/2,y+h/2,z)])
        for i in range(slats):
            cx = x+w*((i+1)/(slats+1)-.5)
            self.face('timber',[(cx-.007,y-h/2,z+.003),(cx+.007,y-h/2,z+.003),
                                (cx+.007,y+h/2,z+.003),(cx-.007,y+h/2,z+.003)])

    def herbs(self, x, y, front, w):
        self.block('timber',(x,y,front-.033),(w,.047,.072),taper=.055)
        if self.lod:
            return
        for i in range(3):
            cx,cz = x+(i-1)*w*.29,front-.043
            cy = y+.05+.008*(i%2)
            self.face('leaf',[(cx+.027,cy+.009,cz),(cx,cy-.016,cz-.011),
                              (cx-.023,cy+.022,cz+.003)])
            self.face('leaf',[(cx+.018,cy+.018,cz-.006),(cx,cy-.010,cz-.013),
                              (cx-.009,cy+.033,cz+.007)])
            col = 'flower' if i != 1 else 'ivory'
            self.cylinder((cx,cy+.014,cz-.005),.012,.013,self.color(col),
                          segments=5,top_radius=.019,material=col)

    def flue(self, x, z, bottom, top, w):
        with self.transform(translate=(x,bottom,z),rotate_z=-3.5):
            h = top-bottom
            self.block('fieldstone',(0,h*.47,0),(w,h*.94,w*.92),taper=.13)
            self.block('fieldstone',(0,h*.946,0),(w*1.12,h*.132,w*1.06))
            self.face('door',[(-w*.32,h*1.013,-w*.29),(-w*.32,h*1.013,w*.29),
                              (w*.32,h*1.013,w*.29),(w*.32,h*1.013,-w*.29)])
            if self.lod == 0:
                self.block('trim',(0,h*.848,0),(w*.996,.018,w*.934))


def dawn_croft(m):
    # The pole-bound gable of a household that gathers reeds beside the river.
    m.course('soil',0,.006,.765,.690,0,.059,.068)
    m.course('plaster',0,0,.704,.626,.051,.339,.044,.006)
    m.roof(.845,.747,.355,.394,'thatch','plaster',ridge_mat='timber')
    m.entry(-.077,-.313,.158,.057,.251,curtain=True)
    m.light(.196,.213,-.310,.082,.098,False)
    m.rear_vent(.071,.313,.210,.128,.110)
    m.bent_rail('trim',[(-.316,.344),(0,.713),(.316,.344)],-.355,.035)
    if m.lod < 2:
        with m.transform(translate=(.349,0,.012),rotate_y=-90):
            m.light(0,.214,-.006,.107,.104)
        for x,z in ((-.321,-.291),(.318,-.290),(.33,.282)):
            m.beam('timber',(x,.046,z),(x*1.025,.343,z*1.02),.041)
        m.block('timber',(0,.347,-.354),(.696,.036,.043))
        m.block('timber',(0,.499,-.352),(.030,.270,.031))
    if m.lod == 0:
        m.herbs(.198,.139,-.312,.137)
        m.basket(-.290,-.362,.058,.088)
        # Bent hazel ties loop over the ridge pole.
        for z in (-.21,.15):
            m.bent_rail('hide',[(-.080,.664),(0,.765),(.080,.664)],z,.021)


def dawn_sheaf(m):
    # Eave-entry hip and a calf-hide rain hood, carried by two forked uprights.
    m.course('soil',0,.079,.850,.624,0,.050,.076)
    m.course('hide',0,.078,.78,.55,.043,.338,.045,.009)
    with m.transform(translate=(0,0,.078),rotate_y=90):
        m.roof(.688,.898,.348,.364,'thatch','hide',hip=.211,ridge_mat='timber')
    m.entry(.138,-.197,.185,.049,.237)
    m.light(-.218,.188,-.196,.097,.083)
    m.rear_vent(-.178,.354,.204,.120,.090)
    for x in (.015,.259):
        m.beam('timber',(x,.006,-.418),(x+.005,.240,-.413),.034)
    p=[(-.029,.237,-.450),(.297,.231,-.450),(.304,.365,-.195),(-.037,.368,-.195)]
    m.face('hide',p[::-1])
    m.face('timber',[p[1],p[0],(-.029,.216,-.445),(.297,.210,-.445)][::-1])
    if m.lod < 2:
        for x in (-.335,-.080,.332):
            m.beam('timber',(x,.041,-.192),(x+.009,.332,-.194),.034)
        if m.lod == 0:
            for x in (.004,.266):
                m.beam('timber',(x,.228,-.432),(x,.354,-.187),.028)
        with m.transform(translate=(.387,0,.09),rotate_y=-90):
            m.light(0,.20,-.004,.091,.085,False)
        m.herbs(-.221,.121,-.200,.154)
    if m.lod == 0:
        # A tied fan of reeds dries on the warm side of the hut.
        with m.transform(translate=(.440,.010,.227),rotate_z=12):
            m.cylinder((0,0,0),.046,.094,m.color('thatch'),segments=7,
                       top_radius=.021,material='thatch',cap_top=False)
            m.cylinder((0,.094,0),.021,.091,m.color('thatch'),segments=7,
                       top_radius=.055,material='thatch')
            m.cylinder((0,.082,0),.027,.023,m.color('hide'),segments=7,
                       top_radius=.028,material='hide')
            for i,delta in enumerate((-.032,-.010,.022)):
                m.beam('thatch',(delta,.176,0),(delta*1.47,.208+i*.008,.006),.014)
        m.face('banner',[(.286,.212,-.450),(.229,.174,-.451),(.196,.222,-.451),(.286,.227,-.451)])


def bronze_oven(m):
    # Limewash protects the lower mudbrick. A clay bread oven adjoins the gable house.
    cx, cz = -.083,.030
    m.course('fieldstone',cx,cz,.66,.72,0,.066,.055)
    m.course('brick',cx,cz,.609,.650,.054,.453,.027,.007)
    m.course('plaster',cx,cz,.616,.657,.070,.168,.029,.004)
    with m.transform(translate=(cx,0,cz)):
        m.roof(.714,.763,.450,.284,'roof','plaster',ridge_mat='metal')
    m.entry(-.150,-.295,.161,.069,.274)
    m.light(.086,.295,-.293,.074,.095)
    m.rear_vent(-.135,.356,.298,.142,.117)
    m.block('trim',(.306,.064,.103),(.270,.128,.330),taper=.055)
    with m.transform(translate=(.305,.123,.102),scale=(1,1,1.17)):
        m.dome((0,0,0),.142,m.color('plaster'),rings=3 if m.lod==0 else 2,
               segments=10 if m.lod==0 else 6,squash=1.23,material='plaster')
    m.block('brick',(.307,.141,-.043),(.145,.114,.077),taper=.08)
    m.face('door',[(.353,.095,-.084),(.261,.095,-.084),(.267,.155,-.084),
                   (.306,.179,-.084),(.347,.155,-.084)])
    if m.lod < 2:
        m.block('timber',(cx,.441,-.298),(.60,.044,.05))
        for s in (-1,1):
            m.beam('trim',(cx+s*.285,.451,-.331),(cx,.708,-.332),.031)
        m.bent_rail('metal',[(cx-.057,.539),(cx,.609),(cx+.057,.539)],-.332,.026)
        m.face('door',[(cx+.044,.540,-.333),(cx-.044,.540,-.333),(cx,.596,-.333)])
        m.jar(.349,-.217,0,.062,.152)
    if m.lod == 0:
        m.herbs(.086,.216,-.300,.128)
        for x,y in ((-.354,.251),(-.324,.358),(.162,.192)):
            m.block('wall',(x,y,-.294),(.057,.028,.012))
        # Low raised clay edge beneath the oven mouth doubles as a baking shelf.
        m.block('roof_tile',(.306,.084,-.088),(.186,.027,.067))


def bronze_terrace(m):
    # A roof terrace for drying grain adjoins a high, hipped sleeping chamber.
    m.course('fieldstone',0,.029,.839,.669,0,.056,.053)
    m.course('wall',-.177,.023,.426,.593,.050,.419,.024,.006)
    m.course('brick',.176,.053,.387,.622,.047,.515,.031,.010)
    with m.transform(translate=(.174,0,.054)):
        m.roof(.503,.719,.512,.226,'roof','plaster',hip=.134,ridge_mat='metal')
    m.block('timber',(-.182,.407,.022),(.469,.055,.646))
    m.block('plaster',(-.182,.443,.024),(.486,.055,.651))
    m.block('brick',(-.182,.488,.326),(.466,.061,.036))
    m.block('brick',(-.286,.488,-.279),(.257,.061,.036))
    m.block('brick',(.016,.488,-.279),(.068,.061,.036))
    m.block('brick',(-.405,.487,.022),(.035,.059,.580))
    m.entry(.183,-.255,.154,.056,.269,curtain=True)
    m.light(-.267,.231,-.272,.108,.104)
    m.rear_vent(.177,.365,.320,.129,.118)
    if m.lod < 2:
        for x in (-.34,-.205,-.07):
            m.block('timber',(x,.395,-.306),(.036,.044,.083))
        with m.transform(translate=(.369,0,.045),rotate_y=-90):
            m.light(0,.298,-.006,.098,.127)
        m.jar(-.285,.088,.471,.062,.144)
        m.block('timber',(.18,.411,-.254),(.335,.041,.048))
    if m.lod == 0:
        m.jar(-.151,.013,.471,.047,.106)
        m.face('thatch',[(-.365,.472,-.207),(-.366,.472,-.036),
                         (-.147,.472,-.027),(-.149,.472,-.204)])
        m.herbs(-.267,.145,-.276,.165)
        for x in (-.144,-.032):
            m.beam('timber',(x,.005,-.418),(x,.500,-.280),.026)
        for i in range(4):
            t = .19+.20*i
            m.beam('timber',(-.144,.005+.495*t,-.418+.138*t),
                   (-.032,.005+.495*t,-.418+.138*t),.018)
    else:
        # The terrace opening and its access survive at village-view distance.
        for x in (-.144,-.032):
            m.face('timber',[(x+.013,.005,-.422),(x-.013,.005,-.422),
                             (x-.013,.500,-.284),(x+.013,.500,-.284)])
        for t in (.21,.48,.76):
            y,z = .005+.495*t,-.421+.138*t
            m.face('timber',[(-.032,y-.011,z),(-.144,y-.011,z),
                             (-.144,y+.011,z+.005),(-.032,y+.011,z+.005)])


def iron_hearth(m):
    # A massive wall hearth supports the canted flue on the long side.
    cx = -.050
    m.course('fieldstone',cx,.015,.757,.692,0,.069,.040)
    m.course('wall',cx,.015,.695,.638,.060,.423,.031,.004)
    with m.transform(translate=(cx,0,.014)):
        m.roof(.812,.734,.431,.397,'slate','plaster',ridge_mat='trim')
    m.entry(-.140,-.304,.161,.067,.253)
    m.light(.139,.264,-.302,.100,.109)
    m.rear_vent(-.110,.336,.258,.156,.108,3)
    m.course('fieldstone',.304,.123,.211,.253,.003,.350,.033,.014)
    m.flue(.301,.123,.158,.876,.143)
    if m.lod < 2:
        m.block('trim',(cx,.410,-.303),(.666,.040,.044))
        m.beam('trim',(cx,.438,-.335),(cx,.784,-.335),.039)
        for s in (-1,1):
            m.beam('trim',(cx+s*.275,.437,-.335),(cx+s*.011,.705,-.335),.030)
        m.herbs(.143,.184,-.307,.155)
    if m.lod == 0:
        with m.transform(translate=(.294,0,-.142),rotate_y=-90):
            m.light(0,.244,-.006,.075,.089,False)
        # A neatly cribbed stack of wood at the sheltered foot of the hearth.
        for y,z in ((.035,-.051),(.035,-.114),(.086,-.083)):
            with m.transform(translate=(.342,y,z),rotate_z=-89):
                m.cylinder((0,0,0),.033,.111,m.color('timber'),segments=6,
                           cap_bottom=True,material='timber')
                m.cylinder((0,.110,0),.024,.003,m.color('hide'),segments=6,material='hide')
        for x,y in ((-.331,.187),(-.282,.29),(.201,.111)):
            m.block('fieldstone',(x,y,-.304),(.067,.035,.015),taper=.06)


def iron_crosshall(m):
    # A split-shingle hall and a projecting whitewashed entry gable.
    m.course('fieldstone',0,.090,.830,.593,0,.062,.051)
    m.course('fieldstone',0,.090,.765,.537,.054,.399,.026,.006)
    with m.transform(translate=(0,0,.090),rotate_y=90):
        m.roof(.657,.883,.402,.323,'roof','plaster',ridge_mat='trim')
    x=-.121
    m.course('fieldstone',x,-.239,.396,.274,.003,.351,.022,.005)
    with m.transform(translate=(x,0,-.200)):
        m.roof(.484,.428,.353,.245,'roof','plaster',ridge_mat='trim')
    m.entry(x,-.376,.167,.058,.249)
    m.light(.230,.249,-.178,.123,.109)
    m.rear_vent(-.21,.359,.250,.144,.109,3)
    m.flue(.208,.169,.380,.795,.115)
    if m.lod < 2:
        m.herbs(.229,.166,-.183,.181)
        m.block('trim',(x,.344,-.397),(.407,.032,.043))
        m.bent_rail('trim',[(x-.174,.357),(x,.554),(x+.174,.357)],-.392,.032)
        with m.transform(translate=(.380,0,.090),rotate_y=-90):
            m.light(0,.233,-.007,.117,.116)
    if m.lod == 0:
        for px in (-.377,.375):
            m.beam('trim',(px,.060,-.145),(px,.396,-.145),.041)
        # A small horn lantern hangs from the wall beside the entry wing.
        m.block('trim',(-.353,.314,-.221),(.029,.028,.111))
        m.block('metal',(-.353,.279,-.264),(.014,.052,.016))
        m.cylinder((-.353,.184,-.264),.029,.069,m.color('emissive'),segments=4,
                   top_radius=.039,material='emissive')
        m.cylinder((-.353,.251,-.264),.046,.020,m.color('metal'),segments=4,
                   top_radius=.018,material='metal')
        m.cylinder((-.353,.170,-.264),.019,.014,m.color('metal'),segments=4,
                   top_radius=.033,material='metal')
        for s in (-1,1):
            m.beam('trim',(s*.423,.400,-.127),(s*.423,.642,.090),.034)
        # The king post sits on the tie beam. Its two shoulders meet the
        # underside of the mitered rafters, with a small reveal at the face.
        pitch = (.554-.357)/.174
        peak = .554-.016*math.hypot(1,pitch)
        shoulder = peak-.014*pitch
        section = [(x+.014,.355),(x-.014,.355),(x-.014,shoulder),
                   (x,peak),(x+.014,shoulder)]
        front = [(px,py,-.405) for px,py in section]
        back = [(px,py,-.384) for px,py in section]
        m.face('timber',front)
        m.face('timber',back[::-1])
        for i in range(len(front)):
            j = (i+1)%len(front)
            m.face('timber',(front[j],front[i],back[i],back[j]))


BUILDERS = {('dawn',0): dawn_croft, ('dawn',1): dawn_sheaf,
            ('bronze',0): bronze_oven, ('bronze',1): bronze_terrace,
            ('iron',0): iron_hearth, ('iron',1): iron_crosshall}


def build(model, lod):
    mesh = House(model,lod)
    BUILDERS[model.ages[0],model.variant](mesh)
    return mesh


if __name__ == '__main__':
    for model in package_models('human_housesancient'):
        high, mid, low = (build(model,lod) for lod in range(3))
        result = publish(high, model.metadata(), model.package, lods=(mid,low), preview_size=240)
        print(result['id'], result['lod_triangles'])
