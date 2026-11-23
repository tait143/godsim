using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Headless
{
    /// <summary>
    /// Scripted god-power casts spread over the run (--powers-demo). Blessings target capitals; shaping and
    /// minor wrath target unclaimed land near a capital so the demo exercises the powers without wiping towns.
    /// Uses no randomness: targets are derived from state, so runs stay deterministic for a given seed.
    /// </summary>
    public sealed class PowersDemo
    {
        private enum TargetKind { Capital, NearbyWilds }

        private struct Planned
        {
            public long Tick;
            public GodPowerType Power;
            public TargetKind Kind;
        }

        private static readonly GodPowerType[] Script =
        {
            GodPowerType.Bless, GodPowerType.Rain, GodPowerType.RaiseLand, GodPowerType.PlantForest,
            GodPowerType.Heal, GodPowerType.BountifulHarvest, GodPowerType.RevealOre, GodPowerType.Fertility,
            GodPowerType.Lightning, GodPowerType.Inspire, GodPowerType.LowerLand, GodPowerType.Wildfire,
        };

        private readonly List<Planned> _plan = new List<Planned>();
        private readonly bool _verbose;
        private int _next;

        public int Attempted { get; private set; }
        public int Accepted { get; private set; }
        public int Executed { get; private set; }
        public int ExecutionFailures { get; private set; }

        public PowersDemo(long startTick, long totalTicks, bool verbose)
        {
            _verbose = verbose;
            if (totalTicks < 1) return;
            int count = Script.Length;
            long gap = Math.Max(1, totalTicks / (count + 1));
            for (int i = 0; i < count; i++)
            {
                long t = startTick + gap * (i + 1);
                if (t > startTick + totalTicks) break;
                var p = Script[i];
                _plan.Add(new Planned
                {
                    Tick = t,
                    Power = p,
                    Kind = IsBlessing(p) ? TargetKind.Capital : TargetKind.NearbyWilds,
                });
            }
        }

        /// <summary>Hooks execution feedback from the divine module.</summary>
        public void Attach(EventBus events)
        {
            if (events == null) return;
            events.Subscribe<GodPowerCastEvent>(OnCast);
            events.Subscribe<GodCommandRejectedEvent>(OnRejected);
        }

        private void OnCast(GodPowerCastEvent e)
        {
            if (e.Success) Executed++; else ExecutionFailures++;
            if (_verbose)
                Console.WriteLine("  [power] " + e.Power + " at " + e.Target + " r" + e.Radius + " -> " + (e.Success ? "executed" : "failed") +
                                  (string.IsNullOrEmpty(e.Message) ? "" : ": " + e.Message));
        }

        private void OnRejected(GodCommandRejectedEvent e)
        {
            ExecutionFailures++;
            if (_verbose) Console.WriteLine("  [power] " + e.Power + " at " + e.Target + " rejected at execution: " + e.Reason);
        }

        /// <summary>Call before each Step(); casts every power scheduled for the upcoming tick.</summary>
        public void BeforeStep(GodSimulation sim)
        {
            while (_next < _plan.Count && _plan[_next].Tick <= sim.Tick + 1)
            {
                var p = _plan[_next++];
                Cast(sim, p);
            }
        }

        private void Cast(GodSimulation sim, Planned p)
        {
            Attempted++;
            var s = sim.State;
            var capital = PickCapital(s, Attempted);
            if (capital == null)
            {
                if (_verbose) Console.WriteLine("  [power] " + p.Power + " skipped: no living settlement to target");
                return;
            }
            Vec2i target = p.Kind == TargetKind.Capital ? capital.Center : FindWilds(s.Map, capital.Center);
            int radius = PickRadius(sim.Db, p.Power);
            string error;
            bool ok;
            try { ok = sim.TryCastPower(p.Power, target, radius, 0, out error); }
            catch (Exception ex) { ok = false; error = "threw " + ex.GetType().Name + ": " + ex.Message; }
            if (ok) Accepted++;
            if (_verbose)
                Console.WriteLine("  [power] Year " + s.Year + ": " + p.Power + " r" + radius + " at " + target + " near " + capital.Name +
                                  (ok ? " -> accepted" : " -> refused: " + (error ?? "(no reason)")));
        }

        private static bool IsBlessing(GodPowerType p) =>
            p == GodPowerType.Bless || p == GodPowerType.Rain || p == GodPowerType.Heal || p == GodPowerType.BountifulHarvest ||
            p == GodPowerType.Fertility || p == GodPowerType.Inspire || p == GodPowerType.Prophet;

        private static Settlement PickCapital(WorldState s, int salt)
        {
            var caps = new List<Settlement>();
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive && items[i].IsCapital) caps.Add(items[i]);
            if (caps.Count == 0) for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) caps.Add(items[i]);
            if (caps.Count == 0) return null;
            caps.Sort((a, b) => a.Id.CompareTo(b.Id));
            return caps[salt % caps.Count];
        }

        /// <summary>Nearest unowned, building-free land tile 10..30 tiles from the center (falls back to the center).</summary>
        private static Vec2i FindWilds(WorldMap map, Vec2i center)
        {
            for (int r = 10; r <= 30; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = center.X + dx, y = center.Y + dy;
                        if (!map.InBounds(x, y)) continue;
                        int i = map.Index(x, y);
                        if (map.Biomes[i].IsLand() && map.Owner[i] < 0 && map.BuildingAt[i] < 0) return new Vec2i(x, y);
                    }
            return map.ClampToBounds(center);
        }

        private static int PickRadius(GameDatabase db, GodPowerType p)
        {
            int idx = (int)p;
            var defs = db != null ? db.GodPowers : null;
            var def = defs != null && idx >= 0 && idx < defs.Count ? defs[idx] : null;
            if (def == null) return 2;
            int r = def.MinRadius + 1;
            return r < def.MinRadius ? def.MinRadius : (r > def.MaxRadius ? def.MaxRadius : r);
        }
    }
}
