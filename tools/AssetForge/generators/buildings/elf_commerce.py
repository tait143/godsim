"""Elven markets, exchange halls and transit buildings. Builder session 8."""
from pathlib import Path
import sys
from math import pi, cos, sin

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import WHITE, generate
from generators.buildings.elf_culturecivic import canopy


def exchange(m,kind):
    e=m.era
    if kind=="Market":
        for x,z,w,turn,h in [(-.40,.19,.39,-9,.75),(.41,.28,.38,13,.57)]:
            with m.transform(translate=(x,0,z),rotate_y=turn):
                canopy(m,0,0,w,h+.05*e)
                m.lathe(0,-.05,w*.90,.21,[(0,.86,"timber"),(.23,.96,m.bark),(.29,1,"accent")])
                for i in range(3 if m.lod<2 else 1):
                    m.tulip((i-1)*w*.47,.29,-.07,w*.20,.115,("flower","gold","leaf")[i])
        m.hall(-.14,-.45,.235,.23,.31,.28,small=True)
    elif kind=="Bank":
        m.hall(-.12,.22,.64,.56,.92,.45)
        # The wheel is mounted on the vault face, between bronze hinge cheeks.
        m.lathe(.43,-.32,.36,.28,[(0,.97,m.masonry),(.24,1,"wall"),(.66,.88,m.bark)])
        m.hood(.43,.66,-.32,.37,.30,.17,small=True)
        with m.transform(translate=(.43,.31,-.55),rotate_y=-22):
            with m.transform(rotate_x=-90):
                m.lathe(0,0,.273,.275,[(-.083,.94,"metal"),(.028,1.04,"trim"),
                                       (.055,.99,"metal"),(.075,.83,"metal")])
            m.hoop(0,0,-.099,.165,.164,.036,"gold")
            for i in range(3):
                a=i*pi/3
                m.bar((-.14*cos(a),-.14*sin(a),-.102),(.14*cos(a),.14*sin(a),-.102),
                      .020,"accent")
            with m.transform(translate=(0,0,-.098),rotate_x=-90):
                m.lathe(0,0,.061,.061,[(0,1,"trim"),(.048,.82,"gold")],n=(8,6,4)[m.lod])
            for y in (-.11,.11):
                m.box((.199,y,-.047),(.091,.054,.078),WHITE,material="accent",bottom=True)
    else:
        m.hall(-.22,.09,.49,.62,1.00,.49)
        m.hall(.40,.18,.29,.42,.62,.40,door=False,small=True)
        # Trade seal suspended from two links on a public sign bracket.
        m.stem([(.44,.02,-.40),(.43,.70,-.42),(.53,.86,-.43),(.76,.87,-.44)],
               [.065,.035,.028,.026],m.bark)
        for x in (.56,.70):
            m.bar((x,.86,-.44),(x,.73,-.44),.015,"accent")
        m.hoop(.63,.59,-.44,.14,.15,.045,"gold")
        m.leaf((.58,.50,-.44),(.68,.69,-.42),.054,"banner")


def boat(m,x,z):
    # A double-ended river hull with a hollow sheer and a raised prow.
    stations=[(-.49,.018,.19),(-.30,.15,.12),(.13,.18,.10),(.43,.072,.14),(.51,.016,.24)]
    if m.lod==2:
        stations=[stations[i] for i in (0,2,4)]
    rings=[]
    for dz,w,lift in stations:
        rings.append([(x-w,.12+lift,z+dz),(x-w*.68,.042+lift*.15,z+dz),
                      (x+w*.68,.042+lift*.15,z+dz),(x+w,.12+lift,z+dz),
                      (x+w*.80,.115+lift,z+dz),(x+w*.50,.085+lift*.17,z+dz),
                      (x-w*.50,.085+lift*.17,z+dz),(x-w*.80,.115+lift,z+dz)])
    for a,b in zip(rings,rings[1:]):
        for i in range(8):
            j=(i+1)%8
            role="accent" if i in (3,7) else "timber"
            m.tri(a[i],a[j],b[j],WHITE,role)
            m.tri(a[i],b[j],b[i],WHITE,role)
    for i in (0,-1):
        ring=rings[i]
        for j in (0,1,2):
            face=[ring[j],ring[j+1],ring[6-j],ring[7-j]]
            m.quad(*(face[::-1] if i==0 else face),WHITE,"timber")
    m.bar((x,.09,z+.08),(x,.76,z+.08),.028,m.bark)
    m.leaf((x,.24,z+.08),(x-.02,.75,z+.08),.14,"banner")


