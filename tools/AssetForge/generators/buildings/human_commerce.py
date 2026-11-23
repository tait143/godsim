"""Human exchanges, waterfronts and passenger gateways. Builder session 10."""
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House


PACKAGE = "human_commerce"
ERAS = ("ancient", "classical", "industrial", "future")


def drum(m, mat, x, y, z, r, h, end=None, sides=None):
    m.cylinder((x,y,z), r, h, m.color(mat), segments=sides or (12,8,5)[m.lod],
               top_radius=end, material=mat)


def pier(m, mat, x, z, base, top, w, d=None):
    m.block(mat, (x,(base+top)/2,z), (w,top-base,d or w))


def panel(m, mat, x, y, z, w, h):
    m.face(mat, [(x+w/2,y-h/2,z),(x-w/2,y-h/2,z),
                 (x-w/2,y+h/2,z),(x+w/2,y+h/2,z)])


def badge(m, x, y, z, r, clock=False):
    """Recessed enamel medallion on a rolled bezel, facing the forecourt."""
    with m.transform(translate=(x,y,z), rotate_x=-90):
        drum(m,"gold",0,0,0,r,.023)
        drum(m,"banner" if not clock else "ivory",0,.023,0,r*.79,.008)
    if clock:
        m.beam("door",(x,y,z-.037),(x-r*.37,y+r*.30,z-.037),r*.11)
        m.beam("door",(x,y,z-.040),(x+r*.43,y+r*.45,z-.040),r*.105)
    else:
        m.face("gold",[(x,y-r*.61,z-.035),(x-r*.27,y+.012,z-.035),
                       (x,y+r*.62,z-.035),(x+r*.27,y-.012,z-.035)])


def flag(m, x, y, z, h, w):
    pier(m,"trim",x,z,y,y+h,.023)
    a=(x,y+h-.008,z)
    b=(x+w*.58,y+h-.033,z-.035)
    c=(x+w,y+h-.015,z-.012)
    d=(x+w*.58,y+h-.143,z-.035)
    e=(x,y+h-.16,z)
    faces=((a,e,d,b),(b,d,c))
    for points in faces:
        m.face("banner",points)
        m.face("banner",[(u,v,s+.004) for u,v,s in reversed(points)])


def glazed(m, x, y, front, w, h, count=3):
    panel(m,"trim",x,y,front-.008,w+.032,h+.036)
    panel(m,"glass",x,y,front-.014,w,h)
    if m.lod < 2:
        for j in range(1,count):
            pier(m,"ivory",x+w*(j/count-.5),front-.024,y-h/2,y+h/2,.019,.025)
        m.block("trim",(x,y-h/2-.023,front-.017),(w+.074,.035,.049))


def stock(m, x, z, base, r, mat):
    """Open produce hamper with a crown of apples or squash."""
    drum(m,"timber",x,base,z,r*.82,.096,r,sides=(8,6,4)[m.lod])
    if m.lod==0:
        for dx,dz in ((-.38,-.20),(.36,-.24),(0,.39)):
            m.dome((x+dx*r,base+.094,z+dz*r),r*.46,m.color(mat),
                   rings=2,segments=6,squash=1.02,material=mat)
    else:
        m.dome((x,base+.09,z),r*.82,m.color(mat),rings=1,segments=6,squash=.46,material=mat)


def canvas(m, x, z, w, d, eave, rise, colours):
    """Tensioned market cloth with hanging scallops integrated into each stripe."""
    with m.transform(translate=(x,eave,z)):
        count=(7,5,3)[m.lod]
        for i in range(count):
            left=w*(i/count-.5)
            right=w*((i+1)/count-.5)
            middle=(left+right)/2
            mat=colours[i%len(colours)]
            # Both sides and the shallow scalloped valance share the same edge.
            for sign in (-1,1):
                k=sign*d/2
                top=[(left,0,k),(right,0,k),(right,rise,0),(left,rise,0)]
                top=top if sign==1 else top[::-1]
                m.face(mat,top)
                m.face(mat,[(a,b-.012,c) for a,b,c in reversed(top)])
                edge=[(left,0,k),(left,-.060,k),(middle,-.090,k),
                      (right,-.060,k),(right,0,k)]
                m.face(mat,edge if sign==1 else edge[::-1])
        if m.lod<2:
            m.beam("trim",(-w*.49,rise-.019,0),(w*.49,rise-.019,0),.036)
    for side in (-1,1):
        pier(m,"trim",x+side*w*.45,z,.083,eave+rise,.052)
        if m.lod<2:
            for end in (-1,1):
                m.beam("trim",(x+side*w*.45,eave+rise-.026,z),
                       (x+side*w*.45,eave-.019,z+end*d*.48),.034)


