"""Human courtyard, gable and loggia houses, from Classical through Renaissance."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House


class MasonryHouse(House):
    def roof(self, w, d, eaves, rise, mat, end_mat, **options):
        super().roof(w,d,eaves,rise,mat,end_mat,**options)
        # The return below the overhang closes porch and balcony ceilings.
        a,b = w/2-.009,d/2-(.009 if options.get('hip') else .021)
        y = eaves-.026
        self.face('timber',[(-a,y,-b),(a,y,-b),(a,y,b),(-a,y,b)])
        if options.get('hip'):
            return
        # Sloping soffit boards bridge the full bargeboard thickness to the
        # recessed gable at either end. Both rakes meet along the ridge.
        profile = [(1,0),(.54,.412),(0,1)] if self.lod<2 else [(1,0),(0,1)]
        for end in (-1,1):
            outer,inner=end*d/2,end*b
            for side in (-1,1):
                for (u,v),(nu,nv) in zip(profile,profile[1:]):
                    x,x2=side*w*u/2,side*w*nu/2
                    h,h2=y+rise*v,y+rise*nv
                    board=[(x,h,outer),(x,h,inner),(x2,h2,inner),(x2,h2,outer)]
                    self.face('timber',board if side*end<0 else board[::-1])

    def rect(self, mat, x, y, z, w, h):
        self.face(mat, [(x+w/2,y-h/2,z),(x-w/2,y-h/2,z),
                        (x-w/2,y+h/2,z),(x+w/2,y+h/2,z)])

    def drum(self, mat, x, y, z, r, h, n, end=None):
        self.cylinder((x,y,z),r,h,self.color(mat),segments=n,top_radius=end,material=mat)

    def column(self, x, z, base, top, r):
        n = (8,6,4)[self.lod]
        if self.lod == 2:
            self.drum('ivory',x,base,z,r,top-base,n)
            return
        self.block('plaster',(x,base+.012,z),(r*2.6,.024,r*2.6))
        self.drum('ivory',x,base+.023,z,r,top-base-.051,n,r*.81)
        self.block('ivory',(x,top-.015,z),(r*2.56,.031,r*2.56))
        if self.lod == 0:
            self.drum('fieldstone',x,base+.019,z,r*1.13,.020,n,r)
            self.drum('wall',x,top-.050,z,r*.82,.023,n,r*1.22)

    def herbs(self,x,y,front,w):
        self.block('timber',(x,y,front-.033),(w,.047,.072),taper=.055)
        if self.lod:
            return
        for i in range(3):
            cx,cz=x+(i-1)*w*.29,front-.042
            crown=y+.057+.008*(i%2)
            # The broad leaf bases enter the soil. Opposing tips cup the flower head.
            self.face('leaf',[(cx+.028,crown+.006,cz),(cx-.004,y+.018,cz-.008),
                              (cx-.012,crown+.008,cz+.004)])
            self.face('leaf',[(cx+.010,crown+.011,cz-.007),(cx+.002,y+.017,cz+.009),
                              (cx-.026,crown+.012,cz-.004)])
            mat='ivory' if i==1 else 'flower'
            # Three solid sides remain visible as the camera crosses the leaf planes.
            self.cylinder((cx,y+.016,cz),.010,crown-y-.010,self.color('leaf'),
                          segments=3,top_radius=.009,cap_top=False,material='leaf')
            self.drum(mat,cx,crown+.002,cz,.012,.013,4,.019)

    def arched(self, x, base, z, w, h, mat, surround='ivory', reveal=0):
        # Extrados and opening are concentric, each with a straight spring line.
        for radius, foot, plane, material in [(w*.59,base-.008,z,surround),
                                                (w*.48,base,z-.004,mat)]:
            spring = base+h-w*.5
            pts = [(x+radius,foot,plane),(x-radius,foot,plane)]
            for i in range((n := (10,6,4)[self.lod])+1):
                a = math.pi * (1-i/n)
                pts.append((x+radius*math.cos(a),spring+radius*math.sin(a),plane))
            self.face(material, pts)
            if reveal and material==surround:
                for i,a in enumerate(pts):
                    b=pts[(i+1)%len(pts)]
                    self.face(surround,[b,a,(a[0],a[1],z+reveal),(b[0],b[1],z+reveal)])
        if self.lod == 0 and mat != 'door':
            self.rect('trim',x,base+h*.41,z-.008,.012,h*.82)
            self.rect('trim',x,base+h-w*.52,z-.009,w*.94,.012)

    def side_light(self, side, z, y, w, h, shutters=True):
        with self.transform(translate=(side,0,z),rotate_y=-90):
            self.light(0,y,0,w,h,shutters)

    def dormer(self, cx, z, base, width, rise):
        with self.transform(translate=(cx,0,z)):
            self.block('plaster',(0,base+.021,0),(width,.096,.166))
            self.roof(width+.053,.200,base+.071,rise,'slate','plaster')
            self.rect('door',0,base+.055,-.086,width*.61,.067)
            if self.lod < 2:
                self.rect('emissive',0,base+.055,-.091,width*.39,.052)


def atrium(m):
    # Courtyard is a genuine cut-through. Its roof planes drain to an open cistern.
    m.course('fieldstone',0,.015,.814,.788,0,.065)
    outer = [(-.375,-.342),(.375,-.342),(.375,.372),(-.375,.372)]
    roof = [(-.422,-.392),(.422,-.392),(.422,.416),(-.422,.416)]
    well = [(-.106,-.107),(.138,-.107),(.138,.131),(-.106,.131)]
    def ring(points, y):
        return [(x,y,z) for x,z in points]
    cloister = [(x*1.90,.536,z*1.83) for x,z in well]
    wall_base, eaves = ring(outer,.059),ring(outer,.414)
    rim, inner, floor = ring(roof,.433),ring(well,.458),ring(well,.071)
    fascia = ring(roof,.405)
    for i in range(4):
        j=(i+1)%4
        m.face('plaster',(wall_base[j],wall_base[i],eaves[i],eaves[j]))
        m.face('ivory',(fascia[j],fascia[i],rim[i],rim[j]))
        m.face('roof_tile',(rim[j],rim[i],cloister[i],cloister[j]))
        m.face('roof_tile',(cloister[j],cloister[i],inner[i],inner[j]))
        m.face('wall',(inner[j],inner[i],floor[i],floor[j]))
        m.face('ivory',(fascia[i],fascia[j],eaves[j],eaves[i]))
    m.face('water',floor[::-1])
    m.entry(-.060,-.343,.15,.063,.25,lintel='ivory')
    m.light(.204,.240,-.344,.087,.109,False)
    if m.lod < 2:
        m.side_light(.376,.035,.240,.134,.112)
        m.block('ivory',(-.060,.318,-.379),(.264,.040,.097))
        for x in (-.169,.049):
            m.column(x,-.399,.060,.310,.023)
        m.block('fieldstone',(-.06,.036,-.396),(.288,.072,.155))
        m.rear_vent(.091,.374,.263,.165,.100)
    if m.lod == 0:
        m.jar(-.293,-.383,0,.058,.126)
        m.herbs(.209,.154,-.345,.153)
        for x in (-.285,.277):
            m.rect('banner',x,.374,-.345,.091,.030)


def portico(m):
    # A deep shadowed porch beneath the continuous gable shelters the family doorway.
    m.course('fieldstone',0,.008,.770,.812,0,.071)
    m.course('wall',0,.097,.690,.565,.064,.404)
    m.roof(.827,.846,.431,.290,'roof_tile','ivory',ridge_mat='roof_tile')
    # A continuous marble architrave carries the projecting pediment.
    m.block('ivory',(0,.406,-.340),(.761,.067,.159))
    for x in (-.303,.303):
        m.column(x,-.332,.067,.379,.041)
    m.entry(-.051,-.186,.173,.072,.260,lintel='ivory')
    m.side_light(.347,.105,.236,.130,.133)
    if m.lod < 2:
        m.rect('banner',0,.411,-.421,.535,.019)
        m.rear_vent(.08,.382,.248,.169,.121)
        m.arched(0,.464,-.405,.097,.110,'banner','roof_tile')
        m.herbs(.239,.128,-.187,.136)
    if m.lod == 0:
        m.jar(.348,-.226,.067,.052,.124)
        for x in (-.275,.261):
            m.rect('ivory',x,.481,-.405,.079,.022)
        m.block('fieldstone',(-.032,.024,-.431),(.321,.048,.116),taper=.07)


def jettied(m):
    m.course('fieldstone',0,.025,.642,.652,0,.312)
    m.block('timber',(0,.308,.025),(.719,.057,.713))
    m.block('plaster',(0,.447,.025),(.701,.241,.695))
    with m.transform(translate=(0,0,.025)):
        m.roof(.830,.816,.571,.386,'slate','plaster',ridge_mat='trim')
    m.entry(-.111,-.302,.157,.055,.238)
    m.light(.109,.445,-.325,.136,.129)
    m.side_light(.352,.042,.447,.116,.120,False)
    m.flue(-.197,.228,.487,.917,.101)
    if m.lod < 2:
        m.block('timber',(0,.561,-.350),(.697,.033,.068))
        for x in (-.317,-.106,.316):
            m.block('timber',(x,.447,-.329),(.033,.207,.035))
        m.bent_rail('timber',[(-.325,.594),(0,.889),(.325,.594)],-.362,.036)
        m.block('timber',(0,.723,-.360),(.033,.300,.029))
        m.herbs(.109,.354,-.329,.189)
        m.rear_vent(.034,.375,.448,.173,.124)
    if m.lod == 0:
        m.beam('timber',(-.293,.354,-.344),(-.133,.538,-.344),.035)
        m.beam('timber',(.180,.574,-.362),(.009,.762,-.362),.031)
        for x in (-.270,-.07,.139,.275):
            m.beam('timber',(x,.208,-.297),(x,.304,-.364),.035)
        m.rect('banner',-.215,.449,-.326,.075,.085)


def longhall(m):
    m.course('fieldstone',-.018,.067,.771,.622,0,.081)
    m.course('plaster',-.018,.067,.734,.579,.075,.430)
    with m.transform(translate=(-.018,0,.067),rotate_y=90):
        m.roof(.709,.850,.427,.364,'roof','plaster',hip=.122,ridge_mat='timber')
    m.dormer(-.149,-.124,.511,.176,.122)
    m.entry(-.160,-.223,.156,.078,.272)
    m.light(.131,.276,-.225,.116,.113)
    m.flue(.214,.182,.367,.834,.123)
    m.side_light(.350,.032,.262,.129,.110,False)
    if m.lod < 2:
        for x in (-.349,-.029,.323):
            m.block('timber',(x,.251,-.227),(.038,.338,.042))
        m.block('timber',(-.02,.403,-.226),(.72,.042,.052))
        m.herbs(.127,.190,-.224,.181)
        m.rear_vent(.138,.358,.263,.148,.114)
    if m.lod == 0:
        m.beam('timber',(.011,.120,-.236),(.077,.388,-.236),.028)
        m.beam('timber',(.279,.127,-.236),(.207,.387,-.236),.028)
        m.block('timber',(-.31,.333,-.294),(.032,.030,.116))
        m.block('metal',(-.31,.294,-.337),(.017,.061,.017))
        m.drum('emissive',-.31,.209,-.337,.034,.069,6,.042)
        m.drum('metal',-.31,.274,-.337,.047,.030,6,.015)


def studiolo(m):
    # A round reading room grows out of the gabled plaster house.
    cx,cz=.189,-.137
    m.course('fieldstone',-.084,.060,.599,.628,0,.058)
    m.course('plaster',-.084,.060,.560,.592,.049,.454)
    with m.transform(translate=(-.084,0,.060)):
        m.roof(.656,.707,.451,.251,'roof_tile','wall',hip=.092,ridge_mat='roof_tile')
    n = (12,8,6)[m.lod]
    m.drum('fieldstone',cx,0,cz,.205,.070,n)
    m.drum('wall',cx,.065,cz,.192,.396,n)
    m.drum('ivory',cx,.453,cz,.207,.049,n)
    m.dome((cx,.499,cz),.215,m.color('roof_tile'),rings=(4,3,2)[m.lod],segments=n,
           squash=1.15,material='roof_tile')
    m.drum('ivory',cx,.731,cz,.042,.066,n)
    m.drum('gold',cx,.794,cz,.055,.064,n,0)
    m.entry(-.188,-.237,.144,.057,.269,lintel='ivory')
    m.arched(cx,.169,-.332,.094,.161,'glass',reveal=.056)
    if m.lod < 2:
        with m.transform(translate=(cx,0,cz),rotate_y=-90):
            m.arched(0,.165,-.195,.098,.164,'glass',reveal=.042)
        m.rear_vent(-.166,.360,.289,.137,.109)
        m.block('ivory',(-.187,.349,-.253),(.262,.034,.072))
        m.block('ivory',(cx,.140,-.311),(.116,.030,.061))
        m.herbs(cx,.140,-.329,.145)
    if m.lod == 0:
        m.jar(-.327,-.294,0,.063,.153)
        m.arched(-.058,.228,-.239,.055,.098,'banner')


def belvedere(m):
    m.course('fieldstone',0,.025,.810,.695,0,.062)
    m.course('wall',0,.025,.754,.650,.055,.402)
    with m.transform(translate=(0,0,.025),rotate_y=90):
        m.roof(.744,.858,.407,.222,'roof_tile','plaster',hip=.181,ridge_mat='roof_tile')
    x,z = -.160,.080
    m.block('plaster',(x,.557,z),(.285,.287,.274))
    m.block('ivory',(x,.699,z),(.322,.039,.311))
    # An open belvedere with four piers, a tiled hip and a gold ridge knob.
    for dx in (-.116,.116):
        for dz in (-.107,.107):
            if m.lod:
                m.block('ivory',(x+dx,.786,z+dz),(.037,.147,.038))
            else:
                m.column(x+dx,z+dz,.716,.858,.023)
    m.block('plaster',(x,.865,z),(.305,.032,.287))
    with m.transform(translate=(x,0,z)):
        m.roof(.386,.364,.890,.160,'roof_tile','plaster',hip=.131,ridge_mat='gold')
    m.arched(-.165,.060,-.303,.158,.280,'door')
    m.block('fieldstone',(-.165,.031,-.345),(.245,.062,.126),taper=.04)
    m.arched(.138,.165,-.304,.122,.162,'glass')
    if m.lod < 2:
        m.arched(.278,.170,-.304,.071,.150,'glass')
        m.herbs(.155,.132,-.303,.193)
        m.side_light(.379,.036,.247,.129,.137)
        m.rear_vent(-.182,.352,.254,.174,.107)
    if m.lod == 0:
        m.rect('banner',x,.623,z-.140,.088,.101)
        m.block('ivory',(-.165,.359,-.322),(.267,.037,.067))


BUILDERS = {('classical',0):atrium, ('classical',1):portico,
            ('medieval',0):jettied, ('medieval',1):longhall,
            ('renaissance',0):studiolo, ('renaissance',1):belvedere}


def build(model, lod):
    m = MasonryHouse(model,lod)
    BUILDERS[model.ages[0],model.variant](m)
    return m


if __name__ == '__main__':
    for model in package_models('human_housesclassical'):
        high, mid, low = [build(model,lod) for lod in range(3)]
        print(model.id,publish(high,model.metadata(),model.package,lods=(mid,low),
                               preview_size=240)['lod_triangles'])
