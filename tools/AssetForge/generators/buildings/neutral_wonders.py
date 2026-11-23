"""Four shared monuments: a navigable beacon, public archive, vaulted market and ring gate."""
from math import cos, sin, pi, radians, degrees
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from forge.atlas import NEUTRAL
from generators.buildings.dwarf_housesancient import ClanHouse


class Monument(ClanHouse):
    def color(self, role):
        return self.colors[NEUTRAL.get(role, role)]

    def drum(self, role, x, y, z, r, h, end=1, n=None, stretch=1):
        with self.transform(translate=(x,y,z), scale=(1,1,stretch)):
            self.cylinder((0,0,0),r,h,self.color(role),segments=n or (12,8,5)[self.lod],
                          top_radius=r*end,material=role)

    def globe(self, role, centre, scale, rings=(5,3,2), segments=(12,9,6)):
        with self.transform(translate=centre,scale=scale):
            self.sphere((0,0,0),1,self.color(role),rings=rings[self.lod],segments=segments[self.lod],material=role)

    def sleeve(self, role, start, end, r0, r1, width=1):
        # Elliptical sections at right angles to a bone or spar.
        a,b=np.array(start),np.array(end)
        d=(b-a)/np.linalg.norm(b-a)
        tangent=np.cross(d,(0,0,1))
        if np.linalg.norm(tangent)<.001:
            tangent=np.cross(d,(1,0,0))
        tangent/=np.linalg.norm(tangent)
        other=np.cross(tangent,d)
        n=(10,7,5)[self.lod]
        sections=[[p+r*(tangent*cos(i*2*pi/n)*width + other*sin(i*2*pi/n)) for i in range(n)]
                  for p,r in ((a,r0),(b,r1))]
        lo,hi=sections
        self.face(role,lo)
        self.face(role,hi[::-1])
        for i in range(n):
            j=(i+1)%n
            self.face(role,(lo[j],lo[i],hi[i],hi[j]))

    def arch(self,x,z,base,r,t,depth,role,keystone=None):
        n=(13,9,5)[self.lod]
        for i in range(n):
            a,b=i*pi/n,(i+1)*pi/n
            outer=r+t+(.045 if keystone and i==n//2 else 0)
            front=[(x+rr*cos(theta),base+rr*sin(theta),z-depth*.5)
                   for rr,theta in ((r,a),(r,b),(outer,b),(outer,a))]
            rear=[(px,py,z+depth*.5) for px,py,_ in front]
            mat=keystone if keystone and i==n//2 else role
            self.face(mat,front)
            self.face(mat,rear[::-1])
            for j in (0,2):
                k=(j+1)%4
                self.face(mat,(front[k],front[j],rear[j],rear[k]))
            if i in (0,n-1) or (keystone and i==n//2):
                for j in (1,3):
                    if (i==0 and j==3) or (i==n-1 and j==1) or (keystone and i==n//2):
                        k=(j+1)%4
                        self.face(mat,(front[k],front[j],rear[j],rear[k]))

    def stairs(self,x,edge,w,height,run,base=0):
        n=(6,4,2)[self.lod]
        for i in range(n):
            h=height*(i+1)/n
            self.block("fieldstone",(x,base+h*.5,edge-run+run*(i+.5)/n),(w,h,run/n-.001),
                       taper=.012)

    def column(self,x,z,y,h,r,role="plaster"):
        n=(9,7,5)[self.lod]
        if self.lod<2:
            self.drum("fieldstone",x,y,z,r*1.50,h*.09,.94,n=n)
            self.drum(role,x,y+h*.09,z,r,h*.80,.86,n=n)
            self.drum("ivory",x,y+h*.89,z,r*.94,h*.11,1.40,n=n)
        else:
            self.drum(role,x,y,z,r,h,.91,n=n)

    def wheel(self,cx,cy,cz,outer,inner,depth,face,edge,n):
        last=[[(cx+r*cos(i*2*pi/n),cy+r*sin(i*2*pi/n),cz+dz) for i in range(n)]
              for r,dz in ((outer,-depth*.5),(inner,-depth*.5),(inner,depth*.5),(outer,depth*.5))]
        for k,ring in enumerate(last):
            nxt=last[(k+1)%4]
            for i in range(n):
                j=(i+1)%n
                self.face(face if k in (0,2) else edge,(ring[i],nxt[i],nxt[j],ring[j]))


def ascension(m):
    # The ring bears on two buttressed pylons. A portal threshold passes through its clear aperture.
    m.course("plaster",0,.14,3.16,2.03,0,.175,corner=.31,inset=.042)
    m.course("ivory",0,.14,3.01,1.92,.175,.24,corner=.29)
    m.stairs(0,-.825,1.38,.24,.64)
    for side in (-1,1):
        x=side*1.07
        m.course("metal",x,.20,.70,1.39,.24,.406,corner=.13)
        m.course("ivory",x,.20,.62,1.26,.406,.81,corner=.095,inset=.079)
        m.course("gold",x,.20,.54,1.02,.81,.90,corner=.093)
        for z in (-.12,.54):
            m.sleeve("plaster",(x,.89,z),(side*1.32,1.58,.20+(z-.20)*.50),.224,.130,.78)
        if m.lod<2:
            for z in (-.18,.58):
                m.sleeve("gold",(x-side*.12,.555,z),(side*1.26,1.44,.20+(z-.20)*.55),.038,.059)
    cy,cz=1.73,.20
    n=(48,32,19)[m.lod]
    m.wheel(0,cy,cz,1.46,1.055,.426,"ivory","glass",n)
    m.wheel(0,cy,cz-.218,1.351,1.283,.057,"gold","gold",n)
    m.wheel(0,cy,cz-.213,1.184,1.025,.063,"ascension","glass",n)
    if m.lod<2:
        m.wheel(0,cy,cz+.218,1.340,1.280,.056,"gold","gold",n)
    for i in range((16,12,6)[m.lod]):
        angle=2*pi*i/(16,12,6)[m.lod]
        with m.transform(translate=(1.394*cos(angle),cy+1.394*sin(angle),cz),rotate_z=degrees(angle)-90):
            m.block("gold",(0,0,0),(.126,.165,.470),taper=.20)
            if not m.lod:
                m.block("glass",(0,-.013,-.242),(.072,.071,.026),taper=.13)
    # Bifurcated crown aligns with the centre of the threshold below it.
    for side in (-1,1):
        m.sleeve("gold",(side*.18,3.10,cz),(side*.21,3.42,cz+.017),.101,.025)
    for side in (-1,1):
        x=side*.79
        m.drum("metal",x,.24,-.61,.112,.36,.68)
        m.drum("gold",x,.60,-.61,.100,.054,1.13)
        m.globe("ascension",(x,.701,-.61),(.076,.086,.076))
    # The chord at y=.81 clears even the faceted LOD2 ring along the full width.
    # Approaches on both faces land flush with the bridge and the lower plinth.
    m.stairs(0,-.048,.69,.570,.766,base=.24)
    m.block("plaster",(0,.525,.194),(.69,.570,.484))
    with m.transform(translate=(0,0,.389),rotate_y=180):
        m.stairs(0,-.047,.69,.570,.646,base=.24)
    with m.transform(translate=(0,0,1.100),rotate_y=180):
        m.stairs(0,0,.80,.24,.39)


def statue(m):
    # Each foot stands on its own quay, leaving open water all the way through the harbor.
    for side in (-1,1):
        x=side*.74
        m.course("fieldstone",x,.04,.89,1.69,0,.20,corner=.15)
        m.course("plaster",x,.04,.81,1.59,.20,.34,corner=.11,inset=.032)
        m.course("gold",x,.04,.84,1.61,.34,.397,corner=.12)
        m.course("ivory",x,.04,.77,1.50,.397,.465,corner=.10)
        m.stairs(x,-.712,.54,.465,.67)
        # Bronze foot, planted heel and forward toes. The knees shift inward under the tunic.
        m.globe("gold",(x,.588,-.095),(.185,.139,.302))
        ankle=(x,.650,.024)
        knee=(side*.515,1.152,.010-side*.063)
        hip=(side*.243,1.674,.039)
        m.sleeve("metal",ankle,knee,.124,.173,1.07)
        m.sleeve("gold",knee,hip,.178,.237,1.08)
        if m.lod<2:
            m.drum("gold",x,.685,.013,.148,.095,1.04)
        if not m.lod:
            # Mooring bollards on the landward corners of each quay.
            for z in (-.435,.58):
                m.drum("metal",x+side*.245,.465,z,.061,.136,1.20,n=8)
                m.drum("gold",x+side*.245,.601,z,.079,.026,.92,n=8)
    # Sculpted bell-shaped tunic has alternating pleats and a belted waist.
    n=(18,12,8)[m.lod]
    courses=[(1.43,.543,.302),(1.69,.413,.255),(1.93,.289,.230),(2.12,.341,.274),
             (2.38,.496,.280),(2.48,.364,.204),(2.53,.177,.143)]
    prev=None
    for k,(y,rx,rz) in enumerate(courses):
        ring=[]
        for i in range(n):
            a=2*pi*i/n
            flare=1+.11*(i%2) if k in (0,1) else 1
            ring.append((rx*flare*cos(a),y+(.050*cos(a)-.020*(i%2) if k==0 else 0),
                         .04+rz*flare*sin(a)))
        if prev:
            for i in range(n):
                j=(i+1)%n
                m.face("metal" if k<3 and i%3==0 else "gold",(prev[j],prev[i],ring[i],ring[j]))
        elif k==0:
            m.face("metal",ring)
        prev=ring
    m.face("gold",prev[::-1])
    m.drum("metal",0,1.905,.04,.326,.080,.98,n=n,stretch=.75)
    # The bearing arm lifts a brazier clear of the crown. The free hand gathers the mantle.
    for a,b,c,raised in (((-.394,2.384,.035),(-.641,2.612,.045),(-.691,2.926,-.01),True),
                        ((.395,2.382,.035),(.640,2.082,.100),(.437,1.999,-.174),False)):
        m.globe("gold",a,(.146,.155,.150))
        m.sleeve("gold",a,b,.154,.102)
        m.sleeve("gold",b,c,.115,.074)
        m.globe("gold",c,(.088,.105,.081))
    m.drum("gold",0,2.49,.035,.145,.169,.90,stretch=.84)
    m.globe("gold",(0,2.79,.018),(.207,.257,.196))
    m.drum("metal",0,2.864,.03,.209,.091,.96,stretch=.92)
    if m.lod<2:
        for s in (-1,1):
            with m.transform(translate=(s*.081,2.804,-.146),rotate_z=-s*8):
                m.block("metal",(0,0,0),(.112,.021,.032))
        m.globe("gold",(0,2.761,-.161),(.038,.067,.057),rings=(3,2,2))
    for i in range((7,5,3)[m.lod]):
        a=pi*(i+.35)/(7,5,3)[m.lod]
        m.sleeve("gold",(.18*cos(a),2.925,.034+.12*sin(a)),(.273*cos(a),3.08+.044*sin(a),.035+.16*sin(a)),.046,.006)
    # A wide dark handle separates the torch silhouette from the raised hand.
    m.drum("metal",-.691,2.844,-.01,.050,.313,1.01)
    m.drum("metal",-.691,3.157,-.01,.091,.135,2.12)
    m.drum("gold",-.691,3.292,-.01,.202,.049,1.04)
    m.globe("forge",(-.69,3.423,-.01),(.142,.192,.133))
    m.sleeve("emissive",(-.69,3.423,-.01),(-.762,3.684,.017),.104,.008)
    # The sea-green mantle sweeps from the free shoulder to the opposite hip.
    # Its narrow folded ribbon touches the skin along the collarbone and waist.
    edge=[((.417,2.416,-.028),(.238,2.487,-.144)),
          ((.288,2.330,-.226),(.093,2.407,-.249)),
          ((.056,2.184,-.246),(-.115,2.254,-.240)),
          ((-.126,2.023,-.179),(-.267,2.105,-.158)),
          ((-.250,1.898,-.107),(-.327,1.977,-.067))]
    if m.lod==2:
        edge=[edge[i] for i in (0,2,4)]
    for first,second in zip(edge,edge[1:]):
        fold=[tuple((np.asarray(a)+b)*.5+[0,0,-.035]) for a,b in (first,second)]
        m.face("turquoise",(first[0],second[0],fold[1],fold[0]))
        m.face("glass",(fold[0],fold[1],second[1],first[1]))
    if m.lod<2:
        m.globe("gold",(.280,2.416,-.165),(.069,.069,.050),rings=(3,2,2))
        # A narrow bronze plaque carries the harbor keeper's crest on the sea-facing side.
        m.block("metal",(1.142,.255,-.091),(.025,.132,.258),taper=.035)
        with m.transform(translate=(1.161,.256,-.091),rotate_x=45):
            m.block("gold",(0,0,0),(.018,.064,.064),taper=.12)


def lantern(m,x,y,z,size):
    m.drum("metal",x,y,z,size*.64,size*.36,1.58,n=(8,6,5)[m.lod])
    m.globe("emissive",(x,y+size*.96,z),(size*.80,size*.77,size*.80),rings=(3,2,2))
    m.drum("gold",x,y+size*1.57,z,size*.98,size*.29,.45,n=(8,6,5)[m.lod])


def library(m):
    m.course("fieldstone",0,.16,2.75,2.10,0,.147,corner=.14)
    m.course("ivory",0,.16,2.70,2.04,.147,.215,corner=.14)
    m.stairs(0,-.86,1.08,.215,.42)
    # Paired stacks wrap a round daylit reading room; the main entrance opens through the portico.
    for s in (-1,1):
        x=s*.928
        m.course("plaster",x,.23,.71,1.61,.215,.912,corner=.081)
        m.course("ivory",x,.23,.758,1.66,.912,.993,corner=.085)
        m.hip_roof((x,.993,.23),.735,1.63,.301,m.color("roof_tile"),material="roof_tile",overhang=.031)
        m.light(x,.611,-.582,.251,.276,shutters=False)
        if m.lod<2:
            for pz in (-.268,.245,.758):
                with m.transform(translate=(x+s*.359,.009,pz),rotate_y=-90*s):
                    m.light(0,.612,-.009,.206,.301,shutters=False)
    cy,cz=.215,.338
    m.drum("plaster",0,cy,cz,.765,1.038,1)
    m.drum("ivory",0,1.253,cz,.795,.099,.98)
    m.drum("glass",0,1.352,cz,.679,.258,.98)
    n=(12,8,5)[m.lod]
    for i in range(n):
        a=2*pi*i/n
        m.column(.666*cos(a),cz+.666*sin(a),1.348,.266,.043,"ivory")
    m.drum("ivory",0,1.610,cz,.731,.081,1.04)
    m.dome((0,1.691,cz),.768,m.color("roof_tile"),rings=(5,3,2)[m.lod],segments=(20,14,8)[m.lod],
           squash=.81,material="roof_tile")
    m.drum("gold",0,2.29,cz,.082,.076,.72)
    m.globe("gold",(0,2.384,cz),(.046,.061,.046),rings=(3,2,2))
    m.course("plaster",0,-.264,1.14,.854,.215,.925,corner=.04)
    m.gate(0,-.701,.215,.352,.506,"ivory")
    for x in (-.512,-.262,.262,.512):
        if m.lod<2 or abs(x)>.4:
            m.column(x,-.790,.215,.62,.063)
    m.block("ivory",(0,.892,-.631),(1.27,.115,.52))
    with m.transform(translate=(0,.949,-.634),rotate_y=90):
        m.gable_roof((0,0,0),.52,1.23,.286,m.color("roof_tile"),material="roof_tile",overhang=.025)
    # The archive's oversized sculpted codex stands on a bronze lectern above the portico.
    m.block("metal",(0,1.149,-.83),(.40,.39,.25),taper=.21)
    with m.transform(translate=(0,1.386,-.907),rotate_x=-18):
        for s in (-1,1):
            with m.transform(rotate_y=-s*24):
                m.block("glass",(s*.24,0,0),(.440,.405,.062))
                m.block("ivory",(s*.237,.010,-.050),(.396,.355,.061),taper=.04)
                for y in (-.052,.036,.116) if m.lod<2 else (.06,):
                    m.block("metal",(s*.228,y,-.084),(.229,.016,.011))
        m.block("gold",(0,0,-.016),(.071,.424,.091))
    if m.lod<2:
        for x in (-.962,.968):
            lantern(m,x,.226,-.692,.071)
    if not m.lod:
        # A reader's bench and a closed scroll chest flank the descent.
        m.block("timber",(-.754,.334,-.674),(.322,.049,.150))
        for x in (-.856,-.649):
            m.block("fieldstone",(x,.268,-.674),(.059,.113,.081))
        m.block("timber",(.749,.291,-.670),(.273,.152,.144),taper=.07)
        m.block("gold",(.748,.380,-.670),(.294,.027,.159))


def arcade(m,x,z,base,r,t,depth,h):
    m.arch(x,z,base+h,r,t,depth,"plaster","gold" if m.lod<2 else None)
    for s in (-1,1):
        px=x+s*(r+t*.5)
        m.block("plaster",(px,base+h*.5,z),(t,h,depth))
        if m.lod<2:
            m.block("ivory",(px,base+h-.022,z),(t*1.24,.041,depth+.022))


def bazaar(m):
    # A transverse covered souk and two projecting vaulted arcades enclose the lamp court.
    m.course("fieldstone",0,.055,2.67,2.27,0,.115,corner=.16)
    m.stairs(0,-1.08,.69,.115,.25)
    for s in (-1,1):
        x=s*.872
        m.course("brick",x,.196,.759,1.72,.115,.746,corner=.054)
        m.vault(x,.196,.805,1.77,.746,.316,"roof_tile","plaster",(12,8,5)[m.lod])
        arcade(m,x,-.810,.115,.252,.089,.234,.319)
        m.block("door",(x,.378,-.697),(.513,.525,.045))
        for j in range(3 if not m.lod else 2):
            pz=-.311+j*.446
            with m.transform(translate=(x+s*.389,0,pz),rotate_y=-s*90):
                m.light(0,.441,-.003,.197,.244,shutters=False)
        if m.lod<2:
            for z in (-.479,.51):
                m.vault(x,z,.824,.067,.757,.320,"gold","gold",(12,8)[m.lod])
    m.course("plaster",0,.601,1.15,.824,.115,.97,corner=.075)
    m.drum("ivory",0, .966,.600,.537,.104,1.03,stretch=.86)
    m.drum("plaster",0,1.070,.600,.453,.222,.96,stretch=.88)
    with m.transform(translate=(0,1.292,.600),scale=(1,1,.88)):
        m.dome((0,0,0),.485,m.color("turquoise"),rings=(5,3,2)[m.lod],segments=(18,12,8)[m.lod],
               squash=1.02,material="turquoise")
    m.drum("gold",0,1.766,.600,.077,.095,.59)
    m.globe("gold",(0,1.883,.600),(.044,.067,.044),rings=(3,2,2))
    arcade(m,0,.097,.115,.279,.108,.235,.391)
    m.block("door",(0,.439,.176),(.566,.646,.041))
    # Two striped sailcloth stalls with sloping canopies, brass bowls and hanging lamps.
    for s in (-1,1):
        x=s*.367
        with m.transform(translate=(x,0,-.934),rotate_y=-s*4):
            w=.431
            for j in (0,1):
                xx=(j-.5)*w*.89
                m.block("timber",(xx,.206,-.219),(.041,.411,.046))
                m.block("timber",(xx,.288,.079),(.040,.576,.044))
            m.block("timber",(0,.326,-.123),(w,.075,.260))
            count=(5,3,3)[m.lod]
            for j in range(count):
                xx=-w*.5+w*(j+.5)/count
                with m.transform(translate=(xx,.519,-.081),rotate_x=-18):
                    m.block("cloth" if j%2==0 else "ivory",(0,0,0),(w/count-.001,.032,.413))
                    if m.lod==0:
                        m.block("cloth" if j%2==0 else "ivory",(0,-.029,-.202),(w/count-.005,.058,.019),taper=.11)
            m.block("timber",(0,.426,-.245),(w*1.02,.060,.055))
            if m.lod<2:
                for j in (-1,1):
                    m.drum("gold",j*.122,.365,-.113,.067,.045,1.30)
                    m.globe("flower" if j<0 else "soil",(j*.122,.414,-.113),(.071,.042,.064),rings=(3,2,2))
    if m.lod<2:
        for s in (-1,1):
            x=s*.403
            m.block("timber",(x,.408,-.228),(.051,.587,.055))
            lantern(m,x,.702,-.228,.077)
    if m.lod==0:
        # Carpet runners follow the walking line across the court.
        for z in (-.735,-.37):
            m.block("cloth",(0,.120,z),(.344,.008,.339))
            for end in (-1,1):
                m.block("gold",(0,.126,z+end*.137),(.302,.004,.023))


def build(spec,lod):
    m=Monument(spec,lod)
    {"AscensionGate":ascension,"Colossus":statue,"GrandBazaar":bazaar,"GreatLibrary":library}[spec.building_type](m)
    return m


if __name__=="__main__":
    for spec in package_models("neutral_wonders"):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),"neutral_wonders",lods=levels[1:],preview_size=288)
        print(spec.id,entry["lod_triangles"],flush=True)
