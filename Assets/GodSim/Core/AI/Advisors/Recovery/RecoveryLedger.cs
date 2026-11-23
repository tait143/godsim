using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The compact "loss ledger" a settlement keeps after catastrophes: per-type losses not yet rebuilt, scars per
    /// catastrophe kind, and the civilization's slower cultural memory. Pure blackboard helpers, allocation-free.
    /// </summary>
    public static class RecoveryLedger
    {
        public const float MaxLost = 40f;
        public const float MaxScar = 4f;
        public const float MaxCivScar = 6f;
        public const float PruneBelow = 0.05f;

        // ------------------------------------------------------------------ reading
        /// <summary>True while the settlement has an active recovery record.</summary>
        public static bool IsRecovering(Settlement s) => s != null && s.Memory.ContainsKey(RecoveryKeys.LastTick);

        /// <summary>Buildings of a type lost and not yet rebuilt (decaying).</summary>
        public static float Lost(Settlement s, BuildingType t) => s == null ? 0f : s.Mem(RecoveryKeys.Lost[(int)t]);

        public static float Scar(Settlement s, RecoveryScar k) => s == null ? 0f : s.Mem(RecoveryKeys.Scar[(int)k]);

        public static float CivScar(Civilization c, RecoveryScar k) => c == null ? 0f : c.Mem(RecoveryKeys.CivScar[(int)k]);

        public static bool HasCivMemory(Civilization c) => c != null && c.Memory.ContainsKey(RecoveryKeys.CivAny);

        public static RecoveryCatastrophe Kind(Settlement s)
        {
            if (s == null) return RecoveryCatastrophe.None;
            int k = (int)MathUtil.Sanitize(s.Mem(RecoveryKeys.Kind));
            return k < 0 || k > (int)RecoveryCatastrophe.Disaster ? RecoveryCatastrophe.None : (RecoveryCatastrophe)k;
        }

        // ------------------------------------------------------------------ writing
        public static void AddLoss(Settlement s, BuildingType t, float amount)
        {
            if (s == null) return;
            string key = RecoveryKeys.Lost[(int)t];
            s.Memory[key] = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(key) + amount), 0f, MaxLost);
        }

        /// <summary>A building of this type was completed: one loss is considered rebuilt.</summary>
        public static void CreditRebuild(Settlement s, BuildingType t)
        {
            if (s == null) return;
            string key = RecoveryKeys.Lost[(int)t];
            if (!s.Memory.TryGetValue(key, out float v)) return;
            v -= 1f;
            if (v < PruneBelow || float.IsNaN(v)) s.Memory.Remove(key); else s.Memory[key] = v;
        }

        /// <summary>Adds a local scar and (at half strength) the civilization's cultural memory of it.</summary>
        public static void AddScar(Settlement s, Civilization civ, RecoveryScar k, float amount)
        {
            amount = MathUtil.Clamp(MathUtil.Sanitize(amount), 0f, MaxScar);
            if (amount <= 0f) return;
            if (s != null)
            {
                string key = RecoveryKeys.Scar[(int)k];
                s.Memory[key] = MathUtil.Clamp(s.Mem(key) + amount, 0f, MaxScar);
            }
            if (civ != null)
            {
                string ck = RecoveryKeys.CivScar[(int)k];
                civ.Memory[ck] = MathUtil.Clamp(civ.Mem(ck) + amount * 0.5f, 0f, MaxCivScar);
                civ.Memory[RecoveryKeys.CivAny] = 1f;
            }
        }

        /// <summary>Names the catastrophe. With keepRecentSpecific, a kind set in the last few ticks (e.g. by a DisasterEvent) is kept.</summary>
        public static void MarkKind(Settlement s, RecoveryCatastrophe kind, long tick, bool keepRecentSpecific)
        {
            if (s == null) return;
            if (keepRecentSpecific)
            {
                var cur = Kind(s);
                if (cur != RecoveryCatastrophe.None && cur != RecoveryCatastrophe.Disaster && tick - (long)s.Mem(RecoveryKeys.KindTick, -1000f) <= 3) return;
            }
            s.Memory[RecoveryKeys.Kind] = (float)kind;
            s.Memory[RecoveryKeys.KindTick] = tick;
        }

        public static void Touch(Settlement s, long tick) { if (s != null) s.Memory[RecoveryKeys.LastTick] = tick; }

        // ------------------------------------------------------------------ decay & cleanup
        /// <summary>Decays losses and scars in place, pruning tiny entries. Reports what is left.</summary>
        public static void Decay(Settlement s, float lostFactor, float scarFactor, out bool hasLosses, out bool hasScars)
        {
            hasLosses = false;
            hasScars = false;
            if (s == null) return;
            var mem = s.Memory;
            var lost = RecoveryKeys.Lost;
            for (int i = 0; i < lost.Length; i++)
            {
                if (!mem.TryGetValue(lost[i], out float v)) continue;
                v = MathUtil.Sanitize(v * lostFactor);
                if (v < PruneBelow) mem.Remove(lost[i]); else { mem[lost[i]] = v; hasLosses = true; }
            }
            var scars = RecoveryKeys.Scar;
            for (int i = 0; i < scars.Length; i++)
            {
                if (!mem.TryGetValue(scars[i], out float v)) continue;
                v = MathUtil.Sanitize(v * scarFactor);
                if (v < PruneBelow) mem.Remove(scars[i]); else { mem[scars[i]] = v; hasScars = true; }
            }
            if (mem.TryGetValue(RecoveryKeys.ForestEdge, out float f))
            {
                f = MathUtil.Sanitize(f * scarFactor);
                if (f < PruneBelow) mem.Remove(RecoveryKeys.ForestEdge); else mem[RecoveryKeys.ForestEdge] = f;
            }
        }

        /// <summary>Decays a civilization's cultural memory; clears the summary flag once everything faded.</summary>
        public static void DecayCiv(Civilization c, float factor)
        {
            if (c == null || !c.Memory.ContainsKey(RecoveryKeys.CivAny)) return;
            var mem = c.Memory;
            bool any = false;
            var keys = RecoveryKeys.CivScar;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!mem.TryGetValue(keys[i], out float v)) continue;
                v = MathUtil.Sanitize(v * factor);
                if (v < PruneBelow) mem.Remove(keys[i]); else { mem[keys[i]] = v; any = true; }
            }
            if (!any) mem.Remove(RecoveryKeys.CivAny);
        }

        public static void ClearLosses(Settlement s)
        {
            if (s == null) return;
            var lost = RecoveryKeys.Lost;
            for (int i = 0; i < lost.Length; i++) s.Memory.Remove(lost[i]);
        }

        /// <summary>Removes every recovery key of the settlement (plan finished).</summary>
        public static void ClearAll(Settlement s)
        {
            if (s == null) return;
            ClearLosses(s);
            var mem = s.Memory;
            for (int i = 0; i < RecoveryKeys.Scar.Length; i++) mem.Remove(RecoveryKeys.Scar[i]);
            mem.Remove(RecoveryKeys.LastTick);
            mem.Remove(RecoveryKeys.Kind);
            mem.Remove(RecoveryKeys.KindTick);
            mem.Remove(RecoveryKeys.LogTick);
            mem.Remove(RecoveryKeys.ForestEdge);
            mem.Remove(RecoveryKeys.FamineTick);
            mem.Remove(RecoveryKeys.HintAvoidForest);
            mem.Remove(RecoveryKeys.HintHighGround);
        }

        // ------------------------------------------------------------------ vocabulary
        public static RecoveryCatastrophe FromDisaster(DisasterType t)
        {
            switch (t)
            {
                case DisasterType.Wildfire: case DisasterType.Lightning: return RecoveryCatastrophe.Fire;
                case DisasterType.Earthquake: return RecoveryCatastrophe.Earthquake;
                case DisasterType.Flood: return RecoveryCatastrophe.Flood;
                case DisasterType.Drought: return RecoveryCatastrophe.Drought;
                case DisasterType.Plague: return RecoveryCatastrophe.Plague;
                case DisasterType.Meteor: return RecoveryCatastrophe.Meteor;
                case DisasterType.Volcano: return RecoveryCatastrophe.Volcano;
                case DisasterType.Storm: return RecoveryCatastrophe.Storm;
                case DisasterType.Famine: return RecoveryCatastrophe.Famine;
                default: return RecoveryCatastrophe.Disaster;
            }
        }

        /// <summary>The lesson a disaster teaches (storms teach flood lessons, meteors and volcanoes fire and quake lessons).</summary>
        public static RecoveryScar ScarOf(DisasterType t)
        {
            switch (t)
            {
                case DisasterType.Wildfire: case DisasterType.Lightning: case DisasterType.Volcano: return RecoveryScar.Fire;
                case DisasterType.Flood: case DisasterType.Storm: return RecoveryScar.Flood;
                case DisasterType.Drought: return RecoveryScar.Drought;
                case DisasterType.Plague: return RecoveryScar.Plague;
                case DisasterType.Famine: return RecoveryScar.Famine;
                default: return RecoveryScar.Quake; // Earthquake, Meteor
            }
        }

        public static string Name(RecoveryCatastrophe k)
        {
            switch (k)
            {
                case RecoveryCatastrophe.Fire: return "the Great Fire";
                case RecoveryCatastrophe.Flood: return "the Great Flood";
                case RecoveryCatastrophe.War: return "the war";
                case RecoveryCatastrophe.DivineWrath: return "the Wrath of the Gods";
                case RecoveryCatastrophe.Earthquake: return "the Earthquake";
                case RecoveryCatastrophe.Famine: return "the Famine";
                case RecoveryCatastrophe.Plague: return "the Plague";
                case RecoveryCatastrophe.Storm: return "the Great Storm";
                case RecoveryCatastrophe.Meteor: return "the Falling Star";
                case RecoveryCatastrophe.Volcano: return "the Eruption";
                case RecoveryCatastrophe.Drought: return "the Drought";
                case RecoveryCatastrophe.Disaster: return "the Catastrophe";
                default: return "the disaster";
            }
        }

        /// <summary>Lower-case display name with a naive English plural ("4 houses").</summary>
        public static string CountedName(BuildingDefinition def, BuildingType t, int count)
        {
            string n = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : t.ToString();
            n = n.ToLowerInvariant();
            if (count == 1) return "1 " + n;
            string p;
            if (n.EndsWith("s", StringComparison.Ordinal) || n.EndsWith("x", StringComparison.Ordinal) || n.EndsWith("ch", StringComparison.Ordinal) || n.EndsWith("sh", StringComparison.Ordinal)) p = n + "es";
            else if (n.Length > 1 && n[n.Length - 1] == 'y' && "aeiou".IndexOf(n[n.Length - 2]) < 0) p = n.Substring(0, n.Length - 1) + "ies";
            else p = n + "s";
            return count + " " + p;
        }
    }
}
