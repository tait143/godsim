using System;
using System.Collections.Generic;

namespace GodSim.Core.World
{
    /// <summary>
    /// Procedural planet generator. Pipeline: domain-warped fBm + preset continent mask + ridged
    /// mountain ranges + border falloff -> percentile sea level (exact ocean ratio) -> rank remap of
    /// land onto <see cref="TerrainLevels"/> -> ocean/lake split -> temperature (latitude, altitude,
    /// noise) -> moisture (noise, ocean proximity, west-to-east rain shadow) -> priority-flood
    /// depression filling (deep pits become lakes) -> rivers by steepest descent with carving ->
    /// Whittaker biomes, beaches, volcanic hotspots -> trees, deposits, fertile soil, sacred sites,
    /// ruins -> fertility. Deterministic for a given <see cref="Rng"/> state; no per-tile allocations.
    /// </summary>
    public static class WorldGenerator
    {
        /// <summary>
        /// Generates a complete world map. <paramref name="settings"/> is cloned and sanitized (null = defaults);
        /// every random decision is drawn from <paramref name="rng"/> (which is advanced).
        /// </summary>
        public static WorldMap Generate(WorldGenSettings settings, Rng rng)
        {
            Guard.NotNull(rng, nameof(rng));
            var s = (settings ?? new WorldGenSettings()).Clone().Sanitize();
            return new Builder(s, rng).Run();
        }

        private const byte KindLand = 0;
        private const byte KindOcean = 1;
        private const byte KindLake = 2;
        private const int DistanceCap = 64;
        private const float FillEpsilon = 1e-5f;
        private const float PitLakeDepth = 0.02f;

        // Temperate moisture band in which ClassifyLand assigns Grassland (drier = Desert, wetter = Forest).
        private const float GrasslandMinMoisture = 0.18f;
        private const float GrasslandMaxMoisture = 0.55f;
        /// <summary>"Wet grassland" (FertileSoil even away from rivers): the upper part of the Grassland band.</summary>
        private const float WetGrasslandMoisture = 0.45f;

        /// <summary>Random draws per deposit cluster center in best-candidate sampling.</summary>
        private const int DepositCenterSamples = 6;

        private const TileFlags DepositMask = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit | TileFlags.CoalDeposit | TileFlags.OilDeposit;

        private static int Bit(Biome b) => 1 << (int)b;

        private struct DepositSpec
        {
            public TileFlags Flag;
            public int BiomeMask;
            public bool Riverbed;
            public float ClustersPer10K;
            public int MinSize;
            public int MaxSize;
        }

        private static readonly DepositSpec[] Deposits =
        {
            new DepositSpec { Flag = TileFlags.StoneDeposit, BiomeMask = Bit(Biome.Hills) | Bit(Biome.Mountain) | Bit(Biome.Desert) | Bit(Biome.Tundra), ClustersPer10K = 7f, MinSize = 3, MaxSize = 8 },
            new DepositSpec { Flag = TileFlags.OreDeposit, BiomeMask = Bit(Biome.Hills) | Bit(Biome.Mountain), ClustersPer10K = 5f, MinSize = 2, MaxSize = 6 },
            new DepositSpec { Flag = TileFlags.CoalDeposit, BiomeMask = Bit(Biome.Hills) | Bit(Biome.Forest) | Bit(Biome.Swamp), ClustersPer10K = 4f, MinSize = 3, MaxSize = 7 },
            new DepositSpec { Flag = TileFlags.OilDeposit, BiomeMask = Bit(Biome.Desert) | Bit(Biome.Swamp) | Bit(Biome.Tundra), ClustersPer10K = 2.5f, MinSize = 2, MaxSize = 5 },
            new DepositSpec { Flag = TileFlags.GoldDeposit, BiomeMask = Bit(Biome.Mountain) | Bit(Biome.Hills), Riverbed = true, ClustersPer10K = 1.4f, MinSize = 1, MaxSize = 3 },
        };

        // Base fertility by biome (indexed by (int)Biome).
        private static readonly float[] BaseFertility =
        {
            0f, 0f, 0f, 0.2f, 0.72f, 0.5f, 0.58f, 0.6f, 0.07f, 0.5f, 0.32f, 0.14f, 0.4f, 0.08f, 0f, 0.35f
        };

        private struct Blob
        {
            public float Cx, Cy, Rx, Ry, Strength;
        }

        private sealed class Builder
        {
            private readonly WorldGenSettings _s;
            private readonly int _w, _h, _n;
            private readonly float _scale;
            private readonly WorldMap _map;
            private readonly Rng _shapeRng, _climateRng, _riverRng, _featureRng, _siteRng;

            private readonly float[] _elev, _temp, _moist;
            private readonly byte[] _kind;
            private readonly int[] _distWater, _distLand, _queue;
            private readonly float[] _work;      // raw elevation, later filled elevation
            private readonly float[] _sortKeys;
            private readonly int[] _sortIdx;
            private readonly long[] _sortComposite; // (ordered key bits << 32) | tile index: unique keys, so any correct sort agrees
            private readonly bool[] _closed;
            private readonly List<int> _list = new List<int>();
            private readonly List<int> _picked = new List<int>();
            private Blob[] _blobs = new Blob[16];
            private int _blobCount;
            private float _uHill, _uMtn, _uSnow, _lowGamma;

            public Builder(WorldGenSettings s, Rng rng)
            {
                _s = s;
                _w = s.Width;
                _h = s.Height;
                _n = _w * _h;
                _scale = (_w + _h) * 0.5f;
                _map = new WorldMap(_w, _h);
                _shapeRng = rng.Fork(0x5A11UL);
                _climateRng = rng.Fork(0xC11AUL);
                _riverRng = rng.Fork(0x121EUL);
                _featureRng = rng.Fork(0xFEA7UL);
                _siteRng = rng.Fork(0x517EUL);
                _elev = _map.Elevation;
                _temp = _map.Temperature;
                _moist = _map.Moisture;
                _kind = new byte[_n];
                _distWater = new int[_n];
                _distLand = new int[_n];
                _queue = new int[_n];
                _work = new float[_n];
                _sortKeys = new float[_n];
                _sortIdx = new int[_n];
                _sortComposite = new long[_n];
                _closed = new bool[_n];
            }

