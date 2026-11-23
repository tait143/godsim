"""Construction details shared by the later Saurian workshops and public buildings.

Coordinates describe the actual deck, wall face and roof bearing. Buildings place their
own machinery and circulation around those bearings. All measurements are in local tiles.
"""
import math


def footing(m, w=1.90, d=1.78, role="fieldstone"):
    m.slab(role, 0, 0, 0, w, .08, d, .97, cut=.12)


def shell(m, x, z, w, d, h, y=.08, door=True, material=None):
    material = material or ("wall", "wall", "brick", "wall")[m.era]
    m.slab(material, x, y, z, w, h, d, cut=.09 if m.lod == 0 else 0)
    front = z-d/2
    if door:
        m.entry(x, y, front, min(.33, w*.34), min(h*.85, .42))
    if m.lod < 2:
        for side in (-1,1):
            m.block("glass" if m.era > 1 else "turquoise", x+side*w*.34,
                    y+h*.48, front-.009, w*.115, h*.23, .022)
        if m.lod == 0:
            m.block("turquoise", x+w/2+.003, y+h*.35, z, .016, h*.26, d*.57)
            m.block("gold", x+w/2+.011, y+h*.66, z, .021, h*.038, d*.63)
    return y+h


def seal(m, x, y, z, radius):
    m.disc(x,y,z,radius,boss=m.lod==0)


def pagoda(m, x, z, w, d, h, y=.08):
    """Two broad stone rain skirts around the clerestory of a stepped sanctuary."""
    top = shell(m,x,z,w*.84,d*.82,h*.49,y)
    m.slab("roof",x,top,z,w,h*.25,d,.60,underside=True)
    top += h*.25
    m.slab("wall",x,top,z,w*.55,h*.17,d*.56,.94)
    top += h*.17
    m.slab("roof",x,top,z,w*.69,h*.29,d*.69,.29,underside=True)
    return top+h*.29


def barrel(m, x, z, w, d, h, rise, y=.08):
    """Long shed with closed transverse end walls and a segmented barrel vault."""
    spring = shell(m,x,z,w,d,h,y)
    n = (10,6,4)[m.lod]
    outer = [(x+w*.53*math.cos(i*math.pi/n), spring+rise*math.sin(i*math.pi/n))
             for i in range(n+1)]
    near,far = z-d*.53,z+d*.53
    m.face("wall",[(a,b,near) for a,b in reversed(outer)])
    m.face("wall",[(a,b,far) for a,b in outer])
    for i,((a,b),(c,e)) in enumerate(zip(outer,outer[1:])):
        m.face("metal" if m.era==2 else "glass",
               [(a,b,near),(a,b,far),(c,e,far),(c,e,near)][::-1])
    if m.lod==0:
        for f in (-.26,.26):
            for (a,b),(c,e) in zip(outer,outer[1:]):
                A=(x+(a-x)*1.018,spring+(b-spring)*1.03)
                C=(x+(c-x)*1.018,spring+(e-spring)*1.03)
                zz=z+f*d
                m.face("trim",[(A[0],A[1],zz-.016),(C[0],C[1],zz-.016),
                               (C[0],C[1],zz+.016),(A[0],A[1],zz+.016)])
    return spring+rise


def northlights(m,x,z,w,d,h,rise,bays=3,y=.08):
    """Production hall with a sawtooth roof and vertical northlight glazing."""
    top=shell(m,x,z,w,d,h,y)
    bay=w/bays
    for i in range(bays):
        left=x-w/2+bay*i
        right=left+bay
        f,b=z-d*.505,z+d*.505
        lo,hi=top+.015,top+rise
        m.face("metal",[(left,lo,f),(left,lo,b),(right,hi,b),(right,hi,f)])
        m.face("glass",[(right,lo,f),(right,hi,f),(right,hi,b),(right,lo,b)])
        for zz,rev in ((f,True),(b,False)):
            p=[(left,lo,zz),(right,lo,zz),(right,hi,zz)]
            m.face("wall",p[::-1] if rev else p)
    return top+rise


def lens(m,x,z,r,h,y=.08):
    """An occupied round chamber below a glazed calotte and its gold sun cap."""
    n=(16,10,6)[m.lod]
    m.drum("wall",x,y,z,r,h,r*.93,n=n)
    # A projecting flat portal allows the opening to meet a faceted round shell.
    m.block("ivory",x,y,z-r*.87,r*.78,h*.79,r*.31)
    m.entry(x,y,z-r*1.025,r*.47,h*.63)
    eave=y+h
    m.drum("turquoise",x,eave,z,r*1.04,r*.13,n=n)
    m.mound("glass",x,eave+r*.13,z,r*.99,r*.72,n=n)
    m.drum("gold",x,eave+r*.80,z,r*.17,r*.12,r*.13,n=(9,6,4)[m.lod])
    return eave+r*.92


def spire(m,x,z,r,h,y=.08,role="brick"):
    n=(12,8,5)[m.lod]
    m.drum(role,x,y,z,r,h,r*.73,n=n)
    m.drum("trim",x,y+h,z,r*.85,r*.28,n=n)
    if m.lod<2:
        m.drum("door",x,y+h+r*.28,z,r*.59,.008,n=n)
        if m.lod==0:
            for f in (.24,.73):
                m.drum("metal",x,y+h*f,z,r*(1.025-.27*f),h*.038,n=n)


