using System;
using System.Collections.Generic;

namespace GodSim.Core.Geography
{
    /// <summary>
    /// Named geography of a <see cref="WorldMap"/>: landmasses, water bodies, biome features and river systems,
    /// each with a deterministic, world-unique atlas name.
    /// <para>
    /// The index is DERIVED data and is never persisted. Every name is seeded by
    /// <c>Hashing.Combine(Hashing.Combine(worldSeed, centroid), kind)</c> and collisions are resolved in a
    /// size-first order that does not depend on region ids, so rebuilding the index from a loaded save (same
    /// map, same seed) yields exactly the same regions and names. Race renames (<see cref="PlaceNames.Rename"/>)
    /// are the exception: they live only in this instance (the <see cref="GeographyCache"/> carries them over
    /// terrain rebuilds, but not across save/load).
    /// </para>
    /// <para>
    /// Three tile layers map every tile to region ids: body (landmass or water body, always set), feature (biome
    /// feature or bay, -1 if none) and river (-1 if none). Build allocates only these arrays, one BFS queue and
    /// one class buffer of map size, plus one object per region.
    /// </para>
    /// </summary>
    public sealed class GeographyIndex
    {
        /// <summary>Land bodies at least this big (or 1/40 of the map, whichever is larger) are continents.</summary>
        public const int ContinentMinTiles = 150;
        /// <summary>Open (map-edge touching) water at least this big (or 1/16 of the map) is an ocean; smaller is a sea.</summary>
        public const int OceanMinTiles = 256;
        /// <summary>Enclosed water up to this size (or 1/256 of the map) is a lake even without Lake biome; larger is an inland sea.</summary>
        public const int EnclosedLakeMaxTiles = 48;
        public const int MinMountainTiles = 6;
        public const int MinBayTiles = 6;
        public const int MinRiverTiles = 3;
        /// <summary>Swamp clusters smaller than this are named as a Marsh.</summary>
        public const int MarshMaxTiles = 24;
        /// <summary>Ray length used to decide whether open water is enclosed by land (a bay).</summary>
        public const int BayRayLength = 10;
        public const int MaxNearbyRadius = 64;

        private const byte GNone = 0, GMountain = 1, GHills = 2, GForest = 3, GJungle = 4, GTaiga = 5, GDesert = 6,
            GSwamp = 7, GTundra = 8, GPlains = 9, GVolcano = 10;
        private static readonly int[] GroupMinTiles = { 0, MinMountainTiles, 6, 8, 8, 8, 8, 6, 8, 24, 3 };

        private readonly int _width;
        private readonly int _height;
        private readonly int[] _body;
        private readonly int[] _feature;
        private readonly int[] _river;
        private readonly List<GeoRegion> _regions = new List<GeoRegion>();
        private readonly List<GeoRegion> _landBySize = new List<GeoRegion>();
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>World seed the names were derived from.</summary>
        public ulong Seed { get; }
        public int Width => _width;
        public int Height => _height;
        /// <summary><see cref="WorldMap.TerrainVersion"/> of the map when this index was built.</summary>
        public int TerrainVersion { get; }
        /// <summary>All regions; <c>Regions[id].Id == id</c>.</summary>
        public IReadOnlyList<GeoRegion> Regions => _regions;

        private GeographyIndex(int width, int height, ulong seed, int terrainVersion)
        {
            _width = width;
            _height = height;
            Seed = seed;
            TerrainVersion = terrainVersion;
            int n = width * height;
            _body = new int[n];
            _feature = new int[n];
            _river = new int[n];
        }

        // ------------------------------------------------------------------------------------------ build

        private struct Acc
        {
            public int Count;
            public long SumX, SumY;
            public int MinX, MinY, MaxX, MaxY;
            public bool Edge;
        }

