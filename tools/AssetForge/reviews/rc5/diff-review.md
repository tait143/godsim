# Round 3 independent diff review

**Signed, 23 November 2026.** I reviewed the working tree on `art/buildings` against `9b09d383b0e39239795c77638a49e5356438cd86`. All five requested pairs pass. I found one gap in the audit of the archived measurements. The corrected checker closes it.

## Geometry and visual checks

I unpacked the indexed GLB position data, rounded each coordinate to three decimals in float64, and counted each source triangle corner whose position occurs in the comparison model. Repeated source corners each count once per occurrence. The arrows follow the era order in each row.

| Pair | Checkpoint forward / reverse | Final forward / reverse | Rebuilt era | LOD0 / LOD1 / LOD2 |
| --- | ---: | ---: | --- | ---: |
| Orc Bank, Classical and Industrial | 1,488/2,082 · 1,488/2,280 | 0/2,082 · 0/2,196 | Industrial | 732 / 426 / 220 |
| Elf Gold Mine, Ancient and Industrial | 2,643/3,825 · 2,643/4,341 | 0/3,825 · 0/4,425 | Industrial | 1,475 / 839 / 439 |
| Elf Fishing Hut, Industrial and Future | 1,141/2,691 · 1,141/2,691 | 0/2,691 · 0/3,282 | Future | 1,094 / 623 / 322 |
| Saurian Bank, Industrial and Future | 1,654/2,226 · 1,660/2,166 | 0/2,226 · 0/3,735 | Future | 1,245 / 709 / 393 |
| Dwarf Well, Ancient and Industrial | 1,854/2,706 · 1,854/3,180 | 0/2,706 · 0/3,882 | Industrial | 1,294 / 648 / 401 |

I reproduced the 36 checkpoint and 36 final grayscale tiles pixel for pixel from the 18 sibling GLBs at each revision. Both 64 px views show the Orc bank’s offset counting tower, the mine’s stack and stamps, the fishing station’s swept canopy and round pen, the Saurian treasury’s two enclosed volumes, and the Dwarf pump beam reaching over its cistern. The held bank and gate image shows the shared upright hoop. In the final painted views, the bank’s lock sits in a solid wall, the teller hall has its own entrance and the gate retains its opening. All five final painted LOD2 views retain the identifying large shapes.

I checked each scoped generator’s dispatch and compared the indexed geometry at all three levels to the checkpoint. Only the five named GLBs changed. Their published bounds, byte sizes, triangle counts, runtime pointers, current package approval hashes and surface records agree with the delivered bytes. Fresh coplanar-overlap and closed-shell winding checks on the five files returned zero findings. The exports use the shared atlas, emissive map and single material.

## Publication, reviews and tests

- I checked the SHA-256 and three serialized LOD counts of every one of the 1,034 models. The other 1,029 GLBs are unchanged. Every LOD1 satisfies the 60% ceiling. The runtime index and all 2,130 bindings match the checkpoint. The diff contains no Core changes.
- My independent catalog comparison found 3,230 directions in 285 multi-model families. Exactly 78 directions in 50 pairs from 21 families exceed 60%. Both peak directions start at the Future Human Shrine and reach its Ancient or Classical sibling at 1,275/1,596 (79.887%). The report correctly labels the 50 retained and six cleared historical exceptions.
- The five current package approvals and 53 director page hashes match their files. I reconstructed all 42 changed overview sheets from the checkpoint images and the expected revised cells. All pixels matched. The report’s 2,656 score-and-finding rows, 212 lifetime escalation IDs and round lists, budget table, total GLB bytes and ten scoped percentages match their sources.
- The strict-build log records two complete runs of the 71 package generators. Both raw inventories match the decompressed repository archive byte for byte. I rehashed all 4,939 paths and compared them to the checkpoint inventory. Exactly 68 generated paths changed. The sorted-JSON inventory digest is `3baa8a54e66390e90d06fbf91a89638f1091b5b553e4e8ced83b0c7e9f06dbab`. The uncompressed inventory file SHA-256 is `da07de3312be012d54c6aad8a2556a7bcd62de1c139c352bd0a51bfcfba1b8cc`.
- I ran the two focused corner-count and LOD1 regression tests, then all 24 AssetForge tests and all 19 headless building integration tests. All passed. The 200-building case assembled 409,720 vertices and 173,105 triangles into four single-material meshes. These figures come from the headless CPU path. The review covers exported art and offline renders.

## Audit finding and closure

The first scoped checker could return success after an archived numerator or SHA was edited. The revised checker reconstructs both pair tables from the checkpoint blobs and final reviewed exports. Its complete output matches `catalog-audit.json`. In four isolated in-memory trials I altered a numerator and a sibling SHA in each of the two archives. The checker rejected every alteration and accepted the untouched files. [Scoped checker](check_pairs.py) · [Catalog audit](catalog-audit.json) · [Rebuild inventory](rebuild-inventory.json.gz)

Signed by `r3_diff_review`, independent whole-diff reviewer.
