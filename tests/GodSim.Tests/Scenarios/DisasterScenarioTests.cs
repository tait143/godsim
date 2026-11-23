using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenario 8 (Disasters off). With EnableDisasters=false and no god casts, 100 years must pass without a
    /// single natural DisasterEvent. Two DisasterTypes are deliberately excluded and reported separately because
    /// their source is the population/economy, not nature:
    ///   - Plague: epidemics emerging from crowding/low health (population module) may be published as
    ///     DisasterEvent{Plague}.
    ///   - Famine: food shortages (economy/population) may be mirrored as DisasterEvent{Famine}; FamineEvent is
    ///     the primary signal for those.
    /// Everything else (Wildfire, Earthquake, Flood, Drought, Meteor, Volcano, Storm, Lightning) is natural.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class DisasterScenarioTests
    {
        private const ulong Seed = 1234UL;
        private const int Years = 100;

        [Test]
        public void DisastersOff_NoNaturalDisasterIn100Years()
        {
            var cfg = ScenarioWorlds.SmallWorld(Seed);
            cfg.EnableDisasters = false;
            var log = new CollectingLogger();
            using (var h = new ScenarioHarness(ScenarioWorlds.Create(cfg, log), log))
            {
                h.RunYears(Years);
                var natural = new List<string>();
                var excluded = new int[YearMetrics.DisasterTypeCount];
                foreach (var d in h.Disasters)
                {
                    var t = d.Event.Type;
                    if (t == DisasterType.Plague || t == DisasterType.Famine) { excluded[(int)t]++; continue; }
                    natural.Add(SimTime.Format(d.Tick) + ": " + t + " at " + d.Event.Position + " r=" + d.Event.Radius + " severity=" + d.Event.Severity.ToString("0.00"));
                }
                int disasterDestroyed = 0, disasterDeaths = 0, divineDeaths = 0;
                foreach (var b in h.Destructions) if (b.Event.Cause == DestroyCause.Disaster) disasterDestroyed++;
                foreach (var p in h.Deaths)
                {
                    if (p.Event.Cause == DeathCause.Disaster) disasterDeaths++;
                    if (p.Event.Cause == DeathCause.DivineWrath) divineDeaths++;
                }
                int droughtSamples = 0;
                foreach (var w in h.WorldSamples) if (w.DroughtTicksRemaining > 0) droughtSamples++;
                var info = new StringBuilder();
                info.Append("excluded (population/economy-driven): Plague=").Append(excluded[(int)DisasterType.Plague])
                    .Append(" Famine=").Append(excluded[(int)DisasterType.Famine])
                    .Append("; samples with Weather.DroughtTicksRemaining>0: ").Append(droughtSamples).Append('/').Append(h.WorldSamples.Count)
                    .Append("; god casts: ").Append(h.Casts.Count);
                TestContext.WriteLine(info.ToString() + "\n" + h.Summary());

                Assert.That(h.Casts.Count, Is.EqualTo(0), "Scenario cast no power, yet GodPowerCastEvents appeared.");
                Assert.That(natural, Is.Empty, "Natural disasters happened with EnableDisasters=false:\n" + ScenarioWorlds.Join(natural) + info);
                Assert.That(disasterDestroyed, Is.EqualTo(0), "Buildings destroyed with DestroyCause.Disaster while disasters are off.");
                Assert.That(disasterDeaths, Is.EqualTo(0), "People died of DeathCause.Disaster while disasters are off.");
                Assert.That(divineDeaths, Is.EqualTo(0), "People died of DivineWrath although the god did nothing.");
            }
        }
    }
}
