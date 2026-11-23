// Compile-only stand-ins for other teams' APIs used by the overlays (documented signatures only).
// NEVER copy into Assets/.
#pragma warning disable CS0067, CS1591
using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public EventBus Events { get; }
    }
}

namespace GodSim.Unity
{
    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; private set; }
        public GodSimulation Simulation { get; private set; }
        public event Action<GodSimulation> SimulationCreated;
        public event Action Ticked;
    }

    public static class GodSimCoords
    {
        public const float TileSize = 1f;
        public static float HeightScale = 6f;
        public static float SurfaceHeight(WorldMap map, int x, int y) => 0f;
    }

    public static class MaterialLibrary
    {
        public static Material Unlit(Color c) => null;
    }

    public static class InputAdapter
    {
        public static bool GetKeyDown(KeyCode k) => false;
    }
}
