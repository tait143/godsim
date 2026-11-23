"""Dwarven clan assemblies from the open bronze moot to the suspended wheel chamber."""
from math import cos, pi, sin
from .hearth_masonry import block, dome, flank, gable, gallery, pane, profile, roundwork, steps, terrace


def council_gate(m,x,front,sill,width,height):
    m.entry(x,sill,front,width,height)
    steps(m,x,front-.012,width*1.24,sill,.22)
    if m.lod<2:
        m.rune(x,sill+height*.86,front-.077,.14)


def bronze_moot(m):
    # The paved meeting circle lies between two banking tiers. The speaker's booth is roofed.
    roundwork(m,'fieldstone',-.02,-.095,.705,0,.10,.694)
    for x in (-.579,.539):
        terrace(m,'brick',x,.023,.378,.983,.099,.253,taper=.93)
        terrace(m,'wall',x,.099,.283,.782,.251,.365,taper=.90)
        terrace(m,'accent',x,.105,.301,.782,.364,.406,taper=.91)
    terrace(m,'brick',-.041,.409,.792,.53,.094,.587)
    gallery(m,'timber',-.038,.125,.863,.338,.104,.468,3)
    gable(m,'roof',-.039,.352,1.041,.806,.615,.292)
    m.entry(-.040,.125,.142,.345,.379)
    terrace(m,'trim',-.04,-.15,.388,.276,.10,.191)
    steps(m,-.020,-.725,.527,.101,.164)
    for x in (-.584,.547):
        roundwork(m,'trim',x,-.386,.096,.24,.438,.076)
        if m.lod<2:
            m.lamp(x,.435,-.386,.100)
    if m.lod==0:
        for x in (-.59,.55):
            for z in (-.143,.16,.393):
                m.block('gold',x,.402,z,.062,.022,.068)
        m.rune(-.04,.743,-.053,.211)


def barrel_arcade(m,front,back,inner,outer,base,role):
    # A continuous barrel over the hearing chamber. Its open intrados remains visible at distance.
    n=(13,9,7)[m.lod]
    def at(i,r,z):
        a=pi*i/n
        return (r*cos(a),base+r*sin(a),z)
    for i in range(n):
        a,b,c,d=at(i,inner,front),at(i,outer,front),at(i+1,outer,front),at(i+1,inner,front)
        e,f,g,h=at(i,inner,back),at(i,outer,back),at(i+1,outer,back),at(i+1,inner,back)
        m.face(role,(d,c,b,a))
        m.face(role,(e,f,g,h))
        m.face('metal' if role=='trim' else role,(c,g,f,b))
        m.face('wall' if role=='slate' else role,(a,e,h,d))
        if i==0:
            m.face(role,(a,b,f,e))
        if i==n-1:
            m.face(role,(d,h,g,c))


def iron_hold(m):
    # Council meets under a single monumental iron-banded barrel. A broad flight opens
    # directly into its hearing aisle, with paired benches facing the rear council doors.
    terrace(m,'fieldstone',0,-.017,1.763,1.472,0,.137,cut=.13)
    for side in (-1,1):
        terrace(m,'fieldstone',side*.677,.137,.331,.945,.137,.446,taper=.95)
        terrace(m,'trim',side*.675,.137,.351,.991,.445,.502)
    barrel_arcade(m,-.332,.598,.530,.812,.502,'slate')
    barrel_arcade(m,-.374,-.327,.489,.848,.502,'trim')
    barrel_arcade(m,.594,.641,.519,.835,.502,'fieldstone')
    terrace(m,'wall',0,.524,1.175,.257,.138,.943)
    m.entry(-.195,.138,.390,.350,.486)
    m.entry(.195,.138,.390,.350,.486)
    for side in (-1,1):
        terrace(m,'wall',side*.413,.016,.168,.563,.137,.238)
        terrace(m,'slate',side*.413,.016,.185,.584,.238,.269)
        if m.lod<2:
            m.lamp(side*.638,.137,-.501,.123)
            m.rune(side*.669,.369,-.359,.146)
    steps(m,0,-.753,.942,.137,.232)
    if m.lod==0:
        m.rune(0,.804,.379,.263)
        for x in (-.72,-.42,.42,.72):
            m.block('metal',x,.143,-.600,.095,.023,.097)


def classical_loggia(m):
    # A deep two-aisled colonnade opens straight into the public square. The record wing lies behind.
    terrace(m,'fieldstone',-.027,-.018,1.74,1.399,0,.127)
    terrace(m,'plaster',-.016,.315,1.391,.625,.127,.555)
    terrace(m,'trim',-.016,.315,1.48,.683,.554,.637)
    gable(m,'roof_tile',-.016,.315,1.57,.74,.637,.203)
    for z in (-.446,-.181):
        for x in (-.651,-.223,.220,.643):
            roundwork(m,'ivory',x,z,.087,.127,.603,.066,segments=(10,6,4))
            if m.lod==0 or (m.lod==1 and z<-.3):
                roundwork(m,'accent',x,z,.096,.568,.623,.097,segments=(10,5,4))
    terrace(m,'trim',0,-.303,1.645,.518,.603,.719)
    gable(m,'roof_tile',0,-.302,1.722,.577,.719,.199)
    steps(m,-.015,-.7175,.978,.127,.226)
    m.entry(-.015,.128,.000,.394,.386)
    if m.lod<2:
        m.rune(0,.663,-.572,.160)
        flank(m,.681,.305,.263,.312,.154)
    if m.lod==0:
        for x in (-.56,.54):
            m.lamp(x,.128,-.64,.110)


