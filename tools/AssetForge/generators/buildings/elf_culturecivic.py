"""Elven public groves, performance courts and the crown arcology. Builder session 8."""
from pathlib import Path
import sys
from math import cos, sin, pi

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_learninghealth import TAU, generate, terrace
from generators.buildings.elf_institution_forms import arch_rib, deck, drum, vault


def grandstand(m, x, z, length, height, *, width=.37):
    steps=(5,3,1)[m.lod]
    for j in range(steps):
        h=height*(j+1)/steps
        d=width/steps
        m.box((x,h*.5,z+j*d),(length,h,d),(1,1,1),material="wall")
        if m.lod==0:
            m.box((x,h+.013,z+j*d-.006),(length*.99,.024,d*.94),(1,1,1),material="timber")


def contest_ground(m, kind):
    if kind=="Stadium":
        # A sprinters' oval lies below a single long, banked grandstand.
        deck(m,0,0,-.16,.83,.53,.092,"grass")
        drum(m,0,-.16,.74,.43,[(.089,1,"wall"),(.102,1,"banner")])
        drum(m,0,-.16,.60,.27,[(.101,1,"wall"),(.113,1,"grass")])
        for side in (-1,1):
            grandstand(m,side*.39,.26,.53,.52,width=.43)
        vault(m,0,.49,.132,.240,.20,.14,roof="ivory",ribs=False)
        for x in (-.48,.48):
            m.stem([(x,.03,.70),(x,.58,.69),(x,.91,.56)],[.068,.055,.036],"ivory")
            m.leaf((x,.88,.57),(x*.54,1.10,.07),.35,"roof")
        for x in (-.23,.23):
            m.box((x,.235,-.52),(.044,.29,.04),(1,1,1),material="ivory")
        m.box((0,.39,-.52),(.50,.042,.045),(1,1,1),material="accent")
        if m.lod==0:
            for x in (-.18,-.036,.108):
                m.box((x,.341,-.52),(.071,.074,.027),(1,1,1),material="banner")
        return
    if m.era==2:
        # The roofed judges' gallery runs transversely across the end of the duelling sand.
        deck(m,.015,0,-.17,.65,.57,.087,"soil")
        vault(m,.025,.49,.63,.216,.38,.36,door=False,roof="metal")
        grandstand(m,.025,.10,1.17,.365,width=.22)
        for side in (-1,1):
            with m.transform(translate=(side*.60,0,-.20),rotate_y=side*90):
                grandstand(m,0,0,.71,.26,width=.18)
            m.stem([(side*.63,.03,-.52),(side*.61,.40,-.53),(side*.55,.60,-.52)],
                   [.054,.04,.027],"timber")
            m.leaf((side*.565,.57,-.532),(side*.48,.29,-.565),.089,"banner")
        sparring_circle(m,.015,.087,-.20,.32)
        return
    # The suspended scoring ring follows the perimeter of the combat floor.
    deck(m,0,0,.025,.63,.55,.16,"soil")
    with m.transform(translate=(0,1.04,.026),rotate_x=90):
        m.hoop(0,0,0,.66,.54,.095,"roof")
    for a in (.27,2.37,4.14):
        c,s=cos(a),sin(a)
        m.stem([(.79*c,.026,.025+.68*s),(.72*c,.61,.025+.60*s),
                (.61*c,1.045,.025+.50*s)],[.093,.066,.052],"trim")
        drum(m,.79*c,.025+.68*s,.106,.11,[(0,1,"wall"),(.09,.91,"ivory")])
    for side in (-1,1):
        with m.transform(translate=(side*.48,0,.05),rotate_y=side*90):
            grandstand(m,0,0,.55,.31,width=.22)
    arch_rib(m,0,0,-.54,.255,.42,"ivory",width=.044)
    sparring_circle(m,0,.16,-.035,.32)
    if m.lod<2:
        for x in (-.43,.43):
            m.lamp(x,.16,-.275,.076)


def sparring_circle(m,x,y,z,r):
    # A bright scoring boundary and opposing fighters' marks identify the sand court.
    n=(15,9,6)[m.lod]
    m.lathe(x,z,r,r,[(y-.003,.98,"wall"),(y+.008,1,"ivory")],n=n)
    m.lathe(x,z,r*.83,r*.83,[(y+.007,.99,"timber"),(y+.014,1,"hide")],n=n)
    if m.lod<2:
        for s in (-1,1):
            m.box((x+s*.105,y+.021,z),(.035,.016,.112),(1,1,1),material="banner")


