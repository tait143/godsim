namespace GodSim.Core.AI
{
    /// <summary>Result of evaluating one civic role for a settlement.</summary>
    public struct UrbanRoleStatus
    {
        /// <summary>The role matters now (it is satisfied, or something can be built for it).</summary>
        public bool Applicable;
        /// <summary>The settlement already has (or has queued) a building filling the role.</summary>
        public bool Satisfied;
        /// <summary>A buildable building was found for the role (<see cref="Building"/>).</summary>
        public bool Resolved;
        /// <summary>Best available building for the role (valid when <see cref="Resolved"/>).</summary>
        public BuildingType Building;
    }

    /// <summary>
    /// Resolves functional roles to the best AVAILABLE building for a civ's age and race: race-favored candidates first,
    /// then preference order, then a data-driven fallback (category + service + age). Every candidate is gated through
    /// <see cref="GameDatabase.IsBuildingAvailable"/>, tier/population minimums, per-settlement caps, civ-wide wonder
    /// uniqueness and terrain placement, so the advisor never votes for something that cannot be built.
    /// </summary>
    public static class UrbanRoleResolver
    {
        /// <summary>Ungated (age/race availability only) resolution, e.g. for UI and tests.</summary>
        public static bool TryResolve(GameDatabase db, Civilization civ, UrbanRole role, out BuildingType type)
        {
            var st = Evaluate(db, civ, role, null, null, null, null);
            type = st.Building;
            return st.Resolved;
        }

        /// <summary>
        /// Evaluates a role. Optional context narrows the result: <paramref name="s"/> (tier/population gates),
        /// <paramref name="comp"/> (what exists, caps), <paramref name="site"/> (placement), <paramref name="state"/> (wonders).
        /// </summary>
        public static UrbanRoleStatus Evaluate(GameDatabase db, Civilization civ, UrbanRole role, Settlement s, UrbanComposition comp, UrbanSiteSurvey site, WorldState state)
        {
            var st = new UrbanRoleStatus();
            if (db == null || civ == null) return st;
            var spec = UrbanRoleCatalog.Get(role);
            var race = RaceOf(db, civ.Race);
            var cands = spec.CandidatesFor(civ.Race);
            if (cands != null)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < cands.Length; i++)
                    {
                        var t = cands[i];
                        if ((pass == 0) != IsFavored(race, t)) continue;
                        bool present = comp != null && comp.Present(t) > 0;
                        // Upgrade roles: only candidates ranked at or above the resolved one satisfy the role.
                        if (present && (!spec.Upgrade || !st.Resolved)) st.Satisfied = true;
                        if (!st.Resolved && db.TryGetBuilding(t, out var def) && Passes(db, civ, def, s, comp, site, state))
                        {
                            st.Resolved = true;
                            st.Building = t;
                        }
                    }
                }
            }

            if (spec.HasFallback)
            {
                int best = -1;
                AgeId bestAge = AgeId.Dawn;
                bool resolvedByCandidates = st.Resolved;
                for (int t = 0; t < Counts.Buildings; t++)
                {
                    if (!db.TryGetBuilding((BuildingType)t, out var def) || !spec.MatchesFallback(def)) continue;
                    if (comp != null && comp.Present((BuildingType)t) > 0 && (!spec.Upgrade || !resolvedByCandidates)) st.Satisfied = true;
                    if (resolvedByCandidates || def.IsWonder || def.UniquePerCivilization) continue;
                    if (!Passes(db, civ, def, s, comp, site, state)) continue;
                    if (best < 0 || def.MinAge > bestAge) { best = t; bestAge = def.MinAge; }
                }
                if (!st.Resolved && best >= 0) { st.Resolved = true; st.Building = (BuildingType)best; }
            }

            st.Applicable = st.Satisfied || st.Resolved;
            return st;
        }

        /// <summary>Full buildability gate used for every vote (null context arguments skip that check).</summary>
        public static bool Passes(GameDatabase db, Civilization civ, BuildingDefinition def, Settlement s, UrbanComposition comp, UrbanSiteSurvey site, WorldState state)
        {
            if (db == null || civ == null || def == null) return false;
            if (!db.IsBuildingAvailable(civ, def.Type)) return false;
            if (s != null)
            {
                if (def.MinTier > s.Tier) return false;
                if (def.MinPopulation > s.Population) return false;
            }
            int present = comp != null ? comp.Present(def.Type) : 0;
            if (def.MaxPerSettlement >= 0 && present >= def.MaxPerSettlement) return false;
            if (site != null && !site.Allows(def, civ.Race)) return false;
            if ((def.UniquePerCivilization || def.IsWonder) && state != null && present == 0 && CivHas(state, civ, def.Type)) return false;
            return true;
        }

        /// <summary>True if any settlement of the civ has (or has queued) a building of the type.</summary>
        public static bool CivHas(WorldState state, Civilization civ, BuildingType type)
        {
            if (state == null || civ == null) return false;
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var s = state.Settlements.Get(sids[i]);
                if (s == null || !s.IsAlive) continue;
                var ids = s.BuildingIds;
                for (int j = 0; j < ids.Count; j++)
                {
                    var b = state.Buildings.Get(ids[j]);
                    if (b != null && !b.IsDestroyed && b.Type == type) return true;
                }
                var q = s.BuildQueue;
                for (int j = 0; j < q.Count; j++)
                    if (q[j] != null && q[j].Type == type) return true;
            }
            return false;
        }

        /// <summary>True if the race favors the type (FavoredBuildings) or owns it (UniqueBuildings).</summary>
        public static bool IsFavored(RaceDefinition race, BuildingType t)
        {
            if (race == null) return false;
            var f = race.FavoredBuildings;
            if (f != null) for (int i = 0; i < f.Length; i++) if (f[i] == t) return true;
            var u = race.UniqueBuildings;
            if (u != null) for (int i = 0; i < u.Length; i++) if (u[i] == t) return true;
            return false;
        }

        public static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            if (db == null) return null;
            int r = (int)race;
            return r >= 0 && r < db.Races.Count ? db.Races[r] : null;
        }
    }
}
