using System;
using System.Collections.Generic;
using System.IO;

namespace GodSim.Core.Persistence.Format
{
    /// <summary>
    /// Writes/reads the WorldState payload in a fixed order of 4-byte-tagged sections (see docs/SAVE_FORMAT.md).
    /// The reader is schema-aware (<c>schema</c> parameter) so future fields can be gated on version.
    /// </summary>
    internal static class PayloadCodec
    {
        internal const string SecWorld = "WRLD", SecMap = "MAP_", SecRng = "RNG_", SecIds = "IDS_", SecPeople = "PEOP",
            SecBuildings = "BLDG", SecSettlements = "SETL", SecCivs = "CIVS", SecDiplomacy = "DIPL", SecGod = "GOD_",
            SecWeather = "WTHR", SecChronicle = "CHRN", SecStats = "STAT", SecMemory = "MEMW", SecEnd = "END_";

        // ============================================================================ write
        public static byte[] Write(WorldState s)
        {
            int estimate = 4096 + s.Map.TileCount * 30 + s.People.Count * 120 + s.Buildings.Count * 120;
            using (var ms = new MemoryStream(Math.Max(4096, Math.Min(estimate, 64 * 1024 * 1024))))
            {
                using (var w = new PayloadWriter(ms))
                {
                    w.Section(SecWorld); w.U64(s.Seed); w.I64(s.Tick);
                    WriteMap(w, s.Map);
                    w.Section(SecRng); w.U64(s.Rng.State); w.U64(s.Rng.Increment);
                    var ids = s.Ids;
                    w.Section(SecIds);
                    w.I32(ids.NextPersonId); w.I32(ids.NextBuildingId); w.I32(ids.NextSettlementId);
                    w.I32(ids.NextCivilizationId); w.I32(ids.NextArmyId); w.I32(ids.NextWarId);
                    WritePeople(w, s);
                    WriteBuildings(w, s);
                    WriteSettlements(w, s);
                    WriteCivilizations(w, s);
                    WriteDiplomacy(w, s.Diplomacy);
                    WriteGod(w, s.God);
                    WriteWeather(w, s.Weather);
                    WriteChronicle(w, s.Chronicle);
                    WriteStats(w, s.Stats);
                    w.Section(SecMemory); w.Memory(s.Memory, "world memory");
                    w.Section(SecEnd);
                    w.Flush();
                }
                return ms.ToArray();
            }
        }

        private static void WriteMap(PayloadWriter w, WorldMap m)
        {
            int n = m.TileCount;
            w.Section(SecMap);
            w.I32(m.Width); w.I32(m.Height); w.I32(n);
            for (int i = 0; i < n; i++) w.F32(m.Elevation[i]);
            for (int i = 0; i < n; i++) w.F32(m.Moisture[i]);
            for (int i = 0; i < n; i++) w.F32(m.Temperature[i]);
            for (int i = 0; i < n; i++) w.F32(m.Fertility[i]);
            for (int i = 0; i < n; i++) w.U8((byte)m.Biomes[i]);
            for (int i = 0; i < n; i++) w.U16((ushort)m.Flags[i]);
            for (int i = 0; i < n; i++) w.F32(m.Resource[i]);
            for (int i = 0; i < n; i++) w.I32(m.Owner[i]);
            for (int i = 0; i < n; i++) w.I32(m.BuildingAt[i]);
            w.F32(m.SeaLevel);
            w.I32(m.TerrainVersion);
        }

