"""Orc military architecture. The ram-horn gate is the clan's common threshold."""
from pathlib import Path
import math
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import AGES, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.orc_towncenters import Lodge
from generators.buildings import orc_civic_trades
from generators.buildings.orc_learninghealth import build_lodge

PACKAGE = "orc_military"
WALL = ("hide", "fieldstone", "brick", "glass")
COVER = ("hide", "roof", "slate", "ivory")
HARD = ("timber", "trim", "metal", "metal")


def slab(m, mat, x, y, z, w, h, d, taper=0):
    m.block(mat, x, y, z, w, h, d, taper)


def rail(m, mat, a, b, rad, end=None, faces=5):
    m.post(mat, a, b, rad, end=end, sides=faces)


def plaque(m, x, y, z, s):
    """A painted chevron split by a downhill crease, shaped like two tusks."""
    for side in (-1, 1):
        a = [(x, y-.27*s, z-.035*s), (x+side*.39*s, y+.25*s, z),
             (x+side*.30*s, y-.11*s, z), (x, y-.48*s, z-.035*s)]
        m.panel("ivory", a if side == 1 else a[::-1])


def shield(m, x, y, z, size):
    # Boss and folded faces stand forward of the timber backing.
    rim = [(0,.54),(.40,.30),(.36,-.16),(0,-.56),(-.36,-.16),(-.40,.30)]
    for i, v in enumerate(rim):
        w = rim[(i+1)%len(rim)]
        m.panel("banner" if i in (0, 1, 4, 5) else "trim",
                [(x+v[0]*size,y+v[1]*size,z),(x+w[0]*size,y+w[1]*size,z),
                 (x,y,z-.14*size)])
    if m.lod == 0:
        rail(m, "gold", (x,y,z-.10*size),(x,y,z-.171*size),.09*size,faces=6)


def crest(m, x, y, z, side, height):
    """A carved sweeping horn with a short ochre mounting socket."""
    n=(5,4,3)[m.lod]
    rings=[]
    for dx,dy,r in ((0,0,.22),(.16,.28,.18),(.24,.65,.12),(.13,1,0)):
        rings.append([(x+height*(side*dx+r*math.cos(j*math.tau/n)),y+height*dy,
                       z+height*(r*math.sin(j*math.tau/n)-dy*.14)) for j in range(n)])
    for k,(lo,hi) in enumerate(zip(rings,rings[1:])):
        for j in range(n):
            nxt=(j+1)%n
            m.panel("accent" if k==0 else "ivory",[lo[nxt],lo[j],hi[j]] if k==2 else
                    [lo[nxt],lo[j],hi[j],hi[nxt]])


def gate(m, x, z, span, height, base=0, horns=True, lintel_z=-.020):
    """Faceted portal projects in front of the wall or fence it serves."""
    f = HARD[m.era]
    slab(m, "door", x, base+.012, z, span*.80, height*.86,.14)
    if m.lod < 2:
        for side in (-1, 1):
            slab(m, f, x+side*.43*span, base, z-.042, span*.16, height*.93,.18,.07)
        slab(m, "accent", x, base+height*.84, z-.034,span*1.10,height*.12,.20,.11)
        if m.lod == 0:
            slab(m,"metal",x,base+.29*height,z-.076,span*.76,.024,.020)
            slab(m,"metal",x,base+.65*height,z-.076,span*.76,.024,.020)
            slab(m,"trim",x,base+.012,z-.080,.022,height*.81,.021)
    else:
        slab(m,"banner",x,base+height*.78,z+lintel_z,span,height*.16,.18)
    if horns:
        for side in (-1, 1):
            m.tusk(x+side*span*.38,base+height*.68,z-.04, side,span*.61)


