# Building art integration review

Independent review on 2026-11-23 in the `art/buildings` worktree at `2c7e33a`. The review includes the tracked changes and the new production assets, generator packages, reviews, tests and integration code.

## First review and corrections

| Finding | Evidence | Disposition |
|---|---|---|
| The renderer applied the procedural definition tint a second time to authored atlas colours. | The Oil Well definition multiplies the entire model by `#2E2B28`. Its approved preview uses only the optional model tint. | Corrected. Authored bodies now use white by default. Procedural bodies and ruins retain the definition colour. The updated lifecycle test includes a dark definition. |
| An authored Town Center could extend into a neighbouring tile when its settlement advanced. | The two-tile Dwarf Bronze center reaches −0.97 tile at the entrance. A 30% horizontal tier increase moved that edge to −1.261 tiles. | Corrected. Authored Town Centers retain their exported horizontal scale and grow vertically by tier. The lifecycle test checks body and collider dimensions. |
| Withdrawing a package could leave its numbered sheets among published pages. | Sheet retirement originally visited only race, age and family directories. | Corrected. The publisher also visits known package directories. The regression preserves separate critic evidence. |
| The two-build SHA inventory omitted package-generator contact sheets. | The generators produce additional images under `previews/Buildings/`. The first correction missed 46 nested Saurian sheets, including `Commerce/sheets/review-01.png`. | Corrected. The final recursive two-build inventory covers all 380 supplemental images, including the 46 nested sheets. |
| The headless batch test collapsed its output positions and normals to zero. | The original matrix accessors, point transform and direction transform returned `default`. | Corrected. Functional transforms now support translations, rotations, nonuniform scale and inverse-transpose normal transforms. The full-catalog fixture independently checks every resulting position and normal and verifies page bounds. |
| The optional Meshy instructions gave outdated integration steps. | The older guide said glTFast still needed to be added, described an all-fragment merger and suggested a nearest-age fallback. | Corrected in `assets/meshy/README.md`. |
| Complete publication could proceed with a missing director ledger or a stale catalog verdict. | The earlier gate treated a missing file as zero holds. Closing a finding did not bind the verdict to the models and images that the director inspected. | Corrected. The ledger is required. Complete strict and partial publications both require a current catalog fingerprint and all signed family-sheet hashes. Independent negative checks rejected a substituted family image before runtime publication. |

## First full assembly, before the cross-catalog art revisions

- Recomputed the current per-model critique fingerprints from each GLB, each declared age PNG, the two shared PNGs, shader and material. All 1,034 models across 71 packages matched their latest passing six-part scores. The serialized survey records referenced the same GLB hashes and contained no reported surface or closed-shell orientation findings.
- Read every GLB's header, three serialized index counts, source texture paths and material count. The 1,034 files have 972,260 LOD0, 549,384 LOD1 and 237,384 LOD2 triangles in total. All 2,096 declared preview files had readable PNG headers.
- Cross-checked the manifest, compact Unity index and every Resource pointer against its GLB SHA-256. The 2,130 runtime bindings reference exactly the declared race, age, model and variant appearances. The catalog covers all 958 work-list IDs and 75 building types.
- Reran the headless NUnitLite building-art suite after the renderer fixes. All 18 tests passed with no skips. The production-data fixture sampled 200 distinct models from the full manifest. It produced four single-material body meshes containing 434,590 vertices and 183,230 triangles. Three pages used 32-bit indices.
- Read the glTFast editor handoff, authored/fallback selection, age-row conversion, batch transforms and indices, both LOD thresholds, and construction/fire/ruin paths. The serialized references use the shared albedo and emissive textures. The runtime index loads individual three-LOD mesh resources by selected ID.
- `git diff --check` passed. Git reported no tracked or untracked changes under `Assets/GodSim/Core`.

## Revised assembly after the art director's findings

- A separate reader opened all 1,034 revised GLBs and checked 1,756,063 triangles across 3,102 LODs. It checked finite positions and UVs, ground contact, footprints, UV age rows and patch insets, face area and agreement between the cross product and all three vertex normals. Counts were 970,975 at LOD0, 548,182 at LOD1 and 236,906 at LOD2. Together the GLBs occupy 148,312,244 bytes.
- All 1,034 individual review fingerprints matched the rebuilt GLBs, 2,096 model/age PNGs and shared art files. All current surface records matched their GLB hashes. During re-review, 34 open director findings correctly withheld 15 complete packages. The independently predicted 721 admissible models matched the runtime manifest, compact index and pointers.
- The final director ledger closed six groups of findings. Reconstructed the signed catalog evidence digest `09bbe5a752a291a03acc11412292bf5e9087eb18333d4d1e91663cb2af7e1bb4`. All 19 signed family-page hashes matched both the director's candidate and the canonical images.
- All 15 AssetForge tests passed. With a deliberately mismatched sheet digest, both complete strict and complete partial builds stopped before publishing runtime data.
- All 19 headless integration tests passed against the revised 1,034-item manifest. The 200 sampled LOD0 meshes contributed 435,072 vertices and 183,314 triangles to four body meshes, three with 32-bit indices. The measured extents of the four headless pages were 231.74 × 2.10 × 39.65, 230.96 × 2.73 × 39.97, 230.63 × 2.67 × 39.30 and 86.54 × 2.34 × 3.42 units.
- Cross-checked the report's 1,706 individual score rows, all 71 rows of the package checklist, each race and age count, and all four triangle-budget rows. The 180 rounds and six final score minima match the recorded critiques. Each source ID's building type, race, variant, footprint and covered ages agree with its published models.
- Examined the full simulation TRX. It records 3,050 cases, 3,023 passes and three failures, with 24 cases outside the executed count. The report names all three failures. No Core file changed.

Device allocation, actual driver draw calls, real glTFast import, shader compilation and iPhone visual/frame-time measurements remain unverified. The geometric surface survey checks co-oriented overlapping triangles using one outward visibility ray and checks orientation for closed components. Visibility from every angle and winding for arbitrary open faces remain visual checks.

## Final disposition

**Passed independent whole-diff review for the offline building-art deliverables.** All seven findings above are closed. The final runtime manifest, index and 1,034 resource pointers agreed with the 1,034 current individual approvals, signed art direction and all 2,130 expected gameplay bindings. The final GLB, preview and report audit found zero mismatches. `git diff --check` passed, and Git reported no changes under `Assets/GodSim/Core`.

Both complete builds admitted 1,034 models, resolved 2,130 lookup keys and produced 280 canonical sheets. I independently constructed the expected output list from the checkout and compared the two saved inventories, then reread and hashed every corresponding working-tree file. All three sets had the same 4,905 entries and every digest matched. The combined inventory SHA-256 is `5182d6604c2060843a4f8d9ca2a0fbab77d57531af8c62c1f8e827783e83aa8a`. No generator source changed during the run. First inventory (local file, not in the repository) · Second inventory (local file, not in the repository)

The builds checked that every production GLB and every model/age preview was rewritten on both passes. The wider comparison also includes fixed material settings and supplemental evidence. Twenty-nine Human review PNGs predate these runs and were included in the byte comparison. Reproducibility was verified on this host. Unity and device measurements remain open as recorded above.
