"""Human scholars and healers, from a herb-drying garden to a pearl diagnostic tower."""
from math import cos, pi, sin
from pathlib import Path
import sys
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import publish
from generators.buildings.human_housesancient import House

PACKAGE = 'human_learninghealth'
ERAS = ('ancient', 'classical', 'industrial', 'future')


class Mason(House):
    def __init__(self, spec, lod):
        super().__init__(spec, lod)
        self.era = ERAS.index(spec.band)

    def solid(self, mat, x, z, w, d, bottom, top):
        self.block(mat, (x, (bottom+top)/2, z), (w, top-bottom, d))

    def drum(self, mat, x, z, r, bottom, top, upper=None, n=None, cap=True):
        self.cylinder((x,bottom,z), r, top-bottom, self.color(mat),
                      segments=n or (14,10,6)[self.lod], top_radius=upper, cap_top=cap, material=mat)

    def cupola(self, mat, x, z, r, base, rise):
        self.dome((x,base,z), r, self.color(mat), rings=(4,3,2)[self.lod],
                  segments=(14,10,6)[self.lod], squash=rise/r, material=mat)

    def hip(self, x, z, w, d, y, h, mat='roof'):
        self.hip_roof((x,y,z), w, d, h, self.color(mat), overhang=0, ridge=.92, material=mat)

    def front_panel(self, mat, x, y, z, w, h):
        self.face(mat, [(x+w/2,y,z),(x-w/2,y,z),(x-w/2,y+h,z),(x+w/2,y+h,z)])

    def pennon(self, x, z, y, length=.27):
        self.solid('trim',x,z,.026,.026,y,y+length+.07)
        top = y+length+.055
        a,b,c,d = (x,top,z),(x+.095,top-.009,z-.018),(x+.205,top-.06,z+.004),(x+.094,top-.106,z-.018)
        low = (x,top-.091,z)
        for patch in ([a,b,d,low],[b,c,d]):
            self.face('banner',patch)
            self.face('banner',[(u,v,w+.004) for u,v,w in reversed(patch)])

    def windows(self, x, z, y, w, h, count):
        # Stained or leaded casements on a continuous sill, with a separate relief mullion.
        for i in range(count):
            cx=x+w*((i+.5)/count-.5)
            bw=w/count*.72
            self.front_panel('door',cx,y,z,bw+.024,h+.028)
            self.front_panel('glass' if self.era>1 else 'emissive',cx,y+.016,z-.004,bw,h)
            if self.lod==0:
                self.solid('trim',cx,z-.009,.020,.015,y+.014,y+h+.016)
        if self.lod<2:
            self.solid('ivory' if self.era>0 else 'timber',x,z-.014,w+.035,.061,y-.024,y+.008)

    def hall(self, x, z, w, d, h, *, base=.064, rise=.33, roof=True, windows=True):
        era=self.era
        material=('wall','wall','brick','wall')[era]
        self.course('fieldstone',x,z,w+.036,d+.036,base-.032,base+.047,.036)
        self.solid(material,x,z,w,d,base+.035,h)
        if roof:
            self.solid('trim' if era<3 else 'ivory',x,z,w+.068,d+.068,h-.043,h+.013)
            if era<=1:
                self.hip(x,z,w+.115,d+.115,h+.01,rise)
            elif era==2:
                self.gable_roof((x,h+.012,z),w+.087,d+.087,rise*.61,self.color('slate'),
                                overhang=0,material='slate')
            else:
                self.solid('glass',x,z,w*.90,d*.88,h+.013,h+.12)
                self.hip(x,z,w*.98,d*.97,h+.12,rise*.34,'metal')
        if self.lod<2 and windows:
            with self.transform(translate=(x+w/2+.005,0,z),rotate_y=-90):
                self.windows(0,0,base+(h-base)*.30,d*.77,(h-base)*.41,3 if self.lod==0 else 2)
            if self.lod==0:
                with self.transform(translate=(x-w/2-.005,0,z),rotate_y=90):
                    self.windows(0,0,base+(h-base)*.33,d*.65,(h-base)*.35,2)
            for sign in (-1,1):
                self.solid('trim' if era<2 else 'wall',x+sign*(w/2-.027),z-d/2-.006,
                           .052,.045,base+.04,h-.044)
        return z-d/2

    def porch(self,x,z,y=.065,h=.43,w=.43):
        self.entry(x,z,w*.59,y,h*.82,lintel='metal' if self.era>1 else 'trim')
        self.solid('fieldstone',x,z-.105,w+.10,.246,y-.038,y+.012)
        if self.lod<2:
            for s in (-1,1):
                self.solid('timber' if self.era<2 else 'metal',x+s*w*.44,z-.175,.039,.045,y+.010,y+h)
        self.hip(x,z-.087,w+.06,.26,y+h,.094,'banner' if self.era>=2 else 'roof')

    def folio(self,x,y,z,w):
        # A bound folio lies open at the centre crease, with slightly lifted page edges.
        with self.transform(translate=(x,y,z),rotate_x=-24,scale=w):
            self.solid('timber',0,0,1.04,.62,-.065,-.016)
            for s in (-1,1):
                with self.transform(translate=(s*.274,.068,0),rotate_z=s*12):
                    if self.lod==2:
                        self.solid('ivory',0,0,.51,.62,-.032,.077)
                        continue
                    self.box((0,0,0),(.52,.068,.63),self.color('banner'),bottom=True,material='banner')
                    self.solid('ivory',0,0,.48,.57,.033,.077)
                    if self.lod==0:
                        for line in (-.13,0,.13):
                            self.solid('timber',0,line,.30,.019,.077,.080)
            if self.lod<2:
                self.solid('gold',0,0,.047,.64,-.015,.054)

    def cross(self,x,y,z,size):
        # A turquoise apothecary cross on a circular enamel sign.
        with self.transform(translate=(x,y,z),rotate_x=-90):
            self.drum('banner',0,0,size*.64,0,.036,n=(12,10,6)[self.lod])
        self.front_panel('ivory',x,y-size*.135,z-.039,size*.88,size*.27)
        for base in (-.44,.135):
            self.front_panel('ivory',x,y+size*base,z-.039,size*.27,size*.305)


