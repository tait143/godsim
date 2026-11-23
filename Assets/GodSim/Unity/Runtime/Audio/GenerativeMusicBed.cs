using System;
using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// Endless, Unity-free generative ambient music: four slowly gliding pad voices that follow a weighted
    /// random chord walk in the current mode, a root drone, sparse bell-like melody notes, and a stereo
    /// feedback delay. Deterministic for a seed and a given sequence of <see cref="SetHarmony"/> calls.
    /// <para>
    /// Threading: <see cref="Render(float[], int)"/> may run on the audio thread (Unity PCM reader callback)
    /// and never allocates. <see cref="SetHarmony"/> may be called from the main thread at any time: it only
    /// writes volatile fields, which the renderer applies at the next chord change (smooth transitions).
    /// Output is soft-limited so |sample| &lt;= 0.9.
    /// </para>
    /// </summary>
    public sealed class GenerativeMusicBed
    {
        private const int Voices = 4;
        private const int Bells = 4;
        private const float OutputGain = 0.9f;
        private static readonly int[] DegreeChoices = { 0, 3, 4, 5, 1, 2, 6 };
        private static readonly float[] DegreeWeights = { 3f, 2.2f, 2f, 1.8f, 1.2f, 1f, 0.5f };
        private static readonly int[] BellDegrees = { 0, 2, 4, 1, 5, 7 };

        private readonly float _sr;
        private ProceduralSynth.Xorshift32 _rng;

        // Requested harmony (written by any thread).
        private volatile int _reqMode;
        private volatile int _reqRoot;
        private volatile float _reqBrightness;
        private volatile float _reqDensity;
        private volatile int _reqVersion;
        private int _appliedVersion = -1;

        // Applied harmony.
        private ProceduralSynth.Mode _mode = ProceduralSynth.Mode.Dorian;
        private int _root = 45;
        private float _brightness = 0.4f;
        private float _density = 0.3f;

        // Pads.
        private readonly float[] _hz = new float[Voices];
        private readonly float[] _targetHz = new float[Voices];
        private readonly int[] _midi = new int[Voices];
        private readonly float[] _phaseA = new float[Voices];
        private readonly float[] _phaseB = new float[Voices];
        private readonly float[] _amp = new float[Voices];
        private readonly float[] _targetAmp = new float[Voices];
        private readonly float[] _lfo = new float[Voices];
        private readonly float[] _lfoRate = new float[Voices];
        private readonly int[] _tones = new int[Voices];
        private int _degree;
        private int _chordCountdown;
        private readonly float _glide, _ampGlide;
        private ProceduralSynth.OnePole _lpL1, _lpL2, _lpR1, _lpR2;
        private int _coefCountdown;
        private float _cutoffPhase;

        // Drone.
        private float _dronePhase, _droneHz, _droneTargetHz, _swellPhase;

        // Bells.
        private readonly float[] _bellPhase = new float[Bells];
        private readonly float[] _bellHz = new float[Bells];
        private readonly float[] _bellEnv = new float[Bells];
        private readonly float[] _bellPan = new float[Bells];
        private readonly int[] _bellAge = new int[Bells];
        private int _nextBell;
        private int _bellCountdown;
        private readonly float _bellDecay;
        private readonly int _bellAttack;

        // Stereo delay.
        private readonly float[] _dl, _dr;
        private int _dli, _dri;
        private float _dlDamp, _drDamp;

        private long _frames;

        /// <summary>Creates a bed; output starts with a 3 s fade-in.</summary>
        public GenerativeMusicBed(uint seed, int sampleRate = ProceduralSynth.SampleRate)
        {
            _sr = Math.Max(8000, sampleRate);
            _rng = new ProceduralSynth.Xorshift32(seed ^ 0x6D75u);
            _glide = 1f - (float)Math.Exp(-1.0 / (1.6 * _sr));
            _ampGlide = 1f - (float)Math.Exp(-1.0 / (2.0 * _sr));
            _bellDecay = (float)Math.Exp(-1.0 / (1.8 * _sr));
            _bellAttack = Math.Max(1, (int)(0.012f * _sr));
            _dl = new float[(int)(0.41f * _sr)];
            _dr = new float[(int)(0.57f * _sr)];
            _lpL1 = new ProceduralSynth.OnePole(900f);
            _lpL2 = new ProceduralSynth.OnePole(900f);
            _lpR1 = new ProceduralSynth.OnePole(900f);
            _lpR2 = new ProceduralSynth.OnePole(900f);
            for (int v = 0; v < Voices; v++)
            {
                _lfo[v] = _rng.NextFloat();
                _lfoRate[v] = _rng.Range(0.05f, 0.17f);
                _phaseA[v] = _rng.NextFloat();
                _phaseB[v] = _rng.NextFloat();
                _midi[v] = _root + 12 + 3 * v;
            }
            _reqMode = (int)_mode;
            _reqRoot = _root;
            _reqBrightness = _brightness;
            _reqDensity = _density;
            _reqVersion = 0;
            _degree = -1;
            NextChord();
            for (int v = 0; v < Voices; v++) { _hz[v] = _targetHz[v]; }
            _droneHz = _droneTargetHz;
            _bellCountdown = (int)(_sr * 4f);
        }

        /// <summary>Currently applied mode (changes at chord boundaries).</summary>
        public ProceduralSynth.Mode CurrentMode => _mode;
        /// <summary>Currently applied root MIDI note.</summary>
        public int CurrentRoot => _root;
        /// <summary>Frames rendered so far.</summary>
        public long FramesRendered => _frames;

        /// <summary>Requests a new harmony (applied at the next chord change). Thread-safe, non-blocking.</summary>
        public void SetHarmony(ProceduralSynth.Mode mode, int rootMidi, float brightness, float density)
        {
            _reqMode = (int)mode;
            _reqRoot = MathUtil.Clamp(rootMidi, 24, 84);
            _reqBrightness = MathUtil.Clamp01(MathUtil.Sanitize(brightness, 0.4f));
            _reqDensity = MathUtil.Clamp01(MathUtil.Sanitize(density, 0.3f));
            _reqVersion = _reqVersion + 1;
        }

        /// <summary>Forces the pending harmony to apply now (starts a new chord).</summary>
        public void ApplyHarmonyNow() => _chordCountdown = 0;

        /// <summary>Fills the whole interleaved buffer (Unity PCMReaderCallback shape).</summary>
        public void Render(float[] data, int channels)
        {
            if (data == null) return;
            int ch = Math.Max(1, channels);
            Render(data, 0, data.Length / ch, ch);
        }

        /// <summary>Renders <paramref name="frames"/> interleaved frames starting at frame <paramref name="offsetFrames"/>.</summary>
        public void Render(float[] data, int offsetFrames, int frames, int channels)
        {
            if (data == null) return;
            int ch = Math.Max(1, channels);
            if (offsetFrames < 0) offsetFrames = 0;
            int maxFrames = data.Length / ch - offsetFrames;
            if (frames > maxFrames) frames = maxFrames;
            float inv = 1f / _sr;
            for (int f = 0; f < frames; f++)
            {
                if (--_chordCountdown <= 0) NextChord();
                if (--_coefCountdown <= 0)
                {
                    _coefCountdown = 64;
                    float cutoff = 260f + 1500f * _brightness + 300f * ProceduralSynth.Sine(_cutoffPhase);
                    _lpL1.SetCutoff(cutoff); _lpL2.SetCutoff(cutoff);
                    _lpR1.SetCutoff(cutoff * 1.05f); _lpR2.SetCutoff(cutoff * 1.05f);
                }
                _cutoffPhase += 0.045f * inv;
                if (_cutoffPhase >= 1f) _cutoffPhase -= 1f;

                // Pads.
                float l = 0f, r = 0f;
                for (int v = 0; v < Voices; v++)
                {
                    _hz[v] += (_targetHz[v] - _hz[v]) * _glide;
                    _amp[v] += (_targetAmp[v] - _amp[v]) * _ampGlide;
                    float ha = _hz[v] * 1.003f, hb = _hz[v] * 0.997f;
                    float a = ProceduralSynth.Triangle(_phaseA[v]) * 0.65f + ProceduralSynth.SawBlep(_phaseA[v], ha * inv) * 0.25f;
                    float b = ProceduralSynth.Triangle(_phaseB[v]) * 0.65f + ProceduralSynth.SawBlep(_phaseB[v], hb * inv) * 0.25f;
                    _phaseA[v] += ha * inv; if (_phaseA[v] >= 1f) _phaseA[v] -= 1f;
                    _phaseB[v] += hb * inv; if (_phaseB[v] >= 1f) _phaseB[v] -= 1f;
                    _lfo[v] += _lfoRate[v] * inv; if (_lfo[v] >= 1f) _lfo[v] -= 1f;
                    float g = _amp[v] * (0.7f + 0.3f * ProceduralSynth.Sine(_lfo[v]));
                    l += (a * 0.7f + b * 0.3f) * g;
                    r += (a * 0.3f + b * 0.7f) * g;
                }
                l = _lpL2.LowPass(_lpL1.LowPass(l)) * 0.2f;
                r = _lpR2.LowPass(_lpR1.LowPass(r)) * 0.2f;

                // Drone.
                _droneHz += (_droneTargetHz - _droneHz) * _glide;
                _dronePhase += _droneHz * inv; if (_dronePhase >= 1f) _dronePhase -= 1f;
                _swellPhase += 0.031f * inv; if (_swellPhase >= 1f) _swellPhase -= 1f;
                float drone = ProceduralSynth.Sine(_dronePhase) * 0.1f * (0.65f + 0.35f * ProceduralSynth.Sine(_swellPhase));
                l += drone; r += drone;

                // Bells.
                if (--_bellCountdown <= 0) TriggerBell();
                for (int k = 0; k < Bells; k++)
                {
                    if (_bellEnv[k] <= 0f) continue;
                    float e = _bellEnv[k];
                    if (_bellAge[k] < _bellAttack) { e *= (float)_bellAge[k] / _bellAttack; _bellAge[k]++; }
                    else { _bellEnv[k] *= _bellDecay; if (_bellEnv[k] < 1e-4f) _bellEnv[k] = 0f; }
                    float p = _bellPhase[k];
                    float s = (ProceduralSynth.Sine(p) + 0.25f * ProceduralSynth.Sine(p * 2f) + 0.08f * ProceduralSynth.Sine(p * 3.01f)) * e * 0.12f;
                    _bellPhase[k] = p + _bellHz[k] * inv;
                    if (_bellPhase[k] >= 1f) _bellPhase[k] -= 1f;
                    l += s * (1f - _bellPan[k]);
                    r += s * _bellPan[k];
                }

                // Stereo feedback delay with cross-feed.
                float dl = _dl[_dli], dr = _dr[_dri];
                _dlDamp += (dl - _dlDamp) * 0.35f;
                _drDamp += (dr - _drDamp) * 0.35f;
                _dl[_dli] = l + _drDamp * 0.42f;
                _dr[_dri] = r + _dlDamp * 0.42f;
                if (++_dli >= _dl.Length) _dli = 0;
                if (++_dri >= _dr.Length) _dri = 0;
                float outL = l + dl * 0.32f;
                float outR = r + dr * 0.32f;

                float fade = _frames < (long)(3f * _sr) ? _frames / (3f * _sr) : 1f;
                _frames++;
                outL = ProceduralSynth.SoftClip(outL * 1.6f * fade) * OutputGain;
                outR = ProceduralSynth.SoftClip(outR * 1.6f * fade) * OutputGain;
                if (float.IsNaN(outL) || float.IsInfinity(outL)) outL = 0f;
                if (float.IsNaN(outR) || float.IsInfinity(outR)) outR = 0f;

                int baseIdx = (offsetFrames + f) * ch;
                if (ch == 1) data[baseIdx] = 0.5f * (outL + outR);
                else
                {
                    data[baseIdx] = outL;
                    data[baseIdx + 1] = outR;
                    for (int c = 2; c < ch; c++) data[baseIdx + c] = 0.5f * (outL + outR);
                }
            }
        }

        private void ApplyPending()
        {
            int ver = _reqVersion;
            if (ver == _appliedVersion) return;
            _appliedVersion = ver;
            int m = _reqMode;
            _mode = m >= 0 && m <= (int)ProceduralSynth.Mode.MinorPentatonic ? (ProceduralSynth.Mode)m : ProceduralSynth.Mode.Dorian;
            _root = _reqRoot;
            _brightness = _reqBrightness;
            _density = _reqDensity;
        }

        private void NextChord()
        {
            ApplyPending();
            // Weighted pick, avoiding an immediate repeat.
            float total = 0f;
            for (int i = 0; i < DegreeChoices.Length; i++) if (DegreeChoices[i] != _degree) total += DegreeWeights[i];
            float pick = _rng.NextFloat() * total;
            int deg = 0;
            for (int i = 0; _degree >= 0 && i < DegreeChoices.Length; i++)
            {
                if (DegreeChoices[i] == _degree) continue;
                pick -= DegreeWeights[i];
                if (pick <= 0f) { deg = DegreeChoices[i]; break; }
            }
            _degree = deg;
            ProceduralSynth.ChordNotes(_root, _mode, deg, _tones);
            for (int v = 0; v < Voices; v++)
            {
                int target = v == 0 ? _tones[0] : _tones[v] + 12;
                // Voice leading: move each voice to the nearest octave of its new chord tone.
                int prev = _midi[v];
                while (target - prev > 6) target -= 12;
                while (prev - target > 6) target += 12;
                int lo = _root + (v == 0 ? -5 : 5), hi = _root + (v == 0 ? 12 : 29);
                while (target < lo) target += 12;
                while (target > hi) target -= 12;
                _midi[v] = target;
                _targetHz[v] = ProceduralSynth.MidiToHz(target);
                _targetAmp[v] = _rng.Chance(0.15f) ? 0.35f : _rng.Range(0.75f, 1f);
            }
            _droneTargetHz = ProceduralSynth.MidiToHz(_root - 12);
            float seconds = _rng.Range(7f, 12f) / (0.75f + 0.5f * _density);
            _chordCountdown = Math.Max(1, (int)(seconds * _sr));
        }

        private void TriggerBell()
        {
            int k = _nextBell;
            _nextBell = (_nextBell + 1) % Bells;
            int deg = _degree + BellDegrees[_rng.Range(0, BellDegrees.Length)];
            int midi = ProceduralSynth.ScaleNote(_root + 24, _mode, deg);
            if (midi > 96) midi -= 12;
            _bellHz[k] = ProceduralSynth.MidiToHz(midi);
            _bellPhase[k] = 0f;
            _bellEnv[k] = _rng.Range(0.6f, 1f);
            _bellAge[k] = 0;
            _bellPan[k] = _rng.Range(0.2f, 0.8f);
            float gap = _rng.Range(1.5f, 6f) / (0.4f + 1.2f * _density);
            _bellCountdown = Math.Max(1, (int)(gap * _sr));
        }
    }
}
