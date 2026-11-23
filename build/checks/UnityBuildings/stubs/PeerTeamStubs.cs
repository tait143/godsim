// Compile-check stubs for APIs owned by OTHER teams (signatures as agreed in the task brief).
using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public WorldState State { get; }
        public GameDatabase Db { get; }
        public EventBus Events { get; }
        public long Tick { get; }
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
        private void SilenceUnused() { SimulationCreated?.Invoke(null); Ticked?.Invoke(); Instance = null; Simulation = null; }
    }

    public static class GodSimCoords
    {
        public const float TileSize = 1f;
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => default;
        public static Vector3 TileToWorld(WorldMap map, float x, float y) => default;
        public static float SurfaceHeight(WorldMap map, int x, int y) => 0f;
    }

    public static class GodSimPalette
    {
        public static Color FromRgba(uint rgba) => default;
        public static Color AgeTint(AgeId a) => default;
        public static Color Category(BuildingCategory c) => default;
    }

    public static class MaterialLibrary
    {
        public static Material VertexColorOpaque => null;
        public static Material VertexColorInstanced => null;
    }
}
