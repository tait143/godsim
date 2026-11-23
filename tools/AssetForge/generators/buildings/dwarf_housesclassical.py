"""Three generations of dwarven courtyard houses in lime, dark slate and burnished copper."""
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import publish
from generators.buildings.dwarf_housesancient import ClanHouse, frame

PACKAGE="dwarf_housesclassical"


def octagonal(m,x,z,base,h,rx,rz,role,scale=1,n=8):
    with m.transform(translate=(x,base,z),scale=(rx,1,rz)):
        m.cylinder((0,0,0),1,h,m.color(role),segments=n,top_radius=scale,material=role)


def cut_mark(m,x,y,z,size):
    # A set of engraved lozenges above each door gives lintels a clan mark.
    for i in range(3 if not m.lod else 1):
        dx=(i-1)*size*.90 if not m.lod else 0
        with m.transform(translate=(x+dx,y,z),rotate_z=45):
            m.block("accent",(0,0,0),(size,size,.013),taper=.08)


def window_reveal(m,x,y,z,w,h,depth):
    rim=[(x+w*.5,y-h*.5,z),(x-w*.5,y-h*.5,z),(x-w*.5,y+h*.5,z),(x+w*.5,y+h*.5,z)]
    if depth<0:
        rim.reverse()
    for j,a in enumerate(rim):
        b=rim[(j+1)%4]
        m.face("fieldstone",(b,a,(a[0],a[1],z+depth),(b[0],b[1],z+depth)))


def portico(m,x,z,w,d,y,height):
    n=(7,5,4)[m.lod]
    for side in (-1,1):
        px=x+side*w*.355
        if m.lod==2:
            m.block("wall",(px,y+height*.5,z-d*.34),(.079,height,.077),taper=.05)
        else:
            octagonal(m,px,z-d*.34,y,.040,.055,.049,"wall",.94,n)
            octagonal(m,px,z-d*.34,y+.040,height-.065,.039,.035,"wall",.84,n)
            octagonal(m,px,z-d*.34,y+height-.025,.025,.056,.050,"trim",1,n)
    m.block("trim",(x,y+height+.019,z),(w,.038,d))
    m.gable_roof((x,y+height+.038,z),w-.025,d-.012,.086,m.color("roof"),overhang=.02,material="roof")


def classical(m,v):
    x=-.058 if v else 0
    z=.088
    w,d=(.600,.478) if v else (.735,.538)
    h=.338 if v else .271
    m.course("wall",x,z,w,d,0,.079,corner=.075)
    m.course("wall",x,z,w-.035,d-.034,.079,h,corner=.049,inset=.019)
    m.course("trim",x,z,w+.02,d+.02,h,h+.039,corner=.063)
    if v:
        m.vault(x,z,w+.051,d+.040,h+.039,.192,"roof","wall",(8,6,4)[m.lod])
        octagonal(m,.243,.192,0,.207,.130,.139,"wall",1.03,n=(8,6,4)[m.lod])
        m.cone((.243,.207,.192),.147,.097,m.color("roof"),segments=(8,6,4)[m.lod],material="roof")
    else:
        m.hip_roof((x,h+.039,z),w+.054,d+.042,.209,m.color("roof"),overhang=.010,material="roof")
    f=z-d*.5-.016
    m.recessed_gate(x,f,.077,.180,h-.074,"fieldstone")
    deck_z=f-.060
    m.course("fieldstone",x,deck_z,.377,.168,0,.077,corner=.038)
    portico(m,x,deck_z,.388,.198,.077,.165)
    m.stair(x,deck_z-.085,.213,.077)
    if m.lod==0:
        m.lamp_face(x+w*.31,.171,f-.016,depth=.116)
        m.pot(x-w*.37,deck_z-.005,.049,.086)
        window_reveal(m,x,.177,z+d*.5,.163,.080,-.075)
        m.rear_vent(x,z+d*.5,.177,.163,.080)


