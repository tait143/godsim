"""Later Orc foundry, textile and machine-shop structures."""
from generators.buildings.orc_era_forms import flue, hall
from generators.buildings.orc_resource_yards import shaft, wall_drive


def foundry(m):
    # Suspended electrodes descend into the open crucible. Cast metal exits
    # the tapping lip into a straight cooling bed in front of the furnace.
    x,z=-.15,.22
    m.profile(x,z,[(0,.39,.40,"fieldstone"),(.14,.46,.47,"metal"),(.32,.40,.42,"trim"),
                   (.43,.57,.55,"ivory"),(.69,.58,.56,"metal"),(.77,.62,.59,"ivory"),
                   (.79,.49,.46,"trim"),(.59,.43,.40,"forge")],n=(14,8,5)[m.lod])
    m.block("metal",.50,0,.53,.33,.19,.36,.10)
    m.block("ivory",.50,.18,.53,.20,1.11,.23,.10)
    m.box((.04,1.275,.40),(1.14,.126,.47),m.col("metal"),material="metal",bottom=True)
    for dx,dz in ((-.27,.07),(0,-.08),(.24,.09)):
        px,pz=x+dx,.30+dz
        shaft(m,"trim",(px,1.32,pz),(px,.76,pz),.066)
        if m.lod<2:
            shaft(m,"gold",(px,1.22,pz),(px,1.05,pz),.093)
    for s in (-1,1):
        m.horn(x+s*.45,.68,z,.61,s)
    m.block("metal",-.09,0,-.56,.76,.22,.47,.10)
    for px in (-.29,-.08,.13):
        m.block("forge",px,.216,-.56,.141,.027,.35,.08)
    m.beam("trim",(-.13,.62,-.18),(-.10,.24,-.50),.158)
    if m.lod==0:
        m.jar(.57,0,-.25,.13,.24,"metal")


def weaver(m):
    # A two-bay north-light shed houses the powered looms. A hoisted external
    # drying rail holds individual bolts beside the brick engine-room chimney.
    x,z=-.12,.26
    m.block("fieldstone",x,0,z,1.27,.13,.93,.05)
    m.block("brick",x,.125,z,1.20,.48,.86)
    for dz in (-.22,.23):
        m.wedge((x,.60,z+dz),1.30,.45,.28,m.col("metal"),material="metal")
        m.block("glass",x,.663,z+dz+.218,1.18,.15,.036)
        if m.lod==0:
            for dx in (-.41,-.14,.14,.41):
                m.block("trim",x+dx,.656,z+dz+.24,.023,.17,.025)
    m.entry(-.27,-.21,.332,.42)
    flue(m,.61,.43,1.33,.127)
    for s in (-1,1):
        px=s*.60
        m.block("metal",px,0,-.51,.153,.12,.19,.09)
        m.block("metal",px,.113,-.51,.085,.60,.087)
        m.horn(px,.67,-.51,.46,s)
    shaft(m,"trim",(-.67,.71,-.51),(.67,.71,-.51),.061)
    for i,px in enumerate((-.35,.01,.35)):
        z0=-.52-.03*(i%2)
        m.box((px,.489,z0),(.235,.41,.051),m.col("ivory" if i==1 else "cloth"),
              material="ivory" if i==1 else "cloth",bottom=True)
        if m.lod==0:
            for h in (.35,.42):
                m.block("gold",px,h,z0-.031,.216,.025,.018)
    if m.lod<2:
        m.jar(.59,0,-.11,.105,.19,"hide")


def workshop(m):
    # An open-sided power hammer drives a broad ram against its die. The
    # adjacent machine shop carries the line shaft and an exposed flywheel.
    hall(m,-.50,.28,.64,.98,.61,.29,"gable")
    x,z=.32,-.13
    m.block("fieldstone",x,0,z,.86,.19,.76,.065)
    for side in (-1,1):
        px=x+side*.31
        m.block("metal",px,.184,z,.185,.16,.39,.09)
        m.beam("metal",(px,.31,z),(px+side*.08,1.11,z+.045),.130,.19)
        m.horn(px+side*.068,1.13,z+.045,.51,side)
    m.box((x,1.14,z+.04),(.96,.19,.33),m.col("metal"),material="metal",bottom=True,taper=.08)
    m.profile(x,z,[(.18,.22,.18,"trim"),(.29,.24,.20,"metal"),(.36,.18,.14,"ivory")],
              n=(8,6,4)[m.lod])
    m.block("trim",x,.52,z,.25,.23,.22,.07)
    shaft(m,"metal",(x,1.23,z),(x,.74,z),.078)
    wall_drive(m,-.667,.49,-.635,-.21,.235)
    if m.lod<2:
        shaft(m,"trim",(-.49,.57,.04),(.10,.93,-.080),.049)
        if m.lod==0:
            m.jar(-.41,0,-.56,.106,.18,"metal")


DESIGNS={("Foundry","future"):foundry, ("Weaver","industrial"):weaver,
         ("Workshop","industrial"):workshop}
