"""Sun-calendar council houses and the small architectural vocabulary of the Saurian delta."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import MeshBuilder, publish
from forge.atlas import palette
from generators.buildings import saurian_delta_works as works
from PIL import Image, ImageDraw

PACKAGE = "saurian_towncenters"
ERAS = ("ancient", "classical", "industrial", "future")


class Delta(MeshBuilder):
    """Coordinates for polygonal masonry, reed roofs, ornaments and working machinery."""

    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.colors = palette(spec.ages[0])[0]
        self.era = ERAS.index(spec.band)
        self.foot = "timber" if self.era == 0 else "fieldstone"
        self.skin = ("thatch", "roof", "metal", "glass")[self.era]

    def color(self, role):
        return self.colors.get(role, self.colors.get("saurian/" + role))

    def face(self, role, vertices):
        self.poly(vertices, self.color(role), material=role)

    def block(self, role, x, y, z, w, h, d, taper=0):
        self.box((x, y+h/2, z), (w,h,d), self.color(role), taper=taper, material=role)

    def slab(self, role, x, y, z, w, h, d, top=1, cut=.12, underside=False):
        # Clipped corners give a readable highlight at both ends of a long wall.
        a,b = w/2,d/2
        c = min(w,d)*cut
        outline = [(-a+c,-b),(a-c,-b),(a,-b+c),(a,b-c),(a-c,b),(-a+c,b),(-a,b-c),(-a,-b+c)]
        if self.lod == 2 or not cut:
            outline = [(-a,-b),(a,-b),(a,b),(-a,b)]
        bot=[(x+i,y,z+j) for i,j in outline]
        lid=[(x+i*top,y+h,z+j*top) for i,j in outline]
        for i in range(len(bot)):
            j=(i+1)%len(bot)
            self.face(role,(bot[j],bot[i],lid[i],lid[j]))
        self.face(role,lid[::-1])
        if underside:
            self.face(role,bot)

    def drum(self, role, x, y, z, r, h, top=None, n=None, bottom=False):
        self.cylinder((x,y,z),r,h,self.color(role),segments=n or (12,8,6)[self.lod],
                      top_radius=top,cap_bottom=bottom,material=role)

    def mound(self, role, x, y, z, radius, height, n=None):
        self.dome((x,y,z),radius,self.color(role),rings=(3,2,1)[self.lod],
                  segments=n or (12,8,6)[self.lod],squash=height/radius,material=role)

    def rod(self, role, a, b, r, n=None):
        dx,dy,dz=(q-p for p,q in zip(a,b))
        length=math.dist(a,b)
        with self.transform(translate=a,rotate_x=math.degrees(math.acos(max(-1,min(1,dy/length)))),
                            rotate_y=math.degrees(math.atan2(dx,dz))):
            self.drum(role,0,0,0,r,length,n=n or (7,5,4)[self.lod],bottom=True)

    def disc(self, x, y, z, r, role="gold", boss=True):
        with self.transform(translate=(x,y,z),rotate_x=-90):
            self.drum(role,0,0,0,r,r*.26,n=(16,10,7)[self.lod],bottom=True)
            if boss and self.lod < 2:
                self.drum("turquoise",0,r*.258,0,r*.50,r*.08,n=(10,7)[self.lod])
                if self.lod == 0:
                    self.drum("gold",0,r*.335,0,r*.20,r*.08,n=8)

    def ring(self, role, x, y, z, r, inner, depth, n=None):
        n=n or (20,12,8)[self.lod]
        def pt(i,rad,offset):
            angle=2*math.pi*i/n
            return (x+rad*math.cos(angle),y+rad*math.sin(angle),z+offset)
        for i in range(n):
            j=(i+1)%n
            a,b,c,d = pt(i,r,0),pt(j,r,0),pt(j,inner,0),pt(i,inner,0)
            e,f,g,h = pt(i,r,depth),pt(j,r,depth),pt(j,inner,depth),pt(i,inner,depth)
            for points in ((d,c,b,a),(e,f,g,h),(a,b,f,e),(c,d,h,g)):
                self.face(role,points)

    def entry(self, x, y, z, w, h):
        # A pentagonal recessed-looking gate and a separate hood casting a deep brow.
        self.face("door",[(x+w/2,y,z-.009),(x-w/2,y,z-.009),(x-w/2,y+h*.79,z-.009),
                          (x,y+h,z-.009),(x+w/2,y+h*.79,z-.009)])
        if self.lod < 2:
            for side in (-1,1):
                self.block("trim",x+side*w*.55,y,z-.013,w*.12,h*.81,.048)
            self.block("accent",x,y+h*.82,z-.018,w*1.32,h*.13,.069)
        if self.lod==0:
            self.disc(x,y+h*.54,z-.012,w*.105,boss=False)

    def stairs(self, x, y, zfront, w, run, rise):
        count=(7,4,2)[self.lod]
        for i in range(count):
            size=run/count
            self.block("wall",x,y,zfront+(i+.5)*size,w,rise*(i+1)/count,size)

    def jar(self, x, y, z, r, role="hide"):
        self.drum(role,x,y,z,r*.62,r*1.16,r)
        self.drum(role,x,y+r*1.16,z,r,r*.37,r*.64)
        if self.lod==0:
            self.drum("turquoise",x,y+r*1.52,z,r*.69,r*.16)
            self.drum("door",x,y+r*1.681,z,r*.47,r*.022)

    def blade(self, role, a, b, width):
        # Folded lance-like reed or carved frond, with closed back and a raised midrib.
        x,y,z=a
        u,v,w=b
        dx,dz=u-x,w-z
        radius=max(.001,math.hypot(dx,dz))
        tx,tz=-dz/radius*width,dx/radius*width
        mid=((x+u)/2,(y+v)/2,(z+w)/2)
        p=(mid[0]+tx,mid[1],mid[2]+tz)
        q=(mid[0]-tx,mid[1],mid[2]-tz)
        ridge=(mid[0],mid[1]+width*.76,mid[2])
        lower=(mid[0],mid[1]-width*.37,mid[2])
        for i,j in ((a,p),(p,b),(b,q),(q,a)):
            self.face(role,(i,j,ridge))
            self.face(role,(j,i,lower))

    def rooflet(self, x, y, z, w, d, h, role):
        self.slab(role,x,y,z,w,h,d,.35,cut=.105,underside=True)
        self.slab("trim",x,y+h-.001,z,w*.40,h*.11,d*.40,.89,cut=.10)

    def house(self, x, y, z, w, d, h, role=None):
        # Low clerestory under a weighty reed/masonry rain skirt.
        role=role or self.skin
        pl=h*.17
        self.slab(self.foot,x,y,z,w*1.08,pl,d*1.08,.96)
        self.slab("wall",x,y+pl,z,w*.91,h*.53,d*.91,cut=.096)
        self.entry(x,y+pl,z-d*.455,w*.30,h*.45)
        if self.lod < 2:
            for side in (-1,1):
                self.block("turquoise",x+side*w*.314,y+h*.40,z-d*.456-.008,
                           w*.137,h*.122,.022)
        self.rooflet(x,y+h*.70,z,w,d,h*(.42,.51,.28,.32)[self.era],role)
        if self.lod==0:
            # A tide-mark tile on the visible flank and a pendant seal under the cornice.
            self.block("accent",x+w*.454,y+h*.39,z,.020,h*.12,d*.49)
            self.disc(x,y+h*.65,z-d*.459-.015,h*.063,boss=False)


def crown(m, age, y):
    """An open reed cap, sun gate, dome, busbar or supported halo crowns each era."""
    if age in (0,1,2,4):
        mat=("thatch","hide","roof","roof","slate")[age]
        m.rooflet(0,y,.16,1.05 if age!=4 else .90,.80,(.44,.32,.35,.61,.55)[age],mat)
        peak=y+(.44,.32,.35,.61,.55)[age]*1.1
        if age==2:
            for s in (-1,1):
                m.rod("trim",(s*.41,y+.01,.13),(s*.25,peak+.12,.13),.050)
        m.block("trim",0,peak-.025,.15,.087,.21,.094)
        m.disc(0,peak+.18,.149,.151 if age!=1 else .20)
    elif age in (3,5):
        m.slab("ivory" if age==3 else "wall",0,y,.16,1.00,.085,.80,.94)
        m.drum("turquoise",0,y+.08,.16,.355,.072)
        if age==3:
            m.mound("roof_tile",0,y+.15,.16,.374,.28)
            top=y+.43
        else:
            m.mound("roof_tile",0,y+.15,.16,.374,.34)
            # An octagonal roof lantern sits in the cut-off crown of the copper dome.
            m.drum("ivory",0,y+.415,.16,.178,.060,n=8)
            m.drum("turquoise",0,y+.475,.16,.140,.177,n=8)
            m.drum("gold",0,y+.652,.16,.177,.138,.043,n=8)
            top=y+.790
        m.drum("gold",0,top-.028,.16,.060,.13,.035)
        m.disc(0,top+.15,.14,.089)
    elif age==6:
        m.rooflet(0,y,.16,1.04,.81,.20,"metal")
        for s in (-1,1):
            m.drum("brick",s*.333,y+.09,.30,.092,.40+.10*s,.070)
            m.drum("metal",s*.333,y+.482+.10*s,.30,.088,.053)
        m.block("metal",0,y+.17,.10,.12,.24,.11)
        m.disc(0,y+.38,.08,.17)
    elif age in (7,8):
        h=.22 if age==7 else .42
        m.slab("glass",0,y,.16,.89,h,.66,.75)
        m.slab("ivory",0,y+h-.003,.16,.78,.065,.58,.90,underside=True)
        for s in (-1,1):
            m.rod("metal",(s*.25,y+h+.04,.16),(s*.15,y+h+.22,.14),.033)
        m.disc(0,y+h+.28,.14,.185)
        if age==8:
            m.rod("metal",(.32,y+.27,.27),(.32,y+.84,.27),.020)
    else:
        for s in (-1,1):
            m.slab("ivory",s*.259,y,.16,.24,.24,.34,.65)
            m.rod("gold",(s*.25,y+.19,.15),(s*.204,y+.34,.14),.048)
        m.ring("gold",0,y+.56,.115,.338,.245,.075)
        if m.lod<2:
            m.rod("ascension",(0,y+.25,.149),(0,y+.55,.149),.018)
            m.disc(0,y+.55,.142,.105,"ascension",False)


def renaissance_council(m):
    # The loggia opens along the palace front. A separate tide clock and
    # open bell chamber govern council meetings and the river landings.
    works.court(m,.18)
    works.shell(m,.205,.265,1.11,.84,.68,material="plaster")
    m.slab("trim",.205,.76,.24,1.21,.09,.95,underside=True)
    m.slab("wall",.205,.85,.29,.95,.27,.70)
    m.hip_roof((.205,1.12,.29),1.20,.87,.35,m.color("roof_tile"),
               overhang=.034,material="roof_tile")
    works.shutters(m,.205,.904,-.071,.86,.137,3)
    for x in (-.23,.19,.61):
        m.drum("ivory",x,.08,-.40,.065,.515,.051)
        if m.lod<2:
            m.drum("turquoise",x,.518,-.40,.063,.073)
    m.slab("wall",.19,.595,-.30,1.06,.086,.40,.95,underside=True)
    m.slab("roof_tile",.19,.681,-.30,1.14,.15,.44,.65,underside=True)
    m.slab("fieldstone",-.59,.08,.08,.48,.29,.53,.86)
    m.slab("plaster",-.59,.37,.08,.40,.81,.45,.95)
    m.slab("trim",-.59,1.18,.08,.47,.077,.51,underside=True)
    for x in (-.735,-.445):
        for z in (-.078,.238):
            m.block("ivory",x,1.257,z,.07,.28,.071)
    m.rooflet(-.59,1.537,.08,.49,.54,.31,"roof_tile")
    m.drum("gold",-.59,1.37,.08,.119,.14,.069)
    m.drum("gold",-.59,1.51,.08,.031,.03)
    works.cartouche(m,-.59,.966,-.139,.106)
    if m.lod==0:
        for x in (-.17,.57):
            m.jar(x,.08,-.57,.070,"turquoise")


def build(spec,lod):
    m=Delta(spec,lod)
    age=AGES.index(spec.ages[0])
    if age==5:
        renaissance_council(m)
        return m
    base=("timber","wall","fieldstone","plaster","fieldstone","wall","brick","plaster","ivory","ivory")[age]
    pillar=("timber","wall","trim","ivory","trim","plaster","metal","metal","metal","gold")[age]
    # Arrival by axial causeway; columns enclose a shaded civic balcony.
    course=(.16,.17,.18,.17,.18,.17,.17,.16,.15,.16)[age]
    extent=(1.62,1.79,1.80,1.86,1.69,1.85,1.82,1.83,1.80,1.90)[age]
    if age==0:
        for x in (-.65,.65):
            for z in (-.41,.58):
                m.drum("timber",x,0,z,.095,.20,n=(9,6,4)[lod])
        m.slab("timber",0,.17,.09,extent,.082,1.38,underside=True)
        y=.252
    else:
        m.slab(base,0,0,.07,extent,course,1.59,.93)
        m.slab("turquoise" if age>=7 else "trim",0,course,.07,extent*.944,.053,1.51,.99)
        y=course+.053
    m.slab(base,0,y,.12,extent*.82,.255,1.22,.87)
    m.slab("accent" if age in (1,3,9) else "wall",0,y+.252,.12,extent*.733,.070,1.09,.99,underside=True)
    y+=.322
    m.stairs(0,0,-.97,.42,.55045,y)
    m.slab("glass" if age in (7,8) else "wall",0,y,.23,.79,.335,.62,cut=.095)
    m.entry(0,y,-.08,.26,.286)
    for s in (-1,1):
        m.drum(pillar,s*.433,y,-.166,.062,.337,.050,n=(9,6,4)[lod])
        if lod<2:
            m.drum("turquoise",s*.433,y+.24,-.166,.063,.055)
            if lod==0:
                m.drum("accent",s*.433,y+.313,-.166,.079,.027)
    m.slab(pillar,0,y+.333,.14,1.06,.069,.83,.98,underside=True)
    crown(m,age,y+.402)
    if lod<2:
        # The calendar stones use three star notches and the gate jars hold offerings.
        for s in (-1,1):
            m.slab(base,s*.54,0,-.64,.24,.261,.263,.76)
            m.disc(s*.54,.170,-.75,.065,"gold",False)
            if lod==0:
                m.jar(s*.54,.261,-.64,.054,"turquoise")
                for j in (-1,0,1):
                    m.block("gold",s*.48+j*.077,y-.16,-.438,.034,.045,.062)
        if age in (1,4,7):
            for s in (-1,1):
                m.slab(base,s*.657,.18,.44,.243,.40,.32,.78)
                m.rooflet(s*.657,.58,.44,.284,.36,.18,"roof")
    return m


def generate(package, builder):
    entries=[]
    for spec in package_models(package):
        levels=[builder(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=224)
        print(spec.id,entry["lod_triangles"],flush=True)
        entries.append(entry)
    preview_sheets(entries)


def preview_sheets(entries):
    # Pair every original with a 64px copy so a critic can inspect the full set in pages.
    tiles=[(e,age,ROOT/path) for e in entries for age,path in e["age_previews"].items()]
    out=tiles[0][2].parent/"sheets"
    out.mkdir(exist_ok=True)
    for start in range(0,len(tiles),9):
        page=tiles[start:start+9]
        sheet=Image.new("RGB",(3*294, math.ceil(len(page)/3)*249),(239,237,229))
        pen=ImageDraw.Draw(sheet)
        for i,(entry,age,path) in enumerate(page):
            x=(i%3)*294
            y=(i//3)*249
            with Image.open(path) as preview:
                sheet.paste(preview,(x,y))
                sheet.paste(preview.resize((64,64),Image.Resampling.LANCZOS),(x+225,y+78))
            pen.text((x+5,y+226),entry["building_type"]+"   "+age,fill=(44,40,32))
        sheet.save(out/f"review-{start//9+1:02}.png")


if __name__=="__main__":
    generate(PACKAGE,build)