            /// <summary>
            /// Maps float bits onto a signed int whose integer order equals the float order (-0 sorts just
            /// below +0). Used to build unique composite sort keys.
            /// </summary>
            private static int OrderedBits(float f)
            {
                int b = BitConverter.SingleToInt32Bits(f);
                return b >= 0 ? b : b ^ 0x7FFFFFFF;
            }

            private static long Composite(float key, int index) => ((long)OrderedBits(key) << 32) | (uint)index;

            /// <summary>
            /// Sorts _sortIdx[0..count) ascending by _sortKeys, breaking ties by tile index. Every composite
            /// key is unique, so the result does not depend on the (unstable, implementation-defined) BCL
            /// sort: Unity Mono/IL2CPP and CoreCLR produce the same order and hence the same world.
            /// Leaves the sorted composites in _sortComposite[0..count).
            /// </summary>
            private void SortIndicesByKey(int count)
            {
                for (int k = 0; k < count; k++) _sortComposite[k] = Composite(_sortKeys[k], _sortIdx[k]);
                Array.Sort(_sortComposite, 0, count);
                for (int k = 0; k < count; k++) _sortIdx[k] = (int)(_sortComposite[k] & 0xFFFFFFFFL);
            }

            public WorldMap Run()
            {
                BuildElevation();
                ClassifyWaterBodies();
                ComputeDistance(_distWater, true);
                BuildTemperature();
                BuildMoisture();
                FillDepressionsAndFormLakes();
                // Pit lakes are water now: river sources must keep their distance from them too. Rivers do
                // not change _kind, so this distance stays valid for the rest of the pipeline.
                ComputeDistance(_distWater, true);
                TraceRivers();
                ApplyRiverMoisture(); // uses _distLand as scratch
                ComputeDistance(_distLand, false);
                AssignBiomes();
                PlaceVolcanoes();
                FinalizeWaterTiles();
                MarkCoastal();
                PlaceTrees();
                PlaceDeposits();
                MarkFertileSoil();
                PlaceSites();
                ComputeFertility();
                SanitizeArrays();
                _map.SeaLevel = TerrainLevels.Sea;
                _map.MarkTerrainChanged();
                return _map;
            }

            // ------------------------------------------------------------------ elevation

            private void BuildElevation()
            {
                var warpA = new FractalNoise(_shapeRng.Fork(1), 3);
                var warpB = new FractalNoise(_shapeRng.Fork(2), 3);
                int detailOctaves = 5 + (_scale > 160f ? 1 : 0) + (_scale > 400f ? 1 : 0);
                var detail = new FractalNoise(_shapeRng.Fork(3), detailOctaves, 2f, 0.5f);
                var ridge = new FractalNoise(_shapeRng.Fork(4), 5, 2.1f, 0.5f);
                BuildBlobs();

                float baseFreq, detailW, warpAmp, ridgeFreq;
                switch (_s.Preset)
                {
                    case WorldPreset.Pangaea: baseFreq = 3.2f; detailW = 0.7f; warpAmp = 0.14f; ridgeFreq = 2.6f; break;
                    case WorldPreset.Archipelago: baseFreq = 5.0f; detailW = 0.8f; warpAmp = 0.1f; ridgeFreq = 3.2f; break;
                    case WorldPreset.Highlands: baseFreq = 3.6f; detailW = 0.65f; warpAmp = 0.14f; ridgeFreq = 2.8f; break;
                    default: baseFreq = 3.6f; detailW = 0.6f; warpAmp = 0.14f; ridgeFreq = 2.8f; break;
                }
                float ridgeW = 0.25f + 0.4f * _s.MountainIntensity;
                const float edgeBand = 0.07f;
                float invS = 1f / _scale, invW = 1f / _w, invH = 1f / _h;

                for (int y = 0; y < _h; y++)
                {
                    float v = y * invS;
                    for (int x = 0; x < _w; x++)
                    {
                        float u = x * invS;
                        float wx = warpA.Fbm(u * 2.2f, v * 2.2f) * warpAmp;
                        float wy = warpB.Fbm(u * 2.2f + 5.2f, v * 2.2f + 1.3f) * warpAmp;
                        float pu = u + wx, pv = v + wy;
                        float mask = Mask(x * invW + wx, y * invH + wy);
                        float d = detail.Fbm(pu * baseFreq, pv * baseFreq);
                        float landness = MathUtil.SmoothStep(0.25f, 0.85f, mask + d * 0.4f);
                        float r = ridge.Ridged(pu * ridgeFreq, pv * ridgeFreq);
                        float h = mask + d * detailW + r * r * r * ridgeW * landness;
                        // Noise-perturbed distance to the map edge (normalized) so coasts near the border are not boxy.
                        float ex = Math.Min(x * invW, (_w - 1 - x) * invW), eyn = Math.Min(y * invH, (_h - 1 - y) * invH);
                        float e = Math.Min(ex, eyn) + (wx + wy) * 0.3f;
                        if (e < edgeBand) h -= (1f - MathUtil.SmoothStep(0f, edgeBand, e)) * 2f;
                        _work[y * _w + x] = h;
                    }
                }

                // Percentile sea level: TargetOceanRatio of the tiles start below sea level (ties broken by index).
                for (int i = 0; i < _n; i++) { _sortKeys[i] = _work[i]; _sortIdx[i] = i; }
                SortIndicesByKey(_n);
                int oceanCount = MathUtil.Clamp(MathUtil.RoundToInt(_n * _s.TargetOceanRatio), 1, _n - 1);
                oceanCount = CompensateEnclosedBasins(oceanCount);
                int landCount = _n - oceanCount;
                ComputeLandFractions();
                float invOcean = 1f / Math.Max(1, oceanCount - 1);
                for (int r = 0; r < _n; r++)
                {
                    int i = _sortIdx[r];
                    if (r < oceanCount)
                    {
                        float depth = (oceanCount - 1 - r) * invOcean; // 0 = just below sea level
                        _elev[i] = -(0.005f + 0.995f * (float)Math.Pow(depth, 0.85));
                    }
                    else
                    {
                        float u = (r - oceanCount + 1) / (float)landCount;
                        _elev[i] = LandElevation(u);
                    }
                }

                // Hard guarantee: the outer two rings are ocean.
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        if (!InOuterRings(x, y)) continue;
                        int i = y * _w + x;
                        if (_elev[i] >= 0f) _elev[i] = -0.01f;
                    }
            }

