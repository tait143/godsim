"""Public life in the dwarf holds. Builder session 6."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from generators.buildings.dwarf_towncenters import Stonework, publish_package


PACKAGE = "dwarf_culturecivic"


class Civic(Stonework):
    def port(self, x, y, z, w, h):
        """A shoulder-cut bronze surround and inset dark double leaf."""
        for role, width, height, offset in (
                ("accent", w, h, .026), ("door", w*.76, h*.85, .038)):
            a = width/2
            self.relief(role, [(x-a,y+.009),(x+a,y+.009),(x+a,y+height*.71),
                               (x+.6*a,y+height),(x-.6*a,y+height),(x-a,y+height*.71)],
                        z-offset, offset+.01, open_edges=(0,))
        if self.lod == 0:
            self.block("gold",x+.03*w,y+h*.39,z-.041,w*.16,h*.054,.016)

    def chevron(self, x, y, z, w, h, mat="gold"):
        points = [(x-w*.50,y+h*.49),(x,y-h*.12),(x+w*.50,y+h*.49),
                  (x+w*.50,y+h*.12),(x,y-h*.50),(x-w*.50,y+h*.12)]
        self.face(mat, [(px,py,z) for px,py in [points[i] for i in (0,1,4,5)]])
        self.face(mat, [(px,py,z) for px,py in [points[i] for i in (1,2,3,4)]])

    def standard(self, x, y, z, h, s=1):
        """A folded clan standard with a split fishtail, carried by an iron stave."""
        self.block("trim",x,y,z,.034,h,.035)
        self.block("gold",x+s*.097,y+h*.916,z+.003,.251,.031,.051)
        a,b,c = x+s*.023,x+s*.119,x+s*.209
        top=y+h*.921
        folds=[("banner",[(a,top,z-.018),(b,top,z-.034),(b,top-h*.29,z-.047),(a,top-h*.48,z-.028)]),
               ("cloth",[(b,top,z-.034),(c,top,z-.018),(c,top-h*.40,z-.019),(b,top-h*.29,z-.047)])]
        for mat,fold in folds:
            self.face(mat,fold if s>0 else fold[::-1])
            back=[(u,v,w+.009) for u,v,w in fold]
            self.face(mat,back[::-1] if s>0 else back)
        if self.lod == 0:
            self.drum("gold",x,y+h-.004,z,.027,.037,top=.012,n=6)

    def ring(self, x, z, start, sweep, count, profile, width=1, depth=1, canopy=False):
        """Revolve an open ordered cross-section from the inner floor out to the ground."""
        def point(index, radius, height):
            angle = math.radians(start+sweep*index/count)
            return (x+math.sin(angle)*radius*width,height,z-math.cos(angle)*radius*depth)
        # Moving clockwise in plan and outward along each riser/tread gives outward normals.
        for j in range(count):
            for (r,y,_),(R,Y,role) in zip(profile,profile[1:]):
                self.face(role,[point(j,r,y),point(j+1,r,y),point(j+1,R,Y),point(j,R,Y)])
        if sweep < 359:
            for j in (0,count):
                if canopy:
                    cap = [point(j,r,y) for r,y,_ in profile[:-1]]
                    self.face("metal",cap if j==count else cap[::-1])
                    continue
                # Wall ends run to ground. Split a non-convex stepped end into trapezoids.
                for (r,y,_),(R,Y,role) in zip(profile,profile[1:]):
                    if abs(r-R)<1e-7 or min(y,Y) < .01:
                        continue
                    end = [point(j,r,.001),point(j,R,.001),point(j,R,Y),point(j,r,y)]
                    self.face("fieldstone",end if j==count else end[::-1])

    def fire(self, x, y, z, r):
        self.drum("slate",x,y,z,r,.12*r,top=r*.86)
        for i in range((5,4,2)[self.lod]):
            angle=math.radians(360*i/5)
            dx,dz=r*math.cos(angle),r*math.sin(angle)
            self.beam("timber",(x+dx*.79,y+.12*r,z+dz*.79),(x-dx*.25,y+.69*r,z-dz*.25),r*.21)
        # Each fire tongue curls above a broader bank of embers. The golden core
        # nestles into the lower front of the flame rather than duplicating its outline.
        self.drum("forge",x,y+.097*r,z,r*.69,.36*r,top=r*.62)
        tongues=[(-.15,.15,.60,1.98,.48),(.28,-.06,.42,1.41,-.19),(-.24,-.27,.33,1.14,-.32)]
        for i,(dx,dz,rad,h,lean) in enumerate(tongues[:(3,2,1)[self.lod]]):
            role="emissive" if i==2 else "forge"
            sections=(6,5,4)[self.lod]
            def loop(k,height,off):
                return [(x+r*(dx+off+rad*k*math.cos(j*2*math.pi/sections)),
                         y+r*height,z+r*(dz+rad*k*.84*math.sin(j*2*math.pi/sections)))
                        for j in range(sections)]
            low=loop(1,.33,0)
            mid=loop(.64,h*.62,-lean*.17)
            tip=(x+r*(dx+lean),y+r*h,z+r*(dz+.09))
            for j in range(sections):
                k=(j+1)%sections
                self.face(role,(low[k],low[j],mid[j],mid[k]))
                self.face(role,(mid[k],mid[j],tip))


def camp(m):
    late=m.era==1
    # The ancient fire is ground-dug. The Classical civic hearth is raised above a hewn dais.
    h=.15 if late else .069
    m.drum("fieldstone" if late else "plaster",0,0,0,.86,h,top=.81,n=(12,9,7)[m.lod])
    if not late:
        m.drum("metal",0,h-.004,.005,.484,.076,top=.462,n=(12,9,6)[m.lod])
    m.drum("trim" if late else "slate",0,h-.008,.005,.35,.11,top=.40)
    if late:
        m.drum("gold",0,h+.09,.005,.407,.044,top=.399)
    m.fire(0,h+.109+(.024 if late else 0),.005,.35)
    # Carved individual listening benches, with an open approach from the south.
    seats=(5,4,2)[m.lod]
    for i in range(seats):
        a=math.radians(-111+222*i/(seats-1))
        with m.transform(translate=(.65*math.sin(a),h-.005,.65*math.cos(a)),
                         rotate_y=math.degrees(a)):
            m.block("fieldstone",0,0,0,.34,.14,.19,taper=.13)
            m.block("wall" if late else "roof",0,.131,0,.40,.079,.207,taper=.06)
            if not late and m.lod<2:
                m.block("metal",0,.196,.076,.39,.11,.063,taper=.07)
            if m.lod==0:
                m.block("accent",-.117,.203,0,.037,.016,.20)
    if m.lod<2:
        for s in (-1,1):
            with m.transform(translate=(s*.43,h-.004,-.38),rotate_y=s*19):
                m.block("trim",0,0,0,.14,.33 if late else .22,.14,taper=.18)
                m.chevron(0,.15 if late else .103,-.069,.093,.065)
        if late:
            m.standard(-.62,h,-.30,.58)
        elif m.lod==0:
            for i in range(3):
                m.beam("timber",(-.32+.018*i,h+.031,-.51-.046*i),(-.02+.017*i,h+.048,-.60-.047*i),.041)


def arena(m):
    future=m.era==3
    n=(22,13,10)[m.lod]
    rx=rz=.93
    start=19
    radius=(.45,.49,.49,.47)[m.era]
    height=(.38,.46,.50,.54)[m.era]
    tiers=(4,2,1)[m.lod]
    m.slab("slate",0,0,0,1.92,.049,1.85,.26)
    m.drum("soil",0,.045,0,radius*1.13,.017,n=n)
    profile=[(radius,.046,"slate")]
    for i in range(tiers):
        r=radius+(.87-radius)*i/tiers
        R=radius+(.87-radius)*(i+1)/tiers
        y=.09+height*(i+1)/tiers
        profile.extend(((r,y,"wall" if i%2==0 else "trim"),(R,y,"roof")))
    profile.extend(((.899,.09+height+.015,"gold"),(.936,.09+height-.025,"wall"),
                    (.958,.047,"wall")))
    m.ring(0,0,start,360-2*start,n,profile,rx,rz)
    # A full-height projecting ticket gate frames the southern entrance gap.
    gate=.09+height*.65
    for s in (-1,1):
        m.block("fieldstone",s*.177,.046,-rz*.803,.115,gate,.22,taper=.10)
    m.block("accent",0,gate+.04,-rz*.81,.50,.064,.251)
    if m.lod<2:
        m.standard(-.57,.04,-.53*rz,.78)
        m.standard(.57,.04,-.53*rz,.79,-1)
        m.chevron(0,gate+.074,-rz*.81-.126,.15,.049)
    # The competition stone and judges' dais remain clear of the public gate.
    m.drum("slate",0,.059,0,.263,.049,top=.249,n=(10,8,5)[m.lod])
    if m.lod<2:
        with m.transform(translate=(0,.106,0),rotate_y=45):
            m.block("gold",0,0,0,.173,.012,.173)
        m.drum("trim",0,.055,.395,.133,.124,top=.111,n=(8,6)[m.lod])
    if m.lod<2:
        # Tangential bays break up the retaining wall. Their chiseled tops repeat the
        # portal's clipped corners. The glazed upper clerestory succeeds the old stone arches.
        bays=(1,3,5,7,10,13,15,17,19,21) if m.lod==0 else (1,3,6,9,11)
        for index in bays:
            a=math.radians(start+(360-2*start)*(index+.5)/n)
            r=.955*rx*math.cos(math.radians((360-2*start)/(2*n)))
            with m.transform(translate=(r*math.sin(a),.048,-r*math.cos(a)),
                             rotate_y=-math.degrees(a)):
                w=.082 if m.era<3 else .103
                h=height*(.64 if m.era<3 else .55)
                m.relief("trim",[(-w,0),(w,0),(w,h*.74),(w*.53,h),(-w*.53,h),(-w,h*.74)],
                         -.012,.042,open_edges=(0,))
                m.relief("glass" if m.era==3 else "door",
                         [(-w*.64,.017),(w*.64,.017),(w*.64,h*.69),
                          (w*.32,h*.86),(-w*.32,h*.86),(-w*.64,h*.69)],
                         -.019,.018,open_edges=(0,))
    if m.era>=2:
        # A faceted roof caps only the north stand; piers lie behind the seat backs.
        span=105 if future else 68
        for a in (-span*.45,0,span*.45):
            angle=math.radians(180+a)
            x,z=math.sin(angle)*.90*rx,-math.cos(angle)*.90*rz
            m.block("metal",x,height+.052,z,.070,.257,.068)
        y=height+.30
        m.ring(0,0,180-span/2,span,(8,5,3)[m.lod],
               [(.68,y+.05,"trim"),(.68,y+.097,"accent"),
                (.96,y,"glass" if future else "roof"),(.97,y-.056,"metal"),(.68,y+.05,"metal")],rx,rz,canopy=True)
    if m.lod==0:
        for index in (2,6,14,18):
            angle=start+(360-2*start)*(index+.5)/n
            a=math.radians(angle)
            x,z=math.sin(a)*.943*rx,-math.cos(a)*.943*rz
            with m.transform(translate=(x,.048,z),rotate_y=-angle):
                m.block("fieldstone",0,0,0,.105,height*.88,.116,taper=.36)


def stadium(m):
    """Two grandstands share an open rectangular pitch with clear sight lines."""
    if m.era==3:
        return cup_stadium(m)
    m.slab("fieldstone",0,0,0,1.91,.089,1.63,.112)
    m.slab("grass",0,.085,-.033,1.53,.027,.65,.051)
    if m.lod<2:
        m.block("ivory",0,.111,-.034,.018,.005,.612)
        # The centre circle and goals make the pitch distinct from the arena's duelling floor.
        m.ring(0,-.034,0,360,(12,8)[m.lod],[(.11,.118,"ivory"),(.121,.118,"ivory")])
        for s in (-1,1):
            for t in (-1,1):
                m.block("ivory",s*.675,.111,-.033+t*.143,.020,.111,.022)
            m.block("ivory",s*.675,.219,-.033,.024,.019,.306)
    for side in (-1,1):
        blocks=[(-.51,.64),(.51,.64)] if side==-1 else [(0,1.72)]
        row_count=(4,2,1)[m.lod]
        for x,width in blocks:
            for j in range(row_count):
                t=(j+1)/row_count
                z=side*(.34+.40*(j+.5)/row_count)
                y=.10+t*(.42 if side==1 else .25)
                d=.40/row_count
                m.block("wall" if j%2==0 else "metal",x,.086+j*.0009,z,width,y-.086,d)
                m.block("banner" if j%2==0 else "roof",x,y-.003,z,width+.008,.027,d*.971)
    # The roof slopes towards the spectator tiers. Heavy raking columns behind the
    # last row carry the canopy. The roof covers one side, so the pitch stays visible.
    canopy=.703
    for x in (-.69,.69):
        m.beam("metal",(x,.08,.746),(x*.94,canopy+.025,.624),.095)
    with m.transform(translate=(0,canopy,.511),rotate_x=-7):
        m.slab("roof",0,-.038,0,1.85,.085,.38,.11)
        m.block("gold",0,-.007,-.190,1.64,.037,.038)
    # A monumental clipped arch and a bronze clan hammer mark the ticket aisle.
    m.slab("fieldstone",0,0,-.682,.81,.096,.41,.12)
    for hand in (-1,1):
        x=hand*.234
        m.slab("wall",x,.092,-.713,.196,.329,.226,.12,taper=.13)
        if m.lod==0:
            m.slab("metal",x,.385,-.711,.200,.073,.233,.11)
    m.slab("wall",0,.414,-.705,.731,.099,.274,.16)
    m.vault("roof",0,.501,-.705,.678,.160,.267)
    if m.lod<2:
        m.relief("gold",[(-.155,.561),(0,.453),(.155,.561),(.122,.682),(-.122,.682)],-.856,.088)
        if m.lod==0:
            m.relief("door",[(-.115,.567),(0,.487),(.115,.568),(.094,.658),(-.094,.658)],-.862,.028)
        m.block("ivory",0,.521,-.865,.038,.100,.017)
        m.block("gold",.001,.606,-.872,.140,.040,.022)
    for s in (-1,1):
        x,z=s*.822,(-.325 if s==1 else .289)
        h=.52
        m.beam("metal",(x,.085,z),(x+s*.019,h,z+s*.031),.046)
        m.block("trim",x+s*.019,h-.009,z+s*.031,.214,.095,.067)
        if m.lod<2:
            for i in (-1,0,1):
                m.block("emissive",x+s*.019+i*.065,h+.016,z+s*.031-.034,.048,.049,.014)
    if m.lod==0:
        for x in (-.55,.55):
            m.slab("wall",x,.088,-.752,.143,.223,.063,.12,taper=.11)
            m.chevron(x,.226,-.785,.094,.061)
        m.standard(.89,.084,.091,.49,-1)


def cup_stadium(m):
    """An enclosed pitch bowl with a split cantilever above its end stands."""
    m.slab("fieldstone",0,0,.01,1.93,.105,1.73,.23)
    m.slab("grass",0,.097,-.018,1.36,.026,.65,.16)
    m.block("ivory",0,.121,-.018,.022,.009,.528)
    m.ring(0,-.018,0,360,(14,9,6)[m.lod],[(.089,.131,"ivory"),(.104,.131,"ivory")])
    for s in (-1,1):
        for t in (-1,1):
            m.block("ivory",s*.531,.121,-.018+t*.11,.023,.155,.025)
        m.block("ivory",s*.531,.272,-.018,.027,.026,.246)
    # Split the bowl at the four access aisles. The two main stands wrap the goals.
    sections=((48,84),(139,82),(228,84),(319,82))
    for j,(start,sweep) in enumerate(sections):
        tiers=(4,3,1)[m.lod]
        profile=[(.606,.096,"wall")]
        for i in range(tiers):
            radius=.606+i*.335/tiers
            y=.142+(i+1)*(.30 if j%2==0 else .22)/tiers
            profile.extend(((radius,y,"wall"),(radius+.335/tiers,y,"banner" if i%2==0 else "roof")))
        profile.append((.952,.091,"ivory"))
        m.ring(0,0,start,sweep,(5,3,2)[m.lod],profile,width=.98,depth=.808)
    # Two upswept ring sectors make the split roof visible across the pitch.
    for side in (-1,1):
        for z in (-.37,.37):
            m.beam("wall",(side*.792,.103,z),(side*.789,.674,z*.81),.095)
        start=58 if side>0 else 238
        profile=[(.617,.529,"metal"),(.638,.615,"accent"),(.993,.748,"glass"),
                 (.969,.657,"ivory"),(.617,.529,"metal")]
        m.ring(0,0,start,64,(5,3,2)[m.lod],profile,width=.988,depth=.88,canopy=True)
    # The suspended score panel faces the grandstands above the north aisle.
    for x in (-.34,.34):
        m.beam("metal",(x,.11,.661),(x*.89,.699,.605),.068)
    m.slab("trim",0,.548,.607,.76,.297,.111,.11)
    m.block("glass",0,.588,.546,.598,.188,.026)
    if m.lod<2:
        for s in (-1,1):
            m.block("ascension",s*.138,.648,.529,.124,.054,.015)
        m.slab("wall",0,.105,-.707,.55,.142,.235,.12)
        m.chevron(0,.203,-.831,.211,.067)
    if m.lod==0:
        for s in (-1,1):
            m.standard(s*.64,.101,-.503,.453,-s)
            for z in (-.233,0,.241):
                m.slab("metal",s*.902,.359,z,.089,.095,.128,.11)


def theater(m):
    era=m.era
    if era==2:
        return fly_tower_theater(m)
    if era==3:
        return shell_theater(m)
    m.slab("fieldstone",0,0,0,1.83,.103,1.76,.15)
    # Stage and fly gallery face the sunken audience, with a full-height back wall.
    m.slab("wall",0,.099,.42,1.50,.20,.72,.09)
    m.slab("timber",0,.288,.399,1.54,.053,.77,.053)
    gallery=(.83,.83,.94,1.04)[era]
    m.slab("trim",0,.335,.65,1.38,gallery-.335,.152,.07)
    if m.lod<2:
        m.block("door",0,.348,.556,1.08,gallery-.37,.049)
    for s in (-1,1):
        m.slab("wall",s*.689,.306,.428,.205,gallery-.28,.43,.13,taper=.13)
        # Drapes descend in angled planes, with a diagonal pale tie at the waist.
        for j in range((4,2,1)[m.lod]):
            x=s*(.47+j*.063)
            width=.099 if m.lod==0 else .13
            m.relief("cloth" if j%2==0 else "banner",
                     [(x-width*.38,.351),(x+width*.38,.351),
                      (x+width*.54,gallery),(x-width*.54,gallery)],.322-j*.019,.044)
        if m.lod<2:
            m.block("gold",s*.52,.545,.285,.182,.034,.056)
    m.vault("roof" if era<3 else "glass",0,gallery-.008,.46,1.67,
            (.11,.24,.14,.19)[era],.57 if era<3 else .68)
    if m.lod<2:
        m.chevron(0,gallery+.074,.46-(.57 if era<3 else .68)/2-.002,.24,.091)
    if era==2:
        m.stack(-.61,.62,.79,.073,.43,"brick")
    elif era==3:
        for s in (-1,1):
            m.slab("accent",s*.56,gallery+.119,.44,.097,.142,.29,.18)
    if m.lod<2:
        # A bronze comedy mask crowns the proscenium, visible above its folded curtains.
        mask_y=gallery+.113
        mask_z=.46-(.57 if era<3 else .68)/2-.015
        m.relief("gold",[(-.132,mask_y+.091),(-.037,mask_y+.116),(.035,mask_y+.115),
                         (.132,mask_y+.096),(.105,mask_y-.031),(0,mask_y-.116),
                         (-.103,mask_y-.032)][::-1],mask_z,.056)
        for s in (-1,1):
            m.block("door",s*.059,mask_y+.025,mask_z-.004,.054,.024,.021)
        m.chevron(0,mask_y-.037,mask_z-.006,.088,.035,"door")
    if m.lod==0:
        m.slab("slate",-.253,.102,.010,.236,.072,.230,.09)
        m.slab("timber",-.253,.168,.073,.239,.078,.135,.09)
        m.slab("timber",-.253,.239,.130,.240,.071,.100,.09)
    rows=(3,2,1)[m.lod]
    # Horseshoe shaped rows around the apron, with a broad central entry aisle.
    for j in range(rows):
        r=.39+.235*j/(max(1,rows-1))
        y=.110+.055*j
        for side in (-1,1):
            angle=22 if side==1 else -83
            profile=[(r,.101,"trim"),(r,y+.105,"wall"),(r+.083,y+.11,"timber"),
                     (r+.097,.102,"fieldstone")]
            m.ring(0,.04,angle,61,(5,3,2)[m.lod],profile)
    if m.lod==0:
        for s in (-1,1):
            m.lamp(s*.527,.341,.075,.080)


def fly_tower_theater(m):
    # A tall brick fly house hoists scenery over a wide thrust stage.
    m.slab("fieldstone",0,0,0,1.89,.111,1.76,.14)
    m.slab("brick",-.157,.105,.518,1.182,1.025,.497,.09)
    m.vault("metal",-.157,1.123,.518,1.283,.197,.59)
    m.slab("timber",-.145,.106,.142,1.161,.213,.642,.09)
    m.block("door",-.157,.313,.259,.927,.674,.044)
    for side in (-1,1):
        x=-.157+side*.48
        m.block("banner",x,.315,.227,.174,.658,.071,taper=-.10)
        if m.lod==0:
            m.block("gold",x,.553,.199,.171,.045,.029)
    m.block("trim",-.157,.947,.215,1.129,.095,.112)
    # The third bay is the loading wing; patrons enter along the centre aisle.
    m.slab("brick",.647,.108,.335,.431,.521,.676,.09)
    m.vault("roof",.647,.623,.335,.49,.118,.727)
    m.port(.647,.114,-.008,.258,.369)
    for side in (-1,1):
        for i in range((3,2,1)[m.lod]):
            z=-.266-i*.191
            h=.147+i*.088
            m.slab("wall",side*.403,.103+i*.009,z,.57,h,.159,.095)
            m.block("banner",side*.403,.099+i*.009+h,z,.55,.043,.126)
    if m.lod<2:
        m.chevron(-.157,1.165,.219,.279,.122)
        for x in (-.593,.274):
            m.lamp(x,.318,-.058,.084)
    if m.lod==0:
        for x in (-.59,-.303,-.017,.267):
            m.block("glass",x,1.055,.264,.139,.052,.034)
        m.standard(.772,.111,-.417,.67,-1)


def shell_theater(m):
    # Open acoustic shell: three cantilevered panels lean forward over a circular thrust stage.
    m.slab("fieldstone",0,0,.01,1.91,.113,1.78,.24)
    m.drum("wall",0,.107,.232,.496,.18,.541)
    m.drum("timber",0,.282,.232,.547,.069,.526)
    for x,z,angle,h in ((-.521,.346,-35,.688),(0,.625,0,.963),(.521,.346,35,.688)):
        with m.transform(translate=(x,.112,z),rotate_y=angle):
            m.slab("trim",0,0,.035,.303,.281,.312,.16)
            m.beam("metal",(0,.249,.058),(0,.93*h,.112),.133)
            with m.transform(translate=(0,.62*h,-.008),rotate_x=-17):
                m.relief("ivory",[(-.164,-h*.34),(.164,-h*.34),(.319,h*.251),
                                  (.239,h*.454),(-.239,h*.454),(-.319,h*.251)],-.061,.133)
                if m.lod<2:
                    m.chevron(0,h*.243,-.065,.313,.137,"accent")
    # Every tier has a clear south entrance and faces the projecting stage apron.
    for side in (-1,1):
        sector=15 if side==1 else -84
        steps=(3,2,1)[m.lod]
        profile=[(.52,.107,"trim")]
        for i in range(steps):
            r=.52+i*.338/steps
            y=.143+(i+1)*.212/steps
            profile.extend(((r,y,"wall"),(r+.338/steps,y,"banner" if i%2==0 else "roof")))
        profile.append((.867,.101,"metal"))
        m.ring(0,.033,sector,69,(6,4,2)[m.lod],profile,width=1.025,depth=.932)
    if m.lod==0:
        for x in (-.741,.741):
            m.lamp(x,.113,.09,.126)
        m.block("gold",0,.315,-.311,.225,.023,.028)


def tavern(m):
    era=m.era
    if era==3:
        return tap_court(m)
    m.slab("fieldstone",0,0,.035,1.78,.125,1.58,.115)
    m.slab("wall",-.12,.118,.084,1.38,.54,1.19,.10)
    m.vault("roof" if era==1 else "metal" if era==2 else "glass",
            -.12,.632,.096,1.54,(.1,.32,.26,.22)[era],1.34)
    if era>=2:
        m.slab("glass" if era==3 else "brick",-.15,.805,.14,.79,.15,.60,.11)
        m.slab("roof",-.15,.939,.14,.84,.064,.65,.12)
    m.port(-.26,.127,-.510,.46,.48)
    # The right taproom advertises itself by the huge coopered tun lodged in an iron cradle.
    m.slab("trim",.58,.113,.066,.50,.39,.84,.145)
    r=.28 if era==1 else .30
    with m.transform(translate=(.575,.560,.466),rotate_x=-90):
        length=.97 if era==3 else .90
        m.drum("timber" if era<3 else "accent",0,0,0,r*.85,length*.20,top=r)
        m.drum("timber" if era<3 else "metal",0,length*.199,0,r,length*.60)
        m.drum("timber" if era<3 else "accent",0,length*.798,0,r,length*.203,top=r*.85)
        if m.lod<2:
            for p in (.23,.76):
                m.drum("gold",0,length*p,0,r*1.025,.041)
            m.drum("trim",0,length*.999,0,r*.72,.018)
    m.stack(-.66,.53,.337,.115,(.1,.65,.84,.71)[era],"brick" if era!=3 else "metal")
    if m.lod<2:
        m.block("trim",.566,.487,-.480,.057,.163,.087)
        m.block("gold",.566,.461,-.532,.091,.052,.061)
        for x in (-.61,.105):
            m.block("trim",x,.299,-.520,.16,.195,.049)
            m.block("emissive",x,.322,-.550,.119,.147,.021)
        # A projecting inn sign with a deliberately heavy tankard motif.
        m.beam("trim",(-.69,.68,-.504),(-.69,.68,-.756),.044)
        m.beam("gold",(-.69,.675,-.722),(-.69,.59,-.722),.024)
        m.slab("banner",-.69,.39,-.722,.176,.214,.052,.13)
        m.relief("ivory",[(-.748,.450),(-.659,.450),(-.649,.533),(-.756,.533)],-.752,.021)
        m.block("gold",-.637,.469,-.753,.025,.050,.017)
        m.block("forge",-.707,.525,-.755,.102,.019,.019)
    if m.lod==0:
        m.slab("slate",-.265,.107,-.583,.473,.053,.17,.1)
        m.slab("timber",.12,.123,-.576,.231,.102,.111,.06)
        m.slab("grass",.12,.222,-.575,.211,.050,.091,.06)


def tap_court(m):
    m.slab("fieldstone",0,0,.012,1.86,.117,1.70,.21)
    # A bridge connects the pair of brewing tanks with the taproom around a recessed beer garden.
    for x,z,r,h in ((-.57,.24,.265,.573),(-.192,.555,.197,.719)):
        m.drum("trim",x,.113,z,r,.109,.87*r)
        m.drum("accent",x,.215,z,r*.88,h,r*.95)
        m.dome((x,.21+h,z),r*.98,m.color("gold"),rings=(4,2,1)[m.lod],
               segments=(12,8,5)[m.lod],squash=.80,material="gold")
        if m.lod==0:
            m.drum("metal",x,.284+h*.51,z,r*.958,.038)
    m.slab("wall",.459,.112,.258,.67,.581,.916,.15,.08)
    m.slab("glass",.459,.689,.258,.642,.168,.861,.16)
    m.vault("ivory",.459,.852,.258,.758,.163,.966)
    m.port(.459,.119,-.204,.347,.395)
    m.slab("metal",.019,.572,.455,.64,.100,.292,.075)
    if m.lod<2:
        for z in (.331,.573):
            m.block("gold",.03,.717,z,.64,.035,.034)
            for x in (-.209,.237):
                m.block("metal",x,.663,z,.028,.066,.03)
    # The front court has circular communal tables. Individual taps project from the tank wall.
    for x,z in ((-.32,-.38),(.207,-.55))[:1 if m.lod==2 else 2]:
        m.drum("trim",x,.115,z,.071,.131,.054)
        m.drum("timber",x,.239,z,.198,.051,.188)
        if m.lod==0:
            for dx in (-.239,.239):
                m.drum("wall",x+dx,.116,z,.069,.092)
    if m.lod<2:
        m.block("trim",-.575,.508,-.008,.133,.104,.158)
        m.drum("gold",-.575,.423,-.077,.044,.099)
    if m.lod==0:
        m.relief("gold",[(.349,.746),(.478,.746),(.498,.842),(.335,.842)],-.181,.032)
        m.block("gold",.517,.773,-.172,.043,.045,.035)


def forum(m):
    era=m.era
    if era==2:
        return assembly_exchange(m)
    if era==3:
        return council_rotunda(m)
    m.slab("fieldstone",0,0,0,1.87,.108,1.78,.17)
    m.slab("wall",0,.101,.255,1.54,.60,.98,.125)
    if era==1:
        m.vault("roof",0,.679,.259,1.70,.268,1.10)
    elif era==2:
        m.slab("roof",0,.688,.269,1.72,.092,1.11,.125)
        m.vault("brick",.15,.771,.38,1.10,.228,.63)
    else:
        m.slab("glass",0,.677,.273,1.63,.162,1.047,.13)
        m.slab("wall",0,.832,.273,1.69,.082,1.105,.13)
        m.vault("glass",-.106,.902,.337,1.15,.199,.72)
    # Stone portico / long speaking terrace, supported across the public forecourt.
    m.slab("slate",0,.103,-.382,1.46,.098,.64,.105)
    top=.718 if era<3 else .759
    for s in (-1,1):
        for v in (.27,.57) if m.lod<2 else (.48,):
            x=s*v
            m.slab("fieldstone",x,.192,-.504,.136,.098,.156,.14)
            m.slab("wall" if era==1 else "metal",x,.282,-.504,.107,top-.27,.126,.19,taper=.13)
            if m.lod==0:
                m.slab("gold",x,top-.045,-.504,.145,.052,.166,.13)
    m.vault("roof" if era!=3 else "glass",0,top-.009,-.409,1.45,.128,.531)
    # The council seal is cast into a stone pediment at the front of the main roof.
    seal_y=top+.106
    m.relief("trim",[(-.159,seal_y-.061),(.159,seal_y-.061),(.112,seal_y+.072),
                     (0,seal_y+.123),(-.112,seal_y+.072)],-.688,.087)
    m.chevron(0,seal_y+.025,-.690,.165,.109)
    m.port(0,.204,-.233,.43,.425)
    # Stepped approach, a clan lectern and a standing tablet describing the assembly.
    m.slab("wall",.0,.100,-.728,.56,.043,.198,.11)
    m.slab("fieldstone",0,.142,-.649,.53,.056,.164,.11)
    if m.lod<2:
        m.slab("accent",.44,.193,-.581,.128,.184,.113,.18,taper=.14)
        with m.transform(translate=(.441,.388,-.583),rotate_x=-17):
            m.block("ivory",0,-.018,0,.195,.037,.138)
        m.slab("trim",-.745,.106,-.329,.159,.287,.097,.115)
        m.chevron(-.745,.296,-.38,.110,.085)
    if m.lod==0:
        for s in (-1,1):
            m.lamp(s*.75,.111,-.542,.09)
            for z in (.14,.406):
                m.block("glass" if era>=2 else "trim",s*.771,.32,z,.025,.161,.12)


def assembly_exchange(m):
    # Union assembly hall. Long brick wings frame a covered iron-and-glass exchange.
    m.slab("fieldstone",0,0,.012,1.91,.12,1.73,.11)
    for side in (-1,1):
        x=side*.582
        m.slab("brick",x,.114,.138,.501,.678,1.145,.10)
        m.vault("roof",x,.787,.138,.551,.199,1.218)
        if m.lod<2:
            m.port(x,.125,-.431,.288,.366)
        if m.lod==0:
            for z in (-.144,.156,.456):
                m.block("glass",side*.837,.429,z,.024,.231,.152)
    m.slab("wall",0,.113,.556,.783,.487,.371,.12)
    m.port(0,.122,.368,.462,.377)
    m.slab("slate",0,.109,-.184,.765,.064,1.088,.09)
    m.vault("glass",0,.706,.224,.819,.391,.95)
    for z in (-.224,.659):
        m.vault("metal",0,.709,z,.844,.414,.07)
        if m.lod<2:
            for s in (-1,1):
                m.beam("metal",(s*.35,.715,z),(s*.212,1.083,z),.044)
    # The public clock belongs to the street-facing corner tower.
    m.slab("brick",-.593,.706,-.288,.367,.565,.381,.107)
    m.vault("metal",-.593,1.265,-.288,.431,.17,.451)
    with m.transform(translate=(-.593,1.092,-.471),rotate_x=-90):
        m.drum("gold",0,0,0,.126,.056)
        m.drum("ivory",0,.05,0,.103,.024)
    if m.lod<2:
        m.beam("trim",(-.593,1.095,-.548),(-.593,1.162,-.548),.017)
        m.beam("trim",(-.593,1.095,-.553),(-.546,1.079,-.553),.016)
        m.standard(.70,.117,-.621,.66,-1)
    if m.lod==0:
        for side in (-1,1):
            m.lamp(side*.32,.174,-.626,.094)
        m.chevron(0,.922,-.269,.178,.073)


def council_rotunda(m):
    # Radial assembly chamber with a daylight oculus and wing ramps enclosing the forum plaza.
    m.slab("ivory",0,0,0,1.90,.137,1.76,.235)
    for role,y,r,h,top in (("wall",.131,.661,.384,.623),("glass",.511,.627,.23,.65),
                            ("ivory",.736,.719,.108,.658),("glass",.839,.476,.161,.389)):
        m.drum(role,0,y,.216,r,h,top,n=(16,10,7)[m.lod])
    m.drum("gold",0,.994,.216,.42,.077,.375)
    m.drum("ascension",0,1.067,.216,.32,.044,.258)
    m.slab("wall",0,.129,-.345,.607,.380,.433,.137,.065)
    m.vault("metal",0,.506,-.345,.676,.125,.498)
    m.port(0,.14,-.565,.373,.327)
    for side in (-1,1):
        x=side*.711
        m.slab("wall",x,.13,-.231,.216,.153,.71,.17)
        m.beam("ivory",(x,.225,-.478),(side*.617,.772,.10),.142)
        if m.lod<2:
            m.slab("trim",side*.48,.132,-.552,.195,.231,.167,.15,.11)
            m.lamp(side*.48,.359,-.552,.084)
    if m.lod==0:
        for i in range(9):
            a=.18+i*math.tau/9
            m.beam("gold",(.625*math.sin(a),.51,.216+.625*math.cos(a)),
                   (.65*math.sin(a),.741,.216+.65*math.cos(a)),.039)
        m.chevron(0,.556,-.597,.149,.068)


def arcology(m):
    m.slab("fieldstone",0,0,.012,1.88,.13,1.82,.18)
    # Inhabited terraces climb toward an open light gate. Each tier contains a usable garden ledge.
    for x,z,w,d,h in ((-.48,.18,.67,.97,1.14),(.41,.066,.74,1.12,1.38)):
        tiers=(4,3,2)[m.lod]
        for i in range(tiers):
            t=i/tiers
            y=.126+t*h
            width=w*(1-.40*t)
            depth=d*(1-.50*t)
            dy=h/tiers
            m.slab("wall",x,y,z,width,dy*.48,depth,.17,taper=.05)
            m.slab("glass",x,y+dy*.468,z,width*.963,dy*.445,depth*.93,.14)
            m.slab("ivory",x,y+dy*.908,z,width*1.035,dy*.11,depth*1.02,.15)
            if m.lod==0:
                m.slab("grass",x-width*.23,y+dy*1.009,z-depth*.344,
                       width*.42,.024,depth*.227,.17)
        m.slab("gold",x,.123+h,z,w*.57,.088,d*.53,.15,taper=.17)
        if m.lod<2:
            for i in range(tiers):
                t=i/tiers
                radius=w*(1-.40*t)*.5
                front=z-d*(1-.50*t)*.494
                for j in (-1,0,1):
                    m.block("metal",x+radius*j*.48,.126+(i+.51)*h/tiers,front,
                            .023,h/tiers*.31,.027)
    # A roof-mounted lantern nested inside twin prongs gives the highest terrace a public identity.
    for s in (-1,1):
        m.beam("ivory",(.41+s*.141,1.571,.067),(.41+s*.094,1.807,.065),.063)
    m.drum("gold",.41,1.569,.066,.098,.048,top=.068)
    m.drum("ascension",.41,1.614,.066,.067,.131,top=.088)
    m.drum("gold",.41,1.740,.066,.094,.052,top=.050)
    # Multiple floors support a high inhabited bridge, and a sunlit channel runs underneath.
    m.slab("glass",-.021,.86,.15,.78,.165,.31,.13)
    m.slab("gold",-.021,1.016,.15,.77,.034,.317,.15)
    m.slab("wall",-.06,.121,-.38,.70,.46,.53,.16,taper=.09)
    m.vault("accent",-.06,.567,-.379,.72,.128,.564)
    m.port(-.06,.131,-.649,.35,.387)
    if m.lod<2:
        m.slab("water",.497,.131,-.629,.40,.025,.32,.24)
        m.slab("ivory",.509,.151,-.611,.130,.193,.087,.13)
        m.drum("ascension",.509,.339,-.61,.068,.066,top=.033)
        m.chevron(-.06,.619,-.663,.17,.061)
    if m.lod==0:
        for x,z in ((-.71,-.41),(-.65,.63),(.78,.34)):
            m.drum("trim",x,.121,z,.107,.061,top=.115,n=7)
            m.dome((x,.177,z),.092,m.color("leaf"),rings=2,segments=7,material="leaf")


def build(spec, lod):
    m=Civic(spec,lod)
    width,depth=spec.footprint
    with m.transform(scale=(width/2,min(width,depth)/2,depth/2)):
        {"Arena":arena,"Stadium":stadium,"Campfire":camp,"Theater":theater,
         "Forum":forum,"Tavern":tavern,"Arcology":arcology}[spec.building_type](m)
    return m


if __name__ == "__main__":
    publish_package(PACKAGE,build)
