"""The Sun Pyramid: a processional stair, calendar sanctuary and gold sunrise sight."""
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from building_scope import package_models
from forge import MeshBuilder, publish

# Semantic materials carry the Saurian colour and age through the shared atlas.
WHITE = (1, 1, 1)
PACKAGE = "saurian_wonders"


def octagon(width, depth, chamfer, y, center_z):
    x, z = width / 2, depth / 2
    corners = [(-x + chamfer, -z), (x - chamfer, -z), (x, -z + chamfer),
               (x, z - chamfer), (x - chamfer, z), (-x + chamfer, z),
               (-x, z - chamfer), (-x, -z + chamfer)]
    return [(a, y, b + center_z) for a, b in corners]


def terrace(mesh, width, depth, base, height, detail):
    # A continuous cross section prevents doubled coplanar faces at the cornices.
    rings = [(0, 1)]
    if detail == 0:
        rings += [(.69, .955), (.71, .981), (.87, .981), (.91, 1.005)]
    elif detail == 1:
        rings += [(.72, .956), (.87, .981)]
    rings.append((1, .988))
    previous = None
    for i, (rise, scale) in enumerate(rings):
        ring = octagon(width*scale, depth*scale, .10*scale, base + height*rise, .19)
        if previous:
            inlay = (detail == 0 and i == 3) or (detail == 1 and i == 2)
            role = "turquoise" if inlay else ("trim" if detail == 0 and i == 2 else "wall")
            for j in range(8):
                k = (j+1) % 8
                mesh.quad(previous[k], previous[j], ring[j], ring[k], WHITE, role)
        previous = ring
    mesh.poly(list(reversed(previous)), WHITE, "wall")


def stair(mesh, count, width, start, end, height):
    for i in range(count):
        z0, z1 = start+(end-start)*i/count, start+(end-start)*(i+1)/count
        yp, y = height*i/count, height*(i+1)/count
        x = width / 2
        mesh.quad((x, yp, z0), (-x, yp, z0), (-x, y, z0), (x, y, z0), WHITE, "wall")
        mesh.quad((-x, y, z1), (x, y, z1), (x, y, z0), (-x, y, z0), WHITE, "wall")
        for s in (-1, 1):
            points = [(s*x, 0, z1), (s*x, 0, z0), (s*x, y, z0), (s*x, y, z1)]
            mesh.quad(*(points if s > 0 else reversed(points)), WHITE, "wall")


def stair_cheek(mesh, x, detail):
    width, length = .105, 1.15
    mesh.wedge((x, 0, -.81), width, length, 1.15, WHITE, "roof")
    if detail != 2:
        # A continuous inlaid runner rises along the inclined stone, between the animal and summit.
        with mesh.transform(translate=(x, .59, -.81), rotate_x=-45):
            mesh.box((0, 0, 0), (width*.56, .035, length*math.sqrt(2)), WHITE, material="turquoise")
    else:
        # A single raised ribbon keeps the processional axis clear at the farthest LOD.
        with mesh.transform(translate=(x,.586,-.81),rotate_x=-45):
            r=width*.39
            d=length*math.sqrt(2)*.5
            mesh.quad((-r,0,d),(r,0,d),(r,0,-d),(-r,0,-d),WHITE,"turquoise")


def sight(mesh, detail):
    # The open azimuth sight is anchored on its bottom bearing; the gold diamond is fixed to a crossbar.
    count = (32, 20, 10)[detail]
    cx, cy, cz = 0, 2.04, .255
    radius = .36
    thickness = .075
    def band(radius, z):
        return [(cx+radius*math.cos(2*math.pi*i/count), cy+radius*math.sin(2*math.pi*i/count), z)
                for i in range(count)]
    outer = band(radius, cz-thickness)
    inner = band(.265, cz-thickness)
    bevel = band(radius+.015, cz-.035)
    back = band(radius, cz+thickness)
    inner_back = band(.265, cz+thickness)
    for j in range(count):
        k = (j+1)%count
        mesh.quad(outer[k], outer[j], inner[j], inner[k], WHITE, "gold")
        if detail == 2:
            mesh.quad(outer[j], outer[k], back[k], back[j], WHITE, "gold")
        else:
            mesh.quad(outer[j], outer[k], bevel[k], bevel[j], WHITE, "accent")
            mesh.quad(bevel[j], bevel[k], back[k], back[j], WHITE, "gold")
        mesh.quad(back[j], back[k], inner_back[k], inner_back[j], WHITE, "gold")
        mesh.quad(inner[k], inner[j], inner_back[j], inner_back[k], WHITE, "gold")
    mesh.box((0, cy, cz), (.58, .032, .035), WHITE, material="gold")
    with mesh.transform(translate=(0, cy, cz), rotate_z=45):
        mesh.box((0, 0, 0), (.163, .163, .104), WHITE, material="turquoise", bottom=True)
    for a in ((-58,-29,0,29,58) if detail < 2 else (-48,0,48)):
        # Each ray penetrates the rim. The central index is taller.
        with mesh.transform(translate=(0,cy,cz), rotate_z=a):
            mesh.pyramid((0,.347,0), .108, .10, .19 if a == 0 else .137, WHITE, "gold")


