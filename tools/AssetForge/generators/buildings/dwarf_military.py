"""Clan garrisons: battered ashlar, brass clan marks and coal-lit watch galleries."""
from math import cos, sin, pi
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import ROOT, package_models
from forge import MeshBuilder, publish
from forge.render import contact_sheet

PACKAGE = "dwarf_military"
ERAS = ("ancient", "classical", "industrial", "future")
WHITE = (1, 1, 1)


class Citadel(MeshBuilder):
    def __init__(self, spec, lod):
        super().__init__(spec.id)
        self.lod = lod
        self.era = ERAS.index(spec.band)

    def face(self, points, role):
        self.poly(points, WHITE, material=role)

    def block(self, x, y, z, w, h, d, role, taper=0):
        self.box((x, y+h/2, z), (w, h, d), WHITE, material=role, taper=taper)

    def drum(self, x, y, z, r, h, role, top=None, segments=None):
        self.cylinder((x, y, z), r, h, WHITE, segments=segments or (12, 8, 6)[self.lod],
                      top_radius=top, material=role)

    def course(self, x, z, w, d, y, h, role, cut=.16, batter=0, lid=None):
        # Chamfers are vertical masonry joints. A battered course pulls its top in on all sides.
        def ring(w, d, height):
            a, b = w/2, d/2
            c = min(w, d)*cut
            outline = ((-a+c,-b),(a-c,-b),(a,-b+c),(a,b-c),
                       (a-c,b),(-a+c,b),(-a,b-c),(-a,-b+c))
            if self.lod == 2:
                outline = ((-a,-b),(a,-b),(a,b),(-a,b))
            return [(x+px, height, z+pz) for px,pz in outline]
        low = ring(w, d, y)
        high = ring(w*(1-batter), d*(1-batter), y+h)
        for i in range(len(low)):
            j = (i+1) % len(low)
            self.face((low[j],low[i],high[i],high[j]), role)
        self.face(high[::-1], lid or role)

    def badge(self, x, y, z, w, h, role="banner"):
        # An inset shield with a bevel all around its proud face.
        shape = [(-.49,.46),(.36,.50),(.50,.32),(.45,-.09),(0,-.53),(-.45,-.12)]
        back = [(x+a*w,y+b*h,z) for a,b in shape]
        front = [(x+a*w*.87,y+b*h*.87,z-.025) for a,b in shape]
        for i in range(len(shape)):
            j = (i+1) % len(shape)
            self.face((back[i],back[j],front[j],front[i]), "gold")
        self.face(front, role)
        if self.lod == 0:
            self.face([(x,y+h*.28,z-.026),(x+w*.15,y+.01*h,z-.026),
                       (x,y-.28*h,z-.026),(x-w*.15,y+.01*h,z-.026)],"gold")

    def entry(self, x, z, floor, w, h):
        # The chamfered lintel is a continuous ring around a recessed dark slab.
        def arch(w,h,z):
            return [(x+a*w,floor+b*h,z) for a,b in
                    ((-.5,0),(-.5,.70),(-.31,1),(.31,1),(.5,.70),(.5,0))]
        inner = arch(w*.74,h*.83,z-.036)
        outer = arch(w,h,z-.051)
        rear = arch(w,h,z+.013)
        self.face(inner, "door")
        for i in range(5):
            self.face((outer[i],outer[i+1],inner[i+1],inner[i]), "trim" if i in (0,4) else "accent")
            self.face((rear[i],rear[i+1],outer[i+1],outer[i]), "fieldstone")
        if self.lod < 2:
            self.block(x,floor,z-.037,.012,h*.77,.009,"metal")
            self.block(x-w*.11,floor+h*.4,z-.044,.028,.025,.015,"gold")

    def lantern(self, x, y, z, size):
        # The amber lozenge has a cast saddle and a sloping cap.
        self.block(x,y,z,size*.70,size*.22,size*.65,"trim")
        self.drum(x,y+size*.22,z,size*.36,size*.53,"emissive",size*.29,6)
        self.drum(x,y+size*.75,z,size*.40,size*.22,"gold",size*.12,6)

    def flag(self, x, y, z, h):
        self.block(x,y,z,.025*h,h,.025*h,"trim")
        a,b,c,d=(x,y+h*.96,z),(x+.20*h,y+h*.91,z+.035*h),(x+.42*h,y+h*.82,z-.028*h),(x,y+h*.67,z)
        e=(x+.20*h,y+h*.65,z+.035*h)
        for p in ((a,b,e,d),(b,c,e)):
            self.face(p,"cloth")
            self.face([(u,v,q+.007) for u,v,q in p[::-1]],"cloth")

    def slit(self, x,y,z,w,h):
        self.block(x,y,z,w,h,.023,"door")
        self.block(x,y+h*.15,z-.011,w*.70,h*.64,.012,"emissive")

    def shaft(self,x,z,r,y,h,role):
        self.drum(x,y,z,r,h,role,r*.72)
        self.drum(x,y+h,z,r*.81,h*.12,"trim",r*.74)