def counter(m, x, z, w, base=.084):
    m.block("wall",(x,base+.115,z),(w,.23,.34))
    m.block("timber",(x,base+.248,z),(w+.047,.041,.378))
    if m.lod<2:
        for i,mat in enumerate(("leaf","roof_tile","thatch")):
            stock(m,x+(i-1)*w*.29,z-.022,base+.268,min(w*.12,.111),mat)
    if m.lod==0:
        for s in (-1,1):
            pier(m,"trim",x+s*(w/2-.026),z-.174,base,base+.24,.042)


def market(m, era):
    m.course("fieldstone",0,0,1.83,1.65,0,.083,.16)
    if era==0:
        canvas(m,-.06,.29,1.39,.74,.64,.32,("banner","ivory"))
        counter(m,-.06,.22,1.15)
        counter(m,.45,-.51,.65)
        if m.lod<2:
            m.jar(-.65,-.42,.084,.120,.27)
            m.jar(-.38,-.51,.084,.092,.19)
    elif era==1:
        m.block("wall",(0,.387,.32),(1.45,.61,.64))
        m.hip_roof((0,.692,.32),1.59,.80,.38,m.color("roof"),overhang=0,material="roof")
        m.entry(-.40,0,.20,.086,.36)
        if m.lod<2:
            m.light(.30,.46,0,.24,.21,True)
        canvas(m,.04,-.38,1.45,.53,.54,.13,("cloth","ivory"))
        counter(m,.10,-.562,1.04)
        if m.lod==0:
            m.jar(-.715,-.515,.084,.085,.22)
            m.flue(.49,.42,.814,1.19,.12)
    else:
        # Covered food exchange; an industrial roof lantern evolves into glazed wings.
        m.block("wall",(0,.48,.26),(1.63,.80,.94))
        if era==2:
            m.prism_x((0,.88,.26),1.76,1.03,.29,m.color("roof"),material="roof")
            m.block("glass",(0,1.13,.26),(1.18,.21,.25))
            m.hip_roof((0,1.233,.26),1.26,.31,.107,m.color("metal"),overhang=0,material="metal")
        else:
            for s in (-1,1):
                with m.transform(translate=(s*.434,.94,.255),rotate_z=s*13):
                    role="glass" if s==1 else "metal"
                    m.box((0,0,0),(.90,.078,1.08),m.color(role),bottom=True,material=role)
            m.block("gold",(0,.869,.26),(.13,.083,1.10))
        m.entry(-.453,-.21,.235,.084,.48)
        glazed(m,.30,.585,-.21,.72,.30,4)
        canvas(m,.32,-.47,.90,.51,.58,.108,("banner","ivory"))
        counter(m,.32,-.640,.72)
        if m.lod<2:
            with m.transform(translate=(.818,0,.28),rotate_y=-90):
                glazed(m,0,.52,0,.61,.28,3)
        if m.lod==0:
            m.herbs(-.66,.252,-.21,.20)
    if m.lod==0 and era!=1:
        flag(m,-.67,.084,-.49,.90,.255)


