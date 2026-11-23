using System;
using System.Collections.Generic;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Moves goods between settlements.
    /// <para><b>Intra-civ:</b> living settlements of the same living civ within <see cref="IntraTradeRange"/> tiles
    /// (pairs visited in ascending id order) redistribute every resource: a donor holding more than 55% of its
    /// capacity sends to a receiver holding less than 20%, at most
    /// (5 + 3 × min(2, donor Commerce coverage)) × DeltaTicks / 24 units per pair and resource, without pushing the
    /// donor below or the receiver above 55%.</para>
    /// <para><b>Inter-civ:</b> for every relation with a trade agreement, contact and no war between two living civs,
    /// the capitals each export their highest relative surplus (versus the partner's fill level, gold excluded),
    /// at most 10 units per 24-tick run, and both earn gold = traded × 0.1 × own Trade modifier. The first trade of
    /// a pair is chronicled once (WorldState.Memory "trade.first.&lt;lo&gt;.&lt;hi&gt;" = 1).</para>
    /// </summary>
    public sealed class TradeSystem : ISimSystem
    {
        /// <summary>Maximum tile distance between two settlements of one civ that redistribute goods.</summary>
        public const int IntraTradeRange = 80;
        /// <summary>Fill ratio above which a settlement donates a resource.</summary>
        public const float DonorThreshold = 0.55f;
        /// <summary>Fill ratio below which a settlement receives a resource.</summary>
        public const float ReceiverThreshold = 0.2f;
        /// <summary>Base units per pair and resource per 24 ticks.</summary>
        public const float BaseThroughput = 5f;
        /// <summary>Extra units per point of donor Commerce coverage (coverage capped at <see cref="MaxCommerceBonusCoverage"/>).</summary>
        public const float ThroughputPerCommerceCoverage = 3f;
        /// <summary>Cap on the Commerce coverage that boosts throughput.</summary>
        public const float MaxCommerceBonusCoverage = 2f;
        /// <summary>Maximum units each capital exports per 24-tick inter-civ run.</summary>
        public const float MaxInterCivUnitsPerRun = 10f;
        /// <summary>Gold earned per traded unit (before the Trade modifier).</summary>
        public const float GoldPerTradedUnit = 0.1f;
        /// <summary>Minimum fill ratio of a resource before a capital will export it.</summary>
        public const float ExportMinFill = 0.3f;
        /// <summary>Fill ratio a capital keeps in reserve when exporting.</summary>
        public const float ExportReserve = 0.3f;
        /// <summary>Minimum fill-ratio advantage over the partner for a resource to be worth exporting.</summary>
        public const float MinExportAdvantage = 0.1f;
        /// <summary>Reference run length the per-run limits are expressed for.</summary>
        public const int ReferenceInterval = 24;

        private const float MinTransfer = 1e-4f;

        private static readonly Comparison<Settlement> ById = (a, b) => a.Id.CompareTo(b.Id);

        private readonly List<Settlement> _members = new List<Settlement>();
        private readonly List<Relation> _relations = new List<Relation>();

        /// <inheritdoc />
        public string Name => "Economy.Trade";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <summary>Runs intra-civ redistribution, then inter-civ capital trade.</summary>
        public void Tick(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            int dt = EconomyServicesShared.Delta(ctx, TickInterval);
            var state = ctx.State;

            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                s.Memory[EconomyServiceKeys.TradeImported] = 0f;
                s.Memory[EconomyServiceKeys.TradeExported] = 0f;
            }

            var civs = state.Civilizations.Items;
            for (int c = 0; c < civs.Count; c++)
            {
                var civ = civs[c];
                if (civ == null || !civ.IsAlive) continue;
                civ.Memory[EconomyServiceKeys.TradeIncome] = 0f;
                RedistributeWithinCiv(state, civ, dt);
            }

            RunInterCivTrade(ctx, dt);
        }

        // ------------------------------------------------------------------ intra-civ

        private void RedistributeWithinCiv(WorldState state, Civilization civ, int dt)
        {
            _members.Clear();
            var ids = civ.SettlementIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var s = state.Settlements.Get(ids[k]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                _members.Add(s);
            }
            if (_members.Count < 2) { _members.Clear(); return; }
            _members.Sort(ById);

            int rangeSq = IntraTradeRange * IntraTradeRange;
            float scale = dt / (float)ReferenceInterval;
            for (int i = 0; i < _members.Count; i++)
            {
                var a = _members[i];
                if (i > 0 && _members[i - 1].Id == a.Id) continue; // duplicate id in civ list
                for (int j = i + 1; j < _members.Count; j++)
                {
                    var b = _members[j];
                    if (b.Id == a.Id) continue;
                    if (a.Center.DistanceSqTo(b.Center) > rangeSq) continue;
                    for (int r = 0; r < Counts.Resources; r++)
                        Redistribute(a, b, (ResourceType)r, scale);
                }
            }
            _members.Clear();
        }

        private static void Redistribute(Settlement a, Settlement b, ResourceType res, float scale)
        {
            float capA = EconomyServicesShared.SafeCapacity(a.Stockpile);
            float capB = EconomyServicesShared.SafeCapacity(b.Stockpile);
            float amtA = a.Stockpile.Get(res);
            float amtB = b.Stockpile.Get(res);
            if (amtA > DonorThreshold * capA && amtB < ReceiverThreshold * capB)
                Transfer(a, b, res, Throughput(a) * scale, amtA - DonorThreshold * capA, DonorThreshold * capB - amtB);
            else if (amtB > DonorThreshold * capB && amtA < ReceiverThreshold * capA)
                Transfer(b, a, res, Throughput(b) * scale, amtB - DonorThreshold * capB, DonorThreshold * capA - amtA);
        }

        /// <summary>Units per pair and resource per 24 ticks a donor can ship: 5 + 3 × min(2, Commerce coverage).</summary>
        public static float Throughput(Settlement donor)
        {
            if (donor == null) return BaseThroughput;
            float cov = MathUtil.Clamp(MathUtil.Sanitize(donor.ServiceCoverage[(int)ServiceType.Commerce]), 0f, MaxCommerceBonusCoverage);
            return BaseThroughput + ThroughputPerCommerceCoverage * cov;
        }

        /// <summary>Moves up to the smallest of the limits from one stockpile to another; returns the amount moved.</summary>
        private static float Transfer(Settlement from, Settlement to, ResourceType res, float limitA, float limitB, float limitC)
        {
            float amount = limitA;
            if (limitB < amount) amount = limitB;
            if (limitC < amount) amount = limitC;
            float free = to.Stockpile.FreeSpace(res);
            if (free < amount) amount = free;
            amount = MathUtil.Sanitize(amount);
            if (!(amount > MinTransfer)) return 0f;

            float taken = from.Stockpile.ConsumeUpTo(res, amount);
            if (!(taken > 0f)) return 0f;
            float stored = to.Stockpile.Add(res, taken);
            if (stored < taken) from.Stockpile.Add(res, taken - stored); // refund rounding leftovers
            AddMemory(from, EconomyServiceKeys.TradeExported, stored);
            AddMemory(to, EconomyServiceKeys.TradeImported, stored);
            return stored;
        }

        private static void AddMemory(Settlement s, string key, float delta)
        {
            s.Memory[key] = MathUtil.Sanitize(s.Mem(key) + delta);
        }

        // ------------------------------------------------------------------ inter-civ

        private void RunInterCivTrade(SimContext ctx, int dt)
        {
            var state = ctx.State;
            // Snapshot first: chronicle events may reach handlers that touch the relation table.
            _relations.Clear();
            foreach (var r in state.Diplomacy.AllRelations)
                if (r != null && r.TradeAgreement && r.HasContact && !r.AtWar) _relations.Add(r);

            float maxUnits = MaxInterCivUnitsPerRun * (dt / (float)ReferenceInterval);
            for (int i = 0; i < _relations.Count; i++)
            {
                var rel = _relations[i];
                if (rel.CivA == rel.CivB) continue;
                var civA = state.Civilizations.Get(rel.CivA);
                var civB = state.Civilizations.Get(rel.CivB);
                if (civA == null || civB == null || !civA.IsAlive || !civB.IsAlive) continue;
                var capA = CapitalOf(state, civA);
                var capB = CapitalOf(state, civB);
                if (capA == null || capB == null || capA == capB) continue;

                float traded = ExchangeCapitals(capA, capB, maxUnits);
                if (!(traded > 0f)) continue;

                float goldA = capA.Stockpile.Add(ResourceType.Gold, traded * GoldPerTradedUnit * EconomyServicesShared.Multiplier(civA.Modifiers.Trade));
                float goldB = capB.Stockpile.Add(ResourceType.Gold, traded * GoldPerTradedUnit * EconomyServicesShared.Multiplier(civB.Modifiers.Trade));
                civA.Memory[EconomyServiceKeys.TradeIncome] = MathUtil.Sanitize(civA.Mem(EconomyServiceKeys.TradeIncome) + goldA);
                civB.Memory[EconomyServiceKeys.TradeIncome] = MathUtil.Sanitize(civB.Mem(EconomyServiceKeys.TradeIncome) + goldB);

                ChronicleFirstTrade(ctx, civA, civB, capA);
            }
            _relations.Clear();
        }

        /// <summary>Each capital exports its best surplus to the other (picked on the pre-trade state). Returns total units moved.</summary>
        private static float ExchangeCapitals(Settlement a, Settlement b, float maxUnits)
        {
            if (!(maxUnits > 0f)) return 0f;
            int exportA = PickExport(a, b);
            int exportB = PickExport(b, a);
            float traded = 0f;
            if (exportA >= 0) traded += Export(a, b, (ResourceType)exportA, maxUnits);
            if (exportB >= 0) traded += Export(b, a, (ResourceType)exportB, maxUnits);
            return traded;
        }

        /// <summary>
        /// The resource (gold excluded) with the largest fill-ratio advantage of the exporter over the importer,
        /// or -1 when nothing qualifies. Ties go to the lower resource index.
        /// </summary>
        public static int PickExport(Settlement exporter, Settlement importer)
        {
            if (exporter == null || importer == null) return -1;
            float capE = EconomyServicesShared.SafeCapacity(exporter.Stockpile);
            float capI = EconomyServicesShared.SafeCapacity(importer.Stockpile);
            int best = -1;
            float bestScore = MinExportAdvantage;
            for (int r = 0; r < Counts.Resources; r++)
            {
                if (r == (int)ResourceType.Gold) continue;
                var res = (ResourceType)r;
                float fillE = exporter.Stockpile.Get(res) / capE;
                if (fillE < ExportMinFill) continue;
                if (importer.Stockpile.FreeSpace(res) <= MinTransfer) continue;
                float score = fillE - importer.Stockpile.Get(res) / capI;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            return best;
        }

        private static float Export(Settlement from, Settlement to, ResourceType res, float maxUnits)
        {
            float cap = EconomyServicesShared.SafeCapacity(from.Stockpile);
            float available = from.Stockpile.Get(res) - ExportReserve * cap;
            return Transfer(from, to, res, maxUnits, available, float.MaxValue);
        }

        private static Settlement CapitalOf(WorldState state, Civilization civ)
        {
            if (civ.CapitalId < 0) return null;
            var s = state.Settlements.Get(civ.CapitalId);
            if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) return null;
            return s;
        }

        private static void ChronicleFirstTrade(SimContext ctx, Civilization civA, Civilization civB, Settlement at)
        {
            string key = EconomyServiceKeys.FirstTradeKey(civA.Id, civB.Id);
            var memory = ctx.State.Memory;
            if (memory.ContainsKey(key)) return;
            memory[key] = 1f;
            if (ctx.Ops == null) return;
            var lo = civA.Id < civB.Id ? civA : civB;
            var hi = civA.Id < civB.Id ? civB : civA;
            ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy,
                "Caravans began trading between the " + lo.Name + " and the " + hi.Name + ".",
                lo.Id, at.Id, at.Center, 1);
        }
    }
}
