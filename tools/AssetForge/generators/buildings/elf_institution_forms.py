"""Architectural sections for the elven schools, civic wards and religious orders."""
from math import cos, pi, sin

WHITE = (1., 1., 1.)


def drum(m, x, z, rx, rz, rows, closed=True):
    """Keep the two silhouette extremes when coarsening a profiled footing or pier."""
    if m.lod == 2 and len(rows) > 4:
        rows = [rows[i] for i in (0, len(rows)//2, -2, -1)]
    elif m.lod == 2 and len(rows)==4:
        rows = [rows[i] for i in (0,2,3)]
    sides=(9,5,4) if max(rx,rz)<.24 else (14,8,5)
    m.lathe(x, z, rx, rz, rows, n=sides[m.lod], closed=closed)


def deck(m, x, y, z, rx, rz, depth, surface="wall"):
    rows=[(y, .80, "trim"), (y+.31*depth, .98, "wall"),
          (y+.78*depth, 1, "accent"), (y+depth, .96, surface)]
    drum(m, x, z, rx, rz, rows if m.lod<2 else rows[::3])


def vault(m, x, z, rx, rz, wall, rise, *, base=0, turn=0, roof="roof", door=True, ribs=True):
    """Bent, laminated barrel roof. End bays narrow in plan toward a pointed-arch gable."""
    with m.transform(translate=(x,base,z), rotate_y=turn):
        n = (8, 5, 4)[m.lod]
        def section(depth, span, height, lift):
            points = [(span,0,depth), (span,wall,depth)]
            for j in range(1,n):
                a = pi*j/n
                points.append((span*cos(a), wall+height*sin(a)+lift*sin(a)**2,depth))
            points += [(-span,wall,depth),(-span,0,depth)]
            return points
        rings = [section(-rz,.75*rx,.80*rise,0),
                 section(-.69*rz,rx,rise,.05*rise),
                 section(.65*rz,rx,1.06*rise,.08*rise),
                 section(rz,.72*rx,.85*rise,0)]
        if m.lod==2:
            rings=[rings[0],section(.06*rz,rx,1.06*rise,.08*rise),rings[-1]]
        for a,b in zip(rings,rings[1:]):
            for j in range(len(a)-1):
                mat = ("brick" if m.era==2 else "wall") if j in (0,len(a)-2) else roof
                m.tri(a[j], b[j+1], b[j], WHITE, mat)
                m.tri(a[j], a[j+1], b[j+1], WHITE, mat)
        m.poly(rings[0][::-1],WHITE,"wall")
        m.poly(rings[-1],WHITE,"wall")
        if door:
            m.portal(0,0,-rz-.011, min(.38,rx*1.14), min(wall+.44*rise,.53))
        if ribs and m.lod < 2:
            for depth in ((-.67*rz,.08*rz,.63*rz) if m.lod==0 else (-.65*rz,.62*rz)):
                pts = [(1.008*rx*cos(pi*j/n),wall+1.10*rise*sin(pi*j/n)+.012,depth)
                       for j in range(n+1)]
                m.stem(pts, [.021]*len(pts),"trim", sides=(5,3)[m.lod])


def pod(m, x, z, rx, rz, base, height, *, role="glass", door=False):
    """A window belt under an overhung lenticular roof, lifted by a flowering pier."""
    if base > 0:
        drum(m,x,z,rx*.47,rz*.54,[(0,1,"wall"),(.12*base,1.12,"wall"),
                                   (.70*base,.64,"ivory"),(base+.14*height,1.52,"trim")])
    drum(m,x,z,rx,rz,[(base,.65,"metal"),(base+.19*height,.99,"ivory"),
                        (base+.61*height,.91,role),(base+.66*height,1.09,"accent"),
                        (base+.82*height,.95,"roof"),(base+height,.43,"ivory")])
    if door:
        m.portal(x,base,z-rz*.94-.012,min(.33,rx*.89),height*.50)
    if m.lod==0:
        for angle in (-1.05,.11,2.47):
            c,s=cos(angle),sin(angle)
            m.stem([(x+rx*.90*c,base+.16*height,z+rz*.90*s),
                    (x+rx*.96*c,base+.44*height,z+rz*.96*s),
                    (x+rx*1.05*c,base+.66*height,z+rz*1.05*s)],
                    [.024,.018,.027],"ivory",sides=5)


def arch_rib(m,x,y,z,span,h,role="trim",lean=.05,width=.061):
    angles = (8,6,4)[m.lod]
    pts=[(x+span*cos(pi*i/angles),y+h*sin(pi*i/angles),z+lean*sin(pi*i/angles))
         for i in range(angles+1)]
    radii=[width*(.71+.29*abs(cos(pi*i/angles))) for i in range(angles+1)]
    if y < width:
        # The rib's shoe meets the floor on a horizontal section.
        pts[0]=(x+span,y+.12*h,z)
        pts[-1]=(x-span,y+.12*h,z)
        pts=[(x+span,y,z),*pts,(x-span,y,z)]
        radii=[width,*radii,width]
    m.stem(pts,radii,role,sides=(6,4,3)[m.lod])


def stair(m,x,z,r,w,d,h,steps=3):
    for i in range(steps):
        depth=d*(1-.17*i)
        drum(m,x,z+i*r,w*(1-.09*i),depth,
             [(i*h,1,"wall"),((i+1)*h,.96,"trim" if i==steps-1 else "wall")])


def open_book(m,x,y,z,s):
    for side in (-1,1):
        with m.transform(translate=(x+side*.168*s,y+.058*s,z),rotate_z=side*13,rotate_x=12):
            m.box((0,0,0),(.34*s,.073*s,.43*s),WHITE,material="ivory",bottom=True)
            if m.lod<2:
                m.box((0,-.041*s,0),(.36*s,.024*s,.45*s),WHITE,material="trim",bottom=True)
