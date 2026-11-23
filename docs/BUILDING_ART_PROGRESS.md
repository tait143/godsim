# Building art production progress

## Coordinator repair round 2

Working from `7e905bb`. An exact audit reproduced the coordinator's 101 families across 3,230 ordered comparisons of 285 multi-model families. At the checkpoint 27 families required redesign, accounting for 142 directed comparisons over 60%. The 74 families subject to visual triage had 408 directed comparisons above 60% across 227 distinct model pairs. The [checkpoint audit](../tools/AssetForge/reviews/rc4/checkpoint_pairs.json) lists every affected ID in both directions. The [independent pair ledger](../tools/AssetForge/reviews/rc4/triage.json) records the critic’s decision for all 227 pairs.

The independent package critics have approved all 154 current changed models. Each of the nine director holds has a revised model and a fresh package judgment. The director reopened the final 19 painted and 34 unpainted sheets, closed all nine holds and signed the 101 listed families. Two full builds matched for all 4,939 outputs. All 24 AssetForge tests and all 19 headless integration tests passed. [Director findings](../tools/AssetForge/reviews/art_direction.md) The [independent final diff review](../tools/AssetForge/reviews/rc4/diff_review.md) closed both findings. All 71 packages passed. The changes remain in the working tree for the coordinator’s commit.

## Coordinator repair round 1

The repair on `art/buildings` compares the catalog with checkpoint `91863de`. Artists reworked all 61 listed era families, the separately pictured Elf Herbalist and 57 additional families that received specific findings during review. The repair changed 461 GLBs. All 1,034 currently meet the 60% LOD1 ceiling. [Binary measurements and model IDs](../tools/AssetForge/reviews/coordinator_round1.json)

All 71 packages have current passing independent reviews. The art director signed all 19 painted comparison sheets and 17 unpainted sheets after the revised Saurian Future Farm passed package round 10. All director findings are closed. Two complete builds matched across 4,922 files, and all 19 AssetForge and 19 headless integration tests passed. The independent whole-diff reviewer closed both findings. [Signed art direction](../tools/AssetForge/reviews/art_direction.md)

<!-- BEGIN GENERATED PACKAGE CHECKLIST -->
## Package checklist

1,034/1,034 models generated. 1,034 have current individual reviews and 1,034 are bound in the runtime manifest. The [art report](BUILDING_ART_REPORT.md) records individual scores and findings.

