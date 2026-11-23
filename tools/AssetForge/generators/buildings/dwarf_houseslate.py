"""Dwarf family homes from the first stove-heated brick rows to the sunlit pearl courts."""
from math import cos, pi, sin
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import ROOT, package_models
from forge import publish
from forge.render import contact_sheet
from generators.buildings.dwarf_housesancient import ClanHouse


PACKAGE = "dwarf_houseslate"
AGES = ("industrial", "modern", "information", "ascension")
FRONT = -.262


def outline(x, y, z, w, d, cut):
    a, b = w / 2, d / 2
    if not cut:
        return [(x+u,y,z+v) for u,v in [(-a,-b),(a,-b),(a,b),(-a,b)]]
    cut = min(cut, a * .85, b * .85)
    points = [(-a+cut, -b), (a-cut, -b), (a, -b+cut), (a, b-cut),
              (a-cut, b), (-a+cut, b), (-a, b-cut), (-a, -b+cut)]
    return [(x+u, y, z+v) for u, v in points]


def shell(m, x, z, levels, *, square_mid=False):
    """Successive outlines skin one solid, including the soffits under projecting eaves."""
    square = m.lod == 2 or (m.lod == 1 and square_mid)
    rings = [outline(x,y,z,w,d,0 if square else c) for y,w,d,c,_ in levels]
    for lower, upper, level in zip(rings, rings[1:], levels[1:]):
        for i in range(len(lower)):
            j = (i+1) % len(lower)
            m.face(level[-1], (lower[j], lower[i], upper[i], upper[j]))
    m.face(levels[-1][-1], rings[-1][::-1])


def buttress(m, side, z, head):
    low = outline(side*.404, 0, z, .095, .122, .024 if not m.lod else 0)
    high = outline(side*.372, head, z, .062, .100, .018 if not m.lod else 0)
    for i in range(len(low)):
        j = (i+1) % len(low)
        m.face("fieldstone", (low[j], low[i], high[i], high[j]))
    m.face("fieldstone", high[::-1])


def panel(m, x, y, z, width, height, role):
    """A glazed opening in a raised frame. Mullions are bounded by the rebate."""
    def pane(w, h, depth, mat):
        m.face(mat, [(x-w/2, y-h/2, depth), (x-w/2, y+h/2, depth),
                     (x+w/2, y+h/2, depth), (x+w/2, y-h/2, depth)])
    m.block("trim", (x, y, z-.006), (width, height, .018))
    pane(width-.024, height-.022, z-.017, role)
    if m.lod == 0:
        pane(.012, height-.020, z-.0185, "trim")
        m.block("metal", (x, y-height/2-.004, z-.013), (width+.018, .012, .043))


def clan_door(m, x, stone):
    # The door and concentric cut-stone arch have the same two clipped shoulders.
    z, base = FRONT, .061
    def profile(w, h, cut, depth):
        return [(x+u, base+v, depth) for u,v in
                [(-w/2, 0), (-w/2, h-cut), (-w/2+cut, h), (w/2-cut, h),
                 (w/2, h-cut), (w/2, 0)]]
    inner = profile(.147, .191, .032, z-.034)
    outer = profile(.224, .231, .051, z-.036)
    if m.lod == 2:
        m.face(stone,profile(.224,.231,.051,z-.008))
        m.face("door",profile(.147,.191,.032,z-.011))
        m.block("fieldstone",(x,base/2,z-.068),(.224,base,.139))
        return
    m.face("door", inner)
    for i in range(5):
        j = i+1
        m.face(stone, (outer[i], outer[j], inner[j], inner[i]))
        m.face(stone, (outer[j], outer[i], (outer[i][0],outer[i][1],z+.006),
                       (outer[j][0],outer[j][1],z+.006)))
    if m.lod < 2:
        # An inset brass house-mark and a small latch distinguish the family entrance.
        mark = [(x, base+.190, z-.040), (x-.021, base+.214, z-.040),
                (x, base+.238, z-.040), (x+.021, base+.214, z-.040)]
        m.face("gold", mark)
        m.block("gold", (x+.041, base+.087, z-.040), (.019,.014,.017))
    # Treads abut across their risers, forming a single closed stepped profile.
    n = (3,2,1)[m.lod]
    for i in range(n):
        h = base*(i+1)/n
        tread = .124/n
        zz = z-.030-.124+(i+.5)*tread
        m.block("fieldstone", (x,h/2,zz), (.230-.008*i,h,tread))


