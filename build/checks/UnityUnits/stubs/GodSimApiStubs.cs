// Compile-only stubs of other teams' APIs consumed by Units/Effects. Signatures mirror the real files
// (Assets/GodSim/Unity/Runtime/Core/*.cs, Assets/GodSim/Core/Composition/GodSimulation.cs). Never shipped.
using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public EventBus Events { get; }
        public long Tick => State.Tick;
    }
}

namespace GodSim.Unity
{
    using GodSim.Core.Composition;

    public sealed class GodSimSettings : ScriptableObject
    {
        public int MaxVisiblePeople = 6000;
    }

    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; private set; }
        public GodSimSettings Settings;
        public event Action<GodSimulation> SimulationCreated;
        public event Action Ticked;
        public GodSimulation Simulation { get; private set; }
        public bool Paused { get; set; }
        public float TicksPerSecond => 0f;
        private void Suppress() { SimulationCreated?.Invoke(null); Ticked?.Invoke(); }
    }

    public static class GodSimCoords
    {
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => default;
        public static Vector3 TileToWorld(WorldMap map, float x, float y) => default;
        public static float SurfaceHeight(WorldMap map, int x, int y) => 0f;
    }

    public static class GodSimPalette
    {
        public static Color FromRgba(uint rgba) => default;
        public static Color Season(Season s) => default;
    }

    public static class MaterialLibrary
    {
        public static Material VertexColorInstanced => null;
        public static Material Unlit(Color c) => null;
    }
}
