using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// Unity-free procedural audio toolkit: oscillators, envelopes, filters, a small reverb, a chord/arpeggio
    /// sequencer and one generator per game sound. Every generator returns mono float PCM at
    /// <see cref="SampleRate"/> Hz, is fully deterministic for a given seed (xorshift noise, table sine),
    /// never produces NaN/Infinity and is normalized so that |sample| &lt;= <see cref="TargetPeak"/>.
    /// Generators allocate; they are meant to run once at startup (GodSimAudio spreads them over frames).
    /// </summary>
    public static class ProceduralSynth
    {
        /// <summary>Output sample rate of every generator.</summary>
        public const int SampleRate = 44100;
        /// <summary>Peak level generators normalize to.</summary>
        public const float TargetPeak = 0.89f;
        private const double TwoPi = Math.PI * 2.0;
        private const float InvSampleRate = 1f / SampleRate;

        // ----------------------------------------------------------------------------------------------
        // Durations (seconds) of every generator's output.
        // ----------------------------------------------------------------------------------------------
        public const float UiClickSeconds = 0.07f;
        public const float UiErrorSeconds = 0.2f;
        public const float PowerSelectSeconds = 0.34f;
        public const float BlessingCastSeconds = 3.2f;
        public const float ShapingCastSeconds = 2.4f;
        public const float CreationCastSeconds = 2.6f;
        public const float WrathCastSeconds = 3.6f;
        public const float LightningSeconds = 2.2f;
        public const float MeteorSeconds = 4.0f;
        public const float MeteorImpactTime = 1.35f;
        public const float EarthquakeSeconds = 4.0f;
        public const float FireLoopSeconds = 4.0f;
        public const float RainLoopSeconds = 5.0f;
        public const float WindLoopSeconds = 8.0f;
        public const float NatureLoopSeconds = 9.0f;
        public const float AgeBellSeconds = 4.5f;
        public const float WarDrumsSeconds = 4.2f;
        public const float HammerSeconds = 1.4f;
        public const float HarpSeconds = 4.0f;
        public const float BattleSeconds = 1.6f;
        public const float SettlementSeconds = 2.2f;
        public const float FloodSeconds = 3.0f;
        public const float OmenSeconds = 4.0f;
        public const float LegendarySeconds = 3.6f;

        /// <summary>Number of samples for a duration at <see cref="SampleRate"/> (at least 1).</summary>
        public static int Samples(float seconds)
        {
            if (!(seconds > 0f)) return 1;
            return Math.Max(1, (int)Math.Round(seconds * SampleRate));
        }

        // ==============================================================================================
        // Random numbers
        // ==============================================================================================

        /// <summary>Deterministic xorshift32 generator used for all noise.</summary>
        public struct Xorshift32
        {
            private uint _state;

            /// <summary>Seeds the generator; the seed is scrambled so small seeds give good sequences.</summary>
            public Xorshift32(uint seed)
            {
                uint x = seed + 0x9E3779B9u;
                x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
                _state = x != 0 ? x : 0x1234567u;
            }

            public uint NextUInt()
            {
                uint x = _state;
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                _state = x;
                return x;
            }

            /// <summary>Uniform in [0, 1).</summary>
            public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
            /// <summary>Uniform in [-1, 1).</summary>
            public float NextSigned() => NextFloat() * 2f - 1f;
            public float Range(float min, float max) => min + (max - min) * NextFloat();
            /// <summary>Uniform int in [min, max).</summary>
            public int Range(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive) return minInclusive;
                return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
            }
            public bool Chance(float p) => NextFloat() < p;
        }

        // ==============================================================================================
        // Oscillators (phase in cycles; any real value, wrapped internally)
        // ==============================================================================================

        public enum Waveform : byte { Sine, Triangle, Saw, Square, Noise }

        private const int SineTableSize = 4096;
        private static readonly float[] SineTable = BuildSineTable();

        private static float[] BuildSineTable()
        {
            var t = new float[SineTableSize + 1];
            for (int i = 0; i <= SineTableSize; i++) t[i] = (float)Math.Sin(TwoPi * i / SineTableSize);
            t[SineTableSize] = t[0];
            return t;
        }

        /// <summary>Fractional part in [0, 1).</summary>
        public static float Frac(float x)
        {
            if (x >= 0f && x < 1f) return x;
            float f = x - (float)Math.Floor(x);
            return f >= 0f && f < 1f ? f : 0f; // also maps NaN to 0
        }

        /// <summary>Table sine (linear interpolation, max error ~1e-6).</summary>
        public static float Sine(float phase)
        {
            if (float.IsNaN(phase) || float.IsInfinity(phase)) return 0f;
            float idx = Frac(phase) * SineTableSize;
            int i = (int)idx;
            if (i >= SineTableSize) i = SineTableSize - 1;
            float f = idx - i;
            return SineTable[i] + (SineTable[i + 1] - SineTable[i]) * f;
        }

        public static float Triangle(float phase)
        {
            float p = Frac(phase);
            return p < 0.5f ? 4f * p - 1f : 3f - 4f * p;
        }

        /// <summary>Naive rising saw in [-1, 1).</summary>
        public static float Saw(float phase) => 2f * Frac(phase) - 1f;

        public static float Square(float phase, float duty = 0.5f) => Frac(phase) < duty ? 1f : -1f;

        /// <summary>PolyBLEP correction term; dt = frequency / sample rate.</summary>
        public static float PolyBlep(float t, float dt)
        {
            if (!(dt > 0f)) return 0f;
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        /// <summary>Band-limited (PolyBLEP) saw.</summary>
        public static float SawBlep(float phase, float dt)
        {
            float p = Frac(phase);
            return 2f * p - 1f - PolyBlep(p, dt);
        }

        /// <summary>Band-limited (PolyBLEP) 50% square.</summary>
        public static float SquareBlep(float phase, float dt)
        {
            float p = Frac(phase);
            float v = p < 0.5f ? 1f : -1f;
            v += PolyBlep(p, dt);
            v -= PolyBlep(Frac(p + 0.5f), dt);
            return v;
        }

        /// <summary>Generic oscillator; Noise ignores the phase and draws from <paramref name="rng"/>.</summary>
        public static float Oscillator(Waveform w, float phase, ref Xorshift32 rng)
        {
            switch (w)
            {
                case Waveform.Sine: return Sine(phase);
                case Waveform.Triangle: return Triangle(phase);
                case Waveform.Saw: return Saw(phase);
                case Waveform.Square: return Square(phase);
                default: return rng.NextSigned();
            }
        }

        /// <summary>Advances a phase accumulator by one sample of <paramref name="hz"/>, wrapped to [0, 1).</summary>
        public static float Advance(float phase, float hz)
        {
            phase += hz * InvSampleRate;
            if (phase >= 1f || phase < 0f) phase = Frac(phase);
            return phase;
        }

        // ==============================================================================================
        // Envelopes
        // ==============================================================================================

        /// <summary>Linear attack/decay, sustain level, quadratic release from the level at gate-off.</summary>
        public struct Adsr
        {
            public float Attack, Decay, Sustain, Release;

            public Adsr(float attack, float decay, float sustain, float release)
            {
                Attack = Math.Max(0f, attack);
                Decay = Math.Max(0f, decay);
                Sustain = MathUtil.Clamp01(sustain);
                Release = Math.Max(0f, release);
            }

            /// <summary>Envelope level (0..1) at time <paramref name="t"/> for a note held <paramref name="gate"/> seconds.</summary>
            public float Evaluate(float t, float gate)
            {
                if (!(t >= 0f)) return 0f;
                if (t < gate) return Held(t);
                if (Release <= 0f) return 0f;
                float r = 1f - (t - gate) / Release;
                if (r <= 0f) return 0f;
                return Held(gate) * r * r;
            }

            private float Held(float t)
            {
                if (t < Attack) return Attack > 0f ? t / Attack : 1f;
                t -= Attack;
                if (t < Decay) return 1f - (1f - Sustain) * (t / Decay);
                return Sustain;
            }

            /// <summary>Total audible length for a gate duration.</summary>
            public float Length(float gate) => Math.Max(0f, gate) + Release;
        }

        /// <summary>Per-sample multiplier giving an exponential decay with time constant <paramref name="tau"/> seconds.</summary>
        public static float DecayFactor(float tau) => tau > 0f ? (float)Math.Exp(-1.0 / (tau * SampleRate)) : 0f;

        private static float Exp(float x) => (float)Math.Exp(x);

        private static float SmoothStep(float x)
        {
            x = MathUtil.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // ==============================================================================================
        // Filters
        // ==============================================================================================

        /// <summary>One-pole low-pass (and complementary high-pass) filter.</summary>
        public struct OnePole
        {
            private float _a;
            private float _z;

            public OnePole(float cutoffHz)
            {
                _a = 0f; _z = 0f;
                SetCutoff(cutoffHz);
            }

            public void SetCutoff(float hz)
            {
                if (!(hz > 1f)) hz = 1f;
                if (hz > SampleRate * 0.49f) hz = SampleRate * 0.49f;
                _a = 1f - (float)Math.Exp(-TwoPi * hz / SampleRate);
            }

            public float LowPass(float x)
            {
                _z += _a * (x - _z);
                if (_z > -1e-20f && _z < 1e-20f) _z = 0f; // flush denormals
                return _z;
            }

            public float HighPass(float x) => x - LowPass(x);
        }

        /// <summary>RBJ-cookbook biquad (low-pass / high-pass / band-pass), direct form I.</summary>
        public struct Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2;
            private float _x1, _x2, _y1, _y2;

            public static Biquad LowPass(float hz, float q) { var f = new Biquad(); f.SetLowPass(hz, q); return f; }
            public static Biquad HighPass(float hz, float q) { var f = new Biquad(); f.SetHighPass(hz, q); return f; }
            public static Biquad BandPass(float hz, float q) { var f = new Biquad(); f.SetBandPass(hz, q); return f; }

            public void SetLowPass(float hz, float q)
            {
                Prepare(hz, q, out double cs, out double alpha);
                Set((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
            }

            public void SetHighPass(float hz, float q)
            {
                Prepare(hz, q, out double cs, out double alpha);
                Set((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
            }

            /// <summary>Band-pass with 0 dB peak gain.</summary>
            public void SetBandPass(float hz, float q)
            {
                Prepare(hz, q, out double cs, out double alpha);
                Set(alpha, 0, -alpha, 1 + alpha, -2 * cs, 1 - alpha);
            }

            private static void Prepare(float hz, float q, out double cs, out double alpha)
            {
                double f = hz > 10f ? hz : 10f;
                if (f > SampleRate * 0.45) f = SampleRate * 0.45;
                double qq = q > 0.05f ? q : 0.05;
                double w = TwoPi * f / SampleRate;
                cs = Math.Cos(w);
                alpha = Math.Sin(w) / (2 * qq);
            }

            private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                _b0 = (float)(b0 / a0); _b1 = (float)(b1 / a0); _b2 = (float)(b2 / a0);
                _a1 = (float)(a1 / a0); _a2 = (float)(a2 / a0);
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                if (float.IsNaN(y) || float.IsInfinity(y)) { y = 0f; _y1 = _y2 = _x1 = _x2 = 0f; }
                if (y > -1e-20f && y < 1e-20f) y = 0f;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }

        /// <summary>In-place one-pole low-pass over a whole buffer.</summary>
        public static void LowPass(float[] buf, float cutoffHz)
        {
            if (buf == null) return;
            var f = new OnePole(cutoffHz);
            for (int i = 0; i < buf.Length; i++) buf[i] = f.LowPass(buf[i]);
        }

        /// <summary>In-place one-pole high-pass over a whole buffer.</summary>
        public static void HighPass(float[] buf, float cutoffHz)
        {
            if (buf == null) return;
            var f = new OnePole(cutoffHz);
            for (int i = 0; i < buf.Length; i++) buf[i] = f.HighPass(buf[i]);
        }

        // ==============================================================================================
        // Delay / reverb
        // ==============================================================================================

        /// <summary>Single damped feedback delay line (echo).</summary>
        public sealed class FeedbackDelay
        {
            private readonly float[] _line;
            private int _idx;
            private readonly float _feedback, _mix;
            private OnePole _damp;

            public FeedbackDelay(float seconds, float feedback, float dampingHz, float mix)
            {
                _line = new float[Math.Max(1, Samples(seconds))];
                _feedback = MathUtil.Clamp(feedback, 0f, 0.95f);
                _mix = MathUtil.Clamp01(mix);
                _damp = new OnePole(dampingHz);
            }

            public float Process(float x)
            {
                float d = _line[_idx];
                _line[_idx] = x + _damp.LowPass(d) * _feedback;
                if (++_idx >= _line.Length) _idx = 0;
                return x * (1f - _mix) + d * _mix;
            }
        }

        /// <summary>
        /// Small Schroeder/Freeverb-style reverb: four damped feedback combs in parallel, two allpasses in
        /// series. Deterministic and stable (feedback is clamped below 1).
        /// </summary>
        public sealed class Reverb
        {
            private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356 };
            private static readonly int[] AllpassLengths = { 556, 441 };
            private readonly float[][] _combs = new float[4][];
            private readonly int[] _combIdx = new int[4];
            private readonly float[] _combLp = new float[4];
            private readonly float[][] _aps = new float[2][];
            private readonly int[] _apIdx = new int[2];
            private readonly float _feedback, _damp, _wet;

            /// <param name="roomScale">Scales delay lengths (0.3..2).</param>
            /// <param name="feedback">Comb feedback (0..0.95); higher = longer tail.</param>
            /// <param name="damping">High-frequency damping in the tail (0..1).</param>
            /// <param name="wet">Wet mix (0..1); dry is 1 - wet/2.</param>
            public Reverb(float roomScale, float feedback, float damping, float wet)
            {
                float s = MathUtil.Clamp(roomScale, 0.3f, 2f);
                for (int i = 0; i < 4; i++) _combs[i] = new float[Math.Max(1, (int)(CombLengths[i] * s))];
                for (int i = 0; i < 2; i++) _aps[i] = new float[Math.Max(1, (int)(AllpassLengths[i] * s))];
                _feedback = MathUtil.Clamp(feedback, 0f, 0.95f);
                _damp = MathUtil.Clamp01(damping);
                _wet = MathUtil.Clamp01(wet);
            }

            public float Process(float x)
            {
                float input = x * 0.25f;
                float acc = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float[] line = _combs[i];
                    int k = _combIdx[i];
                    float y = line[k];
                    _combLp[i] = y * (1f - _damp) + _combLp[i] * _damp;
                    if (_combLp[i] > -1e-20f && _combLp[i] < 1e-20f) _combLp[i] = 0f;
                    line[k] = input + _combLp[i] * _feedback;
                    if (++k >= line.Length) k = 0;
                    _combIdx[i] = k;
                    acc += y;
                }
                for (int i = 0; i < 2; i++)
                {
                    float[] line = _aps[i];
                    int k = _apIdx[i];
                    float b = line[k];
                    float y = -acc + b;
                    line[k] = acc + b * 0.5f;
                    if (++k >= line.Length) k = 0;
                    _apIdx[i] = k;
                    acc = y;
                }
                return x * (1f - _wet * 0.5f) + acc * _wet;
            }
        }

        /// <summary>Runs a buffer through a <see cref="Reverb"/> in place.</summary>
        public static void ApplyReverb(float[] buf, float roomScale, float feedback, float damping, float wet)
        {
            if (buf == null) return;
            var r = new Reverb(roomScale, feedback, damping, wet);
            for (int i = 0; i < buf.Length; i++) buf[i] = r.Process(buf[i]);
        }

        // ==============================================================================================
        // Music theory + sequencer
        // ==============================================================================================

        public enum Mode : byte { Ionian, Dorian, Phrygian, Lydian, Mixolydian, Aeolian, MajorPentatonic, MinorPentatonic }

        private static readonly int[][] ModeIntervals =
        {
            new[] { 0, 2, 4, 5, 7, 9, 11 },   // Ionian (major)
            new[] { 0, 2, 3, 5, 7, 9, 10 },   // Dorian
            new[] { 0, 1, 3, 5, 7, 8, 10 },   // Phrygian
            new[] { 0, 2, 4, 6, 7, 9, 11 },   // Lydian
            new[] { 0, 2, 4, 5, 7, 9, 10 },   // Mixolydian
            new[] { 0, 2, 3, 5, 7, 8, 10 },   // Aeolian (natural minor)
            new[] { 0, 2, 4, 7, 9 },          // major pentatonic
            new[] { 0, 3, 5, 7, 10 },         // minor pentatonic
        };

        /// <summary>Semitone offsets of the scale (copy).</summary>
        public static int[] ScaleIntervals(Mode mode) => (int[])ModeIntervals[ModeIndex(mode)].Clone();

        /// <summary>Number of tones per octave in the scale.</summary>
        public static int ScaleLength(Mode mode) => ModeIntervals[ModeIndex(mode)].Length;

        private static int ModeIndex(Mode mode)
        {
            int m = (int)mode;
            return m >= 0 && m < ModeIntervals.Length ? m : 0;
        }

        /// <summary>MIDI note of a scale degree (0 = root; wraps into octaves, negatives go down).</summary>
        public static int ScaleNote(int rootMidi, Mode mode, int degree)
        {
            int[] s = ModeIntervals[ModeIndex(mode)];
            int n = s.Length;
            int oct = degree >= 0 ? degree / n : -((-degree + n - 1) / n);
            int idx = degree - oct * n;
            return rootMidi + oct * 12 + s[idx];
        }

        /// <summary>Equal-tempered frequency of a MIDI note (A4 = 69 = 440 Hz).</summary>
        public static float MidiToHz(float midi) => 440f * (float)Math.Pow(2.0, (midi - 69f) / 12.0);

        /// <summary>Fills <paramref name="output"/> with a chord stacked in scale thirds on <paramref name="degree"/>.</summary>
        public static void ChordNotes(int rootMidi, Mode mode, int degree, int[] output)
        {
            if (output == null) return;
            for (int i = 0; i < output.Length; i++) output[i] = ScaleNote(rootMidi, mode, degree + 2 * i);
        }

        /// <summary>A scheduled note.</summary>
        public struct NoteEvent
        {
            public float Time;       // seconds from buffer start
            public float Duration;   // seconds (gate)
            public float Midi;
            public float Velocity;   // 0..1
        }

        /// <summary>
        /// Arpeggiates a chord progression: for each degree, plays <paramref name="notesPerChord"/> chord tones
        /// (4-note stacked-thirds chord, rising through octaves; ping-pong when <paramref name="upDown"/>).
        /// </summary>
        public static List<NoteEvent> Arpeggiate(int rootMidi, Mode mode, int[] progression, int notesPerChord,
            float stepSeconds, float noteSeconds, float velocity, bool upDown, float startSeconds = 0f)
        {
            var list = new List<NoteEvent>();
            if (progression == null || notesPerChord <= 0) return list;
            var tones = new int[4];
            float t = Math.Max(0f, startSeconds);
            for (int c = 0; c < progression.Length; c++)
            {
                ChordNotes(rootMidi, mode, progression[c], tones);
                for (int k = 0; k < notesPerChord; k++)
                {
                    int pos = k;
                    if (upDown && notesPerChord > 1)
                    {
                        int period = 2 * (notesPerChord - 1);
                        pos = k % period;
                        if (pos >= notesPerChord) pos = period - pos;
                    }
                    int midi = tones[pos % tones.Length] + 12 * (pos / tones.Length);
                    list.Add(new NoteEvent
                    {
                        Time = t,
                        Duration = noteSeconds,
                        Midi = midi,
                        Velocity = MathUtil.Clamp01(velocity * (k == 0 ? 1f : 0.82f))
                    });
                    t += stepSeconds;
                }
            }
            return list;
        }

        public enum Instrument : byte { Pluck, Harp, Bell, Chime, Pad }

        /// <summary>Renders (adds) a list of notes into a buffer with the given instrument.</summary>
        public static void RenderNotes(float[] buf, IList<NoteEvent> notes, Instrument instrument, uint seed)
        {
            if (buf == null || notes == null) return;
            var rng = new Xorshift32(seed);
            for (int i = 0; i < notes.Count; i++)
            {
                NoteEvent n = notes[i];
                RenderNote(buf, (int)(n.Time * SampleRate), MidiToHz(n.Midi), n.Duration, n.Velocity, instrument, ref rng);
            }
        }

        /// <summary>Renders (adds) one note.</summary>
        public static void RenderNote(float[] buf, int start, float hz, float seconds, float velocity, Instrument instrument, ref Xorshift32 rng)
        {
            if (buf == null || start < 0 || start >= buf.Length || !(hz > 0f)) return;
            switch (instrument)
            {
                case Instrument.Pluck: RenderPluck(buf, start, hz, seconds, velocity, 0.8f, 0.994f, ref rng); break;
                case Instrument.Harp:
                    RenderPluck(buf, start, hz, seconds, velocity * 0.85f, 0.55f, 0.9975f, ref rng);
                    RenderDampedSine(buf, start, hz, 0.9f, velocity * 0.12f, 0.004f);
                    break;
                case Instrument.Bell: RenderBell(buf, start, hz, velocity, 1f); break;
                case Instrument.Chime: RenderChime(buf, start, hz, velocity, 0.7f); break;
                default: RenderPadNote(buf, start, hz, seconds, velocity, new Adsr(0.35f, 0.3f, 0.75f, 0.8f), 1800f); break;
            }
        }

        // ==============================================================================================
        // Voice renderers (add into a buffer)
        // ==============================================================================================

        /// <summary>Adds an exponentially decaying sine (phasor recurrence, double precision).</summary>
        public static void RenderDampedSine(float[] buf, int start, float hz, float tau, float amp, float attackSeconds = 0.002f)
        {
            if (buf == null || start < 0 || start >= buf.Length || !(hz > 0f) || hz >= SampleRate * 0.45f || !(tau > 0f)) return;
            int len = Math.Min(buf.Length - start, Samples(tau * 7f));
            double w = TwoPi * hz / SampleRate;
            double c = Math.Cos(w), s = Math.Sin(w);
            double re = 1.0, im = 0.0;
            float env = amp;
            float k = DecayFactor(tau);
            int attack = Math.Max(1, Samples(attackSeconds));
            for (int i = 0; i < len; i++)
            {
                float a = i < attack ? (float)i / attack : 1f;
                buf[start + i] += (float)im * env * a;
                double nre = re * c - im * s;
                im = re * s + im * c;
                re = nre;
                env *= k;
            }
        }

        /// <summary>Karplus-Strong plucked string.</summary>
        public static void RenderPluck(float[] buf, int start, float hz, float seconds, float amp, float brightness, float decay, ref Xorshift32 rng)
        {
            if (buf == null || start < 0 || start >= buf.Length || !(hz > 20f)) return;
            int period = MathUtil.Clamp((int)Math.Round(SampleRate / hz), 2, 4000);
            var line = new float[period];
            float lp = 0f, mean = 0f;
            float coef = 0.2f + 0.8f * MathUtil.Clamp01(brightness);
            for (int i = 0; i < period; i++)
            {
                lp += (rng.NextSigned() - lp) * coef;
                line[i] = lp;
                mean += lp;
            }
            mean /= period;
            for (int i = 0; i < period; i++) line[i] -= mean;
            int len = Math.Min(buf.Length - start, Samples(seconds));
            int fade = Math.Min(len, Samples(0.03f));
            float d = MathUtil.Clamp(decay, 0.9f, 0.9995f);
            int idx = 0;
            for (int n = 0; n < len; n++)
            {
                int next = idx + 1;
                if (next == period) next = 0;
                float cur = line[idx];
                line[idx] = d * 0.5f * (cur + line[next]);
                float f = n >= len - fade ? (float)(len - n) / fade : 1f;
                buf[start + n] += cur * amp * f;
                idx = next;
            }
        }

        private static readonly float[] BellRatios = { 0.5f, 1.0f, 1.19f, 1.56f, 2.0f, 2.51f, 2.66f, 3.01f, 4.1f };
        private static readonly float[] BellAmps = { 0.35f, 1.0f, 0.5f, 0.42f, 0.38f, 0.24f, 0.2f, 0.14f, 0.1f };
        private static readonly float[] BellTaus = { 1.5f, 1.1f, 0.8f, 0.6f, 0.45f, 0.32f, 0.26f, 0.18f, 0.12f };

        /// <summary>Additive inharmonic church bell.</summary>
        public static void RenderBell(float[] buf, int start, float hz, float amp, float decayScale)
        {
            float ds = MathUtil.Clamp(decayScale, 0.1f, 4f);
            for (int p = 0; p < BellRatios.Length; p++)
                RenderDampedSine(buf, start, hz * BellRatios[p], BellTaus[p] * ds, amp * BellAmps[p] * 0.4f, 0.0015f);
        }

        /// <summary>Marimba/glass chime: sine plus inharmonic overtones.</summary>
        public static void RenderChime(float[] buf, int start, float hz, float amp, float tau)
        {
            RenderDampedSine(buf, start, hz, tau, amp * 0.6f, 0.003f);
            RenderDampedSine(buf, start, hz * 2.76f, tau * 0.3f, amp * 0.22f, 0.002f);
            RenderDampedSine(buf, start, hz * 5.4f, tau * 0.12f, amp * 0.1f, 0.001f);
        }

        /// <summary>Detuned-saw pad note through a low-pass filter.</summary>
        public static void RenderPadNote(float[] buf, int start, float hz, float gate, float amp, Adsr env, float cutoffHz)
        {
            if (buf == null || start < 0 || start >= buf.Length || !(hz > 0f)) return;
            int len = Math.Min(buf.Length - start, Samples(env.Length(gate)));
            var lp1 = Biquad.LowPass(cutoffHz, 0.7f);
            float p1 = 0f, p2 = 0.33f, p3 = 0.67f;
            float h1 = hz * 0.996f, h2 = hz, h3 = hz * 1.004f;
            for (int i = 0; i < len; i++)
            {
                float t = i * InvSampleRate;
                float s = SawBlep(p1, h1 * InvSampleRate) + SawBlep(p2, h2 * InvSampleRate) + SawBlep(p3, h3 * InvSampleRate);
                buf[start + i] += lp1.Process(s * 0.33f) * env.Evaluate(t, gate) * amp;
                p1 = Advance(p1, h1); p2 = Advance(p2, h2); p3 = Advance(p3, h3);
            }
        }

        /// <summary>Pitched drum: sine with a falling pitch envelope plus a noise click.</summary>
        private static void RenderDrum(float[] buf, int start, float f0, float f1, float pitchTau, float ampTau, float amp, float noiseAmp, ref Xorshift32 rng)
        {
            if (buf == null || start < 0 || start >= buf.Length) return;
            int len = Math.Min(buf.Length - start, Samples(ampTau * 6f));
            float pe = 1f, pk = DecayFactor(pitchTau);
            float ae = 1f, ak = DecayFactor(ampTau);
            float ne = 1f, nk = DecayFactor(0.012f);
            var lp = new OnePole(900f);
            float ph = 0f;
            for (int i = 0; i < len; i++)
            {
                float f = f1 + (f0 - f1) * pe;
                float body = Sine(ph) * ae;
                float click = lp.LowPass(rng.NextSigned()) * ne * noiseAmp;
                float a = i < 40 ? i / 40f : 1f;
                buf[start + i] += (body + click) * amp * a;
                ph = Advance(ph, f);
                pe *= pk; ae *= ak; ne *= nk;
            }
        }

        /// <summary>Thunder: a high-passed noise crack with crackles, then a band-limited brown-noise rumble with random bursts.</summary>
        private static void RenderThunder(float[] buf, int start, ref Xorshift32 rng, float crackAmount, float crackTau, float rumbleAmount, float rumbleSeconds)
        {
            if (buf == null || start < 0 || start >= buf.Length) return;
            var hp = Biquad.HighPass(1800f, 0.7f);
            int crackLen = Math.Min(buf.Length - start, Samples(0.5f));
            float ce = 1f, ck = DecayFactor(crackTau);
            float crackle = 0f, crk = DecayFactor(0.003f);
            for (int i = 0; i < crackLen; i++)
            {
                float t = i * InvSampleRate;
                if (rng.NextFloat() < 0.0035f * Exp(-t / 0.15f)) crackle = rng.NextSigned() * 2.5f;
                float x = rng.NextSigned() * ce + crackle * rng.NextSigned();
                buf[start + i] += hp.Process(x) * crackAmount;
                ce *= ck; crackle *= crk;
            }

            const int Bursts = 6;
            var bStart = new int[Bursts];
            var bEnv = new float[Bursts];
            var bK = new float[Bursts];
            var bAmp = new float[Bursts];
            for (int b = 0; b < Bursts; b++)
            {
                bStart[b] = Samples(b == 0 ? 0.01f : rng.Range(0.05f, rumbleSeconds * 0.55f));
                bK[b] = DecayFactor(rng.Range(0.25f, 0.9f) * Math.Max(0.3f, rumbleSeconds * 0.4f));
                bAmp[b] = b == 0 ? 1f : rng.Range(0.3f, 0.9f);
            }
            int attack = Samples(0.06f);
            var lp1 = Biquad.LowPass(170f, 0.6f);
            var lp2 = Biquad.LowPass(320f, 0.5f);
            float brown = 0f;
            int len = Math.Min(buf.Length - start, Samples(rumbleSeconds + 0.6f));
            for (int i = 0; i < len; i++)
            {
                float env = 0f;
                for (int b = 0; b < Bursts; b++)
                {
                    int rel = i - bStart[b];
                    if (rel < 0) continue;
                    if (rel < attack) env += bAmp[b] * rel / attack;
                    else { bEnv[b] = rel == attack ? bAmp[b] : bEnv[b] * bK[b]; env += bEnv[b]; }
                }
                brown = (brown + 0.05f * rng.NextSigned()) * 0.996f;
                buf[start + i] += lp2.Process(lp1.Process(brown)) * env * rumbleAmount * 6f;
            }
        }

        private static void AddGrains(float[] buf, ref Xorshift32 rng, int count, float fromSec, float toSec,
            float minHz, float maxHz, float minTau, float maxTau, float minAmp, float maxAmp)
        {
            for (int g = 0; g < count; g++)
            {
                int s = Samples(rng.Range(fromSec, toSec));
                RenderDampedSine(buf, s, rng.Range(minHz, maxHz), rng.Range(minTau, maxTau), rng.Range(minAmp, maxAmp), 0.0008f);
            }
        }

        // ==============================================================================================
        // Post-processing
        // ==============================================================================================

        /// <summary>Replaces NaN/Infinity with 0.</summary>
        public static void Sanitize(float[] buf)
        {
            if (buf == null) return;
            for (int i = 0; i < buf.Length; i++)
            {
                float v = buf[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) buf[i] = 0f;
            }
        }

        /// <summary>Largest absolute sample.</summary>
        public static float Peak(float[] buf)
        {
            float m = 0f;
            if (buf == null) return 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float a = Math.Abs(buf[i]);
                if (a > m) m = a;
            }
            return m;
        }

        /// <summary>Scales the buffer so its peak equals <paramref name="peak"/> (silence stays silent).</summary>
        public static void Normalize(float[] buf, float peak)
        {
            float m = Peak(buf);
            if (!(m > 1e-9f)) return;
            float g = MathUtil.Clamp(peak, 0f, 1f) / m;
            for (int i = 0; i < buf.Length; i++)
            {
                float v = buf[i] * g;
                buf[i] = v > 1f ? 1f : (v < -1f ? -1f : v);
            }
        }

        /// <summary>Linear fade-in and fade-out.</summary>
        public static void ApplyFades(float[] buf, float fadeInSeconds, float fadeOutSeconds)
        {
            if (buf == null || buf.Length == 0) return;
            int fi = Math.Min(buf.Length, fadeInSeconds > 0f ? Samples(fadeInSeconds) : 0);
            int fo = Math.Min(buf.Length, fadeOutSeconds > 0f ? Samples(fadeOutSeconds) : 0);
            for (int i = 0; i < fi; i++) buf[i] *= (float)i / fi;
            for (int i = 0; i < fo; i++) buf[buf.Length - 1 - i] *= (float)i / fo;
        }

        /// <summary>Soft saturation; |result| &lt;= 1 for any finite input.</summary>
        public static float SoftClip(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>
        /// Turns <paramref name="src"/> (at least loopFrames + crossfade frames long, interleaved with
        /// <paramref name="channels"/>) into a seamless loop of exactly <paramref name="loopFrames"/> frames by
        /// equal-power crossfading the overhang into the head.
        /// </summary>
        public static float[] MakeSeamlessLoop(float[] src, int loopFrames, int channels)
        {
            if (src == null) return new float[0];
            channels = Math.Max(1, channels);
            loopFrames = Math.Max(1, Math.Min(loopFrames, src.Length / channels));
            int xf = Math.Min(loopFrames, src.Length / channels - loopFrames);
            var dst = new float[loopFrames * channels];
            Array.Copy(src, dst, dst.Length);
            for (int i = 0; i < xf; i++)
            {
                float u = (i + 0.5f) / xf;
                float gIn = (float)Math.Sin(u * Math.PI * 0.5);
                float gOut = (float)Math.Cos(u * Math.PI * 0.5);
                for (int c = 0; c < channels; c++)
                {
                    int k = i * channels + c;
                    dst[k] = src[k] * gIn + src[(loopFrames + i) * channels + c] * gOut;
                }
            }
            return dst;
        }

        /// <summary>Sanitize, remove DC, fade edges and normalize (one-shots).</summary>
        public static float[] Finish(float[] buf, float fadeInSeconds, float fadeOutSeconds, float peak = TargetPeak)
        {
            Sanitize(buf);
            HighPass(buf, 12f);
            ApplyFades(buf, fadeInSeconds, fadeOutSeconds);
            Normalize(buf, peak);
            return buf;
        }

        private static float[] FinishLoop(float[] buf, int loopSamples, float peak = TargetPeak)
        {
            Sanitize(buf);
            HighPass(buf, 12f);
            float[] loop = MakeSeamlessLoop(buf, loopSamples, 1);
            Normalize(loop, peak);
            return loop;
        }

        private static float[] Alloc(float seconds) => new float[Samples(seconds)];
        private static int At(float seconds) => (int)(seconds * SampleRate);

        // ==============================================================================================
        // Generators: UI
        // ==============================================================================================

        /// <summary>Short crisp UI tick.</summary>
        public static float[] UiClick(uint seed = 1)
        {
            var buf = Alloc(UiClickSeconds);
            var rng = new Xorshift32(seed ^ 0xC11Cu);
            var hp = new OnePole(3000f);
            float p1 = 0f, p2 = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float tone = Sine(p1) * Exp(-t / 0.006f) * 0.7f + Sine(p2) * Exp(-t / 0.018f) * 0.35f;
                float n = hp.HighPass(rng.NextSigned()) * Exp(-t / 0.0015f) * 0.6f;
                buf[i] = tone + n;
                p1 = Advance(p1, 2600f);
                p2 = Advance(p2, 1300f);
            }
            return Finish(buf, 0.0005f, 0.02f, 0.7f);
        }

        /// <summary>Two descending muted blips (rejected command).</summary>
        public static float[] UiError(uint seed = 2)
        {
            var buf = Alloc(UiErrorSeconds);
            var lp = Biquad.LowPass(1600f, 0.7f);
            float ph = 0f;
            int half = buf.Length / 2;
            for (int i = 0; i < buf.Length; i++)
            {
                bool second = i >= half;
                float t = (second ? i - half : i) * InvSampleRate;
                float f = second ? 220f + 40f * Exp(-t / 0.03f) : 330f + 60f * Exp(-t / 0.03f);
                float env = Math.Min(1f, t / 0.004f) * Exp(-t / 0.045f);
                buf[i] = lp.Process(SquareBlep(ph, f * InvSampleRate)) * env;
                ph = Advance(ph, f);
            }
            return Finish(buf, 0.001f, 0.01f, 0.6f);
        }

        /// <summary>Selection blip themed by power category.</summary>
        public static float[] PowerSelect(GodPowerCategory category, uint seed = 3)
        {
            var buf = Alloc(PowerSelectSeconds);
            var rng = new Xorshift32(seed + (uint)category * 7919u);
            float ph = 0f;
            switch (category)
            {
                case GodPowerCategory.Blessing:
                    RenderChime(buf, 0, MidiToHz(84), 0.8f, 0.22f);
                    RenderChime(buf, At(0.07f), MidiToHz(88), 0.7f, 0.2f);
                    RenderChime(buf, At(0.14f), MidiToHz(91), 0.5f, 0.18f);
                    break;
                case GodPowerCategory.Shaping:
                {
                    var lp = new OnePole(500f);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        float t = i * InvSampleRate;
                        float f = 70f + 130f * Exp(-t / 0.06f);
                        buf[i] = Triangle(ph) * Exp(-t / 0.11f) + lp.LowPass(rng.NextSigned()) * Exp(-t / 0.03f) * 1.5f;
                        ph = Advance(ph, f);
                    }
                    break;
                }
                case GodPowerCategory.Creation:
                {
                    float len = PowerSelectSeconds;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        float t = i * InvSampleRate;
                        float u = t / len;
                        float f = 700f * (float)Math.Pow(3.0, Math.Min(1f, u * 1.3f));
                        float env = (float)Math.Sin(Math.PI * MathUtil.Clamp01(u)) * (0.8f + 0.2f * Sine(t * 18f));
                        buf[i] = Sine(ph) * env * 0.6f;
                        ph = Advance(ph, f);
                    }
                    AddGrains(buf, ref rng, 6, 0.02f, 0.22f, 2500f, 6000f, 0.03f, 0.07f, 0.1f, 0.25f);
                    break;
                }
                default: // Wrath
                {
                    var lp = Biquad.LowPass(900f, 0.9f);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        float t = i * InvSampleRate;
                        float f = 55f + 60f * Exp(-t / 0.12f);
                        float env = Math.Min(1f, t / 0.005f) * Exp(-t / 0.13f);
                        buf[i] = SoftClip(lp.Process(SawBlep(ph, f * InvSampleRate)) * 2.2f) * env;
                        ph = Advance(ph, f);
                    }
                    break;
                }
            }
            return Finish(buf, 0.0005f, 0.04f, 0.75f);
        }

        // ==============================================================================================
        // Generators: god power casts
        // ==============================================================================================

        /// <summary>Cast sound for a power category (blessing choir, shaping rumble, creation shimmer, wrath thunder).</summary>
        public static float[] Cast(GodPowerCategory category, uint seed = 10)
        {
            switch (category)
            {
                case GodPowerCategory.Blessing: return BlessingChoir(seed);
                case GodPowerCategory.Shaping: return ShapingRumble(seed);
                case GodPowerCategory.Creation: return CreationShimmer(seed);
                default: return WrathThunder(seed);
            }
        }

        public static float CastSeconds(GodPowerCategory category)
        {
            switch (category)
            {
                case GodPowerCategory.Blessing: return BlessingCastSeconds;
                case GodPowerCategory.Shaping: return ShapingCastSeconds;
                case GodPowerCategory.Creation: return CreationCastSeconds;
                default: return WrathCastSeconds;
            }
        }

        /// <summary>Angelic "aah" choir pad: detuned saws through vowel formants, with reverb.</summary>
        public static float[] BlessingChoir(uint seed = 10)
        {
            var buf = Alloc(BlessingCastSeconds);
            var rng = new Xorshift32(seed ^ 0xB1E55u);
            int[] notes = { 48, 55, 60, 64, 67, 72 };
            float[] detune = { 0.9954f, 1f, 1.004f };
            int voices = notes.Length * detune.Length;
            var hz = new float[voices];
            var ph = new float[voices];
            var vib = new float[notes.Length];
            for (int n = 0; n < notes.Length; n++)
            {
                vib[n] = rng.NextFloat();
                for (int d = 0; d < detune.Length; d++)
                {
                    hz[n * 3 + d] = MidiToHz(notes[n]) * detune[d];
                    ph[n * 3 + d] = rng.NextFloat();
                }
            }
            var env = new Adsr(0.6f, 0.4f, 0.85f, 1.1f);
            const float gate = 1.9f;
            var f1 = Biquad.BandPass(750f, 4f);
            var f2 = Biquad.BandPass(1150f, 5f);
            var f3 = Biquad.BandPass(2800f, 7f);
            var body = Biquad.LowPass(500f, 0.7f);
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float e = env.Evaluate(t, gate);
                if (e <= 0f && t > gate) { buf[i] = 0f; continue; }
                float sum = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float v = 1f + 0.0025f * Sine(vib[n] + t * 5.2f);
                    for (int d = 0; d < 3; d++)
                    {
                        int k = n * 3 + d;
                        float f = hz[k] * v;
                        sum += SawBlep(ph[k], f * InvSampleRate);
                        ph[k] = Advance(ph[k], f);
                    }
                }
                sum *= e / voices;
                buf[i] = f1.Process(sum) * 1.0f + f2.Process(sum) * 0.6f + f3.Process(sum) * 0.3f + body.Process(sum) * 0.25f;
            }
            RenderChime(buf, At(0.25f), MidiToHz(96), 0.04f, 0.6f);
            RenderChime(buf, At(0.45f), MidiToHz(103), 0.03f, 0.6f);
            ApplyReverb(buf, 1.3f, 0.82f, 0.4f, 0.35f);
            return Finish(buf, 0.01f, 0.3f);
        }

        /// <summary>Grinding earth rumble for terrain shaping.</summary>
        public static float[] ShapingRumble(uint seed = 11)
        {
            var buf = Alloc(ShapingCastSeconds);
            var rng = new Xorshift32(seed ^ 0x5A4Bu);
            var lp = Biquad.LowPass(140f, 0.7f);
            var grind = Biquad.BandPass(420f, 2f);
            float brown = 0f, sub = 0f;
            float a1 = rng.NextFloat(), a2 = rng.NextFloat();
            float len = ShapingCastSeconds;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float env = Math.Min(1f, t / 0.25f) * SmoothStep((len - t) / 0.9f);
                float am = 0.65f + 0.35f * Sine(a1 + t * 3.1f) * Sine(a2 + t * 1.7f);
                brown = (brown + 0.04f * rng.NextSigned()) * 0.997f;
                float rumble = lp.Process(brown) * 8f * am;
                float s = Sine(sub) * 0.5f;
                sub = Advance(sub, 48f - 10f * t / len);
                float g = grind.Process(rng.NextSigned()) * (0.5f + 0.5f * Sine(t * 11f)) * 0.35f;
                buf[i] = (rumble + s + g) * env;
            }
            AddGrains(buf, ref rng, 16, 0.1f, len - 0.6f, 180f, 520f, 0.015f, 0.04f, 0.1f, 0.3f);
            ApplyReverb(buf, 0.8f, 0.7f, 0.5f, 0.2f);
            return Finish(buf, 0.005f, 0.2f);
        }

        /// <summary>Twinkling high pentatonic chimes over a rising glide (creation).</summary>
        public static float[] CreationShimmer(uint seed = 12)
        {
            var buf = Alloc(CreationCastSeconds);
            var rng = new Xorshift32(seed ^ 0xC2EAu);
            for (int g = 0; g < 28; g++)
            {
                float t0 = rng.Range(0f, 1.7f);
                int midi = ScaleNote(84, Mode.MajorPentatonic, rng.Range(0, 9));
                float hz = MidiToHz(midi);
                float amp = rng.Range(0.25f, 0.6f);
                float tau = rng.Range(0.3f, 0.75f);
                RenderDampedSine(buf, At(t0), hz, tau, amp, 0.01f);
                RenderDampedSine(buf, At(t0), hz * 2f, tau * 0.4f, amp * 0.25f, 0.006f);
            }
            float ph = 0f;
            var hp = new OnePole(6000f);
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float u = MathUtil.Clamp01(t / 1.8f);
                float f = 520f * (float)Math.Pow(3.0, u);
                float s = (float)Math.Sin(Math.PI * u);
                float env = s * s * (0.85f + 0.15f * Sine(t * 9f));
                buf[i] += Sine(ph) * env * 0.25f + hp.HighPass(rng.NextSigned()) * env * 0.05f;
                ph = Advance(ph, f);
            }
            ApplyReverb(buf, 1.5f, 0.84f, 0.3f, 0.45f);
            return Finish(buf, 0.005f, 0.3f);
        }

        /// <summary>Rolling thunder for wrath powers.</summary>
        public static float[] WrathThunder(uint seed = 13)
        {
            var buf = Alloc(WrathCastSeconds);
            var rng = new Xorshift32(seed ^ 0x7A1Bu);
            RenderThunder(buf, 0, ref rng, 0.55f, 0.05f, 1f, 2.6f);
            ApplyReverb(buf, 1.4f, 0.78f, 0.5f, 0.25f);
            return Finish(buf, 0.002f, 0.4f);
        }

        // ==============================================================================================
        // Generators: disasters
        // ==============================================================================================

        /// <summary>Sharp lightning crack with sizzle and a short rumble tail.</summary>
        public static float[] LightningCrack(uint seed = 20)
        {
            var buf = Alloc(LightningSeconds);
            var rng = new Xorshift32(seed ^ 0x11A7u);
            int click = Math.Min(buf.Length, Samples(0.003f));
            for (int i = 0; i < click; i++) buf[i] += (1f - (float)i / click) * (i % 2 == 0 ? 1f : -1f) * 0.8f;
            RenderThunder(buf, 0, ref rng, 1f, 0.03f, 0.45f, 1.5f);
            var bp = Biquad.BandPass(4200f, 1.5f);
            int sizzle = Math.Min(buf.Length, Samples(0.4f));
            for (int i = 0; i < sizzle; i++)
                buf[i] += bp.Process(rng.NextSigned()) * Exp(-i * InvSampleRate / 0.08f) * 0.6f;
            ApplyReverb(buf, 1.2f, 0.75f, 0.5f, 0.2f);
            return Finish(buf, 0f, 0.3f);
        }

        /// <summary>Meteor: accelerating whoosh, then a deep boom with debris.</summary>
        public static float[] MeteorImpact(uint seed = 21)
        {
            var buf = Alloc(MeteorSeconds);
            var rng = new Xorshift32(seed ^ 0x3E7Eu);
            int impact = At(MeteorImpactTime);
            var bp = Biquad.BandPass(250f, 1.2f);
            var lpw = Biquad.LowPass(200f, 0.7f);
            float brown = 0f, whistle = 0f;
            for (int i = 0; i < impact && i < buf.Length; i++)
            {
                float p = (float)i / impact;
                float center = 250f * (float)Math.Pow(10.0, p);
                if ((i & 31) == 0) bp.SetBandPass(center, 1.2f);
                float amp = p * p * p * (0.75f + 0.25f * Sine(i * InvSampleRate * 23f));
                brown = (brown + 0.05f * rng.NextSigned()) * 0.996f;
                buf[i] = bp.Process(rng.NextSigned()) * amp + lpw.Process(brown) * 4f * p * p * 0.4f + Sine(whistle) * 0.05f * p * p;
                whistle = Advance(whistle, center * 0.5f);
            }
            var lpb = Biquad.LowPass(260f, 0.7f);
            var hp = Biquad.HighPass(2000f, 0.7f);
            float ph = 0f;
            float be = 1f, bk = DecayFactor(0.75f);
            float ne = 1f, nk = DecayFactor(0.35f);
            float ce = 1f, ck = DecayFactor(0.02f);
            float pe = 1f, pk = DecayFactor(0.18f);
            for (int i = impact; i < buf.Length; i++)
            {
                float f = 26f + 70f * pe;
                float w = rng.NextSigned();
                buf[i] += Sine(ph) * be * 1.2f + lpb.Process(w) * ne * 2.5f + hp.Process(w) * ce * 0.6f;
                ph = Advance(ph, f);
                be *= bk; ne *= nk; ce *= ck; pe *= pk;
            }
            AddGrains(buf, ref rng, 30, MeteorImpactTime + 0.05f, MeteorImpactTime + 1.6f, 150f, 900f, 0.02f, 0.06f, 0.05f, 0.25f);
            for (int i = impact; i < buf.Length; i++) buf[i] = SoftClip(buf[i] * 1.3f);
            ApplyReverb(buf, 1.4f, 0.8f, 0.5f, 0.25f);
            return Finish(buf, 0.01f, 0.4f);
        }

        /// <summary>Deep beating earthquake rumble with rock crumbles.</summary>
        public static float[] EarthquakeRumble(uint seed = 22)
        {
            var buf = Alloc(EarthquakeSeconds);
            var rng = new Xorshift32(seed ^ 0xEA27u);
            var lp1 = Biquad.LowPass(90f, 0.7f);
            var lp2 = Biquad.LowPass(110f, 0.6f);
            float brown = 0f, s1 = 0f, s2 = 0f;
            float r1 = rng.NextFloat(), r2 = rng.NextFloat(), r3 = rng.NextFloat();
            float len = EarthquakeSeconds;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float env = SmoothStep(t / 0.7f) * SmoothStep((len - t) / 1.3f);
                float am = 0.6f + 0.4f * (Sine(r1 + t * 0.7f) + Sine(r2 + t * 1.9f) + Sine(r3 + t * 3.3f)) / 3f;
                brown = (brown + 0.05f * rng.NextSigned()) * 0.997f;
                float rumble = lp2.Process(lp1.Process(brown)) * 10f;
                float sub = 0.6f * Sine(s1) + 0.4f * Sine(s2);
                s1 = Advance(s1, 30f);
                s2 = Advance(s2, 37f);
                buf[i] = (rumble + sub * 0.6f) * am * env;
            }
            AddGrains(buf, ref rng, 40, 0.3f, len - 0.8f, 200f, 700f, 0.015f, 0.05f, 0.05f, 0.2f);
            ApplyReverb(buf, 0.9f, 0.7f, 0.5f, 0.15f);
            return Finish(buf, 0.01f, 0.3f);
        }

        /// <summary>Water surge for floods: swelling filtered noise with bubbles.</summary>
        public static float[] FloodSurge(uint seed = 23)
        {
            var buf = Alloc(FloodSeconds);
            var rng = new Xorshift32(seed ^ 0xF100u);
            var lp = Biquad.LowPass(250f, 0.7f);
            float brown = 0f;
            float len = FloodSeconds;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float u = t / len;
                float s = (float)Math.Sin(Math.PI * MathUtil.Clamp01(u));
                if ((i & 31) == 0) lp.SetLowPass(250f + 850f * s, 0.8f);
                brown = (brown + 0.05f * rng.NextSigned()) * 0.99f;
                float env = (float)Math.Pow(s, 1.5);
                buf[i] = lp.Process(rng.NextSigned() * 0.5f + brown * 3f) * env;
            }
            AddGrains(buf, ref rng, 60, 0.2f, len - 0.4f, 300f, 900f, 0.006f, 0.015f, 0.05f, 0.15f);
            ApplyReverb(buf, 0.9f, 0.7f, 0.5f, 0.2f);
            return Finish(buf, 0.05f, 0.3f);
        }

        /// <summary>Ominous low dissonant toll (plague, drought, famine).</summary>
        public static float[] Omen(uint seed = 24)
        {
            var buf = Alloc(OmenSeconds);
            var rng = new Xorshift32(seed ^ 0x0E1Eu);
            RenderBell(buf, 0, MidiToHz(41), 1f, 1.7f);
            RenderBell(buf, At(0.45f), MidiToHz(42), 0.35f, 1.4f);
            var lp = Biquad.LowPass(80f, 0.7f);
            float brown = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                brown = (brown + 0.05f * rng.NextSigned()) * 0.997f;
                buf[i] += lp.Process(brown) * 2f * SmoothStep(t / 0.8f) * SmoothStep((OmenSeconds - t) / 1.5f);
            }
            ApplyReverb(buf, 1.6f, 0.82f, 0.45f, 0.35f);
            return Finish(buf, 0.003f, 0.5f);
        }

        // ==============================================================================================
        // Generators: ambience loops (seamless)
        // ==============================================================================================

        /// <summary>Seamless crackling-fire loop.</summary>
        public static float[] FireCrackleLoop(uint seed = 30)
        {
            int n = Samples(FireLoopSeconds);
            var buf = new float[n + Samples(0.5f)];
            var rng = new Xorshift32(seed ^ 0xF12Eu);
            AddGrains(buf, ref rng, (int)(6 * FireLoopSeconds), 0f, (buf.Length - 1) * InvSampleRate, 250f, 700f, 0.01f, 0.03f, 0.3f, 0.7f);
            var lpBase = Biquad.LowPass(700f, 0.7f);
            var lpRoar = Biquad.LowPass(180f, 0.7f);
            var hpCrk = Biquad.HighPass(1500f, 0.7f);
            const int Slots = 4;
            var cEnv = new float[Slots];
            var cK = new float[Slots];
            int slot = 0;
            float brown = 0f;
            float r = rng.NextFloat();
            float rate = 45f * InvSampleRate;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                if (rng.NextFloat() < rate)
                {
                    cEnv[slot] = rng.Range(0.3f, 1f);
                    cK[slot] = DecayFactor(rng.Range(0.001f, 0.004f));
                    slot = (slot + 1) % Slots;
                }
                float c = 0f;
                for (int k = 0; k < Slots; k++)
                {
                    if (cEnv[k] <= 1e-4f) continue;
                    c += cEnv[k] * rng.NextSigned();
                    cEnv[k] *= cK[k];
                }
                brown = (brown + 0.05f * rng.NextSigned()) * 0.995f;
                float roar = lpRoar.Process(brown) * 3f * (0.8f + 0.2f * Sine(r + t * 0.9f));
                buf[i] += lpBase.Process(rng.NextSigned()) * 0.12f + roar * 0.5f + hpCrk.Process(c) * 1.2f;
            }
            return FinishLoop(buf, n);
        }

        /// <summary>Seamless rain loop: hiss plus droplets.</summary>
        public static float[] RainLoop(uint seed = 31)
        {
            int n = Samples(RainLoopSeconds);
            var buf = new float[n + Samples(0.6f)];
            var rng = new Xorshift32(seed ^ 0x2A11u);
            var hp = Biquad.HighPass(500f, 0.7f);
            var lp = Biquad.LowPass(7000f, 0.7f);
            var body = Biquad.BandPass(1200f, 0.7f);
            float r = rng.NextFloat();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float w = rng.NextSigned();
                float am = 0.9f + 0.1f * Sine(r + t * 0.37f);
                buf[i] = (lp.Process(hp.Process(w)) * 0.35f + body.Process(w) * 0.15f) * am;
            }
            float total = (buf.Length - 1) * InvSampleRate;
            AddGrains(buf, ref rng, (int)(140 * total), 0f, total, 2000f, 7000f, 0.002f, 0.006f, 0.05f, 0.2f);
            AddGrains(buf, ref rng, (int)(10 * total), 0f, total, 700f, 1600f, 0.008f, 0.015f, 0.1f, 0.25f);
            return FinishLoop(buf, n);
        }

        /// <summary>Seamless wind loop: band-passed noise with a wandering center and gusts.</summary>
        public static float[] WindLoop(uint seed = 32)
        {
            int n = Samples(WindLoopSeconds);
            var buf = new float[n + Samples(1f)];
            var rng = new Xorshift32(seed ^ 0x3172u);
            var bp = Biquad.BandPass(420f, 1.4f);
            var whistle = Biquad.BandPass(1300f, 9f);
            var low = Biquad.LowPass(140f, 0.7f);
            float r1 = rng.NextFloat(), r2 = rng.NextFloat(), r3 = rng.NextFloat(), r4 = rng.NextFloat();
            float brown = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float lfo = (Sine(r1 + t * 0.07f) + Sine(r2 + t * 0.13f) + Sine(r3 + t * 0.31f)) / 3f;
                float gust = 0.5f + 0.5f * Sine(r4 + t * 0.11f) * Sine(r2 + t * 0.23f);
                if ((i & 63) == 0)
                {
                    float c = 420f * (float)Math.Pow(2.0, 1.2 * lfo);
                    bp.SetBandPass(c, 1.4f);
                    whistle.SetBandPass(c * 3f, 9f);
                }
                float w = rng.NextSigned();
                brown = (brown + 0.05f * w) * 0.996f;
                buf[i] = (bp.Process(w) * 0.8f + whistle.Process(w) * 0.25f * Math.Max(0f, lfo) + low.Process(brown) * 1.5f) * (0.35f + 0.65f * gust);
            }
            return FinishLoop(buf, n);
        }

        /// <summary>Seamless nature loop: rustling leaves, bird phrases and faint crickets.</summary>
        public static float[] NatureLoop(uint seed = 33)
        {
            int n = Samples(NatureLoopSeconds);
            var buf = new float[n + Samples(1f)];
            var rng = new Xorshift32(seed ^ 0xB12Du);
            var hp = Biquad.HighPass(1800f, 0.7f);
            var lp = Biquad.LowPass(6500f, 0.7f);
            float r1 = rng.NextFloat(), r2 = rng.NextFloat();
            float cr = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i * InvSampleRate;
                float gust = 0.5f + 0.5f * Sine(r1 + t * 0.17f) * Sine(r2 + t * 0.41f);
                float leaves = lp.Process(hp.Process(rng.NextSigned())) * (0.05f + 0.07f * gust);
                // crickets: 4.6 kHz pulsed at 28 Hz in bursts
                float burst = Frac(t / 1.1f) < 0.35f ? 1f : 0f;
                float pulse = Frac(t * 28f) < 0.4f ? 1f : 0f;
                float cricket = Sine(cr) * burst * pulse * 0.025f;
                cr = Advance(cr, 4600f);
                buf[i] = leaves + cricket;
            }
            float total = (buf.Length - 1) * InvSampleRate;
            int phrases = 6;
            for (int p = 0; p < phrases; p++)
            {
                float t0 = rng.Range(0f, total - 1.2f);
                float baseHz = rng.Range(2400f, 4200f);
                int chirps = rng.Range(3, 8);
                float spacing = rng.Range(0.08f, 0.16f);
                float dur = rng.Range(0.05f, 0.11f);
                float ratio = rng.Chance(0.5f) ? rng.Range(1.15f, 1.5f) : rng.Range(0.7f, 0.88f);
                float amp = rng.Range(0.12f, 0.3f);
                for (int c = 0; c < chirps; c++)
                {
                    int s0 = At(t0 + c * spacing);
                    int len = Samples(dur);
                    float ph = 0f;
                    float h0 = baseHz * rng.Range(0.95f, 1.05f);
                    for (int i = 0; i < len && s0 + i < buf.Length; i++)
                    {
                        float u = (float)i / len;
                        float f = h0 * (1f + (ratio - 1f) * u);
                        float e = (float)Math.Sin(Math.PI * u);
                        e *= e;
                        buf[s0 + i] += (Sine(ph) + 0.15f * Sine(ph * 2f)) * e * amp;
                        ph = Advance(ph, f);
                    }
                }
            }
            return FinishLoop(buf, n);
        }

        // ==============================================================================================
        // Generators: stingers
        // ==============================================================================================

        private static readonly Mode[] AgeModes =
        {
            Mode.MinorPentatonic, Mode.MajorPentatonic, Mode.Dorian, Mode.Mixolydian, Mode.Aeolian,
            Mode.Ionian, Mode.Dorian, Mode.Mixolydian, Mode.Ionian, Mode.Lydian
        };
        private static readonly int[] AgeRootOffsets = { 0, 2, 3, 5, 7, 8, 10, 12, 14, 15 };
        private static readonly int[] BellPattern = { 0, 2, 4, 5, 7, 9 };

        private static int AgeIndex(AgeId age) => MathUtil.Clamp((int)age, 0, AgeModes.Length - 1);

        /// <summary>Scale used by the age-advancement bell of an age.</summary>
        public static Mode AgeBellMode(AgeId age) => AgeModes[AgeIndex(age)];

        /// <summary>Root MIDI note of the age-advancement bell (rises with each age).</summary>
        public static int AgeBellRoot(AgeId age) => 55 + AgeRootOffsets[AgeIndex(age)];

        /// <summary>Bell peal for reaching a new age; later ages ring more, higher bells in brighter scales.</summary>
        public static float[] AgeBell(AgeId age)
        {
            var buf = Alloc(AgeBellSeconds);
            int a = AgeIndex(age);
            Mode mode = AgeBellMode(age);
            int root = AgeBellRoot(age);
            int notes = 3 + a / 3;
            float decay = 1.4f - a * 0.04f;
            const float step = 0.34f;
            for (int k = 0; k < notes; k++)
                RenderBell(buf, At(step * k), MidiToHz(ScaleNote(root, mode, BellPattern[k])), 0.8f - 0.04f * k, decay);
            int last = At(step * notes);
            RenderBell(buf, last, MidiToHz(root + 12), 1f, decay);
            RenderBell(buf, last, MidiToHz(root), 0.5f, decay * 1.2f);
            ApplyReverb(buf, 1.3f, 0.8f, 0.4f, 0.28f);
            return Finish(buf, 0.002f, 0.4f);
        }

        /// <summary>Pounding war drums pattern.</summary>
        public static float[] WarDrums(uint seed = 40)
        {
            var buf = Alloc(WarDrumsSeconds);
            var rng = new Xorshift32(seed ^ 0xD2D2u);
            int[] steps = { 0, 2, 4, 5, 6, 8, 10, 11, 12 };
            float[] vel = { 1f, 0.75f, 1f, 0.6f, 0.7f, 1f, 0.75f, 0.65f, 1.1f };
            const float step = 0.27f;
            for (int i = 0; i < steps.Length; i++)
            {
                int s = At(steps[i] * step + rng.Range(0f, 0.012f));
                bool big = steps[i] % 4 == 0;
                if (big) RenderDrum(buf, s, 150f, 52f, 0.05f, 0.32f, vel[i], 0.8f, ref rng);
                else RenderDrum(buf, s, 220f, 95f, 0.03f, 0.18f, vel[i] * 0.8f, 1.1f, ref rng);
            }
            for (int i = 0; i < buf.Length; i++) buf[i] = SoftClip(buf[i] * 1.5f);
            ApplyReverb(buf, 1.6f, 0.78f, 0.5f, 0.22f);
            return Finish(buf, 0.001f, 0.4f);
        }

        /// <summary>A few hammer taps (wood knock + metal ping).</summary>
        public static float[] ConstructionHammer(uint seed = 41)
        {
            var buf = Alloc(HammerSeconds);
            var rng = new Xorshift32(seed ^ 0x4A33u);
            int taps = rng.Range(3, 5);
            var hp = Biquad.HighPass(1200f, 0.7f);
            for (int k = 0; k < taps; k++)
            {
                float t0 = 0.05f + k * 0.29f + rng.Range(-0.03f, 0.03f);
                float v = (k == taps - 1 ? 1f : 0.75f) * rng.Range(0.85f, 1f);
                int s = At(t0);
                float f = 820f * rng.Range(0.94f, 1.06f);
                RenderDampedSine(buf, s, f, 0.03f, 0.6f * v, 0.0005f);
                RenderDampedSine(buf, s, f * 2.3f, 0.012f, 0.3f * v, 0.0005f);
                RenderDampedSine(buf, s, 160f, 0.03f, 0.35f * v, 0.001f);
                int len = Math.Min(buf.Length - s, Samples(0.03f));
                for (int i = 0; i < len; i++) buf[s + i] += hp.Process(rng.NextSigned()) * Exp(-i * InvSampleRate / 0.0035f) * 0.7f * v;
            }
            ApplyReverb(buf, 0.6f, 0.6f, 0.5f, 0.12f);
            return Finish(buf, 0f, 0.1f);
        }

        /// <summary>Lydian harp glissando and strummed chord for completed wonders.</summary>
        public static float[] WonderHarp(uint seed = 42)
        {
            var buf = Alloc(HarpSeconds);
            var notes = new List<NoteEvent>();
            const int root = 55;
            for (int k = 0; k < 14; k++)
                notes.Add(new NoteEvent { Time = 0.07f * k, Duration = 2.6f, Midi = ScaleNote(root, Mode.Lydian, k), Velocity = 0.55f + 0.02f * k });
            var tones = new int[4];
            ChordNotes(root + 12, Mode.Lydian, 0, tones);
            for (int k = 0; k < tones.Length; k++)
                notes.Add(new NoteEvent { Time = 1.15f + 0.025f * k, Duration = 2.7f, Midi = tones[k], Velocity = 0.9f });
            notes.Add(new NoteEvent { Time = 1.15f, Duration = 2.7f, Midi = root, Velocity = 0.8f });
            RenderNotes(buf, notes, Instrument.Harp, seed ^ 0x4A2Bu);
            ApplyReverb(buf, 1.5f, 0.82f, 0.4f, 0.35f);
            return Finish(buf, 0.001f, 0.5f);
        }

        /// <summary>Clashing blades over a scuffle.</summary>
        public static float[] BattleClash(uint seed = 43)
        {
            var buf = Alloc(BattleSeconds);
            var rng = new Xorshift32(seed ^ 0xBA77u);
            float[] ratios = { 1f, 2.76f, 5.4f, 8.93f, 1.5f };
            float[] amps = { 1f, 0.6f, 0.35f, 0.2f, 0.4f };
            float[] taus = { 0.22f, 0.12f, 0.06f, 0.03f, 0.15f };
            float[] times = { 0f, 0.2f + rng.Range(0f, 0.06f), 0.47f + rng.Range(0f, 0.08f) };
            var hp = Biquad.HighPass(2500f, 0.7f);
            for (int c = 0; c < times.Length; c++)
            {
                int s = At(times[c]);
                float f = 1100f * rng.Range(0.85f, 1.2f);
                float v = rng.Range(0.7f, 1f);
                for (int p = 0; p < ratios.Length; p++) RenderDampedSine(buf, s, f * ratios[p], taus[p], amps[p] * v * 0.4f, 0.0005f);
                RenderDampedSine(buf, s, 120f, 0.05f, 0.4f * v, 0.002f);
                int len = Math.Min(buf.Length - s, Samples(0.04f));
                for (int i = 0; i < len; i++) buf[s + i] += hp.Process(rng.NextSigned()) * Exp(-i * InvSampleRate / 0.006f) * v;
            }
            var bp = Biquad.BandPass(500f, 0.8f);
            for (int i = 0; i < buf.Length; i++)
            {
                float u = (float)i / buf.Length;
                buf[i] += bp.Process(rng.NextSigned()) * (float)Math.Sin(Math.PI * u) * 0.15f;
            }
            ApplyReverb(buf, 1f, 0.7f, 0.5f, 0.18f);
            return Finish(buf, 0f, 0.2f);
        }

        /// <summary>Hopeful plucked arpeggio with a soft pad for a newly founded settlement.</summary>
        public static float[] SettlementFounded(uint seed = 44)
        {
            var buf = Alloc(SettlementSeconds);
            var notes = Arpeggiate(62, Mode.MajorPentatonic, new[] { 0 }, 5, 0.11f, 1.5f, 0.8f, false);
            RenderNotes(buf, notes, Instrument.Pluck, seed ^ 0x5E77u);
            RenderPadNote(buf, 0, MidiToHz(50), 0.9f, 0.25f, new Adsr(0.3f, 0.2f, 0.8f, 0.8f), 900f);
            ApplyReverb(buf, 1.2f, 0.78f, 0.4f, 0.28f);
            return Finish(buf, 0.001f, 0.3f);
        }

        /// <summary>Radiant chime arpeggio over a pad for legendary chronicle entries.</summary>
        public static float[] Legendary(uint seed = 45)
        {
            var buf = Alloc(LegendarySeconds);
            var notes = Arpeggiate(72, Mode.Lydian, new[] { 0, 4, 0 }, 4, 0.16f, 1.5f, 0.7f, true);
            RenderNotes(buf, notes, Instrument.Chime, seed ^ 0x1E6Du);
            var tones = new int[3];
            ChordNotes(60, Mode.Lydian, 0, tones);
            var env = new Adsr(0.5f, 0.3f, 0.8f, 1.2f);
            for (int k = 0; k < tones.Length; k++) RenderPadNote(buf, 0, MidiToHz(tones[k]), 1.6f, 0.2f, env, 1400f);
            ApplyReverb(buf, 1.5f, 0.83f, 0.4f, 0.4f);
            return Finish(buf, 0.005f, 0.4f);
        }
    }
}