def arch(m, x, z, w, h, thick, depth, role="ivory", y=.08):
    """Solid half-elliptical bow with a genuinely open passage between its feet."""
    n=(14,8,5)[m.lod]
    def point(i,inside,zz):
        a=math.pi*i/n
        return (x+(w/2-thick*inside)*math.cos(a),y+(h-thick*inside)*math.sin(a),zz)
    for i in range(n):
        f,b=z-depth/2,z+depth/2
        A,B,C,D=point(i,0,f),point(i+1,0,f),point(i+1,1,f),point(i,1,f)
        E,F,G,H=point(i,0,b),point(i+1,0,b),point(i+1,1,b),point(i,1,b)
        for quad in ((D,C,B,A),(E,F,G,H),(A,B,F,E),(C,D,H,G)):
            m.face(role,quad)


def basin(m,x,z,w,d,y=.08):
    m.slab("turquoise",x,y,z,w,.085,d,.96)
    m.slab("water",x,y+.085,z,w*.88,.012,d*.83,cut=.12)


def vessel(m,x,z,r,h,y=.08,role="metal"):
    n=(12,8,5)[m.lod]
    m.drum("trim",x,y,z,r*.84,h*.12,r,n=n)
    m.drum(role,x,y+h*.12,z,r,h*.68,r*.93,n=n)
    m.mound("metal" if m.era==2 else "glass",x,y+h*.8,z,r*.93,h*.20,n=n)
    if m.lod==0:
        m.drum("turquoise",x,y+h*.42,z,r*.988,h*.047,n=n)


def conveyor(m,x,z,w,d,y,role="timber"):
    for k in (-1,1):
        m.block("trim",x,.08,z+k*d*.36,w*.67,y-.08,.082)
    m.slab("metal",x,y,z,w,.077,d,.95,cut=.055,underside=True)
    m.block(role,x,y+.077,z,w*.76,.036,d*.92)
    if m.lod==0:
        for k in range(5):
            m.block("ivory",x,y+.113,z+d*(k-2)*.16,w*.72,.014,.031)


def ridge(m,x,z,w,d,h,y=.08):
    """A steep, uncluttered solar sail carried by a pair of triangular end frames."""
    for pos in (z-d*.43,z+d*.43):
        for side in (-1,1):
            m.rod("ivory",(x+side*w*.46,y,pos),(x,y+h,pos),w*.045,n=(8,5,4)[m.lod])
    for side in (-1,1):
        p=[(x,y+h,z-d/2),(x+side*w/2,y+h*.28,z-d/2),
           (x+side*w/2,y+h*.28,z+d/2),(x,y+h,z+d/2)]
        m.face("glass",p[::-1] if side>0 else p)
        m.face("roof",[(a,b-.017,c) for a,b,c in (p if side>0 else p[::-1])])
    if m.lod<2:
        m.rod("gold",(x,y+h,z-d*.53),(x,y+h,z+d*.53),w*.03)


def wheel(m,x,y,z,r,role="metal"):
    m.ring(role,x,y,z,r,r*.75,r*.30,n=(18,10,6)[m.lod])
    spoke_count=(8,5,3)[m.lod]
    for i in range(spoke_count):
        a=math.tau*i/spoke_count
        m.rod("trim",(x+r*.105*math.cos(a),y+r*.105*math.sin(a),z+r*.15),(x+r*.89*math.cos(a),y+r*.89*math.sin(a),z+r*.15),
              r*.075,n=4)
    seal(m,x,y,z-.017,r*.22)


def court(m, entry_x=0, w=1.90, d=1.64, role="fieldstone"):
    """Shallow flood terrace with its first tide stair meeting the south edge."""
    m.slab(role,0,0,.045,w,.08,d,cut=.085)
    front=.045-d/2
    m.stairs(entry_x,0,front-.16,.35,.16,.08)


def cartouche(m,x,y,z,r):
    """A gold sun in a squared turquoise lintel, sized for the actual bearing wall."""
    m.block("turquoise",x,y-r*1.18,z,r*3.15,r*2.36,r*.30)
    m.disc(x,y,z-r*.15,r,boss=m.lod==0)


def shutters(m,x,y,z,w,h,count):
    """Front-facing timber shutters or glazing in separated masonry bays."""
    if m.lod==2:
        return
    bay=w/count
    for i in range(count):
        cx=x-w/2+bay*(i+.5)
        m.block("glass" if m.era>=2 else "turquoise",cx,y,z,bay*.64,h,.026)
        if m.lod==0:
            m.block("trim",cx,y+h*.46,z-.015,bay*.69,h*.095,.019)


def panel(m,x,y,z,w,d,tilt,role="glass"):
    """A glazed collector cassette, complete with the back and frame."""
    with m.transform(translate=(x,y,z),rotate_x=tilt):
        m.box((0,0,0),(w,.065,d),m.color("metal"),material="metal",bottom=True)
        panes=(3,2,1)[m.lod]
        for j in range(panes):
            m.block(role,(j-(panes-1)*.5)*w*.92/panes,.0325,0,
                    w*.88/panes,.013,d*.88)
