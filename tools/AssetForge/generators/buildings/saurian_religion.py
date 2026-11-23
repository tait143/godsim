"""Clutch totems, tide shrines, cloisters and monumental Saurian sun sanctuaries."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

PACKAGE="saurian_religion"


def votive(m,x,y,z,r):
    m.drum("trim",x,y,z,r*.66,r*.91,r)
    m.drum("gold",x,y+r*.90,z,r*1.04,r*.22)
    m.mound("emissive",x,y+r*1.12,z,r*.68,r*.58,n=(7,5,4)[m.lod])


def crest(m,x,y,z,r):
    # Curved comb of rays set into a thick gold solar mirror.
    m.block("trim",x,y,z,.094,r*1.08,.093)
    m.disc(x,y+r*1.20,z+.018,r)
    if m.lod==0:
        for i in (-1,0,1):
            a=math.pi/2+i*.53
            m.rod("gold",(x+r*.95*math.cos(a),y+r*1.20+r*.95*math.sin(a),z),
                  (x+r*1.27*math.cos(a),y+r*1.20+r*1.27*math.sin(a),z),r*.083,4)


def totem(m):
    m.slab("fieldstone",0,0,.10,1.41,.18,1.35,.88)
    m.slab("timber",0,.18,.12,.92,.13,.85,.90)
    m.slab("roof",0,.31,.15,.45,.72,.45,.73)
    m.slab("turquoise",0,.70,.15,.434,.079,.425,.92)
    # A carved marsh lizard faces down the approach. Spread cheek frills frame the long snout.
    m.slab("wall",0,.98,.05,.72,.33,.59,.85,cut=.19)
    m.slab("thatch",0,1.003,-.264,.408,.174,.423,.77,cut=.16)
    for s in (-1,1):
        m.blade("turquoise",(s*.22,1.07,.18),(s*.54,1.49,.27),.131)
        if m.lod<2:
            m.disc(s*.224,1.205,-.212,.070,"gold",False)
    crest(m,0,1.302,.14,.253)
    m.stairs(0,0,-.85,.52,.356,.18)
    if m.lod<2:
        for s in (-1,1):
            votive(m,s*.45,.18,-.28,.083)
        if m.lod==0:
            for j in range(3):
                m.disc(0,.43+j*.095,-.059+j*.006,.035,"gold",False)


def shrine(m):
    m.slab("fieldstone",0,0,.07,1.55,.17,1.42,.93)
    m.slab("wall",0,.17,.10,1.27,.10,1.16,.94)
    m.stairs(0,0,-.94,.49,.495,.268)
    for s in (-1,1):
        for t in (-1,1):
            x=s*.454
            z=.16+t*.341
            m.drum("trim" if m.era<2 else "ivory",x,.27,z,.084,.63,.063,n=(8,6,4)[m.lod])
            if m.lod<2:
                m.drum("turquoise",x,.754,z,.079,.069,n=(8,6)[m.lod])
    m.slab("trim",0,.896,.16,1.14,.083,.87,.97,underside=True)
    roof_h=(.42,.48,.30,.34)[m.era]
    m.rooflet(0,.979,.16,1.26,.99,roof_h,m.skin)
    crest(m,0,.979+roof_h*1.10,.16,.129)
    # A seated sun idol and its carved altar occupy the open, south-facing bay.
    m.slab("wall",0,.27,-.046,.46,.165,.34,.85)
    m.slab("turquoise",0,.435,-.070,.28,.153,.20,.70)
    m.block("trim",0,.505,-.125,.101,.17,.106)
    m.disc(0,.623,-.158,.145)
    if m.lod==0:
        for s in (-1,1):
            m.jar(s*.461,.27,-.285,.077,"hide")
    if m.era==1:
        m.mound("roof_tile",0,1.440,.16,.213,.148)
    elif m.era==3:
        for s in (-1,1):
            m.rod("ivory",(s*.474,.95,.16),(s*.22,1.35,.16),.034)


def serpent(m,x,y,z,side):
    m.slab("wall",x,y,z,.171,.14,.244,.85)
    m.slab("turquoise",x,y+.14,z-.014,.129,.071,.216,.88)
    if m.lod==0:
        m.disc(x+side*.023,y+.18,z-.109,.031,"gold",False)


def temple(m):
    masonry=("fieldstone","wall","brick","ivory")[m.era]
    m.slab(masonry,0,0,.085,1.85,.20,1.59,.91)
    m.slab("turquoise",0,.20,.085,1.70,.044,1.46,.98)
    m.slab(masonry,0,.244,.141,1.46,.282,1.15,.83)
    m.slab("accent",0,.526,.141,1.235,.067,.987,.98,underside=True)
    m.stairs(0,0,-.953,.412,.61037,.593)
    m.slab("wall",0,.593,.168,.771,.379,.64)
    m.entry(0,.593,-.152,.296,.312)
    for s in (-1,1):
        m.drum("trim",s*.386,.593,-.156,.059,.382,n=(8,6,4)[m.lod])
        if m.lod<2:
            serpent(m,s*.336,0,-.711,s)
    m.slab("trim",0,.972,.142,.969,.074,.811,underside=True)
    if m.era==0:
        m.rooflet(0,1.046,.142,1.01,.88,.38,"roof")
        crest(m,0,1.461,.142,.157)
    elif m.era==1:
        m.drum("wall",0,1.046,.142,.388,.141,.329)
        m.mound("roof_tile",0,1.187,.142,.366,.262)
        crest(m,0,1.43,.142,.143)
    elif m.era==2:
        m.slab("metal",0,1.046,.142,.964,.185,.808,.54)
        m.drum("roof",0,1.231,.142,.234,.20,.168)
        crest(m,0,1.431,.142,.178)
    else:
        for s in (-1,1):
            m.slab("glass",s*.220,1.046,.141,.201,.25,.32,.73)
        m.ring("gold",0,1.52,.099,.328,.228,.099)
    if m.lod==0:
        for s in (-1,1):
            votive(m,s*.57,.244,-.530,.080)


def cathedral(m):
    masonry=("wall","fieldstone","brick","ivory")[m.era]
    m.slab("wall",0,0,.06,1.93,.124,1.78,.94)
    m.slab(masonry,0,.124,.19,1.69,.38,1.21,.90)
    m.slab("trim",0,.504,.19,1.55,.064,1.116,.98)
    m.slab("glass" if m.era>=2 else "wall",0,.568,.26,.90,.448,.83,.87)
    m.rooflet(0,1.016,.26,.987,.93,.34 if m.era!=3 else .25,m.skin)
    top=1.016+(.34 if m.era!=3 else .25)*1.10
    m.block("accent",0,top-.03,.235,.23,.189,.21)
    m.ring("gold",0,top+.324,.191,.248,.159,.081)
    # Battered pylons create a tall processional portal. Both buttress feet meet the terrace.
    for s in (-1,1):
        x=s*.621
        m.slab(masonry,x,.124,-.268,.40,.65,.47,.77)
        m.slab("turquoise",x,.774,-.268,.331,.063,.385,.96)
        tower_h=(.25,.38,.31,.50)[m.era]
        m.slab("glass" if m.era>=2 else "wall",x,.837,-.268,.265,tower_h,.302,.76)
        m.drum("accent",x,.837+tower_h,-.268,.145,.203,.048,n=(8,6,4)[m.lod])
        if m.lod<2:
            m.disc(x,.600,-.455,.085)
    m.slab("wall",0,.124,-.424,.594,.47,.419)
    m.entry(0,.124,-.6335,.339,.384)
    m.rooflet(0,.594,-.424,.695,.497,.17,"roof")
    for i in range(3):
        m.block("wall",0,0,-.9066+i*.052,.42,.124*(i+1)/3,.052)
    if m.lod==0:
        for s in (-1,1):
            votive(m,s*.581,.124,-.594,.083)


def cloister(m):
    if m.era>=2:
        works.footing(m,1.90,1.82)
        if m.era==2:
            # North dormitory, belfry and covered walking aisle on three sides of a cistern.
            works.barrel(m,.13,.49,1.25,.58,.34,.20)
            top=works.shell(m,-.61,-.02,.41,.53,.91)
            for x in (-.75,-.47):
                for z in (-.15,.11):
                    m.block("wall",x,top,z,.079,.25,.08)
            m.rooflet(-.61,top+.25,-.02,.48,.47,.20,"metal")
            m.drum("gold",-.61,top+.06,-.02,.093,.20,.056)
            works.basin(m,.15,-.20,.66,.48)
            for x in (.57,.80):
                for z in (-.43,.11):
                    m.block("brick",x,.08,z,.075,.38,.079)
            m.slab("roof",.685,.46,-.16,.37,.10,.71,.84,underside=True)
            if m.lod<2:
                m.disc(-.61,.76,-.287,.081)
        else:
            # A meditative cloister spirals up behind a reflective pool. The linked
            # glass sanctums sit under a single tilted, open sun portal.
            works.lens(m,-.45,.40,.32,.39)
            works.lens(m,.36,.49,.46,.57)
            works.basin(m,-.09,-.40,.90,.52)
            works.arch(m,-.07,-.24,1.58,1.24,.115,.13,"gold")
            if m.lod<2:
                for x in (-.64,.61):
                    m.slab("ivory",x,.08,-.55,.21,.19,.38,.83)
                    votive(m,x,.27,-.55,.069)
        return
    m.slab("fieldstone",0,0,0,1.91,.076,1.81,.98)
    # The enclosed water court has an entrance between the two south-facing dormitories.
    for s in (-1,1):
        m.house(s*.641,.076,.051,.55,1.27,.65)
    m.house(0,.076,.562,.85,.541,.79)
    m.slab("wall",0,.076,-.158,.347,.075,.53,cut=.145)
    m.slab("water",0,.151,-.158,.285,.024,.447,cut=.15)
    if m.lod<2:
        for side in (-1,1):
            for z in (-.356,-.144,.068):
                m.slab("plaster",side*.256,.076,z,.139,.018,.188,cut=.045)
        m.slab("plaster",0,.076,.208,.649,.018,.076,cut=.045)
        for i in range(3):
            m.drum("leaf",-.067+.063*i,.174,-.259+i*.096,.035,.014,n=6)
        if m.lod==0:
            m.mound("flower",-.004,.188,-.163,.032,.036,n=6)
    if m.era==1:
        m.slab("wall",0,.97,.55,.22,.25,.21,.83)
        m.rooflet(0,1.22,.55,.31,.31,.17,"roof_tile")
    elif m.era==2:
        m.drum("brick",0,.81,.56,.154,.31,.134)
        m.mound("roof",0,1.12,.56,.181,.144)
    else:
        m.slab("glass",0,.832,.56,.38,.26,.32,.76)
        m.slab("ivory",0,1.092,.56,.327,.055,.287)
    for s in (-1,1):
        m.slab("trim",s*.247,.076,-.683,.143,.297,.173,.79)
        if m.lod<2:
            m.disc(s*.247,.28,-.753,.050,"gold",False)


def build(spec,lod):
    m=Delta(spec,lod)
    with m.transform(scale=spec.footprint[0]/2):
        {"SpiritTotem":totem,"Shrine":shrine,"Temple":temple,
         "Cathedral":cathedral,"Monastery":cloister}[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
