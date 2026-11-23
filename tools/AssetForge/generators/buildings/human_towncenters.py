"""Human council halls. The meeting porch, blue standard and town bell evolve together."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.human_housesancient import House


PACKAGE = "human_towncenters"


class Council(House):
    def box_between(self, mat, x, z, w, d, bottom, top):
        self.block(mat, (x, (bottom + top) / 2, z), (w, top - bottom, d))

    def drum(self, mat, x, y, z, radius, h, top=None, sides=None):
        self.cylinder((x, y, z), radius, h, self.color(mat),
                      segments=sides or (12, 8, 6)[self.lod], top_radius=top, material=mat)

    def ridge(self, x, z, w, d, height, rise, mat, *, across=False, hip=0, cap=None):
        with self.transform(translate=(x, 0, z), rotate_y=90 if across else 0):
            self.roof(d if across else w, w if across else d, height, rise, mat,
                      'wall', hip=hip, ridge_mat=cap if self.lod < 2 else None)

    def pennon(self, x, bottom, z, h, size, pole='trim'):
        self.box_between(pole, x, z, .022, .022, bottom, bottom+h)
        # Folded, swallow-tailed processional standard. Two closed sides share no planes.
        with self.transform(translate=(x, bottom+h, z), scale=size):
            sections = [(0, 0, -.003, -.48, -.003), (.52, -.08, .10, -.57, .11),
                        (1, -.04, .055, -.37, .059)]
            for a, b in zip(sections, sections[1:]):
                f = [(a[0], a[1], a[2]), (b[0], b[1], b[2]),
                     (b[0], b[3], b[4]), (a[0], a[3], a[4])]
                self.face('banner', f)
                self.face('banner', [(u,v,w+.012) for u,v,w in f[::-1]])
            if self.lod == 0:
                self.face('gold',[(.12,-.21,.014),(.22,-.105,.034),(.32,-.24,.049),
                                  (.22,-.35,.033)][::-1])

    def arch(self, x, face_z, w, bottom, h, material='ivory', infill='door', trim=.050):
        # Door or fanlight in its own voussoir surround. Spring line stays above the jambs.
        n = (8, 5, 3)[self.lod]
        r = w/2
        spring = bottom+h-r
        arc = [(x-r*math.cos(math.pi*i/n),spring+r*math.sin(math.pi*i/n)) for i in range(n+1)]
        outer = [(x-(r+trim)*math.cos(math.pi*i/n),spring+(r+trim)*math.sin(math.pi*i/n))
                 for i in range(n+1)]
        fill = [(x-r,bottom),*arc,(x+r,bottom)]
        self.face(infill, [(u,v,face_z-.006) for u,v in fill])
        if self.lod == 2:
            return
        for s in (-1,1):
            self.box_between(material,x+s*(r+trim/2),face_z-.010,trim,.029,bottom,spring)
        for i in range(n):
            a,b = arc[i:i+2]
            c,d = outer[i:i+2]
            self.face(material,[(u,v,face_z-.028) for u,v in (a,c,d,b)])
            self.face(material,[(a[0],a[1],face_z-.028),(b[0],b[1],face_z-.028),
                                (b[0],b[1],face_z),(a[0],a[1],face_z)])
        if self.lod == 0 and infill == 'door':
            self.box_between('timber',x,face_z-.016,.012,.010,bottom,spring+r*.92)
            for s in (-1,1):
                self.block('gold',(x+s*.027,bottom+h*.40,face_z-.025),(.014,.026,.012))

    def steps(self, x, front, width, height, depth, mat='fieldstone'):
        n = (3, 2, 1)[self.lod]
        for i in range(n):
            # Each tread meets the next riser. Tops and risers remain distinct.
            self.box_between(mat,x,front-depth+(i+.5)*depth/n,
                             width-.026*i,depth/n,.001,height*(i+1)/n)

    def crest(self, x, top, z, width, h):
        # The crossed-roads emblem is a gold lozenge on the council's blue shield.
        with self.transform(translate=(x,top,z), scale=(width,h,width)):
            self.face('banner', [(0,-1,-.030),(-.45,-.73,-.018),(-.5,0,0),
                                 (.5,0,0),(.45,-.73,-.018)])
            self.face('gold',[(0,-.71,-.050),(-.18,-.41,-.045),(0,-.14,-.040),
                              (.18,-.41,-.045)])
            if self.lod == 0:
                self.block('gold',(0,.01,-.009),(1.12,.058,.07))

    def garden(self, x, z, w):
        if self.lod == 2:
            return
        self.box_between('fieldstone',x,z,w,.15,.002,.098)
        self.block('leaf',(x,.115,z),(w*.91,.054,.12),taper=.18)
        if self.lod == 0:
            for i, mat in enumerate(('flower','ivory','flower','flower','ivory')):
                self.dome((x+w*(i-2)*.161,.136+.012*(i%2),z+.015*math.cos(i*2)),.026,
                          self.color(mat),rings=1,segments=5,material=mat)

    def bell(self, x, foot, z, width, h, roof='roof', metal='gold'):
        # Four posts on a real curb carry the roof. Bell hangs from its crosshead.
        self.box_between('trim',x,z,width,width,foot-.055,foot+.025)
        for s in (-1,1):
            for t in (-1,1):
                self.box_between('trim',x+s*width*.377,z+t*width*.377,width*.126,
                                 width*.126,foot+.019,foot+h)
        self.box_between('trim',x,z,width*1.04,width*1.04,foot+h-.028,foot+h+.026)
        self.pyramid((x,foot+h+.026,z),width*1.3,width*1.3,width*.64,
                     self.color(roof),material=roof)
        if self.lod < 2:
            self.drum(metal,x,foot+h*.22,z,width*.262,h*.18,width*.175)
            self.drum(metal,x,foot+h*.40,z,width*.175,h*.35,width*.088)
            self.drum('trim',x,foot+h*.75,z,width*.056,h*.19)
            if self.lod == 0:
                self.drum('door',x,foot+h*.12,z,width*.053,h*.11)
        return foot+h+.026+width*.64

    def windows(self, x_positions, y, z, w, h, shutters=True):
        if self.lod != 2:
            for x in x_positions:
                self.light(x,y,z,w,h,shutters)

    def side_lights(self, side, z_positions, y, w, h, shutters=True):
        if self.lod != 2:
            with self.transform(translate=(side,0,0),rotate_y=-90):
                self.windows(z_positions,y,0,w,h,shutters)

    def column(self, x, z, base, top, r):
        if self.lod == 2:
            self.drum('ivory',x,base,z,r,top-base,r*.88,sides=5)
            return
        self.drum('ivory',x,base,z,r*1.32,.045,r*1.14)
        self.drum('plaster',x,base+.045,z,r,top-base-.09,r*.83)
        self.drum('ivory',x,top-.045,z,r*.98,.045,r*1.34)


def dawn(m):
    # A reed moot hall. The entire end opens on a public porch under a second gable.
    m.course('soil',0,.07,1.65,1.40,0,.082,.15)
    m.course('hide',0,.16,1.34,1.01,.078,.63,.065)
    m.ridge(0,.16,1.56,1.30,.626,.65,'thatch',hip=.17,cap='timber')
    m.steps(0,-.60,.86,.083,.24)
    m.arch(0,-.347,.32,.083,.44,'timber',trim=.040)
    for x in (-.43,.43):
        m.beam('timber',(x,.074,-.632),(x*.97,.54,-.615),.075)
        if m.lod < 2:
            m.beam('timber',(x,.43,-.61),(x*.70,.59,-.61),.040)
    m.ridge(0,-.454,.98,.49,.543,.295,'thatch',hip=.01,cap='timber')
    m.crest(0,.765,-.703,.16,.15)
    m.side_lights(.671,[-.065,.38],.32,.15,.16)
    if m.lod < 2:
        for side in (-1,1):
            m.beam('timber',(side*.641,.08,-.31),(side*.663,.625,-.32),.064)
        m.flue(-.42,.46,.60,1.14,.132)
    if m.lod == 0:
        m.basket(-.623,-.473,.090,.132)
        # A hide calling drum sits beside the speaker's porch, on a little trestle.
        for x in (.532,.703):
            m.box_between('timber',x,-.524,.042,.106,.080,.170)
        m.drum('timber',.618,.16,-.524,.110,.163,.127,sides=10)
        m.drum('hide',.618,.319,-.524,.131,.028,sides=10)
        m.beam('ivory',(.543,.353,-.52),(.674,.376,-.552),.016)
        for z in (-.14,.36):
            m.bent_rail('hide',[(-.15,1.121),(0,1.288),(.15,1.121)],z,.026)
    m.pennon(.405,.385,-.62,.48,.22,'timber')


def bronze(m):
    # Two mudbrick wings meet a stepped gatehouse with an open alarm gong.
    m.course('fieldstone',0,.055,1.79,1.39,0,.092,.11)
    m.box_between('brick',0,.12,1.60,1.12,.09,.60)
    m.ridge(0,.13,1.76,1.25,.60,.31,'hide',across=True,hip=.18,cap='timber')
    m.box_between('wall',0,-.25,.71,.72,.088,.91)
    m.box_between('trim',0,-.25,.76,.76,.88,.932)
    m.bell(0,.935,-.25,.52,.30,'roof_tile','metal')
    m.steps(0,-.610,.63,.094,.263)
    m.arch(0,-.612,.30,.09,.48,'hide',trim=.055)
    m.crest(0,.864,-.623,.19,.205)
    m.windows([-.57,.57],.36,-.442,.17,.185)
    m.side_lights(.801,[-.17,.38],.34,.13,.16,False)
    if m.lod < 2:
        for s in (-1,1):
            m.box_between('plaster',s*.326,-.597,.084,.075,.095,.879)
    if m.lod == 0:
        m.jar(.51,-.595,.09,.081,.174)
        m.jar(-.61,-.556,.09,.060,.111)
    m.pennon(-.345,.66,-.39,.47,.25)


def iron(m):
    # Stone assembly range with its own forward watch porch, timber chevrons and shake roofs.
    m.course('fieldstone',0,.06,1.80,1.40,0,.145,.13)
    m.box_between('fieldstone',-.10,.16,1.37,.93,.142,.71)
    m.ridge(-.10,.16,1.59,1.16,.704,.54,'roof',across=True,hip=.035,cap='timber')
    m.box_between('wall',.33,-.27,.56,.61,.143,.99)
    m.ridge(.33,-.29,.72,.79,.98,.49,'roof',cap='timber')
    m.steps(.33,-.575,.58,.147,.247)
    m.arch(.33,-.578,.26,.146,.46,'fieldstone',trim=.046)
    m.crest(.33,.944,-.588,.212,.235)
    m.windows([-.58,-.16],.433,-.307,.147,.176)
    m.side_lights(.613,[-.41,-.10],.727,.105,.143)
    m.side_lights(.588,[.12,.44],.458,.13,.172)
    if m.lod != 2:
        # Porch-side benches sit beyond the tower wall, with two mortised stone feet.
        for z in (.07,.43):
            m.box_between('fieldstone',.681,z,.119,.080,.144,.215)
        m.block('timber',(.684,.238,.252),(.157,.055,.546))
    if m.lod < 2:
        m.box_between('timber',.33,-.27,.594,.644,.655,.707)
        for x in (.077,.582):
            m.box_between('trim',x,-.575,.044,.062,.143,1.006)
        m.flue(-.573,.34,.69,1.30,.172)
    if m.lod == 0:
        m.herbs(-.57,.306,-.308,.224)
        m.bent_rail('trim',[(.071,.975),(.329,1.433),(.591,.975)],-.69,.037)
    m.pennon(.33,1.41,-.19,.308,.241)


def classical(m):
    # Legislative basilica with a recessed colonnade, reached by a wide marble stair.
    m.course('fieldstone',0,.024,1.79,1.49,0,.12,.09)
    m.course('ivory',0,.024,1.69,1.41,.116,.169,.049)
    m.box_between('plaster',0,.185,1.48,.98,.167,.83)
    m.ridge(0,.185,1.71,1.17,.83,.34,'roof_tile',across=True,hip=.10,cap='roof_tile')
    m.arch(0,-.307,.335,.166,.495,'ivory')
    for s in (-1,1):
        m.windows([s*.52],.48,-.31,.145,.183,False)
    for x in (-.64,-.30,.30,.64):
        m.column(x,-.584,.167,.667,.068)
    m.box_between('ivory',0,-.482,1.526,.37,.661,.748)
    m.ridge(0,-.48,1.51,.44,.748,.237,'roof_tile',cap='gold')
    m.crest(0,.898,-.708,.17,.125)
    m.steps(0,-.681,1.31,.169,.266,'plaster')
    m.side_lights(.741,[-.064,.20,.46],.52,.137,.235,False)
    if m.lod < 2:
        m.box_between('ivory',0,.185,1.56,1.045,.787,.840)
        m.garden(.735,-.62,.22)
    m.pennon(0,1.119,.21,.292,.250,'metal')


def medieval(m):
    # Projecting half-timber council chamber, paired gables and a gatehouse belfry.
    m.course('fieldstone',0,.065,1.83,1.38,0,.135,.11)
    m.box_between('fieldstone',0,.15,1.62,.98,.13,.56)
    m.box_between('wall',0,.145,1.68,1.037,.555,.904)
    m.ridge(0,.15,1.86,1.24,.90,.472,'slate',across=True,hip=.117,cap='trim')
    m.box_between('fieldstone',-.16,-.333,.56,.62,.129,1.235)
    m.box_between('ivory',-.16,-.335,.578,.643,.803,1.239)
    tip = m.bell(-.16,1.238,-.336,.54,.307,'slate')
    m.pennon(-.16,tip-.017,-.336,.238,.205,'metal')
    m.steps(-.16,-.65,.59,.134,.21)
    m.arch(-.16,-.647,.29,.136,.443,'plaster')
    m.crest(-.16,1.160,-.666,.23,.28)
    m.windows([-.642,.417,.665],.722,-.376,.105,.132)
    m.side_lights(.841,[-.168,.143,.452],.714,.119,.138)
    if m.lod < 2:
        m.box_between('timber',0,.145,1.698,1.059,.548,.605)
        for x in (-.778,-.501,.247,.539,.797):
            m.box_between('trim',x,-.374,.042,.030,.601,.903)
        for s in (-1,1):
            m.box_between('trim',-.16+s*.261,-.653,.041,.039,.806,1.237)
        m.flue(.58,.33,.915,1.512,.115)
    if m.lod == 0:
        m.garden(.51,-.56,.44)
        for z in (-.32,-.015,.3,.61):
            m.beam('trim',(.849,.61,z),(.849,.887,z+.145),.030)
        m.windows([-.635,.52],.314,-.342,.141,.155,False)


def renaissance(m):
    # Merchants' council palace. The octagonal terracotta council dome sits on a glazed drum.
    m.course('fieldstone',0,.05,1.84,1.41,0,.107,.102)
    m.box_between('wall',0,.12,1.62,.99,.102,.79)
    m.ridge(0,.12,1.78,1.16,.784,.309,'roof_tile',across=True,hip=.12,cap='roof_tile')
    m.drum('ivory',-.045,.948,.12,.436,.110,.407)
    m.drum('wall',-.045,1.058,.12,.399,.223)
    m.drum('ivory',-.045,1.274,.12,.424,.041)
    n=(12,8,6)[m.lod]
    m.dome((-.045,1.314,.12),.427,m.color('roof_tile'),rings=(4,3,2)[m.lod],
           segments=n,squash=.84,material='roof_tile')
    m.drum('gold',-.045,1.660,.12,.083,.081,.060)
    m.pennon(-.045,1.737,.12,.222,.202,'metal')
    for x in (-.52,0,.52):
        m.arch(x,-.380,.25,.105,.49,'plaster',infill='door' if x==0 else 'glass',trim=.040)
    m.box_between('ivory',0,-.396,1.70,.113,.688,.754)
    m.crest(0,.755,-.458,.182,.145)
    m.steps(0,-.645,.54,.109,.235)
    m.side_lights(.812,[-.16,.13,.417],.426,.149,.21,False)
    if m.lod < 2:
        for i in range(n):
            a=(i+.5)*2*math.pi/n
            with m.transform(translate=(-.045+.399*math.sin(a),1.13,.12-.399*math.cos(a)),
                             rotate_y=-math.degrees(a)):
                m.light(0,.035,-.003,.088,.082,False)
        for s in (-1,1):
            m.garden(s*.62,-.552,.32)
    if m.lod == 0:
        m.flue(.58,.31,.805,1.192,.10)


def clock(m,x,y,z,r):
    # Dial stands proud of the tower. Its hands have a shared brass boss.
    with m.transform(translate=(x,y,z),rotate_x=-90):
        m.drum('trim',0,0,0,r,.028)
        m.drum('ivory',0,.026,0,r*.84,.014)
    if m.lod < 2:
        m.beam('door',(x,y,z-.047),(x-r*.33,y+r*.46,z-.047),.016)
        m.beam('door',(x,y,z-.048),(x+r*.55,y+r*.19,z-.048),.013)
    if m.lod == 0:
        for i in range(12):
            a=i*math.pi/6
            m.block('gold',(x+r*.67*math.sin(a),y+r*.67*math.cos(a),z-.047),
                    (.012,.016,.009))


def industrial(m):
    # A railway-era municipal exchange. The public clock rises separately from the boiler flue.
    m.course('fieldstone',0,.065,1.84,1.40,0,.115,.085)
    m.box_between('brick',-.04,.125,1.53,1.045,.11,.813)
    m.ridge(-.04,.125,1.67,1.17,.808,.342,'slate',across=True,hip=.083,cap='metal')
    m.box_between('brick',.39,-.28,.48,.568,.111,1.322)
    m.box_between('ivory',.39,-.28,.518,.599,1.080,1.133)
    m.box_between('wall',.39,-.28,.453,.541,1.307,1.625)
    clock(m,.39,1.456,-.553,.139)
    with m.transform(translate=(.619,1.456,-.28),rotate_y=-90):
        clock(m,0,0,0,.132)
    m.box_between('trim',.39,-.28,.521,.599,1.612,1.672)
    m.pyramid((.39,1.671,-.28),.562,.632,.33,m.color('metal'),material='metal')
    m.pennon(.39,1.985,-.28,.202,.191,'metal')
    m.steps(.39,-.568,.50,.114,.25)
    m.arch(.39,-.567,.253,.114,.441,'plaster')
    m.crest(.39,.996,-.571,.203,.245)
    for x in (-.58,-.195):
        m.arch(x,-.401,.193,.280,.36,'plaster',infill='glass',trim=.027)
    m.side_lights(.728,[-.008,.376],.467,.162,.241,False)
    m.drum('brick',-.587,.78,.362,.104,.694,.078)
    m.drum('trim',-.587,1.47,.362,.090,.063)
    if m.lod < 2:
        m.box_between('plaster',-.04,.125,1.56,1.072,.723,.783)
        m.garden(-.405,-.552,.42)
        m.drum('door',-.587,1.532,.362,.061,.003)
    if m.lod == 0:
        m.box_between('plaster',.39,-.28,.495,.583,.592,.644)


def modern(m):
    # Mid-century council chamber with a clerestory and a freestanding-corner clock slab.
    m.course('fieldstone',0,.03,1.84,1.39,0,.092,.11)
    m.box_between('plaster',0,.12,1.57,1.05,.09,.441)
    m.box_between('glass',0,.12,1.55,1.03,.44,.664)
    m.box_between('ivory',0,.12,1.74,1.18,.659,.742)
    m.box_between('glass',-.19,.19,.94,.71,.739,.943)
    with m.transform(translate=(-.19,.998,.19),rotate_z=-8):
        m.box((0,0,0),(1.04,.084,.807),m.color('metal'),bottom=True,material='metal')
    m.box_between('plaster',.515,-.281,.315,.57,.088,1.342)
    clock(m,.515,1.151,-.570,.126)
    m.box_between('metal',.515,-.281,.346,.606,1.339,1.398)
    m.pennon(.51,1.393,-.268,.33,.242,'metal')
    m.arch(-.14,-.408,.32,.089,.334,'metal',infill='door',trim=.027)
    m.steps(-.14,-.59,.84,.093,.24)
    # Public canopy is braced at the wall and supported on two pavement columns.
    for x in (-.52,.24):
        m.box_between('metal',x,-.576,.046,.055,.087,.440)
    m.box_between('ivory',-.14,-.49,.899,.36,.435,.495)
    m.crest(.517,.916,-.575,.170,.228)
    if m.lod != 2:
        for x in (-.70,-.48,-.25,-.02,.21,.71):
            m.box_between('trim',x,-.399,.023,.027,.442,.658)
        for z in (-.26,-.058,.16,.38,.556):
            m.box_between('metal',.777,z,.023,.032,.442,.658)
        m.garden(.656,-.609,.33)
    if m.lod == 0:
        for x in (-.56,-.34,-.12,.10):
            m.box_between('metal',x,-.168,.025,.030,.745,.952)
        m.garden(-.67,-.571,.25)
        m.block('metal',(-.19,.917,.19),(.76,.11,.62))


def glazed_tower(m,x,z,w,d,base,top,levels):
    m.course('glass',x,z,w,d,base,top,corner=.091)
    ys = range(1,levels) if m.lod < 2 else range(3,levels,3)
    for i in ys:
        y=base+(top-base)*i/levels
        m.course('ivory',x,z,w+.026,d+.026,y,y+.032,corner=.088)
    if m.lod == 0:
        for s in (-1,1):
            m.box_between('metal',x+s*(w*.5-.088),z-d/2,.022,.029,base+.017,top)
        with m.transform(translate=(x+w/2,0,z),rotate_y=-90):
            for s in (-1,1):
                m.box_between('metal',s*d*.25,0,.016,.022,base+.011,top)
    m.course('ivory',x,z,w+.036,d+.036,top-.014,top+.044,corner=.091)


def information(m):
    # Two staggered glass lanterns wrap around a lower council chamber and its public forecourt.
    m.course('fieldstone',0,.024,1.82,1.44,0,.105,.115)
    m.course('wall',0,.107,1.60,1.11,.101,.414,.13)
    if m.lod < 2:
        m.course('ivory',0,.108,1.69,1.18,.408,.466,.14)
    else:
        m.box_between('ivory',0,.106,1.66,1.17,.408,.466)
    glazed_tower(m,-.277,.205,.73,.80,.463,1.640,5)
    glazed_tower(m,.299,.097,.49,.64,.465,1.121,3)
    m.box_between('metal',-.277,.24,.39,.47,1.679,1.778)
    with m.transform(translate=(-.277,1.79,.24),rotate_z=-8):
        m.box((0,0,0),(.56,.063,.56),m.color('glass'),bottom=True,material='glass')
    m.pennon(-.244,1.81,.21,.31,.229,'metal')
    m.steps(.15,-.60,.77,.106,.22)
    m.box_between('glass',.13,-.446,.46,.20,.103,.446)
    m.entry(.13,-.549,.246,.101,.282,lintel='metal')
    for x in (-.17,.46):
        m.box_between('metal',x,-.571,.041,.048,.102,.43)
    m.box_between('ivory',.145,-.457,.78,.378,.425,.480)
    m.crest(.615,.398,-.453,.18,.20)
    if m.lod != 2:
        m.garden(-.487,-.548,.48)
        m.box_between('ascension',.550,.094,.021,.339,.64,.687)
        m.windows([-.57,-.344],.264,-.450,.145,.126,False)
    if m.lod == 0:
        m.box_between('metal',-.103,.394,.026,.026,1.780,1.999)


def halo(m,x,y,z,r,t):
    # A rectangular-section annulus. Three angled struts below carry the council beacon.
    n=(28,18,10)[m.lod]
    def ring(rad,h,i):
        a=i*2*math.pi/n
        return (x+rad*math.cos(a),y+h,z+rad*math.sin(a))
    for i in range(n):
        j=(i+1)%n
        for mat,points in (
                ('gold',(ring(r,0,j),ring(r,0,i),ring(r,t,i),ring(r,t,j))),
                ('ivory',(ring(r-t,t,j),ring(r,t,j),ring(r,t,i),ring(r-t,t,i))),
                ('ascension',(ring(r-t,0,i),ring(r-t,0,j),ring(r-t,t,j),ring(r-t,t,i))),
                ('gold',(ring(r-t,0,i),ring(r,0,i),ring(r,0,j),ring(r-t,0,j)))):
            m.face(mat,points)


def ascension(m):
    # A pearl council rotunda with a suspended-looking ring physically carried by three piers.
    m.course('plaster',0,.03,1.82,1.46,0,.115,.20)
    m.drum('ivory',0,.113,.10,.70,.39,.623)
    m.drum('gold',0,.502,.10,.655,.052,.673)
    m.drum('glass',0,.553,.10,.63,.27,.576)
    m.drum('ivory',0,.820,.10,.64,.102,.51)
    m.dome((0,.921,.10),.506,m.color('ivory'),rings=(4,3,2)[m.lod],
           segments=(16,12,8)[m.lod],squash=.77,material='ivory')
    for i in range(3):
        a=i*math.tau/3+math.pi/2
        ux,uz=math.cos(a),math.sin(a)
        start=(.61*ux,.388,.10+.61*uz)
        elbow=(.69*ux,1.027,.10+.69*uz)
        head=(.503*ux,1.547,.10+.503*uz)
        m.beam('ivory',start,elbow,.137)
        m.beam('gold',elbow,head,.093)
    halo(m,0,1.531,.10,.546,.072)
    m.drum('gold',0,1.29,.10,.077,.112,.051)
    m.drum('ascension',0,1.398,.10,.081,.12,.035)
    m.box_between('ivory',0,-.468,.534,.355,.113,.557)
    m.arch(0,-.649,.297,.113,.358,'gold',trim=.032)
    m.box_between('gold',0,-.475,.608,.401,.55,.59)
    m.crest(0,.559,-.683,.103,.086)
    m.steps(0,-.65,.579,.116,.236,'plaster')
    if m.lod < 2:
        for s in (-1,1):
            m.garden(s*.58,-.523,.31)
        for i in range(9):
            a=i*math.tau/9
            m.beam('gold',(.63*math.cos(a),.557,.10+.63*math.sin(a)),
                   (.58*math.cos(a),.824,.10+.58*math.sin(a)),.023)
    if m.lod == 0:
        for i in range(8):
            # Meridian seams follow the actual faceted dome stations continuously.
            azimuth=i*math.tau/8
            def seam(station, delta):
                latitude=station*math.pi/8
                r=.511*math.cos(latitude)
                return (r*math.cos(azimuth+delta),.923+.511*.77*math.sin(latitude),
                        .10+r*math.sin(azimuth+delta))
            for j in range(3):
                m.face('gold',[seam(j,.015),seam(j,-.015),seam(j+1,-.015),seam(j+1,.015)])


DESIGNS = dict(zip(AGES, (dawn, bronze, iron, classical, medieval, renaissance,
                           industrial, modern, information, ascension)))


def build(spec, lod):
    mesh = Council(spec, lod)
    DESIGNS[mesh.age](mesh)
    rear = {
        'dawn': (.666,.39,(-.29,.31)), 'bronze': (.682,.37,(-.50,.50)),
        'iron': (.626,.43,(-.44,.23)), 'classical': (.678,.50,(-.49,0,.49)),
        'medieval': (.665,.712,(-.56,-.20,.20,.56)),
        'renaissance': (.617,.438,(-.54,0,.54)),
        'industrial': (.650,.491,(-.52,-.14,.29,.57)),
    }
    if mesh.age in rear and lod < 2:
        depth,height,positions = rear[mesh.age]
        with mesh.transform(translate=(0,0,depth),rotate_y=180):
            mesh.windows(positions,height,0,.136,.17,shutters=mesh.age in ('dawn','iron','medieval'))
            if lod == 0 and mesh.age in ('bronze','medieval'):
                mesh.herbs(positions[0],height-.118,0,.215)
    return mesh


if __name__ == '__main__':
    previews = {}
    for spec in package_models(PACKAGE):
        models = [build(spec, lod) for lod in range(3)]
        entry = publish(models[0], spec.metadata(), PACKAGE, lods=models[1:], preview_size=256)
        previews[spec.ages[0]] = ROOT / entry['preview']
        print(spec.id, entry['lod_triangles'], flush=True)
    images = [previews[age] for age in AGES]
    contact_sheet(images, images[0].parent / 'human_towncenters_sheet.png',
                  cols=5, cell=224, labels=[age.title() for age in AGES])
