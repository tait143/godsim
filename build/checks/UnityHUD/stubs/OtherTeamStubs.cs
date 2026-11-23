// Stubs of the other teams' APIs consumed by the HUD (compile check only). Signatures follow the
// published interfaces (GodSimRunner, SelectionController, GodPowerController, GodCameraController,
// TerrainRenderer, GameShortcuts, GodSimulation, SaveSlotInfo, SaveFileStore).
using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Core.Persistence;
using UnityEngine;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public SimConfig Config { get; }
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public EventBus Events { get; }
        public SimulationEngine Engine { get; }
        public long Tick => State.Tick;
        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error) { error = null; return false; }
    }
}

namespace GodSim.Core.Persistence
{
    public sealed class SaveSlotInfo
    {
        public string Slot;
        public long SizeBytes;
        public DateTime LastWriteUtc;
    }

    public sealed class SaveFileStore
    {
        public static bool IsValidSlotName(string slot) => !string.IsNullOrEmpty(slot);
    }
}

namespace GodSim.Unity
{
    public sealed class GodSimSettings : ScriptableObject { }

    public sealed class GodSimRunner : MonoBehaviour
    {
        public static readonly float[] SpeedLevels = { 1f, 4f, 16f, 64f, 240f };
        public static GodSimRunner Instance { get; private set; }
        public GodSimSettings Settings;
        public event Action<GodSimulation> SimulationCreated;
        public event Action Ticked;
        public GodSimulation Simulation { get; private set; }
        public bool Paused { get; set; }
        public int SpeedIndex { get; set; }
        public float TicksPerSecond => 0f;
        public void NewWorld(SimConfig config) { SimulationCreated?.Invoke(null); Ticked?.Invoke(); }
        public bool SaveGame(string slot, out string error) { error = null; return true; }
        public bool LoadGame(string slot, out string error) { error = null; return true; }
        public IReadOnlyList<SaveSlotInfo> ListSaves() => new List<SaveSlotInfo>();
    }

    public sealed class SelectionController : MonoBehaviour
    {
        public static SelectionController Instance { get; private set; }
        public event Action SelectionChanged;
        public bool HasSelection => false;
        public Vec2i SelectedTile => Vec2i.Invalid;
        public int SelectedSettlementId => -1;
        public int SelectedBuildingId => -1;
        public int SelectedArmyId => -1;
        public void SelectSettlement(int id) { SelectionChanged?.Invoke(); }
        public void Clear() { }
    }

    public sealed class GodPowerController : MonoBehaviour
    {
        public static GodPowerController Instance { get; private set; }
        public event Action<GodPowerType, Vec2i> CastSucceeded;
        public event Action<string> CastFailed;
        public bool HasPower => false;
        public GodPowerType SelectedPower => GodPowerType.Bless;
        public int Radius { get; set; }
        public RaceId SpawnRace { get; set; }
        public void SelectPower(GodPowerType p) { CastSucceeded?.Invoke(p, Vec2i.Zero); CastFailed?.Invoke(""); }
        public void ClearPower() { }
    }

    public sealed class GodCameraController : MonoBehaviour
    {
        public static GodCameraController Instance { get; private set; }
        public void FocusOn(Vec2i tile) { }
    }

    public sealed class TerrainRenderer : MonoBehaviour
    {
        public static TerrainRenderer Instance { get; private set; }
        public bool ShowTerritory = true;
        public Texture2D Minimap => null;
    }

    public sealed class GameShortcuts : MonoBehaviour
    {
        public static GameShortcuts Instance { get; private set; }
    }
}
