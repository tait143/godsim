"""The Copper Road: charter houses, covered bazaars and freight interchanges."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


def disc(m, mat, x, y, z, r, d):
    """Front-facing minted tablet, its back at z."""
    with m.transform(translate=(x,y,z), rotate_x=-90):
        m.drum(mat,0,0,0,r,d,n=(16,10,8)[m.lod])


def lozenge(m, mat, x, y, z, w, h, depth):
    m.relief(mat,[(x-w/2,y),(x,y-h/2),(x+w/2,y),(x,y+h/2)],z,depth)


def portal(m,x,y,z,w,h):
    # A shoulder-cut voussoir framing two dark doors and the inlaid lintel key.
    def cut(w,h,base):
        a=w/2
        return [(x-a,base),(x+a,base),(x+a,base+h*.72),(x+a*.51,base+h),
                (x-a*.51,base+h),(x-a,base+h*.72)]
    m.relief("trim",cut(w,h,y),z-.051,.076)
    m.relief("door",cut(w*.71,h*.84,y+.007),z-.061,.019)
    if m.lod<2:
        lozenge(m,"gold",x,y+h*.89,z-.071,w*.20,h*.20,.019)
        if m.lod==0:
            for sign in (-1,1):
                m.block("gold",x+sign*w*.086,y+h*.24,z-.068,w*.034,h*.13,.012)


def mullions(m, x, y, z, w, h, n):
    m.block("glass",x,y,z,w,h,.033)
    if m.lod<2:
        for i in range(1,n):
            m.block("trim",x+w*(i/n-.5),y-.004,z-.019,.022,h+.012,.023)


def canopy(m,x,y,z,w,d,rise,material):
    """Hipped tent or folded sheet metal roof with a substantial flat ridge."""
    m.slab(material,x,y,z,w,rise,d,.09,.45)
    if m.lod<2:
        m.slab("timber" if material=="hide" else "gold",x,y+rise-.007,z,w*.56,.032,d*.56,.10)


def goods(m,x,y,z,role):
    m.block("timber",x,y,z,.23,.085,.19,.05)
    for u,v in ((-.055,-.032),(.049,-.031),(.008,.048)):
        m.dome((x+u,y+.084,z+v),.053,m.color(role),rings=1,segments=6,
               squash=.91,material=role)


def market(m):
    e=m.era
    if e==3:
        return kiosk_market(m)
    m.slab("fieldstone",0,0,.015,1.85,.117,1.66,.12)
    # The raised stone shop behind the open sales counters doubles as a roof abutment.
    h=(.60,.71,.67,.76)[e]
    m.slab("wall",0,.112,.31,1.49,h-.112,.82,.065)
    portal(m,0,.117,-.10,.38,.45)
    m.slab("roof",0,h-.012,.31,1.64,.123,.93,.07,.17)
    # Each post has a scribed, sloping head fitted to the underside of the awning.
    post=("timber","trim","metal","metal")[e]
    slope=.185/.638 if m.lod==2 else .057/.160
    for x in (-.682,.685):
        outline=[(x-.043,-.667),(x+.043,-.667),(x+.043,-.579),(x-.043,-.579)]
        head=[(u,h-.116+(z+.708)*slope,z) for u,z in outline]
        foot=[(u,.115,z) for u,z in outline]
        for i in range(4):
            j=(i+1)%4
            m.face(post,(foot[j],foot[i],head[i],head[j]))
        m.face(post,head[::-1])
    # Canopy ribs follow the dip over the counter. The stripes occupy adjoining
    # sections of one shell so the ivory selvedge stays distinct from the cloth.
    roof_y=h+.060
    count=(7,5,3)[m.lod]
    for i in range(count):
        a=-.829+1.658*i/count
        b=-.829+1.658*(i+1)/count
        role=("banner" if i%2==0 else ("plaster" if e==1 else "ivory")) if e<2 else ("roof" if i%2==0 else "glass")
        points=[(-.708,roof_y-.172),(-.548,roof_y-.115),(-.284,roof_y-.074),(-.07,roof_y+.013)]
        if m.lod==2: points=[points[0],points[-1]]
        for (z0,y0),(z1,y1) in zip(points,points[1:]):
            m.face(role,[(a,y0,z0),(a,y1,z1),(b,y1,z1),(b,y0,z0)])
        lip=roof_y-.177
        m.relief(role,[(a,lip-.061),(a+(b-a)*.48,lip-.103),(b,lip-.061),
                       (b,lip+.008),(a,lip+.008)],-.71,.018,open_edges=(2,4))
    for sign in (-1,1):
        x=sign*.45
        m.block("wall",x,.114,-.641,.55,.252,.327,.07)
        m.block("timber" if e<2 else "accent",x,.362,-.653,.595,.055,.378)
        if m.lod==0:
            goods(m,x-.124,.416,-.768,"leaf" if sign<0 else "flower")
            goods(m,x+.127,.416,-.768,"thatch" if sign<0 else "ivory")
    if e==0:
        canopy(m,0,h+.109,.31,1.44,.77,.283,"hide")
    elif e==1:
        m.drum("wall",0,h+.108,.32,.337,.162,.31)
        canopy(m,0,h+.265,.32,.82,.74,.187,"wall")
    elif e==2:
        for x in (-.43,.43):
            m.vault("glass" if x<0 else "metal",x,h+.109,.32,.68,.238,.72)
    else:
        m.slab("glass",0,h+.108,.30,.91,.31,.67,.20,.31)
        m.drum("accent",0,h+.412,.30,.202,.066,.157)
    if m.lod<2:
        # A stamped trader's coin and two vault lamps face the aisle.
        m.block("trim",0,h-.203,-.691,1.454,.063,.064)
        disc(m,"gold",0,h-.208,-.719,.084,.038)
        if m.lod==0:
            lozenge(m,"trim",0,h-.208,-.762,.080,.101,.012)
            for side in (-1,1):
                m.lamp(side*.722,.308,-.62,.064)


def kiosk_market(m):
    # A public market street runs between two mushroom-roofed sales islands.
    m.slab("fieldstone",0,0,.018,1.90,.115,1.73,.18)
    for x,z,r in ((-.502,-.22,.365),(.473,.028,.398)):
        m.drum("wall",x,.108,z,r*.81,.309,r*.77)
        m.drum("trim",x,.411,z,r*.937,.061,r*.917)
        m.drum("metal",x,.467,z,r*.176,.438,r*.135)
        m.drum("accent",x,.896,z,r*.782,.071,r*1.11)
        m.drum("glass",x,.963,z,r*1.112,.173,r*.323)
        if m.lod<2:
            goods(m,x,.469,z-r*.57,"leaf" if x<0 else "flower")
        if m.lod==0:
            for side in (-1,1):
                m.drum("gold",x+side*r*.565,.47,z-.012,.044,.059)
    m.slab("wall",-.089,.112,.569,1.211,.316,.354,.125)
    m.slab("glass",-.089,.422,.569,1.165,.164,.342,.125)
    m.vault("ivory",-.089,.58,.569,1.291,.127,.402)
    portal(m,-.201,.118,.392,.283,.272)
    if m.lod<2:
        m.block("metal",.21,.683,.565,.085,.166,.109)
        disc(m,"gold",.21,.875,.599,.132,.082)
    if m.lod==0:
        lozenge(m,"trim",.21,.875,.513,.106,.128,.021)
        for x in (-.236,.193):
            m.block("accent",x,.116,-.633,.095,.015,.196)


def vault_bank(m):
    e=m.era
    m.slab("fieldstone",0,0,.046,1.86,.181,1.54,.14)
    h=(0,.78,.86,.70)[e]
    m.slab("wall",0,.168,.104,1.60,h-.168,1.27,.16)
    for sign in (-1,1):
        x=sign*.65
        m.block("fieldstone" if e<3 else "metal",x,.117,-.409,.283,h-.075,.428,.28)
    m.slab("trim",0,h-.023,.111,1.76,.099,1.38,.14)
    m.slab("roof",0,h+.072,.112,1.63,.17,1.28,.16,.16)
    radius=(0,.344,.342,.37)[e]
    cy=max(h*.49+.039,radius+.091)
    # The visible weight of the vault is concentrated in its oversized coin gate.
    disc(m,"trim",0,cy,-.535,radius+.067,.092)
    disc(m,"gold",0,cy,-.622,radius,.043)
    disc(m,"door" if e<3 else "glass",0,cy,-.660,radius*.79,.036)
    if m.lod<2:
        for j in range(4 if m.lod==1 else 8):
            a=(j+.5)*math.tau/(4 if m.lod==1 else 8)
            disc(m,"trim",math.cos(a)*radius*.90,cy+math.sin(a)*radius*.90,-.666,radius*.053,.015)
        lozenge(m,"accent",0,cy,-.722,radius*.79,radius*.95,.054)
        if m.lod==0:
            lozenge(m,"gold",0,cy+.018,-.732,radius*.36,radius*.51,.016)
    y=h+.237
    if e==1:
        m.drum("wall",0,y,.16,.452,.139,.414)
        m.dome((0,y+.136,.16),.47,m.color("roof_tile"),rings=(3,2,1)[m.lod],
               segments=(12,8,6)[m.lod],squash=.62,material="roof_tile")
        m.drum("gold",0,y+.41,.16,.068,.092,.044)
    elif e==2:
        m.vault("roof",0,y,.16,1.17,.25,.86)
        for x in (-.43,.43):
            m.stack(x,y+.102,.37,.091,.39,"brick")
    else:
        m.slab("glass",0,y,.16,1.31,.237,1.001,.22,.21)
        m.slab("ivory",0,y+.23,.16,1.15,.103,.83,.22)
        m.slab("glass",0,y+.33,.16,.78,.25,.57,.21,.17)
        m.slab("gold",0,y+.574,.16,.70,.048,.53,.21)
    if m.lod==0:
        for sign in (-1,1):
            x=sign*.726
            m.lamp(x,.176,-.607,.089)
            lozenge(m,"gold",sign*.65,h*.62,-.616,.09,.121,.085)
            # Amber counting-room lights lie between the deep stone door abutments.
            for zz in (.02,.28):
                m.block("glass",sign*.799,.34,zz,.024,.176,.152)
            m.block("gold",sign*.801,.315,.15,.027,.026,.488)
        for j in range(3):
            m.block("slate",0,.004+j*.039,-.846+j*.066,.70-j*.079,.046,.12)


def charter_bridge(m):
    # Delegates pass below a glazed assembly bridge linking the two guild lodges.
    for s in (-1,1):
        x=s*.605
        m.slab("fieldstone",x,0,.133,.520,.123,1.24,.165)
        m.slab("wall",x,.123,.124,.425,.370,1.10,.16,.18)
        for z in (-.176,.465):
            m.beam("ivory",(x,.452,z),(s*.403,1.053,z),.217,.202)
        portal(m,x,.122,-.424,.239,.283)
    m.slab("trim",0,1.010,.155,1.69,.142,.893,.22)
    m.slab("glass",0,1.152,.160,1.58,.286,.792,.24,.10)
    m.vault("ivory",0,1.438,.162,1.57,.211,.823)
    m.slab("slate",0,0,-.510,.693,.077,.681,.18)
    disc(m,"gold",0,1.255,-.257,.177,.047)
    disc(m,"banner",0,1.255,-.295,.134,.026)
    if m.lod<2:
        m.rune(0,1.259,-.328,.169,"gold")
        for s in (-1,1):
            m.block("trim",s*.432,1.158,-.242,.041,.251,.051)
            m.lamp(s*.246,.077,-.63,.109)
    if m.lod==0:
        for s in (-1,1):
            m.block("gold",s*.605,.188,-.492,.240,.041,.037)
            for z in (.051,.271):
                m.block("glass",s*.804,.275,z,.024,.104,.141)
            m.slab("gold",s*.690,1.127,.155,.119,.034,.631,.18)


def guildhall(m):
    e=m.era
    if e==3:
        return charter_bridge(m)
    m.slab("fieldstone",0,0,.07,1.84,.146,1.60,.11)
    h=(0,.65,.74,.69)[e]
    m.slab("wall",-.053,.138,.135,1.57,h-.138,1.24,.09)
    m.slab("roof",-.053,h-.014,.15,1.72,.141,1.37,.09,.16)
    # Two independent lantern roofs flank the much taller charter chamber.
    for s in (-1,1):
        x=s*.586
        m.block("trim",x,.127,-.431,.185,h-.05,.21,.16)
        canopy(m,x,h+.122,.20,.46,.87,.16,"roof_tile" if e==1 else "metal")
    y=h+.121
    m.slab("wall" if e<3 else "glass",0,y,.207,.722,.33,.828,.13)
    if e==1:
        m.vault("roof",0,y+.321,.203,.87,.295,.99)
    elif e==2:
        m.slab("roof",0,y+.321,.203,.91,.11,.99,.12,.05)
        m.slab("brick",-.09,y+.424,.27,.55,.26,.63,.10)
        m.slab("metal",-.09,y+.68,.27,.64,.061,.70,.10)
    else:
        m.slab("ivory",0,y+.323,.203,.91,.081,.99,.14)
        m.slab("glass",0,y+.398,.211,.615,.294,.67,.15,.23)
        canopy(m,0,y+.688,.219,.602,.627,.106,"gold")
    portal(m,0,.12,-.498,.512,.47)
    # The charter seal is a rack of two opposing hammers, cast into a brass tablet.
    disc(m,"gold",0,y+.16,-.218,.168,.059)
    disc(m,"banner",0,y+.16,-.271,.131,.028)
    if m.lod<2:
        for sign in (-1,1):
            with m.transform(translate=(sign*.012,y+.163,-.310-sign*.006),rotate_z=sign*39):
                m.block("ivory",0,-.082,0,.025,.168,.016)
                m.block("gold",0,.052,-.003,.098,.044,.022)
        for s in (-1,1):
            mullions(m,s*.425,.314,-.496,.184,.193,2)
    if m.lod==0:
        for s in (-1,1):
            x=s*.687
            m.block("trim",x,.17,-.672,.052,.64,.057)
            m.block("gold",x,.772,-.670,.20,.030,.048)
            m.relief("banner",[(x-.067,.713),(x,.541),(x+.067,.713),
                               (x+.067,.77),(x-.067,.77)],-.697,.022)
            m.lamp(s*.301,.139,-.605,.095)
            for zz in (.01,.288):
                m.block("glass",-.053+s*.785,.33,zz,.026,.148,.171)
            m.block("gold",-.053+s*.788,.308,.15,.032,.023,.506)


def ship(m,x,z,e):
    # Broad merchant hull, originally sculled and sailed, later fitted with a wheelhouse.
    with m.transform(translate=(x,0,z)):
        outline=[(0,-.81),(.232,-.55),(.266,.366),(.166,.604),(-.166,.604),(-.266,.366),(-.232,-.55)]
        def ring(sx,sz,y,bow):
            return [(u*sx,y+max(0,-v-.25)*bow,v*sz) for u,v in outline]
        keel,rail=ring(.65,.92,.025,.112),ring(1,1,.275,.21)
        for i in range(len(rail)):
            j=(i+1)%len(rail)
            m.face("timber" if e==1 else "roof",(keel[j],keel[i],rail[i],rail[j]))
        deck=ring(.91,.97,.303,.21)
        for i in range(len(rail)):
            j=(i+1)%len(rail)
            m.face("gold",(rail[j],rail[i],deck[i],deck[j]))
        # Facet the deck longitudinally across the raised bow.
        for a,b,c,d in ((1,6,5,2),(2,5,4,3)):
            m.face("timber",(deck[a],deck[b],deck[c],deck[d]))
        m.face("timber",(deck[0],deck[6],deck[1]))
        if e==1:
            m.drum("trim",0,.293,.01,.045,.99,.026,n=(8,6,4)[m.lod])
            # Curved broadside sail, filled to starboard. The lower edge clears the main hatch.
            profile=[(.59,.035,.27),(1.00,.155,.354),(1.196,.045,.285)]
            for i in range(len(profile)-1):
                y0,x0,w0=profile[i]; y1,x1,w1=profile[i+1]
                for sign in (-1,1):
                    m.face("plaster" if i==0 else "banner",
                        [(x0+sign*.011,y0,-w0),(x1+sign*.011,y1,-w1),
                         (x1+sign*.011,y1,w1),(x0+sign*.011,y0,w0)][::sign])
                    m.face("trim",[(x0-.011,y0,sign*w0),(x0+.011,y0,sign*w0),
                                   (x1+.011,y1,sign*w1),(x1-.011,y1,sign*w1)][::sign])
            for idx,sign in ((0,-1),(-1,1)):
                yy,xx,width=profile[idx]
                m.face("trim",[(xx-.011,yy,-width),(xx-.011,yy,width),
                               (xx+.011,yy,width),(xx+.011,yy,-width)][::sign])
            m.beam("timber",(.043,1.20,-.31),(.043,1.20,.31),.037)
            if m.lod<2:
                m.beam("trim",(0,1.25,0),(0,.32,-.696),.017)
                m.beam("trim",(0,1.25,0),(0,.309,.521),.017)
        else:
            for j in range(1 if m.lod==2 else (2 if e==2 else 3)):
                m.block("banner" if j%2==0 else "accent",0,.303,-.418+j*.224,.337,.149,.187)
            if m.lod==0:
                for j in range(2 if e==2 else 3):
                    m.block("timber" if e==2 else "ivory",0,.447,-.418+j*.224,.34,.024,.035)
            cabin_z=.372
            m.slab("ivory",0,.302,cabin_z,.37,.221,.30,.09)
            m.block("glass",0,.447,cabin_z-.153,.31,.060,.022)
            m.slab("roof",0,.515,cabin_z,.42,.071,.341,.07)
            if m.lod==2:
                m.block("roof",0,.575,cabin_z,.12,.19,.12,.2)
            elif e==2:
                m.stack(0,.579,cabin_z,.073,.33,"metal")
            else:
                m.drum("metal",0,.578,cabin_z,.030,.238,.020)
                m.drum("turquoise",0,.81,cabin_z,.045,.044,.023)
        if m.lod==0:
            m.drum("accent",0,.325,-.586,.054,.09,.043)
            m.drum("trim",-.14,.296,.474,.037,.11)


def harbor(m):
    e=m.era
    m.slab("fieldstone",-.406,0,.018,1.01,.177,1.74,.11)
    m.block("timber" if e==1 else "trim",-.087,.16,-.118,.37,.077,1.43)
    m.slab("wall",-.438,.172,.316,.706,.421,.72,.13)
    if e==3:
        m.slab("ivory",-.438,.582,.316,.827,.073,.853,.13)
        m.slab("glass",-.438,.649,.33,.706,.244,.686,.16,.32)
        m.slab("roof",-.438,.887,.33,.514,.063,.516,.15)
    else:
        m.vault("wall" if e==1 else "roof",-.438,.582,.316,.83,.211,.85)
        if e==1:
            m.slab("roof_tile",-.438,.787,.316,.392,.055,.879,.06,.05)
        if e==2 and m.lod<2:
            m.vault("metal",-.439,.764,.321,.318,.139,.578)
            m.block("glass",-.278,.780,.313,.032,.061,.408)
    portal(m,-.442,.173,-.044,.314,.326)
    # A small davit becomes a counterweighted pedestal crane and then a gantry.
    x,z=-.407,-.46
    m.drum("trim",x,.172,z,.167,.129,.128)
    height=(0,.80,1.01,1.13)[e]
    m.block("timber" if e==1 else "metal",x,.292,z,.127,height-.292,.145,.12)
    m.beam("trim",(x-.086,height+.021,z),(x+.512,height+.124,z),.078)
    m.beam("trim",(x,height*.62,z+.009),(x+.401,height+.089,z+.009),.054)
    m.beam("gold",(x+.492,height+.12,z),(x+.492,height*.60,z),.022)
    if e==3:
        m.block("turquoise",x+.155,height+.073,z-.047,.347,.055,.050)
        m.block("ivory",x-.123,height-.074,z,.25,.143,.182,.09)
    if e==3 and m.lod<2:
        m.block("wall",x-.14,.178,.02,.09,.835,.109)
        m.beam("metal",(x-.14,1.002,.02),(x,1.109,z),.095)
    if m.lod<2:
        m.slab("timber",x+.49,height*.60-.116,z,.178,.117,.169,.09)
        for sx in (-.76,-.056):
            m.drum("trim",sx,.171,-.704,.048,.153,.04)
        if m.lod==0:
            for i in range(2):
                m.block("timber",-.722,.177,-.251+i*.18,.158,.161,.147)
            # Side loading hatch and the blue-and-brass harbor master's mark.
            m.block("trim",-.084,.318,.297,.024,.198,.342)
            m.block("glass",-.071,.347,.297,.018,.132,.272)
            # A stout coil of hawser, with the running end turned about its cleat.
            rings=[]
            for i in range(10):
                angle=math.tau*i/10
                rings.append([(-.674+r*math.cos(angle),yy,-.512+.93*r*math.sin(angle))
                              for r,yy in ((.116,.195),(.096,.215),(.076,.195),(.096,.175))])
            for a,b in zip(rings,rings[1:]+rings[:1]):
                for j in range(4):
                    k=(j+1)%4
                    m.face("hide",(a[j],a[k],b[k],b[j]))
            m.beam("timber",(-.677,.191,-.503),(-.72,.195,-.695),.030)
            lozenge(m,"gold",-.443,.656,-.128,.131,.118,.077)
            disc(m,"gold",x,height*.55,z-.074,.077,.031)
            for k in range(3):
                m.block("metal",-.51+k*.225,.173,-.741,.108,.007,.045)
    ship(m,.483,.072,e)


def aerial_station(m):
    # A through-running monorail crosses above street level. One lift and
    # a covered side platform serve the articulated cars at beam height.
    for x in (-.997,1.001):
        m.slab("fieldstone",x,0,-.218,.457,.134,.559,.21)
        m.slab("ivory",x,.134,-.218,.301,.681,.344,.17,.16)
        m.slab("trim",x,.815,-.218,.421,.172,.469,.14,-.17)
    m.slab("metal",0,.987,-.218,2.78,.157,.299,.13)
    for x,w in ((-.590,1.17),(.561,1.018)):
        m.slab("trim",x,1.144,-.218,w*.80,.108,.248,.17)
        m.slab("ivory",x,1.252,-.218,w,.245,.511,.23,-.035)
        m.slab("glass",x,1.497,-.218,w*1.02,.171,.527,.23,.15)
        m.slab("roof",x,1.668,-.215,w*.874,.053,.451,.21,.10)
        if m.lod==0:
            for k in (-1,0,1):
                m.block("metal",x+k*w*.238,1.507,-.473,.041,.130,.029)
    m.slab("fieldstone",-.196,0,.468,.719,.121,.779,.19)
    m.slab("wall",-.196,.121,.468,.556,1.035,.621,.18)
    m.slab("slate",-.007,1.150,.296,1.73,.102,.465,.145)
    portal(m,-.196,.125,.151,.374,.518)
    if m.lod<2:
        for x in (-.587,.587):
            m.block("trim",x,1.252,.451,.063,.515,.078)
        m.slab("metal",0,1.767,.374,1.51,.074,.429,.15,.13)
        for side in (-1,1):
            m.block("glass",-.196+side*.278,.393,.459,.030,.564,.246)
    if m.lod==0:
        for x in (-.39,0,.39):
            m.block("gold",x,1.256,.180,.180,.017,.056)
        m.slab("slate",-.195,0,.030,.475,.063,.217,.12)
        disc(m,"emissive",-.196,.866,.151,.118,.027)


def rail(m):
    e=m.era
    if e==3:
        return aerial_station(m)
    # The double track and adjoining stepped platform read as rail at all distances.
    m.slab("fieldstone",-.444,0,.04,1.55,.157,2.68,.09)
    m.slab("slate",.811,0,0,.966,.069,2.78,.05)
    for x in (.604,1.005):
        m.block("metal",x,.067,0,.063,.051,2.74)
    if m.lod<2:
        for j in range((9,5)[m.lod]):
            z=-1.21+j*(2.42/(8,4)[m.lod])
            m.block("timber",.801,.070,z,.74,.023,.079)
    m.slab("wall",-.474,.151,.39,1.16,.57,1.61,.09)
    portal(m,-.477,.157,-.419,.41,.425)
    m.vault("roof" if e==2 else "glass",-.474,.71,.39,1.44,.27 if e==2 else .352,1.85)
    if e==2:
        m.slab("brick",-.895,.715,-.164,.337,.566,.362,.10)
        canopy(m,-.895,1.274,-.164,.428,.453,.209,"roof")
        disc(m,"ivory",-.895,1.113,-.347,.115,.026)
        if m.lod<2:
            m.beam("trim",(-.895,1.116,-.381),(-.895,1.195,-.381),.017)
            m.beam("trim",(-.895,1.116,-.395),(-.833,1.091,-.395),.017)
    else:
        m.slab("ivory",-.448,.956,.21,1.04,.113,1.34,.12)
        m.slab("glass",-.448,1.061,.21,.861,.197,1.05,.12,.21)
    # Dwarven shunter or streamlined passenger shuttle rests directly on the tracks.
    x=.804
    m.slab("metal",x,.113,-.03,.49,.139,1.48,.11)
    if e==2:
        with m.transform(translate=(x,.455,-.615),rotate_x=90):
            m.drum("roof",0,0,0,.202,.849)
        disc(m,"gold",x,.455,-.615,.174,.029)
        m.slab("wall",x,.245,.411,.488,.45,.479,.07)
        m.slab("roof",x,.688,.410,.54,.07,.536,.05)
        m.stack(x,.596,-.439,.081,.239,"metal")
        if m.lod<2:
            mullions(m,x,.475,.162,.311,.181,2)
            for side in (-1,1):
                for zz in (-.46,.00,.42):
                    with m.transform(translate=(x+side*.237,.232,zz),rotate_z=-side*90):
                        m.drum("metal",0,0,0,.113,.042,n=8 if m.lod==0 else 5)
            if m.lod==0:
                m.slab("metal",-.484,.95,.41,.446,.116,1.105,.13,.11)
                m.slab("glass",-.485,1.06,.413,.37,.075,.951,.08)
    else:
        m.slab("ivory",x,.246,-.03,.53,.277,1.58,.23,.05)
        m.slab("glass",x,.518,.04,.481,.176,1.26,.24,.27)
        m.slab("roof",x,.689,.06,.354,.058,.9,.18)
    if m.lod<2:
        for j in range(3 if m.lod==0 else 2):
            xx=-1.013+j*.482
            m.block("trim",xx,.154,-.728,.073,.488,.079)
        m.slab("accent",-.521,.636,-.609,1.13,.066,.473,.09)
        if m.lod==0:
            m.lamp(-.93,.151,-1.01,.128)
            m.block("timber",-.47,.153,-.929,.33,.087,.152)


def airport(m):
    # Cutaway apron with an original twin-engine courier and a faceted control lantern.
    m.slab("slate",0,0,.014,2.77,.096,2.66,.093)
    m.slab("wall",-.282,.091,.48,1.85,.416,1.10,.16)
    m.slab("glass",-.282,.503,.48,1.91,.204,1.13,.16)
    m.slab("ivory",-.282,.703,.48,2.02,.122,1.22,.16,.06)
    portal(m,-.423,.094,-.073,.455,.368)
    x,z=.645,.64
    m.slab("wall",x,.09,z,.594,.84,.602,.18,.22)
    m.drum("trim",x,.926,z,.294,.162,.38)
    m.drum("glass",x,1.084,z,.382,.251,.341)
    m.drum("roof",x,1.33,z,.387,.089,.326)
    m.drum("metal",x,1.415,z,.032,.202,.023)
    m.drum("forge",x,1.613,z,.041,.046,.028)
    for s in (-1,1):
        m.slab("slate",-.5+s*.476,.82,.457,.60,.126,.775,.12,.15)
    # An apron aircraft in plan, with swept wings and a twin tail at the back.
    with m.transform(translate=(.15,.093,-.66),rotate_y=16,scale=1.17):
        m.block("metal",0,0,.034,.129,.13,.54)
        m.slab("ivory",0,.123,0,.219,.172,.989,.25,.24)
        m.slab("glass",0,.281,-.214,.145,.081,.264,.23,.36)
        for s in (-1,1):
            with m.transform(scale=(s,1,1)):
                upper=[(.046,.214,-.237),(.06,.214,.132),(.60,.228,.312),(.726,.232,.245)]
                lower=[(a,b-.023,c) for a,b,c in upper]
                m.face("gold",upper)
                m.face("ivory",lower[::-1])
                for i in range(4):
                    j=(i+1)%4
                    m.face("ivory",(lower[i],lower[j],upper[j],upper[i]))
                m.slab("roof",.354,.131,.093,.12,.136,.291,.17,.23)
                m.beam("ivory",(.046,.227,.335),(.224,.386,.456),.074)
    if m.lod<2:
        for j in range(4 if m.lod==0 else 2):
            m.block("ivory",-.94+j*(.37 if not m.lod else .85),.094,-1.076,.196,.007,.043)
        for x in (-.88,.222):
            m.lamp(x,.094,-.194,.091)
        if m.lod==0:
            for i in range(8):
                a=i*math.tau/8
                m.beam("trim",(.645+.364*math.cos(a),1.094,.64+.364*math.sin(a)),
                       (.645+.333*math.cos(a),1.326,.64+.333*math.sin(a)),.024)
            # A collapsible boarding bridge leads from the terminal to the apron.
            m.block("metal",-.961,.094,-.396,.084,.225,.094)
            m.slab("ivory",-.956,.310,-.255,.220,.130,.495,.10)
            for z in (-.335,-.237,-.139):
                m.block("glass",-.849,.352,z,.024,.054,.063)
            for x in (-.91,-.61,-.31,-.01,.29):
                m.block("trim",x,.506,-.092,.024,.198,.030)


BUILDERS={"Airport":airport,"Bank":vault_bank,"Guildhall":guildhall,"Harbor":harbor,
          "Market":market,"RailStation":rail}


def build(spec,lod):
    m=Stonework(spec,lod)
    BUILDERS[spec.building_type](m)
    return m


if __name__=="__main__":
    publish_package("dwarf_commerce",build)
