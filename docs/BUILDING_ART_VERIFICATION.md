# Building art verification

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