def canopy(m,x,z,w,h,angle=0):
    # Forks support a cupped sail on its raised outer edge.
    with m.transform(translate=(x,0,z),rotate_y=angle):
        for side in (-1,1):
            m.stem([(side*w*.74,.025,.10),(side*w*.81,h*.60,.12),(side*w*.68,h,.03)],
                   [.059,.037,.030],m.bark)
        m.bar((-w*.70,h,.03),(w*.70,h,.03),.036,"accent")
        m.leaf((-w*.98,h,.03),(w*.98,h+.056,.03),w*.47,"roof")
        if m.lod<2:
            m.lamp(-w*.67,h-.13,.032,.063)


def cooking_hearth(m):
    # A livewood crane suspends a communal cauldron above an oval fireboat.
    n = (13, 9, 5)[m.lod]
    m.lathe(.02, -.09, .50, .31, [(0,.86,"fieldstone"),(.15,1,"wall"),(.23,.92,"brick")], n=n)
    m.lathe(.02, -.09, .40, .22, [(.228,.95,"timber"),(.25,1,"forge")], n=n)
    fires = [(-.17,-.12,.46),(.03,-.10,.54),(.21,-.05,.41)]
    for x,z,h in fires:
        m.leaf((x,.245,z+.055),(x+.075,h,z-.02),.095,"forge")
        if m.lod == 0:
            m.leaf((x-.035,.25,z-.060),(x+.008,h-.038,z-.081),.051,"emissive")
    for side in (-1,1):
        m.lathe(side*.69,.14,.136,.115,[(0,1,"fieldstone"),(.084,.85,"wall")],n=(9,6,4)[m.lod])
        m.stem([(side*.69,.062,.14),(side*.58,.63,.15),(side*.39,1.04,.07),
                (side*.10,1.16,-.04)], [.114,.085,.059,.038], "timber", sides=(8,5,3)[m.lod])
        if m.lod<2:
            m.leaf((side*.51,.78,.11),(side*.83,1.04,.24),.105,"leaf")
        # Carved bench on continuous low feet leaves the southern approach open.
        m.lathe(side*.55,-.44,.21,.105,[(0,.71,"fieldstone"),(.13,.73,"wall"),(.19,1.04,"timber")],
                n=(11,7,4)[m.lod])
    m.bar((-.13,1.15,-.036),(.13,1.15,-.036),.038,"trim")
    m.bar((.006,1.15,-.036),(.006,.875,-.036),.022,"metal")
    handle = [(-.194,.718,-.036),(-.123,.822,-.036),(.006,.876,-.036),
              (.128,.818,-.036),(.196,.714,-.036)]
    if m.lod==2:
        handle = handle[::2]
    m.stem(handle,[.021]*len(handle),"metal",sides=(6,4,3)[m.lod])
    m.lathe(.004,-.036,.202,.167,[(.535,.40,"metal"),(.565,.83,"metal"),
                                  (.684,1,"metal"),(.72,.96,"gold")],n=n,bottom=True)
    if m.lod<2:
        m.lathe(.004,-.036,.173,.141,[(.719,.97,"hide"),(.727,1,"soil")],n=n)
    if m.lod==0:
        # A ladle rests inside the rim; two split logs lie on the back of the fireboat.
        m.bar((.078,.727,-.02),(.19,.845,.061),.016,"timber")
        for z in (.145,.23):
            m.bar((-.26,.274,z),(.27,.291,z+.038),.042,"timber")


