using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    /// <summary>
    /// Scratch context handed to every <see cref="WorldEventDefinition"/> delegate: the simulation context,
    /// the civilization being rolled for, and the targets resolved by the definition's selector. Reused
    /// across evaluations (owned by the system), so delegates must not keep references to it.
    /// </summary>
    public sealed class WorldEventContext
    {
        public SimContext Sim;
        public Civilization Civ;
        public WorldEventDefinition Definition;

        // ---- targets (resolved by SelectTarget / Effect)
        public Settlement Settlement;
        public Civilization OtherCiv;
        public Settlement OtherSettlement;
        public Person Person;
        public Building Building;
        public int TileIndex = -1;

        // ---- outcome values (set by Effect, used by chronicle templates)
        public float Amount;
        public int Count;
        public bool UseAltTemplates;

        /// <summary>Alive settlements of <see cref="Civ"/> (computed once per reset).</summary>
        public int SettlementCount;

        // ---- reusable buffers (avoid per-event allocations)
        internal readonly List<Person> People = new List<Person>(64);
        internal readonly List<Building> Buildings = new List<Building>(32);
        internal readonly List<int> Tiles = new List<int>(32);
        internal readonly List<ResourceType> Resources = new List<ResourceType>(8);

        public WorldState State => Sim.State;
        public WorldOps Ops => Sim.Ops;
        public GameDatabase Db => Sim.Db;
        public Rng Rng => Sim.State.Rng;
        public WorldMap Map => Sim.State.Map;
        public long Tick => Sim.State.Tick;

        /// <summary>Binds the context to a civilization and clears all targets.</summary>
        public void Reset(SimContext sim, Civilization civ)
        {
            Sim = sim;
            Civ = civ;
            Definition = null;
            SettlementCount = sim != null && sim.State != null ? StoryUtil.AliveSettlementCount(sim.State, civ) : 0;
            ClearTargets();
        }

        /// <summary>Clears targets and outcome values (called before each definition is evaluated).</summary>
        public void ClearTargets()
        {
            Settlement = null;
            OtherCiv = null;
            OtherSettlement = null;
            Person = null;
            Building = null;
            TileIndex = -1;
            Amount = 0f;
            Count = 0;
            UseAltTemplates = false;
        }
    }
}