        /// <summary>Builds the index. Deterministic in (map contents, seed). Throws only on a null map.</summary>
        public static GeographyIndex Build(WorldMap map, ulong seed)
        {
            Guard.NotNull(map, nameof(map));
            int w = map.Width, h = map.Height, n = w * h;
            var idx = new GeographyIndex(w, h, seed, map.TerrainVersion);
            var biomes = map.Biomes;
            var flags = map.Flags;
            var queue = new int[n];
            var cls = new byte[n];
            var hist = new int[Counts.Biomes];
            for (int i = 0; i < n; i++) { idx._body[i] = -1; idx._feature[i] = -1; idx._river[i] = -1; }

            idx.BuildBodies(biomes, cls, queue, hist);
            idx.BuildBays(biomes, cls, queue, hist);
            idx.BuildFeatures(biomes, cls, queue, hist);
            idx.BuildRivers(biomes, flags, cls, queue, hist);

            for (int i = 0; i < n; i++)
            {
                if (idx._feature[i] < -1) idx._feature[i] = -1;
                if (idx._river[i] < -1) idx._river[i] = -1;
            }
            idx.ComputeAnchors();

            for (int r = 0; r < idx._regions.Count; r++)
                if (idx._regions[r].IsLandmass) idx._landBySize.Add(idx._regions[r]);
            idx._landBySize.Sort(CompareBySizeDesc);

            PlaceNames.AssignAll(idx);
            return idx;
        }

        private void BuildBodies(Biome[] biomes, byte[] cls, int[] queue, int[] hist)
        {
            int n = _body.Length;
            int continentMin = Math.Max(ContinentMinTiles, n / 40);
            int oceanMin = Math.Max(OceanMinTiles, n / 16);
            int lakeMax = Math.Max(EnclosedLakeMaxTiles, n / 256);
            for (int i = 0; i < n; i++) cls[i] = biomes[i].IsWater() ? (byte)1 : (byte)2;
            for (int i = 0; i < n; i++)
            {
                if (_body[i] != -1) continue;
                var acc = new Acc();
                Fill(i, _regions.Count, _body, cls, false, queue, hist, biomes, ref acc);
                RegionKind kind;
                if (cls[i] == 2) kind = acc.Count >= continentMin ? RegionKind.Continent : RegionKind.Island;
                else if (hist[(int)Biome.Lake] * 2 >= acc.Count) kind = RegionKind.Lake;
                else if (!acc.Edge) kind = acc.Count <= lakeMax ? RegionKind.Lake : RegionKind.Sea;
                else kind = acc.Count >= oceanMin ? RegionKind.Ocean : RegionKind.Sea;
                Commit(kind, RegionLayer.Body, ref acc, hist, -1);
            }
            AssignEnclosedParents();
        }

        /// <summary>An enclosed lake/sea whose shore belongs to a single landmass gets that landmass as parent.</summary>
        private void AssignEnclosedParents()
        {
            int w = _width, h = _height, n = _body.Length;
            var around = new int[_regions.Count];
            for (int r = 0; r < around.Length; r++) around[r] = -1;
            for (int i = 0; i < n; i++)
            {
                int b = _body[i];
                var reg = _regions[b];
                if (!reg.IsWater || reg.TouchesMapEdge || around[b] == -2) continue;
                int x = i % w, y = i / w;
                if (x > 0) NoteShore(b, i - 1, around);
                if (x < w - 1) NoteShore(b, i + 1, around);
                if (y > 0) NoteShore(b, i - w, around);
                if (y < h - 1) NoteShore(b, i + w, around);
            }
            for (int r = 0; r < around.Length; r++)
                if (around[r] >= 0) _regions[r].ParentId = around[r];
        }

        private void NoteShore(int waterBody, int j, int[] around)
        {
            int lb = _body[j];
            if (lb == waterBody || !_regions[lb].IsLandmass) return;
            if (around[waterBody] == -1) around[waterBody] = lb;
            else if (around[waterBody] != lb) around[waterBody] = -2;
        }

