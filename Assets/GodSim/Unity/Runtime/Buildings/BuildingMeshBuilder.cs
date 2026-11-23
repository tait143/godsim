using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Low-level, allocation-light geometry helpers used by <see cref="ProceduralBuildingMeshes"/>.
    /// Emits flat-shaded, vertex-colored primitives (boxes, prisms, frustums/cones/cylinders, domes, roofs)
    /// into shared static buffers that are reused for every mesh generated. Main thread only.
    /// Face winding is fixed up automatically from an "outward" hint, so callers never have to
    /// care about Unity's clockwise front-face convention.
    /// </summary>
    public static class BuildingMeshBuilder
    {
        private static readonly List<Vector3> s_verts = new List<Vector3>(4096);
        private static readonly List<Vector3> s_normals = new List<Vector3>(4096);
        private static readonly List<Color32> s_colors = new List<Color32>(4096);
        private static readonly List<int> s_tris = new List<int>(8192);

        private static bool s_hasXf;
        private static Vector3 s_offset;
        private static float s_cos = 1f, s_sin;

        private const float TwoPi = Mathf.PI * 2f;

        /// <summary>Number of vertices emitted since <see cref="Begin"/>.</summary>
        public static int VertexCount => s_verts.Count;

        /// <summary>Clears the buffers and resets the local transform.</summary>
        public static void Begin()
        {
            s_verts.Clear();
            s_normals.Clear();
            s_colors.Clear();
            s_tris.Clear();
            ResetTransform();
        }

        /// <summary>Applies a translation + yaw (degrees, around +Y) to every subsequently emitted vertex.</summary>
        public static void SetTransform(Vector3 offset, float yawDegrees)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            s_cos = Mathf.Cos(rad);
            s_sin = Mathf.Sin(rad);
            s_offset = offset;
            s_hasXf = true;
        }

        public static void ResetTransform()
        {
            s_hasXf = false;
            s_offset = Vector3.zero;
            s_cos = 1f;
            s_sin = 0f;
        }

        private static Vector3 Xp(Vector3 p)
        {
            if (!s_hasXf) return p;
            return new Vector3(p.x * s_cos + p.z * s_sin + s_offset.x, p.y + s_offset.y, -p.x * s_sin + p.z * s_cos + s_offset.z);
        }

        private static Vector3 Xn(Vector3 n)
        {
            if (!s_hasXf) return n;
            return new Vector3(n.x * s_cos + n.z * s_sin, n.y, -n.x * s_sin + n.z * s_cos);
        }

        /// <summary>Moves already-emitted vertices [start, VertexCount) by delta (used for sinking / lifting).</summary>
        public static void OffsetVertices(int start, Vector3 delta)
        {
            for (int i = start; i < s_verts.Count; i++) s_verts[i] = s_verts[i] + delta;
        }

        /// <summary>Axis-aligned bounds of everything emitted so far.</summary>
        public static void ComputeBounds(out Vector3 min, out Vector3 max)
        {
            if (s_verts.Count == 0) { min = Vector3.zero; max = Vector3.zero; return; }
            min = s_verts[0];
            max = s_verts[0];
            for (int i = 1; i < s_verts.Count; i++)
            {
                Vector3 v = s_verts[i];
                if (v.x < min.x) min.x = v.x; if (v.y < min.y) min.y = v.y; if (v.z < min.z) min.z = v.z;
                if (v.x > max.x) max.x = v.x; if (v.y > max.y) max.y = v.y; if (v.z > max.z) max.z = v.z;
            }
        }

        // ------------------------------------------------------------------ faces

        /// <summary>Triangle; winding is chosen so the face normal points along <paramref name="outward"/>.</summary>
        public static void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color32 col)
        {
            a = Xp(a); b = Xp(b); c = Xp(c); outward = Xn(outward);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; n = -n; }
            n = n.normalized;
            int i0 = s_verts.Count;
            s_verts.Add(a); s_verts.Add(b); s_verts.Add(c);
            s_normals.Add(n); s_normals.Add(n); s_normals.Add(n);
            s_colors.Add(col); s_colors.Add(col); s_colors.Add(col);
            s_tris.Add(i0); s_tris.Add(i0 + 1); s_tris.Add(i0 + 2);
        }

        /// <summary>Planar quad a-b-c-d (in perimeter order); winding fixed from <paramref name="outward"/>.</summary>
        public static void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color32 col)
        {
            a = Xp(a); b = Xp(b); c = Xp(c); d = Xp(d); outward = Xn(outward);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = d; d = t; n = -n; }
            n = n.normalized;
            int i0 = s_verts.Count;
            s_verts.Add(a); s_verts.Add(b); s_verts.Add(c); s_verts.Add(d);
            s_normals.Add(n); s_normals.Add(n); s_normals.Add(n); s_normals.Add(n);
            s_colors.Add(col); s_colors.Add(col); s_colors.Add(col); s_colors.Add(col);
            s_tris.Add(i0); s_tris.Add(i0 + 1); s_tris.Add(i0 + 2);
            s_tris.Add(i0); s_tris.Add(i0 + 2); s_tris.Add(i0 + 3);
        }

        /// <summary>Vertical rectangle on a wall (door, window, glass band), offset slightly off the wall.</summary>
        /// <param name="center">Center of the rectangle on the wall plane.</param>
        /// <param name="outward">Horizontal unit wall normal.</param>
        public static void FacadeRect(Vector3 center, Vector3 outward, float width, float height, Color32 col)
        {
            Vector3 right = new Vector3(outward.z, 0f, -outward.x) * (width * 0.5f);
            Vector3 up = new Vector3(0f, height * 0.5f, 0f);
            Vector3 c = center + outward * 0.006f;
            QuadFacing(c - right - up, c - right + up, c + right + up, c + right - up, outward, col);
        }

        /// <summary>Horizontal rectangle (e.g. a slab top, a flat field, a dark stairwell).</summary>
        public static void FlatRect(Vector3 center, float sx, float sz, bool facingUp, Color32 col)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            QuadFacing(center + new Vector3(-hx, 0f, -hz), center + new Vector3(-hx, 0f, hz),
                center + new Vector3(hx, 0f, hz), center + new Vector3(hx, 0f, -hz),
                facingUp ? Vector3.up : Vector3.down, col);
        }

        // ------------------------------------------------------------------ solids

        /// <summary>Axis-aligned box from min to max (bottom face omitted unless requested).</summary>
        public static void Box(Vector3 min, Vector3 max, Color32 side, Color32 top, bool bottom = false)
        {
            Vector3 p000 = new Vector3(min.x, min.y, min.z), p100 = new Vector3(max.x, min.y, min.z);
            Vector3 p010 = new Vector3(min.x, max.y, min.z), p110 = new Vector3(max.x, max.y, min.z);
            Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z);
            Vector3 p011 = new Vector3(min.x, max.y, max.z), p111 = new Vector3(max.x, max.y, max.z);
            QuadFacing(p000, p010, p110, p100, Vector3.back, side);
            QuadFacing(p101, p111, p011, p001, Vector3.forward, side);
            QuadFacing(p001, p011, p010, p000, Vector3.left, side);
            QuadFacing(p100, p110, p111, p101, Vector3.right, side);
            QuadFacing(p010, p011, p111, p110, Vector3.up, top);
            if (bottom) QuadFacing(p000, p100, p101, p001, Vector3.down, side);
        }

        /// <summary>Box whose bottom-center sits at <paramref name="baseCenter"/>.</summary>
        public static void BoxAt(Vector3 baseCenter, float sx, float sy, float sz, Color32 side, Color32 top)
        {
            Box(new Vector3(baseCenter.x - sx * 0.5f, baseCenter.y, baseCenter.z - sz * 0.5f),
                new Vector3(baseCenter.x + sx * 0.5f, baseCenter.y + sy, baseCenter.z + sz * 0.5f), side, top);
        }

        /// <summary>
        /// Elliptical frustum (rx0/rz0 at the bottom, rx1/rz1 at the top). A zero top radius makes a cone.
        /// Negative height builds downward. Flat shaded, one quad per segment.
        /// </summary>
        public static void Frustum(Vector3 baseCenter, float rx0, float rz0, float rx1, float rz1, float height, int segments,
            Color32 col, bool capTop, bool capBottom = false, float angleOffsetDeg = 0f)
        {
            if (segments < 3) segments = 3;
            float off = angleOffsetDeg * Mathf.Deg2Rad;
            bool apex = rx1 <= 1e-4f && rz1 <= 1e-4f;
            Vector3 topCenter = baseCenter + new Vector3(0f, height, 0f);
            float ca = Mathf.Cos(off), sa = Mathf.Sin(off);
            for (int i = 0; i < segments; i++)
            {
                float a1 = off + TwoPi * (i + 1) / segments;
                float cb = Mathf.Cos(a1), sb = Mathf.Sin(a1);
                float am = off + TwoPi * (i + 0.5f) / segments;
                Vector3 outward = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                Vector3 b0 = baseCenter + new Vector3(ca * rx0, 0f, sa * rz0);
                Vector3 b1 = baseCenter + new Vector3(cb * rx0, 0f, sb * rz0);
                if (apex) TriFacing(b0, topCenter, b1, outward, col);
                else
                {
                    Vector3 t0 = topCenter + new Vector3(ca * rx1, 0f, sa * rz1);
                    Vector3 t1 = topCenter + new Vector3(cb * rx1, 0f, sb * rz1);
                    QuadFacing(b0, t0, t1, b1, outward, col);
                    if (capTop) TriFacing(topCenter, t0, t1, height >= 0f ? Vector3.up : Vector3.down, col);
                }
                if (capBottom) TriFacing(baseCenter, b0, b1, height >= 0f ? Vector3.down : Vector3.up, col);
                ca = cb; sa = sb;
            }
        }

        public static void Cylinder(Vector3 baseCenter, float radius, float height, int segments, Color32 col, bool capTop = true)
        {
            Frustum(baseCenter, radius, radius, radius, radius, height, segments, col, capTop);
        }

        public static void Cone(Vector3 baseCenter, float radius, float height, int segments, Color32 col, float angleOffsetDeg = 0f)
        {
            Frustum(baseCenter, radius, radius, 0f, 0f, height, segments, col, false, false, angleOffsetDeg);
        }

        /// <summary>Elliptical half-dome (negative height = bowl hanging downward).</summary>
        public static void Dome(Vector3 baseCenter, float rx, float rz, float height, int rings, int segments, Color32 col, float angleOffsetDeg = 0f)
        {
            if (rings < 1) rings = 1;
            for (int k = 0; k < rings; k++)
            {
                float p0 = (Mathf.PI * 0.5f) * k / rings;
                float p1 = (Mathf.PI * 0.5f) * (k + 1) / rings;
                float c0 = Mathf.Cos(p0), c1 = k + 1 == rings ? 0f : Mathf.Cos(p1);
                float y0 = Mathf.Sin(p0) * height, y1 = Mathf.Sin(p1) * height;
                Frustum(baseCenter + new Vector3(0f, y0, 0f), rx * c0, rz * c0, rx * c1, rz * c1, y1 - y0, segments, col, false, false, angleOffsetDeg);
            }
        }

        /// <summary>Closed ellipsoid blob (two domes); good for foliage and orbs.</summary>
        public static void Blob(Vector3 center, float rx, float ry, float rz, int segments, Color32 col, float angleOffsetDeg = 0f)
        {
            Dome(center, rx, rz, ry, 2, segments, col, angleOffsetDeg);
            Dome(center, rx, rz, -ry * 0.75f, 2, segments, col, angleOffsetDeg);
        }

        // ------------------------------------------------------------------ roofs

        private static Vector3 RoofP(Vector3 c, bool alongX, float u, float y, float v)
        {
            return alongX ? new Vector3(c.x + u, c.y + y, c.z + v) : new Vector3(c.x + v, c.y + y, c.z + u);
        }

        /// <summary>Gable roof over an sx*sz rectangle whose eave line is at baseCenter.y; ridge along the longer axis.</summary>
        public static void GableRoof(Vector3 baseCenter, float sx, float sz, float rise, float overhang, Color32 roof, Color32 gable)
        {
            bool alongX = sx >= sz;
            float hl = (alongX ? sx : sz) * 0.5f + overhang;
            float hs = (alongX ? sz : sx) * 0.5f + overhang;
            float hw = (alongX ? sz : sx) * 0.5f;
            Vector3 c = baseCenter;
            Vector3 r0 = RoofP(c, alongX, -hl, rise, 0f), r1 = RoofP(c, alongX, hl, rise, 0f);
            Vector3 fOut = RoofP(Vector3.zero, alongX, 0f, 1f, -1f), bOut = RoofP(Vector3.zero, alongX, 0f, 1f, 1f);
            // eaves drop slightly with the overhang
            float drop = rise * (overhang / Mathf.Max(0.001f, hw));
            QuadFacing(RoofP(c, alongX, -hl, -drop, -hs), RoofP(c, alongX, hl, -drop, -hs), r1, r0, fOut, roof);
            QuadFacing(RoofP(c, alongX, -hl, -drop, hs), RoofP(c, alongX, hl, -drop, hs), r1, r0, bOut, roof);
            float gu = hl - overhang;
            TriFacing(RoofP(c, alongX, gu, 0f, -hw), RoofP(c, alongX, gu, 0f, hw), RoofP(c, alongX, gu, rise, 0f), RoofP(Vector3.zero, alongX, 1f, 0f, 0f), gable);
            TriFacing(RoofP(c, alongX, -gu, 0f, -hw), RoofP(c, alongX, -gu, 0f, hw), RoofP(c, alongX, -gu, rise, 0f), RoofP(Vector3.zero, alongX, -1f, 0f, 0f), gable);
        }

        /// <summary>Hip roof (four slopes). The ridge length shrinks to zero for square footprints (= pyramid).</summary>
        public static void HipRoof(Vector3 baseCenter, float sx, float sz, float rise, float overhang, Color32 roof, bool forcePyramid = false)
        {
            bool alongX = sx >= sz;
            float hl = (alongX ? sx : sz) * 0.5f + overhang;
            float hs = (alongX ? sz : sx) * 0.5f + overhang;
            float rh = forcePyramid ? 0f : Mathf.Max(0f, hl - hs);
            Vector3 c = baseCenter;
            Vector3 r0 = RoofP(c, alongX, -rh, rise, 0f), r1 = RoofP(c, alongX, rh, rise, 0f);
            Vector3 a = RoofP(c, alongX, -hl, 0f, -hs), b = RoofP(c, alongX, hl, 0f, -hs);
            Vector3 d = RoofP(c, alongX, -hl, 0f, hs), e = RoofP(c, alongX, hl, 0f, hs);
            Vector3 fOut = RoofP(Vector3.zero, alongX, 0f, 1f, -1f), bOut = RoofP(Vector3.zero, alongX, 0f, 1f, 1f);
            if (rh > 1e-4f)
            {
                QuadFacing(a, b, r1, r0, fOut, roof);
                QuadFacing(d, e, r1, r0, bOut, roof);
            }
            else
            {
                TriFacing(a, b, r0, fOut, roof);
                TriFacing(d, e, r0, bOut, roof);
            }
            TriFacing(b, e, r1, RoofP(Vector3.zero, alongX, 1f, 1f, 0f), roof);
            TriFacing(a, d, r0, RoofP(Vector3.zero, alongX, -1f, 1f, 0f), roof);
        }

        /// <summary>Single-slope shed roof rising from the -Z edge to the +Z edge.</summary>
        public static void ShedRoof(Vector3 baseCenter, float sx, float sz, float rise, Color32 roof, Color32 side)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Vector3 c = baseCenter;
            Vector3 a = c + new Vector3(-hx, 0f, -hz), b = c + new Vector3(hx, 0f, -hz);
            Vector3 d = c + new Vector3(-hx, rise, hz), e = c + new Vector3(hx, rise, hz);
            QuadFacing(a, b, e, d, new Vector3(0f, 1f, -0.5f), roof);
            QuadFacing(c + new Vector3(-hx, 0f, hz), c + new Vector3(hx, 0f, hz), e, d, Vector3.forward, side);
            TriFacing(a, c + new Vector3(-hx, 0f, hz), d, Vector3.left, side);
            TriFacing(b, c + new Vector3(hx, 0f, hz), e, Vector3.right, side);
        }

        // ------------------------------------------------------------------ output

        /// <summary>Creates a new Mesh from the buffers (UInt32 indices when needed), uploaded and made non-readable.</summary>
        public static Mesh ToMesh(string name)
        {
            var mesh = new Mesh();
            mesh.name = name;
            if (s_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(s_verts);
            mesh.SetNormals(s_normals);
            mesh.SetColors(s_colors);
            mesh.SetTriangles(s_tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ------------------------------------------------------------------ color helpers

        public static Color32 Shade(Color32 c, float k)
        {
            return new Color32(Sat(c.r * k), Sat(c.g * k), Sat(c.b * k), c.a);
        }

        public static Color32 Mix(Color32 a, Color32 b, float t)
        {
            return new Color32(Sat(a.r + (b.r - a.r) * t), Sat(a.g + (b.g - a.g) * t), Sat(a.b + (b.b - a.b) * t), 255);
        }

        private static byte Sat(float v) { return v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)v; }
    }
}