def guardian(mesh, x, lod):
    # Paired crouching monitors are built into the stone at the feet of the two handrails.
    with mesh.transform(translate=(x,0,-1.20)):
        mesh.box((0,.062,0), (.265,.124,.42), WHITE, material="wall", taper=.12)
        mesh.box((0,.183,.04), (.175,.17,.225), WHITE, material="roof", taper=.15)
        mesh.box((0,.258,-.095), (.24,.14,.20), WHITE, material="turquoise", taper=.15)
        # A jade jaw, long pale muzzle and projecting orbital ridge separate the head from the plinth.
        mesh.box((0,.220,-.201), (.177,.051,.17), WHITE, material="roof", taper=.12)
        mesh.wedge((0,.243,-.202),.171,.170,.061,WHITE,"ivory")
        for s in (-1,1):
            mesh.sphere((s*.101,.316,-.144), .043, WHITE, rings=2,segments=6,
                        squash=.83, material="gold")
            mesh.box((s*.091,.352,-.122),(.079,.031,.108),WHITE,material="turquoise",taper=.13)
            if lod == 0:
                mesh.box((s*.135,.320,-.144),(.015,.028,.028),WHITE,material="door",taper=.15)
                mesh.box((s*.109,.133,.039), (.061,.044,.13), WHITE, material="turquoise")


def build(model, lod):
    m = MeshBuilder(seed=model.id)
    heights = (.31, .30, .29, .26)
    y = 0
    for layer, (width, depth) in enumerate(((2.71,2.30),(2.22,1.88),(1.76,1.50),(1.31,1.12))):
        terrace(m,width,depth,y,heights[layer],lod)
        y += heights[layer]
    stair(m,(17,10,6)[lod],.57,-1.47,.19-1.12*.988/2,y)
    for side in (-1,1):
        stair_cheek(m,side*.34,lod)
        if lod < 2:
            guardian(m,side*.39,lod)
    # The front pair of piers leaves a true, shaded negative-space doorway. The rear wall carries the roof.
    for x in (-.31,.31):
        m.box((x,y+.195,-.12),(.17,.39,.21),WHITE,material="wall",taper=.12)
        if lod == 0:
            m.box((x,y+.298,-.12),(.169,.075,.212),WHITE,material="turquoise")
    m.box((0,y+.195,.405),(.81,.39,.31),WHITE,material="wall")
    m.box((0,y+.403,.16),(.96,.095,.92),WHITE,material="roof",bottom=True)
    m.box((0,y+.483,.16),(.81,.075,.73),WHITE,material="roof",taper=.14)
    if lod < 2:
        # Jade fascia, a stepped lintel and cut-stone capitals frame the actual entrance.
        m.box((0,y+.409,-.304),(.88,.050,.018),WHITE,material="turquoise")
        for x in (-.31,.31):
            m.box((x,y+.36,-.12),(.22,.068,.25),WHITE,material="accent")
        if lod == 0:
            for x in (-.35,-.18,0,.18,.35):
                with m.transform(translate=(x,y+.41,-.32),rotate_z=45):
                    m.box((0,0,0),(.031,.031,.019),WHITE,material="gold")
        # Put the devotional light inside the rear wall so it is visible through the portico.
        with m.transform(translate=(0,y+.20,.245),rotate_z=45):
            m.box((0,0,0),(.15,.15,.035),WHITE,material="emissive")
    m.box((0,1.70,.255),(.29,.08,.30),WHITE,material="accent")
    sight(m,lod)
    if lod == 0:
        # Count stones embedded in the terrace face mark the solar calendar.
        for z in (-.40,.00,.40,.80):
            for side in (-1,1):
                with m.transform(translate=(side*1.083,.452,z),rotate_x=45):
                    m.box((0,0,0),(.036,.103,.103),WHITE,material="gold")
        # A water clock and its smaller catch vessel occupy the side of the upper landing.
        for x,z,r in ((.48,.45,.098),(.47,.25,.06)):
            m.cylinder((x,y,z),r*.67,.115,WHITE,segments=8,top_radius=r,material="roof")
            m.cylinder((x,y+.115,z),r,.027,WHITE,segments=8,top_radius=r*1.09,material="gold")
            m.cylinder((x,y+.142,z),r*.79,.003,WHITE,segments=8,material="water")
    return m


if __name__ == "__main__":
    for spec in package_models(PACKAGE):
        high, mid, low = (build(spec, lod) for lod in range(3))
        entry = publish(high,spec.metadata(),PACKAGE,lods=(mid,low),preview_size=288)
        print(spec.id, entry["lod_triangles"])
