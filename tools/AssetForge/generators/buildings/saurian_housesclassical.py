"""Saurian houses."""

import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_housesancient import Dwelling, publish_houses

PACKAGE='saurian_housesclassical'


def column(m,x,z,y,h):
    w=.071
    if m.lod==2:
        m.block('ivory',x,z,y,y+h,w,w)
    else:
        m.block('wall',x,z,y,y+.024,w*1.30,w*1.30)
        m.drum('ivory',(x,y+.024,z),w*.48,h-.052,6 if not m.lod else 4,w*.40)
        m.block('wall',x,z,y+h-.028,y+h,w*1.29,w*1.29)


def sun_tile(m,x,y,z,w,h):
    # The central sun sits inside a stepped turquoise enamel pediment.
    # Each horizontal band is convex; retain the stepped outline at distance.
    for a,b,c in [(0,.42,.5),(.42,.69,.33),(.69,1,.15)]:
        m.face('turquoise',[(x+w*c,y+h*a,z),(x-w*c,y+h*a,z),
                            (x-w*c,y+h*b,z),(x+w*c,y+h*b,z)])
    if m.lod<2:
        m.eye(x,y+h*.42,z-.001,min(w*.17,h*.38))


def porch_lamp(m,x,z,base,h):
    m.block('accent',x,z,base,base+h*.47,.044,.044)
    m.drum('emissive',(x,base+h*.47,z),.030,h*.39,6,.036)
    m.drum('gold',(x,base+h*.86,z),.042,h*.14,6,.024)


def classical(m):
    v=m.variant
    x,w=(-.103,.505) if v else (0,.669)
    z,d=.094,.493
    base=.142
    head=base+(.31 if v else .292)
    front=z-d/2
    door=x-.030
    m.floor(door,base,'wall','fieldstone')
    m.block('plaster',x,z,base,head,w,d)
    coverx,coverz=x,.014
    cw,cd=w+.103,.702
    for side in (-1,1):
        column(m,x+side*(w*.48),-.262,base,head-base)
    m.course('ivory',coverx,coverz,head,head+.047,cw,cd,cw*.968,cd*.968,
             bevel=.054,underside=True)
    h1=.114
    m.course('roof_tile',x,coverz,head+.047,head+.047+h1,cw*.99,cd*.99,cw*.62,cd*.62,
             rim='roof_tile')
    m.roof_lozenge(x,coverz,head+.047,cd*.99,cd*.62,h1,cw*.26)
    upper=head+.047+h1
    m.course('ivory',x,coverz,upper,upper+.077,cw*.61,cd*.61,cw*.57,cd*.57)
    m.course('roof_tile',x,coverz,upper+.077,upper+.244,cw*.68,cd*.66,cw*.18,cd*.17,
             bevel=.04)
    if m.lod<2:
        m.opening(x,upper+.036,coverz-cd*.300,w*.24,.038,light='turquoise')
    m.door_panel(door,front,base,.135,.200,surround='gold')
    if v:
        # Canalside reading room with a lower, independently carried roof.
        wx,wz=.25,.088
        m.block('wall',wx,wz,base,base+.205,.272,.388)
        m.course('roof_tile',wx,wz,base+.205,base+.329,.349,.489,.182,.268,underside=True)
        m.opening(wx,base+.110,wz-.194,.137,.099)
        if m.lod<2:
            m.opening(wx+.136,base+.117,wz,.18,.10,side=True)
    elif m.lod<2:
        m.opening(x+w/2,base+.164,z,.198,.136,side=True)
        m.opening(x+.177,base+.126,front,.096,.095)
    if m.lod<2:
        sun_tile(m,door,base+.219,front-.008,.150,.062)
    if m.lod==0:
        porch_lamp(m,.264 if v else .180,-.282,base,.129)
        m.pot(-.280 if v else -.201,-.210,base,.036)
        with m.transform(translate=(x-w/2,base+.15,z),rotate_y=90):
            m.opening(0,0,0,.141,.094,light='glass')


