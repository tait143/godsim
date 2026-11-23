"""Waterworks, ceramic grain bins, loading sheds and power houses along the Saurian delta."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate
from generators.buildings import saurian_delta_works as works

PACKAGE = "saurian_infrastructure"


def vessel(m,x,y,z,profile,n):
    """Turn an ordered outside-to-inside profile. The last row can be a sunken water surface."""
    rings=[[(x+r*math.cos(2*math.pi*i/n),y+h,z+r*math.sin(2*math.pi*i/n))
            for i in range(n)] for r,h,role in profile]
    for (_,_,role),lower,upper in zip(profile,rings,rings[1:]):
        for j in range(n):
            k=(j+1)%n
            m.face(role,(lower[k],lower[j],upper[j],upper[k]))
    m.face(profile[0][2],rings[0])
    m.face(profile[-1][2],rings[-1][::-1])


def well(m):
    era=m.era
    m.slab("wall",0,0,.026,.89,.075,.82,.93)
    n=(16,10,6)[m.lod]
    vessel(m,0,.075,-.033,[(.27,0,"wall"),(.283,.164,"trim"),(.304,.174,"wall"),
          (.30,.237,"turquoise"),(.228,.237,"trim"),(.219,.139,"water")],n)
    if era==0:
        # The water jar hangs over the well mouth from one end of the balanced sweep.
        m.rod("timber",(-.267,.075,.223),(-.25,.55,.175),.046)
        m.rod("timber",(-.242,.529,.181),(.095,.737,-.061),.027)
        m.rod("thatch",(.093,.736,-.061),(.093,.351,-.061),.009,n=4)
        vessel(m,.093,.30,-.061,[(.040,0,"hide"),(.061,.085,"accent"),(.044,.099,"door"),
                                 (.034,.091,"door")],(8,6,4)[m.lod])
        m.rod("timber",(-.25,.529,.184),(-.379,.457,.305),.029)
        m.drum("wall",-.370,.393,.301,.057,.085,.051,n=6)
        m.disc(-.256,.525,.157,.042,"gold",False)
    elif era==1:
        for s in (-1,1):
            m.drum("wall",s*.294,.075,.052,.052,.43,.039,n=(8,6,4)[m.lod])
            if m.lod<2:
                m.drum("turquoise",s*.294,.399,.052,.046,.045,n=6)
        m.slab("trim",0,.505,.052,.711,.055,.218,underside=True)
        m.rooflet(0,.560,.052,.765,.473,.211,"roof")
        m.rod("timber",(-.302,.36,.052),(.311,.36,.052),.024)
        m.rod("thatch",(0,.36,.05),(0,.215,.05),.013,n=5)
        m.disc(0,.604,-.177,.060)
    elif era==2:
        m.slab("wall",-.234,.075,.20,.201,.104,.206,.97)
        m.drum("roof",-.234,.179,.20,.082,.36,.064)
        m.drum("metal",-.234,.539,.20,.076,.069,.057)
        m.rod("metal",(-.234,.466,.197),(-.115,.466,.011),.031)
        m.rod("metal",(-.115,.467,.011),(-.115,.401,.011),.030)
        m.rod("trim",(-.233,.554,.19),(-.370,.435,.345),.024)
        m.disc(-.236,.313,.13,.044)
    else:
        for s in (-1,1):
            m.slab("ivory",s*.257,.075,.090,.147,.291,.201,.72)
            m.rod("roof",(s*.255,.35,.09),(s*.167,.515,.092),.041)
        m.ring("gold",0,.571,.059,.21,.151,.067)
        m.rod("trim",(0,.39,.091),(0,.518,.091),.028,n=6)
        m.drum("ascension",0,.24,.091,.019,.15,n=6)
    if m.lod<2:
        # Smaller water jug within reach of the lip; a few shallow-marked pavers on the approach.
        m.jar(.301,.075,-.167,.055,"roof")
        if m.lod==0:
            for s in (-1,1):
                m.slab("accent",s*.149,0,-.386,.186,.030,.144,.97,cut=.15)
            m.disc(0,.226,-.319,.042,"gold",False)


def stone_arch(m,c):
    n=(12,8,4)[m.lod]
    r=.365
    spring=.238
    deck=.791
    for i in range(n):
        a,b=math.pi*i/n,math.pi*(i+1)/n
        x0,x1=c+r*math.cos(a),c+r*math.cos(b)
        y0,y1=spring+.410*math.sin(a),spring+.410*math.sin(b)
        for s in (-1,1):
            z=s*.165
            vertices=((x0,y0,z),(x1,y1,z),(x1,deck,z),(x0,deck,z))
            m.face("wall",vertices if s==-1 else vertices[::-1])
        m.face("wall",((x0,y0,-.165),(x0,y0,.165),(x1,y1,.165),(x1,y1,-.165)))
        if m.lod==0:
            # Bevelled arch stones break the spandrel face and cast a shallow shadow.
            a1,b1=a+.012,b-.012
            for s in (-1,1):
                def xy(t,rise,offset):
                    return (c+(r+rise)*math.cos(t),spring+(.410+rise)*math.sin(t),s*offset)
                innerA,innerB=xy(a1,0,.172),xy(b1,0,.172)
                outerB,outerA=xy(b1,.071,.176),xy(a1,.071,.176)
                face=(innerA,innerB,outerB,outerA)
                m.face("accent" if i in (5,6) else "plaster",face if s==-1 else face[::-1])


def aqueduct(m):
    # Local X follows the channel; the outlet and its service ladder face map south.
    with m.transform(rotate_y=-90):
        if m.era==1:
            for c in (-.88,0,.88):
                stone_arch(m,c)
            for x in (-1.32,-.44,.44,1.32):
                m.block("wall",x,0,0,.150,.791,.328)
                m.slab("fieldstone",x,0,0,.218,.147,.491,.85)
        elif m.era==2:
            for x in (-1.28,-.433,.433,1.28):
                m.slab("wall",x,0,0,.227,.15,.458,.92)
                m.slab("brick",x,.15,0,.146,.525,.32,.82)
                m.slab("trim",x,.675,0,.202,.116,.385,1.14)
            for c in (-.855,0,.855):
                for s in ((-1,1) if m.lod==0 else (0,)):
                    m.rod("metal",(c-.38,.47,s*.105),(c,.777,s*.139),.038,n=4)
                    m.rod("metal",(c,.777,s*.139),(c+.38,.47,s*.105),.038,n=4)
            if m.lod==0:
                for x in (-1.28,-.433,.433,1.28):
                    m.disc(x,.648,-.145,.061,"gold",False)
                    m.block("turquoise",x,.291,-.150,.075,.051,.044)
        else:
            for c in (-.928,0,.928):
                m.slab("wall",c,0,0,.383,.133,.494,.87)
                m.slab("glass",c,.133,0,.193,.387,.280,.82)
                for s in (-1,1):
                    m.rod("ivory",(c+s*.044,.448,0),(c+s*.291,.803,0),.083,n=(8,6,4)[m.lod])
                if m.lod<2:
                    m.disc(c,.309,-.14,.071,"gold",False)
        m.slab("trim",-0.005,.791,0,2.79,.092,.417,cut=.033,underside=True)
        for side in (-1,1):
            m.block("roof",-.005,.883,side*.179,2.795,.086,.069)
        # The open water trough continues to the lip, with its bed under both curbs.
        m.block("water",-.006,.883,0,2.792,.033,.289)
        if m.lod<2:
            # A ripple breaks at three weirs, which are spaced to line up with the bays.
            for c in (-.87,0,.87):
                m.block("turquoise",c,.913,0,.043,.012,.282)
            if m.lod==0:
                for x in (-1.25,-.43,.43,1.25):
                    for s in (-1,1):
                        m.block("gold",x,.803,s*.209,.064,.051,.012)
                # Station gauge on the inlet curb.
                m.block("trim",1.205,.968,.180,.057,.152,.043)
                m.disc(1.205,1.127,.173,.078,"gold",False)
        for side in (-1,1):
            m.rod("trim",(-1.417,.009,side*.072),(-1.364,.875,side*.072),.015,n=4)
        for i in range((7,4,2)[m.lod]):
            u=(i+1)/((7,4,2)[m.lod]+1)
            m.block("accent",-1.417+.053*u,.875*u,.0,.029,.026,.156)


def sack(m,x,y,z,r):
    m.drum("hide",x,y,z,r*.68,r*1.13,r,n=(8,6,4)[m.lod])
    m.drum("thatch",x,y+r*1.13,z,r,r*.48,r*.43,n=(8,6,4)[m.lod])
    if m.lod==0:
        m.drum("trim",x,y+r*1.6,z,r*.44,r*.14,n=6)


def grain_sorting_floor(m):
    # Three graduated gravity bins share a sorting floor. Each hopper has a
    # discharge slide to the transverse belt feeding the packing bay at right.
    works.court(m,w=1.91)
    for x,w,h in ((-.54,.56,1.38),(.025,.475,1.07),(.54,.38,.88)):
        z=.28
        for dz in (-.215,.215):
            m.slab("ivory",x,.08,z+dz,w*.72,.64,.10,.88,cut=.065)
        m.slab("metal",x,.483,z,.165,.237,.205,top=w/.165,cut=.09,underside=True)
        m.slab("ivory",x,.72,z,w,h-.72,.64,cut=.075)
        m.slab("turquoise",x,h,z,w+.039,.063,.68,.94,underside=True)
        m.slab("metal",x,h+.063,z,w*.87,.095,.56,.63,cut=.10)
        m.block("glass",x,h-.11,-.044,w*.67,.066,.022)
        with m.transform(translate=(x,.436,-.036),rotate_x=-15):
            m.box((0,0,0),(.17,.043,.69),m.color("metal"),material="metal",bottom=True)
            if m.lod<2:
                for side in (-1,1):
                    m.block("turquoise",side*.08,.0215,0,.018,.049,.68)
    # The head pulley projects beyond its support and discharges over an open receiver.
    for x in (-.66,.36):
        m.slab("metal",x,.08,-.38,.15,.169,.27,.82)
    m.slab("ivory",-.10,.249,-.38,1.43,.073,.33,.98,underside=True)
    m.block("trim",-.10,.322,-.38,1.37,.024,.236)
    if m.lod<2:
        for x in (-.57,-.28,.02,.30):
            m.mound("thatch",x,.346,-.38,.082,.051,n=(9,5)[m.lod])
    m.slab("turquoise",.72,.08,-.38,.29,.17,.36,1.11)
    m.mound("thatch",.72,.25,-.38,.138,.103,n=(12,7,5)[m.lod])
    if m.lod==0:
        sack(m,.73,.08,-.65,.070)
        # The grain cooperative's sheaf is cast into the tallest bin.
        m.rod("gold",(-.54,.87,-.059),(-.54,1.15,-.059),.012,n=5)
        for y in (.90,.99,1.07):
            for side in (-1,1):
                m.blade("gold",(-.54,y,-.060),(-.54+side*.078,y+.094,-.060),.027)


def granary(m):
    era=m.era
    if era==3:
        return grain_sorting_floor(m)
    if era==0:
        # Raised reed longhouse dries grain above the damp floodplain.
        works.court(m,-.16,w=1.86,role="soil")
        for x in (-.62,.51):
            for z in (-.22,.48):
                m.drum("timber",x,.08,z,.095,.31,.080)
                if m.lod<2:
                    m.drum("fieldstone",x,.31,z,.133,.045,.097)
        m.slab("timber",-.055,.39,.13,1.48,.09,1.05,underside=True)
        works.shell(m,-.055,.23,1.27,.73,.39,.48,material="thatch")
        m.hip_roof((-.055,.87,.23),1.46,.90,.49,m.color("thatch"),overhang=.022,material="thatch")
        m.stairs(-.055,.08,-.735,.28,.34,.40)
        for x in (-.54,.49):
            sack(m,x,.08,-.51,.132)
        works.cartouche(m,-.055,.81,-.145,.066)
        return
    for s in (-1,1):
        x=s*.444; z=.148+s*.099
        profile={
            0:[(.235,.225,"wall"),(.356,.39,"wall"),(.315,.737,"trim"),(.28,.761,"thatch"),
               (.382,.797,"thatch"),(.090,1.095,"trim"),(.065,1.12,"trim")],
            1:[(.291,.225,"wall"),(.372,.344,"wall"),(.359,.806,"turquoise"),(.374,.856,"wall"),
               (.34,.916,"roof"),(.39,.94,"roof"),(.123,1.239,"trim")],
            2:[(.25,.225,"metal"),(.331,.408,"wall"),(.331,1.027,"metal"),(.338,1.07,"roof"),
               (.095,1.253,"trim")],
            3:[(.19,.225,"ivory"),(.34,.439,"roof"),(.378,.775,"glass"),(.297,1.13,"ivory"),
               (.315,1.185,"roof"),(.122,1.341,"gold")] }[era]
        m.slab("fieldstone",x,0,z,.63,.102,.63,.90)
        for dx,dz in ((-.166,-.123),(.166,-.123),(0,.183)):
            m.block("timber" if era<2 else "metal",x+dx,.102,z+dz,.088,.125,.091)
        vessel(m,x,0,z,profile,(14,9,5)[m.lod])
        # A discharge hatch and short inclined chute serve each storage bin.
        m.slab("trim",x,.36,z-.286,.153,.156,.136,.86)
        with m.transform(translate=(x,.349,z-.389),rotate_x=-24):
            m.box((0,0,0),(.169,.043,.213),m.color("wall"),material="wall",bottom=True)
        if m.lod<2:
            m.disc(x,.637,z-.335,.085)
        if m.lod==0:
            for sign in (-1,1):
                m.rod("trim",(x+sign*.133,.109,z-.150),(x+sign*.213,.286,z-.18),.021,n=5)
        if m.lod==0 and era>=2:
            for h in (.54,.69,.84):
                m.drum("trim",x,h,z,.334 if era==2 else .362,.024,n=14)
    m.slab("wall",0,0,-.59,1.50,.076,.402,cut=.06)
    for i,(x,z,r) in enumerate(((-.46,-.56,.105),(-.65,-.64,.085),(.459,-.604,.118))):
        if i==1 and m.lod>0:
            continue
        sack(m,x,.076,z,r)
    if era>=2:
        m.slab("metal",0,0,.479,.166,1.166,.232,.89)
        m.slab("roof",0,1.166,.479,.238,.151,.262,.85)
        for s in (-1,1):
            m.rod("accent",(0,1.25,.475),(s*.442,.982,.15+s*.099),.041)
    elif era==1:
        m.slab("fieldstone",0,0,.455,.264,.675,.243,.78)
        m.disc(0,.605,.366,.078,"gold",False)


def cargo(m,x,y,z,w,d):
    m.block("timber",x,y,z,w,w*.80,d)
    if m.lod<2:
        for s in (-1,1):
            m.block("accent",x+s*w*.30,y+w*.012,z,.025,w*.80,d+.016)
        if m.lod==0:
            m.rod("trim",(x-w*.44,y+w*.08,z-d*.50-.009),
                  (x+w*.44,y+w*.71,z-d*.50-.009),.014,n=4)


def store(m):
    era=m.era
    z=.119
    if era==0:
        for x in (-.683,0,.683):
            for dz in (-.163,.265):
                m.drum("timber",x,0,dz,.055,.156,n=(7,5,4)[m.lod])
    else:
        m.slab("wall",0,0,.035,1.79,.145,.73,.92)
    m.slab("trim",0,.145,.014,1.829,.066,.815,cut=.047,underside=True)
    y=.211
    h=(.357,.449,.398,.438)[era]
    m.slab("wall",-.195,y,z,1.241,h,.528)
    front=z-.264
    # Large double cargo door with two separate leaves and an overhead loading hood.
    for s in (-1,1):
        m.block("door",-.205+s*.121,y,front-.006,.233,h*.81,.022)
    m.block("accent",-.205,y+h*.807,front,.572,.050,.089)
    m.rooflet(-.195,y+h,z,1.37,.638,(.249,.329,.151,.186)[era],m.skin)
    if era>=2:
        m.slab("glass",-.193,y+h+.101,z,.69,.16,.25,.84)
        m.slab("roof",-.193,y+h+.261,z,.635,.052,.25,.86)
    for dx in (.442,.81):
        m.block("trim",dx,y,-.151,.055,.31,.061)
    m.block("wall",.626,y,.218,.393,.352,.203)
    with m.transform(translate=(.626,y+.336,.067),rotate_x=-8):
        m.box((0,0,0),(.495,.070,.54),m.color("roof"),material="roof",bottom=True)
    cargo(m,.61,y,-.068,.208,.199)
    m.stairs(-.209,0,-.493,.438,.0995,.211)
    if m.lod<2:
        m.disc(-.605,y+h*.50,front-.003,.074)
        for s in (-1,1):
            m.block("trim",-.205+s*.225,y+.027,front-.024,.024,h*.69,.022)
        if m.lod==0:
            sack(m,-.687,y,-.244,.073)
            m.block("metal",-.192,y+h*.37,front-.033,.063,.022,.016)
            m.rod("trim",(-.398,y+h*.88,front),(-.398,y+h*.92,front-.137),.024,n=5)
            m.rod("thatch",(-.398,y+h*.92,front-.137),(-.398,y+h*.53,front-.137),.012,n=4)
            m.ring("gold",-.398,y+h*.48,front-.149,.033,.020,.024,n=8)
    if era==1:
        m.rooflet(-.551,y+h+.221,z,.47,.335,.221,"roof")
    if era==3:
        m.drum("metal",-.622,y+h+.123,z,.051,.259,.034)
        m.disc(-.622,y+h+.389,z,.066,"ascension",False)


def turbine_hall(m,x,z,y,w,d):
    m.slab("wall",x,y,z,w,.54,d,.93)
    m.slab("glass",x,y+.54,z,w*.948,.152,d*.952,.94)
    m.slab("trim",x,y+.692,z,w*1.012,.077,d*1.01,.96,underside=True)
    m.slab("roof",x,y+.769,z,w*.73,.178,d*.64,.70)
    m.slab("wall",x,y,z-d*.461,.42,.34,.264)
    m.entry(x,y,z-d*.461-.132,.248,.288)


def power(m):
    future=m.era==3
    if future:
        # Two banks of tracking mirrors surround the receiver. A tapered shaft
        # and gold absorber replace the combustion plant's stacks and wet tower.
        with m.transform(scale=1.45):
            works.court(m,-.30)
            works.shell(m,-.30,.30,.85,.71,.33)
            m.slab("metal",-.30,.41,.30,.92,.082,.78,.93,underside=True)
            m.slab("ivory",-.30,.492,.30,.38,.68,.38,.45)
            m.drum("gold",-.30,1.172,.30,.147,.21,.124)
            m.mound("emissive",-.30,1.382,.30,.127,.09)
            for x,z,w,angle in ((-.44,-.44,.79,-29),(.45,-.43,.72,-25),
                                (.54,.32,.55,21)):
                if m.lod==2:
                    m.slab("wall",x,.08,z,.19,.315,.20,.69)
                else:
                    m.slab("wall",x,.08,z,w*.55,.135,.32,.81)
                    m.drum("metal",x,.215,z,.085,.18,.063)
                works.panel(m,x,.395,z,w,.47,angle)
        return
    m.slab("fieldstone",0,0,.025,2.78,.102,2.67,.96)
    turbine_hall(m,-.553,-.13,.102,1.19,1.44)
    # A pinched, open cooling tower stands on a ring of concrete pile caps.
    x,z=.565,.41
    n=(18,11,7)[m.lod]
    for i in range((6,4,3)[m.lod]):
        a=2*math.pi*i/(6,4,3)[m.lod]
        m.slab("wall",x+.266*math.cos(a),.102,z+.266*math.sin(a),.147,.195,.14,.86)
    vessel(m,x,.297,z,[(.406,0,"wall"),(.419,.112,"wall"),(.315,.477,"wall"),
        (.252,.849,"wall"),(.276,1.05,"roof"),(.307,1.15,"trim"),(.311,1.21,"ivory"),
        (.253,1.21,"trim"),(.235,.98,"door")],n)
    if future:
        # The tilted collectors flank a very visible coolant bank.
        for xx,zz in ((.452,-.65),(-.54,.909)):
            m.slab("wall",xx,.102,zz,.48,.124,.40,.85)
            m.rod("ivory",(xx,.226,zz),(xx,.503,zz),.072)
            with m.transform(translate=(xx,.507,zz),rotate_x=-22):
                m.box((0,0,0),(.928,.071,.487),m.color("roof"),material="roof",bottom=True)
                for s in (-1,1):
                    m.block("glass",s*.221,.0355,0,.417,.014,.422)
        if m.lod<2:
            m.disc(x,1.13,z-.232,.116,"gold",False)
    else:
        for xx,zz,h in ((-.97,.85,1.52),(-.538,1.00,1.21)):
            m.slab("brick",xx,.102,zz,.277,.319,.29,.89)
            # The rolled rim turns into a dark flue below the crest. Building
            # the shaft and liner together leaves the chimney mouth open.
            vessel(m,xx,.421,zz,[(.129,0,"brick"),(.074,h-.041,"trim"),
                   (.107,h-.017,"metal"),(.107,h+.063,"trim"),
                   (.079,h+.063,"door"),(.064,h-.169,"door")],(14,9,6)[m.lod])
        m.slab("wall",.675,.102,-.667,.69,.37,.568,.91)
        m.slab("trim",.675,.472,-.667,.72,.071,.591,underside=True)
    if m.lod<2:
        for zz in (.13,.59):
            m.rod("metal",(.040,.432,zz),(.40,.432,zz),.065,n=(10,6)[m.lod])
        m.disc(-.553,.576,-.820,.078)
        if m.lod==0:
            for xx in (-1.002,-.102):
                m.block("turquoise",xx,.35,-.812,.110,.142,.071)
    m.stairs(-.553,0,-1.425,.413,.168,.102)


def fusion(m):
    m.slab("wall",0,0,.055,2.82,.13,2.61,.92)
    m.slab("roof",0,.13,.155,2.21,.258,1.95,.89)
    m.drum("ivory",0,.388,.181,.841,.175,.71,n=(18,12,7)[m.lod])
    vessel(m,0,.563,.181,[(.58,0,"glass"),(.714,.241,"ascension"),(.689,.371,"glass"),
                            (.531,.556,"ivory"),(.287,.614,"gold")],(20,12,7)[m.lod])
    m.mound("ascension",0,1.177,.181,.268,.217)
    # Closed toroidal magnetic collar. Its eight external feet sink into the drum.
    with m.transform(translate=(0,.838,.181),rotate_x=90):
        m.ring("gold",0,0,0,.91,.671,.106,n=(26,16,10)[m.lod])
    for i in range((8,6,4)[m.lod]):
        a=2*math.pi*i/(8,6,4)[m.lod]
        dx,dz=math.cos(a),math.sin(a)
        m.rod("ivory",(.67*dx,.414,.181+.67*dz),(.841*dx,.795,.181+.841*dz),.076,n=(7,5,4)[m.lod])
        if m.lod<2:
            with m.transform(translate=(.812*dx,.836,.181+.812*dz),rotate_y=-math.degrees(a)):
                m.slab("roof",0,0,0,.211,.098,.112,.75)
    m.slab("wall",0,.13,-.784,.732,.392,.47,.93)
    m.slab("trim",0,.522,-.784,.779,.067,.512,.98,underside=True)
    m.entry(0,.13,-1.006,.27,.32)
    m.stairs(0,0,-1.431,.462,.285,.13)
    if m.lod<2:
        m.disc(.246,.41,-.999,.055,"gold",False)
        for s in (-1,1):
            m.slab("ivory",s*1.035,.13,-.629,.259,.287,.471,.71)
            if m.lod==0:
                for t in (-1,1):
                    m.block("turquoise",s*1.035+t*.043,.417,-.628,.036,.040,.241)


def build(spec,lod):
    m=Delta(spec,lod)
    {"Well":well,"Aqueduct":aqueduct,"Granary":granary,"Storehouse":store,
     "PowerPlant":power,"FusionReactor":fusion}[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
