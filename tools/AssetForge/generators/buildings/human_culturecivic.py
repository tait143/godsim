"""Human gathering places. Builder session 10, CultureCivic."""
from math import cos, sin, pi, radians
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House


PACKAGE = 'human_culturecivic'


def cylinder(m, mat, x, y, z, r, height, top=None, sides=None):
    m.cylinder((x, y, z), r, height, m.color(mat), segments=sides or (12, 8, 6)[m.lod],
               top_radius=top, material=mat)


def panel(m, mat, x, y, z, width, height):
    w, h = width/2, height/2
    m.face(mat, [(x+w,y-h,z),(x-w,y-h,z),(x-w,y+h,z),(x+w,y+h,z)])


def pennon(m, x, y, z, h, w):
    m.block('trim',(x,y+h/2,z),(.027,h,.027))
    top = y+h-.014
    cloth=[(x,top,z),(x+w*.57,top-.010,z-.03),(x+w,top-.054,z+.008),
           (x+w*.56,top-.096,z-.03),(x,top-.10,z)]
    for face in ((cloth[0],cloth[1],cloth[3],cloth[4]), (cloth[1],cloth[2],cloth[3])):
        m.face('banner',face)
        m.face('banner',[(a,b,c+.004) for a,b,c in reversed(face)])


def terrace(m, rx, rz, z, ranges, tiers, segments, inner, height, base=.035):
    """Annular grandstand. Radial end walls close each stair separately at the aisles."""
    def pt(radius, y, a):
        return (rx*radius*sin(a),y,z+rz*radius*cos(a))
    for start, end in ranges:
        angles = [radians(start+(end-start)*j/segments) for j in range(segments+1)]
        for j in range(tiers):
            r0, r1 = inner+(1-inner)*j/tiers, inner+(1-inner)*(j+1)/tiers
            low = base+height*j/tiers if j else base
            high = base+height*(j+1)/tiers
            for a,b in zip(angles, angles[1:]):
                m.face('wall',(pt(r1,high,a),pt(r1,high,b),pt(r0,high,b),pt(r0,high,a)))
                m.face('trim' if j else 'wall',(pt(r0,high,a),pt(r0,high,b),pt(r0,low,b),pt(r0,low,a)))
                if j == tiers-1:
                    m.face('wall',(pt(r1,base,a),pt(r1,base,b),pt(r1,high,b),pt(r1,high,a)))
            if end-start < 359:
                for a, reverse in ((angles[0],False),(angles[-1],True)):
                    face=[pt(r1,base,a),pt(r1,high,a),pt(r0,high,a),pt(r0,base,a)]
                    m.face('fieldstone',face[::-1] if reverse else face)


def circular_band(m, mat, cx, y, z, outer, inner, thickness, n, zscale=1):
    def pt(r,h,i):
        a=2*pi*i/n
        return cx+r*sin(a),h,z+zscale*r*cos(a)
    for i in range(n):
        j=(i+1)%n
        m.face(mat,(pt(outer,y,i),pt(outer,y,j),pt(outer,y+thickness,j),pt(outer,y+thickness,i)))
        m.face(mat,(pt(outer,y+thickness,i),pt(outer,y+thickness,j),pt(inner,y+thickness,j),pt(inner,y+thickness,i)))
        m.face(mat,(pt(inner,y+thickness,i),pt(inner,y+thickness,j),pt(inner,y,j),pt(inner,y,i)))
        m.face(mat,(pt(inner,y,i),pt(inner,y,j),pt(outer,y,j),pt(outer,y,i)))


def flame(m,x,y,z,r,h,tilt,mat):
    n=(7,5,3)[m.lod]
    def ring(scale, height, bend):
        return [(x+tilt*bend+r*scale*cos(2*pi*i/n),y+h*height,z+r*scale*sin(2*pi*i/n)) for i in range(n)]
    foot=ring(.52,0,0)
    belly=ring(1,.27,.21)
    neck=ring(.39,.65,-.22)
    tip=(x+tilt,y+h,z-.15*r)
    for i in range(n):
        j=(i+1)%n
        m.face(mat,(foot[j],foot[i],belly[i],belly[j]))
        m.face(mat,(belly[j],belly[i],neck[i],neck[j]))
        m.face(mat,(neck[j],neck[i],tip))


