"""Orc public buildings shaped by the work inside them."""
import math

from generators.buildings.orc_era_forms import hall, ring
from generators.buildings.orc_guild_eras import reading_desk
from generators.buildings.orc_resource_yards import shaft, wheel


def barrel_vault(m, x, z, w, d, eave, rise, mat):
    """A closed barrel roof, including both semicircular gables and the soffit."""
    steps = (10, 6, 4)[m.lod]
    fore = [(x + w*.5*math.cos(i*math.pi/steps), eave + rise*math.sin(i*math.pi/steps),
             z-d*.5) for i in range(steps+1)]
    back = [(u,v,z+d*.5) for u,v,_ in fore]
    m.face(mat, fore[::-1])
    m.face(mat, back)
    for i in range(steps):
        m.face(mat, (fore[i], fore[i+1], back[i+1], back[i]))
    m.face(mat, (fore[0], back[0], back[-1], fore[-1]))


def granary(m):
    if m.e == 1:
        # Stone staddles ventilate a timber-framed grain loft. The cart stair
        # rises to the loading doors under a deep gable.
        for x in ((-.59,.04,.66) if m.lod < 2 else (-.59,.66)):
            for z in (-.31,.44):
                if m.lod == 2:
                    m.block("fieldstone",x,0,z,.24,.30,.25,.12)
                else:
                    m.block("fieldstone",x,0,z,.23,.24,.24,.23)
                    m.block("wall",x,.225,z,.29,.075,.29,.12)
        m.box((.03,.328,.064),(1.61,.081,1.075),m.col("timber"),material="timber",bottom=True)
        m.block("wall",.03,.364,.064,1.53,.52,.99)
        m.prism_x((.03,.882,.064),1.70,1.19,.49,m.col("roof_tile"),material="roof_tile")
        m.entry(-.17,-.45,.44,.46,.368)
        for i in range(3):
            m.block("fieldstone",-.18,0,-.747+i*.112,.50+i*.009,.123*(i+1),.117)
        for x in ((-.67,.02,.73) if m.lod < 2 else ()):
            for s in (-1,1):
                m.block("timber",x,.375,.064+s*.50,.072,.51,.069)
                if m.lod == 0:
                    m.block("cloth",x,.70,.064+s*.505,.084,.081,.082)
        for s in (-1,1):
            m.horn(.03+s*.72,1.065,.06,.57,s)
        if m.lod < 2:
            for x in (-.48,.42):
                for s in (-1,1):
                    m.block("door",x,.63,.064+s*.497,.22,.12,.033)
                    if m.lod == 0:
                        for dx in (-.064,.020):
                            m.block("trim",x+dx,.63,.064+s*.518,.026,.12,.018)
            m.jar(.43,0,-.60,.137,.25,"thatch")
    else:
        # One gravity discharge hopper hangs from a four-foot loading gantry;
        # the inclined sealed auger delivers grain from the low receiving shed.
        x,z=.29,.22
        for s in (-1,1):
            for t in (-1,1):
                px,pz=x+s*.39,z+t*.36
                m.block("fieldstone",px,0,pz,.22,.082,.23,.12)
                m.beam("metal",(px,.065,pz),(x+s*.32,1.05,z+t*.30),.107)
        m.profile(x,z,[(.39,.10,.10,"metal"),(.47,.11,.11,"gold"),(.80,.48,.45,"ivory"),
                        (.86,.52,.48,"metal"),(1.18,.50,.45,"glass"),(1.25,.54,.49,"turquoise"),
                        (1.42,.30,.27,"ivory"),(1.47,.14,.13,"metal")],n=(14,8,5)[m.lod])
        hall(m,-.52,-.32,.62,.63,.35,.17,"shed")
        m.beam("ivory",(-.53,.38,-.17),(.23,1.46,.20),.153)
        for s in (-1,1):
            m.horn(x+s*.38,1.18,z,.59,s)
        if m.lod < 2:
            m.jar(x,0,z,.14,.26,"thatch")
            if m.lod == 0:
                m.jar(.37,0,-.37,.105,.18,"thatch")


