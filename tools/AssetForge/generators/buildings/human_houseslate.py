"""Eight human homes: brick terraces, postwar villas, solar lofts and pearl pavilions."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesclassical import MasonryHouse


class CityHouse(MasonryHouse):
    def glazing(self,x,y,z,w,h,divisions=3,frame='metal',glass='glass'):
        self.rect(frame,x,y,z,w+.026,h+.026)
        self.rect(glass,x,y,z-.004,w,h)
        if self.lod < 2:
            for i in range(1,divisions):
                self.rect(frame,x+w*(i/divisions-.5),y,z-.009,.014,h)
        if self.lod == 0:
            self.block('ivory',(x,y-h/2-.012,z-.010),(w+.056,.026,.043))

    def side_glass(self,x,z,y,w,h,divisions=3):
        with self.transform(translate=(x,0,z),rotate_y=-90):
            self.glazing(0,y,-.002,w,h,divisions)

    def envelope(self,section,front,back,base,wall,roof,end=None):
        # A vertical strip per pitch also closes concave butterfly gables correctly.
        for (x,y),(u,v) in zip(section,section[1:]):
            self.face(roof,[(x,y,front),(x,y,back),(u,v,back),(u,v,front)])
            self.face(end or wall,[(u,base,front),(x,base,front),(x,y,front),(u,v,front)])
            self.face(end or wall,[(x,base,back),(u,base,back),(u,v,back),(x,y,back)])
        left,right=section[0],section[-1]
        for (x,y),s in [(left,-1),(right,1)]:
            face=[(x,base,front),(x,base,back),(x,y,back),(x,y,front)]
            self.face(wall,face if s<0 else face[::-1])

    def folded_roof(self,section,front,back,thickness,roof,fascia):
        for (x,y),(u,v) in zip(section,section[1:]):
            a,b,c,d=(x,y,front),(x,y,back),(u,v,back),(u,v,front)
            e,f,g,h=[(px,py-thickness,pz) for px,py,pz in (a,b,c,d)]
            self.face(roof,(a,b,c,d))
            self.face(fascia,(h,e,a,d))
            self.face(fascia,(f,g,c,b))
            self.face('ivory',(h,g,f,e))
        for (x,y),s in [(section[0],-1),(section[-1],1)]:
            face=[(x,y-thickness,front),(x,y-thickness,back),(x,y,back),(x,y,front)]
            self.face(fascia,face if s<0 else face[::-1])

    def switchback(self,x,z,base,top):
        # A rooftop clothes-drying rack. The blue cloth folds across the rail.
        for u in (-.077,.077):
            self.block('metal',(x+u,(top+base)/2,z),(.014,top-base,.018))
        self.block('timber',(x,top,z),(.170,.016,.020))
        self.face('banner',[(x+.038,top-.075,z-.012),(x-.044,top-.063,z-.014),
                            (x-.044,top+.006,z-.007),(x+.038,top+.006,z-.007)])
        self.face('banner',[(x-.044,top-.035,z+.019),(x+.038,top-.040,z+.018),
                            (x+.038,top+.006,z+.006),(x-.044,top+.006,z+.006)])

    def hearth(self,x,z,bottom,top,twin=False):
        self.block('brick',(x,(bottom+top)/2,z),(.136,top-bottom,.101))
        self.block('brick',(x,top-.024,z),(.161,.049,.123))
        n=(8,6,4)[self.lod]
        for dx in ((-.041,.041) if twin else (0,)):
            self.drum('roof_tile',x+dx,top,z,.029,.079,n,.025)
            if self.lod < 2:
                self.drum('roof_tile',x+dx,top+.072,z,.032,.022,n)
                self.drum('door',x+dx,top+.094,z,.018,.001,n)

    def halo(self,x,y,z,outer,inner,thickness,n):
        def vertex(i,r,h):
            a=2*math.pi*i/n
            return (x+math.cos(a)*r,h,z+math.sin(a)*r)
        for i in range(n):
            j=(i+1)%n
            a,b,c,d=[vertex(k,r,y) for k,r in ((i,outer),(i,inner),(j,inner),(j,outer))]
            e,f,g,h=[(px,py-thickness,pz) for px,py,pz in (a,b,c,d)]
            self.face('ivory',(a,b,c,d))
            self.face('gold',(h,e,a,d))
            self.face('ascension',(f,g,c,b))
            self.face('ivory',(h,g,f,e))


def weaver(m):
    # Tall attic strip lights the family's weaving floor under the steep slate roof.
    m.course('fieldstone',0,.025,.729,.687,0,.064)
    m.course('brick',0,.025,.686,.647,.055,.550)
    with m.transform(translate=(0,0,.025),rotate_y=90):
        m.roof(.718,.780,.548,.286,'slate','brick',ridge_mat='trim')
    m.hearth(-.199,.166,.451,.828,True)
    m.entry(-.162,-.301,.143,.060,.264,lintel='brick')
    m.glazing(.132,.235,-.301,.170,.166,2,frame='timber',glass='emissive')
    m.glazing(.012,.451,-.301,.525,.111,5,frame='ivory')
    m.side_glass(.344,.031,.269,.216,.191,2)
    if m.lod < 2:
        m.block('ivory',(0,.541,-.300),(.671,.029,.041))
        m.herbs(.132,.127,-.300,.211)
        m.rear_vent(-.04,.352,.440,.362,.116,5)
    if m.lod == 0:
        m.arched(-.162,.322,-.306,.115,.055,'glass','ivory')
        for x in (-.304,.299):
            m.rect('roof_tile',x,.374,-.303,.067,.025)
        m.block('metal',(-.278,.246,-.330),(.053,.077,.050))
        m.rect('gold',-.278,.262,-.357,.037,.010)


def gate_cottage(m):
    # Lower kitchen return and forward projecting gable form an L around the entry.
    m.course('fieldstone',.059,.068,.702,.554,0,.065)
    m.course('brick',.1265,.068,.523,.512,.056,.388)
    with m.transform(translate=(.139,0,.068),rotate_y=90):
        m.roof(.615,.601,.393,.244,'slate','brick',ridge_mat='metal')
    m.course('brick',-.161,-.051,.378,.590,0,.454)
    with m.transform(translate=(-.161,0,-.050)):
        m.roof(.469,.693,.459,.281,'slate','brick',ridge_mat='metal')
    m.hearth(.185,.143,.326,.696)
    m.glazing(-.160,.264,-.349,.224,.182,3,frame='ivory')
    m.entry(.169,-.191,.149,.056,.254,lintel='ivory')
    m.glazing(-.161,.497,-.380,.086,.069,2,frame='ivory')
    if m.lod < 2:
        m.side_glass(.389,.051,.247,.165,.139)
        m.herbs(-.16,.147,-.347,.249)
        m.block('slate',(.175,.338,-.236),(.286,.039,.166))
        m.rear_vent(.173,.326,.233,.151,.108)
    if m.lod == 0:
        for x in (.079,.267):
            m.beam('trim',(x,.248,-.191),(x,.323,-.285),.022)
        m.bent_rail('ivory',[(-.344,.460),(-.161,.674),(.022,.460)],-.367,.023)
        m.block('banner',(.312,.196,-.205),(.049,.078,.029))


def butterfly(m):
    section=[(-.365,.557),(.058,.401),(.371,.507)]
    roof=[(-.426,.620),(.058,.442),(.427,.568)]
    m.course('fieldstone',0,.034,.778,.650,0,.063)
    m.envelope(section,-.266,.334,.057,'plaster','metal')
    m.folded_roof(roof,-.338,.383,.047,'metal','ivory')
    m.entry(.178,-.268,.143,.063,.271,lintel='metal')
    m.glazing(-.123,.272,-.268,.356,.187,3,frame='timber')
    m.side_glass(.372,.028,.277,.338,.153)
    if m.lod < 2:
        m.herbs(-.142,.140,-.269,.229)
        m.rear_vent(-.11,.336,.284,.285,.141)
        m.block('banner',(.31,.239,-.281),(.029,.197,.031))
    if m.lod == 0:
        m.switchback(-.194,.375,.004,.299)
        for x in (-.31,-.24,-.17):
            m.rect('timber',x,.433,-.268,.026,.070)
        # Rain chain runs down the low point of the butterfly gutter.
        m.drum('metal',.057,.035,-.318,.009,.370,6)
        m.drum('fieldstone',.057,.007,-.318,.039,.034,8,.030)


def deck_house(m):
    m.course('fieldstone',0,.032,.831,.715,0,.060)
    m.course('plaster',0,.031,.771,.667,.052,.347)
    m.block('ivory',(0,.350,.031),(.839,.051,.719))
    m.block('timber',(-.124,.480,.086),(.438,.214,.469))
    m.block('ivory',(-.125,.599,.086),(.524,.043,.554))
    m.glazing(-.12,.484,-.152,.318,.127,4)
    m.side_glass(.096,.061,.474,.255,.115)
    m.entry(.201,-.304,.145,.060,.231,lintel='metal')
    m.glazing(-.133,.211,-.305,.355,.157,3)
    if m.lod < 2:
        m.side_glass(.388,.084,.214,.296,.127,3)
        m.block('metal',(.362,.444,.055),(.022,.020,.464))
        for z in (-.149,.266):
            m.block('metal',(.362,.401,z),(.016,.064,.016))
        m.herbs(-.169,.103,-.305,.237)
        m.rear_vent(-.113,.366,.212,.231,.117)
    if m.lod == 0:
        m.switchback(.210,.242,.375,.543)
        m.rect('banner',-.297,.487,-.153,.031,.147)
        for i in range(3):
            m.block('timber',(-.320+i*.067,.624,.227),(.025,.020,.235))


def solar_loft(m):
    # Folded photovoltaic shell covers a two-storey glazed end, with a sheltered porch.
    section=[(-.353,.301),(-.035,.845),(.351,.287)]
    m.course('fieldstone',0,.032,.783,.716,0,.063)
    m.envelope(section,-.265,.336,.061,'ivory','metal','glass')
    roof=[(-.416,.237),(-.036,.891),(.417,.237)]
    m.folded_roof(roof,-.339,.381,.051,'glass','ivory')
    m.entry(-.012,-.268,.156,.063,.274,lintel='metal')
    for s in (-1,1):
        m.glazing(s*.233,.202,-.267,.125,.139,2)
    m.side_glass(.353,.056,.169,.373,.124,3)
    if m.lod < 2:
        m.block('ivory',(0,.374,-.272),(.573,.026,.026))
        m.rect('metal',-.029,.547,-.267,.018,.317)
        m.herbs(.217,.100,-.271,.174)
        m.rear_vent(-.01,.338,.452,.230,.190,3)
    if m.lod == 0:
        for z in (-.164,.030,.224):
            for a,b in zip(roof,roof[1:]):
                # Narrow silicon panel seams ride flush with the light-catching panels.
                x,y=a;u,v=b
                m.face('metal',[(x,y+.002,z-.004),(x,y+.002,z+.004),
                                (u,v+.002,z+.004),(u,v+.002,z-.004)])
        m.block('banner',(-.204,.302,-.277),(.081,.027,.021))


def skydeck(m):
    # An upper sunroom wraps the side in a six-facet glass bow.
    m.course('fieldstone',-.002,.008,.799,.722,0,.060)
    m.course('wall',-.010,.008,.744,.675,.055,.372,.054)
    m.block('ivory',(-.010,.362,.008),(.784,.065,.704))
    m.entry(-.196,-.332,.146,.059,.249,lintel='metal')
    m.glazing(.117,.236,-.332,.299,.145,3)
    # The stern of the upper room is square to receive a narrow solar canopy.
    hull=[(-.295,-.167),(.162,-.167),(.293,-.112),(.342,.025),
          (.270,.169),(.140,.229),(-.295,.229)]
    for y,top,scale,mat in [(.390,.426,1,'metal'),(.425,.611,.95,'glass'),
                             (.606,.649,1.04,'ivory')]:
        lo=[(x*scale,y,z*scale) for x,z in hull]
        hi=[(x,top,z) for x,_,z in lo]
        for i in range(len(hull)):
            j=(i+1)%len(hull)
            m.face(mat,(lo[j],lo[i],hi[i],hi[j]))
            if mat=='glass' and (m.lod==0 or (m.lod==1 and i in (1,3,5))):
                m.block('ivory',(lo[i][0],(y+top)/2,lo[i][2]),(.016,top-y+.002,.017))
        m.face(mat,hi[::-1])
    if m.lod < 2:
        m.herbs(.105,.129,-.333,.215)
        m.side_glass(.364,.019,.198,.261,.135)
        m.rear_vent(-.123,.348,.241,.211,.113)
        m.folded_roof([(-.238,.672),(.076,.718)],-.136,.207,.017,'glass','metal')
        for x,h in ((-.217,.020),(.051,.059)):
            m.block('metal',(x,.648+h/2,.039),(.019,h,.272))
    if m.lod == 0:
        m.drum('metal',-.209,.651,.160,.024,.228,6,.010)
        m.drum('ascension',-.209,.876,.160,.018,.018,6,.007)
        m.rect('banner',-.294,.226,-.333,.045,.116)


def pearl_pavilion(m):
    # A high paired shell, its gold folds meeting in a quiet central valley.
    top=[(-.416,.314),(-.203,.728),(-.011,.522),(.161,.788),(.420,.340)]
    base=[(-.373,.357),(-.203,.686),(-.011,.481),(.161,.747),(.378,.371)]
    m.course('ivory',0,.021,.796,.707,0,.083)
    m.envelope(base,-.280,.319,.073,'ivory','gold','plaster')
    m.folded_roof(top,-.344,.382,.048,'ivory','gold')
    m.arched(-.021,.080,-.283,.156,.279,'door','gold')
    m.block('ivory',(-.02,.043,-.335),(.248,.086,.134),taper=.07)
    for x,y in ((-.214,.337),(.166,.365)):
        m.arched(x,.192,-.283,.124,y-.046,'glass','gold')
    if m.lod < 2:
        m.herbs(.169,.155,-.287,.169)
        m.side_glass(.380,.055,.216,.302,.126)
        m.rear_vent(-.121,.322,.323,.221,.149)
        for x,y in ((-.203,.728),(.161,.788)):
            m.beam('ascension',(x,y+.003,-.339),(x,y+.003,.378),.017)
    if m.lod == 0:
        m.rect('banner',-.314,.264,-.284,.037,.117)
        for x,y in ((-.203,.592),(.162,.646)):
            m.arched(x,y-.053,-.282,.054,.085,'ascension','ivory')
        m.jar(-.310,-.376,0,.061,.143)


def halo_house(m):
    m.course('ivory',.002,.018,.801,.697,0,.064,.090)
    m.course('plaster',.004,.018,.743,.645,.060,.343,.082)
    m.course('gold',.003,.018,.785,.678,.333,.361,.089)
    m.course('ivory',.002,.018,.806,.700,.354,.413,.096,.017)
    x,z=-.090,.046
    n=(14,10,5)[m.lod]
    m.drum('glass',x,.406,z,.202,.185,n,.183)
    m.drum('gold',x,.583,z,.205,.037,n)
    m.dome((x,.612,z),.212,m.color('ivory'),rings=(3,2,1)[m.lod],segments=n,
           squash=.92,material='ivory')
    # Three gold brackets seat the halo on the sunroom's drum.
    for i in range(3):
        angle=2*math.pi*i/3+.31
        cx,cz=math.cos(angle),math.sin(angle)
        m.beam('gold',(x+.177*cx,.494,z+.177*cz),(x+.269*cx,.661,z+.269*cz),
               .039 if m.lod<2 else .046)
    m.halo(x,.674,z,.292,.241,.029,(16,12,6)[m.lod])
    m.arched(.123,.063,-.309,.150,.251,'door','gold')
    m.block('ivory',(.123,.031,-.354),(.241,.062,.131),taper=.065)
    m.glazing(-.155,.215,-.307,.194,.116,3,'gold')
    if m.lod < 2:
        m.side_glass(.377,.037,.203,.238,.12)
        m.herbs(-.155,.118,-.310,.205)
        m.rear_vent(.101,.343,.215,.181,.104)
    if m.lod == 0:
        m.rect('banner',.256,.211,-.309,.042,.104)
        m.drum('gold',x,.794,z,.029,.047,8,.019)
        m.drum('ascension',x,.839,z,.019,.031,8,0)


BUILDERS={('industrial',0):weaver,('industrial',1):gate_cottage,
          ('modern',0):butterfly,('modern',1):deck_house,
          ('information',0):solar_loft,('information',1):skydeck,
          ('ascension',0):pearl_pavilion,('ascension',1):halo_house}


def build(model,lod):
    m=CityHouse(model,lod)
    BUILDERS[model.ages[0],model.variant](m)
    return m


if __name__=='__main__':
    for model in package_models('human_houseslate'):
        high,mid,low=[build(model,lod) for lod in range(3)]
        print(model.id,publish(high,model.metadata(),model.package,lods=(mid,low),
                               preview_size=240)['lod_triangles'])
