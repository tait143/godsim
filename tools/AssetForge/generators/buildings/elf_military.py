"""The Elven wardens' living stockades, keeps and lookout crowns. Builder session 8."""
from math import sin, cos
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import TAU, WHITE, generate


def watch(m,x,z,r,h,entrance=True,small=False):
    # Flared roots taper to a waist below a broader, inhabited lookout gallery.
    n=(10,7,5)[m.lod] if small else m.n
    rows=[(0,1,m.masonry),(.10*h,1.06,m.masonry),(.21*h,.82,m.bark),
          (.60*h,.61,"wall"),(.68*h,.70,"trim"),(.76*h,1.10,"accent"),
          (.91*h,.99,"glass" if m.era>1 else "door"),(h,1.01,m.bark)]
    if m.lod==2:
        rows=[rows[i] for i in (0,3,5,7)]
    m.lathe(x,z,r,r,rows,n=n,closed=False)
    m.hood(x,h,z,1.24*r,1.24*r,(.85,1.08,.68,1.19)[m.era]*r,small=small)
    if entrance:
        width=min(.34,r*.96)
        depth=r*.39
        m.lathe(x,z-r*.92,width*.69,depth,[(0,.98,m.masonry),(.22*h,1,"wall"),
                                            (.34*h,.84,"trim"),(.40*h,.36,"wall")])
        m.portal(x,0,z-r*.92-depth-.005,width,h*.32)
    if m.lod<2:
        for i in range(3 if small else 4):
            a=TAU*(i+.10)/(3 if small else 4)
            def node(rad,y):
                return (x+r*rad*cos(a),y,z+r*rad*sin(a))
            m.stem([node(1.09,.04),node(.66,h*.46),node(1.03,h*.76)],
                   [.088*r,.077*r,.12*r],m.bark,sides=(5,4)[m.lod])


def screen(m,kind):
    if kind=="Palisade":
        # Living trunks curve toward the centre gate. Two continuous woven rails lace each wing.
        for side in (-1,1):
            for j in range(4):
                x=side*(.28+.18*j)
                z=.05+.31*(abs(x)-.25)
                h=(.94 if j==0 else .60+.09*((j+1)%3))*(1+.10*m.era)
                m.stem([(x,.014,z),(x-.026*side,h*.65,z),(x-.076*side,h,z+.04)],
                       [.090,.072,.010],"metal" if m.era==0 else m.bark,sides=(8,5,4)[m.lod])
                if m.lod==0:
                    m.lamp(x,h*.46,z-.075,.044)
            for y in (.23,.43):
                m.stem([(side*.24,y,-.035),(side*.54,y+.025,.02),(side*.86,y+.06,.13)],
                       [.049,.038,.041],"trim",sides=(6,5,4)[m.lod])
        m.stem([(-.28,.60,.05),(-.19,.88,.07),(0,1.01,.09),(.19,.88,.07),(.28,.60,.05)],
               [.053,.037,.033,.037,.053],"accent")
        threshold = "plaster" if m.era==0 else "fieldstone"
        m.lathe(0,-.01,.228,.44,[(0,.97,threshold),(.035,1,threshold)])
        m.leaf((-.05,.99,.09),(.09,1.19,.15),.08)
    else:
        # A continuous curved curtain with a parapet walk, strengthened by end piers.
        n=(14,9,5)[m.lod]
        def curve(t):
            return -.13+.30*t*t
        profile=[(-.20,0),(-.21,.12),(-.16,.18),(-.13,.62),(-.16,.67),
                 (-.16,.78),(-.074,.78),(-.074,.65),(.14,.65),(.20,0)]
        if m.lod==2:
            profile=[profile[i] for i in (0,3,5,6,8,9)]
        for k,((z0,y0),(z1,y1)) in enumerate(zip(profile,profile[1:])):
            for i in range(n):
                a,b=2*i/n-1,2*(i+1)/n-1
                row=[(.77*a,y0,curve(a)+z0),(.77*b,y0,curve(b)+z0),
                     (.77*b,y1,curve(b)+z1),(.77*a,y1,curve(a)+z1)]
                m.quad(*row[::-1],WHITE,("accent" if .62<y0<.75 else m.masonry))
        for side in (-1,1):
            x=side*.74
            rows=[(0,1,m.masonry),(.15,1.08,"fieldstone"),(.67,.77,"wall"),
                  (.79,.93,"accent"),(.91,.87,m.bark)]
            m.lathe(x,.14,.20,.30,rows if m.lod<2 else rows[::2])
            m.hood(x,.91,.14,.22,.32,.24,small=True)
        for x in (-.48,0,.48):
            z=curve(x/.77)-.17
            m.lathe(x,z,.09,.069,[(.76,1,"wall"),(.88,.92,m.bark),(.96,.29,"accent")],
                    n=(8,6,4)[m.lod])
            if m.lod==0:
                m.stem([(x,.04,z-.08),(x,.42,z-.016),(x,.70,z+.025)],
                       [.056,.038,.046],m.bark)


def garrison(m,kind):
    if kind=="Castle":
        m.hall(-.03,.30,.46,.40,1.07,.56)
        for x,z,r,h in [(-.58,.26,.23,1.07),(.56,.34,.24,1.20),(-.47,-.41,.22,.77),(.47,-.41,.22,.80)]:
            watch(m,x,z,r,h,entrance=False,small=True)
        m.lathe(.00,-.40,.39,.18,[(0,.97,"fieldstone"),(.42,.94,"wall"),(.51,1,"accent")])
        m.portal(0,0,-.58,.32,.44)
    elif kind=="Barracks":
        m.hall(-.24,.18,.53,.51,.66,.52)
        m.hall(.28,.38,.27,.38,.42,.29,door=False,small=True)
        # The sleeping wing opens internally into the main hall. A high window
        # lights its bunkroom and leaves the practice yard free of doorways.
        m.lathe(.28,.064,.165,.080,[(.212,.87,m.bark),(.237,1,"accent"),
                                    (.309,.94,"glass"),(.330,.86,m.bark)],
                n=(10,7,4)[m.lod])
        # Training yard with two round practice bosses on separate grounded posts.
        for x,z,s in [(.66,-.54,.92),(.81,-.02,.73)]:
            m.bar((x,.015,z),(x,.54*s,z),.045,m.bark)
            with m.transform(translate=(x,.39*s,z-.032),rotate_x=-86):
                m.lathe(0,0,.15*s,.19*s,[(0,1,"trim"),(.032,.92,"banner"),(.056,.53,"ivory"),
                                        (.080,.29,"accent")],n=(10,7,5)[m.lod])
    else:
        m.hall(-.30,.18,.46,.55,.65,.38,roof="glass")
        watch(m,.44,.24,.24,1.16,small=True)
        m.lathe(.28,-.48,.40,.28,[(0,.95,m.masonry),(.19,1,"wall"),(.27,.86,"trim")])
        # A supported receiving dish sits above the dispatch court.
        m.bar((.31,.23,-.44),(.31,.53,-.44),.080,"metal")
        with m.transform(translate=(.31,.51,-.44),rotate_z=-28):
            m.basin(0,0,0,.26,.22,.13,"glass")
        m.lamp(-.57,0,-.52,.115)


def draw(m,kind):
    if kind in ("Palisade","StoneWall"):
        screen(m,kind)
    elif kind=="Watchtower":
        watch(m,0,.08,.50,1.36*(.91,1.0,1.09,1.15)[m.era])
        m.lamp(.50,0,-.32,.105)
    else:
        garrison(m,kind)


if __name__=="__main__":
    generate("elf_military",draw)