def campfire(m,era):
    stone = era == 'classical'
    r=.237 if stone else .223
    if stone:
        cylinder(m,'fieldstone',0,0,0,.32,.052,.30)
        cylinder(m,'trim',0,.052,0,r*.83,.083,r*1.09)
        circular_band(m,'wall',0,.13,0,r*1.10,r*.82,.045,(12,8,4)[m.lod])
        hearth=.148
    else:
        cylinder(m,'wall',0,0,0,.315,.027)
        hearth=.030
    if m.lod<2:
        stones=(10,7)[m.lod] if not stone else (8,6)[m.lod]
        for i in range(stones):
            a=i*2*pi/stones
            with m.transform(translate=(r*sin(a),hearth,r*cos(a)),rotate_y=i*360/stones):
                m.block('fieldstone',(0,.027,0),(.108,.064,.077),taper=.22)
    for i in range(3 if m.lod == 0 else 2):
        with m.transform(translate=(0,hearth+.033,0),rotate_y=i*60+14):
            m.beam('timber',(-.161,.000,0),(.166,.041,.018),.051)
    for x,z,rad,h,lean,mat in ((.012,.034,.101,.315,.074,'forge'),(-.085,-.016,.070,.230,-.047,'forge'),
                               (.077,-.052,.061,.198,.037,'emissive')):
        flame(m,x,hearth+.048,z,rad,h*(1.2 if stone else 1),lean,mat)
    for side in (-1,1):
        # At distance each pair of feet merges into a single carved trestle.
        with m.transform(translate=(side*.335,0,-.022),rotate_y=side*(12 if stone else 24)):
            if m.lod==2:
                m.block('fieldstone' if stone else 'wall',(0,.047,0),(.085,.094,.231),taper=.08)
            else:
                for dz in (-.125,.125):
                    m.block('fieldstone' if stone else 'wall',(0,.048,dz),(.093,.096,.059))
            m.block('wall' if stone else 'roof',(0,.106,0),(.14,.055,.373),taper=.08)
    if m.lod<2:
        if stone:
            pennon(m,-.278,.01,.242,.40,.14)
        elif m.lod == 0:
            with m.transform(translate=(-.07,.026,.37),scale=.68):
                m.basket(0,0,.084,.111)
    if m.lod==0:
        if stone:
            m.jar(.029,.363,0,.058,.119)
        else:
            for i in range(3):
                m.beam('timber',(.21+i*.034,.040,.221),(.182+i*.037,.046,.370),.035)
            with m.transform(translate=(.335,.14,-.022),rotate_y=24):
                m.block('banner',(0,0,.062),(.098,.015,.082))