            private bool InOuterRings(int x, int y) => x <= 1 || y <= 1 || x >= _w - 2 || y >= _h - 2;

            /// <summary>
            /// The percentile cut makes the target share of tiles water, but below-sea basins that do not
            /// touch the border later become lakes, which can leave the (border-connected) ocean well under
            /// target on small maps. When the shortfall exceeds 2% of the map, raise the cut (bisection; the
            /// ocean count is monotone in the cut) and keep whichever neighbouring cut lands closest to the
            /// target. Requires _sortComposite to hold the sorted raw-elevation composites.
            /// </summary>
            private int CompensateEnclosedBasins(int cut)
            {
                int goal = cut;
                int tolerance = Math.Max(1, MathUtil.RoundToInt(_n * 0.02f));
                int oceanLo = OceanTilesForCut(cut);
                if (oceanLo >= goal - tolerance) return cut;
                int lo = cut;
                int hi = MathUtil.Clamp(MathUtil.RoundToInt(_n * Math.Min(0.95f, _s.TargetOceanRatio + 0.25f)), cut, _n - 1);
                if (hi <= lo) return cut;
                int oceanHi = OceanTilesForCut(hi);
                if (oceanHi < goal) return hi; // best effort: never drown more than this
                for (int step = 0; step < 32 && hi - lo > 1; step++)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    int o = OceanTilesForCut(mid);
                    if (o >= goal) { hi = mid; oceanHi = o; }
                    else { lo = mid; oceanLo = o; }
                }
                return goal - oceanLo <= oceanHi - goal ? lo : hi;
            }

