using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Shared helpers of the interaction layer: null-safe access to the running simulation, pointer
    /// picking (cached once per frame), terrain height sampling and outline line renderers. Allocation-free.
    /// </summary>
    internal static class InteractionUtil
    {
        private const float RayLength = 10000f;
        /// <summary>How far past a building collider's surface the hit point is pushed (lands inside the footprint).</summary>
        private const float BuildingHitNudge = 0.05f;

        private static int _pickFrame = -1;
        private static bool _pickHit;
        private static Vec2i _pickTile;
        private static Vector3 _pickPoint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pickFrame = -1;
            _pickHit = false;
        }

        // ------------------------------------------------------------------ simulation access

        /// <summary>The running simulation, or null before the runner has created one.</summary>
        public static GodSimulation Simulation
        {
            get
            {
                GodSimRunner runner = GodSimRunner.Instance;
                return runner != null ? runner.Simulation : null;
            }
        }

        /// <summary>The current world state, or null.</summary>
        public static WorldState State
        {
            get
            {
                GodSimulation sim = Simulation;
                return sim != null ? sim.State : null;
            }
        }

        /// <summary>The current map, or null.</summary>
        public static WorldMap Map
        {
            get
            {
                WorldState s = State;
                return s != null ? s.Map : null;
            }
        }

        /// <summary>The camera used for picking: the god camera when present, otherwise Camera.main.</summary>
        public static Camera ActiveCamera
        {
            get
            {
                GodCameraController ctrl = GodCameraController.Instance;
                if (ctrl != null)
                {
                    Camera c = ctrl.Camera;
                    if (c != null) return c;
                }
                return Camera.main;
            }
        }

        // ------------------------------------------------------------------ input helpers

        public static bool ShiftHeld => InputAdapter.GetKey(KeyCode.LeftShift) || InputAdapter.GetKey(KeyCode.RightShift);
        public static bool CtrlHeld => InputAdapter.GetKey(KeyCode.LeftControl) || InputAdapter.GetKey(KeyCode.RightControl);
        public static bool AltHeld => InputAdapter.GetKey(KeyCode.LeftAlt) || InputAdapter.GetKey(KeyCode.RightAlt);

        /// <summary>True when the pointer lies inside the game view.</summary>
        public static bool PointerInScreen(Vector2 p)
        {
            return p.x >= 0f && p.y >= 0f && p.x < Screen.width && p.y < Screen.height;
        }

        /// <summary>
        /// True while god-power targeting owns the mouse this frame (a power is selected, or was used /
        /// cancelled earlier this frame). Selection must not react to clicks or Esc then.
        /// </summary>
        public static bool PowerTargetingActive
        {
            get
            {
                GodPowerController pc = GodPowerController.Instance;
                return pc != null && pc.isActiveAndEnabled && (pc.HasPower || pc.LastActiveFrame == Time.frameCount);
            }
        }

        // ------------------------------------------------------------------ picking

        /// <summary>
        /// Tile under the pointer (computed once per frame and cached). False when the pointer is over the
        /// HUD, outside the game view, off the map, or before the simulation exists.
        /// </summary>
        public static bool TryGetPointerTile(out Vec2i tile, out Vector3 point)
        {
            int frame = Time.frameCount;
            if (frame != _pickFrame)
            {
                _pickFrame = frame;
                _pickHit = ComputePointerTile(out _pickTile, out _pickPoint);
            }
            tile = _pickTile;
            point = _pickPoint;
            return _pickHit;
        }

        private static bool ComputePointerTile(out Vec2i tile, out Vector3 point)
        {
            tile = Vec2i.Invalid;
            point = Vector3.zero;
            if (InputAdapter.PointerOverUI) return false;
            Vector2 mouse = InputAdapter.MousePosition;
            if (!PointerInScreen(mouse)) return false;
            WorldMap map = Map;
            if (map == null) return false;
            Camera cam = ActiveCamera;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(new Vector3(mouse.x, mouse.y, 0f));
            return PickTile(map, ray, out tile, out point);
        }

        /// <summary>
        /// Resolves a ray to a tile: (1) building colliders (a hit whose tile holds a building), (2) the
        /// terrain layer, (3) an analytic ground plane starting at y = 0 refined against the surface height.
        /// </summary>
        public static bool PickTile(WorldMap map, Ray ray, out Vec2i tile, out Vector3 point)
        {
            tile = Vec2i.Invalid;
            point = Vector3.zero;
            if (map == null) return false;
            RaycastHit hit;

            if (BuildingRenderer.Instance != null &&
                Physics.Raycast(ray, out hit, RayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 inside = hit.point + ray.direction * BuildingHitNudge;
                Vec2i t;
                if (GodSimCoords.WorldToTile(map, inside, out t) && map.BuildingAt[map.Index(t)] >= 0)
                {
                    tile = t;
                    point = hit.point;
                    return true;
                }
            }

            TerrainRenderer terrain = TerrainRenderer.Instance;
            if (terrain != null)
            {
                int mask = terrain.TerrainLayer;
                if (mask != 0 && Physics.Raycast(ray, out hit, RayLength, mask, QueryTriggerInteraction.Ignore))
                {
                    point = hit.point;
                    return GodSimCoords.WorldToTile(map, hit.point, out tile);
                }
            }

            // Fallback: intersect y = 0, then re-intersect at the surface height of the tile found.
            float planeY = 0f;
            bool any = false;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p;
                if (!RayToPlane(ray, planeY, out p)) break;
                any = true;
                point = p;
                Vec2i t;
                if (!GodSimCoords.WorldToTile(map, p, out t)) { tile = t; break; }
                tile = t;
                float h = GodSimCoords.SurfaceHeight(map, t.X, t.Y);
                if (Mathf.Abs(h - planeY) < 0.01f) break;
                planeY = h;
            }
            if (!any) return false;
            Vec2i final;
            bool inside2 = GodSimCoords.WorldToTile(map, point, out final);
            tile = final;
            return inside2;
        }

        /// <summary>Analytic intersection of a ray with the horizontal plane y = planeY (forward hits only).</summary>
        public static bool RayToPlane(Ray ray, float planeY, out Vector3 point)
        {
            Vector3 d = ray.direction;
            Vector3 o = ray.origin;
            point = Vector3.zero;
            if (Mathf.Abs(d.y) < 1e-5f) return false;
            float t = (planeY - o.y) / d.y;
            if (!(t > 0f) || t > RayLength) return false;
            point = new Vector3(o.x + d.x * t, planeY, o.z + d.z * t);
            return true;
        }

        // ------------------------------------------------------------------ terrain sampling

        /// <summary>
        /// Terrain surface height at a world XZ position: the larger of the tile's own height and the
        /// bilinear blend of the surrounding tile centers, so overlays stay above smooth and blocky terrain.
        /// </summary>
        public static float SurfaceAt(WorldMap map, float worldX, float worldZ)
        {
            if (map == null) return 0f;
            if (float.IsNaN(worldX) || float.IsInfinity(worldX) || float.IsNaN(worldZ) || float.IsInfinity(worldZ)) return 0f;
            float ts = GodSimCoords.TileSize;
            float fx = Mathf.Clamp(worldX / ts, -1f, map.Width + 1f);
            float fz = Mathf.Clamp(worldZ / ts, -1f, map.Height + 1f);
            int tx = Mathf.FloorToInt(fx);
            int tz = Mathf.FloorToInt(fz);
            float own = GodSimCoords.SurfaceHeight(map, tx, tz);

            float cx = fx - 0.5f, cz = fz - 0.5f;
            int x0 = Mathf.FloorToInt(cx), z0 = Mathf.FloorToInt(cz);
            float ax = cx - x0, az = cz - z0;
            float h00 = GodSimCoords.SurfaceHeight(map, x0, z0);
            float h10 = GodSimCoords.SurfaceHeight(map, x0 + 1, z0);
            float h01 = GodSimCoords.SurfaceHeight(map, x0, z0 + 1);
            float h11 = GodSimCoords.SurfaceHeight(map, x0 + 1, z0 + 1);
            float a = h00 + (h10 - h00) * ax;
            float b = h01 + (h11 - h01) * ax;
            float smooth = a + (b - a) * az;
            return smooth > own ? smooth : own;
        }

        // ------------------------------------------------------------------ line renderers

        /// <summary>Creates a looped, world-space, shadowless line renderer under the parent.</summary>
        public static LineRenderer CreateLine(Transform parent, string name, Material material, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 0;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (material != null) lr.sharedMaterial = material;
            lr.startColor = color;
            lr.endColor = color;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Line width multiplier that keeps outlines readable at every zoom level.</summary>
        public static float ZoomWidthScale()
        {
            float h = 40f;
            GodCameraController ctrl = GodCameraController.Instance;
            if (ctrl != null) h = ctrl.CurrentHeight;
            else
            {
                Camera c = Camera.main;
                if (c != null) h = c.transform.position.y;
            }
            return Mathf.Clamp(h / 30f, 0.6f, 4f);
        }

        /// <summary>
        /// Writes a looped rectangle outline around tiles [x0, x0+w) x [y0, y0+h) that follows the terrain
        /// (corner heights = max of the four adjacent tiles, plus lift). Size is clamped to 32 x 32 tiles.
        /// </summary>
        public static void DrawTileRect(LineRenderer lr, WorldMap map, int x0, int y0, int w, int h, float lift)
        {
            if (lr == null || map == null) return;
            if (w < 1) w = 1; else if (w > 32) w = 32;
            if (h < 1) h = 1; else if (h > 32) h = 32;
            int count = 2 * (w + h);
            if (lr.positionCount != count) lr.positionCount = count;
            int n = 0;
            for (int i = 0; i < w; i++) lr.SetPosition(n++, Corner(map, x0 + i, y0, lift));
            for (int i = 0; i < h; i++) lr.SetPosition(n++, Corner(map, x0 + w, y0 + i, lift));
            for (int i = 0; i < w; i++) lr.SetPosition(n++, Corner(map, x0 + w - i, y0 + h, lift));
            for (int i = 0; i < h; i++) lr.SetPosition(n++, Corner(map, x0, y0 + h - i, lift));
        }

        /// <summary>World position of tile-grid corner (cx, cy), i.e. the shared corner of tiles (cx-1..cx, cy-1..cy).</summary>
        private static Vector3 Corner(WorldMap map, int cx, int cy, float lift)
        {
            float hgt = GodSimCoords.SurfaceHeight(map, cx, cy);
            float t = GodSimCoords.SurfaceHeight(map, cx - 1, cy); if (t > hgt) hgt = t;
            t = GodSimCoords.SurfaceHeight(map, cx, cy - 1); if (t > hgt) hgt = t;
            t = GodSimCoords.SurfaceHeight(map, cx - 1, cy - 1); if (t > hgt) hgt = t;
            float ts = GodSimCoords.TileSize;
            return new Vector3(cx * ts, hgt + lift, cy * ts);
        }
    }
}
