using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>Deposit kinds the <see cref="ProspectorAdvisor"/> surveys (index into the <see cref="ProspectorKinds"/> tables).</summary>
    public enum ProspectorDeposit : byte { Ore = 0, Stone = 1, Gold = 2, Coal = 3, Oil = 4, Forest = 5 }

    /// <summary>
    /// Static, index-aligned data tables for the prospector: which tile flag marks a deposit, which placement rule an
    /// extractor of it carries, what it yields, from which age it matters, richness thresholds and the namespaced
    /// <c>adv.prosp.*</c> settlement blackboard keys. Also tiny allocation-light text helpers for vote reasons.
    /// </summary>
    public static class ProspectorKinds
    {
        /// <summary>Number of deposit kinds.</summary>
        public const int Count = 6;
        /// <summary>Index of the forest (Trees) kind.</summary>
        public const int Forest = (int)ProspectorDeposit.Forest;

        private static readonly string[] s_short = { "ore", "stone", "gold", "coal", "oil", "wood" };

        /// <summary>Tile flag marking the deposit.</summary>
        public static readonly TileFlags[] TileFlag =
        {
            TileFlags.OreDeposit, TileFlags.StoneDeposit, TileFlags.GoldDeposit, TileFlags.CoalDeposit, TileFlags.OilDeposit, TileFlags.Trees
        };

        /// <summary>Union of every deposit flag (fast reject while scanning).</summary>
        public const TileFlags AnyDepositFlag = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit |
                                                TileFlags.CoalDeposit | TileFlags.OilDeposit | TileFlags.Trees;

        /// <summary>Placement rule an extractor of the deposit carries (data-driven extractor detection).</summary>
        public static readonly PlacementRule[] NearRule =
        {
            PlacementRule.NearOre, PlacementRule.NearStone, PlacementRule.NearGold, PlacementRule.NearCoal, PlacementRule.NearOil, PlacementRule.NearForest
        };

        /// <summary>Union of every deposit-bound placement rule.</summary>
        public const PlacementRule AnyNearDeposit = PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold |
                                                    PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.NearForest;

        /// <summary>Resource the deposit ultimately yields.</summary>
        public static readonly ResourceType[] Yield =
        {
            ResourceType.Ore, ResourceType.Stone, ResourceType.Gold, ResourceType.Fuel, ResourceType.Fuel, ResourceType.Wood
        };

        /// <summary>Canonical extraction job (used when a definition has outputs missing, and to scope job votes).</summary>
        public static readonly JobType[] Job =
        {
            JobType.Miner, JobType.Quarrier, JobType.Miner, JobType.Miner, JobType.Miner, JobType.Woodcutter
        };

        /// <summary>Age from which the deposit matters at all (coal: Industrial, oil: Modern).</summary>
        public static readonly AgeId[] RelevantFrom =
        {
            AgeId.Dawn, AgeId.Dawn, AgeId.Dawn, AgeId.Industrial, AgeId.Modern, AgeId.Dawn
        };

        /// <summary>Lowercase display label.</summary>
        public static readonly string[] Label = { "ore seam", "stone outcrop", "gold vein", "coal seam", "oil field", "forest" };
        /// <summary>Capitalized display label.</summary>
        public static readonly string[] Title = { "Ore seam", "Stone outcrop", "Gold vein", "Coal seam", "Oil field", "Forest" };

        /// <summary>Remaining amount at which a deposit counts as fully rich (richness 1).</summary>
        public static readonly float[] RichAmount = { 1500f, 1500f, 1000f, 1500f, 1500f, 1200f };
        /// <summary>Remaining amount below which an exploited deposit counts as nearly exhausted.</summary>
        public static readonly float[] LowAmount = { 150f, 150f, 100f, 150f, 150f, 150f };
        /// <summary>Amount assumed for a flagged tile without a recorded stock (mirrors the economy's default).</summary>
        public static readonly float[] DefaultTileAmount = { 250f, 250f, 250f, 250f, 250f, 60f };

        // ------------------------------------------------------------------ blackboard keys (settlement memory)
        /// <summary>Remaining total amount per kind.</summary>
        public static readonly string[] AmountKey = Keys("amt");
        /// <summary>Flagged tile count per kind.</summary>
        public static readonly string[] TilesKey = Keys("tiles");
        /// <summary>Highest remaining amount seen for the current survey area.</summary>
        public static readonly string[] PeakKey = Keys("peak");
        /// <summary>Smoothed depletion rate (amount per year).</summary>
        public static readonly string[] RateKey = Keys("rate");

        public const string KeyTick = "adv.prosp.tick";
        public const string KeyRadius = "adv.prosp.radius";
        public const string KeyCenter = "adv.prosp.center";
        public const string KeyLand = "adv.prosp.land";
        public const string KeyFertile = "adv.prosp.fertile";
        public const string KeyWater = "adv.prosp.water";
        public const string KeyCoast = "adv.prosp.coast";
        public const string KeyRiver = "adv.prosp.river";
        public const string KeyHills = "adv.prosp.hills";
        public const string KeyMountain = "adv.prosp.mountain";
        /// <summary>1 while an exploited deposit is running low: the survey then refreshes every half year.</summary>
        public const string KeyWatch = "adv.prosp.watch";

        private static string[] Keys(string what)
        {
            var a = new string[Count];
            for (int i = 0; i < Count; i++) a[i] = "adv.prosp." + what + "." + s_short[i];
            return a;
        }

        /// <summary>True when the deposit kind matters to a civ in the given age.</summary>
        public static bool IsRelevant(int kind, AgeId age) => kind >= 0 && kind < Count && age >= RelevantFrom[kind];

        /// <summary>0..1 richness of a remaining amount (0 at twice the low mark, 1 at the rich mark).</summary>
        public static float Richness(int kind, float amount)
        {
            float low = LowAmount[kind] * 2f;
            return MathUtil.InverseLerp(low, Math.Max(low + 1f, RichAmount[kind]), MathUtil.Sanitize(amount));
        }

        // ------------------------------------------------------------------ text (only called for strong votes)
        /// <summary>Formats a non-negative amount with thousands separators, e.g. 1840 -> "1,840" (culture-independent).</summary>
        public static string Thousands(float v)
        {
            long n = (long)Math.Round(MathUtil.Clamp(MathUtil.Sanitize(v), 0f, 1e12f));
            string raw = n.ToString();
            if (raw.Length <= 3) return raw;
            var sb = new StringBuilder(raw.Length + raw.Length / 3);
            int lead = raw.Length % 3;
            if (lead == 0) lead = 3;
            sb.Append(raw, 0, lead);
            for (int i = lead; i < raw.Length; i += 3) { sb.Append(','); sb.Append(raw, i, 3); }
            return sb.ToString();
        }

        /// <summary>Formats a 0..1 fraction as a whole percent, e.g. 0.064 -> "6%".</summary>
        public static string Percent(float fraction) => MathUtil.RoundToInt(MathUtil.Clamp01(MathUtil.Sanitize(fraction)) * 100f).ToString() + "%";
    }
}
