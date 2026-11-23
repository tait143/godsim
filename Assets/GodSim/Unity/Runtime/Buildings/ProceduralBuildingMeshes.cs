using System;
using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;
using B = GodSim.Unity.BuildingMeshBuilder;

namespace GodSim.Unity
{
    /// <summary>Full + LOD mesh pair for one building appearance (shared, never destroyed by views).</summary>
    internal sealed class BuildingMeshSet
    {
        public Mesh Full;
        public Mesh Lod;
        /// <summary>Highest local Y of the full mesh (roof tip, stack, spire).</summary>
        public float TopHeight;
        /// <summary>Lowest local Y of the full mesh (foundations / sunk bases / pilings).</summary>
        public float BottomHeight;
    }

    /// <summary>
    /// Generates and caches stylized low-poly, vertex-colored building meshes. Appearance is driven by
    /// <see cref="BuildingShape"/>, <see cref="RoofStyle"/>, the race <see cref="ArchitectureStyle"/>, and the
    /// <see cref="AgeId"/> material palette. Meshes are cached by all parameters (variant reduced to 4 buckets)
    /// and shared by every view; callers must never modify or destroy them. Main thread only.
    /// Local space: footprint centered at the origin, ground at y = 0, front (door) facing -Z,
    /// local X spans <c>width</c> tiles and local Z spans <c>height</c> tiles.
    /// </summary>
    public static class ProceduralBuildingMeshes
    {
        private const float T = GodSimCoords.TileSize;

        private struct MeshKey : IEquatable<MeshKey>
        {
            public int Kind, A, B, C, D;
            public ulong Style;