def bench(m, x):
    z = FRONT-.062
    for side in (-1, 1):
        m.block("fieldstone", (x+side*.055,.031,z), (.045,.062,.065))
    m.block("timber", (x,.073,z), (.191,.022,.087))
    # One folded seat pad, in the Dwarf clan colour.
    m.block("banner", (x+.044,.089,z+.001), (.062,.010,.075))


def side_bay(m, role):
    # A reading nook built out from the flank on a continuous battered stone corbel.
    shell(m,0,0,[(.072,.238,.036,.015,"fieldstone"),(.134,.299,.149,.042,"fieldstone"),
                 (.151,.302,.151,.042,"trim"),(.242,.302,.151,.042,role),
                 (.255,.324,.165,.044,"trim"),(.274,.297,.147,.041,"metal")],
          square_mid=True)
    if m.lod == 0:
        for x in (-.080,.080):
            m.block("metal",(x,.196,-.077),(.017,.092,.012))


def doorstep_detail(m, x):
    z = FRONT-.067
    if m.age=="industrial":
        # A stack of split stove billets lies in a three-sided coal scuttle.
        m.block("metal",(x,.031,z),(.177,.062,.101),taper=.08)
        for i in range(3):
            with m.transform(translate=(x+(i-1)*.054,.076+.004*(i%2),z),rotate_x=90):
                m.cylinder((0,-.044,0),.024,.089+.005*i,m.color("timber"),segments=5,
                           cap_bottom=True,material="timber")
    elif m.age=="information":
        m.block("fieldstone",(x,.035,z),(.089,.070,.080),taper=.10)
        m.block("trim",(x,.103,z),(.126,.069,.103))
        m.block("ivory",(x,.144,z),(.140,.020,.113),taper=.12)
        m.block("turquoise",(x,.108,z-.053),(.066,.013,.009))
    else:
        m.block("fieldstone",(x,.049,z),(.175,.098,.093),taper=.065)
        for i in range(3):
            r = .043 if i==1 else .037
            m.dome((x+(i-1)*.050,.097,z),r,m.color("leaf"),segments=5,
                   rings=1,squash=1.27,material="leaf")


def household(m, variant, wall, h, front_pane, surround, window_width=.135):
    x = .055 if variant else -.067
    z = .040
    levels = [(0,.797,.622,.078,"fieldstone"),(.072,.751,.604,.061,"fieldstone")]
    if m.lod < 2:
        levels[1] = (.072,.777,.615,.071,"fieldstone")
        levels.append((.078,.751,.604,.061,"trim"))
    shell(m,0,z,[*levels,(h,.751,.604,.061,wall)],square_mid=True)
    clan_door(m, x, surround)
    wx = -.206 if variant else .205
    if m.lod == 2:
        m.face(front_pane,[(wx-.069,.152,FRONT-.003),(wx-.069,.242,FRONT-.003),
                           (wx+.069,.242,FRONT-.003),(wx+.069,.152,FRONT-.003)])
    if m.lod < 2:
        panel(m, wx, .199, FRONT, window_width, .100, front_pane)
        with m.transform(translate=(.3755,0,.051), rotate_y=-90):
            if variant:
                side_bay(m,front_pane)
            else:
                panel(m,0,.198,0,.239,.108,front_pane)
        for side in (-1,1):
            buttress(m, side, .223, h-.006)
    if m.lod == 0:
        if variant:
            doorstep_detail(m,wx)
        else:
            bench(m,wx)
        # Lights on the rear wall keep the domestic scale in the reverse view.
        with m.transform(translate=(0,0,.342), rotate_y=180):
            panel(m, -.105,.201,0,.226,.086,front_pane)
        m.lamp_face(x-.132,.249,FRONT-.012,.032)