def arena(m,era):
    future=era=='future'
    victorian=era=='industrial'
    side=(26,16,10)[m.lod]
    with m.transform(scale=(1,1,.85)):
        cylinder(m,'fieldstone',0,0,0,1.33,.05,sides=side)
        cylinder(m,'soil',0,.05,0,.88,.016,sides=side)
    h=.55 if future else .49
    terrace(m,1.29,1.11,0,[(-166,166)],(4,3,2)[m.lod],(22,15,8)[m.lod],.53,h,.05)
    # Paired gate pylons and a voice balcony above the service entrance.
    for s in (-1,1):
        x=s*.367
        m.course('wall',x,-.995,.23,.34,.046,h+.16,.045,.012)
        m.block('accent',(x,h+.167,-1.0),(.266,.050,.371))
        if m.lod<2:
            panel(m,'banner',x,h*.66,-1.173,.105,.21)
    m.block('trim',(0,h+.148,-.967),(.60,.074,.195))
    m.block('fieldstone',(0,.041,-1.102),(.43,.082,.365))
    if not future and not victorian:
        for s in (-1,1):
            m.hip_roof((s*.367,h+.19,-1.00),.28,.37,.15,m.color('roof'),overhang=0,material='roof')
    else:
        # The press box overlooks the competition floor on the far side.
        m.block('brick' if victorian else 'wall',(0,h+.165,.86),(.83,.26,.345))
        m.block('glass',(0,h+.309,.86),(.77,.086,.31))
        m.block('roof',(0,h+.355,.86),(.91,.056,.403))
        panel(m,'glass',0,h+.192,.685,.661,.123)
        if m.lod<2:
            for x in (-.211,0,.211):
                m.block('trim',(x,h+.192,.680),(.023,.128,.014))
            panel(m,'banner',0,h+.104,.681,.424,.049)
    if m.lod<2:
        count=12 if m.lod==0 else 8
        for j in range(count):
            a=radians(-150+j*300/(count-1))
            with m.transform(translate=(1.275*sin(a),.054,1.093*cos(a)),rotate_y=a*180/pi+180):
                if future:
                    m.block('metal',(0,h*.5,.006),(.066,h,.09),taper=.09)
                else:
                    # A blind arched passage, recessed within a projecting ashlar pier bay.
                    front=-.045
                    m.face('door',[(.094,.038,front),(-.094,.038,front),(-.094,.216,front),
                                   (-.063,.279,front),(0,.304,front),(.063,.279,front),(.094,.216,front)])
                    if m.lod==0:
                        m.bent_rail('wall',[(-.099,.207),(-.074,.281),(0,.316),(.074,.281),(.099,.207)],front-.009,.025)
        pennon(m,-.36,h+.20,-.98,.33,.20)
    if future:
        # Back-raked canopy with individually sloping facets.
        n=(10,6,4)[m.lod]
        for i in range(n):
            a,b=radians(-97+194*i/n),radians(-97+194*(i+1)/n)
            def rim(angle,radius,y):
                return (1.3*radius*sin(angle),y,1.1*radius*cos(angle))
            p,q=rim(a,1.02,h+.45),rim(b,1.02,h+.45)
            r,t=rim(b,.79,h+.37),rim(a,.79,h+.37)
            m.face('ivory',(p,q,r,t))
            m.face('banner',(t,r,(r[0],r[1]-.035,r[2]),(t[0],t[1]-.035,t[2])))
        for a in ((-91,-48,0,48,91) if m.lod<2 else (-78,0,78)):
            a=radians(a)
            m.beam('metal',(1.22*sin(a),h+.012,1.035*cos(a)),(1.28*sin(a),h+.437,1.082*cos(a)),.056)
    if m.lod==0:
        for s in (-1,1):
            cylinder(m,'gold',s*.393,.06,-1.277,.039,.13,.053,sides=6)


def stadium(m,era):
    future=era=='future'
    m.course('fieldstone',0,0,2.62,2.73,0,.062,.26)
    m.course('grass',0,0,1.34,2.32,.060,.074,.14)
    for s in (-1,1):
        # Seated terraces run the touchlines. Rounded gable canopies on scissor struts
        # turn the arena's round pit into a recognizably different sporting ground.
        n=(4,3,2)[m.lod]
        for i in range(n):
            width=.57/n
            height=.12+.39*(i+1)/n
            m.block('wall' if i%2 else 'banner',(s*(.65+width*(i+.5)),(.061+height)/2,0),
                    (width,height-.061,2.01))
        nz=(10,6,3)[m.lod]
        for j in range(nz):
            a,b=-1.16+2.32*j/nz,-1.16+2.32*(j+1)/nz
            def pts(z):
                y=.69+(.34 if future else .20)*(1-(z/1.18)**2)
                return (s*1.29,y,z),(s*(.72 if future else .79),y+.115,z*.95)
            p,q=pts(a); r,t=pts(b)
            top=[p,q,t,r]
            if s<0: top=top[::-1]
            m.face('metal' if future else 'roof',top)
            m.face('banner',[(c,d-.032,e) for c,d,e in reversed(top)])
            for u,v in ((p,r),(t,q)):
                face=[u,v,(v[0],v[1]-.032,v[2]),(u[0],u[1]-.032,u[2])]
                m.face('metal',face if s>0 else face[::-1])
        for z in ((-.95,-.39,.39,.95) if m.lod<2 else (-.85,.85)):
            y=.69+(.34 if future else .20)*(1-(z/1.18)**2)
            m.beam('trim',(s*1.20,.48,z),(s*1.265,y-.012,z),.060)
            m.beam('metal',(s*1.205,.535,z),(s*.90,y+.064,z*.97),.036)
    for end in (-1,1):
        z=end*.86
        for s in (-1,1):
            m.block('ivory',(s*.216,.153,z),(.029,.158,.030))
        m.block('ivory',(0,.235,z),(.46,.03,.032))
    if m.lod<2:
        for s in (-1,1):
            m.block('ivory',(s*.51,.079,0),(.015,.007,1.815))
            m.block('ivory',(0,.081,s*.90),(1.025,.006,.014))
        m.block('ivory',(0,.080,0),(1.015,.007,.014))
        circular_band(m,'ivory',0,.0815,0,.155,.139,.004,(14,8)[m.lod])
        m.block('wall',(0,.255,1.11),(.39,.39,.15))
        m.block('door',(0,.514,1.10),(.62,.218,.11))
        panel(m,'emissive',0,.52,1.042,.43,.065)
        for s in (-1,1):
            pennon(m,s*.79,.067,-1.14,.38,.155)
        if future:
            for sign in (-1,1):
                m.beam('metal',(sign*.218,.378,1.125),(sign*.333,.725,1.146),.049)
            m.block('glass',(0,.739,1.14),(.77,.128,.11))
            panel(m,'ascension',0,.739,1.079,.56,.041)
    if m.lod==0:
        # Two ticket pedestals frame the open approach behind the home end.
        for side in (-1,1):
            m.block('wall',(side*.335,.149,-1.223),(.147,.175,.13))
            m.block('banner',(side*.335,.248,-1.224),(.16,.035,.15))
            panel(m,'door',side*.335,.182,-1.293,.067,.036)
        for s in (-1,1):
            # Paired lamps on the roof feet, swivelled over play.
            for z in (-.91,.86):
                y=.69+(.34 if future else .20)*(1-(z/1.18)**2)
                m.beam('metal',(s*1.26,y-.008,z),(s*1.348,y+.266,z),.036)
                with m.transform(translate=(s*1.347,y+.27,z),rotate_z=s*19):
                    m.block('trim',(0,0,0),(.083,.107,.19))
                    m.block('emissive',(-s*.045,.007,0),(.022,.07,.149))


