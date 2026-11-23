"""The dwarven moot hall and its masonry vocabulary. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, package_models
from forge import MeshBuilder, publish, style
from forge.render import contact_sheet


class Stonework(MeshBuilder):
    """All dimensions in two-tile design space, base elevations measured from the floor."""
    def __init__(self, model, lod):
        super().__init__(model.id)
        self.lod = lod
        self.age = model.ages[0]
        self.era = ("ancient", "classical", "industrial", "future").index(model.band)

    def color(self, mat):
        return style.blended("dwarf", self.age, mat) if mat in ("wall", "roof", "accent", "trim", "banner") else (.8, .75, .65)

    def block(self, mat, x, y, z, w, h, d, taper=0):
        self.box((x,y+h/2,z), (w,h,d), self.color(mat), material=mat, taper=taper, bottom=y>0)

    def face(self, mat, points):
        self.poly(points, self.color(mat), material=mat)

    def slab(self, mat, x, y, z, w, h, d, bevel=.16, taper=0):
        """Clipped corners with a continuous, battered stone profile."""
        if self.lod == 2:
            self.block(mat,x,y,z,w,h,d,taper)
            return
        a,b = w/2,d/2
        c = min(w,d)*bevel
        outline = [(-a+c,-b),(a-c,-b),(a,-b+c),(a,b-c),
                   (a-c,b),(-a+c,b),(-a,b-c),(-a,-b+c)]
        lower = [(x+u,y,z+v) for u,v in outline]
        upper = [(x+u*(1-taper),y+h,z+v*(1-taper)) for u,v in outline]
        for i in range(len(lower)):
            j=(i+1)%len(lower)
            self.face(mat,(lower[j],lower[i],upper[i],upper[j]))
        self.face(mat,upper[::-1])
        if y>0:
            self.face(mat,lower)

    def drum(self,mat,x,y,z,r,h,top=None,n=None):
        self.cylinder((x,y,z),r,h,self.color(mat),segments=n or (12,8,5)[self.lod],
                      top_radius=top,material=mat,cap_bottom=y>0)

    def relief(self,mat,outline,z,d,open_edges=()):
        """Extrude a convex, counterclockwise elevation; z is its front face."""
        front=[(x,y,z) for x,y in outline]
        back=[(x,y,z+d) for x,y in outline]
        self.face(mat,front[::-1])
        self.face(mat,back)
        for i in range(len(front)):
            if i in open_edges:
                continue
            j=(i+1)%len(front)
            self.face(mat,(front[i],front[j],back[j],back[i]))

    def beam(self,mat,a,b,width,depth=None):
        dx,dy,dz=(t-s for s,t in zip(a,b))
        length=math.dist(a,b)
        with self.transform(translate=tuple((s+t)/2 for s,t in zip(a,b)),
                            rotate_y=math.degrees(math.atan2(dx,dz)),
                            rotate_x=math.degrees(math.atan2(math.hypot(dx,dz),dy))):
            self.box((0,0,0),(width,length,depth or width),self.color(mat),material=mat,bottom=True)

    def rune(self,x,y,z,size,mat="gold"):
        # Two mitered chisel strokes share an edge at the point of the V.
        for (x0,y0),(x1,y1),join in (((-.51,.23),(0,-.27),1),((0,-.27),(.50,.35),3)):
            self.relief(mat,[(x+x0*size,y+(y0-.093)*size),(x+x1*size,y+(y1-.093)*size),
                             (x+x1*size,y+(y1+.093)*size),(x+x0*size,y+(y0+.093)*size)],
                        z-size*.08,size*.16,open_edges=(join,))

    def entry(self,x,y,z,w,h,frame="trim"):
        # Recessed shoulder-cut door inside a stone portal with a projecting key.
        def shape(width,height,sill=0):
            a=width*.5
            return [(x-a,y+sill),(x+a,y+sill),(x+a,y+height*.73),(x+a*.55,y+height),
                    (x-a*.55,y+height),(x-a,y+height*.73)]
        self.relief(frame,shape(w,h),z-.045,.079)
        self.relief("door",shape(w*.72,h*.81,h*.018),z-.052,.014)
        if self.lod<2:
            self.block("accent",x,y+h*.79,z-.058,w*.22,h*.15,.016)
            if self.lod==0:
                for s in (-1,1):
                    self.block("timber",x+s*w*.184,y+.009,z-.062,w*.18,h*.55,.015)
                    self.block("gold",x+s*w*.096,y+h*.32,z-.072,w*.044,h*.079,.018)

    def lamp(self,x,y,z,size,segments=None):
        self.drum("trim",x,y,z,size*.67,size*.44,top=size*.52,n=segments)
        self.drum("forge",x,y+size*.43,z,size*.49,size*.66,top=size*.39,n=segments)
        self.drum("accent",x,y+size*1.08,z,size*.63,size*.32,top=size*.27,n=segments)

    def vault(self,mat,x,y,z,w,h,d):
        # Five-sided transverse barrel with flat crown and two loading shoulders.
        a=w*.5
        self.relief(mat,[(x-a,y),(x+a,y),(x+a*.93,y+h*.48),
                         (x+a*.54,y+h),(x-a*.54,y+h),(x-a*.93,y+h*.48)],z-d*.5,d)

    def hearth(self,x,y,z,scale,era=None,roof_material=None):
        """Low roofed workroom with full-height portal and exterior bracing."""
        era=self.era if era is None else era
        with self.transform(translate=(x,y,z),scale=scale):
            h=(.47,.54,.52,.55)[era]
            self.slab("fieldstone",0,0,0,1,.094,.83,.13)
            self.slab("wall",0,.088,0,.91,h-.088,.72,.10)
            roof=roof_material or ("hide","roof","metal","glass")[era]
            self.vault(roof,0,h-.015,0,1.06,(.264,.21,.145,.18)[era],.88)
            self.entry(0,.018,-.36,.42,h*.90)
            if self.lod<2:
                for s in (-1,1):
                    self.block("trim",s*.352,.042,-.344,.108,h*.90,.145,taper=.18)
                self.rune(0,h+.063,-.45,.16)
                if era>=2:
                    self.block("glass",.46,h*.49,.022,.018,.108,.39)
            if self.lod==0:
                for t in (-1,1):
                    self.block("accent",0,h+.08,t*.376,.58,.056,.10)
                self.lamp(.31,.075,-.408,.081)
                self.slab("slate",-.22,.015,-.399,.19,.063,.145)

    def wheel(self,x,y,z,r,mat="timber",spokes=6):
        """Open annulus in elevation with solid inner and outer edges."""
        n=(16,12,8)[self.lod]
        d=r*.23
        for i in range(n):
            a,b=(2*math.pi*j/n for j in (i,i+1))
            def pt(t,k):
                return (x+r*k*math.cos(t),y+r*k*math.sin(t))
            self.relief(mat,[pt(a,.77),pt(a,1),pt(b,1),pt(b,.77)],z-d/2,d,open_edges=(0,2))
        for j in range(spokes):
            a=j*2*math.pi/spokes
            dx,dy=r*.84*math.cos(a),r*.84*math.sin(a)
            self.beam("trim",(x+dx*.186,y+dy*.186,z),(x+dx,y+dy,z),r*.12)
        with self.transform(translate=(x,y,z+d*.69),rotate_x=-90):
            self.drum("accent",0,0,0,r*.201,d*1.7)

    def stack(self,x,y,z,r,h,mat):
        self.drum(mat,x,y,z,r,h,top=r*.68)
        self.drum("trim",x,y+h-.013,z,r*.83,.055)
        self.drum("door",x,y+h+.039,z,r*.57,.006)
        if self.lod==0:
            self.drum("accent",x,y+h*.64,z,r*.82,.032)


def publish_package(package,build):
    rows=[]
    for model in package_models(package):
        levels=[build(model,i) for i in range(3)]
        e=publish(levels[0],model.metadata(),package,lods=levels[1:],preview_size=224)
        print(model.id,e["lod_triangles"],flush=True)
        rows.append(e)
    root=Path(__file__).resolve().parents[4]
    for age in AGES:
        members=[(root/e["age_previews"][age],e["building_type"]) for e in rows if age in e["ages"]]
        if members:
            paths,labels=zip(*members)
            contact_sheet(paths,paths[0].parent/("sheet_"+age+".png"),cols=min(5,len(paths)),cell=128,labels=labels)
    rows.sort(key=lambda e:(e["building_type"],AGES.index(e["ages"][0])))
    paths=[root/e["preview"] for e in rows]
    labels=[e["building_type"]+" / "+e["ages"][0] for e in rows]
    contact_sheet(paths,paths[0].parent/"contact.png",cols=min(5,len(paths)),cell=168,labels=labels)


def crown(m,age,base):
    # Every moot keeps its stone foundation; each generation builds a new council chamber above it.
    with m.transform(translate=(0,base,.13)):
        if age==0:
            m.vault("thatch",0,0,0,1.48,.35,1.08)
            for z in (-.46,.46):
                m.vault("timber",0,-.005,z,1.50,.367,.065)
            m.beam("timber",(0,.373,-.555),(0,.373,.555),.083)
        elif age==1:
            m.slab("brick",0,0,.05,1.30,.25,.98,taper=.22)
            m.slab("roof",0,.247,.05,1.09,.12,.80,taper=.12)
            m.drum("accent",0,.362,.05,.25,.14,top=.18)
            for x in (-.117,.117):
                for z in (-.055,.155):
                    m.block("trim",x,.498,z,.064,.157,.066)
            m.drum("accent",0,.650,.05,.263,.122,top=.089)
        elif age==2:
            m.vault("slate",0,0,0,1.29,.275,1.06)
            m.slab("fieldstone",0,.27,0,.67,.22,.85,taper=.16)
            m.slab("roof",0,.485,0,.66,.10,.83)
            if m.lod<2:
                m.rune(0,.412,-.432,.145)
        elif age==3:
            for s in (-1,1):
                for t in (-1,1):
                    m.drum("ivory",s*.42,0,t*.323,.10,.33,.082)
            m.slab("trim",0,.322,0,1.15,.08,.91)
            m.vault("roof_tile",0,.40,0,1.13,.23,.90)
        elif age==4:
            m.slab("wall",0,0,0,1.06,.35,.95)
            m.block("trim",0,.048,-.478,.66,.229,.036)
            for s in (-1,1):
                m.entry(s*.152,.056,-.501,.245,.225,"fieldstone")
            m.block("fieldstone",0,.031,-.491,.70,.048,.112)
            m.vault("slate",0,.342,0,1.19,.28,1.03)
            for x in (-.58,.58):
                m.slab("fieldstone",x,0,.10,.31,.46,.64,taper=.09)
                m.slab("roof",x,.457,.10,.36,.095,.70)
                for t in (-1,1):
                    m.block("fieldstone",x,.549,.10+t*.225,.25,.108,.127)
        elif age==5:
            m.drum("plaster",0,0,0,.52,.22)
            m.dome((0,.219,0),.55,m.color("roof_tile"),rings=(4,3,2)[m.lod],
                   segments=(16,12,8)[m.lod],squash=.74,material="roof_tile")
            m.drum("gold",0,.601,0,.07,.13,top=.035)
        elif age==6:
            m.vault("roof",-.17,0,0,1.04,.32,.99)
            for s in (-1,1):
                m.stack(.47,0,s*.29,.17,.63 if s>0 else .49,"brick")
            m.slab("glass",-.17,.299,0,.45,.15,.77,taper=.14)
        elif age==7:
            m.slab("glass",-.08,0,0,1.13,.30,.86)
            m.slab("plaster",-.08,.292,0,1.26,.09,.97)
            for x in (-.42,-.08,.26):
                m.block("trim",x,0,-.435,.037,.296,.035)
            for z in (-.19,.19):
                m.block("trim",.484,0,z,.027,.296,.04)
            m.vault("wall",-.08,.378,.05,.65,.34,.33)
            with m.transform(translate=(-.08,.522,-.124),rotate_x=-90):
                m.drum("gold",0,0,0,.132,.033)
                m.drum("ivory",0,.03,0,.104,.012)
            if m.lod<2:
                m.beam("door",(-.08,.523,-.165),(-.08,.595,-.165),.020)
                m.beam("door",(-.08,.523,-.177),(-.015,.543,-.177),.017)
        elif age==8:
            for i in range(3):
                w=1.15-i*.22
                m.slab("glass",i*.062,i*.215,0,w,.17,.88-i*.135,taper=.08)
                m.slab("wall",i*.062,i*.215+.166,0,w+.05,.053,.93-i*.135)
            m.beam("metal",(.18,.64,.10),(.18,.88,.10),.027)
        else:
            m.slab("ivory",0,0,0,1.23,.14,.92,taper=.18)
            m.slab("ascension",0,.135,0,.91,.08,.67)
            for s in (-1,1):
                m.beam("gold",(s*.42,.10,0),(s*.22,.39,0),.128)
            m.wheel(0,.59,0,.32,"gold",4)


def domed_moot(m):
    # The basilica's high crossing opens onto a lower three-bay council loggia.
    m.slab("fieldstone",0,0,.029,1.89,.119,1.59,.16)
    m.slab("plaster",-.165,.113,.321,1.25,.554,.914,.12)
    m.slab("roof_tile",-.165,.662,.321,1.34,.125,1.001,.15,.095)
    m.drum("plaster",-.165,.781,.323,.448,.213,.424)
    m.dome((-.165,.988,.323),.449,m.color("roof_tile"),rings=(4,3,2)[m.lod],
           segments=(16,10,7)[m.lod],squash=.85,material="roof_tile")
    m.drum("gold",-.165,1.356,.323,.081,.118,.049)
    m.slab("wall",.58,.112,.108,.404,.811,.682,.125,.045)
    m.vault("roof_tile",.58,.915,.108,.466,.176,.753)
    m.entry(0,.167,-.141,.373,.398)
    m.slab("slate",-.084,.109,-.388,1.526,.075,.628,.12)
    for x in (-.702,-.345,.171,.545):
        m.drum("fieldstone",x,.179,-.572,.088,.398,.072,n=(8,6,4)[m.lod])
        if m.lod==0:
            m.slab("gold",x,.568,-.572,.183,.062,.187,.15)
    m.slab("trim",-.08,.571,-.392,1.544,.126,.583,.08)
    m.vault("roof_tile",-.08,.689,-.386,1.605,.139,.635)
    m.slab("wall",-.075,.012,-.798,.637,.098,.241,.10)
    m.slab("slate",-.075,.11,-.635,.593,.071,.17,.10)
    if m.lod<2:
        m.rune(-.076,.753,-.707,.192)
    if m.lod==0:
        for x in (-.701,-.349,.017,.382):
            m.block("accent",x,.721,-.703,.043,.051,.039)
        for a in (-1.24,-.59,.06,.72):
            m.block("door",-.165+.438*math.sin(a),.834,.323-.438*math.cos(a),.056,.108,.060)
        m.lamp(.708,.115,-.579,.122)


def network_moot(m):
    # Two occupied wings step up around a low glazed council bridge. A beacon tops the tall wing.
    m.slab("fieldstone",0,0,.01,1.88,.143,1.67,.215)
    for x,z,w,d,levels in ((-.515,.245,.689,.975,3),(.473,.104,.704,.959,2)):
        for i in range(levels):
            y=.137+.258*i
            width=w-.077*i
            depth=d-.125*i
            m.slab("wall",x,y,z+i*.036,width,.125,depth,.17,.047)
            m.slab("glass",x,y+.121,z+i*.036,width*.953,.145,depth*.954,.185)
            if m.lod==0:
                for dx in (-.22,0,.22):
                    m.block("metal",x+dx*width,y+.132,z+i*.036-depth*.476,.029,.123,.033)
        roof=.138+levels*.258
        m.slab("ivory",x,roof,z+.036*(levels-1),w-.077*(levels-1)+.065,.102,
               d-.125*(levels-1)+.071,.158)
    m.slab("glass",-.032,.419,.348,.732,.23,.348,.12)
    m.slab("ivory",-.032,.642,.348,.792,.083,.402,.135)
    m.slab("wall",-.001,.138,-.103,.678,.292,.488,.107)
    m.entry(0,.143,-.351,.416,.268)
    m.vault("metal",0,.423,-.104,.713,.124,.506)
    m.slab("slate",0,.134,-.505,.513,.059,.339,.126)
    m.slab("glass",-.515,1.008,.318,.321,.16,.378,.21,.14)
    m.beam("metal",(-.515,1.148,.316),(-.515,1.426,.316),.051)
    m.drum("accent",-.515,1.406,.316,.076,.083,.05)
    if m.lod<2:
        for s in (-1,1):
            m.slab("wall",s*.462,.134,-.492,.228,.193,.237,.18,.13)
            m.lamp(s*.462,.321,-.492,.097)
    if m.lod==0:
        m.rune(0,.495,-.359,.147)
        for z in (.089,.519):
            m.beam("trim",(-.825,.146,z),(-.747,.855,z+.014),.082)


def build(spec,lod):
    m=Stonework(spec,lod)
    age=AGES.index(spec.ages[0])
    if age in (5,8):
        if age==5:
            domed_moot(m)
        else:
            network_moot(m)
        return m
    masonry=("fieldstone","brick","fieldstone","plaster","wall","plaster","brick","wall","wall","ivory")[age]
    h=(.39,.44,.46,.51,.52,.51,.54,.48,.48,.49)[age]
    m.slab("fieldstone",0,0,.02,1.86,.12,1.48,.15,.035)
    m.slab(masonry,0,.114,.03,1.69,h-.10,1.35,.13)
    m.slab("roof",0,h,.03,1.83,.105,1.49,.13,.035)
    crown(m,age,h+.101)
    # Bronze-clad gate projects from the retaining wall. The stairs end at its threshold.
    m.vault(masonry,0,.104,-.60,.85,h*.95,.39)
    m.entry(0,.116,-.799,.61,h*.88,frame="fieldstone" if age==0 else "trim")
    m.block("fieldstone",0,0,-.865,.68,.054,.21)
    m.block("wall",0,.052,-.821,.63,.065,.14)
    if lod<2:
        for s in (-1,1):
            m.slab("wall",s*.636,.065,-.535,.245,h+.034,.36,.14,.19)
            m.slab("accent",s*.636,h+.092,-.535,.238,.043,.318,.12)
            # The small braziers need fewer facets than the nearby recessed gate.
            segments=5 if lod==1 and age in (0,7,8,9) else None
            m.lamp(s*.636,h+.13,-.535,.10,segments)
        m.rune(0,h+.012,-.811,.174)
        for s in (-1,1):
            m.block("trim",s*.847,.138,.13,.105,h-.02,.118,taper=.16)
    if lod==0:
        for s in (-1,1):
            for z in (-.31,.38):
                m.block("trim",s*.843,.227,z,.025,.141,.190)
                m.block("emissive",s*.856,.248,z,.014,.077,.124)
            m.slab("trim",s*.831,.10,.48,.158,h*.78,.19,.11,.23)
        for x in (-.61,-.42,.42,.61):
            m.block("accent",x,h+.023,.758,.084,.039,.021)
        for s in (-1,1):
            m.relief("banner",[(s*.35-.052,h*.69),(s*.35,h*.59),(s*.35+.052,h*.69),
                               (s*.35+.052,h*.98),(s*.35-.052,h*.98)],-.806,.013)
    return m


if __name__=="__main__":
    publish_package("dwarf_towncenters",build)
