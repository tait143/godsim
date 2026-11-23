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


def draw(m,kind):
    if kind=="Harbor":
        harbor(m)
    elif kind in ("RailStation","Airport"):
        transit(m,kind)
    else:
        exchange(m,kind)


if __name__=="__main__":
    generate("elf_commerce",draw)
