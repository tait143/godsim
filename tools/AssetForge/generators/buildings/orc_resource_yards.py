"""Orc resource yards. The equipment follows how each resource is worked."""
import math

from generators.buildings.orc_era_forms import flue, hall, ring, turned, wheel as spoked_wheel


def shaft(m, mat, a, b, radius, end=None):
    dx,dy,dz=[v-u for u,v in zip(a,b)]
    with m.transform(translate=a,rotate_y=math.degrees(math.atan2(dx,dz)),
                     rotate_x=math.degrees(math.atan2(math.hypot(dx,dz),dy))):
        m.cylinder((0,0,0),radius,math.sqrt(dx*dx+dy*dy+dz*dz),m.col(mat),
                   material=mat,top_radius=end,segments=(12,7,4)[m.lod],cap_bottom=True)


def wheel(m,x,y,z,r):
    if m.lod==2:
        shaft(m,"trim",(x,y,z+.08*r),(x,y,z-.08*r),r)
    else:
        spoked_wheel(m,x,y,z,r)


def wall_drive(m,x,height,wheel_z,wall_z,r):
    """Run the shaft from the wheel through a grounded outboard bearing into the engine room."""
    bearing_z=wheel_z+.128
    m.block("fieldstone",x,0,bearing_z,.29,.092,.256,.085)
    m.block("metal",x,.082,bearing_z,.23,height-.143,.177,.12)
    shaft(m,"gold",(x,height,bearing_z-.084),(x,height,bearing_z+.086),.109)
    # The bolted plate straddles the masonry face; the shaft continues through
    # it into the building. Keep the stretch between plate and bearing exposed.
    m.block("metal",x,height-.145,wall_z-.014,.28,.29,.104,.065)
    shaft(m,"trim",(x,height,wheel_z-r*.35),(x,height,wall_z+.14),.17*r)
    wheel(m,x,height,wheel_z,r)
    if m.lod==0:
        for dx in (-.092,.092):
            m.disk("gold",x+dx,height+.087,wall_z-.058,.024,.018)


def rubble(m,x,z,r,h,mat="fieldstone",base=0):
    rows=[(base,.79*r,.68*r,"fieldstone"),(base+.24*h,r,.85*r,mat),
          (base+.70*h,.72*r,.63*r,mat),(base+h,.34*r,.27*r,mat)]
    m.profile(x,z,rows if m.lod==0 else [rows[i] for i in (0,1,3)],n=(7,5,4)[m.lod])


def rails(m,x,z,w,d):
    for dz in ((-.39,-.13,.14,.41) if m.lod == 0 else (-.35,.35)):
        m.block("timber",x,0,z+dz*d,w+.15,.057,.089)
    for side in (-1,1):
        m.block(m.frame,x+side*w*.5,.049,z,.059,.058,d)


def mine(m):
    if m.e == 1:
        # A treadwheel works a shallow shaft behind a dressed portal.
        x,z=-.20,.26
        for pz in (z-.18,z+.18):
            for s in (-1,1):
                m.beam("timber",(x+s*.36,.035,pz),(x+s*.03,.76,pz),.091)
        shaft(m,"trim",(x,.72,z-.32),(x,.72,z+.36),.079)
        wheel(m,x,.72,z-.28,.51)
        wheel(m,x,.72,z+.29,.49)
        for i in range((10,6,3)[m.lod]):
            a=i*math.tau/(10,6,3)[m.lod]
            px,py=x+.46*math.cos(a),.72+.46*math.sin(a)
            m.beam("timber",(px,py,z-.29),(px,py,z+.30),.065)
        hall(m,.47,-.12,.57,.75,.47,.24,"gable")
        rails(m,.47,-.64,.24,.48)
        m.horn(-.67,.63,z-.29,.47,-1)
    elif m.e == 2:
        # The hillside drift feeds an inclined conveyor to a brick screening mill.
        hall(m,-.45,.30,.76,.83,.94,.26,"gable")
        flue(m,-.70,.48,1.50,.126)
        m.block("fieldstone",.51,0,-.28,.66,.32,.70,.16)
        m.entry(.52,-.651,.40,.32)
        m.beam("metal",(.54,.32,-.29),(-.44,.91,.29),.21,.27)
        if m.lod<2:
            for s in (-1,1):
                m.beam("trim",(.57,.38,-.28+s*.107),(-.41,.97,.30+s*.107),.042)
        rubble(m,.38,.50,.25,.30)
        if m.lod==0:
            rubble(m,.68,.35,.16,.20)
    else:
        # A shielded horizontal tunnel-borer docks against the cut face.
        x,z=.21,.16
        m.block("fieldstone",x,0,z+.24,1.25,.44,.60,.13)
        m.block("metal",x,.28,z-.02,1.00,.30,.86,.06)
        shaft(m,"metal",(x,.65,-.28),(x,.65,.58),.47,.43)
        ring(m,"ivory",x,.65,-.31,.51,.365,.13)
        m.disk("door",x,.65,-.337,.375,.075)
        for j in range((6,4,3)[m.lod]):
            ang=j*360/(6,4,3)[m.lod]
            with m.transform(translate=(x,.65,-.424),rotate_z=ang):
                m.beam("turquoise",(.038,.03,0),(.18,.30,0),.085)
                if m.lod==0:
                    m.disk("gold",.15,.26,-.046,.067,.04)
        m.disk("metal",x,.65,-.46,.104,.09)
        hall(m,-.57,.19,.52,.79,.55,.27,"shed")
        for s in (-1,1):
            m.horn(x+s*.40,.56,-.16,.49,s)