def planting(m,x,z,w,d):
    m.solid('timber' if m.era<2 else 'ivory',x,z,w,d,.055,.154)
    m.solid('soil',x,z,w*.93,d*.87,.154,.168)
    for i in range((4,3,2)[m.lod]):
        px=x+w*((i+.5)/(4,3,2)[m.lod]-.5)
        r=min(d*.35,.118)
        m.cupola('leaf' if i%2==0 else 'grass',px,z,r,.166,r*1.15)
        if m.lod==0:
            m.drum('flower',px+.013,z-.005,.026,.165+r,.185+r,.034,n=5)


def herbalist(m):
    e=m.era
    z=.27 if e!=3 else .24
    x=-.13 if e!=2 else -.19
    w=(1.16,1.30,1.12,1.24)[e]
    front=m.hall(x,z,w,.90,(.68,.81,.77,.95)[e],rise=(.64,.59,.42,.45)[e])
    m.porch(x-.18,front,w=.39)
    if m.lod<2:
        m.windows(x+.30,front-.009,.33,.31,.18,1)
    planting(m,-.36,-.68,.82,.27)
    # An oversized mortar and its diagonal pestle advertise the dispensary.
    cx,cz=.572,-.42
    m.drum('fieldstone' if e<2 else 'metal',cx,cz,.19,.065,.19,.15)
    m.drum('wall',cx,cz,.146,.19,.44,.266)
    m.drum('trim',cx,cz,.269,.438,.479,.272)
    m.drum('soil',cx,cz,.215,.479,.481)
    m.beam('timber' if e<2 else 'metal',(cx-.03,.46,cz),(cx+.18,.787,cz+.047),.074)
    if e==1:
        m.flue(x-.35,.39,.69,1.46,.15)
    elif e==2:
        m.hall(.56,.42,.40,.64,.57,rise=.18)
    elif e==3:
        m.cupola('glass',x+.04,.29,.36,1.073,.34)
    if m.lod==0:
        m.jar(.68,-.03,.065,.11,.20)
        m.herbs(x+.30,.281,front-.006,.32)