def gathering(m,kind):
    e=m.era
    if kind=="Campfire":
        if e==1:
            cooking_hearth(m)
            return
        m.basin(0,0,.04,.43,.39,.15,"soil")
        for i in range(3):
            a=TAU*i/3
            m.stem([(.29*cos(a),.15,.04+.29*sin(a)),(.07*cos(a+.3),.41,.04+.07*sin(a+.3))],
                   [.074,.047],"timber")
        for x,z,h,role in [(-.09,.03,.41,"forge"),(.07,.05,.65,"emissive"),(.01,-.08,.38,"gold")]:
            m.lathe(x,z,.103,.087,[(.18,.81,role),(.26,1.13,role),(.18+h,.10,role)],n=(8,6,3)[m.lod])
        for i,a in enumerate((-.42,.84,2.10,3.54)):
            if m.lod==2 and i in (1,3):
                continue
            with m.transform(translate=(.65*cos(a),0,.64*sin(a)),rotate_y=-a*180/pi):
                rows=[(0,.77,"fieldstone"),(.15,1,m.bark),(.19,.92,"wall")]
                m.lathe(0,0,.15,.31,rows if m.lod==0 else rows[::2])
        if e:
            canopy(m,-.22,.58,.48,.74)
        else:
            m.stem([(-.65,.02,.38),(-.62,.49,.34),(-.46,.63,.34)],[.043,.031,.023],"timber")
            m.lamp(-.46,.46,.34,.085)
    elif kind=="Tavern":
        if e>=2:
            if e==2:
                # A horizontal cask carried on two saddles faces the serving yard.
                for z in (-.22,.46):
                    m.lathe(-.25,z,.33,.11,[(0,.96,"brick"),(.37,.82,"wall")])
                with m.transform(translate=(-.25,.54,.10),rotate_x=90):
                    m.lathe(0,0,.39,.38,[(-.53,.82,"timber"),(-.45,.93,"metal"),
                            (-.12,1,"timber"),(.33,.95,"timber"),(.48,.82,"trim")],bottom=True)
                    if m.lod==0:
                        for offset in (-.12,.12):
                            m.box((offset,-.539,0),(.023,.015,.48),(1,1,1),material="trim")
                m.hall(.44,.32,.28,.32,.49,.26,small=True)
                m.bar((-.25,.43,-.42),(-.25,.40,-.56),.046,"gold")
            else:
                # The taproom opens onto an elevated oval dining terrace.
                m.lathe(.06,.21,.57,.46,[(0,.68,"ivory"),(.56,.80,"glass"),(.68,1.29,"ivory")])
                m.hood(.03,.71,.45,.39,.34,.48,"glass",small=True)
                m.portal(.07,0,-.095,.26,.44)
                for x,z in (((-.37,.02),(.36,-.10)) if m.lod<2 else ()):
                    m.lathe(x,z,.11,.10,[(.68,.44,"trim"),(.82,.45,"trim"),(.84,1.14,"timber")])
                # The approach rises to a recessed bar. A tapped keg and handled
                # drinking cup sit on the counter, visible from the street.
                for j in range((5,3,2)[m.lod]):
                    steps=(5,3,2)[m.lod]
                    h=.669*(j+1)/steps
                    z=-.72+.48*(j+.5)/steps
                    m.box((.22,h*.5,z),(.32,h,.48/steps),(1,1,1),material="ivory")
                m.lathe(-.40,-.45,.29,.19,[(0,.88,"timber"),(.28,1.02,"trim")])
                m.lathe(-.51,-.43,.14,.14,[(.28,.77,"timber"),(.35,1,"trim"),
                        (.49,1.02,"timber"),(.55,.84,"gold")],n=(12,7,5)[m.lod])
                if m.lod<2:
                    m.lathe(-.25,-.43,.064,.066,[(.28,.80,"trim"),(.42,1,"gold")],n=(8,5)[m.lod])
                    m.hoop(-.18,.365,-.43,.039,.048,.014,"gold")
            if m.lod<2:
                m.lamp(.63,0,-.51,.095)
            return
        m.hall(-.20,.22,.49,.49,.83,.56)
        m.hall(.38,-.14,.38,.49,.43,.32,small=True)
        for x,z in [(-.38,-.43),(.05,-.48)]:
            m.lathe(x,z,.15,.14,[(0,.80,"timber"),(.08,.95,"trim"),(.24,1.09,"timber"),
                                  (.30,1,"metal"),(.38,.85,"timber")])
        m.lathe(-.64,-.72,.088,.10,[(0,.93,m.masonry),(.12,1,"accent")])
        m.stem([(-.64,.10,-.72),(-.64,.61,-.72),(-.71,.73,-.72),(-.88,.74,-.72)],
               [.043,.036,.028,.028],m.bark)
        m.bar((-.86,.74,-.72),(-.86,.62,-.72),.018,"accent")
        m.lathe(-.86,-.72,.114,.052,[(.38,.45,"accent"),(.44,1,"gold"),(.59,1,"gold"),
                                      (.64,.53,"accent")],n=(9,6,4)[m.lod])
    elif kind=="Theater":
        if e==2:
            # A raised proscenium and tall fly loft carry the indoor scenery machinery.
            loft=[(0,1,"brick"),(.19,.97,"trim"),(.83,.79,"wall"),(1.14,.83,"roof"),(1.21,.74,"metal")]
            profile=loft if m.lod==0 else [loft[i] for i in (0,2,4)]
            m.lathe(.04,.27,.66,.44,profile,n=(14,8,6)[m.lod])
            m.lathe(.04,-.17,.62,.37,[(0,.92,"wall"),(.22,1,"trim"),(.27,1,"timber")],
                    n=(14,8,6)[m.lod])
            for side in (-1,1):
                x=.04+side*.47
                m.stem([(x,.245,-.28),(x+side*.05,.72,-.23),(x-side*.12,1.03,-.18)],
                       [.10,.071,.077],m.bark)
            m.hood(.04,.99,-.145,.47,.26,.21,"roof",small=True)
            # Three open rows turn toward the stage, with a center aisle.
            for x in (-.36,.36):
                for j in range((3,2,1)[m.lod]):
                    row=2*j if m.lod==1 else j
                    z=-.43-.15*row
                    height=.115+.095*row
                    m.box((x,height/2,z),(.53,height,.135),(1,1,1),material="wall")
                    if m.lod<2:
                        m.box((x,height+.020,z),(.55,.04,.144),(1,1,1),material="banner")
            if m.lod<2:
                for x in (-.32,.40):
                    m.leaf((x,.88,-.245),(x+.04,.37,-.24),.13,"banner")
            return
        if e==3:
            # A shell of overlapping acoustic leaves opens forward over a broad public stage.
            m.lathe(0,.11,.52,.35,[(0,.89,"wall"),(.24,1,"ivory"),(.33,.96,"timber")])
            # Opposed audience banks rake away from the central thrust stage.
            for side in (-1,1):
                for j in range((3,2,1)[m.lod]):
                    x=side*(.36+j*.15)
                    h=.16+j*.11
                    m.lathe(x,-.32,.12,.44,[(0,.92,"wall"),(h,1,"banner")],n=(9,6,4)[m.lod])
            m.lathe(0,.27,.65,.33,[(0,.96,"wall"),(.45,1,"glass"),(.53,.91,"roof")])
            petals=(5,5,3)[m.lod]
            for j in range(petals):
                a=-.82+1.64*j/(petals-1)
                cx=.52*sin(a)
                if m.lod==2:
                    m.stem([(cx,.36,.28),(cx*1.25,1.04,.25)],[.063,.034],m.bark,sides=3)
                else:
                    m.stem([(cx,.36,.28),(cx*1.19,.84,.35),(cx*1.25,1.04,.25)],
                           [.063,.042,.034],m.bark)
                m.leaf((cx*1.25,1.00,.28),(1.04*sin(a),1.19+.26*cos(a),-.43*cos(a)),.26,"roof")
            if m.lod<2:
                for x in (-.72,.72):
                    m.lamp(x,.03,-.32,.105)
            return
        terrace(m,0,.06,.83,.78,-.90,4.04,(4,3,2)[m.lod])
        m.lathe(0,-.24,.45,.28,[(0,.94,"fieldstone"),(.12,1,"accent"),(.17,.96,"timber")])
        canopy(m,0,.51,.69,.86+.10*e)
        for x in (-.49,.49):
            m.lamp(x,.17,-.11,.09)
    elif kind in ("Arena","Stadium"):
        stadium=kind=="Stadium"
        if (stadium and e==3) or (not stadium and e>=2):
            contest_ground(m,kind)
            return
        terrace(m,0,.06,.87,.82,-1.04,4.18,(5,3,2)[m.lod])
        m.lathe(0,.06,.48,.42,[(0,1,"fieldstone"),(.052,1,"grass" if stadium else "soil")])
        if stadium:
            for x in (-.54,.54):
                canopy(m,x,.21,.46,.95+.12*(e-2),90 if x<0 else -90)
            for x in (-.37,.37):
                m.hoop(x,.18,.04,.068,.135,.025,"ivory",90)
        else:
            for x,z in [(-.53,.61),(.58,.55)]:
                m.lathe(x,z,.17,.15,[(0,1,m.masonry),(.46,.9,"wall"),(.73+.05*e,.74,"trim")])
                m.hood(x,.73+.05*e,z,.22,.21,.32,small=True)
            for x in (-.39,.39):
                m.lamp(x,0,-.65,.095)