def goldmine(m):
    if m.e == 1:
        # A head race tumbles into a sequence of riffled washing tables.
        for j,(x,z,h,w,d) in enumerate(((-.43,.39,.64,.80,.62),(-.08,-.04,.43,.69,.60),
                                        (.34,-.43,.22,.73,.63))):
            m.block("wall",x,0,z,w,h,d,.053)
            m.block("water",x,h,z,w*.88,.031,d*.86)
            if m.lod < 2:
                for s in (-1,1):
                    m.block("timber",x+s*w*.445,h-.012,z,.066,.115,d)
            if m.lod<2:
                for dz in (-.14,.035,.18):
                    m.block("trim",x,h+.030,z+dz,w*.77,.032,.032)
            if j!=2:
                m.beam("water",(x+.22,h+.028,z-.22),(x+.39,h-.193,z-.41),.145)
        for s in (-1,1):
            m.horn(-.43+s*.29,.64,.52,.63,s)
        rubble(m,.62,.32,.26,.32,"gold")
        if m.lod == 0:
            rubble(m,.35,.56,.18,.23)
            m.jar(.73,0,-.11,.115,.21,"gold")
    elif m.e == 2:
        # Drop stamps crush ore against the mortar box beneath an overhead camshaft.
        hall(m,-.50,.27,.69,.74,.58,.22,"gable")
        flue(m,-.65,.47,1.19,.136)
        m.block("fieldstone",.28,0,-.19,1.03,.19,.52,.065)
        for x in (-.19,.76):
            m.block("metal",x,.184,-.185,.115,.90,.26)
            m.horn(x,.98,-.18,.38,-1 if x<0 else 1)
        shaft(m,"metal",(-.24,.97,-.19),(.79,.97,-.19),.078)
        for i,x in enumerate((-.035,.23,.495)):
            y=.31+.09*(i%2)
            m.block("trim",x,y,-.265,.16,.18,.21,.07)
            m.block("metal",x,y+.15,-.236,.083,.91-y,.092)
            m.disk("gold",x,.97,-.207,.102,.095)
        wall_drive(m,-.692,.48,-.504,-.10,.294)
        rubble(m,.41,.46,.28,.30,"gold")
        if m.lod == 0:
            rubble(m,.66,.60,.14,.19,"gold")
    else:
        # A gravity spiral descends around an ore feed mast into the collection basin.
        x,z=.15,.18
        turned(m,x,z,[(0,.51,"metal"),(.10,.56,"ivory"),(.30,.52,"glass"),(.32,.43,"gold"),
                      (.19,.35,"trim")])
        turned(m,x,z,[(.18,.14,"metal"),(1.19,.14,"metal"),(1.33,.41,"ivory"),
                      (1.48,.43,"gold"),(1.50,.35,"metal"),(1.34,.18,"door")])
        # Trapezoidal trough sections share vertices around the two-turn helix.
        n=(32,18,10)[m.lod]
        sections=[]
        for i in range(n+1):
            a=math.tau*(1.8*i/n+.18)
            y=1.23-.94*i/n
            sections.append([(x+r*math.cos(a),y+h,z+r*math.sin(a)) for r,h in
                             ((.125,0),(.43,.034),(.448,.100),(.485,.100),(.472,-.014),(.125,-.051))])
        for a,b in zip(sections,sections[1:]):
            for j in range(6):
                k=(j+1)%6
                m.face("gold" if j<2 else "metal",(a[j],b[j],b[k],a[k]))
        # The section is concave at the raised outer lip. Split each exposed end.
        for row,rev in ((sections[0],False),(sections[-1],True)):
            for ids in ((0,1,4,5),(1,2,3,4)):
                f=[row[i] for i in ids]
                m.face("metal",f[::-1] if rev else f)
        hall(m,-.58,-.32,.51,.60,.47,.17,"shed")
        for s in (-1,1):
            m.horn(x+s*.32,1.40,z,.47,s)


