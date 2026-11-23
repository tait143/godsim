"""Marble porch houses, slate cruck lodges and lantern-domed potters' houses."""
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from generators.buildings.orc_housesancient import Lodge, generate

PACKAGE='orc_housesclassical'


def shutter(m,x,y,z,age):
    with m.transform(translate=(x,y,z),rotate_y=-90):
        m.block('trim',(0,0,0),(.207,.136,.022))
        m.block('emissive',(0,.004,-.013),(.164,.100,.012))
        if m.lod==0:
            m.block('timber',(0,.005,-.024),(.022,.105,.015))
            m.block('roof_tile' if age!='medieval' else 'timber',(0,-.074,-.020),
                    (.225,.035,.080))
            for s in (-1,1):
                with m.transform(translate=(s*.112,.001,-.012),rotate_y=s*21):
                    m.block('cloth' if age=='renaissance' else 'roof',(0,0,0),(.047,.107,.016))


def column(m,x,z,ground,top):
    height=top-ground
    if m.lod==0:
        stations=[(x,ground,z,.050),(x,ground+.033,z,.057),(x,ground+.049,z,.041),
                  (x,top-.054,z,.032),(x,top-.039,z,.043),(x,top-.024,z,.046),(x,top,z,.055)]
        m.path(stations,['fieldstone','ivory','plaster','ivory','cloth','ivory'],6)
    else:
        m.pole('ivory',(x,ground,z),(x,top,z),.046,.039,5 if m.lod==1 else 4)


def tile_house(m):
    v=m.v
    x,z=(.016,.036) if v else (-.018,.049)
    w,d=(.342,.244) if v else (.307,.272)
    # The roof projects above a colonnaded stone porch. The far end rests on solid masonry.
    eave=.372 if v else .336
    tip=.648 if v else .711
    cut=.20 if v else .50
    roof_w,roof_d=(.420,.368) if v else (.414,.391)
    def row(y,hw,hd,cz=z):
        return (y,hw,hd,x,cz)
    if m.lod<2:
        m.shell([row(0,roof_w-.010,roof_d-.008),row(.059,roof_w-.019,roof_d-.017)],['fieldstone'],cut)
    base=.059 if m.lod<2 else 0
    m.shell([row(base,w,d),row(eave,w*.973,d*.975)],['plaster'],cut)
    rings=[row(eave-.013,roof_w,roof_d),row(eave+.023,roof_w,roof_d),
           row(eave+.060,roof_w*.943,roof_d*.943),
           row(tip,.145 if v else .046,.056 if v else .046,z+.019)]
    roles=['ivory','cloth','roof_tile']
    if m.lod==2:
        rings,roles=[rings[0],rings[-1]],['roof_tile']
    m.shell(rings,roles,cut)
    # Wide terminal tile at either end of the longer ridge, or one forked finial on the round roof.
    crest_y,rw,rd,rx,rz=rings[-1]
    m.block('gold',(rx,crest_y+.013,rz),(2*rw,.027,rd*1.55),taper=.13)
    for s in (-1,1):
        m.horn(rx+s*rw*.67,crest_y+.005,rz,s,.47 if v else .61,skin='ivory',socket='gold')
    porch_z=z-roof_d+.038
    for s in (-1,1):
        column(m,x+s*(.277 if v else .192),porch_z,base,eave+.014)
    m.doorway(x+(.085 if v else 0),z-d-.065,base,.257,.209,body='timber',horns=False)
    if m.lod<2:
        shutter(m,x+w-.005,base+.145,z+.025,m.age)
        m.block('fieldstone',(x+(.085 if v else -.037),.023,porch_z-.052),(.265,.046,.089),taper=.12)
    if m.lod==0:
        m.drum(x+(-.267 if v else .267),-.413,.058,.098)
        if v:
            column(m,x+.339,z+.152,base,eave+.012)


def cruck_roof(m,x,z,height,width,depth,v):
    # Three ridge stations sweep upwards at the gables like the upturned bows of a timber sledge.
    def section(end):
        lift=abs(end)*(.072 if v else .056)
        half_width=width*(1-.025*abs(end))
        points=[(-half_width,-.018),(-half_width,.027),(-half_width*.43,height*.67+lift*.30),
                (0,height+lift),(half_width*.43,height*.67+lift*.30),
                (half_width,.027),(half_width,-.018)]
        if m.lod==2:
            points=[points[i] for i in (0,1,3,5,6)]
        return [(a,b,depth*end) for a,b in points]
    with m.transform(translate=(x,.407 if v else .459,z),rotate_y=90 if v else 0):
        sections=[section(i) for i in (-1,0,1)]
        for a,b in zip(sections,sections[1:]):
            for i in range(len(a)-1):
                role='timber' if i in (0,len(a)-2) else 'slate'
                m.face(role,(a[i],b[i],b[i+1],a[i+1]))
        for end,n in ((sections[0],-1),(sections[-1],1)):
            m.face('roof' if v else 'timber',end if n==-1 else end[::-1])
            cy=height+.057
            if m.lod<2:
                m.pole('hide',(0,cy-.064,n*(depth-.023)),(0,cy+.007,n*(depth+.022)),.041,.028)
            for s in (-1,1):
                m.horn(s*.010,cy-.024,n*depth,s,.52,skin='ivory')
            # The inlaid red lozenge remains readable on the distant triangular gable.
            pts=[(0,height*.17,n*(depth+.009)),(-.060,height*.40,n*(depth+.009)),
                 (0,height*.67,n*(depth+.009)),(.060,height*.40,n*(depth+.009))]
            m.face('cloth',pts if n==-1 else pts[::-1])