def hall(m, x,z,w,d,h,era, base=.085):
    """Front-facing pitched trading hall, or a framed clerestory above a late facade."""
    m.block("wall",(x,(h+base)/2,z),(w,h-base,d))
    if era<3:
        with m.transform(translate=(x,0,z)):
            m.roof(w+.108,d+.11,h,h*(.55 if era==1 else .37),"roof","wall",
                   hip=.085 if era==1 else 0,ridge_mat="trim" if m.lod<2 else None)
    else:
        m.block("trim",(x,h+.032,z),(w+.085,.065,d+.077))
        m.block("glass",(x,h+.119,z+.036),(w*.80,.13,d*.69))
        m.hip_roof((x,h+.184,z+.036),w*.89,d*.78,.19,m.color("metal"),overhang=0,material="metal")


def ship(m, z, era):
    # Narrow double-ended cargo hull, moored beside the landing with a recessed hold.
    with m.transform(translate=(.67,0,z)):
        plan=[(0,-.69),(.14,-.49),(.190,-.16),(.183,.34),(.133,.54),
              (0,.62),(-.133,.54),(-.183,.34),(-.190,-.16),(-.14,-.49)]
        if m.lod==2:
            plan=[plan[i] for i in (0,2,3,5,7,8)]
        def ring(sx,sz,y,rake):
            return [(a*sx,y+abs(b)**2*rake,b*sz) for a,b in plan]
        keel,rail,inner,floor=ring(.51,.78,.026,0),ring(1,1,.207,.21),ring(.86,.94,.207,.21),ring(.67,.86,.115,.04)
        for i in range(len(plan)):
            j=(i+1)%len(plan)
            m.face("timber" if era==1 else "roof",(keel[j],keel[i],rail[i],rail[j]))
            if m.lod<2:
                m.face("ivory",(rail[j],rail[i],inner[i],inner[j]))
                m.face("timber",(inner[j],inner[i],floor[i],floor[j]))
        if m.lod<2:
            m.face("timber",floor[::-1])
        else:
            # A flat weather deck replaces the recessed hold of the near model.
            m.face("timber",[(a,.208,b) for a,b in reversed(plan)])
        if era==1:
            pier(m,"trim",0,.10,.12,.98,.039)
            # A furled sail still makes the hull recognisable on a narrow berth.
            m.beam("ivory",(-.16,.72,.10),(.174,.83,.10),.086)
            if m.lod<2:
                m.beam("trim",(0,.94,.10),(0,.30,-.59),.016)
        else:
            m.block("wall",(0,.278,.33),(.242,.26,.24))
            m.block("trim",(0,.425,.33),(.266,.036,.266))
            if m.lod<2:
                glazed(m,0,.327,.21,.174,.073,2)
                for cz in (-.28,-.017):
                    m.block("banner" if cz<0 else "cloth",(0,.225,cz),(.239,.17,.23))