def split_stone(m,x,z,w,d,h):
    m.block("fieldstone",x,0,z,w,h,d,.035)
    if m.lod == 0:
        for side in (-1,1):
            m.block("trim",x+side*w*.22,h-.052,z-d*.47,.037,.074,.060,.16)


def quarry(m):
    if m.e == 1:
        # Three lashed spars lift a cut block by a two-legged stone sling.
        x,z=.14,-.13
        for px,pz in ((-.57,-.57),(.74,-.43),(.17,.62)):
            m.block("fieldstone",px,0,pz,.20,.089,.20,.14)
            shaft(m,"timber",(px,.065,pz),(x,1.33,z),.086,.060)
        m.jar(x,1.25,z,.135,.17,"hide")
        for s in (-1,1):
            m.beam("trim",(x,1.30,z),(x+s*.17,.51,z),.035)
        m.box((x,.40,z),(.50,.24,.33),m.col("fieldstone"),material="fieldstone",bottom=True)
        split_stone(m,-.53,.34,.58,.52,.33)
        if m.lod < 2:
            split_stone(m,.61,.37,.41,.51,.19)
        m.horn(x,1.34,z,.56,1)
        if m.lod < 2:
            # The ground-level winding drum takes the load of the hoist line.
            for px in (-.70,-.44):
                m.block("timber",px,0,-.52,.087,.30,.16,.05)
            shaft(m,"hide",(-.73,.27,-.52),(-.40,.27,-.52),.106)
            m.beam("trim",(-.55,.30,-.51),(x,1.32,z),.027)
            if m.lod==0:
                shaft(m,"gold",(-.42,.27,-.52),(-.34,.27,-.52),.04)
                m.beam("timber",(-.34,.27,-.52),(-.34,.42,-.51),.052)
    elif m.e == 2:
        # Steam-driven rail crane on its turntable, balanced by a rear boiler.
        x,z=-.28,.20
        rails(m,x,z,.49,1.31)
        m.block("metal",x,.101,z,.81,.20,.84,.06)
        turned(m,x,z,[(.298,.35,"metal"),(.39,.32,"trim"),(.435,.35,"metal")])
        m.block("brick",x-.09,.43,z+.10,.54,.35,.46,.06)
        shaft(m,"metal",(x-.08,.64,z),(x+.77,1.21,z-.38),.11,.065)
        m.beam("trim",(x-.22,.73,z+.27),(x+.72,1.225,z-.33),.057)
        m.beam("metal",(x+.75,1.20,z-.37),(x+.75,.47,z-.37),.030)
        m.box((x+.75,.35,z-.37),(.39,.23,.36),m.col("fieldstone"),material="fieldstone",bottom=True)
        flue(m,x-.14,z+.30,1.15,.104)
        m.horn(x-.22,.78,z+.16,.51,-1)
        split_stone(m,.51,.49,.55,.45,.22)
        if m.lod==0:
            wheel(m,x+.17,.52,z-.12,.146)
    else:
        # Rail-mounted diamond-wire bridge straddles a stepped cut.
        for s in (-1,1):
            x=s*.60
            m.block("metal",x,0,.022,.21,.113,1.59,.05)
            m.block("ivory",x,.107,.17,.29,.20,.68,.06)
            m.beam("metal",(x,.29,.18),(x+s*.095,1.18,.22),.16,.23)
            m.horn(x+s*.09,1.13,.22,.56,s)
        m.box((0,1.19,.22),(1.63,.18,.30),m.col("ivory"),material="ivory",bottom=True)
        for x,y in ((-.36,.96),(.36,.76)):
            m.block("metal",x,y,.21,.119,1.22-y,.169)
            wheel(m,x,y,.123,.147)
        m.beam("gold",(-.36,.834,.115),(.36,.632,.115),.028)
        m.block("fieldstone",-.08,0,.02,.82,.43,.86,.06)
        split_stone(m,.16,-.57,.64,.35,.21)
        if m.lod==0:
            m.block("turquoise",.14,1.12,.054,.31,.081,.052)