def muster_standard(m, x, z, h):
    # A deep-folded clan gonfalon marks the parade side of a garrison.
    m.course(x,z,.215,.195,0,.10,"fieldstone",.19,.12)
    m.drum(x,.10,z,.041,h-.10,"trim",.029,6)
    m.block(x,h-.095,z,.39,.044,.055,"gold")
    m.drum(x,h-.051,z,.043,.106,"gold",0,6)
    width=.30
    edges=(-.50,-.18,.18,.50)
    for i in range(3):
        a,b=edges[i],edges[i+1]
        za=z-.026-(i%2)*.017
        zb=z-.026-((i+1)%2)*.017
        ya=h-.50 if i in (0,2) else h-.40
        top=h-.078
        q=[(x+a*width,top,za),(x+b*width,top,zb),
           (x+b*width,ya,zb),(x+a*width,ya-.025,za)]
        m.face(q,"cloth")
        m.face([(u,v,k+.008) for u,v,k in q[::-1]],"cloth")
    if m.lod<2:
        m.badge(x,h-.235,z-.052,.126,.164,"trim")


def barracks(m):
    e,l = m.era,m.lod
    z=.065
    w=(1.39,1.50,1.47,1.57)[e]
    d=(1.04,1.08,1.16,1.13)[e]
    h=(.48,.63,.61,.54)[e]
    front=z-d/2
    m.course(0,z,w+.11,d+.11,0,.105,"fieldstone",.10)
    m.course(0,z,w,d,.105,h,"wall",.10)
    m.course(0,z,w+.06,d+.05,h+.105,.065,"accent",.10)
    base=h+.17
    if e==0:
        m.course(0,z,w+.09,d+.08,base,.24,"roof",.17,.37)
        m.course(0,z,.76,.54,base+.24,.06,"slate",.16)
    elif e==1:
        m.course(0,z,w+.10,d+.08,base,.14,"roof",.12,.18)
        m.course(0,z,.94,.74,base+.14,.19,"wall",.17,.09)
        m.course(0,z,.93,.72,base+.33,.085,"roof",.17,.12)
    elif e==2:
        m.course(0,z,w+.08,d+.09,base,.16,"roof",.10,.15)
        for x in (-.40,.34):
            m.course(x,z+.04,.50,.70,base+.16,.115,"metal",.11,.13)
        m.shaft(.53,z+.39,.11,base+.04,.48,"brick")
    else:
        m.course(0,z,w+.12,d+.08,base,.12,"roof",.18,.09)
        m.course(.15,z+.02,1.02,.74,base+.12,.18,"glass",.19,.15)
        m.course(.15,z+.02,.94,.66,base+.30,.055,"wall",.19)
    muster_standard(m,-.53,front-.266,.88 if e<2 else .99)
    m.entry(-.075,front-.003,.105,.40,h*.82)
    for sign in (-1,1):
        x=sign*w*.38
        m.course(x,front+.013,.18,.205,.103,h*.80,"fieldstone",.19,.21)
        if l<2:
            m.badge(x,h*.64,front-.101,.16,.19)
        if l==0:
            m.lantern(x,.103+h*.80,front+.009,.115)
    if l<2:
        m.block(-.075,0,front-.17,.44,.056,.175,"fieldstone")
        if e==3:
            m.shaft(-.51,z+.22,.042,base+.105,.27,"metal")
        if e==1:
            m.flag(.14,base+.411,z+.16,.42)
    if l==0:
        for sx in (-1,1):
            with m.transform(translate=(sx*w*.5,.105,z-.003),rotate_y=-sx*90):
                for j in (-1,0,1):
                    m.slit(j*.19,h*.42,-.008,.09,.11)


