using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Unity;
using NUnit.Framework;

namespace GodSim.Tests.UnityTutorial
{
    public class TutorialScriptTests
    {
        private static TutorialScript Mini()
        {
            return new TutorialScript(new List<TutorialStep>
            {
                new TutorialStep("intro", "Intro", "Hi", TutorialObjective.None()),
                new TutorialStep("cam", "Cam", "Move", TutorialObjective.CameraMoved("Move")),
                new TutorialStep("bless", "Bless", "Cast", TutorialObjective.CastCategory(GodPowerCategory.Blessing, "Bless")),
                new TutorialStep("raise", "Raise", "Raise", TutorialObjective.CastPower(GodPowerType.RaiseLand, "Raise")),
                new TutorialStep("wait", "Wait", "Wait", TutorialObjective.WaitDays(10, "Wait")),
                new TutorialStep("end", "End", "Bye", TutorialObjective.None(), TutorialAnchors.Center, false),
            });
        }

        [Test]
        public void DefaultContent_IsValid()
        {
            var s = TutorialContent.CreateDefaultScript();
            Assert.That(s.StepCount, Is.InRange(12, 16));
            var known = new HashSet<string> { TutorialAnchors.Center, TutorialAnchors.TopBar, TutorialAnchors.PowersBar,
                TutorialAnchors.Inspector, TutorialAnchors.Chronicle, TutorialAnchors.Minimap, TutorialAnchors.Help };
            for (int i = 0; i < s.StepCount; i++)
            {
                var st = s.GetStep(i);
                Assert.IsFalse(string.IsNullOrEmpty(st.Title), st.Id);
                Assert.IsTrue(st.Body.Length > 60, st.Id);
                Assert.IsTrue(known.Contains(st.Highlight), st.Id);
                Assert.IsTrue(CountOf(st.Body, "<b>") == CountOf(st.Body, "</b>"), "unbalanced <b> in " + st.Id);
                Assert.IsTrue(CountOf(st.Body, "<color") == CountOf(st.Body, "</color>"), "unbalanced <color> in " + st.Id);
            }
            Assert.AreEqual(TutorialContent.Welcome, s.GetStep(0).Id);
            Assert.AreEqual(TutorialContent.Free, s.GetStep(s.StepCount - 1).Id);
            Assert.IsTrue(s.GetStep(s.IndexOf(TutorialContent.Bless)).Objective.MatchesCast(GodPowerType.Bless, GodPowerCategory.Blessing));
            Assert.IsTrue(s.GetStep(s.IndexOf(TutorialContent.Shape)).Objective.MatchesCast(GodPowerType.RaiseLand, GodPowerCategory.Shaping));
            foreach (TutorialTipKind k in Enum.GetValues(typeof(TutorialTipKind)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(TutorialContent.TipTitle(k)));
                Assert.IsFalse(string.IsNullOrEmpty(TutorialContent.TipBody(k)));
            }
        }

        [Test]
        public void Walkthrough_SignalsCompleteObjectives_InOrder()
        {
            var s = Mini();
            s.AutoAdvanceDelay = -1f;
            Assert.AreEqual(TutorialState.NotStarted, s.State);
            Assert.IsFalse(s.OnCameraMoved(), "signals before start are ignored");
            Assert.IsTrue(s.Start());
            Assert.IsFalse(s.Start());
            Assert.AreEqual("intro", s.CurrentStep.Id);
            Assert.IsTrue(s.CanAdvance, "informational step");
            Assert.IsTrue(s.Advance());

            Assert.AreEqual("cam", s.CurrentStep.Id);
            Assert.IsFalse(s.CanAdvance);
            Assert.IsFalse(s.Advance());
            Assert.IsFalse(s.OnSettlementSelected(), "wrong signal");
            Assert.IsTrue(s.OnCameraMoved());
            Assert.IsFalse(s.OnCameraMoved(), "only once");
            Assert.IsTrue(s.Advance());

            Assert.AreEqual("bless", s.CurrentStep.Id);
            Assert.IsFalse(s.OnPowerCast(GodPowerType.Lightning));
            Assert.IsTrue(s.OnPowerCast(GodPowerType.Rain), "any Blessing counts");
            Assert.IsTrue(s.Advance());

            Assert.AreEqual("raise", s.CurrentStep.Id);
            Assert.IsFalse(s.OnPowerCast(GodPowerType.LowerLand), "exact type required");
            Assert.IsTrue(s.OnPowerCast(GodPowerType.RaiseLand));
            Assert.IsTrue(s.Advance());

            Assert.AreEqual("wait", s.CurrentStep.Id);
            Assert.IsFalse(s.OnDaysPassed(4));
            Assert.AreEqual(0.4f, s.ObjectiveProgress, 1e-5f);
            Assert.IsFalse(s.OnDaysPassed(-50));
            Assert.IsFalse(s.OnDaysPassed(0));
            Assert.IsTrue(s.OnDaysPassed(int.MaxValue), "no overflow");
            Assert.AreEqual(10, s.DaysAccumulated);
            Assert.IsTrue(s.Advance());

            Assert.IsTrue(s.IsLastStep);
            Assert.IsFalse(s.CanSkip, "last step not skippable");
            int finished = 0;
            s.Finished += _ => finished++;
            Assert.IsTrue(s.Advance());
            Assert.IsTrue(s.IsFinished);
            Assert.AreEqual(1, finished);
            Assert.IsNull(s.CurrentStep);
            Assert.AreEqual(6, s.CompletedCount);
            Assert.IsFalse(s.Advance());
            Assert.IsFalse(s.OnCameraMoved());
        }