def harbor(m, era):
    m.course("fieldstone",-.355,0,1.15,1.68,0,.17,.095)
    # Mooring finger extends over pile caps; vessel stays outside its edge.
    m.block("timber",(.32,.155,-.008),(.25,.087,1.67))
    for z in ((-.73,.09,.72) if m.lod<2 else (-.73,.72)):
        pier(m,"trim",.37,z,0,.267,.07)
    hall(m,-.34,.325,.83,.75,.73,era,.17)
    m.entry(-.39,-.05,.254,.171,.393)
    if m.lod<2:
        m.light(-.105,.498,-.05,.125,.16,shutters=era==1)
    ship(m,.018,era)
    # Dock cranes: guyed timber derrick, riveted rotating jib, then articulated boom.
    mast_x,mast_z=-.35,-.52
    top=(.84,1.04,1.11)[era-1]
    if m.lod<2:
        drum(m,"fieldstone",mast_x,.17,mast_z,.192,.131,.15)
    pier(m,"timber" if era==1 else "metal",mast_x,mast_z,.30 if m.lod<2 else .17,top,.121)
    if era==1:
        m.beam("trim",(mast_x,.45,mast_z),(.24,.91,mast_z),.082)
        m.beam("trim",(mast_x,top,mast_z),(.24,.91,mast_z),.023)
        boom=(.24,.91,mast_z)
    elif era==2:
        boom=(.38,top+.07,mast_z)
        m.beam("gold",(mast_x-.14,top-.08,mast_z),boom,.09)
        m.beam("metal",(mast_x,top-.41,mast_z),(.32,top+.02,mast_z),.065)
    else:
        boom=(.39,.97,mast_z)
        m.beam("ivory",(mast_x,top-.12,mast_z),(.047,1.261,mast_z),.133)
        m.beam("trim",(.047,1.261,mast_z),boom,.084)
    m.beam("trim",boom,(boom[0],.49,mast_z),.023 if m.lod<2 else .034)
    m.block("hide",(boom[0],.424,mast_z),(.134,.132,.121))
    if m.lod<2:
        m.block("timber",(-.711,.267,-.367),(.224,.194,.22))
        m.block("timber",(-.646,.25,-.64),(.159,.160,.18))
    if m.lod==0:
        # A pair of line tubs and a coil wrapped around a working bollard.
        for cx,cz in ((.146,-.72),(.13,.65)):
            drum(m,"metal",cx,.17,cz,.067,.099,.048,8)
            drum(m,"metal",cx,.264,cz,.075,.032,.071,8)
            for j in range(2):
                drum(m,"hide",cx,.189+.030*j,cz,.066,.022,.068,8)
        with m.transform(translate=(.080,0,.31),rotate_y=-90):
            m.light(0,.483,0,.306,.177,shutters=era==1)
        # Raggedly spaced plank end joints read at close zoom.
        for j in range(7):
            z=-.63+j*.198
            m.block("trim",(.316,.200,z),(.225,.006,.010))
        for x,z,w,h in ((-.711,-.367,.224,.194),(-.646,-.64,.159,.160)):
            for s in (-1,1):
                m.block("trim",(x+s*w*.30,.171+h/2,z),(w*.105,h+.006,w+.011))
        flag(m,-.627,.76,.416,.50,.236)


def shield(m, x,y,z,w,h):
    outline=[(-.5,.5),(.5,.5),(.46,-.12),(.23,-.38),(0,-.55),(-.23,-.38),(-.46,-.12)]
    for role,scale,dz in (("gold",1,0),("banner",.82,-.007)):
        m.face(role,[(x+a*w*scale,y+b*h*scale,z+dz) for a,b in outline])
    if m.lod<2:
        for s in (-1,1):
            # One crossed baton sits in relief above the other on the escutcheon.
            relief=z-(.027 if s==1 else .016)
            m.beam("ivory",(x-s*.22*w,y-h*.20,relief),(x+s*.23*w,y+h*.26,relief),w*.081)


