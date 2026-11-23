"""Saurian houses."""

import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_housesancient import Dwelling, publish_houses

PACKAGE='saurian_houseslate'


def garden(m,x,z,y):
    # Reed leaves fold at the midrib and emerge from the same soil bed.
    m.course('wall',x,z,y,y+.057,.116,.094,.131,.108)
    m.block('soil',x,z,y+.057,y+.061,.105,.080)
    for dx,dz,h in ((-.041,.016,.085),(.012,.026,.116),(.042,-.024,.079)):
        foot=(x,y+.060,z)
        tip=(x+dx,y+.060+h,z+dz)
        a=(x+dx*.45-.013,y+.060+h*.45,z+dz*.45)
        b=(x+dx*.45+.013,y+.060+h*.45,z+dz*.45)
        ridge=(x+dx*.45,y+.060+h*.45,z+dz*.45-.017)
        for face in ((foot,a,ridge),(foot,ridge,b),(a,tip,ridge),(ridge,tip,b),
                     (b,tip,a),(b,a,foot)):
            m.face('leaf',face)


def industrial(m):
    v=m.variant
    x,z=(-.068,.081) if v else (.015,.081)
    w,d=(.586,.501) if v else (.665,.522)
    base=.146
    head=base+(.277 if v else .256)
    dx=x-(.116 if v else .078)
    front=z-d/2
    m.floor(dx,base,'fieldstone','brick')
    m.block('brick',x,z,base,head,w,d)
    m.door_panel(dx,front,base,.134,.205,surround='roof')
    cw,cd=w+.114,d+.133
    rise=.139 if v else .156
    m.course('metal',x,z,head,head+rise,cw,cd,cw*.70,cd*.53,rim='roof',underside=True)
    m.roof_lozenge(x,z,head,cd,cd*.53,rise,cw*.25)
    high=head+rise
    if v:
        # Offset truncated monitor, beneath a turned-over copper hood.
        m.course('glass',x-.030,z,high,high+.126,cw*.56,cd*.51,cw*.52,cd*.45)
        m.course('roof',x-.030,z,high+.126,high+.184,cw*.63,cd*.54,cw*.46,cd*.33)
    else:
        m.course('brick',x,z,high,high+.094,cw*.68,cd*.50,cw*.59,cd*.45)
        m.course('metal',x,z,high+.094,high+.190,cw*.68,cd*.54,cw*.45,cd*.26,
                 rim='roof')
    if v:
        # A round masonry breast rises through the eave at the side of the home.
        fx,fz=x+w*.49,z+.063
        n=(10,7,5)[m.lod]
        m.drum('brick',(fx,base,fz),.094,.344,n,.087)
        m.drum('roof',(fx,base+.344,fz),.086,.300,n,.060)
        m.drum('metal',(fx,base+.644,fz),.075,.050,n,.074)
        if m.lod<2:
            m.drum('door',(fx,base+.694,fz),.048,.003,n)
    else:
        fx,fz=x-w*.32,z+.12
        m.block('brick',fx,fz,head+.035,head+.432,.093,.10)
        m.block('metal',fx,fz,head+.432,head+.463,.113,.12)
        if m.lod<2:
            m.block('door',fx,fz,head+.463,head+.467,.072,.081)
    if m.lod<2:
        m.opening(x+w*.254,base+.143,front,.153,.104)
        m.opening(x+w/2,base+.135,z-.133 if v else z,.137 if v else .195,
                  .109,side=True)
        m.eye(dx,base+.232,front-.010,.031)
        if not v:
            m.opening(x,high+.046,z-cd*.243,.187,.045,light='door')
    if m.lod==0:
        garden(m,.315,-.268,base)
        m.woven_mat(x-w*.398,base+.064,front,.065,.127)
        with m.transform(translate=(x-w*.5,base+.137,z),rotate_y=90):
            m.opening(0,0,0,.164,.099,light='glass')


def ribbon(m,x,z,base,w,d,h,skirt='wall', glazing='glass'):
    # Panoramic living-room window rests on an opaque spandrel and is divided by real posts.
    waist=base+h*.53
    m.course(skirt,x,z,base,waist,w,d,w,d,bevel=.12)
    m.course(glazing,x,z,waist,base+h,w,d,w,d,bevel=.12)
    if m.lod<2:
        for u in (x-w*.28,x+w*.28):
            m.block(skirt,u,z-d*.5-.002,waist,base+h,.024,.010)
        m.block(skirt,x+w*.5+.002,z,waist,base+h,.010,.025)
    return base+h


