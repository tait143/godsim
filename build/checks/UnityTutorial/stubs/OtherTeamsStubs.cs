// Stubs of other teams' APIs the tutorial depends on, exactly as specified by the lead
// (compile check ONLY; the real types live in Assets/GodSim/Core/Composition and Unity/Runtime).
#pragma warning disable CS0067, CS1591
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
    }
}

namespace GodSim.Unity
{
    using GodSim.Core.Composition;

    public sealed class GodSimRunner : MonoBehaviour
    {
        public static GodSimRunner Instance { get; private set; }
        public event Action<GodSimulation> SimulationCreated;
        public GodSimulation Simulation { get; private set; }
        public bool Paused { get; set; }
        public int SpeedIndex { get; set; }
    }

    public sealed class GodPowerController : MonoBehaviour
    {
        public static GodPowerController Instance { get; private set; }
        public bool HasPower { get; }
        public GodPowerType SelectedPower { get; }
        public event Action<GodPowerType, Vec2i> CastSucceeded;
    }

    public sealed class SelectionController : MonoBehaviour
    {
        public static SelectionController Instance { get; private set; }
        public bool HasSelection { get; }
        public int SelectedSettlementId { get; }
        public event Action SelectionChanged;
    }

    public sealed class GodCameraController : MonoBehaviour
    {
        public static GodCameraController Instance { get; private set; }
        public void FocusOn(Vec2i tile) { }
    }

    public sealed class GodSimHUD : MonoBehaviour
    {
        public static bool PointerOverUI { get; }
    }
}
