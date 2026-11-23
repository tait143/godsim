namespace GodSim.Core.Economy
{
    /// <summary>
    /// Per-capita service demand: how many service points per resident per tick a settlement needs for
    /// a coverage of 1 (fully served). Indexed by <see cref="ServiceType"/>.
    /// </summary>
    public static class ServiceNeeds
    {
        /// <summary>Smallest need ever used in a coverage division (guards against a corrupted table).</summary>
        public const float MinNeed = 0.0001f;

        /// <summary>
        /// Points per resident per tick, indexed by (int)ServiceType: Faith 0.05, Health 0.05, Research 0.03,
        /// Entertainment 0.05, Defense 0.04, Water 0.1, Power 0.15, Commerce 0.04, Administration 0.04.
        /// Treat as read-only.
        /// </summary>
        public static readonly float[] PerCapita = Build();

        /// <summary>Need per resident for a service, sanitized (never NaN, never below <see cref="MinNeed"/>).</summary>
        public static float For(ServiceType service)
        {
            int i = (int)service;
            if (i < 0 || i >= PerCapita.Length) return MinNeed;
            float v = MathUtil.Sanitize(PerCapita[i], MinNeed);
            return v < MinNeed ? MinNeed : v;
        }

        private static float[] Build()
        {
            var a = new float[Counts.Services];
            a[(int)ServiceType.Faith] = 0.05f;
            a[(int)ServiceType.Health] = 0.05f;
            a[(int)ServiceType.Research] = 0.03f;
            a[(int)ServiceType.Entertainment] = 0.05f;
            a[(int)ServiceType.Defense] = 0.04f;
            a[(int)ServiceType.Water] = 0.1f;
            a[(int)ServiceType.Power] = 0.15f;
            a[(int)ServiceType.Commerce] = 0.04f;
            a[(int)ServiceType.Administration] = 0.04f;
            return a;
        }
    }
}
