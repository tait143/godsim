"""Crop, herd and fish production in the Orc settlements."""
import math

from generators.buildings.orc_era_forms import flue, hall, pot, turned
from generators.buildings.orc_resource_yards import shaft, wheel


def crop_bed(m,x,z,w,d,base=0):
    m.block("soil",x,base,z,w,.086,d,.12)
    count=(5,3,2)[m.lod]
    for i in range(count):
        cx=x+w*.72*((i+.5)/count-.5)
        with m.transform(translate=(cx,base+.071,z),scale=(1.,.86,1.54)):
            m.dome((0,0,0),min(.105,.80*w/count),m.col("leaf"),rings=(2,1,1)[m.lod],
                   segments=(7,5,4)[m.lod],material="leaf")


def farm(m):
    if m.e == 1:
        # Irrigated vineyard on descending terraces. The highest terrace carries
        # lashed posts and an open arbor above the leaves.
        for j,(z,height) in enumerate(((.49,.38),(-.02,.22),(-.54,.09))):
            x=.04-.08*j
            m.block("fieldstone",x,0,z,1.47-j*.05,height,.44,.06)
            crop_bed(m,x,z,1.39-j*.054,.38,height)
            m.block("water",x,height+.017,z-.201,1.32,.038,.039)
            if j==0:
                for px in (-.55,.63):
                    for pz in (.33,.65):
                        m.block("timber",px,height,pz,.080,.63,.084)
                for pz in (.33,.65):
                    m.beam("timber",(-.64,1.009,pz),(.70,1.009,pz),.078)
                for px in ((-.51,-.13,.24,.59) if m.lod<2 else (-.51,.59)):
                    m.beam("trim",(px,1.063,.23),(px,1.063,.76),.063)
                for s in (-1,1):
                    m.horn(.03+s*.58,.999,.65,.53,s)
                if m.lod<2:
                    for px in (-.40,.04,.45):
                        m.jar(px,.85,.344,.075,.16,"leaf")
    else:
        # Two hydroponic galleries flank the nutrient reservoir. Sloping racks
        # admit light from the glazed outer edges at every level.
        for side in (-1,1):
            for pz in (-.55,.55):
                m.beam("metal",(side*.76,.067,pz),(side*.32,1.00,pz),.126)
                m.block("ivory",side*.72,0,pz,.18,.12,.18,.10)
            # An outrigger under the upper tray puts the support silhouette
            # outside the foliage. The diagonal locks the long frame against sway.
            m.beam("metal",(side*.79,.105,-.55),(side*.50,.834,.55),.083)
            for j in range(3):
                y=.15+.333*j
                x=side*(.68-.137*j)
                m.box((x,y,.00),(.44,.072,1.15),m.col("ivory"),material="ivory",bottom=True)
                with m.transform(translate=(x,y+.032,0),rotate_y=90):
                    if m.lod==2 and j==1:
                        m.block("leaf",0,0,0,1.02,.10,.29,.19)
                    else:
                        crop_bed(m,0,0,1.07,.38)
            m.horn(side*.32,.95,.55,.49,side)
        m.beam("ivory",(-.35,.99,.55),(.35,.99,.55),.107)
        turned(m,.03,.60,[(0,.22,"metal"),(.10,.25,"ivory"),(.62,.20,"glass"),
                            (.69,.24,"turquoise"),(.83,.12,"metal")])
        if m.lod<2:
            for s in (-1,1):
                m.beam("metal",(.03,.43,.60),(s*.53,.46,.41),.048)


def windpump_farm(m):
    # A crank-driven pump fills the head ditch from a steel wind wheel. The
    # several crop strips grow along the race instead of surrounding a house.
    x,z=-.43,.34
    for dx in (-.200,.200):
        for dz in (-.16,.16):
            m.block('fieldstone',x+dx,0,z+dz,.136,.112,.137,.085)
            m.beam('metal',(x+dx,.081,z+dz),(x+dx*.29,1.16,z+dz*.36),.067)
    m.block('metal',x,1.12,z,.205,.146,.19)
    shaft(m,'trim',(x,1.204,z-.224),(x,1.204,z+.191),.073)
    for i in range((10,8,5)[m.lod]):
        with m.transform(translate=(x,1.204,z-.193),rotate_z=360*i/(10,8,5)[m.lod]):
            m.beam('trim',(0,.055,0),(.041,.373,0),.026)
            with m.transform(translate=(.03,.313,0),rotate_z=-12,rotate_y=15):
                m.block('metal',0,-.085,0,.131,.172,.031,.13)
    m.disk('cloth',x,1.204,z-.209,.086,.055)
    m.beam('metal',(x,1.20,z+.06),(x+.084,1.24,z+.391),.032)
    m.block('cloth',x+.088,1.154,z+.38,.186,.159,.043,.14)
    m.block('fieldstone',x,0,-.146,.47,.205,.36,.07)
    m.block('water',x,.203,-.147,.386,.024,.276)
    for dz in ((-.625,-.16,.322,.723) if m.lod<2 else (-.61,-.015,.628)):
        crop_bed(m,.374,dz,.94,.265)
    if m.lod<2:
        m.beam('metal',(x,.252,z-.145),(x,.96,z-.071),.039)
        m.beam('metal',(x,.272,z-.14),(x,.259,-.087),.053)
        m.horn(x-.197,.147,-.085,.36,-1)
        for dx in (-.17,.17):
            m.beam('metal',(x+dx,.21,z-.14),(x-dx*.58,.76,z-.091),.034)
    if m.lod==0:
        m.jar(.836,0,-.681,.095,.187,'timber')


