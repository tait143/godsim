using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Core.Security;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GodSim.Unity.Editor
{
    /// <summary>Top-level "GodSim" menu: demo scene builder, settings, headless runs and validation tools.</summary>
    public static class GodSimMenu
    {
        private const string UndoName = "Create GodSim Demo Scene";
        private const int HeadlessYears = 100;
        private const int DeterminismYears = 20;

        /// <summary>Layers the renderers/controllers look up by name.</summary>
        private static readonly string[] RequiredLayers = { "Terrain", "Buildings" };

        /// <summary>Components added to the camera (resolved by type name).</summary>
        private static readonly string[] CameraComponents = { "GodCameraController" };

        /// <summary>Components added to the "GodSim" root (resolved by type name; missing ones are skipped with a warning).</summary>
        private static readonly string[] RootComponents =
        {
            "TerrainRenderer", "BuildingRenderer", "PeopleRenderer", "ArmyRenderer", "SettlementLabels",
            "GodPowerVFX", "FireVFX", "WeatherVFX", "DayNightCycle",
            "SelectionController", "GodPowerController", "GameShortcuts", "GodSimHUD"
        };

        /// <summary>Optional components (other teams); silently skipped when the type does not exist.</summary>
        private static readonly string[] OptionalRootComponents = { "GodSimAudio" };

        // ================================================================== Create Demo Scene
        [MenuItem("GodSim/Create Demo Scene", false, 0)]
        public static void CreateDemoScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("GodSim", "Exit Play Mode before creating the demo scene.", "OK");
                return;
            }
            string scenePath = GodSimEditorUtil.DemoScenePath;
            if (File.Exists(scenePath) &&
                !EditorUtility.DisplayDialog("GodSim", scenePath + " already exists. Rebuild and overwrite it?", "Overwrite", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave()) return;

            var report = new StringBuilder();
            try
            {
                EditorUtility.DisplayProgressBar("GodSim", "Preparing settings and layers...", 0.1f);
                var settings = GodSimEditorUtil.LoadOrCreateSettings(out bool createdSettings);
                if (createdSettings) report.AppendLine("Created " + GodSimEditorUtil.SettingsAssetPath);
                SimConfig cfg = GodSimEditorUtil.ConfigFrom(settings);
                EnsureLayers(RequiredLayers, report);

                EditorUtility.DisplayProgressBar("GodSim", "Building scene...", 0.4f);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                int undoGroup = Undo.GetCurrentGroup();

                var center = new Vector3(cfg.MapWidth * 0.5f, 0f, cfg.MapHeight * 0.5f); // 1 tile = 1 world unit (x -> X, y -> Z)
                float span = Mathf.Max(cfg.MapWidth, cfg.MapHeight);

                // --- Sun
                var lightGo = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(lightGo, UndoName);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.75f;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.96f, 0.88f, 1f);
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.sun = light;

                // --- Camera (looking down at the map center)
                var camGo = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(camGo, UndoName);
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = Mathf.Max(1000f, span * 4f);
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = center + new Vector3(0f, span * 0.45f, -span * 0.35f);
                camGo.transform.LookAt(center);
                AddComponentsByName(camGo, CameraComponents, true, report);

                // --- Simulation root
                var root = new GameObject("GodSim");
                Undo.RegisterCreatedObjectUndo(root, UndoName);
                var runner = root.AddComponent<GodSimRunner>();
                runner.Settings = settings;
                AddComponentsByName(root, RootComponents, true, report);
                AddComponentsByName(root, OptionalRootComponents, false, report);
                EditorUtility.SetDirty(runner);

                Undo.CollapseUndoOperations(undoGroup);
                EditorSceneManager.MarkSceneDirty(scene);

                EditorUtility.DisplayProgressBar("GodSim", "Saving scene...", 0.85f);
                GodSimEditorUtil.EnsureFolder(GodSimEditorUtil.ScenesFolder);
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                {
                    Debug.LogError(GodSimEditorUtil.LogPrefix + "Could not save the demo scene to " + scenePath + ".");
                    return;
                }
                AddSceneToBuildSettings(scenePath, report);
                Selection.activeObject = root;
                Debug.Log(GodSimEditorUtil.LogPrefix + "Demo scene created at " + scenePath + ". Press Play to start.\n" + report);
            }
            catch (Exception ex)
            {
                Debug.LogError(GodSimEditorUtil.LogPrefix + "Creating the demo scene failed: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void AddComponentsByName(GameObject go, string[] typeNames, bool warnIfMissing, StringBuilder report)
        {
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = GodSimEditorUtil.FindComponentType(typeNames[i]);
                if (type == null)
                {
                    if (warnIfMissing)
                    {
                        Debug.LogWarning(GodSimEditorUtil.LogPrefix + "Component type '" + typeNames[i] + "' not found; skipped on '" + go.name + "'.");
                        report.AppendLine("Skipped missing component " + typeNames[i]);
                    }
                    continue;
                }
                if (go.GetComponent(type) != null) continue; // e.g. auto-added through [RequireComponent]
                Component added = go.AddComponent(type);
                if (added == null)
                {
                    Debug.LogWarning(GodSimEditorUtil.LogPrefix + "Could not add component '" + type.FullName + "' to '" + go.name + "'.");
                    report.AppendLine("Failed to add " + type.FullName);
                }
            }
        }

        /// <summary>Creates the named user layers in free TagManager slots (8..31). Existing layers are kept.</summary>
        private static void EnsureLayers(string[] names, StringBuilder report)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogWarning(GodSimEditorUtil.LogPrefix + "TagManager not found; layers were not created.");
                return;
            }
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogWarning(GodSimEditorUtil.LogPrefix + "TagManager has no 'layers' array; layers were not created.");
                return;
            }

            bool changed = false;
            for (int n = 0; n < names.Length; n++)
            {
                int existing = -1, free = -1;
                for (int i = 8; i < layers.arraySize && i < 32; i++)
                {
                    string value = layers.GetArrayElementAtIndex(i).stringValue;
                    if (value == names[n]) { existing = i; break; }
                    if (free < 0 && string.IsNullOrEmpty(value)) free = i;
                }
                if (existing >= 0) continue;
                if (free < 0)
                {
                    Debug.LogWarning(GodSimEditorUtil.LogPrefix + "No free user layer slot for '" + names[n] + "'. Add it manually in Tags & Layers.");
                    continue;
                }
                layers.GetArrayElementAtIndex(free).stringValue = names[n];
                report.AppendLine("Created layer '" + names[n] + "' in slot " + free);
                changed = true;
            }
            if (changed)
            {
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        private static void AddSceneToBuildSettings(string scenePath, StringBuilder report)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0]);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i] == null || !string.Equals(scenes[i].path, scenePath, StringComparison.Ordinal)) continue;
                if (scenes[i].enabled) return;
                scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                report.AppendLine("Enabled " + scenePath + " in Build Settings");
                return;
            }
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            report.AppendLine("Added " + scenePath + " to Build Settings (index 0)");
        }

        // ================================================================== Settings
        [MenuItem("GodSim/Create Settings Asset", false, 1)]
        public static void CreateSettingsAsset()
        {
            var settings = GodSimEditorUtil.LoadOrCreateSettings(out bool created);
            if (!created) Debug.Log(GodSimEditorUtil.LogPrefix + "Settings asset already exists at " + AssetDatabase.GetAssetPath(settings), settings);
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        // ================================================================== Headless simulation
        [MenuItem("GodSim/Simulate 100 Years (Headless)", false, 20)]
        public static void SimulateHeadless()
        {
            var settings = GodSimEditorUtil.FindSettings();
            if (settings == null) Debug.LogWarning(GodSimEditorUtil.LogPrefix + "No GodSimSettings asset found; simulating with default SimConfig.");
            SimConfig cfg = GodSimEditorUtil.ConfigFrom(settings);
            var log = new EditorSimLogger(LogLevel.Warning);
            const string title = "GodSim - Headless Simulation";

            bool cancelled = false;
            long ticks = 0;
            double genMs = 0, stepMs = 0;
            try
            {
                EditorUtility.DisplayProgressBar(title, "Generating world (seed " + cfg.Seed + ", " + cfg.MapWidth + "x" + cfg.MapHeight + ")...", 0f);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                GodSimulation sim = GodSimulation.CreateNew(cfg, log);
                genMs = watch.Elapsed.TotalMilliseconds;

                int seasons = HeadlessYears * 4;
                watch.Restart();
                for (int s = 0; s < seasons; s++)
                {
                    string info = "Year " + (s / 4) + " / " + HeadlessYears + "  -  population " + sim.State.LivingPopulation;
                    if (EditorUtility.DisplayCancelableProgressBar(title, info, s / (float)seasons)) { cancelled = true; break; }
                    sim.StepMany(SimTime.TicksPerSeason);
                    ticks += SimTime.TicksPerSeason;
                }
                stepMs = watch.Elapsed.TotalMilliseconds;

                bool faulted;
                string summary = BuildSummary(sim, cfg, ticks, genMs, stepMs, cancelled, log, out faulted);
                if (faulted) Debug.LogWarning(summary); else Debug.Log(summary);
            }
            catch (Exception ex)
            {
                Debug.LogError(GodSimEditorUtil.LogPrefix + "Headless simulation failed after " + ticks + " ticks: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string BuildSummary(GodSimulation sim, SimConfig cfg, long ticks, double genMs, double stepMs,
            bool cancelled, EditorSimLogger log, out bool faulted)
        {
            WorldState state = sim.State;
            var sb = new StringBuilder();
            sb.Append(GodSimEditorUtil.LogPrefix).Append("Headless run ").Append(cancelled ? "CANCELLED" : "finished")
              .Append(": ").Append(SimTime.TicksToYears(ticks).ToString("0.#")).Append(" years (").Append(ticks).Append(" ticks)")
              .Append(", seed ").Append(cfg.Seed).Append(", ").Append(cfg.MapWidth).Append('x').Append(cfg.MapHeight)
              .Append(' ').Append(cfg.Preset).AppendLine();
            sb.Append("Date: ").Append(SimTime.Format(state.Tick)).Append("   World age: ").Append(state.WorldAge).AppendLine();
            sb.Append("Timing: world gen ").Append(genMs.ToString("0")).Append(" ms, simulation ")
              .Append((stepMs / 1000.0).ToString("0.00")).Append(" s, ")
              .Append((ticks > 0 ? stepMs / ticks : 0).ToString("0.000")).AppendLine(" ms/tick");

            int living = state.LivingPopulation;
            sb.Append("Population: ").Append(living).Append(" (peak ").Append(state.Stats.PeakPopulation)
              .Append(", births ").Append(state.Stats.TotalBirths).Append(", deaths ").Append(state.Stats.TotalDeaths).AppendLine(")");

            // --- Civilizations
            Dictionary<int, int> popByCiv = GodSimEditorUtil.PopulationByCiv(state);
            var settlementsByCiv = new Dictionary<int, int>();
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                settlementsByCiv.TryGetValue(s.CivilizationId, out int n);
                settlementsByCiv[s.CivilizationId] = n + 1;
            }
            var buildingsByCiv = new Dictionary<int, int>();
            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                buildingsByCiv.TryGetValue(b.CivilizationId, out int n);
                buildingsByCiv[b.CivilizationId] = n + 1;
            }
            sb.AppendLine("Civilizations:");
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null) continue;
                popByCiv.TryGetValue(c.Id, out int pop);
                settlementsByCiv.TryGetValue(c.Id, out int setl);
                buildingsByCiv.TryGetValue(c.Id, out int bld);
                sb.Append("  ").Append(c.Name).Append(" (").Append(c.Race).Append(") ")
                  .Append(c.IsAlive ? "" : "[COLLAPSED] ")
                  .Append("age ").Append(c.Age)
                  .Append(", pop ").Append(pop)
                  .Append(", settlements ").Append(setl)
                  .Append(", buildings ").Append(bld)
                  .Append(", techs ").Append(c.KnownTechs.Count)
                  .Append(", research ").Append(c.TotalResearch.ToString("0")).AppendLine();
            }

            // --- World totals
            GodSimEditorUtil.CountBuildings(state, out int complete, out int underConstruction);
            sb.Append("Settlements: ").Append(GodSimEditorUtil.AliveSettlements(state)).Append(" alive (")
              .Append(state.Stats.SettlementsFounded).AppendLine(" founded)");
            sb.Append("Buildings: ").Append(complete).Append(" standing, ").Append(underConstruction).Append(" under construction (")
              .Append(state.Stats.BuildingsCompleted).Append(" completed, ").Append(state.Stats.BuildingsDestroyed).AppendLine(" destroyed)");

            // --- Wars
            var wars = state.Diplomacy.Wars;
            sb.Append("Wars: ").Append(wars.Count).Append(" recorded, ").Append(GodSimEditorUtil.ActiveWars(state)).Append(" active, ")
              .Append(state.Stats.WarsDeclared).Append(" declared, ").Append(state.Stats.BattlesFought).AppendLine(" battles");
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                sb.Append("  ").Append(GodSimEditorUtil.CivName(state, w.AttackerId)).Append(" vs ")
                  .Append(GodSimEditorUtil.CivName(state, w.DefenderId)).Append(" since year ").Append(SimTime.Year(w.StartTick))
                  .Append(", score ").Append(w.WarScore.ToString("0")).AppendLine();
            }

            // --- Systems
            faulted = log.Errors > 0;
            var profiles = new List<SystemProfile>(sim.Engine.Profiles);
            profiles.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            sb.AppendLine("Slowest systems (avg ms per run):");
            for (int i = 0; i < profiles.Count && i < 8; i++)
            {
                var p = profiles[i];
                sb.Append("  ").Append(p.Name).Append(": ").Append(p.AvgMs.ToString("0.000")).Append(" ms avg, ")
                  .Append(p.Runs).Append(" runs").AppendLine();
            }
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p.TotalFaults == 0 && !p.Disabled) continue;
                faulted = true;
                sb.Append("  FAULTS ").Append(p.Name).Append(": ").Append(p.TotalFaults).Append(p.Disabled ? " (DISABLED)" : "")
                  .Append(" last: ").Append(p.LastError).AppendLine();
            }
            sb.Append("Log: ").Append(log.Errors).Append(" errors, ").Append(log.Warnings).Append(" warnings");
            return sb.ToString();
        }

        // ================================================================== Content validation
        [MenuItem("GodSim/Validate Content", false, 40)]
        public static void ValidateContent()
        {
            try
            {
                EditorUtility.DisplayProgressBar("GodSim", "Building game database...", 0.2f);
                GameDatabase db = GodSimEditorUtil.BuildDatabase();
                EditorUtility.DisplayProgressBar("GodSim", "Validating content...", 0.7f);
                List<string> issues = ContentValidator.Validate(db) ?? new List<string>();

                int errors = 0, warnings = 0;
                for (int i = 0; i < issues.Count; i++)
                {
                    string msg = issues[i] ?? "";
                    bool isWarning = IsWarning(msg);
                    if (isWarning) warnings++; else errors++;
                    if (i >= 200) continue; // count the rest, but do not flood the console
                    if (isWarning) Debug.LogWarning(GodSimEditorUtil.LogPrefix + "Content: " + msg);
                    else Debug.LogError(GodSimEditorUtil.LogPrefix + "Content: " + msg);
                }
                string summary = GodSimEditorUtil.LogPrefix + "Content validation: " + errors + " error(s), " + warnings + " warning(s) in "
                    + CountNonNull(db.Races) + " races, " + CountNonNull(db.Ages) + " ages, " + CountNonNull(db.Buildings) + " buildings, "
                    + db.Techs.Count + " techs, " + CountNonNull(db.GodPowers) + " god powers.";
                if (errors > 0) Debug.LogError(summary);
                else if (warnings > 0) Debug.LogWarning(summary);
                else Debug.Log(summary);
            }
            catch (Exception ex)
            {
                Debug.LogError(GodSimEditorUtil.LogPrefix + "Content validation failed: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool IsWarning(string msg)
        {
            string t = msg.TrimStart(' ', '[', '(');
            return t.StartsWith("warn", StringComparison.OrdinalIgnoreCase) || t.StartsWith("info", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountNonNull<T>(IReadOnlyList<T> list) where T : class
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) n++;
            return n;
        }

        // ================================================================== Determinism
        [MenuItem("GodSim/Determinism Check", false, 41)]
        public static void DeterminismCheck()
        {
            SimConfig cfg = GodSimEditorUtil.ConfigFrom(GodSimEditorUtil.FindSettings());
            const string title = "GodSim - Determinism Check";
            var logA = new EditorSimLogger(LogLevel.Error);
            var logB = new EditorSimLogger(LogLevel.Error);
            try
            {
                EditorUtility.DisplayProgressBar(title, "Creating world A (seed " + cfg.Seed + ")...", 0f);
                GodSimulation a = GodSimulation.CreateNew(GodSimEditorUtil.CloneConfig(cfg), logA);
                EditorUtility.DisplayProgressBar(title, "Creating world B (seed " + cfg.Seed + ")...", 0.02f);
                GodSimulation b = GodSimulation.CreateNew(GodSimEditorUtil.CloneConfig(cfg), logB);

                ulong ha = a.ComputeHash(), hb = b.ComputeHash();
                int divergedYear = ha != hb ? 0 : -1;
                int year = 0;
                bool cancelled = false;
                // Lockstep: interleaving also exposes hidden static/shared mutable state between instances.
                while (divergedYear < 0 && year < DeterminismYears)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(title, "Year " + year + " / " + DeterminismYears + "  -  hash " + ha.ToString("X16"),
                        0.05f + 0.95f * year / DeterminismYears)) { cancelled = true; break; }
                    a.StepMany(SimTime.TicksPerYear);
                    b.StepMany(SimTime.TicksPerYear);
                    year++;
                    ha = a.ComputeHash();
                    hb = b.ComputeHash();
                    if (ha != hb) divergedYear = year;
                }

                if (divergedYear >= 0)
                    Debug.LogError(GodSimEditorUtil.LogPrefix + "Determinism check FAILED: worlds with seed " + cfg.Seed + " diverged at year " + divergedYear
                        + " (A " + ha.ToString("X16") + " != B " + hb.ToString("X16") + "). Look for System.Random, Dictionary/HashSet iteration, "
                        + "static mutable state or wall-clock time in the systems.");
                else if (cancelled)
                    Debug.LogWarning(GodSimEditorUtil.LogPrefix + "Determinism check cancelled at year " + year + "; hashes matched so far (" + ha.ToString("X16") + ").");
                else
                    Debug.Log(GodSimEditorUtil.LogPrefix + "Determinism check PASSED: " + DeterminismYears + " years, seed " + cfg.Seed + ", hash " + ha.ToString("X16")
                        + ", population " + a.State.LivingPopulation + ".");
                if (logA.Errors + logB.Errors > 0)
                    Debug.LogWarning(GodSimEditorUtil.LogPrefix + "Simulation logged " + (logA.Errors + logB.Errors) + " error(s) during the determinism check.");
            }
            catch (Exception ex)
            {
                Debug.LogError(GodSimEditorUtil.LogPrefix + "Determinism check failed with an exception: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