def roof_profile(m, profile, z, depth, base, end, skins):
    """Longitudinal folded roof, including both gables, fascia and exposed underside."""
    f, b = z-depth/2, z+depth/2
    for (x0,y0), (x1,y1), role in zip(profile, profile[1:], skins):
        m.face(role, [(x0,y0,f),(x0,y0,b),(x1,y1,b),(x1,y1,f)])
        if x0 != x1:
            m.face(end, [(x0,base,f),(x0,y0,f),(x1,y1,f),(x1,base,f)])
            m.face(end, [(x1,base,b),(x1,y1,b),(x0,y0,b),(x0,base,b)])
    left, right = profile[0], profile[-1]
    m.face(end, [(left[0],base,b),(*left,b),(*left,f),(left[0],base,f)])
    m.face(end, [(right[0],base,f),(*right,f),(*right,b),(right[0],base,b)])
    m.face(end, [(left[0],base,f),(right[0],base,f),(right[0],base,b),(left[0],base,b)])


def hearth(m, x, z, base, top, square):
    # A tapered stack with a hollow, soot-dark throat and a slightly cocked pot.
    n = 4 if square else (8,6,5)[m.lod]
    with m.transform(translate=(x,base,z), rotate_z=3 if square else -3, rotate_y=45 if square else 0):
        def ring(y,r):
            return [(r*cos(2*pi*i/n),y,r*sin(2*pi*i/n)) for i in range(n)]
        height = top-base
        if m.lod == 2:
            m.cylinder((0,0,0),.079,height-.046,m.color("brick"),segments=n,
                       top_radius=.063,cap_top=False,material="brick")
            m.cylinder((0,height-.046,0),.066,.046,m.color("metal"),segments=n,
                       material="metal")
            return
        rings = [(0,.082), (height-.055,.063), (height-.046,.076),
                 (height,.076), (height,.045), (height-.049,.043)]
        loops = [ring(y,r) for y,r in rings]
        for a,b,role in zip(loops,loops[1:],("brick","metal","metal","metal","door")):
            for i in range(n):
                j=(i+1)%n
                m.face(role,(a[j],a[i],b[i],b[j]))
        m.face("door",loops[-1][::-1])


def industrial(m, variant):
    h = .302
    household(m, variant, "brick", h, "emissive", "fieldstone")
    if variant:
        # Two short northlights span the broad living floor of the weaver's cottage.
        profile = [(-.415,.341),(-.119,.505),(-.087,.374),(.173,.495),(.203,.353),(.415,.330)]
        roles = ["roof","glass","roof","glass","roof"]
        hearth(m,-.214,.194,.387,.689,True)
    else:
        # Stepped shoulders shelter a little dormer clerestory above the hearth room.
        profile = [(-.417,.338),(-.194,.411),(-.174,.499),(-.060,.535),(.109,.515),
                   (.129,.415),(.417,.332)]
        roles = ["roof","metal","slate","slate","glass","roof"]
        hearth(m,.220,.175,.358,.701,False)
    roof_profile(m, profile, .045,.655,h,"wall",roles)
    if m.lod < 2:
        panel(m,-.037 if not variant else -.182,.433 if not variant else .400,
              -.2825,.132 if not variant else .080,.056,"emissive")
    if m.lod == 0:
        # Folded iron seams follow the actual plate pitches.
        for zz in (-.153,.075):
            for (a,y), (b,v), role in zip(profile,profile[1:],roles):
                if role in ("roof","slate") and b-a>.092:
                    m.beam("metal",(a+.006,y+.003,zz),(b-.006,v+.003,zz),.010)


