#!/usr/bin/env node
// Generates a plausible synthetic GodSim snapshot (schema godsim.snapshot/1) for testing the
// WebViewer without running the headless simulation. No dependencies.
//
//   node make-sample.mjs [--size 192] [--seed 1337] [--out sample-snapshot.json]
//
// The output is deterministic for a given seed/size.

import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
function arg(name, def) {
  const i = args.indexOf('--' + name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : def;
}
const SIZE = Math.max(32, Math.min(2048, parseInt(arg('size', '192'), 10) || 192));
const SEED = (parseInt(arg('seed', '1337'), 10) || 1337) >>> 0;
const OUT = arg('out', join(dirname(fileURLToPath(import.meta.url)), 'sample-snapshot.json'));
const W = SIZE, H = SIZE, N = W * H;

// ---------------------------------------------------------------- RNG (mulberry32)
let rs = SEED || 1;
function rnd() {
  rs = (rs + 0x6D2B79F5) >>> 0;
  let t = rs;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);

// ---------------------------------------------------------------- enums
const BIOMES = ['DeepOcean', 'ShallowWater', 'Lake', 'Beach', 'Grassland', 'Savanna', 'Forest', 'Jungle',
  'Desert', 'Swamp', 'Taiga', 'Tundra', 'Hills', 'Mountain', 'Snow', 'Volcanic'];
const B = Object.fromEntries(BIOMES.map((n, i) => [n, i]));
const CATEGORIES = ['Housing', 'Food', 'Resource', 'Industry', 'Storage', 'Civic', 'Religion', 'Education',
  'Health', 'Culture', 'Military', 'Commerce', 'Infrastructure', 'Wonder'];
const AGES = ['Dawn', 'Bronze', 'Iron', 'Classical', 'Medieval', 'Renaissance', 'Industrial', 'Modern',
  'Information', 'Ascension'];
const RACES = ['Human', 'Elf', 'Dwarf', 'Orc', 'Saurian'];
const TIERS = ['Camp', 'Hamlet', 'Village', 'Town', 'City', 'Metropolis', 'Megalopolis'];
const CHRON = ['World', 'Birth', 'Death', 'Building', 'Settlement', 'Civilization', 'Age', 'Technology',
  'War', 'Diplomacy', 'Divine', 'Disaster'];
const RES = ['Food', 'Wood', 'Stone', 'Ore', 'Metal', 'Gold', 'Tools', 'Cloth', 'Goods', 'Weapons', 'Fuel'];
// [type, category, shape, w, h, minAge]
const BT = [
  ['TownCenter', 'Civic', 'Hall', 3, 3, 0], ['House', 'Housing', 'House', 1, 1, 0], ['Well', 'Infrastructure', 'Hut', 1, 1, 0],
  ['Storehouse', 'Storage', 'Hall', 2, 2, 0], ['GathererHut', 'Food', 'Hut', 1, 1, 0], ['HuntingLodge', 'Food', 'Hut', 2, 1, 0],
  ['FishingHut', 'Food', 'Dock', 1, 1, 0], ['WoodcutterCamp', 'Resource', 'Hut', 2, 1, 0], ['StoneQuarry', 'Resource', 'Cave', 2, 2, 0],
  ['SpiritTotem', 'Religion', 'Monument', 1, 1, 0], ['Campfire', 'Culture', 'Hut', 1, 1, 0], ['Farm', 'Food', 'Field', 3, 3, 1],
  ['Pasture', 'Food', 'Field', 3, 2, 1], ['Granary', 'Storage', 'Dome', 2, 2, 1], ['Mine', 'Resource', 'Cave', 2, 2, 1],
  ['Smelter', 'Industry', 'Workshop', 2, 2, 1], ['Shrine', 'Religion', 'Dome', 1, 1, 1], ['Herbalist', 'Health', 'Hut', 1, 1, 1],
  ['Weaver', 'Industry', 'Workshop', 2, 1, 1], ['Blacksmith', 'Industry', 'Workshop', 2, 2, 2], ['Temple', 'Religion', 'Pyramid', 3, 3, 2],
  ['Barracks', 'Military', 'Hall', 3, 2, 2], ['Market', 'Commerce', 'Hall', 3, 3, 2], ['Watchtower', 'Military', 'Tower', 1, 1, 2],
  ['Scriptorium', 'Education', 'Hall', 2, 2, 2], ['Library', 'Education', 'Hall', 3, 2, 3], ['Aqueduct', 'Infrastructure', 'Wall', 1, 4, 3],
  ['Bathhouse', 'Health', 'Dome', 2, 2, 3], ['Theater', 'Culture', 'Dome', 3, 3, 3], ['Forum', 'Civic', 'Hall', 3, 3, 3],
  ['Castle', 'Military', 'Tower', 4, 4, 4], ['Cathedral', 'Religion', 'Spire', 3, 4, 4], ['Mill', 'Food', 'Workshop', 2, 2, 4],
  ['Tavern', 'Culture', 'House', 2, 2, 4], ['University', 'Education', 'Hall', 4, 3, 5], ['Bank', 'Commerce', 'Hall', 2, 2, 5],
  ['Hospital', 'Health', 'Hall', 3, 3, 5], ['Factory', 'Industry', 'Factory', 4, 3, 6], ['RailStation', 'Commerce', 'Hall', 4, 2, 6],
  ['GreatLibrary', 'Wonder', 'Monument', 4, 4, 3],
];
const BUILDING_TYPES = BT.map((b) => b[0]);
const BTI = Object.fromEntries(BT.map((b, i) => [b[0], i]));

// ---------------------------------------------------------------- terrain
function makeNoise(cell) {
  const gw = Math.ceil(W / cell) + 2, gh = Math.ceil(H / cell) + 2;
  const g = new Float32Array(gw * gh).map(() => rnd());
  const sm = (t) => t * t * (3 - 2 * t);
  return (x, y) => {
    const fx = x / cell, fy = y / cell, x0 = Math.floor(fx), y0 = Math.floor(fy);
    const tx = sm(fx - x0), ty = sm(fy - y0);
    const a = g[y0 * gw + x0], b = g[y0 * gw + x0 + 1], c = g[(y0 + 1) * gw + x0], d = g[(y0 + 1) * gw + x0 + 1];
    return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
  };
}
const octs = [makeNoise(W / 3), makeNoise(W / 6), makeNoise(W / 12), makeNoise(W / 24), makeNoise(W / 48)];
const moistN = [makeNoise(W / 4), makeNoise(W / 10), makeNoise(W / 25)];
function fbm(ns, x, y) {
  let v = 0, amp = 1, tot = 0;
  for (const n of ns) { v += n(x, y) * amp; tot += amp; amp *= 0.5; }
  return v / tot;
}
const elevation = new Array(N), biome = new Array(N), flags = new Array(N).fill(0), owner = new Array(N).fill(-1);
const moist = new Float32Array(N);
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const i = y * W + x;
  const dx = (x / W - 0.5) * 2, dy = (y / H - 0.5) * 2;
  const falloff = Math.pow(Math.min(1, Math.sqrt(dx * dx * 0.9 + dy * dy * 0.9)), 2.4);
  let e = fbm(octs, x, y) * 1.15 - falloff * 0.55 + 0.08;
  e = clamp(e, 0, 1);
  elevation[i] = Math.round(clamp(e * 255 * 1.05, 0, 255));
  moist[i] = fbm(moistN, x, y);
}
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const i = y * W + x, e = elevation[i], m = moist[i];
  const lat = Math.abs(y / H - 0.5) * 2; // 0 equator .. 1 poles
  const temp = 1 - lat * 0.85 - Math.max(0, e - 140) / 220;
  let b;
  if (e < 105) b = B.DeepOcean;
  else if (e < 127) b = B.ShallowWater;
  else if (e < 131) b = temp < 0.2 ? B.Tundra : B.Beach;
  else if (e > 228) b = B.Snow;
  else if (e > 205) b = B.Mountain;
  else if (e > 182) b = B.Hills;
  else if (temp < 0.18) b = B.Tundra;
  else if (temp < 0.35) b = m > 0.5 ? B.Taiga : B.Tundra;
  else if (temp > 0.78) b = m > 0.62 ? B.Jungle : m > 0.55 ? B.Swamp : m < 0.38 ? B.Desert : B.Savanna;
  else b = m > 0.6 ? B.Forest : m > 0.56 && e < 140 ? B.Swamp : m < 0.36 ? B.Savanna : B.Grassland;
  biome[i] = b;
}
// volcanic spots on mountains, inland lakes in basins
for (let k = 0; k < 3; k++) {
  const i = ri(0, N - 1);
  if (biome[i] === B.Mountain || biome[i] === B.Hills) {
    const cx = i % W, cy = (i / W) | 0;
    for (let y = cy - 3; y <= cy + 3; y++) for (let x = cx - 3; x <= cx + 3; x++)
      if (x >= 0 && y >= 0 && x < W && y < H && (x - cx) ** 2 + (y - cy) ** 2 < 9 + rnd() * 4) biome[y * W + x] = B.Volcanic;
  }
}
for (let k = 0; k < 6; k++) {
  const cx = ri(10, W - 11), cy = ri(10, H - 11), r = ri(2, 5);
  if (biome[cy * W + cx] !== B.Grassland && biome[cy * W + cx] !== B.Forest) continue;
  for (let y = cy - r; y <= cy + r; y++) for (let x = cx - r; x <= cx + r; x++) {
    const d = (x - cx) ** 2 + (y - cy) ** 2;
    if (d <= r * r * (0.7 + rnd() * 0.4)) { biome[y * W + x] = B.Lake; elevation[y * W + x] = 124; }
  }
}
const isWater = (b) => b <= B.Lake;
const F = { River: 1, Road: 2, Ore: 4, Stone: 8, Gold: 16, Fertile: 32, Ruins: 64, Sacred: 128, Burning: 256,
  Coastal: 512, Coal: 1024, Oil: 2048, Wall: 4096, Farmland: 8192, Trees: 16384, Scorched: 32768 };
