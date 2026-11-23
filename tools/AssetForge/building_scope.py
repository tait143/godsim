"""Translate the simulation's building art work list into package-owned model specifications."""
from dataclasses import dataclass, replace
import json
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
WORK_LIST = ROOT / "assets/meshy/meshy_assets.json"
AGES = ("dawn", "bronze", "iron", "classical", "medieval", "renaissance",
        "industrial", "modern", "information", "ascension")
RACES = ("human", "elf", "dwarf", "orc", "saurian")
GROUPS = {
    "TownCenters": "TownCenter",
    "Food": "GathererHut HuntingLodge FishingHut Farm Pasture Mill IndustrialFarm HydroponicsTower",
    "Extraction": "WoodcutterCamp StoneQuarry Mine GoldMine CoalMine Sawmill OilWell",
    "Industry": "Smelter Weaver Blacksmith Armory Workshop Factory Foundry ElectronicsPlant",
    "Religion": "SpiritTotem Shrine Temple Cathedral Monastery",
    "LearningHealth": "Scriptorium Library University Observatory Printworks ResearchLab DataCenter Herbalist Bathhouse Hospital MedicalCenter",
    "CultureCivic": "Campfire Theater Arena Tavern Stadium Forum Arcology",
    "Commerce": "Market Harbor Guildhall Bank RailStation Airport",
    "Military": "Palisade Barracks Watchtower StoneWall Castle MilitaryBase",
    "Infrastructure": "Well Aqueduct PowerPlant FusionReactor Storehouse Granary",
}
GROUP_BY_TYPE = {name: group for group, names in GROUPS.items() for name in names.split()}
HOUSE_GROUP = {"ancient": "HousesAncient", "classical": "HousesClassical",
               "industrial": "HousesLate", "future": "HousesLate"}


@dataclass(frozen=True)
class Model:
    id: str
    source_ids: tuple
    race: str
    group: str
    building_type: str
    kind: str
    ages: tuple
    footprint: tuple
    variant: int
    band: str

    @property
    def package(self):
        return f"{self.race}_{self.group.lower()}"

    @property
    def category(self):
        return f"Buildings/{self.race.title()}/{self.group}"

    def metadata(self):
        from forge import AssetMeta
        return AssetMeta(id=self.id, kind=self.kind, category=self.category,
                         building_type=self.building_type, race=self.race,
                         ages=list(self.ages), footprint=list(self.footprint), variant=self.variant)


def work_list():
    items = json.loads(WORK_LIST.read_text())["items"]
    return [item for item in items if item["category"] in ("Buildings", "Wonders")]


def early_unlocks():
    """Playable early style ages that the era work list omits, from ContentReport's tech tables."""
    reference = (ROOT / "docs/CONTENT_REFERENCE.md").read_text()
    unlocks = set()
    for line in reference.splitlines():
        fields = [value.strip() for value in line.strip("|").split("|")]
        if not fields[0].startswith("`") or "normally " not in line:
            continue
        match = re.search(r"unlocks \*\*([^*]+)\*\* \(normally ", line)
        if match:
            age = fields[0].strip("`").split(".")[0]
            if age not in AGES:
                raise ValueError(f"unexpected tech age in {line}")
            races = RACES if fields[-1] == "All" else (fields[-1].lower(),)
            for race in races:
                unlocks.add((match[1].replace(" ", ""), race, age))
    return unlocks


def specifications():
    models = []
    wonders = {}
    for row in work_list():
        name = row["buildingType"]
        race, band = row["race"], row["band"]
        if row["category"] == "Wonders":
            wonders.setdefault((race, name), []).append(row)
            continue
        group = HOUSE_GROUP[band] if name == "House" else GROUP_BY_TYPE[name]
        kind = {"House": "house", "TownCenter": "townhall", "Castle": "townhall",
                "Forum": "townhall", "Arcology": "townhall"}.get(name, "building")
        ages = tuple(row["ages"])
        versions = [(row["id"], ages)]
        if name in ("House", "TownCenter"):
            # Preserve every source id on its first eligible age. Extend the same id family
            # for the other ages so ids stay unique across source eras and house variants.
            stem, variant = row["id"].rsplit("_", 1)
            versions = [(row["id"] if i == 0 else f"{stem}_{age}_{variant}", (age,))
                        for i, age in enumerate(ages)]
        for ident, used_ages in versions:
            models.append(Model(ident, (row["id"],), race, group, name, kind, used_ages,
                                tuple(row["footprint"]), row["variant"], band))
    for (race, name), rows in wonders.items():
        row = rows[0]
        ages = tuple(age for age in AGES if any(age in r["ages"] for r in rows))
        models.append(Model(row["id"], tuple(r["id"] for r in rows), race, "Wonders", name,
                            "wonder", ages, tuple(row["footprint"]), row["variant"], "all"))
    for name, race, age in sorted(early_unlocks()):
        candidates = [(i, m) for i, m in enumerate(models) if m.building_type == name and m.race == race]
        if any(age in m.ages for _, m in candidates):
            continue
        i, first = min(candidates, key=lambda pair: AGES.index(pair[1].ages[0]))
        # Preserve the encoded base row and original id. The additional preview uses a negative UV offset.
        models[i] = replace(first, ages=(*first.ages, age))
    return sorted(models, key=lambda m: (m.package, m.id))


def package_models(package):
    models = [m for m in specifications() if m.package == package]
    if not models:
        raise ValueError(f"unknown building package {package!r}")
    return models