        [Test]
        public void SignalForAnotherStep_DoesNotCarryOver()
        {
            var s = Mini();
            s.Start();
            Assert.IsFalse(s.OnPowerCast(GodPowerType.Bless)); // on intro
            s.Advance();
            s.OnCameraMoved();
            s.Advance();
            Assert.AreEqual("bless", s.CurrentStep.Id);
            Assert.IsFalse(s.ObjectiveMet);
        }

        [Test]
        public void Skip_MarksSkipped_AndRespectsSkippable()
        {
            var s = Mini();
            s.Start();
            s.Advance();
            Assert.IsTrue(s.Skip());
            Assert.IsTrue(s.IsStepSkipped(1));
            Assert.IsFalse(s.IsStepCompleted(1));
            Assert.AreEqual(2, s.CurrentIndex);
            s.SkipAll();
            Assert.IsTrue(s.IsFinished);
            Assert.AreEqual(1, s.CompletedCount);
            Assert.AreEqual(5, s.SkippedCount);
        }

        [Test]
        public void AutoAdvance_AfterDelay_OnlyForObjectives()
        {
            var s = Mini();
            s.AutoAdvanceDelay = 1f;
            s.Start();
            s.Update(5f);
            Assert.AreEqual(0, s.CurrentIndex, "informational steps wait for Next");
            s.Advance();
            s.OnCameraMoved();
            s.Update(0.6f);
            s.Update(float.NaN);
            s.Update(-3f);
            Assert.AreEqual(1, s.CurrentIndex);
            s.Update(0.6f);
            Assert.AreEqual(2, s.CurrentIndex);
            Assert.IsTrue(s.IsStepCompleted(1));
        }

        [Test]
        public void Events_Fire()
        {
            var s = Mini();
            int changed = 0, met = 0;
            s.StepChanged += _ => changed++;
            s.ObjectiveCompleted += _ => met++;
            s.Start();
            s.Advance();
            s.OnCameraMoved();
            Assert.AreEqual(2, changed);
            Assert.AreEqual(1, met);
            s.Reset();
            Assert.AreEqual(3, changed);
            Assert.AreEqual(TutorialState.NotStarted, s.State);
            Assert.AreEqual(-1, s.CurrentIndex);
        }

        [Test]
        public void Serialization_RoundTrips()
        {
            var s = Mini();
            s.Start();
            s.Advance();
            s.Skip();
            s.OnPowerCast(GodPowerType.Heal);
            s.Advance();
            string data = s.Serialize();
            Assert.That(data.Length, Is.LessThan(48));
            StringAssert.StartsWith("t1;", data);

            var t = Mini();
            Assert.IsTrue(t.TryDeserialize(data));
            Assert.AreEqual(TutorialState.Active, t.State);
            Assert.AreEqual("raise", t.CurrentStep.Id);
            Assert.IsTrue(t.IsStepCompleted(0));
            Assert.IsTrue(t.IsStepSkipped(1));
            Assert.IsTrue(t.IsStepCompleted(2));
            Assert.IsFalse(t.ObjectiveMet, "objective progress is not persisted");
            Assert.AreEqual(data, t.Serialize());

            s.SkipAll();
            var u = Mini();
            Assert.IsTrue(u.TryDeserialize(s.Serialize()));
            Assert.IsTrue(u.IsFinished);

            var fresh = Mini();
            Assert.IsTrue(Mini().TryDeserialize(fresh.Serialize()));
        }

