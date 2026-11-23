"""Marsh dispensaries, tide archives and instrument houses of the Saurian delta."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE = "saurian_learninghealth"


class College(Delta):
    @property
    def masonry(self):
        return ("wall", "wall", "brick", "ivory")[self.era]

    def footing(self, width=1.85, depth=1.70):
        self.slab(self.foot, 0, 0, 0, width, .064, depth, .99, cut=.09)

    def hall(self, x, z, w, d, h, roof=True, entrance=True):
        y = .064
        self.slab(self.masonry, x, y, z, w, h, d, cut=.092)
        front = z-d/2
        if entrance:
            self.entry(x, y, front, min(.29,w*.38), min(.355,h*.83))
        if self.lod < 2:
            for sign in (-1,1):
                self.block("glass" if self.era >= 2 else "turquoise",
                           x+sign*w*.325, y+h*.43, front-.006, w*.13, h*.26, .018)
            self.block("glass" if self.era>=2 else "banner", x+w/2+.004, y+h*.42,z,
                       .018,h*.25,d*.50)
        if roof:
            self.cap(x, y+h, z, w*1.08, d*1.10, min(.43,w*.30))

    def cap(self, x, y, z, w, d, h, skin=None):
        """A reed skirt, galleried tile crown, monitor roof and glass clerestory."""
        skin = skin or ("thatch","roof","metal","glass")[self.era]
        rise = h*(1.0,1.02,.64,.78)[self.era]
        side = (.23,.45,.58,.69)[self.era]
        self.slab(skin, x,y,z,w,rise,d,side,cut=.115,underside=True)
        self.slab("trim" if self.era<2 else "ivory",x,y+rise-.003,z,
                  w*(side+.037),.046,d*(side+.037),.90)
        if self.lod<2 and self.era in (1,3):
            self.slab("wall" if self.era==1 else "glass",x,y+rise+.043,z,
                      w*side*.69,h*.19,d*side*.68,.85)
        return y+rise+.043

    def folio(self, x,y,z,w,d):
        # Two separate sloping banks of parchment open about their low binding.
        for s in (-1,1):
            with self.transform(translate=(x+s*w*.238,y+w*.075,z),rotate_z=s*15):
                self.box((0,0,0),(w*.48,w*.055,d),self.color("trim"),material="trim",bottom=True)
                self.block("ivory",0,w*.027,0,w*.46,w*.036,d*.94)
                if self.lod==0:
                    for k in (-1,0,1):
                        self.block("timber",0,w*.065,k*d*.19,w*.28,w*.013,d*.042)
        self.rod("gold",(x,y+w*.014,z-d*.51),(x,y+w*.014,z+d*.51),w*.032,6)

    def leafpot(self,x,y,z,r,blossom=False):
        self.drum("hide",x,y,z,r*.73,r*1.02,r,n=(8,6,4)[self.lod])
        if self.lod==0:
            self.drum("turquoise",x,y+r*.91,z,r*1.06,r*.19,n=8)
        for i in range((5,3,2)[self.lod]):
            a=1.2+2*math.pi*i/(5,3,2)[self.lod]
            self.blade("leaf" if i%2 else "grass",(x,y+r,z),
                       (x+1.28*r*math.cos(a),y+r*(2.92 if i%2 else 2.45),z+1.28*r*math.sin(a)),r*.48)
        if blossom and self.lod<2:
            self.mound("flower",x,y+r*1.7,z,r*.39,r*.42,n=6)

    def tile(self,x,y,z,r):
        self.block("trim",x,y-r*.16,z+.012,r*.48,r*.32,.039)
        self.disc(x,y,z,r,"gold",False)
        if self.lod<2:
            self.disc(x,y,z-r*.29,r*.49,"turquoise",False)

    def post(self,x,y,z,r,h):
        self.drum(("timber","wall","metal","ivory")[self.era],x,y,z,r,h,r*.88,n=(8,6,4)[self.lod])
        if self.lod<2:
            self.drum("turquoise",x,y+h*.78,z,r*1.03,h*.105,n=(8,6)[self.lod])


def bathhouse(m):
    m.footing()
    # A public pool in the rain court, with raised stone coping and a cascade spout.
    m.hall(-.43,.29,.73,.80,.49,roof=False)
    m.drum("trim",-.43,.554,.29,.398,.077,n=(14,9,6)[m.lod])
    radius=(.386,.386,.35,.383)[m.era]
    m.mound(("roof","roof","metal","glass")[m.era],-.43,.631,.29,radius,(.27,.39,.215,.32)[m.era])
    m.drum("trim",-.43,.615+(.27,.39,.215,.32)[m.era],.29,.066,.09,.05)
    m.tile(-.43,.71+(.27,.39,.215,.32)[m.era],.29,.085)
    m.hall(.43,.48,.66,.43,.345)
    x,z=.344,-.185
    m.slab("wall",x,.064,z,.86,.10,.72,.97)
    m.slab("water",x,.164,z,.72,.015,.58,.99)
    for dx in (-.379,.379):
        m.block("ivory",x+dx,.164,z,.054,.063,.645)
    for dz in (-.319,.319):
        m.block("ivory",x,.164,z+dz,.704,.063,.054)
    if m.era==2:
        m.drum("brick",-.662,.45,.598,.094,.72,.079)
        m.drum("metal",-.662,1.17,.598,.089,.053)
        m.drum("door",-.662,1.223,.598,.059,.013)
    if m.lod<2:
        m.post(.45,.222,.131,.062,.208)
        m.rod("gold",(.45,.408,.131),(.45,.39,-.027),.035,6)
        m.rod("water",(.45,.383,-.026),(.45,.175,-.041),.020,5)
        m.leafpot(-.64,.064,-.437,.088)
        if m.lod==0:
            for q in (-.22,.18):
                m.block("timber",q,.064,-.648,.091,.100,.122)
            m.block("timber",-.02,.164,-.648,.60,.050,.145)


def herbalist(m):
    if m.era==0:
        # Age rows change both the packing yard and the dominant roof:
        # raw timber/reed in Dawn, coppery hide in Bronze and dark timber in Iron.
        m.slab("wall",0,0,0,1.77,.064,1.78,.99,cut=.09)
        m.hall(-.195,.28,1.03,.91,.51,roof=False)
        m.cap(-.195,.574,.28,1.1124,1.001,.309,skin="roof")
    else:
        m.footing(1.77,1.78)
        m.hall(-.195,.28,1.03,.91,.51)
    # Covered open counter, mortar and pestle. Front right belongs to the living herb stock.
    m.slab("timber" if m.era<2 else "wall",-.17,.064,-.454,.62,.24,.206,.92)
    m.slab("trim",-.17,.304,-.454,.72,.056,.26,cut=.065)
    m.drum("fieldstone",-.32,.36,-.449,.076,.106,.121)
    m.drum("door",-.32,.466,-.449,.082,.012)
    m.rod("ivory",(-.326,.471,-.448),(-.245,.608,-.418),.028,6)
    m.leafpot(.553,.064,-.32,.169,True)
    m.leafpot(.583,.064,.275,.138)
    if m.lod<2:
        m.leafpot(-.655,.064,-.378,.114,True)
        m.tile(-.199,.544,-.183,.105)
        if m.lod==0:
            m.jar(.027,.36,-.462,.060,"turquoise")


def scriptorium(m):
    m.footing(1.89,1.80)
    # A shallow writing gallery has space for both the scribe's desk and the entry.
    m.hall(-.14,.277,1.31,.775,.505,roof=False)
    canopy_y=.569
    for x in (-.729,.435):
        m.post(x,.064,-.20,.06,canopy_y-.064)
    top=m.cap(-.145,canopy_y,.16,1.55,.90,.292)
    # The guild's cut reed pen is pinned to a seal atop the gallery.
    m.drum("trim",.115,top-.004,.162,.090,.077)
    m.rod("gold",(.115,top+.049,.162),(-.136,top+.43,.19),.025,5)
    m.blade("ivory",(.070,top+.114,.169),(-.253,top+.476,.20),.091)
    m.block("timber",-.392,.064,-.42,.111,.232,.202)
    m.block("trim",-.392,.296,-.42,.458,.075,.292)
    m.folio(-.392,.372,-.418,.338,.20)
    if m.lod<2:
        m.tile(.435,.466,-.247,.083)
        # Three rolled manuscripts in a jar at the shelter's open flank.
        x,z=.667,.06
        m.jar(x,.064,z,.114,"hide")
        for j in range(3 if m.lod==0 else 2):
            a=2*math.pi*j/3
            m.rod("ivory",(x+.032*math.cos(a),.223,z+.032*math.sin(a)),
                  (x+.089*math.cos(a),.448+.040*j,z+.089*math.sin(a)),.032,6)


def library(m):
    m.footing()
    m.hall(0,.13,1.32,1.08,.52,roof=False)
    m.slab("trim",0,.584,.13,1.45,.091,1.17,.98,underside=True)
    # A codex-shaped vault is enclosed at its ends. Its two wings dip to a gilt gutter.
    top=(.0,.258,.181,.326)[m.era]
    for sign in (-1,1):
        low,outer=sign*.014,sign*.68
        cross=[(low,.674),(outer,.674),(outer,.674+top),(sign*.46,.674+top*.98),
               (sign*.23,.674+top*.73),(low,.728)]
        # Thin bow of the leaf stays legible against the pelagic masonry at phone size.
        if sign<0:
            cross=cross[::-1]
        front,back=-.405,.672
        m.face("ivory",[(a,b,front) for a,b in reversed(cross)])
        m.face("wall",[(a,b,back) for a,b in cross])
        for j,(a,b) in enumerate(cross):
            c,d=cross[(j+1)%len(cross)]
            m.face("roof" if m.era<3 else "glass",
                   [(a,b,front),(c,d,front),(c,d,back),(a,b,back)])
    m.rod("gold",(0,.726,-.42),(0,.726,.687),.040,8)
    m.slab("wall",0,.064,-.535,.45,.092,.301,.97)
    m.stairs(0,0,-.821,.39,.1355,.156)
    m.tile(0,.529,-.412,.088)
    if m.lod<2:
        # A pair of open shelves are filled with flattened clay codices.
        for s in (-1,1):
            x=s*.424
            m.block("door",x,.16,-.416,.308,.288,.032)
            for j in range(3 if m.lod==0 else 2):
                m.block("ivory" if j%2==0 else "hide",x+.010*(j-1),.176+j*.084,-.441,
                        .26-j*.017,.043,.072)
        if m.lod==0:
            m.leafpot(.632,.064,-.56,.082)


def university(m):
    m.footing(1.88,1.81)
    # Two lecture pavilions surround a meridian court with an inclined gnomon.
    m.hall(0,.44,.84,.67,.72)
    for sign in (-1,1):
        m.hall(sign*.619,-.09,.425,.84,.425)
    m.drum("wall",0,.064,-.458,.275,.071,n=(12,8,5)[m.lod])
    for side in (-1,1):
        m.rod("turquoise",(side*.126,.131,-.458),
              (side*.203,.292,-.392),.046,n=(7,5,4)[m.lod])
    # The teaching armillary is inclined so the cavity, tracer and index can
    # be read together from the game camera. Both forks meet the rim.
    with m.transform(translate=(0,.473,-.465),rotate_x=-23):
        m.ring("gold",0,0,-.019,.297,.232,.071,n=(18,12,8)[m.lod])
        m.rod("trim",(-.254,-.105,.006),(.254,.105,.006),.033,n=5)
        m.disc(0,0,-.012,.079,"turquoise",False)
        if m.lod<2:
            m.rod("ivory",(-.066,-.215,.018),(.066,.217,.018),.023,n=5)
    if m.lod<2:
        m.tile(0,.666,.096,.091)
        for side in (-1,1):
            m.leafpot(side*.331,.064,-.664,.084)


def observatory(m):
    m.footing(1.66,1.65)
    m.slab(m.masonry,0,.064,.10,1.31,.273,1.19,.96)
    # The telescope is carried at its balance point by two trunnions on a real azimuth drum.
    m.drum("wall",0,.337,.14,.492,.252,.447,n=(14,10,7)[m.lod])
    m.drum("turquoise",0,.589,.14,.477,.091,n=(14,10,7)[m.lod])
    m.drum("roof",0,.68,.14,.386,.17,.345,n=(14,10,7)[m.lod])
    m.entry(.0,.064,-.489,.27,.226)
    with m.transform(translate=(0,.840,.14),rotate_y=46):
        for sign in (-1,1):
            m.slab("trim",sign*.155,0,0,.094,.279,.192,.79)
        m.rod("gold",(-.204,.24,0),(.204,.24,0),.057,6)
        angle=(0,52,59,47)[m.era]
        with m.transform(translate=(0,.24,0),rotate_x=-angle):
            with m.transform(translate=(0,-.306,0)):
                m.drum("gold",0,0,0,.075,.209,.092,bottom=True)
                m.drum("roof" if m.era==1 else "metal",0,.209,0,.106,.593,.146)
                m.drum("gold",0,.802,0,.165,.095)
                m.drum("glass",0,.897,0,.132,.014)
                if m.lod==0:
                    m.drum("turquoise",0,.419,0,.128,.048)
                    m.drum("trim",0,.674,0,.156,.043)
    if m.lod<2:
        # A small transit instrument on the apron echoes the great tube.
        m.post(.53,.064,-.55,.068,.191)
        m.tile(.53,.299,-.548,.102)
        m.leafpot(-.56,.064,-.552,.084)


def printworks(m):
    m.footing()
    m.hall(-.23,.372,1.12,.723,.485)
    # The public proof press stands beside the door on the delivery terrace.
    x,z=.403,-.299
    m.slab("trim",x,.064,z,.585,.093,.470,.95)
    for sign in (-1,1):
        m.block("timber" if m.era==1 else "metal",x+sign*.215,.157,z,.091,.58,.126)
    m.block("trim",x,.737,z,.573,.103,.169)
    m.drum("gold",x,.458,z,.039,.389)
    m.slab("roof",x,.423,z,.323,.077,.36)
    m.block("wall",x,.157,z,.378,.117,.338)
    m.block("ivory",x,.274,z-.037,.312,.030,.31)
    m.rod("trim",(x-.142,.891,z),(x+.162,.891,z),.026,6)
    m.drum("metal",x,.83,z,.050,.072)
    if m.era==2:
        m.rod("metal",(.52,.378,-.299),(.729,.378,-.299),.041,6)
        with m.transform(translate=(.713,.378,-.299),rotate_y=90):
            m.ring("gold",0,0,-.015,.159,.106,.038,n=(12,8,6)[m.lod])
            for angle in (0,60,120):
                with m.transform(rotate_z=angle):
                    m.block("metal",0,-.138,0,.026,.276,.027)
    if m.era==3:
        m.slab("metal",-.076,.064,-.301,.288,.246,.38,.87)
        m.rod("ivory",(-.217,.319,-.301),(.069,.319,-.301),.084,8)
        m.block("ivory",.109,.306,-.301,.134,.022,.22)
        if m.lod==0:
            m.rod("metal",(.149,.295,-.301),(.230,.295,-.301),.035,6)
    if m.lod<2:
        m.tile(-.23,.505,.003,.083)
        m.jar(-.54,.064,-.42,.111,"timber")
        if m.lod==0:
            for j in range(3):
                with m.transform(translate=(-.278,.064+j*.034,-.515),rotate_y=2+j*3):
                    m.block("ivory" if j%2==0 else "hide",0,0,0,.285,.034,.195)


def lotus(m,x,y,z,r):
    m.drum("gold",x,y,z,r*.548,r*.89,r*.75)
    for i in range((7,5,4)[m.lod]):
        a=2*math.pi*i/(7,5,4)[m.lod]
        m.blade("turquoise" if i%2 else "accent",(x,y+r*.39,z),
                (x+r*math.cos(a),y+r*(1.76 if i%2 else 2.12),z+r*math.sin(a)),r*.31)
    m.mound("ivory",x,y+r*.70,z,r*.44,r*.72,n=(8,6,4)[m.lod])


def hospital(m):
    m.footing()
    # Cross-plan infirmary: breezy recovery wards flank the high intake hall.
    for sign in (-1,1):
        m.hall(sign*.557,.168,.46,.94,.392)
    m.hall(0,-.007,.616,1.26,.64,roof=False)
    top=m.cap(0,.704,-.007,.716,1.33,.32)
    m.drum("trim",0,top-.002,-.027,.116,.08)
    lotus(m,0,top+.078,-.027,.156)
    m.tile(0,.568,-.641,.110)
    m.block("ivory",0,.507,-.680,.036,.119,.027)
    for s in (-1,1):
        m.block("ivory",s*.0405,.548,-.680,.045,.034,.027)
    if m.lod<2:
        for s in (-1,1):
            m.slab("timber",s*.546,.064,-.51,.265,.091,.199,.94)
            m.drum("ivory",s*.546,.155,-.547,.075,.073)
            if m.lod==0:
                m.leafpot(s*.715,.064,-.511,.067)


def research(m):
    m.footing()
    m.hall(-.177,-.052,1.13,1.17,.45,roof=False)
    m.slab("trim",-.177,.514,-.052,1.22,.071,1.26,.98,underside=True)
    for x,z,r,h in ((-.34,.135,.27,.373),(.09,-.30,.177,.264)):
        m.drum("ivory",x,.585,z,r*1.10,.068)
        m.drum("glass",x,.653,z,r,h,r*(.80 if m.era==3 else .93))
        m.mound("glass",x,.653+h,z,r*(.80 if m.era==3 else .93),r*.79)
        if m.lod<2:
            for s in (-1,1):
                m.rod("gold",(x+s*r,.641,z),(x+s*r*.82,.654+h,z),.025,5)
            m.drum("turquoise",x,.653+h-.027,z,r*.93,.043)
    m.slab("wall",.602,.064,.237,.306,.278,.691,.91)
    m.slab("roof",.602,.342,.237,.318,.058,.68,.94)
    if m.lod<2:
        m.rod("gold",(.51,.347,.215),(.393,.487,.215),.046,6)
        m.tile(-.177,.464,-.638,.089)
        if m.lod==0:
            m.jar(.57,.064,-.369,.091,"turquoise")


def data_center(m):
    m.footing()
    # A short service spine backs three glazed machine vaults; the largest hosts the solar clock.
    m.hall(0,.492,1.48,.46,.598,roof=False)
    m.slab("roof",0,.662,.492,1.58,.103,.535,.82,underside=True)
    for i,x in enumerate((-.516,0,.516)):
        h=(.459,.605,.477)[i]
        m.slab("glass",x,.064,-.135,.372,h,.94,.88)
        m.slab("ivory",x,.064+h,-.135,.365,.11,.904,.73)
        if m.lod<2 and i!=1:
            for j in range(3 if m.lod==0 else 2):
                m.block("metal",x,.111+.107*j,-.603,.285,.043,.042)
            m.block("turquoise",x,.197,-.626,.183,.032,.031)
    m.entry(0,.064,-.605,.167,.231)
    m.drum("ivory",0,.765,.482,.085,.112)
    m.tile(0,.94,.476,.14)
    if m.lod==0:
        for x in (-.48,.48):
            m.drum("metal",x,.765,.493,.095,.034,n=10)
            m.drum("door",x,.799,.493,.061,.016,n=8)


def medical_center(m):
    m.footing()
    # Horseshoe patient wings protect an ambulance intake; a white lookout rises above its canopy.
    for s in (-1,1):
        x=s*.502
        m.slab("wall",x,.064,.007,.536,.342,1.18,.94)
        m.slab("glass",x,.406,.007,.523,.235,1.15,.83)
        m.slab("ivory",x,.641,.007,.477,.093,1.023,.90)
        if m.lod<2:
            m.block("turquoise",x,.258,-.575,.254,.075,.041)
            for j in (-1,1):
                m.block("metal",x+.25*s,.42,j*.289,.038,.185,.034)
    m.hall(0,.276,.618,.694,.561,roof=False)
    m.drum("glass",0,.625,.276,.294,.367,.264)
    m.drum("ivory",0,.992,.276,.287,.068)
    lotus(m,0,1.06,.276,.207)
    for s in (-1,1):
        m.post(s*.205,.064,-.553,.045,.29)
    m.slab("roof",0,.354,-.370,.688,.083,.720,.91,underside=True)
    # Recessed-foundation turquoise shield carrying the intake cross on its upper face.
    m.slab("turquoise",0,.436,-.465,.550,.029,.463,.93,cut=.16)
    m.block("ivory",0,.465,-.465,.108,.027,.332)
    for side in (-1,1):
        m.block("ivory",side*.1165,.465,-.465,.125,.027,.103)
    if m.lod<2:
        m.tile(0,.397,-.707,.058)
        if m.lod==0:
            for s in (-1,1):
                m.leafpot(s*.51,.064,-.682,.079)


BUILDERS={"Bathhouse":bathhouse,"Herbalist":herbalist,"Scriptorium":scriptorium,
          "Library":library,"University":university,"Observatory":observatory,
          "Printworks":printworks,"Hospital":hospital,"ResearchLab":research,
          "DataCenter":data_center,"MedicalCenter":medical_center}


def build(spec,lod):
    m=College(spec,lod)
    w,d=spec.footprint
    with m.transform(scale=(w*.5,(.73 if spec.building_type=="Scriptorium" else min(w,d)*.5),d*.5)):
        BUILDERS[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