def harbor(m):
    m.hall(-.28,.23,.46,.48,.56,.42)
    # Jetty deck, posts and open berthing space alongside it.
    for x in (-.03,.25):
        for z in (-.72,-.22,.15):
            m.lathe(x,z,.047,.047,[(0,1,"timber"),(.22,.86,"trim")],n=(7,5,4)[m.lod])
    m.lathe(.10,-.27,.21,.62,[(.17,.96,"timber"),(.25,1,m.bark)])
    m.lathe(.49,-.20,.30,.69,[(0,.95,"fieldstone"),(.072,1,"water")])
    boat(m,.54,-.26)
    if m.lod<2:
        m.lamp(.22,.24,-.61,.078)


def transit(m,kind):
    if kind=="Airport":
        # Airship terminal with a moored passenger seedship supported by a docking cradle.
        m.hall(-.36,.21,.43,.53,.51,.37)
        for dz in (-.30,.29):
            m.stem([(.22,.04,dz),(.25,.35,dz),(.42,.51,dz)],[.073,.057,.044],"metal")
        m.lathe(.40,-.02,.18,.45,[(.39,.46,"trim"),(.49,.93,"timber"),(.56,1,"glass"),
                                  (.61,.86,"accent")])
        with m.transform(translate=(.39,.67,-.02),scale=(.32,.27,.76)):
            m.lathe(0,0,1,1,[(-.47,.44,"ivory"),(-.11,.91,"trim"),(.24,1,"glass"),
                             (.59,.84,"glass"),(.88,.40,"ivory")])
        for side in (-1,1):
            m.leaf((.39,.67,.32),(.39+side*.38,.74,.72),.11,"roof")
        # High tail rudder and its visible foil root. The keel hangs below the envelope.
        with m.transform(translate=(.39,.73,.56),rotate_y=78):
            points=[(-.13,0),(.20,.065),(.26,.43),(.10,.39)]
            front=[(x,y,-.025) for x,y in points]
            back=[(x,y,.025) for x,y in points]
            m.quad(*front[::-1],WHITE,"banner")
            m.quad(*back,WHITE,"banner")
            for i in range(4):
                j=(i+1)%4
                m.quad(front[i],front[j],back[j],back[i],WHITE,"accent")
        if m.lod<2:
            for z in (-.24,-.04,.16):
                m.lamp(.53,.478,z,.046)
        m.lamp(-.74,0,-.28,.14)
    else:
        m.hall(-.34,.28,.44,.43,.66,.46)
        m.lathe(.17,-.09,.27,.73,[(0,.96,m.masonry),(.12,1,"ivory")])
        canopy(m,.20,-.13,.56,.77+.12*(m.era-2),90)
        # Parallel rails follow the long edge of the boarding platform.
        for x in (.55,.76):
            m.bar((x,.07,-.82),(x,.07,.77),.027,"metal")
        for j in range((8,5,3)[m.lod]):
            z=-.73+1.38*j/((8,5,3)[m.lod]-1)
            m.box((.655,.021,z),(.37,.042,.079),WHITE,material=m.bark)
        m.lamp(-.46,0,-.44,.106)


def vault_face(m,x,y,z,r):
    with m.transform(translate=(x,y,z),rotate_x=-90):
        m.lathe(0,0,r,r,[(0,.94,"metal"),(.065,1,"gold"),(.086,.87,"metal")])
    m.hoop(x,y,z-.102,r*.61,r*.61,r*.11,"trim")
    for a in (() if m.lod==2 else (0,pi/3,2*pi/3)):
        m.bar((x-r*.54*cos(a),y-r*.54*sin(a),z-.109),
              (x+r*.54*cos(a),y+r*.54*sin(a),z-.109),r*.068,"gold")


