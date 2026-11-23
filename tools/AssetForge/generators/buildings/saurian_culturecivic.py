"""The public life of the Saurian delta, from storytellers' fires to garden cities."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.saurian_towncenters import Delta, generate

PACKAGE = "saurian_culturecivic"


def flag(m, x, y, z, height, side=1):
    m.drum("trim", x, y, z, .018, height, n=(6, 5, 4)[m.lod])
    with m.transform(translate=(x, y+height, z), scale=(side, 1, 1)):
        corners = [(0, -.025, 0), (.104, -.043, -.034), (.204, -.061, -.009),
                   (.126, -.148, -.031), (0, -.129, 0)]
        for inds in ((0, 1, 3, 4), (1, 2, 3)):
            panel = [corners[i] for i in inds]
            m.face("cloth", panel)
            m.face("cloth", [(a, b, c+.005) for a,b,c in panel[::-1]])


def lamp(m, x, y, z, r):
    m.drum("trim", x, y, z, r*.77, r*.55, r)
    m.drum("gold", x, y+r*.55, z, r, r*.42, r*.95)
    m.mound("emissive", x, y+r*.97, z, r*.79, r*.77, n=(8, 6, 4)[m.lod])


def flame(m, x, y, z, radius, height, lean, role):
    # A curling, off-axis polygonal tongue holds its profile from all angles.
    count = (8, 6, 5)[m.lod]
    levels = [(0, .7, 0), (.24, 1, -.14), (.63, .58, .38)]
    rings = [[(x+lean*shift+radius*width*math.cos(2*math.pi*i/count),
               y+height*level, z+radius*width*math.sin(2*math.pi*i/count))
              for i in range(count)] for level, width, shift in levels]
    for i in range(count):
        j = (i+1) % count
        for low, high in zip(rings, rings[1:]):
            m.face(role, (low[j], low[i], high[i], high[j]))
        m.face(role, (rings[-1][j], rings[-1][i], (x+lean, y+height, z-.023)))


def campfire(m):
    classical = m.era == 1
    y = .095 if classical else .036
    m.slab("wall", 0, 0, .035, 1.83, y, 1.75, .97, cut=.28)
    if classical:
        m.drum("trim", 0, y, .075, .419, .12, .45)
        m.drum("gold", 0, y+.12, .075, .455, .046, .448)
        bowl = y+.161
    else:
        m.drum("door", 0, y, .075, .33, .035)
        bowl = y+.034
        for i in range((10, 8, 6)[m.lod]):
            a = i*2*math.pi/(10, 8, 6)[m.lod]
            m.drum("fieldstone", .343*math.sin(a), y, .075-.343*math.cos(a),
                   .095+.010*math.cos(3*a), .12+.023*math.sin(2*a), .077, n=(7, 5, 4)[m.lod])
    for turn in (-51, 53):
        with m.transform(translate=(0, bowl+.045, .075), rotate_y=turn):
            m.rod("timber", (-.291, -.003, 0), (.291, .009, 0), .042)
    for x,z,r,h,lean,role in ((-.103,.092,.112,.44,-.088,"forge"), (.05,.114,.153,.68,.115,"forge"),
                              (.087,-.021,.097,.39,-.035,"emissive")):
        flame(m, x, bowl+.064, z, r, h, lean, role)
    # Three backless benches leave the entrance side open toward the marsh path.
    for turn in (-65, 63, 163):
        with m.transform(rotate_y=turn):
            if m.lod < 2:
                for x in (-.188, .188):
                    m.slab("fieldstone", x, y, -.677, .122, .135, .161, .82)
            else:
                m.block("fieldstone", 0, y, -.677, .414, .135, .136)
            m.slab("wall" if classical else "roof", 0, y+.13, -.677, .566, .082, .196)
    if m.lod < 2:
        m.jar(.326, y, -.511, .079, "turquoise")
        if m.lod == 0:
            m.jar(.447, y, -.445, .056, "hide")
        m.slab("wall", -.35, y, -.526, .142, .22, .128, .85)
        m.disc(-.35, y+.236, -.533, .095, boss=m.lod==0)
    if classical:
        for x in (-.38,.38):
            m.block("wall", x, y, .539, .13, .355, .14)
            lamp(m, x, y+.355, .539, .073)


def fan(m, inner, outer, rx, rz, start, end, y, rise, rows, sectors, face, seat,
        center_z=0):
    """Radial terraces. The two cut faces end the seats cleanly at the aisle."""
    def point(radius, level, angle):
        a = math.radians(angle)
        return (rx*radius*math.sin(a), level, center_z-rz*radius*math.cos(a))
    for row in range(rows):
        r0 = inner+(outer-inner)*row/rows
        r1 = inner+(outer-inner)*(row+1)/rows
        low = y+rise*row/rows
        high = y+rise*(row+1)/rows
        for i in range(sectors):
            a, b = start+(end-start)*i/sectors, start+(end-start)*(i+1)/sectors
            m.face(face, (point(r0,low,a), point(r0,low,b), point(r0,high,b), point(r0,high,a)))
            m.face(seat, (point(r1,high,a), point(r0,high,a), point(r0,high,b), point(r1,high,b)))
            if row == rows-1:
                m.face(face, (point(r1,y,b), point(r1,y,a), point(r1,high,a), point(r1,high,b)))
        m.face(face, (point(r1,y,start), point(r0,y,start), point(r0,high,start), point(r1,high,start)))
        m.face(face, (point(r0,y,end), point(r1,y,end), point(r1,high,end), point(r0,high,end)))


def arena(m):
    # A dry sand court is reached through a complete break in the oval spectator bank.
    y = .072
    rx, rz = ((.97,.87), (.91,.96), (.98,.86))[m.era-1]
    with m.transform(scale=(rx, 1, rz)):
        m.drum("fieldstone", 0, 0, 0, 1, y, n=(24,16,10)[m.lod])
        m.drum("hide", 0, y, 0, .555, .013, n=(16,12,8)[m.lod])
    structure = ("wall", "brick", "glass")[m.era-1]
    seat = ("plaster", "wall", "ivory")[m.era-1]
    rows = (4, 3, 2)[m.lod]
    fan(m, .53, .987, rx, rz, 18, 342, y, .37, rows, (20,14,8)[m.lod], structure, seat)
    # The governor's raised loge has its own roof; the low south gate exposes the court.
    m.slab(structure, 0, y, .723, .54, .425, .251)
    if m.era == 1:
        for x in (-.227,.227):
            m.drum("ivory", x, .497, .745, .042, .209, n=(8,6,4)[m.lod])
        m.rooflet(0, .706, .745, .645, .329, .167, "roof")
    elif m.era == 2:
        for x in (-.198,.198):
            m.rod("metal", (x,.497,.781), (x,.765,.678), .031)
        with m.transform(translate=(0,.762,.683),rotate_x=12):
            m.slab("metal",0,0,0,.645,.043,.392,.99,underside=True)
    else:
        m.slab("glass",0,.497,.74,.48,.202,.204,.86)
        m.slab("ivory",0,.699,.74,.60,.061,.313,.91,underside=True)
        m.block("gold",0,.756,.74,.082,.121,.071)
        m.disc(0,.92,.734,.105,"gold",False)
    for s in (-1,1):
        x=s*rx*.31
        m.slab(structure,x,y,-rz*.88,.141,.45,.203,.77)
        m.disc(x,.462,-rz*.975,.061,"gold",False)
        if m.lod < 2:
            flag(m, x,.52,-rz*.876,.243,s)
        if m.lod==0:
            # The stepped exterior buttresses follow the radial load from the top row.
            for deg in (65,101,137):
                a=math.radians(deg*s)
                with m.transform(translate=(rx*.972*math.sin(a),y,-rz*.972*math.cos(a)),rotate_y=-deg*s):
                    m.block("trim",0,0,0,.074,.32,.061,.13)


def playfield(m):
    m.slab("grass",0,.079,.015,.805,.013,1.356,cut=.075)
    # Painted boundary, half line and the kickoff circle stay visible within the roof opening.
    y=.0923
    for x in (-.341,.341):
        m.block("ivory",x,y,.01,.016,.004,1.12)
    for z in (-.549,.01,.569):
        m.block("ivory",0,y,z,.665,.004,.017)
    if m.lod < 2:
        with m.transform(translate=(0,y+.006,.01),rotate_x=90):
            m.ring("ivory",0,0,-.003,.138,.119,.003,n=(14,9)[m.lod])


def stadium(m):
    y=.079
    m.slab("wall",0,0,0,1.89,y,1.92,.97,cut=.145)
    playfield(m)
    for side in (-1,1):
        # Paired cantilever stands leave a long and legible central playing field.
        with m.transform(scale=(side,1,1)):
            rows=(4,3,1)[m.lod]
            for i in range(rows):
                dx=.40/rows
                x=.401+dx*(i+.5)
                m.block("roof" if i%2 else "turquoise",x,y,.03,dx,.315*(i+1)/rows,1.34+.025*i)
            for z in ((-.53,0,.53) if m.lod==0 else (-.49,.49)):
                m.rod("metal",(.727,y,z),(.867,.677,z),.041)
                if m.lod<2:
                    m.rod("gold",(.835,.497,z),(.566,.67,z),.023)
            # Wide fascia is a folded section, bearing continuously on the outer columns.
            if m.era == 3:
                with m.transform(translate=(.69,.637,.026),rotate_z=8):
                    m.slab("glass",0,0,0,.447,.060,1.54,.92,cut=.21,underside=True)
                m.block("ivory",.475,.618,.032,.042,.064,1.30)
            else:
                with m.transform(translate=(.685,.648,.026),rotate_z=6):
                    m.slab("ivory",0,0,0,.437,.058,1.49,.97,cut=.09,underside=True)
            if m.lod==0:
                for z in (-.49,.49):
                    lamp(m,.829,.711,z,.062)
    # The sun clock doubles as the scoreboard, held on two stout piles.
    for x in (-.186,.186):
        m.block("trim",x,y,.81,.053,.337,.064)
    m.slab("metal",0,.408,.81,.552,.208,.106,cut=.07)
    m.block("door",0,.438,.752,.456,.141,.018)
    if m.lod<2:
        for x in (-.14,.14):
            m.block("emissive",x,.469,.74,.070,.064,.012)
    m.disc(0,.522,.731,.063,"gold",False)
    m.slab("wall",0,y,-.832,.41,.157,.196,.87)
    m.entry(0,y,-.930,.20,.148)


def curtain(m, x, y, z, h, side):
    # A tied-back cloth with alternating folds, pinched at its knot.
    xs = (0,.047,.116,.172)
    low=[(x+side*(u*.70),y+(i%2)*.019,z+(i%2)*.028) for i,u in enumerate(xs)]
    knot=[(x+side*(u*.29),y+h*.43,z-.027+(i%2)*.012) for i,u in enumerate(xs)]
    top=[(x+side*u,y+h,z+(i%2)*.035) for i,u in enumerate(xs)]
    for i in range(3):
        for a,b in ((low,knot),(knot,top)):
            pts=(a[i],b[i],b[i+1],a[i+1])
            if side<0:
                pts=pts[::-1]
            m.face("cloth",pts)
            m.face("cloth",[(u,v,w+.004) for u,v,w in pts[::-1]])


def terrace_steps(m, x, width, front, landing, base, summit, count):
    """One staircase skin with only the exposed portion of each riser."""
    left, right = x-width/2, x+width/2
    for i in range(count):
        z0 = front+(landing-front)*i/count
        z1 = front+(landing-front)*(i+1)/count
        low = base+(summit-base)*i/count
        high = base+(summit-base)*(i+1)/count
        m.face("wall", ((right,low,z0),(left,low,z0),(left,high,z0),(right,high,z0)))
        m.face("wall", ((left,high,z0),(left,high,z1),(right,high,z1),(right,high,z0)))
        for side in (left,right):
            cheek=((side,base,z0),(side,base,z1),(side,high,z1),(side,high,z0))
            m.face("wall",cheek if side==left else cheek[::-1])


def theater(m):
    stage=.217
    m.slab("fieldstone",0,0,0,1.84,.065,1.79,.97,cut=.16)
    m.slab("wall",0,.065,.389,1.46,stage-.065,.85,.97)
    terrace_steps(m,-.068,.313,-.188,.389-.85*.97/2,.065,stage,(4,3,2)[m.lod])
    m.slab("roof",0,stage,.726,1.305,.525,.116,.93)
    # A warm, dark backdrop optically separates the stage geometry from the seats.
    m.block("door",0,stage+.054,.656,.985,.399,.027)
    for s in (-1,1):
        m.slab("trim",s*.585,stage,.259,.128,.60,.146,.86)
        curtain(m,s*.549,stage+.048,.187,.525,-s)
    if m.era==1:
        m.rooflet(0,.813,.503,1.48,.66,.27,"roof")
    elif m.era==2:
        m.slab("brick",0,.812,.517,1.34,.135,.54,.88,underside=True)
        m.slab("metal",0,.947,.556,1.19,.111,.464,.65)
        m.block("gold",0,.848,.233,.775,.036,.041)
    else:
        m.slab("ivory",0,.814,.47,1.45,.105,.492,.85,underside=True)
        m.slab("glass",0,.919,.53,1.16,.246,.351,.59)
        m.block("ascension",0,.822,.212,1.03,.038,.031)
    m.disc(0,.593,.633,.126,"gold",boss=m.lod==0)
    # A theatrical sun-mask hangs from the front of the fly loft and keeps the
    # front elevation legible even while the roof obscures the backcloth.
    m.block("trim",0,.717,.221,.062,.115,.058)
    m.disc(0,.721,.202,.123,"gold",boss=True)
    # Splayed seating either side of the axial aisle. Each tier is a closed block.
    for s in (-1,1):
        with m.transform(translate=(s*.429,.065,-.41),rotate_y=s*12):
            seats=(3,3,2)[m.lod]
            for i in range(seats):
                depth=.56/seats
                m.slab("wall",0,0,.245-(i+.5)*depth,.592+.049*i,.23*(i+1)/seats,depth,
                       cut=.075 if m.lod==0 else 0)
                if m.lod<2:
                    m.block("timber",0,.23*(i+1)/seats,.245-(i+.46)*depth,
                            .558+.049*i,.029,.102)
    if m.lod==0:
        for s in (-1,1):
            lamp(m,s*.533,stage,-.007,.057)
        m.jar(.429,stage,.541,.068,"turquoise")


def loggia(m, x):
    h=.394
    m.slab("wall",x,.127,.39,.455,h,.30,.94)
    for s in (-1,1):
        m.drum("trim" if m.era==2 else "ivory",x+s*.158,.127,.046,.038,h,n=(8,6,4)[m.lod])
    m.slab("accent",x,.521,.26,.525,.056,.528,.96,underside=True)
    m.rooflet(x,.577,.279,.535,.546,(.154,.098,.121)[m.era-1],m.skin)


def forum(m):
    m.slab("wall",0,0,.027,1.94,.13,1.69,.96,cut=.102)
    for s in (-1,1):
        loggia(m,s*.671)
    m.slab("glass" if m.era==3 else "wall",0,.13,.384,.868,.502,.685)
    m.entry(0,.13,.0415,.227,.331)
    m.slab("trim",0,.632,.346,1.014,.094,.855,.97,underside=True)
    if m.era==1:
        m.rooflet(0,.726,.36,1.039,.876,.359,"roof")
        m.block("gold",0,1.117,.359,.105,.144,.099)
        m.disc(0,1.258,.355,.142)
    elif m.era==2:
        m.slab("brick",0,.726,.353,.802,.268,.637,.9)
        m.slab("glass",0,.994,.359,.69,.105,.51,.91)
        m.slab("metal",0,1.099,.348,.735,.083,.595,.74,underside=True)
        if m.lod<2:
            m.disc(0,.862,.054,.122)
    else:
        m.slab("glass",0,.726,.354,.76,.256,.626,.68)
        m.slab("ivory",0,.982,.354,.558,.075,.494,.88,underside=True)
        m.slab("gold",0,1.05,.354,.196,.152,.159,.78)
        m.ring("gold",0,1.364,.317,.191,.12,.079)
    for s in (-1,1):
        m.drum("ivory" if m.era!=2 else "metal",s*.331,.13,-.010,.059,.505,.045)
        if m.lod<2:
            m.drum("turquoise",s*.331,.567,-.01,.063,.067)
    # A lectern with a sloped tablet faces the assembly. The walkway stays open around it.
    m.slab("wall",0,.130,-.416,.496,.122,.356,.93)
    m.slab("turquoise",0,.252,-.443,.213,.147,.152,.76)
    with m.transform(translate=(0,.406,-.445),rotate_x=-14):
        m.block("gold",0,0,0,.236,.031,.178)
    terrace_steps(m,0,.478,-.976,.027-1.69*.96/2,0,.13,3)
    if m.lod < 2:
        with m.transform(translate=(0,.442,-.443),rotate_x=-14):
            m.block("ivory",0,0,0,.162,.016,.115)
            m.block("trim",0,.016,0,.010,.005,.101)
    if m.lod<2:
        for s in (-1,1):
            m.jar(s*.607,.13,-.493,.068,"hide")
            if m.lod==0:
                flag(m,s*.842,.129,-.296,.5,-s)


def tavern(m):
    # A broad brewhouse, its copper steeping kettle, and a shaded drinking porch.
    hero_z=.197
    m.slab("fieldstone",-.008,0,.019,1.83,.092,1.77,.96)
    m.slab("wall",-.185,.092,hero_z,1.095,.538,1.03,cut=.081)
    m.entry(-.265,.092,hero_z-.515,.262,.386)
    if m.era==1:
        m.rooflet(-.185,.63,hero_z,1.29,1.20,.454,"roof")
        m.slab("fieldstone",-.539,.859,.385,.158,.407,.181,.85)
        m.slab("trim",-.539,1.266,.385,.166,.046,.182)
    elif m.era==2:
        m.rooflet(-.185,.63,hero_z,1.27,1.19,.257,"metal")
        m.slab("brick",-.23,.912,.313,.60,.193,.383,.95)
        m.slab("trim",-.23,1.105,.313,.67,.062,.462,.88,underside=True)
    else:
        m.slab("ivory",-.185,.63,hero_z,1.259,.081,1.18,.92,underside=True)
        m.slab("glass",-.185,.711,hero_z,1.012,.278,.926,.68)
        m.slab("ivory",-.185,.989,hero_z,.738,.066,.678,.84,underside=True)
    for s in (-1,1):
        m.block("trim",-.184+s*.381,.092,-.641,.056,.375,.065)
    with m.transform(translate=(-.184,.491,-.483),rotate_x=-13):
        m.slab("turquoise",0,0,0,.941,.047,.445,underside=True)
        if m.lod < 2:
            for dx in (-.261,.261):
                m.block("thatch",dx,.0476,0,.050,.008,.435)
    # The kettle has a real neck, domed lid, and a side spout. It survives all LODs.
    m.drum("trim",.559,.092,.327,.237,.132)
    m.drum("hide" if m.era==1 else "metal",.559,.224,.327,.212,.259,.25)
    m.mound("gold",.559,.482,.327,.251,.173)
    m.drum("trim",.559,.632,.327,.092,.149,.074)
    m.drum("gold",.559,.781,.327,.088,.047)
    m.rod("gold",(.552,.389,.141),(.552,.341,-.045),.034)
    m.rod("trim",(.552,.356,-.04),(.552,.277,-.04),.030)
    if m.lod<2:
        m.jar(.547,.092,-.056,.089,"turquoise")
        m.block("trim",.233,.092,-.573,.050,.543,.047)
        m.block("trim",.318,.621,-.573,.217,.035,.052)
        m.block("gold",.396,.554,-.573,.022,.073,.024)
        m.disc(.396,.489,-.570,.086,"turquoise",False)
        if m.lod==0:
            lamp(m,-.497,.322,-.338,.059)
            for x in (-.53,.10):
                m.block("glass" if m.era==3 else "emissive",x,.334,-.327,.147,.126,.026)
            m.jar(-.66,.092,-.621,.066,"hide")


def garden(m,x,y,z,r):
    m.drum("ivory",x,y,z,r*.73,r*.52,r,n=(9,7,5)[m.lod])
    m.mound("leaf",x,y+r*.51,z,r*.84,r*.64,n=(9,7,5)[m.lod])


def arcology(m):
    # A city formed from inhabitable glazed terraces. Distinct gardens meet a low water court.
    m.slab("ivory",0,0,.07,1.93,.124,1.76,.96,cut=.18)
    m.slab("glass",0,.124,.193,1.56,.307,1.20,.88,cut=.19)
    m.slab("ivory",0,.431,.193,1.51,.088,1.178,.94,cut=.19,underside=True)
    levels=[(.519,0,.24,1.132,.307,.922),(.91,-.056,.28,.827,.315,.726),
            (1.309,-.119,.337,.567,.329,.47)]
    for y,x,z,w,h,d in levels:
        m.slab("glass",x,y,z,w,h,d,.82,cut=.18)
        m.slab("ivory",x,y+h,z,w*.938,.084,d*.95,.92,cut=.16,underside=True)
        if m.lod<2:
            for s in (-1,1):
                # The gilt mullions follow the rising setback of the glass face.
                m.rod("gold",(x+s*w*.263,y+.012,z-d*.499),
                      (x+s*w*.221,y+h*.99,z-d*.413),.011,n=4)
    m.slab("gold",-.12,1.722,.337,.201,.181,.166,.77)
    m.ring("gold",-.12,2.056,.303,.197,.134,.074)
    # Two inhabited outriggers with orchard decks; the crown gardens step upward towards the sun.
    for s in (-1,1):
        x=s*.648
        h=.486 if s<0 else .633
        m.slab("glass",x,.124,-.174,.398,h,.426,.78)
        m.slab("ivory",x,.124+h,-.174,.439,.073,.478,.85,underside=True)
        garden(m,x,.197+h,-.174,.119)
        if m.lod==0:
            garden(m,s*.637,.519,.231,.084)
    if m.lod<2:
        garden(m,.441,.91,.354,.084)
        garden(m,.233,1.309,.256,.073)
    m.slab("ivory",0,.124,-.414,.457,.27,.365)
    m.entry(0,.124,-.5965,.265,.231)
    m.stairs(0,0,-.96,.371,.17,.124)
    for s in (-1,1):
        m.slab("water",s*.483,.124,-.551,.371,.017,.222,cut=.24)
    if m.lod <2:
        for s in (-1,1):
            m.rod("gold",(s*.670,.235,-.324),(s*.459,.806,.106),.025)
            if m.lod==0:
                lamp(m,s*.269,.124,-.661,.048)


BUILDERS = {"Arcology":arcology, "Arena":arena, "Campfire":campfire,
            "Forum":forum, "Stadium":stadium, "Tavern":tavern, "Theater":theater}


def build(spec, lod):
    m=Delta(spec,lod)
    w,d=spec.footprint
    with m.transform(scale=(w/2,min(w,d)/2,d/2)):
        BUILDERS[spec.building_type](m)
    return m


if __name__=="__main__":
    generate(PACKAGE,build)
