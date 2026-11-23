using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Unity;
using NUnit.Framework;

namespace GodSim.Tests.UnityAudio
{
    public class ProceduralSynthTests
    {
        private struct Gen
        {
            public string Name;
            public Func<float[]> Make;
            public float Seconds;
        }

        private static List<Gen> AllGenerators()
        {
            var g = new List<Gen>
            {
                new Gen { Name = "UiClick", Make = () => ProceduralSynth.UiClick(), Seconds = ProceduralSynth.UiClickSeconds },
                new Gen { Name = "UiError", Make = () => ProceduralSynth.UiError(), Seconds = ProceduralSynth.UiErrorSeconds },
                new Gen { Name = "Lightning", Make = () => ProceduralSynth.LightningCrack(), Seconds = ProceduralSynth.LightningSeconds },
                new Gen { Name = "Meteor", Make = () => ProceduralSynth.MeteorImpact(), Seconds = ProceduralSynth.MeteorSeconds },
                new Gen { Name = "Earthquake", Make = () => ProceduralSynth.EarthquakeRumble(), Seconds = ProceduralSynth.EarthquakeSeconds },
                new Gen { Name = "Flood", Make = () => ProceduralSynth.FloodSurge(), Seconds = ProceduralSynth.FloodSeconds },
                new Gen { Name = "Omen", Make = () => ProceduralSynth.Omen(), Seconds = ProceduralSynth.OmenSeconds },
                new Gen { Name = "FireLoop", Make = () => ProceduralSynth.FireCrackleLoop(), Seconds = ProceduralSynth.FireLoopSeconds },
                new Gen { Name = "RainLoop", Make = () => ProceduralSynth.RainLoop(), Seconds = ProceduralSynth.RainLoopSeconds },
                new Gen { Name = "WindLoop", Make = () => ProceduralSynth.WindLoop(), Seconds = ProceduralSynth.WindLoopSeconds },
                new Gen { Name = "NatureLoop", Make = () => ProceduralSynth.NatureLoop(), Seconds = ProceduralSynth.NatureLoopSeconds },
                new Gen { Name = "WarDrums", Make = () => ProceduralSynth.WarDrums(), Seconds = ProceduralSynth.WarDrumsSeconds },
                new Gen { Name = "Hammer", Make = () => ProceduralSynth.ConstructionHammer(), Seconds = ProceduralSynth.HammerSeconds },
                new Gen { Name = "Harp", Make = () => ProceduralSynth.WonderHarp(), Seconds = ProceduralSynth.HarpSeconds },
                new Gen { Name = "Battle", Make = () => ProceduralSynth.BattleClash(), Seconds = ProceduralSynth.BattleSeconds },
                new Gen { Name = "Settlement", Make = () => ProceduralSynth.SettlementFounded(), Seconds = ProceduralSynth.SettlementSeconds },
                new Gen { Name = "Legendary", Make = () => ProceduralSynth.Legendary(), Seconds = ProceduralSynth.LegendarySeconds },
            };
            for (int c = 0; c < 4; c++)
            {
                var cat = (GodPowerCategory)c;
                g.Add(new Gen { Name = "Select." + cat, Make = () => ProceduralSynth.PowerSelect(cat), Seconds = ProceduralSynth.PowerSelectSeconds });
                g.Add(new Gen { Name = "Cast." + cat, Make = () => ProceduralSynth.Cast(cat), Seconds = ProceduralSynth.CastSeconds(cat) });
            }
            for (int a = 0; a < Counts.Ages; a++)
            {
                var age = (AgeId)a;
                g.Add(new Gen { Name = "Bell." + age, Make = () => ProceduralSynth.AgeBell(age), Seconds = ProceduralSynth.AgeBellSeconds });
            }
            return g;
        }

