namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Establishes diplomatic contact. Two alive civilizations gain contact when any of their alive
    /// settlements lie within <see cref="RelationsTuning.ContactRange"/> tiles (Chebyshev distance between
    /// settlement centers). While in range, <see cref="Relation.LastContactTick"/> is refreshed every run.
    /// First contact is written to the chronicle. Contact, once made, is never lost.
    /// Work is bounded: only settlement centers are compared (with per-civ bounding-box rejection),
    /// never tiles.
    /// </summary>
    public sealed class ContactSystem : ISimSystem
    {
        private readonly RelationsSettlementIndex _index = new RelationsSettlementIndex();

        /// <summary>Creates the system.</summary>
        public ContactSystem() { }

        /// <inheritdoc />
        public string Name => "Diplomacy.Contact";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            _index.Rebuild(state);
            var civs = _index.CivIds;
            long tick = state.Tick;
            for (int i = 0; i < civs.Count; i++)
            {
                for (int j = i + 1; j < civs.Count; j++)
                {
                    int a = civs[i], b = civs[j];
                    if (a == b || !_index.AnyWithin(a, b, RelationsTuning.ContactRange)) continue;
                    var r = state.Diplomacy.Get(a, b);
                    bool first = !r.HasContact;
                    r.HasContact = true;
                    r.LastContactTick = tick;
                    if (first) ChronicleFirstContact(ctx, a, b);
                }
            }
        }

        private void ChronicleFirstContact(SimContext ctx, int a, int b)
        {
            if (ctx.Ops == null) return;
            var ca = ctx.State.Civilizations.Get(a);
            var cb = ctx.State.Civilizations.Get(b);
            if (ca == null || cb == null) return;
            int lo = ca.Id < cb.Id ? ca.Id : cb.Id;
            ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy,
                "The " + ca.Name + " and the " + cb.Name + " made first contact.",
                lo, -1, _index.ClosestMidpoint(a, b), 1);
        }
    }
}