def bathhouse(m):
    e=m.era
    x,z=-.31,.25
    f=m.hall(x,z,.90,.92,.65,roof=False)
    m.drum('trim',x,z,.514,.62,.71)
    m.cupola('roof' if e==1 else ('slate' if e==2 else 'glass'),x,z,.518,.708,(.49,.32,.53)[e-1])
    m.drum('gold',x,z,.064,(1.174,1.006,1.21)[e-1],(1.26,1.09,1.32)[e-1],.039)
    m.porch(x,f,h=.39)
    px,pz=.40,-.26
    # The outdoor plunge pool has a low, stepped lip around a recessed water surface.
    m.drum('fieldstone',px,pz,.423,.061,.153)
    m.drum('ivory',px,pz,.395,.153,.233)
    m.drum('turquoise' if e==3 else 'water',px,pz,.348,.233,.236)
    if m.lod<2:
        for s in (-1,1):
            m.beam('metal',(px+s*.083,.217,pz-.32),(px+s*.083,.347,pz-.33),.022)
            m.beam('metal',(px+s*.083,.346,pz-.329),(px+s*.083,.287,pz-.213),.023)
        m.windows(x,f-.006,.39,.36,.12,2)
    if e==1:
        m.solid('brick',.286,.47,.56,.24,.064,.36)
        m.hip(.286,.47,.596,.28,.36,.09,'roof')
        m.drum('brick',.55,.46,.128,.065,.79,.095)
        m.drum('trim',.55,.46,.121,.79,.84)
    elif e==2:
        m.hall(.52,.47,.49,.55,.47,rise=.20)
        m.flue(.59,.50,.49,1.26,.15)
    else:
        m.hall(.46,.48,.58,.44,.57,rise=.14)
        m.cupola('roof',.49,.50,.20,.708,.19)
    if m.lod==0:
        m.jar(.01,-.71,.065,.095,.19)
        m.pennon(x-.27,.38,.91,.31)


def library(m):
    e=m.era
    h=(.85,.99,1.15)[e-1]
    front=m.hall(0,.23,1.39,1.04,h,rise=.46)
    # The public reading stair terminates at an overscale open codex above the portal.
    m.hall(0,-.37,.65,.42,.62,roof=False,windows=False)
    m.solid('trim',0,-.37,.72,.50,.60,.656)
    m.solid('timber',0,-.35,.41,.19,.656,.744)
    m.porch(0,-.58,.077,.44,.49)
    m.folio(0,.725,-.389,.56)
    if m.lod<2:
        for s in (-1,1):
            m.windows(s*.51,front-.010,.31,.27,.27,2)
        m.herbs(.50,.263,front-.008,.295)
    if e==1:
        m.drum('wall',-.50,.47,.23,.73,1.19)
        m.cupola('roof',-.50,.47,.247,1.19,.25)
    elif e==2:
        m.hall(0,.28,.77,.51,1.31,base=.95,rise=.19)
        m.flue(.54,.49,.95,1.51,.116)
    else:
        m.hall(-.18,.36,.86,.62,1.51,base=1.13,rise=.17)
        if m.lod<2: m.windows(-.18,.042,1.27,.64,.16,3)
    if m.lod==0:
        m.pennon(.48,.28,h+.12,.30)


def scriptorium(m):
    e=m.era
    f=m.hall(-.05,.13,1.51,1.00,(.73,.86,.89,1.02)[e],rise=(.39,.46,.29,.19)[e])
    m.porch(-.46,f,.074,.44,.39)
    m.windows(.23,f-.009,.31,.58,.24,(3,2,1)[m.lod])
    # Outdoor copy desk with a folded vellum sheet and a feather standing in the inkwell.
    m.solid('trim',.27,-.58,.47,.20,.066,.251)
    m.solid('timber',.27,-.58,.58,.31,.249,.297)
    m.solid('trim',.22,-.58,.20,.14,.297,.353)
    m.folio(.22,.35,-.58,.26)
    m.drum('door',.477,-.57,.052,.297,.386,.044,n=(8,6,4)[m.lod])
    with m.transform(translate=(.477,.378,-.57),rotate_z=-31):
        m.beam('gold',(0,0,0),(0,.698,0),.029)
        # A bent nib and alternating vane lengths give the guild's quill a cut-feather outline.
        rows=[(.126,.014,.014),(.25,.074,.064),(.398,.102,.101),
              (.478,.066,.091),(.52,.087,.092),(.651,.072,.057),(.776,.008,.008)]
        if m.lod:
            rows=[rows[i] for i in ((0,1,2,5,6) if m.lod==1 else (0,2,6))]
        for (a,l,r),(b,ll,rr) in zip(rows,rows[1:]):
            for sign,wa,wb in ((1,r,rr),(-1,l,ll)):
                pts=[(0,a,-.027),(sign*wa,a,0),(sign*wb,b,0),(0,b,-.027)]
                m.face('ivory',pts[::-1] if sign==1 else pts)
                back=[(xx,yy,-zz) for xx,yy,zz in pts]
                m.face('ivory',back if sign==1 else back[::-1])
    if e==1:
        m.flue(-.62,.34,.78,1.43,.143)
    elif e==2:
        m.hall(.094,.18,.64,.48,1.11,base=.87,rise=.16)
    elif e==3:
        m.cupola('glass',-.36,.21,.289,1.13,.24)
    if m.lod==0: m.herbs(.23,.254,f-.008,.42)