def drum(m, x, z, size):
    # The sloping red belly and pale hide membrane read even with the laces removed.
    n=(8,6,5)[m.lod]
    m.shell(x,z,[(0,size*.69,"timber"),(.17*size,size*.84,"banner"),
                 (.98*size,size,"trim"),(1.11*size,.96*size,"hide")],n)
    if m.lod==0:
        for i in range(5):
            a=i*math.tau/5
            rail(m,"ivory",(x+.82*size*math.sin(a),.12*size,z+.82*size*math.cos(a)),
                 (x+.984*size*math.sin(a+.36),.97*size,z+.984*size*math.cos(a+.36)),.032*size,faces=4)
        rail(m,"timber",(x-.62*size,1.13*size,z-.28*size),
             (x+.50*size,1.17*size,z+.47*size),.058*size,faces=5)


def sawroof(m, x,z, y,w,d,rise):
    """Hide pulled to three ridge posts; later eras reinterpret it as crested plate bays."""
    era=m.era
    parts = ((-.50,.025),(-.43,.10),(-.29,-.02),(0,.075),(.28,-.085),(.43,.02),(.50,.09))
    if m.lod == 1:
        parts=(parts[0],parts[2],parts[3],parts[4],parts[6])
    if m.lod == 2:
        parts=(parts[0],parts[3],parts[6])
    profile = []
    for fraction,delta in parts:
        a=x+w*fraction
        crest=y+rise*(1+delta)
        # Ridges shed water outward; hard roofs get taller shoulders and flatter brows.
        shoulder=(.24,.24,.35,.40)[era]
        profile.append([(a,y,z-d*.5),(a,y+rise*shoulder,z-d*.30),
                        (a,crest,z+.018*d),(a,y+rise*shoulder,z+d*.30),(a,y,z+d*.5)])
    for i,(a,b) in enumerate(zip(profile,profile[1:])):
        for j in range(4):
            mat=COVER[era]
            if (i==0 and len(parts)>4) or (i==2 and j in (1,2) and len(parts)>4):
                mat="banner"
            m.panel(mat,[b[j],a[j],a[j+1],b[j+1]])
    m.panel(WALL[era],profile[0][::-1])
    m.panel(WALL[era],profile[-1])
    if m.lod < 2:
        for j in (-1,1):
            rail(m,HARD[era],(x-w*.51,y,z+j*d*.49),(x+w*.51,y,z+j*d*.49),d*.031,faces=5)
    if m.lod==0:
        for i in (1,3,5):
            a=profile[i]
            for j in range(4):
                rail(m,"trim",a[j],a[j+1],d*.015,faces=4)


def longhouse(m,x,z,w,d,wallh,rise,entry=True):
    e=m.era
    slab(m,"fieldstone",x,0,z,w*.96,.11,d*.95,.04)
    slab(m,WALL[e],x,.084,z,w*.91,wallh-.078,d*.85)
    sawroof(m,x,z,wallh,w,d,rise)
    for side in (-1,1):
        crest(m,x+side*w*.409,wallh+rise*.86,z+.009,side,.27 if e!=3 else .31)
    if m.lod<2:
        for side in (-1,1):
            with m.transform(translate=(x+side*(w*.5+.008),wallh+rise*.30,z),rotate_y=-90*side):
                shield(m,0,0,0,min(.32,rise*.72))
                plaque(m,0,0,-min(.32,rise*.72)*.15,min(.32,rise*.72)*.63)
    if entry:
        gate(m,x,z-d*.425-.032,min(.43,w*.39),wallh*.90,horns=True)
    if m.lod<2:
        for k in (-1,1):
            for side in (-1,1):
                px=x+w*.351*k
                pz=z+side*d*.427
                slab(m,HARD[e],px,.068,pz,w*.045,wallh-.06,d*.069)
            if entry and w>1:
                slab(m,"door" if e<2 else "glass",x+k*w*.318,wallh*.41,z-d*.434,w*.15,wallh*.25,.032)
        if m.lod==0:
            for k in (-1,1):
                shield(m,x+k*w*.324,wallh*.55,z-d*.449-.021,min(.21,wallh*.36))


