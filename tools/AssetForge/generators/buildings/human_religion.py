"""Human sanctuaries. The same votive flame travels from a carved wayside bird to glazed civic churches."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.human_housesancient import House

PACKAGE = 'human_religion'
ERAS = ('ancient', 'classical', 'industrial', 'future')


class Sanctuary(House):
    def drum(self, mat, x, y, z, r, h, top=None, n=None):
        self.cylinder((x, y, z), r, h, self.color(mat), segments=n or (12, 8, 6)[self.lod],
                      top_radius=top, material=mat)

    def dome_cap(self, mat, x, y, z, r, rise):
        self.dome((x, y, z), r, self.color(mat), rings=(4, 3, 2)[self.lod],
                  segments=(14, 10, 6)[self.lod], squash=rise/r, material=mat)

    def gable(self, x, y, z, w, d, rise, roof='roof', end='wall'):
        with self.transform(translate=(x, 0, z)):
            self.roof(w, d, y, rise, roof, end, ridge_mat='trim' if self.lod < 2 else None)

    def steps(self, x, front, w, ground, n=3, tread=.073):
        for i in range(n if self.lod < 2 else 1):
            depth = (n-i)*tread
            top = ground * ((i+1)/n if self.lod < 2 else 1)
            self.block('fieldstone', (x, top/2, front-depth/2+.008),
                       (w-.04*i, top, depth))

    def pointed_light(self, x, y, z, w, h, mat='emissive', frame='trim', divided=True):
        # The jamb, springer and pointed head are one continuous piece of tracery.
        def panel(cx, base, width, height, surface, plane):
            coords = ((.5,0),(-.5,0),(-.5,.69),(-.32,.86),(0,1),(.32,.86),(.5,.69))
            self.face(surface, [(cx+sx*width, base+sy*height, plane) for sx, sy in coords])
        panel(x, y, w, h, frame, z-.005)
        panel(x, y+.043*h, w*.77, h*.85, mat, z-.009)
        if divided and self.lod == 0:
            self.block(frame, (x,y+h*.43,z-.014), (.055*w,.65*h,.009))
            self.block(frame, (x,y+h*.51,z-.016), (.78*w,.045*h,.010))

    def rose(self, x, y, z, radius, future=False):
        # The lead lines are the reveals between six petal-shaped glass pieces.
        count = (12,10,8)[self.lod]
        def circle(r, surface, depth):
            self.face(surface, [(x+r*math.cos(-2*math.pi*i/count),
                                 y+r*math.sin(-2*math.pi*i/count),z-depth) for i in range(count)])
        circle(radius, 'ivory' if future else 'accent', .006)
        circle(radius*.85,'trim',.010)
        if self.lod > 0:
            circle(radius*.70, 'ascension' if future else 'glass', .013)
            return
        for i in range(6):
            a = math.pi*i/3
            def point(r, angle):
                return (x+r*math.cos(angle), y+r*math.sin(angle), z-.014)
            flower = [point(radius*.15,a),point(radius*.53,a+.40),point(radius*.73,a+.23),
                      point(radius*.80,a),point(radius*.73,a-.23),point(radius*.53,a-.40)]
            self.face(('glass','emissive','banner')[i%3] if not future else
                      ('ascension','glass')[i%2], flower)
        circle(radius*.15,'gold',.018)

    def flame(self, x, y, z, size, late=False):
        # The sculpted flame is pinched at the base, then curls back over the cupped lamp.
        with self.transform(translate=(x,y,z),scale=size):
            self.drum('gold', 0,0,0,.15,.085,.225,n=(8,6,5)[self.lod])
            levels = [(0,.068,0),(.20,.145,.027),(.37,.088,-.025)]
            n=(7,5,4)[self.lod]
            rings=[[(cx+r*math.cos(2*math.pi*i/n), yy+.073, r*.72*math.sin(2*math.pi*i/n))
                    for i in range(n)] for yy,r,cx in levels]
            mat='ascension' if late else 'emissive'
            for lower,upper in zip(rings,rings[1:]):
                for i in range(n):
                    j=(i+1)%n
                    self.face(mat,[lower[j],lower[i],upper[i],upper[j]])
            for i in range(n):
                self.face(mat,[rings[-1][(i+1)%n],rings[-1][i],(.11,.58,0)])

    def standard(self,x,top,z,w,h):
        # Cloth hangs from a projecting crossbar fixed through a wall socket.
        self.block('trim',(x,top,z-.015),(w*1.23,.025,.052))
        pts=[(x+w*.5,top-.012,z-.024),(x-w*.5,top-.012,z-.024),
             (x-w*.46,top-h*.76,z-.035),(x,top-h,z-.046),(x+w*.46,top-h*.76,z-.035)]
        self.face('banner',pts[::-1])
        if not self.lod:
            self.face('gold',[(x,top-h*.22,z-.048),(x+w*.19,top-h*.42,z-.048),
                              (x,top-h*.59,z-.048),(x-w*.19,top-h*.42,z-.048)])

    def planter(self,x,z,r=.085):
        self.drum('roof_tile',x,0,z,r*.65,r*1.15,r,6)
        self.dome_cap('leaf',x,r*1.1,z,r*1.08,r*.63)
        if self.lod==0:
            for i in range(3):
                a=2*math.pi*i/3
                self.drum('flower' if i%2==0 else 'ivory',x+r*.52*math.cos(a),
                          r*1.56,z+r*.51*math.sin(a),r*.22,r*.15,n=5)

    def spire(self,x,y,z,w,rise,mat='roof'):
        # Four diamond faces above a spreading bell-cast skirt.
        self.block('accent',(x,y+.02,z),(w*.98,.042,w*.98))
        by=y+.041
        for bottom,top,h in [(w,w*.60,rise*.22),(w*.60,w*.12,rise*.63)]:
            self.block(mat,(x,by+h/2,z),(bottom,h,bottom),taper=1-top/bottom)
            by+=h
        self.pyramid((x,by,z),w*.12,w*.12,rise*.15,self.color('gold'),material='gold')

    def bell(self,x,y,z,size):
        # A flared bronze casting hangs from a short yoke between the belfry piers.
        with self.transform(translate=(x,y,z),scale=size):
            self.drum('gold',0,0,0,.34,.10,.32,n=(10,7,5)[self.lod])
            self.drum('accent',0,.099,0,.31,.31,.14,n=(10,7,5)[self.lod])
            self.block('trim',(0,.49,0),(.13,.17,.12))
            if self.lod==0:
                self.drum('trim',0,-.072,0,.057,.077,.045,n=6)

    def belfry(self,x,z,ground,w,h,era):
        sill=ground+h*.63
        self.block('wall',(x,(sill+ground)/2,z),(w,sill-ground,w*.9))
        self.block('accent' if era>=2 else 'fieldstone',(x,sill+.012,z),(w*1.10,.050,w))
        for sign in (-1,1):
            for side in (-1,1):
                self.block('wall',(x+sign*w*.39,sill+h*.17,z+side*w*.345),
                           (w*.17,h*.34,w*.17))
        if self.lod < 2:
            self.bell(x,sill+h*.09,z,.47*h)
        top=sill+h*.34
        self.block('trim',(x,top+.006,z),(w*1.13,.075*h,w*1.02))
        if self.lod<2:
            self.pointed_light(x,ground+h*.25,z-w*.451,w*.28,h*.25,'glass',divided=False)
        if era==1:
            self.spire(x,top+.04*h,z,w*1.18,h*.51,'slate')
        elif era==2:
            self.dome_cap('slate',x,top+.04*h,z,w*.58,w*.53)
            self.drum('accent',x,top+.04*h+w*.49,z,w*.072,w*.2,0,n=6)
        else:
            self.spire(x,top+.04*h,z,w*1.07,h*.69,'glass')


def totem(m):
    m.course('wall',0,.01,.66,.66,0,.078,.11)
    m.drum('fieldstone',0,.058,.076,.24,.15,.189,(9,7,5)[m.lod])
    m.drum('wall',0,.205,.076,.152,.67,.113,(8,7,5)[m.lod])
    for y,r in (((.27,.157),(.68,.138),(.84,.129)) if m.lod==0 else ((.68,.138),) if m.lod==1 else ()):
        m.drum('metal',0,y,.076,r,.034,r*.99,8)
    # The broad hammered mantle above the foot changes from wood-toned hide through copper to iron.
    m.drum('metal',0,.208,.076,.167,.236,.149,(9,7,5)[m.lod])
    # Raised, swept pinions are carved as broad diamond sections; the outer primaries narrow to a tip.
    m.sphere((0,.929,.074),.143,m.color('roof'),rings=(4,3,2)[m.lod],
             segments=(9,7,5)[m.lod],squash=.88,material='roof')
    for side in (-1,1):
        def wing_face(mat, points):
            m.face(mat,points if side>0 else points[::-1])
        span=[(.07,.94,.07,.083,.053),(.205,1.047,.073,.124,.034),
              (.325,1.115,.085,.095,.023)]
        sections=[[(side*x,y,z-depth),(side*x,y+thickness,z-.014),
                   (side*x,y-.013,z+depth),(side*x,y-thickness,z)]
                  for x,y,z,depth,thickness in span]
        wing_face('timber', sections[0][::-1])
        for inner, outer in zip(sections,sections[1:]):
            for i in range(4):
                j=(i+1)%4
                wing_face('roof' if i<2 else 'timber',[inner[i],inner[j],outer[j],outer[i]])
        tip=(side*.437,1.153,.137)
        for i in range(4):
            wing_face('roof',[sections[-1][i],sections[-1][(i+1)%4],tip])
    m.dome((0,.978,-.005),.092,m.color('ivory'),rings=(3,2,1)[m.lod],segments=(9,7,5)[m.lod],squash=1.17,material='ivory')
    with m.transform(translate=(0,.986,-.075),rotate_x=-106):
        m.drum('gold',0,0,0,.064,.112,0,5)
    m.standard(0,.655,-.076,.176,.286)
    m.drum('fieldstone',0,.065,-.213,.119,.045,.124,(7,6,4)[m.lod])
    if m.lod<2:
        m.flame(0,.111,-.213,.28)
    else:
        m.drum('emissive',0,.111,-.213,.04,.146,0,5)
    if m.lod<2:
        for s in (-1,1):
            m.drum('fieldstone',s*.252,.042,-.135,.085,.078,.059,6)
        if m.lod==0:
            for side in (-1,1):
                m.sphere((side*.044,1.042,-.064),.013,m.color('door'),rings=2,segments=5,material='door')


def shrine(m,era):
    # All roadside shrines present an open, accessible lamp under a weatherproof canopy.
    m.course('fieldstone',0,.036,.77,.63,0,.080,.086)
    m.steps(0,-.275,.48,.081,n=2,tread=.068)
    m.block('wall',(0,.156,.033),(.55,.152,.43),taper=.08)
    m.block('accent' if era>1 else 'fieldstone',(0,.243,.033),(.61,.042,.49))
    m.flame(0,.264,-.047,.64,era==3)
    if era==0:
        for s in (-1,1):
            m.block('timber',(s*.301,.400,.10),(.076,.64,.095),taper=.12)
            if m.lod<2:
                m.beam('trim',(s*.304,.54,.10),(s*.173,.728,.10),.043)
        m.block('trim',(0,.73,.10),(.70,.075,.127))
        m.gable(0,.748,.089,.82,.57,.32,'roof','plaster')
        if m.lod<2:
            m.standard(.302,.648,.045,.095,.173)
    elif era==1:
        for x in (-.288,.288):
            for z in (-.142,.205):
                m.drum('wall',x,.08,z,.052,.57,.043,n=(8,6,4)[m.lod])
                if m.lod==0:
                    m.block('ivory',(x,.633,z),(.131,.040,.13))
        m.block('wall',(0,.679,.034),(.77,.078,.56))
        m.drum('accent',0,.717,.034,.32,.052,.29,n=(12,8,6)[m.lod])
        m.dome_cap('roof',0,.769,.034,.29,.265)
        if m.lod<2:
            m.drum('gold',0,1.026,.034,.038,.076,.012,6)
            m.standard(.287,.613,-.194,.085,.15)
    elif era==2:
        for s in (-1,1):
            m.block('brick',(s*.295,.407,.079),(.102,.654,.33))
            m.pointed_light(s*.295,.34,-.087,.064,.27,'banner',divided=False)
        m.gable(0,.734,.04,.79,.65,.338,'slate','wall')
        m.rose(0,.830,-.266,.074)
        if m.lod<2:
            for s in (-1,1):
                m.beam('metal',(s*.251,.65,-.104),(s*.16,.737,-.198),.036)
    else:
        outline=[(-.335,.073),(-.275,.708),(0,.982),(.275,.708),(.335,.073)]
        with m.transform(translate=(0,0,.076),scale=(1,1,.36/.118)):
            m.bent_rail('roof',outline,0,.118)
        if m.lod<2:
            m.bent_rail('ivory',[(-.331,.107),*outline[1:-1],(.331,.107)],-.108,.033)
        m.standard(-.303,.646,-.116,.137,.227)
        m.block('metal',(0,.66,.229),(.54,.051,.06))
        with m.transform(translate=(0,.827,.22),rotate_z=45):
            m.block('glass',(0,0,0),(.26,.26,.065))
    if m.lod<2:
        m.planter(-.333,-.228,.073)
        if m.lod==0:
            m.planter(.337,-.202,.058)


def temple(m,era):
    m.course('fieldstone',0,.07,1.67,1.55,0,.145,.14)
    m.steps(0,-.703,1.04,.146,n=3,tread=.067)
    if era in (0,1):
        # A deep columned porch leads into the enclosed sanctuary of the temple.
        # Meet the underside of the roof at the actual wall plate. The shingle
        # courses and the plain lower LOD roof pitches have slightly different profiles.
        wall_top=((.982,.979,.992) if era==0 else (.952,.952,.962))[m.lod]
        m.block('wall',(0,(.14+wall_top)/2,.212),(1.28,wall_top-.14,1.07))
        m.pointed_light(0,.15,-.324,.36,.50,'door',divided=False)
        for x in ((-.595,.595) if era==0 else (-.624,-.286,.286,.624)):
            if era==0:
                m.block('timber',(x,.475,-.598),(.112,.66,.112),taper=.11)
            else:
                m.drum('wall',x,.145,-.588,.091,.64,.077,n=(9,7,5)[m.lod])
            if m.lod<2:
                m.block('ivory' if era else 'trim',(x,.768,-.59),(.20,.082,.18))
        m.block('trim' if era==0 else 'ivory',(0,.831,-.586),(1.53,.083,.23))
        m.gable(0,.871 if era==1 else .866,.07,1.62,1.57,.44 if era==1 else .61,
                'roof','plaster' if era==0 else 'wall')
        m.rose(0,.993,-.695,.102)
        if era==0:
            # The votive flame stands out in the open, beside the public approach.
            m.drum('fieldstone',.579,0,-.758,.145,.153,.13,7)
            m.drum('timber',.579,.152,-.758,.102,.172,.089,7)
            m.flame(.579,.324,-.758,.62)
            m.standard(-.477,.826,-.707,.202,.333)
        if m.lod<2:
            for s in (-1,1):
                m.standard(s*.443,.758,-.33,.15,.29)
                with m.transform(translate=(s*.641,0,0),rotate_y=-s*90):
                    for u in (-.09,.28):
                        m.pointed_light(u,.30,0,.19,.365,'glass')
            if era==0:
                for z in (-.18,.35):
                    for s in (-1,1):
                        m.block('trim',(s*.643,.472,z),(.086,.65,.067))
        if m.lod==0:
            m.planter(-.733,-.657,.088)
    else:
        radius=.59 if era==2 else .62
        m.drum('wall',0,.142,.156,radius,.71,n=(14,10,8)[m.lod])
        m.drum('brick' if era==2 else 'glass',0,.848,.156,radius*1.018,.205,n=(14,10,8)[m.lod])
        m.drum('accent',0,1.049,.156,radius*1.08,.053,n=(14,10,8)[m.lod])
        m.dome_cap('slate' if era==2 else 'glass',0,1.102,.156,radius*1.065,.535 if era==2 else .38)
        if era==2:
            m.drum('wall',0,1.62,.156,.118,.126,n=(8,6,5)[m.lod])
            m.dome_cap('slate',0,1.746,.156,.146,.146)
        else:
            frame=[(-.666,.145),(-.532,1.235),(0,1.78),(.532,1.235),(.666,.145)]
            with m.transform(translate=(0,0,.34),scale=(1,1,.24/.115)):
                m.bent_rail('wall',frame,0,.115)
        m.block('wall',(0,.443,-.438),(.62,.60,.39))
        m.pointed_light(0,.151,-.636,.379,.491,'door',divided=False)
        for x in (-.334,.334):
            m.block('metal' if era==2 else 'wall',(x,.407,-.615),(.087,.52,.08))
        m.gable(0,.67,-.499,.83,.53,.24 if era==2 else .36,'roof','wall')
        m.rose(0,.744,-.745,.063,era==3)
        if m.lod<2:
            for i in (-3,-2,1,2,3):
                a=2*math.pi*i/(14 if m.lod==0 else 10)
                with m.transform(translate=(0,0,.156),rotate_y=math.degrees(a)):
                    m.pointed_light(0,.33,-radius*.985,.15,.39,'glass')
            m.standard(-.267,.576,-.639,.084,.212)
        if m.lod==0:
            m.planter(.73,-.49,.108)


def cathedral(m,era):
    m.course('fieldstone',0,.08,2.55,2.55,0,.139,.22)
    m.steps(0,-1.189,1.035,.14,n=3,tread=.095)
    body='wall'
    m.block(body,(0,.83,.085),(1.04,1.39,2.19))
    m.gable(0,1.525,.091,1.22,2.42,.59 if era!=3 else .73)
    for s in (-1,1):
        m.block(body,(s*.742,.443,.20),(.505,.61,2.015))
        with m.transform(translate=(s*.744,.745,.20),rotate_y=-s*90):
            m.wedge((0,0,0),2.09,.575,.324,m.color('roof'),material='roof')
        if m.lod<2:
            for z in (-.394,.16,.721):
                m.block('fieldstone' if era<3 else 'wall',(s*1.040,.463,z),
                        (.177,.65,.128),taper=.23)
                m.beam('wall',(s*1.032,.722,z),(s*.493,1.302,z),.112,.143)
                if m.lod==0:
                    with m.transform(translate=(s*.999,0,z+.19),rotate_y=-s*90):
                        m.pointed_light(0,.266,0,.174,.365,'glass')
    front=-1.010
    m.pointed_light(0,.142,front,.49,.70,'door',divided=False)
    m.rose(0,1.235,front,.226,era==3)
    if era==1:
        for s in (-1,1):
            m.belfry(s*.756,-.832,.138,.53,1.63,era)
        if m.lod<2:
            m.gable(0,.84,-1.013,.59,.29,.24,'roof','wall')
    elif era==2:
        # An axial brick campanile sits on the crossing rather than duplicating the medieval facade.
        m.belfry(0,.37,1.28,.69,1.42,era)
        for s in (-1,1):
            m.block('brick',(s*.644,.68,-.875),(.332,1.08,.467))
            m.spire(s*.644,1.22,-.875,.425,.43)
    else:
        for s in (-1,1):
            m.beam('wall',(s*.845,.14,-.718),(s*.69,1.89,-.718),.266,.428)
            m.beam('wall',(s*.69,1.89,-.718),(s*.36,2.83,-.60),.239,.403)
            m.beam('glass',(s*.84,.36,-.943),(s*.69,1.885,-.943),.11,.058)
            m.beam('ascension',(s*.691,1.892,-.94),(s*.362,2.81,-.823),.064,.040)
            with m.transform(translate=(s*.36,2.812,-.60),rotate_z=s*18):
                m.pyramid((0,0,0),.238,.403,.265,m.color('metal'),material='metal')
        # The lantern drum cuts down through both pitches and raises the entire dome clear of the ridge.
        m.drum('wall',0,1.748,.66,.298,.568,n=(12,8,6)[m.lod])
        m.drum('metal',0,2.309,.66,.326,.058,n=(12,8,6)[m.lod])
        m.dome_cap('glass',0,2.367,.66,.303,.214)
    if m.lod<2:
        for s in (-1,1):
            m.standard(s*.389,.888,front,.111,.45)
            with m.transform(translate=(s*.522,0,0),rotate_y=-s*90):
                for z in (-.43,.12,.67):
                    m.pointed_light(z,1.086,0,.19,.351,'emissive')
    if m.lod==0:
        for s in (-1,1):
            m.planter(s*.594,-1.186,.104)


def monastery(m,era):
    # Three residential ranges surround a sunken herb garth; the pedestrian gate is on its south side.
    m.course('fieldstone',0,0,1.85,1.82,0,.08,.13)
    chapel_height=(0,.66,.74,.70)[era]
    for x in (-.626,.626):
        m.block('wall',(x,.31,.022),(.393,.47,1.32))
        if era==3:
            with m.transform(translate=(x,.547,.022),rotate_y=90 if x>0 else -90):
                m.wedge((0,0,0),1.45,.48,.155,m.color('roof'),material='roof')
        else:
            m.gable(x,.547,.022,.50,1.45,.243 if era==1 else .152)
        if m.lod<2:
            m.pointed_light(x,.19,-.642,.14,.258,'glass',divided=False)
            for z in (-.397,-.003,.339):
                with m.transform(translate=(x+(.199 if x>0 else -.199),0,z),rotate_y=-90 if x>0 else 90):
                    m.pointed_light(0,.222,0,.128,.22,'glass',divided=False)
    m.block('wall',(0,(.08+chapel_height)/2,.598),(1.53,chapel_height-.08,.41))
    with m.transform(translate=(0,0,.62),rotate_y=90):
        m.gable(0,chapel_height,0,.536,1.72,.25 if era!=2 else .188)
    if era==1:
        m.belfry(-.48,.606,.504,.257,.53,1)
    elif era==2:
        m.belfry(.45,.598,.572,.32,.67,2)
    else:
        m.drum('wall',0,.683,.60,.319,.263,n=(10,8,6)[m.lod])
        m.dome_cap('glass',0,.945,.60,.348,.278)
    m.pointed_light(0,.081,.388,.235,.401,'door',divided=False)
    for s in (-1,1):
        m.block('wall',(s*.351,.205,-.626),(.252,.251,.161))
        m.block('fieldstone',(s*.351,.335,-.626),(.273,.041,.187))
    for s in (-1,1):
        m.block('trim' if era<3 else 'wall',(s*.146,.302,-.703),(.063,.447,.076))
    m.gable(0,.529,-.679,.472,.276,.155 if era<3 else .213)
    # Gravel path and beds sit side by side so their top surfaces never overlap.
    m.block('plaster',(0,.088,-.11),(.188,.024,1.10))
    for s in (-1,1):
        m.block('soil',(s*.267,.101,-.032),(.196,.043,.618))
        if m.lod<2:
            for j in range(3):
                leaf='leaf' if j!=1 else 'grass'
                m.dome((s*.268,.123,-.24+j*.199),.089,m.color(leaf),rings=2 if m.lod==0 else 1,
                       segments=7 if m.lod==0 else 5,squash=.95,material=leaf)
    if m.lod<2:
        m.standard(.292,.551,.384,.141,.255)
        for s in (-1,1):
            m.light(s*.362,.372,.390,.121,.114,shutters=era<3)
    if m.lod==0:
        for s in (-1,1):
            m.planter(s*.289,-.761,.074)
        if era==2:
            m.flue(-.641,.431,.472,.902,.128)


def build(spec,lod):
    m=Sanctuary(spec,lod)
    era=ERAS.index(spec.band)
    if spec.building_type=='SpiritTotem':
        totem(m)
    else:
        {'Shrine':shrine,'Temple':temple,'Cathedral':cathedral,
         'Monastery':monastery}[spec.building_type](m,era)
    return m


if __name__=='__main__':
    pages = []
    for spec in package_models(PACKAGE):
        levels=[build(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        for age,path in entry['age_previews'].items():
            pages.append((age,spec.building_type,ROOT/path))
        print(spec.id,entry['lod_triangles'],flush=True)
    directory=pages[0][2].parent
    pages.sort(key=lambda row:(AGES.index(row[0]),row[1]))
    for age in AGES:
        rows=[row for row in pages if row[0]==age]
        contact_sheet([r[2] for r in rows],directory/f'sheet_{age}.png',cols=len(rows),
                      cell=224,labels=[f'{age.title()}  {r[1]}' for r in rows])
    contact_sheet([row[2] for row in pages],directory/'sheet_phone.png',cols=6,cell=72,
                  labels=[row[1] for row in pages])
