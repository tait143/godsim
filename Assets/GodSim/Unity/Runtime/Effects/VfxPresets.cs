using UnityEngine;

namespace GodSim.Unity.Effects
{
    /// <summary>Particle effect archetypes. Every one is built and configured in code (no assets).</summary>
    public enum VfxKind : byte
    {
        Dust = 0, Clash, Explosion, RockDust, Sparkles, Motes, Mist, Splash,
        Flame, Smoke, Embers, LightPillar, HeatHaze, Rain, Snow
    }

    /// <summary>How alpha evolves over a particle's life.</summary>
    public enum VfxFade : byte { Out, InOut }

    /// <summary>How size evolves over a particle's life.</summary>
    public enum VfxGrowth : byte { Constant, Grow, Shrink }

    /// <summary>Static description of one <see cref="VfxKind"/>. Sizes/speeds are for a 1-unit (one tile) effect.</summary>
    public sealed class VfxDef
    {
        public VfxKind Kind;
        public bool Additive;
        public float LifeMin = 1f, LifeMax = 1f;
        public float SpeedMin = 1f, SpeedMax = 1f;
        public float SizeMin = 0.2f, SizeMax = 0.4f;
        public float Gravity;
        public Color ColorA = Color.white, ColorB = Color.white;
        public ParticleSystemShapeType Shape = ParticleSystemShapeType.Sphere;
        public float Radius = 0.5f;
        public float Angle = 25f;
        public Vector3 BoxSize = new Vector3(1f, 1f, 0.1f);
        /// <summary>Particles for a one-shot burst (size 1).</summary>
        public int Burst = 20;
        /// <summary>Particles per second while playing continuously (size 1).</summary>
        public float Rate = 10f;
        public int MaxParticles = 256;
        public bool Stretch;
        public float LengthScale = 2f;
        public float VelocityScale;
        public VfxFade Fade = VfxFade.Out;
        public VfxGrowth Growth = VfxGrowth.Constant;
        /// <summary>Emitter local +Z points up (true) or down (false, precipitation).</summary>
        public bool EmitUp = true;
        /// <summary>Pooled emitters of this kind (oldest burst is recycled when exhausted).</summary>
        public int PoolCap = 16;
        public float MaxScreenSize = 0.5f;
    }

    /// <summary>The preset table, indexed by (int)<see cref="VfxKind"/>.</summary>
    public static class VfxPresets
    {
        private static VfxDef[] _defs;

        public static int Count => 15;

        public static VfxDef Get(VfxKind kind)
        {
            if (_defs == null) _defs = Build();
            int i = (int)kind;
            return i >= 0 && i < _defs.Length ? _defs[i] : _defs[0];
        }