def barracks(m):
    e=m.era
    # The two sleeping ranges share a porch at the rear of the muster yard.
    longhouse(m,-.08,.14,1.51,1.16,.48+e*.047,(.53,.65,.34,.29)[e])
    if e == 1:
        with m.transform(translate=(.61,0,.45),scale=(.76,.86,.72)):
            tower(m,small=True)
    elif e>=2:
        r=.288
        top=.85 if e==2 else 1.08
        m.shell(.52,.38,[(0,r,"fieldstone"),(.12,r,"brick" if e==2 else "glass"),
                          (top,r*.85,"metal"),(top+.07,r*.98,"banner"),
                          (top+.18,r*.71,"slate" if e==2 else "ivory")],(9,7,5)[m.lod])
        if e==3:
            m.shell(.52,.38,[(top+.17,.13,"glass"),(top+.36,.07,"ascension")],(8,6,4)[m.lod])
        if m.lod<2:
            for i in range(3):
                slab(m,"trim",.50,.31+i*.14,.102,.29,.04,.048)
    if m.lod<2:
        drum(m,-.56,-.62,.153)
        if m.lod==0:
            for k in (-1,1):
                rail(m,HARD[e],(.34+k*.16,.02,-.64),(.34+k*.16,.34,-.61),.028,faces=5)
            rail(m,HARD[e],(.15,.31,-.61),(.53,.31,-.61),.029,faces=5)
            for k in (-1,1):
                shield(m,.34+k*.102,.238,-.65,.222)
    if e>=1:
        m.flag(-.46,.7,.18,.40 if e==2 else .52,.24)


def steel_lookout(m):
    # A brick guardroom sits at the foot of an open riveted observation gantry.
    if m.lod<2:
        m.block("fieldstone",-.08,0,.06,.67,.103,.67,.06)
        m.block("brick",-.12,.10,.10,.48,.35,.49)
    else:
        m.block("brick",-.12,0,.10,.49,.45,.50)
    m.wedge((-.12,.45,.10),.53,.54,.13,m.col("metal"),material="metal")
    gate(m,-.13,-.168,.23,.32)
    for x in (-1,1):
        for z in (-1,1):
            m.block("fieldstone",x*.321,0,z*.312,.166,.095,.154,.05)
            rail(m,"metal",(x*.32,.084,z*.31),(x*.227,1.07,z*.21),.043,.036,
                 faces=(7,5,4)[m.lod])
        rail(m,"trim",(x*.30,.32,-.30),(x*.237,.95,.22),.029,faces=(6,4,4)[m.lod])
        if m.lod==0:
            rail(m,"trim",(x*.30,.32,.30),(x*.237,.95,-.22),.025,faces=5)
    m.block("trim",0,1.045,0,.77,.093,.72,.04)
    m.block("brick",-.065,1.134,.033,.54,.155,.52)
    m.block("glass",-.065,1.289,.033,.50,.165,.48)
    m.wedge((-.065,1.454,.033),.65,.62,.14,m.col("metal"),material="metal")
    for s in (-1,1):
        m.tusk(s*.30,1.095,-.22,s,.39)
    # Mounted search lamp projects ahead of the cabin brow.
    with m.transform(translate=(.16,1.59,-.06),rotate_y=22,rotate_x=-70):
        m.shell(0,0,[(-.05,.078,"metal"),(.036,.11,"trim"),(.20,.145,"metal"),
                      (.235,.148,"gold"),(.236,.106,"emissive")],(12,7,4)[m.lod])
    if m.lod==0:
        m.flag(-.27,1.49,.23,.27,.123)
        for x in (-.33,.33):
            for z in (-.29,.26):
                rail(m,"metal",(x,1.125,z),(x,1.295,z),.019,faces=5)
            rail(m,"trim",(x,1.29,-.30),(x,1.29,.28),.023,faces=5)
        rail(m,"metal",(-.33,1.29,-.3),(.33,1.29,-.3),.023,faces=5)