def drape(m,x,z,s):
    # The gathers pull the opening away from the centre of the stage.
    for j in range((5,3,2)[m.lod]):
        n=(5,3,2)[m.lod]
        def at(k,band):
            u=k/n
            widths=(.31,.115,.215)
            return (x-s*u*widths[band],(.98,.46,.221)[band],
                    z-.026*(k%2)-(.011 if band==1 else 0))
        for b in range(2):
            face=[at(j,b),at(j+1,b),at(j+1,b+1),at(j,b+1)]
            m.face('cloth',face if s<0 else face[::-1])


def theater(m,era):
    future=era=='future'
    retro=era=='industrial'
    m.course('fieldstone',0,.070,1.78,1.69,0,.055,.15)
    m.course('trim',0,.38,1.54,.80,.052,.185,.049)
    m.block('timber',(0,.191,.363),(1.51,.045,.81))
    back_y, back_h = (.579, .731) if future else (.586, .745)
    m.block('door',(0,back_y,.659),(1.42,back_h,.155))
    for s in (-1,1):
        x=s*.667
        m.block('wall',(x,.573,.29),(.148,.729,.385))
        if m.lod<2:
            m.block('accent',(x,.882,.282),(.181,.059,.424))
        drape(m,s*.60,.199,s)
    if retro:
        m.block('brick',(0,.849,.617),(1.55,.65,.35))
        panel(m,'door',0,.77,.438,1.14,.53)
        m.prism_x((0,1.174,.617),1.63,.438,.17,m.color('roof'),material='roof')
        m.block('trim',(0,1.015,.284),(1.565,.12,.37))
        for i in range(5 if m.lod==0 else 3):
            x=-.61+1.22*i/(4 if m.lod==0 else 2)
            m.block('emissive',(x,1.01,.091),(.048,.045,.018))
    elif future:
        # Rising shell behind the actors, folded in sections over an open-air stage.
        n=(8,6,4)[m.lod]
        # Outer eaves rest directly on the two stage piers at every mesh resolution.
        # The terminal bays are level, keeping the bearing width independent of the facets.
        xs=[-.86]+[-.665+1.33*j/n for j in range(n+1)]+[.86]
        for x0,x1 in zip(xs,xs[1:]):
            def pt(x,front):
                u=max(0,1-(x/.665)**2)
                return (x,.958+(.47 if front else .12)*u,.004-.07*u if front else .778)
            a,b,c,d=pt(x0,True),pt(x1,True),pt(x1,False),pt(x0,False)
            m.face('metal',(d,c,b,a))
            m.face('banner',[(x,y-.032,z) for x,y,z in (a,b,c,d)])
            m.face('metal',(b,a,(a[0],a[1]-.032,a[2]),(b[0],b[1]-.032,b[2])))
    else:
        m.block('ivory',(0,.951,.364),(1.61,.11,.74))
        m.prism_x((0,1.006,.386),1.69,.80,.30,m.color('roof'),material='roof')
        if m.lod<2:
            panel(m,'accent',0,.965,-.012,.94,.028)
    terrace(m,.87,1.15,.305,[(100,166),(194,260)],(3,2,1)[m.lod],(6,4,3)[m.lod],.53,.274,.058)
    for k in range(3 if m.lod<2 else 1):
        m.block('wall',(0,.086+.019*k,-.71+.102*k),(.256,.058+.038*k,.109))
    if m.lod<2:
        # Blue-and-gold comedy mask is a crest, raised above the curtain.
        cylinder_mat='gold' if not future else 'ascension'
        if future:
            m.beam('metal',(0,1.363,.027),(0,1.060,.112),.029)
        elif not retro:
            m.block('trim',(0,.965,-.002),(.14,.124,.048))
        crest_z=.088 if retro else .111 if future else -.028
        with m.transform(translate=(0,.979 if not future else 1.057,crest_z),rotate_x=-90):
            cylinder(m,cylinder_mat,0,0,0,.087,.031,sides=8)
        for s in (-1,1):
            m.block('trim',(s*.605,.459,.170),(.150,.030,.045))
        # Painted hill flats and a rising moon form a small touring stage set.
        front=.429 if retro else .572
        panel(m,'banner',0,.580,front,.91,.454)
        m.face('leaf',[(.448,.354,front-.007),(-.446,.354,front-.007),
                       (-.446,.415,front-.007),(-.169,.581,front-.007)])
        m.face('trim',[(.449,.354,front-.013),(-.154,.354,front-.013),(.202,.537,front-.013),(.449,.426,front-.013)])
        with m.transform(translate=(-.01,.678,front-.010),rotate_x=-90):
            cylinder(m,'gold',0,0,0,.060,.010,sides=(9,6)[m.lod])
        if not future:
            pennon(m,-.632,1.097,.38,.344,.169)
    if m.lod==0:
        for s in (-1,1):
            m.block('trim',(s*.52,.234,-.004),(.091,.045,.055))
            m.block('emissive',(s*.52,.260,-.005),(.068,.024,.04))
        m.jar(.764,-.419,.057,.057,.134)


