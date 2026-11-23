namespace GodSim.Core.Economy
{
    /// <summary>
    /// Every tick, folds each living settlement's stockpile flows into moving averages
    /// (<see cref="Stockpile.RollStatistics"/>, alpha 0.05) and publishes food indicators the AI reads:
    /// <c>econ.foodDays</c> = Food / max(0.001, AvgExpense[Food]) (capped at 9999) and
    /// <c>econ.netFood</c> = Net(Food).
    /// </summary>
    public sealed class EconomyStatsSystem : ISimSystem
    {
        /// <summary>Smoothing factor for the flow moving averages.</summary>
        public const float Smoothing = 0.05f;
        /// <summary>Upper bound for the food-days indicator.</summary>
        public const float MaxFoodDays = 9999f;
        /// <summary>Smallest expense used as divisor for food days.</summary>
        public const float MinExpense = 0.001f;

        /// <inheritdoc />
        public string Name => "Economy.Stats";

        /// <inheritdoc />
        public int TickInterval => 1;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <summary>Rolls statistics and writes the food blackboard keys for every living settlement.</summary>
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var sp = s.Stockpile;
                sp.RollStatistics(Smoothing);
                s.Memory[EconomyServiceKeys.FoodDays] = FoodDays(sp.Get(ResourceType.Food), sp.AvgExpense[(int)ResourceType.Food]);
                s.Memory[EconomyServiceKeys.NetFood] = MathUtil.Sanitize(sp.Net(ResourceType.Food));
            }
        }

        /// <summary>Food / max(0.001, expense), clamped to 0..9999 and never NaN.</summary>
        public static float FoodDays(float food, float avgExpense)
        {
            float exp = MathUtil.Sanitize(avgExpense);
            if (exp < MinExpense) exp = MinExpense;
            float days = MathUtil.Sanitize(food) / exp;
            return MathUtil.Clamp(MathUtil.Sanitize(days, MaxFoodDays), 0f, MaxFoodDays);
        }
    }
}