def tower(m,small=False):
    if not small and m.era==2:
        return steel_lookout(m)
    e=m.era
    n=(10,7,5)[m.lod]
    # Four converging legs leave open sky under the timber lookout. Masonry and brick
    # iterations replace them with a shouldered shaft carrying the same rally deck.
    deck=(.94,1.12,1.32,1.42)[e]
    if e==0:
        for x in (-1,1):
            for z in (-1,1):
                slab(m,"fieldstone",x*.227,0,z*.216,.156,.056,.156,.12)
                rail(m,"timber",(x*.227,.035,z*.216),(x*.174,deck+.03,z*.168),.071,.058,faces=(6,5,4)[m.lod])
        if m.lod<2:
            for side in (-1,1):
                rail(m,"trim",(-.21,.09,side*.210),(.18,.82,side*.175),.028,faces=4)
                if m.lod==0:
                    rail(m,"trim",(.21,.09,side*.21),(-.18,.82,side*.175),.028,faces=4)
        if not small and m.lod==2:
            with m.transform(translate=(0,.02,-.38),rotate_x=13):
                slab(m,"timber",0,0,0,.13,deck,.032)
        if not small and m.lod<2:
            for x in (-.063,.063):
                rail(m,"timber",(x,.014,-.39),(x,deck,-.18),.018,faces=4)
            for i in range(2,9,1 if m.lod==0 else 2):
                rail(m,"timber",(-.067,deck*i/10,-.39+.21*i/10),(.067,deck*i/10,-.39+.21*i/10),.016,faces=4)
    else:
        m.shell(0,0,[(0,.335,"fieldstone"),(.12,.320,WALL[e]),(deck*.77,.231,HARD[e]),
                     (deck,.295,"trim")],n)
        gate(m,0,-.254,.235,.39,horns=False)
        if m.lod<2:
            for i in (-1,1):
                slab(m,"door" if e==1 else "glass",i*.132,deck*.55,-.199,.041,.184,.065)
    if m.lod==2:
        m.shell(0,0,[(deck,.32,"trim"),(deck+.22,.39,"timber")],5)
    else:
        m.shell(0,0,[(deck,.320,"trim"),(deck+.085,.402,"banner"),(deck+.17,.392,HARD[e]),
                 (deck+.22,.395,"timber" if e<2 else "metal")],n)
    for i in range(4):
        a=math.pi/4+i*math.pi/2
        x,z=.259*math.cos(a),.259*math.sin(a)
        if m.lod==2:
            slab(m,HARD[e],x,deck+.21,z,.06,.34,.06)
        else:
            rail(m,HARD[e],(x,deck+.21,z),(x,deck+.55,z),.032,faces=(6,5)[m.lod])
    if m.lod<2:
        m.shell(0,0,[(deck+.215,.389,HARD[e]),(deck+.28,.376,"trim")],n)
    hroof=deck+.55
    if m.lod==2:
        m.shell(0,0,[(hroof,.445,COVER[e]),(hroof+(.38 if e<2 else .26),.044,"trim")],5)
    else:
        m.shell(0,0,[(hroof,.434,"trim"),(hroof+.037,.449,COVER[e]),
                 (hroof+.195,.240,COVER[e]),(hroof+(.38 if e<2 else .26),.058,"accent")],n,panels=True)
    for side in (-1,1):
        m.tusk(side*.253,hroof+.05,-.05,side,.45)
    if not small and m.lod<2:
        if e==2:
            m.shell(0,0,[(hroof+.238,.076,"metal"),(hroof+.435,.062,"forge")],n)
        else:
            m.flag(0,hroof+.31,0,.26,.152)
        if m.lod==0:
            shield(m,0,deck+.12,-.39,.20)


