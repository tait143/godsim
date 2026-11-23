"""Human public works: a springhead, water bridges, grain reserves and generators."""
from math import cos, pi, sin
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.human_learninghealth import Mason, publish_package

PACKAGE = 'human_infrastructure'


def vessel(m,x,z,profile,n=None):
    """Revolve an outside-to-inside section, preserving the open mouth and its lip."""
    n=n or (16,10,6)[m.lod]
    def ring(r,y):
        return [(x+r*cos(2*pi*i/n),y,z+r*sin(2*pi*i/n)) for i in range(n)]
    for (a,lo,_),(b,hi,mat) in zip(profile,profile[1:]):
        lower,upper=ring(a,lo),ring(b,hi)
        for i in range(n):
            j=(i+1)%n
            m.face(mat,[lower[j],lower[i],upper[i],upper[j]])
    return ring(profile[-1][0],profile[-1][1])


def pail(m,x,y,z,r=.084):
    top=vessel(m,x,z,[(r*.72,y,None),(r,y+.132,'timber'),(r*.78,y+.132,'metal'),
                       (r*.72,y+.08,'trim')],n=(8,6,4)[m.lod])
    m.face('water',top[::-1])
    if m.lod<2:
        m.bent_rail('metal',[(x-r*.89,y+.117),(x-r*.59,y+.206),
                             (x+r*.42,y+.215),(x+r*.89,y+.117)],z,.014)