        private void BuildBays(Biome[] biomes, byte[] cls, int[] queue, int[] hist)
        {
            int n = _body.Length;
            int minBay = Math.Max(MinBayTiles, n / 4096);
            // Chebyshev distance to land (capped), stored in the idle BFS queue: water farther than the ray length
            // from any land cannot be enclosed, which skips the ray casts for open ocean.
            int[] dist = queue;
            ChebyshevDistanceToLand(biomes, dist, BayRayLength + 1);
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                cls[i] = 0;
                if (dist[i] > BayRayLength || !biomes[i].IsWater()) continue;
                var k = _regions[_body[i]].Kind;
                if ((k == RegionKind.Ocean || k == RegionKind.Sea) && IsEnclosed(i, biomes)) { cls[i] = 1; any = true; }
            }
            if (!any) return;
            for (int i = 0; i < n; i++)
            {
                if (cls[i] != 1 || _feature[i] != -1) continue;
                var acc = new Acc();
                int id = _regions.Count;
                int count = Fill(i, id, _feature, cls, false, queue, hist, biomes, ref acc);
                var parent = _regions[_body[i]];
                if (count < minBay || count * 2 > parent.TileCount) { Reject(count, queue, _feature); continue; }
                Commit(RegionKind.Bay, RegionLayer.Feature, ref acc, hist, parent.Id);
            }
        }