def palisade(m):
    e=m.era
    if e==1:
        # A gate tower commands the angle between two returned fences.
        # The hoarding overhangs the doors on brackets braced into the jambs.
        x=-.435
        for s in (-1,1):
            for z in (-.128,.219):
                m.shell(x+s*.259,z,[(0,.118,'fieldstone'),(.206,.125,'timber'),
                                     (.933,.094,'trim')],(9,6,4)[m.lod])
        gate(m,x,-.103,.515,.802,horns=False,lintel_z=-.036)
        slab(m,'timber',x,.884,.036,.792,.232,.672,.08)
        for s in (-1,1):
            for z in (-.159,.218):
                rail(m,'timber',(x+s*.290,.625,z*.75),(x+s*.357,.907,z),.050)
            m.tusk(x+s*.315,1.116,-.177,s,.52)
        sawroof(m,x,.036,1.113,.864,.745,.407)
        for s,start,end in ((-1,-.81,-1.369),(1,-.06,1.328)):
            count=(8 if s==1 else 4) if m.lod==0 else (5 if s==1 else 3)
            if m.lod==2:
                count=4 if s==1 else 2
            dx=(end-start)/count
            for i in range(count):
                u=(i+.5)/count
                px=start+u*(end-start)
                z=.079+.199*u
                h=.718-.137*u+.070*(i%2)
                r=abs(dx)*.58
                m.shell(px,z,[(0,r,'timber'),(h-.125,r*.87,'ivory'),(h,0,'trim')],
                        (8,5,4)[m.lod],flatten=.8)
            for y in (.287,.472):
                rail(m,'trim',(start,y,-.006),(end,y-.073,.192),.042,faces=(7,5,4)[m.lod])
        if m.lod<2:
            shield(m,x,1.012,-.303,.214)
            m.flag(x+.152,1.329,.052,.442,.232)
        if m.lod==0:
            for px in (.235,.789,1.168,-1.125):
                rail(m,'timber',(px,.027,.418),(px,.458,.173),.047)
        return
    # Two courses of hand-hewn upright stakes stop at the broad clan gate.
    for side in (-1,1):
        x=side*.858
        if m.lod<2:
            count=8 if m.lod==0 else 6
            pitch=1.095/count
            for j in range(count):
                px=side*(.304+(j+.5)*pitch)
                h=(.61,.74)[e]+(.06,.008,-.035,.025)[j%4]
                m.shell(px,.045,[(0,pitch*.56,"timber"),(h-.125,pitch*.52,"metal" if e==0 else "ivory"),
                                  (h,0,"trim")],(6,5)[m.lod],dx=.035*math.sin(j*.9))
            for y in (.22,.48):
                rail(m,"trim",(x-.539,y,-.024),(x+.539,y,-.024),.045,faces=5)
            if m.lod==0:
                for j in (-1,1):
                    slab(m,"cloth",x+j*.316,.27,-.078,.049,.26,.036)
                    rail(m,"timber",(x+j*.38,.02,.31),(x+j*.38,.52,.07),.049,faces=5)
        else:
            slab(m,"metal" if e==0 else "timber",x,0,.044,1.10,.52+.1*e,.14)
            for j in range(4):
                px=x+(j-1.5)*.26
                m.pyramid((px,.52+.1*e,.044),.266,.14,.10+.035*(j%2),m.col("hide"),material="hide")
    h=.69+.12*e
    gate(m,0,-.018,.54,h,lintel_z=-.038)
    for side in (-1,1):
        m.shell(side*.274,.015,[(0,.117,"fieldstone" if e else "timber"),
            (h+.014,.098,"cloth"),(h+.075,.112,"ivory"),(h+.18,0,"ivory")],(8,6,4)[m.lod])
    if e==1:
        sawroof(m,0,.02,h+.058,.72,.46,.21)
        if m.lod==0:
            shield(m,0,h-.102,-.147,.257)
    else:
        if m.lod<2:
            plaque(m,0,h*.64,-.093,.22)
    if m.lod==0:
        m.flag(-.88,.43,.048,.60,.226)
        for side in (-1,1):
            crest(m,side*.32,h+.09,.0,side,.32)
            shield(m,side*.65,.375,-.087,.26)


