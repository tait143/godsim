"""Orc craft traditions shared by the later guilds, shrines and public works."""
import math


def turned(m, x, z, rows, flatten=1):
    """Revolve a section whose final face closes the solid (or the bottom of a bowl)."""
    m.profile(x, z, [(y, r, r * flatten, mat) for y, r, mat in rows],
              n=(12, 7, 4)[m.lod])


def hall(m, x, z, w, d, h, rise, roof="gable", door=True):
    """Clad, framed range with rain-shedding eaves and a door facing the approach."""
    m.block("fieldstone", x, 0, z, w*1.04, .105, d*1.04, .025)
    m.block(m.cladding, x, .102, z, w, h-.102, d)
    mat = ("hide", "roof_tile", "metal", "glass")[m.e]
    roof_fn = {"gable":m.prism_x, "hip":m.hip_roof, "shed":m.wedge}[roof]
    roof_fn((x,h,z), w+.086, d+.09, rise, m.col(mat), material=mat)
    if door:
        m.entry(x, z-d/2-.029, min(w*.44,.36), min(h*.86,.45))
    if m.lod<2:
        for s in (-1,1):
            m.horn(x+s*w*.43,h-.047,z-.30*d,min(.48,w*.61),s)
    if m.lod==0:
        # Splayed kick braces and inset lights on the side walls.
        for s in (-1,1):
            m.beam(m.frame,(x+s*(w*.5+.048),.032,z+.23*d),
                   (x+s*w*.487,h-.014,z+.29*d),.060)
            m.block("emissive" if m.e<2 else "glass",x+s*w*.50,.25*h,z-.04*d,
                    .032,.41*h,.25*d)
            m.block("cloth",x+s*w*.509,.70*h,z-.035*d,.043,.066*h,.28*d)


def pot(m, x, z, r, h, base=0, plant=False):
    """Terraced lip around a visible surface below the rim."""
    rows = [(base,.80*r,"fieldstone"),(base+.14*h,r,"trim"),
            (base+.75*h,.92*r,m.cladding),(base+.90*h,1.06*r,m.frame),
            (base+h,1.03*r,"ivory"),(base+h,.80*r,"ivory"),
            (base+.67*h,.73*r,"soil" if plant else "water")]
    if m.lod==2:
        rows=[rows[i] for i in (0,3,5,6)]
    turned(m,x,z,rows)
    if plant:
        for i in range((6,4,3)[m.lod]):
            with m.transform(translate=(x,base+.65*h,z),rotate_y=i*137,rotate_z=28+i%2*22):
                m.leaf(0,0,0,r*(1.65+i%3*.24))
    if m.lod==0:
        for side in (-1,1):
            m.horn(x+side*.85*r,base+h*.49,z+.10*r,r*.82,side)


def flue(m,x,z,h,r):
    rows=[(0,r*1.37,"fieldstone"),(.16*h,r*1.32,"brick"),(.28*h,r,"brick"),
          (.73*h,r*.72,"brick"),(.77*h,r*.78,"metal"),(.81*h,r*.72,"metal"),
          (.94*h,r*.70,"brick"),(.96*h,r*.95,"metal"),(h,r*.98,"trim"),
          (h,r*.58,"metal"),(.915*h,r*.54,"door")]
    if m.lod==1:
        rows=[rows[i] for i in (0,1,2,6,7,8,9,10)]
    if m.lod==2:
        rows=[rows[i] for i in (0,2,6,8,10)]
    turned(m,x,z,rows)


