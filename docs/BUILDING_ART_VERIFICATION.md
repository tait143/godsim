# Building art verification

## Coordinator repair round 2

The baseline for this round is `7e905bbc7c393fdfa647a0bf364239328e001203`. The coordinator’s independent all-pairs script and the in-repository audit matched on every family above 60% and its maximum rounded share. The coordinator’s final distribution had 206 multi-model families at or below 40%, 56 above 40% through 60%, and 23 above 60% through 80%. The highest actual fraction was 1,275/1,596, or 79.887%, for the Future Human Shrine measured against both its Ancient and Classical siblings. The independently reviewed Human shrine changes from an open canopy to a tall pointed frame. [Full measurements](../tools/AssetForge/reviews/rc4/final_audit.json)

The in-repository audit compares indexed LOD0 corners across all 3,230 directions, including comparisons within a single era for distinct Town Center and House ages. The 227 checkpoint judgments include the old GLB SHA-256 values and both directions. Hashes for the 32 inspected grayscale pages are also recorded, and missing or changed evidence fails publication. All 276 directions in the 27 mandatory families passed the ceiling. Each of the 154 revised GLBs changed positional geometry at all three LODs. The revised models account for 329 of the 2,096 age previews. [Independent checkpoint decisions](../tools/AssetForge/reviews/rc4/triage.json)

The final independent art-direction pass covered all 101 listed lineages on 19 painted pages and 34 grayscale pages. The 17 pages added in round 2 show 336 models at two 64-pixel camera angles, including all ten separate Town Center ages where applicable. The director closed nine holds after the replacement models passed package critique, and signed the catalog evidence digest `755e649839bc18281984253149f5b4dfeda06f658d5db3f118b0c9f23914ce48`. [Signed page hashes](../tools/AssetForge/reviews/art_direction_approval.json) · [Art direction and hold resolutions](../tools/AssetForge/reviews/art_direction.md)

All 24 AssetForge tests passed. The headless building integration project compiled offline with zero warnings and errors. All 19 NUnitLite tests passed against the republished catalog with zero skips. Its 200-building fixture assembled 409,720 vertices and 173,105 triangles into four single-material meshes containing 64, 64, 64 and eight bodies. Three meshes needed 32-bit indices. The test exercises the production CPU batching path with exported LOD0 geometry. GPU submissions and iPhone performance still need a device capture.

Two complete builds each regenerated all 71 packages, published 1,034 models with 2,130 runtime lookup keys, and produced 314 contact sheets. Both inventories matched byte for byte across 4,939 output paths. A separate read of all 4,939 files in the working tree reproduced every listed SHA-256. The canonical sorted-JSON digest is `7f2d87557eafb7598dd438b7ed58112cea642c0a22d1485f9c6a05007bacc7db`. The uncompressed, indented inventory file SHA-256 is `2670e7646a6308b3fed0e61b05086a57191461313b65b0416ac66c97ca219895`. [Complete rebuild inventory](../tools/AssetForge/reviews/rc4/rebuild_inventory.json.gz)

The independent whole-diff reviewer verified the two corrections, checked all 3,102 serialized LODs, matched all 53 signed page hashes and reconstructed the 908 checkpoint views, 672 final grayscale views and 336 age captions pixel for pixel. The audit reproduced the stored JSON exactly. [Independent final review](../tools/AssetForge/reviews/rc4/diff_review.md)

To reproduce the audit and refresh the report on this branch, run `python -B tools/AssetForge/building_round2.py audit` with the project interpreter. The full-build and test commands below apply to both repair rounds.

## Coordinator repair round 1

The comparison baseline is commit `91863de5a788e77dc2baaaed86fbd9a52c58df63`. The review uses serialized GLBs on the Apple M1 and the repository's NumPy/Pillow pipeline. Reviewers open the age PNGs and compare atlas-free renders of the exported geometry.

Reproduce the checks from the repository root with the project virtual environment active.

