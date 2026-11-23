"""War Idol: a great carved oath mask set over a ritual doorway and a pair of laced council drums."""
import math
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_housesancient import Lodge, generate

PACKAGE='orc_wonders'


def relief(m,outline,front,back,face,rim=None,ridge=.028):
    """A convex piece of deep relief. The carving has a sided return into its timber backing."""
    pts=np.array(outline,dtype=float)
    if sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(pts,np.roll(pts,-1,axis=0)))>0:
        pts=pts[::-1]
    near=[(x,y,front) for x,y in pts]
    far=[(x,y,back) for x,y in pts]
    cx,cy=pts.mean(axis=0)
    for j in range(len(pts)):
        k=(j+1)%len(pts)
        m.face(rim or face,(near[k],near[j],far[j],far[k]))
        if ridge:
            m.face(face,(near[j],near[k],(cx,cy,front-ridge)))
    if not ridge:
        m.face(face,near)
    # Shallow inlays terminate inside solid timber. Keep full backs on isolated pieces.
    if m.lod<2 or back-front>.22:
        m.face(rim or face,far[::-1])


def stage(m):
    # The wide dais is reached by three real treads and leaves room to stand before the inner door.
    tiers=[(0,.889,.748,0,.114),(.060,.925,.765,0,.114),
           (.159,.883,.729,0,.118),(.191,.904,.744,0,.118),
           (.247,.885,.728,0,.117)]
    roles=['fieldstone','wall','trim','fieldstone']
    if m.lod==2:
        tiers,roles=[tiers[0],tiers[-1]],['fieldstone']
    m.shell(tiers,roles,.25)
    for i in range(3):
        top=(i+1)*.063
        front=-.968+i*.117
        back=-.598
        d=back-front
        w=.361-i*.014
        m.shell([(i*.063,w,d/2,0,(back+front)/2),(top,w-.005,d/2-.003,0,(back+front)/2)],
                ['fieldstone' if i%2==0 else 'wall'],.115)
    if m.lod==0:
        for s in (-1,1):
            for i in range(3):
                # Three copper set-stones down each edge of the approach.
                px=s*(.294-i*.015)
                m.block('accent',(px,(i+1)*.063+.004,-.925+i*.117),(.046,.009,.050),taper=.20)
    body=[(.247,.568,.406,0,.160),(.316,.594,.422,0,.160),
          (.528,.482,.349,0,.170),(.575,.500,.359,0,.170),(.623,.463,.323,0,.170)]
    if m.lod==2:
        body=[body[0],body[-1]]
        roles=['wall']
    else:
        roles=['trim','wall','accent','roof']
    m.shell(body,roles,.255)
    m.doorway(0,-.336,.247,.345,.354,body='roof',horns=False)
    if m.lod<2:
        for s in (-1,1):
            m.block('timber',(s*.162,.394,-.298),(.050,.264,.082),taper=.10)


def shoulders(m):
    # Heavy foot braces meet the central trunk beneath the carved lintel.
    trunk=[(.580,.326,.252,0,.166),(.699,.360,.267,0,.164),
           (.875,.285,.233,0,.164),(1.149,.361,.255,0,.157),
           (1.332,.535,.287,0,.151),(1.447,.431,.261,0,.147)]
    if m.lod==2:
        trunk=[trunk[i] for i in (0,2,4,5)]
        roles=['timber','wall','trim']
    else:
        roles=['roof','trim','wall','wall','trim']
    m.shell(trunk,roles,.26)
    for s in (-1,1):
        m.pole('timber',(s*.425,.258,.389),(s*.258,1.247,.314),.106,.086,(8,6,4)[m.lod])
    relief(m,[(-.038,.655),(-.254,.879),(-.358,1.194),(-.224,1.269),
              (.224,1.269),(.358,1.194),(.254,.879),(.038,.655)],
           -.118,.040,'roof','timber',.110)
    # Boar-tooth inlay is cut as two strips meeting along the breastplate's raised middle ridge.
    for s in (-1,1):
        relief(m,[(s*.015,.803),(s*.206,.984),(s*.263,1.110),(s*.038,.985)],
               -.206,-.146,'banner',ridge=.013)
    m.pole('trim',(-.764,1.265,.167),(.782,1.265,.167),.083,.083,(8,6,5)[m.lod])
    for s in (-1,1):
        with m.transform(scale=(s,1,1)):
            m.pole('timber',(.312,.885,.167),(.674,1.246,.167),.046,.044,5)
            # Rectangular mantle strip has a broad shoulder and a split diagonal hem.
            near=[(.537,1.286,.10),(.755,1.286,.10),(.756,.867,.086),
                  (.641,.716,.028),(.548,.816,.056)]
            far=[(a,b,c+.023) for a,b,c in near]
            m.face('banner',near)
            m.face('banner',far[::-1])
            for j in range(5):
                k=(j+1)%5
                m.face('hide',(near[k],near[j],far[j],far[k]))
            if m.lod<2:
                # A brass-weighted hem and a small bone tally on each clan standard.
                m.pole('accent',(.548,.829,.051),(.641,.729,.024),.015,.016,5)
                m.pole('ivory',(.655,.880,.069),(.687,.977,.069),.019,.013,5)
            if m.lod==0:
                for u in (.549,.728):
                    m.pole('hide',(u,1.276,.099),(u,1.306,.181),.025,.025,5)