def medieval(m):
    v=m.variant
    x,w=(-.125,.49) if v else (0,.647)
    z,d=.078,.503
    front=z-d/2
    base=.150
    low=base+.155
    head=low+(.184 if v else .155)
    door=x-.053
    m.floor(door,base,'fieldstone','fieldstone')
    m.block('fieldstone',x,z,base,low,w,d)
    m.block('wall',x,z,low,head,w+.026,d+.023,bottom=True)
    m.door_panel(door,front-.0115,base,.137,.217)
    # The jetty and upper panels rest on dark timber floor beams.
    if m.lod<2:
        for u in (x-w*.45,x+w*.45):
            m.block('trim',u,front-.010,base,head,.048,.044)
        m.rail('timber',(x+w*.44,low+.018,front-.023),
               (x+w*.23,head-.016,front-.023),.029)
    cw,cd=w+.148,d+.145
    rise=.30 if v else .268
    m.course('slate',x,z,head,head+rise,cw,cd,cw*.44,cd*.32,rim='roof',underside=True)
    m.roof_lozenge(x,z,head,cd,cd*.32,rise,cw*.32)
    m.course('roof',x,z,head+rise,head+rise+.081,cw*.49,cd*.39,cw*.40,cd*.25)
    # Smoke flue comes up through the rear roof, with a dark, open-looking throat.
    fx,fz=x-w*.26,z+.13
    with m.transform(translate=(fx,head+.055,fz),rotate_z=4):
        h=rise+.197
        m.block('fieldstone',0,0,0,h,.093,.110)
        m.block('trim',0,0,h,h+.033,.121,.138)
        if m.lod<2:
            m.block('door',0,0,h+.033,h+.036,.074,.09)
    if v:
        wx,wz=.222,.106
        m.block('fieldstone',wx,wz,base,base+.213,.278,.36)
        m.course('slate',wx,wz,base+.213,base+.364,.365,.445,.163,.183,
                 rim='roof',underside=True)
        if m.lod<2:
            m.opening(wx,base+.115,wz-.180,.141,.102)
            m.opening(wx+.139,base+.119,wz,.141,.106,side=True)
    elif m.lod<2:
        m.opening(x+w/2+.013,low+.074,z,.197,.083,side=True)
        m.opening(x+w*.19,low+.070,front-.0115,.085,.064)
    if m.lod<2:
        m.eye(door,base+.260,front-.018,.034)
    if m.lod==0:
        m.woven_mat(x-w*.32,base+.052,front-.015,.077,.144)
        m.pot(.317,-.269,base,.047)
        with m.transform(translate=(x-w/2-.014,low+.064,z),rotate_y=90):
            m.opening(0,0,0,.138,.085,light='door')


def cupola(m,x,z,y,rx,rz,height):
    # A scalloped melon vault on a polygonal curb. Face pairs form continuous
    # baked-clay leaves from springing to crown, with narrow patinated ribs.
    n=(12,8,6)[m.lod]
    angles=[2*math.pi*i/n for i in range(n)]
    def ring(r,h):
        return [(x+rx*r*math.cos(a),y+h*height,z+rz*r*math.sin(a)) for a in angles]
    lo=ring(1,0)
    profile=((.91,.35),(.64,.68),(.24,.93)) if m.lod==0 else ((.72,.56),(.26,.92))
    for r,h in profile:
        hi=ring(r,h)
        for i in range(n):
            j=(i+1)%n
            role='roof' if m.lod==0 and i in (2,5,8,11) else 'roof_tile'
            m.face(role,[lo[j],lo[i],hi[i],hi[j]])
        lo=hi
    tip=(x,y+height,z)
    for i in range(n):
        m.face('gold',[lo[(i+1)%n],lo[i],tip])


def renaissance(m):
    v=m.variant
    x,w=(-.120,.494) if v else (0,.662)
    z,d=.068,.518
    front=z-d/2
    base=.135
    head=base+(.312 if v else .291)
    dx=x-.045
    m.floor(dx,base,'plaster','fieldstone')
    m.course('plaster',x,z,base,head,w,d,w,d,bevel=.085)
    m.course('roof_tile',x,z,head,head+.100,w+.106,d+.122,w*.88,d*.91,
             rim='roof',underside=True)
    cy=head+.100
    m.course('turquoise',x,z,cy,cy+.028,w*.89,d*.92,w*.88,d*.91)
    cupola(m,x,z,cy+.028,w*.439,d*.45,.294 if v else .262)
    m.door_panel(dx,front,base,.147,.202,surround='trim')
    if v:
        wx,wz=.239,.093
        m.block('wall',wx,wz,base,base+.222,.29,.365)
        m.course('roof_tile',wx,wz,base+.222,base+.285,.355,.414,.29,.34,underside=True)
        cupola(m,wx,wz,base+.285,.144,.165,.114)
        if m.lod<2:
            m.opening(wx,base+.116,wz-.1825,.140,.095)
            m.opening(wx+.145,base+.122,wz,.146,.094,side=True)
    elif m.lod<2:
        m.opening(x+w*.5,base+.154,z,.209,.114,side=True)
    if m.lod<2:
        sun_tile(m,dx,base+.232,front-.010,.16,.066)
        if not v:
            m.woven_mat(x+.176,base+.081,front,.086,.133)
    if m.lod==0:
        porch_lamp(m,.332,-.245,base,.114)
        m.pot(-.313,-.268,base,.042)
        with m.transform(translate=(x-w*.5,base+.148,z),rotate_y=90):
            m.opening(0,0,0,.135,.098,light='glass')


def build(spec,lod):
    m=Dwelling(spec,lod)
    {'classical':classical,'medieval':medieval,'renaissance':renaissance}[m.age](m)
    return m


if __name__=='__main__':
    publish_houses(PACKAGE,build)
