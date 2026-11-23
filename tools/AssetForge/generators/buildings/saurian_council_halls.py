"""Public river landings, civic halls and sun-calendar assemblies of the delta."""
from math import cos, pi, sin
from .hearth_masonry import block, dome, flank, gable, gallery, pane, profile, roundwork, steps, terrace


def public_gate(m,x,front,sill,w,h):
    m.entry(x,sill,front,w,h)
    steps(m,x,front-.016,w*1.35,sill,.20)
    if m.lod<2:
        m.disc(x,sill+h+.051,front-.017,w*.135)


def bronze_court(m):
    # A low U of adobe record rooms encloses a raised open meeting court and a solar standard.
    terrace(m,'fieldstone',-.015,-.044,1.56,1.28,0,.103)
    terrace(m,'wall',-.019,.392,1.385,.405,.102,.438,taper=.96)
    terrace(m,'thatch',-.019,.392,1.465,.498,.437,.603,taper=.68)
    for x in (-.611,.575):
        terrace(m,'brick',x,-.11,.278,.694,.104,.233,taper=.90)
        terrace(m,'wall',x,-.042,.228,.551,.232,.316,taper=.87)
    for x in (-.421,.384):
        block(m,'timber',x,.061,.064,.074,.103,.484)
    terrace(m,'roof',-.022,.095,1.005,.318,.481,.559,taper=.85)
    m.entry(-.023,.105,.184,.329,.269)
    roundwork(m,'brick',-.016,-.236,.203,.103,.185,.175)
    block(m,'timber',-.016,-.234,.049,.052,.184,.507)
    m.disc(-.016,.572,-.249,.152)
    steps(m,0,-.684,.634,.103,.218)
    if m.lod<2:
        for s in (-1,1):
            m.disc(s*.44,.282,.184,.066)
    if m.lod==0:
        for x in (-.600,.571):
            m.jar(x,.316,-.117,.066,'turquoise')


def iron_council_nave(m):
    # The river clans meet in a roofed longitudinal nave. The side delegations use
    # separate raised platforms, reached from the wide processional court in front.
    terrace(m,'fieldstone',0,-.009,.951,1.602,0,.150,taper=.97)
    terrace(m,'wall',0,-.009,.932,1.562,.150,.198,taper=.99)
    steps(m,0,-.790,.674,.198,.191,'fieldstone')
    for side in (-1,1):
        cx=side*.640
        terrace(m,'fieldstone',cx,.095,.566,1.066,0,.203,taper=.89)
        terrace(m,'brick',cx,.095,.517,.969,.201,.292,taper=.94)
        terrace(m,'trim',cx,.095,.508,.948,.291,.335,taper=.98)
        # A clerk's desk and backed bench face the central floor. Both are full-size
        # spatial elements through the distant LOD; the two platforms are open to view.
        terrace(m,'timber',side*.747,.166,.135,.581,.335,.437)
        terrace(m,'wall',side*.807,.166,.055,.580,.437,.566,taper=.93)
        if m.lod==2:
            block(m,'wall',side*.655,-.215,.243,.137,.335,.523)
        else:
            terrace(m,'wall',side*.655,-.215,.241,.134,.335,.486,taper=.80)
            terrace(m,'timber',side*.655,-.215,.260,.159,.485,.523)
    # An open six-post frame carries the flanking roofs and a raised central clerestory.
    # The separate roof planes make a continuous row of true daylight openings.
    for side in (-1,1):
        sx=side*.323
        for z in (-.226,.208,.595):
            terrace(m,'fieldstone',sx,z,.125,.131,.198,.292,taper=.82)
            block(m,'timber',sx,z,.079,.086,.290,.796)
            if m.lod<2:
                block(m,'metal',sx,z,.096,.107,.690,.743)
        block(m,'timber',sx,.190,.096,.998,.748,.807)
        shape=[(-.5,.028),(.5,.173)] if side<0 else [(-.5,.173),(.5,.028)]
        profile(m,'roof',side*.271,.180,.425,1.077,.777,shape,'timber')
        # Narrow upper frame remains visible beneath the iron-edged ridge cap.
        for z in (-.197,.249,.552):
            block(m,'timber',side*.101,z,.038,.058,.908,1.075)
    profile(m,'timber',0,.188,.328,1.107,1.039,[(-.5,.023),(0,.113),(.5,.023)],'trim')
    # A marked transverse portal stands at the front of the processional court,
    # well clear of the nave eaves. Its opening frames the shared inner dais.
    for side in (-1,1):
        x=side*.272
        terrace(m,'wall',x,-.599,.166,.161,.198,.414,taper=.85)
        terrace(m,'timber',x,-.599,.101,.102,.414,.626,taper=.91)
    terrace(m,'timber',0,-.599,.734,.126,.624,.693,taper=.94)
    m.disc(0,.677,-.668,.103,'gold',boss=False)
    # The chancellery closes only the rear end of the nave. Its two marked doors
    # flank the central speaker's seat and keep both long sides open to the public.
    terrace(m,'wall',0,.586,.585,.219,.198,.657)
    terrace(m,'timber',0,.581,.639,.255,.656,.703)
    for x in (-.165,.165):
        m.entry(x,.199,.473,.160,.322)
    terrace(m,'fieldstone',0,.273,.291,.288,.199,.265)
    block(m,'timber',0,.294,.201,.108,.265,.392)
    if m.lod<2:
        for side in (-1,1):
            m.rod('timber',(side*.323,.569,-.219),(side*.323,.758,-.050),.027,n=5)
            m.disc(side*.650,.416,-.289,.054,'turquoise',False)
    if m.lod==0:
        for side in (-1,1):
            m.jar(side*.385,.198,-.624,.051,'turquoise')
            for z in (-.072,.221,.447):
                block(m,'metal',side*.807,z,.063,.033,.453,.514)