def well(m):
    e=m.era
    if e==1:
        # The spring issues from a hillside shrine into a public dipping basin.
        m.course('fieldstone',0,.015,.89,.85,0,.063,.10)
        m.solid('wall',-.012,.205,.67,.25,.063,.60)
        m.hip(-.012,.205,.77,.36,.60,.19,'roof')
        for x in (-.27,.25):
            m.drum('ivory',x,.068,.056,.067,.57,.047)
            if m.lod==0:
                m.drum('gold',x,.068,.061,.52,.56)
        inside=vessel(m,-.012,-.15,[(.214,.063,None),(.272,.189,'wall'),
                          (.275,.255,'ivory'),(.221,.255,'trim'),(.193,.156,'wall')])
        m.face('water',inside[::-1])
        m.beam('gold',(-.012,.425,.12),(-.012,.403,-.107),.045)
        m.beam('water',(-.012,.397,-.106),(-.012,.158,-.114),.016)
        if m.lod<2:
            m.front_panel('banner',-.012,.411,.075,.147,.146)
        if m.lod==0:
            pail(m,.326,.063,-.238,.052)
            m.herbs(-.20,.285,.078,.13)
        return
    if e==2:
        # Cast-iron village pump on its brick pedestal; a long rocking lever
        # clears both the column and the shallow trough beneath the spout.
        m.course('fieldstone',0,0,.84,.79,0,.056,.09)
        x,z=-.14,.09
        m.course('brick',x,z,.32,.29,.056,.33,.046)
        m.drum('metal',x,z,.116,.33,.403,.103)
        m.drum('metal',x,z,.085,.403,.79,.066)
        m.drum('gold',x,z,.094,.79,.858,.070)
        m.cupola('metal',x,z,.071,.858,.055)
        m.bent_rail('metal',[(x,.793),(.11,.90),(.327,.59)],z,.048)
        with m.transform(translate=(x,0,0),rotate_y=90):
            m.bent_rail('metal',[(-z,.64),(.175,.654),(.197,.59)],0,.048)
        m.course('wall',x,-.195,.37,.37,.056,.185,.046)
        m.solid('water',x,-.195,.293,.286,.185,.19)
        if m.lod<2:
            m.drum('gold',x,z,.087,.55,.585)
            m.beam('trim',(x+.044,.75,z),(.102,.857,z),.024)
        if m.lod==0:
            pail(m,.26,.056,-.20,.071)
            m.front_panel('banner',x,.155,-.058,.124,.093)
        return
    if e==3:
        # A sealed extraction head supplies a raised pressure tank and filling station.
        m.course('wall',0,.01,.9,.79,0,.075,.10)
        x,z=.14,.14
        m.drum('metal',x,z,.125,.075,.323,.102)
        m.drum('ivory',x,z,.125,.323,.444,.265)
        m.drum('glass',x,z,.265,.444,.734,.24)
        m.cupola('ivory',x,z,.24,.734,.131)
        m.drum('accent',x,z,.059,.855,.908,.036)
        m.solid('metal',-.254,-.133,.143,.174,.075,.45)
        m.beam('ivory',(-.254,.423,-.133),(-.254,.423,-.29),.075)
        m.beam('metal',(-.254,.262,-.134),(.14,.262,.14),.056)
        m.course('metal',-.254,-.297,.266,.171,.075,.14,.025)
        if m.lod<2:
            m.front_panel('turquoise',-.254,.286,-.226,.073,.087)
            m.drum('gold',x,z,.253,.646,.672,.252)
        if m.lod==0:
            for a in range(6):
                t=2*pi*a/6
                m.beam('ivory',(x+.255*cos(t),.445,z+.255*sin(t)),
                       (x+.231*cos(t),.731,z+.231*sin(t)),.023)
            m.herbs(.267,.103,-.288,.165)
        return
    n=(14,7 if e==2 else 10,6)[m.lod]
    m.course('fieldstone',0,0,.88,.85,0,.052,.15)
    profile=[(.322,.052,None),(.350,.101,'fieldstone'),(.32,.125,'trim'),
             (.323,.36,'wall'),(.36,.38,'trim'),(.355,.43,'wall'),
             (.256,.43,'ivory'),(.244,.24,'fieldstone')]
    inside=vessel(m,0,-.015,profile if m.lod<2 else [profile[i] for i in (0,3,5,6,7)],n=n)
    m.face('water',inside[::-1])
    top=(.77,.87,.84,.91)[e]
    if e<2:
        for s in (-1,1):
            m.solid('timber',s*.306,.026,.063,.072,.107,top)
        m.solid('trim',0,.026,.74,.083,top-.068,top+.006)
        if e==0:
            m.hip(0,.022,.84,.52,top,.235,'thatch')
        else:
            with m.transform(translate=(0,top,.025),rotate_y=90):
                m.roof(.57,.85,0,.32,'roof','wall',ridge_mat='trim')
    else:
        m.solid('metal',-.305,.035,.092,.12,.075,top+.052)
        m.solid('wall',.306,.035,.082,.095,.18,top+.016)
        m.solid('metal',0,.035,.73,.115,top-.063,top+.01)
        if e==2:
            m.hip(0,.035,.81,.39,top+.009,.108,'slate')
            m.drum('metal',-.301,.033,.063,top+.014,top+.198,.055)
        else:
            m.cupola('glass',0,.03,.349,top,.195)
            m.drum('accent',0,.03,.061,top+.174,top+.235,.039)
    # The drum winds its rope directly over the water, and a spare pail sits on the coping.
    with m.transform(translate=(-.345,.633,.025),rotate_z=-90):
        m.drum('timber' if e<2 else 'metal',0,0,.052,0,.716,n=(12,8,5)[m.lod])
        if m.lod<2: m.drum('hide',0,0,.077,.295,.476)
    m.beam('trim',(.035,.62,.023),(.035,.28,.023),.015)
    if m.lod<2:
        m.beam('metal',(.372,.632,.025),(.372,.534,-.045),.023)
        m.beam('trim',(.362,.534,-.045),(.425,.534,-.045),.036)
        pail(m,.20,.426,-.229,.064)
    if m.lod==0:
        m.herbs(-.245,.089,-.258,.145)
        m.pennon(-.31,.06,top+.017,.14)
        for side in (-1,1):
            m.beam('trim',(side*.306,top-.16,.027),(side*.20,top,.027),.031)


