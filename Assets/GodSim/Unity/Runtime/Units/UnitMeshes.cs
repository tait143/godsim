using System.Collections.Generic;
using UnityEngine;

namespace GodSim.Unity.Units
{
    /// <summary>
    /// Procedural low-poly unit meshes (built once, flat shaded, vertex colored). Vertex colors are grey
    /// levels: the instanced material multiplies them by the per-instance "_Color", so white parts take the
    /// full instance tint (skin / civ color) and darker parts read as clothing, wood or metal.
    /// Pivot is at the feet; 1 mesh unit ~ the height of an adult.
    /// </summary>
    public static class UnitMeshes
    {
        /// <summary>Meeple: legs + tunic + round head (head pure white = skin tint).</summary>
        public static Mesh CreateMeeple()
        {
            var b = new MeshBuilder();
            b.AddBox(new Vector3(-0.075f, 0.2f, 0f), new Vector3(0.12f, 0.4f, 0.16f), Grey(0.42f));
            b.AddBox(new Vector3(0.075f, 0.2f, 0f), new Vector3(0.12f, 0.4f, 0.16f), Grey(0.42f));
            b.AddPrism(new Vector3(0f, 0.36f, 0f), 0.2f, 0.42f, 8, 0.72f, Grey(0.62f));
            b.AddBox(new Vector3(-0.2f, 0.58f, 0f), new Vector3(0.07f, 0.26f, 0.09f), Grey(0.58f));
            b.AddBox(new Vector3(0.2f, 0.58f, 0f), new Vector3(0.07f, 0.26f, 0.09f), Grey(0.58f));
            b.AddBlob(new Vector3(0f, 0.92f, 0f), 0.15f, Grey(1f));
            return b.ToMesh("GodSim Meeple");
        }

        /// <summary>Soldier: meeple with helmet, shield and spear (white tunic/shield = civ color).</summary>
        public static Mesh CreateSoldier()
        {
            var b = new MeshBuilder();
            b.AddBox(new Vector3(-0.075f, 0.2f, 0f), new Vector3(0.12f, 0.4f, 0.16f), Grey(0.3f));
            b.AddBox(new Vector3(0.075f, 0.2f, 0f), new Vector3(0.12f, 0.4f, 0.16f), Grey(0.3f));
            b.AddPrism(new Vector3(0f, 0.36f, 0f), 0.21f, 0.42f, 8, 0.78f, Grey(1f));
            b.AddBlob(new Vector3(0f, 0.92f, 0f), 0.14f, Grey(0.85f));
            b.AddPrism(new Vector3(0f, 0.95f, 0f), 0.16f, 0.12f, 8, 0.55f, Grey(0.5f));
            b.AddBox(new Vector3(-0.24f, 0.55f, 0.06f), new Vector3(0.05f, 0.34f, 0.26f), Grey(0.9f));
            b.AddBox(new Vector3(0.24f, 0.72f, 0.04f), new Vector3(0.04f, 1.25f, 0.04f), Grey(0.35f));
            b.AddPrism(new Vector3(0.24f, 1.34f, 0.04f), 0.045f, 0.14f, 4, 0f, Grey(0.75f));
            return b.ToMesh("GodSim Soldier");
        }

