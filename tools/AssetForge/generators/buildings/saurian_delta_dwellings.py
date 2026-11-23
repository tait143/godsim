"""Floodplain houses with distinct household plans, landings and roof construction."""
from math import cos, pi, sin
from .hearth_masonry import block, curved_course, dome, flank, gable, gallery, pane, profile, roundwork, steps, terrace


def landing(m,x,z,y,width=.145,height=.195):
    m.door_panel(x,z,y,width,height)
    steps(m,x,z-.012,width*1.34,y,.112,'fieldstone')
    if m.lod<2:
        m.eye(x,y+height+.043,z-.011,.032)


def bronze(m):
    if m.variant:
        # One corbelled adobe drum is the sleeping room. A reed lean-to covers its cook court.
        roundwork(m,'brick',-.116,.099,.277,0,.111,.282)
        roundwork(m,'wall',-.116,.099,.274,.109,.388,.253)
        roundwork(m,'turquoise',-.116,.099,.260,.387,.418,.251)
        dome(m,'hide',-.116,.099,.417,.251,.224)
        terrace(m,'brick',-.105,-.170,.24,.171,0,.316)
        gable(m,'roof',-.105,-.17,.28,.215,.315,.083)
        landing(m,-.105,-.256,.059,.139,.181)
        terrace(m,'wall',.238,.063,.252,.38,0,.078)
        for z in (-.102,.221):
            block(m,'timber',.346,z,.032,.030,.076,.286)
        profile(m,'thatch',.237,.065,.302,.405,.281,[(-.5,.122),(.5,.021)],'timber')
        if m.lod==0:
            m.pot(.24,-.045,.078,.041)
            m.woven_mat(-.192,.136,-.161,.077,.109)
    else:
        # A crescent of sleeping chambers shelters the open kitchen from the river wind.
        # The reed roof wraps its inner court; both thick adobe end walls face the landing.
        start,end=-pi*.226,pi*1.223
        def crescent(section,roles):
            curved_course(m,0,-.025,start,end,section,roles,(14,9,6))
        crescent([(.142,0),(.395,0),(.384,.073),(.150,.073)],
                 ['fieldstone','brick','fieldstone','brick'])
        crescent([(.153,.073),(.376,.073),(.364,.266),(.169,.266)],
                 ['wall','wall','turquoise','wall'])
        crescent([(.127,.266),(.399,.266),(.389,.302),(.271,.493),(.133,.322)],
                 ['thatch','timber','thatch','roof','timber'])
        terrace(m,'brick',.006,-.187,.230,.281,0,.052,taper=.94)
        steps(m,.006,-.328,.159,.052,.108,'brick')
        for a,face in ((start,0),(end,1)):
            x=.259*cos(a); z=-.025+.259*sin(a)
            # -Z faces the open gap at the start and the reversed tangent at the far end.
            with m.transform(translate=(x,.073,z),rotate_y=-a*180/pi+180*face):
                m.door_panel(0,-.003,0,.112,.155,surround='timber')
                if m.lod<2:
                    m.eye(0,.177,-.007,.026)
        if m.lod==0:
            m.pot(.053,-.217,.052,.039)


def iron(m):
    if m.variant:
        # Facing rows of sleeping berths flank a sunken rain court. Timber rain planes
        # drain inward into a shared gutter; their high outer lips shade the openings.
        terrace(m,'fieldstone',0,.037,.760,.581,0,.057)
        for side in (-1,1):
            with m.transform(translate=(side*.214,0,.066),rotate_y=side*17):
                terrace(m,'wall',0,0,.269,.478,.057,.293)
                ridge=[(-.5,.224),(.5,.037)] if side<0 else [(-.5,.037),(.5,.224)]
                profile(m,'roof',0,.004,.337,.566,.292,ridge,'timber')
                m.door_panel(0,-.241,.057,.127,.170)
                if m.lod<2:
                    m.eye(0,.260,-.246,.03)
                    flank(m,.134,.023,.130,.150,.100,sill='timber')
                if m.lod==0:
                    m.woven_mat(-.099,.076,-.242,.045,.114)
        # The covered threshold is shared, while each room has its own door to the courtyard.
        terrace(m,'timber',.002,-.223,.348,.174,.056,.081)
        steps(m,.002,-.310,.214,.081,.133,'fieldstone')
        for x in (-.145,.145):
            block(m,'timber',x,-.274,.028,.035,.080,.223)
        profile(m,'thatch',.003,-.248,.358,.161,.222,[(-.5,.023),(0,.055),(.5,.023)],'timber')
        if m.lod==0:
            m.pot(.060,-.221,.081,.035)
    else:
        # The sleeping hull spans four high piles. Braced gallery and steep access stair below.
        for x in (-.276,.276):
            for z in (-.134,.182):
                roundwork(m,'timber',x,z,.043,0,.289,.039,segments=(8,6,4))
        terrace(m,'timber',0,.030,.749,.477,.286,.339)
        terrace(m,'wall',.025,.038,.627,.349,.338,.517)
        with m.transform(translate=(.025,.517,.036),rotate_y=90):
            profile(m,'roof',0,0,.472,.756,0,[(-.5,.024),(-.27,.161),(0,.238),(.27,.161),(.5,.024)],'timber')
        terrace(m,'timber',-.219,-.126,.254,.251,.315,.338)
        m.door_panel(-.135,-.138,.339,.119,.153)
        steps(m,-.213,-.218,.165,.337,.248,'timber')
        if m.lod<2:
            for x in (-.276,.276):
                m.rail('timber',(x,.032,-.134),(x,.278,.182),.027)
            pane(m,.169,-.137,.387,.136,.075,sill='timber')
        if m.lod==0:
            m.woven_mat(.030,.359,-.138,.063,.091)
            m.pot(-.335,-.152,.339,.034)


