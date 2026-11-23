"""Delta wardens' reed stockades, signal towers, sun forts and muster courts."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE = "saurian_military"


def flag(m, x, y, z, height):
    m.block("trim", x, y, z, .035, height, .034)
    # A gold-edged swallowtail is creased along its hoist.
    with m.transform(translate=(x, y+height-.025, z)):
        a,b,c,d,e,f = [(0,0,0),(.148,-.007,-.033),(.31,-.056,.012),
                       (.206,-.125,-.009),(.285,-.202,.006),(0,-.18,0)]
        g=(.148,-.19,-.033)
        for face in ((a,b,g,f),(b,c,d,g),(d,e,g)):
            m.face("banner",face)
            m.face("banner",[(u,v,w+.005) for u,v,w in reversed(face)])
        if m.lod==0:
            m.disc(.071,-.079,-.019,.030,"gold",False)


def slit(m, x, y, z, w, h):
    m.face("door",[(x+w/2,y,z),(x-w/2,y,z),(x-w*.36,y+h,z),(x+w*.36,y+h,z)])


def battlements(m, x, y, z, w, d, count):
    for side in (-1,1):
        for i in range(count):
            dx=w*((i+.5)/count-.5)
            m.block("wall",x+dx,y,z+side*(d/2-.05),w/count*.58,.105,.102,.12)


def palisade(m):
    late=m.era==1
    for side in (-1,1):
        # A change in the spacing of the uprights marks the hinge and the end post.
        if m.lod==2:
            m.block("timber",side*.856,0,.015,1.12,.56,.17)
        count=(7,6,3)[m.lod]
        for i in range(count):
            x=side*(.33+i*1.05/(count-1))
            h=.67+.065*math.cos(i*1.77)+(i==0)*.14
            r=(.09,.104,.11)[m.lod]
            n=(7,5,4)[m.lod]
            m.drum("timber" if i%3 else "trim",x,0,.018,r,h,n=n)
            m.drum("wall" if late else "metal",x,h,.018,r,.15 if i else .20,0,n=n)
            if m.lod==0 and i in (0,3,6):
                m.drum("turquoise",x,h-.145,.018,r*1.04,.064,n=n)
        for y in ((.26,.51) if m.lod<2 else (.36,)):
            m.block("trim",side*.87,y,.094,1.15,.070,.066)
        if late:
            m.slab("fieldstone",side*1.35,0,.01,.25,.36,.40,.80)
        else:
            # Hammered scales are riveted to the front rail. Bronze and iron
            # have noticeably different metals on the shared age rows.
            m.block("trim",side*.863,.393,-.076,1.10,.179,.097)
            for p in (.55,.866,1.182):
                x=side*p
                outline=[(x+.143,.58),(x+.143,.397),(x,.278),(x-.143,.397),(x-.143,.58)]
                face=[(u,v,-.134) for u,v in outline]
                back=[(u,v,-.094) for u,v in outline]
                m.face("metal",face)
                for j in range(5):
                    k=(j+1)%5
                    m.face("metal",(face[k],face[j],back[j],back[k]))
                if m.lod==0:
                    m.disc(x,.498,-.134,.033,"gold",False)
        if m.lod<2:
            m.rod("timber",(side*1.18,.030,.31),(side*1.18,.50,.052),.055)
    if late:
        for side in (-1,1):
            m.slab("wall",side*.321,0,-.024,.195,.80,.30,.84)
        m.slab("wall",0,.80,-.006,.865,.115,.38,underside=True)
        m.rooflet(0,.915,-.002,.90,.41,.22,"roof")
        top=1.125
    else:
        m.block("roof",0,.775,-.012,.84,.11,.20)
        top=.885
    m.block("trim",0,top-.019,-.025,.084,.13,.07)
    m.disc(0,top+.107,-.045,.144)
    if m.lod<2:
        for side in (-1,1):
            m.block("banner",side*.43,.512,-.097,.122,.228,.026)
            if m.lod==0:
                m.disc(side*.43,.65,-.112,.035,"gold",False)


def watch(m):
    era=m.era
    if era==0:
        # A reed lookout with an open, cross-braced undercroft.
        m.slab("fieldstone",0,0,.035,.66,.105,.65,.94)
        for x in (-.247,.247):
            for z in (-.165,.24):
                m.rod("timber",(x,.105,z),(x*.78,1.05,z*.78),.062,n=(7,5,4)[m.lod])
        if m.lod<2:
            for s in (-1,1):
                m.rod("trim",(s*.236,.20,-.160),(-s*.211,.88,-.138),.029)
                m.rod("trim",(s*.233,.21,.216),(s*.208,.87,-.137),.029)
        floor=1.03
    else:
        m.slab("wall",0,0,.035,.76,.14,.73,.94)
        m.slab("brick" if era==2 else "wall",0,.14,.035,.645,.91,.60,.69)
        m.slab("accent",0,1.05,.035,.499,.055,.466,.99)
        floor=1.105
        m.slab("trim",0,0,-.25,.26,.355,.18,.91)
        m.entry(0,0,-.34,.157,.285)
        if m.lod<2:
            slit(m,0,.62,-.207,.043,.22)
    m.slab("trim",0,floor,.035,.525,.127,.49,1.37,underside=True)
    y=floor+.127
    m.slab("roof",0,y,.035,.79,.085,.69,.99,underside=True)
    y+=.085
    for s in (-1,1):
        for t in (-1,1):
            m.block("trim",s*.283,y,.035+t*.242,.069,.292,.066)
    m.block("wall",0,y,.255,.585,.15,.09)
    m.block("turquoise",0,y,-.209,.60,.127,.081)
    if m.lod<2:
        m.disc(0,y+.074,-.25,.056,"gold",False)
    m.rooflet(0,y+.292,.035,.82,.73,(.32,.423,.203)[era],m.skin)
    crest=y+.292+(.32,.423,.203)[era]*1.11
    if era==2:
        m.drum("metal",0,crest,.035,.085,.23,.06)
        m.disc(0,crest+.237,.033,.119,"emissive")
    else:
        m.block("trim",0,crest-.02,.035,.07,.14,.076)
        m.disc(0,crest+.11,.034,.105)
    if era==0:
        for s in (-1,1):
            m.rod("timber",(s*.079,.012,-.399),(s*.079,y,-.236),.018,n=4)
        for i in range((7,4,1)[m.lod]):
            u=(i+1)/((7,4,1)[m.lod]+1)
            m.block("thatch",0,y*u,-.397+.162*u,.173,.027,.029)
    if m.lod==0 and era>0:
        for s in (-1,1):
            m.slab("wall",s*.285,.09,-.067,.164,.44,.346,.65)
    if m.lod==0 and era==0:
        # Reed lashings and an amber oil lamp at the stairhead.
        for s in (-1,1):
            m.drum("thatch",s*.22,.66,-.144,.065,.060,n=7)
        m.rod("trim",(.29,y+.253,-.21),(.35,y+.253,-.30),.017,n=5)
        m.rod("trim",(.35,y+.253,-.30),(.35,y+.176,-.30),.010,n=4)
        m.drum("emissive",.35,y+.104,-.30,.039,.075,.031,n=7)


def wall(m):
    modern=m.era==2
    # The curtain is continuous along X. A deep central buttress contains its postern.
    m.slab("fieldstone",0,0,.056,.965,.118,.51,.95,cut=.028)
    m.slab("brick" if modern else "wall",0,.118,.059,.954,.47,.433,.94,cut=.036)
    m.slab("trim",0,.588,.059,.957,.085,.50,1.018,cut=.035,underside=True)
    for s in (-1,1):
        m.block("wall",0,.673,.059+s*.201,.95,.096,.097)
    battlements(m,0,.769,.059,.93,.495,4 if modern else 3)
    for s in (-1,1):
        m.slab("wall",s*.30,0,-.152,.180,.60,.258,.65)
    m.slab("wall",0,0,-.166,.316,.393,.244,.98)
    m.entry(0,0,-.288,.177,.32)
    m.slab("roof",0,.393,-.163,.371,.082,.297,.90,underside=True)
    if modern:
        m.slab("metal",0,.77,.05,.39,.20,.29,.85)
        m.slab("trim",0,.97,.05,.39,.043,.294,.94)
    if m.lod<2:
        m.disc(0,.526,-.149,.074)
        for s in (-1,1):
            m.block("turquoise",s*.30,.397,-.253,.119,.06,.037)
            if m.lod==0:
                slit(m,s*.412,.44,-.151,.031,.084)
                m.slab("fieldstone",s*.30,0,-.17,.226,.114,.29,.91)
                m.disc(s*.30,.431,-.274,.033,"gold",False)
                for i in range(2):
                    m.block("accent",s*(.145+i*.066),.614,-.197,.037,.026,.016)


def court_target(m,x,z,r):
    m.slab("wall",x,0,z,r*1.86,.08,r*1.60,.78)
    m.rod("timber",(x,.075,z),(x,.42,z+.047),.045,n=5)
    m.disc(x,.404,z+.019,r,"thatch")
    if m.lod<2:
        m.disc(x,.404,z-.027,r*.37,"banner",False)


def barracks(m):
    era=m.era
    floor=.106
    m.slab("fieldstone",0,0,.09,1.81,floor,1.48,.96)
    # Two dormitory aisles flank an open forecourt; a transverse mess hall joins them.
    for s in (-1,1):
        x=s*.548
        w=.521
        z=.142
        h=(.415,.505,.382,.445)[era]
        m.slab("wall",x,floor,z,w,h,1.092,cut=.08)
        m.entry(x+.058,floor,z-.546,.204,h*.74)
        m.rooflet(x,floor+h,z,w+.112,1.187,(.28,.361,.198,.25)[era],m.skin)
        if m.lod<2:
            with m.transform(translate=(x+s*w/2,floor+h*.47,.15),rotate_y=90):
                m.block("glass" if era>=2 else "turquoise",0,0,0,.50,.072,.028)
    m.slab("wall",0,floor,.496,.62,.426,.497)
    m.slab("trim",0,.532,.496,.673,.077,.542,.99,underside=True)
    m.slab("glass" if era==3 else "roof",0,.609,.496,.592,.201,.46,.66)
    m.entry(0,floor,.2475,.239,.343)
    m.stairs(0,0,-.89,.38,.2696,floor)
    if era>=2:
        m.drum("metal",-.541,.63,.502,.078,.394,.060)
        m.drum("trim",-.541,1.024,.502,.079,.047)
    if era==1:
        m.slab("wall",0,.80,.497,.308,.20,.288,.84)
        m.rooflet(0,1.0,.497,.32,.32,.19,"roof")
    if era==3:
        m.drum("ivory",.542,.69,.39,.142,.21,.11)
        m.mound("glass",.542,.90,.39,.14,.13)
    with m.transform(translate=(0,floor,0)):
        court_target(m,.302,-.505,.135)
    if m.lod<2:
        m.disc(0,.633,.253,.095)
    flag(m,-.645,floor,-.542,.76 if era!=3 else .89)


def fort(m):
    m.slab("fieldstone",0,0,.035,2.77,.17,2.63,.95)
    y=.17
    for s in (-1,1):
        m.slab("wall",s*1.02,y,.10,.235,.71,1.84,.92)
        m.block("roof",s*1.02,.88,.10,.258,.095,1.86)
        for z in (-.80,.88):
            rear=z>0
            h=1.02 if rear else .78
            m.slab("wall",s*1.02,y,z,.54,h,.56,.77)
            m.slab("turquoise",s*1.02,y+h,z,.445,.068,.457,.99)
            m.slab("wall",s*1.02,y+h+.068,z,.55,.152,.56,1.08,underside=True)
            battlements(m,s*1.02,y+h+.22,z,.56,.57,2)
            if m.lod<2:
                slit(m,s*1.02,y+h*.46,z-.252,.060,.211)
        m.slab("wall",s*.761,y,-.816,.53,.64,.255)
        m.slab("trim",s*.761,.81,-.816,.561,.079,.289,underside=True)
    # The elevated rear keep rises above the fortress gate.
    m.slab("wall",0,y,.553,1.52,1.04,.97,.87)
    m.slab("turquoise",0,1.21,.553,1.378,.074,.896)
    m.slab("wall",0,1.284,.553,.989,.52,.75,.94)
    m.rooflet(0,1.804,.553,1.17,.91,.45,"slate")
    flag(m,0,2.29,.55,.39)
    for s in (-1,1):
        m.slab("wall",s*.398,y,-.862,.322,.782,.525,.89)
        m.slab("turquoise",s*.398,.952,-.862,.31,.073,.484,.97)
        if m.lod<2:
            slit(m,s*.396,.56,-1.117,.045,.18)
    m.slab("wall",0,1.025,-.858,1.14,.175,.55,.93,underside=True)
    m.rooflet(0,1.20,-.858,1.19,.60,.286,"roof")
    m.disc(0,1.116,-1.135,.112)
    m.stairs(0,0,-1.457,.487,.24275,.17)
    m.slab("wall",0,.17,.117,.56,.657,.195)
    m.entry(0,.17,.0195,.38,.58)
    if m.lod<2:
        for side in (-1,1):
            m.block("banner",side*.306,1.41,.183,.112,.262,.021)
            if m.lod==0:
                m.disc(side*.306,1.593,.172,.040,"gold",False)
                for z in (-.47,.066,.59):
                    m.slab("fieldstone",side*1.134,.17,z,.283,.49,.207,.66)
                    m.slab("wall",side*1.129,.66,z,.216,.155,.164,.79)
                # Torch bowls and fluted corbels flank the approach.
                m.slab("trim",side*.640,.17,-1.066,.209,.36,.236,.81)
                m.drum("gold",side*.64,.53,-1.066,.066,.10,.114,n=9)
                m.mound("forge",side*.64,.63,-1.066,.092,.145,n=9)
                for j in range(3):
                    m.block("accent",side*(.196+.071*j),1.079,-1.137,.037,.062,.019)


def station(m):
    future=m.era==3
    m.slab("wall",0,0,.032,2.80,.105,2.67,.96)
    # A command gallery and the radio annex shelter two sides of the muster apron.
    m.slab("wall",-.137,.105,.65,1.905,.616,.965,.90)
    m.slab("glass",-.137,.721,.65,1.769,.214,.894,.90)
    m.slab("ivory" if future else "metal",-.137,.935,.65,1.79,.09,.902,.95,underside=True)
    m.slab("wall",-.17,.105,.182,.615,.439,.437)
    m.entry(-.17,.105,-.0365,.337,.361)
    m.slab("turquoise",-.17,.544,.182,.691,.066,.49,.96,underside=True)
    m.slab("roof",.865,.105,.015,.544,.54,1.28,.88)
    m.slab("trim",.865,.645,.015,.585,.085,1.32,.98,underside=True)
    m.rooflet(.865,.730,.356,.475,.556,.185,"glass" if future else "metal")
    m.slab("wall",-.913,.105,-.716,.426,.459,.462,.91)
    m.slab("glass",-.913,.564,-.716,.392,.159,.421,.94)
    m.slab("roof",-.913,.723,-.716,.47,.082,.494,.94,underside=True)
    if future:
        # A gold framed signal sail on a two-legged bearing.
        for s in (-1,1):
            m.rod("ivory",(-.34+s*.231,1.025,.66),(-.34+s*.142,1.37,.66),.064,n=6)
        with m.transform(translate=(-.34,1.55,.641),rotate_z=-16):
            m.ring("gold",0,0,0,.295,.221,.087)
            m.rod("metal",(-.241,0,.043),(.241,0,.043),.03,n=5)
            m.disc(0,0,.052,.079,"ascension",False)
    else:
        m.drum("trim",-.35,1.025,.67,.19,.131,.136)
        m.drum("metal",-.35,1.156,.67,.062,.679,.025)
        for i in (0,1,2):
            w=.42-i*.085
            m.block("gold",-.35,1.526+i*.106,.67,w,.034,.055)
    # The apron insignia uses four flush mosaic lozenges with a negative-space sun at their centre.
    for i in range(4):
        with m.transform(translate=(-.135,.109,-.724),rotate_y=90*i):
            m.face("turquoise" if i%2 else "accent",[(-.063,0,.126),(-.072,0,.265),(0,0,.334),(.067,0,.247),(.057,0,.126)])
    m.stairs(.11,0,-1.423,.55,.1734,.105)
    if m.lod<2:
        for s in (-1,1):
            m.slab("trim",.11+s*.412,.105,-1.057,.129,.275,.141,.86)
            m.disc(.11+s*.412,.302,-1.124,.038,"gold",False)
        flag(m,-1.168,.105,-.46,.81)
        if m.lod==0:
            for z in (-.396,-.17,.056):
                m.block("turquoise",1.111,.413,z,.034,.083,.132)
            for s in (-1,1):
                m.disc(-.17+s*.221,.422,-.044,.049,"gold",False)


def build(spec,lod):
    m=Delta(spec,lod)
    {"Palisade":palisade,"Watchtower":watch,"StoneWall":wall,"Barracks":barracks,
     "Castle":fort,"MilitaryBase":station}[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
