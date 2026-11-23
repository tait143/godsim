"""Original painting of the shared building atlas, its age rows and glTF UV mapping."""
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

from . import style

ART = Path(__file__).resolve().parents[3] / 'Assets/GodSim/Art'
ATLAS = ART / 'Textures/GodSim_BuildingAtlas.png'
MASK = ART / 'Textures/GodSim_BuildingEmissive.png'
SIZE, ROW, CELL_W, CELL_H, GUTTER = 1024, 96, 64, 32, 5
ROW_UV = ROW / SIZE
ROLES = ('wall', 'roof', 'trim', 'accent', 'banner')
DETAILS = ('thatch', 'timber', 'plaster', 'fieldstone', 'brick', 'roof_tile', 'slate', 'metal',
           'glass', 'leaf', 'hide', 'emissive', 'forge', 'ascension', 'door', 'water',
           'gold', 'cloth', 'flower', 'ivory', 'grass', 'soil', 'turquoise')
SLOTS = tuple(f'{race}/{role}' for race in style.RACES for role in ROLES) + DETAILS
SLOT = {name: index for index, name in enumerate(SLOTS)}
LIT = ('emissive', 'forge', 'ascension')
WALL = ('timber', 'brick', 'fieldstone', 'plaster', 'plaster', 'plaster', 'brick', 'plaster', 'glass', 'plaster')
ROOF = ('thatch', 'hide', 'timber', 'roof_tile', 'slate', 'roof_tile', 'metal', 'metal', 'glass', 'metal')
NEUTRAL = dict(wall='plaster', roof='slate', trim='timber', accent='gold', banner='cloth')


@lru_cache(None)
def palette(age):
    a = style.age_mat(age)
    era = style.AGES.index(age)
    details = {
        'thatch': ('#d8b867', 'roof', .22), 'timber': ('#95633d', 'trim', .23),
        'plaster': ('#dfcaa0', 'wall', .74), 'fieldstone': ('#9a9a89', 'stone', .70),
        'brick': ('#ab6850', 'wall', .42), 'roof_tile': ('#bc6c4e', 'roof', .48),
        'slate': ('#586677', 'roof', .40), 'metal': ('#7b8082', 'metal', .8),
        'glass': ('#5287a3', 'glass', .62), 'leaf': ('#669951', 'roof', .12),
        'hide': ('#b88152', 'wall', .17), 'emissive': ('#ffc575', 'glow', .67),
        'forge': ('#ff802c', 'glow', .16), 'ascension': ('#a8f6ef', 'glow', .12),
        'door': ('#452e25', 'trim', .09), 'water': ('#438ba5', 'glass', .10),
        'gold': ('#e8bc60', 'metal', .15), 'cloth': ('#af3f37', 'accent', .09),
        'flower': ('#eb8293', 'accent', .07), 'ivory': ('#f1e4c9', 'wall', .08),
        'grass': ('#82a44f', 'stone', .08), 'soil': ('#846146', 'stone', .10),
        'turquoise': ('#35aeab', 'glass', .12),
    }
    colors = {key: style.mix(style.hex_rgb(h), a[role], weight) for key, (h, role, weight) in details.items()}
    patterns = {key: key for key in details}
    for race in style.RACES:
        for role in ROLES:
            key = f'{race}/{role}'
            colors[key] = style.blended(race, age, role, .35 if role != 'accent' else .63)
            patterns[key] = {'wall': WALL[era], 'roof': ROOF[era], 'trim': 'timber' if era < 7 else 'metal',
                             'accent': 'plain', 'banner': 'cloth'}[role]
            if role == 'roof' and race == 'elf':
                patterns[key] = 'leaf'
            if role == 'roof' and race == 'orc' and era < 5:
                patterns[key] = 'hide'
    return colors, patterns