def guild(m, era):
    m.course("fieldstone",0,.046,1.65,1.50,0,.088,.10)
    height=(.95,1.03,1.13)[era-1]
    hall(m,-.23,.01,.91,1.22,height,era)
    hall(m,.458,.255,.61,.79,.66 if era!=3 else .79,era)
    front=-.60
    m.entry(-.23,front,.26,.089,.431)
    if era==1:
        # Crow-stepped guild facade follows the wall behind the roof edge.
        for s in (-1,1):
            for j in range(3):
                x=-.23+s*(.397-j*.126)
                h=height+.15+j*.137
                pier(m,"wall",x,front-.036,height-.04,h,.126,.111)
                if m.lod<2:
                    m.block("trim",(x,h+.016,front-.036),(.157,.031,.132))
        pier(m,"wall",-.23,front-.036,height-.03,1.52,.164,.111)
        m.block("gold",(-.23,1.54,front-.036),(.185,.042,.132))
    elif era==2:
        # An oriel above the guild entrance, and a roof lantern serving the meeting room.
        m.block("wall",(-.23,.756,front-.083),(.54,.35,.18))
        m.hip_roof((-.23,.931,front-.083),.60,.26,.134,m.color("roof"),overhang=0,material="roof")
        drum(m,"wall",-.23,1.29,.22,.186,.255)
        m.cone((-.23,1.545,.22),.23,.22,m.color("roof"),segments=(8,6,4)[m.lod],material="roof")
    else:
        # Twin registration fins flank the raised glazed council chamber.
        for s in (-1,1):
            pier(m,"ivory",-.23+s*.426,front-.017,.09,1.45,.093,.152)
            m.block("gold",(-.23+s*.426,1.456,front-.017),(.110,.029,.166))
        glazed(m,-.23,.88,front,.65,.349,4)
    sy=(.795,.741,.69)[era-1]
    shield(m,-.23,sy,front-(.183 if era==2 else .054),.28,.36)
    if m.lod<2:
        m.light(.459,.433,-.14,.25,.214,shutters=era<3)
        if era<3:
            for x in (-.626,.17):
                pier(m,"trim",x,front-.014,.09,height,.054)
            m.block("trim",(-.23,.568,front-.013),(.90,.048,.047))
            for s in (-1,1):
                m.beam("trim",(-.23+s*.383,.623,front-.028),
                       (-.23+s*.215,.896,front-.028),.042)
        else:
            with m.transform(translate=(.767,0,.25),rotate_y=-90):
                glazed(m,0,.535,0,.48,.22,3)
    if m.lod==0:
        m.herbs(.459,.280,-.14,.305)
        # Guild officer's blue door hangings and two iron-bound pavement planters.
        for s in (-1,1):
            drum(m,"metal",-.23+s*.307,.09,-.629,.073,.114,.090,8)
            m.dome((-.23+s*.307,.202,-.629),.092,m.color("leaf"),
                   rings=2,segments=7,squash=.95,material="leaf")
        m.beam("gold",(.271,.585,-.166),(.643,.585,-.166),.026)
        for x in (.307,.604):
            panel(m,"banner",x,.522,-.170,.054,.115)
        with m.transform(translate=(.766,0,.237),rotate_y=-90):
            if era<3:
                m.light(0,.418,0,.25,.173,shutters=era==1)
            else:
                m.herbs(0,.35,0,.34)
        flag(m,-.23,1.33 if era==1 else (1.71 if era==2 else 1.43),.01 if era!=2 else .22,.37,.227)


def bank(m, era):
    m.course("fieldstone",0,.0,1.65,1.40,0,.13,.13)
    m.course("wall",0,.15,1.40,.97,.128,.85,.092)
    front=-.335
    # Colonnade and stepped public entry share a treasury rotunda across eras.
    for i in range(3):
        y=.044*(i+1)
        m.block("fieldstone",(0,y/2,-.653+i*.049),(1.01-i*.028,y,.33-i*.034))
    m.entry(0,front,.266,.131,.431)
    for x in (-.55,-.249,.249,.55):
        if m.lod==2 and abs(x)<.4:
            continue
        if era==1:
            drum(m,"ivory",x,.13,-.527,.071,.590,.058,(8,6,4)[m.lod])
        else:
            pier(m,"metal" if era==3 else "wall",x,-.527,.13,.72,.091)
        if m.lod<2:
            m.block("wall",(x,.717,-.527),(.153,.080,.148))
    m.block("trim",(0,.770,-.448),(1.31,.113,.357))
    with m.transform(translate=(0,.827,-.444),rotate_y=90):
        m.prism_x((0,0,0),.394,1.42,.205,m.color("roof"),material="roof")
    m.block("wall",(0,.87,.15),(1.48,.077,1.04))
    if era==1:
        drum(m,"wall",0,.902,.17,.422,.258)
        m.dome((0,1.16,.17),.472,m.color("roof"),rings=(4,3,2)[m.lod],
               segments=(14,10,6)[m.lod],material="roof")
        drum(m,"gold",0,1.608,.17,.055,.106,.021)
    elif era==2:
        # Mansard-vault outline; four dormer vents make a counting-house crown.
        m.block("roof",(0,1.065,.15),(1.45,.316,1.03),taper=.29)
        m.block("trim",(0,1.24,.15),(1.054,.06,.76))
        drum(m,"wall",0,1.269,.162,.242,.132)
        m.dome((0,1.40,.162),.255,m.color("roof"),rings=(3,2,1)[m.lod],
               segments=(10,8,5)[m.lod],squash=.70,material="roof")
    else:
        drum(m,"glass",0,.905,.19,.499,.421,.412)
        drum(m,"ivory",0,1.325,.19,.44,.102,.395)
        drum(m,"glass",0,1.426,.19,.339,.231,.281)
        drum(m,"gold",0,1.657,.19,.304,.053,.260)
        m.dome((0,1.709,.19),.231,m.color("glass"),rings=(3,2,1)[m.lod],
               segments=(12,8,5)[m.lod],squash=.508,material="glass")
    if m.lod<2:
        badge(m,0,.911,-.648,.075)
        with m.transform(translate=(.705,0,.14),rotate_y=-90):
            for x in (-.235,.235):
                m.light(x,.496,0,.159,.264,False)
        if era==1:
            for i in range((10,6)[m.lod]):
                a=i*2*math.pi/(10,6)[m.lod]
                pier(m,"ivory",.414*math.cos(a),.17+.414*math.sin(a),.921,1.15,.052)
        else:
            for x in (-.51,.51):
                glazed(m,x,.485,front,.190,.222,2)
    if m.lod==0:
        for s in (-1,1):
            drum(m,"fieldstone",s*.65,.133,-.583,.080,.115,.099)
            m.dome((s*.65,.248,-.583),.103,m.color("leaf"),rings=2,segments=8,material="leaf")