def university(m):
    e=m.era
    for s in (-1,1):
        x=s*.56
        f=m.hall(x,.06,.49,1.24,.66+.08*(e-1),rise=.39)
        if m.lod<2: m.windows(x,f-.008,.23,.32,.27,2)
    f=m.hall(0,.40,.68,.69,1.09+.10*(e-1),rise=.37)
    m.porch(0,f,.082,.50,.48)
    # A graduated bell lantern stands between two teaching wings.
    ty=1.20+.10*(e-1)
    m.solid('wall',0,.40,.34,.32,ty-.15,ty+.29)
    if m.lod<2:
        m.front_panel('door',0,ty+.04,.234,.175,.17)
        m.drum('gold',0,.207,.059,ty+.037,ty+.115,.031,n=8)
    m.hip(0,.40,.44,.43,ty+.29,.27 if e!=3 else .14,'roof' if e==1 else 'metal')
    m.pennon(0,.40,ty+(.55 if e!=3 else .42),.21)
    if e>=2:
        m.hall(-.56,.37,.31,.48,1.03+.09*(e-2),base=.68,rise=.21)
    if e==3:
        m.solid('glass',0,.55,.95,.205,1.02,1.18)
    if m.lod==0:
        for s in (-1,1): m.herbs(s*.56,.182,-.56,.31)


def observatory(m):
    e=m.era
    x,z=-.14,.17
    m.drum('fieldstone',x,z,.64,.055,.20,.605)
    m.drum('wall' if e!=2 else 'brick',x,z,.59,.20,.90,.54)
    m.drum('trim' if e<3 else 'metal',x,z,.598,.886,.979)
    # Two dome leaves have a continuous north-south slot. Their cut cheeks enclose the shell.
    radius=(.595,.575,.614)[e-1]
    rise=(.59,.46,.57)[e-1]; rings=(6,4,2)[m.lod]; steps=(8,5,3)[m.lod]
    gap=(.075,.10,.135)[e-1]
    for side in (-1,1):
        def point(a,b):
            return (x+side*(gap+(radius-gap)*sin(a)*sin(b)),
                    .978+rise*cos(a),z+radius*sin(a)*cos(b))
        for i in range(rings):
            a,b=pi*.5*i/rings,pi*.5*(i+1)/rings
            for j in range(steps):
                u,v=pi*j/steps,pi*(j+1)/steps
                p=[point(b,u),point(b,v),point(a,v)]
                if i: p.append(point(a,u))
                m.face(('roof','metal','glass')[e-1],p if side==1 else p[::-1])
        edge=[point(pi*.5*k/rings,0) for k in range(rings,-1,-1)]
        edge += [point(pi*.5*k/rings,pi) for k in range(1,rings+1)]
        m.face('trim',edge if side==1 else edge[::-1])
    # A jointed telescope bears on the drum inside the slit and clears the opening.
    with m.transform(translate=(x,1.085,z-.05),rotate_x=-65):
        m.drum('door',0,0,gap*.75,0,.72,n=(10,8,5)[m.lod])
        m.drum('gold' if e<3 else 'turquoise',0,0,gap*.98,.704,.792,n=(10,8,5)[m.lod])
        m.drum('glass',0,0,gap*.79,.792,.797,n=(10,8,5)[m.lod])
    f=m.hall(.395,-.33,.58,.49,.49,rise=.23)
    m.porch(.395,f,.07,.35,.32)
    if m.lod<2:
        with m.transform(translate=(x,0,z)):
            for angle in (-52,4,59):
                with m.transform(rotate_y=angle):
                    m.windows(0,-.558,.40,.17,.27,1)
    if m.lod==0: m.herbs(.66,.19,f+.04,.15)


