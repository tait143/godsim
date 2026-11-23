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
    if m.era:
        works.court(m,w=1.78)
        if m.era==1:
            # Paired stelae frame the solar calendar set above a stepped altar.
            for x in (-.54,.54):
                m.slab("wall",x,.08,.11,.35,.22,.53,.87)
                m.slab("fieldstone",x,.30,.11,.25,.83,.35,.65)
                m.slab("gold",x,1.13,.11,.181,.17,.252,.12)
            m.slab("wall",0,.08,-.22,.75,.14,.67,.89)
            m.slab("turquoise",0,.22,-.18,.49,.18,.40,.77)
            m.slab("trim",0,.40,-.18,.16,.15,.16,.86)
            m.disc(0,.71,-.13,.22)
            if m.lod<2:
                for x in (-.54,.54):
                    m.disc(x,.66,-.047,.072)
        elif m.era==2:
            # A public votive lamp stands on a stepped brick altar. The iron
            # cage carries a broad copper weather hood around the amber glass.
            m.slab("brick",0,.08,.17,1.14,.23,.92,.84)
            m.slab("wall",0,.31,.17,.81,.20,.66,.85)
            m.drum("metal",0,.51,.17,.29,.17,.39)
            m.drum("emissive",0,.68,.17,.228,.43,.241)
            for k in range((8,6,4)[m.lod]):
                a=math.tau*k/(8,6,4)[m.lod]+.17
                m.block("metal",.30*math.cos(a),.68,.17+.30*math.sin(a),.042,.43,.042)
            m.drum("turquoise",0,1.11,.17,.364,.065,.39)
            m.drum("roof",0,1.175,.17,.425,.25,.16)
            m.drum("gold",0,1.425,.17,.113,.11,.039)
            for x in (-.55,.55):
                m.slab("brick",x,.08,-.38,.26,.19,.30,.80)
                m.drum("gold",x,.27,-.38,.10,.058,.115)
                m.mound("emissive",x,.328,-.38,.061,.087)
            if m.lod<2:
                works.cartouche(m,0,.185,-.282,.062)
        else:
            # The sun rests in a three-petalled lotus on an open reflecting pool.
            works.basin(m,0,.10,1.50,1.01)
            m.drum("ivory",0,.177,.12,.32,.13,.24)
            for dx,dz,h in ((-.58,-.18,.91),(.58,-.18,.91),(0,.41,1.19)):
                m.blade("ivory",(dx*.12,.29,.12+dz*.12),(dx,h,.12+dz),.24)
            m.drum("gold",0,.307,.12,.10,.28,.083)
            m.disc(0,.783,.10,.256)
            m.slab("ivory",0,.08,-.50,.59,.17,.35,.70)
            if m.lod<2:
                works.cartouche(m,0,.166,-.666,.055)
        return
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


def calendar_court(m):
    # Three free-standing trilithons mark the sun's seasonal limits. Their
    # doors face a common outdoor altar and the southern approach stays open.
    m.drum("soil",0,0,.045,.89,.085,n=(16,10,7)[m.lod])
    for angle,h in ((-68,.72),(0,1.16),(68,.72)):
        a=math.radians(angle)
        with m.transform(translate=(.61*math.sin(a),.085,.045+.61*math.cos(a)),rotate_y=angle):
            for x in (-.196,.196):
                m.slab("fieldstone",x,0,0,.20,h,.285,.77,cut=.19)
                if m.lod==0:
                    m.block("turquoise",x,h*.70,-.124,.073,.061,.045)
            m.slab("fieldstone",0,h,0,.70,.185,.33,.89,cut=.13,underside=True)
            if m.lod<2:
                m.disc(0,h+.09,-.158,.055,"gold",False)
    m.drum("fieldstone",0,.085,-.10,.395,.15,.355,n=(12,9,6)[m.lod])
    m.slab("wall",0,.235,-.10,.48,.18,.40,.82)
    m.drum("turquoise",0,.415,-.10,.139,.13,.110)
    m.disc(0,.73,-.074,.226)
    for x in (-.44,.44):
        m.slab("fieldstone",x,.085,-.46,.22,.14,.25,.85)
        votive(m,x,.225,-.46,.075)
    # Low processional stones cross the soft outer margin of the ritual court.
    m.slab("fieldstone",0,0,-.817,.39,.075,.28,.94)


def processional_temple(m):
    # A roofless marble avenue leads into a deep, shadowed inner sanctuary.
    # Its two narrow colonnades carry their own stone architraves.
    works.court(m)
    for x in (-.56,.56):
        m.slab("wall",x,.08,-.09,.30,.075,1.22,cut=.06)
        for z in (-.54,-.12,.30):
            base=.155
            if m.lod<2:
                m.drum("trim",x,base,z,.116,.084,.104)
                base+=.084
            m.drum("ivory",x,base,z,.094,.654-base,.073,n=(12,8,5)[m.lod])
            m.slab("turquoise",x,.654,z,.16,.078,.16,1.38,cut=.08)
        m.slab("wall",x,.732,-.10,.27,.118,1.14,.96,underside=True)
    # The side walls, back wall and heavy cornice enclose an actual recess.
    for x in (-.48,.48):
        m.slab("plaster",x,.08,.47,.38,.84,.57,.86,cut=.075)
    m.slab("wall",0,.08,.69,.66,.84,.18)
    m.slab("wall",0,.92,.47,1.48,.15,.665,.94,underside=True)
    m.slab("roof_tile",0,1.07,.48,1.41,.085,.64,.92,underside=True)
    works.cartouche(m,0,.992,.134,.080)
    m.block("turquoise",0,.08,.355,.40,.19,.33)
    m.block("trim",0,.27,.355,.096,.12,.12)
    m.disc(0,.49,.366,.16)
    for side in (-1,1):
        serpent(m,side*.35,.08,-.51,side)


