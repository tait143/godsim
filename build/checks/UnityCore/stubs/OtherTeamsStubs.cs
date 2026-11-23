// Stubs of other teams' DOCUMENTED APIs (compile checks only; never copy into Assets/).
// GodSim.Core.Composition.GodSimulation (lead) and GodSim.Core.Persistence.* (persistence team).
#pragma warning disable CS1591
using System;
using System.Collections.Generic;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public SimConfig Config { get; }
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public EventBus Events { get; }
        public SimulationEngine Engine { get; }
        public CommandQueue Commands { get; }
        public WorldOps Ops { get; }
        public ISimLogger Log { get; }
        public long Tick { get; }
        public static GameDatabase BuildDatabase() => throw new NotImplementedException();
        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null) => throw new NotImplementedException();
        public static GodSimulation Load(byte[] saveData, SimConfig config, ISimLogger log = null, byte[] hmacKey = null) => throw new NotImplementedException();
        public byte[] Save(byte[] hmacKey = null) => throw new NotImplementedException();
        public ulong ComputeHash() => 0;
        public void Step() { }
        public void StepMany(int ticks) { }
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
        public SaveFileStore(string rootDirectory, long maxFileBytes = 128L * 1024 * 1024, int backups = 3) { }
        public bool TryWrite(string slot, byte[] data, out string error) { error = null; return true; }
        public bool TryRead(string slot, out byte[] data, out string error) { data = null; error = null; return false; }
        public bool Delete(string slot) => false;
        public bool Exists(string slot) => false;
        public IReadOnlyList<SaveSlotInfo> List() => new SaveSlotInfo[0];
        public static bool IsValidSlotName(string slot) => !string.IsNullOrEmpty(slot);
    }

    public interface IKeyProvider
    {
        byte[] GetKey();
    }

    public static class InstallKeyFile
    {
        public static IKeyProvider LoadOrCreate(string directory) => throw new NotImplementedException();
    }
}
