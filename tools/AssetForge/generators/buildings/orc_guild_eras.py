"""Guild buildings with successive changes of profession-specific equipment and floor plan."""
import math
from generators.buildings.orc_era_forms import flue, hall, pot, ring, turned, wheel


def medicine_garden(m):
    if m.e==1:
        # A terraced apothecary overlooks its open physic garden.
        hall(m,-.34,.34,.83,.78,.62,.43,"hip")
        for x,z,r in ((.40,.12,.30),(.33,-.57,.28),(-.40,-.56,.24)):
            pot(m,x,z,r,.22,plant=True)
    elif m.e==2:
        # Brick propagation house, glazed roof, boiler stack and a long seedling bench.
        hall(m,-.17,.28,1.24,.70,.63,.38,"gable")
        m.prism_x((-.17,.674,.28),1.03,.68,.366,m.col("glass"),material="glass")
        for x in ((-.57,-.18,.21) if m.lod<2 else (-.18,)):
            m.beam("metal",(x,.68,-.053),(x,1.063,.28),.034)
            m.beam("metal",(x,1.051,.29),(x,.68,.61),.030)
        flue(m,.61,.47,1.29,.15)
        for x in (-.57,.13,.65):
            pot(m,x,-.45,.192,.26,plant=True)
    else:
        # Stacked growing chambers feed a fan of exposed medicinal leaves.
        for x,z,h,r in ((.10,.31,1.24,.40),(-.47,-.13,.68,.30),(.54,-.24,.54,.25)):
            turned(m,x,z,[(0,.85*r,"fieldstone"),(.09,r,"metal"),(.18*h,.87*r,"ivory"),
                          (.66*h,.94*r,"glass"),(.71*h,1.08*r,"turquoise"),
                          (.80*h,r,"metal"),(.91*h,.70*r,"glass"),(h,.39*r,"ivory")])
            for k in range((5,3,2)[m.lod]):
                with m.transform(translate=(x,.96*h,z),rotate_y=137*k,rotate_z=30+k%2*24):
                    m.leaf(0,0,0,r*.98)
        m.entry(.065,-.070,.27,.36)
        m.horn(-.66,.33,-.10,.46,-1)
        m.horn(.68,.25,-.24,.42,1)


def steam_baths(m):
    if m.e==2:
        # The tall boiler backs onto the masonry washroom. A heated open pool occupies one wing.
        hall(m,-.38,.17,.84,1.13,.60,.28,"gable")
        flue(m,.42,.47,1.40,.245)
        pot(m,.41,-.39,.433,.33)
        m.beam("metal",(.44,.56,.47),(.44,.56,-.13),.096)
        m.beam("metal",(.44,.56,-.13),(.44,.31,-.16),.095)
        if m.lod==0:
            wheel(m,.43,.58,.11,.138)
    else:
        # A high thermal spring spills from a mushroom canopy to a pair of stepped pools.
        turned(m,-.25,.31,[(0,.47,"fieldstone"),(.12,.54,"metal"),(.33,.38,"glass"),
                            (.61,.32,"glass"),(.74,.59,"ivory"),(.84,.66,"turquoise"),
                            (.99,.44,"glass"),(1.04,.25,"ivory")],.82)
        m.entry(-.28,-.026,.30,.43)
        for x,z,r,h in ((.43,.04,.399,.50),(.12,-.55,.42,.23)):
            pot(m,x,z,r,h)
        m.beam("water",(.42,.42,-.18),(.27,.20,-.35),.12)
        for s in (-1,1):
            m.horn(-.25+s*.47,.75,.30,.52,s)


def star_watch(m):
    if m.e==2:
        # Iron equatorial refractor with a counterweight. The instrument has its own pad.
        hall(m,-.46,.34,.64,.91,.60,.22,"shed")
        turned(m,.22,-.14,[(0,.52,"fieldstone"),(.16,.55,"brick"),(.30,.42,"metal"),
                            (.45,.27,"trim"),(.64,.18,"metal"),(.70,.23,"gold")])
        for s in (-1,1):
            m.beam("metal",(.22+s*.20,.48,-.15),(.22+s*.24,.91,-.13),.085)
        with m.transform(translate=(.22,.91,-.14),rotate_z=-36,rotate_x=-26):
            turned(m,0,0,[(-.48,.104,"metal"),(-.39,.143,"gold"),(-.23,.134,"metal"),
                           (.23,.168,"metal"),(.30,.196,"cloth"),(.56,.19,"metal"),
                           (.61,.24,"gold"),(.69,.23,"metal"),(.69,.183,"glass")])
        if m.lod<2:
            wheel(m,.21,.61,-.40,.13)
    else:
        # Phased dish array. The shallow concave reflectors and their feed arms tilt skyward.
        hall(m,-.44,-.23,.68,.69,.43,.20,"hip")
        for x,z,h,r,yaw in ((.34,.25,.88,.50,-18),(-.48,.53,.60,.34,30),(.51,-.49,.41,.25,2)):
            turned(m,x,z,[(0,.45*r,"metal"),(.10,.55*r,"ivory"),(.27,.31*r,"metal"),
                          (h,.20*r,"turquoise")])
            with m.transform(translate=(x,h,z),rotate_y=yaw,rotate_x=-44):
                turned(m,0,0,[(-.13*r,.20*r,"metal"),(.03*r,.63*r,"ivory"),
                             (.41*r,r,"ivory"),(.46*r,r,"turquoise"),(.20*r,.63*r,"glass"),
                             (.09*r,.13*r,"glass")])
                m.beam("gold",(0,.09*r,0),(0,.68*r,0),.085*r)
                m.drum("ivory",0,.61*r,0,.105*r,.15*r)