def medieval_palace(m):
    # A civic palace occupies an entire high terrace. The wide external public stair
    # climbs alongside the audience gallery, underneath an open masonry roof comb.
    terrace(m,'fieldstone',-.203,.063,1.187,1.378,0,.255,taper=.95)
    terrace(m,'wall',-.203,.077,1.082,1.269,.251,.542,taper=.92)
    terrace(m,'trim',-.203,.080,1.048,1.207,.540,.608)
    terrace(m,'wall',-.202,.357,.979,.559,.608,.933)
    m.entry(-.431,.608,.072,.242,.239)
    m.entry(-.014,.608,.072,.242,.239)
    for x in (-.638,-.352,-.059,.221):
        terrace(m,'fieldstone',x,-.213,.104,.121,.608,.901,taper=.86)
    terrace(m,'trim',-.209,-.009,1.108,.617,.900,.982)
    terrace(m,'roof',-.204,.121,1.191,.978,.981,1.123,taper=.79)
    # The continuous crest has three genuine sky openings, framed by four piers.
    for x in (-.535,-.314,-.094,.124):
        terrace(m,'brick',x,.208,.099,.171,1.119,1.395,taper=.87)
    terrace(m,'trim',-.205,.207,.809,.206,1.395,1.447)
    for x in (-.438,.035):
        terrace(m,'roof',x,.206,.243,.184,1.447,1.510,taper=.78)
    terrace(m,'wall',.507,.163,.409,.347,0,.605)
    steps(m,.510,-.014,.411,.604,.866)
    terrace(m,'wall',.369,.132,.192,.272,.536,.609)
    if m.lod<2:
        m.disc(-.211,1.412,.098,.082,'turquoise',False)
        for x in (-.554,.130):
            terrace(m,'fieldstone',x,-.412,.208,.115,.608,.690)
    if m.lod==0:
        for x in (-.534,-.314,-.094,.124):
            m.block('turquoise',x,1.248,.119,.046,.067,.017)
        m.jar(.520,.605,.252,.071,'turquoise')


