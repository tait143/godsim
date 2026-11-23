"""The loggia, moon gate and lantern lofts of Elven Classical through Renaissance households."""
from math import cos, sin, pi
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.elf_housesancient import Dwelling, WHITE

PACKAGE="elf_housesclassical"


def stairs(m,x,front,h,width):
    n=(3,2,1)[m.lod]
    for i in range(n):
        top=h*(i+1)/n
        tread=.112/n
        m.box((x,.5*top,front-.110+tread*(.5+i)),(width+.035*(1-i/n),top,tread-.001),
              WHITE,material="wall",taper=.025)


def loggia(m,x,z,base,wall_height,radius):
    m.profile((x,z),[(base,radius,radius*.79,0,"wall"),(base+.029,radius*1.03,radius*.815,0,"accent")],
              (9,7,5)[m.lod])
    for side in (-1,1):
        for front in ((-1,1) if m.lod < 2 else (-1,)):
            px=x+side*radius*.52
            pz=z+front*radius*.36
            shaft=[(base+.024,.039,.037,0,"wall"),
                   (base+wall_height+.047,.034,.031,-side*.021,"wall")]
            if m.lod<2:
                shaft.insert(1,(base+wall_height*.46,.028,.026,side*.012,"wall"))
            m.profile((px,pz),shaft,(6,5,4)[m.lod],close=False)
    m.profile((x,z),[(base+wall_height,radius*.85,radius*.62,0,"trim"),
                       (base+wall_height+.035,radius*.98,radius*.71,0,"accent")],(9,7,5)[m.lod])


def stripe_dome(m,center,y,rx,rz,h,n,lean=0):
    # Porcelain and green-glazed copper lobes meet in a continuous curved roof.
    n=(n,8,6)[m.lod]
    profile=[(0,1),(.23,.98),(.57,.83),(.82,.53)] if m.lod==0 else [(0,1),(.59,.80)]
    rings=[]
    for lift,scale in profile:
        ring=[]
        for j in range(n):
            a=2*pi*j/n-pi/2
            flute=.94 if j%2 and m.lod==0 else 1
            ring.append((center[0]+rx*scale*flute*cos(a)+lean*lift,y+h*lift,
                         center[1]+rz*scale*flute*sin(a)))
        rings.append(ring)
    for i in range(n):
        j=(i+1)%n
        role="roof" if i%4 not in (0,) else "leaf"
        if m.age=="ascension":
            role="ivory" if i%4 else "gold"
        for lo,hi in zip(rings,rings[1:]):
            m.tri(lo[j],lo[i],hi[i],WHITE,role)
            m.tri(lo[j],hi[i],hi[j],WHITE,role)
        m.tri(rings[-1][j],rings[-1][i],(center[0]+lean,y+h,center[1]),WHITE,role)
        m.tri(rings[0][i],rings[0][j],(center[0],y-.006,center[1]),WHITE,"trim")


def light_in_wall(m,x,y,z,turn=0):
    with m.transform(translate=(x,y,z),rotate_y=turn):
        n=(8,6,4)[m.lod]
        with m.transform(rotate_x=-90,scale=(1.,1.,1.24)):
            m.cylinder((0,0,0),.075,.038,WHITE,segments=n,top_radius=.066,material="trim")
            m.cylinder((0,.037,0),.050,.010,WHITE,segments=n,material="emissive")
        if m.lod==0:
            m.box((0,0,-.049),(.011,.100,.010),WHITE,material="accent")


def rear_shutter(m,x,y,z):
    # A split almond-shaped shutter faces the garden behind each home.
    profile=[(-.062,0),(-.041,-.043),(0,-.059),(.062,0),(.040,.043),(0,.059)]
    rim=[(x+a,y+b,z) for a,b in profile]
    m.poly(rim,WHITE,"trim")
    # Set the tapered reveal into the rounded wall, including at its widest offsets.
    for i,p in enumerate(rim):
        m.tri(rim[(i+1)%len(rim)],p,(x,y,z-.110),WHITE,"trim")
    m.tri((x-.045,y,z+.003),(x,y-.041,z+.008),(x,y+.043,z+.008),WHITE,"banner")
    m.tri((x+.045,y,z+.003),(x+.005,y+.040,z+.008),(x+.005,y-.040,z+.008),WHITE,"emissive")


