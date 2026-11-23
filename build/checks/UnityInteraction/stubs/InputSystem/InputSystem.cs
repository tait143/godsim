// Compile-only stubs of the Input System 1.x (com.unity.inputsystem 1.11) members used by InputAdapter.
// Only compiled in the ENABLE_INPUT_SYSTEM configuration (-p:InputSystem=true).
using UnityEngine;

namespace UnityEngine.InputSystem
{
    public abstract class InputControl { }

    public abstract class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue ReadValue() => throw null;
    }

    public class InputDevice : InputControl { }

    public class Pointer : InputDevice
    {
        public static Pointer current { get => throw null; internal set => throw null; }
        public Controls.Vector2Control position { get => throw null; protected set => throw null; }
    }

    public class Mouse : Pointer
    {
        public static new Mouse current { get => throw null; private set => throw null; }
        public Controls.DeltaControl scroll { get => throw null; protected set => throw null; }
        public Controls.ButtonControl leftButton { get => throw null; protected set => throw null; }
        public Controls.ButtonControl middleButton { get => throw null; protected set => throw null; }
        public Controls.ButtonControl rightButton { get => throw null; protected set => throw null; }
        public Controls.ButtonControl backButton { get => throw null; protected set => throw null; }
        public Controls.ButtonControl forwardButton { get => throw null; protected set => throw null; }
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current { get => throw null; private set => throw null; }
        public Controls.KeyControl this[Key key] => throw null;
    }

    /// <summary>Values as in com.unity.inputsystem 1.x (physical, US-layout key locations).</summary>
    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash,
        LeftBracket, RightBracket, Minus, Equals,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, AltGr = RightAlt, LeftCtrl, RightCtrl,
        LeftMeta, RightMeta, LeftWindows = LeftMeta, RightWindows = RightMeta, LeftApple = LeftMeta,
        RightApple = RightMeta, LeftCommand = LeftMeta, RightCommand = RightMeta,
        ContextMenu, Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp,
        Home, End, Insert, Delete, CapsLock, NumLock, PrintScreen, ScrollLock, Pause,
        NumpadEnter, NumpadDivide, NumpadMultiply, NumpadPlus, NumpadMinus, NumpadPeriod, NumpadEquals,
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        OEM1, OEM2, OEM3, OEM4, OEM5, IMESelected
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class AxisControl : InputControl<float> { }

    public class ButtonControl : AxisControl
    {
        public bool isPressed => throw null;
        public bool wasPressedThisFrame => throw null;
        public bool wasReleasedThisFrame => throw null;
    }

    public class KeyControl : ButtonControl { }

    public class Vector2Control : InputControl<Vector2> { }

    public class DeltaControl : Vector2Control { }
}