        /// <summary>Banner: dark pole, white flag (civ tint) and a light finial. Height 1 unit.</summary>
        public static Mesh CreateBanner()
        {
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0.5f, 0f), new Vector3(0.03f, 1f, 0.03f), Grey(0.28f));
            b.AddBox(new Vector3(0.17f, 0.83f, 0f), new Vector3(0.32f, 0.2f, 0.015f), Grey(1f));
            b.AddBox(new Vector3(0.17f, 0.71f, 0f), new Vector3(0.32f, 0.04f, 0.017f), Grey(0.7f));
            b.AddBlob(new Vector3(0f, 1.02f, 0f), 0.035f, Grey(0.95f));
            return b.ToMesh("GodSim Banner");
        }

        private static Color Grey(float v) => new Color(v, v, v, 1f);

        /// <summary>Tiny flat-shaded mesh builder (every triangle gets its own vertices).</summary>
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>(512);
            private readonly List<Color> _c = new List<Color>(512);
            private readonly List<int> _t = new List<int>(768);

            /// <summary>Adds a triangle facing away from <paramref name="inside"/> (winding fixed automatically).</summary>
            private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color col)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(n, centroid - inside) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _c.Add(col); _c.Add(col); _c.Add(col);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color col)
            {
                Tri(a, b, c, inside, col);
                Tri(a, c, d, inside, col);
            }

            public void AddBox(Vector3 center, Vector3 size, Color col)
            {
                Vector3 h = size * 0.5f;
                Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
                Vector3 p100 = center + new Vector3(h.x, -h.y, -h.z);
                Vector3 p010 = center + new Vector3(-h.x, h.y, -h.z);
                Vector3 p110 = center + new Vector3(h.x, h.y, -h.z);
                Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z);
                Vector3 p101 = center + new Vector3(h.x, -h.y, h.z);
                Vector3 p011 = center + new Vector3(-h.x, h.y, h.z);
                Vector3 p111 = center + new Vector3(h.x, h.y, h.z);
                Quad(p000, p100, p110, p010, center, col); // -Z
                Quad(p001, p101, p111, p011, center, col); // +Z
                Quad(p000, p001, p011, p010, center, col); // -X
                Quad(p100, p101, p111, p110, center, col); // +X
                Quad(p010, p110, p111, p011, center, col); // +Y
                Quad(p000, p100, p101, p001, center, col); // -Y
            }

            /// <summary>Upright n-gon prism (frustum when topScale != 1; cone when 0) standing on baseCenter.</summary>
            public void AddPrism(Vector3 baseCenter, float radius, float height, int sides, float topScale, Color col)
            {
                if (sides < 3) sides = 3;
                Vector3 inside = baseCenter + new Vector3(0f, height * 0.5f, 0f);
                Vector3 top = baseCenter + new Vector3(0f, height, 0f);
                float rt = radius * topScale;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = UnitMath.TwoPi * i / sides, a1 = UnitMath.TwoPi * (i + 1) / sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 b0 = baseCenter + d0 * radius, b1 = baseCenter + d1 * radius;
                    Vector3 t0 = top + d0 * rt, t1 = top + d1 * rt;
                    if (rt > 1e-4f)
                    {
                        Quad(b0, b1, t1, t0, inside, col);
                        Tri(top, t0, t1, inside, col);
                    }
                    else
                    {
                        Tri(b0, b1, top, inside, col);
                    }
                    Tri(baseCenter, b1, b0, inside, col);
                }
            }

            /// <summary>Low-poly sphere (6 segments x 4 rings).</summary>
            public void AddBlob(Vector3 center, float radius, Color col)
            {
                const int seg = 6, rings = 4;
                for (int r = 0; r < rings; r++)
                {
                    float v0 = Mathf.PI * r / rings - Mathf.PI * 0.5f;
                    float v1 = Mathf.PI * (r + 1) / rings - Mathf.PI * 0.5f;
                    for (int s = 0; s < seg; s++)
                    {
                        float u0 = UnitMath.TwoPi * s / seg, u1 = UnitMath.TwoPi * (s + 1) / seg;
                        Vector3 a = center + Sph(u0, v0) * radius;
                        Vector3 b = center + Sph(u1, v0) * radius;
                        Vector3 c = center + Sph(u1, v1) * radius;
                        Vector3 d = center + Sph(u0, v1) * radius;
                        if (r == 0) Tri(a, c, d, center, col);
                        else if (r == rings - 1) Tri(a, b, c, center, col);
                        else Quad(a, b, c, d, center, col);
                    }
                }
            }

            private static Vector3 Sph(float u, float v) =>
                new Vector3(Mathf.Cos(v) * Mathf.Cos(u), Mathf.Sin(v), Mathf.Cos(v) * Mathf.Sin(u));

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.vertices = _v.ToArray();
                m.colors = _c.ToArray();
                m.triangles = _t.ToArray();
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