| Package | Due | Generated | Passing critique | In manifest | Next work |
|---|---:|---:|---:|---:|---|
| `dwarf_commerce` | 16 | 16 | 16 | 16 | package passed |
| `dwarf_culturecivic` | 17 | 17 | 17 | 17 | package passed |
| `dwarf_extraction` | 22 | 22 | 22 | 22 | package passed |
| `dwarf_food` | 20 | 20 | 20 | 20 | package passed |
| `dwarf_housesancient` | 6 | 6 | 6 | 6 | package passed |
| `dwarf_housesclassical` | 6 | 6 | 6 | 6 | package passed |
| `dwarf_houseslate` | 8 | 8 | 8 | 8 | package passed |
| `dwarf_industry` | 23 | 23 | 23 | 23 | package passed |
| `dwarf_infrastructure` | 18 | 18 | 18 | 18 | package passed |
| `dwarf_learninghealth` | 30 | 30 | 30 | 30 | package passed |
| `dwarf_military` | 14 | 14 | 14 | 14 | package passed |
| `dwarf_religion` | 15 | 15 | 15 | 15 | package passed |
| `dwarf_towncenters` | 10 | 10 | 10 | 10 | package passed |
| `dwarf_wonders` | 1 | 1 | 1 | 1 | package passed |
| `elf_commerce` | 16 | 16 | 16 | 16 | package passed |
| `elf_culturecivic` | 17 | 17 | 17 | 17 | package passed |
| `elf_extraction` | 22 | 22 | 22 | 22 | package passed |
| `elf_food` | 20 | 20 | 20 | 20 | package passed |
| `elf_housesancient` | 6 | 6 | 6 | 6 | package passed |
| `elf_housesclassical` | 6 | 6 | 6 | 6 | package passed |
| `elf_houseslate` | 8 | 8 | 8 | 8 | package passed |
| `elf_industry` | 23 | 23 | 23 | 23 | package passed |
| `elf_infrastructure` | 18 | 18 | 18 | 18 | package passed |
| `elf_learninghealth` | 30 | 30 | 30 | 30 | package passed |
| `elf_military` | 14 | 14 | 14 | 14 | package passed |
| `elf_religion` | 15 | 15 | 15 | 15 | package passed |
| `elf_towncenters` | 10 | 10 | 10 | 10 | package passed |
| `elf_wonders` | 1 | 1 | 1 | 1 | package passed |
| `human_commerce` | 16 | 16 | 16 | 16 | package passed |
| `human_culturecivic` | 17 | 17 | 17 | 17 | package passed |
| `human_extraction` | 22 | 22 | 22 | 22 | package passed |
| `human_food` | 20 | 20 | 20 | 20 | package passed |
| `human_housesancient` | 6 | 6 | 6 | 6 | package passed |
| `human_housesclassical` | 6 | 6 | 6 | 6 | package passed |
| `human_houseslate` | 8 | 8 | 8 | 8 | package passed |
| `human_industry` | 23 | 23 | 23 | 23 | package passed |
| `human_infrastructure` | 18 | 18 | 18 | 18 | package passed |
| `human_learninghealth` | 30 | 30 | 30 | 30 | package passed |
| `human_military` | 14 | 14 | 14 | 14 | package passed |
| `human_religion` | 15 | 15 | 15 | 15 | package passed |
| `human_towncenters` | 10 | 10 | 10 | 10 | package passed |
| `human_wonders` | 1 | 1 | 1 | 1 | package passed |
| `neutral_wonders` | 4 | 4 | 4 | 4 | package passed |
| `orc_commerce` | 16 | 16 | 16 | 16 | package passed |
| `orc_culturecivic` | 17 | 17 | 17 | 17 | package passed |
| `orc_extraction` | 22 | 22 | 22 | 22 | package passed |
| `orc_food` | 20 | 20 | 20 | 20 | package passed |
| `orc_housesancient` | 6 | 6 | 6 | 6 | package passed |
| `orc_housesclassical` | 6 | 6 | 6 | 6 | package passed |
| `orc_houseslate` | 8 | 8 | 8 | 8 | package passed |
| `orc_industry` | 23 | 23 | 23 | 23 | package passed |
| `orc_infrastructure` | 18 | 18 | 18 | 18 | package passed |
| `orc_learninghealth` | 30 | 30 | 30 | 30 | package passed |
| `orc_military` | 14 | 14 | 14 | 14 | package passed |
| `orc_religion` | 15 | 15 | 15 | 15 | package passed |
| `orc_towncenters` | 10 | 10 | 10 | 10 | package passed |
| `orc_wonders` | 1 | 1 | 1 | 1 | package passed |
| `saurian_commerce` | 16 | 16 | 16 | 16 | package passed |
| `saurian_culturecivic` | 17 | 17 | 17 | 17 | package passed |
| `saurian_extraction` | 22 | 22 | 22 | 22 | package passed |
| `saurian_food` | 20 | 20 | 20 | 20 | package passed |
| `saurian_housesancient` | 6 | 6 | 6 | 6 | package passed |
| `saurian_housesclassical` | 6 | 6 | 6 | 6 | package passed |
| `saurian_houseslate` | 8 | 8 | 8 | 8 | package passed |
| `saurian_industry` | 23 | 23 | 23 | 23 | package passed |
| `saurian_infrastructure` | 18 | 18 | 18 | 18 | package passed |
| `saurian_learninghealth` | 30 | 30 | 30 | 30 | package passed |
| `saurian_military` | 14 | 14 | 14 | 14 | package passed |
| `saurian_religion` | 15 | 15 | 15 | 15 | package passed |
| `saurian_towncenters` | 10 | 10 | 10 | 10 | package passed |
| `saurian_wonders` | 1 | 1 | 1 | 1 | package passed |
<!-- END GENERATED PACKAGE CHECKLIST -->