        private static void AssertClean(string name, float[] buf)
        {
            Assert.IsNotNull(buf, name);
            float peak = 0f;
            double energy = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float v = buf[i];
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), name + " has non-finite sample at " + i);
                peak = Math.Max(peak, Math.Abs(v));
                energy += v * v;
            }
            Assert.LessOrEqual(peak, 1f, name + " peak");
            Assert.Greater(peak, 0.05f, name + " is (nearly) silent");
            Assert.Greater(energy, 0.0, name + " has no energy");
        }

        [Test]
        public void AllGenerators_HaveCorrectLength_NoNaN_PeakAtMostOne()
        {
            var sw = new Stopwatch();
            foreach (var g in AllGenerators())
            {
                sw.Restart();
                float[] buf = g.Make();
                sw.Stop();
                TestContext.WriteLine(g.Name + ": " + buf.Length + " samples, peak " + ProceduralSynth.Peak(buf).ToString("F3") + ", " + sw.ElapsedMilliseconds + " ms");
                Assert.AreEqual(ProceduralSynth.Samples(g.Seconds), buf.Length, g.Name + " length");
                AssertClean(g.Name, buf);
                Assert.LessOrEqual(ProceduralSynth.Peak(buf), ProceduralSynth.TargetPeak + 1e-5f, g.Name + " normalized peak");
            }
        }

        [Test]
        public void AllGenerators_AreDeterministic()
        {
            var first = AllGenerators();
            var second = AllGenerators();
            for (int i = 0; i < first.Count; i++)
            {
                float[] a = first[i].Make();
                float[] b = second[i].Make();
                Assert.AreEqual(a.Length, b.Length, first[i].Name);
                for (int k = 0; k < a.Length; k++)
                    if (a[k] != b[k]) Assert.Fail(first[i].Name + " differs at sample " + k);
            }
        }

        [Test]
        public void Seeds_ChangeNoiseBasedOutput()
        {
            float[] a = ProceduralSynth.ConstructionHammer(1);
            float[] b = ProceduralSynth.ConstructionHammer(2);
            bool differ = false;
            for (int i = 0; i < Math.Min(a.Length, b.Length) && !differ; i++) differ = a[i] != b[i];
            Assert.IsTrue(differ);
        }

        [Test]
        public void AgeBells_DifferPerAge()
        {
            float[] dawn = ProceduralSynth.AgeBell(AgeId.Dawn);
            float[] asc = ProceduralSynth.AgeBell(AgeId.Ascension);
            bool differ = false;
            for (int i = 0; i < dawn.Length && !differ; i++) differ = dawn[i] != asc[i];
            Assert.IsTrue(differ);
            Assert.Greater(ProceduralSynth.AgeBellRoot(AgeId.Ascension), ProceduralSynth.AgeBellRoot(AgeId.Dawn));
            Assert.AreNotEqual(ProceduralSynth.AgeBellMode(AgeId.Dawn), ProceduralSynth.AgeBellMode(AgeId.Ascension));
        }

        [Test]
        public void Loops_AreSeamless()
        {
            foreach (var loop in new[] { ProceduralSynth.RainLoop(), ProceduralSynth.WindLoop(), ProceduralSynth.FireCrackleLoop(), ProceduralSynth.NatureLoop() })
            {
                // The wrap-around step must look like an ordinary neighbouring-sample step.
                float wrap = Math.Abs(loop[0] - loop[loop.Length - 1]);
                double meanStep = 0;
                for (int i = 1; i < loop.Length; i++) meanStep += Math.Abs(loop[i] - loop[i - 1]);
                meanStep /= loop.Length - 1;
                Assert.Less(wrap, Math.Max(0.25, meanStep * 12), "loop discontinuity");
            }
        }

        [Test]
        public void MakeSeamlessLoop_ProducesRequestedLength()
        {
            var src = new float[1000 * 2];
            for (int i = 0; i < src.Length; i++) src[i] = (float)Math.Sin(i * 0.01);
            float[] loop = ProceduralSynth.MakeSeamlessLoop(src, 800, 2);
            Assert.AreEqual(1600, loop.Length);
        }

        [Test]
        public void Oscillators_StayInRange_AndHitKnownValues()
        {
            var rng = new ProceduralSynth.Xorshift32(5);
            for (int i = 0; i < 10000; i++)
            {
                float p = i * 0.00737f - 3f;
                foreach (ProceduralSynth.Waveform w in Enum.GetValues(typeof(ProceduralSynth.Waveform)))
                {
                    float v = ProceduralSynth.Oscillator(w, p, ref rng);
                    Assert.IsTrue(v >= -1.0001f && v <= 1.0001f, w + " out of range: " + v);
                }
                float sb = ProceduralSynth.SawBlep(p, 0.02f);
                float qb = ProceduralSynth.SquareBlep(p, 0.02f);
                Assert.IsTrue(Math.Abs(sb) <= 1.0001f && Math.Abs(qb) <= 1.0001f);
            }
            Assert.AreEqual(0f, ProceduralSynth.Sine(0f), 1e-5f);
            Assert.AreEqual(1f, ProceduralSynth.Sine(0.25f), 1e-5f);
            Assert.AreEqual(-1f, ProceduralSynth.Sine(-0.25f), 1e-5f);
            Assert.AreEqual(1f, ProceduralSynth.Triangle(0.5f), 1e-5f);
            Assert.AreEqual(-1f, ProceduralSynth.Saw(0f), 1e-5f);
            Assert.AreEqual(0f, ProceduralSynth.Sine(float.NaN));
        }

        [Test]
        public void Xorshift_IsDeterministicAndUniformish()
        {
            var a = new ProceduralSynth.Xorshift32(42);
            var b = new ProceduralSynth.Xorshift32(42);
            double sum = 0;
            for (int i = 0; i < 100000; i++)
            {
                float x = a.NextFloat();
                Assert.AreEqual(x, b.NextFloat());
                Assert.IsTrue(x >= 0f && x < 1f);
                sum += x;
            }
            Assert.AreEqual(0.5, sum / 100000, 0.01);
            var z = new ProceduralSynth.Xorshift32(0);
            Assert.AreNotEqual(0u, z.NextUInt());
        }

        [Test]
        public void Adsr_FollowsItsShape()
        {
            var env = new ProceduralSynth.Adsr(0.1f, 0.2f, 0.5f, 0.4f);
            Assert.AreEqual(0f, env.Evaluate(0f, 1f), 1e-6f);
            Assert.AreEqual(0.5f, env.Evaluate(0.05f, 1f), 1e-5f);
            Assert.AreEqual(1f, env.Evaluate(0.1f, 1f), 1e-5f);
            Assert.AreEqual(0.75f, env.Evaluate(0.2f, 1f), 1e-5f);
            Assert.AreEqual(0.5f, env.Evaluate(0.6f, 1f), 1e-5f);
            Assert.Less(env.Evaluate(1.2f, 1f), 0.5f);
            Assert.AreEqual(0f, env.Evaluate(1.41f, 1f), 1e-6f);
            Assert.AreEqual(0f, env.Evaluate(-1f, 1f));
            Assert.AreEqual(1.4f, env.Length(1f), 1e-6f);
        }

        [Test]
        public void Filters_AttenuateTheRightBand()
        {
            float lowGainLp = SineGain(f => { var b = ProceduralSynth.Biquad.LowPass(1000f, 0.707f); return x => b.Process(x); }, 100f);
            float highGainLp = SineGain(f => { var b = ProceduralSynth.Biquad.LowPass(1000f, 0.707f); return x => b.Process(x); }, 10000f);
            float lowGainHp = SineGain(f => { var b = ProceduralSynth.Biquad.HighPass(1000f, 0.707f); return x => b.Process(x); }, 100f);
            float highGainHp = SineGain(f => { var b = ProceduralSynth.Biquad.HighPass(1000f, 0.707f); return x => b.Process(x); }, 10000f);
            float onePoleLow = SineGain(f => { var o = new ProceduralSynth.OnePole(500f); return x => o.LowPass(x); }, 50f);
            float onePoleHigh = SineGain(f => { var o = new ProceduralSynth.OnePole(500f); return x => o.LowPass(x); }, 12000f);
            Assert.Greater(lowGainLp, 0.9f); Assert.Less(highGainLp, 0.05f);
            Assert.Less(lowGainHp, 0.05f); Assert.Greater(highGainHp, 0.9f);
            Assert.Greater(onePoleLow, 0.9f); Assert.Less(onePoleHigh, 0.1f);
        }

        private static float SineGain(Func<int, Func<float, float>> makeFilter, float hz)
        {
            Func<float, float> f = makeFilter(0);
            float phase = 0f, peak = 0f;
            int n = ProceduralSynth.SampleRate / 2;
            for (int i = 0; i < n; i++)
            {
                float y = f(ProceduralSynth.Sine(phase));
                phase = ProceduralSynth.Advance(phase, hz);
                if (i > n / 2) peak = Math.Max(peak, Math.Abs(y));
            }
            return peak;
        }

        [Test]
        public void Reverb_AndDelay_AreStableAndAddTail()
        {
            var buf = new float[ProceduralSynth.SampleRate * 3];
            buf[0] = 1f;
            ProceduralSynth.ApplyReverb(buf, 1.5f, 0.95f, 0.2f, 1f);
            double tail = 0;
            for (int i = ProceduralSynth.SampleRate / 10; i < buf.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(buf[i]) || float.IsInfinity(buf[i]));
                Assert.Less(Math.Abs(buf[i]), 2f);
                tail += Math.Abs(buf[i]);
            }
            Assert.Greater(tail, 0.0);

            var d = new ProceduralSynth.FeedbackDelay(0.1f, 0.6f, 4000f, 0.5f);
            float echo = 0f;
            for (int i = 0; i < ProceduralSynth.SampleRate; i++)
            {
                float y = d.Process(i == 0 ? 1f : 0f);
                if (i == ProceduralSynth.Samples(0.1f)) echo = y;
            }
            Assert.AreEqual(0.5f, echo, 1e-4f);
        }

        [Test]
        public void MusicTheory_ScalesChordsAndArpeggios()
        {
            Assert.AreEqual(60, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Ionian, 0));
            Assert.AreEqual(64, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Ionian, 2));
            Assert.AreEqual(72, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Ionian, 7));
            Assert.AreEqual(59, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Ionian, -1));
            Assert.AreEqual(66, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Lydian, 3));
            Assert.AreEqual(61, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.Phrygian, 1));
            Assert.AreEqual(72, ProceduralSynth.ScaleNote(60, ProceduralSynth.Mode.MinorPentatonic, 5));
            Assert.AreEqual(440f, ProceduralSynth.MidiToHz(69), 1e-3f);
            Assert.AreEqual(261.6256f, ProceduralSynth.MidiToHz(60), 1e-2f);

            var tones = new int[3];
            ProceduralSynth.ChordNotes(60, ProceduralSynth.Mode.Aeolian, 0, tones);
            CollectionAssert.AreEqual(new[] { 60, 63, 67 }, tones);

            var notes = ProceduralSynth.Arpeggiate(60, ProceduralSynth.Mode.Ionian, new[] { 0, 3, 4 }, 6, 0.1f, 0.5f, 1f, true);
            Assert.AreEqual(18, notes.Count);
            for (int i = 1; i < notes.Count; i++) Assert.Greater(notes[i].Time, notes[i - 1].Time);
            Assert.AreEqual(60f, notes[0].Midi);
            Assert.AreEqual(65f, notes[6].Midi); // IV chord root

            var buf = new float[ProceduralSynth.Samples(2.5f)];
            ProceduralSynth.RenderNotes(buf, notes, ProceduralSynth.Instrument.Harp, 3);
            foreach (ProceduralSynth.Instrument inst in Enum.GetValues(typeof(ProceduralSynth.Instrument)))
                ProceduralSynth.RenderNotes(buf, notes, inst, 3);
            ProceduralSynth.Normalize(buf, 0.8f);
            AssertClean("notes", buf);
        }

        [Test]
        public void MusicBed_IsDeterministic_Finite_AndBounded()
        {
            float[] a = RenderBed(77, 12f);
            float[] b = RenderBed(77, 12f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) Assert.Fail("bed differs at " + i);
            AssertClean("bed", a);
            Assert.LessOrEqual(ProceduralSynth.Peak(a), 0.9f + 1e-5f);
        }

        [Test]
        public void MusicBed_AppliesHarmonyAtChordChange_AndSupportsAnyChannelCount()
        {
            var bed = new GenerativeMusicBed(9);
            Assert.AreEqual(ProceduralSynth.Mode.Dorian, bed.CurrentMode);
            bed.SetHarmony(ProceduralSynth.Mode.Phrygian, 50, 0.8f, 0.9f);
            bed.ApplyHarmonyNow();
            var mono = new float[1024];
            bed.Render(mono, 1);
            Assert.AreEqual(ProceduralSynth.Mode.Phrygian, bed.CurrentMode);
            Assert.AreEqual(50, bed.CurrentRoot);
            var six = new float[6 * 512];
            bed.Render(six, 6);
            Assert.AreEqual(1024 + 512, bed.FramesRendered);
            bed.SetHarmony(ProceduralSynth.Mode.Lydian, 1000, float.NaN, float.PositiveInfinity);
            bed.ApplyHarmonyNow();
            var st = new float[2 * ProceduralSynth.SampleRate * 4];
            bed.Render(st, 2);
            Assert.AreEqual(84, bed.CurrentRoot);
            AssertClean("bed after bad input", st);
            bed.Render(null, 2); // no throw
            bed.Render(st, 5, 1000000, 2); // clamps
        }

        private static float[] RenderBed(uint seed, float seconds)
        {
            var bed = new GenerativeMusicBed(seed);
            bed.SetHarmony(ProceduralSynth.Mode.Lydian, 52, 0.7f, 0.8f);
            int frames = (int)(seconds * ProceduralSynth.SampleRate);
            var buf = new float[frames * 2];
            // Render in uneven callback-sized blocks, like Unity's PCM reader does.
            int pos = 0, block = 1024;
            while (pos < frames)
            {
                int n = Math.Min(block, frames - pos);
                bed.Render(buf, pos, n, 2);
                pos += n;
                block = block == 1024 ? 4096 : 1024;
            }
            return buf;
        }

        [Test]
        public void Mapping_CategoriesWondersAndMusicModes()
        {
            Assert.AreEqual(GodPowerCategory.Blessing, GodSimAudioMapping.CategoryOf(GodPowerType.Bless));
            Assert.AreEqual(GodPowerCategory.Blessing, GodSimAudioMapping.CategoryOf(GodPowerType.Prophet));
            Assert.AreEqual(GodPowerCategory.Shaping, GodSimAudioMapping.CategoryOf(GodPowerType.RaiseLand));
            Assert.AreEqual(GodPowerCategory.Shaping, GodSimAudioMapping.CategoryOf(GodPowerType.RevealOre));
            Assert.AreEqual(GodPowerCategory.Creation, GodSimAudioMapping.CategoryOf(GodPowerType.SpawnTribe));
            Assert.AreEqual(GodPowerCategory.Wrath, GodSimAudioMapping.CategoryOf(GodPowerType.Lightning));
            Assert.AreEqual(GodPowerCategory.Wrath, GodSimAudioMapping.CategoryOf(GodPowerType.Drought));
            Assert.IsTrue(GodSimAudioMapping.IsWonder(BuildingType.GreatLibrary));
            Assert.IsTrue(GodSimAudioMapping.IsWonder(BuildingType.AscensionGate));
            Assert.IsFalse(GodSimAudioMapping.IsWonder(BuildingType.House));
            Assert.AreEqual(ProceduralSynth.Mode.Lydian, GodSimAudioMapping.MusicModeFor(0.9f));
            Assert.AreEqual(ProceduralSynth.Mode.Ionian, GodSimAudioMapping.MusicModeFor(0.2f));
            Assert.AreEqual(ProceduralSynth.Mode.Dorian, GodSimAudioMapping.MusicModeFor(0f));
            Assert.AreEqual(ProceduralSynth.Mode.Aeolian, GodSimAudioMapping.MusicModeFor(-0.2f));
            Assert.AreEqual(ProceduralSynth.Mode.Phrygian, GodSimAudioMapping.MusicModeFor(-0.9f));
            Assert.AreEqual(ProceduralSynth.Mode.Dorian, GodSimAudioMapping.MusicModeFor(float.NaN));
            for (int a = 0; a < Counts.Ages; a++)
            {
                int root = GodSimAudioMapping.MusicRootFor((AgeId)a);
                Assert.IsTrue(root >= 40 && root <= 60);
                float br = GodSimAudioMapping.MusicBrightnessFor((AgeId)a, -1f);
                Assert.IsTrue(br >= 0f && br <= 1f);
            }
        }
    }
}