def fold(m,x,z,y,w,d,shape,role):
    """A folded steel canopy on a solid matching clerestory, like a pair of reed leaves."""
    width=[x+w*u for u,_ in shape]
    heights=[y+h for _,h in shape]
    za,zb=z-d/2,z+d/2
    for i in range(len(shape)-1):
        a,b=width[i:i+2]
        ya,yb=heights[i:i+2]
        # Each section has a sloping glass transom, fascia and top plane.
        for plane,normal in ((za,-1),(zb,1)):
            for lo,hi,role2 in (([y,y],[ya,yb],'glass'),([ya,yb],[ya+.029,yb+.029],role)):
                quad=[(b,lo[1],plane),(a,lo[0],plane),(a,hi[0],plane),(b,hi[1],plane)]
                m.face(role2,quad if normal<0 else quad[::-1])
        m.face(role,[(a,ya+.029,zb),(b,yb+.029,zb),(b,yb+.029,za),(a,ya+.029,za)])
    for j,reverse in ((0,False),(-1,True)):
        u,h=width[j],heights[j]+.029
        quad=[(u,y,za),(u,y,zb),(u,h,zb),(u,h,za)]
        m.face(role,quad[::-1] if reverse else quad)
    m.face(role,[(width[0],y,za),(width[-1],y,za),(width[-1],y,zb),(width[0],y,zb)])


def doorhood(m,x,z,base,top,width,role):
    if m.lod==2:
        return
    m.block(role,x,z-.020,top-.024,top,width,.067,bottom=True)
    if m.lod==1:
        return
    for sign in (-1,1):
        m.block(role,x+sign*(width/2-.012),z-.009,base,top-.024,.024,.032)


def modern(m):
    v=m.variant
    x,z=(.018,.073) if v else (-.021,.082)
    w,d=.692,.527
    base=.132
    dx=x+(.098 if v else -.125)
    m.floor(dx,base,'plaster','metal')
    head=ribbon(m,x,z,base,w,d,.255,skirt='plaster')
    front=z-d/2
    m.door_panel(dx,front-.005,base,.14,.202,surround='trim')
    doorhood(m,dx,front,base,base+.236,.207,'metal')
    m.course('ivory',x,z,head,head+.053,w+.113,d+.12,w+.096,d+.103,
             bevel=.13,underside=True)
    curb=head+.053
    if v:
        # A long asymmetric butterfly gathers rain from two roof fields.
        fold(m,x,z,curb,w*.96,d*.95,[(-.5,.17),(-.16,.045),(.5,.113)],'roof')
        # A diamond-shaped rooflight sits on the broad rain plane. The glazing
        # peaks slightly above its flashing rather than lying flat on the steel.
        def daylight(u,v,up):
            return (x+u*w*.96,curb+.045+.029+(.113-.045)*(u+.16)/.66+up,z+v*d)
        frame=[daylight(.16,.32,.004),daylight(.40,-.008,.004),
               daylight(.16,-.34,.004),daylight(-.085,-.008,.004)]
        m.face('gold',frame)
        pane=[daylight(.16,.27,.006),daylight(.35,-.008,.006),
              daylight(.16,-.28,.006),daylight(-.03,-.008,.006)]
        apex=daylight(.16,-.008,.034)
        for i in range(4):
            m.face('turquoise' if i<2 else 'glass',[pane[i],pane[(i+1)%4],apex])
    else:
        ux,uz=x-.054,z+.043
        m.course('glass',ux,uz,curb,curb+.123,w*.72,d*.76,w*.70,d*.71)
        m.course('metal',ux,uz,curb+.123,curb+.241,w*.83,d*.87,w*.44,d*.46,
                 rim='roof',underside=True)
        m.roof_lozenge(ux,uz,curb+.123,d*.87,d*.46,.118,w*.22)
    if m.lod<2:
        m.eye(dx,base+.252,front-.017,.036)
    if m.lod==0:
        garden(m,x+(-.26 if v else .26),-.265,base)
        m.woven_mat(x+(.264 if v else -.28),base+.038,front,.065,.104)
        m.opening(x+w*(-.21 if v else .20),base+.190,front-.013,.102,.076)