def coalmine(m):
    # Bucket wheel reclaiming a coal seam. The teeth and the conveyor are
    # supported by a counterweighted crawler, set directly against the bank.
    hall(m,-.48,.37,.71,.70,.46,.18,"shed")
    m.block("metal",.03,0,-.15,.82,.22,.59,.10)
    turned(m,.03,-.15,[(.21,.27,"metal"),(.40,.25,"ivory"),(.47,.23,"trim")])
    m.beam("metal",(-.18,.37,-.075),(.51,.78,-.42),.162)
    m.beam("ivory",(-.20,.59,-.05),(.49,.80,-.42),.088)
    m.block("metal",-.19,.29,-.075,.28,.31,.32,.10)
    wheel(m,.50,.77,-.43,.36)
    for j in range((9,6,4)[m.lod]):
        with m.transform(translate=(.50,.77,-.43),rotate_z=360*j/(9,6,4)[m.lod]):
            m.box((.037,.352,-.012),(.126,.133,.153),m.col("metal"),material="metal",bottom=True,taper=.21)
    rubble(m,.44,.39,.40,.50,"door")
    rubble(m,.70,-.02,.25,.26,"door")
    if m.lod==0:
        rubble(m,.11,.62,.19,.22,"door")
    m.horn(-.49,.46,.36,.60,-1)


def log(m,x,y,z,length,r):
    shaft(m,"timber",(x,y,z+length*.5),(x,y,z-length*.5),r,r*.91)
    if m.lod==0:
        m.disk("hide",x,y,z-length*.5-.002,r*.77,.012)


def sawmill(m):
    # An enclosed band-saw portal stands astride the powered log carriage.
    x,z=-.18,.16
    m.block("metal",x,0,z,.79,.18,1.52,.06)
    for px in (-.59,.23):
        m.block("ivory",px,0,.27,.24,.24,.37,.07)
        m.beam("metal",(px,.22,.28),(px,.97,.24),.143,.20)
        m.horn(px,.96,.24,.53,-1 if px<0 else 1)
    m.box((x,1.02,.25),(1.05,.25,.34),m.col("ivory"),material="ivory",bottom=True,taper=.10)
    m.block("gold",-.27,.17,.21,.036,.79,.11)
    log(m,-.15,.30,.02,1.27,.135)
    for dx,y,dz in ((0,.16,.12),(.27,.15,-.03),(.13,.38,.04)):
        log(m,.48+dx,y,dz,1.03,.15)
    if m.lod<2:
        for pz in (-.40,-.04,.38):
            shaft(m,"trim",(-.52,.206,pz),(.13,.206,pz),.049)
    if m.lod==0:
        m.disk("turquoise",-.18,1.056,.064,.085,.035)


def splitting_yard(m):
    # Two timber bucks hold a trunk clear of the ground. Their crossed jaws
    # keep it from rolling while the sawyers work the tensioned frame saw.
    log_z=.12
    for x in (-.52,.48):
        if m.lod==2:
            m.block("fieldstone",x,0,log_z,.22,.097,.71,.12)
        for side in (-1,1):
            px=x+side*.026
            if m.lod<2:
                m.block("fieldstone",px,0,log_z+side*.285,.205,.097,.19,.12)
            m.beam("timber",(px,.083,log_z+side*.285),(px,.934,log_z-side*.218),.143,.130)
        if m.lod<2:
            m.disk("gold",x,.56,log_z-.027,.068,.044)
        m.horn(x,.87,log_z+.188,.43,-1 if x<0 else 1)
    m.beam("trim",(-.58,.475,log_z-.045),(.56,.475,log_z-.045),.092)
    shaft(m,"timber",(-.85,.796,log_z),(.81,.796,log_z),.213,.179)
    if m.lod<2:
        shaft(m,"hide",(.802,.796,log_z),(.831,.796,log_z),.143)
    # The steel blade crosses the kerf beneath the timber spreader. The high
    # tension cord leaves a large clear opening in the frame, even in silhouette.
    for pz,y in ((-.26,1.32),(.46,1.46)):
        m.beam("timber",(.088,.60+(y-1.32),pz),(.068,y,pz+.053),.077)
    m.beam("metal",(.088,.807,-.25),(.082,.949,.46),.049,.105)
    m.beam("hide",(.071,1.288,-.209),(.068,1.429,.505),.039)
    m.beam("timber",(.077,1.092,-.228),(.076,1.230,.491),.083)
    if m.lod==0:
        for pz,y in ((-.23,1.08),(.49,1.21)):
            m.block("cloth",.078,y,pz,.094,.091,.095)
    # A crib at the back catches the next trunks. Staggered cut ends show how
    # the pile is seated against its uprights.
    for x in (-.64,.56):
        m.block("timber",x,0,.84,.104,.45,.11,.085)
    for xx,yy,zz,rad,length in ((-.04,.113,.59,.113,1.36),(.07,.109,.77,.109,1.30),
                                 (-.02,.265,.67,.115,1.46)):
        shaft(m,"timber",(xx-length*.5,yy,zz),(xx+length*.5,yy,zz),rad,rad*.93)
    # Cut lengths are loaded into a low cart on the front approach.
    cart_x,cart_z=-.09,-.56
    for s in (-1,1):
        pz=cart_z+s*.216
        wheel(m,cart_x,.173,pz,.167)
        m.block("fieldstone",cart_x,0,pz,.149,.048,.093,.22)
    shaft(m,"trim",(cart_x,.172,cart_z-.254),(cart_x,.172,cart_z+.253),.050)
    m.box((cart_x,.235,cart_z),(.66,.102,.36),m.col("timber"),material="timber",bottom=True)
    for s in (-1,1):
        m.block("trim",cart_x,.278,cart_z+s*.145,.67,.115,.062)
    if m.lod==2:
        m.block("hide",-.10,.282,cart_z,.53,.104,.22,.10)
    else:
        for xx in (-.31,-.10,.11):
            m.block("hide",xx,.282,cart_z,.125,.104,.22,.10)
    m.beam("timber",(cart_x+.18,.216,cart_z),(.65,.14,cart_z+.023),.082)
    m.block("timber",.58,0,cart_z+.018,.09,.17,.11,.09)