def forum(m,era):
    future=era=='future'
    retro=era=='industrial'
    m.course('fieldstone',0,0,2.70,1.68,0,.106,.106)
    m.course('wall',0,.392,2.32,.64,.104,.80,.072)
    m.entry(0,.072,.35,.106,.46)
    for s in (-1,1):
        x=s*.978
        m.course('wall',x,-.099,.383,1.007,.105,.505,.043)
        if future:
            m.block('glass',(x,.576,-.100),(.36,.145,.972))
            m.block('ivory',(x,.667,-.10),(.45,.05,1.09))
        else:
            m.hip_roof((x,.502,-.10),.474,1.09,.211,m.color('roof'),overhang=0,material='roof')
        if m.lod<2:
            for z in (-.448,-.115,.203):
                with m.transform(translate=(x+s*.194,0,z),rotate_y=-90*s):
                    panel(m,'glass' if future else 'door',0,.327,0,.156,.214)
    # An accessible public loggia with a central ceremonial flight of stairs.
    for i in range(3 if m.lod<2 else 1):
        m.block('wall',(0,.032+.025*i,-.871+.103*i),(.91-.047*i,.064+.05*i,.141))
    for x in (-.55,-.27,.27,.55) if m.lod<2 else (-.53,.53):
        if future:
            m.beam('metal',(x,.102,-.375),(x*1.10,.77,-.395),.090)
        else:
            cylinder(m,'wall',x,.104,-.391,.069,.590,.056,sides=(9,6,4)[m.lod])
            m.block('ivory',(x,.714,-.39),(.159,.055,.155))
            if m.lod==0:
                m.block('fieldstone',(x,.146,-.392),(.157,.077,.158))
    m.block('ivory',(0,.767,-.14),(1.336,.094,.672))
    if future:
        m.block('glass',(0,.87,.416),(2.24,.17,.584))
        m.block('ivory',(0,.979,.415),(2.37,.056,.68))
        with m.transform(translate=(.02,.861,-.122),rotate_z=-6):
            m.box((0,0,0),(1.44,.078,.667),m.color('glass'),material='glass',bottom=True)
        if m.lod<2:
            m.block('accent',(0,.797,-.49),(1.29,.026,.034))
            for s in (-1,1):
                for x in (.745, .931):
                    panel(m,'glass',s*x,.562,.066,.145,.245)
                    if m.lod==0:
                        m.block('metal',(s*x,.426,.047),(.16,.024,.041))
                        m.block('ivory',(s*x,.689,.047),(.16,.025,.041))
        with m.transform(translate=(-.689,.998,.438),scale=(1,1,.82)):
            m.dome((0,0,0),.231,m.color('glass'),rings=(3,2,1)[m.lod],
                   segments=(11,8,6)[m.lod],material='glass')
    else:
        with m.transform(translate=(0,.80,-.15),rotate_y=90):
            m.prism_x((0,0,0),.68,1.35,.27,m.color('roof'),material='roof')
        m.hip_roof((0,.799,.402),2.44,.724,.264,m.color('roof'),overhang=0,material='roof')
        if retro:
            # A brick clock lantern projects above the council chamber.
            m.course('brick',.54,.435,.394,.375,.80,1.39,.038)
            m.block('ivory',(.54,1.38,.435),(.426,.077,.405))
            m.hip_roof((.54,1.416,.435),.468,.429,.201,m.color('roof'),overhang=0,material='roof')
            with m.transform(translate=(.54,1.244,.239),rotate_x=-90):
                cylinder(m,'ivory',0,0,0,.101,.018,sides=(12,8,6)[m.lod])
            if m.lod<2:
                m.beam('trim',(.54,1.244,.214),(.54,1.313,.214),.016)
                m.beam('trim',(.54,1.245,.212),(.492,1.221,.212),.018)
        elif m.lod<2:
            for sign in (-1,1):
                panel(m,'banner',sign*.752,.531,.068,.121,.261)
    if m.lod<2:
        pennon(m,0,1.006 if future else 1.052,.41,.405,.201)
        for x in (-.792,.796):
            m.block('wall',(x,.152,-.66),(.241,.109,.223),taper=.10)
            m.dome((x,.201,-.66),.111,m.color('leaf'),segments=(9,6)[m.lod],
                   rings=(2,1)[m.lod],squash=1.32,material='leaf')
        if m.lod==0:
            for x in (-.54,-.27,.27,.54):
                panel(m,'gold',x,.770,-.478,.043,.047)
            for s in (-1,1):
                panel(m,'banner',s*1.00,.371,-.61,.114,.16)


