"""Elven sacred architecture and ceremonial groves."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_food import Grove, TAU, publish_package

PACKAGE="elf_religion"


def crescent(m,x,y,z,r,role="accent",turn=0):
    """A cast crescent with tapered horns, a thick walkable-looking edge and recessed back."""
    n=(16,10,6)[m.lod]
    outer,inner=[],[]
    with m.transform(translate=(x,y,z),rotate_y=turn):
        for i in range(n+1):
            a=math.radians(139+262*i/n)
            width=.04+.26*math.sin(math.pi*i/n)**1.13
            outer.append((r*math.cos(a),r*math.sin(a)))
            inner.append((r*(1-width)*math.cos(a),r*(1-width)*math.sin(a)))
        for i in range(n):
            j=i+1
            face=[outer[i],outer[j],inner[j],inner[i]]
            near=[(u,v,-r*.10) for u,v in face]
            far=[(u,v,r*.10) for u,v in face]
            m.face(role,near[::-1])
            m.face("trim",far)
            for k in (0,2):
                m.face(role,[near[k],near[k+1],far[k+1],far[k]])
            if i==0:
                m.face(role,[near[3],near[0],far[0],far[3]])
            if i==n-1:
                m.face(role,[near[1],near[2],far[2],far[1]])


def arch(m,x,y,z,rx,height,depth,role="wall",width=.12):
    """Paired ribs grow from a wide shoe to a shared pointed keystone."""
    for side in (-1,1):
        pts=[(x+side*rx,y,z),(x+side*rx,y+.10*height,z),
             (x+side*rx*.85,y+.55*height,z+depth*.18),
             (x+side*rx*.48,y+.82*height,z+depth*.56),(x,y+height,z+depth)]
        if m.lod==2:
            pts=[pts[i] for i in (0,1,2,4)]
        radii=[rx*width*f for f in ([1.22,1.04,.90,.72,.55] if m.lod<2 else [1.22,1.04,.89,.55])]
        m.limb(role,pts,radii)


def offering(m,x,y,z,scale):
    # A blossom on a stone dish. The lit central wick touches the bowl.
    m.disk("trim",x,y,z,.084*scale,.080*scale,.052*scale,.79)
    m.disk("accent",x,y+.050*scale,z,.075*scale,.072*scale,.024*scale,1.20)
    m.lump("emissive",(x,y+.089*scale,z),(.042*scale,.039*scale,.040*scale),5)


def totem(m):
    m.disk("metal",0,0,.054,.322,.277,.082,.91)
    # A grown fork holds a moon. One coiled branch also carries a pair of prayer chimes.
    m.limb("roof",[(0,.076,.060),(-.025,.24,.068),(.016,.449,.06),(-.02,.64,.06)],
           [.133,.083,.07,.052])
    for s in (-1,1):
        m.limb("timber",[(.001,.356,.058),(s*.106,.493,.052),(s*.163,.673,.038)],
               [.067,.043,.022])
        m.leaf("leaf",(s*.093,.49,.05),(s*.294,.618,.084),.081,.067)
    crescent(m,-.018,.799,.053,.168)
    if m.lod<2:
        m.limb("trim",[(.02,.404,.043),(.219,.503,.027),(.323,.481,-.025)],[.035,.025,.018])
        for j in range(2):
            x=.257+.052*j
            z=.008-.026*j
            m.limb("timber",[(x,.495-.009*j,z),(x,.395-.034*j,z)],[.007,.007],4)
            m.lump("emissive",(x,.375-.034*j,z),(.026,.04,.023),5)
    m.disk("plaster",0,0,-.205,.168,.127,.060,.91)
    offering(m,.011,.060,-.218,.78)


def shrine(m):
    m.disk("fieldstone",0,0,.014,.384,.351,.073,.95)
    m.disk("wall",0,.071,.023,.306,.278,.044,.94)
    # Prayer niche, open to the south, with small bowls under the arch.
    arch(m,0,.102,.058,.221,.47+.077*m.era,.059,
         "timber" if m.era==0 else "wall",.195)
    h=.57+.077*m.era
    m.crown(.003,h,.109,.296,.252,.215+.028*m.era,"roof",5)
    m.disk("trim",0,.108,.052,.096,.09,.131,.69)
    crescent(m,0,.365,.048,.135)
    if m.era>=2:
        m.disk("glass",0,.22,.12,.074,.077,.247,.70)
    if m.lod<2:
        offering(m,-.126,.109,-.116,.71)
        offering(m,.132,.109,-.111,.65)
        m.leaf("leaf",(-.21,.219,.067),(-.304,.332,.13),.052,.039)
    m.disk("wall",0,0,-.295,.167,.113,.044,.93)


def temple(m):
    # A fluted round sanctuary sits behind a high moon gate and open offering steps.
    h=(.62,.79,.87,.98)[m.era]
    m.disk("fieldstone",0,0,.058,.800,.776,.101,.96)
    m.hall(.006,.098,.214,.505,.416,h,.421+.045*m.era,
           "glass" if m.era==3 else "wall")
    arch(m,0,.075,-.387,.337,.77+.091*m.era,.056,"trim" if m.era==0 else "wall",.177)
    top=.075+.77+.091*m.era
    crescent(m,0,top+.113,-.331,.128)
    m.disk("wall",0,0,-.867,.303,.129,.055,.99)
    m.disk("trim",0,0,-.758,.282,.141,.108,.97)
    if m.lod<2:
        for s in (-1,1):
            m.limb("trim",[(s*.491,.102,.142),(s*.523,.45,.148),(s*.341,h+.16,.204)],
                   [.061,.047,.029])
            offering(m,s*.426,.095,-.31,.88)
    if m.era>=2:
        for s in (-1,1):
            m.limb("accent",[(s*.47,h*.70,.18),(s*.60,h*.92,.22),
                             (s*.68,h+.20,.25)],[.048,.036,.025])
            m.leaf("glass",(s*.675,h+.18,.25),(s*.846,h+.47,.29),.121,.071)


def lancet(m,x,y,z,w,h,role):
    shape=[(-.50,0),(-.50,.54),(-.30,.79),(0,1),(.30,.79),(.50,.54),(.50,0)]
    for scale,base,offset,mat in ((1,0,0,"trim"),(.72,.06,-.009,role)):
        m.face(mat,[(x+u*w*scale,y+h*(v*scale+base),z+offset) for u,v in shape])


def reveal(m,w,h,depth):
    # The pointed aperture frame has actual jamb depth where it spans the next drum facet.
    coords=[(-.5,0),(-.5,.53),(-.37,.77),(0,1),(.37,.77),(.5,.53),(.5,0)]
    front=[(u*w,v*h,0) for u,v in coords]
    for i,a in enumerate(front):
        b=front[(i+1)%len(front)]
        m.face("trim",[a,(a[0],a[1],depth),(b[0],b[1],depth),b])


def spire(m,x,z,r,h,role):
    n=(10,7,5)[m.lod]
    m.disk("wall",x,0,z,r*1.05,r*1.05,.105*h,.91,n)
    m.disk(role,x,.098*h,z,r,r,.54*h,.79,n)
    m.disk("accent",x,.628*h,z,r*.82,r*.82,.025*h,1.08,n)
    m.crown(x,.647*h,z,r*.989,r*.989,h*.32,"roof",4)
    if m.lod<2:
        tangent=r*math.cos(math.pi/n)
        slope=.21*tangent/(.54*h)
        depth=z-tangent+.148*h*slope-.009
        with m.transform(translate=(x,.246*h,depth),rotate_x=math.degrees(math.atan(slope))):
            reveal(m,.73*r,.31*h,.17*r)
            m.portal(0,0,0,.73*r,.31*h,"glass")


def cathedral(m):
    # Three silhouettes communicate the nave, flanking bell towers and lofty crossing.
    h=1.19+.135*(m.era-1)
    m.disk("fieldstone",0,0,.025,1.04,1.181,.099,.962, (16,10,7)[m.lod])
    m.disk("wall",0,.097,.175,.627,.829,h-.092,.98,(14,10,6)[m.lod])
    m.crown(0,h,.175,.758,.986,.71+.12*(m.era-1),"roof",6)
    for s in (-1,1):
        spire(m,s*.55,-.404,.227,1.74+.17*(m.era-1),"brick" if m.era==2 else "wall")
    # The polygonal narthex carries the rose and deep front portal.
    n=(12,8,6)[m.lod]
    m.disk("wall",0,.089,-.528,.337,.372,.836,.938,n)
    tangent=.372*math.cos(math.pi/n)
    slope=tangent*.062/.836
    facade_z=-.528-tangent-.009
    with m.transform(translate=(0,.091,facade_z),rotate_x=math.degrees(math.atan(slope))):
        reveal(m,.363,.538,.094)
        m.portal(0,0,0,.363,.538)
    m.crown(0,.921,-.529,.373,.405,.232,"roof",4)
    with m.transform(translate=(0,.747,facade_z+.658*slope),rotate_x=-90+math.degrees(math.atan(slope))):
        m.disk("accent",0,-.064,0,.151,.151,.092,1)
        m.disk("glass",0,.027,0,.117,.117,.013,1)
        if m.lod<2:
            for j in range(5):
                a=TAU*j/5
                m.limb("accent",[(0,.042,0),(.103*math.cos(a),.043,.103*math.sin(a))],[.013,.009],4)
    m.disk("wall",0,0,-.888,.316,.178,.052,.96)
    m.disk("trim",0,.050,-.832,.287,.152,.045,.95)
    if m.lod<2:
        for s in (-1,1):
            m.limb("trim",[(s*.866,.073,.238),(s*.833,.531,.244),(s*.567,1.01,.25)],
                   [.089,.062,.049])
    if m.era==3:
        m.limb("accent",[(.079,h+.96,.329),(.145,h+1.09,.368),(.221,h+1.19,.397)],
               [.052,.036,.029])
        m.leaf("glass",(.208,h+1.174,.394),(.581,h+1.37,.526),.141,.091)


def cloister(m):
    # A horseshoe of actual rooms surrounds an open medicinal garden. The entry bay is missing by design.
    n=(16,12,8)[m.lod]
    a0=math.radians(-59)
    sweep=math.radians(298)
    h=.39+.072*(m.era-1)
    def at(i,r,y):
        a=a0+sweep*i/n
        return (r*math.cos(a),y,.02+r*.93*math.sin(a))
    for i in range(n):
        j=i+1
        outer,inner=.825,.490
        m.face("wall",[at(j,outer,0),at(i,outer,0),at(i,outer,h),at(j,outer,h)])
        m.face("wall",[at(i,inner,0),at(j,inner,0),at(j,inner,h),at(i,inner,h)])
        crest=h+.186+.018*math.sin(TAU*i/n)
        next_crest=h+.186+.018*math.sin(TAU*j/n)
        m.face("roof",[at(j,outer+.041,h-.01),at(i,outer+.041,h-.01),
                       at(i,.672,crest),at(j,.672,next_crest)])
        m.face("leaf",[at(i,inner-.033,h-.014),at(j,inner-.033,h-.014),
                       at(j,.672,next_crest),at(i,.672,crest)])
        for k,reverse in ((0,True),(n,False)):
            if (k==0 and i==0) or (k==n and j==n):
                face=[at(k,inner,0),at(k,outer,0),at(k,outer,h),at(k,.672,h+.186),at(k,inner,h)]
                m.face("trim",face[::-1] if reverse else face)
        if m.lod<2 and i%2==0:
            # Window bays are tangent to the inside walk's outer wall.
            a=a0+sweep*(i+.5)/n
            r=outer*math.cos(sweep/(2*n))+.007
            with m.transform(translate=(r*math.cos(a),.094,.02+r*.93*math.sin(a)),
                             rotate_y=-90-math.degrees(a)):
                lancet(m,0,0,0,.098,.194,"glass")
    spire(m,-.278,.50,.205,.888+.121*(m.era-1),"glass" if m.era==3 else "wall")
    arch(m,0,0,-.583,.329,.426,-.013,"trim",.15)
    m.disk("fieldstone",0,0,-.001,.279,.261,.069,.92)
    m.disk("water",0,.067,-.001,.234,.213,.014,.94)
    m.disk("trim",0,.076,-.001,.051,.051,.161,.65)
    crescent(m,0,.317,-.001,.086)
    if m.lod<2:
        offering(m,.169,.062,-.075,.59)


def build(spec,lod):
    m=Grove(spec,lod)
    {"SpiritTotem":totem,"Shrine":shrine,"Temple":temple,"Cathedral":cathedral,
     "Monastery":cloister}[spec.building_type](m)
    return m


if __name__=="__main__":
    publish_package(PACKAGE,build)