        [Test]
        public void Deserialize_PrefersStepId_OverIndex()
        {
            var s = Mini();
            Assert.IsTrue(s.TryDeserialize("t1;1;1;1;0;wait"));
            Assert.AreEqual("wait", s.CurrentStep.Id);
            Assert.IsTrue(s.TryDeserialize("t1;1;2;3;0;gone"), "unknown id falls back to index");
            Assert.AreEqual(2, s.CurrentIndex);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("garbage")]
        [TestCase("t0;1;1;0;0;cam")]
        [TestCase("t1;9;1;0;0;cam")]
        [TestCase("t1;1;99;0;0;")]
        [TestCase("t1;1;-5;0;0;nope")]
        [TestCase("t1;1;1;zz;0;cam")]
        [TestCase("t1;1;1;0;0")]
        [TestCase("t1;1;1;11111111111111111;0;cam")]
        [TestCase("t1;-1;1;0;0;cam")]
        public void Deserialize_RejectsGarbage_AndLeavesStateUnchanged(string data)
        {
            var s = Mini();
            s.Start();
            s.Advance();
            string before = s.Serialize();
            Assert.IsFalse(s.TryDeserialize(data));
            Assert.AreEqual(before, s.Serialize());
        }

        [Test]
        public void Deserialize_MasksUnknownBits_AndOverlongInput()
        {
            var s = Mini();
            Assert.IsTrue(s.TryDeserialize("t1;1;1;ffffffffffffffff;ffff;cam"));
            Assert.AreEqual(6, s.CompletedCount);
            Assert.AreEqual(0, s.SkippedCount, "completed wins over skipped");
            Assert.IsFalse(s.TryDeserialize("t1;1;1;0;0;" + new string('a', 400)));
        }

        [Test]
        public void Constructor_ValidatesSteps()
        {
            Assert.Throws<ArgumentNullException>(() => new TutorialScript(null));
            Assert.Throws<ArgumentException>(() => new TutorialScript(new TutorialStep[0]));
            var a = new TutorialStep("a", "A", "", TutorialObjective.None());
            Assert.Throws<ArgumentException>(() => new TutorialScript(new[] { a, a }));
            Assert.Throws<ArgumentException>(() => new TutorialStep("Bad Id;", "x", "y", TutorialObjective.None()));
            Assert.Throws<ArgumentException>(() => new TutorialScript(new TutorialStep[] { a, null }));
        }

        [Test]
        public void CategoryResolver_IsUsed_AndFaultTolerant()
        {
            var s = Mini();
            s.Start(); s.Advance(); s.OnCameraMoved(); s.Advance();
            s.CategoryResolver = p => GodPowerCategory.Blessing; // e.g. a modded database
            Assert.IsTrue(s.OnPowerCast(GodPowerType.Meteor));
            s.Advance();
            s.CategoryResolver = p => throw new InvalidOperationException();
            Assert.IsTrue(s.OnPowerCast(GodPowerType.RaiseLand), "falls back to the default map");
        }

        [Test]
        public void DefaultCategoryOf_CoversEveryPower()
        {
            Assert.AreEqual(GodPowerCategory.Blessing, TutorialScript.DefaultCategoryOf(GodPowerType.Prophet));
            Assert.AreEqual(GodPowerCategory.Shaping, TutorialScript.DefaultCategoryOf(GodPowerType.RevealOre));
            Assert.AreEqual(GodPowerCategory.Creation, TutorialScript.DefaultCategoryOf(GodPowerType.SpawnTribe));
            Assert.AreEqual(GodPowerCategory.Wrath, TutorialScript.DefaultCategoryOf(GodPowerType.Drought));
            Assert.AreEqual(GodPowerCategory.Wrath, TutorialScript.DefaultCategoryOf(GodPowerType.Lightning));
        }

        [Test]
        public void HelpAndSpeedSignals()
        {
            var s = new TutorialScript(new[]
            {
                new TutorialStep("speed", "S", "s", TutorialObjective.SpeedChanged("x")),
                new TutorialStep("help", "H", "h", TutorialObjective.OpenHelp("x")),
                new TutorialStep("sel", "Se", "se", TutorialObjective.SettlementSelected("x")),
            });
            s.AutoAdvanceDelay = 0f;
            s.Start();
            Assert.IsFalse(s.OnHelpOpened());
            Assert.IsTrue(s.OnSpeedChanged());
            s.Update(0.01f);
            Assert.IsTrue(s.OnHelpOpened());
            s.Update(0.01f);
            Assert.IsTrue(s.OnSettlementSelected());
            s.Update(0.01f);
            Assert.IsTrue(s.IsFinished);
        }

        [Test]
        public void TipTracker_OneTime_AndMasked()
        {
            var t = new TutorialTipTracker();
            Assert.IsTrue(t.TryConsume(TutorialTipKind.FirstWar));
            Assert.IsFalse(t.TryConsume(TutorialTipKind.FirstWar));
            Assert.IsTrue(t.WasShown(TutorialTipKind.FirstWar));
            Assert.IsFalse(t.WasShown(TutorialTipKind.LowMana));
            Assert.IsFalse(t.TryConsume((TutorialTipKind)200));
            t.Mask = -1;
            Assert.AreEqual((1 << TutorialTipTracker.Count) - 1, t.Mask);
            t.Reset();
            Assert.AreEqual(0, t.Mask);
        }

        private static int CountOf(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }
    }
}