        private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        private static VfxDef[] Build()
        {
            var d = new VfxDef[Count];
            d[(int)VfxKind.Dust] = new VfxDef
            {
                Kind = VfxKind.Dust, LifeMin = 1.2f, LifeMax = 2.4f, SpeedMin = 0.4f, SpeedMax = 1.4f, SizeMin = 0.6f, SizeMax = 1.4f,
                Gravity = -0.02f, ColorA = C(0.62f, 0.55f, 0.45f, 0.55f), ColorB = C(0.5f, 0.45f, 0.38f, 0.45f),
                Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.6f, Burst = 18, Rate = 12f, Fade = VfxFade.InOut, Growth = VfxGrowth.Grow, PoolCap = 24
            };
            d[(int)VfxKind.Clash] = new VfxDef
            {
                Kind = VfxKind.Clash, Additive = true, LifeMin = 0.25f, LifeMax = 0.5f, SpeedMin = 2f, SpeedMax = 4.5f, SizeMin = 0.06f, SizeMax = 0.14f,
                Gravity = 0.6f, ColorA = C(1f, 0.9f, 0.6f), ColorB = C(1f, 0.6f, 0.2f), Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f,
                Burst = 26, Rate = 30f, Stretch = true, LengthScale = 2.5f, Fade = VfxFade.Out, Growth = VfxGrowth.Shrink, PoolCap = 16
            };
            d[(int)VfxKind.Explosion] = new VfxDef
            {
                Kind = VfxKind.Explosion, Additive = true, LifeMin = 0.4f, LifeMax = 0.9f, SpeedMin = 2f, SpeedMax = 6f, SizeMin = 0.8f, SizeMax = 2f,
                Gravity = -0.1f, ColorA = C(1f, 0.8f, 0.3f), ColorB = C(1f, 0.35f, 0.05f), Shape = ParticleSystemShapeType.Sphere, Radius = 0.5f,
                Burst = 40, Rate = 40f, Fade = VfxFade.Out, Growth = VfxGrowth.Grow, PoolCap = 8, MaxScreenSize = 1f
            };
            d[(int)VfxKind.RockDust] = new VfxDef
            {
                Kind = VfxKind.RockDust, LifeMin = 1.5f, LifeMax = 3f, SpeedMin = 0.8f, SpeedMax = 2.5f, SizeMin = 0.5f, SizeMax = 1.5f,
                Gravity = 0.15f, ColorA = C(0.45f, 0.4f, 0.35f, 0.7f), ColorB = C(0.35f, 0.3f, 0.28f, 0.6f),
                Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.8f, Burst = 26, Rate = 14f, Fade = VfxFade.InOut, Growth = VfxGrowth.Grow, PoolCap = 16
            };
            d[(int)VfxKind.Sparkles] = new VfxDef
            {
                Kind = VfxKind.Sparkles, Additive = true, LifeMin = 1.2f, LifeMax = 2.4f, SpeedMin = 0.2f, SpeedMax = 0.8f, SizeMin = 0.08f, SizeMax = 0.2f,
                Gravity = -0.12f, ColorA = C(1f, 0.85f, 0.35f), ColorB = C(1f, 0.95f, 0.7f), Shape = ParticleSystemShapeType.Circle, Radius = 1f,
                Burst = 40, Rate = 45f, Fade = VfxFade.InOut, Growth = VfxGrowth.Shrink, PoolCap = 16
            };
            d[(int)VfxKind.Motes] = new VfxDef
            {
                Kind = VfxKind.Motes, Additive = true, LifeMin = 1.5f, LifeMax = 3f, SpeedMin = 0.1f, SpeedMax = 0.4f, SizeMin = 0.08f, SizeMax = 0.18f,
                Gravity = -0.05f, ColorA = C(0.5f, 1f, 0.4f), ColorB = C(0.8f, 1f, 0.5f), Shape = ParticleSystemShapeType.Circle, Radius = 1f,
                Burst = 30, Rate = 30f, Fade = VfxFade.InOut, Growth = VfxGrowth.Constant, PoolCap = 16
            };
            d[(int)VfxKind.Mist] = new VfxDef
            {
                Kind = VfxKind.Mist, LifeMin = 2.5f, LifeMax = 4.5f, SpeedMin = 0.05f, SpeedMax = 0.3f, SizeMin = 1.5f, SizeMax = 3f,
                Gravity = -0.01f, ColorA = C(0.45f, 0.75f, 0.3f, 0.35f), ColorB = C(0.35f, 0.6f, 0.2f, 0.25f), Shape = ParticleSystemShapeType.Circle, Radius = 1f,
                Burst = 12, Rate = 10f, Fade = VfxFade.InOut, Growth = VfxGrowth.Grow, PoolCap = 12, MaxScreenSize = 1.5f
            };
            d[(int)VfxKind.Splash] = new VfxDef
            {
                Kind = VfxKind.Splash, LifeMin = 0.4f, LifeMax = 0.8f, SpeedMin = 1.5f, SpeedMax = 3f, SizeMin = 0.08f, SizeMax = 0.18f,
                Gravity = 1f, ColorA = C(0.7f, 0.85f, 1f, 0.8f), ColorB = C(0.55f, 0.75f, 1f, 0.6f), Shape = ParticleSystemShapeType.Box,
                BoxSize = new Vector3(2f, 2f, 0.1f), Burst = 40, Rate = 60f, Fade = VfxFade.Out, Growth = VfxGrowth.Constant, PoolCap = 12
            };
            d[(int)VfxKind.Flame] = new VfxDef
            {
                Kind = VfxKind.Flame, Additive = true, LifeMin = 0.4f, LifeMax = 0.8f, SpeedMin = 0.6f, SpeedMax = 1.4f, SizeMin = 0.3f, SizeMax = 0.6f,
                Gravity = -0.2f, ColorA = C(1f, 0.75f, 0.25f), ColorB = C(1f, 0.35f, 0.05f), Shape = ParticleSystemShapeType.Cone, Radius = 0.25f, Angle = 12f,
                Burst = 12, Rate = 24f, Fade = VfxFade.Out, Growth = VfxGrowth.Shrink, PoolCap = 80
            };
            d[(int)VfxKind.Smoke] = new VfxDef
            {
                Kind = VfxKind.Smoke, LifeMin = 2f, LifeMax = 3.5f, SpeedMin = 0.5f, SpeedMax = 1f, SizeMin = 0.6f, SizeMax = 1.2f,
                Gravity = -0.05f, ColorA = C(0.25f, 0.23f, 0.22f, 0.5f), ColorB = C(0.4f, 0.38f, 0.36f, 0.4f), Shape = ParticleSystemShapeType.Cone,
                Radius = 0.3f, Angle = 10f, Burst = 8, Rate = 6f, Fade = VfxFade.InOut, Growth = VfxGrowth.Grow, PoolCap = 80, MaxScreenSize = 1f
            };
            d[(int)VfxKind.Embers] = new VfxDef
            {
                Kind = VfxKind.Embers, Additive = true, LifeMin = 0.8f, LifeMax = 1.6f, SpeedMin = 0.8f, SpeedMax = 2f, SizeMin = 0.03f, SizeMax = 0.08f,
                Gravity = -0.15f, ColorA = C(1f, 0.6f, 0.2f), ColorB = C(1f, 0.3f, 0.05f), Shape = ParticleSystemShapeType.Cone, Radius = 0.3f, Angle = 25f,
                Burst = 20, Rate = 10f, Fade = VfxFade.Out, Growth = VfxGrowth.Shrink, PoolCap = 32
            };
            d[(int)VfxKind.LightPillar] = new VfxDef
            {
                Kind = VfxKind.LightPillar, Additive = true, LifeMin = 1f, LifeMax = 1.6f, SpeedMin = 6f, SpeedMax = 10f, SizeMin = 0.25f, SizeMax = 0.5f,
                Gravity = 0f, ColorA = C(1f, 0.95f, 0.7f, 0.9f), ColorB = C(1f, 0.85f, 0.45f, 0.7f), Shape = ParticleSystemShapeType.Cone, Radius = 0.4f, Angle = 2f,
                Burst = 60, Rate = 120f, Stretch = true, LengthScale = 4f, Fade = VfxFade.InOut, Growth = VfxGrowth.Constant, PoolCap = 6
            };
            d[(int)VfxKind.HeatHaze] = new VfxDef
            {
                Kind = VfxKind.HeatHaze, LifeMin = 2f, LifeMax = 4f, SpeedMin = 0.1f, SpeedMax = 0.3f, SizeMin = 2f, SizeMax = 4f,
                Gravity = -0.02f, ColorA = C(1f, 0.75f, 0.45f, 0.08f), ColorB = C(1f, 0.65f, 0.35f, 0.05f), Shape = ParticleSystemShapeType.Circle, Radius = 2f,
                Burst = 8, Rate = 6f, Fade = VfxFade.InOut, Growth = VfxGrowth.Grow, PoolCap = 6, MaxScreenSize = 2f
            };
            d[(int)VfxKind.Rain] = new VfxDef
            {
                Kind = VfxKind.Rain, LifeMin = 1f, LifeMax = 1.2f, SpeedMin = 18f, SpeedMax = 22f, SizeMin = 0.03f, SizeMax = 0.05f,
                Gravity = 0f, ColorA = C(0.75f, 0.82f, 0.95f, 0.55f), ColorB = C(0.7f, 0.78f, 0.92f, 0.45f), Shape = ParticleSystemShapeType.Box,
                BoxSize = new Vector3(40f, 40f, 0.5f), Burst = 0, Rate = 0f, MaxParticles = 5000, Stretch = true, LengthScale = 1f, VelocityScale = 0.04f,
                Fade = VfxFade.InOut, EmitUp = false, PoolCap = 1
            };
            d[(int)VfxKind.Snow] = new VfxDef
            {
                Kind = VfxKind.Snow, LifeMin = 7f, LifeMax = 9f, SpeedMin = 1.4f, SpeedMax = 2.2f, SizeMin = 0.06f, SizeMax = 0.12f,
                Gravity = 0f, ColorA = C(1f, 1f, 1f, 0.9f), ColorB = C(0.9f, 0.94f, 1f, 0.8f), Shape = ParticleSystemShapeType.Box,
                BoxSize = new Vector3(40f, 40f, 0.5f), Burst = 0, Rate = 0f, MaxParticles = 4000, Fade = VfxFade.InOut, EmitUp = false, PoolCap = 1
            };
            return d;
        }
    }
}