def information(m):
    v=m.variant
    x,z=(0,.095) if v else (-.023,.073)
    w,d=.693,.525
    base=.146
    dx=x+(.083 if v else -.116)
    front=z-d/2
    m.floor(dx,base,'metal','metal')
    head=ribbon(m,x,z,base,w,d,.265,skirt='ivory',glazing='glass')
    m.door_panel(dx,front-.006,base,.137,.193,surround='trim')
    doorhood(m,dx,front,base,base+.222,.201,'turquoise')
    cw,cd=w+.103,d+.105
    m.course('glass',x,z,head,head+.112,cw,cd,cw*.77,cd*.74,
             bevel=.12,rim='turquoise',underside=True)
    y=head+.112
    if v:
        # Offset stack faces the morning sun. The rooftop sensor is fixed to its cap.
        ux,uz=x-.068,z+.030
        m.course('ivory',ux,uz,y,y+.051,cw*.56,cd*.66,cw*.56,cd*.66)
        m.course('glass',ux,uz,y+.051,y+.201,cw*.61,cd*.69,cw*.44,cd*.51,
                 rim='metal')
        m.course('ivory',ux,uz,y+.201,y+.229,cw*.47,cd*.54,cw*.42,cd*.50)
        m.block('metal',ux-.08,uz+.045,y+.229,y+.354,.021,.021)
        if m.lod<2:
            m.block('ascension',ux-.08,uz+.045,y+.354,y+.376,.031,.031)
    else:
        # Two ranks of continuous solar glazing run from eave to ridge.
        m.course('ivory',x,z,y,y+.037,cw*.80,cd*.78,cw*.77,cd*.75)
        m.course('glass',x,z,y+.037,y+.204,cw*.74,cd*.73,cw*.43,cd*.38,
                 rim='turquoise')
        m.course('metal',x,z,y+.204,y+.246,cw*.44,cd*.39,cw*.36,cd*.30)
    m.roof_lozenge(x,z,head,cd,cd*.74,.112,cw*.20,role='ivory')
    if m.lod<2:
        m.eye(dx,base+.240,front-.014,.033)
        m.opening(x+(-.167 if v else .179),base+.165,front-.008,.120,.080,light='ascension')
    if m.lod==0:
        garden(m,.279 if not v else -.29,-.258,base)
        # Low basking bench stands on the porch along the outer wall.
        bx=.261 if v else -.298
        for side in (-1,1):
            m.block('metal',bx+side*.054,-.273,base,base+.040,.022,.042)
        m.block('timber',bx,-.273,base+.040,base+.060,.152,.066,bottom=True)


def halo(m,x,z,seat,r,tilt):
    # The annulus is supported by two splayed arms. The inclined family's arms
    # are cut to the actual transformed ring coordinates, including the rise.
    angle=math.radians(tilt)
    cy=seat+.072+r*abs(math.sin(angle))
    h=.018
    n=(16,10,6)[m.lod]
    def world(rad,phi,up):
        dx=rad*math.cos(phi)
        dz=rad*.85*math.sin(phi)
        return (x+dx,cy+up*math.cos(angle)-dz*math.sin(angle),
                z+up*math.sin(angle)+dz*math.cos(angle))
    for i in range(n):
        a,b=2*math.pi*i/n,2*math.pi*(i+1)/n
        ob0,ob1=world(r,a,-h),world(r,b,-h)
        ot0,ot1=world(r,a,h),world(r,b,h)
        ib0,ib1=world(r*.70,a,-h),world(r*.70,b,-h)
        it0,it1=world(r*.70,a,h),world(r*.70,b,h)
        for role,face in [('gold',(ob1,ob0,ot0,ot1)),('ivory',(ib0,ib1,it1,it0)),
                          ('gold',(ot1,ot0,it0,it1)),('accent',(ob0,ob1,ib1,ib0))]:
            m.face(role,face)
    for base_phi in (math.pi*.21,math.pi*.79):
        phi=base_phi if tilt>0 else -base_phi
        p=world(r*.85,phi,-h*.70)
        m.rail('gold',(x+r*.47*math.cos(phi),seat-.016,z),p,.036)


def ascension(m):
    v=m.variant
    x,z=(.013,.069) if v else (-.012,.068)
    d,w=.539,.68
    base=.142
    dx=x+(.092 if v else -.072)
    front=z-d*.5
    m.floor(dx,base,'ivory','gold')
    head=ribbon(m,x,z,base,w,d,.263,skirt='ivory',glazing='turquoise')
    m.door_panel(dx,front-.006,base,.141,.206,surround='gold')
    cw,cd=w+.137,d+.114
    rise=.131 if v else .165
    m.course('ivory',x,z,head,head+rise,cw,cd,cw*.66,cd*.66,
             rim='gold',underside=True)
    m.roof_lozenge(x,z,head,cd,cd*.66,rise,cw*.25,role='ascension')
    cap=head+rise
    m.course('ascension',x,z,cap,cap+.052,cw*.63,cd*.63,cw*.59,cd*.59)
    m.course('ivory',x,z,cap+.052,cap+.169,cw*.66,cd*.67,cw*.37,cd*.35,
             rim='gold',underside=True)
    halo(m,x,z,cap+.169,.172 if v else .202,-54 if v else 13)
    if m.lod<2:
        m.eye(dx,base+.240,front-.014,.034)
    if m.lod==0:
        garden(m,.277 if not v else -.275,-.260,base)
        # Low porch beacon houses a glowing cell inside a turned brass socket.
        lx=-.26 if not v else .279
        m.drum('gold',(lx,base,-.275),.037,.043,7,.023)
        m.drum('ascension',(lx,base+.043,-.275),.023,.056,7,.034)
        m.drum('gold',(lx,base+.099,-.275),.037,.021,7,.020)
        m.opening(x+(-.192 if v else .181),base+.139,front-.009,.10,.070,light='ascension')
        m.woven_mat(x+(.271 if v else -.270),base+.042,front,.062,.097)


def build(spec,lod):
    m=Dwelling(spec,lod)
    {'industrial':industrial,'modern':modern,'information':information,
     'ascension':ascension}[m.age](m)
    return m


if __name__=='__main__':
    publish_houses(PACKAGE,build)