def ring(m, mat, x, y, z, outer, inner, depth, tilt=0, arch=False):
    """A thick circular frame facing the street, with an open center."""
    n=(16,10,6)[m.lod]
    with m.transform(translate=(x,y,z),rotate_x=tilt):
        def pt(radius, i, dz):
            a=math.tau*i/n
            return (radius*math.cos(a),radius*math.sin(a),dz)
        for i in range(n//2 if arch else n):
            j=i+1
            a,b,c,d=[pt(r,k,-depth/2) for r,k in ((inner,i),(inner,j),(outer,j),(outer,i))]
            e,f,g,h=[(u,v,w+depth) for u,v,w in (a,b,c,d)]
            m.face(mat,[a,b,c,d])
            m.face(mat,[h,g,f,e])
            m.face(mat,[d,c,g,h])
            m.face(mat,[b,a,e,f])
            if arch and i == 0:
                m.face(mat,[a,d,h,e])
            if arch and j == n//2:
                m.face(mat,[c,b,f,g])


def wheel(m, x,y,z,r):
    ring(m,"trim",x,y,z,r,.73*r,.15*r)
    for i in range((4,3,2)[m.lod]):
        a=math.pi*i/(4,3,2)[m.lod]
        dx,dy=.86*r*math.cos(a),.86*r*math.sin(a)
        # A staggered hub sandwiches the cast spokes.
        m.beam("gold",(x-dx,y-dy,z+i*.003),(x+dx,y+dy,z+i*.003),.105*r)
    m.disk("metal",x,y,z-.06*r,.19*r,.23*r)


def well_pump(m):
    if m.e==1:
        # A Roman-era open pillared fountain. Water rises from the central carved spout.
        pot(m,0,-.17,.59,.34)
        turned(m,0,.06,[(.18,.16,"fieldstone"),(.44,.14,"plaster"),(.54,.23,"gold"),
                         (.61,.19,"ivory"),(.63,.13,"water")])
        for s in (-1,1):
            turned(m,s*.57,.24,[(0,.19,"fieldstone"),(.12,.16,"plaster"),
                                 (.68,.094,"plaster"),(.75,.14,"trim")])
            m.horn(s*.57,.68,.24,.69,s)
        m.block("fieldstone",0,.73,.24,1.32,.14,.27,.04)
        m.prism_x((0,.87,.24),1.39,.35,.26,m.col("roof_tile"),material="roof_tile")
    elif m.e==2:
        # A walking beam pivots on the brick engine house and pumps into the open curb.
        hall(m,-.37,.25,.61,.64,.53,.17,"shed")
        pot(m,.41,-.28,.34,.37)
        m.block("metal",-.11,.43,.20,.17,.47,.19,.17)
        m.beam("trim",(-.55,.71,.18),(.47,1.04,-.15),.113)
        m.beam("metal",(.47,1.03,-.15),(.44,.34,-.18),.051)
        wheel(m,-.51,.49,-.12,.31)
        flue(m,-.68,.48,1.13,.122)
    else:
        # Gravity-fed reservoir: the closed elevated tank feeds two low drinking cisterns.
        turned(m,0,.23,[(0,.39,"metal"),(.16,.40,"ivory"),(.34,.21,"metal"),
                          (.74,.175,"metal"),(.83,.39,"turquoise"),(1.02,.43,"glass"),
                          (1.22,.30,"ivory"),(1.26,.15,"turquoise")])
        for s in (-1,1):
            x=s*.47
            pot(m,x,-.30,.31,.30)
            m.beam("metal",(s*.13,.68,.17),(x,.51,-.18),.075)
            m.beam("ivory",(x,.51,-.18),(x,.31,-.20),.065)
            m.horn(s*.30,.91,.22,.62,s)


def clean_power(m):
    # Generator hall and two wind machines. The hubs sit atop tapered grounded masts.
    hall(m,-.34,-.24,.92,.76,.47,.23,"shed")
    for x,z,h,r,turn in ((.38,.36,1.27,.47,9),(-.55,.55,.92,.32,-23)):
        turned(m,x,z,[(0,.19,"fieldstone"),(.12,.18,"metal"),(.27,.116,"ivory"),
                      (h,.058,"metal")])
        m.disk("metal",x,h,z-.08,.092,.17)
        for k in range(3):
            with m.transform(translate=(x,h,z-.085),rotate_z=turn+k*120):
                shape=[(-.057*r,.11*r),(-.11*r,.33*r),(-.034*r,.81*r),(.105*r,r),
                       (.15*r,.61*r),(.074*r,.16*r)]
                if m.lod==2:
                    shape=[shape[i] for i in (0,1,3,4)]
                # A thick tapered blade with a swept tip.
                fore=[(u,v,-.028) for u,v in shape]
                back=[(u,v,.028) for u,v in shape]
                for i in range(len(shape)):
                    j=(i+1)%len(shape)
                    m.face("ivory",[fore[j],fore[i],back[i],back[j]])
                m.face("ivory",fore)
                m.face("metal",back[::-1])
        m.disk("gold",x,h,z-.137,.079,.055)
    if m.lod<2:
        for s in (-1,1):
            m.horn(-.34+s*.35,.47,-.32,.51,s)
    if m.lod==0:
        for x,z,h,r in ((.38,.36,1.27,.47),(-.55,.55,.92,.32)):
            turned(m,x,z,[(.13,.18,"metal"),(.17,.16,"cloth"),(.19,.14,"trim")])


def skyrail(m):
    # The guideway pier and station island have continuous approaches at each side.
    for x in (-.72,.71):
        m.block("fieldstone",x,0,-.38,.32,.09,.36,.08)
        m.block("metal",x,.085,-.38,.17,.34,.25,.20)
        m.block("ivory",x,.40,-.38,.28,.09,.32,.08)
    m.block("metal",0,.485,-.38,1.99,.105,.23,.055)
    m.block("turquoise",0,.59,-.38,2.00,.039,.18)
    # The rounded cab wraps around the guideway.
    with m.transform(translate=(-.23,0,-.38),rotate_y=90):
        m.profile(0,0,[(.61,.17,.56,"metal"),(.71,.22,.64,"ivory"),
                       (.88,.20,.62,"glass"),(.99,.142,.53,"glass"),(1.03,.08,.40,"ivory")],
                  n=(14,8,4)[m.lod])
    turned(m,.37,.35,[(0,.42,"fieldstone"),(.15,.38,"metal"),(.56,.29,"glass"),
                       (.63,.46,"ivory"),(.73,.49,"turquoise"),(.88,.21,"glass")],.81)
    m.entry(.37,.035,.30,.44)
    for x in ((-.70,-.12,.59) if m.lod<2 else (-.70,.59)):
        m.beam("metal",(x,.05,.18),(x,.61,.075),.10)
    m.block("ivory",-.04,.60,.105,1.65,.10,.53,.04)
    for x in (-.59,.49):
        m.beam("metal",(x,.69,.29),(x+.13,1.05,.34),.118)
        if m.lod<2:
            m.horn(x+.12,1.00,.34,.42,1)
    m.profile(-.05,.30,[(1.025,.84,.39,"metal"),(1.085,.88,.44,"ivory"),
                         (1.18,.62,.26,"glass"),(1.22,.41,.14,"turquoise")],
              n=(12,7,4)[m.lod])
    if m.lod==0:
        for k in range(4):
            m.block("trim",-.71,k*.13,.63-k*.09,.30,.13,.15)
        for x in (-.55,-.31,-.07):
            m.block("ivory",x,.75,-.58,.028,.145,.037)
        for x in (-.72,.71):
            turned(m,x,-.38,[(.09,.137,"trim"),(.16,.121,"cloth"),(.19,.105,"metal")])


def performance_hall(m):
    if m.e==2:
        # The fly tower actually houses the curtain and lights above a timber stage.
        hall(m,-.52,.30,.60,.88,.78,.29,"gable")
        m.block("brick",.07,0,.51,.86,1.13,.35,.09)
        m.block("metal",.07,1.105,.50,1.08,.12,.48,.07)
        m.prism_x((.07,1.225,.50),1.13,.51,.20,m.col("metal"),material="metal")
        m.block("timber",.08,0,.08,1.15,.20,.60,.05)
        for s in (-1,1):
            m.block("cloth",.07+s*.355,.20,.288,.19,.85,.108,.18)
            m.horn(.07+s*.42,1.16,.49,.52,s)
        m.disk("gold",.10,.74,.31,.17,.04)
        for i in range((3,2,1)[m.lod]):
            z=-.28-i*.195
            m.block("wall",.03,0,z,1.39,.13+i*.088,.142,.08)
            if m.lod==0:
                m.block("hide",.03,.12+i*.088,z,1.40,.055,.15)
    else:
        # Concentric acoustic ribs stand over the fan stage. The shell is open to the audience.
        m.profile(0,.12,[(0,.83,.59,"fieldstone"),(.13,.89,.62,"metal"),
                          (.21,.83,.57,"ivory")])
        for j in ((0,1,2) if m.lod==0 else (0,2)):
            z=.48-j*.18
            radius=.59+j*.101
            ring(m,"ivory" if j!=1 else "turquoise",0,.26,z,radius,.83*radius,.095,
                 tilt=-12-j*6,arch=True)
        for s in (-1,1):
            m.block("metal",s*.70,0,.22,.22,.34,.48,.07)
            if m.lod<2:
                m.horn(s*.69,.30,.11,.65,s)
        m.block("door",0,.095,-.47,.34,.13,.07)
        for s in (-1,1):
            m.profile(s*.46,-.54,[(0,.43,.25,"fieldstone"),(.15,.42,.24,"metal"),
                                  (.22,.37,.20,"ivory")],n=(9,6,4)[m.lod])
        if m.lod<2:
            m.disk("gold",0,.47,.51,.134,.065)
            m.block("metal",0,.20,.53,.095,.29,.09)
        if m.lod==0:
            for s in (-1,1):
                for k in (-1,0,1):
                    turned(m,s*.46+.16*k,-.54,[(.216,.061,"metal"),(.254,.073,"cloth"),
                                                (.28,.067,"hide")])