def modern(m, variant):
    h = .317
    household(m, variant, "plaster", h, "glass", "fieldstone", .160)
    if variant:
        # Set-back sleeping floor sits to one side of the planted roof terrace.
        shell(m,0,.040,[(h,.768,.613,.056,"trim"),(.350,.843,.670,.062,"metal"),
                         (.382,.833,.657,.057,"ivory")])
        shell(m,-.097,.106,[(.382,.506,.396,.050,"wall"),(.400,.485,.383,.043,"trim"),
                            (.528,.485,.383,.043,"glass"),(.538,.534,.432,.061,"metal"),
                            (.568,.526,.426,.061,"ivory")])
        if m.lod<2:
            m.block("fieldstone",(-.143,.461,-.088),(.049,.133,.030))
            m.block("fieldstone",(-.340,.459,.084),(.026,.122,.042))
        if m.lod==0:
            m.block("fieldstone",(.263,.412,.145),(.122,.060,.260),taper=.08)
            m.block("leaf",(.263,.449,.145),(.101,.032,.237),taper=.22)
    else:
        # A glazed clerestory rises to meet both wings of a folded concrete plate.
        shell(m,0,.040,[(h,.731,.579,.054,"metal"),(.389,.725,.572,.052,"glass")])
        profile = [(-.428,.524),(-.068,.425),(.426,.510)]
        roof_profile(m,[(-.363,.470),(-.068,.390),(.363,.462)],.041,.570,.385,
                     "glass",["glass","glass"])
        for (x,y),(u,v) in zip(profile,profile[1:]):
            near, far, thickness = -.297,.385,.037
            def face_at(z):
                return [(x,y-thickness,z),(x,y,z),(u,v,z),(u,v-thickness,z)]
            a,b = face_at(near),face_at(far)
            m.face("ivory",a)
            m.face("ivory",b[::-1])
            m.face("roof",(a[1],b[1],b[2],a[2]))
            m.face("ivory",(a[0],a[3],b[3],b[0]))
            if x==profile[0][0]:
                m.face("ivory",(b[0],b[1],a[1],a[0]))
            if u==profile[-1][0]:
                m.face("ivory",(a[3],a[2],b[2],b[3]))
            if m.lod==0:
                for zz in (-.091,.136):
                    m.beam("metal",(x+.004,y+.001,zz),(u-.004,v+.001,zz),.012)
        if m.lod<2:
            for x in (-.244,.165):
                m.block("metal",(x,.354,-.248),(.028,.074,.025))
        if m.lod==0:
            # The central valley carries runoff to a shallow hopper at the rear.
            m.block("metal",(-.066,.384,.377),(.090,.065,.070),taper=.11)
            m.block("metal",(-.066,.187,.386),(.028,.328,.025))


def receiver(m, x, z, y, crossbars=True):
    with m.transform(translate=(x,y,z)):
        m.block("metal",(0,.036,0),(.045,.072,.045),taper=.25)
        m.block("trim",(0,.101,0),(.016,.079,.016))
        if m.lod<2 and crossbars:
            for i in range(2 if m.lod==0 else 1):
                m.block("gold",(.005,.111+.027*i,0),(.087-.024*i,.012,.015))


def information(m, variant):
    h = .291
    household(m, variant, "wall", h, "glass", "metal", .154)
    shell(m,0,.040,[(h,.754,.606,.058,"metal"),(.312,.829,.660,.085,"trim"),
                     (.351,.821,.649,.083,"ivory")])
    if variant:
        # A stair of glazed rooms: a single high lantern anchors a long low sunroom.
        shell(m,.206,.060,[(.351,.315,.475,.045,"glass"),(.421,.272,.418,.046,"glass"),
                           (.437,.274,.422,.048,"turquoise")])
        shell(m,-.123,.074,[(.351,.404,.524,.069,"metal"),(.377,.413,.528,.070,"metal"),
                            (.563,.315,.421,.066,"glass"),(.574,.332,.438,.067,"turquoise"),
                            (.611,.317,.422,.066,"ivory")])
        receiver(m,-.143,.183,.611)
    else:
        # A low eight-facet glazed lantern fills the roof; the white cap echoes a cut gemstone.
        shell(m,.002,.056,[(.351,.681,.508,.125,"metal"),(.401,.684,.509,.125,"glass"),
                           (.515,.521,.383,.105,"glass"),(.531,.512,.377,.105,"turquoise"),
                           (.556,.458,.329,.090,"ivory")])
        receiver(m,-.122,.135,.556,crossbars=m.lod==0)
    if m.lod<2:
        # Separate glazing bars rise with the rake of the windward panes.
        if variant:
            for xx in (-.207,-.056):
                m.beam("metal",(xx,.377,-.190),(xx,.560,-.139),.014)
        else:
            for xx in (-.133,.133):
                m.beam("metal",(xx,.403,-.199),(xx,.515,-.138),.016)