def hospital(m):
    if m.e == 2:
        # Parallel ventilated wards connect at the rear via a brick cross hall.
        for x in (-.53,.52):
            hall(m,x,-.118,.68,1.32,.48,.26,"gable")
            for z in ((-.48,-.11,.24) if m.lod == 0 else (-.30,.20)):
                m.block("glass",x+.334,.232,z,.035,.163,.141)
                if m.lod == 0:
                    m.block("ivory",x+.345,.389,z,.043,.034,.178)
        hall(m,-.03,.535,1.46,.43,.57,.23,"hip",door=False)
        m.block("brick",-.045,0,.436,.45,.91,.39,.035)
        m.block("trim",-.045,.90,.436,.50,.083,.45)
        m.pyramid((-.045,.983,.436),.53,.49,.28,m.col("metal"),material="metal")
        m.crest(-.045,.739,.221,.126,"care")
        if m.lod < 2:
            for x in (-.53,.52):
                for z in (-.53,.16):
                    m.profile(x,z,[(.50,.087,.086,"brick"),(.74,.075,.074,"metal"),
                                   (.80,.104,.100,"trim"),(.86,.064,.06,"metal")],n=(8,5,4)[m.lod])
    else:
        # Emergency arrivals use the apron under a cantilevered recovery ward;
        # the diagnostics ring is seated in a pair of tall, buttressed feet.
        x,z=-.29,.26
        rows=[(0,.42,.43,"metal"),(.13,.46,.47,"ivory"),(.51,.30,.33,"glass"),
                       (.61,.38,.39,"metal"),(.74,.64,.57,"ivory"),(.92,.63,.55,"glass"),
                       (1.02,.54,.48,"turquoise"),(1.08,.39,.34,"ivory")]
        m.profile(x,z,rows if m.lod < 2 else [rows[i] for i in (0,2,4,5,7)],n=(12,7,4)[m.lod])
        m.entry(-.34,-.142,.31,.42)
        for s in (-1,1):
            px=.44+s*.30
            m.block("metal",px,0,-.29,.18,.41,.29,.08)
            m.horn(px,.34,-.25,.47,s)
        ring(m,"ivory",.44,.61,-.27,.419,.272,.20)
        m.block("metal",.44,0,-.38,.25,.28,.59,.11)
        m.box((.44,.31,-.38),(.37,.084,.76),m.col("glass"),material="glass",bottom=True)
        m.crest(x,.867,-.29,.15,"care")
        if m.lod == 0:
            m.jar(-.77,0,-.20,.10,.21,"metal")


def library(m):
    if m.e == 2:
        # Iron barrel vault over the reading room and a square stack tower.
        x,z=-.24,-.085
        m.block("fieldstone",x,0,z,1.12,.119,1.38,.04)
        m.block("brick",x,.114,z,1.06,.56,1.29)
        barrel_vault(m,x,z,1.16,1.41,.672,.41,"glass")
        for dz in ((-.62,-.30,.02,.34,.64) if m.lod == 0 else (-.62,.64)):
            barrel_vault(m,x,z+dz,1.19,.048,.674,.438,"metal")
        m.entry(x,-.746,.43,.52)
        m.crest(x,.794,-.797,.159,"book")
        hall(m,.51,.27,.56,.77,1.10,.24,"hip")
        if m.lod < 2:
            reading_desk(m,.53,-.42,.82)
            for s in (-1,1):
                m.horn(x+s*.47,.671,-.64,.49,s)
    else:
        # A vertical automated archive. Projecting climate-controlled floors
        # alternate with recessed glazed reading galleries on the central core.
        x,z=-.17,.21
        section=[(0,.38,.40,"metal"),(.13,.44,.44,"ivory"),(.32,.32,.34,"glass")]
        for y,r in ((.36,.65),(.76,.53),(1.14,.43)):
            section += [(y,r*.66,r*.57,"metal"),(y+.080,r,r*.84,"ivory"),
                        (y+.149,r*.95,r*.80,"turquoise"),(y+.25,r*.61,r*.55,"glass")]
        section += [(1.49,.18,.16,"ivory")]
        m.profile(x,z,section,n=(12,7,5)[m.lod])
        m.entry(x,-.179,.29,.32)
        reading_desk(m,.43,-.45,1.13)
        for s in (-1,1):
            m.horn(x+s*.32,1.245,z,.57,s)
        if m.lod == 0:
            m.jar(-.61,0,-.38,.11,.20,"metal")


