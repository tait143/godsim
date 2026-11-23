"""Early Elven dwellings, grown around woven reed shells, copper seed pods and birch undercrofts."""
from math import cos, sin, pi
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish

PACKAGE = "elf_housesancient"
WHITE = (1.,1.,1.)


class Dwelling(MeshBuilder):
    def __init__(self, model, lod):
        super().__init__(model.id)
        self.lod = lod
        self.age = model.ages[0]

    def profile(self, center, rings, n, close=True):
        """Sweep hand-shaped elliptical courses. Ring tuples contain y, radii, lean and the side material."""
        last = None
        for y,rx,rz,dx,role in rings:
            ring = [(center[0]+dx+rx*cos(2*pi*(i+.5)/n-pi/2),y,
                     center[1]+rz*sin(2*pi*(i+.5)/n-pi/2)) for i in range(n)]
            if last is not None:
                for i in range(n):
                    j=(i+1)%n
                    self.quad(last[j],last[i],ring[i],ring[j],WHITE,role)
            last = ring
        if close:
            self.poly(last[::-1],WHITE,rings[-1][-1])

    def leaves(self, center, radii, y, height, lobes, lean, pointed=False):
        # Each broad canopy leaf builds part of the weatherproof shell. The underside joins
        # the same boundary as the top; individual veins are ridges in the surface itself.
        samples = 4 if self.lod==0 else 2
        n=lobes*samples
        if self.age=="bronze":
            courses=[(1,0),(.74,.18),(.59,.64),(.30,.90)] if self.lod==0 else [(1,0),(.58,.64)]
        elif self.age=="iron":
            courses=[(1,0),(.65,.15),(.43,.53)] if self.lod==0 else [(1,0),(.49,.44)]
        else:
            courses = [(1,0),(.78,.31),(.43,.75)] if self.lod==0 else [(1,0),(.53,.59)]
        if self.lod==2:
            courses=[(1,0)]
        rows=[]
        for r,t in courses:
            ring=[]
            for i in range(n):
                a=2*pi*i/n-pi/2
                middle=(1+cos(lobes*a+lobes*pi/2))/2
                reach=r*(1-.18*(1-middle)*(1-t))
                lift=(.19 if self.age=="bronze" else .105)*height*(1-middle)*(1-t)
                ring.append((center[0]+lean*t+radii[0]*reach*cos(a),y+height*t+lift,
                             center[1]+radii[1]*reach*sin(a)))
            rows.append(ring)
        apex=(center[0]+lean,y+height,center[1]+(.035 if pointed else 0))
        soffit=[(x,y0-.030,z) for x,y0,z in rows[0]]
        for i in range(n):
            j=(i+1)%n
            leaf=(i+samples//2)//samples
            role="roof" if leaf%3 else "leaf"
            self.quad(soffit[j],soffit[i],rows[0][i],rows[0][j],WHITE,"trim")
            for lower,upper in zip(rows,rows[1:]):
                # Each slightly cupped four-sided leaf uses explicit planar facets.
                self.tri(lower[j],lower[i],upper[i],WHITE,role)
                self.tri(lower[j],upper[i],upper[j],WHITE,role)
            self.tri(rows[-1][j],rows[-1][i],apex,WHITE,role)
            self.tri(soffit[i],soffit[j],(center[0],y-.028,center[1]),WHITE,"timber")

    def portal(self,x,y,z,width,height,role):
        # Deep frame around an inset, pointed door. A chamfer at the crown catches the light.
        shape=[(-.5,0),(-.48,.57),(-.34,.80),(0,1),(.34,.80),(.48,.57),(.5,0)]
        if self.lod==2:
            shape=[shape[i] for i in (0,1,3,5,6)]
        outer=[(x+u*width,y+v*height,z) for u,v in shape]
        inner=[(x+u*width*.69,y+v*height*.82,z) for u,v in shape]
        depth=.066
        for i in range(len(shape)-1):
            j=i+1
            self.quad(outer[i],outer[j],inner[j],inner[i],WHITE,role)
            self.quad(outer[j],outer[i],(outer[i][0],outer[i][1],z+depth),
                      (outer[j][0],outer[j][1],z+depth),WHITE,role)
            if self.lod<2:
                self.quad(inner[i],inner[j],(inner[j][0],inner[j][1],z+.018),
                          (inner[i][0],inner[i][1],z+.018),WHITE,"trim")
        leaf = [(a,b,z+.018) for a,b,_ in inner] if self.lod<2 else inner
        self.poly(leaf,WHITE,"door")
        if self.lod==0:
            self.sphere((x+width*.18,y+height*.37,z+.012),.011,WHITE,rings=2,segments=5,material="gold")

    def sprout(self,x,y,z,size,role="leaf"):
        self.cylinder((x,y,z),size*.45,size*.65,WHITE,segments=6,top_radius=size*.60,material="hide")
        for i in range(3):
            with self.transform(translate=(x,y+size*.61,z),rotate_y=120*i):
                self.dome((size*.24,size*.05,0),size*.33,WHITE,rings=1,segments=5,
                          squash=1.45 if i==0 else .90,material=role)

    def lamp(self,x,y,z,size):
        with self.transform(translate=(x,y,z),scale=size):
            self.profile((0,0),[(0,.40,.40,0,"accent"),(.28,.75,.75,0,"accent"),
                                 (.87,.58,.58,0,"emissive"),(1.14,.23,.23,.04,"accent")],
                         6 if self.lod==0 else 4)
            self.cylinder((.04,1.14,0),.12,.34,WHITE,segments=4,material="trim")


def home(m,plan):
    # x, z, shell footprint, floor, shoulder, canopy height, canopy lobes, sweep, side bay.
    x,z,rx,rz,floor,eaves,rise,lobes,lean,bay=plan
    age=m.age
    n=(12,8,6)[m.lod]
    root_role="timber" if age=="dawn" else "wall"
    foot="fieldstone" if age=="iron" else ("brick" if age=="bronze" else "timber")
    # A continuous shell avoids internal coplanar caps at joints between structural courses.
    rings=[(0,rx*.94,rz*.94,0,foot),(floor,rx,rz,0,foot)]
    if m.lod<2:
        rings.extend([(floor+.043,rx*.94,rz*.95,0,"trim"),
                      (floor+(eaves-floor)*.52,rx*.97,rz*.91,lean*.12,"wall")])
    rings.append((eaves+.025,rx*.74,rz*.72,lean*.15,"wall"))
    m.profile((x,z),rings,n)
    m.leaves((x+lean*.12,z),(rx*1.23,rz*1.22),eaves,rise,lobes,lean,age=="iron")
    # A second envelope gives the twin-pod and the Iron pantry their asymmetric roof silhouette.
    if bay:
        sx=x+.273
        sy=eaves*.57
        sr=rx*.51
        m.profile((sx,z+.019),[(0,sr*.94,sr*.86,0,foot),(sy,sr,sr*.87,0,"wall")],(7,6,5)[m.lod])
        m.leaves((sx,z+.019),(sr*1.17,sr*1.12),sy,rise*.68,3,.067)
    door_x=x-.042
    front=z-rz*.98
    m.portal(door_x,floor,front-.026,.195,eaves-floor-.021,root_role if age=="dawn" else "accent")
    count=(3,2,1)[m.lod]
    for i in range(count):
        step=floor*(i+1)/count
        sfront=front-.154+i*.13/count
        m.box((door_x,step/2,sfront+.064/count),(.215+.037*(1-i/count),step,.129/count),
              WHITE,material=foot,taper=.04)
    if m.lod<2:
        # Bowed birch uprights flare into the eave on both sides of the door.
        for sign in (-1,1):
            px=x+sign*rx*.53
            pz=z-rz*.79
            m.profile((px,pz),[(floor,.044,.035,0,"trim"),
                    (floor+.10,.030,.028,sign*.02,"trim"),
                    (eaves-.018,.030,.033,-sign*.018,root_role)],(6,4)[m.lod])
        lh=eaves-.030
        # A lantern hangs from the roof, on its own slim descending stalk.
        lx,lz=x+rx*.75,z-rz*.66
        m.lamp(lx,lh-.121,lz,.081)
        m.profile((lx,lz),[(lh-.002,.013,.014,0,"trim"),
                            (eaves+.068,.017,.020,-.031,"trim")],4)
        # A recessed oval casement faces the camera-side flank and gives scale to the shell.
        if not bay:
            with m.transform(translate=(x+.834*rx,floor+(eaves-floor)*.51,z-.44*rz),rotate_y=-61):
                m.sphere((0,0,-.002),.058,WHITE,rings=2,segments=6,squash=1.25,material="trim")
                m.sphere((0,0,-.024),.046,WHITE,rings=2,segments=6,squash=1.17,material="emissive")
    if m.lod==0:
        m.sprout(x-rx*.75,0,front-.024,.066,"flower" if age=="bronze" else "leaf")
        if age!="iron":
            m.cylinder((x+.129,0,front-.026),.047,.068,WHITE,segments=7,top_radius=.057,material="hide")
            m.dome((x+.129,.068,front-.026),.046,WHITE,rings=1,segments=6,squash=.55,material="leaf")
    if age=="iron":
        sides=(6,5,4)[m.lod]
        with m.transform(translate=(x+rx*.46,eaves+.014,z+.12),rotate_z=-6):
            m.cylinder((0,0,0),.048,rise*.91,WHITE,segments=sides,top_radius=.037,material="fieldstone")
            m.cone((0,rise*.91,0),.064,.065,WHITE,segments=sides,material="roof")


PLANS={
    ("dawn",0):(.008,.032,.346,.301,.073,.350,.315,5,-.038,False),
    ("dawn",1):(-.094,.034,.281,.286,.104,.416,.293,4,-.079,True),
    ("bronze",0):(.015,.037,.298,.292,.128,.467,.326,6,.065,False),
    ("bronze",1):(-.082,.035,.278,.268,.092,.375,.402,5,.085,True),
    ("iron",0):(.008,.048,.326,.306,.149,.394,.464,4,-.052,False),
    ("iron",1):(-.075,.037,.276,.278,.155,.511,.401,6,-.064,True),
}


def build(spec,lod):
    m=Dwelling(spec,lod)
    home(m,PLANS[(m.age,spec.variant)])
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        result=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=240)
        print(spec.id,result["lod_triangles"])