def medieval(m,v):
    x=-.083 if v else -.007
    z=.055
    w,d=(.578,.563) if v else (.748,.568)
    h=.398 if v else .287
    m.course("fieldstone",x,z,w+.03,d+.032,0,.098,corner=.083)
    m.course("wall",x,z,w,d,.098,h,corner=.045)
    m.course("trim",x,z,w+.015,d+.017,h,h+.034,corner=.053)
    m.vault(x,z,w+.041,d+.030,h+.034,.192 if v else .224,"slate","wall",(8,6,3 if v else 4)[m.lod])
    # A stepped stone firebreak follows each gable, its benches dropping to either side of the ridge.
    for iz in (-1,1):
        bz=z+iz*(d*.5+.026)
        if m.lod==2:
            # The distance mesh keeps a continuous three-step firebreak on both ends.
            # Its lower inside faces are already enclosed by the stone vault.
            bottom=h+.031
            span=w/3
            heights=[bottom+(.158 if v else .18),bottom+(.239 if v else .272),bottom+(.158 if v else .18)]
            outer=bz+iz*.023
            inner=bz-iz*.023
            for j,top in enumerate(heights):
                a=x-w*.5+span*j
                b=a+span
                quads=[[(a,bottom,outer),(b,bottom,outer),(b,top,outer),(a,top,outer)],
                       [(a,top,outer),(b,top,outer),(b,top,inner),(a,top,inner)],
                       [(b,top,inner),(a,top,inner),(a,bottom,inner),(b,bottom,inner)][::-1]]
                for face in quads:
                    m.face("fieldstone",face if iz>0 else face[::-1])
            for j,(low,high,facing) in enumerate(((bottom,heights[0],-1),(heights[0],heights[1],-1),
                                                  (heights[2],heights[1],1),(bottom,heights[2],1))):
                px=x-w*.5+span*j
                face=[(px,low,bz-.023),(px,high,bz-.023),(px,high,bz+.023),(px,low,bz+.023)]
                m.face("fieldstone",face if facing>0 else face[::-1])
            continue
        profile=((-2,.121),(-1,.218),(0,.272),(1,.218),(2,.121))
        step=.20
        if m.lod==1 and iz==1:
            profile=((-1,.155),(0,.272),(1,.155))
            step=.333
        for i,peak in profile:
            if v:
                peak*=.88
            m.block("fieldstone",(x+i*w*step,h+.031+peak*.5,bz),(w*(step-.003),peak,.047))
    if v:
        m.course("fieldstone",.244,.139,.263,.317,0,.188,corner=.040)
        m.vault(.244,.139,.285,.329,.188,.141,"roof","wall",(7,5,3)[m.lod])
    f=z-d*.5-.018
    m.recessed_gate(x-.045,f,.095,.183,.226,"fieldstone")
    m.stair(x-.045,f,.218,.095)
    frame(m,x,z,w,d,h,f,window_side=-1 if v else 1)
    m.flue_pot(x+w*.275,z+.112,h+.091,.322,.096,"slate")
    if m.lod<2:
        cut_mark(m,x-.045,h+.115,z-d*.5-.051,.035)
    if m.lod==0:
        # Rain-shedding capstones follow the three tallest steps of the front parapet.
        for step,peak in ((-1,.218),(0,.272),(1,.218)):
            y=h+.031+peak*(.88 if v else 1)
            m.block("slate",(x+step*w*.2,y+.014,z-d*.5-.026),(w*.195,.028,.068),taper=.07)


def renaissance(m,v):
    x=-.083 if v else -.006
    z=.083
    w,d=(.585,.536) if v else (.736,.543)
    h=.330 if v else .282
    m.course("fieldstone",x,z,w,d,0,.078,corner=.088)
    m.course("wall",x,z,w-.032,d-.028,.078,h,corner=.065,inset=.013)
    m.course("trim",x,z,w+.034,d+.038,h,h+.051,corner=.072)
    if not v:
        m.course("roof",x,z,w-.028,d-.032,h+.051,h+.122,corner=.079,inset=.036)
        capx,r=x-.045,.178
        y=h+.122
    else:
        m.vault(x,z,w+.043,d+.023,h+.051,.156,"roof","wall",(8,6,4)[m.lod])
        capx,r=.216,.139
        y=.356
        octagonal(m,capx,.115,0,y,r,r*1.06,"wall",1,n=(8,6,5)[m.lod])
    capz=z+.035 if not v else .115
    octagonal(m,capx,capz,y,.100,r,r,"glass" if v else "wall",.92,n=(9,7,5)[m.lod])
    octagonal(m,capx,capz,y+.098,.027,r*.96,r*.96,"trim",1.09,n=(9,7,5)[m.lod])
    m.dome((capx,y+.125,capz),r*1.086,m.color("accent"),rings=(3,2,2)[m.lod],segments=(10,7,6)[m.lod],
           squash=.89,material="accent")
    if m.lod==0:
        for a in (-math.pi*.5,-math.pi*.12,math.pi*.25):
            with m.transform(translate=(capx+r*.92*math.cos(a),y+.055,capz+r*.92*math.sin(a)),
                             rotate_y=-90-math.degrees(a)):
                m.lamp_face(0,0,0,.024)
        if v:
            with m.transform(translate=(capx+.117,.189,capz-.063),rotate_y=-62):
                window_reveal(m,0,0,-.008,.090,.132,.080)
                m.light(0,0,-.003,.068,.110,shutters=False)
    f=z-d*.5-.016
    m.recessed_gate(x-.036,f,.076,.19,.216,"trim")
    m.stair(x-.036,f,.226,.076)
    if m.lod<2:
        window_reveal(m,x+w*.29,.175,f-.008,.116,.102,.093)
        m.light(x+w*.29,.175,f-.003,.094,.080,shutters=False)
        cut_mark(m,x-.036,.076+.216*.94,f-.038,.025)
    if m.lod==0:
        m.pot(x-w*.39,f-.022,.048,.087)
        m.jar(x+w*.43,f-.015,0,.040,.092)
        window_reveal(m,x,.171,z+d*.5-.004,.151,.076,-.074)
        m.rear_vent(x,z+d*.5-.004,.171,.151,.076)


def build(spec,lod):
    m=ClanHouse(spec,lod)
    {"classical":classical,"medieval":medieval,"renaissance":renaissance}[m.age](m,spec.variant)
    return m


if __name__=="__main__":
    for spec in package_models(PACKAGE):
        levels=[build(spec,i) for i in range(3)]
        result=publish(levels[0],spec.metadata(),PACKAGE,lods=levels[1:],preview_size=240)
        print(spec.id,result["lod_triangles"])
