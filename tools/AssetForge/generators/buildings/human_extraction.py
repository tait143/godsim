"""The human resource yards: hillside adits, stamp and saw mills, and lifting machinery."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesancient import House

PACKAGE = 'human_extraction'
ERAS = ('ancient', 'classical', 'industrial', 'future')


def drum(m, mat, x, y, z, radius, height, top=None, n=None, bottom=False):
    m.cylinder((x, y, z), radius, height, m.color(mat), top_radius=top,
               segments=n or (10, 7, 5)[m.lod], cap_bottom=bottom, material=mat)


def badge(m, x, y, z, w, h):
    """A stitched, swallowtailed guild pennon hung from a continuous wooden rail."""
    # This is a hanging front-facing cloth with real folds, left open along its lower edge.
    m.block('timber', (x, y+.018, z+.011), (w*1.2, .04, .046))
    a=(x-w*.48,y,z); b=(x,y-.016,z-.024); c=(x+w*.48,y,z)
    p=(x-w*.46,y-h,z+.008); q=(x,y-h*.72,z-.018); r=(x+w*.45,y-h*.93,z+.004)
    for panel in ((a,b,q,p),(b,c,r,q)):
        m.face('banner', panel)
        m.face('banner', [(u,v,t+.005) for u,v,t in reversed(panel)])
    if not m.lod:
        m.face('ivory', [(x,y-.051,z-.028),(x-.027,y-h*.45,z-.026),
                         (x,y-h*.61,z-.024),(x+.026,y-h*.44,z-.026)][::-1])


def cabin(m, x, z, w, d, h, era):
    """Age-shaped roof and working doors on a stone sill; floor is the yard surface."""
    front=z-d*.5
    m.course('fieldstone',x,z,w+.042,d+.042,.053,.132,.045)
    m.block('wall',(x,(h+.12)/2,z),(w,h-.12,d))
    with m.transform(translate=(x,0,z)):
        if era==3:
            m.block('metal',(0,h+.023,0),(w+.07,.052,d+.07))
            m.wedge((-.014,h+.048,-.014),w*.88,d*.88,h*.20,m.color('glass'),material='glass')
        else:
            m.roof(w+.13,d+.14,h,(.46,.56,.25)[era]*w,'roof','wall',
                   hip=(.10,0,.030)[era],ridge_mat='trim' if m.lod<2 else None)
    m.entry(x-w*.13,front,w*.30,.13,h*.52,lintel='metal' if era>=2 else 'trim')
    if m.lod<2:
        m.light(x+w*.285,h*.59,front,w*.16,h*.22,shutters=era<2)
        # On the visible return place a framed inspection window above a planted wooden sill.
        with m.transform(translate=(x+w*.5,0,z),rotate_y=-90):
            m.light(-.035,h*.62,0,d*.31,h*.22,shutters=era<2)
            if not m.lod and era<=1:
                m.herbs(-.035,h*.62-h*.11-.046,0,d*.39)
        if era<2:
            for s in (-1,1):
                m.block('trim',(x+s*(w*.5-.019),(h+.132)*.5,front-.012),(.045,h-.132,.052))
        else:
            m.block('banner',(x+w*.5+.012,h*.83,z),(.026,.052,d*.84))
        if not m.lod:
            m.rear_vent(x,z+d*.5+.005,h*.56,w*.27,h*.22)
            if era==1:
                m.flue(x-w*.26,z+d*.24,h-.022,h+w*.53,w*.12)
            if era==2:
                drum(m,'metal',x-w*.30,h-.015,z+d*.23,.045,.32)
                drum(m,'trim',x-w*.30,h+.286,z+d*.23,.060,.038)


def axle(m, mat, x, y, front, r, length, sides=None):
    """Capped spindle along +Z. Its end faces the main approach."""
    with m.transform(translate=(x,y,front),rotate_x=90):
        drum(m,mat,0,0,0,r,length,n=sides,bottom=True)


def wheel(m, x, y, z, radius, mat, thickness=.065):
    """Open rim, radial spokes, and a projecting hub. Front lies on -Z."""
    if m.lod==2:
        axle(m,mat,x,y,z,radius,thickness,6)
        return
    count=(12,8)[m.lod]
    inner=radius*.74
    def vertex(i,r,d):
        a=2*math.pi*i/count
        return (x+r*math.cos(a),y+r*math.sin(a),z+d)
    for i in range(count):
        j=(i+1)%count
        for radius1,depth1,radius2,depth2 in (
            (radius,0,radius,thickness), (inner,thickness,inner,0),
            (inner,0,radius,0), (radius,thickness,inner,thickness)):
            m.face(mat,(vertex(i,radius1,depth1),vertex(j,radius1,depth1),
                        vertex(j,radius2,depth2),vertex(i,radius2,depth2)))
    for i in range(3 if m.lod!=2 else 2):
        a=.31+math.pi*i/(3 if m.lod!=2 else 2)
        vx,vy=math.cos(a)*inner,math.sin(a)*inner
        m.beam(mat,(x-vx,y-vy,z+thickness*.41),(x+vx,y+vy,z+thickness*.41),radius*.13)
    axle(m,'metal',x,y,z-.019,radius*.19,thickness+.06)


def rock(m, x, z, w, d, h, mat='fieldstone', floor=.064, skew=0):
    """Seven-sided cleft block with an asymmetric shoulder and a broken crown."""
    outline=[(-.48,-.27),(-.27,-.49),(.29,-.46),(.5,-.10),(.42,.38),(-.08,.49),(-.44,.29)]
    if m.lod==2:
        outline=[outline[i] for i in (0,2,3,5)]
    base=[(x+u*w,floor,z+v*d) for u,v in outline]
    shoulder=[(x+u*w*.88,floor+h*(.64+.09*math.sin(k*1.8)),z+v*d*.82) for k,(u,v) in enumerate(outline)]
    peak=(x+w*(.09+skew),floor+h,z+d*.07)
    for i in range(len(base)):
        j=(i+1)%len(base)
        m.face(mat,(base[j],base[i],shoulder[i],shoulder[j]))
        m.face(mat,(shoulder[j],shoulder[i],peak))


def logs(m,x,z,w=.80,scale=1):
    with m.transform(translate=(x,.066,z),scale=scale):
        for dx,y,offset in ((-.18,.11,.022),(.005,.105,-.035),(.177,.090,.040),(-.089,.265,.02),(.078,.241,-.018)):
            if m.lod==2 and y>.24:
                continue
            r=.106 if dx<.08 else .086
            nose=offset-w/2
            axle(m,'timber',dx,y,nose,r,w,(8,6,4)[m.lod])
            axle(m,'hide',dx,y,nose-.005,r*.82,.004,(8,6,4)[m.lod])
            if m.lod==0:
                axle(m,'timber',dx+.008,y+.005,nose-.009,r*.35,.003,6)
        if m.lod<2:
            for dx in (-.265,.260):
                m.beam('trim',(dx,.007,.11),(dx*1.06,.246,.10),.036)


def camp(m,era):
    if era==1:
        # Split logs cross a lined sawpit; the second sawyer works below grade.
        # The shingle-roofed tool cupboard forms the far end of the yard.
        cabin(m,-.43,.495,.74,.48,.545,1)
        for x in (-.49,.21):
            m.course('fieldstone',x,-.276,.18,.97,.066,.272,.048)
        m.block('door',(-.143,.084,-.276),(.52,.036,.90))
        for z in (-.58,.012):
            m.beam('timber',(-.63,.282,z),(.37,.282,z),.108)
        axle(m,'timber',-.174,.442,-.73,.122,.985)
        axle(m,'hide',-.174,.442,-.737,.108,.006)
        # A frame saw spans the log at a slight rake.
        with m.transform(translate=(-.16,.494,-.30),rotate_z=-15):
            for x in (-.153,.153):
                m.beam('timber',(x,-.023,0),(x,.54,0),.036)
            for y in (.043,.479):
                m.beam('timber',(-.18,y,0),(.18,y,0),.040)
            m.beam('metal',(0,-.14,.009),(0,.48,.009),.021,.053)
        if m.lod<2:
            logs(m,.59,.19,.97,.62)
        if m.lod==0:
            m.basket(-.69,-.374,.094,.174)
        return
    if era==2:
        # A steam donkey on timber skids winds the haul cable off a front drum.
        x,z=-.265,.08
        for dx in (-.29,.29):
            m.beam('timber',(x+dx,.138,-.692),(x+dx,.138,.72),.147)
        m.block('metal',(x,.238,z),(.77,.13,1.16))
        axle(m,'metal',x,.498,-.203,.27,.833)
        axle(m,'gold',x,.498,-.212,.209,.014)
        drum(m,'metal',x,.608,.397,.122,.774,.079)
        drum(m,'trim',x,1.38,.397,.111,.092)
        for dx in (-.278,.278):
            m.block('metal',(x+dx,.405,-.471),(.105,.24,.22),taper=.12)
        with m.transform(translate=(x-.354,.459,-.484),rotate_z=-90):
            drum(m,'trim',0,0,0,.139,.71,n=(14,8,5)[m.lod],bottom=True)
            if m.lod<2:
                for k in range(5 if m.lod==0 else 2):
                    drum(m,'hide',0,.184+.051*k,0,.149,.041)
        logs(m,.51,.028,1.12,.74)
        if m.lod==0:
            m.beam('hide',(x,.552,-.60),(.43,.127,-.76),.027)
            badge(m,x,.5,-.228,.15,.18)
        return
    if era==3:
        # Highline yarder: the overhead trolley runs between an enclosed
        # control tower and the outboard A-frame, above the landing.
        cabin(m,-.52,.42,.48,.58,.92,3)
        for z in (-.61,.49):
            m.beam('metal',(.705,.066,z),(.56,1.08,-.10),.098 if z<0 else .087)
        m.beam('ivory',(-.52,1.03,.42),(.56,1.08,-.10),.125)
        m.block('accent',(.194,1.077,.074),(.207,.108,.18),taper=.15)
        m.beam('trim',(.194,1.03,.074),(.194,.555,.074),.029)
        for s in (-1,1):
            m.beam('metal',(.194,.556,.074+s*.016),(.194+s*.176,.418,.074+s*.016),.055)
            m.beam('metal',(.194+s*.176,.418,.074+s*.016),(.194+s*.126,.292,.074+s*.016),.044)
        with m.transform(translate=(.144,0,-.13),rotate_y=13):
            logs(m,0,0,1.16,.83)
        if m.lod==0:
            m.beam('gold',(-.49,1.016,.41),(-.344,1.30,.32),.037)
            m.beam('trim',(-.344,1.30,.32),(.52,1.108,-.083),.023)
            for z in (-.53,.40):
                drum(m,'metal',.70,.065,z,.111,.085,.084)
        return
    cabin(m,-.41,.23,.76,.88,(.58,.71,.64,.70)[era],era)
    logs(m,.405,.04,.96)
    # The raised chopping block and an axe split its top in the direction of the grain.
    drum(m,'timber',-.29,.064,-.55,.207,.215,.19)
    drum(m,'hide',-.29,.279,-.55,.183,.017)
    m.beam('timber',(-.30,.303,-.54),(-.58,.68,-.54),.045)
    with m.transform(translate=(-.305,.369,-.54),rotate_z=31):
        # Flared cutting edge shows above the kerf; the eye bears on the inclined haft.
        m.block('metal',(.024,.018,0),(.237,.087,.072),taper=.15)
        m.block('ivory',(.133,.017,0),(.044,.122,.067),taper=.08)
    if era==0:
        # Tied split-pole rack keeps the hand saw and stored shafts upright.
        for x in (.24,.58):
            m.beam('timber',(x,.066,.64),(x+.019,.62,.60),.054)
        m.beam('timber',(.159,.583,.605),(.669,.603,.602),.063)
        if m.lod<2:
            badge(m,.364,.576,.557,.155,.174)
    if era==1 and m.lod<2:
        badge(m,-.43,.72,-.281,.19,.152)
    if era>=2:
        # The truck-mounted grapple and its operator's hood replace the hand skids.
        m.block('metal',(.37,.193,.65),(.45,.251,.36),taper=.10)
        m.beam('trim',(.33,.31,.64),(.49,.81,.54),.080)
        m.beam('metal',(.49,.81,.54),(.39,.64,.01),.072)
        m.beam('metal',(.39,.64,.01),(.39,.39,-.003),.037)
        if m.lod<2:
            for s in (-1,1):
                m.beam('metal',(.39,.399,-.003),(.39+s*.145,.27,-.006),.040)
            if era==3:
                m.block('glass',(.31,.386,.66),(.32,.18,.24),taper=.22)
    if m.lod==0:
        m.basket(-.67,-.39,.087,.13)


def tracks(m,x,front,back,span):
    for s in (-1,1):
        m.block('metal',(x+s*span/2,.115,(front+back)/2),(.04,.049,back-front))
    if m.lod<2:
        for i in range(6 if not m.lod else 3):
            z=front+(back-front)*(i+.5)/(6 if not m.lod else 3)
            m.block('timber',(x,.077,z),(span+.16,.038,.072))


def wagon(m,x,z,ore):
    m.block('metal',(x,.228,z),(.39,.089,.37))
    m.block('timber',(x,.344,z),(.408,.188,.378))
    m.block('trim',(x,.439,z),(.446,.036,.410))
    rock(m,x,z,.386,.33,.141,ore,.455)
    if m.lod<2:
        for s in (-1,1):
            for t in (-1,1):
                with m.transform(translate=(x+s*.184,.175,z+t*.116),rotate_z=-90):
                    drum(m,'metal',0,0,0,.067,.043,n=8 if not m.lod else 6,bottom=True)


def mine(m,era,gold):
    if era==3:
        if gold:
            # A rotating ore-washing drum feeds a cascade of settling basins.
            rock(m,-.42,.38,.79,.74,.60,'fieldstone')
            for z in (.04,.46):
                m.block('metal',(.23,.27,z),(.61,.41,.14),taper=.08)
            with m.transform(translate=(.23,.55,.25),rotate_x=78):
                drum(m,'ivory',0,-.42,0,.33,.86,top=.26,n=(18,10,6)[m.lod])
                drum(m,'trim',0,-.47,0,.35,.08,n=(16,10,6)[m.lod])
            for j in range((3,3,2)[m.lod]):
                x,z=-.58+j*.36,-.44-j*.08
                drum(m,'metal',x,.066,z,.245,.16+.055*j)
                drum(m,'water',x,.226+.055*j,z,.215,.015)
                if m.lod==0:
                    rock(m,x,z,.14,.12,.052,'gold',.243+.055*j)
            m.entry(-.42,-.002,.25,.066,.31)
        else:
            # The enclosed shaft head raises skips directly above the loading track.
            m.course('ivory',.23,.25,.82,.75,.066,1.04,.11)
            m.course('glass',.23,.22,.91,.82,1.04,1.34,.12)
            m.course('metal',.23,.20,.96,.87,1.34,1.44,.14)
            m.entry(.22,-.128,.33,.068,.58,lintel='metal')
            tracks(m,.23,-.82,-.12,.32)
            wagon(m,.23,-.58,'fieldstone')
            rock(m,-.52,.26,.62,.85,.62)
            if m.lod<2:
                drum(m,'metal',-.45,.066,-.43,.17,.22)
                m.light(.22,1.17,-.198,.49,.14,shutters=False)
        if m.lod==0:
            badge(m,-.46,.54,.002,.16,.15)
        return
    # Outcrop forms the tunnel face. An ore wagon rides straight out from the framed adit.
    cx=-.015
    rock(m,-.28,.35,1.25,1.10,.97)
    rock(m,.32,.49,.82,.76,.78)
    m.block('fieldstone',(cx,.391,-.053),(.80,.65,.64),taper=.07)
    m.face('door',[(cx+.286,.067,-.381),(cx-.286,.067,-.381),
                   (cx-.286,.486,-.381),(cx-.19,.599,-.381),
                   (cx+.21,.599,-.381),(cx+.286,.506,-.381)])
    structural='timber' if era<2 else 'metal'
    for s in (-1,1):
        m.beam(structural,(cx+s*.326,.064,-.404),(cx+s*.282,.598,-.404),.095)
    m.block('trim',(cx,.628,-.409),(.737,.108,.132))
    tracks(m,cx,-.875,-.361,.315)
    if not gold:
        wagon(m,cx,-.612,'metal' if era>=2 else 'fieldstone')
    else:
        # A gold wash table stands beside the adit; its sluice riffles trap heavy ore.
        with m.transform(translate=(.56,.196,-.395),rotate_x=-11):
            m.block('timber',(0,0,0),(.442,.065,.75))
            m.block('water',(0,.038,0),(.34,.016,.71))
            for s in (-1,1):
                m.block('timber',(s*.206,.063,0),(.041,.106,.754))
            for i in range((6,3,1)[m.lod]):
                m.block('gold',(0,.061,-.25+.10*i),(.321,.035,.032))
        for z,y in ((-.64,.155),(-.14,.26)):
            m.block('fieldstone',(.56,(.064+y)*.5,z),(.31,y-.064,.10))
        if era>=2:
            for sx in (.373,.741):
                m.block('metal',(sx,.478,-.077),(.052,.815,.077))
            m.block('metal',(.556,.866,-.080),(.484,.107,.136))
            for sx in ((.456,.650) if m.lod<2 else (.552,)):
                if m.lod==2:
                    m.block('gold',(sx,.624,-.091),(.083,.43,.091))
                else:
                    m.beam('gold',(sx,.837,-.087),(sx,.502,-.096),.052)
                    m.block('metal',(sx,.445,-.095),(.101,.126,.131))
        if m.lod<2:
            drum(m,'timber',-.572,.068,-.659,.150,.091,.223)
            drum(m,'gold',-.572,.159,-.659,.214,.028,.204)
            rock(m,-.562,-.658,.264,.24,.112,'gold',.177)
            if not m.lod:
                rock(m,-.687,-.69,.133,.12,.073,'gold',.177)
    # An ancient windlass on the crest evolves into a tall roofed hoist.
    hx,hz=.018,.272
    peak=(1.095,1.36,1.55,1.74)[era]+(.17 if gold else 0)
    ybase=.44
    for side in (-1,1):
        for depth in (-1,1):
            m.beam(structural,(hx+side*.385,ybase,hz+depth*.29),
                   (hx+side*.237,peak-.075,hz+depth*.169),.081 if era<2 else .097)
    m.block('trim',(hx,peak-.052,hz),(.603,.106,.452))
    axle(m,structural,hx+.015,peak+.047,hz-.245,.124,.50)
    wheel(m,hx+.015,peak+.045,hz-.332,.214 if not gold else .241,'gold' if gold else ('timber' if era<2 else 'metal'))
    if era==3 and m.lod==0:
        badge(m,hx+.135,peak-.02,hz-.242,.14,.177)
    if era==1 or era==3:
        for s in (-1,1):
            m.block(structural,(hx+s*.22,peak+.135,hz+.06),(.045,.32,.054))
        m.hip_roof((hx,peak+.294,hz+.05),.67,.51,.15 if era==1 else .083,
                   m.color('roof'),overhang=0,material='roof')
    cabin(m,-.656,-.045,.464,.637,.486 if era<2 else .562,era)
    if m.lod<2 and era<2:
        badge(m,-.278,.64,-.482,.105,.147)
    if not m.lod:
        # A ladder climbs from the cobbles to the drum service platform.
        for s in (-1,1):
            m.beam(structural,(.429+s*.079,.067,-.08),(.301+s*.079,peak-.03,.13),.039)
        for k in range(8):
            f=(k+.7)/8
            px,pz=.429-.128*f,-.08+.21*f
            m.beam(structural,(px-.079,.067+(peak-.097)*f,pz),
                   (px+.079,.067+(peak-.097)*f,pz),.027)
        for k in range(4):
            m.block('timber',(hx-.22+k*.143,peak+.012,hz+.006),(.137,.029,.46))
        m.block('trim',(hx+.302,peak+.07,hz+.123),(.042,.25,.052))
        m.block('emissive',(hx+.302,peak+.25,hz+.123),(.073,.13,.071))
        m.hip_roof((hx+.302,peak+.313,hz+.123),.091,.088,.044,m.color('metal'),overhang=0,material='metal')
    if m.lod<2:
        for side in (-1,1):
            m.beam(structural,(hx+side*.349,.68,hz-.246),(hx-side*.247,peak-.108,hz-.172),.049)
        m.beam('trim',(hx+.169,peak+.178,hz-.296),(hx+.221,.70,-.306),.022)
        if not gold:
            rock(m,-.509,-.722,.40,.306,.184,'soil')
        if not m.lod:
            if not gold:
                rock(m,-.559,-.704,.20,.164,.10,'metal',.17)
            m.block('trim',(cx+.34,.573,-.512),(.044,.036,.171))
            drum(m,'emissive',cx+.34,.417,-.578,.041,.115,n=6)
            drum(m,'metal',cx+.34,.531,-.578,.046,.036,.018,n=6)


def coal(m,era):
    if era==3:
        # An inclined drift hugs the seam; loaded cars climb to a chain
        # conveyor and a cantilevered discharge, in place of a vertical shaft.
        rock(m,-.20,.36,1.23,.94,.86,'slate',skew=.105)
        m.block('door',(-.21,.395,-.125),(.60,.64,.141))
        for x in (-.544,.123):
            m.beam('ivory',(x,.066,-.23),(x*.86-.034,.725,-.205),.114)
        m.beam('metal',(-.535,.739,-.205),(.143,.739,-.205),.119)
        for x in (-.388,-.041):
            m.beam('metal',(x,.114,-.77),(x,.344,.001),.048)
        wagon(m,-.21,-.46,'slate')
        m.block('metal',(.445,.361,.18),(.249,.59,.364),taper=.14)
        m.beam('ivory',(.11,.281,-.34),(.734,.87,.55),.212,.133)
        if m.lod<2:
            for i in range(6 if m.lod==0 else 3):
                u=(i+.5)/(6 if m.lod==0 else 3)
                m.block('slate',(.11+.624*u,.38+.585*u,-.34+.89*u),(.149,.1,.164),taper=.24)
        if m.lod==0:
            m.flue(-.586,.503,.54,.93,.108)
            for s in (-1,1):
                for j in range(4):
                    z=-.59+.154*j
                    m.block('timber',(-.212+s*.284,.110+j*.039,z),(.111,.073,.087))
            m.light(.443,.425,-.006,.161,.112,False)
            badge(m,-.20,.740,-.273,.165,.104)
        return
    # A cool, subdued multiplier makes the dark stable swatch read as anthracite even
    # on the bright white/gold age row. Its modest tint also unifies the dusty yard.
    m.tint=(.55,.84,1.0)
    cabin(m,-.49,.315,.72,.74,.679 if era==2 else .81,era)
    cx,cz=.38,.22
    # A pyramidal tipple with a real narrowed hopper throat, discharge gate, and coal wagon.
    for x in (-.275,.275):
        for z in (-.273,.273):
            m.block('metal',(cx+x,.658,cz+z),(.09,1.187,.10))
    with m.transform(translate=(cx,1.02,cz),rotate_z=180):
        m.box((0,0,0),(.654,.45,.63),m.color('metal'),material='metal',taper=.67,bottom=True)
    m.block('trim',(cx,1.265,cz),(.70,.093,.67))
    rock(m,cx,cz,.653,.61,.243,'door',1.311)
    m.block('metal',(cx,.77,cz),(.223,.13,.23))
    tracks(m,cx,-.894,.37,.321)
    wagon(m,cx,-.502,'door')
    top=(1.27,1.46)[era-2]
    # The inclined loading elevator terminates above the hopper to discharge by gravity.
    m.beam('trim',(-.65,.26,.56),(cx,top+.17,cz),.181,.24)
    if m.lod<2:
        for z in (-.23,.23):
            m.beam('metal',(cx-.24,.11,cz+z),(cx+.255,.81,cz+z),.055)
        rock(m,-.55,-.415,.63,.53,.298,'door')
        if not m.lod:
            rock(m,-.428,-.534,.26,.22,.131,'door')
            rock(m,-.72,-.574,.24,.18,.095,'door')
        if not m.lod:
            for i in range(5):
                f=(i+.5)/5
                m.block('accent',(-.65+f*(cx+.65),.32+f*(top-.09),.56+f*(cz-.56)),(.10,.039,.254))
            badge(m,cx,1.283,cz-.348,.18,.204)
    if not m.lod:
        for k in range(4):
            x=-.638+k*.236
            m.block('trim',(x,.23,-.799),(.048,.328,.057),taper=.16)
        m.beam('timber',(-.661,.334,-.800),(.109,.334,-.800),.042)
        m.beam('timber',(-.661,.186,-.80),(.110,.186,-.800),.038)
        drum(m,'metal',-.67,.064,.686,.064,.91,.05)
        drum(m,'metal',-.67,.967,.686,.078,.041)
    if era==3:
        for s in (-1,1):
            m.block('wall',(cx+s*.292,1.489,cz+.118),(.065,.44,.09))
        m.hip_roof((cx,1.706,cz+.084),.74,.57,.128,m.color('glass'),overhang=0,material='glass')


def quarry(m,era):
    if era==2:
        # Steam jib quarry. A triangular lattice jib raises a dressed ashlar
        # from a low cut; the boiler house balances the heel behind its bearing.
        for i in range(3):
            m.course('fieldstone',-.29+i*.044,.40+i*.105,1.17-i*.12,.75-i*.16,
                     .066+i*.18,.246+i*.18,.047)
        x,z=.26,-.08
        drum(m,'brick',x,.066,z,.29,.22,.254)
        drum(m,'metal',x,.286,z,.255,.089)
        m.block('metal',(x,.565,z+.176),(.415,.38,.457),taper=.061)
        drum(m,'trim',x,.752,z+.249,.061,.282,.048)
        for dx in (-.118,.118):
            m.beam('metal',(x+dx,.382,z),(x+dx-.369,1.205,-.585),.052)
            m.beam('metal',(x+dx,.705,z+.12),(x+dx-.369,1.205,-.585),.046)
            if m.lod==0:
                for u in (.25,.5,.75):
                    m.beam('gold',(x+dx-.369*u,.382+.823*u,z+(-.585-z)*u),
                           (x+dx-.369*u,.705+.50*u,z+.12+(-.705-z)*u),.022)
        m.beam('metal',(-.23,1.2,-.585),(.02,1.2,-.585),.064)
        m.beam('trim',(-.109,1.18,-.585),(-.109,.452,-.585),.023)
        m.block('wall',(-.109,.296,-.585),(.42,.29,.27))
        if m.lod<2:
            for dx in (-.16,.16):
                m.beam('hide',(-.109,.46,-.585+dx*.06),(-.109+dx,.39,-.585+dx*.06),.025)
        if m.lod==0:
            for pz in (-.19,.10,.37):
                rock(m,-.70,pz,.22,.16,.14,'wall',.067,.026)
            wheel(m,.25,.565,-.141,.16,'metal',.05)
        return
    # Stepped bedding planes, squared lift stock, a crane, and a front-facing tool lodge.
    cabin(m,-.555,.377,.55,.56,.47 if era<2 else .57,era)
    for x,z,w,d,top in ((.265,.403,1.12,.87,.274),(.313,.488,.986,.67,.491),
                         (.373,.551,.80,.47,.742)):
        # The facing of the lower retaining bench changes from palisade timbers
        # through mudbrick into dressed stone. The two upper benches remain exposed.
        face='wall' if top==.274 and era==0 else 'fieldstone'
        m.course(face,x,z,w,d,.064 if top==.274 else (.267 if top==.491 else .484),
                 top,.073,.017)
    if era==0:
        # A broad, slightly pitched weather cover shades the upper cutters' station.
        # The age metal patch progresses from raw brown through copper to grey iron.
        for x in (.015,.711):
            for z in (.419,.698):
                m.block('timber',(x,.957,z),(.055,.442,.063))
        with m.transform(translate=(.361,1.175,.564),rotate_x=-9):
            m.box((0,0,0),(.908,.059,.472),m.color('metal'),material='metal',bottom=True)
        if m.lod<2:
            m.beam('trim',(-.028,1.122,.416),(.757,1.122,.416),.058)
            badge(m,.678,1.117,.379,.13,.148)
    if m.lod<2:
        for x,z,h in ((.11,.31,.19),(.37,.31,.16),(.62,.33,.185)):
            m.block('ivory',(x,.744+h*.5,z+.134),(.202,h+.005,.15),taper=.04)
    cx,cz=-.28,-.22
    t=('timber','timber','metal','wall')[era]
    m.course('fieldstone',cx,cz,.37,.40,.064,.199,.044)
    drum(m,t,cx,.187,cz,.116,(.67,.79,.88,1.0)[era],.086)
    y=(.83,.96,1.06,1.16)[era]
    # Cantilever jib and diagonal stay are triangulated back to a swivel on the kingpost.
    end=(.58,y+.17,cz-.043)
    m.beam(t,(cx-.171,y-.045,cz),end,.107)
    m.beam(t,(cx,y-.382,cz),(end[0]-.105,end[1]-.017,end[2]),.068)
    m.beam('trim',end,(end[0],.497,end[2]),.026)
    block=(end[0],.354,end[2])
    m.block('ivory',block,(.347,.206,.302),taper=.035)
    # The two sides of the sling share a miter at the lifting eye.
    m.bent_rail('trim',[(end[0]-.14,.441),(end[0],.497),(end[0]+.14,.441)],end[2],.033)
    if era==0:
        wheel(m,cx,y-.232,cz-.148,.149,'timber')
    elif era==1:
        wheel(m,cx,y-.264,cz-.183,.243,'timber')
    else:
        m.block('metal',(cx-.148,y-.057,cz),(.38,.195,.32),taper=.16)
        m.block('glass',(cx-.179,y+.092,cz),(.25,.16,.243),taper=.15)
        if era==3:
            m.beam('accent',(cx,y-.03,cz-.071),(.46,y+.194,cz-.101),.032)
            if not m.lod:
                # Roof sight and linked receiver align the diamond cutter with the next bedding plane.
                drum(m,'metal',cx-.18,y+.172,cz,.062,.061)
                drum(m,'turquoise',cx-.18,y+.232,cz,.056,.052,.039)
                for x,bottom in ((.11,.933),(.37,.904),(.62,.928)):
                    drum(m,'metal',x,bottom,.446,.025,.975-bottom)
                m.beam('accent',(.08,.976,.446),(.650,.976,.446),.043)
    if m.lod<2:
        for x,z in ((-.60,-.62),(-.18,-.68)):
            m.block('ivory',(x,.172,z),(.328,.211,.226),taper=.07)
        if m.lod==0:
            # A handcart carries the latest cut beside the bank.
            with m.transform(translate=(.471,.066,-.681),rotate_y=-12):
                for bx in (-.11,.11):
                    m.beam('timber',(bx,.074,-.18),(bx,.123,.234),.046)
                for bz in (-.076,.018,.112):
                    m.block('timber',(0,.109,bz),(.321,.048,.085))
                m.block('ivory',(0,.199,.018),(.25,.138,.233),taper=.04)
                for bx in (-.174,.174):
                    with m.transform(translate=(bx,.092,.006),rotate_z=-90):
                        drum(m,'trim',0,0,0,.089,.033,bottom=True)
            m.beam('timber',(-.68,.281,-.63),(-.43,.447,-.63),.026)
            m.block('metal',(-.444,.434,-.63),(.066,.076,.07))
            badge(m,cx-.08,y-.005,cz-.094,.119,.166)


def saw(m,era):
    if era==3:
        # Portal band mill. The enclosed blade travels between its upper and lower flywheels.
        cabin(m,-.47,.45,.73,.57,.60,era)
        for x in (-.29,.61):
            m.block('wall',(x,.385,-.16),(.186,.65,.36),taper=.05)
        m.block('roof',(.16,.775,-.16),(1.12,.15,.41))
        m.block('glass',(.16,.894,-.16),(.66,.14,.28))
        for x in (-.26,.58):
            axle(m,'metal',x,.67,-.354,.133,.104)
        m.block('metal',(.35,.529,-.31),(.032,.48,.074))
        m.block('timber',(.17,.274,-.22),(.75,.094,1.26))
        for z in (-.70,.23):
            m.block('trim',(.17,.168,z),(.57,.225,.10))
        logs(m,.15,-.51,.42,.69)
        logs(m,-.61,-.31,.36,.71)
        if m.lod<2:
            for j in range((6,3)[m.lod]):
                z=-.72+j*.75/(5 if m.lod==0 else 2)
                m.block('metal',(.14,.324,z),(.64,.035,.031))
            badge(m,-.49,.47,.148,.12,.12)
        return
    cabin(m,-.446,.260,.72,.93,.795 if era==2 else .837,era)
    logs(m,-.372,-.51,.47,.84)
    cx,cz=.38,-.088
    # Slotted saw carriage and exposed steel blade beside its feed bed.
    for z in (-.62,.52):
        for s in (-1,1):
            m.block('trim',(cx+s*.195,.245,z),(.082,.36,.093))
    for s in (-1,1):
        m.block('timber',(cx+s*.14,.434,cz),(.242,.094,1.46))
    bx,by,bz=cx,.563,cz
    with m.transform(translate=(bx,by,bz),rotate_y=-90):
        teeth=(14,10,8)[m.lod]
        radius=.392
        axle(m,'metal',0,0,-.026,radius,.051,teeth)
        axle(m,'ivory',0,0,-.030,radius*.81,.003,teeth)
        for i in range(teeth):
            a=math.tau*i/teeth
            def lip(offset,r,z):
                return (math.cos(a+offset)*r,math.sin(a+offset)*r,z)
            pts=[lip(0,radius*.93,-.027),lip(.107,radius+.066,-.027),
                 lip(math.tau/teeth,radius*.93,-.027)]
            m.face('metal',pts[::-1])
            m.face('metal',[(x,y,.026) for x,y,_ in pts])
            if m.lod<2:
                for i0,i1 in ((0,1),(1,2)):
                    u,v,_=pts[i0]
                    x,y,_=pts[i1]
                    m.face('metal',[(u,v,-.027),(x,y,-.027),(x,y,.026),(u,v,.026)])
        axle(m,'gold',0,0,-.233,.080,.283)
    m.block('metal',(cx,.336,.01),(.521,.155,.262))
    if m.lod<2:
        m.block('metal',(cx+.184,.476,cz),(.186,.236,.152),taper=.15)
        axle(m,'timber',cx+.116,.559,-.78,.074,.51)
        axle(m,'hide',cx+.116,.559,-.784,.062,.003)
        for z in (-.60,-.32,.32,.54):
            m.block('metal',(cx,.49,z),(.445,.026,.035))
    if era==3:
        for x in (cx-.194,cx+.197):
            m.block('metal',(x,.714,cz+.252),(.060,.52,.074))
        m.block('glass',(cx,.982,cz+.251),(.541,.106,.24),taper=.15)
        m.block('turquoise',(cx,.944,cz+.117),(.395,.043,.035))
    elif m.lod<2:
        m.flue(-.652,.536,.70,1.25,.17)
    if not m.lod:
        badge(m,-.633,.765,-.259,.14,.166)


def oil(m,era):
    if era==3:
        # A compact valve tree controls a pressurised bore. Curved pipework
        # crosses the pad to a horizontal separator on two concrete saddles.
        x,z=-.385,-.012
        for y,r,h,mat in ((.066,.239,.13,'wall'),(.196,.134,.58,'metal'),
                          (.754,.161,.086,'ivory'),(.84,.089,.23,'metal'),(1.069,.134,.092,'accent')):
            drum(m,mat,x,y,z,r,h)
        for i,y in enumerate((.422,.67,.976)):
            side=(-1,1,-1)[i]
            m.beam('metal',(x,y,z),(x+side*.309,y,z),.115)
            axle(m,'metal',x+side*.262,y,z-.123,.047,.159)
            wheel(m,x+side*.262,y,z-.16,.125,'gold',.043)
        for pz in (-.30,.33):
            m.block('wall',(.396,.213,pz),(.38,.29,.137),taper=.11)
        axle(m,'ivory',.396,.488,-.525,.237,1.15)
        axle(m,'accent',.396,.488,-.549,.155,.033)
        m.beam('metal',(x,.30,z),(.396,.30,-.009),.078)
        if m.lod<2:
            drum(m,'metal',.396,.687,.37,.069,.286,.047)
            badge(m,.396,.487,-.554,.134,.135)
        return
    cabin(m,-.609,.341,.443,.622,.517 if era==2 else .645,era)
    px,pz=.02,-.239
    end=.763
    skin='trim' if era==2 else 'wall'
    # Horsehead profile follows a polished rod down to the wellhead.
    for s in (-1,1):
        m.beam(skin,(px-.26,.067,pz+s*.213),(px,1.082,pz+s*.098),.116)
        m.beam(skin,(px+.237,.067,pz+s*.206),(px,1.082,pz+s*.098),.111)
    axle(m,'metal',px,1.056,pz-.174,.105,.351)
    m.beam(skin,(-.63,.957,pz),(end-.044,1.195,pz),.134,.175)
    # Solid extruded curved nose, including its forward rubbing surface.
    section=[(.617,1.239),(.741,1.30),(.814,1.191),(.835,1.047),
             (.813,.898),(.740,.835),(.653,.945),(.705,1.079)]
    # Split the concave profile into convex faces around a common interior station.
    center=(.741,1.083)
    for i,(u,v) in enumerate(section):
        x,y=section[(i+1)%len(section)]
        for s in (-1,1):
            f=[(center[0],center[1],pz+s*.10),(u,v,pz+s*.10),(x,y,pz+s*.10)]
            m.face('accent', f if s<0 else f[::-1])
        m.face('accent',[(u,v,pz-.10),(u,v,pz+.10),(x,y,pz+.10),(x,y,pz-.10)])
    m.beam('metal',(.800,.929,pz),(.800,.29,pz),.029)
    drum(m,'metal',.800,.064,pz,.112,.227,.074)
    m.block('trim',(-.472,.264,pz),(.408,.402,.36),taper=.10)
    axle(m,'metal',-.469,.422,pz-.209,.207,.413)
    m.beam('metal',(-.600,.924,pz-.131),(-.57,.50,pz-.214),.062)
    drum(m,'metal',.429,.064,.493,.255,.686,.249)
    m.dome((.429,.748,.493),.251,m.color('metal'),rings=(3,2,1)[m.lod],
           segments=(12,8,5)[m.lod],squash=.30,material='metal')
    if m.lod<2:
        drum(m,'banner',.429,.557,.493,.253,.07)
        m.beam('metal',(.429,.218,.287),(.797,.218,.11),.074)
        m.beam('metal',(.796,.218,.115),(.799,.218,pz),.071)
        if not m.lod:
            wheel(m,.80,.259,pz-.113,.077,'cloth',.027)
            badge(m,-.178,1.069,pz-.104,.105,.143)
    if era==3 and m.lod==0:
        drum(m,'metal',.431,.809,.48,.042,.083)
        drum(m,'turquoise',.431,.891,.48,.038,.041,.023)
    if era==3 and m.lod==2:
        drum(m,'glass',-.04,.065,.621,.157,.85,.119)
    elif era==3:
        drum(m,'wall',-.04,.065,.621,.162,.52)
        drum(m,'glass',-.04,.574,.621,.153,.284,.131)
        drum(m,'wall',-.04,.852,.621,.147,.07,.109)


def build(spec,lod):
    m=House(spec,lod)
    era=ERAS.index(spec.band)
    unit=spec.footprint[0]*.5
    with m.transform(scale=unit):
        m.course('fieldstone' if era>=2 else 'soil',0,0,1.84,1.81,0,.066,.16,.013)
        kind=spec.building_type
        if kind=='WoodcutterCamp':
            camp(m,era)
        elif kind=='Mine' or kind=='GoldMine':
            mine(m,era,kind=='GoldMine')
        elif kind=='CoalMine':
            coal(m,era)
        elif kind=='StoneQuarry':
            quarry(m,era)
        elif kind=='Sawmill':
            saw(m,era)
        else:
            oil(m,era)
    return m


if __name__=='__main__':
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        entry=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=224)
        print(spec.id,entry['lod_triangles'],flush=True)