// rivers: descend from high ground
for (let r = 0; r < Math.round(W / 14); r++) {
  let i = -1;
  for (let t = 0; t < 200; t++) { const c = ri(0, N - 1); if (elevation[c] > 175 && biome[c] !== B.Snow) { i = c; break; } }
  if (i < 0) continue;
  for (let steps = 0; steps < W * 2; steps++) {
    if (isWater(biome[i])) break;
    flags[i] |= F.River;
    const x = i % W, y = (i / W) | 0;
    let best = -1, be = 1e9;
    for (const [ox, oy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = x + ox, ny = y + oy;
      if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
      const j = ny * W + nx;
      if (flags[j] & F.River) continue;
      const ev = elevation[j] + rnd() * 3;
      if (ev < be) { be = ev; best = j; }
    }
    if (best < 0) break;
    if (elevation[best] >= elevation[i]) elevation[best] = Math.max(128, elevation[i] - 1);
    i = best;
  }
}
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const i = y * W + x, b = biome[i];
  if (isWater(b)) continue;
  let coast = false;
  for (const [ox, oy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
    const nx = x + ox, ny = y + oy;
    if (nx >= 0 && ny >= 0 && nx < W && ny < H && isWater(biome[ny * W + nx])) coast = true;
  }
  if (coast) flags[i] |= F.Coastal;
  if ((b === B.Forest || b === B.Jungle || b === B.Taiga) && rnd() < 0.75) flags[i] |= F.Trees;
  if ((b === B.Grassland || b === B.Savanna) && rnd() < 0.06) flags[i] |= F.Trees;
  if ((b === B.Grassland || b === B.Forest) && moist[i] > 0.5 && rnd() < 0.5) flags[i] |= F.Fertile;
  if (flags[i] & F.River && rnd() < 0.6) flags[i] |= F.Fertile;
  if (b === B.Hills || b === B.Mountain) {
    const q = rnd();
    if (q < 0.04) flags[i] |= F.Ore; else if (q < 0.08) flags[i] |= F.Stone; else if (q < 0.095) flags[i] |= F.Gold; else if (q < 0.11) flags[i] |= F.Coal;
  }
  if (b === B.Desert && rnd() < 0.02) flags[i] |= F.Oil;
}

// ---------------------------------------------------------------- civilizations & settlements
const YEAR = 412;
const CIV_DEF = [
  { race: 'Human', name: 'Kingdom of Aldmere', color: '#3b82f6', pref: [B.Grassland, B.Forest], age: 'Renaissance', techs: 41 },
  { race: 'Elf', name: 'Sylvan Concord', color: '#22c55e', pref: [B.Forest, B.Jungle], age: 'Medieval', techs: 36 },
  { race: 'Dwarf', name: 'Holds of Karak Dun', color: '#f59e0b', pref: [B.Hills, B.Mountain], age: 'Classical', techs: 29 },
  { race: 'Orc', name: 'Gorrak Horde', color: '#ef4444', pref: [B.Savanna, B.Tundra, B.Hills], age: 'Iron', techs: 21 },
  { race: 'Saurian', name: 'Tlaxi Dominion', color: '#a855f7', pref: [B.Swamp, B.Jungle, B.Beach], age: 'Classical', techs: 27 },
];
const SYL = { Human: ['ald', 'bran', 'cas', 'dun', 'ford', 'ham', 'ley', 'mar', 'ton', 'wick', 'by', 'shire'],
  Elf: ['ael', 'thal', 'lor', 'sil', 'van', 'ith', 'iel', 'wen', 'dor', 'nae'],
  Dwarf: ['kar', 'dun', 'grim', 'bar', 'zak', 'thor', 'mok', 'din', 'hold'],
  Orc: ['gor', 'rak', 'mug', 'zug', 'ush', 'krag', 'bol', 'grot'],
  Saurian: ['tla', 'xi', 'quo', 'hua', 'cal', 'tzi', 'mec', 'tol'] };
const usedNames = new Set();
function nameFor(race) {
  for (;;) {
    const s = SYL[race];
    let n = pick(s) + pick(s) + (rnd() < 0.4 ? pick(s) : '');
    n = n[0].toUpperCase() + n.slice(1);
    if (!usedNames.has(n)) { usedNames.add(n); return n; }
  }
}
const land = (i) => !isWater(biome[i]) && biome[i] !== B.Snow && biome[i] !== B.Mountain && biome[i] !== B.Volcanic;
const settlements = [], civs = [];
const anchors = [];
for (let c = 0; c < CIV_DEF.length; c++) {
  const def = CIV_DEF[c];
  let best = -1, bs = -1e9;
  for (let t = 0; t < 4000; t++) {
    const x = ri(12, W - 13), y = ri(12, H - 13), i = y * W + x;
    if (!land(i)) continue;
    let s = def.pref.includes(biome[i]) ? 3 : 0;
    if (flags[i] & (F.River | F.Coastal)) s += 1.5;
    let md = 1e9;
    for (const a of anchors) md = Math.min(md, Math.hypot(a[0] - x, a[1] - y));
    s += Math.min(md, W * 0.45) / (W * 0.08);
    if (s > bs) { bs = s; best = i; }
  }
  anchors.push([best % W, (best / W) | 0]);
}
let sid = 0;
for (let c = 0; c < CIV_DEF.length; c++) {
  const def = CIV_DEF[c];
  const [ax, ay] = anchors[c];
  const civ = { id: c, name: def.name, race: def.race, color: def.color, age: def.age, alive: true, population: 0,
    settlements: [], capitalId: -1, techs: def.techs, totalResearch: def.techs * 180 + ri(0, 900),
    devotion: +(0.3 + rnd() * 0.6).toFixed(2), godFavor: +(rnd() * 2 - 1).toFixed(2), militaryPower: 0 };
  const count = ri(3, 5);
  for (let k = 0; k < count; k++) {
    let x = ax, y = ay;
    if (k > 0) {
      let ok = false;
      for (let t = 0; t < 400 && !ok; t++) {
        const ang = rnd() * Math.PI * 2, d = W * (0.07 + rnd() * 0.1);
        x = Math.round(ax + Math.cos(ang) * d); y = Math.round(ay + Math.sin(ang) * d);
        if (x < 6 || y < 6 || x >= W - 6 || y >= H - 6 || !land(y * W + x)) continue;
        ok = settlements.every((s) => Math.hypot(s.x - x, s.y - y) > W * 0.06);
      }
      if (!ok) continue;
    }
    const tier = k === 0 ? (c === 0 ? 4 : 3) : ri(1, 3);
    const s = { id: sid++, name: nameFor(def.race), civId: c, race: def.race, x, y, tier: TIERS[tier], tierIdx: tier,
      population: 0, homeless: 0, unemployed: 0, happiness: 0, health: 0, isCapital: k === 0, buildingCount: 0,
      territoryRadius: 4 + tier * 2 + ri(0, 2), stockpile: {} };
    settlements.push(s);
    civ.settlements.push(s.id);
    if (k === 0) civ.capitalId = s.id;
  }
  civs.push(civ);
}
// territory: nearest settlement within its radius
for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
  const i = y * W + x;
  if (isWater(biome[i])) continue;
  let best = -1, bd = 1e9;
  for (const s of settlements) {
    const d = Math.hypot(s.x - x, s.y - y) / s.territoryRadius;
    if (d < 1 && d < bd) { bd = d; best = s.id; }
  }
  owner[i] = best;
}

