using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenario 6 (WarAndConquest) and the mid-war half of scenario 12. Five races on a small Pangaea (every civ
    /// reachable over land) with warfare on and faster growth, for 150 years. The first moment a war is active
    /// while a building is under construction, the world is saved; the straight run continues and its hash two
    /// years later is compared against a run resumed from that save.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class WarfareScenarioTests
    {
        private const ulong Seed = 31337UL;
        private const int Years = 150;
        private const int MaxWarYears = 30;
        private const int ResumeTicks = 2 * SimTime.TicksPerYear;

        private SimConfig _cfg;
        private ScenarioHarness _h;
        private readonly List<string> _staleWars = new List<string>();

        // mid-war save point
        private byte[] _save;
        private long _saveTick = -1;
        private ulong _hashAtSave, _hashAfterResume;
        private bool _saveWasMidWar;
        private string _saveError;

        [OneTimeSetUp]
        public void RunWorld()
        {
            _cfg = ScenarioWorlds.SmallWorld(Seed);
            _cfg.Preset = WorldPreset.Pangaea;
            _cfg.EnableWarfare = true;
            _cfg.GrowthMultiplier = 1.5f;
            var log = new CollectingLogger();
            _h = new ScenarioHarness(ScenarioWorlds.Create(_cfg, log), log);
            long maxWarTicks = SimTime.YearsToTicks(MaxWarYears);
            _h.OnSample = x =>
            {
                foreach (var w in x.State.Diplomacy.Wars)
                    if (w.IsActive && x.Now - w.StartTick > maxWarTicks && _staleWars.Count < 50)
                        _staleWars.Add("war " + w.Id + " (" + w.AttackerId + " vs " + w.DefenderId + ") active for " + SimTime.TicksToYears(x.Now - w.StartTick).ToString("0.0") + " years at " + SimTime.Format(x.Now));
            };
            _h.AfterStep = x =>
            {
                if (_saveTick >= 0 || _saveError != null) return;
                bool midWar = ScenarioWorlds.ActiveWarCount(x.State) > 0;
                bool lastChance = x.Now >= SimTime.YearsToTicks(Years) - ResumeTicks - 1;
                if ((midWar && HasConstructionSite(x.State)) || lastChance) TakeSave(x, midWar);
            };
            _h.RunYears(Years);
            TestContext.Progress.WriteLine("[Warfare] seed " + Seed + "\n" + _h.Summary() + "\n" + DescribeWars());
        }

        private void TakeSave(ScenarioHarness x, bool midWar)
        {
            try
            {
                _hashAtSave = x.Sim.ComputeHash();
                _save = x.Sim.Save();
                _saveTick = x.Now;
                _saveWasMidWar = midWar;
                long resumeAt = _saveTick + ResumeTicks;
                x.AfterStep = y => { if (y.Now == resumeAt) _hashAfterResume = y.Sim.ComputeHash(); };
            }
            catch (Exception ex) { _saveError = ex.ToString(); }
        }

        private static bool HasConstructionSite(WorldState s)
        {
            var b = s.Buildings.Items;
            for (int i = 0; i < b.Count; i++) if (!b[i].IsDestroyed && !b[i].IsComplete) return true;
            return false;
        }

        [OneTimeTearDown]
        public void TearDown() { _h?.Dispose(); }

        [Test]
        public void WarAndConquest_WarsEndWithin30YearsAndBattlesHappen()
        {
            long maxWarTicks = SimTime.YearsToTicks(MaxWarYears);
            if (_h.WarsDeclared.Count == 0)
            {
                Assert.Warn("No war was declared in " + Years + " years with warfare enabled on a small Pangaea.\n" + _h.Summary());
                return;
            }
            var failures = new List<string>();
            foreach (var wd in _h.WarsDeclared)
            {
                long end = -1;
                foreach (var p in _h.PeacesSigned) if (p.Event.WarId == wd.Event.WarId) { end = p.Tick; break; }
                var war = _h.State.Diplomacy.GetWar(wd.Event.WarId);
                if (end < 0 && war != null && !war.IsActive) end = war.EndTick;
                long duration = (end >= 0 ? end : _h.Now) - wd.Tick;
                if (duration > maxWarTicks)
                    failures.Add("war " + wd.Event.WarId + " (" + wd.Event.AttackerId + " vs " + wd.Event.DefenderId + ") declared " + SimTime.Format(wd.Tick) +
                                 (end >= 0 ? " lasted " : " still running after ") + SimTime.TicksToYears(duration).ToString("0.0") + " years");
            }
            foreach (var w in _h.State.Diplomacy.Wars)
                if (!w.IsActive && w.EndTick >= 0 && w.EndTick - w.StartTick > maxWarTicks)
                    failures.Add("war record " + w.Id + " lasted " + SimTime.TicksToYears(w.EndTick - w.StartTick).ToString("0.0") + " years");
            failures.AddRange(_staleWars);
            Assert.That(failures, Is.Empty, "Wars must end within " + MaxWarYears + " years:\n" + ScenarioWorlds.Join(failures) + DescribeWars());
            Assert.That(_h.Battles.Count, Is.GreaterThan(0), _h.WarsDeclared.Count + " wars were declared but no BattleEvent occurred.\n" + DescribeWars());
            Assert.That(_h.BattleViolations, Is.Empty, "Battle/war event inconsistencies:\n" + ScenarioWorlds.Join(_h.BattleViolations));
        }

        [Test]
        public void WarAndConquest_EveryPeaceClosesItsWar()
        {
            var failures = new List<string>();
            foreach (var p in _h.PeacesSigned)
            {
                bool declared = false;
                foreach (var wd in _h.WarsDeclared) if (wd.Event.WarId == p.Event.WarId) { declared = true; if (p.Tick < wd.Tick) failures.Add("peace for war " + p.Event.WarId + " precedes its declaration"); break; }
                if (!declared && p.Event.WarId >= 0) failures.Add("peace signed for war " + p.Event.WarId + " that was never declared (since harness start)");
                var war = _h.State.Diplomacy.GetWar(p.Event.WarId);
                if (war != null && war.IsActive) failures.Add("war " + war.Id + " is still active after its PeaceSignedEvent at " + SimTime.Format(p.Tick));
                if (war != null && !war.IsActive && war.EndTick < 0) failures.Add("war " + war.Id + " ended without an EndTick");
                if (_h.State.Diplomacy.TryGet(p.Event.AttackerId, p.Event.DefenderId, out var rel) && rel.WarId == p.Event.WarId)
                    failures.Add("relation " + rel.CivA + "-" + rel.CivB + " still points at ended war " + p.Event.WarId);
            }
            Assert.That(failures, Is.Empty, ScenarioWorlds.Join(failures));
        }

        [Test]
        public void WarAndConquest_CapturesTransferEveryResidentAndBuilding()
        {
            Assert.That(_h.CaptureViolations, Is.Empty, "Conquest left inconsistent ownership:\n" + ScenarioWorlds.Join(_h.CaptureViolations));
            // Independently of captures: every settlement's people and buildings belong to its owner at the end.
            var bad = new List<string>();
            foreach (var st in ScenarioWorlds.AliveSettlements(_h.State))
            {
                foreach (int pid in st.ResidentIds)
                {
                    var p = _h.State.People.Get(pid);
                    if (p != null && p.CivilizationId != st.CivilizationId) { bad.Add("settlement " + st.Id + " resident " + pid + " has civ " + p.CivilizationId + " != " + st.CivilizationId); break; }
                }
                foreach (int bid in st.BuildingIds)
                {
                    var b = _h.State.Buildings.Get(bid);
                    if (b != null && b.CivilizationId != st.CivilizationId) { bad.Add("settlement " + st.Id + " building " + bid + " has civ " + b.CivilizationId + " != " + st.CivilizationId); break; }
                }
            }
            Assert.That(bad, Is.Empty, ScenarioWorlds.Join(bad));
            TestContext.WriteLine(_h.Captures.Count + " captures, " + _h.SettlementsDestroyed.Count + " settlements destroyed, " + _h.CivsCollapsed.Count + " civs collapsed.");
        }

        [Test]
        public void SaveLoad_MidWarAndMidConstruction_HashesMatchStraightRun()
        {
            Assert.That(_saveError, Is.Null, "Save() threw during the straight run: " + _saveError);
            Assert.That(_save, Is.Not.Null, "No save point was taken.");
            Assert.That(_hashAfterResume, Is.Not.EqualTo(0UL), "Straight run did not reach save tick + " + ResumeTicks + ".");
            if (!_saveWasMidWar) Assert.Warn("No war was active at any point with a construction site; saved at " + SimTime.Format(_saveTick) + " instead (mid-construction only).");

            var loaded = GodSimulation.Load(_save, _cfg.Clone().Sanitize(), new CollectingLogger());
            Assert.That(loaded.State.Tick, Is.EqualTo(_saveTick), "Loaded tick differs.");
            Assert.That(loaded.ComputeHash(), Is.EqualTo(_hashAtSave), "Hash right after Load differs from the hash at Save.");
            for (int i = 0; i < ResumeTicks; i++) loaded.Step();
            Assert.That(loaded.ComputeHash(), Is.EqualTo(_hashAfterResume),
                "Resumed run diverged from the straight run within " + ResumeTicks + " ticks after a save at " + SimTime.Format(_saveTick) +
                " (midWar=" + _saveWasMidWar + "). Profiles of loaded run: " + ScenarioWorlds.DescribeProfiles(loaded.Engine));
        }

        private string DescribeWars()
        {
            var sb = new StringBuilder();
            sb.Append("wars declared=").Append(_h.WarsDeclared.Count).Append(" peaces=").Append(_h.PeacesSigned.Count)
              .Append(" battles=").Append(_h.Battles.Count).Append(" captures=").Append(_h.Captures.Count).AppendLine();
            int shown = 0;
            foreach (var w in _h.State.Diplomacy.Wars)
            {
                if (shown++ >= 15) break;
                sb.Append("  war ").Append(w.Id).Append(": ").Append(w.AttackerId).Append(" vs ").Append(w.DefenderId)
                  .Append(" from ").Append(SimTime.Format(w.StartTick)).Append(w.IsActive ? " (active)" : " to " + SimTime.Format(w.EndTick))
                  .Append(" battles=").Append(w.Battles).Append(" score=").Append(w.WarScore.ToString("0")).Append(" captured=").Append(w.SettlementsCaptured).AppendLine();
            }
            return sb.ToString();
        }
    }
}