```sh
python -B -m unittest discover -s tools/AssetForge/tests -v
python -B tools/AssetForge/build_all.py --verify-rebuild --hash-dir "$verification_directory"
dotnet build build/checks/BuildingArt/BuildingArt.Tests.csproj --no-restore --disable-build-servers
dotnet build/checks/BuildingArt/bin/Debug/net8.0/BuildingArt.Tests.dll --noresult --workers=0
```

The rebuild inventories record a SHA-256 for every output. Publication checks both the painted family pages and the unpainted era comparisons against the director's signed hashes. The headless integration executable exercises the catalog, loader, LOD selection and batch mesh construction with managed fixtures.

## Repair checks

The 19 AssetForge tests passed on the final Farm revision. They cover the serialized LOD1 ceiling, winding of tapered shells, retention of broad wall, roof and tread surfaces, directional era comparisons, and rejection of stale reviews and rebuilds that reuse old model files. The headless test project compiled offline with zero warnings and zero errors. After the first strict build published the repaired manifest, all 19 NUnitLite integration tests passed, with zero skips. The 200-building case read the actual exported LOD0s and assembled 420,470 vertices and 176,994 triangles into four single-material meshes with 64, 64, 64 and eight buildings. Three meshes required 32-bit indices. This is a managed CPU batching result. GPU draw calls require a player capture.

The art director signed 19 painted family pages and 17 grayscale comparison pages. Each of the 361 era meshes on the grayscale pages appears at 64 pixels from two angles. The signed catalog evidence digest is `a019521f7faeff6fe8478107e730c5284b967c7ded349000903e89a5b1f4b0f1`. [Signed page hashes](../tools/AssetForge/reviews/art_direction_approval.json)

Two strict builds recreated all 71 packages in succession. Each published 1,034 models, 2,130 lookup keys and 297 contact sheets. The two inventories were byte-identical across all 4,922 paths, and a separate read of the working tree matched every listed SHA-256. The canonical inventory digest was `a75e4f69514711b03cd11a718241748de3a697b4ff53dc560a02e8ce4ac31bf9`. The digest uses sorted-key JSON as implemented in `build_all.py`. The repository copy contains the complete, indented inventory from both runs. Its uncompressed file SHA-256 is `2915041d9d91018e0126f655e98ab251900516da785181ba20912d264dfbb695`. [Full rebuild inventory](../tools/AssetForge/reviews/coordinator_rebuild.json.gz)

The coordinator’s original GLB audit found 285 groups with multiple models. It classified 191 as clearly different and 94 as averaging 50–80% shared positions. None had identical geometry or averaged above 80%. The stricter per-pair repair audit uses all directions and all era pairs in the 61 listed families. [Per-family measurements](../tools/AssetForge/reviews/coordinator_round1.json)

## Checkpoint test history

Before the repair, 15 AssetForge tests and 19 headless building integration tests passed. The checkpoint's two independent file inventories matched across 4,905 files. Their canonical inventory digest was `5182d6604c2060843a4f8d9ca2a0fbab77d57531af8c62c1f8e827783e83aa8a`. The 200-building headless fixture assembled four body meshes with 435,072 vertices and 183,314 triangles. The shader, shadow pass and driver draw calls require player verification.

The simulation check at the checkpoint used the cached .NET test host 17.12.0 because the pinned 17.11.1 test SDK was unavailable offline. Its recorded full-suite TRX listed 3,050 tests, 3,026 executed, 3,023 passed, 24 skipped and three failed. The earlier verification recorded the same outcomes on a second run.

| Simulation test | Recorded failure |
|---|---|
| `FamineRecovery_SettlementStarvesThenRecoversWithin15Years` | Eight famine events and zero starvation deaths. The assertion required at least one death. |
| `TownsGrowSensibly_After60Years` | Excess houses in multiple settlements and missing civic buildings. |
| `HousingRule_OccupancyAndHomelessnessStayWithinDesignBands` | Residents per completed house fell outside the design band. |

The repair leaves the simulation source untouched. The project specifies Unity 6000.0.23f1. The checkpoint's managed compilation used the locally available 6000.0.38f1 assemblies. Editor import, glTFast import on a real Unity player, shader compilation, iPhone profiling and repeatability on a second machine remain unverified.
