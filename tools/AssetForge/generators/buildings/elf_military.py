"""The Elven wardens' living stockades, keeps and lookout crowns. Builder session 8."""
from math import sin, cos
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import TAU, WHITE, generate
from generators.buildings.elf_institution_forms import deck, drum, pod, stair, vault


def watch(m,x,z,r,h,entrance=True,small=False,paired_ribs=False):
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
        ribs=2 if paired_ribs and m.lod==1 else (3 if small else 4)
        for i in range(ribs):
            a=TAU*(i+.10)/ribs
            def node(rad,y):
                return (x+r*rad*cos(a),y,z+r*rad*sin(a))
            m.stem([node(1.09,.04),node(.66,h*.46),node(1.03,h*.76)],
                   [.088*r,.077*r,.12*r],m.bark,sides=(5,3 if paired_ribs else 4)[m.lod])


def screen(m,kind):
    if kind=="Palisade" and m.era==1:
        # A roofed gate walk surmounts a real passage. The two thorn fences
        # fold back from its abutments to make a shallow defensive forecourt.
        for s in (-1,1):
            x=s*.26
            m.lathe(x,.035,.11,.27,[(0,1,'fieldstone'),(.23,1.04,'wall'),(1.02,.79,'trim')])
            if m.lod==2:
                # A continuous pointed screen survives reduction better than
                # separated stakes. The repeated peaks follow the bend in plan.
                with m.transform(scale=(s,1,1)):
                    rings=[]
                    for i in range(11):
                        u=i/10
                        px=.283+.631*u
                        pz=.100+.417*u
                        h=.64-.239*u+(.207 if i%2 else 0)
                        rings.append([(px,0,pz-.059),(px,0,pz+.073),
                                      (px+.021,h,pz+.018),(px+.021,h,pz-.06)])
                    for a,b in zip(rings,rings[1:]):
                        for j in range(4):
                            k=(j+1)%4
                            m.quad(a[j],b[j],b[k],a[k],WHITE,'trim' if j==2 else m.bark)
                    m.poly(rings[0],WHITE,m.bark)
                    m.poly(rings[-1][::-1],WHITE,m.bark)
                continue
            for i in range((5,4,3)[m.lod]):
                u=i/((5,4,3)[m.lod]-1)
                px=s*(.367+.495*u)
                z=.138+.381*u
                h=.83-.41*u
                m.stem([(px,.026,z),(px+s*.014,h*.68,z-.052),(px+s*.072,h,z-.13)],
                       [.085,.061,.008],m.bark,sides=(7,5,4)[m.lod])
            for y in (.14,.363):
                m.bar((s*.293,y,.09),(s*.889,y-.072,.491),.047,'trim')
        m.box((0,1.011,.035),(.68,.12,.52),WHITE,material='trim',bottom=True)
        for x in (-.249,.249):
            for z in (-.13,.21):
                m.bar((x,1.057,z),(x,1.403,z),.036,'wall')
        m.hood(0,1.379,.033,.386,.355,.50)
        for z in ((-.16,.235) if m.lod<2 else (-.16,)):
            m.bar((-.278,1.166,z),(.278,1.166,z),.038,'accent')
        if m.lod<2:
            for x in (-.25,.25):
                m.lamp(x,.78,-.214,.069)
            m.leaf((0,1.811,.039),(-.094,2.009,.09),.054)
        return
    if kind=="StoneWall" and m.era==2:
        # Buttressed double bastion with an inset guard passage between the rounded salients.
        for side in (-1,1):
            x=side*.64
            rows=[(0,.95,"fieldstone"),(.17,1,"wall"),(.60,.76,"wall"),
                  (.67,.97,"metal"),(.88,.90,"wall"),(.91,.85,"trim")]
            m.lathe(x,.012,.33,.50,rows if m.lod<2 else [rows[i] for i in (0,2,3,5)])
            for j in (-1,1):
                if m.lod<2:
                    m.stem([(x+j*.16,.040,-.34),(x+j*.13,.46,-.29),(x+j*.11,.70,-.26)],
                           [.072,.045,.044],m.bark)
            if m.lod<2:
                m.lamp(x,.68,-.391,.053)
        m.lathe(0,.19,.54,.25,[(0,.99,"fieldstone"),(.48,.91,"wall"),
                               (.55,1,"metal"),(.64,.94,"wall")])
        m.portal(0,0,-.074,.25,.39)
        return
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
            watch(m,x,z,r,h,entrance=False,small=True,paired_ribs=m.era==1)
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


