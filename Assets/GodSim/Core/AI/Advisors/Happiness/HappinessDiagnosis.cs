using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The happiness advisor's reading of one settlement (reused between settlements; copy what you need).
    /// Losses are estimated happiness points each cause costs, mirroring the wellbeing formula
    /// (housing 0.35, satiation 0.2, entertainment 0.1, faith 0.08, war 0.08), plus grief and remoteness.
    /// </summary>
    public sealed class HappinessDiagnosis
    {
        public int SettlementId = -1;
        public int Population;
        public float Happiness, Unrest;
        public float HappinessTrend, UnrestTrend, PopulationTrend;
        public float UnrestForecast, HappinessForecast;
        public float EntertainmentCoverage, EntertainmentProjected;
        public float FaithCoverage, FaithProjected;
        public int Homeless, PredictedHomeless;
        public float FoodDays, FoodSeverity;
        public bool Starving, AtWar, Rising, Urgent;
        public float RecentDeaths, DeathShare;
        public float Remote, DistanceToCapital;
        public float HousingLoss, FoodLoss, EntertainmentLoss, FaithLoss, WarLoss, GriefLoss, RemoteLoss, TotalLoss;
        public HappinessCause Cause;
        public float CauseLoss;
        public float Risk, Urgency;

        /// <summary>Clears every field for a new settlement.</summary>
        public void Reset(int settlementId)
        {
            SettlementId = settlementId;
            Population = 0;
            Happiness = Unrest = HappinessTrend = UnrestTrend = PopulationTrend = 0f;
            UnrestForecast = HappinessForecast = 0f;
            EntertainmentCoverage = EntertainmentProjected = FaithCoverage = FaithProjected = 0f;
            Homeless = PredictedHomeless = 0;
            FoodDays = FoodSeverity = 0f;
            Starving = AtWar = Rising = Urgent = false;
            RecentDeaths = DeathShare = Remote = DistanceToCapital = 0f;
            HousingLoss = FoodLoss = EntertainmentLoss = FaithLoss = WarLoss = GriefLoss = RemoteLoss = TotalLoss = 0f;
            Cause = HappinessCause.None;
            CauseLoss = Risk = Urgency = 0f;
        }

        /// <summary>One-line human-readable summary for debug UI (allocates; call on demand only).</summary>
        public string Describe()
        {
            var sb = new StringBuilder(96);
            sb.Append("mood ").Append(Pct(Happiness)).Append("% unrest ").Append(Pct(Unrest)).Append('%');
            if (Rising) sb.Append(" (rising)");
            sb.Append(", risk ").Append(Pct(Risk)).Append("%, cause ").Append(Cause);
            if (Homeless > 0) sb.Append(", ").Append(Homeless).Append(" homeless");
            if (FoodSeverity > 0.25f) sb.Append(", ").Append(MathUtil.RoundToInt(FoodDays)).Append(" days of food");
            sb.Append(", fun ").Append(Pct(EntertainmentCoverage)).Append('%');
            if (Remote > 0.05f) sb.Append(", remote ").Append(Pct(Remote)).Append('%');
            return sb.ToString();
        }

        internal static int Pct(float v) => MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(v), 0f, 9.99f) * 100f);
    }
}
