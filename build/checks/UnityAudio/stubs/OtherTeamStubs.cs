// Stubs for APIs owned by other teams (compile check only; never copied into Assets/).
using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public WorldState State { get; }
        public EventBus Events { get; }
    }
}

namespace GodSim.Unity
{
    public class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; private set; }
        public GodSim.Core.Composition.GodSimulation Simulation { get; }
        public event Action<GodSim.Core.Composition.GodSimulation> SimulationCreated;
        public bool Paused { get; }
        public int SpeedIndex { get; }
        private void Unused() { SimulationCreated?.Invoke(null); Instance = null; }
    }

    public static class GodSimCoords
    {
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => new Vector3(t.X + 0.5f, 0f, t.Y + 0.5f);
    }
}
