# GodSim building art direction review

**The coordinator’s 101 RC4 lineages passed independent art direction after nine corrections.** All 53 final sheets and the current 1,034-model evidence set are signed. The inherited Elf FishingHut observation remains outside this scope. [Signed RC4 disposition](#signed-rc4-disposition)

## First full assembly, 23 November 2026

**Catalog disposition: hold 34 model IDs across 16 functions for revision.** All 71 packages had independent model approvals when this assembly was produced. Viewing the models beside neighboring functions and ages exposed three function collisions and several age transitions that are difficult to read at phone scale. The holds below apply to this assembly.

The catalog manifest contains 1,034 models, 2,130 gameplay lookup keys and 75 functions. The 19 canonical family pages display 2,096 age previews: 413 Human, 415 Elf, 416 Dwarf, 416 Orc, 415 Saurian and 21 neutral. I opened all 19 pages as images and compared every occupied cell horizontally across applicable ages and vertically across races and house variants. Blank cells correspond to combinations outside each model’s assigned scope. The neutral wonders occupy a single shared row.

I also opened one mixed-function contact page for each race and each age, using the 122 px and 48 px views. Individual source PNGs resolved close calls, including the Elf airship and its mooring supports, the Dwarf smith’s anvil and smelter outlet, all five future Hospital/MedicalCenter pairs, the three Dwarf Industrial mine types, and late farm variants. I compared the weakest neighboring ages again as pairs cropped directly from the canonical 64 px cells. Colour differences screened pairs for inspection; appearance at 64 px determined the findings.

### Revisions required

| Comparison | Exact model IDs | Observed problem and acceptance at 64 px |
|---|---|---|
| Dwarf specialist mines | `bld_dwarf_coal_mine_industrial_v0`<br>`bld_dwarf_coal_mine_future_v0` | A rear pipe is the main silhouette change from ordinary Mine. Add a clearly projecting loading chute and bulk bunker. Keep the shared headframe. |
| Dwarf specialist mines | `bld_dwarf_gold_mine_ancient_v0`<br>`bld_dwarf_gold_mine_classical_v0`<br>`bld_dwarf_gold_mine_industrial_v0`<br>`bld_dwarf_gold_mine_future_v0` | Gold nuggets change the cart and a small patch of the same Mine roof. Give the gold works an exposed stepped ore-washing or sorting installation that changes the outline. |
| MedicalCenter and Hospital | `bld_elf_medical_center_future_v0`<br>`bld_dwarf_medical_center_future_v0`<br>`bld_orc_medical_center_future_v0` | The clinic cross and front entrance occupy the same position on almost the same three masses. Elf changes chiefly the main roof colour, Dwarf one skylight, Orc one roof tube. Add a substantial dedicated wing, raised ward or projecting triage entrance. Keep the race’s architectural treatment. |
| IndustrialFarm and Farm | `bld_dwarf_industrial_farm_industrial_v0`<br>`bld_dwarf_industrial_farm_future_v0`<br>`bld_orc_industrial_farm_industrial_v0`<br>`bld_orc_industrial_farm_future_v0` | The shed, planting rows and silo position duplicate Farm. A narrow bar and taller tanks disappear into that outline. Give the mechanized beds a substantial glasshouse, harvester or moving gantry large enough to read at 64 px. Preserve some visible crop rows. |
| Information and Ascension hydroponics | `bld_human_hydroponics_tower_future_v0`<br>`bld_elf_hydroponics_tower_future_v0`<br>`bld_dwarf_hydroponics_tower_future_v0`<br>`bld_saurian_hydroponics_tower_future_v0` | Blue domes, planted bands and light plinths dominate both ages. A brighter blue crown or small trim change is insufficient. Use a broad age-responsive crown or tier fascia that clearly changes from Information steel/cyan to Ascension pearl/gold. Preserve the planted bands. |
| Early storehouse | `bld_orc_storehouse_ancient_v0` | Dawn, Bronze and Iron retain the same brown canopy, loading door and crate. Carry age materials onto the large roof or wall panels and keep the small horn tips and loading frame. |
| Early farms | `bld_human_farm_ancient_v0`<br>`bld_elf_farm_ancient_v0`<br>`bld_saurian_farm_ancient_v0` | Dawn and Bronze read as the same brown plot and shed. In the Elf case the main canopy shifts between close greens. Use a visible age-aware shed wall or roof surround and irrigation or bed-edge material. |
| Early pastures | `bld_orc_pasture_ancient_v0`<br>`bld_saurian_pasture_ancient_v0`<br>`bld_elf_pasture_ancient_v0` | Bronze and Iron share the same yard, fence and dominant shelter. Give the siding or shelter a clearly distinct age value and reinforce it on a larger section of the enclosure. |
| Early Saurian shelters and extraction | `bld_saurian_woodcutter_camp_ancient_v0`<br>`bld_saurian_fishing_hut_ancient_v0`<br>`bld_saurian_hunting_lodge_ancient_v0`<br>`bld_saurian_gatherer_hut_ancient_v0`<br>`bld_saurian_stone_quarry_ancient_v0` | The shelters retain the same yellow roof and timber base across Dawn/Bronze. Their tool, catch or crop props correctly identify the job. At the quarry, brown lifting gear and gray stepped rock stay almost constant. Put age contrast on a large roof surround, lower wall or built quarry platform. Keep cut stone and raw wood recognisable. |
| Early palisades and Elf totem | `bld_elf_palisade_ancient_v0`<br>`bld_orc_palisade_ancient_v0`<br>`bld_elf_spirit_totem_ancient_v0` | The two fence lines are near-identical brown in Bronze and Iron. Use an age-material gate or continuous bound rail. Dawn and Bronze Elf totems have the same forked trunk, crescent and gray plinth. An evolving broad ritual stone or mounting band can preserve the tree. |
| Late aqueduct and fishing hut | `bld_elf_aqueduct_future_v0`<br>`bld_orc_fishing_hut_future_v0` | Elf arches and the blue channel are almost invariant between Information and Ascension. Apply the age contrast to visible outer piers or fascia. The Orc hut retains its red/white roof, canoe and dock, leaving little age signal. Keep the red and give the roof surround or dock face a broader pearl/gold finish. |

For re-review, use the updated age preview beside its original neighbor, or the revised specialist beside the ordinary building in the same race and age. Confirm the affected age or function from the major shapes or materials at 64 px and check the larger image for support and retained race cues. Revised geometry and surface assignments also need current independent package evidence.

### Coverage by family page

| Page | Functions | Visual comparison |
|---|---|---|
| 01 | Airport, Aqueduct, Arcology, Arena | An airship moored beside the Elf terminal and the Saurian beacon distinguish the airports. Water atop repeated arches, inhabited towers and open seating bowls carry the other three functions. Elf Aqueduct has a weak final age transition. |
| 02 | Armory, AscensionGate, Bank, Barracks | Weapon displays and shields sit under long workshop roofs. Vault emblems and formal entries separate the banks from the garrison halls. The neutral gate’s supported ring reads in profile. |
| 03 | Bathhouse, Blacksmith, Campfire, Castle | Open turquoise baths, a forge and anvil, a central flame, and defended corner towers give four different silhouettes. The Dwarf smith’s broad anvil contrasts with the Smelter’s larger furnace and pouring outlet on page 15. |
| 04 | Cathedral, CoalMine, Colossus, DataCenter | Aisles or paired towers make the cathedrals read as civic landmarks. The Colossus pose survives every age. Vents, banks of servers or cylinders identify data centers. Dwarf CoalMine needs a distinct bulk-handling profile. |
| 05 | DeepForge, ElectronicsPlant, Factory, Farm | The Dwarf forge is recessed against rock. The plants use housings or repeated equipment, while factories use production sheds and stacks. Crop rows anchor all farms. Three early-age farms have weak age cues. |
| 06 | FishingHut, Forum, Foundry, FusionReactor | Small boats identify the fishing buildings across roof styles. Forums frame open space, foundries emphasize vessels and orange heat, and fusion cores use rings or luminous domes. Two FishingHut age transitions are held. |
| 07 | GathererHut, GoldMine, Granary, GrandBazaar | Produce baskets and clustered bins distinguish gathering from bulk grain storage. The neutral bazaar has domes, vaults and stall awnings. Dwarf GoldMine overlaps the ordinary mine. The Saurian gatherer’s early age is unclear. |
| 08 | GrandCathedral, GreatLibrary, Guildhall, Harbor | The clustered Human cathedral spires and the neutral library’s single dome make distinct landmarks. Guildhalls stack marked entrance volumes. Harbors combine a landing, boat and cargo or hoist. |
| 09 | Herbalist, Hospital, House, HuntingLodge | Planters or a mortar identify herbalists. Hospital emblems sit at the main door. Both House variants were compared in every age and race. Elves use attached leaf pods, Dwarves low wide roofs, Orcs horned fabric, Saurians stepped wings and Humans domestic gables. The Saurian hunting shelter needs a clearer early age. |
| 10 | HydroponicsTower, IndustrialFarm, Library, Market | Planted decks define the towers. Several towers retain nearly the same crown and terraces in both late ages. IndustrialFarm separates from Farm through greenhouse masses in Human and Elf, and taller twin tanks in Saurian. Dwarf and Orc need broader equipment. Book forms and stall awnings give the bottom row strong function cues. |
| 11 | MedicalCenter, MilitaryBase, Mill, Mine | The Human medical center adds several floors. The Saurian center adds ward wings and a marked entrance canopy. Three other medical centers remain too close to their hospitals. Dishes distinguish military bases. Wind rotors and waterwheels distinguish mills. The ordinary Dwarf Mine supplies the comparison for both specialist mines. |
| 12 | Monastery, Observatory, OilWell, Palisade | Courtyards, exposed optics, pumpjacks and uninterrupted defensive fences read across all rows. The Elf’s curved wall and the Orc’s lashed wall each need a clearer Bronze-to-Iron change. |
| 13 | Pasture, PowerPlant, Printworks, RailStation | Enclosed grass and livestock establish pasture, with three weak early-age transitions. Cooling towers and generating halls, projecting presses or paper stacks, and platforms beside rails identify the other functions. |
| 14 | ResearchLab, Sawmill, Scriptorium, Shrine | Lab instruments or vessels, log feed lines, a writing bench or quill, and an open-sided altar give the buildings distinct work areas. The Saurian quill remains visible above the long roof. |
| 15 | Smelter, SpiritTotem, Stadium, StoneQuarry | Tapered furnaces keep their form across races. Each totem has a distinctive symbol. Turf and larger spectator stands separate stadiums from arenas. Stepped stone and lifting gear establish the quarry. Elf totem and Saurian quarry need clearer early-age materials. |
| 16 | StoneWall, Storehouse, SunPyramid, Tavern | Broad defensive parapets, warehouse loading doors, the Saurian axial stair and disc, and an inn’s sign, barrels or awning establish separate uses. The Orc warehouse roof reads brown across all three early ages. |
| 17 | Temple, Theater, TownCenter, University | Temples frame ritual entrances. Theaters expose a stage or semicircle. Town centers were compared through all ten ages, from raised halls or tents to towers and rings. An Elf courtyard, Human wings and Dwarf or Orc grouped teaching halls retain campus structure. |
| 18 | WarIdol, Watchtower, Weaver, Well | The Orc idol remains an unmistakable horned figure. Tower lookouts, exposed looms and hanging cloth, and the round rim with suspended well gear survive reduction. |
| 19 | WoodcutterCamp, Workshop, WorldTree | Logs plus stump or rack establish a working timber camp. Benches and workpieces distinguish workshops. The living trunk, canopy and entry identify the Elf wonder throughout. The Saurian camp roof and timber need a stronger early-age contrast. |

Across the set, the strongest race cues are the Elf’s leaf curves and split pods, the Dwarf’s broad, low beam-and-stone masses, the Orc’s horn tips, red panels and lashed structures, the Saurian’s stepped platforms and turquoise sun ornament, and the Human’s timber, shutters, gables and upright civic flags. Rows such as Well, Shrine, Mill and House retain different construction methods even where the camera and age palette coincide. In later ages, the change from industrial brick to concrete to glass generally reads at thumbnail scale. Gold trim confined to a few pixels, and unchanging hide or reed roofs in the early ages, account for the held exceptions.

This review covers offline art renders from one fixed angle, with scale normalized to each preview. Relative scale in a settlement, perspective changes, occlusion, runtime LOD switching, Unity lighting, batching and iPhone rendering/performance require separate evidence. The sheet review supplies no Unity or device test result.

### Image fingerprints for the first full assembly

SHA-256 values below identify the observed canonical PNGs under `tools/AssetForge/previews/_sheets/`. The corresponding `Assets/GodSim/Art/Models/manifest.json` SHA-256 is `9c1a17c0bc0cd90ad8fba6dfe4cd82c3edac77a6302b49f97bd70ba012b3a331`.

| Canonical sheet | SHA-256 |
|---|---|
| `families/01.png` | `59f35406dd48a6478740f8e1542e7bc11ee4711eda31b62b79edd794fb068d5a` |
| `families/02.png` | `d9d4a9cbd624c3b054b485d5d9bca708e3b3c85d8f0136407de49c467e0c1512` |
| `families/03.png` | `33dbdae52097b20a1015e09aa5c9147d254bb35856d7ece025957289b5efe132` |
| `families/04.png` | `9c9edd5820bbe96e344bd6bd7254c41d48eab228de7247e892d4e9154c9aa9cb` |
| `families/05.png` | `ecfc64dcb770823114e58ce338e53b1db4b0cd5f25328e8eb0294d1e8e1459e0` |
| `families/06.png` | `8b4cf9a2472102946e70dfb7724c2ef7c73c171a93e5190c6498d592bb74e418` |
| `families/07.png` | `ba27127b1c8860b400d876749ace31463c192ee6240eaa6e9d323fea5e33359a` |
| `families/08.png` | `506a1c6635e5ab2680c1969256f18116b70096e1e77b2508b47df7b1988390e2` |
| `families/09.png` | `44f30dda39a55e6cce95c57a199f8686fa116e4d5e73d6b13cfc1222f50feec0` |
| `families/10.png` | `c46d798ec1ec608f1f615aa18d04fbf0d1fc08d9c2668e55763e2fa81663cd60` |
| `families/11.png` | `1dab306429ee2a6c8e1bbeeb042dc74bde13b9ae084bcc4176ad5b12972ef94f` |
| `families/12.png` | `b5a0cf5a83ba52fb9456561519a971e9549633ab13c817480823eb050a9486ac` |
| `families/13.png` | `2d4a24c615094d14c477adda682e789f5f8ac1ac8926deb2bc4b075a97683f9b` |
| `families/14.png` | `2ead68bc5c6978681fec83f9b5e555eec54db83c4bab5aaa85b8290bef0b93d0` |
| `families/15.png` | `13fb5f40464e1c6d4ed276d1ed1b580697507893888389d37996c88664db3c07` |
| `families/16.png` | `4a74ea0b21a835d6e190268fd980345135d10390fa6b7a827cb1a85658eb2d77` |
| `families/17.png` | `e35a2637e77723e0fb93a6dcba69f03a9bccefd4ebdc2bf078e4dcb2551de9f7` |
| `families/18.png` | `55661396188332d81893774eec075c25bccfe1d5f06e57be368d85e0acdb5ec8` |
| `families/19.png` | `398f32ea0899f0019ca99eea2c14a7703e784968520fbe99a4587cb492f3357e` |
| `race/human/06.png` | `8c98a8909cce50248760b7e9530b9869683cab5c83e32ba1d2c669142b377c5c` |
| `race/elf/01.png` | `047f4ab724ec424f9f74949a4484cac4340d0a4873996193a86326ee1df26aa2` |
| `race/dwarf/07.png` | `86651d2206145de32479aa06897233aa6f259b6319718ff6e544dc746c06af8b` |
| `race/orc/11.png` | `f3e7b33c2257f50e3eed61e690a52e5ec71a07c65d18471bfbb5f9b506fc7cc2` |
| `race/saurian/10.png` | `e6edce133b4ac54059b0a2cb1a712c4066876836fe9ec231a875a2649ec558f5` |
| `age/dawn/01.png` | `0888bed5683b89ab80a450f845758317689ac08dcaec1a0be69a8f35a3a68895` |
| `age/bronze/02.png` | `16233ea51734f2901a4e9b81359301317135be8dc4abebc04dbb8f31e69d414f` |
| `age/iron/03.png` | `2bab52efcdb0bca922b2f1bab64ed7d6469dec64ff0f9c65db35f93535270ec0` |
| `age/classical/04.png` | `3678152d7beb08086e8034b74482e0abbd14cec6a5139e8c227048867aa9a44d` |
| `age/medieval/05.png` | `75005c95dda7d4b36914012855196fdf4d603ca9a2ca9bc236ffeb2e46193d4d` |
| `age/renaissance/06.png` | `b0731097eebb253c875504f0bbfee25086895ff2e6013efef48ce209c1b08ede` |
| `age/industrial/07.png` | `c4cd0a22784ffe7ceee74451f8a8f65c405108daeecfe9583c9fcd94676fe5a9` |
| `age/modern/08.png` | `a7d28d80d555de73c6f31a655db8c5ab01d9e37f3d178f9f51a2f6d2bb21e485` |
| `age/information/09.png` | `ca83e8d8e506005f70144ece42ade2d7a177a59164235abafdef914eb30b0c13` |
| `age/ascension/02.png` | `fc483fc10579fb30d876f4d74b6a31dd8ddc9463a75d28e4f4b89c6cbebd1cff` |

## Corrected full assembly, 23 November 2026

**Approved for catalog art direction. All 34 previously held model IDs passed visual re-review.** I opened candidate pages 01 through 19 from `director-candidate-_tnfo8kp/families`, compared the 16 affected functions across all represented races and ages, and revisited the ordinary Mine, Farm and Hospital as direct controls. I isolated the 21 surface-related models at the original 64 px cell size to compare adjoining ages without unrelated rows. The individual HydroponicsTower and clinic source images clarified the crown materials, porticos and signs.

The package records for all 1,034 models had current file evidence, passing scores and empty defect lists when checked. The aggregate evidence SHA-256 is `09bbe5a752a291a03acc11412292bf5e9087eb18333d4d1e91663cb2af7e1bb4`. The 19 page hashes below identify the complete reviewed set. Approval covers these offline assets and images. It carries no game, device or performance result.

| Model ID | Ages compared | Decision | Visual basis |
|---|---|---|---|
| `bld_dwarf_coal_mine_industrial_v0` | Renaissance, Industrial, Modern | Approve | An open coal bunker above the hoist house and a long descending conveyor produce a diagonal side profile. The ordinary mine has an open headframe. |
| `bld_dwarf_coal_mine_future_v0` | Information, Ascension | Approve | The hopper stays visible above the discharge belt in both finishes. Ore remains dark against the cyan or gold equipment. |
| `bld_dwarf_gold_mine_ancient_v0` | Iron | Approve | Wide stepped blue washing beds occupy the foreground of the Iron workings. Ore extraction remains legible without the ordinary mine’s large headframe. |
| `bld_dwarf_gold_mine_classical_v0` | Classical, Medieval, Renaissance | Approve | Tiered wash beds, an overhead cap and a short side structure survive all three roof and plinth material changes. |
| `bld_dwarf_gold_mine_industrial_v0` | Industrial, Modern | Approve | A large horizontal sorting drum terminates the ascending wash beds. Its side view remains clear in both ages. |
| `bld_dwarf_gold_mine_future_v0` | Information, Ascension | Approve | The faceted feed drum sits above cyan sorting terraces. Both stand apart from the coal bunker and from ordinary mine rails. |
| `bld_elf_medical_center_future_v0` | Information, Ascension | Approve | Tall paired ward pods with separate caps open around a signed, projecting entrance. The low clustered Hospital retains its single principal roof. |
| `bld_dwarf_medical_center_future_v0` | Information, Ascension | Approve | The broad covered triage entrance, stepped rear ward and cross form a long horizontal front. The ordinary Hospital has a shorter tower and small entrance beside a barrel-roof wing. |
| `bld_orc_medical_center_future_v0` | Information, Ascension | Approve | A red projecting medical canopy sits in front of the circular ward and two tall tanks. Its supported entrance and horn points retain Orc character. Hospital has three tent roofs. |
| `bld_dwarf_industrial_farm_industrial_v0` | Modern | Approve | Sawtooth glass bays now span the planted area, with crop boxes along the low frontage. The Modern Farm has open fields and a free-standing shed. |
| `bld_dwarf_industrial_farm_future_v0` | Information, Ascension | Approve | Two broad banks of glasshouse roofs dominate both future silhouettes. The regular farm keeps exposed crop rows. |
| `bld_orc_industrial_farm_industrial_v0` | Modern | Approve | The large red harvesting machine and elevated canopy give the Modern industrial field a distinctive profile beside an ordinary farm plot. |
| `bld_orc_industrial_farm_future_v0` | Information, Ascension | Approve | The arched growing house, strong ribs and red entrance read together. Crops remain visible at the front, with dark ends in Information and gold in Ascension. |
| `bld_orc_storehouse_ancient_v0` | Dawn, Bronze, Iron | Approve | The entire roof shifts from umber through ochre to slate over the same loading porch, crate and broad door. |
| `bld_human_hydroponics_tower_future_v0` | Information, Ascension | Approve | The crown changes from a dark, cool dome to a bright gold dome above planted terraces. The blue entrance canopy still identifies the Human structure. |
| `bld_elf_hydroponics_tower_future_v0` | Information, Ascension | Approve | The pointed leaf crown changes from deep green to yellow-gold across its visible faces. Green planting rings stay consistent. |
| `bld_dwarf_hydroponics_tower_future_v0` | Information, Ascension | Approve | The broad cap and every tier fascia change from graphite to brass. The planted green strips remain clearly visible between them. |
| `bld_saurian_hydroponics_tower_future_v0` | Information, Ascension | Approve | The crown below the sun emblem changes from dark turquoise to warm gold. Turquoise rings and stepped planting beds retain the Saurian silhouette. |
| `bld_orc_pasture_ancient_v0` | Bronze, Iron | Approve | The roof changes from warm hide to cool charcoal around the red stripes, with a visible enclosure around the herd. |
| `bld_saurian_farm_ancient_v0` | Dawn, Bronze, Iron | Approve | The raised shed roof and field edge now distinguish earth brown, ochre and cool gray. Turquoise irrigation beds remain visible. |
| `bld_human_farm_ancient_v0` | Dawn, Bronze, Iron | Approve | The gabled roof and working bed change from warm timber tones to copper and then darker material on a gray-edged plot. The green planted rows remain constant. |
| `bld_saurian_woodcutter_camp_ancient_v0` | Dawn, Bronze, Iron | Approve | The shelter roof changes from raw brown to ochre to slate directly above the unchanged log pile and chopping stump. |
| `bld_elf_farm_ancient_v0` | Dawn, Bronze, Iron | Approve | The large oval bed edge changes from darker earth to pale ochre and then gray. The planted rows and leaf canopy retain the farm identity. |
| `bld_saurian_fishing_hut_ancient_v0` | Dawn, Bronze, Iron | Approve | Brown, ochre and blue-gray shelter roofs read independently above the same canoe and turquoise landing. |
| `bld_saurian_pasture_ancient_v0` | Bronze, Iron | Approve | Bronze warm enclosure surfaces and shed roof give way to darker, cool construction in Iron. The inner pasture and stock stay green and visible. |
| `bld_elf_pasture_ancient_v0` | Bronze, Iron | Approve | A wide ochre perimeter becomes gray in Iron. The central rounded green paddock and small leaf shelter remain legible. |
| `bld_elf_palisade_ancient_v0` | Bronze, Iron | Approve | The low continuous fence and peaked binders change from warm ochre to a cool, dark material around the central curved gate. |
| `bld_orc_palisade_ancient_v0` | Bronze, Iron | Approve | The full reinforced upper rail is warm brown in Bronze and near-charcoal in Iron. Red paint, horns and the gate establish the same Orc fence. |
| `bld_saurian_stone_quarry_ancient_v0` | Dawn, Bronze, Iron | Approve | The broad quarry steps change from dark earth to warm sandy stone and then gray. The lifting frame and cut blocks remain visible. |
| `bld_elf_spirit_totem_ancient_v0` | Dawn, Bronze, Iron | Approve | The full circular plinth changes from dark brown to ochre to cool gray. The crescent in the forked tree remains the focus. |
| `bld_saurian_hunting_lodge_ancient_v0` | Dawn, Bronze, Iron | Approve | Broad umber, ochre and slate roofs mark each early age. The projecting hunting rack remains visible outside the hut. |
| `bld_saurian_gatherer_hut_ancient_v0` | Dawn, Bronze, Iron | Approve | The same three-part roof transition leaves the produce baskets, entrance and sun ornament readable at thumbnail size. |
| `bld_elf_aqueduct_future_v0` | Information, Ascension | Approve | A continuous dark Information channel fascia becomes gold at Ascension. Blue water and the curved pale arcade remain visible. |
| `bld_orc_fishing_hut_future_v0` | Information, Ascension | Approve | The red-striped fishing shelter and broad dock change from dark steel to gold. The adjacent wooden canoe continues to identify the activity. |

### Corrected full-catalog family sheets

| Page | Reviewed PNG SHA-256 |
|---|---|
| `01.png` | `1ad414e4a24340dc2a05465c0dc97b7e32eaa2dcb38a519ff4f24fc715bb96a7` |
| `02.png` | `d9d4a9cbd624c3b054b485d5d9bca708e3b3c85d8f0136407de49c467e0c1512` |
| `03.png` | `33dbdae52097b20a1015e09aa5c9147d254bb35856d7ece025957289b5efe132` |
| `04.png` | `dbd288f51d245545211412cd5e77a61916e9aacb2c181b573fb8a3d384a9570a` |
| `05.png` | `070fcaee95ecc1c2d67b8be10954c0074353ac3793939b1798f2e0e2ad980c9e` |
| `06.png` | `3f4b1aee06b9ff3217d3aab8bdede84ca0a6c9a1f9df7f3409ed310f53fc1d05` |
| `07.png` | `8f7a9cab1129c9588bc41776857627756f9a620c0c836902e7feb8eb20f4fe0d` |
| `08.png` | `506a1c6635e5ab2680c1969256f18116b70096e1e77b2508b47df7b1988390e2` |
| `09.png` | `33f030caaeb9987fbee29508d7f0adf0d0a4cfc2b734925fd59f7fef551788a3` |
| `10.png` | `698cdfb8da0cc08de564f2bb84b6166edbdccb68f011178ec510803ca17f91e8` |
| `11.png` | `37784634caf473436019f5d5f151b45f9a54b0ce47dfe839dcf6eb316f445f1f` |
| `12.png` | `b78b5ca480cd518b8853bf7acbf4eac10ad67c450df26fcfa5d53fecf8a1aef6` |
| `13.png` | `c34b698e9b58f1d7c5e81391cf4f7f9389a034ccf7c5b3f21270e38bdc067ac7` |
| `14.png` | `2ead68bc5c6978681fec83f9b5e555eec54db83c4bab5aaa85b8290bef0b93d0` |
| `15.png` | `ad019a1b5a2aaaad1bca0c42be3df50cba90b46a21c7ab3e7a693ef0f80f525f` |
| `16.png` | `f98e4ba4dfbf8c2cea7cb25534ff195b90cd1034bf3aa2f17aeb9c3b1ec446f9` |
| `17.png` | `e35a2637e77723e0fb93a6dcba69f03a9bccefd4ebdc2bf078e4dcb2551de9f7` |
| `18.png` | `55661396188332d81893774eec075c25bccfe1d5f06e57be368d85e0acdb5ec8` |
| `19.png` | `49a8888e7ca35be30aba366cfc28287c26254266fb99b46d4d4f75d69bee85ae` |

## Coordinator RC1 visual review, 23 November 2026

I opened all **36 canonical pages**. The 19 colour family sheets contain 2,096 age previews from 1,034 models across all 75 functions. The 17 unpainted sheets show 361 era models in 119 race/function lineages, each rendered twice at 64 px from the serialized LOD0 mesh, at 215° and 145°. I compared the earliest and latest available models in each row, followed intermediate generations, and used the colour sheets to compare the same function across races and against adjacent functions. The 119 lineages comprise the coordinator’s 61, the separately pictured Elf Herbalist, and 57 lineages with additional critic findings. The images include all siblings of each repaired model.

| Race | Coordinator lineages, including pictured Herbalist | Additional lineages | Era models / unpainted views | Form and technology observed |
|---|---:|---:|---:|---|
| Dwarf | 11 | 8 | 58 / 116 | The cathedral progresses from a square nave and side tower to a domed hall, then buttress-like ribs enclosing a tall faceted center. The quarry’s lever and exposed blocks give way to an enclosed lifting unit and a gantry over cut beds. Wide bases and rectilinear working bays keep both Dwarven. |
| Elf | 6 | 30 | 110 / 220 | The Herbalist moves from pots beside a leaf hut to a canopied planting bed, a suspended pair of processing vessels and a tall flowering treatment station. The future factory exposes its elevated production line, while the theater splits its seating around a leaf-shaded performance end. The repaired Classical fishing station has two jetties and a hanging net. The Classical gold works expose a wheel in a masonry arch above a sluice. |
| Human | 4 | 7 | 32 / 64 | A boat at a roofed waterfront landing becomes a dockside loading tower, then a crane spanning the quay. The pasture advances from a hut and fenced animals to masonry and industrial stock shelters, then a broad roof above an open pen. The theater preserves the stage as its enclosure changes from an open shed to a hall and a curved audience bowl. |
| Orc | 14 | 9 | 71 / 142 | The early ritual shelter becomes an arch, paired industrial uprights and a gem held between tusks. At the observatory, a short roof-mounted optic gives way to a larger enclosed instrument and then a dish array. The research lab’s surrounding instrument frame and the rail station’s long raised platform remain distinct even in gray. |
| Saurian | 27 | 3 | 90 / 180 | The forge and furnace lineages move from small kiln buildings to mechanized sheds, then a supported energy furnace or working arch. The theater progresses from a framed, open stage to a roofed hall and a fan-shaped acoustic shell. The college’s paired pavilions and suspended horizontal ring read differently from the monastery’s frontal arch and pool. |

I also compared the future Elf Hospital and MedicalCenter at the same two unpainted angles and in colour. The Hospital encloses a stair among three elevated wards. The MedicalCenter has two staggered towers and an open, projecting triage entry. In the supplemental Elf fishing and gold-mining lineages, the Industrial and Future checkpoint siblings remain close silhouettes. The distinctly rebuilt Classical siblings resolve the findings recorded for those lineages. This review does not establish independent recreations of every inherited era mesh.

### RC1 first candidate finding

**Held `bld_saurian_farm_future_v0` for its Information and Ascension appearances.** At 64 px the ordinary farm was a three-tier circular planter with a leaf crown on a nearly empty plot. The coexisting HydroponicsTower used the same round planted tiers and central cap. Comparing both unpainted angles confirmed that the similarly shaped towers occupied the playable silhouettes. Restore a low spread of cultivated beds or irrigation terraces as the primary form of the ordinary Future Farm. Keep the coexisting industrial glasshouses and hydroponics tower distinct.

The held painted Farm page 05 had SHA-256 `7e0ae8c8c817e991b79d8a6804f376ce0c8b074f4721403cb62dab8e5463b720`. Its corresponding `saurian-01.png` geometry page had SHA-256 `2704dbda439aefa1c0fd1e709800573c23aeb60cb1f4a0a5eaffbf2ddceb2fb4`. The HydroponicsTower comparison on page 10 had SHA-256 `7232ca2770dc1d164a9d12b903d84b246c94d26c05fea01548ff7a7f487e4c9b`.

### Signed RC1 disposition

**Approved. The Saurian future Farm hold is closed.** I reopened painted pages 05 and 10 and unpainted page `saurian-01.png` after the repair. Four gently curving crop rows fill the farm plot. A low irrigation channel and a small shelter at the corner leave the field visible in both Information and Ascension. At both gray angles the Farm reads as a broad cultivated surface. The HydroponicsTower remains a stack of circular planting decks, and the IndustrialFarm remains a group of pitched glasshouses. Saurian Food round 10 independently passed the revised model.

Updated painted page 05 has SHA-256 `de7247208cd7b0df52aa5668ea0c469c4f64ae9076470161e8039c5661f18e87`. Updated unpainted page `saurian-01.png` has SHA-256 `3598986ca125ff0ba421c43f2448aa216961368ca6c7c32396efafe013995473`. The other 34 page hashes match those I inspected in the initial RC1 comparison. The repaired GLB has SHA-256 `0b80a224fcdebc552d245af199168653e7c0b1ff263e75bc0d2b4dd7c8668fb9`.

I checked the latest individual review against current evidence for each of the 1,034 models in 71 package fragments. All passed. I signed the aggregate evidence digest `a019521f7faeff6fe8478107e730c5284b967c7ded349000903e89a5b1f4b0f1` and the SHA-256 values for all 19 painted and all 17 unpainted pages. This signature covers the inspected assets, appearances and scoped era repairs. Runtime and device verification are separate. [Signed evidence and sheet hashes](art_direction_approval.json)

Signed by `catalog_art_director`, 23 November 2026.

## RC4 catalog review at checkpoint 7e905bb

**Held `bld_saurian_mine_future_v0` for its Information and Ascension appearances.** On family page 11 the ordinary Mine has a tall splayed tripod with a turquoise cap over a flat square plot. The contemporary OilWell on page 12 has the same dominant outline. The original painted previews show a drill suspended over bare earth on the Mine and larger tankage on the OilWell. From town scale the Mine reads as a second bore rig. Give the Mine an exposed mineral face or entrance and a legible haulage element. The OilWell remains the comparison reference.

The held Mine page 11 had SHA-256 `a906a57466b22b9f84a24ac608eb1619a6ba222d89506423043b494857492b82`. OilWell reference page 12 had SHA-256 `e7aeb63d7c52a4db9ab3a272645b2afa1a0087d98df5cd1d67d9f1ef4c61d377`.

The held Mine GLB had SHA-256 `0ee43805b37d11203e4b32714d5732cb13d3f0bbd8601c9d42f34b4c61e4ca45`. Its RC4 geometry page, `rc4-saurian-02.png`, had SHA-256 `1df35268190b54306d3528b951b5a53d5f03bbe47451f58164a8f87ce93b4945`.

**Held `bld_orc_fishing_hut_classical_v0` for Classical, Medieval and Renaissance.** The earlier dock and its Classical successor both place the same conical round hut, near-side skiff and triangular net on a square pier. The change to pale masonry contributes most of the perceived difference in the painted versions. At both gray angles, the outlines of the dock, its fishing gear and its hut nearly coincide. Give the Classical station a different working section and roofline that still communicate a fishing station, for example a raised net-drying gallery with a skiff below. The later tall working quay and reinforced Future dock provide adjacent references.

The held FishingHut GLB had SHA-256 `86c1e87ed9308566cd9c2e675b4c2162c37ac156dc9878d70699140689cbe385`. Family page 06 had SHA-256 `822d8cfcc84823ba09f64bb0d269e70b241cfd7d2fd69ac08f17a93b9cd4f690`, and geometry page `rc4-orc-01.png` had SHA-256 `ef254e89866f68d14044706f9e34708011bdbb40f920bb49f0d9f0090084e01e`.

### Further findings in the coordinator’s 101 families

| Held GLB | Observed geometry and required change | Held GLB SHA-256 |
| --- | --- | --- |
| `bld_human_barracks_classical_v0` | The Classical and Iron stations have the same long cross-gabled block, projecting gabled entry, shield and pennant. Changing the cladding and pitch leaves the troop quarters practically identical in gray. Give Classical a courtyard or transverse block that changes the arrangement of the quarters. | `8f68e77f86aa94d35440f31fc6a75d11eb19413d5e8d34ce1b8c6ef7f16b90a2` |
| `bld_human_blacksmith_classical_v0` | Both early shops place a low awning beside a gabled shop and a flared furnace at the same end. The Classical roof tiles provide most of the new read. Give its forge a recognizably different working section, such as a deep hood with an open masonry bay and bellows. | `335e9e95fa37d5f452b059da953459137e4981da899c7e9b37e3430a4934a09b` |
| `bld_orc_woodcutter_camp_classical_v0` | Both early camps have the same conical hut, stack of three logs, axe and stump in the same positions. Give the Classical production yard an open frame for splitting or storing timber. | `1e85e8656e157851565603cc44f5b225eec5b3d83621c85f8be995ec36aeec8d` |
| `bld_saurian_granary_future_v0` | The Industrial and Future granaries present paired silos, an overhead feed elbow, identical discharge chutes, two base slabs and a front bin line. Rounding the silo bodies leaves the same storage plant at phone size. Reconfigure the grain bins and their loading floor or conveyor. | `bb33f60ce74500e36377cf69980e43f86e6a1bdf6d1e0548c6549ddc98f06a16` |
| `bld_saurian_temple_ancient_v0` | The Iron Temple and Iron TownCenter both have a broad stepped square platform, front staircase, hipped upper room and small sun crest. Give the Temple an open offering court or distinctly framed sanctuary. | `e55cfc00ce5eaf8a32f3ab536636f7c6cf07cca1207b64117a620171fa44a54a` |
| `bld_saurian_temple_classical_v0` | The Classical Temple and Classical TownCenter put matching round domes over the same stepped pavilion and central stair. Use a recognizably religious plan, such as a processional colonnade around an altar. | `ca69a51c0753ad16dd451027e6ac99ea083f4d7212504acfafd1621a27dfc81c` |
| `bld_saurian_temple_future_v0` | At Ascension, the Temple and TownCenter put the same large upright solar ring over the stepped civic base. Give the late Temple its own congregation space and change the support and profile of its focal object. The Industrial Temple already separates from its civic counterpart. | `5e5847a801c416f42ab23a238e86a2c4248c336d2d10870e2e84cc75ace080cd` |

The candidate geometry SHA-256 values were `a45afbc9239fbf84393d78ad39dde7bf5d7a59b66811b3daaf367ca3e4b8e39c` for `rc4-human-01.png`, `022cb1742584cf8ddbba01642357eca25bbce3df260ce4e19c6a8650db4ad415` for `rc4-orc-03.png`, `1df35268190b54306d3528b951b5a53d5f03bbe47451f58164a8f87ce93b4945` for `rc4-saurian-02.png`, and `fc6c567577797b04b8e74c1a24cf38567988bd2a6b420a9d41a8a4513a9f032a` for `rc4-saurian-03.png`.

| Painted comparison | Candidate page SHA-256 |
| --- | --- |
| 02, Barracks | `6d4663867ce3acc4eb09079a2a8efff44f8cf04d3effc17a5f2229937dd346fb` |
| 03, Blacksmith | `3f0c3ad31e9c3b06cadf8b0b1a62571ffb6af56281b3dadeb38e1580caa5cb2a` |
| 07, Granary | `b2c65666444a7972be18dd83473e4a775704799904dfdd0f8c7120a7167f5577` |
| 17, Temple and TownCenter | `b9702d468544848776a0e5fc9fab8c8ec59a779f52b4eda06baaa74cc311e859` |
| 19, WoodcutterCamp | `457d54d86faafc0f9c1c2ccb0db9a44d3520a0170f610be03f5eab879585de91` |

I opened all 19 painted family pages, the 17 existing unpainted gallery pages and the 17 RC4 unpainted pages. The RC4 set covers 336 models in all 101 listed lineages and shows 672 views. The existing gallery contains 361 models in 119 lineages and shows 722 views. The two collections overlap. I compared every retained checkpoint pair using its four original 64 px images, two per model. The 60 distinct pairs account for 95 ordered exceptions. The blunt roof monitor, dome and twin stacks of the Dwarf Bathhouse, for example, are visible on the same hall and pools. The paired granary, workshops and docks reported above illustrate other sibling relationships in listed families. The Saurian Mine and three Temples concern confusion between simultaneous functions. Some Temple pairs also occur in the exception table, where their different crowns remain visible when comparing within that single family.

**Nine models in seven listed lineages require reinspection.** I examined each individual Dwarf and Saurian TownCenter age. The Dwarf sequence changes its roof from split thatch to hood, boxed upper room, taller hall, chimneys, clerestory and finally a ring. Saurian early civic halls share their plinth and stair but gain a side turret at Bronze, raised corner ribs at Iron and a round crown at Classical. Later ages add the towered Renaissance wing, industrial stacks and civic upper decks. This review uses rendered asset images and geometric silhouettes.

### Inherited family outside the RC4 list

**Open visual observation on `bld_elf_fishing_hut_future_v0` for Information and Ascension.** The existing Elf gallery revealed this inherited issue during the cross-race FishingHut comparison. Although this lineage falls outside the 101 targeted for RC4, its two late stations on family page 06 have practically the same outline. Both put a leaf-roofed hut beneath a bent lifting frame on a round timber dock, with the same hanging bucket and skiff. The original previews confirm that changing the wall and lamp colors supplies most of the visual distinction. A future revision could give the landing a more advanced means of handling the catch, visibly different from the Industrial bucket arm. The coordinator's RC4 repair scope covers the Human, Orc and Saurian findings above. The Elf pair remains a documented limitation of any broader catalog claim.

The observed Elf FishingHut GLB had SHA-256 `cf7a948b908a8ad5a68ed2977568753e65999880931ecc4885d7695697146de3`. Geometry page `elf-01.png` had SHA-256 `7183d874c2a8840acf851fb7e5bad99f03c610e97efbb1c302594050104ef7f6`. The painted page 06 is the same file identified for the Orc finding above.

### Signed RC4 disposition

**Approved for the coordinator’s 101 listed lineages. All nine director holds are closed.** I reopened all 53 final canonical sheets. The 19 painted pages contain 2,096 age appearances across 75 building functions. The final 17 RC4 geometry pages contain both 64 px views for 336 models, including each of the ten Dwarf and Saurian TownCenter ages. Their age captions now read chronologically, with a visible separator. The 17 older geometry pages show both views for 361 models across 119 lineages. These galleries overlap.

| Approved replacement | What now reads at 64 px | Approved GLB SHA-256 |
| --- | --- | --- |
| `bld_human_barracks_classical_v0` | A long two-story range and short return wing enclose an open muster court. The shield stands in the yard. Both views separate this from the compact Iron quarters. | `9e4f9c7585ca35b36d1fa9624b0a4274a33a0dca3549d1aaa98cd83ad242e247` |
| `bld_human_blacksmith_classical_v0` | A tall flared hood and chimney stand over a bright open hearth. The bellows and work surfaces spread to either side. The shed and low canopy of the Bronze and Iron shop have a different outline. | `1e91d7073935d50c54881f74906d012ab01cb655c2c3c23b0b5d8090060a4f14` |
| `bld_orc_fishing_hut_classical_v0` | Two net panels hang from a horn-tipped frame above a stone landing and skiff. Open space between the nets reads in both angles. The earlier dock has an enclosed cone hut and the Industrial dock has a tall work shed. | `632f708968ea256baa4edbca4491548a124c2219d7c77aa1f48156f467bb2f86` |
| `bld_orc_woodcutter_camp_classical_v0` | A large log spans a braced splitting frame with smaller stock and a handcart underneath. The open yard, visible cut ends and cart distinguish it from the earlier hut and from the Orc sawmill. | `88eee5ec4a92087d2dd1d68be4d80c46db3fcfc2797381851480f9a38b812ff4` |
| `bld_saurian_granary_future_v0` | Three rectangular grain bins step upward above separate delivery chutes and a common loading line. The wheat mark, visible grain and staggered profile preserve the function and distinguish it from the paired Industrial silos. | `b381086131b7f454430a21b6dcfd3b95955ff4910289ca818bdc696bdd146fac` |
| `bld_saurian_mine_future_v0` | A low exposed rock face with two mineral seams feeds a loaded tracked hauler. The open cut holds its shape from both sides. The OilWell keeps its tripod, the GoldMine its sluice, and the StoneQuarry its gantry and cut blocks. | `f05e2cb46f6f8ea5a32813173ff49fd39652349681144a72b1bccafd8a5cf2c8` |
| `bld_saurian_temple_ancient_v0` | Standing stones enclose a sun altar with offering lights around a round court. The view through the stones separates this place of worship from the Iron TownCenter’s roofed hall and stair. | `db7dec6f264ef3d7681fe29411ccff02c5113aeb8cb62b5a483fad5519a7ed0f` |
| `bld_saurian_temple_classical_v0` | Twin colonnades lead into a roofed sanctuary around a sun medallion. The aisle reads from both sides and the corresponding Classical TownCenter retains its dome above a stepped base. | `23f1b4aa8c70097529ff3cb6a6c5f6815a1a45720a10e020da7ba28bef9e07a6` |
| `bld_saurian_temple_future_v0` | Two leaning pierced panels frame an open altar. They produce a tall triangular silhouette at both late ages, distinct from the upright crown and broad Ascension TownCenter. | `66c30e0d21487c718c31caabf5e3e88351ebc613fc890d8965235e677b99a688` |

Compared with `7e905bb`, 154 GLBs changed in 87 listed lineages. The strict package loader accepted all 1,034 models in all 71 packages. The geometry check returned 3,230 ordered comparisons. Of the 60 checkpoint pairs visually examined in the candidate round, 56 pairs in 23 lineages retain their exact approved bytes and account for 88 ordered exceptions. The geometry check binds each surviving exception to the two reviewed GLBs and their original corner counts.

The inherited Elf FishingHut observation remains open outside the RC4 list. Both of its original exports retain the same dock, leaf hut, skiff and bucket frame in the final sheets. The comparison gives 1,141 of 2,691 matching corners in each direction (42.40%). Its Industrial GLB has SHA-256 `aed534d3ddddf6415dee66142fa284148bf34bea06ad8e7b17568093698e982b`. Its Future GLB has SHA-256 `cf7a948b908a8ad5a68ed2977568753e65999880931ecc4885d7695697146de3`. This visual similarity limits any broader claim about the full catalog. The review covered the rendered assets and exported model evidence. Runtime and device verification are separate.

Signed by `catalog_art_director`, 23 November 2026. Catalog evidence SHA-256 `755e649839bc18281984253149f5b4dfeda06f658d5db3f118b0c9f23914ce48`. The approval record binds this disposition to all 19 painted and 34 geometry pages. [Signed evidence and sheet hashes](art_direction_approval.json)


