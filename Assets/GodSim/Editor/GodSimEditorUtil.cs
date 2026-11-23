using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEditor;
using UnityEngine;

namespace GodSim.Unity.Editor
{
    /// <summary>Shared helpers for the GodSim editor tooling (asset paths, settings, type lookup, database).</summary>
    internal static class GodSimEditorUtil
    {
        public const string SettingsFolder = "Assets/GodSim/Settings";
        public const string SettingsAssetPath = SettingsFolder + "/GodSimSettings.asset";
        public const string ScenesFolder = "Assets/GodSim/Scenes";
        public const string DemoScenePath = ScenesFolder + "/GodSimDemo.unity";
        public const string CoreAsmdefPath = "Assets/GodSim/Core/GodSim.Core.asmdef";
        public const string LogPrefix = "[GodSim] ";

        /// <summary>Finds the canonical settings asset, or any GodSimSettings asset in the project. May return null.</summary>
        public static GodSimSettings FindSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GodSimSettings>(SettingsAssetPath);
            if (settings != null) return settings;
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(GodSimSettings).Name);
            if (guids == null) return null;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                settings = AssetDatabase.LoadAssetAtPath<GodSimSettings>(path);
                if (settings != null) return settings;
            }
            return null;
        }

        /// <summary>Returns the existing settings asset or creates <see cref="SettingsAssetPath"/>.</summary>
        public static GodSimSettings LoadOrCreateSettings(out bool created)
        {
            created = false;
            var settings = FindSettings();
            if (settings != null) return settings;

            EnsureFolder(SettingsFolder);
            settings = ScriptableObject.CreateInstance<GodSimSettings>();
            settings.name = "GodSimSettings";
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            created = true;
            Debug.Log(LogPrefix + "Created settings asset at " + SettingsAssetPath, settings);
            return settings;
        }

        /// <summary>Sanitized config from a settings asset (defaults when null or when ToConfig fails).</summary>
        public static SimConfig ConfigFrom(GodSimSettings settings)
        {
            SimConfig cfg = null;
            if (settings != null)
            {
                try { cfg = settings.ToConfig(); }
                catch (Exception ex) { Debug.LogWarning(LogPrefix + "GodSimSettings.ToConfig() failed, using defaults: " + ex.Message, settings); }
            }
            if (cfg == null) cfg = new SimConfig();
            return cfg.Sanitize();
        }

        /// <summary>Deep-enough copy of a config so two simulations never share mutable arrays.</summary>
        public static SimConfig CloneConfig(SimConfig cfg)
        {
            var c = cfg.Clone();
            if (cfg.StartingRaces != null) c.StartingRaces = (RaceId[])cfg.StartingRaces.Clone();
            return c;
        }

        /// <summary>Creates every missing folder of an "Assets/..." path.</summary>
        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>Editor-friendly random seed (UI only; never used inside the deterministic core).</summary>
        public static ulong RandomSeed()
        {
            ulong mixed = Hashing.Mix64((ulong)DateTime.UtcNow.Ticks ^ ((ulong)System.Diagnostics.Stopwatch.GetTimestamp() << 21));
            return mixed % 1000000000UL;
        }

        /// <summary>
        /// Resolves a component type by simple name: first "GodSim.Unity.&lt;name&gt;" in the GodSim.Unity
        /// assembly, then any concrete MonoBehaviour with that name in GodSim*/Assembly-CSharp assemblies.
        /// Returns null when the type does not exist (optional components are skipped by callers).
        /// </summary>
        public static Type FindComponentType(string simpleName)
        {
            Type preferred = typeof(GodSimRunner).Assembly.GetType("GodSim.Unity." + simpleName, false);
            if (IsAddableComponent(preferred)) return preferred;

            var assemblies = new List<Assembly>(AppDomain.CurrentDomain.GetAssemblies());
            assemblies.Sort((a, b) => string.CompareOrdinal(a.GetName().Name, b.GetName().Name));
            for (int i = 0; i < assemblies.Count; i++)
            {
                string asmName = assemblies[i].GetName().Name;
                if (!asmName.StartsWith("GodSim", StringComparison.Ordinal) && !asmName.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = assemblies[i].GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                if (types == null) continue;
                for (int t = 0; t < types.Length; t++)
                    if (types[t] != null && types[t].Name == simpleName && IsAddableComponent(types[t])) return types[t];
            }
            return null;
        }

        private static bool IsAddableComponent(Type t) =>
            t != null && !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(MonoBehaviour).IsAssignableFrom(t);

        /// <summary>
        /// Builds a frozen, fully registered GameDatabase. Uses GodSimulation.BuildDatabase() when the
        /// composition root exposes it; otherwise creates a minimal 64x64 world and takes its database.
        /// </summary>
        public static GameDatabase BuildDatabase()
        {
            MethodInfo[] methods = typeof(GodSimulation).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name != "BuildDatabase" || !typeof(GameDatabase).IsAssignableFrom(m.ReturnType)) continue;
                ParameterInfo[] ps = m.GetParameters();
                bool allOptional = true;
                for (int p = 0; p < ps.Length; p++) if (!ps[p].IsOptional) { allOptional = false; break; }
                if (!allOptional) continue;
                var args = new object[ps.Length];
                for (int p = 0; p < ps.Length; p++) args[p] = ps[p].DefaultValue;
                try { return (GameDatabase)m.Invoke(null, args); }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    throw;
                }
            }

            var cfg = new SimConfig { MapWidth = 64, MapHeight = 64, InitialPopulationPerRace = 2 }.Sanitize();
            return GodSimulation.CreateNew(cfg, NullLogger.Instance).Db;
        }

        /// <summary>Counts living people per civilization id.</summary>
        public static Dictionary<int, int> PopulationByCiv(WorldState state)
        {
            var result = new Dictionary<int, int>();
            var people = state.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p == null || !p.IsAlive) continue;
                result.TryGetValue(p.CivilizationId, out int n);
                result[p.CivilizationId] = n + 1;
            }
            return result;
        }

        public static int AliveSettlements(WorldState state)
        {
            int n = 0;
            var items = state.Settlements.Items;
            for (int i = 0; i < items.Count; i++) if (items[i] != null && items[i].IsAlive) n++;
            return n;
        }

        public static void CountBuildings(WorldState state, out int complete, out int underConstruction)
        {
            complete = 0;
            underConstruction = 0;
            var items = state.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b == null || b.IsDestroyed) continue;
                if (b.IsComplete) complete++; else underConstruction++;
            }
        }

        public static int ActiveWars(WorldState state)
        {
            int n = 0;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].IsActive) n++;
            return n;
        }

        public static string CivName(WorldState state, int civId)
        {
            var civ = state.Civilizations.Get(civId);
            return civ != null ? civ.Name : "#" + civId;
        }
    }

    /// <summary>Routes simulation log output to the Unity console, with a cap so faulting systems cannot flood it.</summary>
    internal sealed class EditorSimLogger : ISimLogger
    {
        private const int MaxForwarded = 40;

        public EditorSimLogger(LogLevel minLevel) { MinLevel = minLevel; }

        public LogLevel MinLevel { get; }
        public int Errors { get; private set; }
        public int Warnings { get; private set; }

        public void Log(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            if (level >= LogLevel.Error)
            {
                Errors++;
                if (Errors <= MaxForwarded) Debug.LogError(GodSimEditorUtil.LogPrefix + message);
            }
            else if (level == LogLevel.Warning)
            {
                Warnings++;
                if (Warnings <= MaxForwarded) Debug.LogWarning(GodSimEditorUtil.LogPrefix + message);
            }
            else
            {
                Debug.Log(GodSimEditorUtil.LogPrefix + message);
            }
        }
    }
}