def later_exchange(m,kind):
    e=m.era
    if kind=="Market":
        if e==1:
            # A cloister of stalls faces the stone weighing court.
            for x,z,r,h in ((-.49,.26,.32,.58),(.34,.43,.37,.65),(.50,-.24,.27,.44)):
                if m.lod==2:
                    m.bar((x,.24,z),(x,h,z),.055,m.bark)
                    m.leaf((x-r,h,z),(x+r,h+.05,z),r*.49,"roof")
                else:
                    canopy(m,x,z,r,h)
                m.lathe(x,z-.02,r*.80,r*.48,[(0,.84,"wall"),(.25,1,"accent")])
                if x<0:
                    m.cargo(x,.25,z-.025,(.27,.13,.18),"cloth")
                    m.cargo(x+.04,.38,z-.015,(.19,.068,.15),"ivory")
                elif z>0:
                    for side in (-1,1):
                        m.lathe(x+side*.093,z-.02,.084,.076,[(.25,.72,"hide"),(.32,1,"hide"),
                                (.36,.80,"gold")],n=(9,6,4)[m.lod])
                else:
                    m.cargo(x,.25,z-.02,(.24,.12,.17),"leaf")
            m.basin(-.23,0,-.35,.32,.22,.12,"gold")
        elif e==2:
            # One high steel market shed covers both wholesale aisles.
            m.lathe(-.07,.11,.76,.53,[(0,.98,"brick"),(.18,1,"trim")])
            for x in (-.50,.37):
                for z in (-.23,.41):
                    m.bar((x,.18,z),(x*.78,.83,z),.059,"metal")
            m.hood(-.06,.82,.09,.80,.63,.32,"metal")
            for x in (-.44,.30):
                m.lathe(x,-.36,.23,.20,[(.13,.81,"timber"),(.34,1.02,"accent")])
                if m.lod<2:
                    m.tulip(x,.34,-.36,.094,.17,"gold")
        else:
            # Two inhabited trading balconies spiral around the auction steward.
            m.hall(.16,.34,.34,.36,.93,.41,small=True)
            for x,z,y,r in ((-.40,-.09,.39,.39),(.37,-.31,.56,.34)):
                m.lathe(x,z,r,r*.82,[(0,.43,"ivory"),(y*.73,.59,"glass"),(y,1,"ivory")])
                if m.lod<2:
                    m.tulip(x,y,z,.11,.19,"flower")
            m.lamp(-.61,0,-.49,.12)
            m.cargo(-.46,.39,-.16,(.23,.16,.18),"cloth")
            m.cargo(.38,.56,-.34,(.20,.12,.16),"gold")
            # Shared sales desk and panel awning face the path between the decks.
            m.box((-.01,.145,-.53),(.46,.29,.18),WHITE,material="timber")
            m.bar((-.20,.29,-.52),(-.23,.83,-.53),.031,"ivory")
            m.bar((.17,.29,-.52),(.19,.83,-.53),.031,"ivory")
            m.leaf((-.28,.83,-.53),(.24,.88,-.52),.15,"banner")
    elif kind=="Bank":
        if e==2:
            # Two masonry strongrooms support a single uninterrupted banking floor.
            for x in (-.45,.41):
                m.lathe(x,.15,.35,.55,[(0,.97,"brick"),(.49,1,"wall"),(.59,.94,"trim")])
            m.box((-.02,.686,.16),(1.45,.20,.87),WHITE,material="metal")
            m.hood(-.02,.779,.16,.80,.51,.24,"metal")
            vault_face(m,-.44,.31,-.34,.224)
            m.portal(.42,0,-.38,.28,.42)
        else:
            # The vault rises vertically. Each upper strongroom sits on the neck below.
            m.lathe(.31,.18,.46,.47,[(0,.69,"wall"),(.49,.78,"ivory"),(.62,1,"trim"),
                    (.96,.90,"glass"),(1.08,1.04,"ivory"),(1.33,.63,"metal")])
            m.hall(-.41,-.26,.34,.35,.43,.24,small=True)
            vault_face(m,.30,.70,-.226,.225)
    elif kind=="Guildhall":
        if e==2:
            m.hall(-.21,.19,.58,.46,.59,.24,roof="metal")
            m.lathe(.46,.02,.22,.25,[(0,.98,"brick"),(.82,.86,"wall"),(1.02,1.21,"trim")])
            m.hood(.46,1.02,.02,.32,.31,.31,"metal",small=True)
            m.hoop(.44,.80,-.21,.106,.113,.029,"gold")
        else:
            # The guild seal hangs between the slender wings of the exchange.
            for x,z,h in ((-.49,.15,.92),(.42,.21,.69)):
                m.hall(x,z,.26,.38,h,.27,roof="glass",small=True)
            m.bar((-.49,.84,.04),(.42,.78,.04),.059,"ivory")
            m.bar((-.04,.81,.04),(-.04,.68,.04),.026,"gold")
            m.hoop(-.04,.49,.04,.22,.20,.054,"gold")
            m.lamp(-.14,0,-.48,.11)
    if m.lod==0:
        m.lamp(.73,0,-.56,.062)