def tavern(m,era):
    future=era=='future'
    pub=era=='industrial'
    h=.80 if future else .84 if pub else .704
    wall='brick' if pub else 'wall'
    m.course('fieldstone',-.06,.075,1.41,1.139,0,.111,.085)
    m.course(wall,-.06,.075,1.35,1.07,.106,h,.034)
    front=-.461
    m.entry(-.32,front,.244,.109,.389)
    m.light(.135,.415,front,.248,.174,shutters=not future)
    if future:
        m.block('glass',(-.06,.876,.075),(1.31,.162,1.031))
        m.block('ivory',(-.06,.977,.075),(1.46,.065,1.145))
        cylinder(m,'metal',-.30,1.0,.22,.294,.044)
        m.dome((-.30,1.044,.22),.272,m.color('glass'),rings=(3,2,1)[m.lod],
               segments=(12,8,6)[m.lod],squash=.68,material='glass')
    else:
        with m.transform(translate=(-.06,0,.075),rotate_y=90):
            m.roof(1.17,1.51,h,.54 if not pub else .303,'roof','wall',hip=.13 if pub else .025,ridge_mat='trim')
        m.flue(.315,.362,h-.07,h+.62,.142)
        if pub:
            m.block('wall',(-.30,1.075,-.253),(.345,.292,.21))
            m.prism_x((-.30,1.219,-.253),.418,.302,.121,m.color('roof'),material='roof')
            panel(m,'glass',-.30,1.116,-.361,.208,.137)
        else:
            x=-.321
            with m.transform(translate=(x,.549,-.497)):
                m.roof(.53,.405,0,.363,'roof','plaster',hip=.0,ridge_mat='trim')
            for s in (-1,1):
                m.block('trim',(x+s*.210,.324,-.646),(.052,.439,.057))
    # Private beer garden attaches to the taproom. A projecting striped awning shades the tables.
    for z in (-.369,.380):
        m.block('metal' if future else 'timber',(.848,.296,z),(.043,.592,.043))
    with m.transform(translate=(.707,.626,.01),rotate_z=-15):
        n=(5,3,1)[m.lod]
        for j in range(n):
            m.box((0,0,-.435+.87*(j+.5)/n),(.363,.038,.87/n),m.color('banner' if j%2==0 else 'ivory'),
                  material='banner' if j%2==0 else 'ivory',bottom=True)
    # The tankard identifies the inn, including the modern rooftop restaurant.
    m.beam('trim',(.37,.716,-.449),(.399,.714,-.665),.035)
    m.block('metal',(.399,.655,-.650),(.022,.117,.022))
    cylinder(m,'gold',.398,.399,-.655,.089,.202,.098,sides=(10,7,5)[m.lod])
    cylinder(m,'ivory',.398,.594,-.655,.106,.045,.092,sides=(10,7,5)[m.lod])
    if m.lod<2:
        m.bent_rail('gold',[(.464,.55),(.526,.563),(.546,.530),(.533,.475),(.470,.466)],-.654,.031)
        for z in (-.211,.243):
            cylinder(m,'trim',.735,0,z,.031,.159,sides=6)
            cylinder(m,'timber',.735,.158,z,.130,.029,sides=8)
        if future:
            with m.transform(translate=(.618,0,.05),rotate_y=-90):
                panel(m,'glass',0,.40,-.002,.564,.276)
                if m.lod==0:
                    for dx in (-.160,.16):
                        m.block('ivory',(dx,.40,-.009),(.025,.28,.021))
            with m.transform(translate=(-.322,.569,-.501),rotate_x=9):
                m.box((0,0,0),(.371,.045,.210),m.color('glass'),material='glass',bottom=True)
        if not future:
            for x in (-.677,.563):
                m.block('trim',(x,.41,front),(.051,.595,.056))
            m.block('trim',(-.06,h-.05,front),(1.31,.066,.052))
        if m.lod==0:
            m.herbs(.134,.283,front,.302)
            with m.transform(translate=(-.63,0,-.423)):
                m.jar(0,-.181,0,.074,.164)
            if future:
                pennon(m,.270,1.007,.310,.318,.160)
            else:
                for x in (-.54,.439):
                    m.beam('trim',(x-.081,.399,front-.018),(x+.082,.653,front-.018),.036)