        private void ChebyshevDistanceToLand(Biome[] biomes, int[] dist, int cap)
        {
            int w = _width, h = _height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!biomes[i].IsWater()) { dist[i] = 0; continue; }
                    int d = cap;
                    if (x > 0 && dist[i - 1] + 1 < d) d = dist[i - 1] + 1;
                    if (y > 0)
                    {
                        if (dist[i - w] + 1 < d) d = dist[i - w] + 1;
                        if (x > 0 && dist[i - w - 1] + 1 < d) d = dist[i - w - 1] + 1;
                        if (x < w - 1 && dist[i - w + 1] + 1 < d) d = dist[i - w + 1] + 1;
                    }
                    dist[i] = d;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    int d = dist[i];
                    if (d == 0) continue;
                    if (x < w - 1 && dist[i + 1] + 1 < d) d = dist[i + 1] + 1;
                    if (y < h - 1)
                    {
                        if (dist[i + w] + 1 < d) d = dist[i + w] + 1;
                        if (x < w - 1 && dist[i + w + 1] + 1 < d) d = dist[i + w + 1] + 1;
                        if (x > 0 && dist[i + w - 1] + 1 < d) d = dist[i + w - 1] + 1;
                    }
                    dist[i] = d;
                }
        }

        /// <summary>
        /// Open water counts as enclosed (bay) when rays in at least 5 of 8 directions hit land within
        /// <see cref="BayRayLength"/> and the misses form a single opening (two openings = a strait).
        /// </summary>
        private bool IsEnclosed(int i, Biome[] biomes)
        {
            int w = _width, h = _height;
            int x0 = i % w, y0 = i / w;
            int hits = 0, mask = 0;
            var dirs = Vec2i.Dirs8;
            for (int d = 0; d < 8; d++)
            {
                if (hits + (8 - d) < 5) return false;
                int x = x0, y = y0, dx = dirs[d].X, dy = dirs[d].Y;
                for (int s = 0; s < BayRayLength; s++)
                {
                    x += dx; y += dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) break;
                    if (!biomes[y * w + x].IsWater()) { hits++; mask |= 1 << d; break; }
                }
            }
            if (hits < 5) return false;
            if (hits == 8) return true;
            int openings = 0;
            for (int d = 0; d < 8; d++)
            {
                bool hit = (mask & (1 << d)) != 0;
                bool nextHit = (mask & (1 << ((d + 1) & 7))) != 0;
                if (hit && !nextHit) openings++;
            }
            return openings == 1;
        }

        private void BuildFeatures(Biome[] biomes, byte[] cls, int[] queue, int[] hist)
        {
            int n = _body.Length;
            int plainsMin = Math.Max(GroupMinTiles[GPlains], n / 2048);
            for (int i = 0; i < n; i++) cls[i] = GroupOf(biomes[i]);
            for (int i = 0; i < n; i++)
            {
                byte g = cls[i];
                if (g == GNone || _feature[i] != -1) continue;
                var acc = new Acc();
                int count = Fill(i, _regions.Count, _feature, cls, false, queue, hist, biomes, ref acc);
                int min = g == GPlains ? plainsMin : GroupMinTiles[g];
                if (count < min) { Reject(count, queue, _feature); continue; }
                Commit(KindOf(g, hist, count), RegionLayer.Feature, ref acc, hist, _body[i]);
            }
        }

        private void BuildRivers(Biome[] biomes, TileFlags[] flags, byte[] cls, int[] queue, int[] hist)
        {
            int n = _body.Length;
            for (int i = 0; i < n; i++) cls[i] = (flags[i] & TileFlags.River) != 0 && !biomes[i].IsWater() ? (byte)1 : (byte)0;
            for (int i = 0; i < n; i++)
            {
                if (cls[i] == 0 || _river[i] != -1) continue;
                var acc = new Acc();
                int count = Fill(i, _regions.Count, _river, cls, true, queue, hist, biomes, ref acc);
                if (count < MinRiverTiles) { Reject(count, queue, _river); continue; }
                Commit(RegionKind.River, RegionLayer.River, ref acc, hist, _body[i]);
            }
        }

        private static byte GroupOf(Biome b)
        {
            switch (b)
            {
                case Biome.Mountain: case Biome.Snow: return GMountain;
                case Biome.Hills: return GHills;
                case Biome.Forest: return GForest;
                case Biome.Jungle: return GJungle;
                case Biome.Taiga: return GTaiga;
                case Biome.Desert: return GDesert;
                case Biome.Swamp: return GSwamp;
                case Biome.Tundra: return GTundra;
                case Biome.Grassland: case Biome.Savanna: return GPlains;
                case Biome.Volcanic: return GVolcano;
                default: return GNone;
            }
        }

        private static RegionKind KindOf(byte g, int[] hist, int count)
        {
            switch (g)
            {
                // Snow fields without a single mountain tile are polar ice, not a range.
                case GMountain: return hist[(int)Biome.Mountain] > 0 ? RegionKind.MountainRange : RegionKind.Tundra;
                case GHills: return RegionKind.Hills;
                case GForest: return RegionKind.Forest;
                case GJungle: return RegionKind.Jungle;
                case GTaiga: return RegionKind.Taiga;
                case GDesert: return RegionKind.Desert;
                case GSwamp: return count < MarshMaxTiles ? RegionKind.Marsh : RegionKind.Swamp;
                case GTundra: return RegionKind.Tundra;
                case GVolcano: return RegionKind.Volcano;
                default: return RegionKind.Plains;
            }
        }

        /// <summary>BFS over tiles sharing <c>cls[seed]</c>; visited tiles end up in queue[0..count).</summary>
        private int Fill(int seed, int id, int[] label, byte[] cls, bool eight, int[] queue, int[] hist, Biome[] biomes, ref Acc acc)
        {
            int w = _width, h = _height;
            byte c = cls[seed];
            Array.Clear(hist, 0, hist.Length);
            acc.MinX = int.MaxValue; acc.MinY = int.MaxValue; acc.MaxX = int.MinValue; acc.MaxY = int.MinValue;
            int head = 0, tail = 0;
            label[seed] = id;
            queue[tail++] = seed;
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % w, y = i / w;
                acc.Count++;
                acc.SumX += x;
                acc.SumY += y;
                if (x < acc.MinX) acc.MinX = x;
                if (x > acc.MaxX) acc.MaxX = x;
                if (y < acc.MinY) acc.MinY = y;
                if (y > acc.MaxY) acc.MaxY = y;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) acc.Edge = true;
                hist[(int)biomes[i]]++;
                bool l = x > 0, r = x < w - 1, d = y > 0, u = y < h - 1;
                if (l) Push(i - 1, c, id, label, cls, queue, ref tail);
                if (r) Push(i + 1, c, id, label, cls, queue, ref tail);
                if (d) Push(i - w, c, id, label, cls, queue, ref tail);
                if (u) Push(i + w, c, id, label, cls, queue, ref tail);
                if (!eight) continue;
                if (l && d) Push(i - w - 1, c, id, label, cls, queue, ref tail);
                if (r && d) Push(i - w + 1, c, id, label, cls, queue, ref tail);
                if (l && u) Push(i + w - 1, c, id, label, cls, queue, ref tail);
                if (r && u) Push(i + w + 1, c, id, label, cls, queue, ref tail);
            }
            return tail;
        }

        private static void Push(int j, byte c, int id, int[] label, byte[] cls, int[] queue, ref int tail)
        {
            if (label[j] != -1 || cls[j] != c) return;
            label[j] = id;
            queue[tail++] = j;
        }

        /// <summary>Marks a too-small cluster as visited-without-region (-2, cleared to -1 after the build).</summary>
        private static void Reject(int count, int[] queue, int[] label)
        {
            for (int k = 0; k < count; k++) label[queue[k]] = -2;
        }

        private void Commit(RegionKind kind, RegionLayer layer, ref Acc acc, int[] hist, int parentId)
        {
            int count = Math.Max(1, acc.Count);
            int cx = (int)((acc.SumX * 2 + count) / (2L * count));
            int cy = (int)((acc.SumY * 2 + count) / (2L * count));
            int dominant = 0;
            for (int b = 1; b < hist.Length; b++) if (hist[b] > hist[dominant]) dominant = b;
            float lat = Math.Abs((cy + 0.5f) / _height - 0.5f) * 2f;
            var r = new GeoRegion
            {
                Id = _regions.Count,
                Kind = kind,
                Layer = layer,
                TileCount = acc.Count,
                Bounds = new GeoBounds(acc.MinX, acc.MinY, acc.MaxX, acc.MaxY),
                Centroid = new Vec2i(cx, cy),
                Anchor = new Vec2i(cx, cy),
                ParentId = parentId,
                DominantBiome = (Biome)dominant,
                Latitude = MathUtil.Clamp01(lat),
                TouchesMapEdge = acc.Edge,
            };
            r.NameSeed = PlaceNames.RegionSeed(Seed, r.Centroid, kind);
            _regions.Add(r);
        }

        private void ComputeAnchors()
        {
            int count = _regions.Count;
            if (count == 0) return;
            var best = new int[count];
            for (int r = 0; r < count; r++) best[r] = int.MaxValue;
            AnchorLayer(_body, best);
            AnchorLayer(_feature, best);
            AnchorLayer(_river, best);
        }

        private void AnchorLayer(int[] layer, int[] best)
        {
            int w = _width;
            for (int i = 0; i < layer.Length; i++)
            {
                int id = layer[i];
                if (id < 0) continue;
                var r = _regions[id];
                int x = i % w, y = i / w;
                int dx = x - r.Centroid.X, dy = y - r.Centroid.Y;
                int d = dx * dx + dy * dy;
                if (d < best[id]) { best[id] = d; r.Anchor = new Vec2i(x, y); }
            }
        }

        internal static int CompareBySizeDesc(GeoRegion a, GeoRegion b)
        {
            int c = b.TileCount.CompareTo(a.TileCount);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }

        // ------------------------------------------------------------------------------------------ names

        internal bool IsNameTaken(string name, int exceptId)
        {
            int id;
            return _byName.TryGetValue(name, out id) && id != exceptId;
        }

        /// <summary>Sets a region's phrase/name and keeps the uniqueness dictionary in sync.</summary>
        internal void SetName(GeoRegion r, string phrase)
        {
            int prev;
            if (r.Name != null && _byName.TryGetValue(r.Name, out prev) && prev == r.Id) _byName.Remove(r.Name);
            r.Phrase = phrase;
            r.Name = PlaceNames.StripArticle(phrase);
            if (r.BaseName == null) r.BaseName = r.Name;
            _byName[r.Name] = r.Id;
        }

        /// <summary>
        /// Re-applies race renames from an older index of the same world to the matching regions (same kind and
        /// layer at the old anchor tile). Used by <see cref="GeographyCache"/> after a terrain rebuild.
        /// </summary>
        internal void CarryOverRenames(GeographyIndex old)
        {
            if (old == null || old._width != _width || old._height != _height) return;
            for (int i = 0; i < old._regions.Count; i++)
            {
                var o = old._regions[i];
                if (!o.Renamed) continue;
                var target = RegionInLayer(o.Anchor, o.Layer);
                if (target == null || target.Kind != o.Kind || target.Renamed) continue;
                if (IsNameTaken(o.Name, target.Id)) continue;
                SetName(target, o.Phrase);
                target.NativeRace = o.NativeRace;
                target.Renamed = true;
            }
        }

        // ------------------------------------------------------------------------------------------ queries

        public bool InBounds(Vec2i p) => p.X >= 0 && p.Y >= 0 && p.X < _width && p.Y < _height;

        /// <summary>Region by id, or null when out of range.</summary>
        public GeoRegion Get(int id) => id >= 0 && id < _regions.Count ? _regions[id] : null;

        /// <summary>Landmass / water body id of the tile (-1 out of bounds).</summary>
        public int BodyIdAt(Vec2i p) => InBounds(p) ? _body[p.Y * _width + p.X] : -1;
        /// <summary>Biome feature or bay id of the tile (-1 none).</summary>
        public int FeatureIdAt(Vec2i p) => InBounds(p) ? _feature[p.Y * _width + p.X] : -1;
        /// <summary>River system id of the tile (-1 none).</summary>
        public int RiverIdAt(Vec2i p) => InBounds(p) ? _river[p.Y * _width + p.X] : -1;

        /// <summary>The most specific region at the tile: river, else feature/bay, else landmass/water body. Null out of bounds.</summary>
        public GeoRegion RegionAt(Vec2i tile)
        {
            if (!InBounds(tile)) return null;
            int i = tile.Y * _width + tile.X;
            if (_river[i] >= 0) return _regions[_river[i]];
            if (_feature[i] >= 0) return _regions[_feature[i]];
            return _regions[_body[i]];
        }

        /// <summary>The top-level body containing the tile: a Continent/Island on land, an Ocean/Sea/Lake on water. Null out of bounds.</summary>
        public GeoRegion Landmass(Vec2i tile) => Get(BodyIdAt(tile));

        /// <summary>Biome feature or bay at the tile, or null.</summary>
        public GeoRegion Feature(Vec2i tile) => Get(FeatureIdAt(tile));

        /// <summary>River system at the tile, or null.</summary>
        public GeoRegion River(Vec2i tile) => Get(RiverIdAt(tile));

        private GeoRegion RegionInLayer(Vec2i tile, RegionLayer layer)
        {
            switch (layer)
            {
                case RegionLayer.Feature: return Feature(tile);
                case RegionLayer.River: return River(tile);
                default: return Landmass(tile);
            }
        }

        /// <summary>
        /// Atlas phrase for a tile, e.g. "the Ashen Peaks of Northreach", "the River Orneth in the Elder Wood of
        /// Northreach", "Sunken Bay of the Grey Ocean", "northern Northreach".
        /// </summary>
        public string Describe(Vec2i tile)
        {
            if (!InBounds(tile)) return "the edge of the world";
            int i = tile.Y * _width + tile.X;
            var body = _regions[_body[i]];
            var feat = _feature[i] >= 0 ? _regions[_feature[i]] : null;
            var river = _river[i] >= 0 ? _regions[_river[i]] : null;
            if (river != null)
            {
                string s = river.Phrase;
                if (feat != null) s += " in " + feat.Phrase;
                return s + (feat != null ? " of " : " in ") + body.Phrase;
            }
            if (feat != null) return feat.Phrase + (feat.Phrase.IndexOf(" of ", StringComparison.Ordinal) >= 0 ? " in " : " of ") + body.Phrase;
            if (body.ParentId >= 0) return body.Phrase + " in " + _regions[body.ParentId].Phrase;
            if (body.Kind == RegionKind.Continent)
            {
                string dir = Compass(tile, body);
                if (dir != null)
                    return body.Phrase.StartsWith("the ", StringComparison.Ordinal)
                        ? "the " + dir + " " + body.Phrase.Substring(4)
                        : dir + " " + body.Phrase;
            }
            return body.Phrase;
        }

        private static string Compass(Vec2i tile, GeoRegion r)
        {
            float hx = Math.Max(1f, r.Bounds.Width * 0.5f), hy = Math.Max(1f, r.Bounds.Height * 0.5f);
            float dx = (tile.X - r.Centroid.X) / hx, dy = (tile.Y - r.Centroid.Y) / hy;
            const float t = 0.35f;
            string ns = dy > t ? "north" : dy < -t ? "south" : "";
            string ew = dx > t ? "east" : dx < -t ? "west" : "";
            if (ns.Length == 0 && ew.Length == 0) return null;
            return ns + ew + "ern";
        }

        /// <summary>
        /// Nearest landmark within a Euclidean radius (clamped to <see cref="MaxNearbyRadius"/>), including the tile's
        /// own feature. Specific landmarks (rivers, peaks, woods, fens, lakes, bays...) always win over broad ones
        /// (plains, islands, seas), which are only returned when nothing specific is in range; continents and oceans
        /// are never returned. Ties prefer rivers, then features, then lower ids. Null when nothing is in range.
        /// For chronicle text: "near " + region.Phrase.
        /// </summary>
        public GeoRegion NearbyFeature(Vec2i tile, int radius)
        {
            if (!InBounds(tile)) return null;
            int r = MathUtil.Clamp(radius, 0, MaxNearbyRadius);
            int r2 = r * r;
            var best = new Candidate { Id = -1, D = int.MaxValue, Rank = int.MaxValue };
            var broad = best;
            int y0 = Math.Max(0, tile.Y - r), y1 = Math.Min(_height - 1, tile.Y + r);
            int x0 = Math.Max(0, tile.X - r), x1 = Math.Min(_width - 1, tile.X + r);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - tile.Y;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - tile.X;
                    int d = dx * dx + dy * dy;
                    if (d > r2 || d > best.D) continue;
                    int i = y * _width + x;
                    Consider(_river[i], 0, d, ref best, ref broad);
                    Consider(_feature[i], 1, d, ref best, ref broad);
                    Consider(_body[i], 2, d, ref best, ref broad);
                }
            }
            return Get(best.Id >= 0 ? best.Id : broad.Id);
        }

        private struct Candidate { public int Id, D, Rank; }

        private static bool IsBroad(RegionKind k) => k == RegionKind.Plains || k == RegionKind.Island || k == RegionKind.Sea;

        private void Consider(int id, int rank, int d, ref Candidate best, ref Candidate broad)
        {
            if (id < 0) return;
            var k = _regions[id].Kind;
            if (!k.IsLandmark()) return;
            if (IsBroad(k)) Offer(id, rank, d, ref broad);
            else Offer(id, rank, d, ref best);
        }

        private static void Offer(int id, int rank, int d, ref Candidate c)
        {
            if (d < c.D || (d == c.D && (rank < c.Rank || (rank == c.Rank && id < c.Id))))
            {
                c.Id = id; c.D = d; c.Rank = rank;
            }
        }

        /// <summary>Finds a region by name or phrase (case-insensitive; a leading "the " is ignored). Null if unknown.</summary>
        public GeoRegion Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = PlaceNames.StripArticle(name.Trim());
            int id;
            return _byName.TryGetValue(key, out id) ? _regions[id] : null;
        }

        /// <summary>The n largest landmasses (continents and islands), biggest first. Allocates the returned list.</summary>
        public List<GeoRegion> LargestLandmasses(int n)
        {
            int c = MathUtil.Clamp(n, 0, _landBySize.Count);
            var list = new List<GeoRegion>(c);
            for (int i = 0; i < c; i++) list.Add(_landBySize[i]);
            return list;
        }

        /// <summary>Number of regions of the given kind.</summary>
        public int CountKind(RegionKind kind)
        {
            int c = 0;
            for (int i = 0; i < _regions.Count; i++) if (_regions[i].Kind == kind) c++;
            return c;
        }
    }
}