def clan_tallies(m):
    # Three deliberately carved ram-face pendants hang from an ochre cord across the breast.
    knots=[(-.269,1.157,-.141),(-.141,1.103,-.219),(0,1.087,-.242),
           (.141,1.103,-.219),(.269,1.157,-.141)]
    for a,b in zip(knots,knots[1:]):
        m.pole('thatch',a,b,.012,.012,5)
    for x,y,z in knots[1:-1]:
        with m.transform(translate=(x,y,z-.019),rotate_z=x*37,scale=.87 if x else 1):
            m.pole('trim',(0,0,.005),(0,-.041,-.017),.009,.010,5)
            relief(m,[(0,-.158),(-.039,-.116),(-.057,-.071),(-.035,-.039),
                      (.035,-.039),(.057,-.071),(.039,-.116)],-.032,.003,'ivory',ridge=.018)
            for s in (-1,1):
                relief(m,[(s*.011,-.091),(s*.021,-.075),(s*.041,-.076),(s*.035,-.091)],
                       -.048,-.025,'door',ridge=.003)
                m.pole('roof',(s*.032,-.06,0),(s*.060,-.033,.004),.014,.005,5)


def crown_horn(m,s):
    # The upper ivory lengths shrink along a true three-dimensional centerline.
    data=[(.314,1.960,.105,.156),(.489,1.955,.126,.150),(.555,1.990,.141,.139),
          (.691,2.105,.147,.122),(.769,2.298,.127,.095),
          (.743,2.461,.073,.059),(.655,2.596,.004,.027),(.548,2.665,-.048,0)]
    roles=['roof','accent','ivory','ivory','ivory','ivory','ivory']
    if m.lod==1:
        data=[data[i] for i in (0,2,3,4,5,7)]
        roles=['roof','accent','ivory','ivory','ivory']
    if m.lod==2:
        data=[data[i] for i in (0,2,4,5,7)]
        roles=['roof','ivory','ivory','ivory']
    m.path([(s*x,y,z,r) for x,y,z,r in data],roles,(11,8,5)[m.lod])