def bank(m):
    # The freestanding vault presents its combination wheel directly to the
    # street. The glazed counting room and wicket adjoin one side of the vault.
    x,z=-.29,.25
    m.block("fieldstone",x,0,z,1.02,.16,1.02,.07)
    m.block("metal",x,.15,z,.91,.83,.89,.07)
    barrel_vault(m,x,z,1.04,1.00,.96,.40,"ivory")
    shaft(m,"trim",(x,.60,-.139),(x,.60,-.291),.444)
    m.disk("metal",x,.60,-.29,.38,.08)
    wheel(m,x,.60,-.398,.238)
    shaft(m,"gold",(x,.60,-.36),(x,.60,-.435),.069)
    for j in range((8,5,3)[m.lod]):
        a=math.tau*j/(8,5,3)[m.lod]
        m.disk("gold",x+.318*math.cos(a),.60+.318*math.sin(a),-.373,.034,.022)
    for side in (-1,1):
        m.horn(x+side*.39,.94,z,.61,side)
    m.profile(.46,-.06,[(0,.34,.43,"fieldstone"),(.11,.37,.47,"metal"),(.39,.32,.40,"glass"),
                         (.46,.39,.47,"ivory"),(.60,.26,.32,"turquoise")],n=(12,7,4)[m.lod])
    m.entry(.45,-.454,.25,.32)
    if m.lod==0:
        for xcoin in (.34,.56):
            m.drum("gold",xcoin,.46,-.36,.053,.033,n=8)


def cathedral(m):
    # An iron drum carries a copper dome over a brick crossing. The long ridge,
    # transept and broad lantern replace the old paired bell-tower facade.
    hall(m,0,-.01,.80,1.53,.71,.41,"gable")
    hall(m,-.60,.22,.49,.61,.49,.30,"gable",door=False)
    hall(m,.57,.22,.46,.59,.49,.28,"gable",door=False)
    x,z=0,.24
    m.profile(x,z,[(.52,.41,.42,"brick"),(.88,.45,.46,"brick"),(1.00,.42,.43,"metal"),
                   (1.09,.48,.49,"trim"),(1.25,.42,.43,"glass"),
                   (1.29,.455,.465,"metal")],n=(12,8,5)[m.lod])
    m.dome((x,1.285,z),.467,m.col("roof_tile"),rings=(4,3,2)[m.lod],
           segments=(14,8,5)[m.lod],squash=.94,material="roof_tile")
    m.profile(x,z,[(1.66,.091,.093,"metal"),(1.76,.086,.086,"gold"),
                   (1.86,.053,.053,"glass"),(1.91,.015,.015,"gold")],n=(8,5,4)[m.lod])
    for side in (-1,1):
        m.horn(side*.68,.66,.22,.72,side)
    m.disk("gold",0,.679,-.798,.113,.036)
    if m.lod < 2:
        for side in (-1,1):
            for pz in (-.51,-.12,.51):
                m.beam("brick",(side*.52,.043,pz),(side*.389,.60,pz),.104)
                if m.lod==0:
                    m.block("glass",side*.40,.34,pz+.109,.034,.20,.076)


def military_base(m):
    # Low signal bunker with an inclined phased-array panel and two recessed
    # landing aprons. Forward posts mark the command entrance between the pads.
    x,z=.02,.37
    m.profile(x,z,[(0,.74,.43,"fieldstone"),(.13,.81,.47,"metal"),(.43,.66,.39,"ivory"),
                   (.50,.72,.43,"metal"),(.65,.58,.33,"glass"),(.72,.43,.27,"ivory")],
              n=(12,7,5)[m.lod])
    m.entry(x,-.027,.37,.38)
    for s in (-1,1):
        m.beam("metal",(x+s*.28,.56,z),(x+s*.24,.995,z+.03),.12)
        m.horn(x+s*.55,.40,z,.71,s)
    with m.transform(translate=(x,1.01,z),rotate_y=-15,rotate_x=-30):
        m.box((0,.03,0),(1.19,.11,.63),m.col("metal"),material="metal",bottom=True,taper=.08)
        m.block("glass",0,.080,0,1.09,.046,.55)
        if m.lod==0:
            for px in (-.40,-.14,.14,.40):
                m.block("turquoise",px,.126,0,.019,.01,.52)
    for s in (-1,1):
        px,pz=s*.49,-.40
        m.profile(px,pz,[(0,.33,.35,"metal"),(.09,.37,.39,"ivory"),(.13,.34,.36,"metal"),
                         (.13,.27,.29,"turquoise"),(.096,.25,.27,"trim")],n=(10,6,4)[m.lod])
        if m.lod<2:
            for dx in (-.069,.069):
                m.block("ivory",px+dx,.097,pz,.029,.012,.18)
            m.block("metal",s*.18,0,-.24,.092,.26,.10,.13)
            m.horn(s*.18,.22,-.24,.32,s)
