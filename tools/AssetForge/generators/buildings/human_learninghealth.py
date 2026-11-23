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
    if e==1:
        # The herb merchant tends four beds in a walled physic garden.
        # A raised tiled portico shades the open counters on its northern side.
        m.solid('plaster',-.02,.47,1.45,.40,.065,.65)
        for x in (-.66,.63):
            m.drum('ivory',x,.029,.066,.065,.619,.049)
        m.hip(-.02,.33,1.64,.81,.619,.35)
        m.entry(-.16,.27,.34,.072,.40)
        for sx in (-.48,.40):
            for z in (-.30,-.65):
                m.solid('timber',sx,z,.53,.263,.065,.145)
                m.solid('soil',sx,z,.485,.223,.145,.158)
                for j in range((3,2,1)[m.lod]):
                    px=sx+(j-((3,2,1)[m.lod]-1)/2)*.145
                    m.dome((px,.158,z),.088,m.color('leaf'),rings=(2,2,1)[m.lod],
                           segments=(8,6,5)[m.lod],squash=1.2,material='leaf')
                    if m.lod==0: m.drum('flower',px,z,.024,.253,.28,.032,n=5)
        if m.lod<2:
            m.jar(.42,.13,.065,.134,.255)
            m.windows(.41,.265,.307,.28,.196,1)
        if m.lod==0:
            m.pennon(-.65,.057,.66,.23)
        return
    if e==2:
        # A rooftop drying kiln vents the long apothecary range. Copper
        # alembic and receiver are joined by the condenser's sloping neck.
        f=m.hall(-.30,.26,.86,.94,.73,rise=.38)
        m.entry(-.38,f,.255,.074,.43)
        m.drum('brick',-.30,.41,.238,.76,1.13,.193)
        m.drum('slate',-.30,.41,.234,1.13,1.39,.058)
        m.drum('metal',-.30,.41,.065,1.39,1.52,.052)
        x,z=.42,-.18
        m.drum('brick',x,z,.223,.065,.31)
        m.drum('gold',x,z,.13,.31,.49,.227)
        m.drum('gold',x,z,.227,.49,.69,.11)
        m.drum('metal',x,z,.092,.69,.88,.063)
        m.beam('gold',(x,.85,z),(.643,.625,-.456),.066)
        m.jar(.644,-.46,.065,.11,.52)
        if m.lod<2: planting(m,-.432,-.63,.661,.29)
        if m.lod==0:
            m.windows(-.085,f-.008,.31,.201,.198,1)
            m.herbs(-.085,.246,f,.24)
        return
    if e==3:
        # A medicinal plant carousel grows specimens on staggered trays.
        # The dispensary sits against the lower shaft, beneath the collector.
        x,z=.14,.13
        m.drum('glass',x,z,.241,.065,1.31,.169)
        for j in range(3):
            a=.4+2.24*j
            px,pz=x+.249*cos(a),z+.249*sin(a); y=.37+.327*j
            m.drum('metal',px,pz,.266,y,y+.069,.33)
            m.drum('soil',px,pz,.312,y+.069,y+.085)
            for k in range((5,3,2)[m.lod]):
                t=2*pi*k/(5,3,2)[m.lod]
                m.dome((px+.182*cos(t),y+.085,pz+.182*sin(t)),.098,m.color('leaf'),
                       rings=(2,2,1)[m.lod],segments=(9,6,4)[m.lod],squash=1.48,material='leaf')
        m.drum('ivory',x,z,.162,1.31,1.39,.397)
        m.cupola('glass',x,z,.397,1.39,.119)
        m.course('ivory',-.246,-.321,.66,.53,.065,.441,.105)
        m.entry(-.315,-.586,.209,.070,.276,lintel='metal')
        if m.lod<2: m.cross(-.11,.346,-.592,.14)
        return
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
    if e==2:
        # A barrel-vaulted municipal swimming hall with a recessed glazed
        # entrance and a separate boiler stack. Roof ribs span the entire pool.
        x,z=-.199,.052
        m.course('brick',x,z,1.10,1.30,.065,.567,.043)
        for i in range((12,7,4)[m.lod]):
            n=(12,7,4)[m.lod]; a=pi*i/n; b=pi*(i+1)/n
            def point(t,d): return (x+.593*cos(t),.565+.484*sin(t),z+d)
            m.face('glass',(point(a,-.68),point(b,-.68),point(b,.68),point(a,.68)))
            for d in (-.68,.68):
                tri=[(x,.565,z+d),point(a,d),point(b,d)]
                m.face('glass',tri if d>0 else tri[::-1])
            if m.lod==0:
                for d in (-.46,-.10,.26,.59):
                    def rib(t,v): return (x+.605*cos(t),.566+.496*sin(t),z+v)
                    m.face('ivory',[rib(a,d-.011),rib(b,d-.011),rib(b,d+.011),rib(a,d+.011)])
        m.drum('brick',.551,.417,.146,.065,1.35,.087)
        m.drum('trim',.551,.417,.11,1.35,1.43)
        m.entry(x,-.598,.332,.071,.39)
        if m.lod<2:
            for sx in (-.57,.16):
                m.windows(sx,-.603,.265,.182,.196,1)
            m.solid('metal',.49,-.18,.29,.69,.065,.279)
        return
    if e==3:
        # The bathing terraces step down an occupied core. Alternating basins
        # overlook the entry court and a narrow falling sheet connects them.
        m.course('ivory',-.08,.19,1.36,.99,.064,.35,.19)
        m.course('glass',-.22,.32,.83,.66,.35,.64,.14)
        m.course('ivory',-.22,.32,.93,.76,.64,.705,.15)
        for x,z,r,base in ((-.29,.36,.318,.705),(.294,.07,.32,.35),(.37,-.522,.29,.065)):
            m.drum('ivory',x,z,r,base,base+.104,r*1.10)
            m.drum('water',x,z,r*.94,base+.104,base+.112)
            if m.lod==0:
                for s in (-1,1):
                    m.drum('gold',x+s*r*.28,z-r*.94,.015,base+.101,base+.178)
        m.course('ivory',-.44,-.388,.54,.45,.065,.333,.069)
        m.entry(-.44,-.613,.233,.072,.211,lintel='metal')
        if m.lod<2:
            m.solid('water',.36,-.304,.15,.068,.166,.45)
            m.windows(-.23,-.017,.429,.41,.14,2)
        if m.lod<2:
            planting(m,-.347,-.762,.46,.145)
        return
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
    if e==1:
        # The chapterhouse has high reading lights. A sheltered open desk
        # occupies the apse beside the door, facing out into the garden.
        x,z=-.16,.20
        m.drum('wall',x,z,.614,.065,.789,n=8)
        m.drum('timber',x,z,.637,.789,.844,n=8)
        m.drum('roof',x,z,.70,.844,1.27,.134,n=8)
        m.cupola('gold',x,z,.133,1.27,.099)
        m.course('wall',-.26,-.343,.49,.43,.065,.485,.035)
        m.entry(-.26,-.558,.241,.068,.347)
        m.hip(-.26,-.345,.58,.52,.485,.195)
        m.solid('timber',.414,-.399,.39,.272,.065,.336)
        m.folio(.413,.34,-.398,.39)
        if m.lod<2:
            for j in (0,1,2,5,6):
                a=2*pi*(j+.5)/8
                with m.transform(translate=(x+.574*sin(a),0,z+.574*cos(a)),rotate_y=180+a*180/pi):
                    m.windows(0,0,.397,.218,.28,1)
        if m.lod==0:
            planting(m,-.633,-.497,.296,.34)
        return
    if e==2:
        # Daylight falls through three tall roof monitors onto copying benches.
        # A separate binding tower stores finished volumes above the loading bay.
        m.solid('brick',-.27,.19,.91,1.10,.065,.66)
        for z in (-.17,.194,.558):
            m.wedge((-.27,.66,z),1.01,.355,.243,m.color('slate'),material='slate')
            if m.lod<2:
                with m.transform(translate=(0,0,z+.182),rotate_y=180):
                    m.windows(.27,0,.698,.78,.14,3)
        m.course('brick',.38,-.134,.45,.66,.065,1.082,.04)
        m.hip(.38,-.134,.51,.72,1.082,.21,'roof')
        m.entry(.38,-.464,.228,.067,.406)
        m.solid('timber',-.26,-.53,.64,.21,.065,.262)
        m.folio(-.29,.269,-.529,.43)
        if m.lod<2:
            m.windows(.38,-.469,.679,.269,.207,2)
        if m.lod==0:
            m.pennon(.38,-.121,1.27,.262)
            m.herbs(.38,.609,-.47,.264)
        return
    if e==3:
        # A vertical archive wraps reading pods around the lift shaft. Each
        # stacked floor has a broad projecting visor like the leaf of a book.
        x,z=.01,.21
        m.course('ivory',x,z,.65,.62,.065,1.30,.134)
        for j,(dx,dz) in enumerate(((-.22,-.20),(.22,.02),(-.10,.16))):
            y=.314+j*.334
            m.course('glass',x+dx,z+dz,.86,.64,y,y+.205,.11)
            m.course('ivory',x+dx,z+dz,1.02,.77,y+.205,y+.271,.14)
            if m.lod==0:
                for k in (-.22,0,.22):
                    m.solid('metal',x+dx+k,z+dz-.322,.026,.041,y+.014,y+.186)
        m.course('wall',-.10,-.36,.59,.49,.065,.379,.06)
        m.entry(-.10,-.606,.235,.071,.251,lintel='metal')
        m.solid('metal',.403,-.532,.155,.175,.065,.273)
        m.folio(.402,.277,-.53,.382)
        if m.lod<2: planting(m,-.54,-.57,.321,.24)
        return
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
    if e==2:
        # An open equatorial refractor on a brick observing pier. The glass
        # objective, brass lens cell and eyepiece share the tilted optical axis.
        x,z=.18,.055
        m.drum('brick',x,z,.334,.065,.265,.285)
        m.drum('wall',x,z,.223,.265,.69,.183)
        m.beam('metal',(x,.663,z),(x-.133,.933,z),.118)
        with m.transform(translate=(x-.09,.942,z),rotate_z=-54):
            m.drum('gold',0,0,.090,-.475,.102,.129)
            m.drum('metal',0,0,.132,.102,.54,.159)
            m.drum('gold',0,0,.183,.54,.65,.179)
            m.drum('glass',0,0,.147,.65,.662)
            m.drum('trim',0,0,.055,-.577,-.475)
            if m.lod==0:
                for y in (-.31,-.08,.30):
                    m.drum('gold',0,0,.146 if y>.2 else .119,y,y+.036)
        f=m.hall(-.48,.38,.56,.59,.518,rise=.19)
        m.entry(-.48,f,.236,.070,.312)
        if m.lod<2:
            m.solid('timber',.12,-.515,.53,.28,.065,.282)
            m.folio(.12,.286,-.515,.35)
        if m.lod==0: m.pennon(-.65,.375,.579,.237)
        return
    if e==3:
        # An offset-focus radio dish on a fork mount; its feed is suspended
        # above the concave reflector. A low service cabin sits to the left.
        x,z=.10,.13
        m.drum('metal',x,z,.311,.065,.256,.242)
        for s in (-1,1):
            m.beam('ivory',(x+s*.183,.232,z),(x+s*.233,.728,z),.096)
        with m.transform(translate=(x,.751,z),rotate_x=-25):
            n=(22,12,7)[m.lod]
            def rim(i,r,y):
                t=2*pi*i/n
                return (r*cos(t),y,r*sin(t))
            for i in range(n):
                j=(i+1)%n
                m.face('ivory',[(0,.003,0),rim(j,.618,.292),rim(i,.618,.292)])
                m.face('metal',[(0,-.104,0),rim(i,.618,.269),rim(j,.618,.269)])
                m.face('gold',[rim(j,.618,.269),rim(i,.618,.269),rim(i,.618,.292),rim(j,.618,.292)])
            for a in (0,2*pi/3,4*pi/3):
                m.beam('metal',(.535*cos(a),.257,.535*sin(a)),(0,.562,0),.026)
            m.drum('accent',0,0,.064,.535,.635,.037)
        f=m.hall(-.526,-.365,.52,.47,.353,rise=.10)
        m.entry(-.526,f,.214,.069,.237,lintel='metal')
        if m.lod==0:
            m.pennon(-.70,-.29,.419,.246)
            m.herbs(-.69,.169,f,.12)
            m.solid('metal',.10,.13,.26,.32,.23,.392)
            m.folio(-.344,.234,-.612,.165)
        return
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
    if e==2:
        # The rotary press carries a broad roll of paper between cheek frames.
        # The two-storey typesetting room's high windows light the compositors.
        f=m.hall(-.28,.313,1.02,.72,1.19,rise=.26)
        m.entry(-.49,f,.237,.071,.411)
        m.windows(-.24,f-.011,.815,.66,.205,3 if m.lod==0 else 2)
        m.solid('metal',.219,-.459,.83,.53,.065,.199)
        for x in (-.114,.556):
            m.solid('trim',x,-.459,.071,.412,.199,.714)
        for z,y,r,role in ((-.38,.50,.158,'metal'),(-.546,.684,.113,'ivory')):
            with m.transform(translate=(-.16,y,z),rotate_z=-90):
                m.drum(role,0,0,r,0,.765 if role=='metal' else .739)
        with m.transform(translate=(.219,.403,-.652),rotate_x=-29):
            m.box((0,0,0),(.55,.033,.278),m.color('ivory'),material='ivory',bottom=True)
        if m.lod==0:
            m.flue(-.65,.443,1.226,1.57,.108)
            m.herbs(.079,.471,f,.165)
        return
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
    if e==3:
        # A three-point materials testing frame encircles the sample stage.
        # The low control room gives operators a direct view into the chamber.
        x,z=.17,.20
        m.drum('metal',x,z,.472,.065,.188,.423)
        m.drum('glass',x,z,.282,.188,.601,.267)
        m.drum('accent',x,z,.288,.601,.663,.241)
        for j in range(3):
            a=2*pi*j/3+.16
            outside=(x+.484*cos(a),.188,z+.484*sin(a))
            top=(x+.178*cos(a),1.101,z+.178*sin(a))
            m.beam('ivory',outside,top,.135)
            if m.lod==0:
                m.drum('metal',outside[0],outside[2],.084,.065,.266,.073)
        m.drum('metal',x,z,.245,1.06,1.187,.184)
        m.drum('gold',x,z,.065,.78,1.063)
        f=m.hall(-.375,-.408,.79,.46,.448,rise=.12,windows=False)
        m.entry(-.477,f,.23,.07,.302,lintel='metal')
        if m.lod<2:
            m.windows(-.131,f-.006,.207,.137,.16,1)
            m.windows(-.36,-.169,.215,.52,.124,2)
        if m.lod==0:
            m.pennon(-.596,-.39,.562,.22)
            m.herbs(-.131,.178,f,.195)
        return
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