def nave_shell(m,side):
    """A sloping chapel wall and a true octagonal oculus through its thickness."""
    def point(uv,inner=False):
        u,v=uv
        x=.73-v*.593-(.074 if inner else 0)
        return (side*x,.31+v*(.82+.56*u),-.51+1.18*u)
    def face(role,vertices):
        m.face(role,vertices[::-1] if side==1 else vertices)
    n=16 if m.lod==0 else 8
    outer=[]
    surround=[]
    hole=[]
    for i in range(n):
        a=i*math.tau/n
        x,y=math.cos(a),math.sin(a)
        k=.5/max(abs(x),abs(y))
        outer.append((.5+k*x,.5+k*y))
        surround.append((.5+.273*x,.51+.260*y))
        hole.append((.5+.205*x,.51+.190*y))
    for i in range(n):
        j=(i+1)%n
        lip=hole if m.lod==2 else surround
        face("ivory",[point(outer[i]),point(outer[j]),point(lip[j]),point(lip[i])])
        if m.lod<2:
            face("gold",[point(surround[i]),point(surround[j]),point(hole[j]),point(hole[i])])
        face("glass",[point(outer[i],True),point(hole[i],True),
                      point(hole[j],True),point(outer[j],True)])
        face("gold",[point(hole[i]),point(hole[j]),point(hole[j],True),point(hole[i],True)])
        role="gold" if outer[i][1]>.99 or outer[j][1]>.99 else "ivory"
        face(role,[point(outer[j]),point(outer[i]),point(outer[i],True),point(outer[j],True)])
    # Glazing bars make a four-rayed sun inside the open clerestory.
    if m.lod<2:
        for i in (0,n//4):
            m.rod("gold",point(hole[i],True),point(hole[i+n//2],True),.019,n=4)


def solar_nave(m):
    # The two deep folded shells rise toward the apse. A continuous roof
    # slot admits daylight to the occupied nave and its illuminated altar.
    works.court(m,w=1.90,d=1.66)
    front,back=-.51,.67
    for s in (-1,1):
        m.slab("turquoise",s*.65,.08,.08,.22,.23,1.30,.91,cut=.08)
        nave_shell(m,s)
        if m.lod<2:
            for z in (-.32,.48):
                ridge=1.13+.56*(z-front)/(back-front)
                m.rod("gold",(s*.741,.319,z),(s*.147,ridge,z),.020,n=4)
    m.slab("metal",0,.08,-.096,.54,.17,.43,.82)
    m.slab("turquoise",0,.25,-.096,.33,.18,.30,.79)
    m.drum("gold",0,.43,-.096,.087,.19,.063)
    m.mound("emissive",0,.62,-.096,.145,.24)
    for x in (-.33,.33):
        for z in (-.23,.12):
            m.slab("ivory",x,.08,z,.215,.12,.12,cut=.08)
    if m.lod==0:
        for z in (-.66,-.48,-.30):
            m.slab("gold",0,.08,z,.195,.014,.06,cut=.09)


def temple(m):
    if m.era==2:
        # A tall lantern contains the sanctuary, flanked by lower brick
        # processional aisles in place of the earlier stacked pyramid.
        works.court(m)
        works.shell(m,0,.10,.65,1.11,.80,material="brick")
        m.slab("turquoise",0,.88,.10,.72,.092,1.18,.93,underside=True)
        m.slab("glass",0,.972,.20,.48,.43,.67,.84)
        m.rooflet(0,1.402,.20,.55,.76,.19,"metal")
        for side in (-1,1):
            works.barrel(m,side*.55,.23,.39,.90,.39,.20)
            x=side*.30
            m.slab("brick",x,.08,-.48,.15,.62,.29,.64)
        works.cartouche(m,0,.688,-.463,.102)
        if m.lod==0:
            for z in (-.02,.18,.39):
                m.block("metal",.219,1.02,z,.041,.32,.032)
        return
    {0:calendar_court,1:processional_temple,3:solar_nave}[m.era](m)


def cathedral(m):
    if m.era>=2:
        works.court(m)
        if m.era==2:
            # Iron ribs span the glazed processional nave. The detached brick
            # campanile stands on its own foundation beside the entrance.
            works.barrel(m,-.17,.11,1.00,1.27,.60,.48)
            works.shell(m,.59,.26,.37,.46,1.04,material="brick",door=False)
            for x in (.473,.707):
                for z in (.115,.405):
                    m.block("metal",x,1.12,z,.061,.30,.079)
            m.mound("gold",.59,1.36,.26,.105,.115)
            m.rod("metal",(.59,1.445,.26),(.59,1.51,.26),.026)
            m.rooflet(.59,1.42,.26,.44,.52,.28,"roof")
            works.cartouche(m,-.17,.83,-.571,.106)
            for x in (-.77,.40):
                m.slab("brick",x,.08,-.29,.17,.43,.31,.69)
        else:
            # A free-standing solar mandorla frames a low, round sanctuary.
            works.lens(m,0,.16,.47,.42)
            for x in (-.69,.69):
                m.slab("ivory",x,.08,.12,.29,.38,.60,.77)
            works.arch(m,0,.12,1.57,1.60,.15,.24,"ivory",.40)
            m.rod("gold",(0,1.99,.12),(0,1.65,.12),.041)
            m.ring("gold",0,1.47,.076,.208,.125,.087)
            for x in (-.64,.64):
                m.slab("turquoise",x,.08,-.35,.23,.22,.35,.71)
        return
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