def shield(m,x,z,y,r):
    m.bar((x,.026,z),(x,y+.03,z),.049,m.bark)
    with m.transform(translate=(x,y,z-.025),rotate_x=-90):
        drum(m,0,0,r,r*1.16,[(0,.86,"trim"),(.032,1,"banner"),(.052,.76,"ivory"),
                               (.075,.43,"banner"),(.097,.22,"accent")])


def training_quarters(m):
    if m.era==1:
        # Shared portico along the bunk rooms opens onto the oval shield court.
        for i,x in enumerate((-.51,-.04,.43)):
            vault(m,x,.30+.055*i,.20,.37,.42+.035*i,.24,door=True)
        deck(m,-.04,0,-.37,.78,.43,.09,"soil")
        for x in (-.50,.42):
            shield(m,x,-.44,.40,.15)
    elif m.era==2:
        vault(m,-.28,.25,.37,.65,.62,.29,turn=75)
        # The dug-out drill court has a floor below the perimeter retaining banks.
        deck(m,.03,0,-.38,.72,.35,.074,"soil")
        for side in (-1,1):
            drum(m,side*.65,-.36,.105,.38,[(0,1,"brick"),(.22,.84,"brick"),(.247,.99,"trim")])
        for x,z,y in ((-.22,-.35,.38),(.28,-.29,.44)):
            shield(m,x,z,y,.14)
        if m.lod<2:
            m.bar((-.56,.20,-.41),(-.56,.71,-.41),.026,"metal")
            m.leaf((-.56,.70,-.41),(-.32,.65,-.38),.095,"banner")
    else:
        deck(m,.01,0,-.20,.75,.58,.23,"metal")
        # Long raised sleeping wings flank the deck. A high clerestory joins their rear ends.
        for x,z,h,turn in ((-.50,.11,.46,-13),(.51,.17,.60,14)):
            with m.transform(translate=(x,0,z),rotate_y=turn):
                for end in (-.32,.32):
                    drum(m,0,end,.135,.125,[(0,1,"wall"),(.32*h,.69,"ivory"),(h,1.12,"trim")])
                vault(m,0,0,.23,.53,.28,.21,base=h,roof="glass",ribs=False)
                if m.lod<2:
                    for end in ((-.25,.23) if m.lod==0 else (-.25,)):
                        n=(8,5)[m.lod]
                        arc=[(.231*cos(TAU*i/(2*n)),h+.285+.244*sin(TAU*i/(2*n)),end)
                             for i in range(n+1)]
                        m.stem(arc,[.018]*len(arc),"ivory",sides=(5,3)[m.lod])
            # A rising gangway lands directly across each quarter's forward threshold.
            side=1 if x>0 else -1
            z0=z-.57
            with m.transform(translate=(0,0,z0),scale=(side,1,1)):
                top=[(.05,.35,-.09),(.45,h+.012,-.09),(.45,h+.012,.09),(.05,.35,.09)]
                low=[(u,v-.04,w) for u,v,w in top]
                m.quad(*top[::-1],WHITE,"ivory")
                m.quad(*low,WHITE,"metal")
                for i,a in enumerate(top):
                    j=(i+1)%4
                    m.quad(a,top[j],low[j],low[i],WHITE,"trim")
                if m.lod<2:
                    m.stem([(.08,.35,-.088),(.08,.49,-.088),(.43,h+.16,-.088),(.43,h,-.088)],
                           [.022,.018,.018,.022],"ivory",sides=(5,3)[m.lod])
        vault(m,0,.57,.13,.38,.12,.12,base=.65,turn=86,roof="glass",door=False,ribs=False)
        deck(m,.06,.21,-.22,.48,.34,.14,"ivory")
        with m.transform(translate=(0,.28,0)):
            shield(m,.21,-.32,.43,.19)
        stair(m,-.09,-.80,.076,.40,.15,.084)


