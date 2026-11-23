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
        for z in (-.88,0,.88): arch(m,z)
        zs=(-1.286,-.44,.44,1.286)
        for z in zs:
            m.course('fieldstone',0,z,.58,.205,0,.109,.048)
            m.solid('fieldstone',0,z,.51,.170,.109,.246)
        h=1.106
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