def arcology(m,era):
    # A planted residential acropolis. Terraces wind down from the beacon to two
    # neighbourhood towers. Each terrace bears directly on its glazed drum below.
    m.course('fieldstone',0,.015,2.39,2.12,0,.122,.267)
    m.course('ivory',0,.01,2.23,1.97,.119,.411,.238,.072)
    m.block('wall',(0,.269,-.947),(.557,.302,.176),taper=.09)
    m.entry(0,-1.035,.29,.122,.229,lintel='gold')
    si=(14,10,6)[m.lod]
    def tower(x,z,start,r,h,levels):
        floor=h/levels
        for i in range(levels):
            y=start+i*floor
            radius=r*(1-.115*i)
            cylinder(m,'glass',x,y,z,radius*.93,floor*.88,radius*.82,sides=si)
            cylinder(m,'ivory',x,y+floor*.87,z,radius*.86,floor*.13,radius*1.08,sides=si)
            if m.lod<2:
                cylinder(m,'grass',x,y+floor+.001,z,radius*1.055,.016,sides=si)
                if i<levels-1 and (i%2==0 or m.lod==0):
                    for j in range((6,3)[m.lod]):
                        a=2*pi*j/(6 if m.lod==0 else 3)
                        rr=radius*.927
                        m.dome((x+rr*sin(a),y+floor+.016,z+rr*cos(a)),radius*.104,
                               m.color('leaf'),segments=5,rings=1,squash=1.58,material='leaf')
        top=start+h
        roof=r*(1-.115*(levels-1))
        m.dome((x,top+.014,z),roof*.83,m.color('glass'),rings=(3,2,1)[m.lod],segments=si,
               squash=.74,material='glass')
        if m.lod<2:
            for a in (40,147,264):
                ang=radians(a)
                m.beam('gold',(x+r*.91*sin(ang),start,z+r*.91*cos(ang)),
                       (x+roof*.78*sin(ang),top,z+roof*.78*cos(ang)),.047)
        return top+roof*.60
    top=tower(-.09,.299,.411,.514,1.924,4)
    tower(-.669,-.326,.411,.354,1.006,3)
    tower(.647,-.264,.411,.399,1.301,3)
    if m.lod<2:
        for s in (-1,1):
            m.block('ivory',(s*.36,1.089,.026),(.40,.122,.165))
            m.block('glass',(s*.36,1.191,.026),(.40,.082,.158))
    # Four raking supports carry a luminous annular crown around the beacon.
    cy=top+.171
    for a in (30,120,210,300) if m.lod<2 else (40,220):
        a=radians(a)
        m.beam('gold',(-.09+.16*sin(a),top-.091,.299+.16*cos(a)),
               (-.09+.40*sin(a),cy+.027,.299+.40*cos(a)),.052)
    circular_band(m,'gold',-.09,cy,.299,.448,.364,.056,(18,12,8)[m.lod])
    cylinder(m,'metal',-.09,top-.039,.299,.072,.182,.051)
    m.dome((-.09,top+.143,.299),.083,m.color('ascension'),rings=1,segments=si,
           squash=1.56,material='ascension')
    if m.lod<2:
        # A row of windows and a stepped street door scale the inhabited podium.
        for s in (-1,1):
            for x in (.44,.69):
                panel(m,'glass',s*x,.259,-.936,.161,.127)
                if m.lod==0:
                    m.block('gold',(s*x,.334,-.934),(.170,.021,.027))
        for s in (-1,1):
            m.block('ivory',(s*.171,.255,-1.074),(.038,.268,.043))
        m.block('gold',(0,.399,-1.054),(.394,.031,.117))
        for j in range(2):
            m.block('fieldstone',(0,.034+.028*j,-1.184+.066*j),(.41-.031*j,.068+.056*j,.079))
    if m.lod==0:
        for x in (-.751,.739):
            m.herbs(x,.297,-.922,.277)
        pennon(m,.47,1.717,-.235,.247,.151)


