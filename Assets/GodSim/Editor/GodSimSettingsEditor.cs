using System;
using System.Collections.Generic;
using GodSim.Core;
using UnityEditor;
using UnityEngine;

namespace GodSim.Unity.Editor
{
    /// <summary>
    /// Sectioned inspector for <see cref="GodSimSettings"/>. Fields are grouped by name (so it keeps working when the
    /// runtime team adds fields) and the resulting <see cref="SimConfig"/> is checked for clamped or extreme values.
    /// </summary>
    [CustomEditor(typeof(GodSimSettings))]
    public sealed class GodSimSettingsEditor : UnityEditor.Editor
    {
        private static readonly string[] Sections =
        {
            "World", "Civilizations", "Balance", "Divine & Disasters", "Limits & Rules", "Runtime", "Debug & Safety", "Other"
        };
        private const int World = 0, Civs = 1, Balance = 2, Divine = 3, Limits = 4, Runtime = 5, Debugging = 6, Other = 7;

        private static readonly bool[] Expanded = { true, true, true, true, false, true, false, true };

        private readonly List<SerializedProperty>[] _groups = new List<SerializedProperty>[Sections.Length];
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _clamped = new List<string>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            for (int i = 0; i < _groups.Length; i++)
            {
                if (_groups[i] == null) _groups[i] = new List<SerializedProperty>();
                _groups[i].Clear();
            }

            SerializedProperty script = null;
            SerializedProperty it = serializedObject.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script") { script = it.Copy(); continue; }
                _groups[Classify(it.name)].Add(it.Copy());
            }

