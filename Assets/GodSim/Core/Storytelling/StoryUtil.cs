using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    /// <summary>
    /// Deterministic world queries and clamped mutations shared by the event catalog. Iteration is always
    /// by list index (civ.SettlementIds, settlement.ResidentIds/BuildingIds, EntityStore.Items).
    /// </summary>
    internal static class StoryUtil
    {
        internal const int MaxScanRadius = 16;

        internal const TileFlags DepositOrFeature =
            TileFlags.GoldDeposit | TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.CoalDeposit |
            TileFlags.OilDeposit | TileFlags.Trees | TileFlags.Road | TileFlags.Wall | TileFlags.Ruins |
            TileFlags.Sacred | TileFlags.Burning;

        // ------------------------------------------------------------------ settlements & civs
        internal static Settlement Live(WorldState st, Civilization civ, int settlementId)
        {
            if (st == null || civ == null || settlementId < 0) return null;
            var s = st.Settlements.Get(settlementId);
            return s != null && s.IsAlive && s.CivilizationId == civ.Id ? s : null;
        }

        internal static Settlement Capital(WorldState st, Civilization civ) => civ == null ? null : Live(st, civ, civ.CapitalId);

        internal static int AliveSettlementCount(WorldState st, Civilization civ)
        {
            if (st == null || civ == null) return 0;
            int n = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++) if (Live(st, civ, ids[i]) != null) n++;
            return n;
        }

        internal static int AliveCivilizations(WorldState st)
        {
            int n = 0;
            var civs = st.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i].IsAlive) n++;
            return n;
        }

        internal static int CivPopulation(WorldState st, Civilization civ)
        {
            int n = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++) { var s = Live(st, civ, ids[i]); if (s != null) n += AliveResidents(st, s); }
            return n;
        }

        internal static Person AlivePerson(WorldState st, int id)
        {
            if (id < 0) return null;
            var p = st.People.Get(id);
            return p != null && p.IsAlive ? p : null;
        }

        internal static int AliveResidents(WorldState st, Settlement s)
        {
            if (s == null) return 0;
            int n = 0;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++) if (AlivePerson(st, ids[i]) != null) n++;
            return n;
        }

        /// <summary>Fills buf with alive residents passing the filter (null = all).</summary>
        internal static int CollectResidents(WorldState st, Settlement s, List<Person> buf, Func<Person, bool> filter)
        {
            buf.Clear();
            if (s == null) return 0;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = AlivePerson(st, ids[i]);
                if (p != null && (filter == null || filter(p))) buf.Add(p);
            }
            return buf.Count;
        }

        internal static int CountResidents(WorldState st, Settlement s, Func<Person, bool> filter)
        {
            if (s == null) return 0;
            int n = 0;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = AlivePerson(st, ids[i]);
                if (p != null && (filter == null || filter(p))) n++;
            }
            return n;
        }

        /// <summary>Most common race among alive residents; ties (and empty settlements) favor the settlement's race.</summary>
        internal static RaceId MajorityRace(WorldState st, Settlement s)
        {
            var counts = new int[Counts.Races];
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = AlivePerson(st, ids[i]);
                if (p != null && (int)p.Race < counts.Length) counts[(int)p.Race]++;
            }
            RaceId best = s.Race;
            int bestCount = (int)s.Race < counts.Length ? counts[(int)s.Race] : 0;
            for (int r = 0; r < counts.Length; r++)
                if (counts[r] > bestCount) { bestCount = counts[r]; best = (RaceId)r; }
            return best;
        }

        internal static int SoldierCount(WorldState st, Settlement s)
        {
            int n = CountResidents(st, s, p => p.Job == JobType.Soldier);
            return Math.Max(n, Math.Max(0, s.Soldiers));
        }

        internal static bool IsBesieged(WorldState st, Settlement s)
        {
            var armies = st.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a.IsAlive && a.State == ArmyState.Besieging && a.TargetSettlementId == s.Id && a.CivilizationId != s.CivilizationId) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ buildings
        internal static bool IsStanding(Building b) => b != null && !b.IsDestroyed;

        internal static bool HasCompleteBuilding(WorldState st, Settlement s, BuildingType type)
        {
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = st.Buildings.Get(ids[i]);
                if (IsStanding(b) && b.IsComplete && b.Type == type) return true;
            }
            return false;
        }

        internal static bool IsWonder(GameDatabase db, Building b) => db.TryGetBuilding(b.Type, out var def) && def.IsWonder;

        /// <summary>Fills buf with standing buildings passing the filter (null = all).</summary>
        internal static int CollectBuildings(WorldState st, Settlement s, List<Building> buf, Func<Building, bool> filter)
        {
            buf.Clear();
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = st.Buildings.Get(ids[i]);
                if (IsStanding(b) && (filter == null || filter(b))) buf.Add(b);
            }
            return buf.Count;
        }

        internal static int CountBuildings(WorldState st, Settlement s, Func<Building, bool> filter)
        {
            int n = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = st.Buildings.Get(ids[i]);
                if (IsStanding(b) && (filter == null || filter(b))) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ map
        internal static int ScanRadius(Settlement s) => MathUtil.Clamp(s.TerritoryRadius, 2, MaxScanRadius);

        /// <summary>Territory tile (Owner == s) passing the predicate; picks the nearest (or farthest) to the center. -1 if none.</summary>
        internal static int FindTerritoryTile(WorldMap map, Settlement s, Func<int, bool> pred, bool preferFar, int minDistance = 0)
        {
            int r = ScanRadius(s), r2 = r * r, min2 = minDistance * minDistance;
            int best = -1, bestD = preferFar ? -1 : int.MaxValue;
            for (int y = s.Center.Y - r; y <= s.Center.Y + r; y++)
                for (int x = s.Center.X - r; x <= s.Center.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - s.Center.X, dy = y - s.Center.Y, d2 = dx * dx + dy * dy;
                    if (d2 > r2 || d2 < min2) continue;
                    int i = map.Index(x, y);
                    if (map.Owner[i] != s.Id || !pred(i)) continue;
                    if (preferFar ? d2 > bestD : d2 < bestD) { bestD = d2; best = i; }
                }
            return best;
        }

        internal static bool NearWater(WorldMap map, Vec2i c, int r)
        {
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i) || (map.Flags[i] & (TileFlags.Coastal | TileFlags.River)) != 0) return true;
                }
            return false;
        }

        internal static int RuggedTilesNear(WorldMap map, Vec2i c, int r)
        {
            int n = 0;
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    var b = map.Biomes[map.Index(x, y)];
                    if (b == Biome.Hills || b == Biome.Mountain || b == Biome.Volcanic) n++;
                }
            return n;
        }

        internal static bool IsColdClimate(WorldMap map, Vec2i c)
        {
            if (!map.InBounds(c)) return false;
            int i = map.Index(c);
            var b = map.Biomes[i];
            return b == Biome.Tundra || b == Biome.Taiga || b == Biome.Snow || MathUtil.Sanitize(map.Temperature[i], 0.5f) < 0.3f;
        }

        // ------------------------------------------------------------------ clamped mutations
        internal static void ShiftMood(WorldState st, Settlement s, float delta)
        {
            if (s == null) return;
            s.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f) + delta);
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = AlivePerson(st, ids[i]);
                if (p != null) p.Happiness = MathUtil.Clamp01(MathUtil.Sanitize(p.Happiness, 0.5f) + delta);
            }
        }

        internal static void ShiftUnrest(Settlement s, float delta)
        {
            if (s != null) s.Unrest = MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest) + delta);
        }

        internal static void ShiftFaith(WorldState st, Settlement s, float settlementDelta, float personDelta)
        {
            if (s == null) return;
            s.Faith = MathUtil.Clamp01(MathUtil.Sanitize(s.Faith, 0.5f) + settlementDelta);
            if (personDelta == 0f) return;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = AlivePerson(st, ids[i]);
                if (p != null) p.Faith = MathUtil.Clamp01(MathUtil.Sanitize(p.Faith, 0.5f) + personDelta);
            }
        }

        internal static void ShiftDevotion(Civilization civ, float delta)
        {
            if (civ != null) civ.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f) + delta);
        }

        /// <summary>Adds to a relation's opinion, clamped to -100..100.</summary>
        internal static void ShiftOpinion(Relation r, float delta)
        {
            if (r != null) r.Opinion = MathUtil.Clamp(MathUtil.Sanitize(r.Opinion) + delta, -100f, 100f);
        }

        /// <summary>
        /// Research grant = (civ research output per tick x days), clamped to [min, max] scaled by age so the
        /// boost stays meaningful but bounded in every era.
        /// </summary>
        internal static float ResearchBoost(WorldState st, Civilization civ, float days, float min, float max)
        {
            float rate = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = Live(st, civ, ids[i]);
                if (s != null) rate += MathUtil.Clamp(MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Research]), 0f, 1e5f);
            }
            rate *= MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Research, 1f), 0f, 10f);
            float ageScale = 1f + 0.5f * (int)civ.Age;
            return MathUtil.Clamp(MathUtil.Sanitize(rate * days), min * ageScale, max * ageScale);
        }

        internal static void AddResearch(Civilization civ, float amount)
        {
            amount = MathUtil.Clamp(MathUtil.Sanitize(amount), 0f, 1e6f);
            civ.ResearchPool = MathUtil.Clamp(MathUtil.Sanitize(civ.ResearchPool) + amount, 0f, 1e12f);
            civ.TotalResearch = MathUtil.Clamp(MathUtil.Sanitize(civ.TotalResearch) + amount, 0f, 1e12f);
        }

        /// <summary>Adds up to the free capacity without distorting flow statistics. Returns the amount added.</summary>
        internal static float AddStock(Settlement s, ResourceType t, float amount)
        {
            float cur = s.Stockpile.Get(t);
            float cap = Math.Max(cur, MathUtil.Sanitize(s.Stockpile.Capacity, Stockpile.DefaultCapacity));
            float add = MathUtil.Clamp(MathUtil.Sanitize(amount), 0f, Math.Max(0f, cap - cur));
            if (add > 0f) s.Stockpile.SetRaw(t, cur + add);
            return add;
        }

        /// <summary>Removes a fraction (0..1) of a stockpile. Returns the amount removed.</summary>
        internal static float RemoveFraction(Settlement s, ResourceType t, float fraction)
        {
            float cur = s.Stockpile.Get(t);
            float take = cur * MathUtil.Clamp01(MathUtil.Sanitize(fraction));
            if (take > 0f) s.Stockpile.SetRaw(t, cur - take);
            return take;
        }

        // ------------------------------------------------------------------ random picks (effects only)
        /// <summary>Removes and returns a random element (order of the remaining list changes deterministically).</summary>
        internal static T TakeRandom<T>(Rng rng, List<T> pool) where T : class
        {
            if (pool.Count == 0) return null;
            int i = rng.Range(0, pool.Count);
            T item = pool[i];
            pool[i] = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return item;
        }

        internal static int KillRandom(SimContext ctx, List<Person> pool, int count, DeathCause cause)
        {
            int killed = 0;
            while (killed < count && pool.Count > 0)
            {
                var p = TakeRandom(ctx.Rng, pool);
                if (p == null || !p.IsAlive) continue;
                ctx.Ops.KillPerson(p, cause);
                killed++;
            }
            return killed;
        }

        internal static int RoomForPeople(SimContext ctx)
        {
            int cap = ctx.Config != null ? ctx.Config.MaxPopulation : 40000;
            return Math.Max(0, cap - ctx.State.LivingPopulation);
        }

        internal static RaceDefinition RaceDef(GameDatabase db, RaceId r)
        {
            int i = (int)r;
            return db != null && i >= 0 && i < db.Races.Count ? db.Races[i] : null;
        }
    }
}