def bastion(m,x,z,w,d,h):
    m.course(x,z,w,d,0,h*.78,"wall",.22,.09)
    m.course(x,z,w*.97,d*.97,h*.78,h*.12,"trim",.20,-.10)
    m.course(x,z,w*1.07,d*1.07,h*.90,h*.10,"roof",.18)
    for sign in (-1,1):
        m.block(x+sign*w*.33,h,z,w*.21,h*.14,d*.72,"fieldstone",.12)
    if m.lod==0:
        m.slit(x,h*.46,z-d*.49,w*.12,h*.22)
        m.badge(x,h*.78,z-d*.5-.006,w*.38,h*.30)


def castle(m):
    l=m.lod
    m.course(0,0,2.75,2.53,0,.10,"fieldstone",.10)
    # Four curtain walls surround the sunken court. Two disconnected gate legs leave a true passage.
    for s in (-1,1):
        m.course(s*1.09,.01,.18,1.65,.10,.52,"wall",.08)
        m.course(s*.70,-.91,.84,.21,.10,.55,"wall",.10)
    m.course(0,.91,2.20,.22,.10,.59,"wall",.08)
    m.course(0,-.91,.64,.26,.58,.21,"fieldstone",.11)
    for s in (-1,1):
        for t in (-1,1):
            bastion(m,s*1.045,t*.90,.53,.55,.79 if t<0 else .93)
    m.course(0,.28,1.48,1.12,.10,.90,"wall",.15,.06)
    m.course(0,.28,1.52,1.14,1.0,.11,"trim",.14)
    m.course(0,.33,1.20,.94,1.11,.43,"roof",.16,.15)
    m.course(0,.33,1.10,.86,1.54,.095,"accent",.16)
    m.course(-.14,.41,.70,.57,1.635,.23,"wall",.19,.12)
    if l<2:
        for s in (-1,1):
            for x in (-.23,.15):
                m.block(s*1.09,.62,x,.20,.13,.17,"fieldstone",.09)
            for j in range(2):
                m.block(s*(.43+.23*j),.65,-.91,.14,.14,.23,"fieldstone",.09)
            m.lantern(s*.44,.65,-.91,.158)
        m.entry(0,-.283,.10,.40,.56)
        m.badge(0,.736,-1.047,.25,.26)
        m.flag(-.17,1.865,.47,.49)
    if l==0:
        # Short portcullis teeth hang above the passage. The lower half stays open.
        for x in (-.19,-.095,0,.095,.19):
            m.block(x,.46,-.96,.030,.128,.033,"metal",-.10)
        for x in (-.41,0,.41):
            m.slit(x,1.21,-.136,.089,.17)
        for x in (-.34,.34):
            m.badge(x,.85,-.270,.20,.31,"cloth")
        for s in (-1,1):
            for z in (-.36,.28,.74):
                m.course(s*.692,z,.17,.16,.10,.62,"fieldstone",.17,.27)
        for i in range(3):
            m.block(0,.015*i,-1.19+.078*i,.58,.035,.076,"fieldstone")


def palisade(m):
    e,l=m.era,m.lod
    # Unbroken rows of split-heart stakes frame a walk-through gate with a raised crossbar.
    gate=.26 if e==0 else .31
    n=(6,5,3)[l]
    for s in (-1,1):
        m.course(s*(1.42+gate)*.5,0,1.42-gate,.32,0,.128,"fieldstone",.12)
        step=(1.40-gate)/n
        for i in range(n):
            x=s*(gate+(i+.50)*step)
            ht=(.66 if e==0 else .79)+(.035,.088,-.012,.06)[i%4]
            r=step*.548
            seg=(7,5,4)[l]
            m.drum(x,.12,.004*(i%2),r,ht-.12,"timber",r*.92,seg)
            m.drum(x,ht,.004*(i%2),r*.92,.145 if e==0 else .103,"timber" if e==0 else "trim",0,seg)
        for i,yy in enumerate((.31,.51) if l==0 else (.39,)):
            m.block(s*(gate+1.43)/2,yy,-.126,1.43-gate,.075,.065,"trim" if e else "timber")
        if e==1:
            m.course(s*1.31,.004,.264,.379,0,.39,"wall",.16,.17)
        x=s*(gate+.006)
        m.course(x,.006,.20,.35,0,.24,"wall",.14,.09)
        m.block(x,.24,.006,.145,.65+e*.085,.165,"timber")
        if l==0:
            m.lantern(x,.61+e*.085,-.105,.113)
        if l<2:
            m.badge(s*.82,.452,-.168,.17,.205)
    h=.80+e*.085
    m.block(0,h,0,gate*2+.29,.15,.218,"trim",.06)
    if e==0:
        # The gate opening is framed by armoured cheeks. Their copper plate becomes iron.
        for sign in (-1,1):
            x=sign*.365
            m.course(x,.011,.31,.46,.121,.44,"wall",.16,.14)
            m.course(x,.011,.281,.417,.561,.258,"metal",.17,.08)
        m.course(0,.009,1.02,.431,h+.15,.147,"metal",.16,.12)
        if l<2:
            m.course(0,.008,.76,.335,h+.297,.076,"roof",.13,.15)
    if l<2:
        m.badge(0,h+.09,-.113,.156,.19)
        if e:
            m.course(0,.0,.79,.323,h+.15,.092,"roof",.12,.20)


