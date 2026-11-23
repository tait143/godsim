# Building package critiques

One builder owns each package generator and its output folder. The independent critic opens the generated PNGs, checks every age at 40–80 pixels and inspects the larger view for support, surface and modelling defects. The six scores apply to the weakest applicable age of that model. Use the geometry and UV validation output alongside the images. A pleasing image by itself cannot establish winding, LOD validity or texture references.

From the repository root, run `python tools/AssetForge/building_review.py <package>` with the project interpreter. It reopens each GLB and prints the three triangle counts, evidence digest and paths of all age previews. The digest covers the actual GLB, age previews, shared textures, material and shader.

The same command reports exposed coplanar surfaces by face index, atlas material and position. Check all three LODs after fixing a surface. List individual intersections with `python tools/AssetForge/tests/inspect_coplanar.py --exposed-only --limit 0 <model.glb>`. Exactly repeated LOD0 shapes in different eras or house variants also require a mesh revision. The lead's catalog survey checks these conditions across packages before assembling the manifest. New geometry requires fresh image evidence and independent review.

Save each round in `<package>.json`. The top-level fields are `package` and `rounds`. Within a round, record its one-based `round`, the independent `critic` name, and a list named `models`. Each model record has these fields.

| Field | Contents |
|---|---|
| `id` | Exact model ID |
| `evidence_sha256` | Digest of the files actually inspected in this round |
| `observations` | Model-specific observations from the images and technical checks |
| `scores` | Six numbers from 0–100 keyed by `silhouette`, `identity`, `construction`, `charm`, `colour`, and `technical` |
| `defects` | Every open finding with its concrete fix, or an empty array if all checks pass |

Keep previous rounds. A subsequent round may cover just the remediated models. A passing model requires 99 or more for technical health, 90 or more for each visual criterion, a matching evidence digest and zero open defects. The full-build command checks all records before publishing a playable manifest. Escalate a model still failing after three rounds to the lead and identify its outstanding defects in the report.