def cruck_house(m):
    v=m.v
    x,z=(.014,.064) if v else (-.017,.045)
    w,d=(.356,.263) if v else (.303,.311)
    eave=.407 if v else .459
    base=.073 if m.lod<2 else 0
    if m.lod<2:
        m.shell([(0,w+.034,d+.034,x,z),(base,w+.029,d+.029,x,z)],['fieldstone'],.26)
    m.shell([(base,w,d,x,z),(eave,w*.988,d*.990,x,z)],['plaster' if not v else 'fieldstone'],.16)
    cruck_roof(m,x,z,.278 if v else .311,(d if v else w)+.069,(w if v else d)+.064,v)
    ex=x+(.086 if v else -.031)
    front=z-d-.079
    m.doorway(ex,front,.026,.289,.235,body='timber')
    if m.lod<2:
        shutter(m,x+w-.003,.226,z+.002,m.age)
        for s in (-1,1):
            m.pole('trim',(x+s*w*.77,.069,z-d-.008),(x+s*w*.78,eave+.02,z-d+.005),.031,.026)
    if m.lod==0:
        for s in (-1,1):
            m.pole('timber',(x+w-.012,.091,z+s*d*.73),(x+w-.009,eave-.02,z+s*.090),.026,.023)
        m.drum(x-.239,-.370,.064,.107)
    bx=x-.139 if v else x-.157
    by=eave+.19
    with m.transform(translate=(bx,by,z+.066),rotate_z=-5):
        m.block('brick',(0,.064,0),(.087,.23,.093))
        if m.lod<2:
            m.block('trim',(0,.188,0),(.107,.027,.114))


def lantern_dome(m):
    v=m.v
    x,z=(.028,.053) if v else (-.021,.053)
    w,d=(.359,.263) if v else (.325,.319)
    cut=.29 if v else .56
    eave=.342 if v else .325
    tip=.605 if v else .681
    base=.061 if m.lod<2 else 0
    if m.lod<2:
        m.shell([(0,w+.025,d+.029,x,z),(base,w+.019,d+.023,x,z)],['fieldstone'],cut)
    m.shell([(base,w,d,x,z),(eave,w*.987,d*.987,x,z)],['plaster'],cut)
    r,q=w+.038,d+.042
    ridge=.105 if v else .084
    courses=[(eave-.012,r,q,x,z),(eave+.029,r,q,x,z),
             (eave+.091,r*.967,q*.967,x,z),(tip-.105,r*.73,q*.73,x,z),
             (tip,ridge,ridge*.88,x+.018*v,z+.015)]
    roles=['trim','roof_tile','roof_tile','roof_tile']
    if m.lod==2:
        courses=([courses[0],courses[3],courses[-1]] if not v else
                 [courses[0],courses[2],courses[-1]])
        roles=['roof_tile']*(len(courses)-1)
    m.shell(courses,roles,cut)
    cx,cz=courses[-1][3:]
    n=(8,6,5)[m.lod]
    cup=.095 if v else .075
    m.round('trim',cx,tip,cz,ridge*.82,cup,.79*ridge,n)
    if m.lod<2:
        # Vent slats divide the dark lantern. The light is contained below its projecting copper cap.
        for s in (-1,1):
            m.pole('gold',(cx+s*.044,tip,cz-.050),(cx+s*.042,tip+cup,cz-.048),.012,.010,4)
    m.round('roof',cx,tip+cup,cz,ridge*1.16,.082 if v else .064,.012,n)
    m.doorway(x+(-.1 if v else .011),z-d-.086,.020,.263,.232,body='roof',horns=True)
    if m.lod<2:
        shutter(m,x+w-.005,.196,z+.018,m.age)
        if v:
            m.block('ivory',(x+.181,.152,z-d+.002),(.124,.148,.035),taper=.14)
            m.block('emissive',(x+.181,.151,z-d-.018),(.079,.097,.010))
    if m.lod==0:
        # Small glazed water jars clustered at the potter's threshold.
        for px,pz,r,h in ((.300,-.369,.071,.116),(.171,-.379,.053,.079)):
            m.round('roof_tile',px,0,pz,r*.69,h*.73,r,7)
            m.round('roof_tile',px,h*.73,pz,r,h*.27,r*.60,7)
            m.round('turquoise',px,h,pz,r*.64,.014,r*.65,7)


def build(spec,lod):
    m=Lodge(spec,lod)
    {'classical':tile_house,'medieval':cruck_house,'renaissance':lantern_dome}[m.age](m)
    return m


if __name__=='__main__':
    generate(PACKAGE,build)
