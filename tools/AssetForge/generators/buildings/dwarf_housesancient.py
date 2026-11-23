"""Dwarven clan hearths: turf-backed shelters, copper-capped courts and Iron Age vaulted lodges."""
from math import sin, cos, pi
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House

PACKAGE="dwarf_housesancient"


class ClanHouse(House):
    def color(self,role):
        return self.colors.get(role) or self.colors["dwarf/"+role]

    def vault(self,x,z,width,depth,base,height,roof,front_role,segments):
        n=segments
        pts=[(x+width*.5*cos(pi-i*pi/n),base+height*sin(pi*i/n),z-depth*.5) for i in range(n+1)]
        back=[(a,b,z+depth*.5) for a,b,_ in pts]
        self.face(front_role,pts)
        self.face(front_role,back[::-1])
        for i in range(n):
            self.face(roof,(pts[i+1],pts[i],back[i],back[i+1]))
        self.face("trim",(pts[0],pts[-1],back[-1],back[0]))

    def gate(self,x,z,y,w,h,face="trim",reveal=0):
        # Cut the face of the door as a five-sided lintelled opening and carry the cap on two jambs.
        points=[(x-w*.5,y,z),(x-w*.5,y+h*.74,z),(x-w*.28,y+h,z),
                (x+w*.28,y+h,z),(x+w*.5,y+h*.74,z),(x+w*.5,y,z)]
        self.face("door",points)
        if reveal:
            for i,start in enumerate(points):
                end=points[(i+1)%len(points)]
                self.face(face,(end,start,(start[0],start[1],z+reveal),(end[0],end[1],z+reveal)))
        if self.lod==2:
            self.block(face,(x,y+h*.96,z),(.091+w,h*.18,.050),taper=.12)
            return
        for side in (-1,1):
            self.block(face,(x+side*(w*.5+.015),y+h*.41,z-.005),(.043,h*.82,.053))
        self.block(face,(x,y+h*.92,z-.004),(w+.088,h*.22,.065),taper=.13)
        if self.lod<2:
            self.block("accent",(x,y+h*1.071,z-.005),(w*.75,h*.090,.071))
        if self.lod==0:
            self.block("metal",(x-w*.31,y+h*.48,z-.014),(.031,.024,.023))

    def recessed_gate(self,x,z,y,w,h,face="trim"):
        self.gate(x,z,y,w,h,face,reveal=.11)

    def lamp_face(self,x,y,z,r=.043,depth=.043):
        # A ruby of amber, seated in a chamfered copper socket.
        with self.transform(translate=(x,y,z),rotate_z=45):
            self.box((0,0,(depth-.043)*.5),(r*1.77,r*1.77,depth),self.color("trim"),material="trim",taper=.09)
            self.box((0,0,-.025),(r*1.20,r*1.20,.019),self.color("emissive"),material="emissive",taper=.16)

    def pot(self,x,z,r,h):
        n=(7,5,4)[self.lod]
        self.cylinder((x,0,z),r*.75,h*.67,self.color("hide"),segments=n,top_radius=r,material="hide")
        self.cylinder((x,h*.67,z),r,h*.33,self.color("metal"),segments=n,top_radius=r*.85,material="metal")
        self.dome((x,h,z),r*.77,self.color("soil"),rings=1,segments=n,squash=.42,material="soil")

    def stair(self,x,z,w,top):
        n=(3,2,1)[self.lod]
        for i in range(n):
            h=top*(i+1)/n
            self.block("fieldstone",(x,h*.5,z-.13+.13*(i+.5)/n),(w+.021*(1-i/n),h,.13/n-.001),taper=.03)

    def flue_pot(self,x,z,base,h,w,metal):
        with self.transform(translate=(x,base,z),rotate_z=-4.4):
            self.block("wall",(0,h*.43,0),(w,h*.86,w*.83),taper=.14)
            self.block(metal,(0,h*.86,0),(w*1.15,h*.14,w*1.03),taper=.04)
            if self.lod<2:
                self.block("door",(0,h*.935,0),(w*.64,h*.012,w*.56))
            if self.lod==0:
                self.block("trim",(0,h*.64,0),(w*1.01,h*.09,w*.88))


def frame(m,x,z,w,d,h,front,feet=2,window_side=1):
    # Isolated battered buttresses seal each exposed side. They meet the wall on an inclined plane.
    for side in (-1,1):
        for row in range(feet if m.lod<2 else 1):
            sz=z-d*.23 + row*d*.57
            px=x+side*(w*.50+.027)
            corners=((-1,-1),(1,-1),(1,1),(-1,1))
            base=[(px+a*.045,0,sz+b*.049) for a,b in corners]
            top=[(px-side*.072+a*.034,h*1.02,sz+b*.038) for a,b in corners]
            for i in range(4):
                j=(i+1)%4
                m.face("fieldstone",(base[j],base[i],top[i],top[j]))
            m.face("fieldstone",top[::-1])
    if m.lod<2:
        m.lamp_face(x+w*.31,h*.64,front-.011,.040,depth=.115)
    if m.lod==0:
        m.pot(x-w*.415,front-.048,.060,.102)
        m.block("trim",(x,h*.56,z+d*.5-.022),(.149,.088,.052))
        m.rear_vent(x,z+d*.5+.005,h*.56,.143,.083)
        # The side light has its own projecting hood and a single thick mullion.
        with m.transform(translate=(x+window_side*w*.49,0,z-d*.044),rotate_y=-90*window_side):
            m.block("trim",(0,h*.59,.024),(.129,.098,.057))
            m.light(0,h*.59,-.003,.104,.072,shutters=False)
            m.block("roof",(0,h*.59+.055,-.028),(.153,.026,.074),taper=.15)


