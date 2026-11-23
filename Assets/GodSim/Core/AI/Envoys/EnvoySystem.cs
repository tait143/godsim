using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Envoy statecraft: turns the civilization AI's diplomacy from "opinion drift plus war rolls" into readable
    /// stories. Every 48 ticks, in a fixed order:
    /// <list type="number">
    /// <item>betrayal grudges are applied (<see cref="PactEnvoy.ApplyGrudges"/>);</item>
    /// <item>coalitions form against (or dissolve around) a hegemon (<see cref="CoalitionEnvoy"/>);</item>
    /// <item>each civ (by index) may pay tribute to a looming bully (<see cref="TributeEnvoy"/>), then may send a gift
    /// (<see cref="GiftEnvoy"/>, rolled from its Diplomacy/Mercantilism propensity);</item>
    /// <item>each friendly pair may sign or renew a non-aggression pact (<see cref="PactEnvoy"/>) and may unite its royal
    /// houses (<see cref="MarriageEnvoy"/>).</item>
    /// </list>
    /// Betrayals are detected as they happen through <see cref="WarDeclaredEvent"/>. Opinion changes are impulses that the
    /// relations module's drift fades; truces (pacts, tribute) are honored by the war-decision module. All randomness goes
    /// through <c>ctx.Rng</c>; iteration is by store index; no allocations except chronicle text.
    /// </summary>
    public sealed class EnvoySystem : ISimSystem
    {
        private readonly Action<WarDeclaredEvent> _onWar;
        private SimContext _ctx;
        private EventBus _bus;

        /// <summary>Creates the system and its behaviours.</summary>
        public EnvoySystem() { _onWar = OnWarDeclared; }

        /// <inheritdoc />
        public string Name => "AI.Envoys";

        /// <inheritdoc />
        public int TickInterval => 48;

        /// <summary>Gift behaviour.</summary>
        public GiftEnvoy Gifts { get; } = new GiftEnvoy();
        /// <summary>Tribute behaviour.</summary>
        public TributeEnvoy Tribute { get; } = new TributeEnvoy();
        /// <summary>Coalition behaviour.</summary>
        public CoalitionEnvoy Coalitions { get; } = new CoalitionEnvoy();
        /// <summary>Pact and betrayal behaviour.</summary>
        public PactEnvoy Pacts { get; } = new PactEnvoy();
        /// <summary>Royal marriage behaviour.</summary>
        public MarriageEnvoy Marriages { get; } = new MarriageEnvoy();

        /// <summary>Years between two new pacts signed by the same civ.</summary>
        public const float NewPactCooldownYears = 1f;
        /// <summary>Years between two royal marriages of the same civ.</summary>
        public const float MarriageCooldownYears = 10f;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (_bus != null) _bus.Unsubscribe(_onWar);
            _ctx = ctx;
            _bus = ctx != null ? ctx.Events : null;
            if (_bus != null) _bus.Subscribe(_onWar);
        }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            if (_ctx == null || _ctx.State == null) return;
            Pacts.OnWarDeclared(_ctx, e.AttackerId, e.DefenderId);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            if (_ctx != ctx) _ctx = ctx;
            var state = ctx.State;
            int dt = Math.Max(1, ctx.DeltaTicks);

            Pacts.ApplyGrudges(ctx);
            Coalitions.Run(ctx);

            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (!EnvoyUtil.IsAlive(civ) || EnvoyUtil.Capital(state, civ) == null) continue;
                Tribute.TryPay(ctx, civ);
                if (Gifts.WantsToGive(ctx, civ)
                    && ctx.Rng.Chance(Rng.PerYearToPerTicks(EnvoyTuning.GiftChancePerYear * GiftEnvoy.Propensity(ctx.Db, civ), dt)))
                    Gifts.TryGive(ctx, civ);
            }

            long tick = state.Tick;
            for (int i = 0; i < civs.Count; i++)
            {
                var a = civs[i];
                if (!EnvoyUtil.IsAlive(a)) continue;
                for (int j = i + 1; j < civs.Count; j++)
                {
                    var b = civs[j];
                    if (!EnvoyUtil.IsAlive(b)) continue;
                    var r = EnvoyUtil.RelationOf(state, a.Id, b.Id);
                    if (r == null || !r.HasContact || !EnvoyUtil.IsFriendly(r)) continue;
                    TryPact(ctx, a, b, tick, dt);
                    TryMarriage(ctx, a, b, tick, dt);
                }
            }
        }

        private void TryPact(SimContext ctx, Civilization a, Civilization b, long tick, int dt)
        {
            if (!Pacts.CanSign(ctx, a, b)) return;
            if (Pacts.HasPact(ctx.State, a.Id, b.Id, tick)) { Pacts.Sign(ctx, a, b); return; } // quiet renewal
            if (!EnvoyUtil.CooldownReady(a, EnvoyKeys.PactCooldown, NewPactCooldownYears, tick)
                || !EnvoyUtil.CooldownReady(b, EnvoyKeys.PactCooldown, NewPactCooldownYears, tick)) return;
            float will = Math.Min(PactEnvoy.Willingness(ctx.Db, a), PactEnvoy.Willingness(ctx.Db, b));
            if (ctx.Rng.Chance(Rng.PerYearToPerTicks(EnvoyTuning.PactChancePerYear * will, dt))) Pacts.Sign(ctx, a, b);
        }

        private void TryMarriage(SimContext ctx, Civilization a, Civilization b, long tick, int dt)
        {
            float will = Marriages.Willingness(ctx, a, b);
            if (will <= 0f) return;
            if (!EnvoyUtil.CooldownReady(a, EnvoyKeys.MarriageCooldown, MarriageCooldownYears, tick)
                || !EnvoyUtil.CooldownReady(b, EnvoyKeys.MarriageCooldown, MarriageCooldownYears, tick)) return;
            if (ctx.Rng.Chance(Rng.PerYearToPerTicks(EnvoyTuning.MarriageChancePerYear * will, dt))) Marriages.Marry(ctx, a, b);
        }
    }
}
