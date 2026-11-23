// Compile-check stubs for APIs owned by OTHER teams (signatures as agreed in the task brief).
using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public WorldState State { get; set; }
        public GameDatabase Db { get; set; }
        public EventBus Events { get; set; }
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
        public static Color FromRgba(uint rgba) => new Color(
            ((rgba >> 24) & 255) / 255f, ((rgba >> 16) & 255) / 255f,
            ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
        public static Color AgeTint(AgeId a) => default;
        public static Color Category(BuildingCategory c) => default;
    }

    public static class MaterialLibrary
    {
        public static Material VertexColorOpaque { get; set; }
        public static Material VertexColorInstanced { get; set; }
    }
}