def arch_roof(m,x,z,w,d,eave,rise,mat):
    """Shallow barrel shell, including the fascia at both platform ends."""
    steps=(10,6,4)[m.lod]
    def p(j,end,offset=0):
        a=j*math.pi/steps
        return x+w*.5*math.cos(a),eave+rise*math.sin(a)+offset,z+d*.5*end
    for i in range(steps):
        top=[p(i,-1),p(i+1,-1),p(i+1,1),p(i,1)]
        m.face(mat if i not in (0,steps-1) else "trim",top)
        m.face("trim",[(u,v-.033,s) for u,v,s in reversed(top)])
        for end in (-1,1):
            lip=[p(i,end),p(i,end,-.033),p(i+1,end,-.033),p(i+1,end)]
            m.face("ivory",lip if end==-1 else lip[::-1])


def rails(m, era):
    # Continuous paired rails across the forecourt with ballast and discrete sleepers.
    m.block("fieldstone",(0,.024,-.644),(1.85,.048,.434))
    if m.lod<2:
        n=(13,7)[m.lod]
        for j in range(n):
            x=-.849+1.698*j/(n-1)
            m.block("timber" if era==2 else "wall",(x,.060,-.644),(.042,.025,.337))
    for s in (-1,1):
        m.block("metal",(0,.079,-.644+s*.112),(1.87,.025,.034))


def butterfly(m, mat, z, width, depth, bottom, thickness):
    """One folded sheet with a mitered valley and continuous end fascias."""
    front=z-depth/2
    back=z+depth/2
    sections=[]
    for fraction in (-.5,0,.5):
        x=.343+fraction*width
        y=bottom+abs(fraction*width)*math.tan(math.radians(20))
        sections.append([(x,y,front),(x,y+thickness,front),
                         (x,y+thickness,back),(x,y,back)])
    for a,b in zip(sections,sections[1:]):
        for i in range(4):
            j=(i+1)%4
            m.face(mat,(a[i],a[j],b[j],b[i]))
    m.face(mat,sections[0][::-1])
    m.face(mat,sections[-1])