def _paint(pattern, color):
    # Paint the interior on a periodic coordinate grid, then extrude its edge texels into the gutter.
    h, w = CELL_H - 2 * GUTTER, CELL_W - 2 * GUTTER
    y, x = np.mgrid[0:h, 0:w]
    u, v = (x + .5) / w, (y + .5) / h
    wave = lambda t: np.sin(2 * np.pi * t)
    soft = .985 + .025 * wave(v) + .017 * wave(u + v)
    grain = .008 * wave(7*u + 4*v) * wave(5*v - u)
    if pattern in ('brick', 'fieldstone', 'slate', 'roof_tile'):
        courses = 3 if pattern in ('brick', 'roof_tile') else 2
        rows = v * courses
        blocks = u * (4 if pattern == 'brick' else 3) + .5 * (np.floor(rows) % 2)
        if pattern == 'fieldstone':
            blocks += .09 * wave(rows)
            rows += .07 * wave(4*u)
        if pattern == 'roof_tile':
            rows += .15 * np.cos(2*np.pi * blocks)
        mortar = (rows % 1 < .10) | (blocks % 1 < .065)
        value = soft + .035 * wave(blocks) + .035 * (1 - rows % 1) - .17 * mortar
    elif pattern in ('timber', 'door', 'thatch'):
        lines = u * (12 if pattern == 'thatch' else 4) + .12 * wave(v) + .045 * wave(2*v + u)
        value = soft + .045 * wave(lines) - .13 * (lines % 1 < .085) + .025 * wave(2*lines)
    elif pattern == 'leaf':
        limb = abs((u + .5) % 1 - .5)
        value = soft + .07 * wave(v - 3*limb) + .08 * (limb < .045) - .035 * (wave(2*v - 4*limb) > .75)
    elif pattern == 'metal':
        joint = (u < .035) | (v < .055)
        rivet = ((u - .09)**2 + (v - .16)**2 * .24) < .0015
        value = soft + .035 * wave(u + .3*v) - .13 * joint + .12 * rivet
    elif pattern in ('glass', 'water', 'turquoise'):
        value = soft + .065 * wave(2*u + v) + .05 * (wave(2*u + v) > .90)
    elif pattern == 'hide':
        value = soft + .035 * wave(u) * wave(2*v) - .08 * (abs(wave(2*u + v)) < .12)
    elif pattern == 'cloth':
        value = soft + .06 * wave(2*u) + .02 * wave(4*v)
    else:
        value = soft + .025 * wave(u) * wave(v)
    pixels = np.clip(255 * np.array(color) * (value + grain)[..., None], 0, 255).round().astype('u1')
    return np.pad(pixels, ((GUTTER, GUTTER), (GUTTER, GUTTER), (0, 0)), mode='edge')


def create_textures():
    atlas = np.empty((SIZE, SIZE, 3), dtype='u1')
    mask = np.zeros((SIZE // 4, SIZE // 4, 3), dtype='u1')
    for row, age in enumerate(style.AGES):
        colors, patterns = palette(age)
        for index, key in enumerate(SLOTS):
            x, y = index % 16 * CELL_W, row * ROW + index // 16 * CELL_H
            atlas[y:y+CELL_H, x:x+CELL_W] = _paint(patterns[key], colors[key])
            if key in LIT:
                strength = {'emissive': .34, 'forge': .62, 'ascension': .52}[key]
                mask[y//4:(y+CELL_H)//4, x//4:(x+CELL_W)//4] = np.round(255 * strength * np.array(colors[key])).astype('u1')
    atlas[len(style.AGES) * ROW:] = atlas[len(style.AGES) * ROW - 1]
    ATLAS.parent.mkdir(parents=True, exist_ok=True)
    for path, pixels in ((ATLAS, atlas), (MASK, mask)):
        Image.fromarray(pixels).save(path, compress_level=9)
    textures.cache_clear()


@lru_cache(None)
def textures():
    with Image.open(ATLAS) as a, Image.open(MASK) as b:
        return np.array(a.convert('RGB')), np.array(b.convert('RGB'))


@lru_cache(None)
def _candidates(age, race):
    names = [f'{race}/{r}' for r in ROLES] if race in style.RACES else []
    names += [k for k in DETAILS if k not in LIT]
    colors = palette(age)[0]
    return np.array([SLOT[k] for k in names]), np.array([colors[k] for k in names])


def uv_for(tris, age, race):
    indices = np.empty(len(tris), dtype='i4')
    legacy = []
    for i, tri in enumerate(tris):
        key = tri.material
        if key == 'base':
            legacy.append(i)
        else:
            if key in ROLES:
                key = f'{race}/{key}' if race in style.RACES else NEUTRAL[key]
            indices[i] = SLOT[key]
    if legacy:
        slots, colors = _candidates(age, race)
        desired = np.array([tris[i].color[:3] for i in legacy])
        delta = desired[:, None, :] - colors
        indices[legacy] = slots[np.argmin(np.sum(delta**2, axis=-1), axis=1)]
    origins = np.column_stack([indices % 16 * CELL_W, indices // 16 * CELL_H + style.AGES.index(age) * ROW])
    local = np.stack([tri.local_uv for tri in tris])
    return ((origins[:, None, :] + GUTTER + .5 + local * [CELL_W - 2*GUTTER - 1, CELL_H - 2*GUTTER - 1]) / SIZE).astype('<f4')


def astc_bytes(width, height, block=6):
    """RGBA ASTC always takes 16 bytes per block; count every mip including sub-block tails."""
    total = 0
    while True:
        total += ((width + block - 1) // block) * ((height + block - 1) // block) * 16
        if width == height == 1:
            return total
        width, height = max(1, width//2), max(1, height//2)


if __name__ == '__main__':
    create_textures()
