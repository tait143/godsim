using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Diplomacy.Relations
{
    /// <summary>Small builders shared by the diplomacy relation tests.</summary>
    internal static class RelationsTestKit
    {
        /// <summary>A stub race with neutral personality (same defaults as TestDb stubs).</summary>
        public static RaceDefinition Race(RaceId id)
        {
            int i = (int)id;
            var r = new RaceDefinition { Id = id, Name = id.ToString(), PluralName = id + "s", Adjective = id.ToString() };
            for (int b = 0; b < Counts.Biomes; b++) r.BiomeHabitability[b] = ((Biome)b).IsWater() ? 0f : 0.6f;
            for (int a = 0; a < Counts.Races; a++) r.RaceAffinity[a] = a == i ? 1f : 0f;
            return r;
        }

        /// <summary>Builds and freezes a database whose race definitions may be tuned by <paramref name="configure"/>.</summary>
        public static GameDatabase Db(Action<RaceDefinition[]> configure = null)
        {
            var races = new RaceDefinition[Counts.Races];
            for (int i = 0; i < races.Length; i++) races[i] = Race((RaceId)i);
            configure?.Invoke(races);
            var db = new GameDatabase();
            foreach (var r in races) db.Register(r);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Two races that loathe each other (mutual affinity -1, full xenophobia, no diplomacy).</summary>
        public static void Hate(RaceDefinition[] races, RaceId a, RaceId b)
        {
            races[(int)a].RaceAffinity[(int)b] = -1f;
            races[(int)b].RaceAffinity[(int)a] = -1f;
            races[(int)a].Xenophobia = 1f;
            races[(int)b].Xenophobia = 1f;
            races[(int)a].Diplomacy = 0f;
            races[(int)b].Diplomacy = 0f;
        }

        /// <summary>Two races that like each other (mutual affinity +0.9, no xenophobia, diplomatic).</summary>
        public static void Like(RaceDefinition[] races, RaceId a, RaceId b)
        {
            races[(int)a].RaceAffinity[(int)b] = 0.9f;
            races[(int)b].RaceAffinity[(int)a] = 0.9f;
            races[(int)a].Xenophobia = 0f;
            races[(int)b].Xenophobia = 0f;
            races[(int)a].Diplomacy = 0.8f;
            races[(int)b].Diplomacy = 0.8f;
        }

        public static SimContext Ctx(GameDatabase db, ulong seed = 7) => TestWorlds.CreateContext(db, 128, seed);

        /// <summary>A civ with one settlement, at the given age and military power.</summary>
        public static Civilization Civ(SimContext ctx, RaceId race, Vec2i at, AgeId age = AgeId.Bronze, float power = 10f)
        {
            TestWorlds.AddSettlement(ctx, race, at, 4, out var civ);
            civ.Age = age;
            civ.MilitaryPower = power;
            return civ;
        }

        public static Settlement Capital(SimContext ctx, Civilization civ) => ctx.State.Settlements.Get(civ.CapitalId);

        /// <summary>Creates (or gets) a relation already in contact with the given opinion and stance.</summary>
        public static Relation Contact(SimContext ctx, Civilization a, Civilization b, float opinion, DiplomaticStance stance = DiplomaticStance.Neutral)
        {
            var r = ctx.State.Diplomacy.Get(a.Id, b.Id);
            r.HasContact = true;
            r.LastContactTick = ctx.State.Tick;
            r.Opinion = opinion;
            r.Stance = stance;
            return r;
        }

        /// <summary>Starts a war by hand (as the military/other systems would see it).</summary>
        public static War StartWar(SimContext ctx, Civilization attacker, Civilization defender, long startTick = 0)
        {
            var w = new War { Id = ctx.State.Ids.War(), AttackerId = attacker.Id, DefenderId = defender.Id, StartTick = startTick, CasusBelli = "test" };
            ctx.State.Diplomacy.Wars.Add(w);
            var r = ctx.State.Diplomacy.Get(attacker.Id, defender.Id);
            r.HasContact = true;
            r.WarId = w.Id;
            r.Stance = DiplomaticStance.War;
            return w;
        }

        /// <summary>Runs a system tick by tick until the condition holds (or maxTicks pass). Returns the condition.</summary>
        public static bool RunUntil(ISimSystem system, SimContext ctx, Func<bool> done, int maxTicks, bool initialize = true)
        {
            if (initialize) { ctx.DeltaTicks = system.TickInterval; system.Initialize(ctx); ctx.Ops.FlushRemovals(); }
            for (int t = 0; t < maxTicks && !done(); t++) TestWorlds.Run(system, ctx, 1, false);
            return done();
        }

        public static List<ISimSystem> AllSystems() =>
            new List<ISimSystem> { new ContactSystem(), new RelationsSystem(), new WarDecisionSystem() };

        public static int CountChronicle(SimContext ctx, string fragment)
        {
            int n = 0;
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Text != null && e.Text.Contains(fragment)) n++;
            return n;
        }

        public static int ActiveWars(SimContext ctx)
        {
            int n = 0;
            foreach (var w in ctx.State.Diplomacy.Wars) if (w.IsActive) n++;
            return n;
        }
    }
}
