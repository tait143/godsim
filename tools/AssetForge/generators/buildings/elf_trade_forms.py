"""Carpentry and work surfaces for the Elf production buildings."""
import math


def block(m, role, center, size, taper=0):
    m.box(center, size, m.color(role), material=role, taper=taper,
          bottom=center[1] > size[1]*.5+1e-6)


def shaft(m, role, start, end, radius, end_radius=None):
    m.limb(role, [start, end], [radius, radius if end_radius is None else end_radius],
           (10, 7, 4)[m.lod])


def gable(m, x, y, z, width, length, rise, role="roof"):
    """Boat-shaped roof with a swept ridge, thick eaves and a projecting prow at each end."""
    stations = [(-1, .74, .18), (-.78, .97, .04), (0, 1, 0),
                (.78, .97, .04), (1, .74, .18)]
    if m.lod:
        stations = [stations[i] for i in (0, 2, 4)]
    rings = []
    for t, broad, lift in stations:
        outer = broad * width * .5
        rings.append([(x + u * outer, y + (h + lift) * rise, z + t * length * .5)
                      for u, h in ((-1, 0), (1, 0), (1, .13), (.5, .47),
                                   (0, 1), (-.5, .47), (-1, .13))])
    # End cheeks are triangulated about an interior point to follow the curved pitch.
    for ring, facing in ((rings[0], -1), (rings[-1], 1)):
        c = (x, ring[0][1]+.22*rise, ring[0][2])
        for i, p in enumerate(ring):
            q = ring[(i+1) % len(ring)]
            m.face("trim", [c, q, p] if facing < 0 else [c, p, q])
    for a, b in zip(rings, rings[1:]):
        for i, p in enumerate(a):
            j = (i+1) % len(a)
            mat = role if i in (2,3,4,5) else "trim"
            m.face(mat, [p, a[j], b[j]])
            m.face(mat, [p, b[j], b[i]])


def arch(m, x, y, z, span, height, depth, role="wall"):
    """A pointed structural arch. Its thick haunches remain open through the depth."""
    profile = [(-.5,0,-.32,0),(-.48,.43,-.31,.42),(-.41,.70,-.267,.58),
               (-.235,.865,-.15,.726),(0,1,0,.82),(.235,.865,.15,.726),
               (.41,.70,.267,.58),(.48,.43,.31,.42),(.5,0,.32,0)]
    if m.lod == 2:
        profile = [profile[i] for i in (0,2,4,6,8)]
    faces = []
    for ox, oy, ix, iy in profile:
        faces.append([(x+ox*span, y+oy*height, z-depth*.5),
                      (x+ix*span, y+iy*height, z-depth*.5),
                      (x+ix*span, y+iy*height, z+depth*.5),
                      (x+ox*span, y+oy*height, z+depth*.5)])
    for a,b in zip(faces,faces[1:]):
        for i in range(4):
            j = (i+1)%4
            m.face(role if i != 1 else "trim",[a[j],a[i],b[i],b[j]])
    m.face(role, faces[0])
    m.face(role, faces[-1][::-1])


def textile(m, x, y, z, width, drop, role="banner", sweep=0):
    """Warp-faced textile looped over a rail, striped and cut to a swallowtail hem."""
    columns = (-1, -.58, 0, .58, 1) if m.lod < 2 else (-1, 0, 1)
    rows = []
    for t in (0, .26, .36, .83, 1):
        row = []
        for u in columns:
            hem = max(0, 1-abs(u)*1.7)*.17*t**4
            row.append((x+.5*width*u, y-drop*(t-hem),
                        z+sweep*t+.056*drop*math.cos(math.pi*u*2)-.19*drop*t*(1-t)))
        rows.append(row)
    th = .025*drop
    def back(p):
        return (p[0],p[1],p[2]+th)
    for i,(a,b) in enumerate(zip(rows,rows[1:])):
        mat = "accent" if i in (1,3) else role
        for j in range(len(a)-1):
            front = [a[j],a[j+1],b[j+1],b[j]]
            for side in (front, [back(p) for p in reversed(front)]):
                m.face(mat, side[:3])
                m.face(mat, [side[0],side[2],side[3]])
            if i == 0:
                m.face(mat,[a[j+1],a[j],back(a[j]),back(a[j+1])])
            if i == 3:
                m.face(mat,[b[j],b[j+1],back(b[j+1]),back(b[j])])
        for index,order in ((0,1),(-1,-1)):
            edge = [a[index],b[index],back(b[index]),back(a[index])]
            m.face(mat,edge[::order])