def stonewall(m):
    e=m.era
    # A narrow sally door is recessed between two battered terminal piers.
    w=.94
    h=.50 if e==1 else .63
    slab(m,"fieldstone",0,0,0,w,.14,.48,.07)
    slab(m,"wall" if e==1 else "brick",0,.12,0,w*.95,h-.10,.39,.085)
    slab(m,"trim",0,h,0,w,.082,.418)
    for x in (-.377,.377):
        slab(m,"fieldstone",x,0,-.003,.205,h+.083,.51,.095)
        if m.lod<2:
            slab(m,"banner",x,h+.083,-.003,.211,.046,.44)
        if e==1:
            m.tusk(x,h+(.121 if m.lod<2 else .082),-.003,1 if x>0 else -1,.30)
        else:
            base=h+(.129 if m.lod<2 else .08)
            slab(m,"metal",x,base,-.003,.198,.17,.36,.18)
            if m.lod<2:
                m.shell(x,-.014,[(base+.164,.078,"trim"),(base+.193,.084,"metal"),
                                   (base+.215,.077,"gold")],(9,6)[m.lod],flatten=1.1)
            crest(m,x,base+(.211 if m.lod<2 else .166),-.014,1 if x>0 else -1,.29)
    if m.lod<2:
        for x in (-.222,.0,.222):
            slab(m,HARD[e],x,h+.08,.005,.13,.118,.37,.15)
    gate(m,0,-.202,.245,h*.80,horns=False)
    if m.lod==0:
        for x in (-.277,.277):
            slab(m,"trim",x,.104,-.196,.048,.328,.055)
            shield(m,x,h*.65,-.233,.149)
        m.flag(.35,h+.073,.14,.385,.133)


def castle(m):
    # Compact oval ward. Its two low gate drums frame a much taller, hammerhead keep.
    for side in (-1,1):
        slab(m,"fieldstone",side*.97,0,.05,.29,.56,1.83,.075)
        slab(m,"trim",side*.97,.549,.05,.301,.077,1.83)
        if m.lod<2:
            for z in (-.55,0,.55):
                slab(m,"wall",side*.97,.625,z,.289,.15,.27,.075)
    slab(m,"fieldstone",0,0,.91,2.03,.65,.25,.04)
    slab(m,"fieldstone",0,0,-.77,1.96,.59,.29)
    gate(m,0,-.926,.58,.63)
    for side in (-1,1):
        x=side*.91
        m.shell(x,-.74,[(0,.322,"fieldstone"),(.13,.32,"wall"),(.651,.28,"trim"),
                         (.737,.342,"banner"),(.783,.351,"slate"),(.97,.19,"roof"),(1.14,.056,"accent")],
                (10,7,5)[m.lod],panels=True)
        if m.lod<2:
            m.tusk(x+side*.102,.849,-.77,side,.42)
    with m.transform(translate=(0,0,.34),scale=(1.74,1.12,1.55)):
        m.shell(0,0,[(0,.434,"fieldstone"),(.16,.42,"wall"),(.95,.34,"trim"),(1.02,.392,"banner"),
                     (1.11,.43,"wall"),(1.36,.392,"slate"),(1.63,.227,"roof"),(1.90,.073,"trim")],
                (12,8,6)[m.lod],panels=True)
        for side in (-1,1):
            m.tusk(side*.22,1.42,-.04,side,.65)
        m.flag(0,1.87,0,.34,.242)
        if m.lod<2:
            for x in (-.15,0,.15):
                slab(m,"door",x,1.16,-.345,.061,.143,.067)
    if m.lod==0:
        drum(m,-.45,-.31,.14)
        shield(m,-.52,.38,-.926,.266)
        shield(m,.52,.38,-.926,.266)


