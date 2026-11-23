using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Mortals perceive, interpret and remember the player-god's deeds.
    ///
    /// <para><b>Perception</b> (event-driven): every successful <see cref="GodPowerCastEvent"/> is felt by the
    /// settlements within <c>radius + 5</c> tiles of the target (full impact inside the radius, fading outside).
    /// Natural <see cref="DisasterEvent"/>s are buffered and, unless they belong to a god cast in the same tick
    /// (wrath powers publish both), pious civilizations may blame them on the god.</para>
    ///
    /// <para><b>Interpretation</b>: blessings breed gratitude (more with every consecutive gift, and for the gift
    /// a race cherishes); wrath breeds fear. Pious civs read wrath as deserved punishment (devotion rises),
    /// impious ones as tyranny (resentment builds, devotion and GodFavor fall). A god that alternates gifts and
    /// punishment makes mortals uncertain, which drags devotion down. Feelings decay slowly every run, scaled
    /// by the race's memory. Direct Devotion/GodFavor nudges are banked and applied at most
    /// <see cref="GodPerceptionTuning.MaxNudgePerRun"/> per run (the Divine FaithSystem owns the drift).</para>
    ///
    /// <para><b>Response</b>: Advisor Council votes for faith buildings (thanks or appeasement, e.g.
    /// "Offerings to calm the Storm-Father") and priests in affected settlements; a civ that has turned its back
    /// on the heavens votes against them. Rare, rate-limited chronicle entries mark meaningful moments.</para>
    ///
    /// State lives in civ/settlement Memory (<see cref="GodPerceptionKeys"/>); the only transient state is the
    /// same-tick disaster buffer. Deterministic and allocation-free in the steady state.
    /// </summary>
    public sealed class GodPerceptionSystem : AdvisorSystemBase
    {
        /// <summary>Advisor id ("god").</summary>
        public const string Id = "god";

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private struct PendingDisaster
        {
            public DisasterType Type;
            public Vec2i Position;
            public int Radius;
            public float Severity;
            public long Tick;
            public bool Consumed;
        }

        private const int DisasterBufferSize = 32;
        private const int MaxRadius = 64;
        private const int MaxCoordinateSpan = 1 << 16;

        // Chronicle moment codes (stored as float in civ memory).
        private const int ChFirstBlessing = 1, ChPiousWrath = 2, ChImpiousWrath = 3, ChTurnedBack = 4, ChAdoring = 5, ChErratic = 6, ChReturned = 7;

        private readonly PendingDisaster[] _disasters = new PendingDisaster[DisasterBufferSize];
        private int _disasterCount;
        private readonly Action<GodPowerCastEvent> _onCast;
        private readonly Action<DisasterEvent> _onDisaster;
        private EventBus _bus;
        private SimContext _ctx;
        private long _runTick = long.MinValue;

        public GodPerceptionSystem()
        {
            _onCast = OnCast;
            _onDisaster = OnDisaster;
        }

        /// <summary>Diagnostics: number of (civ, act) perceptions processed since construction.</summary>
        public long Perceptions { get; private set; }

        /// <summary>Diagnostics: chronicle entries written since construction.</summary>
        public int ChronicleEntriesWritten { get; private set; }

        public override void Initialize(SimContext ctx)
        {
            _ctx = ctx;
            _runTick = long.MinValue;
            _disasterCount = 0;
            var bus = ctx?.Events;
            if (ReferenceEquals(bus, _bus)) return;
            if (_bus != null) { _bus.Unsubscribe(_onCast); _bus.Unsubscribe(_onDisaster); }
            _bus = bus;
            if (_bus != null) { _bus.Subscribe(_onCast); _bus.Subscribe(_onDisaster); }
        }

        // ================================================================== events
        private void OnCast(GodPowerCastEvent e)
        {
            var ctx = _ctx;
            if (!e.Success || ctx == null || ctx.State == null) return;
            long tick = ctx.State.Tick;
            FlushDisasters(ctx, tick, false);
            int radius = MathUtil.Clamp(e.Radius, 0, MaxRadius);
            if (!ValidTarget(e.Target)) return;

            // Wrath powers publish their own DisasterEvent first: that one is the god's act, not nature's.
            for (int i = 0; i < _disasterCount; i++)
                if (!_disasters[i].Consumed && _disasters[i].Tick == tick && _disasters[i].Position.ChebyshevTo(e.Target) <= radius + 1)
                    _disasters[i].Consumed = true;

            var kind = DivineActClassifier.Classify(e.Power);
            if (kind == DivineActKind.None) return;
            Perceive(ctx, kind, e.Power, default, false, e.Target, radius, 1f);
        }

        private void OnDisaster(DisasterEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null || !ValidTarget(e.Position)) return;
            long tick = ctx.State.Tick;
            FlushDisasters(ctx, tick, false);
            if (_disasterCount >= DisasterBufferSize) FlushDisasters(ctx, tick, true);
            _disasters[_disasterCount++] = new PendingDisaster
            {
                Type = e.Type,
                Position = e.Position,
                Radius = MathUtil.Clamp(e.Radius, 0, MaxRadius),
                Severity = MathUtil.Clamp01(MathUtil.Sanitize(e.Severity, 0.5f)),
                Tick = tick,
            };
        }

        /// <summary>Perceives buffered natural disasters older than <paramref name="tick"/> (or all of them).</summary>
        private void FlushDisasters(SimContext ctx, long tick, bool all)
        {
            if (_disasterCount == 0) return;
            int keep = 0;
            for (int i = 0; i < _disasterCount; i++)
            {
                var d = _disasters[i];
                if (all || d.Tick < tick)
                {
                    if (!d.Consumed) Perceive(ctx, DivineActKind.BlamedDisaster, default, d.Type, true, d.Position, d.Radius, d.Severity);
                }
                else _disasters[keep++] = d;
            }
            _disasterCount = keep;
        }

        private static bool ValidTarget(Vec2i p) => p.IsValid && Math.Abs(p.X) < MaxCoordinateSpan && Math.Abs(p.Y) < MaxCoordinateSpan;

        // ================================================================== perception
        private void Perceive(SimContext ctx, DivineActKind kind, GodPowerType power, DisasterType disaster, bool natural, Vec2i target, int radius, float severity)
        {
            var civs = ctx.State.Civilizations.Items;
            var settlements = ctx.State.Settlements;
            long tick = ctx.State.Tick;
            for (int ci = 0; ci < civs.Count; ci++)
            {
                var civ = civs[ci];
                if (civ == null || !civ.IsAlive) continue;

                float scale = 1f;
                int epithet;
                GodPowerType? like;
                if (natural)
                {
                    // Only the pious see the hand of the god in nature's blows.
                    float piety = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Piety);
                    scale = MathUtil.InverseLerp(GodPerceptionTuning.BlameMinPiety, GodPerceptionTuning.BlameFullPiety, piety) * (0.4f + 0.6f * severity);
                    if (scale < 0.05f) continue;
                    epithet = GodPerceptionLore.EpithetFor(disaster);
                    like = GodPerceptionLore.PowerLike(disaster);
                }
                else
                {
                    epithet = GodPerceptionLore.EpithetFor(power, civ.Race);
                    like = power;
                }

                float best = 0f;
                int affected = 0;
                var ids = civ.SettlementIds;
                for (int si = 0; si < ids.Count; si++)
                {
                    var s = settlements.Get(ids[si]);
                    if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                    float impact = Impact(s.Center, target, radius);
                    if (impact <= 0f) continue;
                    affected++;
                    if (impact > best) best = impact;
                    MarkSettlement(s, kind, impact * scale, epithet);
                }
                if (affected == 0) continue;

                float civImpact = MathUtil.Clamp01(best + 0.1f * (affected - 1)) * scale;
                Interpret(ctx, civ, kind, like, epithet, civImpact, best >= 1f, tick);
                Perceptions++;
            }
        }

        /// <summary>1 inside the radius, fading from <see cref="GodPerceptionTuning.NearImpact"/> to ~0.1 at radius+5, else 0.</summary>
        public static float Impact(Vec2i settlement, Vec2i target, int radius)
        {
            if (!ValidTarget(settlement) || !ValidTarget(target)) return 0f;
            radius = MathUtil.Clamp(radius, 0, MaxRadius);
            float d = settlement.DistanceTo(target);
            if (d <= radius + 0.5f) return 1f;
            float over = d - radius;
            if (over > GodPerceptionTuning.AwarenessMargin) return 0f;
            return GodPerceptionTuning.NearImpact * (1f - over / (GodPerceptionTuning.AwarenessMargin + 1f));
        }

        private static void MarkSettlement(Settlement s, DivineActKind kind, float impact, int epithet)
        {
            float v = MathUtil.Clamp01(MathUtil.Sanitize(impact));
            if (v <= 0f) return;
            switch (kind)
            {
                case DivineActKind.Blessing:
                    Accumulate(s, GodPerceptionKeys.SettlementThanks, v);
                    break;
                case DivineActKind.Wrath:
                case DivineActKind.BlamedDisaster:
                    Accumulate(s, GodPerceptionKeys.SettlementAppease, v);
                    s.Memory[GodPerceptionKeys.SettlementEpithet] = epithet;
                    break;
                case DivineActKind.Shaping:
                    // The ground moving under one's feet is cause for an offering, but a small one.
                    if (v >= 1f)
                    {
                        Accumulate(s, GodPerceptionKeys.SettlementAppease, 0.3f);
                        if (!s.Memory.ContainsKey(GodPerceptionKeys.SettlementEpithet)) s.Memory[GodPerceptionKeys.SettlementEpithet] = epithet;
                    }
                    break;
            }
        }

        private static void Accumulate(Settlement s, string key, float v)
        {
            float old = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(key)));
            s.Memory[key] = MathUtil.Clamp01(old + (1f - old) * v);
        }

        // ================================================================== interpretation
        private void Interpret(SimContext ctx, Civilization civ, DivineActKind kind, GodPowerType? like, int epithet, float impact, bool direct, long tick)
        {
            var temper = GodPerceptionLore.Temperament(civ.Race);
            float piety = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Piety);
            float pious = MathUtil.SmoothStep(GodPerceptionTuning.ImpiousPiety, GodPerceptionTuning.PiousPiety, piety);
            impact = MathUtil.Clamp01(MathUtil.Sanitize(impact));

            float g = Read(civ, GodPerceptionKeys.Gratitude);
            float f = Read(civ, GodPerceptionKeys.Fear);
            float r = Read(civ, GodPerceptionKeys.Resentment);
            float u = Read(civ, GodPerceptionKeys.Uncertainty);
            float fearBefore = f;
            float pDev = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.PendingDevotion));
            float pFav = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.PendingFavor));

            // --- erratic god: a gift right after a punishment (or vice versa) breeds uncertainty
            int pol = DivineActClassifier.Polarity(kind);
            float streak = MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.Streak)), -50f, 50f);
            if (pol != 0)
            {
                float lastPol = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.LastPolarity));
                if (lastPol != 0f && Math.Sign(lastPol) != pol && civ.Memory.TryGetValue(GodPerceptionKeys.LastPolarityTick, out float lastPolTick))
                {
                    long dt = tick - (long)MathUtil.Sanitize(lastPolTick);
                    if (dt >= 0 && dt <= GodPerceptionTuning.ErraticWindowTicks)
                    {
                        float closeness = 1f - 0.5f * dt / GodPerceptionTuning.ErraticWindowTicks;
                        float w = closeness * Math.Min(1f, impact + 0.3f);
                        u += (1f - u) * GodPerceptionTuning.UncertaintyGain * w;
                        pDev += GodPerceptionTuning.FlipDevotion * w;
                        civ.Memory[GodPerceptionKeys.Flips] = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.Flips)) + 1f;
                    }
                }
                streak = (streak * pol > 0f ? streak : 0f) + pol;
                civ.Memory[GodPerceptionKeys.Streak] = MathUtil.Clamp(streak, -50f, 50f);
                civ.Memory[GodPerceptionKeys.LastPolarity] = pol;
                civ.Memory[GodPerceptionKeys.LastPolarityTick] = tick;
            }

            switch (kind)
            {
                case DivineActKind.Blessing:
                {
                    int run = MathUtil.Clamp((int)streak - 1, 0, GodPerceptionTuning.MaxStreakBonus);
                    float cherished = like.HasValue && like.Value == temper.Cherished ? 1.5f : 1f;
                    float mul = impact * temper.Gratitude * cherished * (1f + GodPerceptionTuning.StreakBonus * run) * (1f - 0.5f * u);
                    g += (1f - g) * MathUtil.Clamp01(GodPerceptionTuning.GratitudeGain * mul);
                    f *= 1f - 0.08f * impact;
                    r *= 1f - 0.25f * impact;
                    pDev += GodPerceptionTuning.BlessDevotion * impact * (0.5f + piety);
                    pFav += GodPerceptionTuning.BlessFavor * impact;
                    float n = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.BlessCount));
                    civ.Memory[GodPerceptionKeys.BlessCount] = n + 1f;
                    if (n < 0.5f && impact >= 0.5f) QueueChronicle(civ, ChFirstBlessing, GodPerceptionLore.RaceEpithet(civ.Race), tick);
                    break;
                }
                case DivineActKind.Wrath:
                case DivineActKind.BlamedDisaster:
                {
                    float dreaded = like.HasValue && like.Value == temper.Dreaded ? 1.4f : 1f;
                    f += (1f - f) * MathUtil.Clamp01(GodPerceptionTuning.FearGain * impact * temper.Fear * dreaded);
                    g *= 1f - 0.12f * impact;
                    r += (1f - r) * MathUtil.Clamp01(GodPerceptionTuning.ResentGain * impact * (1f - pious) * temper.Resentment);
                    pDev += impact * (GodPerceptionTuning.PiousWrathDevotion * pious + GodPerceptionTuning.ImpiousWrathDevotion * (1f - pious));
                    pFav += GodPerceptionTuning.ImpiousWrathFavor * impact * (1f - pious);
                    string counter = kind == DivineActKind.Wrath ? GodPerceptionKeys.WrathCount : GodPerceptionKeys.BlameCount;
                    civ.Memory[counter] = MathUtil.Sanitize(civ.Mem(counter)) + 1f;
                    civ.Memory[GodPerceptionKeys.WrathEpithet] = epithet;

                    if (direct && kind == DivineActKind.Wrath && f >= GodPerceptionTuning.GreatWrathFear)
                    {
                        bool crossed = fearBefore < GodPerceptionTuning.GreatWrathFear;
                        bool longAgo = !civ.Memory.TryGetValue(GodPerceptionKeys.GreatWrathTick, out float gt) || tick - (long)MathUtil.Sanitize(gt) >= GodPerceptionTuning.GreatWrathGapTicks;
                        if (crossed || longAgo)
                        {
                            civ.Memory[GodPerceptionKeys.GreatWrathTick] = tick;
                            QueueChronicle(civ, pious >= 0.5f ? ChPiousWrath : ChImpiousWrath, epithet, tick);
                        }
                    }
                    break;
                }
                case DivineActKind.Shaping:
                {
                    f += (1f - f) * MathUtil.Clamp01(0.06f * impact * temper.Fear);
                    g += (1f - g) * MathUtil.Clamp01(0.04f * impact * (1f - r));
                    civ.Memory[GodPerceptionKeys.ShapeCount] = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.ShapeCount)) + 1f;
                    break;
                }
            }

            Write(civ, GodPerceptionKeys.Gratitude, g);
            Write(civ, GodPerceptionKeys.Fear, f);
            Write(civ, GodPerceptionKeys.Resentment, r);
            Write(civ, GodPerceptionKeys.Uncertainty, u);
            civ.Memory[GodPerceptionKeys.PendingDevotion] = ClampPending(pDev);
            civ.Memory[GodPerceptionKeys.PendingFavor] = ClampPending(pFav);
            civ.Memory[GodPerceptionKeys.LastKind] = (float)kind;
            civ.Memory[GodPerceptionKeys.LastTick] = tick;
            civ.Memory[GodPerceptionKeys.LastEpithet] = epithet;
        }

        // ================================================================== periodic run
        /// <summary>Civ-level work, once per run, before any settlement is advised.</summary>
        private void BeginRun(SimContext ctx)
        {
            long tick = ctx.State.Tick;
            if (tick == _runTick) return;
            _runTick = tick;
            if (!ReferenceEquals(_ctx, ctx)) _ctx = ctx;
            FlushDisasters(ctx, tick, true);

            float runs = Math.Max(1, ctx.DeltaTicks) / (float)GodPerceptionTuning.ReferenceRunTicks;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive || !civ.Memory.ContainsKey(GodPerceptionKeys.LastTick)) continue;
                UpdateCiv(ctx, civ, runs, tick);
            }
        }

        private void UpdateCiv(SimContext ctx, Civilization civ, float runs, long tick)
        {
            var temper = GodPerceptionLore.Temperament(civ.Race);
            double exp = runs / Math.Max(0.25f, temper.Memory);

            float g = Read(civ, GodPerceptionKeys.Gratitude) * (float)Math.Pow(GodPerceptionTuning.GratitudeRetention, exp);
            float f = Read(civ, GodPerceptionKeys.Fear) * (float)Math.Pow(GodPerceptionTuning.FearRetention, exp);
            float r = Read(civ, GodPerceptionKeys.Resentment) * (float)Math.Pow(GodPerceptionTuning.ResentRetention, exp);
            float u = Read(civ, GodPerceptionKeys.Uncertainty) * (float)Math.Pow(GodPerceptionTuning.UncertaintyRetention, exp);
            Write(civ, GodPerceptionKeys.Gratitude, g);
            Write(civ, GodPerceptionKeys.Fear, f);
            Write(civ, GodPerceptionKeys.Resentment, r);
            Write(civ, GodPerceptionKeys.Uncertainty, u);

            // --- turning points
            bool turned = civ.Mem(GodPerceptionKeys.Turned) > 0.5f;
            if (!turned && r >= GodPerceptionTuning.TurnBackResentment && r > g)
            {
                turned = true;
                civ.Memory[GodPerceptionKeys.Turned] = 1f;
                QueueChronicle(civ, ChTurnedBack, (int)civ.Mem(GodPerceptionKeys.WrathEpithet), tick);
            }
            else if (turned && r < GodPerceptionTuning.ForgiveResentment)
            {
                turned = false;
                civ.Memory[GodPerceptionKeys.Turned] = 0f;
                QueueChronicle(civ, ChReturned, GodPerceptionLore.RaceEpithet(civ.Race), tick);
            }

            bool adoring = civ.Mem(GodPerceptionKeys.AdoreFlag) > 0.5f;
            if (!adoring && g >= GodPerceptionTuning.AdoreGratitude)
            {
                civ.Memory[GodPerceptionKeys.AdoreFlag] = 1f;
                QueueChronicle(civ, ChAdoring, GodPerceptionLore.RaceEpithet(civ.Race), tick);
            }
            else if (adoring && g < 0.5f) civ.Memory[GodPerceptionKeys.AdoreFlag] = 0f;

            bool erratic = civ.Mem(GodPerceptionKeys.ErraticFlag) > 0.5f;
            if (!erratic && u >= GodPerceptionTuning.ErraticUncertainty)
            {
                civ.Memory[GodPerceptionKeys.ErraticFlag] = 1f;
                QueueChronicle(civ, ChErratic, GodPerceptionQueries.EpithetIndex(civ), tick);
            }
            else if (erratic && u < 0.25f) civ.Memory[GodPerceptionKeys.ErraticFlag] = 0f;

            // --- small, capped nudges (the Divine FaithSystem owns the main drift)
            float max = GodPerceptionTuning.MaxNudgePerRun;
            float drag = (GodPerceptionTuning.UncertaintyDrag * u + (turned ? GodPerceptionTuning.TurnedDrag : 0f)) * Math.Min(runs, 1f);
            float totalDev = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.PendingDevotion)) + drag;
            float stepDev = MathUtil.Clamp(totalDev, -max, max);
            civ.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f) + stepDev);
            StorePending(civ, GodPerceptionKeys.PendingDevotion, totalDev - stepDev);

            float totalFav = MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.PendingFavor));
            float stepFav = MathUtil.Clamp(totalFav, -max, max);
            civ.GodFavor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor) + stepFav, -1f, 1f);
            StorePending(civ, GodPerceptionKeys.PendingFavor, totalFav - stepFav);

            EmitChronicle(ctx, civ, tick);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            BeginRun(ctx);
            float thx = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(GodPerceptionKeys.SettlementThanks)));
            float app = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(GodPerceptionKeys.SettlementAppease)));
            bool turned = GodPerceptionQueries.HasTurnedAway(civ);
            if (thx < 0.01f && app < 0.01f && !turned)
            {
                if (s.Memory.ContainsKey(AdvisorBoard.UrgencyKey(Id))) Urgency(s, 0f);
                return;
            }

            float piety = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Piety);
            float g = GodPerceptionQueries.Gratitude(civ), f = GodPerceptionQueries.Fear(civ), r = GodPerceptionQueries.Resentment(civ);
            var faith = GodPerceptionQueries.PreferredFaithBuilding(ctx.Db, civ);
            float coverage = MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Faith]);
            float saturation = coverage >= 1.5f ? 0.4f : (coverage >= 1f ? 0.7f : 1f);

            if (turned)
            {
                // They have turned their back on the heavens: shrines are neglected, priests leave.
                VoteBuild(s, civ, faith, -(0.3f + 0.5f * r), GodPerceptionLore.SpurnReason(GodPerceptionQueries.EpithetIndex(civ)));
                VoteJob(s, civ, JobType.Priest, -0.4f * r);
                Urgency(s, r);
            }
            else
            {
                float devotionTilt = 0.6f + 0.6f * piety;
                float appease = app * (0.45f + 0.55f * f) * devotionTilt * (1f - 0.6f * r);
                float thanks = thx * (0.45f + 0.55f * g) * devotionTilt;
                float vote = MathUtil.Clamp01((Math.Max(appease, thanks) + 0.5f * Math.Min(appease, thanks)) * saturation);
                if (vote > 0.01f)
                {
                    string reason = appease >= thanks
                        ? GodPerceptionLore.AppeaseReason((int)s.Mem(GodPerceptionKeys.SettlementEpithet))
                        : GodPerceptionLore.ThanksReason(GodPerceptionLore.RaceEpithet(civ.Race));
                    VoteBuild(s, civ, faith, vote, reason);
                    VoteJob(s, civ, JobType.Priest, 0.5f * vote);
                }
                Urgency(s, Math.Max(app, thx));
            }

            float retain = (float)Math.Pow(GodPerceptionTuning.ResponseRetention, Math.Max(1, ctx.DeltaTicks) / (float)GodPerceptionTuning.ReferenceRunTicks);
            Fade(s, GodPerceptionKeys.SettlementThanks, thx * retain);
            Fade(s, GodPerceptionKeys.SettlementAppease, app * retain);
        }

        protected override void AfterRun(SimContext ctx) => BeginRun(ctx);

        // ================================================================== chronicle
        private static int ChronicleImportance(int code) => code == ChPiousWrath || code == ChImpiousWrath || code == ChTurnedBack ? 2 : 1;

        private static void QueueChronicle(Civilization civ, int code, int epithet, long tick)
        {
            if (civ.Memory.TryGetValue(GodPerceptionKeys.ChronicleQueued, out float existing)
                && ChronicleImportance((int)existing) > ChronicleImportance(code)) return;
            civ.Memory[GodPerceptionKeys.ChronicleQueued] = code;
            civ.Memory[GodPerceptionKeys.ChronicleQueuedTick] = tick;
            civ.Memory[GodPerceptionKeys.ChronicleQueuedEpithet] = epithet;
        }

        private void EmitChronicle(SimContext ctx, Civilization civ, long tick)
        {
            if (!civ.Memory.TryGetValue(GodPerceptionKeys.ChronicleQueued, out float qf)) return;
            int code = (int)MathUtil.Sanitize(qf);
            long queuedAt = (long)MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.ChronicleQueuedTick));
            if (code <= 0 || tick - queuedAt > GodPerceptionTuning.ChronicleQueueTtlTicks) { ClearQueue(civ); return; }
            if (civ.Memory.TryGetValue(GodPerceptionKeys.ChronicleTick, out float civLast) && tick - (long)civLast < GodPerceptionTuning.CivChronicleGapTicks) return;
            var world = ctx.State.Memory;
            if (world.TryGetValue(GodPerceptionKeys.WorldChronicleTick, out float worldLast) && tick - (long)worldLast < GodPerceptionTuning.WorldChronicleGapTicks) return;

            int epithet = (int)MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.ChronicleQueuedEpithet));
            ClearQueue(civ);
            civ.Memory[GodPerceptionKeys.ChronicleTick] = tick;
            world[GodPerceptionKeys.WorldChronicleTick] = tick;
            if (ctx.Ops == null) return;

            var capital = ctx.State.Settlements.Get(civ.CapitalId);
            Vec2i at = capital != null ? capital.Center : Vec2i.Invalid;
            ctx.Ops.AddChronicle(ChronicleCategory.Divine, ChronicleText(code, civ, epithet), civ.Id, capital != null ? capital.Id : -1, at, ChronicleImportance(code));
            ChronicleEntriesWritten++;
        }

        private static void ClearQueue(Civilization civ)
        {
            civ.Memory.Remove(GodPerceptionKeys.ChronicleQueued);
            civ.Memory.Remove(GodPerceptionKeys.ChronicleQueuedTick);
            civ.Memory.Remove(GodPerceptionKeys.ChronicleQueuedEpithet);
        }

        private static string ChronicleText(int code, Civilization civ, int epithet)
        {
            string name = string.IsNullOrEmpty(civ.Name) ? "a forgotten people" : civ.Name;
            string ep = GodPerceptionLore.Epithet(epithet);
            string noun = GodPerceptionLore.Noun(epithet);
            switch (code)
            {
                case ChFirstBlessing: return "The " + name + " received their first gift from " + ep + " and gave thanks.";
                case ChPiousWrath: return "The " + name + " called the " + noun + " a just punishment and fell to their knees before " + ep + ".";
                case ChImpiousWrath: return "The " + noun + " left the " + name + " terrified, and bitter whispers against " + ep + " spread.";
                case ChTurnedBack: return "Embittered, the " + name + " turned their back on the heavens and cursed " + ep + ".";
                case ChAdoring: return "The " + name + " proclaimed " + ep + " the giver of all things.";
                case ChErratic: return "Blessed one season and smitten the next, the " + name + " no longer know what " + ep + " wants of them.";
                case ChReturned: return "The " + name + " forgave " + ep + "; incense rises again from their shrines.";
                default: return "The " + name + " pondered the will of " + ep + ".";
            }
        }

        // ================================================================== helpers
        private static float Read(Civilization civ, string key) => MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(key)));

        private static void Write(Civilization civ, string key, float v)
        {
            v = MathUtil.Clamp01(MathUtil.Sanitize(v));
            civ.Memory[key] = v < 1e-4f ? 0f : v;
        }

        private static float ClampPending(float v) => MathUtil.Clamp(MathUtil.Sanitize(v), -GodPerceptionTuning.MaxPendingNudge, GodPerceptionTuning.MaxPendingNudge);

        private static void StorePending(Civilization civ, string key, float v)
        {
            v = ClampPending(v);
            if (Math.Abs(v) < 1e-4f) civ.Memory.Remove(key); else civ.Memory[key] = v;
        }

        private static void Fade(Settlement s, string key, float v)
        {
            if (v < 0.02f || float.IsNaN(v)) s.Memory.Remove(key);
            else s.Memory[key] = v;
        }
    }
}
