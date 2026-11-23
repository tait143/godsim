# Independent RC4 working-tree review

**Approved for the 101 RC4 families on 23 November 2026.** I reviewed the tracked changes and task-owned untracked files against `7e905bbc7c393fdfa647a0bf364239328e001203` on `art/buildings`. Both review findings are closed.

## Findings

1. **The standalone repair report could accept incomplete or stale reviews.** Its original implementation collected whatever fragment IDs and latest scores were available. It could miss a model and could reuse a checkpoint score after a GLB changed. The repair now requires the complete checkpoint ID set, the strict package publication checks and passing post-checkpoint evidence for every changed GLB. I checked the new code and ran its regression tests.
2. **Some grayscale age labels ran backwards or omitted an age.** Early unlocks are appended after the authored texture base age. The Human Coal Mine panel consequently showed Industrial through Renaissance and omitted Modern. Its final caption reads Renaissance through Modern. I reconstructed all 17 final RC4 sheets from the published GLBs with independently derived chronological captions. The 336 labels and 672 camera views matched pixel for pixel. The widest caption occupied 108 of its available 138 pixels.

## Independent evidence

I decoded the Git checkpoint and working GLBs and compared indexed triangle corners in both directions. I rounded float64 positions to three decimals, counted repeated source corners with a multiset and tested membership in each other model’s position set. The external coordinator file and the repository copy are byte-identical. The recomputation reproduced the checkpoint tier for every listed family and every one of its 550 directed comparisons above 60%.

The catalog yields 3,230 directions across 285 multi-model families. Those include 900 House directions grouped by race and variant and 450 Town Center directions grouped by race. The baseline has 27 mandatory and 74 moderate families. The archived critic considered all 227 distinct moderate pairs, retaining 73 and requesting 154 repairs. I re-rendered the four stated 64-pixel grayscale tiles for every row from the original checkpoint exports. All 908 image crops matched the archive pixel for pixel, including their page, row and camera placement.

The final repair changed all three geometry levels in 154 GLBs across 87 listed families. The other 880 GLBs retained their baseline hashes. All 329 individual age previews for the revised models changed. All 1,767 other individual previews matched Git. All mandatory comparisons and all pairs marked for repair reached 60% or below. The highest mandatory comparison was 1,224/2,076, and the highest involving a revised export was 276/1,440.

There are 88 remaining directed exceptions across 56 distinct pairs in 23 families. For each of the 56 report rows I checked both measurements, both original reviewed GLB hashes, the observation and its archived page and row. The maximum of 1,275/1,596 occurs twice, from Future Human Shrine to its Ancient and Classical siblings. Visual inspection covered family pages for all five races and both ten-age Town Center lineages. The final Orc Classical Fishing Hut shows an upright net and boat. The Saurian Future Mine shows a low cut mineral face and tracked hauler.

I validated all 3,102 serialized LODs. All were within their triangle budgets and every middle LOD met the 60% ceiling. The single shared material, atlas and emissive mask, UVs, face orientation and file references passed payload validation. The surface ledger matched every GLB digest. Running the strict audit again reproduced the archived final audit. I checked the chronological prefix of every package ledger against Git, all 2,650 reported score rows against their sources and all 211 escalation histories. All 154 revised GLBs had fresh post-checkpoint evidence and passing six-criterion scores. The 60 new package rounds contained 171 evaluations. The nine models held by the director have final approvals. All 53 director page signatures and the catalog evidence digest `755e649839bc18281984253149f5b4dfeda06f658d5db3f118b0c9f23914ce48` matched the final working tree.

## Final verification

The strict-build log records all 71 packages generated in each of two complete runs. The two raw inventories and the decompressed repository archive were byte-identical. I separately read and hashed every one of the 4,939 listed files. All matched. The canonical SHA-256 was `7f2d87557eafb7598dd438b7ed58112cea642c0a22d1485f9c6a05007bacc7db`. The raw inventory SHA-256 was `2670e7646a6308b3fed0e61b05086a57191461313b65b0416ac66c97ca219895`. The published manifest, all 1,034 runtime model references, 2,130 runtime lookup keys, 71 fragments and 314 generated sheets agreed. [Complete inventory](rebuild_inventory.json.gz)

I independently ran all 24 AssetForge tests and all 19 NUnitLite building integration tests. They passed. The 200-building fixture produced four single-material meshes with 409,720 vertices and 173,105 triangles. Three meshes required 32-bit indices. The round changed no Core, Unity runtime, shader or shared texture source. The report documents the inherited Elf Fishing Hut similarity outside the 101 families. Unity Editor and device rendering, actual glTFast import, device memory, GPU submissions and cross-machine reproducibility remain unverified. [Verification and limitations](../../../../docs/BUILDING_ART_VERIFICATION.md)

Signed by the independent RC4 whole-diff reviewer, `rc4_final_diff`.
