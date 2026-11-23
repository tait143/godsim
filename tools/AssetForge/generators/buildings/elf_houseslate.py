"""Elven industrial conservatories, rain collectors, glass roosts and pearl houses."""
from math import cos, sin, pi
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.elf_housesancient import Dwelling, WHITE
from generators.buildings.elf_housesclassical import stairs, light_in_wall, rear_shutter, stripe_dome

PACKAGE="elf_houseslate"


def shell(m,x,z,rx,rz,eaves,glass,lean=0):
    n=(12,9,6)[m.lod]
    floor=.075
    if m.lod==2:
        rows=[(0,rx,rz,0,"wall"),(eaves*.66,rx*.91,rz*.94,lean*.5,"wall"),
              (eaves,rx,rz,lean,glass)]
    else:
        rows=[(0,rx*.965,rz*.97,0,"wall"),(floor,rx,rz,0,"wall"),
              (floor+.035,rx*.97,rz*.95,0,"trim"),(eaves*.54,rx*.91,rz*.91,lean*.25,"wall"),
              (eaves-.040,rx*.94,rz*.94,lean*.87,glass),
              (eaves,rx,rz,lean,"accent")]
    m.profile((x,z),rows,n,close=False)
    m.portal(x-.025,floor,z-rz-.016,.18,max(.21,min(.32,eaves-.071)),"trim")
    stairs(m,x-.025,z-rz-.015,floor,.205)


def vents(m,x,z,bottom,height,large):
    # A corbelled flower-shaped exhaust cowl, open to the flue inside.
    scale=1 if large else .73
    with m.transform(translate=(x,bottom,z),scale=scale):
        n=(7,6,4)[m.lod]
        h=height/scale
        m.profile((0,0),[(0,.068,.061,0,"brick"),(.77*h,.054,.053,.022,"brick"),
                  (.82*h,.072,.067,.024,"trim"),(.94*h,.082,.076,.026,"metal"),
                  (h,.065,.061,.027,"roof"),(h,.042,.039,.027,"metal"),
                  (.91*h,.038,.036,.024,"door")],n)


def industrial(m,v):
    x=-.071 if v else .008
    z=.055
    rx,rz=(.253,.280) if v else (.329,.278)
    eaves=.45 if v else .32
    shell(m,x,z,rx,rz,eaves,"brick")
    # Glazed factory lantern, scaled down for the household glassblower's roof studio.
    stripe_dome(m,(x,z),eaves,rx*1.14,rz*1.16,.25 if v else .299,12,lean=-.045)
    m.profile((x-.024,z),[(eaves+.208,.145,.067,0,"trim"),(eaves+.269,.129,.058,0,"glass")],
              (8,6,4)[m.lod],close=False)
    m.leaves((x-.024,z),(.157,.083),eaves+.265,.101,3,-.032)
    vents(m,x+rx*.67,z+.085,eaves-.075,.403 if not v else .47,True)
    if v:
        m.profile((.193,.090),[(0,.17,.16,0,"wall"),(.242,.158,.154,0,"brick")],(8,6,5)[m.lod])
        stripe_dome(m,(.193,.09),.242,.185,.165,.119,8)
    elif m.lod<2:
        vents(m,x+rx*.79,z+.016,eaves-.083,.263,False)
    if m.lod<2:
        with m.transform(translate=(x-rx*.65,.188,z-rz*.66),rotate_y=42):
            with m.transform(translate=(0,0,.078),rotate_x=-90,scale=(1,1,1.24)):
                m.cylinder((0,0,0),.061,.084,WHITE,segments=6,material="trim")
        light_in_wall(m,x-rx*.65,.188,z-rz*.66,42)
    if m.lod==0:
        m.sprout(x-rx*.72,0,z-rz*.89,.085)
        rear_shutter(m,x,.221,z+rz*.95)


def rain_roof(m,x,z,y,w,d,rise,sweep):
    # Opposing wings rise from a solid central gutter. The ribs join the shell along its rounded ridge.
    n=(14,10,6)[m.lod]
    def edge(i,scale,dy):
        a=2*pi*i/n
        return (x+w*scale*cos(a),y+dy+rise*(abs(cos(a))**2)*scale+sweep*sin(a)*scale,
                z+d*scale*sin(a))
    upper=[edge(i,1.,.038) for i in range(n)]
    lower=[edge(i,.96,0) for i in range(n)]
    # A continuous glazed clerestory closes the gap under the upswept rain roof.
    # Its top follows the actual ruled underside of the two sloping wings.
    shoulder=[]
    sill=[]
    for px,py,pz in lower:
        shoulder.append((x+(px-x)*.84,y-.012+(py-y+.012)*.84+.009,z+(pz-z)*.84))
        sill.append((x+(px-x)*.75,y-.073,z+(pz-z)*.75))
    for i in range(n):
        j=(i+1)%n
        role="roof" if i%5 else "leaf"
        m.tri(upper[j],upper[i],(x,y+.03,z),WHITE,role)
        m.quad(lower[j],lower[i],upper[i],upper[j],WHITE,"wall")
        m.tri(lower[i],lower[j],(x,y-.012,z),WHITE,"trim")
        m.quad(sill[j],sill[i],shoulder[i],shoulder[j],WHITE,"glass")