def hunting_lodge(m):
    # A lookout raised above the lodge gives the hunters a clear view over
    # the forest. A lashed ladder and a covered kit porch face the approach.
    hall(m,-.35,.18,.88,1.10,.54,.39,"hip")
    x,z=.43,.33
    for s in (-1,1):
        for t in (-1,1):
            m.beam("timber",(x+s*.26,.029,z+t*.24),(x+s*.17,1.17,z+t*.16),.086)
    m.box((x,.84,z),(.54,.092,.49),m.col("timber"),material="timber",bottom=True)
    m.hip_roof((x,1.15,z),.70,.62,.36,m.col("hide"),overhang=0,material="hide")
    for side in (-1,1):
        m.horn(x+side*.22,1.20,z,.54,side)
        m.beam("timber",(x+side*.095,.034,-.45),(x+side*.095,.85,.16),.048)
    for j in range((6,4,2)[m.lod]):
        f=(j+.45)/(6,4,2)[m.lod]
        m.beam("trim",(x-.107,.034+.816*f,-.45+.61*f),(x+.107,.034+.816*f,-.45+.61*f),.043)
    if m.lod<2:
        m.jar(-.64,0,-.46,.123,.21,"hide")
        if m.lod==0:
            m.jar(-.43,0,-.52,.094,.165,"hide")


def drying_net(m,x,z,w,top,drop):
    """A hanging net with open diamond meshes, a sagging edge and a heavy lower rope."""
    cols,rows=((3,3),(2,2),(1,2))[m.lod]
    def point(u,v,back=False):
        span=2*u-1
        px=x+w*.5*span*(.79+.21*v)
        py=top-drop*(1-v)-(.037+.080*(1-v))*(1-span*span)
        pz=z-.07*(1-v)+.029*abs(span)+(0.032 if back else 0)
        return (px,py,pz)
    for i in range(cols):
        for j in range(rows):
            def patch(u,v,back=False):
                return point((i+u)/cols,(j+v)/rows,back)
            corners=((0,0),(0,1),(1,1),(1,0))
            hole=((.085,.50),(.50,.89),(.915,.50),(.50,.105))
            fore=[patch(u,v) for u,v in corners]
            rear=[patch(u,v,True) for u,v in corners]
            eye=[patch(u,v) for u,v in hole]
            far_eye=[patch(u,v,True) for u,v in hole]
            for k,boundary in enumerate((i==0,j==rows-1,i==cols-1,j==0)):
                q=(k+1)%4
                m.face("hide",(fore[k],fore[q],eye[q],eye[k]))
                m.face("trim",(rear[q],rear[k],far_eye[k],far_eye[q]))
                m.face("hide",(eye[k],eye[q],far_eye[q],far_eye[k]))
                if boundary:
                    m.face("trim",(fore[q],fore[k],rear[k],rear[q]))
    for u in (0,1):
        px,py,pz=point(u,1)
        shaft(m,"hide",(px,py-.023,pz+.012),(px,top+.086,z+.036),.027)
    if m.lod==0:
        for u in (.23,.77):
            px,py,pz=point(u,0)
            m.jar(px,py-.059,pz+.018,.037,.083,"fieldstone")