            public bool Equals(MeshKey o) => Kind == o.Kind && A == o.A && B == o.B && C == o.C && D == o.D && Style == o.Style;
            public override bool Equals(object obj) => obj is MeshKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    ulong h = Hashing.Combine((ulong)(uint)Kind, (ulong)(uint)A);
                    h = Hashing.Combine(h, ((ulong)(uint)B << 32) | (uint)C);
                    h = Hashing.Combine(h, (ulong)(uint)D);
                    h = Hashing.Combine(h, Style);
                    return (int)(h ^ (h >> 32));
                }
            }
        }

        private static readonly Dictionary<MeshKey, BuildingMeshSet> s_sets = new Dictionary<MeshKey, BuildingMeshSet>(256);
        private static readonly Dictionary<MeshKey, Mesh> s_misc = new Dictionary<MeshKey, Mesh>(64);
        private static readonly ArchitectureStyle s_defaultStyle = new ArchitectureStyle();

        /// <summary>Number of cached appearance sets plus auxiliary meshes (scaffolds, ruins, banner).</summary>
        public static int CachedMeshCount => s_sets.Count + s_misc.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode" without domain reload: drop references from the last session.
            s_sets.Clear();
            s_misc.Clear();
        }

        /// <summary>Destroys every cached mesh. Views using them must be released (or re-meshed) first.</summary>
        public static void ClearCache()
        {
            foreach (var kv in s_sets) { DestroyMesh(kv.Value.Full); DestroyMesh(kv.Value.Lod); }
            foreach (var kv in s_misc) DestroyMesh(kv.Value);
            s_sets.Clear();
            s_misc.Clear();
        }

        private static void DestroyMesh(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(m);
            else UnityEngine.Object.DestroyImmediate(m);
        }

        // =============================================================================== public API

        /// <summary>Returns the cached (or newly generated) full-detail mesh for this appearance.</summary>
        public static Mesh Get(BuildingShape shape, RoofStyle roof, ArchitectureStyle style, AgeId age, int width, int height, float visualHeight, int variant)
        {
            return GetSet(shape, roof, style, age, width, height, visualHeight, variant).Full;
        }

        /// <summary>Simple colored box matching the bounds of <see cref="Get"/> (distance LOD).</summary>
        public static Mesh GetLod(BuildingShape shape, RoofStyle roof, ArchitectureStyle style, AgeId age, int width, int height, float visualHeight, int variant)
        {
            return GetSet(shape, roof, style, age, width, height, visualHeight, variant).Lod;
        }

        /// <summary>
        /// Picks the roof a renderer should use: the race roof, overridden for tree dwellers (leaf), stilt
        /// builders (pyramid), Dawn huts (thatch) and Modern+ generic roofs (flat with parapet).
        /// </summary>
        public static RoofStyle ResolveRoof(BuildingShape shape, ArchitectureStyle style, AgeId age)
        {
            RoofStyle r = style != null ? style.Roof : RoofStyle.Gable;
            bool dwelling = IsStiltShape(shape);
            if (style != null && dwelling && style.BuildsInTrees) r = RoofStyle.Leaf;
            else if (style != null && dwelling && style.BuildsOnStilts) r = RoofStyle.Pyramid;
            if (age == AgeId.Dawn && (r == RoofStyle.Gable || r == RoofStyle.Hip)) r = RoofStyle.Thatch;
            if (age >= AgeId.Modern && shape != BuildingShape.Hut && (r == RoofStyle.Gable || r == RoofStyle.Hip || r == RoofStyle.Thatch)) r = RoofStyle.Flat;
            return r;
        }

        internal static BuildingMeshSet GetSet(BuildingShape shape, RoofStyle roof, ArchitectureStyle style, AgeId age, int width, int height, float visualHeight, int variant)
        {
            if (style == null) style = s_defaultStyle;
            width = MathUtil.Clamp(width, 1, 8);
            height = MathUtil.Clamp(height, 1, 8);
            int ageI = MathUtil.Clamp((int)age, 0, 9);
            visualHeight = MathUtil.Clamp(MathUtil.Sanitize(visualHeight, 1f), 0.1f, 16f);
            int bucket = variant & 3;
            int hq = MathUtil.RoundToInt(visualHeight * 20f);
            var key = new MeshKey
            {
                Kind = 0,
                A = (int)shape | ((int)roof << 8) | (ageI << 16) | (bucket << 24),
                B = width,
                C = height,
                D = hq,
                Style = StyleHash(style)
            };
            if (s_sets.TryGetValue(key, out var set) && set.Full != null && set.Lod != null) return set;
            if (set != null) { DestroyMesh(set.Full); DestroyMesh(set.Lod); }
            set = Generate(shape, roof, style, (AgeId)ageI, width, height, hq / 20f, bucket);
            s_sets[key] = set;
            return set;
        }

        /// <summary>Wireframe-like construction scaffold (poles and rails) for a footprint.</summary>
        public static Mesh GetScaffold(int width, int height, float topHeight)
        {
            width = MathUtil.Clamp(width, 1, 8);
            height = MathUtil.Clamp(height, 1, 8);
            int hq = MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(topHeight, 1f) * 5f), 1, 100);
            var key = new MeshKey { Kind = 1, B = width, C = height, D = hq };
            if (s_misc.TryGetValue(key, out var m) && m != null) return m;
            m = BuildScaffold(width, height, hq / 5f);
            s_misc[key] = m;
            return m;
        }

        /// <summary>Low rubble pile for destroyed buildings.</summary>
        public static Mesh GetRuin(int width, int height, AgeId age, int variant)
        {
            width = MathUtil.Clamp(width, 1, 8);
            height = MathUtil.Clamp(height, 1, 8);
            int ageI = MathUtil.Clamp((int)age, 0, 9);
            var key = new MeshKey { Kind = 2, A = ageI | ((variant & 3) << 8), B = width, C = height };
            if (s_misc.TryGetValue(key, out var m) && m != null) return m;
            m = BuildRuin(width, height, (AgeId)ageI, variant & 3);
            s_misc[key] = m;
            return m;
        }

        /// <summary>Unit banner (pole 1 unit tall + flag), white so a MaterialPropertyBlock tint shows the civ color.</summary>
        public static Mesh GetBanner()
        {
            var key = new MeshKey { Kind = 3 };
            if (s_misc.TryGetValue(key, out var m) && m != null) return m;
            B.Begin();
            var pole = new Color32(150, 150, 150, 255);
            var flag = new Color32(255, 255, 255, 255);
            B.Cylinder(Vector3.zero, 0.02f, 1f, 5, pole);
            B.Cone(new Vector3(0f, 1f, 0f), 0.035f, 0.07f, 5, new Color32(235, 210, 120, 255));
            Vector3 a = new Vector3(0.02f, 0.58f, 0f), b = new Vector3(0.02f, 0.95f, 0f);
            Vector3 c = new Vector3(0.38f, 0.92f, 0f), d = new Vector3(0.38f, 0.62f, 0f);
            B.QuadFacing(a, b, c, d, Vector3.back, flag);
            B.QuadFacing(a, b, c, d, Vector3.forward, flag);
            m = B.ToMesh("Banner");
            s_misc[key] = m;
            return m;
        }

        // =============================================================================== generation context

        private struct Ctx
        {
            public BuildingShape Shape;
            public RoofStyle Roof;
            public AgeId Age;
            public int Bucket;
            public uint Seed;
            public ArchitectureStyle Style;
            public float FootW, FootD;    // full footprint (world units)
            public float W, D, H;         // body extents and base wall height
            public float Roundness;
            public bool Lifted;           // on stilts / trunk: no stone foundation
            // body shape produced by Body()
            public int Segs;              // 0 = box, else polygon segments
            public float Off;             // polygon angle offset (deg), a face always faces -Z
            public float Rx, Rz;          // polygon vertex radii
            // palette
            public Color32 Wall, WallDark, RoofC, RoofDark, RoofLight, Trim, Accent, Window, Door, Glass, Glow,
                Stone, Wood, WoodDark, Soot, Brick, Leaf, LeafDark, LeafLight, FoundTop, FoundSide, LodTop;
        }

        private static float Rand(ref Ctx c)
        {
            unchecked { c.Seed = c.Seed * 1664525u + 1013904223u; }
            return (c.Seed >> 8) / 16777216f;
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // Age material palettes (wall, roof, trim, accent).
        private static readonly Color[] AgeWall =
        {
            new Color(0.56f, 0.43f, 0.31f), new Color(0.80f, 0.62f, 0.38f), new Color(0.53f, 0.49f, 0.43f),
            new Color(0.93f, 0.92f, 0.88f), new Color(0.56f, 0.56f, 0.55f), new Color(0.92f, 0.80f, 0.63f),
            new Color(0.62f, 0.28f, 0.21f), new Color(0.67f, 0.67f, 0.65f), new Color(0.93f, 0.96f, 0.97f),
            new Color(0.97f, 0.96f, 0.93f)
        };
        private static readonly Color[] AgeRoof =
        {
            new Color(0.73f, 0.61f, 0.36f), new Color(0.66f, 0.50f, 0.30f), new Color(0.43f, 0.33f, 0.25f),
            new Color(0.78f, 0.38f, 0.22f), new Color(0.33f, 0.27f, 0.23f), new Color(0.80f, 0.42f, 0.25f),
            new Color(0.19f, 0.18f, 0.18f), new Color(0.36f, 0.46f, 0.58f), new Color(0.84f, 0.90f, 0.93f),
            new Color(0.95f, 0.79f, 0.32f)
        };
        private static readonly Color[] AgeTrim =
        {
            new Color(0.36f, 0.26f, 0.16f), new Color(0.47f, 0.33f, 0.20f), new Color(0.31f, 0.25f, 0.19f),
            new Color(0.86f, 0.83f, 0.76f), new Color(0.25f, 0.18f, 0.12f), new Color(0.71f, 0.61f, 0.46f),
            new Color(0.13f, 0.12f, 0.12f), new Color(0.45f, 0.55f, 0.66f), new Color(0.40f, 0.82f, 0.93f),
            new Color(0.93f, 0.76f, 0.30f)
        };
        private static readonly Color[] AgeAccent =
        {
            new Color(0.70f, 0.30f, 0.20f), new Color(0.72f, 0.45f, 0.20f), new Color(0.55f, 0.55f, 0.60f),
            new Color(0.80f, 0.68f, 0.30f), new Color(0.60f, 0.15f, 0.15f), new Color(0.75f, 0.60f, 0.25f),
            new Color(0.85f, 0.55f, 0.20f), new Color(0.85f, 0.20f, 0.15f), new Color(0.40f, 0.90f, 1.00f),
            new Color(0.45f, 1.00f, 1.00f)
        };

        private static Color32 C32(Color c)
        {
            return new Color32(Sat(c.r), Sat(c.g), Sat(c.b), 255);
        }

        private static byte Sat(float v) { v *= 255f; return v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)v; }

        private static void BuildPalette(ref Ctx c)
        {
            int a = (int)c.Age;
            var st = c.Style;
            Color raceWall = GodSimPalette.FromRgba(st.WallColor);
            Color raceRoof = GodSimPalette.FromRgba(st.RoofColor);
            Color raceTrim = GodSimPalette.FromRgba(st.TrimColor);
            Color raceAccent = GodSimPalette.FromRgba(st.AccentColor);
            float jitter = 0.94f + 0.035f * c.Bucket;
            Color wall = Color.Lerp(AgeWall[a], raceWall, 0.3f) * jitter;
            Color roof = Color.Lerp(AgeRoof[a], raceRoof, 0.35f) * (1.04f - 0.03f * c.Bucket);
            Color trim = Color.Lerp(AgeTrim[a], raceTrim, 0.3f);
            Color accent = Color.Lerp(AgeAccent[a], raceAccent, c.Age >= AgeId.Information ? 0.2f : 0.5f);

            c.Wall = C32(wall);
            c.WallDark = B.Shade(c.Wall, 0.8f);
            c.RoofC = C32(roof);
            c.RoofDark = B.Shade(c.RoofC, 0.78f);
            c.RoofLight = B.Shade(c.RoofC, 1.12f);
            c.Trim = C32(trim);
            c.Accent = C32(accent);
            c.Door = B.Shade(c.Trim, 0.6f);
            c.Glass = c.Age >= AgeId.Information ? new Color32(105, 205, 240, 255)
                : c.Age >= AgeId.Modern ? new Color32(80, 120, 155, 255) : new Color32(55, 62, 72, 255);
            c.Window = c.Age >= AgeId.Modern ? c.Glass : B.Mix(B.Shade(c.Wall, 0.3f), new Color32(30, 24, 18, 255), 0.5f);
            c.Glow = c.Age == AgeId.Ascension ? new Color32(120, 255, 255, 255)
                : c.Age == AgeId.Information ? new Color32(140, 230, 255, 255) : c.Accent;
            c.Stone = B.Mix(new Color32(124, 120, 112, 255), c.Wall, 0.25f);
            c.Wood = B.Mix(new Color32(120, 86, 52, 255), c.Trim, 0.3f);
            c.WoodDark = B.Shade(c.Wood, 0.7f);
            c.Soot = new Color32(36, 34, 33, 255);
            c.Brick = c.Age >= AgeId.Modern ? new Color32(150, 150, 146, 255) : new Color32(150, 64, 46, 255);
            Color32 leafBase = st.Roof == RoofStyle.Leaf ? C32(raceRoof) : new Color32(72, 132, 62, 255);
            c.Leaf = B.Mix(new Color32(72, 132, 62, 255), leafBase, 0.5f);
            if (c.Age == AgeId.Ascension) c.Leaf = B.Mix(c.Leaf, new Color32(170, 255, 230, 255), 0.3f);
            c.LeafDark = B.Shade(c.Leaf, 0.75f);
            c.LeafLight = B.Shade(c.Leaf, 1.2f);
            c.FoundSide = B.Shade(c.Stone, 0.75f);
            c.FoundTop = B.Shade(c.Stone, 0.9f);
            c.LodTop = c.RoofC;
        }

        private static ulong StyleHash(ArchitectureStyle s)
        {
            ulong h = 0x9E3779B97F4A7C15UL;
            h = Hashing.Combine(h, (ulong)s.Roof);
            h = Hashing.Combine(h, (ulong)(uint)MathUtil.RoundToInt(MathUtil.Sanitize(s.HeightScale, 1f) * 1000f));
            h = Hashing.Combine(h, (ulong)(uint)MathUtil.RoundToInt(MathUtil.Sanitize(s.WidthScale, 1f) * 1000f));
            h = Hashing.Combine(h, (ulong)(uint)MathUtil.RoundToInt(MathUtil.Sanitize(s.Roundness) * 1000f));
            h = Hashing.Combine(h, (s.BuildsUnderground ? 1UL : 0UL) | (s.BuildsInTrees ? 2UL : 0UL) | (s.BuildsOnStilts ? 4UL : 0UL));
            h = Hashing.Combine(h, ((ulong)s.WallColor << 32) | s.RoofColor);
            h = Hashing.Combine(h, ((ulong)s.TrimColor << 32) | s.AccentColor);
            return h | 1UL;
        }

        private static bool IsStiltShape(BuildingShape s) =>
            s == BuildingShape.Hut || s == BuildingShape.House || s == BuildingShape.Hall || s == BuildingShape.Workshop;

        private static bool IsSinkShape(BuildingShape s) =>
            s == BuildingShape.Hut || s == BuildingShape.House || s == BuildingShape.Hall || s == BuildingShape.Workshop ||
            s == BuildingShape.Tower || s == BuildingShape.Dome || s == BuildingShape.Factory || s == BuildingShape.Spire ||
            s == BuildingShape.Skyscraper;

        // =============================================================================== main generator

        private static BuildingMeshSet Generate(BuildingShape shape, RoofStyle roof, ArchitectureStyle style, AgeId age, int width, int height, float visualHeight, int bucket)
        {
            var c = new Ctx
            {
                Shape = shape,
                Roof = roof,
                Age = age,
                Bucket = bucket,
                Style = style,
                Seed = (uint)(((int)shape * 7919) ^ ((int)age * 104729) ^ (bucket * 15485863) ^ (width * 31 + height * 131) ^ 0x5bd1e995),
                FootW = width * T,
                FootD = height * T,
                Roundness = MathUtil.Clamp01(MathUtil.Sanitize(style.Roundness))
            };
            float fill = MathUtil.Clamp(0.84f * MathUtil.Sanitize(style.WidthScale, 1f), 0.6f, 0.94f);
            float hs = MathUtil.Clamp(MathUtil.Sanitize(style.HeightScale, 1f), 0.5f, 2f);
            c.W = c.FootW * fill;
            c.D = c.FootD * fill;
            c.H = Mathf.Max(0.2f, visualHeight) * 0.7f * T * hs;
            bool stilts = IsStiltShape(shape) && (style.BuildsInTrees || style.BuildsOnStilts);
            c.Lifted = stilts;
            BuildPalette(ref c);

            B.Begin();
            int start = B.VertexCount;
            switch (shape)
            {
                case BuildingShape.Hut: Hut(ref c); break;
                case BuildingShape.House: House(ref c); break;
                case BuildingShape.Hall: Hall(ref c); break;
                case BuildingShape.Tower: Tower(ref c); break;
                case BuildingShape.Dome: DomeShape(ref c); break;
                case BuildingShape.Pyramid: PyramidShape(ref c); break;
                case BuildingShape.Spire: SpireShape(ref c); break;
                case BuildingShape.Field: Field(ref c); break;
                case BuildingShape.Dock: Dock(ref c); break;
                case BuildingShape.Wall: WallShape(ref c); break;
                case BuildingShape.Tree: TreeShape(ref c); break;
                case BuildingShape.Workshop: Workshop(ref c); break;
                case BuildingShape.Factory: Factory(ref c); break;
                case BuildingShape.Skyscraper: Skyscraper(ref c); break;
                case BuildingShape.Monument: Monument(ref c); break;
                case BuildingShape.Cave: Cave(ref c); break;
                default: House(ref c); break;
            }
            B.ResetTransform();
            ApplyRaceStyle(ref c, start);

            B.ComputeBounds(out Vector3 min, out Vector3 max);
            string name = shape + "_" + roof + "_" + age + "_" + width + "x" + height + "_v" + bucket;
            var set = new BuildingMeshSet { TopHeight = max.y, BottomHeight = min.y };
            set.Full = B.ToMesh(name);

            // Distance LOD: one colored box with the same silhouette bounds (clamped to the footprint).
            B.Begin();
            float hx = Mathf.Min(Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)), c.FootW * 0.5f);
            float hz = Mathf.Min(Mathf.Max(Mathf.Abs(min.z), Mathf.Abs(max.z)), c.FootD * 0.5f);
            float top = Mathf.Max(max.y * 0.85f, 0.03f);
            B.Box(V(-hx, Mathf.Max(min.y, -0.05f), -hz), V(hx, top, hz), c.Wall, c.LodTop);
            set.Lod = B.ToMesh(name + "_LOD");
            return set;
        }

        private static void ApplyRaceStyle(ref Ctx c, int start)
        {
            var st = c.Style;
            float S = Mathf.Min(c.FootW, c.FootD);
            if (IsStiltShape(c.Shape) && st.BuildsInTrees)
            {
                // Elves: houses raised on a living trunk with a round platform.
                float lift = 0.55f * T * MathUtil.Clamp(st.HeightScale, 0.6f, 1.6f);
                B.OffsetVertices(start, V(0f, lift, 0f));
                var bark = B.Mix(c.WoodDark, new Color32(92, 70, 48, 255), 0.5f);
                B.Frustum(Vector3.zero, S * 0.17f, S * 0.17f, S * 0.1f, S * 0.1f, lift, 7, bark, false);
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 120f + c.Bucket * 25f) * Mathf.Deg2Rad;
                    B.Cone(V(Mathf.Cos(a) * S * 0.16f, 0f, Mathf.Sin(a) * S * 0.16f), S * 0.07f, lift * 0.35f, 5, bark);
                }
                B.Frustum(V(0f, lift - 0.05f * T, 0f), c.FootW * 0.47f, c.FootD * 0.47f, c.FootW * 0.47f, c.FootD * 0.47f, 0.05f * T, 10, c.Wood, true, true);
                B.Dome(V(c.FootW * 0.3f, lift - 0.05f * T, -c.FootD * 0.3f), S * 0.1f, S * 0.1f, -S * 0.12f, 1, 6, c.LeafDark);
            }
            else if (IsStiltShape(c.Shape) && st.BuildsOnStilts)
            {
                // Saurians: wooden stilts and a deck with a ramp.
                float lift = 0.32f * T;
                B.OffsetVertices(start, V(0f, lift, 0f));
                float px = c.W * 0.46f, pz = c.D * 0.46f;
                int nx = c.FootW > 1.5f * T ? 3 : 2, nz = c.FootD > 1.5f * T ? 3 : 2;
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float x = -px + 2f * px * ix / (nx - 1);
                        float z = -pz + 2f * pz * iz / (nz - 1);
                        B.Cylinder(V(x, -0.1f * T, z), 0.025f * T, lift + 0.1f * T, 5, c.WoodDark, false);
                    }
                B.BoxAt(V(0f, lift - 0.04f * T, 0f), c.W * 1.08f, 0.04f * T, c.D * 1.08f, c.WoodDark, c.Wood);
                float rz = -c.D * 0.54f;
                B.QuadFacing(V(-0.07f * T, lift, rz), V(0.07f * T, lift, rz), V(0.07f * T, 0.01f, rz - 0.28f * T), V(-0.07f * T, 0.01f, rz - 0.28f * T), V(0f, 1f, -1f), c.Wood);
            }
            else if (st.BuildsUnderground && IsSinkShape(c.Shape))
            {
                // Dwarves: half-sunk halls ringed with dressed stone and a stairwell to the door.
                float sink = 0.4f * c.H;
                B.OffsetVertices(start, V(0f, -sink, 0f));
                float rx = c.FootW * 0.48f, rzr = c.FootD * 0.48f, t = 0.06f * T, h = 0.09f * T;
                var ring = B.Shade(c.Stone, 0.95f);
                B.Box(V(-rx, -0.04f, -rzr), V(rx, h, -rzr + t), ring, B.Shade(ring, 1.1f));
                B.Box(V(-rx, -0.04f, rzr - t), V(rx, h, rzr), ring, B.Shade(ring, 1.1f));
                B.Box(V(-rx, -0.04f, -rzr + t), V(-rx + t, h, rzr - t), ring, B.Shade(ring, 1.1f));
                B.Box(V(rx - t, -0.04f, -rzr + t), V(rx, h, rzr - t), ring, B.Shade(ring, 1.1f));
                float dz = -c.D * 0.5f;
                B.FlatRect(V(0f, 0.012f, (dz + (-rzr + t)) * 0.5f), 0.18f * T, Mathf.Max(0.02f, dz - (-rzr + t)), true, new Color32(26, 22, 20, 255));
            }
            else if (c.Shape == BuildingShape.Cave)
            {
                StoneRing(ref c, c.FootW * 0.46f, c.FootD * 0.46f);
            }
        }

        private static void StoneRing(ref Ctx c, float rx, float rz)
        {
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                float a = (i * 360f / n + Rand(ref c) * 15f);
                float ar = a * Mathf.Deg2Rad;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 270f)) < 35f) continue; // keep the entrance clear
                B.SetTransform(V(Mathf.Cos(ar) * rx, 0f, Mathf.Sin(ar) * rz), Rand(ref c) * 90f);
                float s = (0.07f + Rand(ref c) * 0.06f) * T;
                B.BoxAt(Vector3.zero, s * 1.4f, s, s, B.Shade(c.Stone, 0.85f + Rand(ref c) * 0.2f), c.Stone);
            }
            B.ResetTransform();
        }

        // =============================================================================== shared parts

        private static void Foundation(ref Ctx c, float w, float d)
        {
            if (c.Lifted) return;
            float m = 0.05f * T;
            if (c.Segs > 0)
                B.Frustum(V(0f, -0.3f * T, 0f), c.Rx + m, c.Rz + m, c.Rx + m, c.Rz + m, 0.34f * T, c.Segs, c.FoundSide, true, false, c.Off);
            else
                B.BoxAt(V(0f, -0.3f * T, 0f), w + m, 0.34f * T, d + m, c.FoundSide, c.FoundTop);
        }

        private static float PolyOffset(int segs)
        {
            float step = 360f / segs;
            return (270f % step) - step * 0.5f;
        }

        /// <summary>Chooses box / octagon / round body from roundness (or <paramref name="forceRound"/>) and records it in ctx.</summary>
        private static void SetupBody(ref Ctx c, float w, float d, bool forceRound)
        {
            float r = forceRound ? Mathf.Max(c.Roundness, 0.85f) : c.Roundness;
            if (r < 0.3f) { c.Segs = 0; c.Rx = w * 0.5f; c.Rz = d * 0.5f; c.Off = 0f; return; }
            c.Segs = r < 0.6f ? 8 : (r < 0.85f ? 10 : 12);
            c.Off = PolyOffset(c.Segs);
            float k = 1f / Mathf.Cos(Mathf.PI / c.Segs);
            c.Rx = w * 0.5f * (c.Segs == 8 ? k : 1f);
            c.Rz = d * 0.5f * (c.Segs == 8 ? k : 1f);
        }

        private static void Body(ref Ctx c, Vector3 b, float w, float d, float h, Color32 side, Color32 top, float taper = 1f)
        {
            if (c.Segs == 0) B.BoxAt(b, w, h, d, side, top);
            else B.Frustum(b, c.Rx, c.Rz, c.Rx * taper, c.Rz * taper, h, c.Segs, side, true, false, c.Off);
        }

        private static void Openings(ref Ctx c, Vector3 b, float w, float d, float h, bool door)
        {
            float floorPitch = 0.34f * T;
            int floors = Mathf.Clamp((int)(h / floorPitch), 1, 12);
            float floorH = h / floors;
            float doorW = Mathf.Min(0.15f * T, w * 0.3f), doorH = Mathf.Min(0.27f * T, floorH * 0.78f);
            float winW = 0.085f * T, winH = Mathf.Min(0.11f * T, floorH * 0.4f);
            bool bands = c.Age >= AgeId.Information;
            if (c.Segs == 0)
            {
                if (door) B.FacadeRect(b + V(0f, doorH * 0.5f, -d * 0.5f), Vector3.back, doorW, doorH, c.Door);
                for (int f = 0; f < floors; f++)
                {
                    float y = b.y + floorH * (f + 0.6f);
                    if (bands)
                    {
                        float bh = floorH * 0.32f;
                        if (!(door && f == 0)) B.FacadeRect(V(b.x, y, b.z - d * 0.5f), Vector3.back, w * 0.9f, bh, c.Glass);
                        B.FacadeRect(V(b.x, y, b.z + d * 0.5f), Vector3.forward, w * 0.9f, bh, c.Glass);
                        B.FacadeRect(V(b.x - w * 0.5f, y, b.z), Vector3.left, d * 0.9f, bh, c.Glass);
                        B.FacadeRect(V(b.x + w * 0.5f, y, b.z), Vector3.right, d * 0.9f, bh, c.Glass);
                        continue;
                    }
                    int nx = Mathf.Max(1, (int)(w / (0.3f * T)));
                    for (int i = 0; i < nx; i++)
                    {
                        float x = b.x - w * 0.5f + w * (i + 0.5f) / nx;
                        bool nearDoor = door && f == 0 && Mathf.Abs(x - b.x) < doorW;
                        if (!nearDoor) B.FacadeRect(V(x, y, b.z - d * 0.5f), Vector3.back, winW, winH, c.Window);
                        B.FacadeRect(V(x, y, b.z + d * 0.5f), Vector3.forward, winW, winH, c.Window);
                    }
                    int nz = Mathf.Max(1, (int)(d / (0.3f * T)));
                    for (int i = 0; i < nz; i++)
                    {
                        float z = b.z - d * 0.5f + d * (i + 0.5f) / nz;
                        B.FacadeRect(V(b.x - w * 0.5f, y, z), Vector3.left, winW, winH, c.Window);
                        B.FacadeRect(V(b.x + w * 0.5f, y, z), Vector3.right, winW, winH, c.Window);
                    }
                }
                if (c.Age == AgeId.Ascension)
                {
                    float gy = b.y + h - 0.03f * T;
                    B.FacadeRect(V(b.x, gy, b.z - d * 0.5f), Vector3.back, w, 0.025f * T, c.Glow);
                    B.FacadeRect(V(b.x, gy, b.z + d * 0.5f), Vector3.forward, w, 0.025f * T, c.Glow);
                    B.FacadeRect(V(b.x - w * 0.5f, gy, b.z), Vector3.left, d, 0.025f * T, c.Glow);
                    B.FacadeRect(V(b.x + w * 0.5f, gy, b.z), Vector3.right, d, 0.025f * T, c.Glow);
                }
            }
            else
            {
                float k = Mathf.Cos(Mathf.PI / c.Segs);
                if (door) B.FacadeRect(b + V(0f, doorH * 0.5f, -c.Rz * k), Vector3.back, doorW, doorH, c.Door);
                for (int f = 0; f < floors; f++)
                {
                    float y = b.y + floorH * (f + 0.6f);
                    for (int i = 0; i < c.Segs; i++)
                    {
                        float m = c.Off + 360f * (i + 0.5f) / c.Segs;
                        if (f == 0 && door && Mathf.Abs(Mathf.DeltaAngle(m, 270f)) < 40f) continue;
                        if (!bands && (i & 1) == 1) continue;
                        float mr = m * Mathf.Deg2Rad;
                        Vector3 o = V(Mathf.Cos(mr), 0f, Mathf.Sin(mr));
                        Vector3 p = V(b.x + o.x * c.Rx * k, y, b.z + o.z * c.Rz * k);
                        float faceW = 2f * Mathf.Min(c.Rx, c.Rz) * Mathf.Sin(Mathf.PI / c.Segs);
                        if (bands) B.FacadeRect(p, o, faceW * 0.9f, floorH * 0.32f, c.Glass);
                        else B.FacadeRect(p, o, Mathf.Min(winW, faceW * 0.6f), winH, c.Window);
                    }
                }
            }
        }

        private static void Spike(ref Ctx c, Vector3 top, float scale)
        {
            B.Cone(top - V(0f, 0.01f, 0f), 0.03f * T * scale, 0.14f * T * scale, 4, B.Shade(c.Trim, 0.8f));
        }

        /// <summary>Roof over a body whose eave is at <paramref name="e"/>. Returns the roof's top Y.</summary>
        private static float Roof(ref Ctx c, Vector3 e, float w, float d, RoofStyle roof)
        {
            bool round = c.Segs > 0;
            int segs = round ? c.Segs : 8;
            float off = round ? c.Off : 22.5f;
            float rx = round ? c.Rx : w * 0.5f, rz = round ? c.Rz : d * 0.5f;
            float m = Mathf.Min(w, d);
            if (round && (roof == RoofStyle.Gable || roof == RoofStyle.Hip)) roof = RoofStyle.Cone;
            switch (roof)
            {
                case RoofStyle.Flat:
                {
                    float slab = 0.04f * T, ph = 0.07f * T, t = 0.03f * T;
                    float py = e.y + slab;
                    if (round)
                    {
                        B.Frustum(e, rx * 1.03f, rz * 1.03f, rx * 1.03f, rz * 1.03f, slab, segs, c.Trim, true, false, off);
                        B.Frustum(V(e.x, py, e.z), rx * 1.03f, rz * 1.03f, rx * 1.03f, rz * 1.03f, ph, segs, c.WallDark, false, false, off);
                    }
                    else
                    {
                        float W2 = w + 0.03f * T, D2 = d + 0.03f * T;
                        B.BoxAt(e, W2, slab, D2, c.Trim, c.RoofC);
                        B.Box(V(e.x - W2 * 0.5f, py, e.z - D2 * 0.5f), V(e.x + W2 * 0.5f, py + ph, e.z - D2 * 0.5f + t), c.WallDark, c.Trim);
                        B.Box(V(e.x - W2 * 0.5f, py, e.z + D2 * 0.5f - t), V(e.x + W2 * 0.5f, py + ph, e.z + D2 * 0.5f), c.WallDark, c.Trim);
                        B.Box(V(e.x - W2 * 0.5f, py, e.z - D2 * 0.5f + t), V(e.x - W2 * 0.5f + t, py + ph, e.z + D2 * 0.5f - t), c.WallDark, c.Trim);
                        B.Box(V(e.x + W2 * 0.5f - t, py, e.z - D2 * 0.5f + t), V(e.x + W2 * 0.5f, py + ph, e.z + D2 * 0.5f - t), c.WallDark, c.Trim);
                    }
                    if (c.Age >= AgeId.Modern && m > 0.35f * T)
                        B.BoxAt(V(e.x + w * 0.14f, py, e.z + d * 0.1f), m * 0.26f, 0.08f * T, m * 0.22f, new Color32(140, 144, 150, 255), new Color32(110, 114, 120, 255));
                    return py + ph;
                }
                case RoofStyle.Gable:
                {
                    float rise = Mathf.Min(0.5f * m, 0.7f * T);
                    B.GableRoof(e, w, d, rise, 0.06f * T, c.RoofC, c.WallDark);
                    return e.y + rise;
                }
                case RoofStyle.Hip:
                {
                    float rise = Mathf.Min(0.4f * m, 0.6f * T);
                    B.HipRoof(e, w, d, rise, 0.06f * T, c.RoofC);
                    return e.y + rise;
                }
                case RoofStyle.Pyramid:
                {
                    float rise = 0.6f * m;
                    if (round) B.Frustum(e, rx * 1.15f, rz * 1.15f, 0f, 0f, rise, segs, c.RoofC, false, false, off);
                    else B.HipRoof(e, w, d, rise, 0.05f * T, c.RoofC, true);
                    B.BoxAt(V(e.x, e.y + rise * 0.82f, e.z), m * 0.12f, rise * 0.22f, m * 0.12f, c.Accent, c.Accent);
                    return e.y + rise * 1.04f;
                }
                case RoofStyle.Dome:
                {
                    float rise = 0.5f * m;
                    B.Dome(e, rx * 1.03f, rz * 1.03f, rise, 3, round ? segs : 10, c.RoofC, off);
                    B.Cylinder(V(e.x, e.y + rise * 0.98f, e.z), 0.025f * T, 0.08f * T, 5, c.Accent);
                    return e.y + rise + 0.08f * T;
                }
                case RoofStyle.Cone:
                {
                    float rise = 0.8f * m;
                    float k = round ? 1.18f : 1.45f;
                    B.Frustum(e - V(0f, 0.02f * T, 0f), rx * k, rz * k, 0f, 0f, rise, segs, c.RoofC, false, false, off);
                    Spike(ref c, V(e.x, e.y + rise * 0.96f, e.z), 1f);
                    return e.y + rise + 0.12f * T;
                }
                case RoofStyle.Spire:
                {
                    float rise = 1.7f * m;
                    float k = round ? 1.12f : 1.42f;
                    B.Frustum(e, rx * k, rz * k, 0f, 0f, rise, segs, c.RoofDark, false, false, off);
                    B.Blob(V(e.x, e.y + rise, e.z), 0.03f * T, 0.03f * T, 0.03f * T, 5, c.Accent);
                    return e.y + rise + 0.03f * T;
                }
                case RoofStyle.Thatch:
                    return Thatch(ref c, e, rx * (round ? 1f : 1.12f), rz * (round ? 1f : 1.12f));
                case RoofStyle.Tiered:
                    return Tiered(ref c, e, w, d, round);
                case RoofStyle.Leaf:
                    return LeafRoof(ref c, e, rx * (round ? 1f : 1.1f), rz * (round ? 1f : 1.1f));
                default:
                {
                    float rise = Mathf.Min(0.5f * m, 0.7f * T);
                    B.GableRoof(e, w, d, rise, 0.06f * T, c.RoofC, c.WallDark);
                    return e.y + rise;
                }
            }
        }

        private static float Thatch(ref Ctx c, Vector3 e, float rx, float rz)
        {
            float m = Mathf.Min(rx, rz) * 2f;
            float h = 0.7f * m;
            B.Frustum(e - V(0f, 0.05f * T, 0f), rx * 1.25f, rz * 1.25f, rx * 0.8f, rz * 0.8f, h * 0.42f, 9, c.RoofDark, false, false, c.Bucket * 13f);
            B.Frustum(e + V(0f, h * 0.28f, 0f), rx * 0.98f, rz * 0.98f, rx * 0.48f, rz * 0.48f, h * 0.38f, 8, c.RoofC, false, false, 20f + c.Bucket * 7f);
            B.Dome(e + V(0f, h * 0.6f, 0f), rx * 0.56f, rz * 0.56f, h * 0.32f, 2, 7, c.RoofLight, 35f);
            return e.y + h * 0.92f;
        }

        private static float LeafRoof(ref Ctx c, Vector3 e, float rx, float rz)
        {
            float m = Mathf.Min(rx, rz) * 2f;
            B.Frustum(e + V(0f, 0.02f * T, 0f), rx * 1.3f, rz * 1.3f, rx * 1.18f, rz * 1.18f, -0.08f * T, 9, c.LeafDark, false, false, 10f);
            B.Dome(e + V(0f, 0.02f * T, 0f), rx * 1.3f, rz * 1.3f, 0.26f * m, 2, 9, c.LeafDark, 10f);
            B.Dome(e + V(0.05f * rx, 0.17f * m, -0.04f * rz), rx * 0.95f, rz * 0.95f, 0.3f * m, 2, 8, c.Leaf, 30f);
            B.Dome(e + V(-0.06f * rx, 0.37f * m, 0.05f * rz), rx * 0.55f, rz * 0.55f, 0.24f * m, 2, 7, c.LeafLight, 50f);
            return e.y + 0.62f * m;
        }

        private static float Tiered(ref Ctx c, Vector3 e, float w, float d, bool round)
        {
            int tiers = 2 + (c.Bucket >= 2 ? 1 : 0);
            float m = Mathf.Min(w, d);
            float y = e.y, s = 1f;
            for (int t = 0; t < tiers; t++)
            {
                float rise = 0.2f * m * s;
                Color32 col = (t & 1) == 0 ? c.RoofC : c.RoofDark;
                if (round) B.Frustum(V(e.x, y, e.z), c.Rx * s * 1.3f, c.Rz * s * 1.3f, c.Rx * s * 0.35f, c.Rz * s * 0.35f, rise, c.Segs, col, true, false, c.Off);
                else B.HipRoof(V(e.x, y, e.z), w * s, d * s, rise, 0.13f * T * s, col);
                y += rise * 0.45f;
                if (t < tiers - 1)
                {
                    s *= 0.66f;
                    float wallH = 0.16f * T * Mathf.Max(0.6f, s);
                    B.BoxAt(V(e.x, y, e.z), w * s * 0.8f, wallH, d * s * 0.8f, c.Wall, c.WallDark);
                    y += wallH;
                }
            }
            float tipH = 0.16f * T;
            B.Cylinder(V(e.x, y, e.z), 0.018f * T, tipH, 5, c.Accent);
            return y + tipH;
        }

        private static void Chimney(ref Ctx c, Vector3 b, float w, float d, float wallTop, float roofTop)
        {
            bool industrial = c.Age >= AgeId.Industrial;
            if (!industrial && !(c.Age >= AgeId.Medieval && c.Bucket >= 2)) return;
            if (c.Roof == RoofStyle.Leaf || c.Roof == RoofStyle.Dome) return;
            float x = b.x + w * 0.27f, z = b.z + d * 0.16f;
            float y0 = wallTop - 0.05f * T;
            float h = Mathf.Max(roofTop - y0, 0.1f * T) + 0.08f * T;
            float s = 0.075f * T;
            Color32 side = industrial ? c.Brick : c.Stone;
            B.BoxAt(V(x, y0, z), s, h, s, side, c.Soot);
            B.BoxAt(V(x, y0 + h - 0.025f * T, z), s * 1.25f, 0.03f * T, s * 1.25f, B.Shade(side, 0.8f), c.Soot);
        }

        private static void AscensionCrown(ref Ctx c, Vector3 top)
        {
            if (c.Age != AgeId.Ascension) return;
            B.Blob(top + V(0f, 0.05f * T, 0f), 0.045f * T, 0.045f * T, 0.045f * T, 6, c.Glow);
        }

        private static void Columns(ref Ctx c, float x0, float x1, float z, float y, float h, float r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float x = n == 1 ? (x0 + x1) * 0.5f : x0 + (x1 - x0) * i / (n - 1);
                B.Cylinder(V(x, y, z), r, h, 6, c.Trim, false);
                B.BoxAt(V(x, y + h - 0.02f * T, z), r * 2.6f, 0.02f * T, r * 2.6f, c.Trim, c.Trim);
            }
        }

        // =============================================================================== shapes

        private static void Hut(ref Ctx c)
        {
            float w = Mathf.Min(c.W, c.D) * 0.95f;
            SetupBody(ref c, w, w, true);
            Foundation(ref c, w, w);
            float h = c.H * 0.45f;
            Body(ref c, Vector3.zero, w, w, h, c.Wall, c.WallDark, 0.94f);
            Openings(ref c, Vector3.zero, w, w, Mathf.Min(h, 0.33f * T), true);
            RoofStyle r = c.Roof == RoofStyle.Flat || c.Roof == RoofStyle.Gable || c.Roof == RoofStyle.Hip ? RoofStyle.Cone : c.Roof;
            float top = Roof(ref c, V(0f, h, 0f), w * 0.94f, w * 0.94f, r);
            if (c.Age == AgeId.Dawn)
            {
                // tent poles poking out of the roof tip
                for (int i = 0; i < 3; i++)
                {
                    B.SetTransform(V(0f, top - 0.1f * T, 0f), i * 120f + 15f);
                    B.BoxAt(V(0.02f * T, 0f, 0f), 0.015f * T, 0.16f * T, 0.015f * T, c.WoodDark, c.WoodDark);
                }
                B.ResetTransform();
            }
            AscensionCrown(ref c, V(0f, top, 0f));
        }

        private static void House(ref Ctx c)
        {
            SetupBody(ref c, c.W, c.D, false);
            Foundation(ref c, c.W, c.D);
            float h = c.H * 0.62f;
            Body(ref c, Vector3.zero, c.W, c.D, h, c.Wall, c.WallDark);
            Openings(ref c, Vector3.zero, c.W, c.D, h, true);
            float top = Roof(ref c, V(0f, h, 0f), c.W, c.D, c.Roof);
            Chimney(ref c, Vector3.zero, c.Segs > 0 ? c.Rx * 1.2f : c.W, c.Segs > 0 ? c.Rz * 1.2f : c.D, h, top);
            if (c.Bucket == 3 && c.Segs == 0)
            {
                // small porch roof over the door
                B.BoxAt(V(0f, Mathf.Min(0.3f * T, h * 0.75f), -c.D * 0.5f - 0.05f * T), Mathf.Min(0.3f * T, c.W * 0.5f), 0.025f * T, 0.1f * T, c.Trim, c.RoofDark);
            }
            AscensionCrown(ref c, V(0f, top, 0f));
        }

        private static void Hall(ref Ctx c)
        {
            SetupBody(ref c, c.W, c.D, false);
            bool portico = c.Segs == 0 && (c.Age == AgeId.Classical || (c.Age == AgeId.Renaissance && c.Bucket >= 2));
            Foundation(ref c, c.W, c.D);
            float h = c.H * 0.72f;
            if (portico)
            {
                float depth = c.D * 0.78f;
                float zc = c.D * 0.11f;
                B.BoxAt(V(0f, 0f, 0f), c.W + 0.06f * T, 0.05f * T, c.D + 0.06f * T, c.Stone, c.Trim); // stylobate
                Vector3 bb = V(0f, 0.05f * T, zc);
                B.BoxAt(bb, c.W * 0.94f, h - 0.05f * T, depth, c.Wall, c.WallDark);
                Openings(ref c, bb, c.W * 0.94f, depth, h - 0.05f * T, true);
                int n = Mathf.Max(4, MathUtil.RoundToInt(c.W / (0.2f * T)));
                Columns(ref c, -c.W * 0.46f, c.W * 0.46f, -c.D * 0.46f, 0.05f * T, h - 0.05f * T, 0.032f * T, n);
            }
            else
            {
                Body(ref c, Vector3.zero, c.W, c.D, h, c.Wall, c.WallDark);
                Openings(ref c, Vector3.zero, c.W, c.D, h, true);
                if (c.Segs == 0 && c.Age >= AgeId.Iron && c.Age <= AgeId.Renaissance)
                {
                    // buttresses along the long sides
                    bool alongX = c.W >= c.D;
                    float len = alongX ? c.W : c.D;
                    int n = Mathf.Max(2, (int)(len / (0.35f * T)));
                    for (int i = 0; i <= n; i++)
                    {
                        float u = -len * 0.5f + len * i / n;
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            if (alongX) B.BoxAt(V(u, 0f, sgn * c.D * 0.5f), 0.05f * T, h * 0.7f, 0.08f * T, c.Stone, c.Stone);
                            else B.BoxAt(V(sgn * c.W * 0.5f, 0f, u), 0.08f * T, h * 0.7f, 0.05f * T, c.Stone, c.Stone);
                        }
                    }
                }
            }
            float top = Roof(ref c, V(0f, h, 0f), c.W, c.D, c.Roof);
            Chimney(ref c, Vector3.zero, c.W, c.D, h, top);
            AscensionCrown(ref c, V(0f, top, 0f));
        }

        private static void Tower(ref Ctx c)
        {
            float m = Mathf.Min(c.W, c.D);
            bool round = c.Roundness >= 0.3f || (c.Bucket & 1) == 1;
            float w = round ? m * 0.82f : m * 0.74f;
            if (round) { c.Segs = 8; c.Off = PolyOffset(8); c.Rx = c.Rz = w * 0.5f; }
            else { c.Segs = 0; c.Rx = c.Rz = w * 0.5f; }
            Foundation(ref c, w, w);
            float h = c.H * 1.25f;
            Color32 side = c.Age >= AgeId.Iron && c.Age <= AgeId.Renaissance ? B.Mix(c.Wall, c.Stone, 0.4f) : c.Wall;
            Body(ref c, Vector3.zero, w, w, h, side, B.Shade(side, 0.85f));
            Openings(ref c, Vector3.zero, w, w, h, true);
            bool crenel = c.Roof == RoofStyle.Flat || (c.Age >= AgeId.Iron && c.Age <= AgeId.Renaissance && c.Bucket < 2);
            float top;
            Vector3 e = V(0f, h, 0f);
            float tw = w;
            if (crenel && c.Age < AgeId.Industrial)
            {
                float pr = tw * 0.62f, ph = 0.06f * T;
                B.Frustum(e, pr, pr, pr, pr, ph, 8, side, true, false, 22.5f);
                int n = 8;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / n;
                    B.BoxAt(V(Mathf.Cos(a) * pr * 0.88f, h + ph, Mathf.Sin(a) * pr * 0.88f), 0.07f * T, 0.08f * T, 0.07f * T, side, B.Shade(side, 0.9f));
                }
                top = h + ph + 0.08f * T;
            }
            else
            {
                RoofStyle r = c.Roof == RoofStyle.Gable || c.Roof == RoofStyle.Hip || c.Roof == RoofStyle.Thatch ? RoofStyle.Cone : c.Roof;
                if (c.Age >= AgeId.Modern && r != RoofStyle.Leaf) r = RoofStyle.Flat;
                top = Roof(ref c, e, tw, tw, r);
            }
            if (c.Age >= AgeId.Modern)
            {
                B.Cylinder(V(0f, top, 0f), 0.012f * T, 0.45f * T, 4, new Color32(170, 175, 180, 255), false);
                B.Blob(V(0f, top + 0.45f * T, 0f), 0.02f * T, 0.02f * T, 0.02f * T, 4, c.Age == AgeId.Ascension ? c.Glow : new Color32(230, 50, 40, 255));
                top += 0.47f * T;
            }
            if (c.Age >= AgeId.Information)
            {
                B.Frustum(V(0f, h * 0.7f, 0f), tw * 0.56f, tw * 0.56f, tw * 0.56f, tw * 0.56f, 0.05f * T, 8, c.Glow, true, true, 22.5f);
            }
        }

        private static void DomeShape(ref Ctx c)
        {
            c.Segs = 0;
            Foundation(ref c, c.W, c.D);
            float h0 = c.H * 0.3f;
            B.BoxAt(Vector3.zero, c.W, h0, c.D, c.Wall, c.WallDark);
            Openings(ref c, Vector3.zero, c.W, c.D, h0, true);
            float r = Mathf.Min(c.W, c.D) * 0.4f;
            float h1 = c.H * 0.3f;
            B.Cylinder(V(0f, h0, 0f), r, h1, 12, c.WallDark);
            if (c.Age == AgeId.Classical || c.Age == AgeId.Iron)
            {
                for (int i = 0; i < 10; i++)
                {
                    float a = i * Mathf.PI * 2f / 10f;
                    B.Cylinder(V(Mathf.Cos(a) * r * 1.15f, h0, Mathf.Sin(a) * r * 1.15f), 0.022f * T, h1, 5, c.Trim, false);
                }
                B.Frustum(V(0f, h0 + h1 - 0.03f * T, 0f), r * 1.24f, r * 1.24f, r * 1.24f, r * 1.24f, 0.04f * T, 12, c.Trim, true);
            }
            Color32 domeCol = c.Age >= AgeId.Information && c.Age < AgeId.Ascension ? c.Glass
                : c.Age == AgeId.Ascension ? c.Accent
                : c.Age == AgeId.Classical ? c.Wall : c.RoofC;
            float dr = r * 1.03f, dh = r * 0.95f;
            B.Dome(V(0f, h0 + h1, 0f), dr, dr, dh, 4, 12, domeCol);
            float top = h0 + h1 + dh;
            if (c.Age >= AgeId.Renaissance)
            {
                B.Cylinder(V(0f, top - 0.02f * T, 0f), r * 0.16f, 0.1f * T, 6, c.Trim);
                B.Cone(V(0f, top + 0.08f * T, 0f), r * 0.2f, 0.1f * T, 6, domeCol);
                top += 0.18f * T;
            }
            if (c.Age == AgeId.Ascension)
                B.Frustum(V(0f, h0 + h1 - 0.02f * T, 0f), r * 1.08f, r * 1.08f, r * 1.08f, r * 1.08f, 0.03f * T, 12, c.Glow, true, true);
            AscensionCrown(ref c, V(0f, top, 0f));
        }

        private static void PyramidShape(ref Ctx c)
        {
            c.Segs = 0;
            int steps = 4 + (c.Bucket & 1);
            float total = c.H * 1.1f;
            float stepH = total / (steps + 1.3f);
            float y = 0f;
            Foundation(ref c, c.W, c.D);
            float f = 1f;
            for (int k = 0; k < steps; k++)
            {
                f = 1f - k * 0.78f / steps;
                B.BoxAt(V(0f, y, 0f), c.W * f, stepH, c.D * f, (k & 1) == 0 ? c.Wall : c.WallDark, c.Wall);
                y += stepH;
            }
            // front staircase ramp
            float sw = Mathf.Min(0.16f * T, c.W * 0.2f);
            float z0 = -c.D * 0.5f - 0.02f * T, z1 = -c.D * f * 0.5f - 0.005f;
            B.QuadFacing(V(-sw, 0.01f, z0), V(sw, 0.01f, z0), V(sw, y, z1), V(-sw, y, z1), V(0f, 1f, -1f), c.Trim);
            // shrine on top
            float sf = f * 0.62f;
            float sh = stepH * 1.2f;
            B.BoxAt(V(0f, y, 0f), c.W * sf, sh, c.D * sf, c.Accent, c.WallDark);
            B.FacadeRect(V(0f, y + sh * 0.4f, -c.D * sf * 0.5f), Vector3.back, c.W * sf * 0.35f, sh * 0.6f, c.Door);
            float top = Roof(ref c, V(0f, y + sh, 0f), c.W * sf, c.D * sf, c.Roof == RoofStyle.Flat ? RoofStyle.Flat : RoofStyle.Pyramid);
            if (c.Age == AgeId.Ascension)
                B.Frustum(V(0f, y + sh, 0f), c.W * sf * 0.25f, c.D * sf * 0.25f, 0f, 0f, top - (y + sh) + 0.1f * T, 4, c.Glow, false, false, 45f);
        }

        private static void SpireShape(ref Ctx c)
        {
            c.Segs = 0;
            bool alongX = c.W > c.D;
            float L = Mathf.Max(c.W, c.D), S = Mathf.Min(c.W, c.D);
            if (alongX) B.SetTransform(Vector3.zero, 90f);
            bool small = L < 1.5f * T;
            Color32 towerCol = c.Age >= AgeId.Iron && c.Age <= AgeId.Renaissance ? B.Mix(c.Wall, c.Stone, 0.35f) : c.Wall;
            if (!small)
            {
                // nave along local Z with a front tower at -Z
                B.BoxAt(V(0f, -0.3f * T, 0f), S + 0.05f * T, 0.34f * T, L + 0.05f * T, c.FoundSide, c.FoundTop);
                float nh = c.H * 0.55f, nw = S * 0.66f, nl = L * 0.74f, nz = L * 0.13f;
                B.BoxAt(V(0f, 0f, nz), nw, nh, nl, towerCol, c.WallDark);
                B.GableRoof(V(0f, nh, nz), nw, nl, Mathf.Min(nw * 0.55f, 0.6f * T), 0.04f * T, c.RoofDark, B.Shade(towerCol, 0.9f));
                int n = Mathf.Max(2, (int)(nl / (0.25f * T)));
                Color32 stained = c.Age >= AgeId.Information ? c.Glass : B.Mix(c.Accent, new Color32(60, 80, 160, 255), 0.5f);
                for (int i = 0; i < n; i++)
                {
                    float z = nz - nl * 0.5f + nl * (i + 0.5f) / n;
                    B.FacadeRect(V(-nw * 0.5f, nh * 0.5f, z), Vector3.left, 0.06f * T, nh * 0.5f, stained);
                    B.FacadeRect(V(nw * 0.5f, nh * 0.5f, z), Vector3.right, 0.06f * T, nh * 0.5f, stained);
                    B.BoxAt(V(-nw * 0.5f - 0.03f * T, 0f, z + nl * 0.5f / n), 0.06f * T, nh * 0.75f, 0.05f * T, c.Stone, c.Stone);
                    B.BoxAt(V(nw * 0.5f + 0.03f * T, 0f, z + nl * 0.5f / n), 0.06f * T, nh * 0.75f, 0.05f * T, c.Stone, c.Stone);
                }
            }
            float tw = small ? S * 0.6f : S * 0.42f;
            float tz = small ? 0f : -L * 0.5f + tw * 0.55f;
            float th = c.H * (small ? 0.9f : 1.0f);
            if (small) B.BoxAt(V(0f, -0.3f * T, 0f), tw + 0.08f * T, 0.34f * T, tw + 0.08f * T, c.FoundSide, c.FoundTop);
            B.BoxAt(V(0f, 0f, tz), tw, th, tw, towerCol, c.WallDark);
            B.FacadeRect(V(0f, 0.12f * T, tz - tw * 0.5f), Vector3.back, tw * 0.4f, 0.24f * T, c.Door);
            Color32 rose = c.Age >= AgeId.Information ? c.Glow : B.Mix(c.Accent, new Color32(70, 90, 170, 255), 0.4f);
            B.FacadeRect(V(0f, th * 0.62f, tz - tw * 0.5f), Vector3.back, tw * 0.35f, tw * 0.35f, rose);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                B.SetTransform(alongX ? V(tz, 0f, 0f) : V(0f, 0f, tz), (alongX ? 90f : 0f) + a);
                B.FacadeRect(V(0f, th * 0.85f, -tw * 0.5f), Vector3.back, tw * 0.18f, th * 0.1f, c.Window);
            }
            if (alongX) B.SetTransform(Vector3.zero, 90f); else B.ResetTransform();
            float sh = c.H * 0.95f;
            Color32 spireCol = c.Age >= AgeId.Information ? c.Glass : c.RoofDark;
            B.Frustum(V(0f, th, tz), tw * 0.72f, tw * 0.72f, 0f, 0f, sh, 4, spireCol, false, false, 45f);
            B.Cylinder(V(0f, th + sh * 0.95f, tz), 0.012f * T, 0.12f * T, 4, c.Accent, false);
            B.BoxAt(V(0f, th + sh * 0.95f + 0.07f * T, tz), 0.08f * T, 0.015f * T, 0.015f * T, c.Accent, c.Accent);
            if (c.Age == AgeId.Ascension) B.Blob(V(0f, th + sh + 0.12f * T, tz), 0.04f * T, 0.04f * T, 0.04f * T, 6, c.Glow);
            B.ResetTransform();
        }

        private static void Field(ref Ctx c)
        {
            float fw = c.FootW * 0.96f, fd = c.FootD * 0.96f;
            Color32 soil = B.Mix(new Color32(112, 82, 54, 255), c.Wall, 0.1f);
            B.BoxAt(V(0f, -0.04f, 0f), fw, 0.07f * T, fd, B.Shade(soil, 0.8f), B.Shade(soil, 0.85f));
            Color32 crop;
            switch (c.Bucket)
            {
                case 0: crop = new Color32(206, 172, 82, 255); break;   // wheat
                case 1: crop = new Color32(96, 152, 62, 255); break;    // greens
                case 2: crop = new Color32(70, 122, 56, 255); break;    // tubers
                default: crop = new Color32(152, 182, 92, 255); break;  // flax / barley
            }
            if (c.Age >= AgeId.Modern) crop = B.Shade(crop, 1.05f);
            c.LodTop = crop;
            bool alongX = fw >= fd;
            float across = alongX ? fd : fw, along = alongX ? fw : fd;
            int n = Mathf.Max(3, MathUtil.RoundToInt(across / (0.15f * T)));
            float pitch = across / n;
            float y0 = -0.04f + 0.07f * T;
            for (int i = 0; i < n; i++)
            {
                float v = -across * 0.5f + pitch * (i + 0.5f);
                float ch = (0.045f + Rand(ref c) * 0.03f) * T;
                Color32 rc = (i & 1) == 0 ? crop : B.Shade(crop, 0.9f);
                if (alongX) B.BoxAt(V(0f, y0, v), along * 0.92f, ch, pitch * 0.55f, B.Shade(rc, 0.8f), rc);
                else B.BoxAt(V(v, y0, 0f), pitch * 0.55f, ch, along * 0.92f, B.Shade(rc, 0.8f), rc);
            }
            if (c.Age >= AgeId.Iron && c.Age < AgeId.Modern && c.Bucket >= 1)
            {
                float hx = fw * 0.5f, hz = fd * 0.5f, ry = y0 + 0.06f * T, t = 0.015f * T;
                B.Box(V(-hx, ry, -hz), V(hx, ry + t, -hz + t), c.Wood, c.Wood);
                B.Box(V(-hx, ry, hz - t), V(hx, ry + t, hz), c.Wood, c.Wood);
                B.Box(V(-hx, ry, -hz), V(-hx + t, ry + t, hz), c.Wood, c.Wood);
                B.Box(V(hx - t, ry, -hz), V(hx, ry + t, hz), c.Wood, c.Wood);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        B.BoxAt(V(sx * (hx - t), y0, sz * (hz - t)), 0.025f * T, 0.09f * T, 0.025f * T, c.WoodDark, c.WoodDark);
            }
            if (c.Age >= AgeId.Industrial && c.FootW * c.FootD >= 2f * T * T)
            {
                Vector3 p = V(fw * 0.5f - 0.13f * T, y0, fd * 0.5f - 0.13f * T);
                Color32 metal = c.Age >= AgeId.Modern ? new Color32(190, 195, 200, 255) : c.Brick;
                B.Cylinder(p, 0.1f * T, 0.45f * T, 8, metal, false);
                B.Dome(p + V(0f, 0.45f * T, 0f), 0.1f * T, 0.1f * T, 0.07f * T, 2, 8, B.Shade(metal, 0.85f));
            }
        }

        private static void Dock(ref Ctx c)
        {
            float fw = c.FootW * 0.96f, fd = c.FootD * 0.96f;
            bool stone = c.Age >= AgeId.Classical;
            Color32 deck = stone ? c.Stone : c.Wood;
            Color32 deckSide = stone ? B.Shade(c.Stone, 0.8f) : c.WoodDark;
            B.BoxAt(V(0f, -0.03f * T, 0f), fw, 0.08f * T, fd, deckSide, deck);
            if (!stone)
            {
                bool alongX = fw >= fd;
                int planks = Mathf.Max(3, (int)((alongX ? fw : fd) / (0.12f * T)));
                for (int i = 0; i < planks; i += 2)
                {
                    float u = -(alongX ? fw : fd) * 0.5f + (alongX ? fw : fd) * (i + 0.5f) / planks;
                    if (alongX) B.FlatRect(V(u, 0.051f * T, 0f), (fw / planks) * 0.9f, fd * 0.98f, true, B.Shade(c.Wood, 0.9f));
                    else B.FlatRect(V(0f, 0.051f * T, u), fw * 0.98f, (fd / planks) * 0.9f, true, B.Shade(c.Wood, 0.9f));
                }
            }
            // pilings
            int nx = Mathf.Max(2, (int)(fw / (0.3f * T)) + 1), nz = Mathf.Max(2, (int)(fd / (0.3f * T)) + 1);
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    if (ix != 0 && ix != nx - 1 && iz != 0 && iz != nz - 1) continue;
                    float x = -fw * 0.47f + fw * 0.94f * ix / (nx - 1), z = -fd * 0.47f + fd * 0.94f * iz / (nz - 1);
                    B.Cylinder(V(x, -1.2f * T, z), 0.035f * T, 1.25f * T, 5, deckSide);
                }
            // bollards
            for (int i = 0; i < 3; i++)
                B.Cylinder(V(-fw * 0.35f + fw * 0.35f * i, 0.05f * T, -fd * 0.42f), 0.025f * T, 0.06f * T, 5, c.Soot);
            if (c.Bucket >= 1)
            {
                Vector3 sb = V(-fw * 0.22f, 0.05f * T, fd * 0.22f);
                float sw = Mathf.Min(fw * 0.4f, 0.4f * T), sd = Mathf.Min(fd * 0.4f, 0.35f * T);
                B.BoxAt(sb, sw, 0.22f * T, sd, c.Wall, c.WallDark);
                B.FacadeRect(sb + V(0f, 0.08f * T, -sd * 0.5f), Vector3.back, sw * 0.4f, 0.16f * T, c.Door);
                if (c.Age >= AgeId.Modern) B.BoxAt(sb + V(0f, 0.22f * T, 0f), sw + 0.02f * T, 0.03f * T, sd + 0.02f * T, c.Trim, c.RoofC);
                else B.GableRoof(sb + V(0f, 0.22f * T, 0f), sw, sd, Mathf.Min(sw, sd) * 0.45f, 0.03f * T, c.RoofC, c.WallDark);
            }
            if (c.Age >= AgeId.Industrial)
            {
                Color32 steel = c.Age >= AgeId.Information ? new Color32(220, 225, 230, 255) : new Color32(200, 150, 40, 255);
                Vector3 p = V(fw * 0.3f, 0.05f * T, 0f);
                B.BoxAt(p, 0.06f * T, 0.75f * T, 0.06f * T, steel, steel);
                B.BoxAt(p + V(-0.1f * T, 0.7f * T, -fd * 0.2f), 0.05f * T, 0.05f * T, fd * 0.75f, steel, steel);
            }
            if (c.Age == AgeId.Ascension)
            {
                B.FacadeRect(V(0f, 0.02f * T, -fd * 0.5f), Vector3.back, fw * 0.9f, 0.02f * T, c.Glow);
                B.FacadeRect(V(0f, 0.02f * T, fd * 0.5f), Vector3.forward, fw * 0.9f, 0.02f * T, c.Glow);
            }
        }

        private static void WallShape(ref Ctx c)
        {
            float fw = c.FootW, fd = c.FootD;
            bool square = Mathf.Abs(fw - fd) < 0.01f;
            // Square pieces fill most of the tile so neighbouring segments merge into a continuous wall;
            // straight multi-tile segments run along their long axis at half thickness.
            float sx, sz;
            if (square) { sx = fw * 0.9f; sz = fd * 0.9f; }
            else if (fw > fd) { sx = fw; sz = fd * 0.5f; }
            else { sx = fw * 0.5f; sz = fd; }
            if (c.Age <= AgeId.Bronze)
            {
                // palisade: sharpened logs
                int nx = MathUtil.Clamp(MathUtil.RoundToInt(sx / (0.16f * T)), 1, 8);
                int nz = MathUtil.Clamp(MathUtil.RoundToInt(sz / (0.16f * T)), 1, 8);
                bool ring = nx > 3 && nz > 3; // big square posts: perimeter logs only
                float r = Mathf.Min(sx / nx, sz / nz) * 0.52f;
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        if (ring && ix > 0 && ix < nx - 1 && iz > 0 && iz < nz - 1) continue;
                        float x = -sx * 0.5f + sx * (ix + 0.5f) / nx, z = -sz * 0.5f + sz * (iz + 0.5f) / nz;
                        float h = c.H * (0.75f + Rand(ref c) * 0.25f);
                        B.Cylinder(V(x, -0.05f, z), r, h, 5, (ix + iz) % 2 == 0 ? c.Wood : c.WoodDark, false);
                        B.Cone(V(x, h - 0.05f, z), r, r * 1.6f, 5, c.WoodDark);
                    }
                c.LodTop = c.Wood;
                return;
            }
            float hh = c.H * (c.Age <= AgeId.Iron ? 0.85f : c.Age <= AgeId.Renaissance ? 1.05f : c.Age <= AgeId.Industrial ? 0.95f : 0.75f);
            Color32 side = c.Age >= AgeId.Industrial ? (c.Age == AgeId.Industrial ? c.Brick : c.Wall) : B.Mix(c.Wall, c.Stone, 0.4f);
            Color32 topC = B.Shade(side, 0.9f);
            B.BoxAt(V(0f, -0.2f * T, 0f), sx, hh + 0.2f * T, sz, side, topC);
            c.LodTop = topC;
            float y = hh;
            if (c.Age <= AgeId.Renaissance)
            {
                float ms = 0.075f * T, mh = 0.08f * T;
                // merlons along the outer edges (both long faces for straight segments, all four for posts)
                for (int edge = 0; edge < 4; edge++)
                {
                    bool xEdge = edge < 2;
                    if (!square && ((fw > fd) != xEdge)) continue;
                    float len = xEdge ? sx : sz;
                    int slots = Mathf.Max(3, MathUtil.RoundToInt(len / ms));
                    float sgn = (edge & 1) == 0 ? -1f : 1f;
                    for (int i = 0; i < slots; i += 2)
                    {
                        float u = -len * 0.5f + len * (i + 0.5f) / slots;
                        if (xEdge) B.BoxAt(V(u, y, sgn * (sz * 0.5f - ms * 0.5f)), len / slots, mh, ms, side, topC);
                        else B.BoxAt(V(sgn * (sx * 0.5f - ms * 0.5f), y, u), ms, mh, len / slots, side, topC);
                    }
                }
            }
            else
            {
                Color32 cap = c.Age >= AgeId.Modern ? new Color32(120, 130, 140, 255) : c.Trim;
                B.BoxAt(V(0f, y, 0f), sx + 0.02f * T, 0.035f * T, sz + 0.02f * T, cap, cap);
                if (c.Age >= AgeId.Information)
                {
                    Color32 band = c.Age == AgeId.Ascension ? c.Glow : c.Glass;
                    B.FacadeRect(V(0f, y * 0.7f, -sz * 0.5f), Vector3.back, sx * 0.96f, 0.04f * T, band);
                    B.FacadeRect(V(0f, y * 0.7f, sz * 0.5f), Vector3.forward, sx * 0.96f, 0.04f * T, band);
                    B.FacadeRect(V(-sx * 0.5f, y * 0.7f, 0f), Vector3.left, sz * 0.96f, 0.04f * T, band);
                    B.FacadeRect(V(sx * 0.5f, y * 0.7f, 0f), Vector3.right, sz * 0.96f, 0.04f * T, band);
                }
            }
        }

        private static void TreeShape(ref Ctx c)
        {
            float S = Mathf.Min(c.FootW, c.FootD);
            float th = c.H * 0.9f;
            float r0 = S * 0.17f, r1 = S * 0.09f;
            var bark = B.Mix(new Color32(92, 68, 46, 255), c.WoodDark, 0.3f);
            c.LodTop = c.Leaf;
            B.Frustum(V(0f, -0.1f * T, 0f), r0, r0, r1, r1, th + 0.1f * T, 8, bark, false);
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + c.Bucket * 11f) * Mathf.Deg2Rad;
                B.Cone(V(Mathf.Cos(a) * r0 * 1.1f, -0.03f, Mathf.Sin(a) * r0 * 1.1f), r0 * 0.55f, th * 0.16f, 5, B.Shade(bark, 0.9f));
            }
            // spiral platforms with small pods
            for (int p = 0; p < 3; p++)
            {
                float y = th * (0.22f + 0.2f * p);
                float pr = r0 * (1f - 0.15f * p) + S * 0.12f;
                B.Frustum(V(0f, y, 0f), pr, pr, pr, pr, 0.035f * T, 10, c.Wood, true, true, p * 17f);
                float a = (p * 130f + c.Bucket * 40f) * Mathf.Deg2Rad;
                Vector3 pod = V(Mathf.Cos(a) * pr * 0.75f, y + 0.035f * T, Mathf.Sin(a) * pr * 0.75f);
                B.Cylinder(pod, S * 0.05f, 0.08f * T, 6, c.Wall, false);
                B.Dome(pod + V(0f, 0.08f * T, 0f), S * 0.07f, S * 0.07f, S * 0.06f, 2, 6, c.LeafDark);
            }
            // branches
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f + c.Bucket * 10f;
                B.SetTransform(V(0f, th * 0.72f, 0f), a);
                B.BoxAt(V(S * 0.17f, 0f, 0f), S * 0.34f, 0.06f * T, 0.06f * T, bark, bark);
            }
            B.ResetTransform();
            // canopy
            B.Blob(V(0f, th + S * 0.08f, 0f), S * 0.48f, S * 0.26f, S * 0.48f, 10, c.Leaf);
            B.Blob(V(0f, th + S * 0.26f, 0f), S * 0.3f, S * 0.18f, S * 0.3f, 8, c.LeafLight, 20f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f + c.Bucket * 10f) * Mathf.Deg2Rad;
                Vector3 p = V(Mathf.Cos(a) * S * 0.34f, th * 0.75f + 0.08f * T, -Mathf.Sin(a) * S * 0.34f);
                B.Blob(p, S * 0.2f, S * 0.13f, S * 0.2f, 7, (i & 1) == 0 ? c.LeafDark : c.Leaf, i * 15f);
            }
            if (c.Age >= AgeId.Information)
            {
                for (int i = 0; i < 5; i++)
                {
                    float a = (i * 72f + 30f) * Mathf.Deg2Rad;
                    B.Blob(V(Mathf.Cos(a) * S * 0.3f, th * 0.68f, Mathf.Sin(a) * S * 0.3f), 0.03f * T, 0.03f * T, 0.03f * T, 5, c.Glow);
                }
            }
        }

        private static void Workshop(ref Ctx c)
        {
            c.Segs = 0;
            float mw = c.W * 0.64f, aw = c.W - mw;
            Vector3 mb = V(-c.W * 0.5f + mw * 0.5f, 0f, 0f);
            Vector3 ab = V(c.W * 0.5f - aw * 0.5f, 0f, 0.02f * T);
            float h = c.H * 0.6f;
            Foundation(ref c, c.W, c.D);
            B.BoxAt(mb, mw, h, c.D, c.Wall, c.WallDark);
            Openings(ref c, mb, mw, c.D, h, true);
            RoofStyle r = c.Roof;
            float top = Roof(ref c, mb + V(0f, h, 0f), mw, c.D, r);
            // lean-to annex with shed roof
            float ah = h * 0.62f;
            B.BoxAt(ab, aw, ah, c.D * 0.9f, c.WallDark, c.WallDark);
            B.ShedRoof(ab + V(0f, ah, 0f), aw + 0.03f * T, c.D * 0.9f + 0.04f * T, 0.1f * T, c.RoofDark, c.WallDark);
            B.FacadeRect(ab + V(0f, ah * 0.4f, -c.D * 0.45f), Vector3.back, aw * 0.6f, ah * 0.65f, c.Door);
            // forge chimney (always) — brick from the Industrial age, stone before
            float s = 0.08f * T;
            Color32 chim = c.Age >= AgeId.Industrial ? c.Brick : c.Stone;
            float cy = h * 0.5f, chH = top - cy + 0.12f * T;
            B.BoxAt(mb + V(-mw * 0.3f, cy, c.D * 0.25f), s, chH, s, chim, c.Soot);
            // awning over the door
            float aY = Mathf.Min(0.3f * T, h * 0.8f);
            Color32 cloth = c.Age >= AgeId.Modern ? c.Trim : c.Accent;
            B.QuadFacing(mb + V(-mw * 0.3f, aY, -c.D * 0.5f), mb + V(mw * 0.3f, aY, -c.D * 0.5f),
                mb + V(mw * 0.3f, aY - 0.06f * T, -c.D * 0.5f - 0.12f * T), mb + V(-mw * 0.3f, aY - 0.06f * T, -c.D * 0.5f - 0.12f * T), V(0f, 1f, -0.4f), cloth);
            // crates
            float fz = -c.D * 0.5f - 0.05f * T;
            B.SetTransform(V(c.W * 0.3f, 0f, fz), 12f + c.Bucket * 20f);
            B.BoxAt(Vector3.zero, 0.07f * T, 0.07f * T, 0.07f * T, c.Wood, B.Shade(c.Wood, 1.1f));
            B.SetTransform(V(c.W * 0.4f, 0f, fz + 0.02f * T), -20f);
            B.BoxAt(Vector3.zero, 0.06f * T, 0.06f * T, 0.06f * T, c.WoodDark, c.Wood);
            B.ResetTransform();
            if (c.Age == AgeId.Ascension) AscensionCrown(ref c, mb + V(0f, top, 0f));
        }

        private static void Factory(ref Ctx c)
        {
            c.Segs = 0;
            bool alongX = c.W >= c.D;
            float L = alongX ? c.W : c.D, S = alongX ? c.D : c.W;
            if (!alongX) B.SetTransform(Vector3.zero, 90f);
            // in this frame the long axis is local X; with yaw 90 local X maps to world -Z
            B.BoxAt(V(0f, -0.3f * T, 0f), L + 0.05f * T, 0.34f * T, S + 0.05f * T, c.FoundSide, c.FoundTop);
            float h = c.H * 0.55f;
            Color32 side = c.Age >= AgeId.Industrial && c.Age < AgeId.Modern ? c.Brick : c.Wall;
            B.BoxAt(Vector3.zero, L, h, S, side, B.Shade(side, 0.85f));
            // sawtooth roof: vertical glass faces + sloped roof faces
            int n = Mathf.Max(2, MathUtil.RoundToInt(L / (0.4f * T)));
            float tw = L / n, tth = Mathf.Min(0.22f * T, tw * 0.6f);
            Color32 roof = c.Age >= AgeId.Modern ? c.RoofC : c.Soot;
            Color32 glass = c.Age >= AgeId.Modern ? c.Glass : new Color32(140, 160, 170, 255);
            for (int i = 0; i < n; i++)
            {
                float x0 = -L * 0.5f + i * tw, x1 = x0 + tw;
                float hz = S * 0.5f;
                B.QuadFacing(V(x0, h, -hz), V(x0, h + tth, -hz), V(x0, h + tth, hz), V(x0, h, hz), Vector3.left, glass);
                B.QuadFacing(V(x0, h + tth, -hz), V(x1, h, -hz), V(x1, h, hz), V(x0, h + tth, hz), V(tth, tw, 0f), roof);
                B.TriFacing(V(x0, h, -hz), V(x0, h + tth, -hz), V(x1, h, -hz), Vector3.back, side);
                B.TriFacing(V(x0, h, hz), V(x0, h + tth, hz), V(x1, h, hz), Vector3.forward, side);
            }
            // windows and loading door on the long front (-Z)
            int nw = Mathf.Max(2, (int)(L / (0.22f * T)));
            for (int i = 0; i < nw; i++)
            {
                float x = -L * 0.5f + L * (i + 0.5f) / nw;
                if (Mathf.Abs(x) < 0.12f * T) continue;
                B.FacadeRect(V(x, h * 0.6f, -S * 0.5f), Vector3.back, 0.08f * T, h * 0.35f, c.Window);
                B.FacadeRect(V(x, h * 0.6f, S * 0.5f), Vector3.forward, 0.08f * T, h * 0.35f, c.Window);
            }
            B.FacadeRect(V(0f, h * 0.3f, -S * 0.5f), Vector3.back, 0.2f * T, h * 0.6f, c.Door);
            float top = h + tth;
            if (c.Age < AgeId.Ascension)
            {
                int stacks = c.Age >= AgeId.Information ? 1 : 1 + c.Bucket % 3;
                float sh = c.H * (c.Age >= AgeId.Modern ? 1.2f : 1.45f);
                Color32 stackCol = c.Age >= AgeId.Modern ? new Color32(170, 170, 165, 255) : c.Brick;
                for (int i = 0; i < stacks; i++)
                {
                    float x = stacks == 1 ? L * 0.3f : -L * 0.35f + L * 0.7f * i / (stacks - 1);
                    Vector3 p = V(x, 0f, S * 0.3f);
                    float r = 0.075f * T;
                    B.Frustum(p, r * 1.2f, r * 1.2f, r, r, sh, 8, stackCol, false);
                    B.Cylinder(p + V(0f, sh - 0.06f * T, 0f), r * 1.05f, 0.06f * T, 8, c.Soot);
                    if (c.Age == AgeId.Modern)
                    {
                        B.Cylinder(p + V(0f, sh * 0.7f, 0f), r * 1.06f, 0.05f * T, 8, new Color32(210, 50, 40, 255), false);
                        B.Cylinder(p + V(0f, sh * 0.82f, 0f), r * 1.04f, 0.05f * T, 8, new Color32(210, 50, 40, 255), false);
                    }
                    top = Mathf.Max(top, sh);
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                    B.Cylinder(V(-L * 0.3f + L * 0.3f * i, h + tth * 0.5f, S * 0.25f), 0.05f * T, 0.15f * T, 8, c.Glow);
            }
            if (c.Age >= AgeId.Information)
                B.FacadeRect(V(0f, h * 0.92f, -S * 0.5f), Vector3.back, L * 0.96f, 0.04f * T, c.Glow);
            B.ResetTransform();
        }

        private static void Skyscraper(ref Ctx c)
        {
            c.Segs = 0;
            int tiers = 3 + c.Bucket % 3;
            float total = c.H * 1.1f;
            B.BoxAt(V(0f, -0.3f * T, 0f), c.W + 0.05f * T, 0.34f * T, c.D + 0.05f * T, c.FoundSide, c.FoundTop);
            Color32 wall = c.Age >= AgeId.Information ? c.Wall : c.Age >= AgeId.Modern ? B.Mix(c.Wall, new Color32(160, 160, 158, 255), 0.4f) : c.Wall;
            Color32 band = c.Age >= AgeId.Modern ? c.Glass : c.Window;
            float y = 0f;
            float baseH = total * 0.4f;
            float restH = (total - baseH) / (tiers - 1);
            float f = 1f;
            float floorH = 0.16f * T;
            for (int k = 0; k < tiers; k++)
            {
                float th = k == 0 ? baseH : restH;
                float w = c.W * f, d = c.D * f;
                B.BoxAt(V(0f, y, 0f), w, th, d, wall, c.Trim);
                int floors = Mathf.Max(1, MathUtil.RoundToInt(th / floorH));
                float fh = th / floors;
                for (int j = 0; j < floors; j++)
                {
                    float by = y + fh * (j + 0.55f);
                    float bh = fh * (c.Age >= AgeId.Information ? 0.62f : 0.42f);
                    if (k == 0 && j == 0)
                    {
                        B.FacadeRect(V(0f, by, -d * 0.5f), Vector3.back, w * 0.4f, fh * 0.7f, c.Door);
                        continue;
                    }
                    B.FacadeRect(V(0f, by, -d * 0.5f), Vector3.back, w * 0.92f, bh, band);
                    B.FacadeRect(V(0f, by, d * 0.5f), Vector3.forward, w * 0.92f, bh, band);
                    B.FacadeRect(V(-w * 0.5f, by, 0f), Vector3.left, d * 0.92f, bh, band);
                    B.FacadeRect(V(w * 0.5f, by, 0f), Vector3.right, d * 0.92f, bh, band);
                }
                y += th;
                B.BoxAt(V(0f, y, 0f), w + 0.02f * T, 0.03f * T, d + 0.02f * T, c.Trim, B.Shade(c.Trim, 0.9f));
                if (c.Age == AgeId.Ascension)
                    B.FacadeRect(V(0f, y - 0.03f * T, -d * 0.5f - 0.01f * T), Vector3.back, w, 0.02f * T, c.Glow);
                y += 0.03f * T;
                f *= 0.8f - 0.03f * c.Bucket;
            }
            if (c.Age == AgeId.Ascension)
                B.Frustum(V(0f, y, 0f), c.W * f * 0.4f, c.D * f * 0.4f, 0f, 0f, 0.6f * T, 6, c.Glow, false);
            else
            {
                B.Cylinder(V(0f, y, 0f), 0.012f * T, 0.5f * T, 4, new Color32(180, 185, 190, 255), false);
                B.Blob(V(0f, y + 0.5f * T, 0f), 0.02f * T, 0.02f * T, 0.02f * T, 4, new Color32(230, 50, 40, 255));
            }
        }

        private static void Monument(ref Ctx c)
        {
            float S = Mathf.Min(c.FootW, c.FootD);
            B.BoxAt(V(0f, -0.2f * T, 0f), c.FootW * 0.9f, 0.28f * T, c.FootD * 0.9f, c.FoundSide, c.FoundTop);
            B.BoxAt(V(0f, 0.08f * T, 0f), c.FootW * 0.66f, 0.1f * T, c.FootD * 0.66f, c.Stone, c.Wall);
            float y = 0.18f * T;
            Color32 figure = c.Age == AgeId.Bronze || c.Age == AgeId.Iron ? B.Mix(c.Accent, new Color32(150, 110, 60, 255), 0.5f)
                : c.Age >= AgeId.Modern && c.Age < AgeId.Ascension ? new Color32(185, 190, 196, 255)
                : c.Age == AgeId.Ascension ? c.Accent : c.Wall;
            float top;
            if (c.Bucket < 2)
            {
                // obelisk with a gilded pyramidion
                float r0 = S * 0.11f * 1.414f, r1 = r0 * 0.62f;
                float h = c.H * 1.3f;
                B.Frustum(V(0f, y, 0f), r0, r0, r1, r1, h, 4, c.Age == AgeId.Classical ? c.Wall : B.Mix(c.Wall, c.Stone, 0.3f), false, false, 45f);
                B.Frustum(V(0f, y + h, 0f), r1, r1, 0f, 0f, r1 * 1.3f, 4, c.Age == AgeId.Dawn ? c.Stone : c.Accent, false, false, 45f);
                top = y + h + r1 * 1.3f;
            }
            else
            {
                // statue on a pedestal
                float ph = c.H * 0.3f;
                B.BoxAt(V(0f, y, 0f), S * 0.3f, ph, S * 0.3f, c.Stone, B.Shade(c.Stone, 1.08f));
                y += ph;
                float fh = c.H * 0.4f;
                B.Frustum(V(0f, y, 0f), S * 0.09f, S * 0.09f, S * 0.06f, S * 0.06f, fh * 0.55f, 7, figure, false);
                B.Frustum(V(0f, y + fh * 0.55f, 0f), S * 0.06f, S * 0.05f, S * 0.08f, S * 0.06f, fh * 0.3f, 7, figure, true);
                B.Blob(V(0f, y + fh * 0.85f + S * 0.04f, 0f), S * 0.045f, S * 0.05f, S * 0.045f, 6, figure);
                B.SetTransform(V(S * 0.07f, y + fh * 0.8f, 0f), 0f);
                B.BoxAt(Vector3.zero, S * 0.03f, fh * 0.45f, S * 0.03f, figure, figure);
                B.Cone(V(0f, fh * 0.45f, 0f), S * 0.03f, S * 0.08f, 5, c.Age >= AgeId.Industrial ? c.Glow : c.Accent);
                B.ResetTransform();
                top = y + fh * 0.8f + fh * 0.45f + S * 0.08f;
            }
            if (c.Age == AgeId.Ascension)
            {
                B.Frustum(V(0f, top * 0.6f, 0f), S * 0.3f, S * 0.3f, S * 0.3f, S * 0.3f, 0.02f * T, 12, c.Glow, true, true);
                AscensionCrown(ref c, V(0f, top, 0f));
            }
            c.LodTop = c.Wall;
        }

        private static void Cave(ref Ctx c)
        {
            float rx = c.FootW * 0.46f, rz = c.FootD * 0.46f;
            float h = c.H * 0.6f;
            Color32 earth = B.Mix(c.Stone, new Color32(108, 94, 74, 255), 0.45f);
            B.Dome(V(0f, -0.05f, 0f), rx, rz, h, 3, 9, earth, 7f + c.Bucket * 9f);
            B.Dome(V(rx * 0.15f, h * 0.65f, rz * 0.1f), rx * 0.45f, rz * 0.45f, h * 0.35f, 2, 7, B.Mix(earth, new Color32(90, 120, 70, 255), 0.35f), 20f);
            c.LodTop = earth;
            // arched entrance on the front (-Z)
            float aw = Mathf.Min(0.3f * T, rx * 0.7f), ah = Mathf.Min(0.24f * T, h * 0.55f);
            float zf = -rz * 0.86f;
            Color32 dark = new Color32(22, 19, 17, 255);
            B.BoxAt(V(0f, -0.05f, zf + 0.05f * T), aw + 0.12f * T, ah + aw * 0.5f + 0.06f, 0.14f * T, c.Stone, B.Shade(c.Stone, 1.05f));
            float zo = zf + 0.05f * T - 0.07f * T;
            B.FacadeRect(V(0f, ah * 0.5f, zo), Vector3.back, aw, ah, dark);
            // half-disc arch top
            int seg = 6;
            Vector3 ac = V(0f, ah, zo - 0.006f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = Mathf.PI * i / seg, a1 = Mathf.PI * (i + 1) / seg;
                B.TriFacing(ac, ac + V(Mathf.Cos(a0) * aw * 0.5f, Mathf.Sin(a0) * aw * 0.5f, 0f), ac + V(Mathf.Cos(a1) * aw * 0.5f, Mathf.Sin(a1) * aw * 0.5f, 0f), Vector3.back, dark);
            }
            // voussoir stones around the arch
            for (int i = 0; i <= 4; i++)
            {
                float a = Mathf.PI * i / 4f;
                Vector3 p = ac + V(Mathf.Cos(a) * (aw * 0.5f + 0.03f * T), Mathf.Sin(a) * (aw * 0.5f + 0.03f * T) - 0.025f * T, -0.01f);
                B.BoxAt(p, 0.05f * T, 0.05f * T, 0.03f * T, B.Shade(c.Stone, 1.12f), B.Shade(c.Stone, 1.12f));
            }
            // lanterns
            Color32 lamp = c.Age >= AgeId.Industrial ? c.Glow : new Color32(255, 190, 90, 255);
            B.BoxAt(V(-aw * 0.5f - 0.1f * T, ah * 0.6f, zo - 0.02f), 0.03f * T, 0.04f * T, 0.03f * T, lamp, lamp);
            B.BoxAt(V(aw * 0.5f + 0.1f * T, ah * 0.6f, zo - 0.02f), 0.03f * T, 0.04f * T, 0.03f * T, lamp, lamp);
            if (c.Age >= AgeId.Industrial)
            {
                Vector3 vp = V(-rx * 0.3f, h * 0.55f, rz * 0.2f);
                B.Cylinder(vp, 0.04f * T, h * 0.6f, 6, c.Age >= AgeId.Modern ? new Color32(160, 160, 160, 255) : c.Brick);
                B.Cylinder(vp + V(0f, h * 0.6f - 0.03f * T, 0f), 0.045f * T, 0.03f * T, 6, c.Soot);
            }
        }

        // =============================================================================== auxiliary meshes

        private static Mesh BuildScaffold(int width, int height, float topHeight)
        {
            B.Begin();
            float fw = width * T * 0.94f, fd = height * T * 0.94f;
            float hx = fw * 0.5f, hz = fd * 0.5f;
            float h = Mathf.Max(0.2f * T, topHeight + 0.08f * T);
            float t = 0.022f * T;
            var pole = new Color32(160, 122, 74, 255);
            var rail = new Color32(135, 100, 60, 255);
            int nx = Mathf.Max(2, MathUtil.RoundToInt(fw / (0.45f * T)) + 1);
            int nz = Mathf.Max(2, MathUtil.RoundToInt(fd / (0.45f * T)) + 1);
            for (int ix = 0; ix < nx; ix++)
            {
                float x = -hx + fw * ix / (nx - 1);
                B.BoxAt(V(x, -0.05f, -hz), t, h, t, pole, pole);
                B.BoxAt(V(x, -0.05f, hz), t, h, t, pole, pole);
            }
            for (int iz = 1; iz < nz - 1; iz++)
            {
                float z = -hz + fd * iz / (nz - 1);
                B.BoxAt(V(-hx, -0.05f, z), t, h, t, pole, pole);
                B.BoxAt(V(hx, -0.05f, z), t, h, t, pole, pole);
            }
            int levels = Mathf.Max(1, (int)(h / (0.33f * T)));
            float rt = t * 0.8f;
            for (int l = 1; l <= levels; l++)
            {
                float y = h * l / (levels + 0.3f);
                B.Box(V(-hx, y, -hz - rt), V(hx, y + rt, -hz + rt), rail, rail);
                B.Box(V(-hx, y, hz - rt), V(hx, y + rt, hz + rt), rail, rail);
                B.Box(V(-hx - rt, y, -hz), V(-hx + rt, y + rt, hz), rail, rail);
                B.Box(V(hx - rt, y, -hz), V(hx + rt, y + rt, hz), rail, rail);
            }
            // diagonal braces on the front and back faces (thin strips)
            float y0 = 0f, y1 = h * 1f / (levels + 0.3f);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float z = sgn * (hz + rt * 1.5f);
                Vector3 o = sgn < 0 ? Vector3.back : Vector3.forward;
                B.QuadFacing(V(-hx, y0, z), V(-hx, y0 + rt * 2f, z), V(hx, y1 + rt * 2f, z), V(hx, y1, z), o, rail);
            }
            return B.ToMesh("Scaffold_" + width + "x" + height);
        }

        private static Mesh BuildRuin(int width, int height, AgeId age, int bucket)
        {
            B.Begin();
            var c = new Ctx { Age = age, Bucket = bucket, Style = s_defaultStyle, Seed = (uint)(width * 7349 + height * 3571 + (int)age * 911 + bucket * 131 + 17) };
            BuildPalette(ref c);
            float fw = width * T * 0.9f, fd = height * T * 0.9f;
            Color32 wall = B.Shade(c.Wall, 0.7f), roof = B.Shade(c.RoofC, 0.6f), char_ = new Color32(40, 36, 33, 255);
            B.FlatRect(V(0f, 0.012f, 0f), fw, fd, true, B.Mix(new Color32(60, 52, 45, 255), wall, 0.2f));
            int n = MathUtil.Clamp(6 + width * height * 3, 6, 48);
            for (int i = 0; i < n; i++)
            {
                float x = (Rand(ref c) - 0.5f) * fw * 0.9f, z = (Rand(ref c) - 0.5f) * fd * 0.9f;
                B.SetTransform(V(x, -0.02f, z), Rand(ref c) * 180f);
                float sx = (0.06f + Rand(ref c) * 0.14f) * T, sz = (0.05f + Rand(ref c) * 0.12f) * T, sy = (0.03f + Rand(ref c) * 0.1f) * T;
                float pick = Rand(ref c);
                Color32 col = pick < 0.55f ? wall : pick < 0.8f ? roof : char_;
                B.BoxAt(Vector3.zero, sx, sy, sz, B.Shade(col, 0.85f), col);
            }
            // broken wall stubs
            for (int i = 0; i < 2; i++)
            {
                float sgn = i == 0 ? -1f : 1f;
                float sh = (0.12f + Rand(ref c) * 0.18f) * T;
                B.SetTransform(V(sgn * fw * 0.38f, -0.02f, (Rand(ref c) - 0.5f) * fd * 0.5f), 0f);
                B.BoxAt(Vector3.zero, 0.06f * T, sh, fd * (0.3f + Rand(ref c) * 0.3f), wall, B.Shade(wall, 0.9f));
            }
            // charred beams
            for (int i = 0; i < 3; i++)
            {
                B.SetTransform(V((Rand(ref c) - 0.5f) * fw * 0.6f, 0.02f, (Rand(ref c) - 0.5f) * fd * 0.6f), Rand(ref c) * 180f);
                B.BoxAt(Vector3.zero, fw * 0.45f, 0.03f * T, 0.035f * T, char_, char_);
            }
            B.ResetTransform();
            return B.ToMesh("Ruin_" + width + "x" + height + "_" + age);
        }
    }
}
