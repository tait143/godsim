// Compile-only stubs of OTHER teams' APIs consumed by Assets/GodSim/Editor (signatures from the integration brief).
// Renderer/controller/HUD/audio components are resolved by type name at runtime and need no stubs.
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    public class GodSimSettings : ScriptableObject
    {
        public SimConfig ToConfig() => new SimConfig();
    }

    public class GodSimRunner : MonoBehaviour
    {
        public GodSimSettings Settings;
        public GodSimulation Simulation { get; private set; }
        public bool Paused { get; set; }
        public int SpeedIndex { get; set; }
        public float TicksPerSecond { get; private set; }
        public void NewWorld(SimConfig config) { }
        public bool SaveGame(string slot, out string message) { message = null; return false; }
    }
}

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null) => null;
        public WorldState State { get; private set; }
        public SimulationEngine Engine { get; private set; }
        public GameDatabase Db { get; private set; }
        public void StepMany(int ticks) { }
        public ulong ComputeHash() => 0UL;
    }
}

namespace GodSim.Core.World
{
    public sealed class WorldGenSettings
    {
        public static WorldGenSettings FromConfig(SimConfig c) => new WorldGenSettings();
    }

    public static class WorldGenerator
    {
        public static WorldMap Generate(WorldGenSettings settings, Rng rng) => null;
    }
}

namespace GodSim.Core.Security
{
    public static class ContentValidator
    {
        public static List<string> Validate(GameDatabase db) => new List<string>();
    }
}
