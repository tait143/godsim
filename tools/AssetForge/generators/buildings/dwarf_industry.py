"""Clan craft halls and heavier industry. Builder session 5."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def kiln(m,x,z,r,h):
    """Battered smelting pot with a clinker arch, glowing throat and metal tap."""
    e=m.era
    m.drum("fieldstone",x,.105,z,r*1.05,.136)
    m.drum("brick" if e<3 else "wall",x,.234,z,r,h*.52,r*.82)
    m.drum("roof",x,.23+h*.52,z,r*.84,h*.27,r*.50)
    m.stack(x,.222+h*.79,z,r*.50,h*.30,"brick" if e<3 else "metal")
    with m.transform(translate=(x,.109,z-r*.84),scale=r):
        m.vault("trim",0,0,0,1.28,.76,.50)
        m.relief("forge",[(-.43,.05),(.43,.05),(.40,.37),(.21,.55),(-.21,.55),(-.40,.37)],-.261,.035)
        if m.lod<2:
            for t in (-.22,.16): m.block("door",t,.061,-.281,.061,.33,.051)
    if m.lod==0:
        m.drum("accent",x,.28+h*.23,z,r*.96,.038)
        m.drum("trim",x,.20+h*.57,z,r*.85,.045)


def ingots(m,x,y,z):
    for i in range(3 if m.lod==0 else 1):
        m.block("metal",x+(i%2)*.15,y+(i//2)*.072,z+i*.012,.141,.075,.252,taper=.26)


def furnace(m):
    if m.era:
        return (pot_furnaces, blast_furnace, rotary_smelter)[m.era-1](m)
    m.slab("fieldstone",0,0,.01,1.86,.119,1.66,.12)
    m.hearth(-.41,.114,.21,(.91,1.14,1.05),roof_material="roof" if m.era==0 else None)
    kiln(m,.41,.17,.377,(.83,.93,1.04,.91)[m.era])
    m.block("trim",.41,.114,-.44,.24,.10,.49)
    m.block("forge",.41,.210,-.438,.137,.018,.45)
    if m.lod<2:
        ingots(m,-.59,.12,-.52)
        m.slab("wall",.02,.114,-.56,.29,.17,.34,.10)
    if m.era>=2:
        m.beam("metal",(-.43,.59,.26),(.39,.70,.28),.117)
    if m.lod==0: m.lamp(-.78,.116,.05,.113)


def anvil(m,x,y,z,scale):
    with m.transform(translate=(x,y,z),scale=scale):
        m.drum("timber",0,0,0,.28,.25,top=.254)
        # Two convex forgings form the horn, crown, narrow waist and flared feet.
        m.relief("metal",[(-.30,.246),(.29,.246),(.17,.365),(-.15,.365)],-.138,.276)
        m.relief("metal",[(-.147,.36),(.169,.36),(.288,.506),(-.29,.506)],-.126,.252)
        m.relief("trim",[(-.32,.50),(.27,.50),(.30,.615),(-.32,.615)],-.145,.29)
        m.relief("metal",[(-.324,.50),(-.316,.612),(-.56,.59)],-.09,.18)
        if m.lod==0:
            m.drum("trim",0,.073,0,.28,.045)


def blacksmith(m):
    e=m.era
    if e:
        return (guild_smithy, powered_smithy, induction_smithy)[e-1](m)
    m.slab("fieldstone",0,0,.03,1.88,.112,1.63,.12)
    m.hearth(-.35,.109,.33,(1.05,1.13,.95),roof_material="roof" if e==0 else None)
    kiln(m,.48,.43,.268,(.69,.79,.87,.77)[e])
    anvil(m,.28,.111,-.38,.92)
    if m.lod<2:
        m.drum("timber",-.64,.112,-.46,.162,.202)
        m.drum("water",-.64,.312,-.46,.132,.011)
    if m.lod==0:
        m.beam("timber",(.29,.697,-.39),(.44,.693,-.54),.040)
        m.block("metal",.28,.673,-.38,.121,.064,.068)


def guild_smithy(m):
    # A ribbed smoke hood straddles an open-sided guild hearth. The two front
    # columns frame the smith's anvil and form a walk-in porch under the hood.
    m.slab("fieldstone",0,0,.055,1.61,.16,1.40,.20)
    m.slab("wall",0,.151,.366,1.16,.49,.56,.11)
    m.entry(.31,.159,.081,.35,.414)
    for x in (-.548,.548):
        m.drum("wall",x,.151,-.325,.149,.54,.116,n=(8,6,4)[m.lod])
        if m.lod<2:
            m.drum("trim",x,.622,-.325,.154,.087,.156,n=(8,6)[m.lod])
    m.slab("trim",0,.676,.023,1.43,.116,1.043,.13)
    m.slab("roof_tile",0,.786,.023,1.45,.438,1.062,.15,.58)
    m.slab("brick",0,1.218,.023,.61,.302,.47,.11,.16)
    m.vault("trim",0,1.506,.023,.601,.096,.485)
    anvil(m,-.053,.157,-.469,.75)
    if m.lod<2:
        m.relief("forge",[(-.48,.269),(-.11,.269),(-.11,.429),(-.18,.508),(-.397,.508),(-.48,.415)],.069,.033)
        for x in (-.43,.43):
            m.beam("accent",(x,.792,-.499),(x*.42,1.222,-.197),.052)
    if m.lod==0:
        m.rune(0,.73,-.507,.211)
        m.drum("timber",-.65,.157,.075,.119,.205)
        m.drum("water",-.65,.36,.075,.099,.013)
        m.beam("timber",(.194,.164,-.494),(.280,.468,-.431),.047)
        m.block("metal",.281,.453,-.426,.13,.075,.079)


def powered_smithy(m):
    # Brick belt house with an external flywheel and walking-beam trip hammer.
    m.slab("brick",-.36,0,.23,.84,.63,1.18,.09)
    m.wedge((-.36,.624,.23),.96,1.29,.287,m.color("slate"),material="slate")
    m.entry(-.388,.016,-.364,.406,.476)
    m.stack(-.49,.799,.568,.146,.446,"brick")
    m.slab("fieldstone",.407,0,-.05,.81,.144,1.39,.13)
    m.slab("metal",.377,.137,.186,.32,.71,.333,.10,.17)
    m.beam("timber",(.375,.712,.588),(.375,.962,-.477),.134,.156)
    m.block("metal",.375,.635,-.493,.28,.355,.275)
    m.slab("trim",.373,.141,-.496,.403,.248,.349,.133,.20)
    m.slab("metal",.373,.382,-.496,.321,.122,.287,.11)
    with m.transform(translate=(.38,.525,.46),rotate_y=90):
        m.wheel(0,0,0,.326,"accent",spokes=(6,4,3)[m.lod])
    if m.lod<2:
        m.beam("metal",(.465,.513,.692),(.442,.726,.557),.061)
        m.block("forge",.372,.496,-.505,.204,.047,.129)
        for z in ((-.062,.246,.525) if m.lod==0 else (.183,)):
            m.block("glass",-.785,.328,z,.028,.179,.174)
    if m.lod==0:
        m.lamp(-.645,.025,-.371,.10)
        ingots(m,.721,.147,-.421)
        m.block("accent",.375,.948,-.493,.30,.074,.284)


def induction_smithy(m):
    # An open forging throat with a suspended die and concentric heating coils.
    # The insulated control room nestles into the outer cheek of the press.
    m.drum("metal",-.211,0,-.041,.671,.156,.696,n=(12,8,6)[m.lod])
    m.slab("ivory",-.411,.147,.282,.552,1.10,.557,.15,.20)
    m.vault("accent",-.411,1.239,-.002,.638,.241,1.107)
    m.drum("trim",-.410,.94,-.348,.221,.338,.259)
    m.drum("ivory",-.410,.748,-.348,.168,.203,.201)
    m.drum("metal",-.41,.151,-.347,.306,.242,.24)
    m.drum("forge",-.41,.385,-.347,.211,.112,.184)
    m.slab("wall",.471,0,.221,.693,.352,.926,.145)
    m.slab("glass",.471,.344,.221,.683,.238,.913,.138,.12)
    m.vault("ivory",.47,.577,.221,.72,.126,.977)
    m.entry(.464,.015,-.248,.387,.384)
    if m.lod<2:
        for i in range(4 if m.lod==0 else 2):
            m.drum("gold",-.41,.984+.067*i,-.348,.235+.006*i,.032,n=(12,7)[m.lod])
        m.rune(-.409,1.344,-.566,.241)
    if m.lod==0:
        for x in (.261,.677):
            m.block("metal",x,.354,-.238,.043,.207,.039)
        m.drum("trim",-.167,.153,-.565,.084,.226,.067)
        m.drum("turquoise",-.167,.375,-.565,.073,.044)


def pot_furnaces(m):
    # Two bottle kilns share a stone tapping gallery. The molten metal is
    # carried from the forward mouth to moulds along the channel at ground level.
    m.slab("fieldstone",-.026,0,.017,1.69,.116,1.46,.23)
    for x,z,r,h in ((-.383,-.028,.342,.998),(.309,.355,.303,1.261)):
        m.drum("brick",x,.109,z,r,.361,r*1.11)
        m.drum("roof_tile",x,.46,z,r*1.11,h*.507,r*.46)
        m.drum("wall",x,.453+h*.507,z,r*.47,h*.30,r*.49)
        top=.446+h*.807
        m.drum("trim",x,top,z,r*.57,.087,r*.56)
        if m.lod<2:
            m.drum("door",x,top+.083,z,r*.379,.012)
        m.vault("wall",x,.112,z-r*.92,r*1.49,.367,.181)
        m.vault("forge",x,.128,z-r*.92-.09,r*.91,.222,.038)
        if m.lod==0:
            m.drum("accent",x,.405,z,r*1.119,.051)
    if m.lod<2:
        m.block("trim",-.378,.115,-.542,.223,.100,.409)
        m.block("forge",-.378,.21,-.54,.122,.018,.409)
    else:
        m.block("forge",-.378,.115,-.542,.167,.104,.409)
    m.slab("wall",.433,.107,-.209,.58,.411,.486,.11)
    m.vault("slate",.433,.51,-.209,.637,.118,.548)
    m.entry(.436,.119,-.455,.318,.32)
    if m.lod<2:
        ingots(m,.12,.118,-.524)
    if m.lod==0:
        m.lamp(.674,.118,-.462,.08)


def blast_furnace(m):
    # The charge climbs a braced skip incline to the steel top of the furnace.
    x,z=-.39,.36
    m.slab("brick",x,0,z,.875,.417,.777,.126)
    m.vault("forge",x,.104,-.042,.419,.195,.052)
    m.drum("brick",x,.409,z,.402,.322,.459)
    m.drum("metal",x,.725,z,.453,.768,.259)
    m.drum("trim",x,1.486,z,.291,.171,.235)
    if m.lod==2:
        m.drum("brick",x,1.652,z,.16,.278,.115)
    else:
        m.stack(x,1.652,z,.16,.249,"brick")
    m.slab("wall",.448,0,-.237,.753,.339,1.026,.12)
    m.vault("roof",.448,.329,-.237,.787,.125,1.075)
    m.entry(.458,.014,-.755,.333,.301)
    for dx in (-.112,.112):
        m.beam("metal",(.49+dx,.41,-.335),(x+dx,1.568,z),.072)
    m.beam("trim",(.489,.433,-.335),(x,1.544,z),.175,.093)
    m.slab("brick",.019,0,.077,.239,.637,.254,.10)
    m.beam("metal",(.019,.628,.077),(-.056,1.03,.11),.111)
    if m.lod<2:
        m.slab("accent",-.125,1.112,.169,.31,.169,.298,.16,.14)
        for i in range(3 if m.lod==0 else 1):
            at=.813+.207*i
            m.drum("accent",x,at,z,.44-.051*i,.044)
    if m.lod==0:
        m.slab("trim",-.394,.005,-.243,.311,.093,.375,.12)
        m.block("forge",-.394,.091,-.239,.126,.018,.344)


def rotary_smelter(m):
    # Two saddles support the long tilting crucible. Its charging mouth is at
    # the rear; the southern lip pours into a removable receiving vessel.
    for z,h in ((-.231,.47),(.401,.606)):
        m.slab("metal",-.185,0,z,1.097,h,.249,.135,.38)
    with m.transform(translate=(-.185,.65,-.525),rotate_x=77):
        n=(16,9,6)[m.lod]
        m.drum("accent",0,0,0,.34,.167,.411,n=n)
        m.drum("ivory",0,.161,0,.412,.926,.407,n=n)
        m.drum("metal",0,1.083,0,.416,.203,.28,n=n)
        with m.transform(rotate_x=180):
            m.drum("door",0,-.012,0,.279,.025,n=n)
            m.drum("forge",0,.011,0,.198,.027,n=n)
        if m.lod<2:
            for y in ((.267,.548,.813,1.026) if m.lod==0 else (.293,.871)):
                m.drum("trim",0,y,0,.426,.059,n=n)
    m.drum("metal",-.189,0,-.701,.201,.188,.249)
    m.drum("forge",-.189,.185,-.701,.196,.023)
    m.slab("wall",.577,0,.142,.536,.419,.94,.088)
    m.slab("glass",.577,.414,.142,.526,.164,.924,.10)
    m.vault("metal",.577,.572,.142,.577,.158,.987)
    m.entry(.579,.014,-.334,.305,.354)
    if m.lod==0:
        m.beam("gold",(.233,.324,.395),(.511,.399,.411),.076)
        m.lamp(.779,.014,-.435,.095)


def shield(m,x,y,z,w,h,mat):
    def face(s,shift,col):
        a=w*s*.5
        b=h*s
        m.relief(col,[(x,y+.01+(h-b)*.5),(x+a,y+.01+(h-b)*.5+b*.39),
                      (x+a*.92,y+.01+(h-b)*.5+b),(x-a*.92,y+.01+(h-b)*.5+b),
                      (x-a,y+.01+(h-b)*.5+b*.39)],z+shift,.049)
    face(1,0,"accent")
    face(.80,-.013,mat)
    if m.lod<2:
        m.rune(x,y+h*.57,z-.036,w*.48)


def armory(m):
    e=m.era
    if e==1:
        return arsenal_court(m)
    if e==2:
        return issue_depot(m)
    if e==3:
        return secure_magazine(m)
    m.slab("fieldstone",0,0,.04,1.87,.134,1.60,.13)
    m.hearth(0,.13,.12,(1.54,1.24,1.29),roof_material="roof" if e==0 else None)
    # Standards sit on two gate piers; broad, pointed shields identify the clan armory.
    for s in (-1,1):
        m.slab("wall",s*.60,.13,-.36,.382,.58,.44,.13,.10)
        m.slab("trim",s*.60,.705,-.36,.411,.083,.472)
        shield(m,s*.60,.238,-.596,.356,.415,"banner" if e<2 else "metal")
    crest_base=.13+1.24*((.47,.54,.52,.55)[e]-.015+(.264,.21,.145,.18)[e])
    m.block("trim",0,crest_base-.012,.15,.80,.082,.73)
    m.vault("roof",0,crest_base+.066,.15,.89,.167,.80)
    if m.lod==0:
        for s in (-1,1):
            m.lamp(s*.60,.787,-.529,.092)
    if e>=2: m.block("glass",0,crest_base+.225,.15,.40,.098,.55)


def arsenal_court(m):
    # A shield court between two fortified magazines replaces the single gatehouse.
    m.slab("fieldstone",0,0,.012,1.89,.119,1.69,.13)
    for x in (-.582,.582):
        m.drum("wall",x,.113,.216,.321,.644,.293,n=(8,6,5)[m.lod])
        m.drum("trim",x,.75,.216,.339,.099,.343,n=(8,6,5)[m.lod])
        m.drum("roof_tile",x,.843,.216,.355,.247,.136,n=(8,6,5)[m.lod])
    m.slab("wall",0,.111,.489,.813,.389,.401,.1)
    m.entry(0,.119,.288,.383,.337)
    m.vault("slate",0,.492,.489,.902,.166,.457)
    for x in (-.58,.58):
        m.slab("slate",x,.113,-.405,.512,.167,.469,.11)
        m.block("timber",x,.275,-.361,.44,.449,.087)
        if m.lod<2:
            shield(m,x,.268,-.429,.388,.385,"banner")
        else:
            m.relief("banner",[(x,.288),(x+.17,.428),(x+.17,.627),(x-.17,.627),(x-.17,.428)],-.428,.055)
    if m.lod==0:
        for x in (-.62,.62):
            m.rune(x,.561,-.09,.148)
            m.lamp(x,.121,-.676,.101)


def issue_depot(m):
    # Sawtooth lighting serves the long inspection floor; issue counters face the forecourt.
    m.slab("fieldstone",0,0,.02,1.88,.132,1.69,.10)
    m.slab("brick",-.081,.126,.217,1.493,.544,1.021,.087)
    for i in range(3):
        z=-.127+i*.34
        m.wedge((-.081,.665,z),1.557,.329,.213,m.color("metal"),material="metal")
        if m.lod<2:
            m.block("glass",-.081,.722,z+.168,1.328,.122,.025)
    m.entry(-.48,.139,-.297,.366,.437)
    m.block("door",.218,.303,-.306,.572,.253,.039)
    m.slab("metal",.218,.282,-.368,.627,.064,.192,.075)
    m.slab("trim",.149,.132,-.609,.789,.174,.347,.10)
    for x in ((-.037,.333) if m.lod<2 else (.149,)):
        m.block("metal",x,.298,-.604,.221,.288,.211)
        shield(m,x,.338,-.722,.173,.178,"accent")
    m.stack(.716,.13,.534,.118,.867,"brick")
    if m.lod==0:
        m.lamp(-.757,.134,-.46,.102)
        for x in (-.179,.056,.293,.527):
            m.block("gold",x,.544,-.322,.112,.039,.031)


def secure_magazine(m):
    # A low armored vault occupies the left side. The loading track crosses the open court.
    m.slab("fieldstone",0,0,.014,1.92,.135,1.71,.15)
    m.slab("wall",-.313,.127,.185,1.124,.481,1.268,.13,.09)
    m.slab("glass",-.313,.601,.185,1.031,.139,1.164,.145)
    m.vault("ivory",-.313,.733,.18,1.183,.242,1.33)
    m.entry(-.473,.139,-.447,.358,.407)
    m.slab("trim",-.478,.128,-.533,.475,.075,.267,.12)
    # A counterbalanced roof crane raises a shield pallet above its receiving cradle.
    m.drum("trim",-.311,.964,.176,.223,.13,.196)
    m.drum("accent",-.311,1.086,.176,.181,.139,.153)
    m.beam("metal",(-.685,1.167,.329),(.741,1.406,-.257),.147,.177)
    m.slab("wall",-.665,1.127,.319,.328,.268,.349,.16,.12)
    apex=(-.286,1.575,.168)
    m.beam("ivory",(-.372,1.195,.202),apex,.105)
    m.beam("gold",apex,(.702,1.405,-.241),.056)
    m.beam("trim",(-.637,1.308,.312),apex,.062)
    m.slab("trim",.611,1.303,-.207,.201,.132,.181,.14)
    m.beam("metal",(.611,1.312,-.207),(.611,.955,-.207),.052)
    m.block("accent",.611,.905,-.207,.448,.077,.153)
    m.relief("metal",[(.611,.438),(.838,.629),(.819,.919),(.404,.919),(.384,.629)],
             -.252,.144)
    if m.lod<2:
        shield(m,.611,.484,-.271,.341,.381,"banner")
    m.slab("trim",.566,.131,-.211,.621,.083,.563,.11)
    for x in (.366,.758):
        if m.lod==2:
            m.block("metal",x,.209,-.211,.083,.098,.296)
        else:
            m.beam("wall",(x,.202,-.364),(x,.332,-.211),.086)
            m.beam("metal",(x,.324,-.211),(x,.211,-.07),.072)
    if m.lod<2:
        for z in (-.062,.429):
            m.beam("trim",(-.804,.133,z),(-.819,.60,z+.041),.091)
        m.rune(-.311,.848,-.491,.187)
    if m.lod==0:
        for x in (-.57,-.32,-.07):
            m.block("metal",x,.609,-.397,.033,.124,.036)
        with m.transform(translate=(.614,1.351,-.134),rotate_x=-90):
            m.drum("metal",0,0,0,.084,.132)
            m.drum("gold",0,.121,0,.093,.037)
        for z in (-.542,.099):
            m.slab("accent",.565,.133,z,.457,.032,.067,.11)
        m.lamp(-.766,.134,-.484,.104)


def loom(m):
    e=m.era
    if e == 1:
        return dyers_guild(m)
    if e == 2:
        return power_loom(m)
    if e == 3:
        return filament_loom(m)
    m.slab("fieldstone",0,0,0,1.86,.103,1.69,.10)
    m.hearth(-.32,.099,.31,(1.01,1.16,.98),roof_material="roof")
    x,z=.37,-.38
    for s in (-1,1):
        m.block("timber",x+s*.318,.104,z,.089,.72,.114)
        m.block("trim",x+s*.318,.104,z,.127,.075,.40)
    m.block("timber",x,.765,z,.77,.084,.10)
    # The warp hangs from the breast beam and rolls back over the front cloth beam.
    m.block("banner",x,.307,z,.564,.310,.044)
    for j in range((7,5,3)[m.lod]):
        dx=(j/((7,5,3)[m.lod]-1)-.5)*.51
        m.block("ivory",x+dx,.609,z,.020,.165,.019)
    m.block("trim",x,.284,z,.691,.070,.127)
    m.block("cloth",x,.141,z-.056,.48,.169,.058)
    if m.lod<2:
        for j in range(3):
            dx=(j-1)*.146
            m.rune(x+dx,.483,z-.032,.132,"gold")
        m.block("ivory",x,.560,z-.023,.556,.031,.026)
    if m.lod==0:
        for i in range(3):
            sx=-.70+i*.17
            m.drum("trim",sx,.104,-.50,.082,.031)
            m.drum(("ivory","banner","gold")[i],sx,.131,-.50,.065,.15,top=.058)
            m.drum("trim",sx,.28,-.50,.078,.023)


def roll(m, material, x, y, z, radius, width):
    """Cloth or sheet gathered onto a horizontal spindle between its bearings."""
    with m.transform(translate=(x-width/2,y,z),rotate_z=-90):
        m.drum(material,0,0,0,radius,width,n=(12,7,4)[m.lod])
        if m.lod == 0:
            for along in (-.017,width-.002):
                m.drum("trim",0,along,0,radius*1.11,.022,n=10)


def dyers_guild(m):
    m.slab("fieldstone",0,0,.02,1.83,.095,1.65)
    m.slab("wall",0,.088,.37,1.54,.50,.79,.085)
    m.hip_roof((0,.581,.37),1.57,.83,.38,m.color("roof_tile"),overhang=.035,material="roof_tile")
    m.entry(.46,.095,-.032,.36,.42)
    # Finished lengths hang over a covered drying rail above the dye vats.
    for x in (-.70,.19):
        m.block("timber",x,.093,-.415,.082,.66,.084)
    m.block("trim",-.255,.733,-.415,.985,.069,.103)
    for i,mat in enumerate(("cloth","banner","ivory")):
        x=-.53+i*.272
        height=(.38,.46,.33)[i]
        m.block(mat,x,.763-height,-.471,.212,height,.036)
        if m.lod==0:
            m.rune(x,.797-height,-.493,.125)
    for x,z in ((-.47,-.57),(.41,-.43))[:1 if m.lod==2 else 2]:
        m.drum("timber",x,.096,z,.199,.19,.204)
        m.drum("banner",x,.283,z,.165,.015)
        if m.lod==0:
            m.drum("metal",x,.144,z,.202,.032)
            m.beam("timber",(x-.07,.26,z),(x+.14,.46,z+.05),.039)


def power_loom(m):
    m.slab("fieldstone",0,0,.01,1.89,.12,1.52,.07)
    m.slab("brick",-.12,.114,.38,1.40,.51,.59,.09)
    m.wedge((-.12,.617,.38),1.49,.69,.23,m.color("metal"),material="metal")
    m.stack(.61,.120,.42,.168,.94,"brick")
    m.block("glass",-.14,.609,.708,.91,.15,.04)
    # A cast-iron power loom: triangular end frames hold two beams and the beating reed.
    for x in (-.55,.38):
        m.slab("wall",x,.117,-.30,.185,.38,.69,.12,.22)
        m.beam("accent",(x,.417,-.48),(x,.78,-.13),.084)
    roll(m,"banner",-.085,.442,-.493,.139,1.01)
    roll(m,"timber",-.085,.768,-.126,.084,1.01)
    with m.transform(translate=(-.085,.60,-.31),rotate_x=-47):
        m.block("cloth",0,-.021,0,.83,.042,.44)
    if m.lod<2:
        with m.transform(translate=(.50,.374,-.29),rotate_y=90):
            m.wheel(0,0,0,.235,"metal",4)
    if m.lod==0:
        for x in (-.41,-.24,-.07,.10,.27):
            m.beam("ivory",(x,.487,-.48),(x,.759,-.134),.021)
        m.entry(-.50,.126,.078,.32,.39)


def filament_loom(m):
    m.slab("wall",0,0,.035,1.67,.15,1.74,.21)
    # Three cartridges feed a broad translating print head over the take-up roll.
    for x in (-.50,0,.50):
        m.drum("trim",x,.145,.43,.189,.102)
        m.drum("ivory" if x else "banner",x,.239,.43,.151,.51,.132)
        m.drum("metal",x,.741,.43,.192,.085,.128)
        if m.lod==0:
            for y in (.315,.419,.523,.627):
                m.drum("accent",x,y,.43,.156-(y-.30)*.04,.022,n=8)
    for x in (-.63,.63):
        m.slab("wall",x,.144,-.19,.23,.70,.61,.14,.20)
    m.vault("metal",0,.806,-.16,1.53,.24,.52)
    m.slab("glass",-.08,.986,-.125,.66,.17,.34)
    m.block("banner",0,.422,-.299,.92,.365,.067)
    roll(m,"cloth",0,.416,-.38,.139,1.14)
    if m.lod<2:
        for x in (-.31,0,.31):
            m.block("ascension",x,.511,-.336,.032,.238,.016)
    if m.lod==0:
        m.rune(-.08,.942,-.433,.195)


def assembly_works(m):
    m.slab("fieldstone",0,0,.012,1.90,.153,1.69,.20)
    m.slab("wall",-.04,.147,.36,1.57,.51,.85,.20,.07)
    m.slab("glass",-.04,.652,.36,1.49,.175,.79,.19)
    m.slab("ivory",-.04,.819,.36,1.65,.12,.91,.23,.05)
    m.vault("metal",-.37,.935,.39,.59,.251,.67)
    m.entry(.38,.153,-.066,.41,.39)
    m.slab("metal",.05,.149,-.50,1.54,.18,.40,.16)
    m.block("door",.05,.321,-.50,1.34,.034,.29)
    # Opposed articulated pick-and-place arms reach over a conveyor.
    for s in (-1,1):
        x=s*.60
        m.drum("trim",x,.151,-.225,.16,.276,.125)
        base=(x,.419,-.225)
        joint=(s*.71,.781,-.32)
        wrist=(s*.38,.70,-.51)
        m.beam("accent",base,joint,.143)
        m.beam("ivory",joint,wrist,.106)
        m.block("metal",wrist[0],.551,wrist[2],.092,.15,.089)
        if m.lod<2:
            roll(m,"metal",joint[0],joint[1],joint[2],.088,.167)
            m.block("trim",wrist[0],.514,wrist[2],.164,.053,.081)
    if m.lod==0:
        for x in (-.43,.03,.44):
            m.slab("metal",x,.351,-.495,.21,.113,.19,.16,.20)
        m.rune(-.37,1.065,.041,.171)


def arc_foundry(m):
    m.slab("fieldstone",0,0,0,1.91,.138,1.72,.17)
    # Three electrodes descend from a cantilever over the open refractory vessel.
    m.drum("wall",-.18,.132,-.116,.42,.22,.49)
    m.drum("metal",-.18,.348,-.116,.49,.292,.442)
    m.drum("trim",-.18,.635,-.116,.468,.106,.48)
    m.drum("forge",-.18,.735,-.116,.406,.014)
    m.slab("wall",.593,.132,.22,.40,.91,.73,.17,.14)
    m.slab("glass",.593,1.037,.22,.35,.14,.63,.14)
    m.slab("ivory",.22,1.167,.065,1.13,.158,.45,.14)
    for x,z in ((-.34,-.10),(-.08,-.12),(-.19,.13)):
        m.drum("trim",x,.786,z,.062,.412,.071)
        if m.lod==0:
            m.drum("accent",x,1.049,z,.089,.064)
    m.block("trim",.035,.158,-.639,.50,.18,.295)
    m.beam("metal",(-.18,.608,-.48),(.034,.345,-.615),.145)
    if m.lod<2:
        ingots(m,.30,.14,-.58)
        m.beam("forge",(-.18,.638,-.50),(.034,.364,-.64),.069)
    if m.lod==0:
        m.rune(.594,.66,-.154,.209)
        for z in (.39,.58):
            m.slab("metal",-.43,.137,z,.69,.173,.15)


def workshop(m):
    if m.era==2:
        return steam_hammer_shop(m)
    m.slab("fieldstone",0,0,0,1.81,.116,1.69,.14)
    m.hearth(-.27,.113,.25,(1.12,1.36,1.15))
    # The drill press has a worktable, a belt flywheel, bearings and a visible quill.
    x,z=.49,-.33
    m.slab("trim",x,.113,z,.53,.128,.47,.12)
    m.block("wall",x+.151,.235,z,.16,.69,.23)
    m.block("metal",x-.023,.837,z,.51,.147,.274)
    m.block("trim",x-.136,.476,z,.122,.374,.10)
    m.block("timber",x-.096,.36,z,.372,.084,.261)
    m.block("metal",x-.096,.238,z,.141,.13,.147)
    m.wheel(x+.079,.91,z-.15,.206,"accent",4 if m.lod==2 else 6)
    if m.lod==0:
        ingots(m,-.61,.114,-.54)
        m.lamp(.13,.117,.59,.099)


def steam_hammer_shop(m):
    # Boiler and steam main supply the arch-framed hammer on the open forging floor.
    m.slab("fieldstone",0,0,0,1.88,.127,1.71,.14)
    m.slab("brick",-.365,.12,.428,.946,.538,.681,.09)
    m.vault("metal",-.365,.653,.428,1.026,.17,.756)
    m.entry(-.437,.124,.086,.374,.418)
    m.stack(-.626,.526,.521,.157,.792,"brick")
    with m.transform(translate=(.428,.517,.759),rotate_x=-90):
        m.drum("metal",0,0,0,.242,.588)
        if m.lod==0:
            for t in (.09,.453):
                m.drum("trim",0,t,0,.251,.041)
    m.slab("brick",.428,.124,.458,.483,.25,.664,.075)
    m.slab("wall",.207,.12,-.36,.974,.188,.634,.15)
    for s in (-1,1):
        m.beam("trim",(.207+s*.388,.282,-.343),(.207+s*.257,1.032,-.343),.162,.209)
    m.vault("metal",.207,.975,-.343,.704,.183,.297)
    m.drum("metal",.207,1.144,-.343,.185,.267,.171)
    m.drum("accent",.207,.687,-.343,.075,.336)
    m.block("metal",.207,.591,-.343,.319,.102,.187)
    m.slab("trim",.207,.30,-.343,.338,.205,.238,.13,-.08)
    m.beam("metal",(.425,.717,.464),(.425,1.264,.455),.082)
    m.beam("metal",(.425,1.254,.461),(.206,1.252,-.342),.088)
    if m.lod<2:
        m.rune(.207,1.049,-.50,.15)
    if m.lod==0:
        ingots(m,-.761,.127,-.437)
        m.lamp(-.684,.129,.007,.113)


def sheds(m):
    e=m.era
    m.slab("fieldstone",0,0,0,1.88,.12,1.76,.11)
    m.slab("brick" if e==2 else "wall",0,.116,.04,1.55,.426,1.36,.06)
    for i in range(3):
        z=-.416+i*.46
        m.wedge((0,.54,z),1.63,.46,.204,m.color("roof"),material="roof")
        if m.lod<2:
            m.block("glass",0,.565,z+.233,1.31,.148,.026)
    m.entry(-.31,.117,-.646,.44,.375)
    if m.lod<2:
        for s in (-1,1):
            for z in (-.34,.26):
                m.block("trim",s*.779,.19,z,.040,.26,.29)
                m.block("glass",s*.803,.218,z,.021,.184,.235)
    if m.lod==0:
        for x in (.16,.39,.62):
            m.block("metal",x,.247,-.65,.128,.139,.031)
        m.lamp(-.62,.12,-.59,.103)


def large(m,kind):
    e=m.era
    if kind=="Factory":
        if e == 3:
            return assembly_works(m)
        sheds(m)
        for x in (-.49,.03):
            m.stack(x,.571,.463,.137,(.59 if x<0 else .45),"brick")
    elif kind=="Foundry":
        if e == 3:
            return arc_foundry(m)
        m.slab("fieldstone",0,0,0,1.88,.115,1.76,.12)
        m.hearth(-.45,.111,.32,(.81,1.41,1.06))
        kiln(m,.376,.33,.41,1.02)
        # Tapping ladle in its trunnion fork and channel below the furnace mouth.
        x,z=.27,-.409
        m.drum("trim",x,.144,z,.177,.322,top=.346)
        m.drum("forge",x,.461,z,.295,.014)
        for s in (-1,1):
            m.block("metal",x+s*.33,.113,z,.095,.298,.16)
            m.beam("accent",(x+s*.184,.343,z),(x+s*.34,.343,z),.071)
        # A dedicated tap above the tuyere pours downslope into the ladle.
        m.block("trim",.376,.564,-.019,.185,.118,.127)
        m.beam("trim",(.376,.61,-.032),(.291,.512,-.284),.102,.116)
        m.beam("forge",(.376,.628,-.043),(.289,.529,-.287),.056,.071)
        if m.lod<2: ingots(m,-.67,.113,-.39)
        if m.lod==0: m.lamp(-.09,.116,-.67,.111)
    else:
        m.slab("fieldstone",0,0,0,1.85,.12,1.73,.14)
        m.slab("wall",0,.116,.04,1.59,.40,1.38,.15)
        m.slab("glass",0,.51,.04,1.62,.149,1.41,.15)
        m.slab("ivory",0,.656,.04,1.68,.086,1.47,.15)
        m.entry(0,.121,-.653,.46,.362)
        # The capped ceramic insulators flank a dark, ribbed processing chamber.
        m.vault("metal",0,.737,.10,.73,.28,.82)
        for x in (-.55,.55):
            for z in (-.33,.35):
                m.drum("trim",x,.739,z,.074,.262)
                for i in range(3 if m.lod<2 else 1):
                    m.drum("turquoise",x,.78+i*.07,z,.107,.039,top=.066)
        if m.lod<2:
            for x in (-.59,.59):
                m.beam("accent",(x,.745,-.25),(x,.745,.39),.029)
        if m.lod==0:
            for z in (-.16,.08,.32): m.block("trim",0,.997,z,.407,.046,.062)
            m.lamp(.48,.119,-.636,.11)


def build(spec,lod):
    m=Stonework(spec,lod)
    w,d=spec.footprint
    with m.transform(scale=(w/2,(.58*w+.42*d)/2,d/2)):
        kind=spec.building_type
        if kind=="Smelter": furnace(m)
        elif kind=="Armory": armory(m)
        elif kind=="Blacksmith": blacksmith(m)
        elif kind=="Weaver": loom(m)
        elif kind=="Workshop": workshop(m)
        else: large(m,kind)
    return m


if __name__=="__main__":
    publish_package("dwarf_industry",build)