def wall(m):
    l,e=m.lod,m.era
    industrial=e==2
    m.course(0,.016,.99,.48,0,.13,"fieldstone",.10)
    m.course(0,.016,.97,.35,.13,.54 if industrial else .46,"brick" if industrial else "wall",.075,.07)
    y=.67 if industrial else .59
    m.course(0,.016,.995,.45,y,.083,"trim",.09)
    for x in (-.365,0,.365):
        m.course(x,.015,.239,.275,y+.083,.17 if industrial else .192,"roof" if industrial else "wall",.16,.12)
    # The reinforced nose of the rampart carries the clan's heraldic shield.
    m.course(0,-.149,.32 if industrial else .38,.25,0,y*.75,"fieldstone",.17,.23)
    if l<2:
        m.badge(0,y*.47,-.260,.201,.249)
        for x in (-.362,.362):
            if industrial:
                m.block(x,.127,-.159,.058,.51,.063,"metal",.14)
            else:
                m.slit(x,.29,-.156,.043,.13)
    if l==0:
        for s in (-1,1):
            m.lantern(s*.365,y+.083+(.17 if industrial else .192),.006,.075)
            m.course(s*.354,.153,.12,.20,.073,.27,"fieldstone",.12,.16)


def watchtower(m):
    l,e=m.lod,m.era
    h=(.75,.90,1.0)[e]
    d=(.54,.59,.55)[e]
    m.course(0,.015,.76,.71,0,.105,"fieldstone",.17)
    m.course(0,.015,.60,d,.105,h-.105,"wall" if e!=2 else "brick",.17,.19)
    m.entry(0,.012-d*.5,.104,.25,.34)
    m.course(0,.012,.58,.55,h,.112,"trim",.15,-.25)
    m.course(0,.012,.80,.73,h+.112,.083,"roof",.14)
    deck=h+.195
    if e==0:
        for x in (-.23,.23):
            for z in (-.20,.23):
                m.block(x,deck,z,.058,.268,.067,"timber",.09)
        m.course(0,.012,.73,.65,deck+.268,.114,"roof",.16,.38)
        m.drum(0,deck,.02,.086,.115,"metal",.112)
        m.drum(0,deck+.115,.02,.085,.13,"forge",.029)
    elif e==1:
        m.course(0,.045,.437,.40,deck,.19,"wall",.19)
        m.course(0,.045,.45,.43,deck+.19,.07,"trim",.16)
        m.drum(0,deck+.26,.045,.144,.082,"gold",.191)
        m.drum(0,deck+.342,.045,.136,.20,"forge",.032)
        for s in (-1,1):
            m.block(s*.292,deck,.012,.083,.118,.44,"wall",.10)
    else:
        m.course(0,.02,.63,.58,deck,.207,"glass",.20,.08)
        m.course(0,.02,.67,.62,deck+.207,.065,"roof",.19)
        m.drum(-.07,deck+.272,.044,.064,.132,"metal",.056)
        with m.transform(translate=(-.07,deck+.399,.044),rotate_x=-74):
            m.drum(0,-.08,0,.095,.18,"metal",.114)
            m.drum(0,.10,0,.100,.013,"emissive",.094)
    if l<2:
        m.badge(0,h*.83,-.234,.167,.19)
        for s in (-1,1):
            m.course(s*.242,-.110,.146,.174,0,h*.49,"fieldstone",.13,.36)
        if e==1:
            m.flag(-.23,deck,.10,.405)
    if l==0:
        for s in (-1,1):
            with m.transform(translate=(s*.257,0,.018),rotate_y=-90*s):
                m.slit(0,h*.52,-.022,.079,.17)
        # A riveted rear ladder reaches the watch deck. Its rungs clear the battered shaft.
        for xx in (-.085,.085):
            m.block(xx,.102,.298,.022,h+.06,.034,"trim")
        for j in range(5):
            m.block(0,.16+j*(h-.08)/5,.319,.188,.023,.034,"metal")
        m.lantern(.214,.105,-.236,.11)
        m.block(0,0,-.348,.27,.048,.12,"fieldstone")


