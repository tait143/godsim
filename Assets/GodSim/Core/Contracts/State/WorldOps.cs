using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>
    /// The ONLY sanctioned way to create/destroy entities and change cross-entity links (home, job,
    /// settlement membership, ownership). Keeps references consistent (no dangling ids), publishes
    /// events, writes the chronicle, and defers store removals until <see cref="FlushRemovals"/>
    /// so systems can safely iterate while killing/destroying.
    /// </summary>
    public sealed class WorldOps
    {
        private readonly WorldState _s;
        private readonly GameDatabase _db;
        private readonly EventBus _bus;

        public WorldOps(WorldState state, GameDatabase db, EventBus bus)
        {
            _s = Guard.NotNull(state, nameof(state));
            _db = Guard.NotNull(db, nameof(db));
            _bus = bus ?? new EventBus();
        }

        public WorldState State => _s;

        // ================================================================== civilizations
        public Civilization CreateCivilization(RaceId race, string name = null)
        {
            var def = _db.Race(race);
            var civ = new Civilization
            {
                Id = _s.Ids.Civilization(),
                Race = race,
                Name = string.IsNullOrEmpty(name) ? NameGenerator.CivilizationName(def, _s.Rng) : name,
                Color = def.BannerColor,
                FoundedTick = _s.Tick,
                AgeEnteredTick = _s.Tick,
            };
            _s.Civilizations.Add(civ);
            _bus.Publish(new CivilizationFoundedEvent { CivilizationId = civ.Id, Race = race });
            AddChronicle(ChronicleCategory.Civilization, "The " + civ.Name + " of the " + def.PluralName + " rose from the wilds.", civ.Id, -1, Vec2i.Invalid, 2);
            return civ;
        }

        public void CollapseCivilization(Civilization civ, int conquerorId)
        {
            if (civ == null || !civ.IsAlive) return;
            civ.IsAlive = false;
            civ.CollapsedTick = _s.Tick;
            var armies = _s.Diplomacy.Armies.Items;
            for (int i = armies.Count - 1; i >= 0; i--) if (armies[i].CivilizationId == civ.Id) DisbandArmy(armies[i]);
            foreach (var war in _s.Diplomacy.Wars)
            {
                if (!war.IsActive || (war.AttackerId != civ.Id && war.DefenderId != civ.Id)) continue;
                war.IsActive = false;
                war.EndTick = _s.Tick;
                if (_s.Diplomacy.TryGet(war.AttackerId, war.DefenderId, out var rel)) { rel.WarId = -1; rel.Stance = DiplomaticStance.Hostile; }
                _bus.Publish(new PeaceSignedEvent { WarId = war.Id, AttackerId = war.AttackerId, DefenderId = war.DefenderId, WarScore = war.WarScore });
            }
            _bus.Publish(new CivilizationCollapsedEvent { CivilizationId = civ.Id, ConquerorId = conquerorId });
            var conq = conquerorId >= 0 ? _s.Civilizations.Get(conquerorId) : null;
            AddChronicle(ChronicleCategory.Civilization,
                conq != null ? "The " + civ.Name + " fell before the armies of the " + conq.Name + "." : "The " + civ.Name + " has faded from history.",
                civ.Id, -1, Vec2i.Invalid, 3);
        }

        // ================================================================== settlements
        public Settlement CreateSettlement(Civilization civ, Vec2i center, string name = null, int parentSettlementId = -1, int territoryRadius = 6)
        {
            Guard.NotNull(civ, nameof(civ));
            if (!_s.Map.InBounds(center)) throw new ArgumentOutOfRangeException(nameof(center));
            var def = _db.Race(civ.Race);
            var s = new Settlement
            {
                Id = _s.Ids.Settlement(),
                Name = string.IsNullOrEmpty(name) ? NameGenerator.SettlementName(def, _s.Rng) : name,
                CivilizationId = civ.Id,
                Race = civ.Race,
                Center = center,
                FoundedTick = _s.Tick,
                ParentSettlementId = parentSettlementId,
            };
            _s.Settlements.Add(s);
            civ.SettlementIds.Add(s.Id);
            if (civ.CapitalId < 0 || _s.Settlements.Get(civ.CapitalId) == null) { civ.CapitalId = s.Id; s.IsCapital = true; }
            ClaimTerritory(s, territoryRadius);
            _s.Stats.SettlementsFounded++;
            _bus.Publish(new SettlementFoundedEvent { SettlementId = s.Id, CivilizationId = civ.Id, Position = center, ParentSettlementId = parentSettlementId });
            AddChronicle(ChronicleCategory.Settlement, "The " + def.Adjective + " settlement of " + s.Name + " was founded.", civ.Id, s.Id, center, 1);
            return s;
        }

        /// <summary>Creates a settlement and places a completed Town Center at (or near) the center tile.</summary>
        public Settlement FoundSettlement(Civilization civ, Vec2i center, int parentSettlementId = -1, string name = null)
        {
            var s = CreateSettlement(civ, center, name, parentSettlementId);
            if (_db.TryGetBuilding(BuildingType.TownCenter, out var tc))
            {
                var site = FindFreeSite(center, tc.Width, tc.Height, 8);
                if (site.IsValid)
                {
                    var b = CreateBuilding(s, BuildingType.TownCenter, site, 0, true);
                    if (b != null) { s.TownCenterId = b.Id; s.Center = b.Center; }
                }
            }
            s.Stockpile.Add(ResourceType.Food, 60f);
            s.Stockpile.Add(ResourceType.Wood, 40f);
            s.Stockpile.Add(ResourceType.Stone, 10f);
            return s;
        }

        /// <summary>Claims unowned land tiles within a circle. Never steals tiles owned by others.</summary>
        public int ClaimTerritory(Settlement s, int radius)
        {
            radius = MathUtil.Clamp(radius, 1, 64);
            var map = _s.Map;
            int claimed = 0, r2 = radius * radius;
            for (int y = s.Center.Y - radius; y <= s.Center.Y + radius; y++)
                for (int x = s.Center.X - radius; x <= s.Center.X + radius; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - s.Center.X, dy = y - s.Center.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    if (map.Owner[i] >= 0 || map.IsWater(i)) continue;
                    map.Owner[i] = s.Id;
                    claimed++;
                }
            if (radius > s.TerritoryRadius) s.TerritoryRadius = radius;
            if (claimed > 0) _bus.Publish(new TerritoryChangedEvent { SettlementId = s.Id });
            return claimed;
        }

        public void ReleaseTerritory(Settlement s)
        {
            var map = _s.Map;
            int r = s.TerritoryRadius + 4;
            for (int y = s.Center.Y - r; y <= s.Center.Y + r; y++)
                for (int x = s.Center.X - r; x <= s.Center.X + r; x++)
                    if (map.InBounds(x, y) && map.Owner[map.Index(x, y)] == s.Id) map.Owner[map.Index(x, y)] = -1;
            _bus.Publish(new TerritoryChangedEvent { SettlementId = s.Id });
        }

        /// <summary>Abandons/destroys a settlement. Residents become refugees in the nearest friendly settlement, or perish.</summary>
        public void DestroySettlement(Settlement s, DestroyCause cause, bool killResidents = false)
        {
            if (s == null || !s.IsAlive) return;
            s.IsAlive = false;
            var civ = _s.Civilizations.Get(s.CivilizationId);
            foreach (int bid in s.BuildingIds.ToArray()) { var b = _s.Buildings.Get(bid); if (b != null) DestroyBuilding(b, cause); }
            Settlement refuge = killResidents ? null : NearestSettlementOf(s.CivilizationId, s.Center, s.Id);
            foreach (int pid in s.ResidentIds.ToArray())
            {
                var p = _s.People.Get(pid);
                if (p == null || !p.IsAlive) continue;
                if (refuge != null) MovePersonToSettlement(p, refuge);
                else KillPerson(p, cause == DestroyCause.War ? DeathCause.Violence : DeathCause.Exposure);
            }
            ReleaseTerritory(s);
            s.BuildQueue.Clear();
            _s.PendingSettlementRemovals.Add(s.Id);
            _bus.Publish(new SettlementDestroyedEvent { SettlementId = s.Id, CivilizationId = s.CivilizationId, Position = s.Center });
            AddChronicle(ChronicleCategory.Settlement, s.Name + " was " + (cause == DestroyCause.War ? "razed" : "abandoned") + ".", s.CivilizationId, s.Id, s.Center, 2);
            if (civ != null)
            {
                civ.SettlementIds.Remove(s.Id);
                if (civ.CapitalId == s.Id) ReassignCapital(civ);
                if (civ.SettlementIds.Count == 0) CollapseCivilization(civ, -1);
            }
        }

        /// <summary>Conquest: the settlement (with its people and buildings) changes owner.</summary>
        public void TransferSettlement(Settlement s, Civilization newOwner)
        {
            if (s == null || newOwner == null || !s.IsAlive || s.CivilizationId == newOwner.Id) return;
            var old = _s.Civilizations.Get(s.CivilizationId);
            int oldId = s.CivilizationId;
            s.CivilizationId = newOwner.Id;
            s.IsCapital = false;
            s.BuildQueue.Clear();
            newOwner.SettlementIds.Add(s.Id);
            foreach (int bid in s.BuildingIds) { var b = _s.Buildings.Get(bid); if (b != null) b.CivilizationId = newOwner.Id; }
            foreach (int pid in s.ResidentIds)
            {
                var p = _s.People.Get(pid);
                if (p == null) continue;
                p.CivilizationId = newOwner.Id;
                if (p.ArmyId >= 0) { var a = _s.Diplomacy.Armies.Get(p.ArmyId); a?.SoldierIds.Remove(p.Id); p.ArmyId = -1; }
            }
            _bus.Publish(new SettlementCapturedEvent { SettlementId = s.Id, OldCivilizationId = oldId, NewCivilizationId = newOwner.Id });
            AddChronicle(ChronicleCategory.War, s.Name + " was conquered by the " + newOwner.Name + ".", newOwner.Id, s.Id, s.Center, 2);
            if (old != null)
            {
                old.SettlementIds.Remove(s.Id);
                if (old.CapitalId == s.Id) ReassignCapital(old);
                if (old.SettlementIds.Count == 0) CollapseCivilization(old, newOwner.Id);
            }
        }

        private void ReassignCapital(Civilization civ)
        {
            civ.CapitalId = -1;
            Settlement best = null;
            foreach (int sid in civ.SettlementIds)
            {
                var c = _s.Settlements.Get(sid);
                if (c != null && c.IsAlive && (best == null || c.Population > best.Population)) best = c;
            }
            if (best != null) { civ.CapitalId = best.Id; best.IsCapital = true; }
        }

        public Settlement NearestSettlementOf(int civId, Vec2i from, int excludeId = -1)
        {
            Settlement best = null;
            int bestD = int.MaxValue;
            var all = _s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.IsAlive || c.Id == excludeId || c.CivilizationId != civId) continue;
                int d = c.Center.DistanceSqTo(from);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ================================================================== people
        public static LifeStage StageFor(RaceDefinition def, float ageYears) =>
            ageYears < def.AdulthoodAge ? LifeStage.Child : (ageYears < def.ElderAge ? LifeStage.Adult : LifeStage.Elder);

        /// <summary>Creates a person in a settlement. isBirth=true counts a birth and publishes PersonBornEvent.</summary>
        public Person CreatePerson(Settlement s, RaceId race, Sex sex, float ageYears, int motherId = -1, int fatherId = -1, bool isBirth = false)
        {
            Guard.NotNull(s, nameof(s));
            var def = _db.Race(race);
            var rng = _s.Rng;
            var mother = motherId >= 0 ? _s.People.Get(motherId) : null;
            var father = fatherId >= 0 ? _s.People.Get(fatherId) : null;
            var p = new Person
            {
                Id = _s.Ids.Person(),
                Race = race,
                Sex = sex,
                AgeYears = Math.Max(0f, ageYears),
                BirthTick = _s.Tick - SimTime.YearsToTicks(Math.Max(0f, ageYears)),
                SettlementId = s.Id,
                CivilizationId = s.CivilizationId,
                Position = s.Center,
                MotherId = motherId,
                FatherId = fatherId,
                Generation = Math.Max(mother?.Generation ?? -1, father?.Generation ?? -1) + 1,
                Skill = MathUtil.Clamp(rng.Gaussian(1f, 0.15f), 0.6f, 1.4f),
                Faith = MathUtil.Clamp(rng.Gaussian(0.5f * def.FaithRate, 0.15f), 0f, 1f),
            };
            p.Name = NameGenerator.PersonName(def, sex, rng, father ?? mother);
            p.Stage = StageFor(def, p.AgeYears);
            p.Traits = RollTraits(rng, mother, father);
            _s.People.Add(p);
            s.ResidentIds.Add(p.Id);
            if (mother != null) mother.ChildrenCount++;
            if (father != null) father.ChildrenCount++;
            if (isBirth)
            {
                _s.Stats.TotalBirths++;
                _bus.Publish(new PersonBornEvent { PersonId = p.Id, SettlementId = s.Id, CivilizationId = s.CivilizationId, Race = race, MotherId = motherId, FatherId = fatherId });
            }
            return p;
        }

        private static PersonTraits RollTraits(Rng rng, Person mother, Person father)
        {
            PersonTraits t = PersonTraits.None;
            PersonTraits inherited = (mother?.Traits ?? 0) | (father?.Traits ?? 0);
            for (int bit = 0; bit < 11; bit++) // Hero (bit 11) is never rolled; granted by events/prophets
            {
                var f = (PersonTraits)(1 << bit);
                float chance = (inherited & f) != 0 ? 0.35f : 0.06f;
                if (rng.Chance(chance)) t |= f;
            }
            if ((t & PersonTraits.Hardy) != 0) t &= ~PersonTraits.Sickly;
            return t;
        }

        public void KillPerson(Person p, DeathCause cause)
        {
            if (p == null || !p.IsAlive) return;
            p.IsAlive = false;
            p.DeathTick = _s.Tick;
            p.DeathCause = cause;
            ClearHome(p);
            ClearJob(p);
            if (p.ArmyId >= 0) { var a = _s.Diplomacy.Armies.Get(p.ArmyId); a?.SoldierIds.Remove(p.Id); p.ArmyId = -1; }
            if (p.SpouseId >= 0) { var sp = _s.People.Get(p.SpouseId); if (sp != null && sp.SpouseId == p.Id) sp.SpouseId = -1; p.SpouseId = -1; }
            var s = _s.Settlements.Get(p.SettlementId);
            s?.ResidentIds.Remove(p.Id);
            var civ = _s.Civilizations.Get(p.CivilizationId);
            if (civ != null && civ.LeaderId == p.Id) civ.LeaderId = -1;
            _s.Stats.TotalDeaths++;
            _s.Stats.DeathsByCause[(int)cause]++;
            _s.PendingPersonRemovals.Add(p.Id);
            _bus.Publish(new PersonDiedEvent { PersonId = p.Id, SettlementId = p.SettlementId, CivilizationId = p.CivilizationId, Cause = cause, AgeYears = p.AgeYears });
        }

        public bool AssignHome(Person p, Building home)
        {
            if (p == null || home == null || home.IsDestroyed || !home.IsComplete) return false;
            if (p.HomeId == home.Id) return true;
            if (home.FreeHousing <= 0) return false;
            ClearHome(p);
            home.Residents.Add(p.Id);
            p.HomeId = home.Id;
            p.Position = home.Center;
            return true;
        }

        public void ClearHome(Person p)
        {
            if (p == null || p.HomeId < 0) return;
            var h = _s.Buildings.Get(p.HomeId);
            h?.Residents.Remove(p.Id);
            p.HomeId = -1;
        }

        /// <summary>Assigns a job; workplace may be null for jobs without a building (e.g. Builder).</summary>
        public bool AssignJob(Person p, Building workplace, JobType job)
        {
            if (p == null || !p.IsAlive) return false;
            if (workplace != null)
            {
                if (workplace.IsDestroyed || !workplace.IsComplete) return false;
                if (p.WorkplaceId == workplace.Id && p.Job == job) return true;
                if (_db.TryGetBuilding(workplace.Type, out var def) && workplace.Workers.Count >= def.JobSlots) return false;
            }
            ClearJob(p);
            p.Job = job;
            p.Experience = 0f;
            if (workplace != null) { workplace.Workers.Add(p.Id); p.WorkplaceId = workplace.Id; }
            return true;
        }

        public void ClearJob(Person p)
        {
            if (p == null) return;
            if (p.WorkplaceId >= 0) { var w = _s.Buildings.Get(p.WorkplaceId); w?.Workers.Remove(p.Id); }
            p.WorkplaceId = -1;
            p.Job = JobType.None;
        }

        public void MovePersonToSettlement(Person p, Settlement to)
        {
            if (p == null || to == null || !p.IsAlive || !to.IsAlive || p.SettlementId == to.Id) return;
            int from = p.SettlementId;
            ClearHome(p);
            ClearJob(p);
            _s.Settlements.Get(from)?.ResidentIds.Remove(p.Id);
            to.ResidentIds.Add(p.Id);
            p.SettlementId = to.Id;
            p.CivilizationId = to.CivilizationId;
            p.Position = to.Center;
            _bus.Publish(new PersonMigratedEvent { PersonId = p.Id, FromSettlementId = from, ToSettlementId = to.Id });
        }

        // ================================================================== buildings
        /// <summary>True if every footprint tile is in bounds, buildable land and free. allowRugged also permits Mountain tiles (dwarves).</summary>
        public bool CanPlace(Vec2i origin, int width, int height, bool allowRugged = false)
        {
            var map = _s.Map;
            for (int y = origin.Y; y < origin.Y + height; y++)
                for (int x = origin.X; x < origin.X + width; x++)
                {
                    if (!map.InBounds(x, y)) return false;
                    int i = map.Index(x, y);
                    if (map.Biomes[i].IsBuildable()) { if (!map.IsFreeBuildable(i)) return false; continue; }
                    if (allowRugged && map.Biomes[i] == Biome.Mountain && map.BuildingAt[i] < 0 && (map.Flags[i] & (TileFlags.Road | TileFlags.Wall)) == 0) continue;
                    return false;
                }
            return true;
        }

        /// <summary>Spiral search for the nearest free footprint origin within maxRadius. Returns Vec2i.Invalid if none.</summary>
        public Vec2i FindFreeSite(Vec2i near, int width, int height, int maxRadius, bool allowRugged = false)
        {
            if (CanPlace(near, width, height, allowRugged)) return near;
            for (int r = 1; r <= maxRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        var p = new Vec2i(near.X + dx, near.Y + dy);
                        if (CanPlace(p, width, height, allowRugged)) return p;
                    }
            return Vec2i.Invalid;
        }

        /// <summary>Places a building (under construction unless complete=true). Returns null if the footprint is blocked.</summary>
        public Building CreateBuilding(Settlement s, BuildingType type, Vec2i origin, byte rotation = 0, bool complete = false, bool allowRugged = false)
        {
            Guard.NotNull(s, nameof(s));
            var def = _db.Building(type);
            if (!CanPlace(origin, def.Width, def.Height, allowRugged)) return null;
            var civ = _s.Civilizations.Get(s.CivilizationId);
            var b = new Building
            {
                Id = _s.Ids.Building(),
                Type = type,
                SettlementId = s.Id,
                CivilizationId = s.CivilizationId,
                Origin = origin,
                Width = def.Width,
                Height = def.Height,
                Rotation = (byte)(rotation & 3),
                Variant = _s.Rng.Range(0, 1 << 16),
                StyleRace = civ != null ? civ.Race : s.Race,
                StyleAge = civ != null ? civ.Age : AgeId.Dawn,
                PlacedTick = _s.Tick,
                HousingCapacity = def.HousingMax > 0 ? _s.Rng.RangeInclusive(def.HousingMin, def.HousingMax) : 0,
            };
            var map = _s.Map;
            for (int y = origin.Y; y < origin.Y + def.Height; y++)
                for (int x = origin.X; x < origin.X + def.Width; x++)
                {
                    int i = map.Index(x, y);
                    map.BuildingAt[i] = b.Id;
                    if (map.Owner[i] < 0) map.Owner[i] = s.Id;
                }
            _s.Buildings.Add(b);
            s.BuildingIds.Add(b.Id);
            _bus.Publish(new BuildingPlacedEvent { BuildingId = b.Id, Type = type, SettlementId = s.Id, Origin = origin });
            if (complete) CompleteBuilding(b);
            return b;
        }

        public void CompleteBuilding(Building b)
        {
            if (b == null || b.IsComplete || b.IsDestroyed) return;
            b.IsComplete = true;
            b.CostPaid = true;
            b.ConstructionProgress = 1f;
            b.Condition = 1f;
            b.CompletedTick = _s.Tick;
            _s.Stats.BuildingsCompleted++;
            if (b.Type == BuildingType.TownCenter)
            {
                var owner = _s.Settlements.Get(b.SettlementId);
                if (owner != null && (owner.TownCenterId < 0 || _s.Buildings.Get(owner.TownCenterId) == null)) owner.TownCenterId = b.Id;
            }
            _bus.Publish(new BuildingCompletedEvent { BuildingId = b.Id, Type = b.Type, SettlementId = b.SettlementId });
            if (_db.TryGetBuilding(b.Type, out var def) && def.IsWonder)
            {
                var s = _s.Settlements.Get(b.SettlementId);
                AddChronicle(ChronicleCategory.Building, "The wonder " + def.Name + " was completed in " + (s?.Name ?? "a distant land") + ".", b.CivilizationId, b.SettlementId, b.Center, 3);
            }
        }

        public void DestroyBuilding(Building b, DestroyCause cause)
        {
            if (b == null || b.IsDestroyed) return;
            b.IsDestroyed = true;
            foreach (int pid in b.Residents.ToArray()) { var p = _s.People.Get(pid); if (p != null) p.HomeId = -1; }
            b.Residents.Clear();
            foreach (int pid in b.Workers.ToArray()) { var p = _s.People.Get(pid); if (p != null) { p.WorkplaceId = -1; p.Job = JobType.None; } }
            b.Workers.Clear();
            var map = _s.Map;
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.BuildingAt[i] == b.Id) map.BuildingAt[i] = -1;
                    if (cause != DestroyCause.Demolished && b.IsComplete) map.Set(i, TileFlags.Ruins);
                }
            var s = _s.Settlements.Get(b.SettlementId);
            if (s != null)
            {
                s.BuildingIds.Remove(b.Id);
                if (s.TownCenterId == b.Id) s.TownCenterId = -1;
            }
            _s.Stats.BuildingsDestroyed++;
            _s.PendingBuildingRemovals.Add(b.Id);
            _bus.Publish(new BuildingDestroyedEvent { BuildingId = b.Id, Type = b.Type, SettlementId = b.SettlementId, Origin = b.Origin, Cause = cause });
        }

        // ================================================================== armies
        public Army CreateArmy(Civilization civ, Settlement home, IList<Person> soldiers)
        {
            Guard.NotNull(civ, nameof(civ));
            Guard.NotNull(home, nameof(home));
            var a = new Army
            {
                Id = _s.Ids.Army(),
                CivilizationId = civ.Id,
                HomeSettlementId = home.Id,
                Position = home.Center,
                Destination = home.Center,
                CreatedTick = _s.Tick,
            };
            if (soldiers != null)
                foreach (var p in soldiers)
                    if (p != null && p.IsAlive && p.ArmyId < 0) { a.SoldierIds.Add(p.Id); p.ArmyId = a.Id; }
            _s.Diplomacy.Armies.Add(a);
            _bus.Publish(new ArmyCreatedEvent { ArmyId = a.Id, CivilizationId = civ.Id, Position = a.Position });
            return a;
        }

        /// <summary>Removes a person from their army (e.g. a retiring or reassigned soldier). Safe if not in an army.</summary>
        public void RemoveFromArmy(Person p)
        {
            if (p == null || p.ArmyId < 0) return;
            var a = _s.Diplomacy.Armies.Get(p.ArmyId);
            a?.SoldierIds.Remove(p.Id);
            p.ArmyId = -1;
        }

        public void DisbandArmy(Army a)
        {
            if (a == null || !a.IsAlive) return;
            a.IsAlive = false;
            a.State = ArmyState.Disbanding;
            foreach (int pid in a.SoldierIds) { var p = _s.People.Get(pid); if (p != null && p.ArmyId == a.Id) p.ArmyId = -1; }
            a.SoldierIds.Clear();
            _s.PendingArmyRemovals.Add(a.Id);
            _bus.Publish(new ArmyDisbandedEvent { ArmyId = a.Id, CivilizationId = a.CivilizationId });
        }

        // ================================================================== chronicle
        public void AddChronicle(ChronicleCategory category, string text, int civId = -1, int settlementId = -1, Vec2i position = default, int importance = 1)
        {
            var e = new ChronicleEntry
            {
                Tick = _s.Tick, Category = category, Text = text ?? "", CivilizationId = civId,
                SettlementId = settlementId, Position = position, Importance = MathUtil.Clamp(importance, 0, 3)
            };
            _s.Chronicle.Add(e);
            _bus.Publish(new ChronicleAddedEvent { Entry = e });
        }

        // ================================================================== maintenance
        /// <summary>Physically removes entities marked dead/destroyed. Called by the engine after every system.</summary>
        public void FlushRemovals()
        {
            if (_s.PendingPersonRemovals.Count > 0) { foreach (int id in _s.PendingPersonRemovals) _s.People.Remove(id); _s.PendingPersonRemovals.Clear(); }
            if (_s.PendingBuildingRemovals.Count > 0) { foreach (int id in _s.PendingBuildingRemovals) _s.Buildings.Remove(id); _s.PendingBuildingRemovals.Clear(); }
            if (_s.PendingSettlementRemovals.Count > 0) { foreach (int id in _s.PendingSettlementRemovals) _s.Settlements.Remove(id); _s.PendingSettlementRemovals.Clear(); }
            if (_s.PendingArmyRemovals.Count > 0) { foreach (int id in _s.PendingArmyRemovals) _s.Diplomacy.Armies.Remove(id); _s.PendingArmyRemovals.Clear(); }
        }
    }
}
