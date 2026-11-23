using System;
using System.Globalization;
using System.IO;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// A temporary hold on the main camera by a cinematic mode (spectator director, photo mode). Acquiring it
    /// saves the camera pose and field of view and disables <see cref="GodCameraController"/>; releasing it
    /// restores both. A lease can be handed from one mode to another so the original pose survives.
    /// </summary>
    internal struct CinematicCameraLease
    {
        public Camera Camera;
        public GodCameraController Controller;
        public Vector3 Position;
        public Quaternion Rotation;
        public float FieldOfView;
        public bool ControllerWasEnabled;
        public bool IsValid;

        /// <summary>Finds the game camera (controller camera, else Camera.main), saves its pose and disables the controller.</summary>
        public static bool TryAcquire(out CinematicCameraLease lease)
        {
            lease = default;
            GodCameraController controller = GodCameraController.Instance;
            Camera cam = controller != null ? controller.Camera : null;
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            Transform t = cam.transform;
            lease.Camera = cam;
            lease.Controller = controller;
            lease.Position = t.position;
            lease.Rotation = t.rotation;
            lease.FieldOfView = cam.fieldOfView;
            lease.ControllerWasEnabled = controller != null && controller.enabled;
            if (lease.ControllerWasEnabled) controller.enabled = false;
            lease.IsValid = true;
            return true;
        }

        /// <summary>Restores the field of view (and the pose when <paramref name="restorePose"/>), then re-enables the controller.</summary>
        public void Release(bool restorePose)
        {
            if (!IsValid) return;
            IsValid = false;
            if (Camera != null)
            {
                if (restorePose) Camera.transform.SetPositionAndRotation(Position, Rotation);
                Camera.fieldOfView = FieldOfView;
            }
            if (ControllerWasEnabled && Controller != null) Controller.enabled = true;
            Camera = null;
            Controller = null;
        }
    }

    /// <summary>Allocation-free terrain helpers and cached number strings shared by the cinematic components.</summary>
    internal static class CinematicUtil
    {
        private const int NumberMin = -360;
        private const int NumberMax = 2000;
        private static string[] s_numbers;

        /// <summary>World units per tile edge, derived from <see cref="GodSimCoords.TileToWorld(WorldMap, Vec2i)"/> (1 when unknown).</summary>
        public static float TileSize(WorldMap map)
        {
            if (map == null) return 1f;
            float s = GodSimCoords.TileToWorld(map, new Vec2i(1, 0)).x - GodSimCoords.TileToWorld(map, new Vec2i(0, 0)).x;
            return s > 0.0001f && !float.IsInfinity(s) ? s : 1f;
        }

        /// <summary>Terrain surface height under a world position (clamped to the map; 0 without a map).</summary>
        public static float GroundAt(WorldMap map, float tileSize, Vector3 p)
        {
            if (map == null) return 0f;
            return GodSimCoords.SurfaceHeight(map, ToTile(p.x, tileSize), ToTile(p.z, tileSize));
        }

        /// <summary>Highest surface among the tile under <paramref name="p"/> and its four neighbours.</summary>
        public static float GroundAround(WorldMap map, float tileSize, Vector3 p)
        {
            if (map == null) return 0f;
            int x = ToTile(p.x, tileSize), y = ToTile(p.z, tileSize);
            float h = GodSimCoords.SurfaceHeight(map, x, y);
            h = Mathf.Max(h, GodSimCoords.SurfaceHeight(map, x + 1, y));
            h = Mathf.Max(h, GodSimCoords.SurfaceHeight(map, x - 1, y));
            h = Mathf.Max(h, GodSimCoords.SurfaceHeight(map, x, y + 1));
            h = Mathf.Max(h, GodSimCoords.SurfaceHeight(map, x, y - 1));
            return h;
        }

        /// <summary>
        /// Keeps a camera at least <paramref name="clearance"/> above the terrain and raises it when a hill would
        /// block the line of sight to <paramref name="target"/>. Also rejects NaN/Infinity.
        /// </summary>
        public static void ClearTerrain(WorldMap map, float tileSize, ref Vector3 cam, Vector3 target, float clearance)
        {
            if (!IsFinite(cam)) cam = target + new Vector3(0f, 20f, -20f);
            if (map == null) return;
            float min = GroundAround(map, tileSize, cam) + clearance;
            if (cam.y < min) cam.y = min;

            float raise = 0f;
            for (int i = 1; i < 8; i++)
            {
                float f = i / 8f;
                Vector3 p = Vector3.Lerp(target, cam, f);
                float need = GroundAt(map, tileSize, p) + 0.6f * tileSize - p.y;
                if (need > 0f) raise = Mathf.Max(raise, need / f);
            }
            if (raise > 0f) cam.y += Mathf.Min(raise, 200f * tileSize);
        }

        /// <summary>Clamps the XZ position to the map rectangle plus a margin (world units).</summary>
        public static void ClampToMap(WorldMap map, float tileSize, ref Vector3 p, float margin)
        {
            if (map == null) return;
            p.x = Mathf.Clamp(p.x, -margin, map.Width * tileSize + margin);
            p.z = Mathf.Clamp(p.z, -margin, map.Height * tileSize + margin);
        }

        public static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        public static int ToTile(float world, float tileSize)
        {
            if (float.IsNaN(world) || float.IsInfinity(world)) return 0;
            float f = world / tileSize;
            if (f < -100000f) return -100000;
            if (f > 100000f) return 100000;
            return Mathf.FloorToInt(f);
        }

        /// <summary>Cached string for an integer (allocation-free for -360..2000; values outside are clamped).</summary>
        public static string Number(int v)
        {
            if (s_numbers == null)
            {
                s_numbers = new string[NumberMax - NumberMin + 1];
                for (int i = 0; i < s_numbers.Length; i++) s_numbers[i] = (i + NumberMin).ToString(CultureInfo.InvariantCulture);
            }
            if (v < NumberMin) v = NumberMin; else if (v > NumberMax) v = NumberMax;
            return s_numbers[v - NumberMin];
        }

        /// <summary>Deterministic pseudo-angle (degrees) for a shot, from its focus tile and a counter.</summary>
        public static float ShotAngle(Vec2i tile, int counter)
        {
            uint h = unchecked((uint)(tile.X * 73856093) ^ (uint)(tile.Y * 19349663) ^ (uint)(counter * 83492791));
            return (h % 360u);
        }
    }

    /// <summary>
    /// Builds output paths for screenshots and timelapse frames. Every path lives under
    /// <see cref="Application.persistentDataPath"/>; file and folder names are generated here from a fixed
    /// character set (never from user or simulation text), and the final path is verified to stay inside its folder.
    /// </summary>
    internal static class CinematicFiles
    {
        public const string ScreenshotFolder = "Screenshots";
        public const string TimelapseFolder = "Timelapse";

        /// <summary>Application.persistentDataPath/&lt;folder&gt;, or null when the data path is unavailable.</summary>
        public static string Root(string folder)
        {
            string basePath = Application.persistentDataPath;
            if (string.IsNullOrEmpty(basePath) || !IsSafeName(folder)) return null;
            return Path.Combine(basePath, folder);
        }

        /// <summary>UTC timestamp suitable for a file name, e.g. 20261119_154502_123.</summary>
        public static string Timestamp() => DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

        /// <summary>True for 1..96 chars of [A-Za-z0-9_-.], not starting with '.' and without "..".</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 96 || name[0] == '.' || name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Combines a safe file name with a directory and checks that the result stays inside it. Null if unsafe.</summary>
        public static string SafeCombine(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory) || !IsSafeName(fileName)) return null;
            string dirFull = Path.GetFullPath(directory);
            string full = Path.GetFullPath(Path.Combine(dirFull, fileName));
            string prefix = dirFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? dirFull : dirFull + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
        }

        /// <summary>A path for stem+ext in the directory that does not exist yet (adds _1.._99). Null if none.</summary>
        public static string UniquePath(string directory, string stem, string ext)
        {
            for (int n = 0; n < 100; n++)
            {
                string name = n == 0 ? stem + ext : stem + "_" + n.ToString(CultureInfo.InvariantCulture) + ext;
                string path = SafeCombine(directory, name);
                if (path == null) return null;
                if (!File.Exists(path)) return path;
            }
            return null;
        }
    }
}