def timber_camp(m):
    if m.e==2:
        # A skid-mounted steam donkey winches felled trunks toward the drum.
        for x in (-.63,-.13):
            m.block("timber",x,0,.14,.16,.123,1.46,.11)
        m.block("metal",-.38,.118,.15,.68,.132,1.12,.045)
        turned(m,-.38,.37,[(.248,.22,"metal"),(.34,.27,"brick"),(.61,.26,"metal"),
                            (.71,.20,"metal"),(.77,.12,"trim"),(1.11,.095,"metal"),
                            (1.17,.13,"trim"),(1.17,.078,"trim"),(1.09,.071,"door")])
        for x in (-.62,-.14):
            m.block("metal",x,.245,-.18,.113,.28,.25,.14)
        shaft(m,"hide",(-.64,.47,-.18),(-.11,.47,-.18),.155)
        wheel(m,-.39,.47,-.31,.213)
        for x in (.28,.60):
            log(m,x,.152,-.04,1.34,.152)
        m.beam("trim",(-.26,.44,-.22),(.30,.16,-.38),.027)
        m.horn(-.56,.58,.37,.54,-1)
    else:
        # Articulated felling boom folds over the drive cab. The open grab
        # closes around a trunk alongside the central tracked carrier.
        for x in (-.65,-.22):
            m.block("metal",x,0,.14,.22,.23,.92,.16)
        m.block("ivory",-.435,.21,.18,.65,.19,.78,.08)
        m.profile(-.44,.35,[(.39,.28,.29,"metal"),(.51,.32,.32,"ivory"),(.77,.27,.26,"glass"),
                            (.84,.30,.29,"metal"),(.895,.21,.21,"ivory")],n=(10,6,4)[m.lod])
        m.beam("metal",(-.27,.38,-.073),(.12,1.13,.04),.14)
        m.beam("ivory",(.12,1.13,.04),(.55,.75,-.31),.13)
        shaft(m,"gold",(.55,.75,-.31),(.55,.46,-.32),.063)
        for s in (-1,1):
            jaw_z=-.32+s*.03
            m.beam("metal",(.55,.48,jaw_z),(.55+s*.19,.28,jaw_z),.087)
            m.beam("ivory",(.55+s*.19,.28,jaw_z),(.55+s*.095,.12,jaw_z),.063)
        log(m,.55,.135,-.13,1.25,.135)
        m.horn(-.60,.76,.36,.57,-1)
        if m.lod<2:
            m.beam("gold",(-.17,.45,-.09),(.18,1.01,.035),.051)
        if m.lod==0:
            for px in (-.65,-.22):
                for pz in (-.15,.13,.44):
                    shaft(m,"trim",(px-.128,.125,pz),(px+.128,.125,pz),.086)
            for px,py,pz in ((-.27,.43,-.073),(.12,1.10,.04),(.55,.70,-.31)):
                m.disk("gold",px,py,pz-.073,.072,.045)


DESIGNS={
    ("CoalMine","future"):coalmine, ("Sawmill","future"):sawmill,
    ("WoodcutterCamp","classical"):splitting_yard,
    **{("WoodcutterCamp",era):timber_camp for era in ("industrial","future")},
    **{(name,era):fn for name,fn in (("Mine",mine),("GoldMine",goldmine),("StoneQuarry",quarry))
       for era in ("classical","industrial","future")},
}
