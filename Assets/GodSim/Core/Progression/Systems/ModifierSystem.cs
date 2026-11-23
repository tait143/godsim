using System;
using System.Collections.Generic;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// Aggregates <see cref="Civilization.Modifiers"/> from race × age × known techs, applies tech building unlocks
    /// and refreshes <see cref="Civilization.Population"/>.
    /// Runs every tick as a cheap dirty check. The full recompute happens when <see cref="ProgressionKeys.Dirty"/> is set,
    /// the first time a civ is seen, and every <see cref="RecomputeInterval"/> ticks (staggered by civ id).
    /// The recompute is a pure function of race, age and techs, so extra recomputes never change the outcome.
    /// <para>Freshness: <see cref="ResearchSystem"/> and <see cref="AgeProgressionSystem"/> call <see cref="RecomputeFor"/>
    /// right after they change a civ's techs or age, so at every tick boundary the modifiers of every civ already match
    /// race × age × techs. Only a request from another module made after this tick's Progression phase can still be
    /// pending (it is applied by the next <see cref="Tick"/>).</para>
    /// </summary>
    public sealed class ModifierSystem : ISimSystem
    {
        /// <summary>Ticks between periodic full recomputes of a civ (staggered by civ id).</summary>
        public const int RecomputeInterval = 12;
        /// <summary>Upper bound for any multiplier (protects against runaway content).</summary>
        public const float MaxMultiplier = 1000f;

        // Scratch buffers for the static recompute. Core is single-threaded; ThreadStatic keeps parallel test runners safe.
        [ThreadStatic] private static float[] _prodAddScratch;
        [ThreadStatic] private static float[] _serviceAddScratch;

        private readonly HashSet<int> _seen = new HashSet<int>(); // membership only; never iterated

        /// <inheritdoc />
        public string Name => "Progression.Modifiers";

        /// <summary>1: a cheap dirty check every tick; the heavy recompute is interval-gated per civ.</summary>
        public int TickInterval => 1;

        /// <summary>
        /// Prepares every alive civilization for a new or loaded world without changing what an uninterrupted run would hold:
        /// <list type="bullet">
        /// <item>Modifiers are recomputed. This is a no-op on a consistent save, and it restores them if a save omitted them.
        /// On a world that has already ticked (<c>State.Tick &gt; 0</c>), a pending <see cref="ProgressionKeys.Dirty"/>
        /// request means another module changed inputs after the last Progression phase. The uninterrupted run keeps the
        /// previous values until the next <see cref="Tick"/>, so they are kept here too.</item>
        /// <item><see cref="ProgressionKeys.Dirty"/> is never consumed here; the next <see cref="Tick"/> consumes it.</item>
        /// <item><see cref="Civilization.Population"/> is refreshed only on a world that has not ticked yet. On a loaded
        /// world it keeps its saved value, which <see cref="Tick"/> refreshes in the next Progression phase.</item>
        /// </list>
        /// </summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            _seen.Clear();
            bool freshWorld = ctx.State.Tick == 0;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                _seen.Add(civ.Id);
                if (freshWorld) civ.Population = TechQueries.PopulationOf(ctx.State, civ);
                if (!freshWorld && IsDirty(civ, out _)) continue; // pending request: Tick applies it, exactly as without a reload
                RecomputeFor(civ, ctx.Db);
            }
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            long tick = ctx.State.Tick;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                civ.Population = TechQueries.PopulationOf(ctx.State, civ);
                bool dirty = IsDirty(civ, out bool hasFlag);
                long phase = (tick + civ.Id) % RecomputeInterval;
                if (dirty || phase == 0 || !_seen.Contains(civ.Id))
                {
                    RecomputeFor(civ, ctx.Db);
                    _seen.Add(civ.Id);
                }
                // Consumes the request, and also clears NaN / non-positive leftovers so no junk stays in persisted Memory.
                if (hasFlag) civ.Memory.Remove(ProgressionKeys.Dirty);
            }
        }

        /// <summary>Same as <see cref="RecomputeFor"/> (kept as an instance method for callers holding the system).</summary>
        public void Recompute(Civilization civ, GameDatabase db) => RecomputeFor(civ, db);

        /// <summary>
        /// Recomputes <paramref name="civ"/>.Modifiers from scratch: Reset, then race aptitudes, then the current age's
        /// bonuses, then known techs (additive bonuses summed per kind, then applied as <c>mult *= 1 + sum</c>).
        /// Also applies every known tech's UnlockBuilding effect. Pure function of race, age and known techs: calling it
        /// again on unchanged inputs gives bit-identical results. Never throws on bad data and never writes NaN.
        /// Does not allocate.
        /// </summary>
        public static void RecomputeFor(Civilization civ, GameDatabase db)
        {
            if (civ == null || db == null) return;
            var m = civ.Modifiers;
            m.Reset();

            // ---- race
            var race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;
            if (race != null)
            {
                var pm = race.ProductionMultiplier;
                if (pm != null)
                    for (int r = 0; r < m.Production.Length && r < pm.Length; r++) m.Production[r] *= Factor(pm[r]);
                m.Research *= Factor(race.ResearchRate);
                m.Faith *= Factor(race.FaithRate);
                m.Military *= Factor(race.MilitaryStrength);
                m.BuildSpeed *= Factor(race.BuildSpeed);
                m.Trade *= Factor(race.TradeAptitude);
                m.MoveSpeed *= Factor(race.MoveSpeed);
            }

            // ---- age
            var age = (int)civ.Age < db.Ages.Count ? db.Ages[(int)civ.Age] : null;
            if (age != null)
            {
                float prod = Bonus(age.ProductionBonus);
                for (int r = 0; r < m.Production.Length; r++) m.Production[r] *= prod;
                m.Research *= Bonus(age.ResearchBonus);
                m.LifespanBonusYears += MathUtil.Sanitize(age.LifespanBonusYears);
                m.HealthBonus += MathUtil.Sanitize(age.HealthBonus);
            }

            // ---- techs (iterate db by id: deterministic and allocation-free)
            var prodAdd = _prodAddScratch ?? (_prodAddScratch = new float[Counts.Resources]);
            var serviceAdd = _serviceAddScratch ?? (_serviceAddScratch = new float[Counts.Services]);
            for (int r = 0; r < prodAdd.Length; r++) prodAdd[r] = 0f;
            for (int s = 0; s < serviceAdd.Length; s++) serviceAdd[s] = 0f;
            float allProd = 0f, research = 0f, growth = 0f, military = 0f, build = 0f, faith = 0f, storage = 0f, trade = 0f, move = 0f;
            float lifespan = 0f, health = 0f, happiness = 0f;
            if (civ.KnownTechs.Count > 0)
            {
                var techs = db.Techs;
                for (int i = 0; i < techs.Count; i++)
                {
                    var t = techs[i];
                    if (t == null || t.Effects == null || !civ.KnownTechs.Contains(t.Id)) continue;
                    for (int e = 0; e < t.Effects.Length; e++)
                    {
                        var fx = t.Effects[e];
                        float v = MathUtil.Sanitize(fx.Value);
                        switch (fx.Kind)
                        {
                            case TechEffectKind.UnlockBuilding: Unlock(civ, fx.Building); break;
                            case TechEffectKind.ProductionMultiplier: if ((int)fx.Resource < prodAdd.Length) prodAdd[(int)fx.Resource] += v; break;
                            case TechEffectKind.AllProductionMultiplier: allProd += v; break;
                            case TechEffectKind.ResearchMultiplier: research += v; break;
                            case TechEffectKind.GrowthMultiplier: growth += v; break;
                            case TechEffectKind.LifespanBonus: lifespan += v; break;
                            case TechEffectKind.HealthBonus: health += v; break;
                            case TechEffectKind.HappinessBonus: happiness += v; break;
                            case TechEffectKind.MilitaryMultiplier: military += v; break;
                            case TechEffectKind.BuildSpeedMultiplier: build += v; break;
                            case TechEffectKind.FaithMultiplier: faith += v; break;
                            case TechEffectKind.StorageMultiplier: storage += v; break;
                            case TechEffectKind.TradeMultiplier: trade += v; break;
                            case TechEffectKind.ServiceMultiplier: if ((int)fx.Service < serviceAdd.Length) serviceAdd[(int)fx.Service] += v; break;
                            case TechEffectKind.MoveSpeedMultiplier: move += v; break;
                        }
                    }
                }
            }
            float all = Bonus(allProd);
            for (int r = 0; r < m.Production.Length; r++) m.Production[r] = Clean(m.Production[r] * Bonus(prodAdd[r]) * all);
            for (int s = 0; s < m.Service.Length; s++) m.Service[s] = Clean(m.Service[s] * Bonus(serviceAdd[s]));
            m.Research = Clean(m.Research * Bonus(research));
            m.Growth = Clean(m.Growth * Bonus(growth));
            m.Military = Clean(m.Military * Bonus(military));
            m.BuildSpeed = Clean(m.BuildSpeed * Bonus(build));
            m.Faith = Clean(m.Faith * Bonus(faith));
            m.Storage = Clean(m.Storage * Bonus(storage));
            m.Trade = Clean(m.Trade * Bonus(trade));
            m.MoveSpeed = Clean(m.MoveSpeed * Bonus(move));
            m.LifespanBonusYears = MathUtil.Clamp(MathUtil.Sanitize(m.LifespanBonusYears + lifespan), -1000f, 10000f);
            m.HealthBonus = MathUtil.Clamp(MathUtil.Sanitize(m.HealthBonus + health), -1f, 1f);
            m.HappinessBonus = MathUtil.Clamp(MathUtil.Sanitize(m.HappinessBonus + happiness), -1f, 1f);
        }

        /// <summary>Sets the unlock flag for a building type (bounds-checked). Flags are never cleared here.</summary>
        internal static void Unlock(Civilization civ, BuildingType type)
        {
            int b = (int)type;
            if (b >= 0 && b < civ.UnlockedBuildings.Length) civ.UnlockedBuildings[b] = true;
        }

        /// <summary>
        /// True if a recompute request is pending: <see cref="ProgressionKeys.Dirty"/> &gt; 0, or NaN (a corrupt value is
        /// treated as a request). <paramref name="hasFlag"/> reports whether the key exists at all.
        /// </summary>
        private static bool IsDirty(Civilization civ, out bool hasFlag)
        {
            hasFlag = civ.Memory.TryGetValue(ProgressionKeys.Dirty, out float d);
            return hasFlag && (d > 0f || float.IsNaN(d));
        }

        private static float Factor(float v) => MathUtil.Clamp(MathUtil.Sanitize(v, 1f), 0f, MaxMultiplier);
        private static float Bonus(float sum) => MathUtil.Clamp(1f + MathUtil.Sanitize(sum), 0f, MaxMultiplier);
        private static float Clean(float v) => MathUtil.Clamp(MathUtil.Sanitize(v, 1f), 0f, MaxMultiplier);
    }
}
