"""Elven makers, workshops and process halls."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.elf_food import Grove, TAU, publish_package
from generators.buildings.elf_extraction import wheel

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


def weaver(m):
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


def works(m,t):
    factory=t=="Factory"
    foundry=t=="Foundry"
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


def workshop(m):
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