def printworks(m):
    e=m.era
    f=m.hall(-.21,.27,1.17,.89,.78+.08*(e-1),rise=.32)
    m.porch(-.48,f,.073,.42,.35)
    if m.lod<2: m.windows(.095,f-.009,.37,.38,.21,2)
    # An iron hand press and, later, a powered roll press sit under an open-sided frame.
    x,z=.285,-.428
    m.solid('trim',x,z,.69,.43,.060,.173)
    for s in (-1,1): m.solid('timber' if e==1 else 'metal',x+s*.265,z,.083,.13,.172,.74)
    m.solid('trim',x,z,.68,.20,.715,.804)
    m.solid('metal',x,z,.061,.065,.429,.908)
    m.solid('metal',x,z,.42,.28,.416,.475)
    m.solid('ivory',x,z-.019,.38,.32,.174,.221)
    if e==1:
        m.beam('timber',(x-.17,.90,z),(x+.20,.90,z),.052)
    else:
        with m.transform(translate=(x-.25,.42,z-.11),rotate_z=-90):
            m.drum('ivory',0,0,.093,0,.50,n=(12,8,5)[m.lod])
        if e==2: m.flue(-.56,.46,.77,1.38,.145)
        else: m.hall(-.28,.35,.71,.50,1.16,base=.90,rise=.13)
    if m.lod==0:
        m.jar(.716,-.16,.064,.074,.127)
        m.herbs(.092,.32,f-.007,.34)


def hospital(m):
    e=m.era
    h=(.89,1.11,1.32)[e-1]
    for s in (-1,1):
        f=m.hall(s*.49,.21,.65,1.09,.67+.09*(e-1),rise=.31)
        if m.lod<2: m.windows(s*.49,f-.009,.30,.43,.20,2)
    f=m.hall(0,-.10,.59,1.19,h,rise=.38)
    m.porch(0,f,.074,.40,.41)
    m.cross(0,.65+(e-1)*.10,f-.009,.24)
    if e==1: m.flue(.54,.50,.67,1.18,.129)
    if e==2:
        m.solid('wall',0,.17,.38,.39,1.16,1.46)
        m.hip(0,.17,.46,.48,1.46,.19,'slate')
    if e==3: m.cupola('glass',0,.05,.253,h+.124,.24)
    if m.lod==0:
        for s in (-1,1): m.herbs(s*.49,.244,-.344,.37)


def clinical_tower(m):
    # A bilateral clinic canopy and three stepped diagnostic galleries.
    f=m.hall(0,.10,1.53,1.12,.41,rise=.16)
    for i,(x,w,d,y,h) in enumerate(((-.19,.95,.80,.53,.94),(-.23,.82,.69,.93,1.35),(-.27,.68,.55,1.34,1.70))):
        m.solid('glass',x,.24,w,d,y,h)
        m.solid('wall',x,.24,w+.096,d+.10,h-.094,h+.027)
        if m.lod==0:
            for s in (-1,1): m.solid('ivory',x+s*w*.34,.24-d/2,.033,.025,y+.017,h-.094)
    m.cupola('metal',-.27,.24,.28,1.727,.14)
    m.porch(.36,f,.067,.36,.60)
    m.solid('metal',-.27,-.190,.28,.121,.66,.915)
    m.cross(-.27,.81,-.250,.399)
    if m.lod<2:
        planting(m,-.38,-.65,.53,.20)
        m.pennon(.55,.33,.55,.25)


