"""Ironworker's row houses, folded concrete bungalows, signal lodges and gilt sky courts."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_housesancient import Lodge, generate

PACKAGE='orc_houseslate'


def sill(m,x,z,w,d,h,role,cut,covered=False):
    bottom=0
    if m.lod<2:
        m.shell([(0,w+.017,d+.017,x,z),(.053,w+.014,d+.014,x,z)],['fieldstone'],cut)
        bottom=.053
    m.shell([(bottom,w,d,x,z),(h,w*.994,d*.995,x,z)],[role],cut,lid=not covered)



def distant_portal(m,x,z,y,h,width,body,lining):
    # A single pentagonal inset and continuous returns keep the arched doorway visible at town scale.
    outline=[(-.5,0),(-.5,.75),(0,1),(.5,.75),(.5,0)]
    outer=[(x+a*width,y+b*h,z) for a,b in outline]
    inner=[(x+a*(width-.051),y+.003+b*(h-.028),z-.001) for a,b in outline]
    rear=[(a,b,c+.135) for a,b,c in outer]
    for i in range(4):
        m.face(body,(outer[i],outer[i+1],inner[i+1],inner[i]))
        m.face(body,(outer[i+1],outer[i],rear[i],rear[i+1]))
    m.face(lining,inner)
    for s in (-1,1):
        dx=x+s*.34*width
        by=y+h*.80
        m.path([(dx,by,z+.038,.043),(dx+s*.084,by+.068,z+.029,.029),
                (dx+s*.090,by+.171,z+.027,0)],['ivory','ivory'],3)


def sweep(m,profile,depth,x,z,y,cap,cladding,glazed,paint=False):
    """Extrude a folded slab over a glazed or masonry attic. Each panel meets its neighbour at the fold."""
    left,right=x+profile[0][0],x+profile[-1][0]
    front,back=z-depth+.018,z+depth-.018
    m.face(cladding,[(left,y,front),(right,y,front),(right,y,back),(left,y,back)])
    for i,((a,b),(c,d)) in enumerate(zip(profile,profile[1:])):
        left,right=x+a,x+c
        low,high=y+b,y+d
        front,back=z-depth,z+depth
        thickness=.037
        # Front/back spandrels carry the complete folded roof down onto the enclosing walls.
        for edge,sign in ((front+.018,-1),(back-.018,1)):
            pts=[(right,y,edge),(left,y,edge),(left,low,edge),(right,high,edge)]
            m.face(glazed if i%2==0 else cladding,pts if sign<0 else pts[::-1])
        if i in (0,len(profile)-2):
            xx,yy=(left,low) if i==0 else (right,high)
            side=[(xx,y,front+.018),(xx,y,back-.018),(xx,yy,back-.018),(xx,yy,front+.018)]
            m.face(cladding,side if i==0 else side[::-1])
        sections=[(front,back,cap)]
        if paint:
            mid=z-.12*depth
            span=.17*depth
            sections=[(front,mid-span,cap),(mid-span,mid+span,'cloth'),(mid+span,back,cap)]
        for near,far,role in sections:
            m.face(role,[(left,low+thickness,near),(left,low+thickness,far),
                         (right,high+thickness,far),(right,high+thickness,near)])
        for edge,n in ((front,-1),(back,1)):
            face=[(right,high,edge),(left,low,edge),(left,low+thickness,edge),(right,high+thickness,edge)]
            m.face(cap,face if n<0 else face[::-1])
        for edge,height,n in ((left,low,-1),(right,high,1)):
            if (i==0 and n<0) or (i==len(profile)-2 and n>0):
                face=[(edge,height,front),(edge,height,back),(edge,height+thickness,back),(edge,height+thickness,front)]
                m.face(cap,face if n<0 else face[::-1])
        m.face(cap,[(left,low,front),(right,high,front),(right,high,back),(left,low,back)])


def side_light(m,x,z,y,width,colour,shutter):
    with m.transform(translate=(x,y,z),rotate_y=-90):
        m.block('trim',(0,0,0),(width+.034,.143,.030))
        m.block(colour,(0,0,-.017),(width,.103,.009))
        if m.lod==0:
            for s in (-1,1):
                m.block('metal',(s*width*.23,0,-.025),(.013,.113,.015))
            m.block(shutter,(0,.078,-.011),(width+.052,.023,.067))


def kiln(m,x,z,y,h,v):
    n=(7 if v else 8,6,5)[m.lod]
    radius=.077 if not v else .056
    if m.lod==2:
        m.round('brick',x,y,z,radius,h,radius*.75,n)
        return
    # The flue and collar share one contour and the flue mouth is recessed.
    levels=[(y,radius),(y+h*.68,radius*.82),(y+h*.73,radius*.86),
            (y+h*.76,radius*.80),(y+h*.92,radius*.76),(y+h*.94,radius*.91),
            (y+h,radius*.90),(y+h,radius*.55),(y+h-.037,radius*.55)]
    # Rotational profile may turn down into the throat. Keep a fixed radial frame through the lip.
    rings=[[(x+r*math.cos(2*math.pi*i/n),yy,z+r*math.sin(2*math.pi*i/n))
            for i in range(n)] for yy,r in levels]
    if m.lod==1:
        rings=[rings[i] for i in (0,4,5,6,7,8)]
        roles=['brick','metal','trim','metal','door']
    else:
        roles=['brick','metal','metal','brick','metal','trim','metal','door']
    for a,b,role in zip(rings,rings[1:],roles):
        for i in range(n):
            j=(i+1)%n
            m.face(role,(a[j],a[i],b[i],b[j]))
    m.face('door',rings[-1][::-1])


def industrial(m):
    v=m.v
    x,z=(-.025,.058) if not v else (.015,.046)
    w,d=(.336,.259) if v else (.304,.307)
    eave=.317 if v else .300
    sill(m,x,z,w,d,eave,'brick',.16)
    profile=([(-.373,.031),(-.286,.160),(-.131,.255),(.032,.275),(.199,.221),(.309,.125),(.373,.035)]
             if not v else [(-.398,.051),(-.159,.236),(-.126,.098),(.130,.264),(.161,.105),(.398,.139)])
    if not v and m.lod==2:
        profile=profile[::2]
    sweep(m,profile,d+.048,x,z,eave,'metal','brick','glass' if v else 'brick',not v)
    kiln(m,x-.217 if not v else x-.215,z+.096 if not v else z+.075,.375,
         .405 if not v else .342,v)
    if v:
        kiln(m,x-.052,z+.156,.387,.417,v)
    front=z-d-.089
    m.doorway(x+(.086 if v else -.065),front,.023,.275,.216,body='metal',horns=True)
    if m.lod<2:
        side_light(m,x+w-.003,z-.007,.187,.205,'emissive','brick')
    if m.lod==0:
        # The old hide drum now lives beside a brick worker's boot scraper.
        m.drum(x+(-.19 if v else .242),front-.026,.063,.104)
        scraper_x=x+(.245 if v else -.23)
        for s in (-1,1):
            m.block('metal',(scraper_x+s*.042,.022,front-.042),(.014,.044,.024))
        m.block('timber',(scraper_x,.047,front-.042),(.103,.015,.027))


def concrete(m):
    v=m.v
    x,z=(.015,.060) if v else (-.023,.060)
    w,d=(.341,.263) if v else (.315,.289)
    eave=.291 if v else .317
    sill(m,x,z,w,d,eave,'plaster',.21)
    # One family folded the slab into a butterfly, the other lifted its broad end toward the street.
    profile=([(-.394,.196),(-.174,.090),(.063,.052),(.392,.251)] if not v else
             [(-.399,.068),(-.189,.075),(.146,.230),(.397,.205)])
    if m.lod==2 and not v:
        profile=[profile[0],profile[2],profile[-1]]
    sweep(m,profile,d+.062,x,z,eave,'ivory','glass','glass')
    front=z-d-.084
    if v and m.lod==2:
        distant_portal(m,x+.084,front,.023,.279,.239,'metal','door')
    else:
        m.doorway(x+(.084 if v else -.067),front,.023,.279,.239,body='metal')
    if m.lod<2:
        side_light(m,x+w-.002,z-.011,.174,.244,'glass','trim')
        # The mail drum and its red enamelling are inherited from the older doorstep drums.
        cx=x+(-.225 if v else .218)
        m.round('trim',cx,0,front-.011,.031,.074,.031,5)
        m.round('cloth',cx,.074,front-.011,.059,.077,.061,6)
        if m.lod==0:
            m.round('metal',cx,.151,front-.011,.064,.024,.055,6)
    if m.lod==0:
        # Roof scuppers under the lowest fold discharge at the rear garden.
        px=x+(-.26 if v else .065)
        m.pole('metal',(px,.065,z+d+.020),(px,.408,z+d+.024),.021,.021,6)
        m.pole('metal',(px,.409,z+d+.023),(px,.423,z+d+.053),.026,.026,6)
        m.block('trim',(x+(.084 if v else -.067),.022,front-.013),(.136,.044,.095),taper=.15)


def mast(m,x,y,z,h):
    m.pole('trim',(x,y,z),(x+.026,y+h,z),.021,.010,5)
    m.pole('metal',(x-.067,y+h*.74,z-.008),(x+.102,y+h*.86,z+.008),.011,.010,4)
    if m.lod<2:
        m.round('cloth',x+.025,y+h-.009,z,.017,.019,.011,5)


def receiver(m):
    v=m.v
    x,z=(.017,.056) if v else (-.015,.049)
    w,d=(.349,.247) if v else (.302,.297)
    base=.245 if v else .253
    sill(m,x,z,w,d,base,'ivory',.23 if v else .42)
    cut=.26 if v else .55
    dx=.071 if v else -.047
    y=.565 if v else .585
    corners=[(base-.008,w+.039,d+.040,x,z),(base+.027,w+.047,d+.045,x,z),
             (base+.072,w+.015,d+.012,x,z),(y-.094,w*.88,d*.85,x+dx*.6,z+.022),
             (y-.067,w*.85,d*.82,x+dx*.7,z+.029),(y,w*.72,d*.69,x+dx,z+.041),
             (y+.029,w*.75,d*.72,x+dx,z+.040)]
    mats=['metal','ivory','glass','trim','glass','ivory']
    if m.lod==2:
        corners=[corners[i] for i in (0,2,5,6)]
        mats=['ivory','glass','ivory']
    m.shell(corners,mats,cut)
    # Two different rooflines: a shingled signal stack and a broad slanted solar collector.
    if v:
        with m.transform(translate=(x+dx-.035,y+.055,z+.037),rotate_z=8):
            m.block('glass',(0,0,0),(.360,.068,.292),taper=.12,bottom=True)
        mast(m,x+dx-.164,y+.018,z+.139,.205)
    else:
        m.shell([(y+.029,.139,.131,x+dx,z+.055),(y+.090,.109,.101,x+dx+.019,z+.063)],
                ['metal'],.55)
        mast(m,x+dx+.011,y+.091,z+.072,.170)
    front=z-d-.089
    ex=x+(.077 if v else -.035)
    if m.lod==2:
        distant_portal(m,ex,front,.018,.219,.206,'trim','door')
    else:
        m.doorway(ex,front,.018,.219,.206,body='trim')
    if m.lod<2:
        side_light(m,x+w-.002,z+.013,.139,.159,'glass','metal')
        # A small red family pennon is mounted to the door surround beneath the glazing.
        px=x+(-.230 if v else .200)
        m.block('cloth',(px,.149,z-d+.002),(.087,.123,.028),taper=.18)
    if m.lod==0:
        m.drum(x+(-.245 if v else .250),-.337,.065,.098,head='metal')


def halo(m,center,radius,inner,thickness,n,angle):
    with m.transform(translate=center,rotate_x=angle):
        rings=[[(r*math.cos(2*math.pi*i/n),offset,r*math.sin(2*math.pi*i/n))
                for i in range(n)] for r,offset in
               ((radius,-thickness/2),(radius,thickness/2),(inner,thickness/2),(inner,-thickness/2))]
        roles=['gold','ivory','ascension','gold']
        if m.lod==2:
            rings=[rings[i] for i in (0,1,2)]
            roles=['gold','ivory','ascension']
        for k,role in enumerate(roles):
            a,b=rings[k],rings[(k+1)%len(rings)]
            for i in range(n):
                j=(i+1)%n
                m.face(role,(a[j],a[i],b[i],b[j]))


def sky_court(m):
    v=m.v
    x,z=(.018,.06) if v else (-.017,.049)
    w,d=(.345,.269) if v else (.317,.302)
    cut=.29 if v else .52
    base=.263 if v else .279
    sill(m,x,z,w,d,base,'plaster',cut,covered=m.lod==2)
    dx=.045 if v else -.018
    roof=.521 if v else .551
    rings=[(base-.018,w+.008,d+.009,x,z),(base+.047,w+.048,d+.044,x,z),
           (base+.077,w+.046,d+.043,x,z),(roof-.071,w*.75,d*.79,x+dx,z+.029),
           (roof-.046,w*.69,d*.74,x+dx,z+.031),(roof,.135 if v else .149,.132,x+dx,z+.038)]
    roles=['gold','ivory','ivory','ascension','ivory']
    if m.lod>0:
        rings=[rings[i] for i in (0,1,3,4,5)]
        roles=['gold','ivory','ascension','ivory']
    m.shell(rings,roles,cut)
    cx,cz=x+dx,z+.038
    halo_y=roof+(.170 if v else .146)
    radius=.172 if v else .213
    angle=58 if v else -10
    # Two flared branches cradle the annulus at its actual left and right contact points.
    for s in (-1,1):
        a,b=(cx+s*.096,roof-.012,cz),(cx+s*(radius-.013),halo_y,cz)
        if m.lod>=1:
            m.pole('gold',a,b,.041,.025,3 if m.lod==2 else 4)
        else:
            m.path([(*a,.041),(cx+s*.117,roof+.053,cz,.034),(*b,.025)],
                   ['gold','ivory'],7)
    halo(m,(cx,halo_y,cz),radius,radius-.036,.033,(16,10,7)[m.lod],angle)
    front=z-d-.083
    ex=x+(-.091 if v else .016)
    if m.lod==2:
        distant_portal(m,ex,front,.021,.236,.224,'gold','glass')
    else:
        m.doorway(ex,front,.021,.236,.224,body='gold',lining='glass')
    if m.lod<2:
        side_light(m,x+w-.002,z,.147,.218,'ascension','ivory')
        px=x+(.238 if v else -.198)
        m.round('gold',px,0,front-.010,.052,.062,.034,6)
        m.round('ascension',px,.062,front-.010,.036,.079,.024,6)
    if m.lod==0:
        # A red house seal in a gold mounting marks the main entry even after Ascension.
        px=x+(-.091 if v else .016)
        m.block('gold',(px,.257,front+.025),(.085,.060,.079),taper=.11)
        m.block('cloth',(px,.257,front-.016),(.054,.043,.011),taper=.13)


def build(spec,lod):
    m=Lodge(spec,lod)
    {'industrial':industrial,'modern':concrete,'information':receiver,'ascension':sky_court}[m.age](m)
    return m


if __name__=='__main__':
    generate(PACKAGE,build)