def dawn(m,v):
    x=.025 if v==0 else -.079
    z=.053
    w,d=(.703,.58) if not v else (.603,.585)
    h=.25 if not v else .280
    m.course("fieldstone",x,z,w+.02,d+.025,0,.061,corner=.077)
    m.course("timber",x,z,w,d,.061,h,corner=.042,inset=.02)
    if not v:
        m.vault(x,z,w+.102,d+.111,h,.271,"thatch","timber",(8,5,4)[m.lod])
        if m.lod<2:
            for side in (-1,1):
                with m.transform(translate=(x+w*.48*side,h+.020,z),rotate_z=side*8):
                    m.block("timber",(0,0,0),(.046,.048,d+.117))
    else:
        m.hip_roof((x,h,z),w+.008,d+.016,.249,m.color("roof"),overhang=.046,material="roof")
        m.course("fieldstone",.257,.096,.235,.352,0,.171,corner=.043)
        m.gable_roof((.257,.171,.096),.224,.32,.102,m.color("thatch"),overhang=.018,material="thatch")
    f=z-d*.5-.012
    m.recessed_gate(x-.049,f,.059,.160,.213,"timber")
    m.stair(x-.049,f,.180,.059)
    frame(m,x,z,w,d,h,f,1 if v else 2,window_side=-1 if v else 1)
    m.flue_pot(x+.18,z+.102,h+.078,.275,.075,"timber")
    if m.lod==0 and v:
        m.block("timber",(x+.163,.066,f-.045),(.139,.045,.080))
        for s in (-1,1):
            m.block("fieldstone",(x+.163+s*.045,.027,f-.045),(.032,.055,.050))


def bronze(m,v):
    x=-.025 if v==0 else -.072
    z=.042
    w,d=(.726,.585) if not v else (.604,.543)
    h=.295 if not v else .357
    m.course("fieldstone",x,z,w,d,0,.091,corner=.077)
    m.course("wall",x,z,w-.025,d-.026,.091,h,corner=.044,inset=.023)
    m.course("trim",x,z,w+.027,d+.025,h,h+.042,corner=.083)
    top=h+.042
    if not v:
        m.course("roof",x,z,w-.018,d-.035,top,top+.136,corner=.077,inset=.052)
        m.course("accent",x-.054,z+.041,.355,.294,top+.131,top+.177,corner=.053)
        m.hip_roof((x-.054,top+.177,z+.041),.335,.277,.076,m.color("roof"),overhang=0,material="roof")
    else:
        m.vault(x,z,w+.055,d+.052,top,.169,"roof","wall",(8,6,4)[m.lod])
        m.course("wall",.251,.146,.266,.318,0,.208,corner=.045)
        m.course("accent",.251,.146,.283,.343,.208,.245,corner=.039)
        m.course("roof",.251,.146,.26,.303,.245,.301,corner=.055,inset=.041)
    f=z-d*.5-.013
    m.recessed_gate(x-.055,f,.088,.179,.216)
    m.stair(x-.055,f,.205,.088)
    frame(m,x,z,w,d,h,f,window_side=-1 if v else 1)
    m.flue_pot(x+.173,z+.075,top+.040,.243,.091,"accent")
    if m.lod<2:
        for i in range(3):
            cx=x+w*(i-1)*.153
            with m.transform(translate=(cx,h+.022,z-d*.5-.020),rotate_z=45):
                m.block("gold",(0,0,0),(.021,.021,.012))


def iron(m,v):
    x=-.013 if v==0 else -.092
    z=.063
    w,d=(.738,.57) if not v else (.569,.586)
    h=.328 if not v else .403
    m.course("fieldstone",x,z,w+.035,d+.030,0,.094,corner=.091)
    m.course("wall",x,z,w,d,.094,h,corner=.041,inset=.020)
    m.vault(x,z,w+.065,d+.065,h,.247 if not v else .266,"roof","wall",(8,6,4)[m.lod])
    if m.lod<2:
        m.vault(x,z-.208,w+.080,.046,h+.008,.255 if not v else .275,"metal","trim",(8,6)[m.lod])
    if v:
        m.course("wall",.234,.098,.285,.324,0,.197,corner=.05)
        m.vault(.234,.098,.327,.358,.197,.124,"slate","wall",(7,5,4)[m.lod])
    f=z-d*.5-.014
    m.recessed_gate(x-.050,f,.091,.195,.259,"fieldstone")
    m.stair(x-.050,f,.221,.091)
    frame(m,x,z,w,d,h,f,window_side=-1 if v else 1)
    m.flue_pot(x+.135,z+.11,h+.094,.263 if not v else .30,.096,"metal")
    if m.lod==0:
        for side in (-1,1):
            with m.transform(translate=(x-.05+side*.104,.305,f-.039),rotate_z=45):
                m.block("accent",(0,0,0),(.020,.020,.009))


def build(spec,lod):
    m=ClanHouse(spec,lod)
    {"dawn":dawn,"bronze":bronze,"iron":iron}[m.age](m,spec.variant)
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        result=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=240)
        print(spec.id,result["lod_triangles"])