// ---------------------------------------------------------------- buildings & roads
const occ = new Int32Array(N).fill(-1);
const buildings = [];
function canPlace(x, y, w, h, sidReq) {
  if (x < 1 || y < 1 || x + w >= W - 1 || y + h >= H - 1) return false;
  for (let yy = y; yy < y + h; yy++) for (let xx = x; xx < x + w; xx++) {
    const i = yy * W + xx;
    if (occ[i] >= 0 || !land(i) || (flags[i] & F.Road) || (sidReq >= 0 && owner[i] !== sidReq)) return false;
  }
  return true;
}
function place(type, x, y, s, civ, opts = {}) {
  const def = BT[BTI[type]];
  const w = opts.w || def[3], h = opts.h || def[4];
  if (!canPlace(x, y, w, h, s.id)) return null;
  const id = buildings.length;
  const complete = opts.complete !== undefined ? opts.complete : rnd() > 0.06;
  const ageIdx = AGES.indexOf(civ.age);
  const cap = type === 'House' ? ri(3, 5) : 0;
  const b = { id, type, category: def[1], shape: def[2], x, y, w, h, settlementId: s.id, civId: civ.id,
    styleRace: civ.race, styleAge: AGES[clamp(ri(Math.max(0, ageIdx - 3), ageIdx), 0, 9)], complete,
    progress: complete ? 1 : +(rnd() * 0.9).toFixed(2), residents: complete ? Math.max(0, cap - ri(0, 1)) : 0,
    capacity: cap, workers: type === 'House' ? 0 : complete ? ri(0, 8) : 0, condition: +(0.55 + rnd() * 0.45).toFixed(2) };
  for (let yy = y; yy < y + h; yy++) for (let xx = x; xx < x + w; xx++) occ[yy * W + xx] = id;
  if (def[2] === 'Field') for (let yy = y; yy < y + h; yy++) for (let xx = x; xx < x + w; xx++) flags[yy * W + xx] |= F.Farmland;
  buildings.push(b);
  return b;
}
function road(x0, y0, x1, y1) {
  let x = x0, y = y0, guard = 0;
  while ((x !== x1 || y !== y1) && guard++ < W * 4) {
    const i = y * W + x;
    if (!isWater(biome[i]) || (flags[i] & F.River)) flags[i] |= F.Road;
    const dx = x1 - x, dy = y1 - y;
    if (Math.abs(dx) > Math.abs(dy) || (Math.abs(dx) === Math.abs(dy) && rnd() < 0.5)) x += Math.sign(dx); else y += Math.sign(dy);
    if (rnd() < 0.15 && dy !== 0 && dx !== 0) { if (rnd() < 0.5) x += Math.sign(dx); else y += Math.sign(dy); }
  }
}
const civById = (id) => civs[id];
for (const s of settlements) {
  const civ = civById(s.civId);
  const ageIdx = AGES.indexOf(civ.age);
  // street grid: cross + ring streets
  const R = Math.max(3, Math.round(s.territoryRadius * 0.7));
  road(s.x - R, s.y, s.x + R, s.y); road(s.x, s.y - R, s.x, s.y + R);
  if (s.tierIdx >= 3) {
    road(s.x - R + 1, s.y - Math.round(R / 2), s.x + R - 1, s.y - Math.round(R / 2));
    road(s.x - R + 1, s.y + Math.round(R / 2), s.x + R - 1, s.y + Math.round(R / 2));
    road(s.x - Math.round(R / 2), s.y - R + 1, s.x - Math.round(R / 2), s.y + R - 1);
    road(s.x + Math.round(R / 2), s.y - R + 1, s.x + Math.round(R / 2), s.y + R - 1);
  }
  for (let yy = s.y - 1; yy <= s.y + 1; yy++) for (let xx = s.x - 1; xx <= s.x + 1; xx++) flags[yy * W + xx] &= ~F.Road;
  const tc = place('TownCenter', s.x - 1, s.y - 1, s, civ, { complete: true });
  if (!tc) { for (let yy = s.y - 1; yy <= s.y + 1; yy++) for (let xx = s.x - 1; xx <= s.x + 1; xx++) occ[yy * W + xx] = -2; }
  // civic & economy buildings
  const avail = BT.filter((d) => d[5] <= ageIdx && d[0] !== 'TownCenter' && d[0] !== 'House' && d[2] !== 'Field' && d[0] !== 'GreatLibrary');
  const nCivic = 2 + s.tierIdx * 3;
  for (let k = 0; k < nCivic; k++) {
    const d = pick(avail);
    for (let t = 0; t < 30; t++) {
      const ang = rnd() * Math.PI * 2, dist = 2 + rnd() * R * 0.9;
      if (place(d[0], Math.round(s.x + Math.cos(ang) * dist), Math.round(s.y + Math.sin(ang) * dist), s, civ)) break;
    }
  }
  if (s.isCapital && ageIdx >= 3 && civ.id <= 2) {
    for (let t = 0; t < 60; t++) if (place('GreatLibrary', s.x + ri(-R, R), s.y + ri(-R, R), s, civ)) break;
  }
  // houses: dense near center, sparser outward
  const nHouses = [4, 10, 22, 45, 80, 120, 160][s.tierIdx] + ri(0, 8);
  let placed = 0;
  for (let t = 0; t < nHouses * 25 && placed < nHouses; t++) {
    const ang = rnd() * Math.PI * 2, dist = 2 + Math.pow(rnd(), 1.4) * (R + 2);
    const big = ageIdx >= 4 && rnd() < 0.25;
    const x = Math.round(s.x + Math.cos(ang) * dist), y = Math.round(s.y + Math.sin(ang) * dist);
    // prefer road frontage
    let near = false;
    for (const [ox, oy] of [[1, 0], [-1, 0], [0, 1], [0, -1], [2, 0], [-2, 0], [0, 2], [0, -2]]) {
      const j = (y + oy) * W + (x + ox);
      if (j >= 0 && j < N && (flags[j] & F.Road)) near = true;
    }
    if (!near && rnd() < 0.7) continue;
    if (place('House', x, y, s, civ, big ? { w: 2, h: 1 } : {})) placed++;
  }
  // farms on the outskirts
  if (ageIdx >= 1) {
    const nFarms = 1 + s.tierIdx * 2;
    for (let k = 0, t = 0; k < nFarms && t < 300; t++) {
      const ang = rnd() * Math.PI * 2, dist = R * (0.8 + rnd() * 0.5);
      if (place(rnd() < 0.75 ? 'Farm' : 'Pasture', Math.round(s.x + Math.cos(ang) * dist), Math.round(s.y + Math.sin(ang) * dist), s, civ)) k++;
    }
  }
}
// inter-settlement roads (same civ: capital to each)
for (const civ of civs) {
  const cap = settlements.find((s) => s.id === civ.capitalId);
  for (const id of civ.settlements) {
    if (id === cap.id) continue;
    const s = settlements.find((q) => q.id === id);
    road(cap.x, cap.y + 2, s.x, s.y + 2);
  }
}
// walls around the human & dwarf capitals
for (const civ of civs.slice(0, 3)) {
  const s = settlements.find((q) => q.id === civ.capitalId);
  const r = Math.round(s.territoryRadius * 0.85);
  for (let a = 0; a < 360; a += 1.5) {
    const x = Math.round(s.x + Math.cos(a * Math.PI / 180) * r), y = Math.round(s.y + Math.sin(a * Math.PI / 180) * r);
    const i = y * W + x;
    if (x > 0 && y > 0 && x < W && y < H && land(i) && occ[i] < 0) flags[i] |= F.Wall;
  }
}
// ruins, sacred groves, scorched & burning patches
for (let k = 0; k < 10; k++) {
  const i = ri(0, N - 1);
  if (!land(i) || owner[i] >= 0) continue;
  const cx = i % W, cy = (i / W) | 0;
  for (let t = 0; t < 14; t++) { const x = clamp(cx + ri(-3, 3), 0, W - 1), y = clamp(cy + ri(-3, 3), 0, H - 1); if (land(y * W + x)) flags[y * W + x] |= F.Ruins; }
}
for (let k = 0; k < 4; k++) { const i = ri(0, N - 1); if (land(i)) flags[i] |= F.Sacred; }
for (let k = 0; k < 3; k++) {
  const i = ri(0, N - 1);
  if (!land(i)) continue;
  const cx = i % W, cy = (i / W) | 0, r = ri(3, 6);
  for (let y = cy - r; y <= cy + r; y++) for (let x = cx - r; x <= cx + r; x++) {
    if (x < 0 || y < 0 || x >= W || y >= H) continue;
    const d = Math.hypot(x - cx, y - cy);
    if (d <= r && land(y * W + x)) { flags[y * W + x] |= F.Scorched; flags[y * W + x] &= ~F.Trees; if (d < 1.5 && k === 0) flags[y * W + x] |= F.Burning; }
  }
}