def lab(m):
    e=m.era
    f=m.hall(-.28,.17,.97,1.22,.85 if e==2 else 1.01,rise=.24)
    m.porch(-.32,f,.073,.42,.42)
    x=.47
    for z in (.36,):
        top=1.11+(.15 if e==3 else 0)
        m.drum('metal',x,z,.264,.065,.245)
        m.drum('glass',x,z,.229,.245,top)
        m.drum('ivory',x,z,.26,top,top+.086,.211)
        m.cupola('roof' if e==2 else 'turquoise',x,z,.203,top+.084,.173)
        if m.lod<2:
            m.drum('banner',x,z,.236,.39,.453)
            m.beam('metal',(x-.19,top-.05,z),(-.12,top-.09,z),.083)
    # The instrument court shows a microscope in profile, with an open throat between
    # its objective and specimen stage. A glazed process vessel stands behind it.
    cz=-.35
    m.drum('metal',x,cz,.30,.065,.192,.263)
    m.beam('trim',(x,.171,cz+.110),(x,.55,cz+.205),.128)
    m.beam('metal',(x,.49,cz+.201),(x,.91,cz+.167),.116)
    m.beam('metal',(x,.88,cz+.166),(x,.994,cz-.010),.113)
    m.solid('ivory',x,cz-.014,.405,.40,.419,.473)
    m.beam('metal',(x,.40,cz+.117),(x,.444,cz+.023),.094)
    m.solid('glass',x,cz-.074,.221,.142,.473,.492)
    with m.transform(translate=(x,.67,cz-.13),rotate_x=28):
        m.drum('metal',0,0,.067,0,.15,.085)
        m.drum('gold' if e==2 else 'ivory',0,0,.113,.144,.498,.092)
        m.drum('trim',0,0,.067,.498,.616)
        m.drum('glass',0,0,.085,.616,.635)
    if m.lod<2:
        with m.transform(translate=(x,.82,cz+.171),rotate_z=-90):
            m.drum('banner',0,0,.091,.04,.126)
    if e==3: m.hall(-.33,.47,.57,.48,1.28,base=1.03,rise=.15)
    if m.lod==0:
        m.pennon(-.59,.25,.94 if e==2 else 1.12,.30)
        m.herbs(-.60,.244,f-.008,.174)


def data_center(m):
    f=m.hall(-.04,.06,1.49,1.26,.64,rise=.18)
    m.porch(-.34,f,.070,.40,.48)
    for i,x in enumerate((-.48,.00,.48)):
        h=.99+(1-i%2)*.115
        m.solid('metal',x,.25,.328,.67,.72,h)
        m.solid('glass',x,.25,.351,.688,h-.12,h+.008)
        m.solid('wall',x,.25,.37,.72,h+.008,h+.072)
        if m.lod<2:
            m.drum('trim',x,.25,.129,h+.071,h+.11)
            for s in (-1,1):
                m.block('metal',(x+s*.055,h+.119,.25),(.076,.021,.044))
            m.front_panel('turquoise',x,h-.084,-.099,.218,.025)
    if m.lod<2: m.windows(.29,f-.008,.31,.60,.11,3)
    if m.lod==0:
        m.pennon(-.63,-.21,.75,.30)
        m.herbs(.37,.23,f-.009,.34)


BUILDERS={'Herbalist':herbalist,'Bathhouse':bathhouse,'Scriptorium':scriptorium,'Library':library,
          'University':university,'Observatory':observatory,'Printworks':printworks,'Hospital':hospital,
          'MedicalCenter':clinical_tower,'ResearchLab':lab,'DataCenter':data_center}


def build(spec,lod):
    m=Mason(spec,lod)
    tall=.58 if spec.building_type=='Herbalist' else (.78 if spec.building_type=='Scriptorium' else 1)
    with m.transform(scale=(spec.footprint[0]/2,tall,spec.footprint[1]/2)):
        m.course('fieldstone',0,0,1.85,1.80,0,.065,.12,.012)
        BUILDERS[spec.building_type](m)
    return m


def publish_package(package, builder):
    examples=[]
    by_age={age: [] for age in AGES}
    for spec in package_models(package):
        levels=[builder(spec,lod) for lod in range(3)]
        result=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=224)
        print(result['id'],result['lod_triangles'],flush=True)
        examples.append((spec.building_type+' / '+spec.band, ROOT/result['preview']))
        for age,path in result['age_previews'].items():
            by_age[age].append((spec.building_type, ROOT/path))
    for name,tiles in [('models',examples), *by_age.items()]:
        if not tiles:
            continue
        width=min(5,len(tiles))
        step=174
        sheet=Image.new('RGB',(step*width,32+(step+22)*((len(tiles)+width-1)//width)),(235,233,225))
        draw=ImageDraw.Draw(sheet)
        draw.text((8,9),'Human  /  '+spec.group+'  /  '+name.title(),fill=(36,36,36))
        for i,(label,path) in enumerate(tiles):
            x=i%width*step; y=32+i//width*(step+22)
            with Image.open(path) as tile:
                sheet.paste(tile.resize((step,step),Image.Resampling.LANCZOS),(x,y))
            draw.text((x+5,y+step+3),label,fill=(36,36,36))
        sheet.save(tiles[0][1].with_name('contact-'+name+'.png'))


if __name__=='__main__':
    publish_package(PACKAGE,build)
