using System;
using System.Collections.Generic;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>`verify`: integrity (checksum / HMAC) + full parse + StateValidator. Exit 0 / 4 / 5.</summary>
    public static class VerifyCommand
    {
        private const int MaxIssuesShown = 50;

        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("verify", "file").Option("--key-file").Option("--key-hex").Flag("--require-signature").Parse(args, 1);
            byte[] key = KeyMaterial.FromArgs(p);
            try
            {
                bool require = p.Has("--require-signature");
                LoadedSave save = SaveAccess.Load(p.Positional(0), key, require);
                string name = TextOut.Safe(save.Path, 300);

                List<string> issues;
                try { issues = StateValidator.Validate(save.State, save.Db) ?? new List<string>(); }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    throw new ToolException(ExitCodes.Corrupt, "State validation crashed on " + name + " (" + e.GetType().Name + "): " + TextOut.Safe(e.Message, 200), e);
                }

                string integrity = save.Header.IsSigned ? "HMAC-SHA256 signature verified" : "SHA-256 checksum OK (unsigned" + (key != null ? "; key not used" : "") + ")";
                TextOut.Line("File:      " + name);
                TextOut.Line("Integrity: " + integrity);
                TextOut.Line("Parse:     OK (schema " + save.Header.SchemaVersion + ", tick " + save.State.Tick + ", " + save.State.LivingPopulation + " living people)");
                TextOut.Line("Hash:      " + TextOut.Hash(StateHasher.Compute(save.State)));
                if (issues.Count > 0)
                {
                    TextOut.Line("State:     INVALID (" + issues.Count + " issue" + (issues.Count == 1 ? "" : "s") + ")");
                    for (int i = 0; i < issues.Count && i < MaxIssuesShown; i++) TextOut.Line("  - " + TextOut.Safe(issues[i], 300));
                    if (issues.Count > MaxIssuesShown) TextOut.Line("  ... " + (issues.Count - MaxIssuesShown) + " more");
                    TextOut.Line("RESULT: INVALID");
                    return ExitCodes.Corrupt;
                }
                TextOut.Line("State:     OK (StateValidator found no issues)");
                TextOut.Line("RESULT: VALID");
                return ExitCodes.Ok;
            }
            finally
            {
                KeyMaterial.Wipe(key);
            }
        }
    }
}
