// Stand-ins for APIs owned by other teams, exactly as listed in the UnityTerrain task brief.
// Compile-only; NEVER copy into Assets/.
using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Core.Composition
{
    /// <summary>Stub of the lead's simulation facade.</summary>
    public sealed class GodSimulation
    {
        public WorldState State { get; }
        public GameDatabase Db { get; }
        public EventBus Events { get; }
    }
}

namespace GodSim.Unity
{
    /// <summary>Stub of the Unity Core team's settings object.</summary>
    [Serializable]
    public sealed class GodSimSettings
    {
        public bool ShowTerritory = true;
        public float HeightScale = 1f;
    }

    /// <summary>Stub of the Unity Core team's runner.</summary>
    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; }
        public GodSimSettings Settings;
        public GodSimulation Simulation { get; }
        public event Action<GodSimulation> SimulationCreated;
        public event Action Ticked;

        private void Touch() { SimulationCreated?.Invoke(null); Ticked?.Invoke(); }
    }

    /// <summary>Stub of the Unity Core team's tile/world coordinate helpers.</summary>
    public static class GodSimCoords
    {
        public const float TileSize = 1f;
        public static float HeightScale;
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => default(Vector3);
        public static float SurfaceHeight(WorldMap map, int x, int y) => 0f;
    }

    /// <summary>Stub of the Unity Core team's palette.</summary>
    public static class GodSimPalette
    {
        public static Color Biome(Biome b) => default(Color);
        public static Color FromRgba(uint rgba) => default(Color);
    }

    /// <summary>Stub of the Unity Core team's material library.</summary>
    public static class MaterialLibrary
    {
        public static Material VertexColorOpaque { get; }
        public static Material VertexColorInstanced { get; }
        public static Material Water { get; }
    }
}