def civic(m,kind):
    if kind=="Forum":
        if m.era>=2:
            if m.era==2:
                # The speaker stands on a balcony under a radial fan of roof ribs.
                m.lathe(.02,.40,.53,.35,[(0,.91,"brick"),(.39,1,"wall"),(.50,1.04,"trim")])
                for x in (-.39,.43):
                    m.bar((x,.49,.40),(x,.92,.41),.067,"metal")
                m.hood(.02,.92,.38,.57,.40,.28,"metal")
                for j in range((3,2,2)[m.lod]):
                    h=.13+.072*j
                    m.box((.01,h/2,-.26-.18*j),(1.16-.09*j,h,.13),(1,1,1),material="timber")
                m.lathe(.05,.26,.14,.10,[(.50,.74,"trim"),(.67,.67,"wall"),(.74,1.12,"ivory")],
                        n=(14,8,4)[m.lod])
                for j in range((4,3,2)[m.lod]):
                    n=(4,3,2)[m.lod]
                    h=.50*(j+1)/n
                    m.box((-.55+.28*(j+.5)/n,h*.5,.29),(.28/n,h,.20),(1,1,1),material="brick")
            else:
                # A circular debating chamber surrounds a freestanding lectern.
                terrace(m,.01,.035,.76,.67,-.75,3.87,(3,2,1)[m.lod])
                m.lathe(-.05,-.26,.23,.21,[(0,.78,"ivory"),(.36,.83,"glass"),(.42,1.20,"ivory")])
                for x in (-.55,.53):
                    m.stem([(x,.37,.41),(x*1.09,.96,.37),(x*.60,1.22,.34)],
                           [.073,.052,.033],"ivory")
                m.bar((-.33,1.21,.34),(.32,1.21,.34),.037,"gold")
            if m.lod<2:
                m.lamp(.52,0,-.53,.08)
            return
        m.hall(-.10,.31,.51,.38,.68,.43)
        for side in (-1,1):
            for j in range(3 if m.lod<2 else 2):
                z=.12-.28*j
                x=side*(.67-.045*j)
                m.stem([(x,.03,z),(x-.03*side,.43,z),(x-.12*side,.63,z+.035)],
                       [.082,.046,.057],m.bark)
            m.hood(side*.60,.62,-.16,.20,.51,.19,small=True)
        m.lathe(0,-.48,.25,.18,[(0,.81,m.masonry),(.18,.84,"wall"),(.30,1,"accent")])
    else:
        # Continuous inhabited terraces widen into sky gardens, then contract up the spire.
        x,z=-.07,.12
        for i,(y,r,h,dx) in enumerate([(0,.63,.65,0),(.59,.49,.62,.05),(1.15,.34,.48,.10)]):
            cx=x+dx
            rows=[(y,.91,"ivory"),(y+h*.20,.86,"trim"),(y+h*.80,.79,"glass"),
                  (y+h*.91,1.11,"ivory"),(y+h,1.09,"leaf")]
            m.lathe(cx,z,r,r*.81,rows if m.lod<2 else rows[::2])
        m.hood(.03,1.63,z,.33,.30,.49,"glass")
        m.portal(x,0,-.36,.38,.49)
        for side in (-1,1):
            m.stem([(side*.68,.05,-.02),(side*.62,.49,.09),(side*.27,1.27,.15)],
                   [.10,.069,.047],"ivory")
        m.lamp(.54,0,-.44,.135)


def draw(m,kind):
    if kind in ("Forum","Arcology"):
        civic(m,kind)
    else:
        gathering(m,kind)


if __name__=="__main__":
    generate("elf_culturecivic",draw)