def industrial_council(m):
    # An open civic concourse faces the lower landing beneath a corrugated roof and brick tide clock.
    terrace(m,'brick',-.210,.159,1.132,.773,0,.580)
    profile(m,'metal',-.210,.159,1.217,.864,.579,
            [(-.5,.035),(-.35,.196),(0,.315),(.35,.196),(.5,.036)],'brick')
    terrace(m,'fieldstone',-.212,-.398,1.139,.419,0,.079)
    gallery(m,'metal',-.212,-.358,1.187,.422,.079,.307,4)
    profile(m,'roof',-.211,-.36,1.231,.469,.429,[(-.5,.032),(.5,.078)],'metal')
    m.entry(-.210,.078,-.231,.307,.291)
    steps(m,-.230,-.609,.49,.080,.172)
    terrace(m,'brick',.533,-.08,.362,.424,0,1.064,taper=.90)
    terrace(m,'metal',.533,-.08,.411,.475,1.062,1.145)
    terrace(m,'wall',.533,-.08,.344,.401,1.144,1.439)
    m.disc(.533,1.297,-.281,.125,'gold',False)
    if m.lod<2:
        m.rod('door',(.533,1.298,-.318),(.531,1.374,-.318),.011)
        m.rod('door',(.533,1.299,-.326),(.590,1.303,-.326),.012)
    gable(m,'metal',.533,-.080,.412,.463,1.439,.148)
    if m.lod==0:
        for x in (-.586,.167):
            pane(m,x,-.228,.201,.191,.193,sill='brick',light='glass')
        m.disc(-.213,.429,-.594,.087)


def modern_pavilion(m):
    # Three radial assembly wings radiate from a round, half-open municipal rotunda.
    roundwork(m,'plaster',0,.055,.364,0,.480,.339)
    roundwork(m,'glass',0,.055,.341,.480,.707,.332)
    roundwork(m,'ivory',0,.055,.405,.706,.782,.392)
    for ang in (0,120,240):
        with m.transform(translate=(0,0,.055),rotate_y=ang):
            terrace(m,'plaster',0,.472,.343,.375,0,.298)
            terrace(m,'glass',0,.462,.388,.455,.297,.441)
            terrace(m,'ivory',0,.483,.481,.562,.441,.522,taper=.94)
            if m.lod<2:
                flank(m,.193,.462,.320,.240,.087,sill='turquoise',light='glass')
    terrace(m,'plaster',0,-.245,.307,.207,0,.289)
    public_gate(m,0,-.350,.046,.258,.206)
    m.disc(0,.612,-.291,.100,'turquoise')
    if m.lod==0:
        for x in (-.227,.230):
            m.jar(x,0,-.305,.054,'turquoise')


def information_spire(m):
    # A sloping digital calendar and an elevated open council disc share a compact quorum room.
    terrace(m,'fieldstone',-.160,-.121,.751,.755,0,.114)
    terrace(m,'glass',-.160,-.121,.656,.691,.113,.387)
    public_gate(m,-.160,-.467,.094,.315,.236)
    roundwork(m,'ivory',-.117,-.077,.519,.386,.481,.505)
    for a in (0,2*pi/3,4*pi/3):
        x=-.117+.396*cos(a); z=-.077+.396*sin(a)
        roundwork(m,'turquoise',x,z,.046,.480,.701,.036,segments=(9,6,4))
    roundwork(m,'ivory',-.117,-.077,.532,.700,.784,.502)
    terrace(m,'ivory',.333,.327,.483,.444,0,.234)
    terrace(m,'glass',.333,.327,.455,.412,.231,1.157,taper=.72,shift=(-.109,.058))
    terrace(m,'turquoise',.225,.384,.332,.303,1.156,1.606,taper=.30,shift=(-.158,.008))
    if m.lod<2:
        for i in range(3):
            t=.23+i*.27
            m.disc(.333-.109*t,.231+.926*t,.119+.111*t,.070,'turquoise',False)
    if m.lod==0:
        flank(m,.172,-.115,.207,.225,.124,sill='ivory',light='glass')


COUNCILS={1:bronze_court,2:iron_council_nave,4:medieval_palace,6:industrial_council,
          7:modern_pavilion,8:information_spire}