def base(m):
    e=m.era
    # A folded patrol hangar, low entrance checkpoint and elevated radio command post.
    slab(m,"fieldstone",0,0,.01,2.54,.093,2.35,.025)
    with m.transform(translate=(-.18,.085,.33)):
        longhouse(m,0,0,1.69,1.25,.53,.29 if e==2 else .42)
    with m.transform(translate=(.76,.087,.72)):
        n=(8,6,5)[m.lod]
        cab=1.22 if e==2 else 1.48
        m.shell(0,0,[(0,.28,"wall"),(.79,.212,"metal"),(.90,.335,"glass"),
                     (cab,.31,"banner"),(cab+.04,.33,"metal"),(cab+.23,.13,"trim")],n)
        rail(m,"metal",(0,cab+.22,0),(.03,cab+.6,.012),.040,.014,faces=5)
        if m.lod<2:
            for side in (-1,1):
                crest(m,side*.20,cab+.10,-.017,side,.26)
            if m.lod==0:
                for i in range(4):
                    a=(i+.5)*math.pi/2
                    rail(m,"metal",(.302*math.cos(a),.9,.302*math.sin(a)),
                         (.287*math.cos(a),cab+.01,.287*math.sin(a)),.022,faces=4)
    for side in (-1,1):
        x=side*.86
        slab(m,"metal",x,.09,-.77,.80,.28,.29,.23)
        if m.lod<2:
            slab(m,"banner",x,.36,-.77,.71,.038,.22)
    slab(m,"wall",-.65,.09,-.76,.50,.41,.42)
    m.hip_roof((-.65,.5,-.76),.63,.55,.18,m.col(COVER[e]),overhang=0,material=COVER[e])
    gate(m,-.63,-.959,.225,.415,base=.09,horns=False)
    for side in (-1,1):
        x=side*.388
        slab(m,"trim",x,.093,-.83,.135,.425,.207,.15)
        m.tusk(x,.51,-.82,side,.46)
    if m.lod<2:
        m.flag(-.92,.29,-.72,.68,.268)
        slab(m,"glass",-.78,.282,-.977,.141,.14,.033)
        if m.lod==0:
            for i in range(3):
                slab(m,"ivory",.02,.093,-.98+i*.31,.22,.009,.098)
            with m.transform(translate=(0,.093,0)):
                drum(m,.67,-.34,.172)
    if e==3:
        m.shell(-.82,.68,[(.09,.19,"metal"),(.84,.147,"glass"),(1.13,.18,"ivory"),(1.28,.114,"ascension")],
                (10,7,5)[m.lod])
        if m.lod<2:
            rail(m,"gold",(-.82,1.27,.68),(-.79,1.54,.70),.027,.012,faces=5)


def build(spec,lod):
    if spec.building_type=="MilitaryBase" and spec.band=="future":
        return build_lodge(spec,lod,lambda m,t: orc_civic_trades.military_base(m))
    m=Lodge(spec,lod)
    draw={"Barracks":barracks,"Watchtower":tower,"Castle":castle,"MilitaryBase":base,
          "Palisade":palisade,"StoneWall":stonewall}
    draw[spec.building_type](m)
    return m


if __name__=="__main__":
    entries=[]
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        entries.append(entry)
        print(spec.id,entry["lod_triangles"],flush=True)
    root=Path(__file__).resolve().parents[4]
    previews=[(s,age,root/path) for s in entries for age,path in s["age_previews"].items()]
    directory=previews[0][2].parent
    contact_sheet([p for _,_,p in previews],directory/"contact.png",cols=6,cell=128,
                  labels=[s["building_type"]+" "+age for s,age,_ in previews])
    for age in AGES:
        by_age=[(entry,path) for entry,row_age,path in previews if row_age==age]
        if by_age:
            contact_sheet([path for _,path in by_age],directory/("sheet_"+age+".png"),
                          cols=min(len(by_age),5),cell=160,
                          labels=[entry["building_type"]+" "+age for entry,_ in by_age])