def fishing_stage(m):
    # Nets drain above the masonry quay. Both faces of the rack are open to
    # the wind. The lower steps lead directly down to the skiff alongside.
    m.block("fieldstone",0,0,.41,1.67,.245,.70,.07)
    for x,z,w,h in ((.43,-.105,.67,.096),(.41,.005,.70,.177)):
        m.block("wall",x,0,z,w,h,.145,.055)
    for x in (-.69,.70):
        m.block("timber",x,.235,.46,.152,.97,.145,.08)
        m.beam("timber",(x,.30,.67),(x,.88,.45),.09)
        m.horn(x,1.15,.46,.56,-1 if x<0 else 1)
        if m.lod<2:
            m.block("cloth",x,1.04,.46,.16,.076,.157)
    shaft(m,"timber",(-.815,1.174,.43),(.815,1.174,.43),.061)
    drying_net(m,-.316,.394,.64,1.093,.45)
    drying_net(m,.343,.394,.55,1.093,.63)

    x,z=-.04,-.48
    m.profile(x,z,[(0,.53,.071,"trim"),(.071,.69,.157,"timber"),
                   (.198,.77,.222,"timber"),(.244,.79,.232,"cloth"),
                   (.246,.696,.163,"hide"),(.117,.59,.112,"timber")],n=(14,8,5)[m.lod])
    for dx in (-.25,.22):
        m.box((x+dx,.202,z),(.088,.043,.365),m.col("hide"),material="hide",bottom=True)
    m.horn(x+.66,.193,z,.36,1)
    m.beam("hide",(x+.56,.21,z+.095),(.60,.238,.112),.034)
    if m.lod<2:
        m.jar(-.58,.245,.163,.129,.17,"thatch")
        if m.lod==0:
            m.jar(-.37,.245,.176,.092,.144,"thatch")


def fishing_quay(m):
    # The catch is swung from an angular trawler to a ventilated ice house.
    hall(m,-.44,.28,.69,.91,.72,.29,"gable")
    m.block("fieldstone",-.37,0,-.45,.82,.16,.54,.08)
    x,z=.41,-.10
    m.profile(x,z,[(0,.123,.49,"trim"),(.10,.228,.63,"metal"),(.25,.31,.72,"roof"),
                   (.29,.32,.73,"ivory"),(.29,.25,.64,"trim"),(.20,.20,.58,"timber")],
              n=(12,7,5)[m.lod])
    m.block("metal",x,.196,z+.28,.29,.16,.35,.09)
    m.block("glass",x,.35,z+.28,.27,.13,.315,.08)
    m.block("ivory",x,.472,z+.28,.32,.043,.36)
    shaft(m,"metal",(x,.20,z+.017),(x,.93,z+.13),.047)
    m.beam("trim",(x,.86,z+.11),(x-.44,.77,z-.19),.061)
    m.beam("trim",(x,.92,z+.13),(x-.425,.78,z-.18),.025)
    m.beam("trim",(x-.425,.77,z-.18),(x-.425,.50,z-.18),.025)
    m.profile(x-.425,z-.18,[(.31,.057,.057,"hide"),(.37,.165,.15,"hide"),
              (.47,.128,.118,"trim"),(.52,.032,.032,"hide")],n=(10,6,4)[m.lod])
    m.horn(x,.42,z+.29,.57,1)
    if m.lod<2:
        for px in (-.55,-.23):
            m.jar(px,.16,-.49,.13,.19,"timber")
        if m.lod==0:
            wheel(m,x,.40,z+.10,.084)


def mill(m):
    # Grain feeds from a square hopper into two iron rollers. A belt wheel
    # is keyed to the front of the drive shaft beside the engine house.
    hall(m,-.44,.29,.73,.89,.67,.24,"gable")
    flue(m,-.68,.56,1.35,.128)
    for px in (.06,.62):
        m.block("brick",px,0,-.22,.15,.53,.58,.045)
    for pz in (-.38,-.12):
        shaft(m,"metal",(.045,.47,pz),(.64,.47,pz),.123)
    x,z=.33,-.19
    m.profile(x,z,[(.55,.14,.13,"metal"),(.65,.17,.16,"trim"),(.96,.40,.33,"timber"),
                   (1.04,.41,.34,"metal"),(1.04,.33,.26,"trim"),(.89,.22,.18,"thatch")],
              n=(8,6,4)[m.lod])
    wheel(m,.29,.43,-.56,.267)
    m.beam("metal",(.29,.43,-.57),(.29,.44,-.31),.082)
    if m.lod<2:
        m.jar(.61,0,.35,.15,.22,"thatch")
        if m.lod==0:
            m.jar(.39,0,.51,.107,.17,"thatch")
    m.horn(x+.24,.95,z,.49,1)


def boar(m,x,z,turn,scale=1):
    with m.transform(translate=(x,0,z),rotate_y=turn,scale=scale):
        for s in (-1,1):
            for t in (-1,1):
                m.block("trim",s*.08,0,t*.10,.052,.143,.062,.08)
        with m.transform(translate=(0,.182,.025),scale=(1,.82,1.4)):
            m.sphere((0,0,0),.15,m.col("hide"),rings=(4,3,2)[m.lod],
                     segments=(9,6,4)[m.lod],material="hide")
        m.block("roof",0,.112,-.161,.17,.145,.145,.12)
        m.block("hide",0,.123,-.234,.12,.066,.071,.06)
        if m.lod<2:
            for s in (-1,1):
                m.horn(s*.071,.13,-.206,.135,s)


