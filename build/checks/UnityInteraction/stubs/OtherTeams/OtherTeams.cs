// Compile-only stubs of the other teams' APIs consumed by the interaction layer (as specified in the brief).
using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public WorldState State => throw null;
        public GameDatabase Db => throw null;
        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error) => throw null;
    }
}

namespace GodSim.Unity
{
    using GodSim.Core.Composition;

    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get => throw null; private set => throw null; }
        public static readonly float[] SpeedLevels = { 0.5f, 1f, 2f, 4f, 8f };
        public GodSimulation Simulation => throw null;
        public bool Paused { get => throw null; set => throw null; }
        public int SpeedIndex { get => throw null; set => throw null; }
        public event Action<GodSimulation> SimulationCreated;
        private void Touch() => SimulationCreated?.Invoke(null);
    }

    public static class GodSimCoords
    {
        public const float TileSize = 1f;
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => throw null;
        public static bool WorldToTile(WorldMap map, Vector3 world, out Vec2i tile) => throw null;
        public static float SurfaceHeight(WorldMap map, int x, int y) => throw null;
    }

    public static class MaterialLibrary
    {
        public static Material Unlit(Color c) => throw null;
    }

    public sealed class GodSimHUD : MonoBehaviour
    {
        public static bool PointerOverUI => throw null;
    }

    public sealed class TerrainRenderer : MonoBehaviour
    {
        public static TerrainRenderer Instance { get => throw null; private set => throw null; }
        public LayerMask TerrainLayer;
    }

    public sealed class BuildingRenderer : MonoBehaviour
    {
        public static BuildingRenderer Instance { get => throw null; private set => throw null; }
    }
}