def classical(m,v):
    x=-.091 if v else -.026
    z=.109 if v else .082
    rx,rz=(.264,.233) if v else (.334,.277)
    eaves=.49 if v else .362
    n=(12,8,6)[m.lod]
    m.profile((x,z),[(0,rx,rz,0,"wall"),(.078,rx*1.025,rz*1.025,0,"wall"),
                     (eaves-.026,rx*.90,rz*.91,0,"wall"),(eaves,rx,rz,0,"accent")],n,close=False)
    stripe_dome(m,(x,z),eaves,rx*1.11,rz*1.12,.31 if v else .26,10 if v else 12,lean=.055*v)
    m.portal(x,.081,z-rz*.988-.02,.176,eaves-.103,"trim")
    # The Classical loggia is on the approach axis and actually shelters the front door.
    porch_x=x+.012
    porch_z=z-rz-.076
    loggia(m,porch_x,porch_z,.076,.209,.175)
    stripe_dome(m,(porch_x,porch_z),.320,.192,.139,.100,8)
    m.profile((porch_x,porch_z),[(0,.154,.123,0,"wall"),(.076,.175,.137,0,"wall")],(8,6,4)[m.lod])
    stairs(m,porch_x,porch_z-.118,.070,.194)
    if v:
        m.profile((.212,.14),[(0,.138,.14,0,"wall"),(.253,.129,.12,0,"wall")],(8,6,4)[m.lod])
        stripe_dome(m,(.212,.14),.253,.171,.148,.188,8)
    if m.lod==0:
        light_in_wall(m,x+rx*.85,eaves*.51,z-rz*.44,-62)
        m.sprout(x-rx*.81,0,z-rz*.73,.076)
        rear_shutter(m,x,eaves*.58,z+rz*.956)
    if m.lod<2:
        m.lamp(porch_x+.101,.23,porch_z-.055,.063)


def medieval(m,v):
    x=-.091 if v else -.002
    z=.053
    rx,rz=(.238,.247) if v else (.322,.296)
    base=.106
    eaves=.47 if v else .397
    m.profile((x,z),[(0,rx*.96,rz*.96,0,"fieldstone"),(base,rx,rz,0,"fieldstone"),
                       (eaves-.031,rx*.83,rz*.85,0,"wall"),(eaves,rx*.98,rz*.97,0,"trim")],
              (12,8,6)[m.lod])
    m.leaves((x,z),(rx*1.22,rz*1.20),eaves,.459 if v else .444,4,-.055,True)
    m.portal(x,base,z-rz-.009,.199,eaves-base+.031,"ivory")
    stairs(m,x,z-rz-.010,base,.203)
    if v:
        m.profile((.165,.134),[(0,.189,.168,0,"fieldstone"),(.27,.156,.147,0,"wall"),
                                (.315,.172,.155,.012,"trim")],(9,7,5)[m.lod])
        m.leaves((.172,.134),(.226,.197),.315,.301,3,.059)
    for s in (-1,1):
        # Stone roots rise out of the actual footings and clasp the eave.
        m.profile((x+s*rx*.73,z-rz*.30),[(0,.063,.070,s*.065,"fieldstone"),
                     (.095,.055,.058,s*.064,"wall"),(.289,.038,.038,s*.016,"wall"),
                     (eaves+.020,.036,.039,0,"trim")],(6,5,4)[m.lod],close=False)
    if m.lod<2:
        light_in_wall(m,x+rx*.51,eaves*.68,z-rz*.77,-28)
    if m.lod==0:
        m.sprout(x-.187,0,z-rz-.040,.073,"flower")
        m.lamp(x+rx*.79,eaves-.101,z-rz*.34,.070)
        rear_shutter(m,x,eaves*.58,z+rz*.959)


def renaissance(m,v):
    x=-.089 if v else -.035
    z=.062
    rx,rz=(.251,.241) if v else (.334,.271)
    cornice=.435 if v else .333
    m.profile((x,z),[(0,rx*.98,rz*.98,0,"wall"),(.08,rx,rz,0,"trim"),
                       (cornice-.054,rx*.94,rz*.93,0,"wall"),
                       (cornice-.035,rx*1.052,rz*1.05,0,"accent"),
                       (cornice,rx*1.059,rz*1.06,0,"wall")],(12,8,6)[m.lod])
    stripe_dome(m,(x,z),cornice,rx*1.12,rz*1.14,.267,12)
    # An inhabited lantern at the crest doubles as the workshop's north light.
    top=cornice+.243
    m.profile((x,z),[(top,.087,.078,0,"accent"),(top+.078,.074,.066,0,"glass"),
                     (top+.085,.091,.080,0,"accent")],(8,6,4)[m.lod])
    m.cone((x,top+.085,z),.10,.063,WHITE,segments=(8,6,4)[m.lod],material="roof")
    m.portal(x-.031,.071,z-rz-.018,.18,cornice-.070,"accent")
    stairs(m,x-.03,z-rz-.015,.070,.193)
    if v:
        loggia(m,.165,-.023,.047,.203,.191)
        stripe_dome(m,(.165,-.023),.285,.216,.158,.129,8,lean=.04)
        m.profile((.165,-.023),[(0,.167,.131,0,"wall"),(.047,.187,.144,0,"wall")],(8,6,4)[m.lod])
    if m.lod<2:
        light_in_wall(m,x+rx*.84,cornice*.56,z-rz*.46,-61)
    if m.lod==0:
        m.sprout(x-rx*.79,0,z-rz*.76,.079,"flower")
        m.sprout(x+rx*.15,0,z-rz-.041,.058)
        rear_shutter(m,x,cornice*.54,z+rz*.96)


def build(spec,lod):
    m=Dwelling(spec,lod)
    {"classical":classical,"medieval":medieval,"renaissance":renaissance}[m.age](m,spec.variant)
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        result=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=240)
        print(spec.id,result["lod_triangles"])