def lighter(m,x,z):
    if m.lod<2:
        boat(m,x,z)
        return
    m.lathe(x,z,.16,.51,[(.06,.55,"timber"),(.22,1.01,"timber")],n=5)
    m.bar((x,.22,z),(x,.67,z),.027,"trim")
    m.leaf((x,.28,z),(x+.04,.67,z),.12,"banner")


def freight_berth(m):
    if m.era==2:
        # A harbour crane pivots over a low stone quay and a loaded river lighter.
        m.lathe(-.40,.06,.38,.62,[(0,.98,"brick"),(.21,1,"wall")])
        m.lathe(-.40,.24,.23,.22,[(.21,1,"metal"),(.81,.56,"trim")])
        m.stem([(-.40,.79,.24),(-.31,1.05,.18),(.42,1.06,-.20)],
               [.092,.068,.046],"metal")
        m.bar((-.39,.58,.23),(.24,1.03,-.13),.044,"trim")
        m.bar((.39,1.06,-.19),(.39,.57,-.19),.018,"gold")
        lighter(m,.44,-.23)
        if m.lod<2:
            m.portal(-.40,0,-.525,.26,.21)
    else:
        # Ferry passengers board through a raised gangway bridging two moorings.
        for x,z,h in ((-.52,.28,.56),(.47,-.04,.40)):
            m.lathe(x,z,.27,.33,[(0,.84,"ivory"),(h,1,"glass"),(h+.08,1.08,"trim")])
            m.hood(x,h+.08,z,.32,.37,.20,"glass",small=True)
        m.bar((-.53,.51,.25),(.47,.40,-.04),.10,"ivory")
        m.lathe(.04,-.44,.75,.29,[(0,.93,"wall"),(.074,1,"water")])
        with m.transform(translate=(.06,.05,-.40),rotate_y=72,scale=.78):
            lighter(m,0,0)
        m.portal(-.52,0,-.04,.23,.37)
    if m.lod<2:
        m.lamp(-.67,0,-.39,.091)


def draw(m,kind):
    if kind=="Market" and m.era or kind in ("Bank","Guildhall") and m.era>=2:
        later_exchange(m,kind)
    elif kind=="Harbor" and m.era>=2:
        freight_berth(m)
    elif kind=="RailStation" and m.era==3:
        # A suspended train passes between the two signal books at the platform.
        for z in (-.48,.51):
            m.stem([(-.41,.024,z),(-.43,.64,z),(-.24,.95,z),(.44,.95,z)],
                   [.096,.075,.065,.051],"ivory")
        m.bar((.32,.94,-.77),(.32,.94,.78),.063,"metal")
        m.lathe(.32,.01,.18,.54,[(.42,.58,"ivory"),(.55,1,"glass"),(.71,.88,"trim"),
                                  (.77,.51,"ivory")])
        m.bar((.32,.75,0),(.32,.94,0),.067,"metal")
        m.hall(-.49,.15,.27,.28,.38,.28,small=True)
        m.lathe(-.11,-.18,.23,.64,[(0,.92,"wall"),(.17,1,"ivory")])
    elif kind=="Harbor":
        harbor(m)
    elif kind in ("RailStation","Airport"):
        transit(m,kind)
    else:
        exchange(m,kind)


if __name__=="__main__":
    generate("elf_commerce",draw)