// ---------------------------------------------------------------- settlement stats
for (const s of settlements) {
  const own = buildings.filter((b) => b.settlementId === s.id);
  s.buildingCount = own.length;
  const housed = own.reduce((a, b) => a + b.residents, 0);
  s.homeless = ri(0, Math.round(housed * 0.08));
  s.population = housed + s.homeless;
  s.unemployed = ri(0, Math.round(s.population * 0.12));
  s.happiness = +(0.35 + rnd() * 0.6).toFixed(2);
  s.health = +(0.45 + rnd() * 0.5).toFixed(2);
  const sc = s.population / 20;
  s.stockpile = {};
  for (const r of RES) if (rnd() < 0.85 || r === 'Food') s.stockpile[r] = Math.round(rnd() * 120 * sc * (r === 'Food' ? 3 : 1));
  delete s.tierIdx;
}
for (const civ of civs) {
  const ss = settlements.filter((s) => s.civId === civ.id);
  civ.population = ss.reduce((a, s) => a + s.population, 0);
  civ.militaryPower = Math.round(civ.population * (civ.race === 'Orc' ? 0.35 : 0.15) + ri(0, 40));
}

// ---------------------------------------------------------------- wars & armies
const wars = [
  { id: 0, attackerId: 3, defenderId: 0, startYear: YEAR - 14, endYear: -1, active: true, warScore: -0.18, casusBelli: 'Conquest' },
  { id: 1, attackerId: 4, defenderId: 1, startYear: YEAR - 6, endYear: -1, active: true, warScore: 0.32, casusBelli: 'Holy War' },
  { id: 2, attackerId: 2, defenderId: 3, startYear: YEAR - 120, endYear: YEAR - 97, active: false, warScore: 0.55, casusBelli: 'Border Dispute' },
  { id: 3, attackerId: 0, defenderId: 1, startYear: YEAR - 260, endYear: YEAR - 244, active: false, warScore: -0.1, casusBelli: 'Succession' },
];
const armies = [];
const states = ['Marching', 'Besieging', 'Idle', 'Retreating'];
for (const w of wars.filter((q) => q.active)) {
  const att = settlements.filter((s) => s.civId === w.attackerId), def = settlements.filter((s) => s.civId === w.defenderId);
  for (let k = 0; k < 3; k++) {
    const from = pick(att), to = pick(def), t = 0.35 + rnd() * 0.5;
    const soldiers = ri(40, 260);
    armies.push({ id: armies.length, civId: w.attackerId, x: Math.round(from.x + (to.x - from.x) * t), y: Math.round(from.y + (to.y - from.y) * t),
      strength: +(soldiers * (0.8 + rnd() * 0.6)).toFixed(1), soldiers, state: k === 2 ? 'Besieging' : pick(states), targetSettlementId: to.id });
  }
  const d = pick(def);
  armies.push({ id: armies.length, civId: w.defenderId, x: d.x + ri(-3, 3), y: d.y + ri(-3, 3), strength: ri(60, 200), soldiers: ri(50, 180), state: 'Idle', targetSettlementId: -1 });
}

