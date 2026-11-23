# GodSim Web Viewer

A standalone browser viewer for world snapshots exported by the headless tool
(`tools/Headless/`, schema `godsim.snapshot/1`). You can inspect a simulated world without Unity.
It is one self-contained file, `index.html`, with inline CSS and JS. It needs no build step,
no CDN and no network access.

## Files

| File | Purpose |
|---|---|
| `index.html` | The viewer. |
| `make-sample.mjs` | Node script with no dependencies. It generates a synthetic `sample-snapshot.json` for testing. |
| `sample-snapshot.json` | A 192×192 synthetic world: 5 civs, 17 settlements, about 800 buildings, roads, history and a chronicle. |
| `screenshots/` | Reference screenshots from the Playwright check: desktop 1280×800 and phone 390×844. |

## Loading a snapshot

There are four ways to load one:

1. **Drag and drop** a `.json` snapshot anywhere on the page.
2. Use the **Open snapshot…** file picker.
3. **`?src=`**: `index.html?src=snapshot.json` fetches a file served next to the page. Only
   same-origin relative URLs are accepted. Absolute, protocol-relative and cross-origin URLs are refused.
4. **Embedded data**: paste the snapshot JSON into
   `<script id="snapshot-data" type="application/json"></script>` at the bottom of `index.html`.
   When the slot is not empty, it loads automatically. Escape `<` as `<`, for example
   `json.replace(/</g, '\\u003c')`, so that a `</script>` inside a string cannot end the tag.

Browsers block `fetch` from `file://`, so `?src=` needs a local server:

```bash
cd tools/WebViewer
node make-sample.mjs                 # optional: regenerate the sample (--size 192 --seed 1337 --out file.json)
python3 -m http.server 8000
# open http://localhost:8000/index.html?src=sample-snapshot.json
```

Drag and drop and the embedded slot also work when you open the file directly.

## Features

- **Map** (canvas). Terrain is drawn once into an offscreen image: a stylized biome palette, a
  hillshade from elevation, water depth, and overlays for river, road, farmland, ruins, scorched,
  burning, wall and sacred tiles. On top of the terrain the map draws:
  - territory tint in civ colors, with crisp inset vector borders;
  - buildings as footprint rectangles, colored by category with a darker outline;
  - unfinished buildings, hatched with a dashed outline;
  - settlement markers sized by tier, with a star for capitals, and labels that avoid collisions;
  - armies as civ-colored chevrons that point at their target, with a dashed ring when besieging;
  - deposit dots when zoomed in close.
- **Navigation**. Drag to pan. Zoom with the wheel, a trackpad or a pinch; zoom stays centered on
  the cursor or fingers. The map has buttons for zoom in, zoom out and fit, plus animated fly-to.
  Keyboard: `+` and `-` zoom, `0` fits the world, the arrow keys pan (`Shift` pans in bigger steps),
  and `Esc` clears the selection.
- **Hover tooltip** shows the tile's biome, elevation, flags, owner settlement and civ, the
  building, and any settlement or army under the cursor.
- **Click selection**. The click picks the first thing it finds, in this order: settlement marker,
  army, building, the settlement that owns the tile. The pick opens in the **Inspect** tab.
- **Overview tab**:
  - world numbers and god mana and alignment;
  - the civilization list: color, race, age badge, population, settlements, techs and at-war
    tags. Clicking a civ highlights its territory and flies the map to it;
  - the war list;
  - global stats.
- **Inspect tab**. It covers settlements (happiness and health bars, stockpile table), buildings,
  civilizations and armies. Settlements and civs also get a hand-written SVG **stacked bar of
  buildings by category**. For a civ there is one bar per settlement; for a settlement there are
  bars for complete and in-progress buildings.
- **History tab**. A multi-line SVG chart of population per civ over the years, in civ colors. It
  has a crosshair hover readout, a legend that toggles each civ, a table view and a ranking of
  current populations.
- **Chronicle tab**. You can search by words, which all must match, and filter by category and by
  civilization. The list shows entries in pages. Clicking an entry that has coordinates flies the
  map there and pings the spot.
- **Layer toggles**: territory, buildings, roads, labels, armies and hillshade.
- **Themes**: light and dark, with CSS custom properties on `:root`. By default the page follows
  `prefers-color-scheme`. The **Theme** button cycles Auto, Light and Dark, and the choice is
  remembered in `localStorage`.
- **Layout**: a compact layout with the map and a side panel. Below 860px it becomes a single column.

## Security and robustness

- Snapshot strings never reach `innerHTML`. All DOM is built with `createElement` and `textContent`.
  Colors are accepted only as `#rrggbb`.
- Every field is optional. Numbers are checked with `Number.isFinite` and clamped. Strings are
  trimmed, length-capped and stripped of control and bidi characters. Enum values may be names or
  indices, and unknown values fall back safely.
- Caps:
  - tiles: 4,194,304, so a 2048×2048 map fits;
  - buildings: 500k;
  - chronicle: 50k;
  - settlements: 100k;
  - armies: 50k;
  - history: 20k;
  - file size: 512 MB.
  The viewer notes any truncation in the status bar.
- Partial snapshots still render. Missing tiles give a neutral grid, and the map size can be
  inferred from square tile arrays or from entity extents. Panels without data show friendly
  placeholders, and invalid JSON shows an error message.
- A CSP meta tag disables `object`, `base` and form targets.

## Performance

- Terrain, territory tint, a far-zoom building raster and a tile-to-building index are built
  once per snapshot. Territory borders are built once as one `Path2D` per civ.
- Buildings are culled with a 32-tile spatial grid and batched into one path per category and
  state, so each frame uses about 30 fill and stroke calls.
- Below 3 px per tile, the viewer draws the prebuilt building raster instead of vectors.
- Drawing only happens on demand, through `requestAnimationFrame`.
- Measured in headless Chromium on a 512×512 world with 50,000 buildings: load and preparation
  took about 350 ms, and the median frame time was about 16.7 ms (vsync) at 1.4, 5 and 20 px per
  tile.

## Verification

The viewer was checked with the preinstalled Playwright and Chromium at 1280×800 and 390×844.
The checks were:

- no console errors or page errors;
- the map canvas does not have uniform pixels;
- the civ list has 5 entries;
- chronicle search and category filtering work;
- there is no horizontal page scroll;
- malformed and hostile snapshots, including XSS strings, bad types and oversized dimensions,
  load without errors and without injected markup.