def mask(m):
    z=.111
    # The forehead projects well beyond the neck, while the snout and brows are separately carved.
    courses=[(1.353,.275,.248,0,z),(1.453,.384,.337,0,z),
             (1.729,.466,.398,0,z),(1.959,.519,.401,0,z),
             (2.103,.414,.340,0,z),(2.167,.312,.253,0,z)]
    if m.lod==2:
        courses=[courses[i] for i in (0,2,3,5)]
        roles=['wall','wall','roof']
    else:
        roles=['trim','wall','wall','roof','trim']
    m.shell(courses,roles,.35)
    # Back crown rises out of the massive wood joint between the two horns.
    for s in (-1,1):
        crown_horn(m,s)
    relief(m,[(-.150,2.057),(-.145,2.166),(-.084,2.300),(0,2.365),
              (.084,2.300),(.145,2.166),(.150,2.057)],
           .042,.274,'roof','trim',.052)
    if m.lod<2:
        relief(m,[(0,2.130),(-.047,2.207),(0,2.287),(.047,2.207)],
               -.006,.056,'banner',ridge=.009)
    # Ears are timber cheek flanges. Each eye is set back under its own canted brow shelf.
    for s in (-1,1):
        with m.transform(scale=(s,1,1)):
            relief(m,[(.317,1.578),(.320,1.816),(.476,1.917),(.586,1.872),(.525,1.643)],
                   -.107,.180,'roof','timber',.060)
            relief(m,[(.070,1.794),(.090,1.905),(.335,1.989),(.405,1.924),(.321,1.819)],
                   -.319,-.206,'door','trim',.016)
            relief(m,[(.110,1.825),(.138,1.879),(.303,1.936),(.287,1.890)],
                   -.338,-.312,'emissive','accent',.004)
            relief(m,[(.058,1.888),(.058,1.981),(.302,2.103),(.430,2.050),(.397,1.972)],
                   -.355,-.203,'banner','roof',.064)
            relief(m,[(.139,1.566),(.144,1.672),(.358,1.822),(.417,1.743),(.351,1.578)],
                   -.296,-.167,'wall','roof',.044)
            if m.lod<2:
                relief(m,[(.227,1.624),(.230,1.685),(.377,1.780),(.363,1.715)],
                       -.334,-.271,'cloth',ridge=.009)
    # A central kite-shaped nose bridge and a lower jaw with two upward-curving carved ivory tusks.
    relief(m,[(0,1.620),(-.135,1.698),(-.090,1.899),(0,2.005),(.090,1.899),(.135,1.698)],
           -.352,-.198,'roof','wall',.145)
    relief(m,[(-.214,1.498),(-.248,1.584),(-.110,1.649),(.110,1.649),
              (.248,1.584),(.214,1.498),(0,1.470)],-.322,-.230,'door','trim',.030)
    relief(m,[(-.165,1.385),(-.252,1.469),(-.232,1.521),(.232,1.521),
              (.252,1.469),(.165,1.385),(0,1.345)],-.334,-.140,'wall','roof',.061)
    for s in (-1,1):
        m.path([(s*.166,1.464,-.309,.067),(s*.215,1.502,-.364,.056),
                (s*.244,1.602,-.380,.033),(s*.220,1.703,-.356,0)],
               ['accent','ivory','ivory'],(8,6,4)[m.lod])
    if m.lod<2:
        # Rear clan plaque and spine are visible when the player orbits behind the idol.
        with m.transform(translate=(0,0,.562),rotate_y=180):
            relief(m,[(-.190,1.627),(-.211,1.922),(0,2.011),(.211,1.922),(.19,1.627),(0,1.545)],
                   .030,.150,'roof','trim',.035)
            relief(m,[(0,1.631),(-.097,1.825),(0,1.934),(.097,1.825)],
                   -.005,.056,'banner',ridge=.009)


def council_drum(m,s):
    x,z=s*.605,-.267+(.019 if s<0 else 0)
    y=.247
    r=.172 if s>0 else .157
    h=.355 if s>0 else .304
    n=(12,8,6)[m.lod]
    stations=[(0,.70),(.08,.79),(.22,.77),(.58,.97),(.76,1),(.83,1.07),(.96,1.05),(1,.98)]
    roles=['trim','roof','timber','roof','accent','hide','thatch']
    if m.lod==1:
        stations=[stations[i] for i in (0,2,5,7)]
        roles=['trim','timber','hide']
    if m.lod==2:
        stations=[stations[i] for i in (0,5,7)]
        roles=['roof','hide']
    m.path([(x,y+a*h,z,r*b) for a,b in stations],roles,n)
    if m.lod<2:
        for j in range(0,n,2):
            a=2*math.pi*j/n
            b=a+2*math.pi/n
            start=(x+r*.82*math.cos(b),y+h*.268,z+r*.82*math.sin(b))
            top=(x+r*1.045*math.cos(a),y+h*.812,z+r*1.045*math.sin(a))
            m.pole('thatch',start,top,.009,.009,4)
            if m.lod==0:
                end=(x+r*1.045*math.cos(b+2*math.pi/n),y+h*.812,z+r*1.045*math.sin(b+2*math.pi/n))
                m.pole('thatch',start,end,.009,.009,4)
    if m.lod==0:
        # The padded beater lies across the hide head, with its grip projecting over the rim.
        with m.transform(translate=(x,y+h+.015,z),rotate_y=28 if s>0 else -21):
            m.pole('timber',(-r*1.14,0,0),(r*.52,0,0),.014,.012,6)
            m.pole('cloth',(r*.46,0,0),(r*.71,0,0),.027,.025,7)


def build(spec,lod):
    m=Lodge(spec,lod)
    stage(m)
    shoulders(m)
    mask(m)
    if lod==0:
        clan_tallies(m)
    for s in (-1,1):
        council_drum(m,s)
    return m


if __name__=='__main__':
    generate(PACKAGE,build)
