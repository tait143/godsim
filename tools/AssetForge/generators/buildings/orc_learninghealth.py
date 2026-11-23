"""Orc halls of learning and healing."""
import hashlib
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import ROOT, package_models
from forge import MeshBuilder, publish
from forge.atlas import palette
from forge.render import contact_sheet
from generators.buildings.orc_guild_eras import DESIGNERS, FIRST_ERA

PACKAGE = "orc_learninghealth"
ERAS = ("ancient", "classical", "industrial", "future")


class Lodge(MeshBuilder):
    """The camp's masonry, curved tusk corbels and stitched, faceted roof shells."""

    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.e = ERAS.index(spec.band)
        self.colors = palette(spec.ages[0])[0]
        self.frame = ("timber", "trim", "metal", "metal")[self.e]
        self.skin = ("hide", "roof", "metal", "glass")[self.e]
        self.cladding = ("wall", "wall", "brick", "plaster")[self.e]
        self.n = (12, 8, 5)[lod]

    def col(self, mat):
        return self.colors.get(mat, self.colors.get("orc/" + mat))

    def face(self, mat, pts):
        self.poly(pts, self.col(mat), material=mat)

    def block(self, mat, x, base, z, w, h, d, taper=0):
        self.box((x, base+h/2, z), (w, h, d), self.col(mat), material=mat, taper=taper)

    def drum(self, mat, x, y, z, r, h, rt=None, n=None):
        self.cylinder((x,y,z), r, h, self.col(mat), segments=n or self.n,
                      top_radius=rt, material=mat)

    def beam(self, mat, a, b, w, depth=None):
        dx, dy, dz = (v-u for u,v in zip(a,b))
        length = math.sqrt(dx*dx + dy*dy + dz*dz)
        with self.transform(translate=a, rotate_y=math.degrees(math.atan2(dx,dz)),
                            rotate_x=math.degrees(math.atan2(math.hypot(dx,dz),dy))):
            self.box((0,length/2,0),(w,length,depth or w),self.col(mat),
                     material=mat,bottom=True)

    def horn(self, x, y, z, size, side=1, lean=0):
        # Swell at the bound socket, then sweep away and turn back to a point.
        n = (7,5,3)[self.lod]
        stations = [(0,0,.15),(.17,.12,.12),(.27,.32,.067)]
        if self.lod==2:
            stations = [stations[0],stations[-1]]
        rings = []
        for cx, cy, rad in stations:
            rings.append([(x+size*(side*cx+rad*math.cos(i*math.tau/n)), y+size*cy,
                           z+size*(lean*cy+rad*math.sin(i*math.tau/n))) for i in range(n)])
        for j,(lower,upper) in enumerate(zip(rings,rings[1:])):
            for i in range(n):
                k=(i+1)%n
                self.face("hide" if not j and self.lod<2 else "ivory",
                          (lower[k],lower[i],upper[i],upper[k]))
        for i in range(n):
            self.face("ivory",(rings[-1][(i+1)%n],rings[-1][i],(x+size*side*.19,y+size*.64,z+size*lean*.64)))

    def profile(self, x, z, rows, n=None, stripe=False):
        """Closed-top lathe; radii are independently specified along X and Z."""
        n=n or self.n
        levels=[]
        for y,rx,rz,mat in rows:
            ring=[(x+rx*math.cos((i+.5)*math.tau/n),y,
                   z+rz*math.sin((i+.5)*math.tau/n)) for i in range(n)]
            if levels:
                last=levels[-1]
                for i in range(n):
                    k=(i+1)%n
                    color="cloth" if stripe and mat==self.skin and i in (0,n//2) else mat
                    self.face(color,(last[k],last[i],ring[i],ring[k]))
            levels.append(ring)
        self.face(rows[-1][3],list(reversed(levels[-1])))

    def cell(self,x,z,w,d,h):
        e=self.e
        # A battered inhabited drum, a cantilevered rim and a creased crown.
        roof=(.77,.60,.36,.46)[e]*h
        rows=[(0,.89*w,.89*d,self.frame),(.12*h,w,d,"fieldstone"),
              (h,.91*w,.91*d,self.cladding)]
        if self.lod<2:
            rows += [(h+.045*h,1.045*w,1.045*d,self.frame)]
        rows += [(h+.085*h,1.05*w,1.05*d,"cloth" if e<3 else "turquoise")]
        if self.lod==0:
            rows += [(h+roof*.54,.67*w,.66*d,self.skin)]
        rows += [(h+roof,(.09,.18,.56,.32)[e]*w,(.12,.21,.53,.30)[e]*d,self.skin)]
        if e==2:
            rows += [(h+roof+.14*h,.47*w,.43*d,"glass"),(h+roof+.19*h,.51*w,.48*d,"metal")]
        elif e==3:
            rows += [(h+roof+.052*h,.34*w,.32*d,"turquoise"),(h+roof+.19*h,.105*w,.10*d,"ivory")]
        self.profile(x,z,rows,stripe=True)
        if self.lod<2:
            for s in (-1,1):
                self.horn(x+s*w*.87,h*.81,z-d*.35,min(w,d)*1.12,s,-.19)
        if self.lod<2:
            # Mount each window flush to an actual drum facet, using that facet's chord.
            n=self.n
            for i in (0,n//2,n-1):
                a=(i+.5)*math.tau/n
                b=(i+1.5)*math.tau/n
                u=(math.cos(a),math.sin(a))
                v=(math.cos(b),math.sin(b))
                def pane(mat,left,right,low,high,offset):
                    def pt(t,y):
                        fac=1-.09*(y-.12)/.88+offset
                        return (x+fac*w*((1-t)*u[0]+t*v[0]),y*h,
                                z+fac*d*((1-t)*u[1]+t*v[1]))
                    self.face(mat,(pt(right,low),pt(left,low),pt(left,high),pt(right,high)))
                pane(self.frame,.10,.90,.29,.73,.010)
                pane("emissive" if e<2 else "glass",.17,.83,.34,.67,.019)
                if self.lod==0:
                    pane("trim",.46,.53,.34,.67,.026)
        if self.lod==0:
            for s in (-1,1):
                self.beam(self.frame,(x+s*w*.92,.045*h,z-d*.22),
                          (x+s*w*.82,h,z-d*.25),.065*min(1,w/.5))
                self.beam("cloth",(x+s*w*.90,h*.54,z-d*.23),
                          (x+s*w*.88,h*.63,z-d*.24),.077*min(1,w/.5))
        return rows[-1][0]

    def entry(self,x,z,w,h,base=0):
        # A projecting, five-sided door surround seats in the shell behind it.
        def arch(mat,width,height,front,back):
            ring=[(-.5,0),(-.5,.72),(0,1),(.5,.72),(.5,0)]
            f=[(x+u*width,base+v*height,front) for u,v in ring]
            b=[(u,v,back) for u,v,_ in f]
            self.face(mat,f)
            for i in range(4):
                k=(i+1)%5
                self.face(mat,(f[k],f[i],b[i],b[k]))
        arch(self.frame,w,h,z,z+w*.46)
        arch("door",w*.69,h*.80,z-.013,z+.008)
        if self.lod<2:
            self.block("fieldstone",x,base,z-.08*w,w*1.12,.045*h,w*.57)
            self.block("gold",x+w*.14,base+h*.32,z-.026,w*.075,h*.075,.026)
        if self.lod==0:
            for s in (-1,1):
                self.horn(x+s*w*.47,base+h*.62,z+w*.08,w*.52,s,-.14)

    def disk(self,mat,x,y,z,r,d,n=None):
        with self.transform(translate=(x,y,z),rotate_x=-90):
            self.drum(mat,0,0,0,r,d,n=n)

    def leaf(self,x,y,z,r,turn=0):
        # A thick lanceolate blade folded around its midrib.
        with self.transform(translate=(x,y,z),rotate_y=turn,scale=r):
            a,b,c,d,e=(0,0,0),(-.31,.64,.14),(0,1.48,.32),(.31,.64,.14),(0,.68,-.12)
            for face in ((a,b,e),(b,c,e),(c,d,e),(d,a,e),(a,d,c,b)):
                self.face("leaf",face)

    def jar(self,x,y,z,r,h,mat="hide"):
        self.profile(x,z,[(y,r*.72,r*.72,mat),(y+h*.38,r,r,mat),
                         (y+h*.88,r*.65,r*.65,mat),(y+h,r*.67,r*.67,"trim")],
                     n=(8,6,4)[self.lod])

    def crest(self,x,y,z,s,kind):
        if kind=="care":
            # The healers' waymark: a pale plus on an unpainted deep-green disk.
            self.disk("door",x,y,z,s*1.04,s*.30)
            self.block("ivory",x,y-s*.62,z-s*.35,s*.39,s*1.24,s*.12)
            for side in (-1,1):
                self.block("ivory",x+side*s*.415,y-s*.178,z-s*.35,s*.44,s*.355,s*.12)
        elif kind=="book":
            for side in (-1,1):
                with self.transform(translate=(x+side*s*.52,y,z+side*s*.018),rotate_z=-side*14):
                    self.block("cloth",0,-s*.49,0,s*.96,s*.98,s*.32)
                    self.block("ivory",0,-s*.40,-s*.18,s*.79,s*.81,s*.078)
                    if self.lod==0:
                        for h in (.08,-.13):
                            self.block("timber",0,h*s,-s*.23,s*.43,s*.035,s*.026)


def build_lodge(spec,lod,designer):
    m=Lodge(spec,lod)
    w,d=spec.footprint
    # Keep civic entrances about the same scale in adjacent lots.
    with m.transform(scale=(w*.475, (.63+.18*min(w,d)), d*.475)):
        designer(m,spec.building_type)
    return m


def bath(m):
    # The stepped bath occupies the foreground. Its bright recessed surface is below the coping.
    m.cell(-.13,.32,.68,.53,.60)
    m.entry(-.49,-.125,.30,.44)
    x,z=.34,-.55
    m.profile(x,z,[(0,.52,.385,"fieldstone"),(.105,.59,.43,"fieldstone"),
                   (.30,.56,.40,"wall"),(.325,.595,.426,"ivory"),
                   (.325,.49,.33,"ivory"),(.234,.455,.30,"turquoise")])
    if m.lod<2:
        for k in range(2 if m.lod==0 else 1):
            m.block("fieldstone",-.51,0,-.412+k*.152,.30+.014*k,.038+.024*k,.152)
        m.horn(.84,.22,-.67,.45,1)
    if m.lod==0:
        m.jar(-.75,0,-.18,.092,.19)
    # Stove and flue attach to the hot room behind the pool.
    m.profile(.42,.38,[(0,.20,.20,"fieldstone"),(.74,.14,.14,m.cladding),
                       (1.01,.108,.108,m.frame),(1.07,.145,.145,m.frame)])


def herbalist(m):
    if m.e==0:
        m.skin="roof"
    top=m.cell(-.12,.18,.68,.64,.53)
    m.entry(-.21,-.413,.32,.42)
    x,z=.42,-.43
    m.profile(x,z,[(0,.17,.17,"fieldstone"),(.07,.23,.23,"fieldstone"),
                   (.28,.28,.28,"metal" if m.e==0 else "hide"),
                   (.315,.25,.25,"ivory"),(.315,.185,.185,"soil")])
    for i in range((5,3,2)[m.lod]):
        with m.transform(translate=(x,.305,z),rotate_y=i*137+11,rotate_z=31+i%2*12):
            m.leaf(0,0,0,.22+i%3*.04)
    if m.lod<2:
        m.horn(-.33,top-.09,.19,.48,-1)
        m.jar(-.65,0,-.39,.105,.18)
    if m.lod==0:
        m.jar(-.53,0,-.59,.076,.13,"roof_tile")


def field_surgery(m):
    # The broad, low receiving hall has a flared triage awning and a separate oxygen plant.
    m.profile(-.23,.23,[(0,.66,.51,"wall"),(.116,.69,.54,"fieldstone"),
              (.581,.59,.46,"plaster"),(.641,.71,.55,"metal"),(.843,.48,.36,"roof"),
              (.879,.43,.33,"trim")])
    if m.lod<2:
        m.profile(-.24,.20,[(.879,.210,.180,"metal"),(1.03,.175,.149,"glass"),
                  (1.10,.199,.167,"ivory"),(1.16,.068,.057,"roof")])
    for s in (-1,1):
        m.block("fieldstone",-.16+s*.44,0,-.73,.13,.065,.13)
        m.beam("metal",(-.16+s*.44,.043,-.73),(-.16+s*.30,.477,-.588),.067)
        m.horn(-.16+s*.32,.479,-.592,.43,s)
    m.profile(-.16,-.446,[(.413,.50,.37,"metal"),(.456,.60,.43,"cloth"),
              (.559,.49,.34,"cloth"),(.644,.27,.175,"banner")])
    m.entry(-.20,-.341,.44,.400)
    m.block("trim",-.16,.455,-.630,.090,.348,.094)
    m.crest(-.16,.777,-.654,.175,"care")
    for j,z in enumerate((.515,-.021)):
        x=.661
        r=.211 if j==0 else .178
        h=1.20 if j==0 else .83
        m.profile(x,z,[(0,r*1.13,r*1.13,"wall"),(.14,r,r,"metal"),
                  (h-.16,r*.90,r*.90,"glass"),(h-.11,r*1.05,r*1.05,"ivory"),
                  (h,r*.58,r*.58,"roof")])
        if m.lod<2:
            m.drum("metal",x,.35,z,r*1.014,.058)
            m.beam("metal",(x,.55,z),(x-.53,.53,z-.075),.071)
    if m.lod==0:
        m.jar(-.69,0,-.43,.093,.149)
        m.jar(-.78,0,-.27,.064,.113)


def ward(m):
    height=.72
    m.cell(.03,.20,.54,.59,height)
    for s in (-1,1):
        m.cell(s*.57,-.09,.355,.49,.39)
    m.entry(.04,-.37,.38,.47)
    m.crest(.035,height*.80,-.367,.175,"care")
    if m.lod==0:
        for s in (-1,1):
            for k in (-1,1):
                m.block("glass" if m.e>1 else "emissive",s*.57+k*.084,.16,-.53,.107,.13,.047)
        m.jar(-.21,0,-.47,.075,.13)
    if m.lod<2:
        for s in (-1,1):
            m.beam("ivory",(s*.19,.035,-.59),(s*.19,.28,-.37),.043)


def archive(m,university=False):
    # Stair-stepped reading galleries surround an open-book pediment.
    mid=m.cell(.08,.19,.59,.62,.90 if university else .74)
    m.cell(-.54,-.015,.36,.45,.58 if university else .35)
    m.cell(.59,.04,.33,.43,.52 if university else .34)
    m.entry(.035,-.43,.38,.47)
    m.crest(.05,.71 if university else .60,-.392,.153,"book")
    if university:
        m.profile(-.47,.31,[(.40,.203,.203,"wall"),(1.07,.163,.163,m.cladding),
                             (1.14,.23,.23,"cloth"),(1.34,.061,.061,m.skin)])
        m.horn(-.46,1.32,.31,.43,-1)
    if m.lod<2:
        for s in (-1,1):
            m.block("fieldstone",s*.42,.01,-.47,.20,.095,.13)
    if m.lod==0:
        for s in (-1,1):
            for x in (.49,.67):
                m.block("glass" if m.e>=2 else "emissive",s*x,.16,-.353,.09,.12,.085)
        m.jar(-.28,0,-.47,.076,.137)


def observatory(m):
    m.cell(-.32,.15,.53,.56,.52)
    m.entry(-.34,-.38,.33,.42)
    x,z=.32,-.025
    m.profile(x,z,[(0,.42,.42,"fieldstone"),(.18,.48,.48,"wall"),
                   (.67,.385,.385,m.cladding),(.75,.445,.445,"cloth")])
    m.dome((x,.745,z),.41,m.col(m.skin),rings=(4,2,1)[m.lod],segments=m.n,
           squash=.90,material=m.skin)
    # Counterweighted tube rises above the roof shutter. Objective faces skyward.
    with m.transform(translate=(x,.985,z),rotate_z=-43,rotate_x=-18):
        m.drum(m.frame,0,-.10,0,.088,.57,.133)
        m.drum("gold",0,.45,0,.154,.075)
        m.drum("glass",0,.525,0,.122,.017)
        if m.lod==0:
            m.drum("cloth",0,.24,0,.125,.043)
    if m.lod<2:
        m.horn(.63,.66,-.14,.53,1)
    if m.lod==0:
        m.disk("gold",.28,.43,-.432,.106,.02)


def machinery(m,t):
    data=t=="DataCenter"
    m.cell(-.22,.105,.64,.62,.62 if data else .59)
    m.entry(-.30,-.489,.34,.44)
    if t in ("Scriptorium","Printworks"):
        # An outsized page / press demonstration sits in its own covered workbay.
        x,z=.42,-.24
        m.block("timber" if m.e<2 else "metal",x,0,z,.53,.16,.53,.05)
        if t=="Printworks":
            for s in (-1,1):
                m.block(m.frame,x+s*.19,.16,z,.08,.52,.095)
            m.block(m.frame,x,.62,z,.52,.09,.13)
            m.drum("gold",x,.38,z,.047,.32)
            m.block("trim",x,.34,z,.30,.07,.24)
            m.block("ivory",x,.16,z-.06,.35,.042,.32)
            if m.lod<2:
                m.beam("timber",(x-.16,.759,z),(x+.14,.759,z),.046)
                m.drum(m.frame,x,.69,z,.044,.091)
        else:
            m.block(m.frame,x,.15,z,.19,.16,.20)
            with m.transform(translate=(x,.31,z),rotate_x=-13):
                m.block("cloth",0,0,0,.50,.052,.36)
                m.block("ivory",0,.052,0,.45,.016,.31)
                if m.lod==0:
                    for k in (-1,0,1):
                        m.block("timber",-.03,.069,k*.072,.25,.007,.015)
            # Carved feather in the ink vessel.
            m.jar(.64,.155,-.39,.085,.121,"trim")
            with m.transform(translate=(.64,.27,-.39),rotate_z=-18):
                m.beam("gold",(0,0,0),(0,.49,0),.032)
                pts=[(0,.10,-.01),(-.091,.30,-.01),(-.065,.495,-.01),
                     (.012,.63,-.01),(.064,.38,-.01)]
                m.face("ivory",pts)
                m.face("ivory",[(a,b,c+.025) for a,b,c in reversed(pts)])
        m.crest(-.27,.54,-.449,.12,"book")
    else:
        x,z=.44,-.10
        if data:
            for k in (-1,0,1):
                cx=x+k*.195
                h=.93-abs(k)*.14
                m.block("metal",cx,0,z,.18,h,.63,.06)
                m.block("glass",cx,.14,z-.302,.137,h*.70,.054)
                if m.lod<2:
                    for j in range(3 if m.lod==0 else 1):
                        m.block("turquoise",cx,.28+j*.15,z-.34,.10,.025,.024)
        else:
            m.profile(x,z,[(0,.265,.265,"fieldstone"),(.17,.31,.31,"metal"),
                           (.25,.25,.25,"ivory"),(.44,.241,.241,"glass"),
                           (.45,.255,.255,"metal"),(.495,.255,.255,"turquoise"),
                           (.505,.237,.237,"metal"),(.67,.23,.23,"glass"),
                           (.74,.26,.26,"metal"),(.91,.12,.12,"glass")])
            m.horn(x,.81,z,.54,1)
        if m.lod==0:
            m.jar(-.64,0,-.39,.087,.173,"metal")
    if m.lod==0 and t in ("Printworks","Scriptorium"):
        m.jar(-.67,0,-.36,.084,.17)


def design(m,t):
    if t in DESIGNERS and m.e>=FIRST_ERA[t]:
        DESIGNERS[t](m)
    elif t=="Bathhouse":
        bath(m)
    elif t=="Herbalist":
        herbalist(m)
    elif t=="MedicalCenter":
        field_surgery(m)
    elif t=="Hospital":
        ward(m)
    elif t in ("Library","University"):
        archive(m,t=="University")
    elif t=="Observatory":
        observatory(m)
    else:
        machinery(m,t)


def build(spec,lod):
    return build_lodge(spec,lod,design)


def run_package(package,build_model):
    entries=[]
    for spec in package_models(package):
        lods=[build_model(spec,i) for i in range(3)]
        entry=publish(lods[0],spec.metadata(),package,lods=lods[1:],preview_size=208)
        entries.append(entry)
        print(spec.id,entry["lod_triangles"],hashlib.sha256((ROOT/entry["path"]).read_bytes()).hexdigest(),flush=True)
    directory=(ROOT/entries[0]["preview"]).parent
    for age in sorted({age for e in entries for age in e["ages"]}):
        group=[e for e in entries if age in e["ages"]]
        contact_sheet([ROOT/e["age_previews"][age] for e in group],
                      directory/("sheet_"+age+".png"),cols=min(5,len(group)),cell=112,
                      labels=[e["building_type"] for e in group])


if __name__=="__main__":
    run_package(PACKAGE,build)
