"""Exchange courts and transport landings of the sun delta."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

PACKAGE = "saurian_commerce"


def awning(m, x, y, z, w, d, rise, role, alternate):
    """Stitched awning with a high back rail and a bowed middle seam."""
    bands = (5, 3, 1)[m.lod]
    for i in range(bands):
        left, right = (x + w * (a / bands - .5) for a in (i, i + 1))
        material = alternate if i % 2 else role
        a = [(u, y + v, z + t) for u in (left, right)
             for v, t in ((0, -d/2), (.22*rise, -.04*d), (rise, d/2))]
        m.face(material, (a[0], a[1], a[4], a[3]))
        m.face(material, (a[1], a[2], a[5], a[4]))
        m.face(material, (a[0], a[3], (right, y-.045, z-d/2),
                          ((left+right)/2, y-.094, z-d/2-.009), (left, y-.045, z-d/2)))
        if m.lod < 2:
            for f in ((0, 3, 4, 1), (1, 4, 5, 2)):
                m.face(material, [(a[j][0], a[j][1]-.012, a[j][2]) for j in f])
    for side in (-1, 1):
        m.block("trim", x+side*w*.46, .08, z-d*.47, .050, y-.08, .052)
        m.block("trim", x+side*w*.46, .08, z+d*.47, .054, y+rise-.08, .056)
    if m.lod < 2:
        m.block("trim", x, y-.024, z-d*.48, w, .028, .037)


def planter(m, x, y, z, r):
    m.drum("hide", x,y,z,r*.70,r*1.26,r,n=(7,5,4)[m.lod])
    for i in range((5,3,2)[m.lod]):
        angle=2*math.pi*i/(5,3,2)[m.lod]+.22
        m.blade("leaf", (x,y+r*1.20,z),
                (x+r*1.19*math.cos(angle),y+r*(2.1+.4*(i%2)),z+r*1.19*math.sin(angle)),r*.37)


def pennon(m, x, y, z, h):
    m.block("trim",x,y,z,.029,h,.031)
    a=(x+.012,y+h-.02,z)
    b=(x+.154,y+h-.055,z+.028)
    c=(x+.258,y+h-.078,z+.003)
    e=(x+.012,y+h-.151,z)
    d=(x+.154,y+h-.181,z+.028)
    for face in ((a,b,d,e),(b,c,d)):
        m.face("turquoise",tuple(reversed(face)))
        m.face("turquoise", [(u,v,w+.005) for u,v,w in face])


def counter(m,x,z,w,goods):
    m.slab("timber" if m.era==1 else "ivory",x,.08,z,w,.22,.275,.87)
    m.slab("turquoise",x,.30,z,w*1.04,.045,.298,underside=True)
    for i,role in enumerate(goods):
        if m.lod==2:
            break
        m.mound(role,x+(i-(len(goods)-1)/2)*w*.26,.345,z,w*.098,.078,n=(8,5)[m.lod])


def exchange_market(m):
    works.court(m)
    if m.era==1:
        # An open trading loggia carries a clay-tiled roof on cut stone columns.
        for x in (-.68,0,.68):
            for z in (.03,.59):
                m.drum("wall",x,.08,z,.086,.63,.068)
                if m.lod<2:
                    m.drum("turquoise",x,.624,z,.088,.066)
        m.slab("wall",0,.71,.31,1.61,.115,.79,underside=True)
        m.hip_roof((0,.825,.31),1.55,.75,.46,m.color("roof_tile"),
                   overhang=.048,material="roof_tile")
        for x in (-.46,.48):
            counter(m,x,-.235,.65,("leaf","thatch","flower"))
        works.cartouche(m,0,.762,-.095,.059)
    elif m.era==2:
        # Covered wholesale hall with a tall sawtooth clerestory and a row of
        # traders' counters beneath the broad awning on the street.
        works.northlights(m,0,.25,1.54,.93,.61,.40)
        works.shutters(m,0,.51,-.226,1.46,.135,6)
        awning(m,.025,.52,-.397,1.53,.34,.087,"turquoise","ivory")
        for x in (-.52,.52):
            counter(m,x,-.52,.38,("leaf","hide"))
        if m.lod<2:
            m.jar(.47,.08,-.20,.081,"hide")
    else:
        # A mushroom-shaped shopping rotunda shelters a ring of vendor islands.
        m.drum("glass",0,.08,.22,.31,.55,.28)
        m.block("ivory",0,.08,-.055,.32,.40,.16)
        m.entry(0,.08,-.135,.20,.335)
        m.drum("ivory",0,.63,.22,.39,.26,.73,n=(16,10,7)[m.lod])
        m.drum("turquoise",0,.89,.22,.746,.059,.70,n=(16,10,7)[m.lod])
        m.mound("glass",0,.949,.22,.67,.29)
        for x,z in ((-.56,-.23),(.52,-.34),(0,-.53)):
            counter(m,x,z,.40,("gold","leaf"))
        if m.lod<2:
            m.disc(0,.80,-.282,.078)


def market(m):
    if m.era:
        return exchange_market(m)
    m.slab("fieldstone",0,0,0,1.87,.08,1.77,.986,cut=.075)
    e=m.era
    # The roofline steps from the high exchange hut into the two open produce bays.
    m.slab("wall",-.23,.08,.37,1.13,.63,.62,cut=.10)
    if e<2:
        m.rooflet(-.23,.71,.37,1.28,.76,.40 if e==0 else .56,"thatch" if e==0 else "roof")
    else:
        m.slab("trim",-.23,.71,.37,1.21,.064,.73,underside=True)
        m.slab("glass" if e==3 else "roof",-.23,.774,.37,.94,.32 if e==3 else .21,.57,.62)
        if e==3:
            m.block("ivory",-.23,1.09,.37,.63,.073,.36)
        else:
            m.slab("brick",-.23,.984,.37,.53,.139,.32)
            m.slab("metal",-.23,1.122,.37,.62,.078,.40,.71,underside=True)
    m.entry(-.30,.08,.06,.26,.40)
    # Permanent booths occupy opposite sides of the open path to the exchange.
    for side in (-1,1):
        x=side*.525
        z= -.27 if side==-1 else -.07
        if side==1:
            z += .19 if e>=2 else .08
        y=(.59,.67,.72,.77)[e] + .065*(side==1)
        role = "thatch" if e==0 and side==-1 else ("turquoise" if side==1 else "cloth")
        alt = "ivory" if e!=3 else "glass"
        awning(m,x,y,z,.715,.72,.19 if e<2 else .09,role,alt)
        m.block("timber" if e<2 else "trim",x,.08,z-.235,.58,.20,.227)
        m.block("thatch" if side==-1 else "turquoise",x,.28,z-.235,.61,.041,.25)
        if m.lod<2:
            for j in range(3 if not m.lod else 2):
                px=x+(j-1)*.156
                m.block("soil",px,.321,z-.235,.14,.031,.195)
                m.mound(("flower","leaf","thatch")[j],px,.352,z-.235,.061,.048,n=6)
    m.block("trim",.23,.08,-.76,.059,.72,.061)
    m.disc(.23,.835,-.76,.111)
    if m.lod==0:
        planter(m,-.74,.08,.65,.083)
        m.jar(.78,.08,-.59,.092)
        if e>0:
            pennon(m,-.33,(1.279,1.19,1.15)[e-1],.37,.28)


def solar_treasury(m):
    """A sloping armoured strongroom and a lower public banking hall share a forecourt."""
    x,z=-.274,.115
    m.slab("fieldstone",.005,0,.035,1.82,.105,1.43,.975,cut=.125)
    # The chamfered concrete vault rises toward its locked street face. The
    # shallow glazing at the eaves follows the incline of the armour above it.
    outline=[(-.402,-.570),(.402,-.570),(.582,-.392),(.582,.408),
             (.411,.57),(-.411,.57),(-.582,.408),(-.582,-.392)]
    def course(width,depth,height,slope):
        return [(x+u*width,height-v*depth*slope,z+v*depth) for u,v in outline]
    foundation=course(1,1,.105,0)
    shoulder=course(.944,.947,.836,.475)
    clerestory=course(.950,.953,.907,.475)
    fascia=course(1.005,1.017,.931,.475)
    roof=course(.974,.985,.993,.475)
    for lower,upper,role in ((foundation,shoulder,"wall"),(shoulder,clerestory,"glass"),
                             (clerestory,fascia,"metal"),(fascia,roof,"ivory")):
        for i,a in enumerate(lower):
            j=(i+1)%len(lower)
            m.face(role,(lower[j],a,upper[i],upper[j]))
    m.face("metal",roof[::-1])
    # Two raised plates are bolted along the roof fall, with turquoise weather seals.
    pitch=math.degrees(math.atan(.475))
    for side in (-1,1):
        with m.transform(translate=(x+side*.269,.993,z),rotate_x=pitch):
            if m.lod<2:
                m.slab("turquoise",0,0,0,.196,.032,.985,.93,cut=.16)
            m.slab("ivory",0,.032 if m.lod<2 else 0,0,.172,.059,.947,.76,cut=.20)
    # A thick door leaf, sun-wheel dial and two hinge knuckles sit inside the wall outline.
    door_y=.586
    m.block("trim",x,.159,-.428,.796,.839,.102,.026)
    m.disc(x,door_y,-.480,.365,role="metal",boss=False)
    m.disc(x,door_y,-.571,.289,role="ivory",boss=False)
    if m.lod<2:
        m.disc(x,door_y,-.643,.196,role="trim",boss=False)
    else:
        m.block("trim",x,door_y-.123,-.665,.255,.245,.070,.10)
    m.ring("gold",x,door_y,-.708,.143,.108,.026,n=(18,12,7)[m.lod])
    m.disc(x,door_y,-.702,.044,role="gold",boss=False)
    for angle in (0,120,240):
        with m.transform(translate=(x,door_y,-.700),rotate_z=angle):
            m.block("gold",0,.024,0,.026,.100,.019)
    for y in (.379,.739):
        m.block("gold",x+.282,y,-.544,.177,.070,.113,.08)
    if m.lod==0:
        for i in range(8):
            angle=math.tau*(i+.5)/8
            with m.transform(translate=(x+.257*math.cos(angle),door_y+.257*math.sin(angle),-.645),
                             rotate_x=-90):
                m.drum("metal",0,0,0,.025,.010,n=8)

    # Clerks serve through a continuous counter in the side wing. Its flat
    # canopy covers the public entrance and turns the corner above the wicket.
    hx,hz=.548,.065
    m.slab("wall",hx,.105,hz,.648,.490,1.095,.98,cut=.10)
    m.slab("ivory",hx,.595,hz,.735,.098,1.205,.95,cut=.13,underside=True)
    m.slab("turquoise",hx,.693,hz,.615,.071,.958,.88,cut=.19)
    m.entry(hx,.106,-.492,.262,.373)
    m.stairs(hx,0,-.876,.395,.252,.109)
    m.block("door",.859,.278,hz,.039,.235,.754)
    m.slab("turquoise",.864,.257,hz,.116,.042,.781,.96,cut=.07)
    for offset in ((-.251,0,.251) if m.lod<2 else (-.17,.17)):
        m.block("gold",.883,.294,hz+offset,.024,.219,.027)
    if m.lod<2:
        m.disc(hx,.528,-.495,.066,boss=False)
        # Reinforced piers along the blind side bear the sloping vault roof.
        for offset in (-.258,.223):
            height=.806-offset*.475
            m.slab("metal",x-.552,.105,z+offset,.139,height,.139,.61,cut=.09)
    if m.lod==0:
        for offset in (-.33,.33):
            m.slab("ivory",.873,.112,hz+offset,.105,.148,.095,.68,cut=.11)


def bank(m):
    e=m.era
    if e==3:
        return solar_treasury(m)
    m.slab("fieldstone",0,0,.03,1.74,.13,1.54,.95,cut=.14)
    m.slab("wall",0,.13,.12,1.55,.54,1.16,.96)
    # Broad barred treasury wing on either side of a south-facing public entrance.
    m.slab("trim",0,.667,.12,1.61,.083,1.22,.98,underside=True)
    if e==1:
        m.rooflet(0,.75,.18,1.48,1.14,.40,"roof")
        m.slab("wall",0,1.15,.18,.54,.093,.43)
    elif e==2:
        m.slab("brick",0,.75,.18,1.19,.31,1.01,.85)
        m.slab("metal",0,1.06,.18,1.05,.12,.90,.81)
        for s in (-1,1):
            m.block("trim",s*.56,.73,.36,.148,.32,.22)
    else:
        m.slab("glass",0,.75,.18,1.24,.46,.99,.75)
        m.slab("ivory",0,1.21,.18,1.00,.092,.81,.96)
    top=(0,1.24,1.18,1.30)[e]
    m.slab("turquoise",0,top-.012,.16,.34,.082,.30,.83)
    m.disc(0,top+.243,.16,.204)
    m.block("trim",0,.13,-.536,.66,.62,.30)
    m.slab("wall",0,.73,-.536,.84,.15,.43,.76,underside=True)
    m.entry(0,.13,-.687,.321,.44)
    m.stairs(0,0,-.936,.56,.262,.131)
    for s in (-1,1):
        x=s*.575
        m.block("door",x,.324,-.463,.34,.237,.033)
        if m.lod<2:
            for j in (-1,0,1):
                m.block("gold",x+j*.089,.32,-.494,.023,.241,.024)
            # The descending shoulders anchor the central cash vault to the podium.
            m.slab("wall",s*.378,.13,-.52,.163,.49,.41,.70)
    if m.lod==0:
        planter(m,-.684,.13,-.56,.102)
        m.disc(0,.766,-.745,.067,boss=False)
        for z in (-.17,.19,.51):
            m.block("turquoise",.745,.367,z,.038,.138,.155)


def sail(m,x,y,z,width,height,depth,role,reverse=False):
    """Four panels meet along the projecting batten of a curved delta sail."""
    s=-1 if reverse else 1
    hem=(x+s*.01,y,z-depth*.12)
    heel=(x+s*width,y+height*.14,z)
    tack=(x+s*width*.75,y+height*.43,z+depth*.24)
    tip=(x,y+height,z+depth)
    spine=(x+s*width*.27,y+height*.48,z+depth*.49)
    for a,b in ((heel,hem),(tack,heel),(tip,tack),(hem,tip)):
        face=[a,b,spine]
        if reverse:
            face.reverse()
        m.face(role,face)
        m.face(role,[(u,v,w+.009) for u,v,w in reversed(face)])


def charter_assembly(m):
    # The ceremonial stair opens onto a broad council loggia. Its folded
    # roof rises on two outward-facing wings above the public chamber.
    works.court(m,-.19)
    m.slab("ivory",-.18,.08,.15,1.28,.20,1.10,cut=.075)
    m.stairs(-.19,.08,-.72,.67,.32,.20)
    works.shell(m,-.18,.33,1.02,.65,.55,.28,door=False,material="glass")
    m.block("ivory",-.18,.28,-.012,.49,.54,.10)
    m.entry(-.18,.28,-.062,.40,.44)
    m.slab("metal",-.18,.83,.30,.78,.14,.51)
    for x in (-.70,.34):
        m.block("ivory",x,.28,-.19,.103,.86,.12)
    offsets=(-.69,-.31,.31,.69)
    eaves=(1.25,.957,.957,1.25)
    front,back=-.31,.74
    for i,(left,right) in enumerate(zip(offsets,offsets[1:])):
        x1,x2=left-.18,right-.18
        low,high=eaves[i],eaves[i+1]
        A,B,C,D=(x1,low,front),(x2,high,front),(x2,high,back),(x1,low,back)
        E,F,G,H=((x,y+.071,z) for x,y,z in (A,B,C,D))
        m.face("ivory",[A,B,C,D])
        m.face("turquoise",[H,G,F,E])
        m.face("gold",[B,A,E,F])
        m.face("ivory",[D,C,G,H])
        if i==0:
            m.face("ivory",[A,D,H,E])
        if i==2:
            m.face("ivory",[C,B,F,G])
    # The public charter hangs from a tall, footed standard at the stairhead.
    x,z=.72,-.37
    m.slab("ivory",x,.08,z,.26,.17,.27,.78)
    m.drum("gold",x,.25,z,.043,1.16,.033)
    m.rod("gold",(x-.46,1.32,z),(x+.047,1.32,z),.033)
    a=(x-.41,1.30,z); b=(x-.08,1.30,z)
    c=(x-.08,.76,z-.027); d=(x-.245,.87,z-.039); e=(x-.41,.72,z-.033)
    for poly in ((a,b,d),(b,c,d),(a,d,e)):
        m.face("banner",poly)
        m.face("turquoise",[(xx,yy,zz+.014) for xx,yy,zz in reversed(poly)])
    m.disc(x-.245,1.115,z-.017,.081,boss=False)
    if m.lod==0:
        m.disc(-.18,.76,-.068,.051)


def guild(m):
    e=m.era
    if e>=2:
        if e==2:
            # The weigh house and the trade clock stand on opposite sides
            # of a single brick exchange floor.
            works.court(m,-.25)
            works.northlights(m,-.25,.17,1.08,1.13,.54,.29)
            works.shell(m,.52,-.23,.41,.48,1.04,material="brick")
            m.slab("trim",.52,1.12,-.23,.49,.10,.55,.95,underside=True)
            m.rooflet(.52,1.22,-.23,.47,.52,.29,"roof")
            m.disc(.52,.91,-.477,.138)
            if m.lod==0:
                m.rod("trim",(.52,.91,-.522),(.581,.967,-.522),.012,n=4)
            works.shutters(m,-.25,.38,-.405,.99,.14,4)
        else:
            charter_assembly(m)
        return
    m.slab("fieldstone",0,0,.03,1.85,.09,1.64,.978,cut=.11)
    m.slab("wall",-.10,.09,.17,1.49,.63,1.08,.97)
    m.slab("trim",-.10,.72,.17,1.60,.07,1.14,underside=True)
    # The raised charter house sits between two lower porticoes.
    m.slab("glass" if e==3 else "wall",-.13,.79,.23,.92,.31 if e==1 else .40,.76,.91)
    h=1.10 if e==1 else 1.19
    m.rooflet(-.13,h,.23,1.19,.98,(.0,.42,.25,.22)[e],"roof" if e<3 else "ivory")
    if e==2:
        m.drum("brick",.37,.68,.51,.104,.72,.081)
        m.drum("metal",.37,1.4,.51,.10,.052)
        m.slab("wall",-.13,1.44,.23,.445,.13,.34,.91)
        m.slab("roof",-.13,1.57,.23,.53,.068,.41,.76,underside=True)
    # A charter pennant hangs from the sloping yard over the public portico.
    x,z=.32,-.396
    base=.09
    height=(0,1.38,1.53,1.67)[e]
    m.rod("trim" if e==1 else "metal",(x,base,z),(x-.05,height,z+.07),.039,n=6)
    sail(m,x-.052,height-.64,z+.05,.39,.625,.041,"banner")
    if m.lod<2:
        m.rod("gold",(x-.055,height-.63,z+.045),(x+.34,height-.55,z+.05),.016,n=4)
    if e==3:
        sail(m,x-.054,height-.52,z+.06,.21,.51,.046,"glass",True)
    for sx in (-.63,.36):
        m.block("trim",sx,.09,-.59,.094,.52,.10)
    m.slab("turquoise",-.135,.61,-.475,1.20,.107,.51,.82,underside=True)
    m.entry(-.21,.09,-.372,.30,.44)
    m.stairs(-.21,0,-.926,.55,.15404,.09)
    if m.lod < 2:
        m.disc(-.14,h-.133,-.128,.104)
        for sz in (-.04,.22,.49):
            m.block("glass" if e>1 else "banner",.633,.30,sz,.036,.24,.138)
        planter(m,.61,.09,-.59,.09)
    if m.lod==0:
        m.jar(-.70,.09,-.518,.080,"thatch")
        for s in (-1,1):
            m.block("gold",-.13+s*.403,.805,-.151,.044,.236,.047)


def cargo_boat(m,x,z,scale):
    e=m.era
    with m.transform(translate=(x,0,z),scale=scale):
        plan=[(0,-.76),(.162,-.51),(.216,.19),(.144,.56),(0,.66),(-.144,.56),(-.216,.19),(-.162,-.51)]
        if m.lod==2:
            plan=[plan[i] for i in (0,2,4,6)]
        keel=[(.62*a,.042+abs(b)**2*.077,.86*b) for a,b in plan]
        sheer=[(a,.203+abs(b)**2*.23,b) for a,b in plan]
        for j in range(len(plan)):
            k=(j+1)%len(plan)
            m.face("timber" if e==1 else "roof",(keel[k],keel[j],sheer[j],sheer[k]))
        # Flush cargo deck spans the hull, with an actual sheer line towards bow and stern.
        deck=(0,.19,0)
        for j in range(len(plan)):
            m.face("thatch" if e==1 else "metal",(sheer[(j+1)%len(plan)],sheer[j],deck))
        if e==1:
            m.rod("timber",(0,.192,.06),(0,1.05,.09),.026,n=5)
            with m.transform(translate=(0,.415,.066),rotate_y=-27):
                sail(m,0,0,0,.453,.615,.034,"ivory")
                if m.lod<2:
                    m.rod("timber",(0,0,0),(.462,.085,.002),.011,n=4)
        else:
            m.slab("wall",0,.212,.23,.30,.192,.34,.85)
            m.slab("glass",0,.404,.23,.262,.102,.30,.88)
            m.slab("ivory",0,.506,.23,.315,.040,.35,underside=True)
            m.block("turquoise",0,.22,-.22,.249,.128,.37)
            if m.lod<2:
                m.block("gold",0,.348,-.22,.047,.011,.37)
        if m.lod==0:
            m.rod("trim",(0,.265,-.58),(0,.406,-.58),.016,n=5)


def harbor(m):
    e=m.era
    if e>=2:
        m.slab("water",0,0,0,1.93,.055,1.85,cut=.10)
        if e==2:
            # A bucket elevator feeds a long, high loading spout over the barge.
            m.slab("brick",-.56,.055,.08,.72,.21,1.53,.97)
            works.barrel(m,-.56,.39,.59,.74,.37,.26,y=.265)
            for x in (-.46,-.20):
                for z in (-.43,-.17):
                    m.block("metal",x,.265,z,.066,.75,.067)
            works.vessel(m,-.33,-.30,.22,.42,y=1.015)
            m.rod("metal",(-.29,1.23,-.30),(.38,.66,-.27),.095)
            cargo_boat(m,.53,-.10,.92)
            m.stairs(-.59,.055,-.895,.30,.21,.21)
        else:
            # A drive-through tidal lock straddles the hull, with a high control
            # room carried by the lock walls and two distinct navigation beacons.
            for x in (-.58,.58):
                m.slab("ivory",x,.055,0,.39,.37,1.56,.92)
                m.slab("turquoise",x,.425,0,.385,.075,1.57,.96)
                works.shell(m,x,.28,.32,.52,.49,y=.50,door=False)
                m.drum("gold",x,.50,-.56,.087,.20,.066)
                m.mound("emissive",x,.70,-.56,.066,.09)
            m.slab("glass",0,.99,.28,1.54,.26,.53,.91,underside=True)
            m.slab("ivory",0,1.25,.28,1.49,.07,.55,.90,underside=True)
            works.cartouche(m,0,1.118,.014,.066)
            cargo_boat(m,0,-.32,.81)
            m.stairs(-.58,.055,-.96,.30,.18,.445)
        return
    m.slab("water",0,0,0,1.93,.060,1.86,.985,cut=.096)
    for x in (-.76,.106):
        for z in (-.52,.65):
            m.block("timber" if e==1 else "fieldstone",x,.055,z,.108,.216,.12)
    m.slab("timber" if e==1 else "wall",-.34,.258,.108,1.20,.105,1.434,cut=.05,underside=True)
    cargo_boat(m,.543,-.02,.84)
    # The freight office backs the landing; reach of the shore crane crosses the gunwale.
    with m.transform(translate=(-.37,.363,.419),scale=.94):
        m.house(0,0,0,.91,.57,.61)
    m.stairs(-.32,.06,-.925,.447,.316,.303)
    base=(.03,.363,-.22)
    end=(.534,1.01,-.265)
    m.drum("metal" if e>=2 else "timber",base[0],base[1],base[2],.098,.14)
    mast=(.025,1.24,-.22)
    m.rod("trim",(.025,.50,-.22),mast,.043)
    m.rod("timber" if e==1 else "metal",(.025,.64,-.22),end,.048)
    m.rod("gold",mast,end,.014,n=4)
    m.rod("trim",end,(.534,.65,-.265),.013,n=4)
    m.block("hide",.534,.558,-.265,.115,.093,.12)
    if m.lod<2:
        badge_y=1.30 if e==3 else (1.18 if e==1 else 1.05)
        m.block("trim",-.37,badge_y-.17,.408,.061,.13,.056)
        m.disc(-.37,badge_y,.408,.092)
        for x,z in ((-.65,-.44),(-.45,-.42)):
            m.block("hide",x,.362,z,.170,.152,.18)
            if m.lod==0:
                m.block("gold",x,.51,z,.041,.010,.181)
        if e==3:
            m.slab("glass",-.38,.975,.42,.45,.168,.29,.65)
    if m.lod==0:
        m.jar(-.76,.363,.14,.083)
        for z in (-.52,.18,.67):
            m.drum("trim",.211,.358,z,.039,.092,n=6)


def rails(m):
    m.slab("fieldstone",0,0,0,1.89,.062,1.89,.985,cut=.075)
    for i in range((12,7,3)[m.lod]):
        z=-.84+1.68*i/((12,7,3)[m.lod]-1)
        m.block("timber",.44,.062,z,.66,.031,.057)
    for x in (.22,.66):
        m.block("metal",x,.093,0,.035,.038,1.84)


def station(m):
    e=m.era
    if e==3:
        # The maglev runs across the site on a raised guideway. Its terminal
        # occupies the ground floor and runs to the sheltered boarding level.
        works.court(m,-.32)
        works.shell(m,-.32,.25,.97,.83,.52,material="glass")
        for x in (-.63,.06,.66):
            m.slab("ivory",x,.08,-.37,.19,.63,.29,.78)
        m.slab("turquoise",0,.71,-.33,1.82,.105,.47,.95,underside=True)
        m.slab("metal",0,.815,-.33,1.76,.046,.27,.98)
        m.slab("ivory",-.08,.861,-.33,1.28,.21,.37,.89,cut=.21)
        m.slab("glass",-.08,1.071,-.33,1.09,.14,.33,.73,cut=.25)
        m.slab("ivory",-.32,.60,.25,1.08,.10,.90,.89,underside=True)
        for x in (-.67,.03):
            m.block("ivory",x,.70,.36,.086,.42,.123)
        works.panel(m,-.32,1.12,.36,1.10,.67,-12)
        if m.lod<2:
            works.cartouche(m,-.32,.47,-.172,.065)
        return
    rails(m)
    m.slab("wall",-.43,.062,.02,.88,.10,1.73,.99,cut=.04)
    m.slab("brick" if e==2 else "glass",-.54,.162,.43,.55,.46,.61,.96)
    m.entry(-.54,.162,.125,.21,.35)
    # Long, upswept eaves shade the loading platform. A south stair opens off the railway.
    for z in (-.60,.06,.68):
        m.block("metal",-.60,.16,z,.062,.53,.058)
        if m.lod<2:
            m.rod("trim",(-.60,.46,z),(-.20,.70,z),.026,n=4)
    if e==2:
        m.rooflet(-.46,.686,.035,.92,1.76,.226,"roof")
        m.drum("brick",-.72,.61,.62,.072,.42,.059)
        m.drum("trim",-.72,1.03,.62,.079,.055)
    else:
        m.slab("ivory",-.455,.685,.035,.96,.082,1.78,.85,cut=.16,underside=True)
        m.slab("glass",-.46,.767,.059,.69,.16,1.40,.69,cut=.18)
    m.disc(-.45,.793,-.827,.082)
    m.stairs(-.47,.061,-.947,.36,.155,.101)
    # Broad wheels land on the rail heads. A ribbed motor cabin terminates the forward car.
    for x in (.22,.66):
        for z in (-.23,.35):
            with m.transform(translate=(x-.037,.181,z),rotate_z=-90):
                m.drum("trim",0,0,0,.057,.074,n=(8,6,4)[m.lod],bottom=True)
    m.slab("trim",.44,.201,.072,.59,.079,1.01,cut=.13,underside=True)
    if e==2:
        m.slab("roof",.44,.278,.08,.57,.236,.98,.93,cut=.14)
        m.slab("turquoise",.44,.514,.222,.52,.164,.57,.92,cut=.10)
        m.slab("metal",.44,.678,.22,.57,.049,.62,underside=True)
        m.drum("trim",.44,.511,-.192,.066,.21,.078)
    else:
        m.slab("ivory",.44,.278,.079,.59,.206,1.033,.90,cut=.22)
        m.slab("glass",.44,.484,.105,.52,.15,.86,.75,cut=.20)
        m.slab("turquoise",.44,.634,.11,.39,.046,.67,.91,cut=.20)
    if m.lod<2:
        m.disc(.44,.391,-.418 if e==2 else -.423,.055,boss=False)
        m.block("banner",-.79,.176,-.45,.039,.41,.087)
        if m.lod==0:
            m.block("gold",-.79,.58,-.45,.12,.070,.12)
            for z in (-.05,.10,.26,.41):
                m.block("ivory",.69,.525,z,.023,.12,.021)


def plane(m):
    # A high-mounted swept wing and a smaller tailplane share the dorsal ridge of the fuselage.
    m.slab("ivory",.30,.147,-.10,.184,.131,1.03,.77,cut=.24)
    m.slab("glass",.30,.277,-.334,.135,.078,.268,.64)
    for side in (-1,1):
        stations=[(.054,-.227,.112,.259),(.396,.038,.237,.253),(.50,.128,.195,.297)]
        for j in range(2):
            p,q=stations[j:j+2]
            a=(.30+side*p[0],p[3],p[1])
            b=(.30+side*p[0],p[3],p[2])
            c=(.30+side*q[0],q[3],q[2])
            d=(.30+side*q[0],q[3],q[1])
            quad=[a,b,c,d]
            if side<0:
                quad.reverse()
            role="turquoise" if j==1 else "ivory"
            m.face(role,quad)
            m.face("metal",[(x,y-.018,z) for x,y,z in reversed(quad)])
            for a,b in zip(quad,quad[1:]+quad[:1]):
                if abs(a[0]-b[0])>.001 or abs(a[0]-.30)>.46:
                    m.face(role,(b,a,(a[0],a[1]-.018,a[2]),(b[0],b[1]-.018,b[2])))
        m.blade("turquoise",(.30,.25,.269),(.30+side*.243,.273,.403),.056)
    m.blade("banner",(.30,.26,.271),(.30,.428,.421),.044)
    for x,z in ((.30,-.40),(.248,.118),(.352,.118)):
        m.block("trim",x,.081,z,.043,.078,.068)


def airport(m):
    m.slab("slate",0,0,0,1.94,.081,1.89,.987,cut=.074)
    # Taxi edge dots, sun-facing terminal doors, an air-traffic lantern and a parked delta glider.
    m.slab("ivory",-.51,.081,.03,.59,.29,1.52,cut=.18)
    m.slab("glass",-.51,.371,.03,.56,.158,1.46,.94,cut=.18)
    m.slab("ivory",-.51,.529,.03,.68,.063,1.57,.91,cut=.16,underside=True)
    m.entry(-.51,.081,-.730,.24,.25)
    m.slab("wall",-.53,.59,.46,.25,.43,.24,.82)
    m.slab("trim",-.53,1.02,.46,.24,.099,.23,1.74,underside=True)
    m.slab("glass",-.53,1.119,.46,.415,.164,.39,.96)
    m.slab("ivory",-.53,1.28,.46,.49,.069,.46,.84,underside=True)
    if m.lod==0:
        for side in (-1,1):
            m.block("gold",-.53+side*.18,1.11,.292,.027,.171,.034)
        m.block("turquoise",-.51,.585,-.15,.35,.038,.63)
        for z in (-.28,-.04):
            m.block("metal",-.51,.623,z,.26,.037,.045)
    m.drum("trim",-.53,1.345,.46,.038,.134,n=6)
    m.disc(-.53,1.51,.46,.075)
    with m.transform(translate=(.052,0,0)):
        plane(m)
    if m.lod<2:
        for z in ((-.75,-.49,-.23,.03,.29,.55,.79) if m.lod==0 else (-.65,0,.65)):
            m.block("gold",.81,.082,z,.042,.012,.13)
        for z in ((-.43,-.15,.13,.41) if m.lod==0 else (-.32,.30)):
            m.block("trim",-.234,.375,z,.032,.144,.030)
    if m.lod==0:
        for x in (-.08,.12,.32):
            m.block("emissive",x,.081,-.819,.075,.025,.038)
        planter(m,-.819,.081,-.69,.073)


BUILDERS={"Airport": airport,"Bank":bank,"Guildhall":guild,"Harbor":harbor,
          "Market":market,"RailStation":station}


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        BUILDERS[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
