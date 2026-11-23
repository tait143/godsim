"""Orc council halls and their carved, bent-tusk timberwork."""
import math
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, ROOT, package_models
from forge import MeshBuilder, publish
from forge.atlas import palette, ROLES
from generators.buildings.orc_clan_assemblies import COUNCILS

PACKAGE = "orc_towncenters"
ERAS = ("ancient", "classical", "industrial", "future")


class Lodge(MeshBuilder):
    """Orc joinery. Coordinates use actual ground, with polygon flats facing the gate."""
    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.age = AGES.index(spec.ages[0])
        self.era = ERAS.index(spec.band)
        self.colors = palette(spec.ages[0])[0]

    def col(self, mat):
        return self.colors["orc/" + mat if mat in ROLES else mat]

    def panel(self, mat, points):
        self.poly(points, self.col(mat), material=mat)

    color = col
    face = panel

    def block(self, mat, x, y, z, w, h, d, taper=0):
        self.box((x,y+h/2,z),(w,h,d),self.col(mat),material=mat,taper=taper)

    def post(self, mat, a, b, radius, end=None, sides=None):
        """A closed-ended spar with true axial cross-sections, including horizontal logs."""
        delta = np.asarray(b, dtype=float) - a
        length = np.linalg.norm(delta)
        dx, dy, dz = delta / length
        yaw = math.degrees(math.atan2(dx, dz))
        pitch = math.degrees(math.acos(max(-1., min(1., dy))))
        with self.transform(translate=a, rotate_y=yaw, rotate_x=pitch):
            self.cylinder((0,0,0),radius,length,self.col(mat),segments=sides or (8,6,4)[self.lod],
                          top_radius=end,material=mat,cap_bottom=True)

    def shell(self, x, z, rings, sides=None, flatten=1., panels=False, dx=0, dz=0):
        """Turn continuous flared profiles. A ring's material covers the skin above it."""
        n = sides or (12,8,6)[self.lod]
        contours=[]
        for y,r,mat in rings:
            contours.append([(x+dx*y+r*math.sin(2*math.pi*(j+.5)/n), y,
                              z+dz*y-flatten*r*math.cos(2*math.pi*(j+.5)/n)) for j in range(n)])
        for i, (lower,upper) in enumerate(zip(contours,contours[1:])):
            mat=rings[i][2]
            for j in range(n):
                k=(j+1)%n
                face_mat = "banner" if panels and i==1 and j in (0,n//2+1,n-2) else mat
                if rings[i+1][1]==0:
                    self.panel(face_mat,[lower[k],lower[j],upper[j]])
                else:
                    self.panel(face_mat,[lower[k],lower[j],upper[j],upper[k]])
        if rings[-1][1]>0:
            self.panel(rings[-1][2],contours[-1][::-1])

    def tusk(self,x,y,z,side,size,mat="ivory"):
        # Outward curve and an inward-pointing tip, with a dark ferrule at the root.
        shape=[(0.,0.,.115),(.17,.18,.093),(.25,.40,.061),(.22,.60,.028),(.15,.745,0.)]
        if self.lod==2:
            shape=[shape[i] for i in (0,2,4)]
        n=(7,5,4)[self.lod]
        rings=[[(x+side*o*size+r*size*math.cos(j*2*math.pi/n), y+h*size,
                 z+r*size*math.sin(j*2*math.pi/n)-h*.11*size) for j in range(n)] for o,h,r in shape]
        for i,(a,b) in enumerate(zip(rings,rings[1:])):
            for j in range(n):
                k=(j+1)%n
                self.panel(mat,[a[k],a[j],b[j]] if i==len(rings)-2 else [a[k],a[j],b[j],b[k]])
        if self.lod==0:
            self.shell(x,z,[(y-.024*size,.132*size,"trim"),(y+.031*size,.130*size,"gold")],7)

    def drum(self,x,y,z,r,h):
        self.shell(x,z,[(y,r*.73,"timber"),(y+h*.20,r*.9,"banner"),(y+h*.78,r,"trim"),
                        (y+h,r*.98,"hide")],(10,7,5)[self.lod])
        if self.lod==0:
            for j in range(6):
                a=j*math.pi/3
                self.post("ivory",(x+r*.84*math.cos(a),y+h*.18,z+r*.84*math.sin(a)),
                          (x+r*math.cos(a+.24),y+h*.86,z+r*math.sin(a+.24)),r*.052,sides=4)

    def flag(self,x,base,z,h,span):
        self.post("trim",(x,base,z),(x,base+h+.025,z),span*.065,sides=5)
        with self.transform(translate=(x,base+h,z),scale=span):
            # A thick, folded swallowtail with a raised seam and a rain-shedding notch.
            p=[(0,0,0),(.52,-.05,-.07),(1,-.11,.02),(.73,-.31,-.025),(.98,-.57,.02),
               (.52,-.52,-.07),(0,-.46,0)]
            for inds in ((0,1,5,6),(1,2,3,5),(5,3,4)):
                face=[p[i] for i in inds]
                self.panel("cloth",face)
                self.panel("cloth",[(a,b,c+.018) for a,b,c in reversed(face)])

    def hut(self,x,z,s,era=None,base=0,entry=True,roof_material=None):
        """A small service lodge with a projecting vestibule and painted roof gores."""
        era=self.era if era is None else era
        with self.transform(translate=(x,base,z),scale=s):
            h=(.37,.50,.48,.55)[era]
            wall=("wall","fieldstone","brick","glass")[era]
            roof=roof_material or ("hide","roof_tile","metal","plaster")[era]
            self.shell(0,0,[(0,.475,"fieldstone"),(.068,.48,wall),(h,.429,"trim")],flatten=.84)
            self.shell(0,0,[(h-.035,.54,"trim"),(h+.025,.555,roof),(h+.25,.27,roof),
                            (h+(.50,.56,.39,.30)[era],.065,"trim")],flatten=.89,panels=True)
            if entry:
                self.entry(0,-.348,0,.29,h*.85,era)
            if self.lod<2:
                for side in (-1,1):
                    self.tusk(side*.14,h+.205,.012,side,.34)
            if self.lod==0:
                for side in (-1,1):
                    self.post("trim",(side*.39,.049,-.215),(side*.36,h+.03,-.215),.032)
                if era>=2:
                    self.block("glass",.29,.14,-.298,.14,.112,.09)
            if era==3:
                self.shell(.014,0,[(h+.27,.099,"glass"),(h+.37,.073,"ascension")],6)

    def entry(self,x,z,y,w,h,era):
        frame=("timber","trim","metal","plaster")[era]
        self.block("door",x,y+.006,z,w,h,.16)
        for side in (-1,1):
            self.block(frame,x+side*w*.51,y,z-.05,.080*w+.026,h+.01,.157)
        self.block("banner",x,y+h,z-.04,w*1.24,h*.145,.193,.09)
        if self.lod==0:
            self.block("gold",x-w*.18,y+h*.47,z-.089,.035*w+.007,.030,.016)


def council(spec,lod):
    m=Lodge(spec,lod)
    a=m.age
    if a in COUNCILS:
        COUNCILS[a](m)
        return m
    rise=(.36,.44,.54,.56,.70,.61,.65,.62,.66,.64)[a]
    peak=(.68,.55,.65,.48,.73,.65,.36,.31,.41,.57)[a]
    wall=("hide","wall","fieldstone","plaster","fieldstone","plaster","brick","wall","glass","ivory")[a]
    roof=("hide","hide","roof","roof_tile","slate","roof_tile","metal","metal","glass","plaster")[a]
    profile=(8,10,8,12,8,12,10,8,10,12)[a]
    n=(profile,max(6,profile-2),6)[lod]
    with m.transform(translate=(0,0,.12)):
        m.shell(0,0,[(0,.75,"fieldstone"),(.08,.76,wall),(rise,.692,"trim")],n,flatten=.91)
        r=.835
        shoulder=(.40,.43,.36,.55,.36,.55,.69,.66,.52,.39)[a]
        m.shell(0,0,[(rise-.022,r*.965,"trim"),(rise+.039,r,roof),
                     (rise+peak*.59,shoulder,roof),(rise+peak,.11,"trim")],n,flatten=.945,panels=True)
        # Broad gate projects through the eave and reads as the front even at the lowest LOD.
        m.entry(0,-.662,0,.44,.34+rise*.18,m.era)
        cap=.34+rise*.18
        m.hip_roof((0,cap+.044,-.648),.57,.47,.18,m.col(roof),overhang=0,material=roof)
        for side in (-1,1):
            m.tusk(side*.245,cap*.83,-.73,side,.50 if a<6 else .44)
        top=rise+peak
        m.shell(0,0,[(top-.018,.145,"trim"),(top+.058,.145,"cloth"),(top+.124,.10,"gold")],n)
        if a in (0,1,4,5,8):
            m.flag(0,top+.12,0,(.35,.38,.4,.35,.40,.31,.28,.3,.44,.3)[a],.29)
        if a in (2,3,6,7,9):
            for side in (-1,1):
                m.tusk(side*.062,top+.09,0,side,.49 if a in (2,9) else .30,
                       "gold" if a==9 else "ivory")
        if lod<2:
            for j in (1,3,5,7):
                ang=math.pi*j/4
                x,z=.71*math.sin(ang),.64*math.cos(ang)
                m.post("timber" if a<6 else "metal",(x,.035,z),(x*.96,rise,z*.96),.044)
                if lod==0:
                    m.post("banner",(x*.973,rise*.65,z*.973),(x*.97,rise*.75,z*.97),.051)
        if lod==0:
            for side in (-1,1):
                m.block("fieldstone",side*.29,0,-.789,.095,.084,.162,.08)
    # Age-specific civic wings progress from a drumming circle to a lit council crown.
    if a==1:
        # Bronze debt tablets are tallied on a stepped mudbrick dais facing the plaza.
        m.block("brick",.57,0,-.11,.73,.25,.65,.08)
        m.block("brick",.58,.241,-.11,.64,.25,.58,.11)
        m.block("gold",.58,.485,-.11,.58,.065,.54,.04)
        m.shell(.63,-.11,[(.548,.26,"hide"),(.74,.16,"gold"),(.83,.06,"trim")],(8,6,4)[lod])
        if lod<2:
            m.block("door",.67,.068,-.418,.17,.28,.04)
    if a in (4,5):
        with m.transform(translate=(-.63,0,.26),scale=(.57, .69 if a==4 else .55,.72)):
            m.hut(0,0,1,0 if a==1 else 1)
    if a in (2,3,6,7,8,9):
        count=2 if a in (3,7,9) else 1
        for x in ((-.695,.695) if count==2 else (.62,)):
            h={2:.69,3:.86,6:1.21,7:.98,8:1.28,9:.94}[a]
            if a==3:
                with m.transform(translate=(x,0,-.094)):
                    m.shell(0,0,[(0,.242,"fieldstone"),(.116,.239,"plaster"),(.17,.175,"ivory"),
                                 (h,.142,"plaster"),(h+.055,.195,"gold"),(h+.10,.21,"roof_tile"),
                                 (h+.29,.030,"gold")],(10,8,5)[lod])
                continue
            if a==7:
                m.shell(x,-.06,[(0,.226,"plaster"),(.57,.203,"metal"),(.62,.205,"glass"),
                                 (.90,.190,"metal"),(.96,.209,"slate"),(1.02,.167,"metal")],(10,8,5)[lod])
                if lod<2:
                    for j in (0,2,4,6):
                        t=j*math.pi/4
                        m.post("metal",(x+.195*math.cos(t),.60,-.06+.195*math.sin(t)),
                               (x+.183*math.cos(t),.93,-.06+.183*math.sin(t)),.017,sides=4)
                continue
            m.shell(x,.3,[(0,.22,"fieldstone"),(.16,.20,wall),(h,.156,"trim"),
                          (h+.064,.19,roof),(h+.21,.098,"metal" if a==6 else "accent")],
                    (9,7,5)[lod])
            if a==6:
                m.shell(x,.3,[(h+.208,.105,"trim"),(h+.30,.113,"door")],(8,6,5)[lod])
            if a in (8,9):
                m.post("gold" if a==9 else "metal",(x,h+.20,.3),(x+.022,h+.49,.31),.050,0)
    if a==9:
        # Supported, open aureole in the horizontal plane, with a luminous inner rim.
        crown=rise+peak+.28
        for side in (-1,1):
            m.post("gold",(side*.067,rise+peak+.09,.12),(side*.29,crown,.12),.035)
        for j in range((12,8,6)[lod]):
            nring=(12,8,6)[lod]
            p,q=[(math.cos(2*math.pi*k/nring)*.30,crown,.12+math.sin(2*math.pi*k/nring)*.30)
                 for k in (j,j+1)]
            m.post("ascension",p,q,.038,sides=4)
    m.drum(.59,0,-.45,.168,.313)
    if lod<2:
        m.drum(-.52,0,-.49,.123,.218)
        if lod==0:
            for x in (.55,.64):
                m.post("timber",(x,.32,-.514),(x+.023,.329,-.391),.011,sides=5)
    return m


def sheets(package, entries):
    from PIL import Image, ImageDraw
    out=ROOT/entries[0]["preview"]
    out=out.parent
    for age in AGES:
        rows=[e for e in entries if age in e["age_previews"]]
        if not rows:
            continue
        cols=min(5,len(rows))
        sheet=Image.new("RGB",(cols*269,math.ceil(len(rows)/cols)*218),"#efebe2")
        draw=ImageDraw.Draw(sheet)
        for i,e in enumerate(rows):
            x=i%cols*269
            y=i//cols*218
            with Image.open(ROOT/e["age_previews"][age]) as im:
                sheet.paste(im.resize((192,192),Image.Resampling.LANCZOS),(x+5,y))
                sheet.paste(im.resize((64,64),Image.Resampling.LANCZOS),(x+199,y+128))
            draw.text((x+6,y+197),e["building_type"]+" / "+age,fill="#25201f")
        sheet.save(out/("sheet_"+age+".png"))

    families=sorted({e["building_type"] for e in entries})
    lookup={(e["building_type"],age):path for e in entries for age,path in e["age_previews"].items()}
    margin,cell=104,98
    overview=Image.new("RGB",(margin+len(AGES)*cell,30+cell*len(families)),"#efebe2")
    draw=ImageDraw.Draw(overview)
    for column,age in enumerate(AGES):
        draw.text((margin+column*cell+6,9),age.capitalize(),fill="#25201f")
    for row,family in enumerate(families):
        y=30+row*cell
        draw.text((5,y+cell//2-6),family,fill="#25201f")
        for column,age in enumerate(AGES):
            path=lookup.get((family,age))
            if path:
                with Image.open(ROOT/path) as im:
                    overview.paste(im.resize((cell-3,cell-3),Image.Resampling.LANCZOS),
                                   (margin+column*cell,y))
    overview.save(out/(package+"_ages.png"))


def generate(package, build):
    entries=[]
    for spec in package_models(package):
        levels=[build(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=224)
        entries.append(entry)
        print(spec.id,entry["lod_triangles"],flush=True)
    sheets(package,entries)


if __name__=="__main__":
    generate(PACKAGE,council)
