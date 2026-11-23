# GodSim Art Style Guide — "Storybook Monumental"

> **Gravitas in the silhouette, whimsy in the details.**
> The architecture is serious: real construction logic, the right materials for each age, and believable
> function. The proportions and details are playful: chunky, slightly exaggerated forms and lovingly
> placed little touches that reward a closer look.

## 1. Pillars
1. **Readable from the god's eye.** The camera is high and at 3/4 view (about 32° pitch). Silhouettes must read at 64 px:
   - strong roof shapes;
   - clear value contrast between roof and walls;
   - at most 5 main colours plus 1–2 accents per model.
2. **Serious bones.** Buildings are built the way their age would build them:
   - posts carry beams;
   - arches carry weight;
   - Industrial chimneys rise from the boiler house;
   - a farm has fields, and a mine has a headframe and spoil heap.
   No floating parts and no impossible overhangs.
3. **Playful flesh.**
   - Roofs are tall and chunky, 40–60% of total height on houses.
   - Walls are thick, beams oversized and plinths sturdy.
   - Small deliberate imperfections: chimneys lean 3–8°, `wobble(0.005–0.02)` adds a hand-made feel.
   - 2–4 charming details per building: flower boxes, laundry lines, barrels, crates, lanterns, banners, crooked signboards, birdhouses, wind chimes, stacked firewood, a cat-sized statue.
4. **Age tells the story.** The material ladder: thatch & hide → mudbrick → fieldstone → marble & columns → stone & timber → terracotta & domes → red brick & smokestacks → concrete & steel → glass towers → luminous white/gold. The same building type for the same race must look clearly *older* or *newer* across ages while staying recognisably the same race.
5. **Race tells the soul.** Each race has signature motifs (below) visible on *every* building it owns.

## 2. Races
| Race | Feel | Signature motifs | Roof | Proportions |
|---|---|---|---|---|
| Human | hopeful, orderly, homely | timber framing, flower boxes, chimneys, shutters, blue banners, cobbled plinths | gable / hip | balanced, upright |
| Elf | graceful, organic, ancient | living trunks, leaf canopies, curved ribs, lantern pods, moon-silver accents, vines; nothing square | leaf / dome | tall, slender, round (roundness 0.85) |
| Dwarf | stout, heavy, proud | carved stone, rune bands, brass trim, half-sunk halls, forge glow, thick buttresses | flat / tiered | squat (0.8 h), wide (1.2 w) |
| Orc | loud, rugged, proud warrior camps | hide & lashed timber, tusk/bone spikes, bold war-paint stripes, horned totems (stylised, never gory), big drums | cone / spiked | chunky, sprawling, spiky silhouettes |
| Saurian | patient, mystical, sun-worshipping | stilts over water, stepped pyramids, gold sun discs, reed thatch, turquoise inlays, star-chart carvings | pyramid / stepped | low wide bases, stepped |

Use `forge.style.RACE_STYLE[race]` and `style.blended(race, age, key)` for colours, so race identity survives every age.

## 3. Ages (material palette: `forge.style.AGE_MATERIALS`)
Dawn (thatch, hide, raw timber) · Bronze (mudbrick, ochre, copper accents) · Iron (fieldstone, dark timber) ·
Classical (white marble, terracotta, columns) · Medieval (grey stone, half-timber, slate) · Renaissance
(warm plaster, terracotta domes, arches) · Industrial (red brick, soot, smokestacks, iron girders) · Modern
(concrete, steel, big windows) · Information (glass bands, white panels, cyan accents, antennae) · Ascension
(pearl white, gold, floating-looking (but supported) rings, emissive cyan).

## 4. Technical rules (enforced by `forge.registry.validate`)
- **Space:** Y-up; 1 unit = 1 tile; ground at y = 0; pivot at ground centre; footprint centred on the origin; front/door faces **−Z**.
- **Size:** the model fits its footprint W×D tiles (≤ +25% overhang; roofs and props may overhang slightly).
- **Triangle budgets:** house 1800, building 3500, townhall 4500, wonder 9000, prop 500, nature 700, unit 700, vehicle 1500, animal 500, kit 900, fx 400.
- **Shading:** flat shaded with vertex colours (COLOR_0). Materials:
  - `base` for normal surfaces;
  - `emissive` for lit windows, forge glow and Ascension light;
  - `metal`/`glass` are optional.
- **Geometry hygiene:**
  - No coplanar z-fighting: offset decals and windows by 0.01.
  - No NaN and no degenerate triangles.
  - Keep surfaces closed where visible.
- **Determinism:** seed every builder (`MeshBuilder(seed=asset_id)`), so outputs are identical on every run.
- **IDs:** `[a-z0-9_]`, e.g. `bld_dwarf_mine_bronze_v0`, `nat_tree_oak_v2`, `unit_orc_soldier_iron`, `prop_market_stall_v1`.

## 5. Quality bar & process (every artist agent)
1. Sketch the silhouette first: footprint, massing, roof.
2. Add the structure: posts, plinths, beams, buttresses.
3. Add the race motifs.
4. Add the age materials.
5. Add 2–4 fun details.
6. Apply wobble.
7. **Look at your previews**: `tools/AssetForge/previews/...png` via the image viewer. Check the silhouette at thumbnail size, colour balance (no mud), the details' readability, and front/door orientation. Revise at least twice.
8. Check that the same building across ages and races reads as one family.
9. Never copy or imitate existing franchises' iconic designs. Everything is original.
