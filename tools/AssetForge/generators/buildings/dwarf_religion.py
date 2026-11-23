"""Ancestor stones, votive bells and rock-cut sanctuaries. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def bell(m,x,y,z,size):
    """y is the mouth of the bell. The crown loop meets the overhead beam."""
    with m.transform(translate=(x,y,z),scale=size):
        if m.lod==2:
            m.drum("accent",0,.032,0,.49,.54,top=.17,n=5)
            m.drum("trim",0,.562,0,.11,.19,n=4)
            return
        m.drum("trim",0,.036,0,.49,.071,top=.46)
        m.drum("accent",0,.105,0,.47,.18,top=.30)
        m.drum("gold",0,.282,0,.30,.28,top=.19)
        m.drum("trim",0,.56,0,.119,.19)
        m.drum("gold",0,-.087,0,.081,.144,top=.045)
        if m.lod==0:
            m.drum("gold",0,.116,0,.459,.033,top=.434)


def votive(m,x,y,z,scale):
    with m.transform(translate=(x,y,z),scale=scale):
        if m.lod==2:
            m.drum("accent",0,0,0,.105,.18,top=.151,n=4)
            m.cone((0,.175,0),.117,.19,m.color("forge"),segments=4,material="forge")
            return
        if m.lod==1:
            m.drum("trim",0,0,0,.132,.112,top=.10,n=5)
            m.drum("accent",0,.109,0,.103,.080,top=.152,n=5)
            m.cone((0,.185,0),.113,.191,m.color("forge"),segments=5,material="forge")
            return
        n=7 if m.lod==0 else 5
        m.drum("trim",0,0,0,.137,.107,top=.10,n=n)
        m.drum("accent",0,.101,0,.109,.074,top=.167,n=n)
        m.drum("forge",0,.172,0,.121,.073,top=.083,n=n)
        m.drum("emissive",0,.242,0,.073,.14,top=.008,n=n)


def stairs(m,w,front,depth,top):
    n=(3,2,1)[m.lod]
    for i in range(n):
        length=depth*(1-i/n)
        m.block("fieldstone" if i%2==0 else "wall",0,i*top/n,
                front+depth-length/2,w-i*.031,top/n+.002,length)


def oathstone(m):
    m.slab("fieldstone",0,0,.06,1.33,.17,1.05,.21,.07)
    m.slab("wall",0,.165,.10,.92,.16,.75,.16)
    m.vault("fieldstone",0,.315,.12,.745,1.06,.58)
    # The clan re-caps its founding stone in the roofing material of the age.
    if m.lod<2:
        m.slab("accent",0,1.087,.12,.677,.066,.643,.12)
    m.vault("roof",0,1.149,.12,.723,.298,.669)
    # A braids-and-brows ancestral mask carved into the front of the standing stone.
    m.relief("wall",[(-.26,.65),(0,.455),(.26,.65),(.25,1.02),(-.25,1.02)],-.195,.087)
    for s in (-1,1):
        m.beam("trim",(s*.026,.993,-.225),(s*.248,1.064,-.226),.077)
        m.block("emissive",s*.135,.923,-.200,.105,.044,.021)
        if m.lod<2:
            m.beam("accent",(s*.146,.727,-.209),(s*.049,.57,-.206),.051)
    m.relief("accent",[(-.061,.751),(.075,.751),(.044,.956),(-.044,.956)],-.263,.080)
    if m.lod<2:
        for x in (-.51,.51): votive(m,x,.166,-.17,.60)
    m.slab("roof",0,.122,-.52,.58,.076,.31,.13)
    if m.lod==0:
        for s in (-1,1): m.rune(s*.32,.27,-.283,.095)


def shrine(m):
    e=m.era
    if e:
        return (ancestor_apse,founders_chime,crystal_sanctum)[e-1](m)
    m.slab("fieldstone",0,0,.02,1.66,.152,1.46,.17,.045)
    m.slab("wall",0,.148,.08,1.36,.18,1.10,.13)
    stairs(m,.66,-.91,.48,.251)
    # Two corbelled uprights bridge a sheltered ancestor bell. The opening stays clear.
    h=(.85,1.01,.94,1.08)[e]
    for s in (-1,1):
        m.slab("wall",s*.42,.319,.16,.284,h-.313,.45,.13,.12)
        m.slab("accent",s*.42,h-.169,.16,.284,.075,.458,.13)
        m.beam("trim",(s*.418,h-.17,.16),(s*.267,h+.055,.16),.131,.203)
    m.slab("trim",0,h+.015,.16,1.13,.125,.58,.13)
    m.vault("roof",0,h+.136,.16,1.22,(.172,.222,.145,.177)[e],.66)
    s=.53
    bell(m,0,h+.035-.75*s,.145,s)
    m.slab("slate",0,.324,-.15,.46,.072,.33,.17)
    for x in (-.53,.53): votive(m,x,.149,-.519,.53)
    if m.lod<2:
        m.rune(0,h+.187,-.18,.141)
        for x in (-.42,.42):
            m.block("banner",x,.46,-.065,.133,.222,.047)
    if e==3:
        m.slab("ascension",0,h+.309,.16,.54,.063,.35,.13)
    if m.lod==0:
        m.slab("accent",-.13,.393,-.14,.12,.046,.127)
        m.slab("ivory",.094,.393,-.13,.103,.052,.095)


def ancestor_apse(m):
    m.slab("fieldstone",0,0,.019,1.73,.124,1.63,.22)
    m.slab("wall",0,.118,.188,1.39,.198,1.18,.21)
    stairs(m,.81,-.867,.483,.304)
    # An ashlar apse encloses the bust of the clan founder. Short columns mark the approach.
    m.drum("wall",0,.310,.341,.479,.462,.446,n=(10,8,5)[m.lod])
    m.cylinder((0,.769,.341),.477,.162,m.color("roof_tile"),segments=(10,8,5)[m.lod],
               top_radius=.35,material="roof_tile")
    m.slab("trim",0,.311,-.110,.459,.117,.33,.13)
    m.slab("slate",0,.422,-.109,.37,.184,.269,.105,.17)
    m.slab("gold",0,.598,-.112,.336,.225,.237,.19,.10)
    m.slab("wall",0,.814,-.099,.263,.104,.224,.14,.08)
    for x in (-.503,.503):
        if m.lod==2:
            m.drum("plaster",x,.313,-.13,.095,.534,.079,n=5)
            continue
        sides=7 if m.lod==0 else 5
        m.drum("fieldstone",x,.313,-.13,.117,.108,.095,n=sides)
        m.drum("plaster",x,.415,-.13,.074,.37,.070,n=sides)
        m.drum("accent",x,.780,-.13,.111,.067,n=sides)
        if m.lod==0:
            votive(m,x,.842,-.13,.403)
        else:
            m.cone((x,.842,-.13),.064,.14,m.color("forge"),segments=5,material="forge")
    if m.lod==0:
        m.rune(0,.512,-.248,.127)
        for s in (-1,1):
            m.slab("accent",s*.087,.731,-.239,.064,.031,.036,.10)


def founders_chime(m):
    m.slab("fieldstone",0,0,-.015,1.82,.127,1.56,.16)
    # An exposed cantilever carries a great bronze bell beside a narrow counterweight tower.
    m.slab("brick",-.466,.121,.23,.526,.84,.67,.10,.079)
    m.slab("metal",-.466,.955,.23,.486,.235,.615,.14)
    m.vault("slate",-.466,1.185,.23,.584,.175,.701)
    m.beam("metal",(-.49,1.083,.13),(.452,1.16,-.007),.123)
    m.beam("trim",(-.34,.682,.121),(.341,1.134,.007),.075)
    m.block("metal",.332,1.011,.014,.073,.126,.084)
    bell(m,.332,.602,.014,.569)
    m.slab("wall",.319,.123,.01,.595,.121,.527,.17)
    m.entry(-.465,.131,-.110,.31,.406)
    if m.lod<2:
        m.block("door",-.469,.564,-.104,.144,.435,.032)
        m.block("metal",-.468,.661,-.124,.095,.15,.031)
        votive(m,.266,.13,-.524,.58)
    if m.lod==0:
        m.rune(-.467,1.057,-.085,.156)
        m.slab("slate",.595,.127,-.349,.25,.087,.218,.13)


def crystal_sanctum(m):
    m.slab("fieldstone",0,0,.015,1.71,.131,1.62,.24)
    m.drum("trim",0,.125,.10,.447,.166,.386,n=(10,8,5)[m.lod])
    # Twin torn stone wings lean away from the long faceted reliquary, exposing its entire profile.
    for s in (-1,1):
        with m.transform(translate=(s*.524,.126,.174),rotate_z=-s*16):
            m.slab("wall",0,0,0,.31,.65,.608,.20,.25)
            m.vault("ivory",0,.641,.012,.239,.258,.455)
    m.drum("gold",0,.286,.096,.20,.168,.157,n=6)
    m.drum("turquoise",0,.447,.096,.141,.514,.263,n=6)
    m.cone((0,.958,.096),.263,.582,m.color("ascension"),segments=6,material="ascension")
    m.slab("slate",0,.128,-.436,.482,.134,.263,.15)
    if m.lod<2:
        for x in (-.34,.34):
            votive(m,x,.131,-.516,.50)
    if m.lod==0:
        m.slab("ivory",0,.257,-.439,.223,.048,.145,.12)
        for s in (-1,1):
            m.slab("accent",s*.70,.131,.40,.231,.105,.232,.12)


def prayer_door(m,x,y,z,w,h):
    m.entry(x,y,z,w,h,"fieldstone")
    if m.lod<2:
        m.rune(x,y+h*.55,z-.084,w*.25,"gold")


def temple(m):
    e=m.era
    m.slab("fieldstone",0,0,.04,1.84,.166,1.58,.14,.035)
    m.slab("wall",0,.16,.18,1.52,.46,1.17,.14)
    m.vault("roof",0,.614,.19,1.69,(.251,.311,.232,.258)[e],1.33)
    m.vault("wall",0,.159,-.413,.97,.64,.36)
    prayer_door(m,0,.164,-.60,.605,.556)
    stairs(m,.79,-.928,.343,.158)
    # The embedded fork supports the central bell clear of the roof's crest.
    roof_top=.614+(.251,.311,.232,.258)[e]
    for s in (-1,1):
        m.beam("accent",(s*.323,roof_top-.048,.16),(s*.212,roof_top+.38,.16),.101)
    m.block("trim",0,roof_top+.324,.16,.575,.098,.187)
    bell(m,0,roof_top+.354-.75*.394,.16,.394)
    for s in (-1,1):
        m.slab("wall",s*.612,.158,-.626,.242,.331,.207,.14,.14)
        votive(m,s*.612,.487,-.626,.594)
    if m.lod<2:
        for s in (-1,1):
            m.block("trim",s*.758,.31,.22,.056,.211,.30)
            m.block("emissive",s*.789,.355,.22,.019,.105,.212)
        m.rune(0,.687,-.601,.165)
    if e>=2:
        m.slab("glass",0,roof_top-.015,.56,.62,.143,.34,.12)
    if m.lod==0:
        for s in (-1,1):
            for z in (-.14,.49):
                m.slab("fieldstone",s*.753,.128,z,.156,.48,.166,.11,.20)


def cathedral(m):
    e=m.era
    if e == 2:
        return crossing_cathedral(m)
    if e == 3:
        return lantern_cathedral(m)
    m.slab("fieldstone",0,0,.03,1.82,.135,1.76,.16)
    m.vault("wall",0,.132,.15,1.19,.91,1.32)
    m.vault("roof",0,.744,.20,1.13,.36,1.35)
    prayer_door(m,0,.134,-.515,.57,.55)
    stairs(m,.69,-.91,.398,.13)
    # Twin bell piers straddle the nave; the dark air around the bells reads at 64 px.
    for s in (-1,1):
        x=s*.623
        m.slab("wall",x,.128,-.322,.392,.66,.56,.12,.06)
        m.slab("trim",x,.784,-.322,.428,.072,.576,.10)
        for side in (-1,1):
            m.block("fieldstone",x+side*.142,.851,-.322,.082,.308,.325)
        bell(m,x,.945,-.433,.232)
        m.block("trim",x,1.113,-.433,.039,.065,.043)
        m.vault("roof",x,1.154,-.322,.48,.202,.61)
        if m.lod<2:
            m.block("banner",x,.393,-.603,.170,.255,.046)
            if m.lod==0:
                m.rune(x,.522,-.628,.115)
    with m.transform(translate=(0,.804,-.499),rotate_x=-90):
        m.drum("gold",0,0,0,.165,.050)
        m.drum("glass",0,.047,0,.134,.021)
    if m.lod<2:
        m.beam("trim",(-.103,.741,-.57),(.103,.865,-.57),.029)
        m.beam("trim",(.103,.741,-.559),(-.103,.865,-.559),.029)
    for s in (-1,1):
        for z in ((.09,.54) if m.lod<2 else (.41,)):
            m.beam("fieldstone",(s*.794,.123,z),(s*.40,.785,z),.132,.128)
    if m.lod==0:
        for x in (-.69,.69): votive(m,x,.133,-.729,.416)


def monastery(m):
    e=m.era
    if e == 2:
        return terrace_abbey(m)
    if e == 3:
        return quiet_court(m)
    m.slab("fieldstone",0,0,0,1.85,.112,1.75,.10)
    # The cloister wraps an actual herb court, with an open processional path from the gate.
    m.hearth(-.23,.109,.446,(1.18,1.04,.71))
    for s in (-1,1):
        x=s*.596
        m.block("wall",x,.108,.033,.345,.333,.994)
        m.vault("roof",x,.435,.033,.424,.173,1.09)
        if m.lod<2:
            for z in (-.276,.10,.43):
                m.block("trim",x-s*.172,.157,z,.029,.161,.129)
                m.block("emissive",x-s*.191,.184,z,.017,.094,.084)
    for x in (-.258,.258):
        m.slab("grass",x,.111,-.159,.211,.041,.364,.18)
    m.drum("fieldstone",.249,.15,-.12,.113,.098)
    m.drum("water",.249,.245,-.12,.086,.012)
    for x in (-.54,.54):
        m.slab("wall",x,.11,-.654,.65,.175,.159,.12)
    m.slab("wall",.56,.427,.48,.41,.31,.44,.12)
    for s in (-1,1):
        m.block("trim",.56+s*.135,.731,.48,.075,.236,.25)
    bell(m,.56,.816,.48,.214)
    m.vault("roof",.56,.962,.48,.421,.146,.417)
    if m.lod==0:
        for s in (-1,1):
            votive(m,s*.329,.113,-.466,.39)
            for z in (-.093,-.213):
                m.dome((s*.209,.15,z),.063,m.color("leaf"),rings=2,segments=6,squash=.85,material="leaf")


def crossing_cathedral(m):
    m.slab("fieldstone",0,0,.038,1.80,.142,1.69,.18)
    # A cruciform brick nave carries a lantern and a ribbed copper crossing dome.
    m.vault("brick",0,.135,.035,.90,.74,1.44)
    m.vault("slate",0,.563,.036,.97,.349,1.49)
    m.slab("wall",0,.138,.31,1.66,.428,.58,.12)
    m.vault("roof",0,.561,.31,1.74,.192,.63)
    m.drum("fieldstone",0,.727,.245,.456,.266,.409,n=(12,8,6)[m.lod])
    m.dome((0,.987,.245),.437,m.color("accent"),rings=(5,3,2)[m.lod],
           segments=(16,10,6)[m.lod],squash=.94,material="accent")
    m.drum("trim",0,1.379,.245,.106,.175,.092)
    m.pyramid((0,1.547,.245),.255,.245,.163,m.color("roof"),material="roof")
    prayer_door(m,0,.14,-.70,.51,.484)
    if m.lod<2:
        with m.transform(translate=(0,.640,-.719),rotate_x=-90):
            m.drum("gold",0,0,0,.129,.023)
            m.drum("glass",0,.018,0,.103,.016)
        for s in (-1,1):
            for z in (-.49,-.14):
                m.beam("fieldstone",(s*.62,.139,z),(s*.36,.596,z),.094)
    if m.lod==0:
        for s in (-1,1):
            m.rune(s*.606,.404,.009,.132)
            votive(m,s*.603,.144,-.603,.43)


def lantern_cathedral(m):
    m.slab("fieldstone",0,0,.09,1.75,.16,1.59,.22)
    m.slab("wall",0,.154,.19,1.38,.351,1.18,.20,.10)
    m.slab("ivory",0,.496,.20,1.43,.116,1.254,.21)
    # Four converging buttresses enclose a blade of glass over the sanctuary.
    for s in (-1,1):
        for t in (-1,1):
            # The inner faces open towards the altar; the outer arrises climb in two flights.
            x,z=s*.68,.20+t*.40
            if m.lod==2:
                m.beam("ivory",(x,.268,z),(s*.18,1.583,.20+t*.102),.181)
                continue
            m.beam("wall",(x,.268,z),(s*.441,.958,.20+t*.295),.195,.180)
            m.beam("ivory",(s*.441,.934,.20+t*.295),(s*.18,1.583,.20+t*.102),.146)
    m.drum("gold",0,.605,.20,.288,.16,.237,n=8)
    m.drum("turquoise",0,.756,.20,.18,.541,.218,n=6)
    m.cone((0,1.293,.20),.219,.533,m.color("ascension"),segments=6,material="ascension")
    m.vault("metal",0,.15,-.434,.813,.503,.522)
    prayer_door(m,0,.16,-.703,.48,.38)
    stairs(m,.59,-.909,.243,.157)
    if m.lod<2:
        for s in (-1,1):
            m.relief("glass",[(s*.58-.055,.312),(s*.58+.055,.312),
                              (s*.58+.055,.441),(s*.58,.477),(s*.58-.055,.441)],-.411,.029)
    if m.lod==0:
        for s in (-1,1):
            votive(m,s*.447,.159,-.557,.48)
            m.slab("accent",s*.62,.387,.585,.25,.051,.28,.16)


def terrace_abbey(m):
    m.slab("fieldstone",0,0,.02,1.88,.13,1.68,.12)
    # A gabled refectory and bellcote overlook terraced herb walks.
    m.slab("brick",-.375,.125,.165,.87,.587,1.10,.085)
    m.vault("slate",-.375,.705,.165,.975,.227,1.19)
    m.slab("wall",-.375,.889,.38,.456,.239,.421,.09)
    for x in (-.52,-.23):
        m.block("trim",x,1.125,.38,.076,.244,.238)
    bell(m,-.375,1.169,.315,.24)
    m.hip_roof((-.375,1.354,.38),.49,.47,.206,m.color("roof"),
               overhang=.015,material="roof")
    prayer_door(m,-.375,.132,-.397,.394,.473)
    for i in range(2):
        x=.27+i*.357
        m.slab("fieldstone",x,.129,-.013,.32,.163+i*.091,1.13,.09)
        m.slab("grass",x,.286+i*.091,-.025,.249,.037,1.00,.11)
        if m.lod<2:
            for z in ((-.31,.0,.31) if m.lod==0 else (-.24,.26)):
                m.dome((x,.319+i*.091,z),.103,m.color("leaf"),rings=(3,2)[m.lod],
                       segments=(7,5)[m.lod],squash=.79,material="leaf")
    if m.lod==0:
        for z in (-.18,.118,.43):
            m.block("glass",.058,.313,z,.022,.193,.126)
        votive(m,-.704,.132,-.502,.4)


def quiet_court(m):
    m.slab("fieldstone",0,0,0,1.82,.14,1.76,.23)
    # Hermit cells step up around an open octagonal water garden.
    cells=((-.57,-.10,.37,.54,.62),(-.38,.46,.48,.69,.48),
           (.21,.54,.52,.83,.42),(.56,.017,.46,.49,.42))
    for x,z,w,h,d in cells:
        m.slab("wall",x,.136,z,w,h,d,.20,.085)
        m.slab("glass",x,.130+h,z,w*.96,.093,d*.97,.19)
        m.slab("ivory",x,.217+h,z,w*1.12,.10,d*1.13,.21,.09)
        if m.lod==0:
            m.entry(x,.142,z-d*.50,w*.54,min(.304,h*.7))
    m.drum("slate",-.02,.138,-.155,.325,.084,.298,n=(12,8,6)[m.lod])
    m.drum("water",-.02,.217,-.155,.248,.013,n=(12,8,6)[m.lod])
    # A suspended prayer bell marks the walk from the outer gate into the garden.
    for x in (-.27,.27):
        m.beam("ivory",(x,.14,-.669),(x*.64,.720,-.66),.103)
    m.vault("metal",0,.699,-.660,.52,.12,.24)
    bell(m,0,.485,-.669,.30)
    if m.lod<2:
        m.drum("accent",-.02,.227,-.155,.098,.064,.066)
    if m.lod==0:
        m.rune(.214,.715,.294,.178)


def build(spec,lod):
    m=Stonework(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        {"SpiritTotem":oathstone,"Shrine":shrine,"Temple":temple,"Cathedral":cathedral,
         "Monastery":monastery}[spec.building_type](m)
    return m


if __name__=="__main__":
    publish_package("dwarf_religion",build)