def industrial_exchange(m):
    # Three glazed northlights over the longitudinal council floor; a separate civic clock stack.
    terrace(m,'brick',.195,.015,1.022,1.089,0,.554)
    terrace(m,'metal',.195,.015,1.09,1.152,.551,.620)
    for i in range(3):
        cx=-.161+i*.361
        profile(m,'metal',cx,.014,.356,1.184,.619,
                [(-.5,.048),(.35,.261),(.5,.236)],'brick')
        if m.lod<2:
            pane(m,cx+.065,-.581,.679,.149,.092,sill='metal',light='glass')
    terrace(m,'brick',-.563,.153,.378,.508,0,1.120,taper=.86)
    terrace(m,'trim',-.563,.153,.405,.535,1.118,1.184)
    terrace(m,'wall',-.563,.153,.333,.450,1.182,1.459)
    m.hip_roof((-.563,1.458,.153),.445,.561,.185,m.color('slate'),overhang=0,material='slate')
    with m.transform(translate=(-.563,1.318,-.079),rotate_x=-90):
        roundwork(m,'accent',0,0,.117,0,.029,.111)
        roundwork(m,'ivory',0,0,.092,.029,.038)
    if m.lod<2:
        m.beam('door',(-.563,1.318,-.123),(-.563,1.384,-.123),.015)
        m.beam('door',(-.563,1.318,-.129),(-.510,1.337,-.129),.014)
    terrace(m,'fieldstone',.104,-.558,.599,.271,0,.427)
    profile(m,'metal',.104,-.558,.663,.333,.426,[(-.5,.036),(0,.141),(.5,.036)])
    council_gate(m,.104,-.694,.071,.410,.314)
    for z in (-.264,.133,.37):
        flank(m,.706,z,.178,.203,.218,light='glass')
    if m.lod==0:
        pane(m,-.558,-.078,.678,.134,.274,sill='fieldstone')
        m.rune(.103,.496,-.726,.116)


def modern_assembly(m):
    # An occupied council bar bridges a ground-level concourse, with a second wing at right angles.
    terrace(m,'fieldstone',-.205,-.043,.605,.824,0,.390)
    for x in (-.565,.487):
        for z in (-.232,.299):
            terrace(m,'wall',x,z,.132,.160,0,.487,taper=.77)
    terrace(m,'ivory',-.073,.019,1.511,.811,.388,.485,taper=1.05)
    terrace(m,'glass',-.073,.019,1.481,.769,.485,.746)
    terrace(m,'ivory',-.073,.019,1.58,.863,.746,.819)
    terrace(m,'wall',.435,.311,.397,.683,.817,1.059)
    profile(m,'metal',.435,.311,.466,.744,1.059,[(-.5,.038),(.5,.218)],'glass')
    terrace(m,'fieldstone',-.200,-.365,.598,.369,0,.059)
    council_gate(m,-.201,-.457,.058,.377,.275)
    if m.lod<2:
        for x in (-.65,-.36,-.07,.22,.51):
            block(m,'trim',x,-.367,.030,.038,.488,.744)
        pane(m,.434,-.035,.885,.262,.104,light='glass')
    if m.lod==0:
        for z in (-.217,.03,.261):
            block(m,'metal',.672,z,.031,.023,.486,.745)
        m.rune(-.512,.431,-.396,.140)


def wheel_assembly(m):
    # The open vertical wheel marks the public crossing between two inclined, inhabited piers.
    for side in (-1,1):
        x=side*.588
        terrace(m,'wall',x,.09,.452,.751,0,.269,taper=.93)
        terrace(m,'ivory',x,.09,.412,.689,.268,.865,taper=.62,shift=(-side*.201,0))
        terrace(m,'glass',x-side*.200,.09,.286,.432,.864,1.045,taper=.80,shift=(-side*.069,0))
    terrace(m,'wall',-.004,-.193,.642,.720,0,.322)
    council_gate(m,-.005,-.554,.069,.418,.245)
    terrace(m,'gold',-.005,-.144,.792,.754,.323,.412,taper=.95)
    m.wheel(0,1.026,.060,.530,'gold',4)
    if m.lod<2:
        for s in (-1,1):
            pane(m,s*.576,-.288,.082,.205,.108,light='ascension')
    if m.lod==0:
        m.lamp(-.261,.051,-.542,.105)
        m.lamp(.263,.051,-.542,.105)


COUNCILS={1:bronze_moot,2:iron_hold,3:classical_loggia,6:industrial_exchange,
          7:modern_assembly,9:wheel_assembly}