BUILDERS={'Arcology':arcology,'Arena':arena,'Campfire':campfire,'Forum':forum,
          'Stadium':stadium,'Tavern':tavern,'Theater':theater}


def build(spec,lod):
    m=House(spec,lod)
    BUILDERS[spec.building_type](m,spec.band)
    return m


def contact_sheets(entries):
    from PIL import Image, ImageDraw
    root=Path(__file__).resolve().parents[4]
    folder=(root/entries[0]['preview']).parent
    models=[(e['building_type']+' '+e['id'].split('_')[-2],e['preview']) for e in entries]
    ages=[(e['building_type']+' '+age,path) for e in entries for age,path in e['age_previews'].items()]
    for filename,views,width,columns in [('contact-models',models,174,5),('contact-ages',ages,75,7)]:
        cell=max(width,120)
        height=width+26
        rows=(len(views)+columns-1)//columns
        sheet=Image.new('RGB',(cell*columns,height*rows),(242,238,226))
        draw=ImageDraw.Draw(sheet)
        for i,(label,path) in enumerate(views):
            x,y=(i%columns)*cell,(i//columns)*height
            with Image.open(root/path) as view:
                sheet.paste(view.resize((width,width),Image.Resampling.LANCZOS),(x+(cell-width)//2,y))
            draw.text((x+4,y+width+3),label,fill='black')
        sheet.save(folder/(filename+'.png'))


if __name__=='__main__':
    entries=[]
    for spec in package_models(PACKAGE):
        high,medium,low=(build(spec,i) for i in range(3))
        entry=publish(high,spec.metadata(),PACKAGE,lods=(medium,low),preview_size=240)
        print(spec.id,entry['lod_triangles'],flush=True)
        entries.append(entry)
    contact_sheets(entries)