// ---------------------------------------------------------------- history
const history = [];
const ageStart = (civIdx) => { const a = AGES.indexOf(civs[civIdx].age); return Array.from({ length: a + 1 }, (_, k) => Math.round((k / (a + 1)) * YEAR * 0.95)); };
const starts = civs.map((_, c) => ageStart(c));
const foundYear = [0, 0, 8, 22, 40];
for (let yr = 0; yr <= YEAR; yr += 4) {
  const entry = { year: yr, totalPopulation: 0, civs: [] };
  civs.forEach((civ, c) => {
    if (yr < foundYear[c]) return;
    const t = (yr - foundYear[c]) / (YEAR - foundYear[c]);
    let pop = civ.population / (1 + Math.exp(-9 * (t - 0.55))) * (1 + 0.04 * Math.sin(yr / 9 + c));
    if (c === 0 && yr > YEAR - 30 && yr < YEAR - 18) pop *= 0.82; // plague
    if (c === 3 && yr > YEAR - 110 && yr < YEAR - 95) pop *= 0.75; // dwarf war losses
    pop = Math.max(6, Math.round(pop * (0.97 + rnd() * 0.06)));
    if (yr === YEAR || yr + 4 > YEAR) pop = civ.population;
    let age = 0;
    starts[c].forEach((s, k) => { if (yr >= s) age = k; });
    const nS = Math.max(1, Math.round(civ.settlements.length * Math.min(1, t * 1.4)));
    entry.civs.push({ id: civ.id, population: pop, age: AGES[age], settlements: nS, buildings: Math.round(pop / 3.2) });
    entry.totalPopulation += pop;
  });
  history.push(entry);
}