        private static void WritePeople(PayloadWriter w, WorldState s)
        {
            var items = s.People.Items;
            w.Section(SecPeople);
            w.Count(items.Count, SaveFormatLimits.MaxEntities, "people");
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                w.I32(p.Id); w.Str(p.Name, "person name");
                w.U8((byte)p.Race); w.U8((byte)p.Sex); w.I64(p.BirthTick); w.F32(p.AgeYears); w.U8((byte)p.Stage);
                w.Bool(p.IsAlive); w.I64(p.DeathTick); w.U8((byte)p.DeathCause);
                w.F32(p.Health); w.F32(p.Happiness); w.F32(p.Satiation); w.F32(p.Faith);
                w.U16((ushort)p.Traits); w.F32(p.Skill); w.F32(p.Experience);
                w.U8((byte)p.Job); w.I32(p.WorkplaceId); w.I32(p.HomeId); w.I32(p.SettlementId); w.I32(p.CivilizationId); w.I32(p.ArmyId);
                w.I32(p.MotherId); w.I32(p.FatherId); w.I32(p.SpouseId); w.I32(p.ChildrenCount); w.I32(p.Generation);
                w.Vec(p.Position);
            }
        }

        private static void WriteBuildings(PayloadWriter w, WorldState s)
        {
            var items = s.Buildings.Items;
            w.Section(SecBuildings);
            w.Count(items.Count, SaveFormatLimits.MaxEntities, "buildings");
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                w.I32(b.Id); w.U8((byte)b.Type); w.I32(b.SettlementId); w.I32(b.CivilizationId);
                w.Vec(b.Origin); w.I32(b.Width); w.I32(b.Height); w.U8(b.Rotation); w.I32(b.Variant);
                w.Bool(b.IsComplete); w.Bool(b.IsDestroyed); w.F32(b.ConstructionProgress); w.Bool(b.CostPaid);
                w.F32(b.Condition); w.I32(b.Level); w.Bool(b.IsOnFire); w.I32(b.HousingCapacity);
                w.IntList(b.Residents, SaveFormatLimits.MaxListCount, "building residents");
                w.IntList(b.Workers, SaveFormatLimits.MaxListCount, "building workers");
                w.F32(b.Efficiency); w.U8((byte)b.StyleRace); w.U8((byte)b.StyleAge); w.I64(b.PlacedTick); w.I64(b.CompletedTick);
            }
        }

        private static void WriteSettlements(PayloadWriter w, WorldState s)
        {
            var items = s.Settlements.Items;
            w.Section(SecSettlements);
            w.Count(items.Count, SaveFormatLimits.MaxEntities, "settlements");
            for (int i = 0; i < items.Count; i++)
            {
                var st = items[i];
                w.I32(st.Id); w.Str(st.Name, "settlement name"); w.I32(st.CivilizationId); w.U8((byte)st.Race);
                w.Vec(st.Center); w.I64(st.FoundedTick); w.U8((byte)st.Tier); w.Bool(st.IsCapital); w.Bool(st.IsAlive);
                w.I32(st.TownCenterId); w.I32(st.ParentSettlementId); w.I32(st.TerritoryRadius);
                w.IntList(st.BuildingIds, SaveFormatLimits.MaxListCount, "settlement buildings");
                w.IntList(st.ResidentIds, SaveFormatLimits.MaxListCount, "settlement residents");
                var sp = st.Stockpile;
                w.F32(sp.Capacity);
                w.I32(Counts.Resources);
                for (int r = 0; r < Counts.Resources; r++) w.F32(sp.Get((ResourceType)r));
                w.FloatArray(sp.AvgIncome);
                w.FloatArray(sp.AvgExpense);
                w.Count(st.BuildQueue.Count, SaveFormatLimits.MaxListCount, "build queue");
                for (int q = 0; q < st.BuildQueue.Count; q++)
                {
                    var o = st.BuildQueue[q];
                    if (o == null) throw PayloadWriter.TooBig("settlement " + st.Id + " has a null BuildOrder.");
                    w.U8((byte)o.Type); w.F32(o.Priority); w.I64(o.RequestedTick); w.Str(o.Reason, "build order reason");
                    w.I32(o.Attempts); w.Bool(o.HasPreferredSite); w.Vec(o.PreferredSite);
                }
                w.FloatArray(st.ServiceOutput); w.FloatArray(st.ServiceCoverage); w.FloatArray(st.JobPriority);
                w.I32(st.DesiredBuilders); w.I32(st.Homeless); w.I32(st.Unemployed); w.I32(st.Soldiers);
                w.F32(st.Happiness); w.F32(st.Health); w.F32(st.Unrest); w.F32(st.Faith); w.F32(st.DefenseStrength);
                w.I64(st.LastStarvationTick);
                w.Memory(st.Memory, "settlement memory");
                // schema 2 (appended): current-period (not yet rolled) flow counters, so the next RollStatistics matches a straight run
                w.I32(Counts.Resources);
                for (int r = 0; r < Counts.Resources; r++) { w.F32(sp.PeriodProduced((ResourceType)r)); w.F32(sp.PeriodConsumed((ResourceType)r)); }
            }
        }

        private static void WriteCivilizations(PayloadWriter w, WorldState s)
        {
            var items = s.Civilizations.Items;
            w.Section(SecCivs);
            w.Count(items.Count, SaveFormatLimits.MaxListCount, "civilizations");
            for (int i = 0; i < items.Count; i++)
            {
                var c = items[i];
                w.I32(c.Id); w.Str(c.Name, "civilization name"); w.U8((byte)c.Race); w.U32(c.Color); w.U8((byte)c.Age);
                w.I64(c.AgeEnteredTick); w.I64(c.FoundedTick); w.Bool(c.IsAlive); w.I64(c.CollapsedTick);
                w.I32(c.CapitalId);
                w.IntList(c.SettlementIds, SaveFormatLimits.MaxListCount, "civilization settlements");
                w.I32(c.LeaderId);
                w.F32(c.ResearchPool); w.F32(c.TotalResearch); w.I32(c.CurrentResearchTechId);
                w.Count(c.KnownTechs.Count, SaveFormatLimits.MaxListCount, "known techs");
                foreach (int t in c.KnownTechs) w.I32(t); // SortedSet: ascending
                w.BoolArray(c.UnlockedBuildings);
                var m = c.Modifiers;
                w.FloatArray(m.Production); w.FloatArray(m.Service);
                w.F32(m.Research); w.F32(m.Growth); w.F32(m.BuildSpeed); w.F32(m.Faith); w.F32(m.Military);
                w.F32(m.Storage); w.F32(m.Trade); w.F32(m.MoveSpeed);
                w.F32(m.HealthBonus); w.F32(m.HappinessBonus); w.F32(m.LifespanBonusYears);
                w.F32(c.Devotion); w.F32(c.GodFavor); w.I32(c.Population); w.F32(c.MilitaryPower);
                w.Memory(c.Memory, "civilization memory");
            }
        }

        private static void WriteDiplomacy(PayloadWriter w, DiplomacyState d)
        {
            w.Section(SecDiplomacy);
            var rels = new List<Relation>(d.AllRelations); // SortedDictionary order: ascending key
            w.Count(rels.Count, SaveFormatLimits.MaxListCount, "relations");
            for (int i = 0; i < rels.Count; i++)
            {
                var r = rels[i];
                w.I32(r.CivA); w.I32(r.CivB); w.F32(r.Opinion); w.U8((byte)r.Stance); w.Bool(r.HasContact); w.Bool(r.TradeAgreement);
                w.I64(r.TruceUntilTick); w.I64(r.LastContactTick); w.I32(r.WarId);
            }
            w.Count(d.Wars.Count, SaveFormatLimits.MaxListCount, "wars");
            for (int i = 0; i < d.Wars.Count; i++)
            {
                var war = d.Wars[i];
                if (war == null) throw PayloadWriter.TooBig("Diplomacy.Wars contains null.");
                w.I32(war.Id); w.I32(war.AttackerId); w.I32(war.DefenderId); w.I64(war.StartTick); w.I64(war.EndTick);
                w.Bool(war.IsActive); w.F32(war.WarScore); w.Str(war.CasusBelli, "casus belli");
                w.I32(war.Battles); w.I32(war.AttackerLosses); w.I32(war.DefenderLosses); w.I32(war.SettlementsCaptured);
            }
            var armies = d.Armies.Items;
            w.Count(armies.Count, SaveFormatLimits.MaxListCount, "armies");
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                w.I32(a.Id); w.I32(a.CivilizationId); w.I32(a.HomeSettlementId); w.I32(a.TargetSettlementId); w.I32(a.WarId);
                w.U8((byte)a.State); w.Vec(a.Position); w.Vec(a.Destination);
                w.F32(a.MoveProgress); w.F32(a.Strength); w.F32(a.Morale); w.Bool(a.IsAlive);
                w.IntList(a.SoldierIds, SaveFormatLimits.MaxListCount, "army soldiers");
                w.Count(a.Path.Count, SaveFormatLimits.MaxListCount, "army path");
                for (int k = 0; k < a.Path.Count; k++) w.Vec(a.Path[k]);
                w.I32(a.PathIndex); w.I64(a.CreatedTick);
            }
        }

        private static void WriteGod(PayloadWriter w, GodState g)
        {
            w.Section(SecGod);
            w.F32(g.Mana); w.F32(g.MaxMana); w.F32(g.ManaPerTick); w.F32(g.Alignment);
            w.LongArray(g.CooldownUntil);
            w.I32(g.PowersCast); w.I64(g.LastCastTick);
        }

        private static void WriteWeather(PayloadWriter w, WeatherState x)
        {
            w.Section(SecWeather);
            w.U8((byte)x.Season); w.F32(x.Rainfall); w.F32(x.TemperatureOffset); w.F32(x.FoodYieldMultiplier);
            w.I32(x.DroughtTicksRemaining); w.I32(x.RainTicksRemaining); w.F32(x.StormIntensity);
        }

        private static void WriteChronicle(PayloadWriter w, Chronicle c)
        {
            w.Section(SecChronicle);
            var e = c.Entries;
            int start = Math.Max(0, e.Count - Chronicle.MaxEntries); // Chronicle keeps only the newest MaxEntries
            w.I32(e.Count - start);
            for (int i = start; i < e.Count; i++)
            {
                var x = e[i];
                w.I64(x.Tick); w.U8((byte)x.Category); w.Str(x.Text, "chronicle text");
                w.I32(x.CivilizationId); w.I32(x.SettlementId); w.Vec(x.Position); w.I32(x.Importance);
            }
        }

        private static void WriteStats(PayloadWriter w, WorldStats st)
        {
            w.Section(SecStats);
            w.I64(st.TotalBirths); w.I64(st.TotalDeaths); w.I64(st.BuildingsCompleted); w.I64(st.BuildingsDestroyed);
            w.I64(st.SettlementsFounded); w.I64(st.WarsDeclared); w.I64(st.BattlesFought); w.I64(st.GodPowersCast);
            w.I32(st.PeakPopulation);
            w.LongArray(st.DeathsByCause);
        }

        // ============================================================================ read
        public static WorldState Read(BoundedReader r, int schema)
        {
            r.ExpectSection(SecWorld);
            ulong seed = r.U64();
            long tick = r.I64();
            if (tick < 0) throw BoundedReader.Corrupt("negative tick");
            var map = ReadMap(r);
            var s = new WorldState(seed, map) { Tick = tick };

            r.ExpectSection(SecRng);
            ulong rngState = r.U64(), rngInc = r.U64();
            if ((rngInc & 1UL) == 0) throw BoundedReader.Corrupt("RNG increment must be odd");
            s.Rng = Rng.FromState(rngState, rngInc);

            r.ExpectSection(SecIds);
            s.Ids.NextPersonId = r.I32(); s.Ids.NextBuildingId = r.I32(); s.Ids.NextSettlementId = r.I32();
            s.Ids.NextCivilizationId = r.I32(); s.Ids.NextArmyId = r.I32(); s.Ids.NextWarId = r.I32();

            ReadPeople(r, s);
            ReadBuildings(r, s);
            ReadSettlements(r, s, schema);
            ReadCivilizations(r, s);
            ReadDiplomacy(r, s);
            ReadGod(r, s.God);
            ReadWeather(r, s.Weather);
            ReadChronicle(r, s.Chronicle);
            ReadStats(r, s.Stats);
            r.ExpectSection(SecMemory);
            r.Memory(s.Memory, "world memory");
            r.ExpectSection(SecEnd);
            if (!r.AtEnd()) throw BoundedReader.Corrupt("trailing data after end marker");
            return s;
        }

        private static WorldMap ReadMap(BoundedReader r)
        {
            r.ExpectSection(SecMap);
            int w = r.I32(), h = r.I32(), n = r.I32();
            if (w < WorldMap.MinSize || h < WorldMap.MinSize || w > WorldMap.MaxSize || h > WorldMap.MaxSize)
                throw BoundedReader.Corrupt("map size " + w + "x" + h + " out of range");
            if ((long)w * h > SaveFormatLimits.MaxMapTiles || n != w * h) throw BoundedReader.Corrupt("map tile count mismatch");
            var m = new WorldMap(w, h);
            for (int i = 0; i < n; i++) m.Elevation[i] = r.F32("map elevation");
            for (int i = 0; i < n; i++) m.Moisture[i] = r.F32("map moisture");
            for (int i = 0; i < n; i++) m.Temperature[i] = r.F32("map temperature");
            for (int i = 0; i < n; i++) m.Fertility[i] = r.F32("map fertility");
            for (int i = 0; i < n; i++) m.Biomes[i] = r.Enum8<Biome>("map biome");
            for (int i = 0; i < n; i++) m.Flags[i] = (TileFlags)r.Flags16(FlagMasks.TileFlags, "map flags");
            for (int i = 0; i < n; i++) m.Resource[i] = r.F32("map resource");
            for (int i = 0; i < n; i++) m.Owner[i] = r.I32();
            for (int i = 0; i < n; i++) m.BuildingAt[i] = r.I32();
            m.SeaLevel = r.F32("map sea level");
            m.TerrainVersion = r.I32();
            return m;
        }

        private static void ReadPeople(BoundedReader r, WorldState s)
        {
            r.ExpectSection(SecPeople);
            int n = r.Count(SaveFormatLimits.MaxEntities, "people");
            for (int i = 0; i < n; i++)
            {
                var p = new Person();
                p.Id = r.I32(); p.Name = r.Str("person name");
                p.Race = r.Enum8<RaceId>("person race"); p.Sex = r.Enum8<Sex>("person sex"); p.BirthTick = r.I64();
                p.AgeYears = r.F32("person age"); p.Stage = r.Enum8<LifeStage>("person stage");
                p.IsAlive = r.Bool("person alive"); p.DeathTick = r.I64(); p.DeathCause = r.Enum8<DeathCause>("death cause");
                p.Health = r.F32("person health"); p.Happiness = r.F32("person happiness"); p.Satiation = r.F32("person satiation"); p.Faith = r.F32("person faith");
                p.Traits = (PersonTraits)r.Flags16(FlagMasks.PersonTraits, "person traits");
                p.Skill = r.F32("person skill"); p.Experience = r.F32("person experience");
                p.Job = r.Enum8<JobType>("person job");
                p.WorkplaceId = r.I32(); p.HomeId = r.I32(); p.SettlementId = r.I32(); p.CivilizationId = r.I32(); p.ArmyId = r.I32();
                p.MotherId = r.I32(); p.FatherId = r.I32(); p.SpouseId = r.I32(); p.ChildrenCount = r.I32(); p.Generation = r.I32();
                p.Position = r.Vec();
                if (s.People.Contains(p.Id)) throw BoundedReader.Corrupt("duplicate person id " + p.Id);
                s.People.Add(p);
            }
        }

        private static void ReadBuildings(BoundedReader r, WorldState s)
        {
            r.ExpectSection(SecBuildings);
            int n = r.Count(SaveFormatLimits.MaxEntities, "buildings");
            for (int i = 0; i < n; i++)
            {
                var b = new Building();
                b.Id = r.I32(); b.Type = r.Enum8<BuildingType>("building type"); b.SettlementId = r.I32(); b.CivilizationId = r.I32();
                b.Origin = r.Vec(); b.Width = r.I32(); b.Height = r.I32(); b.Rotation = r.U8(); b.Variant = r.I32();
                b.IsComplete = r.Bool("building complete"); b.IsDestroyed = r.Bool("building destroyed");
                b.ConstructionProgress = r.F32("construction progress"); b.CostPaid = r.Bool("cost paid");
                b.Condition = r.F32("building condition"); b.Level = r.I32(); b.IsOnFire = r.Bool("building on fire");
                b.HousingCapacity = r.I32();
                r.IntList(b.Residents, SaveFormatLimits.MaxListCount, "building residents");
                r.IntList(b.Workers, SaveFormatLimits.MaxListCount, "building workers");
                b.Efficiency = r.F32("building efficiency");
                b.StyleRace = r.Enum8<RaceId>("building style race"); b.StyleAge = r.Enum8<AgeId>("building style age");
                b.PlacedTick = r.I64(); b.CompletedTick = r.I64();
                if (s.Buildings.Contains(b.Id)) throw BoundedReader.Corrupt("duplicate building id " + b.Id);
                s.Buildings.Add(b);
            }
        }

        private static void ReadSettlements(BoundedReader r, WorldState s, int schema)
        {
            r.ExpectSection(SecSettlements);
            int n = r.Count(SaveFormatLimits.MaxEntities, "settlements");
            for (int i = 0; i < n; i++)
            {
                var st = new Settlement();
                st.Id = r.I32(); st.Name = r.Str("settlement name"); st.CivilizationId = r.I32(); st.Race = r.Enum8<RaceId>("settlement race");
                st.Center = r.Vec(); st.FoundedTick = r.I64(); st.Tier = r.Enum8<SettlementTier>("settlement tier");
                st.IsCapital = r.Bool("settlement capital"); st.IsAlive = r.Bool("settlement alive");
                st.TownCenterId = r.I32(); st.ParentSettlementId = r.I32(); st.TerritoryRadius = r.I32();
                r.IntList(st.BuildingIds, SaveFormatLimits.MaxListCount, "settlement buildings");
                r.IntList(st.ResidentIds, SaveFormatLimits.MaxListCount, "settlement residents");
                var sp = st.Stockpile;
                sp.Capacity = r.F32("stockpile capacity");
                int res = r.Count(Counts.Resources, "stockpile amounts");
                for (int k = 0; k < res; k++) sp.SetRaw((ResourceType)k, r.F32("stockpile amount"));
                r.FloatArray(sp.AvgIncome, "stockpile income");
                r.FloatArray(sp.AvgExpense, "stockpile expense");
                int q = r.Count(SaveFormatLimits.MaxListCount, "build queue");
                for (int k = 0; k < q; k++)
                {
                    var o = new BuildOrder();
                    o.Type = r.Enum8<BuildingType>("build order type"); o.Priority = r.F32("build order priority");
                    o.RequestedTick = r.I64(); o.Reason = r.Str("build order reason"); o.Attempts = r.I32();
                    o.HasPreferredSite = r.Bool("build order site flag"); o.PreferredSite = r.Vec();
                    st.BuildQueue.Add(o);
                }
                r.FloatArray(st.ServiceOutput, "service output");
                r.FloatArray(st.ServiceCoverage, "service coverage");
                r.FloatArray(st.JobPriority, "job priority");
                st.DesiredBuilders = r.I32(); st.Homeless = r.I32(); st.Unemployed = r.I32(); st.Soldiers = r.I32();
                st.Happiness = r.F32("settlement happiness"); st.Health = r.F32("settlement health"); st.Unrest = r.F32("settlement unrest");
                st.Faith = r.F32("settlement faith"); st.DefenseStrength = r.F32("settlement defense");
                st.LastStarvationTick = r.I64();
                r.Memory(st.Memory, "settlement memory");
                if (schema >= 2)
                {
                    int flows = r.Count(Counts.Resources, "stockpile period flows");
                    for (int k = 0; k < flows; k++)
                    {
                        float produced = r.F32("stockpile period produced");
                        float consumed = r.F32("stockpile period consumed");
                        sp.SetPeriodFlows((ResourceType)k, produced, consumed);
                    }
                }
                if (s.Settlements.Contains(st.Id)) throw BoundedReader.Corrupt("duplicate settlement id " + st.Id);
                s.Settlements.Add(st);
            }
        }

        private static void ReadCivilizations(BoundedReader r, WorldState s)
        {
            r.ExpectSection(SecCivs);
            int n = r.Count(SaveFormatLimits.MaxListCount, "civilizations");
            for (int i = 0; i < n; i++)
            {
                var c = new Civilization();
                c.Id = r.I32(); c.Name = r.Str("civilization name"); c.Race = r.Enum8<RaceId>("civilization race"); c.Color = r.U32();
                c.Age = r.Enum8<AgeId>("civilization age"); c.AgeEnteredTick = r.I64(); c.FoundedTick = r.I64();
                c.IsAlive = r.Bool("civilization alive"); c.CollapsedTick = r.I64(); c.CapitalId = r.I32();
                r.IntList(c.SettlementIds, SaveFormatLimits.MaxListCount, "civilization settlements");
                c.LeaderId = r.I32();
                c.ResearchPool = r.F32("research pool"); c.TotalResearch = r.F32("total research"); c.CurrentResearchTechId = r.I32();
                int techs = r.Count(SaveFormatLimits.MaxListCount, "known techs");
                int prev = -1;
                for (int k = 0; k < techs; k++)
                {
                    int t = r.I32();
                    if (t <= prev) throw BoundedReader.Corrupt("known techs not strictly increasing");
                    c.KnownTechs.Add(t);
                    prev = t;
                }
                r.BoolArray(c.UnlockedBuildings, "unlocked buildings");
                var m = c.Modifiers;
                r.FloatArray(m.Production, "production modifiers");
                r.FloatArray(m.Service, "service modifiers");
                m.Research = r.F32("modifier"); m.Growth = r.F32("modifier"); m.BuildSpeed = r.F32("modifier"); m.Faith = r.F32("modifier");
                m.Military = r.F32("modifier"); m.Storage = r.F32("modifier"); m.Trade = r.F32("modifier"); m.MoveSpeed = r.F32("modifier");
                m.HealthBonus = r.F32("modifier"); m.HappinessBonus = r.F32("modifier"); m.LifespanBonusYears = r.F32("modifier");
                c.Devotion = r.F32("devotion"); c.GodFavor = r.F32("god favor"); c.Population = r.I32(); c.MilitaryPower = r.F32("military power");
                r.Memory(c.Memory, "civilization memory");
                if (s.Civilizations.Contains(c.Id)) throw BoundedReader.Corrupt("duplicate civilization id " + c.Id);
                s.Civilizations.Add(c);
            }
        }

        private static void ReadDiplomacy(BoundedReader r, WorldState s)
        {
            r.ExpectSection(SecDiplomacy);
            var d = s.Diplomacy;
            int rels = r.Count(SaveFormatLimits.MaxListCount, "relations");
            long prevKey = long.MinValue;
            for (int i = 0; i < rels; i++)
            {
                var rel = new Relation();
                rel.CivA = r.I32(); rel.CivB = r.I32();
                if (rel.CivA >= rel.CivB) throw BoundedReader.Corrupt("relation civ ids not ordered");
                long key = DiplomacyState.Key(rel.CivA, rel.CivB);
                if (i > 0 && key <= prevKey) throw BoundedReader.Corrupt("relations not strictly ordered");
                prevKey = key;
                rel.Opinion = r.F32("relation opinion"); rel.Stance = r.Enum8<DiplomaticStance>("relation stance");
                rel.HasContact = r.Bool("relation contact"); rel.TradeAgreement = r.Bool("relation trade");
                rel.TruceUntilTick = r.I64(); rel.LastContactTick = r.I64(); rel.WarId = r.I32();
                d.AddRelation(rel);
            }
            int wars = r.Count(SaveFormatLimits.MaxListCount, "wars");
            var warIds = new HashSet<int>();
            for (int i = 0; i < wars; i++)
            {
                var war = new War();
                war.Id = r.I32(); war.AttackerId = r.I32(); war.DefenderId = r.I32(); war.StartTick = r.I64(); war.EndTick = r.I64();
                war.IsActive = r.Bool("war active"); war.WarScore = r.F32("war score"); war.CasusBelli = r.Str("casus belli");
                war.Battles = r.I32(); war.AttackerLosses = r.I32(); war.DefenderLosses = r.I32(); war.SettlementsCaptured = r.I32();
                if (!warIds.Add(war.Id)) throw BoundedReader.Corrupt("duplicate war id " + war.Id);
                d.Wars.Add(war);
            }
            int armies = r.Count(SaveFormatLimits.MaxListCount, "armies");
            for (int i = 0; i < armies; i++)
            {
                var a = new Army();
                a.Id = r.I32(); a.CivilizationId = r.I32(); a.HomeSettlementId = r.I32(); a.TargetSettlementId = r.I32(); a.WarId = r.I32();
                a.State = r.Enum8<ArmyState>("army state"); a.Position = r.Vec(); a.Destination = r.Vec();
                a.MoveProgress = r.F32("army progress"); a.Strength = r.F32("army strength"); a.Morale = r.F32("army morale");
                a.IsAlive = r.Bool("army alive");
                r.IntList(a.SoldierIds, SaveFormatLimits.MaxListCount, "army soldiers");
                int path = r.Count(SaveFormatLimits.MaxListCount, "army path");
                for (int k = 0; k < path; k++) a.Path.Add(r.Vec());
                a.PathIndex = r.I32(); a.CreatedTick = r.I64();
                if (d.Armies.Contains(a.Id)) throw BoundedReader.Corrupt("duplicate army id " + a.Id);
                d.Armies.Add(a);
            }
        }

        private static void ReadGod(BoundedReader r, GodState g)
        {
            r.ExpectSection(SecGod);
            g.Mana = r.F32("mana"); g.MaxMana = r.F32("max mana"); g.ManaPerTick = r.F32("mana per tick"); g.Alignment = r.F32("alignment");
            r.LongArray(g.CooldownUntil, "god cooldowns");
            g.PowersCast = r.I32(); g.LastCastTick = r.I64();
        }

        private static void ReadWeather(BoundedReader r, WeatherState x)
        {
            r.ExpectSection(SecWeather);
            x.Season = r.Enum8<Season>("weather season"); x.Rainfall = r.F32("rainfall"); x.TemperatureOffset = r.F32("temperature offset");
            x.FoodYieldMultiplier = r.F32("food yield"); x.DroughtTicksRemaining = r.I32(); x.RainTicksRemaining = r.I32();
            x.StormIntensity = r.F32("storm intensity");
        }

        private static void ReadChronicle(BoundedReader r, Chronicle c)
        {
            r.ExpectSection(SecChronicle);
            int n = r.Count(Chronicle.MaxEntries, "chronicle entries");
            for (int i = 0; i < n; i++)
            {
                var e = new ChronicleEntry();
                e.Tick = r.I64(); e.Category = r.Enum8<ChronicleCategory>("chronicle category"); e.Text = r.Str("chronicle text");
                e.CivilizationId = r.I32(); e.SettlementId = r.I32(); e.Position = r.Vec(); e.Importance = r.I32();
                c.Entries.Add(e); // direct add: preserves text exactly (Add() would re-truncate)
            }
        }

        private static void ReadStats(BoundedReader r, WorldStats st)
        {
            r.ExpectSection(SecStats);
            st.TotalBirths = r.I64(); st.TotalDeaths = r.I64(); st.BuildingsCompleted = r.I64(); st.BuildingsDestroyed = r.I64();
            st.SettlementsFounded = r.I64(); st.WarsDeclared = r.I64(); st.BattlesFought = r.I64(); st.GodPowersCast = r.I64();
            st.PeakPopulation = r.I32();
            r.LongArray(st.DeathsByCause, "deaths by cause");
        }
    }
}