def pasture(m):
    if m.e == 1:
        # A masonry sheepfold has an open gate between its semicircular walls.
        # A hide rain shelter stands inside the curved northern bank.
        n=(14,8,5)[m.lod]
        for i in range(n):
            aa,bb=[math.radians(-52+284*k/n) for k in (i,i+1)]
            def point(a,r,h):
                return (r*math.cos(a),h,r*.82*math.sin(a))
            lo,hi=(0,.29)
            outer=[point(a,.86,y) for a,y in ((aa,lo),(bb,lo),(bb,hi),(aa,hi))]
            inner=[point(a,.73,y) for a,y in ((aa,lo),(bb,lo),(bb,hi),(aa,hi))]
            m.face("fieldstone",outer[::-1])
            m.face("wall",inner)
            m.face("fieldstone",(outer[3],outer[2],inner[2],inner[3])[::-1])
            if i==0:
                m.face("wall",(outer[0],outer[3],inner[3],inner[0]))
            if i==n-1:
                m.face("wall",(inner[1],inner[2],outer[2],outer[1]))
        for s in (-1,1):
            m.block("timber",s*.42,0,.41,.093,.72,.10)
            m.block("timber",s*.42,0,.10,.095,.72,.10)
            m.horn(s*.42,.67,.40,.56,s)
        m.hip_roof((0,.70,.27),1.07,.63,.35,m.col("hide"),overhang=0,material="hide")
        boar(m,.17,-.32,-37,1.08)
        if m.lod==0:
            boar(m,-.37,-.20,28,.79)
    elif m.e == 2:
        # Dairy barn, upright fodder silo and a fenced feeding lane.
        x,z=-.26,.25
        m.block("fieldstone",x,0,z,1.14,.14,.92,.05)
        m.block("brick",x,.13,z,1.07,.50,.86)
        barrel_gambrel(m,x,z)
        m.entry(x,-.197,.53,.47)
        turned(m,.56,.32,[(0,.22,"fieldstone"),(.14,.25,"metal"),(.88,.23,"metal"),
                           (.94,.25,"trim"),(1.09,.13,"metal"),(1.13,.065,"trim")])
        for s in (-1,1):
            m.horn(x+s*.46,.62,z-.24,.48,s)
        for px in (-.73,.01,.69):
            m.block("timber",px,0,-.70,.089,.34,.091)
        m.beam("timber",(-.77,.26,-.70),(.74,.26,-.70),.053)
        boar(m,.27,-.38,68,1.13)
        if m.lod==0:
            boar(m,-.46,-.395,-67,.86)
    else:
        # The herd walks between radial feed stalls beneath an automatic
        # carousel. The covered center carries a metering tank and feed lines.
        turned(m,0,.10,[(0,.72,"fieldstone"),(.095,.78,"metal"),(.19,.75,"ivory")],.87)
        turned(m,0,.10,[(.185,.26,"metal"),(.34,.22,"glass"),(.56,.22,"metal"),
                         (.66,.49,"ivory"),(.92,.45,"glass"),(1.01,.49,"turquoise"),
                         (1.20,.22,"ivory")])
        for ang in ((-82,-20,42,102,165) if m.lod<2 else (-82,42,165)):
            with m.transform(translate=(0,.18,.10),rotate_y=ang):
                m.beam("metal",(.47,.02,.00),(.46,.49,.00),.067)
                m.beam("ivory",(.18,.62,0),(.54,.48,0),.077)
                m.box((.54,.13,0),(.34,.08,.105),m.col("metal"),material="metal",bottom=True)
        if m.lod<2:
            with m.transform(translate=(0,.19,0)):
                boar(m,.38,-.15,104,.86)
        boar(m,-.35,-.72,21,.94)
        m.horn(.32,.91,.13,.58,1)
        m.horn(-.32,.91,.13,.58,-1)


def barrel_gambrel(m,x,z):
    edge=[(-.51,.623),(-.35,.942),(-.14,1.07),(.15,1.07),(.36,.94),(.51,.623)]
    fore=[(x+u,y,z-.49) for u,y in edge]
    rear=[(u,y,z+.49) for u,y,_ in fore]
    m.face("roof",fore)
    m.face("roof",rear[::-1])
    for i in range(len(edge)-1):
        m.face("metal",(rear[i],rear[i+1],fore[i+1],fore[i]))
    m.face("metal",(fore[0],fore[-1],rear[-1],rear[0]))


DESIGNS={
    ("FishingHut","classical"):fishing_stage,
    ("Farm","classical"):farm, ("Farm","industrial"):windpump_farm, ("Farm","future"):farm,
    ("FishingHut","industrial"):fishing_quay,
    ("HuntingLodge","classical"):hunting_lodge, ("Mill","industrial"):mill,
    **{("Pasture",era):pasture for era in ("classical","industrial","future")},
}
