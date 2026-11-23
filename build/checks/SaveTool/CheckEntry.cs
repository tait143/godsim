// Check-only entry point (build/checks/SaveTool): adds sample generators for the smoke test, then
// delegates to the real tool. Never part of tools/GodSim.SaveTool.
#pragma warning disable CS1591
using System;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.SaveTool.Check
{
    public static class CheckEntry
    {
        public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "make-sample")
            {
                // make-sample <out> [keyHex|-] [tick] [civName]
                var sim = GodSimulation.CreateNew(new SimConfig { Seed = 42, MapWidth = 64, MapHeight = 64 });
                if (args.Length >= 4) sim.StepMany(int.Parse(args[3]));
                if (args.Length >= 5 && sim.State.Civilizations.Count > 0) sim.State.Civilizations[0].Name = args[4];
                byte[] key = args.Length >= 3 && args[2] != "-" ? KeyMaterial.ParseHex(args[2]) : null;
                File.WriteAllBytes(args[1], sim.Save(key));
                Console.WriteLine("wrote " + args[1]);
                return 0;
            }
#if SAVETOOL_STUBS
            if (args.Length >= 2 && args[0] == "make-replay")
            {
                using (var w = new BinaryWriter(File.Create(args[1])))
                {
                    w.Write(new[] { (byte)'G', (byte)'R', (byte)'P', (byte)'L' });
                    w.Write(3);
                    long[] ticks = { 2, 5, 5 };
                    foreach (long t in ticks) { w.Write(t); w.Write((byte)0); w.Write(10); w.Write(10); w.Write(2); w.Write(0); }
                }
                Console.WriteLine("wrote " + args[1]);
                return 0;
            }
#endif
            return Program.Main(args);
        }
    }
}