            /// <summary>Border-connected water tiles if the lowest <paramref name="cut"/> ranks (plus the outer rings) were water. Uses _kind/_queue as scratch.</summary>
            private int OceanTilesForCut(int cut)
            {
                long threshold = cut < _n ? _sortComposite[cut] : long.MaxValue;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        bool water = InOuterRings(x, y) || Composite(_work[i], i) < threshold;
                        _kind[i] = water ? KindLake : KindLand;
                    }
                return FloodOceanFromBorder();
            }

            private void ComputeLandFractions()
            {
                float mi = _s.MountainIntensity;
                float mFrac = MathUtil.Clamp(0.055f * mi + 0.005f, 0.004f, 0.35f);
                float hFrac = MathUtil.Clamp(0.12f * (0.55f + 0.45f * mi), 0.03f, 0.4f);
                _uSnow = 1f - mFrac * 0.25f;
                _uMtn = 1f - mFrac;
                _uHill = 1f - mFrac - hFrac;
                _lowGamma = _s.Preset == WorldPreset.Highlands ? 0.75f : (_s.Preset == WorldPreset.Archipelago ? 1.5f : 1.3f);
            }

            /// <summary>Maps a land rank fraction u in (0,1] onto fixed terrain levels (piecewise, monotone).</summary>
            private float LandElevation(float u)
            {
                if (u < _uHill)
                {
                    float t = u / _uHill;
                    return 0.002f + (TerrainLevels.Hill - 0.002f) * (float)Math.Pow(t, _lowGamma);
                }
                if (u < _uMtn) return TerrainLevels.Hill + (TerrainLevels.Mountain - TerrainLevels.Hill) * ((u - _uHill) / (_uMtn - _uHill));
                if (u < _uSnow) return TerrainLevels.Mountain + (TerrainLevels.Snow - TerrainLevels.Mountain) * ((u - _uMtn) / (_uSnow - _uMtn));
                return TerrainLevels.Snow + (1f - TerrainLevels.Snow) * MathUtil.Clamp01((u - _uSnow) / Math.Max(1e-6f, 1f - _uSnow));
            }

            private void AddBlob(float cx, float cy, float rx, float ry, float strength)
            {
                if (_blobCount == _blobs.Length) Array.Resize(ref _blobs, _blobs.Length * 2);
                _blobs[_blobCount++] = new Blob { Cx = cx, Cy = cy, Rx = Math.Max(0.01f, rx), Ry = Math.Max(0.01f, ry), Strength = strength };
            }

            private void RandomUnit(Rng rng, out float dx, out float dy)
            {
                for (int k = 0; k < 16; k++)
                {
                    float a = rng.Range(-1f, 1f), b = rng.Range(-1f, 1f);
                    float l2 = a * a + b * b;
                    if (l2 > 0.04f && l2 <= 1f)
                    {
                        float inv = 1f / (float)Math.Sqrt(l2);
                        dx = a * inv; dy = b * inv;
                        return;
                    }
                }
                dx = 1f; dy = 0f;
            }

            private void BuildBlobs()
            {
                _blobCount = 0;
                var rng = _shapeRng;
                float landFrac = 1f - _s.TargetOceanRatio;
                int count;
                float coverage, lo, hi, sepFactor;
                int lobes;
                switch (_s.Preset)
                {
                    case WorldPreset.Pangaea: count = 1; coverage = 1.1f; lo = 0.45f; hi = 0.55f; sepFactor = 0f; lobes = 3; break;
                    case WorldPreset.Archipelago: count = rng.RangeInclusive(18, 30); coverage = 1.3f; lo = 0.12f; hi = 0.88f; sepFactor = 0.9f; lobes = 0; break;
                    case WorldPreset.Highlands: count = rng.RangeInclusive(2, 3); coverage = 1.3f; lo = 0.3f; hi = 0.7f; sepFactor = 0.7f; lobes = 2; break;
                    default: count = rng.RangeInclusive(2, 4); coverage = 1.25f; lo = 0.26f; hi = 0.74f; sepFactor = 0.95f; lobes = 2; break;
                }
                float areaEach = landFrac * coverage / count;
                float maxR = _s.Preset == WorldPreset.Pangaea ? 0.5f : 0.33f;
                var cxs = new float[count];
                var cys = new float[count];
                var rs = new float[count];
                for (int b = 0; b < count; b++)
                {
                    float r = Math.Min(maxR, (float)Math.Sqrt(areaEach / Math.PI) * rng.Range(0.85f, 1.15f));
                    float aspect = rng.Range(0.7f, 1.4f);
                    float sq = (float)Math.Sqrt(aspect);
                    float bestX = 0.5f, bestY = 0.5f, bestScore = float.NegativeInfinity;
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        float cx = rng.Range(lo, hi), cy = rng.Range(lo, hi);
                        float score = float.PositiveInfinity;
                        for (int o = 0; o < b; o++)
                        {
                            float dx = cx - cxs[o], dy = cy - cys[o];
                            float need = (r + rs[o]) * sepFactor;
                            float ratio = (float)Math.Sqrt(dx * dx + dy * dy) / Math.Max(1e-4f, need);
                            if (ratio < score) score = ratio;
                        }
                        if (score > bestScore) { bestScore = score; bestX = cx; bestY = cy; }
                        if (score >= 1f) break;
                    }
                    cxs[b] = bestX; cys[b] = bestY; rs[b] = r;
                    AddBlob(bestX, bestY, r * sq, r / sq, 1f);
                    for (int l = 0; l < lobes; l++)
                    {
                        RandomUnit(rng, out float dx, out float dy);
                        float off = r * rng.Range(0.45f, 0.75f);
                        float lr = r * rng.Range(0.4f, 0.65f);
                        AddBlob(bestX + dx * off, bestY + dy * off, lr * rng.Range(0.8f, 1.25f), lr * rng.Range(0.8f, 1.25f), rng.Range(0.8f, 1f));
                    }
                }
                if (_s.Preset == WorldPreset.Pangaea || _s.Preset == WorldPreset.Continents)
                {
                    int islands = rng.RangeInclusive(2, 5);
                    for (int k = 0; k < islands; k++)
                    {
                        float ir = rng.Range(0.025f, 0.05f);
                        AddBlob(rng.Range(0.1f, 0.9f), rng.Range(0.1f, 0.9f), ir, ir * rng.Range(0.7f, 1.3f), rng.Range(0.7f, 0.9f));
                    }
                }
            }

            private float Mask(float px, float py)
            {
                float best = 0f;
                for (int b = 0; b < _blobCount; b++)
                {
                    ref Blob bl = ref _blobs[b];
                    float dx = (px - bl.Cx) / bl.Rx;
                    if (dx > 1f || dx < -1f) continue;
                    float dy = (py - bl.Cy) / bl.Ry;
                    if (dy > 1f || dy < -1f) continue;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= 1f) continue;
                    float d = (float)Math.Sqrt(d2);
                    float t = MathUtil.Clamp01((d - 0.15f) / 0.85f);
                    float f = bl.Strength * (1f - t * t * (3f - 2f * t));
                    if (f > best) best = f;
                }
                return best;
            }

            // ------------------------------------------------------------------ water bodies & distances

            private void ClassifyWaterBodies()
            {
                for (int i = 0; i < _n; i++) _kind[i] = _elev[i] < TerrainLevels.Sea ? KindLake : KindLand;
                FloodOceanFromBorder();
            }

            /// <summary>Turns every KindLake tile 4-connected to the map border into KindOcean; returns the ocean tile count.</summary>
            private int FloodOceanFromBorder()
            {
                int head = 0, tail = 0;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        if (x != 0 && y != 0 && x != _w - 1 && y != _h - 1) continue;
                        int i = y * _w + x;
                        if (_kind[i] == KindLake) { _kind[i] = KindOcean; _queue[tail++] = i; }
                    }
                while (head < tail)
                {
                    int c = _queue[head++];
                    int cx = c % _w, cy = c / _w;
                    if (cx > 0 && _kind[c - 1] == KindLake) { _kind[c - 1] = KindOcean; _queue[tail++] = c - 1; }
                    if (cx < _w - 1 && _kind[c + 1] == KindLake) { _kind[c + 1] = KindOcean; _queue[tail++] = c + 1; }
                    if (cy > 0 && _kind[c - _w] == KindLake) { _kind[c - _w] = KindOcean; _queue[tail++] = c - _w; }
                    if (cy < _h - 1 && _kind[c + _w] == KindLake) { _kind[c + _w] = KindOcean; _queue[tail++] = c + _w; }
                }
                return tail;
            }

            /// <summary>Chebyshev BFS distance to the nearest water tile (toWater) or land tile, capped.</summary>
            private void ComputeDistance(int[] dist, bool toWater)
            {
                int head = 0, tail = 0;
                for (int i = 0; i < _n; i++)
                {
                    bool src = (_kind[i] != KindLand) == toWater;
                    if (src) { dist[i] = 0; _queue[tail++] = i; }
                    else dist[i] = DistanceCap;
                }
                Bfs8(dist, head, tail, DistanceCap);
            }

            private void Bfs8(int[] dist, int head, int tail, int cap)
            {
                while (head < tail)
                {
                    int c = _queue[head++];
                    int nd = dist[c] + 1;
                    if (nd >= cap) continue;
                    int cx = c % _w, cy = c / _w;
                    int x0 = cx > 0 ? cx - 1 : 0, x1 = cx < _w - 1 ? cx + 1 : cx;
                    int y0 = cy > 0 ? cy - 1 : 0, y1 = cy < _h - 1 ? cy + 1 : cy;
                    for (int y = y0; y <= y1; y++)
                    {
                        int row = y * _w;
                        for (int x = x0; x <= x1; x++)
                        {
                            int ni = row + x;
                            if (dist[ni] > nd) { dist[ni] = nd; _queue[tail++] = ni; }
                        }
                    }
                }
            }

            // ------------------------------------------------------------------ climate

            private void BuildTemperature()
            {
                var noise = new FractalNoise(_climateRng.Fork(1), 3);
                float half = Math.Max(1f, (_h - 1) * 0.5f);
                float invS = 1f / _scale;
                float bias = _s.TemperatureBias;
                for (int y = 0; y < _h; y++)
                {
                    float lat = Math.Abs(y - half) / half;
                    float tl = 1f - (float)Math.Pow(lat, 1.25);
                    float v = y * invS;
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        float e = _elev[i] > 0f ? _elev[i] : 0f;
                        float t = 0.04f + 0.92f * tl - 0.45f * e + 0.08f * noise.Fbm(x * invS * 4f, v * 4f) + bias;
                        _temp[i] = MathUtil.Clamp01(t);
                    }
                }
            }

            private void BuildMoisture()
            {
                var noise = new FractalNoise(_climateRng.Fork(2), 4);
                float invS = 1f / _scale;
                float decay = 1f / Math.Max(2f, _scale * 0.05f);
                float baseLoss = 0.010f * 192f / _scale;
                float riseGain = _scale / 192f;
                var prox = new float[DistanceCap + 1];
                for (int d = 0; d <= DistanceCap; d++) prox[d] = (float)Math.Exp(-d * decay);

                for (int y = 0; y < _h; y++)
                {
                    float air = 1f, prevElev = 0f;
                    float v = y * invS;
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        if (_kind[i] != KindLand)
                        {
                            air += (1f - air) * (_kind[i] == KindOcean ? 0.15f : 0.06f);
                            _moist[i] = 1f;
                            prevElev = 0f;
                            continue;
                        }
                        float e = _elev[i];
                        float rise = e > prevElev ? (e - prevElev) * riseGain : 0f;
                        float frac = MathUtil.Clamp01(baseLoss + rise * 4f + (e >= TerrainLevels.Mountain ? 0.04f : 0f));
                        float precip = air * frac;
                        air -= precip;
                        float rain = MathUtil.Clamp01(air * 0.8f + precip * 5f);
                        float n = MathUtil.Clamp01(0.5f + 0.55f * noise.Fbm(x * invS * 3.5f, v * 3.5f));
                        float oro = 0.15f * MathUtil.Clamp01((e - 0.3f) * 2.5f);
                        _moist[i] = 0.42f * n + 0.33f * rain + 0.25f * prox[MathUtil.Clamp(_distWater[i], 0, DistanceCap)] + oro;
                        prevElev = e;
                    }
                }

                // Rank-blend land moisture so climate bands are stable across presets and sizes.
                int count = 0;
                for (int i = 0; i < _n; i++)
                    if (_kind[i] == KindLand) { _sortKeys[count] = _moist[i]; _sortIdx[count] = i; count++; }
                if (count == 0) return;
                SortIndicesByKey(count);
                float invScale = 1f / _s.MoistureScale;
                for (int r = 0; r < count; r++)
                {
                    int i = _sortIdx[r];
                    float m = MathUtil.Clamp01(0.6f * ((r + 0.5f) / count) + 0.4f * MathUtil.Clamp01(_moist[i]));
                    _moist[i] = invScale == 1f ? m : (float)Math.Pow(m, invScale);
                }
            }

            // ------------------------------------------------------------------ hydrology

            private void FillDepressionsAndFormLakes()
            {
                float[] filled = _work;
                var heap = new MinHeap(_n);
                var flatNoise = new FractalNoise(_riverRng.Fork(7), 3);
                float fs = 10f / _scale;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float v = 0.5f + 0.5f * flatNoise.Fbm(x * fs, y * fs) * 1.6f;
                        v = MathUtil.Clamp01(v);
                        _sortKeys[y * _w + x] = 0.1f + 6f * v * v * v;
                    }
                for (int i = 0; i < _n; i++)
                {
                    _closed[i] = false;
                    filled[i] = _elev[i];
                }
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] == KindLand) continue;
                    _closed[i] = true;
                    if (HasLandNeighbor4(i)) heap.Push(i, filled[i]);
                }
                while (heap.Count > 0)
                {
                    int c = heap.Pop();
                    int cx = c % _w, cy = c / _w;
                    float fc = filled[c];
                    if (cx > 0) FloodVisit(c - 1, fc, heap);
                    if (cx < _w - 1) FloodVisit(c + 1, fc, heap);
                    if (cy > 0) FloodVisit(c - _w, fc, heap);
                    if (cy < _h - 1) FloodVisit(c + _w, fc, heap);
                }
                // Deep depressions become lakes; shallow ones stay land (rivers carve through them).
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] != KindLand || !_closed[i]) continue;
                    if (filled[i] - _elev[i] > PitLakeDepth)
                    {
                        _kind[i] = KindLake;
                        _elev[i] = -0.02f;
                        _moist[i] = 1f;
                    }
                }
            }

            private void FloodVisit(int n, float fc, MinHeap heap)
            {
                if (_closed[n]) return;
                _closed[n] = true;
                // Noise-weighted epsilon: strictly increasing fill (guaranteed drainage) whose flats are
                // crossed along curved least-cost paths instead of grid-aligned straight lines.
                float f = fc + FillEpsilon * _sortKeys[n];
                _work[n] = _elev[n] > f ? _elev[n] : f;
                heap.Push(n, _work[n]);
            }

            private bool HasLandNeighbor4(int i)
            {
                int x = i % _w, y = i / _w;
                return (x > 0 && _kind[i - 1] == KindLand) || (x < _w - 1 && _kind[i + 1] == KindLand)
                    || (y > 0 && _kind[i - _w] == KindLand) || (y < _h - 1 && _kind[i + _w] == KindLand);
            }

            private bool HasWaterNeighbor4(int i)
            {
                int x = i % _w, y = i / _w;
                return (x > 0 && _kind[i - 1] != KindLand) || (x < _w - 1 && _kind[i + 1] != KindLand)
                    || (y > 0 && _kind[i - _w] != KindLand) || (y < _h - 1 && _kind[i + _w] != KindLand);
            }

            private void CollectRiverSources(float minElev, int minDist)
            {
                _list.Clear();
                for (int i = 0; i < _n; i++)
                    if (_kind[i] == KindLand && _elev[i] >= minElev && _distWater[i] >= minDist)
                        _list.Add(i);
            }

            private void TraceRivers()
            {
                int want = _s.RiverCount;
                if (want <= 0) return;
                // Sources: the wettest hill/mountain tiles (relative ranking), preferring those far from water.
                // Lower foothills/lowland sources are fallbacks only for maps with too few highland tiles.
                CollectRiverSources(TerrainLevels.Hill, 4);
                if (_list.Count < want) CollectRiverSources(TerrainLevels.Hill * 0.85f, 4);
                if (_list.Count < want) CollectRiverSources(TerrainLevels.Hill * 0.5f, 3);
                if (_list.Count < want) CollectRiverSources(TerrainLevels.Lowland, 2);
                int count = _list.Count;
                if (count == 0) return;
                for (int k = 0; k < count; k++)
                {
                    int i = _list[k];
                    _sortKeys[k] = -(_moist[i] + 0.005f * Math.Min(_distWater[i], 40));
                    _sortIdx[k] = i;
                }
                SortIndicesByKey(count);
                int keep = Math.Min(count, Math.Max(want * 5, count / 3));
                _list.Clear();
                for (int k = 0; k < keep; k++) _list.Add(_sortIdx[k]);
                _riverRng.Shuffle(_list);
                float spacing = Math.Max(4f, _scale / 22f);
                float spacing2 = spacing * spacing;
                _picked.Clear();
                for (int k = 0; k < _list.Count && _picked.Count < want; k++)
                {
                    int src = _list[k];
                    if (_map.Has(src, TileFlags.River)) continue;
                    if (!_riverRng.Chance(0.5f + 0.5f * _moist[src])) continue;
                    int sx = src % _w, sy = src / _w;
                    bool ok = true;
                    for (int p = 0; p < _picked.Count; p++)
                    {
                        int q = _picked[p];
                        int dx = q % _w - sx, dy = q / _w - sy;
                        if (dx * dx + dy * dy < spacing2) { ok = false; break; }
                    }
                    if (!ok) continue;
                    _picked.Add(src);
                    TraceRiver(src);
                }
            }

            private void TraceRiver(int src)
            {
                float[] filled = _work;
                int cur = src;
                for (int guard = 0; guard < _n; guard++)
                {
                    if (_kind[cur] != KindLand) break;
                    bool merged = cur != src && (_map.Flags[cur] & TileFlags.River) != 0;
                    _map.Flags[cur] |= TileFlags.River;
                    if (merged || HasWaterNeighbor4(cur)) break;
                    int cx = cur % _w, cy = cur / _w;
                    // Steepest descent: among neighbours lower on the depression-filled surface (guarantees
                    // progress to water), take the lowest real elevation so rivers meander along valley floors.
                    int next = -1;
                    float fc = filled[cur], best = float.PositiveInfinity;
                    if (cx > 0) ConsiderStep(cur - 1, fc, ref best, ref next);
                    if (cx < _w - 1) ConsiderStep(cur + 1, fc, ref best, ref next);
                    if (cy > 0) ConsiderStep(cur - _w, fc, ref best, ref next);
                    if (cy < _h - 1) ConsiderStep(cur + _w, fc, ref best, ref next);
                    if (next < 0) break;
                    if (_kind[next] == KindLand && _elev[next] > _elev[cur])
                        _elev[next] = Math.Max(0.002f, _elev[cur]); // carve through shallow pits/rims
                    cur = next;
                }
            }

            private void ConsiderStep(int n, float fc, ref float best, ref int next)
            {
                if (_work[n] >= fc) return;
                float key = _kind[n] != KindLand ? -2f : _elev[n];
                if (key < best) { best = key; next = n; }
            }

            private void ApplyRiverMoisture()
            {
                const int reach = 4;
                int tail = 0;
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] == KindLand && (_map.Flags[i] & TileFlags.River) != 0) { _distLand[i] = 0; _queue[tail++] = i; }
                    else _distLand[i] = reach;
                }
                Bfs8(_distLand, 0, tail, reach);
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] != KindLand || _distLand[i] >= reach) continue;
                    _moist[i] = MathUtil.Clamp01(_moist[i] + 0.22f * (1f - _distLand[i] / (float)reach));
                }
            }

            // ------------------------------------------------------------------ biomes

            /// <summary>4-neighbour ocean adjacency: the same adjacency MarkCoastal uses, so every Beach tile is Coastal.</summary>
            private bool AdjacentOcean4(int i)
            {
                int x = i % _w, y = i / _w;
                return (x > 0 && _kind[i - 1] == KindOcean) || (x < _w - 1 && _kind[i + 1] == KindOcean)
                    || (y > 0 && _kind[i - _w] == KindOcean) || (y < _h - 1 && _kind[i + _w] == KindOcean);
            }

            private void AssignBiomes()
            {
                var biomes = _map.Biomes;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        byte k = _kind[i];
                        if (k == KindOcean)
                            biomes[i] = (_elev[i] > TerrainLevels.ShallowDepth || _distLand[i] <= 2) ? Biome.ShallowWater : Biome.DeepOcean;
                        else if (k == KindLake)
                            biomes[i] = Biome.Lake;
                        else
                            biomes[i] = ClassifyLand(i);
                    }
            }

            private Biome ClassifyLand(int i)
            {
                float e = _elev[i], t = _temp[i], m = _moist[i];
                if (e >= TerrainLevels.Snow || t < 0.08f) return Biome.Snow;
                if (e >= TerrainLevels.Mountain) return t < 0.12f ? Biome.Snow : Biome.Mountain;
                if (e < TerrainLevels.Beach && t > 0.2f && AdjacentOcean4(i)) return Biome.Beach;
                if (e >= TerrainLevels.Hill) return Biome.Hills;
                if (t < 0.2f) return Biome.Tundra;
                if (t < 0.38f) return m > 0.42f ? Biome.Taiga : Biome.Tundra;
                if (m > 0.8f && e < TerrainLevels.Lowland) return Biome.Swamp;
                if (t < 0.66f) return m < GrasslandMinMoisture ? Biome.Desert : (m < GrasslandMaxMoisture ? Biome.Grassland : Biome.Forest);
                return m < 0.24f ? Biome.Desert : (m < 0.58f ? Biome.Savanna : Biome.Jungle);
            }

            private void PlaceVolcanoes()
            {
                var biomes = _map.Biomes;
                var flags = _map.Flags;
                // Rivers never flow through lava: neither cores nor converted tiles may carry a River.
                _list.Clear();
                for (int i = 0; i < _n; i++) if (biomes[i] == Biome.Mountain && (flags[i] & TileFlags.River) == 0) _list.Add(i);
                if (_list.Count == 0) for (int i = 0; i < _n; i++) if (biomes[i] == Biome.Hills && (flags[i] & TileFlags.River) == 0) _list.Add(i);
                if (_list.Count == 0) for (int i = 0; i < _n; i++) if (_kind[i] == KindLand && (flags[i] & TileFlags.River) == 0) _list.Add(i);
                if (_list.Count == 0) return;
                float mul = _s.Preset == WorldPreset.Archipelago || _s.Preset == WorldPreset.Highlands ? 1.6f : 1f;
                int count = Math.Max(1, MathUtil.RoundToInt(_n / 30000f * mul));
                float minD = Math.Max(6f, _scale / 6f);
                _picked.Clear();
                for (int attempt = 0; attempt < count * 30 && _picked.Count < count; attempt++)
                {
                    int c = _list[_featureRng.Range(0, _list.Count)];
                    if (!FarFromPicked(c, minD) && attempt < count * 20) continue;
                    _picked.Add(c);
                    int cx = c % _w, cy = c / _w;
                    int radius = _featureRng.Chance(0.35f) ? 2 : 1;
                    for (int y = cy - radius; y <= cy + radius; y++)
                        for (int x = cx - radius; x <= cx + radius; x++)
                        {
                            if (!_map.InBounds(x, y)) continue;
                            int i = y * _w + x;
                            if (_kind[i] != KindLand || (flags[i] & TileFlags.River) != 0) continue;
                            bool core = x == cx && y == cy;
                            Biome b = biomes[i];
                            if (core || b == Biome.Mountain || b == Biome.Snow || (b == Biome.Hills && _featureRng.Chance(0.5f)))
                                biomes[i] = Biome.Volcanic;
                        }
                }
            }

            private bool FarFromPicked(int c, float minD)
            {
                int cx = c % _w, cy = c / _w;
                float m2 = minD * minD;
                for (int p = 0; p < _picked.Count; p++)
                {
                    int q = _picked[p];
                    int dx = q % _w - cx, dy = q / _w - cy;
                    if (dx * dx + dy * dy < m2) return false;
                }
                return true;
            }

            private void FinalizeWaterTiles()
            {
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] == KindLand) continue;
                    _moist[i] = 1f;
                    _map.Flags[i] = TileFlags.None;
                    _map.Resource[i] = 0f;
                    if (_elev[i] >= 0f) _elev[i] = -0.01f;
                }
            }

            // ------------------------------------------------------------------ flags & resources

            private void MarkCoastal()
            {
                for (int i = 0; i < _n; i++)
                    if (_kind[i] == KindLand && HasWaterNeighbor4(i)) _map.Flags[i] |= TileFlags.Coastal;
            }

            private void PlaceTrees()
            {
                var noise = new FractalNoise(_featureRng.Fork(21), 3);
                float invS = 1f / _scale;
                var biomes = _map.Biomes;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        if (_kind[i] != KindLand) continue;
                        float p;
                        switch (biomes[i])
                        {
                            case Biome.Forest: p = 0.86f; break;
                            case Biome.Jungle: p = 0.92f; break;
                            case Biome.Taiga: p = 0.8f; break;
                            case Biome.Swamp: p = 0.35f; break;
                            case Biome.Grassland: p = 0.08f; break;
                            case Biome.Savanna: p = 0.06f; break;
                            default: continue;
                        }
                        float clump = 0.5f + 0.5f * noise.Fbm(x * invS * 9f, y * invS * 9f);
                        p = MathUtil.Clamp01(p * (0.55f + 0.9f * clump));
                        if (_featureRng.NextFloat() < p)
                        {
                            _map.Flags[i] |= TileFlags.Trees;
                            _map.Resource[i] = _featureRng.Range(60f, 160f);
                        }
                    }
            }

            private bool DepositEligible(int i, ref DepositSpec spec)
            {
                if (_kind[i] != KindLand) return false;
                TileFlags f = _map.Flags[i];
                if ((f & (TileFlags.Trees | DepositMask)) != 0) return false;
                Biome b = _map.Biomes[i];
                if ((spec.BiomeMask & Bit(b)) != 0) return true;
                // Placer gold in riverbeds, but only where people can actually work it.
                return spec.Riverbed && (f & TileFlags.River) != 0 && b.IsBuildable();
            }

            private void PlaceDeposits()
            {
                float density = _s.DepositDensity;
                if (density <= 0f) return;
                float amountScale = MathUtil.Clamp((float)Math.Sqrt(density), 0.5f, 2f);
                for (int s = 0; s < Deposits.Length; s++)
                {
                    DepositSpec spec = Deposits[s];
                    _list.Clear();
                    for (int i = 0; i < _n; i++) if (DepositEligible(i, ref spec)) _list.Add(i);
                    if (_list.Count == 0) continue;
                    int clusters = Math.Max(1, MathUtil.RoundToInt(_n / 10000f * spec.ClustersPer10K * density));
                    _picked.Clear();
                    for (int c = 0; c < clusters; c++)
                    {
                        int cur = PickSpreadClusterCenter();
                        _picked.Add(cur);
                        int size = _featureRng.RangeInclusive(spec.MinSize, spec.MaxSize);
                        int placed = 0;
                        for (int step = 0; step < size * 6 && placed < size; step++)
                        {
                            if (DepositEligible(cur, ref spec))
                            {
                                _map.Flags[cur] |= spec.Flag;
                                _map.Resource[cur] = _featureRng.Range(250f, 1200f) * amountScale;
                                placed++;
                            }
                            var d = Vec2i.Dirs8[_featureRng.Range(0, 8)];
                            int nx = cur % _w + d.X, ny = cur / _w + d.Y;
                            if (nx >= 0 && ny >= 0 && nx < _w && ny < _h) cur = ny * _w + nx;
                        }
                    }
                }
            }

            /// <summary>
            /// Best-candidate (Mitchell) sampling over the eligible tiles in _list: of a few random draws, keep
            /// the one farthest from the clusters already placed (_picked). Spreads each deposit type evenly
            /// over its biomes, so most regions have one within reach, without changing the deposit count.
            /// </summary>
            private int PickSpreadClusterCenter()
            {
                int best = _list[_featureRng.Range(0, _list.Count)];
                if (_picked.Count == 0) return best;
                int bestD = NearestPicked2(best);
                for (int s = 1; s < DepositCenterSamples; s++)
                {
                    int c = _list[_featureRng.Range(0, _list.Count)];
                    int d = NearestPicked2(c);
                    if (d > bestD) { bestD = d; best = c; }
                }
                return best;
            }

            private int NearestPicked2(int c)
            {
                int cx = c % _w, cy = c / _w, best = int.MaxValue;
                for (int p = 0; p < _picked.Count; p++)
                {
                    int q = _picked[p];
                    int dx = q % _w - cx, dy = q / _w - cy;
                    int d2 = dx * dx + dy * dy;
                    if (d2 < best) best = d2;
                }
                return best;
            }

            private void MarkFertileSoil()
            {
                var biomes = _map.Biomes;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        if (_kind[i] != KindLand) continue;
                        Biome b = biomes[i];
                        if (!b.IsBuildable() || b == Biome.Tundra || b == Biome.Beach) continue;
                        // Must stay inside the Grassland band (m < GrasslandMaxMoisture) or it never fires.
                        bool fertile = b == Biome.Grassland && _moist[i] > WetGrasslandMoisture;
                        if (!fertile && _elev[i] < TerrainLevels.Hill && b != Biome.Hills)
                        {
                            for (int dy = -1; dy <= 1 && !fertile; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                                    if ((_map.Flags[ny * _w + nx] & TileFlags.River) != 0) { fertile = true; break; }
                                }
                        }
                        if (fertile) _map.Flags[i] |= TileFlags.FertileSoil;
                    }
            }

            private void PlaceSites()
            {
                var biomes = _map.Biomes;
                // Sacred sites: prefer notable terrain (peaks, volcanoes, deep forest, lake shores).
                _picked.Clear();
                int want = _s.SacredSiteCount;
                int maxTries = want * 80;
                float minD = Math.Max(6f, _scale / 9f);
                for (int t = 0; t < maxTries && _picked.Count < want; t++)
                {
                    int i = _siteRng.Range(0, _n);
                    if (_kind[i] != KindLand) continue;
                    Biome b = biomes[i];
                    bool notable = b == Biome.Mountain || b == Biome.Snow || b == Biome.Volcanic || b == Biome.Hills
                        || b == Biome.Forest || b == Biome.Jungle || NearLake(i);
                    if (!notable && t < maxTries / 2) continue;
                    if (!FarFromPicked(i, t < maxTries * 3 / 4 ? minD : minD * 0.5f)) continue;
                    _picked.Add(i);
                    _map.Flags[i] |= TileFlags.Sacred;
                }
                // Ruins: buildable land, not on deposits.
                _picked.Clear();
                want = _s.RuinCount;
                maxTries = want * 80;
                minD = Math.Max(5f, _scale / 11f);
                for (int t = 0; t < maxTries && _picked.Count < want; t++)
                {
                    int i = _siteRng.Range(0, _n);
                    if (_kind[i] != KindLand || !biomes[i].IsBuildable()) continue;
                    if ((_map.Flags[i] & (DepositMask | TileFlags.Sacred)) != 0) continue;
                    if (!FarFromPicked(i, t < maxTries * 3 / 4 ? minD : minD * 0.5f)) continue;
                    _picked.Add(i);
                    _map.Flags[i] |= TileFlags.Ruins;
                }
            }

            private bool NearLake(int i)
            {
                int x = i % _w, y = i / _w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                        if (_kind[ny * _w + nx] == KindLake) return true;
                    }
                return false;
            }

            private void ComputeFertility()
            {
                var biomes = _map.Biomes;
                var fert = _map.Fertility;
                for (int i = 0; i < _n; i++)
                {
                    if (_kind[i] != KindLand) { fert[i] = 0f; continue; }
                    Biome b = biomes[i];
                    float f = BaseFertility[(int)b];
                    if (f <= 0f) { fert[i] = 0f; continue; }
                    f += (_moist[i] - 0.5f) * 0.3f;
                    TileFlags fl = _map.Flags[i];
                    if ((fl & TileFlags.River) != 0) f += 0.15f;
                    if ((fl & TileFlags.FertileSoil) != 0) f += 0.15f;
                    float comfort = 1f - Math.Abs(_temp[i] - 0.58f) * 2f;
                    f += 0.12f * comfort;
                    fert[i] = MathUtil.Clamp01(f);
                }
            }

            private void SanitizeArrays()
            {
                for (int i = 0; i < _n; i++)
                {
                    _elev[i] = MathUtil.Clamp(MathUtil.Sanitize(_elev[i], 0f), -1f, 1f);
                    _temp[i] = MathUtil.Clamp01(MathUtil.Sanitize(_temp[i], 0.5f));
                    _moist[i] = MathUtil.Clamp01(MathUtil.Sanitize(_moist[i], 0.5f));
                    _map.Fertility[i] = MathUtil.Clamp01(MathUtil.Sanitize(_map.Fertility[i], 0f));
                    _map.Resource[i] = Math.Max(0f, MathUtil.Sanitize(_map.Resource[i], 0f));
                }
            }
        }

        /// <summary>Binary min-heap of tile indices keyed by float (deterministic tie order).</summary>
        private sealed class MinHeap
        {
            private readonly int[] _items;
            private readonly float[] _keys;
            public int Count;

            public MinHeap(int capacity)
            {
                _items = new int[Math.Max(1, capacity)];
                _keys = new float[Math.Max(1, capacity)];
            }

            public void Push(int item, float key)
            {
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p];
                    _keys[i] = _keys[p];
                    i = p;
                }
                _items[i] = item;
                _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int lastItem = _items[--Count];
                float lastKey = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = (r < Count && _keys[r] < _keys[l]) ? r : l;
                    if (_keys[m] >= lastKey) break;
                    _items[i] = _items[m];
                    _keys[i] = _keys[m];
                    i = m;
                }
                if (Count > 0) { _items[i] = lastItem; _keys[i] = lastKey; }
                return top;
            }
        }
    }
}
