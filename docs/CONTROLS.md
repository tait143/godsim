# GodSim — Controls

Default mouse and keyboard bindings. All input goes through `InputAdapter`, so every binding works
with the **legacy Input Manager** and with the **new Input System**
([UNITY_SETUP.md §6](UNITY_SETUP.md#6-input-system)).

These bindings were checked against `Assets/GodSim/Unity/Runtime/Interaction/` (`GodCameraController`,
`SelectionController`, `GodPowerController`, `GameShortcuts`) and `UI/GodSimHUD.cs` on 2026-11-19.
Entries marked **(design target)** are planned but were not found in code.

> **Known issue (C14 in GAME_DESIGN §20.3).** The demo scene adds both `GameShortcuts` and
> `GodSimHUD`, and each handles **Space** and **+/−** on its own. The power hotkeys are handled by
> both `GodPowerController` and the HUD. Until one owner is chosen, a single press can be applied
> twice: Space pauses and unpauses, + jumps two speed levels, and a digit selects then deselects.
> **Workaround:** untick *Handle Time Hotkeys* and *Handle Power Hotkeys* on the `GodSimHUD`
> component.

## Camera (`GodCameraController`)

The camera works while paused (unscaled time), is clamped to the map, and ignores mouse input over
the HUD.

| Action | Keyboard | Mouse |
|---|---|---|
| Pan | `W` `A` `S` `D` or arrow keys (speed scales with height) | **Middle-drag** (grabs the ground) |
| Fast pan | Hold `Shift` while panning | — |
| Edge scroll | — | Cursor at the screen edge, 12 px border. Toggle: `EdgeScrollEnabled` / Inspector |
| Zoom | `PageUp` / `PageDown` | Scroll wheel (smooth, toward the cursor) |
| Rotate | `Q` / `E` | **Right-drag** |
| Focus selection | `F` | — |
| Jump to the largest settlement | `Home` (most populous living settlement) | — |
| Jump anywhere | — | Left-click the minimap |
| Follow selection | `Shift+F` **(design target)** | Double-click **(design target)** |
| Cycle settlements | `Tab` / `Shift+Tab` **(design target)** | — |

Height runs from 8 to 160 world units, and pitch follows zoom. Camera shake comes from wrath VFX
(`AddShake`).

## Selection (`SelectionController`)

| Action | Input |
|---|---|
| Select | **Left-click** when no power is armed. It resolves, in order: the building on the tile (which also selects its settlement), the nearest army within 1.5 tiles, the settlement that owns the tile, or just the tile |
| Clear selection | `Esc`, or click off the map |
| Hover | Hovered and selected tiles are outlined. Building footprints are outlined whole |
| Inspector (`HudInspector`) | **Settlement:** population, homeless, unemployed, happiness, health, faith, unrest, services provided, builders wanted, *recent AI decisions*. **Building:** status, condition, efficiency, level, workers or residents, style (race and age), construction progress, paid or "waiting for resources", on fire. **Army:** state, strength, morale, soldiers, target ("Show target"). **Tile:** biome, elevation, moisture, temperature, fertility, features, resource left, owner. Every panel has a **Focus camera** link |

Selections are checked again every frame. A destroyed building or a dead army drops out of the
selection.

## God powers (`GodPowerController`, HUD power bar)

There are 19 powers on 19 hotkeys, in `GodPowerType` order. Number keys `1`–`9` and `0` are the
first ten, and `Shift` + `1`–`9` are the rest. The power bar shows them as `1…9, 0, S+1…S+9`.

| Key | Power | Category | | Key | Power | Category |
|---|---|---|---|---|---|---|
| `1` | Bless | Blessing | | `Shift+1` | Reveal Ore | Shaping |
| `2` | Rain | Blessing | | `Shift+2` | Spawn Tribe | Creation |
| `3` | Fertility | Blessing | | `Shift+3` | Lightning | Wrath |
| `4` | Inspire | Blessing | | `Shift+4` | Wildfire | Wrath |
| `5` | Heal | Blessing | | `Shift+5` | Earthquake | Wrath |
| `6` | Bountiful Harvest | Blessing | | `Shift+6` | Meteor | Wrath |
| `7` | Prophet | Blessing | | `Shift+7` | Plague | Wrath |
| `8` | Raise Land | Shaping | | `Shift+8` | Flood | Wrath |
| `9` | Lower Land | Shaping | | `Shift+9` | Drought | Wrath |
| `0` | Plant Forest | Shaping | | | | |

| Action | Input |
|---|---|
| Arm a power | Its hotkey, or click it on the power bar. Press the **same hotkey again to disarm** |
| Cast | **Left-click** on the world |
| Radius | `[` / `]`, or **`Ctrl` + scroll wheel**. Clamped to the power's `MinRadius`–`MaxRadius` (BALANCE §6) |
| Paint terraform | **Hold** left mouse with Raise Land, Lower Land or Plant Forest armed. It casts again at most every **0.15 s** until you release or a cast fails (mana, cooldown or rate limit) |
| Cancel | `Esc`, or **right-click** (press and release without dragging, so right-drag still rotates) |
| Spawn Tribe race | Pick the race in the power bar's **race picker** (`HudPowerBar`) after arming Spawn Tribe, then click open land |
| Radius by mouse | The power bar's radius slider for the selected power |
| Repeat the last power | `R` **(design target)** |

Modifier rules: hotkeys are ignored while `Ctrl` or `Alt` is held, which keeps those chords free for
the editor and the OS.

**Targeting preview.** A terrain-following ring shows the radius. It is gold for blessings, green
for shaping, cyan for creation and red for wrath. It turns **grey** when the power can't be
afforded or is cooling down.

**Results.**

- A successful cast plays VFX and a sound, shows a toast, and adds a chronicle entry if the effect
  matters.
- A rejected cast shows the validator's reason (for example "Not enough mana: …" or a radius out of
  range) and costs nothing.
- Commands pass through the queue and rate limiter: 4 per tick globally, plus a per-source bucket.
  **Casts made while paused are queued and resolve on the next tick.** Hold-to-paint while paused
  can fill the bucket.

## Time

| Action | Input |
|---|---|
| Pause / resume | `Space` |
| Faster | `=` / `+` / `Keypad +` / `.` |
| Slower | `-` / `Keypad -` / `,` |
| Speed levels | **1, 4, 16, 64, 240 ticks per second** (1 tick = 1 day, 96 per year). That is 96 s, 24 s, 6 s, 1.5 s or 0.4 s per sim year. Speeding up from pause also unpauses |
| Step one day while paused | `Ctrl+.` **(design target)** |

Speed never changes results. The simulation is deterministic, and the runner only decides how
many ticks run per frame, within an 8 ms budget and with at most 2 s of backlog. The top bar
shows the date, age, mana, alignment and population.

## Panels and menus

| Key | Panel |
|---|---|
| `F1` | Help overlay: controls and the power reference |
| `F3` | Performance overlay: FPS, sim ms per tick, per-system timings and faults, entity counts |
| `Esc` | Closes the innermost thing first: text field focus → open menu (back) → help → armed power → otherwise opens the **Esc menu** (new world, save, load, settings, quit). The sim pauses while the menu is open (`_pauseWhileMenuOpen`) |
| `F2` (chronicle), `F4` (overlay cycle), `F5`/`F9` (quick save and load), `F12` (screenshot) | **(design target)**. The chronicle feed and save and load are available on the HUD and in the Esc menu today |

**Overlays** (`Overlays/OverlayMode`): Territory, Population Density, Happiness, Health, Faith,
Fertility, Resources, Elevation, Moisture, Temperature, Danger, Wealth, Age and Religion. The data
model and color ramps exist. The overlay renderer, its HUD selector and an `F4` cycle hotkey are
**(design target)**: nothing references `OverlayMode` outside its folder yet.

## Minimap

| Action | Input |
|---|---|
| Jump the camera | Left-click the minimap (`HudMinimap`, bottom-right) |
| Drag the view | Left-drag on the minimap |
| Read it | Terrain colors with settlement markers. Texel (x, y) is tile (x, y) |
| Open a settlement | Click its marker **(design target)**. Today, jump there and click the town |
| Event pings | Battles, fires, famine and disasters pulse **(design target)** |

## Chronicle

| Action | Input |
|---|---|
| Read | The bottom-left chronicle feed (`HudChronicle`), newest first, colored by category. Toasts (`GodSimToasts`) for notable entries |
| Jump to an event | Click an entry that has a location. The camera focuses there and selects its settlement |
| Filter | By category (World, Birth, Death, Building, Settlement, Civilization, Age, Technology, War, Diplomacy, Divine, Disaster) and by minimum importance (0 minor … 3 legendary) |
| Follow one civ | Civ filter **(design target)**. The civilizations panel (`HudCivPanel`) jumps to a civ's capital |

## Tutorial

The first-run tutorial (`Unity/Runtime/Tutorial`) walks through 14 steps: welcome, camera,
peoples, inspect, time, chronicle, mana, bless, shape, build, ages, wrath, objectives, free play. It
advances when you do the thing it asks: move the camera, change the speed, cast Bless, and so on.
Skippable steps can be skipped.

The script and content are on disk. The component that shows the tutorial in the demo scene is
**(design target)**: nothing outside the Tutorial folder references it yet.

## Accessibility options

| Option | Status |
|---|---|
| Edge scroll toggle | Done |
| UI scale (`GodSimHUD.UiScale`, on top of DPI scale) | Done |
| Key remapping, hold-to-paint toggle mode, reduced shake and flash | **(design target)**, in Settings → Controls and Accessibility |