            if (script != null)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(script, true);
            }
            EditorGUILayout.HelpBox("World creation and balance settings. Values are clamped by SimConfig.Sanitize() before the simulation uses them.",
                MessageType.None);

            for (int s = 0; s < Sections.Length; s++)
            {
                if (_groups[s].Count == 0) continue;
                Expanded[s] = EditorGUILayout.Foldout(Expanded[s], Sections[s], true);
                if (!Expanded[s]) continue;
                EditorGUI.indentLevel++;
                for (int p = 0; p < _groups[s].Count; p++) EditorGUILayout.PropertyField(_groups[s][p], true);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }
            serializedObject.ApplyModifiedProperties();

            DrawValidation(target as GodSimSettings);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("World Preview")) WorldPreviewWindow.Open();
            if (GUILayout.Button("Simulate 100 Years")) EditorApplication.delayCall += GodSimMenu.SimulateHeadless;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Maps a serialized field name to a section (case/prefix-insensitive substring rules).</summary>
        private static int Classify(string fieldName)
        {
            string n = (fieldName ?? "").ToLowerInvariant();
            if (n.StartsWith("m_", StringComparison.Ordinal)) n = n.Substring(2);
            n = n.TrimStart('_');

            if (Has(n, "strict", "fault", "debug", "log", "verbose", "profil")) return Debugging;
            if (Has(n, "mana", "disaster", "god", "power", "weather", "favor")) return Divine;
            if (Has(n, "tickspersecond", "speed", "autostart", "autosave", "pause", "startpaused")) return Runtime;
            if (Has(n, "multiplier", "growth", "research", "production", "balance")) return Balance;
            if (Has(n, "max", "capacity", "limit", "command", "warfare", "enable")) return Limits;
            if (Has(n, "seed", "map", "width", "height", "preset", "size", "world")) return World;
            if (Has(n, "race", "initial", "population", "civ", "settler")) return Civs;
            return Other;
        }

        private static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++) if (s.Contains(keys[i])) return true;
            return false;
        }

        // ------------------------------------------------------------------ validation
        private void DrawValidation(GodSimSettings settings)
        {
            if (settings == null) return;
            SimConfig raw;
            try { raw = settings.ToConfig(); }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox("ToConfig() threw: " + ex.Message, MessageType.Error);
                return;
            }
            if (raw == null)
            {
                EditorGUILayout.HelpBox("ToConfig() returned null; the runner will fall back to defaults.", MessageType.Error);
                return;
            }

            SimConfig cfg = GodSimEditorUtil.CloneConfig(raw).Sanitize();
            CollectClamped(raw, cfg);
            CollectWarnings(cfg);

            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            int races = cfg.StartingRaces != null ? cfg.StartingRaces.Length : 0;
            EditorGUILayout.LabelField("Summary", cfg.MapWidth + "x" + cfg.MapHeight + " " + cfg.Preset + ", "
                + races + " races x " + cfg.InitialPopulationPerRace + " settlers, seed " + cfg.Seed, EditorStyles.miniLabel);
            for (int i = 0; i < _clamped.Count; i++) EditorGUILayout.HelpBox(_clamped[i], MessageType.Warning);
            for (int i = 0; i < _warnings.Count; i++) EditorGUILayout.HelpBox(_warnings[i], MessageType.Warning);
            if (_clamped.Count == 0 && _warnings.Count == 0) EditorGUILayout.HelpBox("All values are within recommended ranges.", MessageType.Info);
        }

        private void CollectClamped(SimConfig raw, SimConfig cfg)
        {
            _clamped.Clear();
            Clamp("Map Width", raw.MapWidth, cfg.MapWidth);
            Clamp("Map Height", raw.MapHeight, cfg.MapHeight);
            Clamp("Initial Population Per Race", raw.InitialPopulationPerRace, cfg.InitialPopulationPerRace);
            Clamp("Max Population", raw.MaxPopulation, cfg.MaxPopulation);
            Clamp("Max Settlements Per Civ", raw.MaxSettlementsPerCiv, cfg.MaxSettlementsPerCiv);
            Clamp("Max Buildings Per Settlement", raw.MaxBuildingsPerSettlement, cfg.MaxBuildingsPerSettlement);
            Clamp("Max Armies", raw.MaxArmies, cfg.MaxArmies);
            Clamp("Max Commands Per Tick", raw.MaxCommandsPerTick, cfg.MaxCommandsPerTick);
            Clamp("Command Queue Capacity", raw.CommandQueueCapacity, cfg.CommandQueueCapacity);
            Clamp("Growth Multiplier", raw.GrowthMultiplier, cfg.GrowthMultiplier);
            Clamp("Research Multiplier", raw.ResearchMultiplier, cfg.ResearchMultiplier);
            Clamp("Production Multiplier", raw.ProductionMultiplier, cfg.ProductionMultiplier);
            Clamp("Disaster Frequency", raw.DisasterFrequency, cfg.DisasterFrequency);
            Clamp("Starting Mana", raw.StartingMana, cfg.StartingMana);
            Clamp("Mana Regen Multiplier", raw.ManaRegenMultiplier, cfg.ManaRegenMultiplier);
            Clamp("Max System Faults", raw.MaxSystemFaults, cfg.MaxSystemFaults);
            int rawRaces = raw.StartingRaces != null ? raw.StartingRaces.Length : 0;
            if (rawRaces == 0) _clamped.Add("No starting races selected: all five races will be used.");
        }

        private void Clamp(string label, double raw, double sanitized)
        {
            if (raw == sanitized || (double.IsNaN(raw) && double.IsNaN(sanitized))) return;
            _clamped.Add(label + " = " + raw.ToString("0.###") + " is out of range and will be clamped to " + sanitized.ToString("0.###") + ".");
        }

        private void CollectWarnings(SimConfig c)
        {
            _warnings.Clear();
            long tiles = (long)c.MapWidth * c.MapHeight;
            if (tiles > 512L * 512L) _warnings.Add("Huge map (" + tiles + " tiles): world generation, saves and terrain meshes will be slow and memory hungry.");
            else if (tiles > 320L * 320L) _warnings.Add("Large map (" + tiles + " tiles): expect slower generation and rendering.");
            int races = c.StartingRaces != null ? c.StartingRaces.Length : 0;
            if (races > Counts.Races) _warnings.Add(races + " starting civilizations on one map: duplicates of the same race compete for the same habitat.");
            if ((long)races * c.InitialPopulationPerRace > 400) _warnings.Add("Large starting population (" + races * c.InitialPopulationPerRace + " people).");
            if (c.MaxPopulation > 60000) _warnings.Add("Max Population " + c.MaxPopulation + " is far above the 5,000-person performance budget (3 ms/tick).");
            if (c.MaxPopulation < 500) _warnings.Add("Max Population " + c.MaxPopulation + " is very low: civilizations will stall early.");
            if (c.GrowthMultiplier > 3f) _warnings.Add("Growth Multiplier " + c.GrowthMultiplier + ": population will explode and hit the global cap quickly.");
            if (c.GrowthMultiplier <= 0f) _warnings.Add("Growth Multiplier is 0: nobody will be born.");
            if (c.ResearchMultiplier > 8f) _warnings.Add("Research Multiplier " + c.ResearchMultiplier + ": ages will pass in a few years.");
            if (c.ResearchMultiplier <= 0f) _warnings.Add("Research Multiplier is 0: civilizations stay in the Dawn age forever.");
            if (c.ProductionMultiplier > 4f) _warnings.Add("Production Multiplier " + c.ProductionMultiplier + ": the economy will have no scarcity.");
            if (c.ProductionMultiplier <= 0f) _warnings.Add("Production Multiplier is 0: nothing will be produced and settlements will starve.");
            if (c.EnableDisasters && c.DisasterFrequency > 3f) _warnings.Add("Disaster Frequency " + c.DisasterFrequency + ": disasters may wipe out young civilizations.");
            if (c.StartingMana > 5000f) _warnings.Add("Starting Mana " + c.StartingMana + " effectively removes the mana economy.");
            if (c.ManaRegenMultiplier > 10f) _warnings.Add("Mana Regen Multiplier " + c.ManaRegenMultiplier + " makes god powers nearly free.");
            if (c.MaxCommandsPerTick > 16) _warnings.Add("Max Commands Per Tick " + c.MaxCommandsPerTick + " weakens the god-command rate limit.");
            if (c.StrictMode) _warnings.Add("Strict Mode rethrows system exceptions: one bug stops the world. Use it only for tests/CI.");
            if (c.MaxSystemFaults < 3) _warnings.Add("Max System Faults " + c.MaxSystemFaults + ": systems will be disabled after a single transient error.");
            if (!c.EnableWarfare) _warnings.Add("Warfare is disabled: armies, sieges and conquest will not happen.");
        }
    }
}
