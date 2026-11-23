using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEditor;
using UnityEngine;

namespace GodSim.Unity.Editor
{
    /// <summary>
    /// Inspector for <see cref="GodSimRunner"/>. Play Mode: live world stats, civilizations, time controls,
    /// per-system profiling, quick save and new world. Edit Mode: default inspector plus a settings helper.
    /// </summary>
    [CustomEditor(typeof(GodSimRunner))]
    public sealed class GodSimRunnerEditor : UnityEditor.Editor
    {
        private static bool _showCivs = true;
        private static bool _showProfiles = true;
        private static bool _sortProfilesByCost = true;
        private static bool _randomSeedForNewWorld;

        private readonly List<SystemProfile> _profiles = new List<SystemProfile>();
        private string _lastSaveMessage;

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var runner = target as GodSimRunner;
            if (runner == null) return;
            if (Application.isPlaying) DrawPlayMode(runner);
            else DrawEditMode(runner);
        }

        // ------------------------------------------------------------------ edit mode
        private void DrawEditMode(GodSimRunner runner)
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (runner.Settings == null)
            {
                GodSimSettings existing = GodSimEditorUtil.FindSettings();
                EditorGUILayout.HelpBox("No GodSimSettings assigned. Assign one so the world uses your seed, map size and balance values.", MessageType.Warning);
                if (GUILayout.Button(existing != null ? "Assign " + AssetDatabase.GetAssetPath(existing) : "Create Settings"))
                {
                    GodSimSettings settings = GodSimEditorUtil.LoadOrCreateSettings(out _);
                    Undo.RecordObject(runner, "Assign GodSim Settings");
                    runner.Settings = settings;
                    EditorUtility.SetDirty(runner);
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Settings")) Selection.activeObject = runner.Settings;
                if (GUILayout.Button("World Preview")) WorldPreviewWindow.Open();
                EditorGUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ play mode
        private void DrawPlayMode(GodSimRunner runner)
        {
            GodSimulation sim = runner.Simulation;
            if (sim == null || sim.State == null)
            {
                EditorGUILayout.HelpBox("The simulation has not started yet.", MessageType.Info);
                DrawWorldButtons(runner, null);
                return;
            }
            WorldState state = sim.State;

            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Date", SimTime.Format(state.Tick));
            EditorGUILayout.LabelField("Tick", state.Tick + "   (world age " + state.WorldAge + ")");
            EditorGUILayout.LabelField("Population", state.LivingPopulation + "   (peak " + state.Stats.PeakPopulation + ")");
            GodSimEditorUtil.CountBuildings(state, out int complete, out int underConstruction);
            EditorGUILayout.LabelField("Settlements", GodSimEditorUtil.AliveSettlements(state).ToString());
            EditorGUILayout.LabelField("Buildings", complete + " standing, " + underConstruction + " in construction");
            EditorGUILayout.LabelField("Wars", GodSimEditorUtil.ActiveWars(state) + " active, " + state.Stats.WarsDeclared + " declared");
            EditorGUILayout.LabelField("Mana", state.God.Mana.ToString("0") + " / " + state.God.MaxMana.ToString("0")
                + "   (+" + state.God.ManaPerTick.ToString("0.00") + "/day)");
            EditorGUILayout.LabelField("Step time", sim.Engine != null ? sim.Engine.LastStepMs.ToString("0.00") + " ms" : "-");

            EditorGUILayout.Space();
            DrawTimeControls(runner, sim);
            DrawWorldButtons(runner, sim);

            EditorGUILayout.Space();
            _showCivs = EditorGUILayout.Foldout(_showCivs, "Civilizations (" + state.Civilizations.Count + ")", true);
            if (_showCivs) DrawCivilizations(state);

            if (sim.Engine != null)
            {
                _showProfiles = EditorGUILayout.Foldout(_showProfiles, "System Profiles (" + sim.Engine.Profiles.Count + ")", true);
                if (_showProfiles) DrawProfiles(sim.Engine);
            }
        }

        private void DrawTimeControls(GodSimRunner runner, GodSimulation sim)
        {
            EditorGUILayout.LabelField("Time", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Speed", "index " + runner.SpeedIndex + "  -  " + runner.TicksPerSecond.ToString("0.#") + " ticks/s"
                + (runner.Paused ? "  (paused)" : ""));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(runner.Paused ? "Resume" : "Pause")) runner.Paused = !runner.Paused;
            if (GUILayout.Button("Slower")) runner.SpeedIndex = Math.Max(0, runner.SpeedIndex - 1);
            if (GUILayout.Button("Faster")) runner.SpeedIndex = runner.SpeedIndex + 1;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Step Day")) Step(sim, 1);
            if (GUILayout.Button("Step Season")) Step(sim, SimTime.TicksPerSeason);
            if (GUILayout.Button("Step Year")) Step(sim, SimTime.TicksPerYear);
            EditorGUILayout.EndHorizontal();
        }

        private static void Step(GodSimulation sim, int ticks)
        {
            try { sim.StepMany(ticks); }
            catch (Exception ex) { Debug.LogError(GodSimEditorUtil.LogPrefix + "Manual step failed: " + ex); }
            GUIUtility.ExitGUI(); // world changed mid-event: restart the GUI pass to keep layout consistent
        }

        private void DrawWorldButtons(GodSimRunner runner, GodSimulation sim)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(sim == null))
            {
                if (GUILayout.Button("Save Quick"))
                {
                    try
                    {
                        runner.SaveGame("quicksave", out string message);
                        _lastSaveMessage = string.IsNullOrEmpty(message) ? "Saved." : message;
                        Debug.Log(GodSimEditorUtil.LogPrefix + "Save Quick: " + _lastSaveMessage);
                    }
                    catch (Exception ex)
                    {
                        _lastSaveMessage = "Save failed: " + ex.Message;
                        Debug.LogError(GodSimEditorUtil.LogPrefix + "Save Quick failed: " + ex);
                    }
                }
            }
            if (GUILayout.Button("New World") &&
                EditorUtility.DisplayDialog("GodSim", "Discard the current world and generate a new one?", "New World", "Cancel"))
            {
                SimConfig cfg = GodSimEditorUtil.ConfigFrom(runner.Settings);
                if (_randomSeedForNewWorld) cfg.Seed = GodSimEditorUtil.RandomSeed();
                try { runner.NewWorld(cfg); }
                catch (Exception ex) { Debug.LogError(GodSimEditorUtil.LogPrefix + "New World failed: " + ex); }
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            _randomSeedForNewWorld = EditorGUILayout.Toggle("Random Seed On New World", _randomSeedForNewWorld);
            if (!string.IsNullOrEmpty(_lastSaveMessage)) EditorGUILayout.LabelField(_lastSaveMessage, EditorStyles.miniLabel);
        }