def reading_desk(m,x,z,s):
    with m.transform(translate=(x,0,z),scale=s):
        turned(m,0,0,[(0,.24,"fieldstone"),(.06,.26,"trim"),(.12,.13,"timber"),
                      (.40,.09,"trim"),(.44,.20,"gold")],.75)
        with m.transform(translate=(0,.46,0),rotate_x=-16):
            m.block("trim",0,0,0,.56,.066,.46)
            for side in (-1,1):
                with m.transform(translate=(side*.131,.073,0),rotate_z=side*9):
                    m.block("ivory",0,0,0,.24,.042,.37)
                    if m.lod==0:
                        for dz in (-.070,.025,.10):
                            m.block("timber",0,.042,dz,.16,.004,.012)


def writing_house(m):
    if m.e==1:
        # The scribe's desk moves into a roofed street loggia with an upper archive.
        hall(m,-.16,.40,1.18,.69,.76,.48,"hip")
        for s in (-1,1):
            x=.19+s*.44
            turned(m,x,-.49,[(0,.11,"fieldstone"),(.084,.10,"plaster"),
                              (.51,.065,"plaster"),(.58,.093,"gold")])
            m.horn(x,.54,-.49,.36,s)
        m.hip_roof((.19,.58,-.33),1.12,.59,.19,m.col("roof_tile"),overhang=0,material="roof_tile")
        reading_desk(m,.19,-.50,.69)
        m.crest(-.47,.58,.042,.11,"book")
    elif m.e==2:
        # A narrow clerestory with three high north-facing windows lights a row of desks.
        hall(m,-.16,.27,1.37,.80,.61,.15,"shed")
        for j in range(3):
            x=-.60+j*.43
            m.wedge((x,.745,.39),.36,.47,.27,m.col("glass"),material="glass")
            if m.lod<2:
                m.block("trim",x,1.004,.62,.37,.04,.065)
        reading_desk(m,.38,-.43,.91)
        m.crest(-.48,.42,-.149,.12,"book")
        if m.lod==0:
            m.jar(-.60,0,-.31,.112,.20)
    else:
        # Scribes work beneath a tilted, monumental pair of illuminated leaves.
        turned(m,-.27,.22,[(0,.52,"fieldstone"),(.12,.55,"metal"),(.48,.40,"glass"),
                            (.63,.44,"ivory"),(.70,.35,"turquoise")],.82)
        m.entry(-.27,-.18,.31,.40)
        for s in (-1,1):
            with m.transform(translate=(-.27+s*.22,.66,.20),rotate_z=-s*29):
                m.block("metal",0,0,0,.35,.68,.33,.13)
                m.block("ivory",0,.10,-.161,.278,.49,.041,.07)
                if m.lod==0:
                    for j in range(3):
                        m.block("turquoise",-.012,.21+j*.11,-.184,.17,.022,.019)
        reading_desk(m,.51,-.34,1.00)
        m.horn(.61,.31,-.26,.44,1)


