using System;

namespace GodSim.Core.Races
{
    /// <summary>
    /// Races module entry point (contract section 6): registers the five <see cref="RaceDefinition"/>s
    /// into the shared <see cref="GameDatabase"/> before it is frozen. Registration order is the fixed
    /// <see cref="RaceId"/> order, so the result is deterministic and needs no reflection or dictionaries.
    /// </summary>
    public static class RaceCatalog
    {
        /// <summary>
        /// Registers Human, Elf, Dwarf, Orc and Saurian (replacing any definitions already in those slots).
        /// Throws <see cref="ArgumentNullException"/> for a null database and, through
        /// <see cref="GameDatabase.Register(RaceDefinition)"/>, <see cref="InvalidOperationException"/> if it is frozen.
        /// </summary>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            if (db.IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; register races before Freeze().");
            var all = CreateAll();
            for (int i = 0; i < all.Length; i++) db.Register(all[i]);
        }

        /// <summary>
        /// Creates fresh definitions for every race, indexed by (int)<see cref="RaceId"/>. Each call returns new
        /// instances (no shared arrays), so callers may tweak them before registering without side effects.
        /// </summary>
        public static RaceDefinition[] CreateAll()
        {
            var all = new RaceDefinition[Counts.Races];
            for (int i = 0; i < all.Length; i++) all[i] = Create((RaceId)i);
            return all;
        }

        /// <summary>Creates a fresh definition for one race. Throws <see cref="ArgumentOutOfRangeException"/> for an unknown id.</summary>
        public static RaceDefinition Create(RaceId race)
        {
            switch (race)
            {
                case RaceId.Human: return HumanRace.Create();
                case RaceId.Elf: return ElfRace.Create();
                case RaceId.Dwarf: return DwarfRace.Create();
                case RaceId.Orc: return OrcRace.Create();
                case RaceId.Saurian: return SaurianRace.Create();
                default: throw new ArgumentOutOfRangeException(nameof(race), race, "No definition for race " + (int)race + ".");
            }
        }
    }
}
