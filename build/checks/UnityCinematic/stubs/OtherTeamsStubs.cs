// Stubs of other teams' APIs used by the cinematic layer, for the UnityCinematic compile check ONLY.
// Members mirror the documented signatures of GodSimRunner, GodSimCoords, GodCameraController,
// InputAdapter, GodSimToasts (GodSim.Unity) and GodSimulation (GodSim.Core.Composition).
#pragma warning disable CS0067, CS1591
using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public WorldState State { get; }
        public EventBus Events { get; }
        public GameDatabase Db { get; }
    }
}

namespace GodSim.Unity
{
    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; private set; }
        public GodSimulation Simulation { get; private set; }
        public event Action<GodSimulation> SimulationCreated;
        public bool Paused { get; set; }
    }

    public static class GodSimCoords
    {
        public static Vector3 TileToWorld(WorldMap map, Vec2i t) => default;
        public static float SurfaceHeight(WorldMap map, int x, int y) => 0f;
    }

    public sealed class GodCameraController : MonoBehaviour
    {
        public static GodCameraController Instance { get; private set; }
        public Camera Camera { get; }
        public float MinHeight;
        public float MaxHeight;
        public void FocusOn(Vector3 world) { }
    }

    public static class InputAdapter
    {
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetKey(KeyCode k) => false;
        public static Vector2 MousePosition => default;
        public static bool MouseDown(int button) => false;
        public static bool MouseHeld(int button) => false;
        public static float ScrollDelta => 0f;
        public static bool PointerOverUI => false;
    }

    public static class GodSimToasts
    {
        public static void Show(string message, Color color, float seconds = 4f) { }
    }
}
