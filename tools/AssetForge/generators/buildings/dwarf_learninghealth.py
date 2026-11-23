"""Dwarven civic workshops: reading vaults, public baths and guild infirmaries."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.dwarf_housesancient import ClanHouse
from forge.render import contact_sheet

PACKAGE = "dwarf_learninghealth"
ERAS = ("ancient", "classical", "industrial", "future")


class Stonework(ClanHouse):
    """Architectural courses, dressed portals and wrought fittings in local tile space."""

    def __init__(self, spec, lod):
        super().__init__(spec, lod)
        self.era = ERAS.index(spec.band)

    def slab(self, mat, x, z, w, d, low, high, cut=.08, inset=0):
        self.course(mat, x, z, w, d, low, high, corner=cut, inset=inset)

    def drum(self, mat, x, y, z, r, h, top=None, n=None):
        self.cylinder((x,y,z), r, h, self.color(mat), segments=n or (12,8,6)[self.lod],
                      top_radius=top, material=mat)

    def crown(self, mat, x, y, z, r, squash, n=None):
        self.dome((x,y,z), r, self.color(mat), rings=(3,2,1)[self.lod],
                  segments=n or (12,8,6)[self.lod], squash=squash, material=mat)

    def ring(self, mat, x, y, z, outer, inner, h, n=None):
        """Open polygonal annulus with rim, inner return and underside."""
        n = n or (14,10,6)[self.lod]
        def point(i,r,dy):
            a = 2*math.pi*i/n
            return (x+r*math.cos(a), y+dy, z+r*math.sin(a))
        for i in range(n):
            j = i+1
            a,b,c,d = (point(i,outer,0),point(j,outer,0),point(j,outer,h),point(i,outer,h))
            e,f,g,k = (point(i,inner,0),point(j,inner,0),point(j,inner,h),point(i,inner,h))
            self.face(mat,(b,a,d,c))
            self.face(mat,(d,k,g,c))
            self.face(mat,(e,f,g,k))
            self.face(mat,(a,b,f,e))

    def plaque(self, mat, x, y, z, w, h):
        """An inset cut-corner panel, facing the approaching townspeople."""
        c = min(w*.16,h*.20)
        self.face(mat,[(x-w/2+c,y-h/2,z),(x-w/2,y-h/2+c,z),(x-w/2,y+h/2-c,z),
                       (x-w/2+c,y+h/2,z),(x+w/2-c,y+h/2,z),(x+w/2,y+h/2-c,z),
                       (x+w/2,y-h/2+c,z),(x+w/2-c,y-h/2,z)])

    def portal(self, x, y, z, w, h):
        self.plaque("trim",x,y+h*.5,z-.012,w,h)
        self.plaque("door",x,y+h*.46,z-.016,w*.72,h*.86)
        if self.lod < 2:
            self.bent_rail("gold",[(x-w*.43,y+h*.72),(x-w*.27,y+h*.94),
                                   (x+w*.27,y+h*.94),(x+w*.43,y+h*.72)],z-.026,.027)
            self.plaque("emissive",x,y+h*.74,z-.019,w*.35,h*.07)
        if self.lod == 0:
            self.block("metal",(x+.055,y+h*.43,z-.025),(.038,.027,.029))

    def hall(self, x, z, w, d, h, *, door=True, windows=2, wall="wall", cap="roof"):
        """Half-buried hall with stepped coping and battered stone supports."""
        e = self.era
        self.slab("fieldstone",x,z,w+.04,d+.04,0,.105,.085,.018)
        self.slab(wall,x,z,w,d,.105,h,.09)
        self.slab(cap,x,z,w+.065,d+.065,h,h+.10,.11,.020)
        front = z-d/2
        if door:
            self.portal(x,.082,front,min(.34,w*.48),min(.40,h*.83))
        if self.lod < 2:
            self.slab("gold" if e<2 else "metal",x,z,w+.021,d+.021,h-.044,h-.016,.085)
            for i in (-1,1):
                if windows and w>.65:
                    self.plaque("trim",x+i*w*.30,h*.59,front-.008,w*.165,h*.33)
                    self.plaque("glass" if e>1 else "emissive",x+i*w*.30,h*.59,front-.011,w*.132,h*.235)
                self.block("fieldstone" if e<2 else "trim",(x+i*(w/2-.04),h*.32,front+.045),
                           (.14,h*.64,.21),taper=.27)
            if self.lod == 0:
                for side in (-1,1):
                    with self.transform(translate=(x+side*w/2,0,z),rotate_y=-90*side):
                        for iz in range(windows):
                            u=(iz-(windows-1)/2)*d*.35
                            self.plaque("trim",u,h*.58,-.012,d*.20,h*.33)
                            self.plaque("glass" if e>=2 else "emissive",u,h*.58,-.015,d*.15,h*.23)
        return h+.10

    def rune(self, x, y, z, size):
        self.plaque("accent",x,y,z,size,size)
        runes=[(-.26,-.25),(-.26,.22),(0,-.05),(.23,.24),(.23,-.25)]
        for i in range(len(runes)-1):
            a,b=runes[i:i+2]
            # Different stations prevent the stems sharing faces with the diagonals.
            self.beam("gold",(x+size*a[0],y+size*a[1],z-.009-i*.001),
                      (x+size*b[0],y+size*b[1],z-.009-i*.001),size*.075)

    def stairway(self,x,z,w,h,run):
        steps=(3,2,1)[self.lod]
        for i in range(steps):
            rise=h*(i+1)/steps
            self.block("fieldstone",(x,rise/2,z-run+(i+.5)*run/steps),
                       (w+.018*(steps-i),rise,run/steps))

    def book(self,x,y,z,size,pages="ivory"):
        # The two page blocks meet in a recessed V. The covers project at fore-edge and spine.
        with self.transform(translate=(x,y,z),scale=size):
            for s in (-1,1):
                with self.transform(scale=(s,1,1)):
                    for mat,w,half_d,base,thick in (("trim",.433,.245,0,.035),(pages,.408,.224,.035,.056)):
                        lo=[(0,base,-half_d),(w,base+w*.24,-half_d),
                            (w,base+w*.24,half_d),(0,base,half_d)]
                        hi=[(u,v+thick,t) for u,v,t in lo]
                        self.face(mat,hi[::-1])
                        self.face(mat,lo)
                        for i in range(3):
                            j=i+1
                            self.face(mat,(lo[j],lo[i],hi[i],hi[j]))
                    if self.lod==0 and s==1:
                        with self.transform(translate=(.27,.159,-.034),rotate_z=13.5):
                            self.block("banner",(0,0,0),(.032,.010,.379))

    def reservoir(self, x, z, y, r, rim, water="water",base="fieldstone"):
        self.drum(base,x,y,z,r*.91,.18,r)
        self.ring(rim,x,y+.18,z,r*1.04,r*.84,.07)
        self.drum(water,x,y+.18,z,r*.94,.035,n=(14,10,6)[self.lod])

    def flue(self,x,z,y,h,r):
        self.drum("brick" if self.era==2 else "wall",x,y,z,r,h,r*.72)
        self.drum("trim",x,y+h,z,r*.87,.075,r*.84)
        if self.lod<2:
            self.drum("door",x,y+h+.075,z,r*.60,.009)
        if self.lod==0:
            self.drum("gold",x,y+h*.66,z,r*.86,.035)

    def foliate(self,x,y,z,r):
        self.drum("roof_tile",x,y,z,r*.72,r*.85,r)
        self.drum("soil",x,y+r*.85,z,r*.93,r*.10)
        # Pointed, folded leaves share a stalk. All leaf faces have a back.
        for j in range((7,5,3)[self.lod]):
            a=j*2.39996
            with self.transform(translate=(x,y+r*.92,z),rotate_y=math.degrees(a)):
                v=[(0,0,0),(-r*.23,r*.63,r*.35),(0,r*.95,r),(r*.23,r*.63,r*.35),(0,r*.63,r*.44)]
                for left,right in ((0,1),(1,2),(2,3),(3,0)):
                    self.face("leaf",(v[left],v[right],v[4]))
                    self.face("grass",[(u,w-.002,t) for u,w,t in (v[4],v[right],v[left])])


def baths(m):
    e=m.era
    h=(.49,.57,.62,.52)[e]
    m.hall(0,.43,1.54,.70,h,cap="plaster" if e==1 else "roof")
    m.stairway(0,.078,.38,.087,.15)
    # Communal pool is cut into a stone terrace in front of the changing house.
    m.reservoir(.31,-.43,0,.43,"plaster" if e==1 else "metal",base="wall" if e==1 else "fieldstone")
    if e==1:
        m.drum("plaster",-.32,h+.10,.46,.31,.09)
        m.crown("roof",-.32,h+.19,.46,.30,.88)
    elif e==2:
        m.flue(-.45,.46,h+.09,.61,.15)
        m.flue(-.12,.51,h+.097,.43,.109)
    else:
        m.slab("glass",-.16,.41,.98,.54,h+.10,h+.34,.12,.091)
        m.slab("ivory",-.16,.41,.82,.39,h+.34,h+.39,.065)
    if m.lod<2:
        m.reservoir(-.57,-.23,0,.209,"trim")
        m.block("timber",(-.43,.13,-.67),(.48,.07,.13))
        for x in (-.60,-.26):
            m.block("fieldstone",(x,.052,-.67),(.07,.104,.099))
    if m.lod==0:
        m.foliate(.64,0,-.008,.096)
        m.rune(.40,h*.82,.073,.10)


def herbs(m):
    e=m.era
    if e:
        return (apothecary_court,still_house,culture_pods)[e-1](m)
    h=(.42,.55,.61,.64)[e]
    # Stepped apothecary with an open mortar, pestle and a public physic garden.
    m.hall(-.24,.17,1.06,1.06,h)
    m.stairway(-.24,-.356,.36,.09,.16)
    m.slab(("roof","plaster","metal","glass")[e],-.24,.21,.94,.86,h+.10,h+.24+.03*e,.17,.115)
    m.slab(("wall","plaster","trim","trim")[e],-.24,.21,.74,.68,h+.24+.03*e,h+.29+.03*e,.12)
    m.drum("trim",.50,0,-.27,.23,.16,.18)
    m.drum("wall",.5,.16,-.27,.18,.29,.32)
    m.ring("gold" if e>0 else "timber",.5,.45,-.27,.331,.257,.06)
    m.drum("leaf",.5,.45,-.27,.265,.021)
    m.beam("timber" if e<2 else "metal",(.44,.46,-.29),(.72,.83,-.20),.085)
    m.foliate(-.65,0,-.59,.18)
    if m.lod<2:
        m.foliate(-.02,0,-.69,.13)
        m.foliate(.59,0,.48,.166)
    if m.lod==0:
        m.rune(-.24,h+.135,-.245,.105)


def apothecary_court(m):
    m.slab("fieldstone",0,.005,1.80,1.64,0,.104,.15)
    # A covered herb-drying walk faces an enclosed garden. Its open front lets air through the racks.
    m.slab("wall",-.13,.576,1.36,.26,.099,.594,.045)
    for x in (-.678,-.12,.426):
        m.drum("fieldstone",x,.102,.087,.095,.098,.077,n=7)
        m.drum("plaster",x,.196,.087,.062,.411,.055,n=7)
    m.hip_roof((-.12,.592,.333),1.49,.77,.354,m.color("roof_tile"),material="roof_tile",overhang=.04)
    for x in (-.748,.706):
        m.slab("wall",x,-.247,.198,.86,.104,.30,.05)
    m.slab("wall",.437,-.649,.58,.184,.102,.332,.055)
    for x,z,r in ((-.42,-.293,.173),(.06,-.378,.16),(.46,-.196,.135))[:1 if m.lod==2 else 3]:
        m.foliate(x,.105,z,r)
    if m.lod<2:
        m.drum("fieldstone",-.533,.103,-.612,.128,.154,.155)
        m.drum("leaf",-.533,.25,-.612,.106,.012)
        m.beam("timber",(-.55,.254,-.611),(-.423,.419,-.55),.031)
    if m.lod==0:
        for x in (-.49,-.29,.10,.30):
            m.beam("trim",(x,.589,.36),(x,.383,.325),.024)
            m.drum("leaf",x,.331,.326,.054,.098,.031,n=7)


def still_house(m):
    m.slab("fieldstone",0,.01,1.84,1.61,0,.114,.13)
    m.hall(-.38,.361,.77,.69,.488,wall="brick",cap="slate")
    m.wedge((-.38,.584,.36),.81,.72,.225,m.color("roof"),material="roof")
    # A heated copper alembic leads into a tall water-jacketed condenser.
    m.drum("brick",.04,.109,-.383,.269,.231,.248)
    m.drum("accent",.04,.336,-.383,.229,.213,.288)
    m.drum("gold",.04,.546,-.383,.291,.232,.097)
    m.drum("metal",.04,.774,-.383,.074,.164)
    m.drum("trim",.579,.112,.12,.24,.128,.196)
    m.drum("metal",.579,.235,.12,.188,.794,.176)
    m.drum("accent",.579,1.023,.12,.202,.106,.123)
    m.beam("gold",(.04,.911,-.383),(.579,1.043,.12),.055)
    if m.lod<2:
        m.foliate(-.55,.116,-.396,.179)
        for y in ((.402,.581,.760,.939) if m.lod==0 else (.457,.813)):
            m.drum("gold",.579,y,.12,.195,.044)
    if m.lod==0:
        m.drum("metal",.519,.113,-.485,.105,.166,.119)
        m.beam("gold",(.543,.369,-.045),(.522,.288,-.468),.046)


def culture_pods(m):
    m.slab("wall",0,.01,1.83,1.66,0,.144,.18)
    # Two transparent plant cultures on raised vessels supply the dispensing counter.
    for x,z,r,h in ((-.446,.26,.352,.648),(.381,.42,.277,.456)):
        m.drum("metal",x,.14,z,r,.148,r*.92)
        m.drum("glass",x,.283,z,r*.90,h,r*.84)
        m.crown("glass",x,.280+h,z,r*.86,.67)
        m.drum("ivory",x,.252,z,r*1.01,.084)
        if m.lod<2:
            for s in (-1,1):
                m.beam("ivory",(x+s*r*.85,.298,z),(x+s*r*.79,.27+h,z),.041)
    m.slab("ivory",.145,-.387,.96,.543,.14,.406,.112)
    m.slab("glass",.145,-.391,.89,.518,.403,.548,.119)
    m.slab("trim",.145,-.399,1.02,.583,.544,.618,.118)
    m.portal(.146,.145,-.661,.32,.33)
    # A public herb bed is visible from the dispensing door, below the culture pods.
    m.foliate(-.58,.144,-.464,.239)
    if m.lod<2:
        m.foliate(.405,.618,-.39,.145)
    if m.lod<2:
        m.beam("metal",(-.43,.279,.075),(-.185,.298,-.321),.098)
    if m.lod==0:
        for x,z,r,h in ((-.446,.26,.352,.648),(.381,.42,.277,.456)):
            m.drum("gold",x,h+.245,z,r*.88,.052)


def trauma_center(m):
    # A stepped operating tower spans a deep ambulance court, with an attached recovery hall.
    m.slab("fieldstone",0,0,1.85,1.67,0,.091,.14)
    m.hall(.005,.371,1.62,.77,.70,door=False,wall="glass",windows=2)
    m.slab("wall",-.22,.319,1.08,.70,.80,1.17,.10,.041)
    m.slab("roof",-.22,.319,1.15,.77,1.17,1.237,.078)
    m.slab("glass",-.22,.349,.86,.57,1.237,1.44,.085,.025)
    m.slab("metal",-.22,.349,.96,.65,1.44,1.498,.083)
    m.slab("plaster",-.22,-.10,1.51,1.13,.465,.578,.118)
    m.slab("roof",-.22,-.10,1.56,1.17,.578,.639,.115)
    for x in (-.856,.420):
        m.slab("wall",x,-.505,.192,.237,.091,.467,.048,.02)
    m.slab("ivory",-.22,-.624,.382,.101,.577,.891,.023)
    m.block("turquoise",(-.22,.740,-.683),(.069,.235,.025))
    for s in (-1,1):
        m.block("turquoise",(-.22+s*.070,.740,-.683),(.071,.067,.025))
    m.portal(-.22,.091,-.02,.393,.36)
    if m.lod<2:
        m.slab("metal",.693,-.369,.259,.545,.091,.325,.061,.019)
        m.slab("ivory",.693,-.369,.231,.491,.325,.382,.049)
        m.foliate(-.724,0,.813,.086)
    if m.lod==0:
        for s in (-1,1):
            for x in (-.552,-.244,.067):
                m.plaque("glass",x,1.026,.319+s*.345,.165,.148)
        m.rune(.730,.51,-.020,.092)


def clinic(m):
    e=m.era
    # Long infirmary wards step down from a tall receiving bay.
    h=.69+.055*e
    m.hall(-.32,.045,.91,1.31,h)
    m.hall(.43,.14,.60,1.02,h*.67,door=False,wall="plaster" if e<3 else "glass")
    y=h+.10
    m.stairway(-.32,-.61,.43,.09,.18)
    m.slab("plaster" if e<2 else "glass",-.32,.12,.73,.89,y,y+.15+(e-1)*.045,.08,.026)
    top=y+.15+(e-1)*.045
    m.slab("roof",-.32,.12,.79,.95,top,top+.06,.10)
    if e==2:
        m.flue(.45,.38,h*.67+.10,.37,.09)
    elif e==3:
        m.vault(.43,.15,.53,.79,h*.67+.10,.16,"glass","metal",(9,6,4)[m.lod])
    # Green apothecary cross over the entrance and on the roof beacon.
    m.slab("ivory",-.32,-.43,.43,.17,y-.02,top+.21,.048)
    front=-.516
    m.plaque("turquoise",-.32,top+.07,front,.085,.20)
    for s in (-1,1):
        m.plaque("turquoise",-.32+s*.079,top+.07,front,.073,.072)
    if m.lod<2:
        m.slab("roof",-.32,-.665,.59,.27,h*.61,h*.61+.057,.044)
        for s in (-1,1):
            m.block("trim",(-.32+s*.267,h*.305,-.732),(.045,h*.61,.048))
        m.foliate(.52,0,-.59,.14)
    if m.lod==0:
        m.foliate(-.70,0,.80,.093)
        m.plaque("ivory",.44,h*.46,-.375,.19,.12)


def writing_desk(m):
    # The manuscript is stretched between two rollers on a tilted stone lectern.
    x,z=.47,-.731
    m.slab("wall",x,z,.55,.31,0,.27,.047,.045)
    m.wedge((x,.27,z),.60,.36,.15,m.color("timber"),material="timber")
    with m.transform(translate=(x,.357,z),rotate_x=-22.62):
        m.block("ivory",(0,0,0),(.49,.019,.36))
        for edge in (-1,1):
            with m.transform(translate=(-.26,.028,edge*.162),rotate_z=-90):
                m.drum("ivory",0,0,0,.045,.52,n=(8,6,5)[m.lod])
        if m.lod<2:
            for i in range(3):
                m.block("trim",(-.031+i*.008,.012,-.059+i*.052),(.28-i*.037,.005,.009))
    # A cut brass quill rises from an inkwell at the edge of the writing surface.
    qx,qy,qz=.775,.34,-.69
    m.slab("trim",qx,qz,.19,.20,0,qy-.08,.035)
    m.drum("glass",qx,qy-.08,qz,.087,.088,.064)
    with m.transform(translate=(qx,qy,qz),rotate_z=-9,rotate_y=-34,scale=(1.53,1.40,1)):
        m.beam("gold",(0,0,0),(0,.62,0),.032)
        shape=[(0,.12),(-.083,.27),(-.100,.44),(-.063,.546),(-.049,.494),
               (-.034,.590),(0,.714),(.068,.550),(.094,.401),(.075,.272)]
        center=(0,.411,-.033)
        rear=(0,.411,.019)
        outline=[(u,v,0) for u,v in shape]
        for i,a in enumerate(outline):
            b=outline[(i+1)%len(outline)]
            m.face("ivory" if a[0]<=0 else "gold",(center,a,b))
            m.face("gold",(rear,b,a))


def reading(m,scriptorium=False):
    e=m.era
    if scriptorium and e==1:
        return writing_cloister(m)
    if not scriptorium and e==3:
        return radial_archive(m)
    h=(.43,.55,.62,.67)[e]
    if scriptorium:
        m.hall(0,.065,1.64,1.15,h)
        # The north-light clerestory illuminates a continuous row of writing desks.
        m.slab("trim",-.14,.17,1.12,.58,h+.10,h+.17,.05)
        if e==2:
            for x in (-.42,.14):
                m.wedge((x,h+.17,.17),.51,.52,.28,m.color("glass"),material="glass")
        elif e==3:
            m.vault(-.14,.17,1.04,.51,h+.17,.26,"glass","metal",(10,6,4)[m.lod])
        else:
            m.wedge((-.14,h+.17,.17),1.04,.51,.20+.05*e,m.color("glass" if e else "roof"),material="glass" if e else "roof")
        m.stairway(0,-.510,.39,.09,.15)
        m.block("timber",(-.54,.163,-.65),(.37,.326,.25),taper=.12)
        m.book(-.54,.326,-.65,.41)
        writing_desk(m)
        if m.lod<2:
            m.flue(.57,.34,h+.10,.21+e*.075,.083)
    else:
        m.hall(0,.02,1.54,1.23,h)
        # The flared covers act as the roof above an ashlar book stack.
        m.slab("glass" if e==3 else "wall",0,.055,1.18,.97,h+.10,h+.28+.045*e,.095,.045)
        y=h+.28+.045*e
        if e==2:
            m.flue(.55,.49,h+.09,.56,.094)
        elif e==3:
            m.slab("metal",0,.055,1.22,1.01,y-.135,y-.093,.13)
        with m.transform(translate=(0,y,.054),scale=(1,1,1.55)):
            m.book(0,0,0,1.60,pages="plaster" if e==1 else "ivory")
        m.stairway(0,-.595,.44,.097,.17)
        if m.lod<2:
            for s in (-1,1):
                x=s*.56
                m.block("trim",(x,.22,-.68),(.187,.42,.193),taper=.06)
                m.book(x,.43,-.68,.30)
    if m.lod==0:
        m.rune(0,h-.042,-(.515 if scriptorium else .606),.087)


def writing_cloister(m):
    m.slab("fieldstone",-.01,.005,1.84,1.68,0,.112,.14)
    # Two writing rooms flank a higher north-light arcade. Desks face the open court.
    for x,h in ((-.606,.52),(.574,.706)):
        m.slab("wall",x,.015,.422,1.16,.107,h,.062)
        m.vault(x,.011,.475,1.23,h-.008,.20,"roof_tile","slate",(6,4,2)[m.lod])
        m.portal(x,.114,-.568,.26,h*.61)
    for x in (-.316,.257):
        m.drum("plaster",x,.111,.371,.078,.804,.061,n=(9,7,4)[m.lod])
    m.slab("trim",-.029,.379,.91,.316,.911,1.008,.053)
    m.hip_roof((-.029,1.004,.38),1.08,.472,.19,m.color("roof_tile"),overhang=.022,material="roof_tile")
    for x,z in ((-.185,-.248),(.198,.117)):
        m.block("timber",(x,.228,z),(.321,.233,.212),taper=.16)
        m.book(x,.343,z,.382)
    if m.lod<2:
        m.plaque("banner",.575,.551,-.574,.128,.110)
    if m.lod==0:
        for x,z in ((-.185,-.248),(.198,.117)):
            m.block("accent",(x,.211,z-.112),(.206,.034,.039))
            m.drum("trim",x+.119,.341,z+.109,.031,.053,n=7)
            m.beam("gold",(x+.119,.387,z+.109),(x+.190,.555,z+.146),.018)
        for x in (-.316,.257):
            m.drum("fieldstone",x,.112,.371,.111,.110,.085,n=8)
            m.drum("accent",x,.850,.371,.09,.065,.109,n=8)
        m.rune(-.030,.963,.214,.13)


def radial_archive(m):
    m.slab("fieldstone",0,0,1.81,1.70,0,.121,.24)
    # Reading floor at the centre; radial book stacks make a horseshoe of terraced wings.
    m.drum("trim",0,.116,-.077,.333,.067,.315)
    m.drum("plaster",0,.179,-.077,.242,.104,.28)
    m.book(0,.278,-.077,.46)
    for angle,height in ((-70,.51),(-36,.75),(0,.98),(36,.75),(70,.51)):
        a=math.radians(angle)
        x,z=.679*math.sin(a),-.109+.688*math.cos(a)
        with m.transform(translate=(x,0,z),rotate_y=angle):
            m.slab("wall",0,.036,.32,.481,.118,height,.05,.018)
            m.slab("glass",0,.036,.299,.45,height-.004,height+.082,.052)
            m.slab("ivory",0,.035,.374,.52,height+.076,height+.156,.055,.026)
            if m.lod<2:
                m.plaque("door",0,height*.59,-.207,.219,height*.47)
                if m.lod==1:
                    for y in (.41,.68):
                        m.block("gold",(0,height*y,-.210),(.196,.022,.028))
            if m.lod==0:
                for y in (.37,.55,.73):
                    m.block("gold",(0,height*y,-.210),(.196,.019,.028))
                    for k in (-1,0,1):
                        m.block("banner" if k==0 else "ivory",(k*.052,height*y+.043,-.209),(.028,.064,.023))
    if m.lod<2:
        m.slab("slate",0,-.562,.58,.28,.121,.193,.066)
        m.book(0,.189,-.562,.315)


def academy(m):
    e=m.era
    m.hall(0,.23,.67,.83,.79+.08*e)
    for s in (-1,1):
        h=.48+s*.036+.025*e
        m.hall(s*.545,.03,.53,1.09,h,door=False)
        if e==3:
            m.vault(s*.545,.04,.48,.84,h+.10,.19,"glass","metal",(8,5,3)[m.lod])
        else:
            m.slab("roof",s*.545,.04,.48,.84,h+.10,h+.236,.074,.08)
            if e==2:
                m.flue(s*.55,.32,h+.22,.24,.077)
    y=.89+.08*e
    m.drum("wall",0,y,.24,.30,.245)
    m.drum("gold",0,y+.245,.24,.325,.055)
    m.crown("roof" if e!=3 else "glass",0,y+.30,.24,.306,.78)
    m.stairway(0,-.185,.39,.098,.27)
    if m.lod<2:
        m.block("fieldstone",(0,.125,-.65),(.32,.25,.28),taper=.18)
        m.book(0,.25,-.65,.61)
        for s in (-1,1):
            m.foliate(s*.52,0,-.68,.116)
    if m.lod==0:
        m.rune(0,.76,-.192,.126)


def telescope(m):
    e=m.era
    h=.43+.062*e
    m.hall(0,.04,1.32,1.16,h)
    y=h+.10
    m.drum("wall",0,y,.08,.47,.21)
    m.drum("trim",0,y+.21,.08,.50,.075)
    if e==2:
        m.drum("metal",0,y+.285,.08,.45,.185,.30)
    else:
        m.crown("roof" if e<3 else "glass",0,y+.285,.08,.45,.62)
    pivot=y+.64
    # The twin cheeks carry a real axle through the rising sight tube.
    for s in (-1,1):
        m.block("metal",(s*.081,y+.45,.08+s*.101),(.107,.47,.133),taper=.14)
    with m.transform(translate=(0,pivot,.08),rotate_y=77,rotate_x=-54):
        m.drum("gold" if e<3 else "ivory",0,-.32,0,.092,.83,.137)
        m.drum("trim",0,.51,0,.16,.09)
        m.drum("glass",0,.60,0,.128,.016)
        if m.lod<2:
            m.drum("gold",0,-.40,0,.064,.08)
            m.drum("trim",0,-.15,0,.119,.045)
    if m.lod<2:
        m.stairway(0,-.54,.43,.093,.22)
        m.rune(.33,h*.75,-.55,.137)
    if m.lod==0:
        m.foliate(-.58,0,-.57,.12)


def press(m):
    e=m.era
    h=.58+.042*e
    m.hall(-.20,.28,1.19,.86,h)
    m.stairway(-.20,-.15,.30,.085,.12)
    # An oversize screw platen, complete with bed, paper and turning handle.
    x,z=.32,-.38
    m.slab("fieldstone",x,z,.82,.64,0,.15,.07)
    m.block("timber",(x,.22,z),(.70,.14,.48))
    y=.86+.055*e
    for s in (-1,1):
        m.block("trim",(x+s*.30,(.29+y)/2,z),(.095,y-.29,.165),taper=.045)
    m.block("trim",(x,y+.05,z),(.77,.10,.21),taper=.06)
    if e==3:
        m.slab("glass",-.24,.32,.87,.61,h+.10,h+.29,.095,.048)
    elif e==2:
        with m.transform(translate=(x+.307,.62,z-.105),rotate_x=-90):
            m.ring("metal",0,0,0,.175,.119,.06)
            m.beam("gold",(-.14,.027,0),(.14,.027,0),.037)
    m.drum("metal",x,.44,z,.068,y-.28)
    m.block("door",(x,.429,z),(.43,.077,.38))
    m.block("ivory",(x,.322,z-.045),(.47,.064,.42))
    m.beam("gold",(x-.22,y+.157,z-.074),(x+.22,y+.165,z+.074),.038)
    if m.lod<2:
        for i in range(4):
            m.drum("gold",x,.51+i*.082,z,.081,.028)
        m.flue(-.48,.40,h+.10,.21+e*.10,.109)
    if m.lod==0:
        m.block("timber",(-.25,.126,-.57),(.49,.252,.32),taper=.07)
        m.book(-.25,.271,-.57,.50)
        m.rune(-.52,h*.80,-.16,.11)


def specimen_bench(m,y):
    # The front of the specimen chamber opens to a cut mineral on its examination stage.
    # A glass inspection lens pivots between two cheeks behind the exposed sample.
    x,z=.20,-.09
    m.slab("trim",x,z,.83,.65,y,y+.105,.13)
    m.drum("ivory",x,y+.105,z-.07,.208,.073,.186)
    m.drum("metal",x,y+.178,z-.07,.138,.072,.092)
    with m.transform(translate=(x,y+.25,z-.07),rotate_z=-9):
        m.drum("turquoise",0,0,0,.090,.242,.144,n=5)
        m.drum("ascension",0,.242,0,.144,.143,0,n=5)
    cy=y+.55
    with m.transform(translate=(x,cy,z+.13),rotate_y=-24):
        for s in (-1,1):
            m.beam("metal",(s*.34,-.45,0),(s*.302,.032,0),.061)
            if m.lod<2:
                m.block("gold",(s*.305,0,0),(.106,.096,.103),taper=.08)
        with m.transform(rotate_x=-86):
            m.ring("gold",0,-.025,0,.348,.275,.073,n=(16,10,7)[m.lod])
            m.drum("glass",0,-.026,0,.284,.042,n=(16,10,7)[m.lod])


def laboratory(m,data=False):
    e=m.era
    h=.54+e*.05
    if data:
        for j in range(3):
            x=(j-1)*.52
            m.hall(x,.12,.47,1.31,h+.11*(j==1),door=(j==1),windows=2,wall="glass")
            y=h+.10+.11*(j==1)
            if m.lod<2:
                for k in (.35,.49,.63):
                    m.block("metal",(x,y+.027,k),(.34,.054,.063),taper=.15)
            for k in (-.14,):
                m.drum("metal",x,y,k,.191,.087)
                m.ring("trim",x,y+.087,k,.18,.13,.037,n=(8,6,5)[m.lod])
                m.drum("door",x,y+.087,k,.137,.012)
                if m.lod==0:
                    for j2 in range(4):
                        with m.transform(translate=(x,y+.10,k),rotate_y=90*j2+18):
                            m.block("metal",(.063,.007,0),(.107,.010,.034),taper=.14)
        m.stairway(0,-.535,.34,.094,.19)
    else:
        m.hall(0,.05,1.56,1.12,h)
        m.stairway(0,-.51,.38,.092,.20)
        specimen_bench(m,h+.10)
        samples=((-.46,.32,.20,.19),) if e==2 else ((-.51,.26,.16,.26),(-.17,.41,.12,.14))
        for x,z,r,rise in samples:
            y=h+.10
            m.drum("metal",x,y,z,r*1.07,.07)
            m.drum("glass",x,y+.07,z,r,rise)
            m.crown("glass",x,y+.07+rise,z,r,.88)
            m.drum("gold",x,y+.13,z,r*1.023,.045)
            if m.lod<2:
                m.drum("metal",x,y+rise+.07+r*.81,z,r*.26,.083)
        if m.lod==0:
            m.foliate(.65,0,-.61,.129)
    if m.lod==0:
        m.rune(0,h*.79,-(.552 if data else .534),.10)


def build(spec,lod):
    m=Stonework(spec,lod)
    w,d=spec.footprint
    functions={"Bathhouse":baths,"Herbalist":herbs,"Hospital":clinic,"Library":reading,
               "MedicalCenter":trauma_center,"Observatory":telescope,"Printworks":press,
               "DataCenter":lambda m:laboratory(m,True),"ResearchLab":laboratory,
               "Scriptorium":lambda m:reading(m,True),"University":academy}
    with m.transform(scale=(w/2,min(.96,(w+d)/4),d/2)):
        functions[spec.building_type](m)
    return m


def publish_package(package,builder):
    entries=[]
    for spec in package_models(package):
        levels=[builder(spec,lod) for lod in range(3)]
        result=publish(levels[0],spec.metadata(),package,lods=levels[1:],preview_size=224)
        entries.append(result)
        print(spec.id,result["lod_triangles"],flush=True)
    root=Path(__file__).resolve().parents[4]
    views=[(item["building_type"],age,root/path) for item in entries for age,path in item["age_previews"].items()]
    directory=views[0][2].parent
    contact_sheet([p for _,_,p in views],directory/"_contact.png",cols=8,cell=132,
                  labels=[name+" "+age for name,age,_ in views])
    for row in sorted({age for _,age,_ in views}):
        singles=[(name,p) for name,age,p in views if age==row]
        contact_sheet([p for _,p in singles],directory/("sheet_"+row+".png"),
                      cols=min(6,len(singles)),cell=144,labels=[name for name,_ in singles])


if __name__=="__main__":
    publish_package(PACKAGE,build)