def circlet(m, x, y, z, radius, vertical=False):
    """The closed inner rim catches the light through the aperture."""
    count = (8,6,6)[m.lod]
    with m.transform(translate=(x,y,z), rotate_x=-72 if vertical else 0):
        loops = []
        for r,h in [(radius,-.019),(radius,.019),(radius-.031,.019),(radius-.031,-.019)]:
            loops.append([(r*cos(2*pi*(i+.5)/count),h,r*sin(2*pi*(i+.5)/count))
                          for i in range(count)])
        for j,role in enumerate(("gold","gold","ascension","gold")):
            a,b = loops[j],loops[(j+1)%4]
            for i in range(count):
                k = (i+1)%count
                m.face(role,(a[k],a[i],b[i],b[k]))


def wheel_tracery(m):
    # A single casting. The central triangle shares its three edges with the arms.
    rim = [(-.010,-.134),(.010,-.134),(.010,-.017),(.108,.071),(.096,.086),
           (0,.002),(-.096,.086),(-.108,.071),(-.010,-.017)]
    near = [(x,-.009,z) for x,z in rim]
    far = [(x,.009,z) for x,z in rim]
    panels = [(0,1,2,8),(2,3,4,5),(5,6,7,8),(8,2,5)]
    for panel_indices in panels:
        m.face("gold",[near[i] for i in panel_indices])
        m.face("gold",[far[i] for i in reversed(panel_indices)])
    for i in range(len(rim)):
        j=(i+1)%len(rim)
        m.face("gold",(near[j],near[i],far[i],far[j]))


def ascension(m, variant):
    h = .301
    household(m, variant, "ivory", h, "ascension", "gold")
    shell(m,0,.040,[(h,.755,.605,.067,"trim"),(.316,.820,.662,.100,"gold"),
                     (.360,.820,.662,.100,"ivory"),(.422,.705,.538,.093,"wall")])
    if variant:
        # A reading court. Three bearing stones carry an open gilded sunshade.
        cx,cz = .028,.071
        shell(m,cx,cz,[(.422,.528,.414,.099,"trim"),(.460,.484,.376,.097,"ivory")])
        for i in range(3):
            angle = 2*pi*i/3+pi/2
            x,z = .176*cos(angle),.176*sin(angle)
            m.beam("ivory",(cx+x*.83,.456,cz+z*.83),(cx+x,.558,cz+z),.064)
        circlet(m,cx,.576,cz,.209)
    else:
        # The sun wheel is held in a long pearl saddle, between two shoulder blocks.
        shell(m,-.025,.068,[(.422,.492,.388,.073,"metal"),(.474,.436,.323,.083,"ivory"),
                             (.489,.387,.284,.078,"gold")])
        cx,cz = -.035,.086
        cy = .619
        circlet(m,cx,cy,cz,.152,True)
        # A branching lancet divides the open wheel into three little lights.
        with m.transform(translate=(cx,cy,cz),rotate_x=-72):
            wheel_tracery(m)
        if m.lod < 2:
            for side in (-1,1):
                m.beam("ivory",(cx+side*.123,.484,cz-.018),
                       (cx+side*.106,.533,cz-.029),.046)
    if m.lod == 0:
        # Gold lozenges are set into the ivory eave over the threshold.
        for x in (-.137,0,.137):
            with m.transform(translate=(x,.339,-.293),rotate_z=45):
                m.block("gold",(0,0,0),(.023,.023,.013))


def build(spec, lod):
    house = ClanHouse(spec,lod)
    ages = dict(industrial=industrial, modern=modern, information=information, ascension=ascension)
    ages[house.age](house,spec.variant)
    return house


if __name__ == "__main__":
    entries = []
    for spec in sorted(package_models(PACKAGE), key=lambda s: (s.variant,AGES.index(s.ages[0]))):
        levels = [build(spec,lod) for lod in range(3)]
        entry = publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=256)
        entries.append(entry)
        print(spec.id,entry["lod_triangles"])
    paths = [ROOT/entry["preview"] for entry in entries]
    labels = [f"{entry['ages'][0].title()}  {entry['variant']+1}" for entry in entries]
    contact_sheet(paths, paths[0].parent/"dwarf_houseslate_sheet.png", cols=4,cell=192,labels=labels)