def modern(m,v):
    x=-.073 if v else -.005
    z=.055
    rx,rz=(.262,.264) if v else (.32,.291)
    eaves=.442 if v else .337
    shell(m,x,z,rx,rz,eaves,"glass",lean=.024 if v else -.028)
    rain_roof(m,x+.003,z,eaves-.015,rx*1.30,rz*1.15,.24 if v else .205,.026)
    if v:
        shell_x=.20
        m.profile((shell_x,z+.025),[(0,.171,.154,0,"wall"),(.194,.165,.149,.015,"glass")],(8,6,4)[m.lod])
        rain_roof(m,shell_x,z+.025,.19,.217,.173,.105,-.014)
    for s in (-1,1):
        px=x+s*rx*.83
        m.profile((px,z),[(0,.041,.049,0,"wall"),
                    (eaves*.57,.023,.039,s*.024,"wall"),
                    (eaves+(.090 if v else .072),.033,.043,s*.014,"trim")],(6,5,4)[m.lod])
    if m.lod==0:
        # A pair of rain cisterns receives the roofs' runoff.
        for sz in (.09,.188):
            m.profile((x-rx*.92,z+sz),[(0,.046,.041,0,"metal"),(.118,.056,.050,0,"metal"),
                                      (.134,.049,.043,0,"water")],6)
        m.sprout(x+.157,0,z-rz*.915,.071)
    if m.lod<2:
        m.lamp(x+rx*.61,eaves-.100,z-rz*.74,.079)


def information(m,v):
    x=-.055 if v else .006
    z=.048
    rx,rz=(.294,.253) if v else (.250,.250)
    h=.627 if not v else .465
    shell(m,x,z,rx,rz,h,"glass",lean=-.055)
    # Broad decks in variant 1 and a narrow continuous glass spine in variant 0.
    m.leaves((x-.055,z),(rx*1.31,rz*1.24),h-.009,.251 if v else .219,4,.094)
    levels=(h*.54,h*.77) if v else (h*.55,)
    for y in levels:
        m.profile((x-.011,z+.051),[(y,rx*.96,rz*.82,0,"wall"),
                  (y+.020,rx*1.13,rz*.94,-.006,"accent"),
                  (y+.039,rx*1.135,rz*.95,-.009,"wall")],(10,7,5)[m.lod])
    if v:
        m.profile((.20,z+.082),[(0,.123,.119,0,"wall"),(.216,.122,.118,0,"glass"),
                              (.24,.142,.131,0,"accent")],(7,6,4)[m.lod])
    m.profile((x+.093,z+.05),[(h-.01,.024,.027,0,"metal"),(h+.227,.013,.015,-.012,"metal"),
                              (h+.257,.019,.021,-.018,"ascension")],3 if m.lod==2 else 5)
    if m.lod==0:
        m.sprout(x-rx*.80,0,z-rz*.71,.072)
        m.sprout(x+.113,0,z-rz-.046,.046,"flower")


def crown(m,x,z,y,radius,lean):
    n=(16,10,7)[m.lod]
    # A gold-edged garden ring is carried on converging, pale structural stems.
    m.profile((x,z),[(y,radius*.80,radius*.64,0,"gold"),(y+.018,radius,radius*.79,0,"gold"),
                       (y+.041,radius*.99,radius*.78,0,"ascension"),(y+.047,radius*.79,radius*.60,0,"ivory"),
                       (y,radius*.78,radius*.59,0,"gold")],n,close=False)
    for j in range(3 if m.lod<2 else 2):
        a=2*pi*j/(3 if m.lod<2 else 2)
        dx=radius*.85*cos(a)
        dz=radius*.675*sin(a)
        m.profile((x+dx,z+dz),[(y-.173,.022,.023,-lean,"ivory"),
                  (y+.012,.017,.019,0,"gold")],(6,5,4)[m.lod],close=False)


def ascension(m,v):
    x=-.080 if v else .004
    z=.066
    rx,rz=(.268,.249) if v else (.316,.261)
    h=.445 if v else .346
    shell(m,x,z,rx,rz,h,"wall",lean=.029)
    stripe_dome(m,(x+.026,z),h,rx*1.14,rz*1.15,.288,12,lean=.021)
    crown(m,x+.055,z,h+.269,.176 if v else .201,.027)
    if v:
        m.profile((.201,z-.042),[(0,.142,.132,0,"wall"),(.149,.158,.146,0,"wall"),
                                (.226,.141,.133,0,"ascension")],(8,6,4)[m.lod])
        stripe_dome(m,(.201,z-.042),.226,.171,.152,.123,8)
    if m.lod<2:
        m.lamp(x+rx*.61,h-.093,z-rz*.70,.073)
    if m.lod==0:
        m.sprout(x-rx*.80,0,z-rz*.73,.082,"flower")
        rear_shutter(m,x,.206,z+rz*.98)


def build(spec,lod):
    m=Dwelling(spec,lod)
    {"industrial":industrial,"modern":modern,"information":information,"ascension":ascension}[m.age](m,spec.variant)
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        result=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=240)
        print(spec.id,result["lod_triangles"])