def arch(m,z):
    # Extrude a solid spandrel around a true vaulted opening. Clear spans lie along Z.
    spring=.22; top=1.105; r=.364; rise=.68; half=.232
    n=(12,8,4)[m.lod]
    def section(i):
        angle=pi*i/n
        return z-r*cos(angle),spring+rise*sin(angle)
    for i in range(n):
        a,ya=section(i); b,yb=section(i+1)
        for sign in (-1,1):
            x=sign*half
            pts=[(x,ya,a),(x,top,a),(x,top,b),(x,yb,b)]
            m.face('wall',pts if sign==1 else pts[::-1])
            if m.lod<2:
                # Each voussoir has its own outward reveal and a small mortar joint.
                delta=.012
                u=pi*(i+delta)/n; v=pi*(i+1-delta)/n
                def edge(t,rad,height):
                    return (sign*(half+.015),spring+height*sin(t),z-rad*cos(t))
                outer = r + (.065 if m.lod==1 else .077)
                q=[edge(u,r,rise),edge(u,outer,rise+.080),edge(v,outer,rise+.080),edge(v,r,rise)]
                m.face('ivory' if i==n//2 else 'fieldstone',q if sign==1 else q[::-1])
        m.face('fieldstone',[(-half,ya,a),(half,ya,a),(half,yb,b),(-half,yb,b)])
    for side in (-1,1):
        end=z+side*r
        m.solid('wall',0,end+side*.038,.469,.076,.04,top)


def distant_bearing(m,era,z,h):
    # The distant bridge keeps the open legs. Omit end faces at the footing and trough.
    def panel(mat,base,top):
        xy=[*base,*reversed(top)]
        front=[(x,y,z-.061) for x,y in xy]
        back=[(x,y,z+.061) for x,y in xy]
        m.face(mat,front[::-1])
        m.face(mat,back)
        for i in (1,3):
            j=(i+1)%4
            m.face(mat,(front[i],front[j],back[j],back[i]))
    if era==2:
        for s in (-1,1):
            panel('metal',[(s*.249-.047,.106),(s*.249+.047,.106)],
                  [(s*.144-.047,h),(s*.144+.047,h)])
    else:
        mid=h*.49
        panel('wall',[(-.103,.106),(.103,.106)],[(-.103,mid),(.103,mid)])
        panel('ivory',[(-.103,mid),(0,mid)],[(-.286,h),(-.181,h)])
        panel('ivory',[(0,mid),(.103,mid)],[(.181,h),(.286,h)])


def aqueduct(m):
    e=m.era
    if e==1:
        # The castellum receives a high feeder and sends water into a lower
        # distribution channel. Side doors give access to the settling chamber.
        z=.534
        if m.lod<2:
            m.course('fieldstone',0,z,.808,.751,0,.153,.099)
        else:
            m.solid('fieldstone',0,z,.76,.707,0,.153)
        m.course('wall',0,z,.703,.637,.148,1.153,.101,.043)
        if m.lod<2:
            m.course('trim',0,z,.789,.711,1.147,1.215,.104)
        else:
            m.solid('trim',0,z,.729,.653,1.147,1.215)
        m.course('fieldstone',0,z,.764,.687,1.207,1.308,.105)
        m.course('water',0,z,.607,.535,1.307,1.322,.077)
        for lo,hi,ht in ((-1.415,.255,.480),(.807,1.415,1.258)):
            cz=(lo+hi)/2
            for pz in ((lo+.115,cz,hi-.087) if hi<1 else (hi-.087,)):
                if m.lod==2:
                    m.solid('wall',0,pz,.370,.146,0,ht)
                else:
                    m.course('fieldstone',0,pz,.474,.194,0,.088,.032)
                    m.course('wall',0,pz,.370,.146,.084,ht,.026)
            m.solid('wall',0,cz,.477,hi-lo,ht,ht+.089)
            m.solid('water',0,cz,.308,hi-lo-.012,ht+.087,ht+.116)
            for sx in (-.197,.197):
                m.solid('wall',sx,cz,.077,hi-lo+.004,ht+.087,ht+.159)
        with m.transform(translate=(.339,0,z),rotate_y=-90):
            m.entry(0,-.016,.218,.118,.367)
            if m.lod<2: m.windows(0,-.023,.811,.38,.151,2)
        if m.lod<2:
            for side in (-1,1):
                m.solid('trim',side*.347,z,.046,.440,.583,.649)
            m.front_panel('door',0,.621,z-.304,.237,.137)
        if m.lod==0:
            for y in (.733,.849,.965,1.081):
                m.solid('metal',-.338,z,.052,.149,y,y+.023)
            for pz in (.303,.748):
                for side in (-1,1):
                    m.block('fieldstone',(side*.336,.344,pz),(.109,.443,.117),taper=.18)
            m.pennon(-.25,.68,1.297,.227)
            for pz in (-1.215,-.69,-.17):
                m.solid('trim',.203,pz,.042,.119,.320,.485)
        return
    else:
        h=1.02 if e==2 else 1.15
        for z in (-1.28,-.426,.426,1.28):
            m.course('fieldstone',0,z,.68,.23,0,.106,.053)
            if m.lod==2:
                distant_bearing(m,e,z,h)
            elif e==2:
                for s in (-1,1):
                    m.beam('metal',(s*.249,.101,z),(s*.144,h,z),.080)
                m.solid('brick',0,z,.301,.19,.106,.39)
            else:
                m.solid('wall',0,z,.24,.18,.105,h*.55)
                for s in (-1,1):
                    m.beam('ivory',(0,h*.44,z),(s*.248,h,z),.102)
            if m.lod<2: m.solid('banner',0,z,.245,.194,.215,.263)
            if m.lod==0:
                for s in (-1,1):
                    m.solid('trim' if e==2 else 'metal',s*(.15 if e==2 else .23),z,
                            .12,.15,h-.070,h+.005)
        if e==2:
            for sx in (-.192,.192):
                m.beam('metal',(sx,h-.193,-1.28),(sx,h-.193,1.28),.050)
                count=(12,6,3)[m.lod]
                zigzag=[(-1.28+2.56*i/count,h-(.015 if i%2 else .185)) for i in range(count+1)]
                with m.transform(translate=(sx,0,0),rotate_y=-90):
                    m.bent_rail('trim',zigzag,0,.037)
    m.solid('trim' if e==1 else 'metal',0,0,.592,2.806,h,h+.094)
    m.solid('water',0,0,.465,2.799,h+.094,h+.108)
    for s in (-1,1):
        m.solid('roof' if e==3 else 'wall',s*.263,0,.070,2.814,h+.091,h+.193)
        if m.lod<2: m.solid('ivory',s*.263,0,.089,2.821,h+.192,h+.218)
    if m.lod<2:
        # A keeper's hatch in the front footing, and the iron rungs to the inspection ledge.
        z=-1.31
        m.solid('fieldstone',0,z,.39,.19,.01,.45)
        m.entry(-.03,z-.095,.146,.039,.256)
        if m.lod==0:
            for s in (-1,1):
                m.beam('metal',(.267,.091,-1.24+s*.068),(.267,h+.182,-1.24+s*.068),.018)
            for i in range(7):
                y=.17+i*.143
                m.beam('metal',(.268,y,-1.31),(.268,y,-1.17),.018)
    if m.lod==0: m.pennon(-.261,-1.18,h+.205,.25)


def freight(m,x,z,base,w,h):
    m.solid('timber',x,z,w,w*.75,base,base+h)
    if m.lod==0:
        f=z-w*.375
        for s in (-1,1):
            m.solid('trim',x+s*(w/2-.019),f-.005,.027,.014,base+.008,base+h-.008)
        m.beam('hide',(x-w*.44,base+.013,f-.012),(x+w*.43,base+h-.014,f-.012),.026)
        m.solid('ivory',x+w*.16,f-.015,w*.204,.012,base+h*.48,base+h*.72)


def storehouse(m):
    e=m.era
    if e==1:
        # A merchant's hoist loft and an open wagon court share a loading floor.
        m.course('fieldstone',.06,.025,1.73,.80,0,.115,.075)
        x,z=.50,.045
        m.hall(x,z,.62,.59,1.12,base=.12,rise=.47,windows=False)
        front=z-.295
        for y in (.116,.675):
            m.front_panel('door',x,y,front-.006,.34,.319)
            m.solid('trim',x,front-.023,.396,.083,y+.309,y+.348)
        for s in (-1,1):
            m.beam('timber',(x+s*.255,.13,front-.017),(x+s*.251,1.11,front-.017),.047)
        if m.lod<2:
            m.beam('trim',(x,1.13,front+.024),(x,1.14,-.424),.063)
            m.beam('metal',(x,1.13,-.413),(x,.60,-.413),.021)
            freight(m,x,-.378,.43,.23,.17)
        m.hip(-.305,.133,.99,.54,.565,.244)
        for px in (-.712,.087):
            for pz in (-.074,.344):
                m.solid('timber',px,pz,.071,.071,.113,.569)
        freight(m,-.49,.13,.115,.32,.245)
        if m.lod<2:
            freight(m,-.209,.158,.115,.207,.171)
            m.windows(x,.346,.45,.38,.23,2)
            for px in (-.712,.087):
                m.beam('trim',(px,.397,-.076),(px+(.122 if px<0 else -.122),.569,-.076),.039)
        if m.lod==0:
            for side in (-1,1):
                with m.transform(translate=(x+side*.315,0,z),rotate_y=-90*side):
                    m.windows(0,-.006,.69,.33,.194,2)
                    m.beam('timber',(-.24,.226,-.013),(.24,.516,-.013),.038)
            for y in (.304,.895):
                m.beam('timber',(x-.148,y-.075,front-.016),(x+.144,y+.077,front-.016),.034)
            m.pennon(x+.168,z,1.36,.19)
        return
    m.course('fieldstone',0,0,1.89,.89,0,.035,.064)
    # The covered loading gallery has a paired sliding door facing the hand-cart apron.
    z=.106; width=(1.21,1.31,1.36,1.24)[e]; h=(.56,.70,.78,.86)[e]
    f=m.hall(-.20,z,width,.53,h,rise=(.40,.44,.32,.19)[e])
    m.solid('fieldstone',-.205,f-.085,.93,.26,.002,.116)
    for s in (-1,1):
        x=-.23+s*.123
        m.front_panel('timber' if e<2 else 'slate',x,.118,f-.013,.237,h*.54)
        if m.lod<2: m.beam('trim',(x-.099,.129,f-.029),(x+.1,.11+h*.53,f-.029),.027)
    m.solid('trim',-.23,f-.039,.59,.067,.12+h*.54,.157+h*.54)
    for s in (-1,1):
        m.solid('trim' if e<2 else 'metal',-.20+s*.45,f-.145,.042,.046,.11,h-.083)
    m.hip(-.20,f-.098,1.01,.267,h-.091,.095,'roof' if e<2 else 'metal')
    freight(m,.615,-.17,.012,.29,.22)
    if m.lod<2:
        freight(m,.575,.111,.012,.24,.21)
        freight(m,.612,-.16,.232,.207,.153)
    if e==1: m.flue(-.63,.26,h-.03,1.16,.108)
    if e==2:
        m.hall(-.16,.105,.52,.27,.995,base=.775,rise=.15,windows=False)
        if m.lod<2: m.windows(-.15,-.036,.874,.39,.08,2)
    if e==3:
        m.solid('wall',.52,.20,.39,.35,.012,.84)
        m.hip(.52,.20,.445,.407,.84,.13,'glass')
        if m.lod<2: m.windows(.52,.019,.46,.26,.21,2)
    if m.lod==0:
        m.herbs(-.69,.24,f-.007,.19)
        m.pennon(-.62,.12,h+.20,.21)


def granary(m):
    e=m.era
    if e==1:
        # A masonry horreum: ventilated, buttressed storage nave and an open
        # sack-weighing porch at the short end.
        x,z=-.14,.16
        f=m.hall(x,z,.90,1.07,.83,rise=.41,windows=False)
        for sx in (-1,1):
            for bz in (-.27,.13,.53):
                m.block('fieldstone',(x+sx*.47,.325,bz),(.16,.52,.12),taper=.18)
                if m.lod==0:
                    with m.transform(translate=(x+sx*.455,0,bz),rotate_y=-90*sx):
                        m.windows(0,-.004,.64,.12,.087,1)
        m.entry(x,f,.31,.103,.47)
        for sx in (-.45,.17):
            m.drum('timber',sx,-.59,.040,.065,.57,.032)
        m.hip(x,-.49,.82,.40,.568,.14)
        for j,(bx,bz) in enumerate(((.565,-.26),(.56,.087),(.63,.42))):
            if m.lod==2 and j==1: continue
            if m.lod==2:
                m.drum('roof_tile',bx,bz,.108,.066,.284,.091,n=5)
            else:
                m.jar(bx,bz,.066,.130,.27)
        if m.lod==0:
            m.pennon(-.51,-.58,.614,.25)
        return
    if e==3:
        # Steep-sided dispensing hoppers sit in a braced rack. Grain descends
        # to the collection bay beneath them from an enclosed top distributor.
        for j,(x,z,r) in enumerate(((-.36,.21,.305),(.29,.29,.256))):
            for s in (-1,1):
                for t in (-1,1):
                    m.solid('metal',x+s*r*.66,z+t*r*.69,.069,.069,.064,.78)
            m.drum('metal',x,z,r*.22,.34,.69,r)
            m.drum('ivory',x,z,r,.69,1.02,r*.98)
            m.cupola('roof',x,z,r*.98,1.02,.13)
            if m.lod<2:
                m.drum('accent',x,z,r*1.008,.78,.827)
                if m.lod==0:
                    for s in (-1,1):
                        m.beam('metal',(x-r*.66,.13,z+s*r*.69),(x+r*.66,.64,z+s*r*.69),.033)
        m.course('wall',-.01,-.418,.69,.43,.064,.39,.071)
        m.entry(-.01,-.633,.29,.070,.265,lintel='metal')
        m.solid('metal',.642,.06,.215,.28,.065,1.29)
        m.solid('glass',.642,.06,.19,.25,1.29,1.44)
        m.beam('metal',(.64,1.33,.06),(-.36,1.14,.21),.089)
        if m.lod==0:
            m.windows(.642,-.085,.42,.14,.42,1)
            m.pennon(.63,.06,1.431,.23)
            for y in (.32,.49,.66,.83,1.00,1.17):
                m.solid('gold',.757,.06,.042,.144,y,y+.027)
        return
    if e<2:
        # A timber loft on mushroom-capped staddles keeps the store above damp ground.
        x,z=-.20,.16; w,d=1.18,.91
        for sx in (-.46,.46):
            for sz in (-.33,.33):
                m.drum('fieldstone',x+sx,z+sz,.137,.05,.216,.089,n=(8,6,4)[m.lod])
                m.drum('wall',x+sx,z+sz,.163,.216,.274,.14,n=(8,6,4)[m.lod])
        f=m.hall(x,z,w,d,.925 if e==0 else 1.11,base=.282,rise=.45 if e==0 else .49,windows=False)
        m.entry(x,f,.279,.286,.37)
        if m.lod<2:
            for sign in (-1,1):
                m.front_panel('door',x+sign*.392,.50,f-.007,.181,.201)
                for j in range((4,2)[m.lod]):
                    m.solid('timber',x+sign*.392,f-.021,.205,.027,.509+j*.13/(4,2)[m.lod],.531+j*.13/(4,2)[m.lod])
            for s in (-1,1): m.beam('trim',(x+s*.535,.317,f-.01),(x+s*.273,.81,f-.01),.045)
        for i in range((3,3,2)[m.lod]):
            count=(3,3,2)[m.lod]
            run=.225/count
            m.solid('timber',x,f-.225+(i+.5)*run,.34,run,.053,.054+.23*(i+1)/count)
        bx,bz=.60,.11
        m.drum('timber',bx,bz,.235,.058,.65,.217)
        m.drum('thatch' if e==0 else 'roof',bx,bz,.273,.65,.94,0)
        if e==1:
            m.hall(-.25,.21,.44,.36,1.53,base=1.12,rise=.24,windows=False)
            m.front_panel('door',-.25,1.33,.023,.22,.116)
        if m.lod==0:
            with m.transform(translate=(0,.065,0)):
                m.basket(.46,-.43,.103,.134)
                m.basket(.68,-.31,.086,.109)
    else:
        for i,(x,z,r,h) in enumerate(((-.43,.33,.33,1.24),(.28,.40,.30,1.39),(.50,-.21,.256,1.01))):
            m.drum('fieldstone',x,z,r*1.02,.064,.18)
            m.drum('metal' if e==2 else 'wall',x,z,r,.18,h)
            m.drum('roof' if e==2 else 'glass',x,z,r*1.07,h,h+.20,.060)
            if m.lod<2:
                for band in (.36,.77,h-.053):
                    m.drum('trim' if e==2 else 'banner',x,z,r*1.016,band,band+.036)
            if m.lod==0:
                m.drum('gold',x,z,.067,h+.20,h+.232)
        f=m.hall(-.32,-.37,.71,.48,.49,rise=.22)
        m.porch(-.32,f,.069,.35,.36)
        if e==3:
            m.beam('metal',(-.43,1.36,.33),(.274,1.50,.40),.099)
            m.beam('metal',(.28,1.47,.40),(.50,1.16,-.21),.075)
        if m.lod==0: freight(m,.08,-.67,.064,.203,.160)


def cooling(m,x,z,r,h):
    profile=[(r,.067,None),(r*.96,h*.23,'wall'),(r*.71,h*.48,'wall'),
             (r*.61,h*.71,'wall'),(r*.74,h,'wall'),(r*.78,h+.053,'trim'),
             (r*.61,h+.053,'ivory'),(r*.55,h*.86,'door')]
    inner=vessel(m,x,z,profile if m.lod<2 else [profile[i] for i in (0,3,4,6,7)])
    m.face('door',inner[::-1])


def power_plant(m):
    e=m.era
    f=m.hall(-.47,-.32,1.28,1.26,.77 if e==2 else .87,rise=.37)
    m.porch(-.60,f,.075,.47,.47)
    if m.lod<2: m.windows(-.167,f-.008,.33,.37,.22,2)
    cooling(m,.52,.51,.553,1.69 if e==2 else 1.48)
    if e==2:
        x,z=-.69,.67
        profile=[(.192,.063,None),(.197,.23,'brick'),(.155,.29,'trim'),(.123,1.71,'brick'),
                 (.14,1.80,'metal'),(.096,1.80,'fieldstone'),(.092,1.62,'door')]
        inner=vessel(m,x,z,profile)
        m.face('door',inner[::-1])
        m.solid('brick',x,.31,.32,.53,.064,.45)
    else:
        # A second low collector faces the service aisle, carried on its own glazed stanchion.
        x,z=-.70,.64
        m.drum('metal',x,z,.187,.064,.67,.131)
        with m.transform(translate=(x,.72,z),rotate_x=-19):
            m.box((0,0,0),(.83,.075,.65),m.color('metal'),material='metal',bottom=True)
            for i in range((3,2,1)[m.lod]):
                count=(3,2,1)[m.lod]; w=.77/count
                m.solid('glass',-.385+w*(i+.5),0,w-.018,.60,.038,.054)
        if m.lod<2: m.pennon(-.96,.61,.74,.27)
    if m.lod<2:
        for px,z in ((.62,-.55),(.39,-.31)):
            m.beam('metal',(.04,.35,z),(px,.35,z),.112)
            m.beam('metal',(px,.35,z),(px,.27,.146),.118)
        m.solid('wall',.50,-.49,.47,.61,.063,.225)
    if m.lod==0:
        m.herbs(-.21,.268,f-.009,.25)
        m.pennon(-.97,-.28,.95,.29)


def fusion(m):
    x,z=.13,.20
    m.drum('fieldstone',x,z,1.00,.063,.19,.95)
    m.drum('wall',x,z,.91,.19,.46,.81)
    m.drum('gold',x,z,.84,.455,.524)
    m.cupola('glass',x,z,.786,.524,.54)
    # Inclined hoops are held in bronze shoes on a raised octagonal containment vessel.
    for angle,r in ((-30,.76),(60,.65)):
        with m.transform(translate=(x,.50,z),rotate_y=angle):
            n=(18,12,6)[m.lod]
            for s in (-1,1):
                m.solid('metal',s*r,0,.12,.18,.013,.27)
            for i in range(n):
                a=pi*i/n; b=pi*(i+1)/n
                def point(t,radius,depth):
                    return (radius*cos(t),.244+radius*1.15*sin(t),depth)
                front=[point(a,r,-.049),point(b,r,-.049),point(b,r+.071,-.049),point(a,r+.071,-.049)]
                back=[(u,v,.049) for u,v,_ in front]
                m.face('gold' if angle<0 else 'ivory',front)
                m.face('gold' if angle<0 else 'ivory',back[::-1])
                m.face('metal',[front[1],front[0],back[0],back[1]])
                m.face('ascension',[front[3],front[2],back[2],back[3]])
    f=m.hall(-.54,-.70,.91,.61,.54,rise=.20)
    m.porch(-.54,f,.073,.40,.42)
    if m.lod<2: m.windows(-.265,f-.007,.265,.21,.15,1)
    if m.lod==0:
        m.pennon(-.82,-.66,.70,.26)
        for sign in (-1,1):
            m.drum('metal',.75,-.73+.15*sign,.082,.063,.34,.072)
            m.cupola('turquoise',.75,-.73+.15*sign,.075,.34,.073)


BUILDERS={'Well':well,'Aqueduct':aqueduct,'Storehouse':storehouse,'Granary':granary,
          'PowerPlant':power_plant,'FusionReactor':fusion}


def build(spec,lod):
    m=Mason(spec,lod)
    if spec.building_type in ('PowerPlant','FusionReactor'):
        m.course('fieldstone',0,0,2.67,2.49,0,.065,.16)
    elif spec.building_type=='Granary':
        m.course('fieldstone',0,0,1.83,1.61,0,.065,.12)
    BUILDERS[spec.building_type](m)
    return m


if __name__=='__main__':
    publish_package(PACKAGE,build)
