using System;
using System.Collections.Generic;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Procedural low-poly tree meshes (flat-shaded, vertex-colored) for GPU instancing. Pivot at the trunk base,
    /// +Y up, roughly 1 world unit tall at scale 1 and ~0.7 wide so a tree fits one tile.
    /// Meshes are created once per renderer (call <see cref="Create"/>); the caller owns and destroys them.
    /// </summary>
    public static class TreeMeshFactory
    {
        public const int KindCount = 3;

        private static readonly Color Bark = new Color(0.40f, 0.27f, 0.17f, 1f);
        private static readonly Color PalmBark = new Color(0.56f, 0.43f, 0.28f, 1f);
        private static readonly Color ConiferDark = new Color(0.10f, 0.30f, 0.17f, 1f);
        private static readonly Color ConiferLight = new Color(0.20f, 0.46f, 0.26f, 1f);
        private static readonly Color LeafDark = new Color(0.22f, 0.44f, 0.15f, 1f);
        private static readonly Color LeafLight = new Color(0.42f, 0.66f, 0.25f, 1f);
        private static readonly Color FrondDark = new Color(0.20f, 0.48f, 0.16f, 1f);
        private static readonly Color FrondLight = new Color(0.40f, 0.70f, 0.24f, 1f);

        private const float Phi = 1.6180340f;

        private static readonly float[] IcoVerts =
        {
            -1f, Phi, 0f,   1f, Phi, 0f,   -1f, -Phi, 0f,   1f, -Phi, 0f,
            0f, -1f, Phi,   0f, 1f, Phi,   0f, -1f, -Phi,   0f, 1f, -Phi,
            Phi, 0f, -1f,   Phi, 0f, 1f,   -Phi, 0f, -1f,   -Phi, 0f, 1f
        };

        private static readonly int[] IcoFaces =
        {
            0, 11, 5,  0, 5, 1,  0, 1, 7,  0, 7, 10,  0, 10, 11,
            1, 5, 9,  5, 11, 4,  11, 10, 2,  10, 7, 6,  7, 1, 8,
            3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
            4, 9, 5,  2, 4, 11,  6, 2, 10,  8, 6, 7,  9, 8, 1
        };

        /// <summary>Creates a new mesh for the given tree kind. Not cached: the caller destroys it.</summary>
        public static Mesh Create(TreeKind kind)
        {
            var v = new List<Vector3>(512);
            var n = new List<Vector3>(512);
            var c = new List<Color32>(512);
            var t = new List<int>(768);
            Build(kind, v, n, c, t);
            var mesh = new Mesh();
            mesh.name = "GodSim Tree " + kind;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0, true);
            return mesh;
        }

        /// <summary>Appends the flat-shaded geometry of a tree kind to the given lists.</summary>
        public static void Build(TreeKind kind, List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t)
        {
            switch (kind)
            {
                case TreeKind.Conifer: BuildConifer(v, n, c, t); break;
                case TreeKind.Palm: BuildPalm(v, n, c, t); break;
                default: BuildDeciduous(v, n, c, t); break;
            }
        }

        private static void BuildConifer(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t)
        {
            Prism(v, n, c, t, 6, 0.065f, 0.05f, 0f, 0.26f, 0f, 0f, Bark);
            // three stacked, slightly rotated cones
            for (int tier = 0; tier < 3; tier++)
            {
                float baseY = 0.18f + tier * 0.23f;
                float radius = 0.36f - tier * 0.09f;
                float apex = baseY + 0.44f - tier * 0.05f;
                Cone(v, n, c, t, 7, radius, baseY, apex, tier * 0.45f, ConiferDark, ConiferLight);
            }
        }

        private static void BuildDeciduous(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t)
        {
            Prism(v, n, c, t, 6, 0.075f, 0.055f, 0f, 0.42f, 0f, 0f, Bark);
            Blob(v, n, c, t, new Vector3(0f, 0.66f, 0f), new Vector3(0.33f, 0.29f, 0.33f), 1u);
            Blob(v, n, c, t, new Vector3(0.12f, 0.82f, -0.05f), new Vector3(0.20f, 0.18f, 0.20f), 7u);
        }

        private static void BuildPalm(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t)
        {
            // curved trunk: segments leaning toward +X
            const int segments = 5;
            const float height = 0.95f, lean = 0.14f;
            for (int s = 0; s < segments; s++)
            {
                float t0 = s / (float)segments, t1 = (s + 1) / (float)segments;
                float r0 = 0.058f - 0.018f * t0, r1 = 0.058f - 0.018f * t1;
                Color bark = PalmBark * (s % 2 == 0 ? 1f : 0.88f);
                Prism(v, n, c, t, 5, r0, r1, t0 * height, t1 * height, lean * t0 * t0, lean * t1 * t1, bark);
            }
            Vector3 top = new Vector3(lean, height, 0f);
            const int fronds = 7;
            for (int f = 0; f < fronds; f++)
            {
                float a = f * (6.2831853f / fronds) + 0.3f;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                Vector3 dir = new Vector3(ca, 0f, sa);
                Vector3 side = new Vector3(-sa, 0f, ca);
                Vector3 mid = top + dir * 0.24f + new Vector3(0f, 0.06f, 0f);
                Vector3 tip = top + dir * 0.46f + new Vector3(0f, -0.16f, 0f);
                Color col = Color.Lerp(FrondDark, FrondLight, (f % 3) * 0.5f);
                DoubleTri(v, n, c, t, top, mid + side * 0.09f, tip, col);
                DoubleTri(v, n, c, t, top, tip, mid - side * 0.09f, col);
            }
        }

        // ------------------------------------------------------------------------------------------------

        /// <summary>Open n-sided frustum along +Y (trunk segment), bottom ring centered at (ox0, y0), top at (ox1, y1).</summary>
        private static void Prism(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t, int sides,
                                  float r0, float r1, float y0, float y1, float ox0, float ox1, Color col)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * (6.2831853f / sides), a1 = (i + 1) * (6.2831853f / sides);
                Vector3 b0 = new Vector3(ox0 + (float)Math.Cos(a0) * r0, y0, (float)Math.Sin(a0) * r0);
                Vector3 b1 = new Vector3(ox0 + (float)Math.Cos(a1) * r0, y0, (float)Math.Sin(a1) * r0);
                Vector3 t0 = new Vector3(ox1 + (float)Math.Cos(a0) * r1, y1, (float)Math.Sin(a0) * r1);
                Vector3 t1 = new Vector3(ox1 + (float)Math.Cos(a1) * r1, y1, (float)Math.Sin(a1) * r1);
                Vector3 axis = new Vector3((ox0 + ox1) * 0.5f, (y0 + y1) * 0.5f, 0f);
                Tri(v, n, c, t, b0, b1, t1, axis, col, col);
                Tri(v, n, c, t, b0, t1, t0, axis, col, col);
            }
        }

        /// <summary>n-sided cone with a closed bottom disk; faces shaded dark->light by how much they face up.</summary>
        private static void Cone(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t, int sides,
                                 float radius, float baseY, float apexY, float rot, Color dark, Color light)
        {
            Vector3 apex = new Vector3(0f, apexY, 0f);
            Vector3 center = new Vector3(0f, baseY, 0f);
            Vector3 axisRef = new Vector3(0f, baseY + (apexY - baseY) * 0.3f, 0f);
            Vector3 above = new Vector3(0f, baseY + 1f, 0f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = rot + i * (6.2831853f / sides), a1 = rot + (i + 1) * (6.2831853f / sides);
                Vector3 p0 = new Vector3((float)Math.Cos(a0) * radius, baseY, (float)Math.Sin(a0) * radius);
                Vector3 p1 = new Vector3((float)Math.Cos(a1) * radius, baseY, (float)Math.Sin(a1) * radius);
                Tri(v, n, c, t, p0, p1, apex, axisRef, dark, light);
                Tri(v, n, c, t, center, p1, p0, above, dark * 0.8f, dark * 0.8f);
            }
        }

        /// <summary>Squashed, deterministically jittered icosahedron (foliage clump).</summary>
        private static void Blob(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t, Vector3 center, Vector3 radius, uint salt)
        {
            float inv = 1f / (float)Math.Sqrt(1f + Phi * Phi);
            for (int f = 0; f < IcoFaces.Length; f += 3)
            {
                Vector3 p0 = IcoPoint(IcoFaces[f], center, radius, inv, salt);
                Vector3 p1 = IcoPoint(IcoFaces[f + 1], center, radius, inv, salt);
                Vector3 p2 = IcoPoint(IcoFaces[f + 2], center, radius, inv, salt);
                Tri(v, n, c, t, p0, p1, p2, center, LeafDark, LeafLight);
            }
        }

        private static Vector3 IcoPoint(int idx, Vector3 center, Vector3 radius, float inv, uint salt)
        {
            uint h = unchecked((uint)(idx + 1) * 2654435761u ^ salt * 0x85EBCA6Bu);
            float jitter = 0.88f + 0.24f * ((h >> 8) & 0xFFFF) / 65535f;
            float x = IcoVerts[idx * 3] * inv * jitter, y = IcoVerts[idx * 3 + 1] * inv * jitter, z = IcoVerts[idx * 3 + 2] * inv * jitter;
            return new Vector3(center.x + x * radius.x, center.y + y * radius.y, center.z + z * radius.z);
        }

        /// <summary>
        /// Flat-shaded triangle whose front face points away from <paramref name="inside"/> (winding fixed automatically;
        /// Unity front faces are clockwise, i.e. Cross(p1-p0, p2-p0) points toward the viewer).
        /// Color interpolates dark->light by the face's upward-facing amount (baked sky light).
        /// </summary>
        private static void Tri(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
                                Vector3 p0, Vector3 p1, Vector3 p2, Vector3 inside, Color dark, Color light)
        {
            Vector3 nr = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 centroid = (p0 + p1 + p2) / 3f;
            if (Vector3.Dot(nr, centroid - inside) < 0f)
            {
                Vector3 tmp = p1; p1 = p2; p2 = tmp;
                nr = -nr;
            }
            nr = nr.normalized;
            Color32 col = Color.Lerp(dark, light, 0.5f + 0.5f * nr.y);
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2);
            n.Add(nr); n.Add(nr); n.Add(nr);
            c.Add(col); c.Add(col); c.Add(col);
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
        }

        /// <summary>Two-sided thin triangle (palm fronds): front and back copies with opposite normals.</summary>
        private static void DoubleTri(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
                                      Vector3 p0, Vector3 p1, Vector3 p2, Color col)
        {
            Vector3 nr = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            Color32 front = col;
            Color32 back = col * 0.8f;
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2);
            n.Add(nr); n.Add(nr); n.Add(nr);
            c.Add(front); c.Add(front); c.Add(front);
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            b = v.Count;
            Vector3 nb = -nr;
            v.Add(p0); v.Add(p2); v.Add(p1);
            n.Add(nb); n.Add(nb); n.Add(nb);
            c.Add(back); c.Add(back); c.Add(back);
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
        }
    }
}