def medieval_stairhouse(m):
    # A tidewatch household with a steep masonry base, an oriel, and one occupied tower.
    terrace(m,'fieldstone',.073,.073,.406,.468,0,.319,taper=.91)
    terrace(m,'wall',.073,.073,.371,.420,.316,.685)
    terrace(m,'trim',.073,.073,.461,.521,.683,.739)
    profile(m,'slate',.073,.073,.482,.553,.738,[(-.5,.022),(0,.308),(.5,.022)],'timber')
    terrace(m,'wall',-.197,-.089,.260,.284,0,.248)
    gable(m,'roof',-.198,-.091,.302,.328,.248,.128)
    landing(m,-.198,-.232,.046,.126,.171)
    pane(m,.073,-.139,.465,.199,.133)
    flank(m,.260,.063,.469,.161,.113)
    if m.lod==0:
        m.woven_mat(.167,.124,-.150,.056,.124)
        m.pot(-.321,-.197,0,.040)


def renaissance(m):
    if m.variant:
        # A tiled octagonal merchant's house with an inset high gallery and a porter's lodge.
        x,z=.116,.136
        roundwork(m,'plaster',x,z,.261,0,.374,.229,segments=(8,8,6))
        roundwork(m,'roof_tile',x,z,.271,.373,.425,.269,segments=(8,8,6))
        roundwork(m,'turquoise',x,z,.193,.424,.633,.188,segments=(8,8,6))
        roundwork(m,'trim',x,z,.227,.632,.669,.219,segments=(8,8,6))
        dome(m,'roof_tile',x,z,.667,.220,.128)
        terrace(m,'plaster',-.187,-.074,.284,.402,0,.248)
        m.hip_roof((-.187,.248,-.074),.322,.432,.138,m.color('roof_tile'),overhang=0,material='roof_tile')
        landing(m,-.187,-.275,.047,.146,.168)
        for angle in (0,60,120) if m.lod<2 else ():
            with m.transform(translate=(x,.459,z),rotate_y=angle):
                pane(m,0,-.177,0,.092,.130,sill='trim',light='door')
        if m.lod==0:
            m.pot(.252,-.121,0,.049)
    else:
        # A sunlit courtyard between a low colonnade and the owners' tall enclosed pavilion.
        terrace(m,'plaster',-.190,.149,.405,.383,0,.448)
        terrace(m,'turquoise',-.190,.149,.445,.426,.447,.487)
        m.hip_roof((-.190,.487,.149),.474,.450,.203,m.color('roof_tile'),overhang=0,material='roof_tile')
        terrace(m,'fieldstone',.141,-.074,.360,.515,0,.063)
        terrace(m,'plaster',.147,.106,.302,.164,.062,.264)
        with m.transform(translate=(.141,.062,-.074),rotate_y=-90):
            gallery(m,'ivory',0,0,.522,.315,0,.212,3)
            gable(m,'roof_tile',0,0,.553,.357,.255,.101)
        landing(m,-.190,-.044,.049,.149,.189)
        pane(m,-.19,-.044,.299,.194,.086)
        if m.lod==0:
            m.pot(-.293,-.163,0,.046)
            m.woven_mat(-.060,.072,-.046,.071,.133)


def information_loft(m):
    # A flood-adaptive glazed living pod cantilevers over a smaller entrance and two forks.
    terrace(m,'ivory',-.154,.072,.302,.379,0,.309,taper=.93)
    landing(m,-.154,-.119,.039,.136,.185)
    for z in (-.106,.239):
        m.rail('metal',(.213,.016,z),(.320,.350,z),.049)
        m.rail('metal',(.213,.016,z),(.126,.350,z),.048)
    terrace(m,'metal',.047,.071,.731,.459,.310,.382,taper=1.04,cut=.22)
    terrace(m,'glass',.059,.072,.724,.448,.382,.584,taper=.92,cut=.25)
    terrace(m,'ivory',.056,.072,.723,.451,.584,.628,taper=.94,cut=.21)
    if m.lod<2:
        for x in (-.128,.047,.222):
            m.rail('turquoise',(x,.383,-.155),(x+.015,.581,-.136),.020)
        flank(m,-.010,.085,.112,.141,.076)
    if m.lod==0:
        m.pot(-.293,-.135,0,.037)
        m.eye(-.154,.268,-.119,.030)