def scanner(m,x,y,z,future):
    # An inclined concave reflector with a turned rim and a rear mounting boss.
    m.drum(x,y,z,.151,.146,"trim",.102)
    m.block(x,y+.146,z,.107,.23,.135,"metal")
    with m.transform(translate=(x,y+.372,z),rotate_y=-14 if future else 17,rotate_x=-45):
        n=(14,9,7)[m.lod]
        r=.35 if future else .31
        m.drum(0,-.065,0,r*.23,.065,"metal",r*.39,n)
        for i in range(n):
            a,b=2*pi*i/n,2*pi*(i+1)/n
            def p(t,rad,yy):
                return (cos(t)*rad,yy,sin(t)*rad)
            m.face((p(b,r*.39,0),p(a,r*.39,0),p(a,r,.096),p(b,r,.096)),"metal")
            m.face((p(b,r,.096),p(a,r,.096),p(a,r,.117),p(b,r,.117)),"gold")
            m.face((p(a,r*.20,.029),p(b,r*.20,.029),p(b,r,.117),p(a,r,.117)),"glass" if future else "ivory")
        m.drum(0,.013,0,r*.21,.19,"trim",r*.045,n)
        if m.lod==0:
            m.drum(0,.203,0,.026,.031,"emissive",.017,6)


def checkpoint(m,future):
    # A staffed armoured lodge anchors the lifting barrier on the edge of the vehicle lane.
    x,z=-.86,-.90
    m.course(x,z,.68,.47,.09,.23,"wall",.16,.08)
    m.course(x,z,.58,.40,.32,.25,"glass" if future else "door",.14,.06)
    m.course(x,z,.68,.47,.57,.103,"metal",.16,.11)
    m.course(x+.30,z-.01,.21,.27,.09,.43,"trim",.12,.08)
    with m.transform(translate=(x+.295,.476,z-.100),rotate_z=37 if future else 43):
        # Alternating panels terminate joint-to-joint, including the squared counterweight.
        m.block(-.075,-.058,0,.17,.116,.101,"metal")
        panels=(6,4,2)[m.lod]
        length=.828/panels
        for j in range(panels):
            m.block(.010+(j+.5)*length,-.047,0,length,.094,.084,
                    ("ivory","banner" if future else "cloth")[j%2])
    m.badge(x,.361,z-.219,.30,.31,"cloth")
    if m.lod<2:
        m.lantern(x-.163,.673,z,.145)
    if m.lod==0:
        for px in (-.18,.18):
            m.block(x+px,.326,z-.196,.026,.232,.043,"metal")