def press_hall(m):
    if m.e==2:
        # Flywheel, bed and impression cylinder of a rotary press in an open engine bay.
        hall(m,-.43,.34,.78,.91,.64,.27,"shed")
        flue(m,-.66,.49,1.19,.13)
        m.block("fieldstone",.38,0,-.17,.78,.15,.90,.08)
        for x in (.12,.65):
            m.block("metal",x,.12,-.16,.125,.47,.79,.09)
        for z in (-.39,.08):
            with m.transform(translate=(.11,.55,z),rotate_z=-90):
                m.drum("gold" if z<0 else "ivory",0,0,0,.166,.59,n=(14,8,5)[m.lod])
        wheel(m,.41,.56,-.56,.31)
        m.block("ivory",.39,.39,-.46,.41,.045,.30)
    else:
        # The sealed imaging tower feeds a broad folded-paper delivery chute.
        turned(m,-.30,.27,[(0,.47,"metal"),(.14,.50,"ivory"),(.32,.37,"turquoise"),
                            (.80,.33,"glass"),(.95,.41,"metal"),(1.08,.34,"ivory"),
                            (1.14,.19,"turquoise")],.88)
        m.entry(-.32,-.13,.29,.41)
        m.block("metal",.38,0,-.17,.72,.38,.77,.10)
        for j in range((3,2,1)[m.lod]):
            with m.transform(translate=(.38,.42+.115*j,-.37+.106*j),rotate_x=-25):
                m.block("ivory",0,-.026,0,.57-.024*j,.050,.40)
        m.horn(-.56,.91,.26,.62,-1)
        m.horn(-.04,.91,.26,.62,1)
        if m.lod<2:
            m.crest(.38,.29,-.572,.12,"book")


def research_lab(m):
    # Two tilted magnetic hoops enclose an illuminated specimen over the test chamber.
    turned(m,0,.22,[(0,.69,"fieldstone"),(.15,.77,"metal"),(.36,.65,"glass"),
                      (.45,.72,"ivory"),(.54,.65,"turquoise")],.78)
    m.entry(0,-.373,.35,.37)
    for s in (-1,1):
        m.beam("metal",(s*.48,.44,.20),(s*.45,1.00,.21),.14)
        with m.transform(translate=(0,.97,.21),rotate_y=s*46):
            outer=.54 if s<0 else .429
            ring(m,"ivory" if s<0 else "turquoise",0,0,0,outer,outer-.068,.08)
        if m.lod<2:
            m.horn(s*.54,.39,.22,.55,s)
    turned(m,0,.22,[(.53,.18,"metal"),(.68,.125,"turquoise"),(.79,.156,"gold"),
                      (.97,.206,"glass"),(1.15,.12,"ascension"),(1.24,.036,"ivory")])


def university(m):
    if m.e==2:
        # Iron-and-brick polytechnic: clock tower, long workshop and a lecture wing.
        hall(m,.24,.35,1.12,.82,.66,.33)
        hall(m,.39,-.34,.83,.48,.37,.16,"shed")
        m.block("fieldstone",-.53,0,.17,.61,.16,.54,.06)
        m.block("brick",-.53,.15,.17,.48,.95,.43,.07)
        m.block("trim",-.53,1.09,.17,.55,.10,.50,.05)
        m.block("wall",-.53,1.18,.17,.46,.28,.40)
        m.disk("ivory",-.53,1.32,-.05,.12,.039)
        m.pyramid((-.53,1.46,.17),.59,.51,.34,m.col("metal"),material="metal")
        m.crest(.39,.28,-.618,.12,"book")
        if m.lod<2:
            m.beam("trim",(-.53,1.32,-.093),(-.49,1.36,-.093),.019)
            m.beam("trim",(-.53,1.32,-.096),(-.53,1.40,-.096),.018)
    else:
        # Central lecture galleries project over a narrow concourse. Pods form a low forecourt.
        turned(m,-.12,.33,[(0,.47,"fieldstone"),(.15,.49,"ivory"),(.23,.30,"metal"),
                            (.70,.27,"glass"),(.80,.58,"turquoise"),(.95,.64,"ivory"),
                            (1.12,.56,"glass"),(1.17,.42,"metal"),(1.29,.37,"ivory")],.84)
        m.entry(-.12,.043,.27,.42)
        for x,z,r,h in ((-.56,-.36,.31,.47),(.48,-.21,.36,.63)):
            turned(m,x,z,[(0,r,"metal"),(.12,r*1.04,"ivory"),(.63*h,r*.88,"glass"),
                          (.81*h,r*1.19,"turquoise"),(h,.60*r,"ivory")],.85)
            if m.lod<2:
                m.crest(x,.46*h,z-r*.84,.083,"book")
            m.beam("metal",(x,.18,z),(x*.48,.18,.29),.15)
            m.horn(x+(.14 if x>0 else -.14),.77*h,z,.44,1 if x>0 else -1)


DESIGNERS = {"Bathhouse":steam_baths,"Herbalist":medicine_garden,"Observatory":star_watch,
             "Printworks":press_hall,"Scriptorium":writing_house,"ResearchLab":research_lab,
             "University":university}
FIRST_ERA = {"Bathhouse":2,"Herbalist":1,"Observatory":2,"Printworks":2,"Scriptorium":1,
             "ResearchLab":3,"University":2}
