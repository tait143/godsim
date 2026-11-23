using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Pure, deterministic military formulas shared by <see cref="MilitarySystem"/> and
    /// <see cref="ArmySystem"/>: soldier and settlement strength, combat rolls, casualties, war score,
    /// target scoring and the blackboard keys the military module writes. Every result is sanitized so
    /// no NaN/Infinity can reach <see cref="WorldState"/>.
    /// </summary>
    public static class MilitaryMath
    {
        // ------------------------------------------------------------------ blackboard keys
        /// <summary>Settlement.Memory: id of the field army raised from this settlement (absent = none).</summary>
        public const string KeyArmyId = "mil.armyId";
        /// <summary>Settlement.Memory: soldier count the army in <see cref="KeyArmyId"/> was raised with.</summary>
        public const string KeyArmyInitialSoldiers = "mil.armyInit";
        /// <summary>Settlement.Memory: siege damage 0..<see cref="MaxSiegeDamage"/> that reduces service defense.</summary>
        public const string KeySiegeDamage = "mil.siegeDamage";
        /// <summary>Settlement.Memory: tick until which no new army is raised here (regrouping after a campaign).</summary>
        public const string KeyRestUntil = "mil.restUntil";
        /// <summary>Settlement.Memory: target id whose path search from here ran out of budget (long route; retried later).</summary>
        public const string KeyNoPathTarget = "mil.noPathTarget";
        /// <summary>Settlement.Memory: tick until which <see cref="KeyNoPathTarget"/> is skipped.</summary>
        public const string KeyNoPathUntil = "mil.noPathUntil";
        /// <summary>Civilization.Memory: tick of the last chronicled (non-decisive) battle, to keep the chronicle readable.</summary>
        public const string KeyLastBattleChronicle = "mil.lastBattleChronicle";

        // ------------------------------------------------------------------ tuning
        /// <summary>Weapons per soldier in the settlement stockpile needed for the armed bonus.</summary>
        public const float WeaponsPerSoldier = 0.5f;
        /// <summary>Strength multiplier for soldiers with enough weapons.</summary>
        public const float WeaponsBonus = 1.3f;
        /// <summary>Fraction of the free garrison that marches out with a new army.</summary>
        public const float RaiseFraction = 0.7f;
        /// <summary>Free soldiers a settlement needs before it can raise an army.</summary>
        public const int MinSoldiersToRaise = 4;
        /// <summary>Capture when defender power falls below this fraction of the attacker's strength...</summary>
        public const float CaptureDefenseRatio = 0.15f;
        /// <summary>...and at most this many soldiers defend.</summary>
        public const int CaptureMaxGarrison = 1;
        /// <summary>War-score bonus for taking (or razing) a settlement.</summary>
        public const float CaptureWarScore = 25f;
        /// <summary>Upper bound of siege damage (walls never vanish entirely).</summary>
        public const float MaxSiegeDamage = 0.9f;
        /// <summary>Race aggression above which camps are razed rather than captured.</summary>
        public const float RazeAggression = 0.7f;

        private const float MaxStrengthPerSoldier = 10000f;
        private const float MaxPower = 1e9f;

        // ------------------------------------------------------------------ strength
        /// <summary>Age base strength x civ military modifier (before the weapons bonus). Never negative or NaN.</summary>
        public static float BaseSoldierStrength(GameDatabase db, Civilization civ)
        {
            if (civ == null) return 1f;
            float baseStrength = 1f;
            if (db != null)
            {
                int age = (int)civ.Age;
                var ages = db.Ages;
                if (age >= 0 && age < ages.Count && ages[age] != null) baseStrength = ages[age].BaseSoldierStrength;
            }
            baseStrength = MathUtil.Clamp(MathUtil.Sanitize(baseStrength, 1f), 0f, MaxStrengthPerSoldier);
            float mod = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Military, 1f), 0f, 100f);
            return MathUtil.Clamp(baseStrength * mod, 0f, MaxStrengthPerSoldier);
        }

        /// <summary>True when the stockpile holds at least <see cref="WeaponsPerSoldier"/> weapons per soldier.</summary>
        public static bool HasWeapons(Stockpile stockpile, int soldiers)
        {
            if (stockpile == null || soldiers <= 0) return false;
            return stockpile.Get(ResourceType.Weapons) + 1e-4f >= WeaponsPerSoldier * soldiers;
        }

        /// <summary>
        /// soldierStrength(civ) = Age.BaseSoldierStrength * civ.Modifiers.Military * (armed ? 1.3 : 1),
        /// where armed means the supplying stockpile has 0.5 weapons per soldier.
        /// </summary>
        public static float SoldierStrength(GameDatabase db, Civilization civ, Stockpile stockpile, int soldiers)
        {
            float s = BaseSoldierStrength(db, civ);
            if (HasWeapons(stockpile, soldiers)) s *= WeaponsBonus;
            return MathUtil.Clamp(MathUtil.Sanitize(s), 0f, MaxStrengthPerSoldier);
        }

        /// <summary>DefenseStrength = ServiceOutput[Defense] * (1 - siegeDamage) + garrison * soldierStrength.</summary>
        public static float DefenseStrength(float serviceDefense, int garrison, float soldierStrength, float siegeDamage)
        {
            float service = Math.Max(0f, MathUtil.Sanitize(serviceDefense));
            float dmg = MathUtil.Clamp(MathUtil.Sanitize(siegeDamage), 0f, MaxSiegeDamage);
            float v = service * (1f - dmg) + Math.Max(0, garrison) * Math.Max(0f, MathUtil.Sanitize(soldierStrength));
            return MathUtil.Clamp(MathUtil.Sanitize(v), 0f, MaxPower);
        }

        /// <summary>Soldiers of the settlement currently away in living field armies raised there.</summary>
        public static int AwaySoldiers(WorldState state, int settlementId)
        {
            if (state == null || settlementId < 0) return 0;
            int n = 0;
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a.IsAlive && a.HomeSettlementId == settlementId) n += a.SoldierIds.Count;
            }
            return n;
        }

        /// <summary>Soldiers defending at home: s.Soldiers minus those away in field armies (never negative).</summary>
        public static int GarrisonCount(Settlement s, int awaySoldiers) => s == null ? 0 : Math.Max(0, s.Soldiers - Math.Max(0, awaySoldiers));

        /// <summary>Recomputes and stores <see cref="Settlement.DefenseStrength"/>; returns it.</summary>
        public static float UpdateDefense(GameDatabase db, WorldState state, Settlement s, int awaySoldiers)
        {
            if (s == null) return 0f;
            var civ = state?.Civilizations.Get(s.CivilizationId);
            int garrison = GarrisonCount(s, awaySoldiers);
            float per = SoldierStrength(db, civ, s.Stockpile, Math.Max(s.Soldiers, garrison));
            float service = s.ServiceOutput.Length > (int)ServiceType.Defense ? s.ServiceOutput[(int)ServiceType.Defense] : 0f;
            s.DefenseStrength = DefenseStrength(service, garrison, per, s.Mem(KeySiegeDamage));
            return s.DefenseStrength;
        }

        // ------------------------------------------------------------------ combat
        /// <summary>Gaussian(1, 0.2) combat luck, clamped to [0.2, 2].</summary>
        public static float CombatRoll(Rng rng) => rng == null ? 1f : MathUtil.Clamp(MathUtil.Sanitize(rng.Gaussian(1f, 0.2f), 1f), 0.2f, 2f);

        /// <summary>Rounds v down or up with probability equal to its fraction (unbiased, deterministic via rng).</summary>
        public static int StochasticRound(Rng rng, float v)
        {
            v = MathUtil.Sanitize(v);
            if (v <= 0f) return 0;
            if (v > 1e6f) v = 1e6f;
            int f = (int)Math.Floor(v);
            float frac = v - f;
            if (frac > 0f && rng != null && rng.NextFloat() < frac) f++;
            return f;
        }

        /// <summary>
        /// Casualties among <paramref name="committed"/> soldiers: 10-25% for the loser (at least 1),
        /// 3-10% for the winner. Never more than committed.
        /// </summary>
        public static int Casualties(Rng rng, int committed, bool lost)
        {
            if (committed <= 0 || rng == null) return 0;
            float pct = lost ? rng.Range(0.10f, 0.25f) : rng.Range(0.03f, 0.10f);
            int n = StochasticRound(rng, committed * pct);
            if (lost && n < 1) n = 1;
            return MathUtil.Clamp(n, 0, committed);
        }

        /// <summary>Collateral civilian deaths when a settlement loses a siege round: 1-2% (stochastically rounded).</summary>
        public static int CivilianCasualties(Rng rng, int civilians)
        {
            if (civilians <= 0 || rng == null) return 0;
            return MathUtil.Clamp(StochasticRound(rng, civilians * rng.Range(0.01f, 0.02f)), 0, civilians);
        }

        /// <summary>War-score swing of a battle, 5..15.</summary>
        public static float BattleWarScore(Rng rng) => rng == null ? 10f : rng.Range(5f, 15f);

        /// <summary>Adds delta to the war score and clamps to [-100, 100].</summary>
        public static float ApplyWarScore(float current, float delta) => MathUtil.Clamp(MathUtil.Sanitize(current + delta, MathUtil.Sanitize(current)), -100f, 100f);

        /// <summary>+1 when the civ is the war's attacker (war score is attacker-positive), else -1.</summary>
        public static float FavourSign(War war, int civId) => war != null && war.AttackerId == civId ? 1f : -1f;

        /// <summary>Morale after a battle: winner +0.05, loser -0.15, clamped to [0, 1].</summary>
        public static float MoraleAfter(float morale, bool won) => MathUtil.Clamp01(MathUtil.Sanitize(morale, 0.5f) + (won ? 0.05f : -0.15f));

        /// <summary>True when the settlement falls: defender power below 15% of the attacker and at most one soldier.</summary>
        public static bool ShouldCapture(float defenderPower, int garrison, float attackerStrength)
        {
            attackerStrength = MathUtil.Sanitize(attackerStrength);
            if (attackerStrength <= 0f) return false;
            return MathUtil.Sanitize(defenderPower) < CaptureDefenseRatio * attackerStrength && garrison <= CaptureMaxGarrison;
        }

        /// <summary>Soldiers marching out of a garrison of the given size (0 when below the minimum).</summary>
        public static int RaiseCount(int freeSoldiers)
        {
            if (freeSoldiers < MinSoldiersToRaise) return 0;
            return Math.Max(1, (int)Math.Floor(freeSoldiers * RaiseFraction));
        }

        /// <summary>
        /// Target desirability (higher is better): favours weak and near settlements.
        /// odds = projected / (projected + defense); proximity = 1 / (1 + distance / 24).
        /// </summary>
        public static float TargetScore(float projectedStrength, float targetDefense, float distance)
        {
            float p = Math.Max(0.01f, MathUtil.Sanitize(projectedStrength));
            float d = Math.Max(0f, MathUtil.Sanitize(targetDefense));
            float odds = p / (p + d);
            float proximity = 1f / (1f + Math.Max(0f, MathUtil.Sanitize(distance)) / 24f);
            return MathUtil.Sanitize(odds * proximity);
        }

        /// <summary>Minimum projected/defense ratio an attacker accepts: 0.6 - 0.5 * aggression (clamped 0.05..0.6).</summary>
        public static float MinAttackOdds(float aggression) => MathUtil.Clamp(0.6f - 0.5f * MathUtil.Clamp01(MathUtil.Sanitize(aggression, 0.5f)), 0.05f, 0.6f);

        // ------------------------------------------------------------------ terrain
        /// <summary>Movement cost of a tile for land armies: Biome.MoveCost(), x0.5 on roads; +Infinity on water.</summary>
        public static float TileMoveCost(WorldMap map, int index)
        {
            if (map == null || index < 0 || index >= map.TileCount) return float.PositiveInfinity;
            float c = map.Biomes[index].MoveCost();
            if (float.IsInfinity(c) || float.IsNaN(c)) return float.PositiveInfinity;
            if ((map.Flags[index] & TileFlags.Road) != 0) c *= 0.5f;
            return c;
        }

        /// <summary>The tile itself when passable, else the nearest passable tile within 2 (Vec2i.Invalid if none).</summary>
        public static Vec2i PassableNear(WorldMap map, Vec2i p)
        {
            if (map == null) return Vec2i.Invalid;
            p = map.ClampToBounds(p);
            if (!float.IsInfinity(TileMoveCost(map, map.Index(p)))) return p;
            for (int r = 1; r <= 2; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = p.X + dx, y = p.Y + dy;
                        if (!map.InBounds(x, y)) continue;
                        if (!float.IsInfinity(TileMoveCost(map, map.Index(x, y)))) return new Vec2i(x, y);
                    }
            return Vec2i.Invalid;
        }

        /// <summary>Living soldiers of the army that still belong to its civilization.</summary>
        public static int LivingSoldiers(WorldState state, Army a)
        {
            if (state == null || a == null) return 0;
            int n = 0;
            var ids = a.SoldierIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p != null && p.IsAlive && p.CivilizationId == a.CivilizationId) n++;
            }
            return n;
        }

        /// <summary>Fills <paramref name="garrison"/> with living residents that are free soldiers (Job Soldier, no army) and optionally <paramref name="civilians"/> with the rest.</summary>
        public static void SplitResidents(WorldState state, Settlement s, List<Person> garrison, List<Person> civilians)
        {
            garrison?.Clear();
            civilians?.Clear();
            if (state == null || s == null) return;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                if (p.ArmyId >= 0) continue;
                if (p.Job == JobType.Soldier) garrison?.Add(p);
                else civilians?.Add(p);
            }
        }
    }
}