// ---------------------------------------------------------------- chronicle
const chronicle = [];
const sName = (id) => settlements.find((s) => s.id === id).name;
function chron(year, category, text, civId = -1, settlementId = -1, x = -1, y = -1, importance = 1) {
  chronicle.push({ tick: year * 96 + ri(0, 95), year, category, text, civId, settlementId, x, y, importance });
}
chron(0, 'World', 'The world was shaped by the god\'s hand.', -1, -1, -1, -1, 3);
civs.forEach((civ, c) => {
  const cap = settlements.find((s) => s.id === civ.capitalId);
  chron(foundYear[c], 'Civilization', `The ${civ.race} people founded the ${civ.name}.`, c, cap.id, cap.x, cap.y, 3);
  starts[c].forEach((s, k) => { if (k > 0) chron(s, 'Age', `${civ.name} entered the ${AGES[k]} Age.`, c, -1, -1, -1, 3); });
  for (const id of civ.settlements) {
    if (id === cap.id) continue;
    const s = settlements.find((q) => q.id === id);
    const yr = ri(foundYear[c] + 30, YEAR - 20);
    chron(yr, 'Settlement', `Settlers from ${cap.name} founded ${s.name}.`, c, s.id, s.x, s.y, 2);
  }
});
const techNames = ['Pottery', 'Agriculture', 'Bronze Working', 'Writing', 'Masonry', 'Iron Working', 'Mathematics', 'Philosophy',
  'Engineering', 'Theology', 'Feudalism', 'Astronomy', 'Banking', 'Printing Press', 'Navigation', 'Gunpowder'];