def base(m):
    l=m.lod
    future=m.era==3
    if future:
        return landing_command(m)
    m.course(0,0,2.69,2.48,0,.09,"fieldstone",.13)
    x,z=(-.37,.37) if future else (-.32,.45)
    w,d=(1.53,1.34) if future else (1.56,1.14)
    h=.69 if future else .55
    m.course(x,z,w,d,.09,h,"wall",.17,.10)
    m.course(x,z,w*1.01,d*1.01,.09+h,.107,"trim",.17)
    m.course(x+.045,z+.007,w*.79,d*.73,h+.197,.22,"glass" if future else "roof",.17,.13)
    m.course(x+.045,z+.007,w*.74,d*.66,h+.417,.065,"roof",.16)
    sensor_floor=h+.482
    if future:
        # The new array stands atop a dedicated, outward-stepped communications drum.
        m.drum(x-.04,sensor_floor,z+.10,.265,.19,"metal",.203)
        m.drum(x-.04,sensor_floor+.19,z+.10,.221,.115,"glass",.263)
        m.drum(x-.04,sensor_floor+.305,z+.10,.283,.054,"gold",.26)
        sensor_floor+=.359
    scanner(m,x-.04,sensor_floor,z+.10,future)
    m.entry(x-.07,z-d*.49,.091,.45,.40)
    # A second hardened hall with two shutters faces the vehicle court.
    bx=.77
    m.course(bx,.19,.72,1.47,.09,.38 if future else .32,"roof",.12,.07)
    by=.47 if future else .41
    m.course(bx,.19,.79,1.49,by,.12,"wall",.12,.20)
    m.block(bx,.092,-.549,.47,.26,.036,"metal")
    if l<2:
        for j in range(3):
            m.block(bx,.133+.071*j,-.568,.448,.022,.013,"trim")
        m.badge(x+w*.30,h*.65,z-d*.48,.19,.24)
        if future:
            m.shaft(bx+.08,.59,.073,by+.12,.33,"metal")
        else:
            m.flag(bx-.12,by+.12,.67,.40)
    checkpoint(m,future)
    m.course(.91,-.89,.54,.23,.09,.19 if future else .15,"fieldstone",.12,.10)
    if l<2:
        m.lantern(.77,.28 if future else .24,-.87,.14)
    if l==0:
        for s in (-1,1):
            m.course(bx+s*.302,-.485,.087,.184,.09,.23,"trim",.16,.12)
            for j in (-1,0,1):
                m.block(bx+s*.302,.14+j*.035,-.579,.051,.014,.012,"gold")
        for j in range(4):
            m.block(bx,by+.12,.29+j*.085,.32,.06,.038,"metal")
        for j in range(3):
            m.block(x-.19+j*.19,.091,-.64,.071,.006,.27,"ivory")
        for j in (-1,1):
            m.slit(x+j*.48,h*.49,z-d*.48,.11,.094)


def landing_command(m):
    m.course(0,0,2.73,2.54,0,.108,"fieldstone",.19)
    # Split command blocks overlook a sunken landing court. The observation bridge spans the gap.
    for x,h in ((-.67,1.03),(.578,1.32)):
        m.course(x,.516,.789,.979,.104,h,"wall",.21,.19)
        m.course(x,.516,.721,.898,.103+h,.199,"glass",.17)
        m.course(x,.516,.795,.968,.294+h,.127,"ivory",.20,.055)
        m.entry(x,.030,.115,.427,.51)
        if m.lod<2:
            m.badge(x,.813,.035,.28,.312)
        if m.lod==0:
            m.shaft(x+.047,.686,.089,.415+h,.23,"metal")
            for s in (-1,1):
                m.slit(x+s*.247,.674,.044,.086,.14)
    m.course(-.051,.621,.879,.381,.805,.224,"glass",.14)
    m.course(-.051,.621,.902,.416,1.023,.073,"trim",.12)
    m.drum(-.021,.105,-.555,.588,.064,"metal",.570,segments=(16,10,7)[m.lod])
    # Three raised landing marks frame an uninterrupted central manoeuvring surface.
    for i in range(3):
        with m.transform(translate=(-.021,.167,-.555),rotate_y=i*120):
            m.block(0,0,-.373,.29,.017,.067,"ivory")
    for x in (-1.007,.99):
        m.course(x,-.502,.367,1.138,.101,.312,"wall",.18,.11)
        m.course(x,-.502,.382,1.159,.410,.079,"trim",.15)
        if m.lod<2:
            m.lantern(x,.483,-.881,.151)
    if m.lod==0:
        for x in (-1.008,.99):
            for z in (-.574,-.378,-.182):
                m.block(x,.485,z,.245,.045,.062,"metal")


BUILDERS = dict(Barracks=barracks, Castle=castle, MilitaryBase=base,
                Palisade=palisade, StoneWall=wall, Watchtower=watchtower)


def build(spec,lod):
    m=Citadel(spec,lod)
    BUILDERS[spec.building_type](m)
    return m


if __name__ == "__main__":
    views=[]
    for spec in package_models(PACKAGE):
        levels=[build(spec,lod) for lod in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        for age,path in entry["age_previews"].items():
            views.append((spec.building_type,age,ROOT/path))
        print(spec.id,*entry["lod_triangles"],flush=True)
    folder=views[0][2].parent
    contact_sheet([path for _,_,path in views],folder/"_contact.png",cols=6,cell=160,
                  labels=[name+" "+age for name,age,_ in views])
    for age in sorted({age for _,age,_ in views}):
        tiles=[(name,path) for name,row,path in views if row==age]
        contact_sheet([path for _,path in tiles],folder/f"sheet_{age}.png",
                      cols=min(6,len(tiles)),cell=144,labels=[name for name,_ in tiles])
