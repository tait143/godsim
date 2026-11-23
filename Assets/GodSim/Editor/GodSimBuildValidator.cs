using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GodSim.Unity.Editor
{
    /// <summary>
    /// Pre-build gate: the demo scene must be in (and enabled in) Build Settings, and GodSim.Core.asmdef must keep
    /// "noEngineReferences": true so the deterministic core never picks up UnityEngine dependencies.
    /// </summary>
    public sealed class GodSimBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = new List<string>();
            CheckDemoScene(problems);
            CheckCoreAssemblyDefinition(problems);
            if (problems.Count > 0)
                throw new BuildFailedException("GodSim build validation failed:\n - " + string.Join("\n - ", problems));
            Debug.Log(GodSimEditorUtil.LogPrefix + "Build validation passed.");
        }

        internal static void CheckDemoScene(List<string> problems)
        {
            string path = GodSimEditorUtil.DemoScenePath;
            bool listed = false, enabled = false;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] == null || !string.Equals(scenes[i].path, path, StringComparison.Ordinal)) continue;
                listed = true;
                enabled |= scenes[i].enabled;
            }
            if (!listed)
                problems.Add(path + " is not in Build Settings. Run 'GodSim/Create Demo Scene' or add it under File > Build Settings.");
            else if (!enabled)
                problems.Add(path + " is in Build Settings but disabled. Enable it under File > Build Settings.");
            if (!File.Exists(path))
                problems.Add(path + " does not exist on disk. Run 'GodSim/Create Demo Scene' to rebuild it.");
        }

        internal static void CheckCoreAssemblyDefinition(List<string> problems)
        {
            string path = GodSimEditorUtil.CoreAsmdefPath;
            if (!File.Exists(path))
            {
                problems.Add(path + " is missing. The deterministic core must live in its own engine-free assembly.");
                return;
            }

            AsmdefJson asmdef;
            try { asmdef = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                problems.Add(path + " could not be parsed as JSON: " + ex.Message);
                return;
            }
            if (asmdef == null)
            {
                problems.Add(path + " is empty or not valid JSON.");
                return;
            }
            if (asmdef.name != "GodSim.Core")
                problems.Add(path + " must be named \"GodSim.Core\" (found \"" + asmdef.name + "\").");
            if (!asmdef.noEngineReferences)
                problems.Add(path + " must keep \"noEngineReferences\": true. GodSim.Core is pure C# (deterministic, headless-testable); "
                    + "move engine-dependent code to GodSim.Unity instead.");
            if (asmdef.references != null)
            {
                for (int i = 0; i < asmdef.references.Length; i++)
                {
                    string r = asmdef.references[i] ?? "";
                    if (r.StartsWith("GodSim.Unity", StringComparison.Ordinal) || r.StartsWith("GodSim.Editor", StringComparison.Ordinal) || r.StartsWith("Unity", StringComparison.Ordinal))
                        problems.Add(path + " references \"" + r + "\"; the core may not depend on Unity or presentation assemblies.");
                }
            }
        }

#pragma warning disable 0649 // populated by JsonUtility
        [Serializable]
        private sealed class AsmdefJson
        {
            public string name;
            public bool noEngineReferences;
            public string[] references;
        }
#pragma warning restore 0649
    }
}