        private static void DrawCivilizations(WorldState state)
        {
            Dictionary<int, int> popByCiv = GodSimEditorUtil.PopulationByCiv(state);
            var civs = state.Civilizations.Items;
            EditorGUI.indentLevel++;
            for (int i = 0; i < civs.Count; i++)
            {
                Civilization c = civs[i];
                if (c == null) continue;
                popByCiv.TryGetValue(c.Id, out int pop);
                string label = c.Name + " (" + c.Race + ")";
                string value = c.IsAlive
                    ? c.Age + "  -  pop " + pop + "  -  " + c.SettlementIds.Count + " settl.  -  " + c.KnownTechs.Count + " techs"
                    : "collapsed in year " + SimTime.Year(c.CollapsedTick);
                EditorGUILayout.LabelField(label, value);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawProfiles(SimulationEngine engine)
        {
            _profiles.Clear();
            for (int i = 0; i < engine.Profiles.Count; i++) if (engine.Profiles[i] != null) _profiles.Add(engine.Profiles[i]);
            _sortProfilesByCost = EditorGUILayout.Toggle("Sort By Cost", _sortProfilesByCost);
            if (_sortProfilesByCost) _profiles.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));

            const float num = 58f;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("System", EditorStyles.miniBoldLabel, GUILayout.MinWidth(80f));
            GUILayout.Label("Avg ms", EditorStyles.miniBoldLabel, GUILayout.Width(num));
            GUILayout.Label("Last ms", EditorStyles.miniBoldLabel, GUILayout.Width(num));
            GUILayout.Label("Runs", EditorStyles.miniBoldLabel, GUILayout.Width(num));
            GUILayout.Label("Faults", EditorStyles.miniBoldLabel, GUILayout.Width(num));
            EditorGUILayout.EndHorizontal();

            double total = 0;
            for (int i = 0; i < _profiles.Count; i++)
            {
                SystemProfile p = _profiles[i];
                total += p.AvgMs;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(p.Disabled ? p.Name + " [DISABLED]" : p.Name, EditorStyles.miniLabel, GUILayout.MinWidth(80f));
                GUILayout.Label(p.AvgMs.ToString("0.000"), EditorStyles.miniLabel, GUILayout.Width(num));
                GUILayout.Label(p.LastMs.ToString("0.000"), EditorStyles.miniLabel, GUILayout.Width(num));
                GUILayout.Label(p.Runs.ToString(), EditorStyles.miniLabel, GUILayout.Width(num));
                GUILayout.Label(p.TotalFaults.ToString(), EditorStyles.miniLabel, GUILayout.Width(num));
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(p.LastError) && (p.Disabled || p.ConsecutiveFaults > 0))
                    EditorGUILayout.HelpBox(p.Name + ": " + p.LastError, p.Disabled ? MessageType.Error : MessageType.Warning);
            }
            EditorGUILayout.LabelField("Sum of averages", total.ToString("0.000") + " ms", EditorStyles.miniLabel);
        }
    }
}
