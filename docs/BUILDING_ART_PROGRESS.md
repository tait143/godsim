# Building art production progress

Worktree `/Volumes/BattleRoomEmpire_SSD/GodSim`, branch `art/buildings`, base `2c7e33a`. The generated checklist below reports the exact package status from the work list, review records and published manifest.

All 71 generators produced their model lists. The individual package reviews and corrective rounds cover all 1,034 GLBs and 2,096 model/age previews. All 34 cross-catalog corrections passed the art director’s re-review. The 19 canonical comparison pages match the independent signature. All 1,034 models are published. The [art report](BUILDING_ART_REPORT.md) lists thresholds, actual counts, every critic score, escalations and verification limits.

Two complete signed builds reproduced 4,905 files with zero differences. The reproduction command is `tools/AssetForge/build_all.py --verify-rebuild --hash-dir DIRECTORY` with the project virtual environment. It runs one generator at a time. The per-model and per-age source mappings are available from `building_scope.specifications()`. No work was committed. Unity import and physical iPhone measurements remain unverified.

<!-- BEGIN GENERATED PACKAGE CHECKLIST -->
## Package checklist

1,034/1,034 models generated. 1,034 have current individual reviews and 1,034 are bound in the runtime manifest. The [art report](BUILDING_ART_REPORT.md) lists outstanding IDs and geometry findings.

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