for (let k = 0; k < 120; k++) {
  const c = ri(0, 4), civ = civs[c];
  chron(ri(foundYear[c] + 5, YEAR), 'Technology', `${civ.name} discovered ${pick(techNames)}.`, c, -1, -1, -1, 1);
}
for (let k = 0; k < 260; k++) {
  const b = pick(buildings);
  if (b.type === 'House' && rnd() < 0.7) continue;
  const s = settlements.find((q) => q.id === b.settlementId);
  chron(ri(Math.max(0, YEAR - 300), YEAR), 'Building', `${s.name} completed a ${b.type.replace(/([a-z])([A-Z])/g, '$1 $2')}.`, b.civId, s.id, b.x, b.y, b.category === 'Wonder' ? 3 : 1);
}
for (let k = 0; k < 120; k++) {
  const s = pick(settlements);
  const kind = rnd();
  if (kind < 0.5) chron(ri(10, YEAR), 'Birth', `A child of prophecy was born in ${s.name}.`, s.civId, s.id, s.x, s.y, 1);
  else chron(ri(10, YEAR), 'Death', `The elder ${nameFor(s.race)} of ${s.name} died of old age.`, s.civId, s.id, s.x, s.y, 1);
}
for (const w of wars) {
  const a = civs[w.attackerId], d = civs[w.defenderId];
  const target = settlements.find((s) => s.civId === d.id);
  chron(w.startYear, 'War', `${a.name} declared war on ${d.name} (${w.casusBelli}).`, a.id, target.id, target.x, target.y, 3);
  for (let k = 0; k < 4; k++) {
    const t = pick(settlements.filter((s) => s.civId === d.id));
    chron(ri(w.startYear, w.active ? YEAR : w.endYear), 'War', `Battle near ${t.name}: ${a.name} ${rnd() < 0.5 ? 'routed' : 'was repelled by'} the defenders.`, a.id, t.id, t.x + ri(-4, 4), t.y + ri(-4, 4), 2);
  }
  if (!w.active) chron(w.endYear, 'Diplomacy', `${a.name} and ${d.name} signed a peace treaty.`, a.id, -1, -1, -1, 2);
}
for (let k = 0; k < 40; k++) {
  const s = pick(settlements);
  const p = pick(['blessed the fields of', 'sent rain upon', 'struck lightning near', 'inspired the scholars of', 'healed the sick of']);
  chron(ri(50, YEAR), 'Divine', `The god ${p} ${s.name}.`, s.civId, s.id, s.x, s.y, 2);
}
for (let k = 0; k < 20; k++) {
  const s = pick(settlements);
  const p = pick(['A wildfire swept the outskirts of', 'An earthquake shook', 'Plague broke out in', 'Floods drowned the farms of']);
  chron(ri(30, YEAR), 'Disaster', `${p} ${s.name}.`, s.civId, s.id, s.x + ri(-5, 5), s.y + ri(-5, 5), 2);
}
chronicle.sort((a, b) => a.tick - b.tick);

