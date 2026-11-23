"""Orc building art. Claimed by builder session 2."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_learninghealth import build_lodge, run_package
from generators.buildings.orc_era_forms import clean_power, well_pump
from generators.buildings.orc_resource_yards import shaft, wheel
from generators.buildings import orc_civic_trades

PACKAGE = "orc_infrastructure"


def bucket(m,x,y,z,r):
    if m.lod==2:
        m.drum("timber",x,y,z,r*.72,r*1.52,r)
        return
    m.profile(x,z,[(y,r*.72,r*.72,"trim"),(y+.04,r*.83,r*.83,"trim"),
                   (y+r*1.45,r,r,"timber"),(y+r*1.55,r*1.04,r*1.04,"metal"),
                   (y+r*1.55,r*.75,r*.75,"metal"),(y+r*1.19,r*.70,r*.70,"water")],
              n=(8,6,4)[m.lod])
    if m.lod<2:
        for s in (-1,1):
            m.beam("metal",(x+s*r*.83,y+r*1.45,z),(x+s*r*.18,y+r*2.13,z),r*.13)
        m.beam("hide",(x-r*.22,y+r*2.13,z),(x+r*.22,y+r*2.13,z),r*.16)


def well(m):
    # Open cut-stone well, capped only below the curb so the water can be seen.
    m.profile(0,-.11,[(0,.61,.58,"fieldstone"),(.105,.67,.64,"fieldstone"),
                      (.14,.52,.52,"trim"),(.44,.52,.52,m.cladding),
                      (.48,.565,.56,"cloth"),(.535,.55,.55,"ivory"),
                      (.535,.423,.423,"ivory"),(.30,.397,.397,"fieldstone"),
                      (.286,.39,.39,"water")],n=(14,10,6)[m.lod])
    for s in (-1,1):
        x=s*.445
        m.beam(m.frame,(x,.09,.03),(x*.90,1.18,.08),.105)
        if m.lod==0:
            m.block("cloth",x*.94,.84,.06,.121,.076,.123)
    with m.transform(translate=(-.53,.92,.066),rotate_z=-90):
        m.drum(m.frame,0,0,0,.082,1.13)
        if m.lod<2:
            m.drum("hide",0,.40,0,.128,.30)
    m.beam("trim",(.01,.60,-.025),(.01,.945,-.025),.028)
    bucket(m,.01,.45,-.025,.078)
    if m.e<=1:
        # Small ridged hood leaves the forward half of the well open.
        m.hip_roof((0,1.15,.10),1.20,.56,.31+.11*m.e,m.col(m.skin),material=m.skin,overhang=0)
        m.horn(-.44,1.14,.10,.51,-1)
        m.horn(.44,1.14,.10,.51,1)
    elif m.e==2:
        m.block("metal",0,1.15,.08,1.06,.12,.25)
        with m.transform(translate=(.54,.91,.06),rotate_z=-90):
            m.drum("cloth",0,0,0,.225,.08)
            m.drum("metal",0,.08,0,.095,.045)
        m.horn(-.41,1.22,.065,.64,-1)
    else:
        m.profile(0,.095,[(1.14,.60,.27,"metal"),(1.21,.67,.34,"turquoise"),
                           (1.39,.36,.21,"glass"),(1.45,.14,.12,"ivory")])
        m.horn(.43,1.17,.08,.50,1)
    if m.lod<2:
        m.beam("trim",(-.53,.92,.06),(-.55,.78,-.09),.045)
        m.beam("gold",(-.55,.78,-.09),(-.68,.78,-.09),.048)
    if m.lod==0:
        bucket(m,-.42,0,-.57,.105)


def suspended_flume(m):
    # One central A-pylon carries the water trough by four pairs of cable stays.
    for side in (-1,1):
        x=side*.66
        m.block("fieldstone",x,0,.03,.40,.13,.32,.12)
        m.beam("ivory",(x,.09,.03),(side*.19,1.52,.03),.18,.17)
        m.horn(side*.20,1.40,.03,.67,side)
        for z in (-.85,.85):
            m.beam("metal",(side*.22,1.47,.031),(side*.27,.63,z),.048)
            if m.lod<2:
                m.beam("trim",(side*.23,1.38,.023),(side*.27,.63,z*.55),.029)
                m.block("gold",side*.27,.57,z,.12,.11,.071)
    m.block("metal",0,1.44,.03,.48,.15,.20,.11)
    if m.lod==0:
        for y in (.31,.48,.65,.82,1.0):
            x=.66-(y-.09)*.47/1.43
            m.beam("metal",(x-.09,y,-.085),(x+.085,y,-.085),.034)
    # Two end shoes rest on concrete piers. The raised water runs between tall side walls.
    for z in (-.88,.88):
        m.block("fieldstone",0,0,z,.48,.48,.14,.15)
    m.block("ivory",0,.455,0,.61,.133,1.98,.04)
    m.block("water",0,.585,0,.38,.034,1.96)
    for s in (-1,1):
        m.block("turquoise",s*.25,.581,0,.108,.126,1.98)
        if m.lod==0:
            for z in (-.89,-.48,0,.48,.89):
                with m.transform(translate=(s*.272,.53,z),rotate_x=90):
                    m.drum("metal",0,0,0,.069,.043,n=8)



def aqueduct(m):
    if m.e==3:
        return suspended_flume(m)
    if m.e==2:
        # Water crosses the hollow by pressure. Twin masonry header tanks feed
        # a flanged iron inverted siphon on saddles at the bottom of the valley.
        for sign in (-1,1):
            z=sign*.759
            m.block('fieldstone',.02,0,z,.69,.145,.338,.10)
            m.block('brick',.02,.140,z,.572,.703,.271,.14)
            m.block('metal',.02,.826,z,.732,.099,.379,.055)
            m.block('metal',.02,.912,z,.693,.156,.344)
            m.block('water',.02,1.068,z,.551,.017,.282)
            if m.lod<2:
                for s in (-1,1):
                    m.horn(.02+s*.272,.994,z,.357,s)
            shaft(m,'metal',(.02,.724,sign*.711),(.02,.237,sign*.351),.186)
            shaft(m,'trim',(.02,.419,sign*.484),(.02,.372,sign*.451),.205)
            m.block('fieldstone',.02,0,sign*.305,.469,.132,.151,.08)
        shaft(m,'metal',(.02,.237,-.37),(.02,.237,.37),.189)
        for z in ((-.35,-.136,.137,.35) if m.lod<2 else (-.32,.32)):
            shaft(m,'trim',(.02,.237,z-.019),(.02,.237,z+.019),.203)
        if m.lod<2:
            shaft(m,'metal',(.119,.265,.028),(.41,.266,.026),.053)
            with m.transform(translate=(.393,.27,.025),rotate_y=90):
                wheel(m,0,0,0,.149)
        if m.lod==0:
            for sign in (-1,1):
                m.block('cloth',.313,.536,sign*.762,.044,.155,.102)
        return
    # The arch has a continuous soffit and a level top carrying the actual trough.
    # Coordinates across each face are longitudinal Z / elevation Y.
    mat=("wall","fieldstone","brick","plaster")[m.e]
    h=(1.00,1.04,1.14,1.30)[m.e]
    n=(10,6,3)[m.lod]
    centers=(-.455,.455)
    for z in (-.91,0,.91):
        if m.lod==2:
            m.block(mat,0,0,z,.51,.13+.27*h,.15,.08)
        else:
            m.block("fieldstone",0,0,z,.60,.13,.165,.08)
            m.block(mat,0,.13,z,.42,.27*h,.134,.06)
        if m.lod<2:
            m.block(m.frame,0,.13+.26*h,z,.50,.063,.166)
    for z in centers:
        extent=.455
        inner=.396
        axis=.13+.27*h
        crown=h*.85
        def corner(side,t):
            ang=math.pi*(1-t)
            return (side*.201,axis+(crown-axis)*math.sin(ang),z+inner*math.cos(ang))
        for i in range(n):
            t0,t1=i/n,(i+1)/n
            A,B=corner(1,t0),corner(1,t1)
            C,D=corner(-1,t1),corner(-1,t0)
            lo=z-extent+2*extent*t0
            hi=z-extent+2*extent*t1
            U,V=(.201,h,lo),(.201,h,hi)
            W,X=(-.201,h,hi),(-.201,h,lo)
            face_mat="ivory" if m.e<2 and i in (n//2-1,n//2) else mat
            m.face(face_mat,(B,A,U,V))
            m.face(face_mat,(D,C,W,X))
            m.face(mat,(A,B,C,D))
            if m.lod==0 and i==n//2:
                # The builders' mark on the central voussoir.
                m.block("cloth",.205,crown+.03,z+.018,.024,(h-crown)*.62,.072)
        for end,idx in ((-1,0),(1,1)):
            # Exposed vertical end at the terminus only.
            if (z<0 and end<0) or (z>0 and end>0):
                Z=z+end*extent
                m.block(mat,0,axis,Z-end*.037,.404,h-axis,.079)
    m.block("trim",0,h,0,.53,.072,1.958)
    m.block("water",0,h+.072,0,.295,.026,1.94)
    for s in (-1,1):
        m.block("metal" if m.e>=2 else "wall",s*.218,h+.072,0,.113,.12,1.952)
    for z in (-.89,.89):
        m.horn(.18,h+.145,z,.39,1)
        if m.lod<2:
            m.horn(-.18,h+.145,z,.39,-1)
    # A painted saddle spans the center pier. Two large tusk corbels rise
    # outside the channel, leaving its full length clear for water.
    for side in (-1,1):
        m.block("cloth",side*.215,h-.22,side*.04,.235,.33,.17,.12)
        m.horn(side*.30,h-.045,side*.042,1.10,side,.035*side)
    if m.e>=2:
        # External side pipes ride directly on the masonry brackets.
        for side in (1,-1) if m.lod<2 else (1,):
            with m.transform(translate=(side*.275,h*.84,-.93),rotate_x=90):
                m.drum("turquoise" if m.e==3 else "metal",0,0,0,.059,1.86)
            if m.lod<2:
                for p in (-.85,0,.85):
                    m.block("metal",side*.22,h*.77,p,.15,.08,.093)
    if m.lod==0:
        # Maintenance ladder on the middle pier, clear of the watercourse.
        for z in (-.062,.062):
            m.beam("trim",(.38,.004,z),(.279,h+.14,z),.032)
        for j in range(6):
            y=.11+j*.155
            x=.38-.101*y/(h+.14)
            m.beam("hide",(x,y,-.068),(x,y,.068),.027)


def granary(m):
    # The number and proportions of the bins evolve with mechanical conveying.
    e=m.e
    positions=[(-.40,.22,.40,1.02),(.37,.22,.40,.84)]
    if e>=2:
        positions=[(-.47,.29,.32,1.17),(.08,.46,.29,1.00),(.54,.14,.30,1.09)]
    for x,z,r,h in positions:
        # Staddle stones or reinforced hoppers keep the grain above damp soil.
        rows=[(0,r*.65,r*.65,"fieldstone"),(.14,r*.78,r*.78,m.frame),(.28,r,r,"trim")]
        for y in ((h*.40,h*.67) if m.lod==0 else ((h*.67,) if m.lod==1 else ())):
            rows.extend([(y,r*.99,r*.99,m.cladding),(y+.008,r*1.028,r*1.028,m.frame),
                         (y+.044,r*1.028,r*1.028,m.frame),(y+.051,r*.985,r*.985,m.frame)])
        rows.extend([(h*.90,r*.97,r*.97,m.cladding),(h,r*1.10,r*1.10,"cloth"),
                     (h+.28,r*.14,r*.14,m.skin)])
        m.profile(x,z,rows,n=(9,7,5)[m.lod])
        m.horn(x+r*.68,h-.06,z-r*.22,r*.76,1)
    m.cell(-.04,-.285,.38,.36,.33)
    m.entry(-.045,-.615,.28,.29)
    if e>=2:
        m.beam(m.frame,(-.47,1.26,.29),(.53,1.18,.14),.106)
        if m.lod==0:
            m.beam("metal",(-.46,.70,.22),(-.04,1.15,.21),.046)
    if m.lod<2:
        m.jar(.40,0,-.43,.137,.18,"thatch")
        if m.lod==0:
            m.jar(.26,0,-.61,.104,.14,"thatch")
            m.jar(-.38,0,-.51,.092,.16)


def warehouse(m):
    # A long, low store with pitched load doors and bundles under a side gantry.
    e=m.e
    x,z=-.22,.13
    sx,sz=.59,.60
    h=(.62,.84,.78,.93)[e]
    m.block("fieldstone",x,0,z,sx*2,.106,sz*2,.04)
    m.block(m.cladding,x,.106,z,2*sx*.95,h-.106,2*sz*.93)
    m.block(m.frame,x,h,z,sx*2.08,.063,sz*2.06)
    roof=(.48,.46,.29,.325)[e]
    roofing="metal" if e==0 else m.skin
    m.hip_roof((x,h+.063,z),sx*2.10,sz*2.09,roof,m.col(roofing),material=roofing,overhang=0)
    if e==2 and m.lod<2:
        m.block("glass",x-.17,h+.265,z,.30,.16,.32)
        m.block("metal",x-.17,h+.425,z,.35,.048,.39)
    elif e==3:
        m.profile(x,z,[(h+.24,.29,.31,"metal"),(h+.40,.26,.24,"glass"),
                       (h+.48,.12,.13,"ivory")])
    m.entry(x,-.478,.53,h*.83)
    for s in (-1,1):
        m.horn(x+s*.46,h,z-.28,.48,s)
    # The foreman tallies goods under a cloth-covered weighing bay.
    for a in (.33,.79):
        m.block(m.frame,a,0,-.18,.082,.64,.092)
    m.beam(m.frame,(.29,.615,-.18),(.84,.615,-.18),.077)
    if m.lod<2:
        m.beam("trim",(.73,.61,-.18),(.82,.45,-.18),.045)
        m.beam("trim",(.54,.612,-.18),(.54,.315,-.18),.028)
    m.block("hide",.54,0,-.27,.29,.235,.37,.06)
    if m.lod<2:
        m.block("trim",.55,.076,-.270,.31,.044,.385)
        m.jar(.59,0,.33,.184,.32)
        if m.lod==0:
            m.jar(.29,0,.48,.12,.24)
            for a in (-.68,.23):
                m.block(m.frame,a,0,-.414,.058,h,.074)
            m.block("cloth",-.69,h*.49,-.447,.11,h*.32,.037)
    if e>=2:
        m.block("metal",.56,.646,-.16,.69,.048,.32)


def works(m,t):
    fusion=t=="FusionReactor"
    # Ring coils clamp a bright core. Low converter hall anchors the cable ends.
    if fusion:
        x,z=.10,.12
        m.profile(x,z,[(0,.65,.63,"fieldstone"),(.18,.76,.74,"metal"),
                       (.29,.55,.54,"ivory"),(.46,.486,.475,"turquoise"),
                       (.47,.509,.498,"metal"),(.53,.509,.498,"metal"),
                       (.54,.48,.47,"metal"),(.72,.474,.464,"glass"),
                       (.73,.509,.495,"metal"),(.785,.509,.495,"metal"),
                       (.795,.47,.46,"metal"),(.84,.47,.46,"glass"),
                       (1.02,.47,.45,"ascension"),(1.13,.33,.31,"metal"),
                       (1.21,.18,.17,"turquoise")])
        for j in range(4):
            with m.transform(translate=(x,0,z),rotate_y=45+90*j):
                # Broad angled fins, with contrasting insulating collars.
                m.beam("ivory",(0,.14,.65),(0,.77,.55),.16,.21)
                m.beam("gold",(0,.74,.56),(0,.99,.42),.137,.17)
                if m.lod<2:
                    m.horn(0,.84,.52,.48,1)
        m.cell(-.36,-.43,.33,.30,.35)
        m.entry(-.38,-.715,.245,.275)
    else:
        m.cell(-.26,-.12,.57,.60,.58)
        m.entry(-.30,-.685,.36,.46)
        for x,z,h,r in ((.32,.28,1.50,.27),(.66,-.07,1.14,.206)):
            # Hyperboloid cooling stacks have a visible recessed mouth.
            m.profile(x,z,[(0,r*1.18,r*1.18,"fieldstone"),(.18,r*1.21,r*1.21,"trim"),
                           (.43*h,r*.73,r*.73,m.cladding),(.70*h,r*.66,r*.66,m.cladding),
                           (.86*h,r*.79,r*.79,"cloth"),(h,r*.94,r*.94,"wall"),
                           (h,r*.71,r*.71,m.frame),(.87*h,r*.67,r*.67,"door")])
        if m.e==3:
            m.beam("metal",(.30,.91,.28),(.67,.77,-.07),.102)
            m.horn(.33,1.42,.47,.51,1)
        if m.lod<2:
            m.horn(.46,.37,-.48,.46,1)
            m.block(m.frame,.43,0,-.48,.26,.39,.27,.12)
    if m.lod==0:
        m.jar(-.68,0,-.42,.099,.19,"metal")
        m.block("gold",-.67,.08,-.49,.17,.05,.13)


def freight_depot(m):
    # Riveted gantry over an open loading square. The tall enclosed left pier
    # houses the lift motor; the opposite legs straddle the cart track.
    m.block("fieldstone",0,0,.032,1.82,.084,1.64,.046)
    m.block("brick",-.576,.084,.16,.435,.85,.66,.038)
    m.block("trim",-.576,.934,.16,.488,.075,.724)
    m.entry(-.57,-.177,.256,.414,base=.086)
    for z in (-.137,.443):
        m.beam("metal",(.67,.084,z),(.551,1.00,z),.10)
        if m.lod<2:
            m.beam("trim",(.378,.084,z),(.55,.649,z),.069)
    m.block("metal",-.028,1.009,.154,1.622,.122,.706)
    m.block("cloth",-.51,1.131,.158,.59,.152,.65,.03)
    m.horn(-.72,1.279,.156,.31,-1)
    m.disk("gold",-.571,.708,-.175,.102,.034)
    m.block("metal",.187,.832,.103,.228,.177,.237)
    m.beam("trim",(.188,.442,.104),(.188,.837,.104),.027)
    loads=[(.188,.104,.298,.204),(-.285,-.384,.326,.085)]
    if m.lod<2:
        loads.extend(((.436,-.332,.264,.084),(-.36,.457,.28,.085)))
    for x,z,w,y in loads:
        m.block("timber",x,y,z,w,.83*w,w*.91)
        if m.lod<2:
            for side in (-1,1):
                m.block("trim",x+side*w*.276,y+.004,z,.027,.835*w,w*.945)
    if m.lod==0:
        for x in (-.411,.004,.412):
            m.disk("gold",x,1.060,-.205,.033,.017)


def design(m,t):
    if t=="Well" and m.e>0:
        well_pump(m)
    elif t=="PowerPlant" and m.e==3:
        clean_power(m)
    elif t=="Well":
        well(m)
    elif t=="Aqueduct":
        aqueduct(m)
    elif t=="Storehouse" and m.e==2:
        freight_depot(m)
    elif t=="Storehouse":
        warehouse(m)
    elif t=="Granary":
        (orc_civic_trades.granary if m.e in (1,3) else granary)(m)
    else:
        works(m,t)


def build(spec,lod):
    return build_lodge(spec,lod,design)


if __name__=="__main__":
    run_package(PACKAGE,build)
