"""Elven makers, workshops and process halls."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_food import Grove, TAU, publish_package
from generators.buildings.elf_extraction import wheel
from generators.buildings.elf_trade_forms import arch, block, gable, shaft, textile

PACKAGE="elf_industry"


def kiln(m,x,z,r,height,large=False):
    """A hollow draw chimney. Flared fired-clay leaves form its cowl above the hearth."""
    n=(12,8,5)[m.lod]
    body="brick" if m.era==2 else "wall"
    m.disk("fieldstone",x,0,z,r*1.18,r*1.04,.085*height,.96,n)
    m.disk(body,x,.074*height,z,r,r*.91,.42*height,.87,n)
    m.disk(body,x,.491*height,z,r*.873,r*.797,.17*height,.54,n)
    m.disk("metal" if m.era>=2 else "trim",x,.654*height,z,r*.49,r*.446,.275*height,.76,n)
    # Outer and inner lip, the dark combustion throat is visibly recessed.
    for i in range(n):
        a,b=TAU*i/n,TAU*(i+1)/n
        def pt(angle,rad,y):
            return (x+r*rad*math.cos(angle),y*height,z+r*.91*rad*math.sin(angle))
        def tip(a):
            return 1.015+.045*math.cos(a*(3 if m.lod<2 else 2))
        low_i,low_j=pt(a,.35,.915),pt(b,.35,.915)
        outer_i,outer_j=pt(a,.46,tip(a)),pt(b,.46,tip(b))
        inner_i,inner_j=pt(a,.31,tip(a)-.005),pt(b,.31,tip(b)-.005)
        m.face("accent",[low_j,low_i,outer_i,outer_j])
        m.face("trim",[outer_j,outer_i,inner_i,inner_j])
        m.face("door",[inner_j,inner_i,pt(a,.32,.941),pt(b,.32,.941)])
        m.face("door",[pt(b,.32,.941),pt(a,.32,.941),(x,.941*height,z)])
    front=z-r*.90*math.cos(math.pi/n)
    m.disk(body,x,.037*height,front,r*.61,r*.28,.37*height,.85,n)
    m.portal(x,.044*height,front-r*.26,r*.79,.318*height,"forge")
    if m.lod<2:
        for s in (-1,1):
            m.limb("trim",[(x+s*.78*r,.078*height,z-.36*r),
                           (x+s*.71*r,.406*height,z-.49*r),(x+s*.42*r,.592*height,z-.36*r)],
                   [.12*r,.081*r,.051*r])
    if large and m.lod<2:
        for y,radius in ((.264,.975),(.482,.88)):
            m.disk("metal",x,height*y,z,r*radius,r*.91*radius,.029*height,.99,n)


def ingots(m,x,z,role):
    for i,(dx,dz,dy) in enumerate(((-.063,0,0),(.063,0,0),(0,.013,.050))):
        if m.lod==2 and i>0:
            continue
        m.box((x+dx,dy+.026,z+dz),(.115,.052,.149),m.color(role),material=role,taper=.18)


def smith(m):
    m.hall(-.34,0,.098,.31,.275,.40+.044*m.era,.306)
    kiln(m,.358,.117,.194,.66+.138*m.era)
    # The asymmetric beak, squared table and waist are distinct from a smelting pot.
    m.disk("timber",.167,0,-.249,.143,.122,.149,.94)
    m.disk("metal",.167,.148,-.249,.108,.093,.080,.63)
    m.box((.158,.251,-.249),(.25,.057,.121),m.color("metal"),material="metal")
    m.limb("metal",[(.267,.252,-.249),(.414,.263,-.245)],[.043,.009],5)
    if m.lod<2:
        m.limb("timber",[(.072,.276,-.249),(.223,.289,-.228)],[.012,.016],5)
        m.lump("trim",(.088,.282,-.249),(.035,.028,.029),5)
    if m.era>=2:
        wheel(m,.65,.218,.13,.143,"x")
        m.limb("metal",[(.508,.238,.13),(.669,.221,.13)],[.043,.034])
    if m.lod==0:
        ingots(m,.586,-.214,"metal")


def smelter(m):
    h=(.88,1.04,1.16,1.22)[m.era]
    kiln(m,-.17,.061,.317,h,True)
    # Pouring spout runs down into a broad casting basin.
    m.disk("fieldstone",.305,0,-.023,.196,.161,.107)
    m.disk("metal",.305,.104,-.023,.161,.137,.125,1.15)
    m.disk("forge",.305,.229,-.023,.152,.120,.01)
    m.limb("metal",[(.035,.433,.064),(.149,.338,.051),(.241,.285,-.013)],[.063,.045,.028])
    if m.lod<2:
        m.limb("forge",[(.24,.283,-.013),(.247,.236,-.012)],[.019,.015],5)
        ingots(m,.575,-.18,"metal")
    m.hall(-.62,0,.116,.205,.191,.272+.024*m.era,.183)
    if m.era>=2:
        m.disk("metal",.521,0,.191,.101,.109,.412,.92)
        m.limb("trim",[(.516,.389,.19),(.49,.564,.169),(.08,.58,.11)],[.038,.035,.040])
    if m.era==3:
        m.leaf("glass",(-.181,.68,.12),(-.41,.79,.164),.108,.061)


def loom(m,x,y,z,s):
    with m.transform(translate=(x,y,z),scale=s):
        for side in (-1,1):
            m.limb("trim",[(side*.22,0,.025),(side*.22,.041,.025),
                          (side*.21,.34,.038),(side*.176,.51,.059)],[.036,.031,.024,.021])
        for ht in (.113,.432):
            m.limb("timber",[(-.218,ht,.035),(.218,ht,.035)],[.023,.023])
        # Woven cloth rolls over the beam and hangs toward the viewer in a pointed border.
        bands=[(.425,.039,.164),(.304,.003,.161),(.207,-.021,.157),(.136,-.044,.153)]
        for i in range(3):
            top,bot=bands[i],bands[i+1]
            ht,dt,wt=top
            hb,db,wb=bot
            face=[(-wb,hb,db),(-wt,ht,dt),(wt,ht,dt),(wb,hb,db)]
            role=("banner","accent","banner")[i]
            m.face(role,face)
            m.face(role,[(u,v,w+.006) for u,v,w in reversed(face)])
        if m.lod<2:
            for i in range(5):
                u=(i-2)*.060
                m.face("accent",[(u-.022,.134,-.044),(u+.022,.134,-.044),(u,.085,-.047)])
            m.limb("gold",[(-.154,.251,-.019),(.078,.247,-.026)],[.011,.009],4)


def weaving_court(m):
    # The drying court is a low, three-sided terrace that leaves the loom exposed.
    m.disk("fieldstone",0,0,0,.471,.424,.049,.97, (16,10,8)[m.lod])
    with m.transform(translate=(-.191,.049,.037),rotate_y=-24,scale=.68):
        loom(m,0,0,0,1)
        block(m,"timber",(0,.062,.106),(.532,.075,.276))
    for x in (-.395,.03,.389):
        shaft(m,"timber",(x,.050,.208),(x*.96,.416,.231),.030,.022)
    shaft(m,"trim",(-.410,.404,.231),(.412,.407,.232),.025)
    for i,(x,w,dy) in enumerate(((-.205,.278,0),(.150,.271,.014))):
        textile(m,x,.405+dy,.209,w,.255+.041*i,"cloth" if i else "banner",-.015)
    m.pot(.257,.052,-.119,.105,"cloth")
    if m.lod<2:
        m.pot(.089,.05,-.222,.082,"thatch")
        shaft(m,"timber",(-.298,.054,-.28),(-.297,.239,-.277),.021)
        m.lump("ivory",(-.294,.159,-.277),(.052,.076,.048),6)


def power_weavery(m):
    # A folded monitor roof covers the rear half of the power-loom floor, leaving
    # the two tilted webs and the flywheel visible along the open factory side.
    block(m,"brick",(0,.042,.010),(.884,.084,.598),.035)
    for x in (-.324,.329):
        shaft(m,"metal",(x,.076,.289),(x,.556,.255),.038,.028)
        shaft(m,"trim",(x,.077,.002),(x,.478,.046),.032,.021)
    with m.transform(translate=(0,.510,.157),rotate_y=90):
        gable(m,0,0,0,.363,.936,.136)
    for x in (-.192,.153):
        with m.transform(translate=(x,.085,-.137),rotate_x=13,scale=(.65,.70,.68)):
            loom(m,0,0,0,1)
        if m.lod<2:
            shaft(m,"metal",(x-.126,.134,-.10),(x+.127,.134,-.10),.036)
    wheel(m,.377,.263,-.108,.147,"x")
    shaft(m,"metal",(-.304,.267,-.09),(.40,.265,-.09),.025)
    block(m,"metal",(.336,.162,-.094),(.072,.163,.09),.10)
    if not m.lod:
        for x in (-.22,.21):
            m.limb("metal",[(x,.256,-.095),(x,.132,-.026),(x,.124,.080)],
                   [.014,.014,.012],4)
    if m.lod<2:
        for i,x in enumerate((-.241,.118)):
            shaft(m,"trim",(x-.057,.147,-.23),(x+.061,.147,-.23),.028)
            shaft(m,"cloth" if i else "banner",(x-.04,.149,-.23),(x+.042,.149,-.23),.046)
    if not m.lod:
        for i,z in enumerate((.028,.125,.221)):
            m.disk("metal",-.388,.081,z,.040,.040,.051)
            m.disk("ivory" if i!=1 else "cloth",-.388,.130,z,.033,.033,.116,.77)


def aerial_weavery(m):
    # A folded length of fabric travels from the upper warp beam down to the
    # winding roll. The adjacent low finishing bay receives the completed bolt.
    for s in (-1,1):
        x=-.106+s*.235
        m.disk("wall",x,0,-.040,.097,.134,.096,.79)
        m.limb("trim",[(x,.081,-.041),(x+s*.035,.415,-.027),
                       (x+s*.020,.772,.042),(x-s*.096,.935,.091)],[.061,.043,.033,.029])
    shaft(m,"metal",(-.30,.867,.07),(.088,.867,.07),.046)
    for t in range((7,5,3)[m.lod]):
        x=-.255+.298*t/((7,5,3)[m.lod]-1)
        shaft(m,"ivory",(x,.862,.065),(x,.697,.028),.006)
    textile(m,-.105,.705,.031,.318,.394,"banner",-.102)
    shaft(m,"accent",(-.286,.311,-.058),(.075,.311,-.058),.041)
    m.limb("trim",[(.051,.31,-.059),(.131,.293,-.032),(.285,.232,.026)],
           [.041,.044,.050])
    m.disk("wall",.292,0,.119,.157,.232,.265,.93)
    m.crown(.292,.259,.119,.182,.259,.173,"glass",5)
    if m.lod<2:
        m.portal(.292,.014,-.101,.130,.19)
        m.leaf("roof",(-.202,.880,.078),(-.338,1.00,.203),.063,.05)


def weaver(m):
    if m.era:
        (weaving_court,power_weavery,aerial_weavery)[m.era-1](m)
        return
    m.hall(-.068,0,.163,.261,.214,.377+.028*m.era,.293+.021*m.era)
    loom(m,.039,0,-.253,.97)
    m.pot(-.249,0,-.206,.081,"cloth")
    if m.era==0:
        m.disk("timber",.306,0,.028,.055,.055,.04)
        m.limb("timber",[(.306,.031,.028),(.303,.248,.026)],[.014,.014],4)
        m.lump("ivory",(.303,.171,.026),(.042,.056,.043),6)
    else:
        wheel(m,.288,.176,-.016,.116+.011*m.era)
        m.limb("trim",[(.283,0,.041),(.283,.038,.041),(.288,.181,.017)],[.038,.032,.024])
    if m.era==3:
        m.leaf("glass",(-.104,.407,.12),(.207,.505,-.013),.103,.045)


def shield(m,x,y,z,w,h,role):
    # Convex leaf-shaped shield, silver central boss and inset coloured fields.
    outline=[(0,-.50),(-.40,-.14),(-.51,.22),(-.27,.43),(0,.51),(.27,.43),(.51,.22),(.40,-.14)]
    points=[(x+u*w,y+v*h,z) for u,v in outline]
    back=[(u,v,depth+.042*w) for u,v,depth in points]
    for i in range(len(points)):
        j=(i+1)%len(points)
        a,b=points[i],points[j]
        m.face("accent" if i in (0,4) else role,[a,b,(x,y+.05*h,z-.15*w)])
        m.face("trim",[a,back[i],back[j],b])
    m.face("hide",back[::-1])
    if m.lod<2:
        m.lump("gold",(x,y+.05*h,z-.15*w),(.085*w,.085*w,.052*w),5)


def armory(m):
    # Paired armour vaults share a low spine; a rack of leaf-shaped bucklers marks the entrance.
    m.hall(-.33,0,.088,.339,.287,.352+.063*m.era,.341)
    m.hall(.231,0,.135,.248,.212,.319+.033*m.era,.205)
    for s in (-1,1):
        m.limb("trim",[(.224+s*.242,0,-.218),(.224+s*.242,.040,-.218),
                       (.224+s*.24,.351,-.207)],[.034,.030,.022])
    m.limb("timber",[(-.045,.330,-.212),(.499,.330,-.211)],[.033,.031])
    for i,(x,dy) in enumerate(((.025,0),(.222,.026),(.419,0))):
        shield(m,x,.243+dy,-.237,.164,.237,"banner" if i!=1 else "metal")
    if m.era>=2:
        m.disk("metal",.56,0,.145,.121,.118,.443,.89)
        m.crown(.56,.44,.145,.13,.124,.13,"glass",4)


def production_line(m):
    # Feed and discharge conveyors run all the way through the open stamping press.
    # The heavy ram has a matching die below, and a jointed feeder lifts the billets.
    for x in (-1.07,-.33,.50,1.08):
        block(m,"wall",(x,.19,-.183),(.208,.38,.489),.17)
    block(m,"metal",(0,.397,-.183),(2.54,.108,.53),.018)
    for z in (-.473,.111):
        block(m,"trim",(0,.447,z),(2.57,.111,.068),.015)
    for j in range((11,8,3)[m.lod]):
        x=-1.144+2.288*j/((11,8,3)[m.lod]-1)
        shaft(m,"metal",(x,.461,-.439),(x,.461,.071),.053)
    # The arch of the press runs across the work, its center clear for the moving stock.
    for z in (-.595,.231):
        block(m,"fieldstone",(.172,.101,z),(.318,.202,.239),.13)
        m.limb("wall",[(.172,.178,z),(.172,.715,z),
                       (.172,1.088,-.183+(z+.183)*.77),(.172,1.204,-.183+(z+.183)*.45)],
               [.115,.081,.073,.091])
    block(m,"roof",(.172,1.204,-.183),(.358,.175,.63),.14)
    m.disk("metal",.172,.85,-.183,.104,.132,.318,1.09)
    block(m,"accent",(.172,.842,-.183),(.31,.092,.292),.06)
    block(m,"metal",(.172,.564,-.183),(.333,.10,.31),.12)
    for x in (-.98,.871):
        block(m,"accent" if x>0 else "metal",(x,.55,-.180),(.253,.083,.262),.14)
    if m.lod<2:
        m.disk("roof",.871,.58,-.183,.093,.111,.075,.62)
    # A rail-mounted pick arm reaches over the feeding rollers.
    m.disk("wall",-.817,0,.507,.229,.236,.217,.87)
    m.disk("metal",-.817,.204,.507,.142,.166,.144,1.04)
    m.limb("accent",[(-.817,.342,.507),(-.983,.771,.430),(-.736,.936,.218),
                     (-.603,.811,.006)],[.091,.076,.062,.034])
    if m.lod<2:
        for x,y,z,r in ((-.981,.768,.432,.094),(-.736,.936,.218,.069)):
            m.lump("metal",(x,y,z),(r,r,r),7)
    for s in (-1,1):
        shaft(m,"trim",(-.602,.808,.005),(-.604+s*.070,.709,-.035),.026,.019)
    block(m,"wall",(.662,.220,.595),(.606,.44,.473),.09)
    m.crown(.662,.436,.595,.432,.336,.268,"roof",5)
    if not m.lod:
        m.portal(.662,.011,.352,.261,.306)
    if m.lod<2:
        arch(m,.662,.012,.348,.337,.341,.052,"trim")
        for x in (-.953,.971):
            shaft(m,"accent",(x,.174,-.396),(x-.080,.395,-.459),.032)


def induction_foundry(m):
    # The crucible tilts between two bearing cheeks. The lip, hot metal surface and
    # pouring stream remain exposed below the charging bridge.
    cx,cz=-.326,-.151
    for z in (-.482,.177):
        block(m,"wall",(cx,.260,z),(.335,.52,.159),.20)
    shaft(m,"metal",(cx,.405,-.591),(cx,.405,.249),.098)
    with m.transform(translate=(cx,.301,cz),rotate_z=-20):
        rings=[]
        profile=[(.187,0),(.281,.116),(.355,.213),(.379,.249),(.389,.327),
                 (.404,.358),(.431,.445),(.428,.488),(.343,.470),(.316,.386)]
        n=(16,10,6)[m.lod]
        if m.lod==2:
            profile=[profile[j] for j in (0,2,4,6,7,8,9)]
        for r,y in profile:
            rings.append([(r*math.cos(i*TAU/n),y,r*math.sin(i*TAU/n)) for i in range(n)])
        for k,(lo,hi) in enumerate(zip(rings,rings[1:])):
            role="metal" if k<2 else "accent" if k%2==0 else "wall"
            for i in range(n):
                j=(i+1)%n
                m.face(role,[lo[j],lo[i],hi[i],hi[j]])
        m.face("metal",rings[0])
        m.face("forge",rings[-1][::-1])
    m.limb("forge",[(.185,.607,cz),(.251,.556,cz),(.313,.350,-.163),(.342,.156,-.165)],
           [.049,.044,.031,.027])
    for j in range(3):
        x=.347+.295*j
        block(m,"metal",(x,.065,-.157),(.248,.13,.301),.08)
        block(m,"forge" if j<2 else "accent",(x,.132,-.157),(.17,.022,.219),.08)
    # Charge hopper and its inclined feed bridge project above the rear lip.
    block(m,"fieldstone",(-.746,.139,.730),(.396,.278,.314),.16)
    m.disk("metal",-.746,.261,.730,.151,.137,.453,1.96)
    m.disk("slate",-.746,.707,.730,.276,.252,.081,.62)
    m.limb("trim",[(-.746,.628,.629),(-.718,.911,.402),(-.438,.913,.116),
                   (-.354,.775,.022)],[.095,.093,.083,.074])
    shaft(m,"metal",(-.643,.008,.425),(-.653,.883,.339),.054,.038)
    if m.lod<2:
        wheel(m,cx,.407,-.579,.149)
        m.lump("metal",(.96,.198,-.151),(.089,.064,.095),6)
    if not m.lod:
        ingots(m,.878,-.501,"metal")


def works(m,t):
    factory=t=="Factory"
    foundry=t=="Foundry"
    if m.era == 3 and (factory or foundry):
        (production_line if factory else induction_foundry)(m)
        return
    if foundry:
        kiln(m,-.33,.22,.484,1.54 if m.era==2 else 1.77,True)
        kiln(m,.341,.54,.292,1.19 if m.era==2 else 1.39)
        m.hall(-.67,0,-.426,.353,.297,.473,.332)
        # Elevated teeming ladle and a rail of moulds.
        for x in (.21,.83):
            m.limb("trim",[(x,0,-.42),(x,.80,-.42)],[.059,.046])
        m.limb("metal",[(.18,.788,-.42),(.86,.794,-.42)],[.057,.051])
        m.limb("metal",[(.48,.79,-.42),(.48,.711,-.42)],[.025,.020])
        for side in (-1,1):
            m.limb("trim",[(.48,.714,-.42),(.48+side*.17,.557,-.42)],
                   [.021,.022],4)
        m.disk("metal",.48,.37,-.42,.16,.154,.211,1.19)
        m.disk("forge",.48,.581,-.42,.168,.158,.013)
        if m.lod<2:
            ingots(m,.507,-.81,"metal")
        return

    if factory:
        # Three parallel, scalloped monitor lights run above the long assembly floor.
        m.disk("brick" if m.era==2 else "wall",0,0,.093,1.12,.816,.517,.94, (16,10,6)[m.lod])
        for i in range(3):
            x=(i-1)*.527
            m.disk("glass",x,.489,.103,.271,.559,.241+.034*i,.93)
            m.crown(x,.713+.034*i,.103,.31,.661,.282,"roof",4)
        m.disk("wall",.01,.004,-.63,.349,.243,.433,.92)
        m.portal(.01,.016,-.863,.407,.36)
        for x,h in ((-.66,1.36),(-.26,1.11)):
            m.disk("metal",x,.35,.52,.113,.11,h-.35,.78)
            m.disk("accent",x,h-.067,.52,.099,.098,.069)
        if m.lod<2:
            m.limb("metal",[(.46,.25,-.495),(.78,.295,-.669),(.907,.141,-.765)],[.049,.048,.043])
        if m.era==3:
            m.limb("trim",[(.52,.87,.17),(.54,1.12,.18),(.63,1.29,.19)],[.055,.038,.032])
            m.leaf("glass",(.63,1.27,.19),(.99,1.48,.32),.157,.081)
        return

    # A wafer-fabrication cleanroom under a continuous glazed lens and a bank of cooling fins.
    m.disk("wall",-.16,0,.13,.759,.761,.389,.98)
    # Glazing and mullions share the wall profile. A continuous ring replaces
    # each glazed stripe, so its cap can never cut through an underlying glass face.
    cuts=[(.383,"glass")]
    if m.lod<2:
        for i in range(3):
            low=.447+i*.151
            cuts.extend([(low,"accent"),(low+.035,"glass")])
    cuts.append((.903,None))
    for (low,mat),(high,_) in zip(cuts,cuts[1:]):
        extra=.017 if mat=="accent" else 0
        bottom=1-.20*(low-.383)/.52+extra
        top=1-.20*(high-.383)/.52+extra
        m.disk(mat,-.16,low,.13,.746*bottom,.744*bottom,high-low,top/bottom)
    m.crown(-.16,.895,.13,.633,.64,.414,"glass")
    m.hall(-.54,0,-.565,.301,.248,.373,.24)
    for j in range(3):
        z=-.45+.36*j
        m.disk("trim",.666,0,z,.203,.134,.55+.074*j,.82)
        m.leaf("accent",(.65,.536+.074*j,z),(.94,.77+.074*j,z+.09),.159,.13)
    if m.lod<2:
        m.limb("metal",[(.644,.35,.32),(.52,.414,.389),(.405,.462,.37)],[.048,.043,.041])


def machine_shed(m):
    # A single roof follows a line shaft over two open machine bays. The light
    # front edge leaves a clear loading route across the lathe and drilling table.
    block(m,"fieldstone",(0,.049,.052),(1.72,.098,1.082),.025)
    block(m,"brick",(-.62,.332,.246),(.279,.491,.599),.05)
    for x in (-.674,.0,.715):
        shaft(m,"timber",(x,.095,.476),(x,.893,.449),.056,.040)
    for x in (-.692,.719):
        m.limb("trim",[(x,.095,-.161),(x,.512,-.151),(x,.836,-.048)],
               [.050,.036,.036])
    # The brick drive enclosure and its flue break the roof on the engine end.
    # The remaining roof spans the open machines, stepping down to a loading hood.
    with m.transform(translate=(-.157,.854,.226),rotate_y=90):
        gable(m,0,0,0,.692,1.468,.271)
    with m.transform(translate=(.673,.789,.234),rotate_y=90):
        gable(m,0,0,0,.636,.464,.128)
    m.disk("brick",-.598,.565,.421,.088,.092,.601,.77)
    m.disk("metal",-.598,1.158,.421,.079,.081,.078,.92)
    m.disk("door",-.598,1.234,.421,.055,.057,.006)
    shaft(m,"metal",(-.725,.661,.139),(.716,.661,.139),.050)
    for x in (-.361,.334):
        shaft(m,"trim",(x,.664,.135),(x,.910,.196),.022)
    wheel(m,-.726,.592,.119,.196,"x")
    for x in (-.446,-.192):
        block(m,"wall",(x,.206,-.258),(.126,.222,.184),.1)
    block(m,"metal",(-.322,.348,-.263),(.484,.095,.234))
    shaft(m,"trim",(-.492,.432,-.265),(-.169,.432,-.265),.050)
    block(m,"metal",(-.517,.417,-.265),(.081,.174,.173),.09)
    block(m,"metal",(-.143,.417,-.265),(.072,.155,.137),.09)
    # Tall drill spindle over a separate work rest at the loading end.
    block(m,"wall",(.354,.188,-.251),(.275,.182,.238),.10)
    shaft(m,"metal",(.424,.255,-.192),(.424,.600,-.188),.039)
    block(m,"roof",(.35,.589,-.199),(.222,.076,.107),.08)
    shaft(m,"metal",(.281,.584,-.207),(.281,.405,-.207),.021,.011)
    block(m,"metal",(.32,.327,-.238),(.182,.048,.168))
    if m.lod<2:
        for x,h in ((-.404,.43),(.412,.572)):
            m.limb("hide",[(x,.671,.125),(x,h,-.189),(x,h,-.276)],
                   [.019,.018,.016],4)
        for x in (-.546,.035):
            shaft(m,"timber",(x,.100,.480),(x+.284,.831,.449),.027)
    if not m.lod:
        block(m,"timber",(.487,.116,-.552),(.318,.074,.113),.03)
    if m.lod<2:
        # A belt pulley and the lathe chuck stand proud of their drive shafts.
        for x in (-.451,.391):
            shaft(m,"accent",(x-.022,.661,.138),(x+.020,.661,.138),.094)
        shaft(m,"trim",(-.502,.437,-.265),(-.471,.437,-.265),.074)
        shaft(m,"hide",(-.464,.433,-.265),(-.240,.433,-.265),.060,.043)
        m.disk("metal",.609,.097,.282,.102,.118,.298,.91)
        m.limb("trim",[(.609,.391,.280),(.568,.498,.231),(.417,.416,-.102)],
               [.025,.022,.021],5)
        if not m.lod:
            wheel(m,.431,.381,-.192,.068,role="accent")


def workshop(m):
    if m.era == 2:
        machine_shed(m)
        return
    m.hall(-.25,0,.20,.408,.371,.534+.093*(m.era==2),.405)
    # The geared capstan and its pivoted arm lift finished work from the open bench.
    wheel(m,.343,.428,-.217,.253)
    m.limb("timber",[(.464,0,-.180),(.464,.071,-.180),(.343,.437,-.174)],[.072,.065,.046])
    m.limb("trim",[(.272,0,-.060),(.272,.079,-.060),(.252,.600,-.092),
                   (.108,.81,-.13),(-.10,.76,-.27)],[.077,.071,.047,.039,.021])
    m.limb("trim",[(-.10,.762,-.27),(-.10,.371,-.27)],[.010,.010],4)
    for x in (-.43,-.005):
        m.disk("trim",x,0,-.383,.041,.041,.217)
    m.disk("timber",-.218,.212,-.38,.304,.152,.055)
    if m.lod<2:
        m.lump("accent",(-.188,.295,-.379),(.074,.038,.052),5)
        m.pot(.58,0,.131,.087,"metal")
    if m.era==2:
        m.disk("metal",-.53,.28,.341,.084,.084,.688,.79)
        m.disk("trim",-.53,.961,.341,.076,.076,.054)


def build(spec,lod):
    m=Grove(spec,lod)
    t=spec.building_type
    if t in ("Foundry","Factory","ElectronicsPlant"):
        works(m,t)
    else:
        {"Armory":armory,"Blacksmith":smith,"Smelter":smelter,"Weaver":weaver,"Workshop":workshop}[t](m)
    return m


if __name__=="__main__":
    publish_package(PACKAGE,build)