def lookout(m):
    if m.era==1:
        x,z=-.10,.17
        drum(m,x,z,.44,.38,[(0,1.09,"fieldstone"),(.14,1.03,"trim"),(.67,.71,"wall"),
                               (.81,.79,"wall"),(.92,1.17,"trim"),(.98,1.14,"wall")])
        m.portal(x,0,z-.40,.30,.43)
        for k in range(4):
            a=TAU*(k+.13)/4
            u,v=x+.37*cos(a),z+.33*sin(a)
            m.stem([(u,.95,v),(u,1.25,v),(x+.32*cos(a),1.42,z+.30*sin(a))],
                   [.052,.034,.054],"trim")
        # A wide, shallow lookout canopy rolls up from the eaves over the open gallery.
        vault(m,x,z,.36,.54,.045,.195,base=1.39,turn=74,door=False,roof="roof")
        # A flanking stepped stair turrets out of the tower base toward the court.
        for k in range(4):
            a=-1.35+.45*k
            u,v=x+.44*cos(a),z+.40*sin(a)
            drum(m,u,v,.174,.170,[(0,.99,"fieldstone"),(.16*(k+1),.94,"wall")])
        if m.lod<2:
            m.stem([(x+.49,.30,z-.37),(x+.51,.55,z-.18),(x+.48,.84,z+.04)],
                   [.036,.029,.030],"trim")
            m.lamp(x+.49,.81,z+.04,.069)
    else:
        # Four outward-splayed legs and a diagonally latticed midsection leave clear daylight.
        x,z=-.18,.18
        corners=[(-1,-1),(1,-1),(1,1),(-1,1)]
        def footing(pair,y,span):
            a,b=pair
            return x+span*a,y,z+.88*span*b
        for i,p in enumerate(corners):
            q=corners[(i+1)%4]
            m.stem([footing(p,.06,.43),footing(p,.78,.29),footing(p,1.43,.23)],
                   [.077,.047,.063],"metal")
            drum(m,x+.43*p[0],z+.378*p[1],.101,.096,[(0,1,"wall"),(.13,.69,"accent")])
            m.bar(footing(p,.20,.40),footing(q,.78,.29),.032,"trim")
            if m.lod<2 or i%2==0:
                m.bar(footing(q,.78,.29),footing(p,1.36,.24),.030,"trim")
        deck(m,x,1.39,z,.49,.45,.14,"wall")
        for i,p in enumerate(corners):
            q=corners[(i+1)%4]
            m.stem([footing(p,1.48,.29),footing(p,1.69,.29)],[.027,.027],"metal",
                   sides=(7,5,3)[m.lod],cap=False)
            m.bar(footing(p,1.68,.29),footing(q,1.68,.29),.025,"metal")
        vault(m,.46,-.25,.26,.35,.43,.19)
        if m.lod<2:
            for dx in (-.073,.073):
                m.bar((x+dx,.05,z-.41),(x+dx,1.41,z-.24),.021,"metal")
            for i in range((7,4)[m.lod]):
                t=(i+.6)/(7,4)[m.lod]
                m.stem([(x-.079,.05+1.36*t,z-.412+.17*t),(x+.079,.05+1.36*t,z-.412+.17*t)],
                       [.016,.016],"trim",sides=4)
        m.stem([(x,1.49,z),(x,1.86,z),(x+.07,2.00,z+.02)],[.045,.030,.012],"accent")
        m.leaf((x,1.89,z),(x+.26,1.82,z+.05),.077,"banner")


def command_post(m):
    # A low motor pool runs under a broad cantilevered command room.
    deck(m,-.03,0,-.06,.82,.65,.14,"metal")
    for x in (-.51,.24):
        m.stem([(x,.12,-.22),(x,.53,-.17),(x*.78,.83,-.04)],
               [.113,.087,.115],"ivory")
    pod(m,-.17,.13,.55,.42,.77,.61,door=True)
    vault(m,.39,.04,.34,.53,.29,.19,base=.13,roof="metal",door=False)
    m.portal(.39,.13,-.505,.46,.37,"door")
    for x,z,h in ((-.58,.39,1.82),(.66,.36,1.08)):
        drum(m,x,z,.135,.14,[(0,1,"wall"),(.26,1.06,"metal"),(.84*h,.36,"metal"),
                               (.86*h,.70,"accent"),(h,.24,"ivory")])
        with m.transform(translate=(x,h-.02,z),rotate_z=-35,rotate_x=-20):
            m.basin(0,0,0,.205,.177,.095,"glass")
    if m.lod<2:
        drum(m,.32,-.62,.13,.20,[(.12,.67,"metal"),(.21,1,"trim"),(.26,.64,"glass")])


def draw(m,kind):
    if kind=="Barracks" and m.era>=1:
        training_quarters(m)
    elif kind=="MilitaryBase" and m.era==3:
        command_post(m)
    elif kind=="Watchtower" and m.era in (1,2):
        lookout(m)
    elif kind in ("Palisade","StoneWall"):
        screen(m,kind)
    elif kind=="Watchtower":
        watch(m,0,.08,.50,1.36*(.91,1.0,1.09,1.15)[m.era])
        m.lamp(.50,0,-.32,.105)
    else:
        garrison(m,kind)


if __name__=="__main__":
    generate("elf_military",draw)
