"""GodSim art direction in code: palettes, race styles, age materials and helpers.

Style name: "Storybook Monumental" - serious, believable architecture (real construction logic per age and
race) rendered with chunky, slightly exaggerated proportions, faceted low-poly forms, warm grounded colours and
small playful details (flower boxes, banners, crooked chimneys, lanterns, laundry). See docs/ART_STYLE_GUIDE.md.
"""
from __future__ import annotations

import colorsys
import hashlib

RGB = tuple  # (r, g, b) floats 0..1 in sRGB


def hex_rgb(h: str) -> RGB:
    h = h.lstrip('#')
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0)


def shade(c: RGB, f: float) -> RGB:
    """Multiply brightness (f < 1 darker, > 1 lighter), clamped."""
    return tuple(max(0.0, min(1.0, x * f)) for x in c)


def mix(a: RGB, b: RGB, t: float) -> RGB:
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def saturate(c: RGB, f: float) -> RGB:
    h, l, s = colorsys.rgb_to_hls(*c)
    return colorsys.hls_to_rgb(h, l, max(0.0, min(1.0, s * f)))


def jitter(c: RGB, seed: str, amount: float = 0.06) -> RGB:
    """Deterministic small brightness/hue variation for a hand-painted, faceted feel."""
    d = hashlib.blake2b(seed.encode(), digest_size=4).digest()
    v = (d[0] / 255.0 - 0.5) * 2 * amount
    hshift = (d[1] / 255.0 - 0.5) * amount * 0.25
    h, l, s = colorsys.rgb_to_hls(*c)
    return colorsys.hls_to_rgb((h + hshift) % 1.0, max(0.0, min(1.0, l + v * 0.5)), s)


def stable_rand(seed: str) -> float:
    """Deterministic float in [0,1) from a string."""
    d = hashlib.blake2b(seed.encode(), digest_size=8).digest()
    return int.from_bytes(d, 'little') / 2 ** 64


# ----------------------------------------------------------------------------------------------- ages
AGES = ['dawn', 'bronze', 'iron', 'classical', 'medieval', 'renaissance', 'industrial', 'modern', 'information', 'ascension']

# Material palette per age: wall, roof, trim, accent, stone, metal, glass, glow
AGE_MATERIALS = {
    'dawn':        dict(wall=hex_rgb('#8a6a48'), roof=hex_rgb('#c9a15a'), trim=hex_rgb('#5b4030'), accent=hex_rgb('#b5432c'), stone=hex_rgb('#8f8a80'), metal=hex_rgb('#6b5a4a'), glass=hex_rgb('#3a3a3a'), glow=hex_rgb('#ffb347')),
    'bronze':      dict(wall=hex_rgb('#c49a6c'), roof=hex_rgb('#a8783f'), trim=hex_rgb('#6e4b2a'), accent=hex_rgb('#c0782b'), stone=hex_rgb('#a39a8a'), metal=hex_rgb('#b0803a'), glass=hex_rgb('#3b3530'), glow=hex_rgb('#ffbf5a')),
    'iron':        dict(wall=hex_rgb('#9a9183'), roof=hex_rgb('#6f5a45'), trim=hex_rgb('#4a3d32'), accent=hex_rgb('#8c2f2f'), stone=hex_rgb('#8a857c'), metal=hex_rgb('#5f6266'), glass=hex_rgb('#2f3236'), glow=hex_rgb('#ffb04a')),
    'classical':   dict(wall=hex_rgb('#ece6d8'), roof=hex_rgb('#c0623a'), trim=hex_rgb('#d8cfba'), accent=hex_rgb('#2f5f9a'), stone=hex_rgb('#e2dccd'), metal=hex_rgb('#b08a3a'), glass=hex_rgb('#3a4048'), glow=hex_rgb('#ffd27a')),
    'medieval':    dict(wall=hex_rgb('#e3d7bd'), roof=hex_rgb('#5f4b3e'), trim=hex_rgb('#4a3426'), accent=hex_rgb('#8e2a2a'), stone=hex_rgb('#8b8680'), metal=hex_rgb('#5a5c60'), glass=hex_rgb('#40505c'), glow=hex_rgb('#ffc061')),
    'renaissance': dict(wall=hex_rgb('#e8c99a'), roof=hex_rgb('#b85a33'), trim=hex_rgb('#f2e6cf'), accent=hex_rgb('#2d6b5e'), stone=hex_rgb('#d7c4a3'), metal=hex_rgb('#a77f3c'), glass=hex_rgb('#4a6272'), glow=hex_rgb('#ffd88a')),
    'industrial':  dict(wall=hex_rgb('#9a4a36'), roof=hex_rgb('#3e3f44'), trim=hex_rgb('#d9cdb8'), accent=hex_rgb('#2f4a6b'), stone=hex_rgb('#77726c'), metal=hex_rgb('#44474c'), glass=hex_rgb('#56707e'), glow=hex_rgb('#ffcf7a')),
    'modern':      dict(wall=hex_rgb('#c9c7c1'), roof=hex_rgb('#5c6066'), trim=hex_rgb('#e9e7e2'), accent=hex_rgb('#d9532b'), stone=hex_rgb('#a9a6a0'), metal=hex_rgb('#7b8189'), glass=hex_rgb('#6fa3c2'), glow=hex_rgb('#fff1c2')),
    'information': dict(wall=hex_rgb('#eef1f4'), roof=hex_rgb('#3f4b57'), trim=hex_rgb('#ffffff'), accent=hex_rgb('#1fb5c9'), stone=hex_rgb('#c9ced4'), metal=hex_rgb('#9aa4ae'), glass=hex_rgb('#58b9e6'), glow=hex_rgb('#9ff3ff')),
    'ascension':   dict(wall=hex_rgb('#f7f4ec'), roof=hex_rgb('#d9b45a'), trim=hex_rgb('#fff9ea'), accent=hex_rgb('#7de8ff'), stone=hex_rgb('#ebe5d6'), metal=hex_rgb('#e3c36b'), glass=hex_rgb('#a6f0ff'), glow=hex_rgb('#c8fbff')),
}

