"""The Grand Cathedral of the Two Bells, the Human racial wonder."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.human_housesclassical import MasonryHouse


class Cathedral(MasonryHouse):
    def lancet(self,x,z,base,w,h,glass='glass',frame='ivory',mullion=True):
        """Pointed glazing or joinery set into a pale carved surround."""
        def arch(radius,foot,peak,plane,mat):
            shoulder=peak-radius*1.36
            points=[(x+radius,foot),(x-radius,foot),(x-radius,shoulder)]
            if self.lod<2:
                points += [(x-radius*.86,shoulder+radius*.57)]
            points += [(x,peak)]
            if self.lod<2:
                points += [(x+radius*.86,shoulder+radius*.57)]
            points += [(x+radius,shoulder)]
            self.face(mat,[(px,py,plane) for px,py in points])
        arch(w*.58,base-.013,base+h+.022,z,frame)
        arch(w*.46,base,base+h-.010,z-.006,glass)
        if self.lod==0 and mullion:
            self.rect('ivory',x,base+h*.41,z-.011,w*.074,h*.82)
            self.rect('gold',x,base+h*.49,z-.012,w*.85,.014)

    def medallion(self,x,y,z,r):
        # Eight barley grains around a sun, in blue and amber stained glass.
        n=(20,12,8)[self.lod]
        def disc(mat,rad,plane):
            self.face(mat,[(x+rad*math.cos(-i*2*math.pi/n),
                            y+rad*math.sin(-i*2*math.pi/n),plane) for i in range(n)])
        disc('ivory',r,z)
        disc('gold',r*.90,z-.006)
        disc('glass',r*.81,z-.012)
        if self.lod<2:
            for i in range(8):
                a=2*math.pi*i/8
                ux,uy=math.cos(a),math.sin(a)
                def petal(rad,side):
                    return (x+r*(rad*ux-side*uy),y+r*(rad*uy+side*ux),z-.019)
                self.face('emissive' if i%2==0 else 'banner',
                          [petal(.18,0),petal(.53,.107),petal(.73,0),petal(.53,-.107)])
            disc('gold',r*.16,z-.022)

    def spire(self,x,z,base,r,h,n,roof):
        # Concave broached spire profile. All surfaces share one connected ring at each lift.
        levels=[(0,1),(.12,.86),(.39,.47),(.73,.18),(1,0)]
        if self.lod==1:
            levels=[(0,1),(.21,.70),(.63,.25),(1,0)]
        if self.lod==2:
            levels=[(0,1),(.32,.57),(1,0)] if h>.5 else [(0,1),(1,0)]
        def ring(level):
            t,k=level
            return [(x+r*k*math.cos(i*2*math.pi/n+math.pi/n),base+h*t,
                     z+r*k*math.sin(i*2*math.pi/n+math.pi/n)) for i in range(n)]
        for k,(lo,hi) in enumerate(zip(levels,levels[1:])):
            a,b=ring(lo),ring(hi)
            for i in range(n):
                j=(i+1)%n
                poly=[a[j],a[i],b[i]]
                if hi[1]:
                    poly.append(b[j])
                self.face('gold' if hi[1]==0 and k>=3 else roof,poly)

    def flag(self,x,y,z,length):
        self.block('metal',(x,y,z),(.150,.023,.040))
        self.face('banner',[(x+.057,y-length+.028,z-.022),(x,y-length,z-.023),
                            (x-.056,y-length+.026,z-.023),(x-.056,y,z-.022),
                            (x+.057,y,z-.022)])
        if self.lod==0:
            self.face('gold',[(x,y-length*.69,z-.027),(x-.022,y-length*.44,z-.027),
                              (x,y-length*.28,z-.027),(x+.022,y-length*.44,z-.027)])


def tower(m,x,z,high_bell):
    w=.554
    m.course('fieldstone',x,z,.648,.652,.078,.251,.053)
    m.course('wall',x,z,w,.557,.237,1.521,.035,.007)
    m.block('ivory',(x,1.493,z),(.606,.078,.605))
    m.block('fieldstone',(x,1.584,z),(.512,.125,.510))
    # Four piers and a structural cap form an open belfry; a cast bell hangs inside.
    for dx in (-.209,.209):
        for dz in (-.207,.207):
            m.block('plaster',(x+dx,1.810,z+dz),(.089,.345,.091),taper=.035)
    m.block('ivory',(x,2.017,z),(.568,.106,.564))
    m.drum('roof',x,2.056,z,.380,.107,8,.317)
    m.spire(x,z,2.157,.324,.721 if high_bell else .676,6 if m.lod==2 else 8,'roof')
    if m.lod<2:
        m.block('timber',(x,1.962,z),(.370,.047,.077))
        m.drum('metal',x,1.859,z,.029,.085,8)
        bell_y=1.714 if high_bell else 1.691
        m.drum('gold',x,bell_y,z,.115,1.877-bell_y,10,.044)
        m.drum('gold',x,bell_y-.021,z,.125,.027,10,.111)
        m.drum('door',x,bell_y-.044,z,.024,.025,8)
    if m.lod==0:
        # The two different pitches have differently sized clappers.
        for dx in (-.193,.193):
            m.block('fieldstone',(x+dx,.862,z-.267),(.061,1.223,.072),taper=.08)
        m.lancet(x,z-.279,.861,.142,.389)
        m.flag(x,1.414,z-.283,.185)
        with m.transform(translate=(x,0,z),rotate_y=-90 if x>0 else 90):
            m.lancet(0,-.281,.848,.146,.422)
        with m.transform(translate=(x,0,z),rotate_y=180):
            m.lancet(0,-.280,.938,.118,.337)
    elif m.lod==1:
        m.lancet(x,z-.281,.896,.139,.343)


def flyer(m,side,z):
    # Load crosses the aisle roof through the rising arch and down the external pier.
    x=side*1.037
    m.course('fieldstone',x,z,.235,.210,.099,.237,.031)
    m.block('plaster',(x,.571,z),(.175,.713,.162),taper=.077)
    m.block('ivory',(x,.950,z),(.203,.079,.197))
    m.spire(x,z,.982,.108,.265,(6,5,4)[m.lod],'slate')
    profile=[(.497,1.396,1.513),(.675,1.271,1.404),(.833,1.123,1.287),
             (.964,.939,1.171),(1.050,.900,1.115)]
    if m.lod:
        profile=[profile[i] for i in (0,2,4)]
    for a,b in zip(profile,profile[1:]):
        xx,lo,hi=a
        u,v,w=b
        with m.transform(scale=(side,1,1)):
            front=[(u,v,z-.052),(xx,lo,z-.052),(xx,hi,z-.052),(u,w,z-.052)]
            back=[(px,py,z+.052) for px,py,_ in front]
            m.face('wall',front)
            m.face('wall',back[::-1])
            m.face('ivory',(front[2],back[2],back[3],front[3]))
            m.face('fieldstone',(front[0],back[0],back[1],front[1]))
    with m.transform(scale=(side,1,1)):
        u,v,w=profile[-1]
        m.face('wall',[(u,v,z+.052),(u,v,z-.052),(u,w,z-.052),(u,w,z+.052)])


def apse(m):
    # Five radiating chapels share a polygonal ambulatory behind the crossing.
    n=(12,10,6)[m.lod]
    z=.731
    m.drum('fieldstone',0,.086,z,.599,.172,n)
    m.drum('plaster',0,.229,z,.558,.732,n)
    m.drum('ivory',0,.935,z,.594,.070,n)
    m.drum('slate',0,.997,z,.606,.451,n,.184)
    m.drum('wall',0,1.398,z,.202,.158,n,.176)
    m.spire(0,z,1.541,.209,.297,n,'roof')
    if m.lod<2:
        for s in (-1,0,1):
            # The face midpoint and apothem register the glass directly to the facet.
            i=n//4+s
            angle=(i+.5)*2*math.pi/n
            normal=(math.cos(angle),math.sin(angle))
            yaw=math.degrees(math.atan2(-normal[0],-normal[1]))
            apothem=.558*math.cos(math.pi/n)
            with m.transform(translate=(normal[0]*apothem,0,z+normal[1]*apothem),rotate_y=yaw):
                m.lancet(0,-.004,.388,.150,.421)


def processional_plinth(m):
    # The front edge folds back between the lamp socles. Each step projects
    # ahead of the next riser while both tower foundations stay on the platform.
    plan=[(-1.1735,-1.208,1.198),(-.997,-1.385,1.375),(-.497,-1.385,1.375),
          (-.407,-1.212,1.375),(.407,-1.212,1.375),(.497,-1.385,1.375),
          (.997,-1.385,1.375),(1.1735,-1.208,1.198)]
    for (x,f,b),(u,g,c) in zip(plan,plan[1:]):
        m.face('fieldstone',[(x,.127,f),(x,.127,b),(u,.127,c),(u,.127,g)])
        m.face('fieldstone',[(u,0,g),(x,0,f),(x,.127,f),(u,.127,g)])
        m.face('fieldstone',[(x,0,b),(u,0,c),(u,.127,c),(x,.127,b)])
    for x,f,b in (plan[0],plan[-1]):
        side=[(x,0,f),(x,0,b),(x,.127,b),(x,.127,f)]
        m.face('fieldstone',side if x<0 else side[::-1])


def cathedral(m):
    # Low processional platform and steps make the entrance legible below the two bells.
    processional_plinth(m)
    for i in range(3):
        m.block('plaster',(0,.023+i*.020,-1.388+i*.070),
                (.832-i*.052,.046+i*.040,.142))
    m.course('plaster',0,-.173,1.697,1.829,.118,.784,.077)
    m.course('wall',0,-.160,1.039,1.985,.708,1.570,.027)
    with m.transform(translate=(0,0,-.126)):
        m.roof(1.192,2.148,1.567,.651,'slate','plaster',ridge_mat='gold')
    for side in (-1,1):
        # Each aisle sheds away from the clerestory into its own row of buttresses.
        with m.transform(scale=(side,1,1)):
            x,u=.466,.914
            a,b,c,d=(x,1.221,-1.097),(x,1.221,.754),(u,.808,.754),(u,.808,-1.097)
            m.face('slate',(a,b,c,d))
            m.face('ivory',(d,c,(u,.754,.754),(u,.754,-1.097)))
            m.face('plaster',[(u,.754,-1.097),(x,.754,-1.097),a,d])
            m.face('plaster',[(x,.754,.754),(u,.754,.754),c,b])
        with m.transform(translate=(side*.519,0,-.19),rotate_y=-90 if side>0 else 90):
            for k in ((-1,0,1) if m.lod<2 else (0,)):
                m.lancet(k*.456,-.004,1.274,.181,.219,mullion=False)
        for z in (-.527,-.047):
            flyer(m,side,z)
        tower(m,side*.675,-.862,side<0)
    apse(m)
    # Broad transepts bookend the crossing. Their gables face east and west.
    m.course('fieldstone',0,.493,2.548,.658,.064,.235,.068)
    m.course('wall',0,.493,2.419,.552,.222,1.117,.035)
    with m.transform(translate=(0,0,.493),rotate_y=90):
        m.roof(.719,2.591,1.127,.509,'roof','plaster',ridge_mat='gold')
    for side in (-1,1):
        with m.transform(translate=(side*1.213,0,.493),rotate_y=-90 if side>0 else 90):
            m.lancet(0,-.002,.329,.279,.617)
            if m.lod<2:
                m.medallion(0,1.256,-.063,.113)
    # At the intersection, an eight-sided lantern continues the vertical axis.
    m.drum('ivory',0,1.708,.454,.374,.258,8)
    m.drum('wall',0,1.960,.454,.329,.434,8)
    m.drum('gold',0,2.372,.454,.354,.065,8)
    m.spire(0,.454,2.431,.365,.893,8,'slate')
    m.drum('gold',0,3.291,.454,.030,.121,4 if m.lod==2 else 8,.012)
    if m.lod<2:
        for i in range(8):
            angle=2*math.pi*(i+.5)/8
            nx,nz=math.cos(angle),math.sin(angle)
            with m.transform(translate=(nx*.304,0,.454+nz*.304),
                             rotate_y=math.degrees(math.atan2(-nx,-nz))):
                m.lancet(0,-.004,2.039,.136,.268,mullion=False)
    # A pale facade above the door brings the rose nearly to the ridge.
    m.block('plaster',(0,.841,-1.097),(.908,1.459,.120))
    m.medallion(0,1.285,-1.159,.241)
    m.lancet(0,-1.161,.119,.465,.858,'door','ivory',False)
    m.block('fieldstone',(0,.137,-1.172),(.534,.065,.156))
    if m.lod<2:
        m.rect('timber',0,.428,-1.172,.018,.562)
        m.rect('gold',0,.687,-1.176,.405,.028)
        for side in (-1,1):
            m.flag(side*.365,1.415,-1.161,.436)
    if m.lod==0:
        # Cut stone voussoirs flank the western arch, each one bearing on the next.
        for s in (-1,1):
            for i in range(3):
                m.block('ivory',(s*.269,.245+i*.136,-1.164),(.077,.124,.117))
            for dx,y,tilt in ((.269,.654,4),(.246,.760,21),(.184,.851,39),(.097,.922,53)):
                with m.transform(translate=(s*dx,y,-1.177),rotate_z=s*tilt):
                    m.block('plaster',(0,0,0),(.083,.100,.113),taper=.047)
        m.block('gold',(0,.969,-1.183),(.081,.087,.121),taper=.18)
        for side in (-1,1):
            m.rect('gold',side*.065,.473,-1.179,.024,.074)
            m.drum('fieldstone',side*.473,.127,-1.219,.070,.097,8,.048)
            m.drum('metal',side*.473,.222,-1.219,.022,.124,8)
            m.drum('emissive',side*.473,.340,-1.219,.048,.084,8,.058)
            m.spire(side*.473,-1.219,.421,.073,.094,8,'gold')
            with m.transform(translate=(side*.851,0,-.235),rotate_y=-90 if side>0 else 90):
                for dz in (-.232,.230):
                    m.lancet(dz,-.002,.289,.154,.355,mullion=False)
        # A barley crest over the rose, repeated in miniature on the side banners.
        m.face('gold',[(0,1.789,-1.181),(-.073,1.852,-1.181),
                       (0,1.995,-1.181),(.073,1.852,-1.181)])


def build(model,lod):
    m=Cathedral(model,lod)
    cathedral(m)
    return m


if __name__=='__main__':
    for model in package_models('human_wonders'):
        high,mid,low=[build(model,lod) for lod in range(3)]
        print(model.id,publish(high,model.metadata(),model.package,lods=(mid,low),
                               preview_size=320)['lod_triangles'])
