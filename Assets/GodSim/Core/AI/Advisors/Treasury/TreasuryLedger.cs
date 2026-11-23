using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// One settlement's books as the treasury reads them: gold stock, income (observed and modelled), upkeep of
    /// complete buildings, future upkeep (under construction + queued), the economy's unpaid-upkeep share,
    /// runway, stress and wealth, wonder funding needs and the terrain features that gate gold producers.
    /// Reused between settlements (no per-run allocations). All values are finite.
    /// </summary>
    public sealed class TreasuryLedger
    {
        /// <summary>Cap for "infinite" year values.</summary>
        public const float MaxYears = 99f;
        private const float Eps = 1e-4f;

        // money
        public float Gold, ObservedIncome, ModelIncome, Income, NetObserved, Trend;
        public bool HasTrend;
        public float Upkeep, PendingUpkeep, ProjectedUpkeep, Shortfall, Trade;
        public float CommerceCoverage, DefenseCoverage;
        // projections
        public float Deficit, FutureDeficit, RunwayYears, FutureRunwayYears, CoverYears, Margin, Surplus, HorizonYears;
        // verdicts
        public float Stress, Wealth;
        // wonder funding (capital only)
        public bool WonderPending;
        public BuildingType WonderType;
        public float WonderGoldCost, WonderNeed;
        // composition
        public int MilitaryComplete;
        public readonly int[] Existing = new int[Counts.Buildings];
        public readonly int[] Queued = new int[Counts.Buildings];
        public readonly bool[] GoldJobs = new bool[Counts.Jobs];
        // terrain within the territory
        public bool Coastal, Water, River, Hills, Mountain, Trees, Ore, Stone, Coal, Oil;
        public int GoldDeposits;

        /// <summary>Fills the ledger for one settlement.</summary>
        public void Compute(WorldState state, TreasuryBuildingBook book, Civilization civ, Settlement s, bool isCapital, int deltaTicks, float mercantilism)
        {
            Reset();
            if (state == null || book == null || s == null) return;
            var stock = s.Stockpile;
            int g = (int)ResourceType.Gold;

            Gold = NonNeg(stock.Get(ResourceType.Gold));
            Trade = civ != null ? MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Trade, 1f), 0f, 10f) : 1f;
            ObservedIncome = NonNeg(stock.AvgIncome[g]);
            NetObserved = MathUtil.Clamp(MathUtil.Sanitize(stock.Net(ResourceType.Gold)), -1000f, 1000f);
            Shortfall = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(TreasuryKeys.EconUpkeepShortfall)));
            CommerceCoverage = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Commerce]), 0f, 5f);
            DefenseCoverage = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Defense]), 0f, 5f);
            float model = NonNeg(s.ServiceOutput[(int)ServiceType.Commerce]) * TreasuryBuildingBook.GoldPerCommercePoint * Trade;

            // ---- buildings: upkeep now and later
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings || book.Defs[t] == null) continue;
                Existing[t]++;
                if (b.IsComplete)
                {
                    Upkeep += book.Upkeep[t];
                    var def = book.Defs[t];
                    if (book.MinedGold[t] > 0f)
                    {
                        float staff = def.JobSlots > 0 ? MathUtil.Clamp01(b.Workers.Count / (float)def.JobSlots) : 1f;
                        model += book.MinedGold[t] * staff * MathUtil.Clamp01(MathUtil.Sanitize(b.Condition, 1f));
                    }
                    if (book.Roles[t] == TreasuryRole.GoldSource && def.Job != JobType.None) GoldJobs[(int)def.Job] = true;
                    if (book.Roles[t] == TreasuryRole.Military) MilitaryComplete++;
                }
                else
                {
                    PendingUpkeep += book.Upkeep[t];
                    if (isCapital && book.IsWonder(t) && !b.CostPaid) NoteWonder((BuildingType)t, book.GoldCost[t]);
                }
            }
            var q = s.BuildQueue;
            for (int k = 0; k < q.Count; k++)
            {
                var o = q[k];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= Counts.Buildings || book.Defs[t] == null) continue;
                Queued[t]++;
                PendingUpkeep += book.Upkeep[t];
                if (isCapital && book.IsWonder(t)) NoteWonder(o.Type, book.GoldCost[t]);
            }

            ModelIncome = NonNeg(model);
            Income = Math.Max(ObservedIncome, ModelIncome);
            ProjectedUpkeep = Upkeep + PendingUpkeep;

            if (s.Memory.TryGetValue(TreasuryKeys.LastGold, out float last) && deltaTicks > 0)
            {
                Trend = MathUtil.Clamp(MathUtil.Sanitize((Gold - NonNeg(last)) / deltaTicks), -1000f, 1000f);
                HasTrend = true;
            }

            // ---- projections
            Deficit = Math.Max(0f, Math.Max(Upkeep - Income, Math.Max(-NetObserved, HasTrend ? -Trend : 0f)));
            FutureDeficit = Math.Max(0f, ProjectedUpkeep - Income);
            RunwayYears = Years(Gold, Deficit);
            FutureRunwayYears = Years(Gold, FutureDeficit);
            CoverYears = Years(Gold, ProjectedUpkeep);
            Surplus = Income - ProjectedUpkeep;
            Margin = ProjectedUpkeep > Eps ? MathUtil.Clamp(Surplus / ProjectedUpkeep, -1f, 5f) : (Income > Eps ? 5f : 0f);
            HorizonYears = 3f + 5f * MathUtil.Clamp01(mercantilism); // mercantile civs plan further ahead

            // ---- stress: unpaid bills, draining runway, a deficit on the horizon, thin reserves
            float sShort = Shortfall > 0.001f ? 0.65f + 0.35f * Shortfall : 0f;
            float sRunway = Deficit > Eps ? 1f - MathUtil.InverseLerp(0.5f, HorizonYears, RunwayYears) : 0f;
            float sFuture = FutureDeficit > Eps ? 0.75f * (1f - MathUtil.InverseLerp(1f, HorizonYears * 1.5f, FutureRunwayYears)) : 0f;
            float sThin = Upkeep > Eps && CoverYears < 0.5f && Income < ProjectedUpkeep * 1.05f ? 0.4f * (1f - CoverYears / 0.5f) : 0f;
            Stress = MathUtil.Clamp01(Math.Max(Math.Max(sShort, sRunway), Math.Max(sFuture, sThin)));

            // ---- wealth: deep reserves and a surplus
            float stockF = MathUtil.InverseLerp(80f, 800f, Gold);
            float coverF = ProjectedUpkeep > Eps ? MathUtil.InverseLerp(1f, 8f, CoverYears) : stockF;
            float marginF = ProjectedUpkeep > Eps ? MathUtil.InverseLerp(-0.05f, 0.5f, Margin) : (Income > Eps ? 1f : 0.5f);
            float w = Math.Max(coverF * marginF, stockF * (Surplus >= 0f ? 1f : 0.3f));
            Wealth = Gold < 30f ? 0f : MathUtil.Clamp01(w * (1f - MathUtil.Clamp01(Stress * 3f)));

            // ---- wonder funding need
            if (WonderPending)
            {
                float lack = Math.Max(0f, WonderGoldCost - Gold);
                WonderNeed = WonderGoldCost > Eps ? 0.45f + 0.55f * MathUtil.InverseLerp(0f, WonderGoldCost, lack) : 0.35f;
            }

            SurveyTerrain(state.Map, s);
            Sanitize();
        }

        /// <summary>The treasury mode implied by stress and wealth.</summary>
        public TreasuryMode Mode
        {
            get
            {
                if (Stress >= 0.5f) return TreasuryMode.Austerity;
                if (Stress >= 0.2f) return TreasuryMode.Caution;
                if (Wealth >= 0.4f) return TreasuryMode.Prosperity;
                return TreasuryMode.Balanced;
            }
        }

        /// <summary>True when the hard placement requirements of a rule are satisfiable in this territory.</summary>
        public bool PlacementFeasible(PlacementRule r)
        {
            if ((r & PlacementRule.Coastal) != 0 && !Coastal) return false;
            if ((r & PlacementRule.NearWater) != 0 && !(Water || River || Coastal)) return false;
            if ((r & PlacementRule.NearRiver) != 0 && !River) return false;
            if ((r & PlacementRule.OnHills) != 0 && !Hills) return false;
            if ((r & PlacementRule.NearMountain) != 0 && !Mountain) return false;
            if ((r & PlacementRule.NearForest) != 0 && !Trees) return false;
            if ((r & PlacementRule.NearOre) != 0 && !Ore) return false;
            if ((r & PlacementRule.NearStone) != 0 && !Stone) return false;
            if ((r & PlacementRule.NearGold) != 0 && GoldDeposits <= 0) return false;
            if ((r & PlacementRule.NearCoal) != 0 && !Coal) return false;
            if ((r & PlacementRule.NearOil) != 0 && !Oil) return false;
            return true;
        }

        private void NoteWonder(BuildingType t, float goldCost)
        {
            if (!WonderPending) WonderType = t;
            WonderPending = true;
            WonderGoldCost += goldCost;
        }

        private void SurveyTerrain(WorldMap map, Settlement s)
        {
            if (map == null) return;
            int r = MathUtil.Clamp(s.TerritoryRadius, 3, 16);
            int cx = s.Center.X, cy = s.Center.Y;
            TileFlags acc = TileFlags.None;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= map.Height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= map.Width) continue;
                    int i = map.Index(x, y);
                    var f = map.Flags[i];
                    acc |= f;
                    if ((f & TileFlags.GoldDeposit) != 0) GoldDeposits++;
                    var bio = map.Biomes[i];
                    if (bio == Biome.Hills) Hills = true;
                    else if (bio == Biome.Mountain) Mountain = true;
                    else if (bio.IsWater()) Water = true;
                }
            }
            Coastal = (acc & TileFlags.Coastal) != 0;
            River = (acc & TileFlags.River) != 0;
            Trees = (acc & TileFlags.Trees) != 0;
            Ore = (acc & TileFlags.OreDeposit) != 0;
            Stone = (acc & TileFlags.StoneDeposit) != 0;
            Coal = (acc & TileFlags.CoalDeposit) != 0;
            Oil = (acc & TileFlags.OilDeposit) != 0;
        }

        private void Reset()
        {
            Gold = ObservedIncome = ModelIncome = Income = NetObserved = Trend = 0f;
            HasTrend = false;
            Upkeep = PendingUpkeep = ProjectedUpkeep = Shortfall = 0f;
            Trade = 1f;
            CommerceCoverage = DefenseCoverage = 0f;
            Deficit = FutureDeficit = Surplus = Margin = 0f;
            RunwayYears = FutureRunwayYears = CoverYears = MaxYears;
            HorizonYears = 3f;
            Stress = Wealth = 0f;
            WonderPending = false; WonderType = BuildingType.TownCenter; WonderGoldCost = 0f; WonderNeed = 0f;
            MilitaryComplete = 0;
            Array.Clear(Existing, 0, Existing.Length);
            Array.Clear(Queued, 0, Queued.Length);
            Array.Clear(GoldJobs, 0, GoldJobs.Length);
            Coastal = Water = River = Hills = Mountain = Trees = Ore = Stone = Coal = Oil = false;
            GoldDeposits = 0;
        }

        private void Sanitize()
        {
            Income = NonNeg(Income); Upkeep = NonNeg(Upkeep); PendingUpkeep = NonNeg(PendingUpkeep);
            ProjectedUpkeep = NonNeg(ProjectedUpkeep); Deficit = NonNeg(Deficit); FutureDeficit = NonNeg(FutureDeficit);
            Surplus = MathUtil.Clamp(MathUtil.Sanitize(Surplus), -1000f, 1000f);
            Stress = MathUtil.Clamp01(MathUtil.Sanitize(Stress)); Wealth = MathUtil.Clamp01(MathUtil.Sanitize(Wealth));
            WonderNeed = MathUtil.Clamp01(MathUtil.Sanitize(WonderNeed)); WonderGoldCost = NonNeg(WonderGoldCost);
        }

        private static float Years(float gold, float perTick)
        {
            if (!(perTick > Eps)) return MaxYears;
            return MathUtil.Clamp(MathUtil.Sanitize(gold / perTick / SimTime.TicksPerYear, MaxYears), 0f, MaxYears);
        }

        private static float NonNeg(float v) => TreasuryBuildingBook.NonNeg(v);
    }
}