def station(m, era):
    rails(m,era)
    m.course("fieldstone",0,.118,1.86,1.10,0,.117,.084)
    hall(m,-.47,.298,.65,.59,.68,era,.117)
    m.entry(-.456,.003,.176,.119,.313)
    # Copper clock face set into a high booking-office gable.
    if m.lod<2:
        with m.transform(translate=(-.47,0,0)):
            m.block("wall",(0,.715,.001),(.28,.25,.10))
            m.block("trim",(0,.853,.001),(.31,.038,.128))
        badge(m,-.47,.725,-.052,.102,clock=True)
    if era==2:
        # A barrel vault of iron and glass covers the open passenger platform.
        for x in (-.047,.725):
            for z in (-.293,.427):
                pier(m,"metal",x,z,.117,.702,.052)
        arch_roof(m,.339,.07,.879,.891,.70,.267,"glass")
        if m.lod<2:
            m.block("trim",(.339,.671,-.293),(.818,.047,.055))
            for x in (-.046,.721):
                m.beam("metal",(x,.517,-.293),(.339,.680,-.293),.029)
                m.block("gold",(x,.171,-.293),(.088,.112,.089))
        m.flue(-.588,.426,.678,1.095,.106)
    else:
        # Tapered canopy petals spread out from the central queue spine.
        for z in (-.262,.422):
            pier(m,"ivory",.343,z,.117,.617,.105)
            for s in (-1,1):
                m.beam("metal",(.343,.496,z),(.343+s*.327,.817,z),.074)
        butterfly(m,"glass",.083,.955,.947,.652,.059)
        if m.lod==0:
            for zz in (-.265,-.020,.225,.470):
                butterfly(m,"ivory",zz,.943,.018,.709,.010)
    if m.lod<2:
        for x in (-.014,.585):
            for s in (-1,1):
                pier(m,"trim",x+s*.061,-.17,.117,.225,.029)
            m.block("banner",(x,.229,-.17),(.183,.026,.072))
        m.light(-.681,.439,.003,.086,.142,era==2)
    if m.lod==0:
        m.herbs(-.680,.338,.003,.135)
        flag(m,-.80,.115,-.258,.618,.210)
        # A departure board and a stack of cases on the paved platform.
        for x in (.523,.761):
            pier(m,"trim",x,.311,.117,.491,.031)
        m.block("trim",(.642,.464,.311),(.31,.182,.046))
        panel(m,"banner",.642,.464,.281,.267,.137)
        for j in range(3):
            panel(m,"ivory",.624,.498-j*.034,.274,.179-j*.031,.009)
        m.block("hide",(-.039,.173,.016),(.171,.112,.106))
        m.block("timber",(-.050,.258,.018),(.12,.061,.086))
        for x in (-.089,.007):
            m.block("gold",(x,.174,.013),(.015,.116,.109))
        for s in (-1,1):
            m.block("gold",(s*.614,.124,-.399),(.501,.014,.029))


def aircraft(m, x,z):
    """Civil apron shuttle, parked wheels-down. Wings taper into the fuselage."""
    with m.transform(translate=(x,.058,z),rotate_y=-19,scale=1.30):
        # Sections along the airframe give the nose and tail distinct tapers.
        sections=[(-.35,.012,.077),(-.26,.071,.101),(-.11,.084,.102),
                  (.14,.071,.102),(.31,.034,.124),(.37,.010,.143)]
        if m.lod==2:
            sections=[sections[i] for i in (0,2,3,5)]
        count=(8,6,4)[m.lod]
        rings=[[(r*math.cos(i*2*math.pi/count),h+r*.77*math.sin(i*2*math.pi/count),v)
                for i in range(count)] for v,r,h in sections]
        for n,(a,b) in enumerate(zip(rings,rings[1:])):
            for i in range(count):
                j=(i+1)%count
                role="glass" if n==1 and i<count//2 else "ivory"
                m.face(role,(a[i],a[j],b[j],b[i]))
        m.face("trim",rings[0][::-1])
        m.face("ivory",rings[-1])
        for s in (-1,1):
            for extent,root,tip,chord in ((.36,-.085,.076,.155),(.154,.234,.315,.064)):
                pts=[(s*.027,.102,root),(s*extent,.125,tip),
                     (s*extent,.123,tip+chord*.23),(s*.029,.101,root+chord)]
                pts=pts[::-1] if s==1 else pts
                m.face("banner",pts)
                m.face("trim",[(a,b-.012,c) for a,b,c in reversed(pts)])
            if m.lod<2:
                pier(m,"trim",s*.070,-.005,-.010,.081,.032)
        m.face("banner",[(0,.138,.225),(0,.287,.318),(0,.293,.348),(0,.147,.344)])
        m.face("banner",[(.006,.147,.344),(.006,.293,.348),(.006,.287,.318),(.006,.138,.225)])
        pier(m,"trim",0,-.118,-.010,.083,.035)