// ---------------------------------------------------------------- assemble
const totalPop = civs.reduce((a, c) => a + c.population, 0);
const snapshot = {
  schema: 'godsim.snapshot/1',
  meta: { seed: SEED, tick: YEAR * 96 + 37, year: YEAR, width: W, height: H, preset: 'Continents', generator: 'make-sample.mjs (synthetic)', gameVersion: '0.1.0-sample' },
  enums: { biomes: BIOMES, buildingTypes: BUILDING_TYPES, categories: CATEGORIES, ages: AGES, races: RACES, tiers: TIERS, chronicleCategories: CHRON },
  tiles: { biome, elevation, flags, owner },
  civilizations: civs,
  settlements,
  buildings,
  armies,
  wars,
  chronicle,
  history,
  god: { mana: 734.5, maxMana: 1000, alignment: 0.27, powersCast: 58 },
  stats: { totalBirths: Math.round(totalPop * 6.3), totalDeaths: Math.round(totalPop * 5.3), buildingsCompleted: buildings.length + 211,
    buildingsDestroyed: 211, settlementsFounded: settlements.length + 3, warsDeclared: wars.length, battlesFought: 23, peakPopulation: Math.round(totalPop * 1.04) },
};
writeFileSync(OUT, JSON.stringify(snapshot));
console.log(`wrote ${OUT}: ${W}x${H}, ${civs.length} civs, ${settlements.length} settlements, ${buildings.length} buildings, ${chronicle.length} chronicle entries`);
