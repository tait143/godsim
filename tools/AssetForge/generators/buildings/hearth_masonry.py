"""Masonry, folded eaves and inset joinery for small dwellings and council halls."""
from math import cos, pi, sin


def terrace(m, role, x, z, w, d, low, high, *, taper=1, shift=(0,0), cut=.09):
    """Two clipped plan rings, with optional corbel or lean between storeys."""
    def ring(width, depth, cx, cz, y):
        a,b=width/2,depth/2
        c=min(width,depth)*cut if m.lod==0 else 0
        outline=((-a+c,-b),(a-c,-b),(a,-b+c),(a,b-c),(a-c,b),(-a+c,b),
                 (-a,b-c),(-a,-b+c)) if c else ((-a,-b),(a,-b),(a,b),(-a,b))
        return [(cx+u,y,cz+v) for u,v in outline]
    lo=ring(w,d,x,z,low)
    hi=ring(w*taper,d*taper,x+shift[0],z+shift[1],high)
    for i,a in enumerate(lo):
        j=(i+1)%len(lo)
        m.face(role,(lo[j],a,hi[i],hi[j]))
    m.face(role,hi[::-1])
    if low>0:
        m.face(role,lo)


def profile(m, role, x, z, w, d, y, heights, end='wall'):
    """Extrude a front-to-back roof with arbitrary, piecewise linear ridges."""
    front=z-d/2
    back=z+d/2
    for (u,h),(v,k) in zip(heights,heights[1:]):
        left=x+u*w; right=x+v*w
        a=(left,y+h,front); b=(right,y+k,front)
        c=(right,y+k,back); e=(left,y+h,back)
        m.face(role,(a,e,c,b))
        m.face(end,((right,y,front),(left,y,front),a,b))
        m.face(end,((left,y,back),(right,y,back),c,e))
    for (u,h),side in ((heights[0],-1),(heights[-1],1)):
        points=[(x+u*w,y,front),(x+u*w,y,back),(x+u*w,y+h,back),(x+u*w,y+h,front)]
        m.face(role,points if side<0 else points[::-1])
    m.face(role,((x-w/2,y,front),(x+w/2,y,front),(x+w/2,y,back),(x-w/2,y,back)))


def roundwork(m,role,x,z,r,low,high,top=None,segments=(12,8,6),depth=1):
    with m.transform(translate=(x,low,z),scale=(1,1,depth)):
        m.cylinder((0,0,0),r,high-low,m.color(role),segments=segments[m.lod],
                   top_radius=top,material=role,cap_bottom=low>0)


def block(m,role,x,z,w,d,low,high):
    m.box((x,(low+high)/2,z),(w,high-low,d),m.color(role),material=role,bottom=low>0)


def pane(m,x,z,y,w,h,sill='trim',light='emissive'):
    """A shallow cased opening, with a lintel and mullion on the nearest mesh."""
    if m.lod==2:
        return
    block(m,sill,x,z-.012,w,.035,y,y+h)
    m.face(light,((x+w*.405,y+h*.13,z-.031),(x-w*.405,y+h*.13,z-.031),
                  (x-w*.405,y+h*.87,z-.031),(x+w*.405,y+h*.87,z-.031)))
    if m.lod==0:
        block(m,sill,x,z-.034,w*.076,.011,y+h*.10,y+h*.90)
        block(m,sill,x,z-.021,w*1.16,.060,y-.013,y+.007)


def flank(m,x,z,y,w,h,**roles):
    with m.transform(translate=(x,y,z),rotate_y=-90):
        pane(m,0,0,0,w,h,**roles)


def gable(m,role,x,z,w,d,y,h):
    m.gable_roof((x,y,z),w,d,h,m.color(role),overhang=0,material=role)


def dome(m,role,x,z,y,r,rise):
    m.dome((x,y,z),r,m.color(role),rings=(4,2,1)[m.lod],segments=(14,9,6)[m.lod],
           squash=rise/r,material=role)


def steps(m,x,front,w,rise,run,role='fieldstone'):
    n=(5,3,1)[m.lod]
    if n==1:
        m.wedge((x,0,front-run/2),w,run,rise,m.color(role),material=role)
        return
    for j in range(n):
        d=run/n
        block(m,role,x,front-run+(j+.5)*d,w-.006*j,d,0,rise*(j+1)/n)


def gallery(m,role,x,z,w,d,y,height,count=3):
    """Recessed open loggia. Piers, capitals and cornice retain the roof load path."""
    for i in range(count):
        cx=x+w*(i/(count-1)-.5)*.84
        radius=min(w/(count*7),d*.17)
        roundwork(m,role,cx,z-d*.31,radius,y,y+height,top=radius*.86,
                  segments=(8,5,4))
        if m.lod==0:
            roundwork(m,'trim',cx,z-d*.31,radius*1.27,y+height*.80,y+height*.98,
                      segments=(8,5,4))
    terrace(m,'trim',x,z,w,d,y+height,y+height+.043)


def curved_course(m,x,z,start,end,section,roles,segments):
    """Sweep a radius/elevation profile around an open arc; retain both end walls."""
    n=segments[m.lod]
    rings=[]
    for i in range(n+1):
        angle=start+(end-start)*i/n
        rings.append([(x+r*cos(angle),y,z+r*sin(angle)) for r,y in section])
    m.face(roles[-1],rings[0][::-1])
    m.face(roles[-1],rings[-1])
    for left,right in zip(rings,rings[1:]):
        for j,role in enumerate(roles):
            k=(j+1)%len(roles)
            m.face(role,(left[j],left[k],right[k],right[j]))