def airport(m, era):
    m.course("fieldstone",0,0,1.87,1.77,0,.044,.14)
    # The low glass concourse leaves the splayed control cabin unobstructed.
    m.block("glass",(.125,.288,.318),(1.24,.487,.56))
    for z in (.021,.614):
        m.block("trim",(.125,.505,z),(1.31,.066,.042))
    with m.transform(translate=(.125,.560,.318),rotate_z=-5):
        m.box((0,0,0),(1.42,.072,.71),m.color("metal"),bottom=True,material="metal")
        if m.lod<2:
            m.block("glass",(-.018,.051,.081),(.91,.035,.22))
            if m.lod==0:
                for x in (-.36,-.133,.094,.321):
                    m.block("ivory",(x,.070,.081),(.020,.012,.234))
    for x in (-.484,.667):
        pier(m,"ivory",x,.038,.046,.548-(x-.125)*.087,.053)
    drum(m,"wall",-.64,.045,.30,.131,.79,.106)
    drum(m,"trim",-.64,.835,.30,.119,.13,.205)
    drum(m,"glass",-.64,.965,.30,.211,.224,.242)
    drum(m,"roof",-.64,1.189,.30,.269,.081,.207)
    pier(m,"metal",-.64,.30,1.264,1.45,.024)
    m.entry(-.212,.038,.200,.045,.327,lintel="ivory")
    if m.lod<2:
        for x in (.008,.236,.461,.682):
            pier(m,"ivory",x,.025,.058,.499,.033,.054)
        for s in (-1,1):
            m.beam("metal",(-.64+s*.110,.991,.127),(-.64+s*.124,1.176,.107),.024)
    aircraft(m,.34,-.399)
    if m.lod<2:
        # Marshalling line and threshold bars stay clear of the passenger entrance.
        m.block("gold",(0,.048,-.731),(1.57,.008,.021))
        for x in (-.67,-.56,-.45):
            m.block("ivory",(x,.05,-.448),(.057,.010,.224))
    if m.lod==0:
        flag(m,-.76,.046,-.16,.57,.180)
        for s in (-1,1):
            drum(m,"ivory",.60+s*.101,.045,-.031,.046,.074)
            m.dome((.60+s*.101,.119,-.031),.047,m.color("leaf"),rings=2,segments=6,material="leaf")
        # White apron service cart with a blue luggage canopy against the terminal.
        m.block("metal",(-.429,.113,-.15),(.237,.077,.126))
        m.block("banner",(-.455,.172,-.149),(.143,.067,.117))
        for s in (-1,1):
            for t in (-1,1):
                m.block("trim",(-.429+s*.081,.078,-.149+t*.061),(.047,.067,.029))


BUILDERS={"Market":market,"Harbor":harbor,"Guildhall":guild,"Bank":bank,
          "RailStation":station,"Airport":airport}


def build(spec,lod):
    m=House(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        BUILDERS[spec.building_type](m,ERAS.index(spec.band))
    return m


def contact_sheets(entries):
    root=Path(__file__).resolve().parents[4]
    directory=(root/entries[0]["preview"]).parent
    eras=[(item["building_type"]+" · "+item["ages"][0],item["preview"]) for item in entries]
    rows={"eras":eras}
    for item in entries:
        for age,preview in item["age_previews"].items():
            rows.setdefault(age,[]).append((item["building_type"]+" · "+age,preview))
    for name,views in rows.items():
        columns=min(4,len(views))
        sheet=Image.new("RGB",(224*columns,246*math.ceil(len(views)/columns)),"#e8e2d5")
        labels=ImageDraw.Draw(sheet)
        for i,(label,path) in enumerate(views):
            x,y=i%columns*224,i//columns*246
            with Image.open(root/path) as pic:
                sheet.paste(pic,(x,y))
            labels.text((x+7,y+227),label,fill="#292720")
        sheet.save(directory/("contact_"+name+".png"))


if __name__ == "__main__":
    entries=[]
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        entries.append(entry)
        print(spec.id,entry["lod_triangles"],flush=True)
    contact_sheets(entries)