# ----------------------------------------------------------------------------------------------- races
RACES = ['human', 'elf', 'dwarf', 'orc', 'saurian']

RACE_STYLE = {
    'human': dict(
        banner=hex_rgb('#3b6fd8'), wall=hex_rgb('#e6dcc6'), roof=hex_rgb('#a5482f'), trim=hex_rgb('#5a3a1e'), accent=hex_rgb('#d4af37'),
        roof_kind='gable', roundness=0.0, height=1.0, width=1.0,
        motifs=['timber framing', 'flower boxes', 'chimneys', 'shuttered windows', 'banners', 'cobbled plinths'],
        feel='hopeful, orderly, homely; tidy gables and bright banners over serious masonry'),
    'elf': dict(
        banner=hex_rgb('#2fa84f'), wall=hex_rgb('#e9f0dc'), roof=hex_rgb('#4f8f4a'), trim=hex_rgb('#8a6a3e'), accent=hex_rgb('#cfe7f5'),
        roof_kind='leaf', roundness=0.85, height=1.15, width=0.95,
        motifs=['living trunks', 'leaf canopies', 'curved ribs', 'lantern pods', 'moon-silver accents', 'vines'],
        feel='graceful, organic, ancient; soaring curves and leaf roofs, nothing square'),
    'dwarf': dict(
        banner=hex_rgb('#c8741e'), wall=hex_rgb('#8d8478'), roof=hex_rgb('#5b5048'), trim=hex_rgb('#3a3029'), accent=hex_rgb('#e0a83a'),
        roof_kind='flat', roundness=0.1, height=0.8, width=1.2,
        motifs=['carved stone', 'rune bands', 'brass trim', 'half-sunk halls', 'forge glow', 'thick buttresses'],
        feel='stout, heavy, proud; squat massive stone with glowing forge-light and brass'),
    'orc': dict(
        banner=hex_rgb('#b3261e'), wall=hex_rgb('#8a6a4a'), roof=hex_rgb('#6b4a32'), trim=hex_rgb('#2f2420'), accent=hex_rgb('#e8e0cf'),
        roof_kind='cone', roundness=0.5, height=1.05, width=1.05,
        motifs=['hide and timber', 'tusk/bone spikes', 'war paint stripes', 'skull totems (stylized, not gory)', 'lashed poles', 'big drums'],
        feel='loud, rugged, proud warrior camps; spiky silhouettes, chunky lashed timber, bold stripes'),
    'saurian': dict(
        banner=hex_rgb('#8e44ad'), wall=hex_rgb('#c8b37a'), roof=hex_rgb('#3f7a5a'), trim=hex_rgb('#5a3f2a'), accent=hex_rgb('#e5c14a'),
        roof_kind='pyramid', roundness=0.35, height=1.0, width=1.0,
        motifs=['stilts over water', 'stepped pyramids', 'sun discs', 'reed thatch', 'turquoise inlays', 'star-chart carvings'],
        feel='patient, mystical, sun-worshipping; stepped forms, stilts, gold sun discs and teal inlays'),
}

COMMON = dict(
    grass=hex_rgb('#7aa64a'), dirt=hex_rgb('#8a6a48'), water=hex_rgb('#3f8fc4'), wood=hex_rgb('#8b5a2b'),
    darkwood=hex_rgb('#5a3a22'), stone=hex_rgb('#9a958c'), leaf=hex_rgb('#4f8f3a'), leaf_dark=hex_rgb('#2f6b2f'),
    snow=hex_rgb('#f2f5f7'), sand=hex_rgb('#e3cf94'), gold=hex_rgb('#e3b341'), cloth=hex_rgb('#c94f3f'), smoke=hex_rgb('#9a9a9a'),
    window_lit=hex_rgb('#ffd27a'), window_dark=hex_rgb('#2b3038'), door=hex_rgb('#4a2f1e'),
)


def age_mat(age: str) -> dict:
    return AGE_MATERIALS[age]


def race_style(race: str) -> dict:
    return RACE_STYLE[race]


def blended(race: str, age: str, key: str, race_weight: float = 0.55) -> RGB:
    """Blend a race colour with the age material (race identity + era material truth)."""
    r = RACE_STYLE[race].get(key)
    a = AGE_MATERIALS[age].get(key)
    if r is None:
        return a
    if a is None:
        return r
    return mix(a, r, race_weight)
