using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Global keyboard shortcuts: Space pause / resume; + / = / keypad + or . faster; - / keypad - or , slower;
    /// F focus the camera on the selection; Home focus on the most populous living settlement.
    /// Ignored while Ctrl or Alt is held (leaves room for editor / OS shortcuts).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("GodSim/Game Shortcuts")]
    public sealed class GameShortcuts : MonoBehaviour
    {
        /// <summary>The active instance (last enabled), or null.</summary>
        public static GameShortcuts Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("GameShortcuts: more than one active instance; the latest enabled one is used.");
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (InteractionUtil.CtrlHeld || InteractionUtil.AltHeld) return;

            GodSimRunner runner = GodSimRunner.Instance;
            if (runner != null)
            {
                if (InputAdapter.GetKeyDown(KeyCode.Space)) runner.Paused = !runner.Paused;

                int speed = 0;
                if (InputAdapter.GetKeyDown(KeyCode.Equals) || InputAdapter.GetKeyDown(KeyCode.KeypadPlus) || InputAdapter.GetKeyDown(KeyCode.Period)) speed++;
                if (InputAdapter.GetKeyDown(KeyCode.Minus) || InputAdapter.GetKeyDown(KeyCode.KeypadMinus) || InputAdapter.GetKeyDown(KeyCode.Comma)) speed--;
                if (speed != 0) ChangeSpeed(runner, speed);
            }

            if (InputAdapter.GetKeyDown(KeyCode.F)) FocusSelection();
            if (InputAdapter.GetKeyDown(KeyCode.Home)) FocusLargestSettlement();
        }

        private static void ChangeSpeed(GodSimRunner runner, int delta)
        {
            float[] levels = GodSimRunner.SpeedLevels;
            int n = levels != null ? levels.Length : 0;
            if (n == 0) return;
            int next = Mathf.Clamp(runner.SpeedIndex + delta, 0, n - 1);
            if (next != runner.SpeedIndex) runner.SpeedIndex = next;
        }

        private static void FocusSelection()
        {
            GodCameraController cam = GodCameraController.Instance;
            SelectionController sel = SelectionController.Instance;
            if (cam == null || sel == null || !sel.HasSelection) return;
            WorldState state = InteractionUtil.State;
            Vec2i target = sel.SelectedTile;
            if (state != null)
            {
                if (sel.SelectedBuildingId >= 0)
                {
                    Building b = state.Buildings.Get(sel.SelectedBuildingId);
                    if (b != null && !b.IsDestroyed) target = b.Center;
                }
                else if (sel.SelectedArmyId >= 0)
                {
                    Army a = state.Diplomacy.Armies.Get(sel.SelectedArmyId);
                    if (a != null && a.IsAlive) target = a.Position;
                }
            }
            if (target.IsValid) cam.FocusOn(target);
        }

        private static void FocusLargestSettlement()
        {
            GodCameraController cam = GodCameraController.Instance;
            WorldState state = InteractionUtil.State;
            if (cam == null || state == null) return;
            var items = state.Settlements.Items;
            Settlement best = null;
            for (int i = 0; i < items.Count; i++)
            {
                Settlement s = items[i];
                if (s == null || !s.IsAlive) continue;
                if (best == null || s.Population > best.Population || (s.Population == best.Population && s.Id < best.Id)) best = s;
            }
            if (best != null) cam.FocusOn(best.Center);
        }
    }
}
