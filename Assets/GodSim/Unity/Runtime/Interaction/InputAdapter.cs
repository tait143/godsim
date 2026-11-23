using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace GodSim.Unity
{
    /// <summary>
    /// Thin, allocation-free facade over the active input backend. With the Input System package active
    /// (<c>ENABLE_INPUT_SYSTEM</c>, also when "Both" backends are enabled) keys are mapped from
    /// <see cref="KeyCode"/> to <c>Keyboard.current</c> and the mouse is read from <c>Mouse.current</c>;
    /// otherwise the legacy Input Manager (<see cref="Input"/>) is used. Every query returns a neutral
    /// value (false / zero) when the device is missing, so callers never need null checks.
    /// Mouse buttons: 0 = left, 1 = right, 2 = middle, 3 = back, 4 = forward.
    /// </summary>
    public static class InputAdapter
    {
        /// <summary>Raw scroll magnitudes at or above this are treated as platform "120 per notch" units.</summary>
        private const float RawNotchThreshold = 20f;
        private const float RawUnitsPerNotch = 120f;
        private const float MaxScrollPerFrame = 10f;

        /// <summary>True on the frame the key went down.</summary>
        public static bool GetKeyDown(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;
            Key key = ToKey(k);
            return key != Key.None && kb[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }

        /// <summary>True while the key is held.</summary>
        public static bool GetKey(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;
            Key key = ToKey(k);
            return key != Key.None && kb[key].isPressed;
#else
            return Input.GetKey(k);
#endif
        }

        /// <summary>Pointer position in screen pixels (origin bottom-left), like <c>Input.mousePosition</c>.</summary>
        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                return m != null ? m.position.ReadValue() : Vector2.zero;
#else
                Vector3 p = Input.mousePosition;
                return new Vector2(p.x, p.y);
#endif
            }
        }

        /// <summary>True on the frame the mouse button went down.</summary>
        public static bool MouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl b = Button(button);
            return b != null && b.wasPressedThisFrame;
#else
            return button >= 0 && button <= 6 && Input.GetMouseButtonDown(button);
#endif
        }

        /// <summary>True while the mouse button is held.</summary>
        public static bool MouseHeld(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl b = Button(button);
            return b != null && b.isPressed;
#else
            return button >= 0 && button <= 6 && Input.GetMouseButton(button);
#endif
        }

        /// <summary>True on the frame the mouse button was released.</summary>
        public static bool MouseUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl b = Button(button);
            return b != null && b.wasReleasedThisFrame;
#else
            return button >= 0 && button <= 6 && Input.GetMouseButtonUp(button);
#endif
        }

        /// <summary>
        /// Vertical wheel movement this frame in legacy-like units (about +/-1 per wheel notch, positive =
        /// away from the user). The Input System's platform ranges (120 per notch on Windows) are normalised.
        /// </summary>
        public static float ScrollDelta
        {
            get
            {
                float y;
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                if (m == null) return 0f;
                y = m.scroll.ReadValue().y;
                if (Mathf.Abs(y) >= RawNotchThreshold) y /= RawUnitsPerNotch;
#else
                y = Input.mouseScrollDelta.y;
#endif
                if (float.IsNaN(y) || float.IsInfinity(y)) return 0f;
                return Mathf.Clamp(y, -MaxScrollPerFrame, MaxScrollPerFrame);
            }
        }

        /// <summary>True while the pointer is over the HUD; world interaction must ignore mouse input then.</summary>
        public static bool PointerOverUI => GodSimHUD.PointerOverUI;

#if ENABLE_INPUT_SYSTEM
        private static ButtonControl Button(int button)
        {
            Mouse m = Mouse.current;
            if (m == null) return null;
            switch (button)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.backButton;
                case 4: return m.forwardButton;
                default: return null;
            }
        }

        /// <summary>Maps a legacy KeyCode to the Input System's (US-layout, physical) Key. Unmapped = Key.None.</summary>
        private static Key ToKey(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.A: return Key.A; case KeyCode.B: return Key.B; case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D; case KeyCode.E: return Key.E; case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G; case KeyCode.H: return Key.H; case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J; case KeyCode.K: return Key.K; case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M; case KeyCode.N: return Key.N; case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P; case KeyCode.Q: return Key.Q; case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S; case KeyCode.T: return Key.T; case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V; case KeyCode.W: return Key.W; case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y; case KeyCode.Z: return Key.Z;

                case KeyCode.Alpha0: return Key.Digit0; case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2; case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4; case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6; case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8; case KeyCode.Alpha9: return Key.Digit9;

                case KeyCode.Keypad0: return Key.Numpad0; case KeyCode.Keypad1: return Key.Numpad1;
                case KeyCode.Keypad2: return Key.Numpad2; case KeyCode.Keypad3: return Key.Numpad3;
                case KeyCode.Keypad4: return Key.Numpad4; case KeyCode.Keypad5: return Key.Numpad5;
                case KeyCode.Keypad6: return Key.Numpad6; case KeyCode.Keypad7: return Key.Numpad7;
                case KeyCode.Keypad8: return Key.Numpad8; case KeyCode.Keypad9: return Key.Numpad9;
                case KeyCode.KeypadPlus: return Key.NumpadPlus; case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadMultiply: return Key.NumpadMultiply; case KeyCode.KeypadDivide: return Key.NumpadDivide;
                case KeyCode.KeypadPeriod: return Key.NumpadPeriod; case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.KeypadEquals: return Key.NumpadEquals;

                case KeyCode.F1: return Key.F1; case KeyCode.F2: return Key.F2; case KeyCode.F3: return Key.F3;
                case KeyCode.F4: return Key.F4; case KeyCode.F5: return Key.F5; case KeyCode.F6: return Key.F6;
                case KeyCode.F7: return Key.F7; case KeyCode.F8: return Key.F8; case KeyCode.F9: return Key.F9;
                case KeyCode.F10: return Key.F10; case KeyCode.F11: return Key.F11; case KeyCode.F12: return Key.F12;

                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.Insert: return Key.Insert;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;

                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.LeftCommand: return Key.LeftMeta;   // == LeftApple
                case KeyCode.RightCommand: return Key.RightMeta; // == RightApple
                case KeyCode.LeftWindows: return Key.LeftMeta;
                case KeyCode.RightWindows: return Key.RightMeta;

                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Backslash: return Key.Backslash;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                default: return Key.None;
            }
        }
#endif
    }
}
