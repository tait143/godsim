// Compile-only stubs of UnityEngine.KeyCode (values as in Unity) and the legacy Input Manager API.
namespace UnityEngine
{
    public enum KeyCode
    {
        None = 0, Backspace = 8, Delete = 127, Tab = 9, Return = 13, Escape = 27, Space = 32,
        Keypad0 = 256, Keypad1 = 257, Keypad2 = 258, Keypad3 = 259, Keypad4 = 260,
        Keypad5 = 261, Keypad6 = 262, Keypad7 = 263, Keypad8 = 264, Keypad9 = 265,
        KeypadPeriod = 266, KeypadDivide = 267, KeypadMultiply = 268, KeypadMinus = 269,
        KeypadPlus = 270, KeypadEnter = 271, KeypadEquals = 272,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276,
        Insert = 277, Home = 278, End = 279, PageUp = 280, PageDown = 281,
        F1 = 282, F2 = 283, F3 = 284, F4 = 285, F5 = 286, F6 = 287, F7 = 288, F8 = 289,
        F9 = 290, F10 = 291, F11 = 292, F12 = 293,
        Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52,
        Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57,
        Quote = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47, Semicolon = 59, Equals = 61,
        LeftBracket = 91, Backslash = 92, RightBracket = 93, BackQuote = 96,
        A = 97, B = 98, C = 99, D = 100, E = 101, F = 102, G = 103, H = 104, I = 105, J = 106,
        K = 107, L = 108, M = 109, N = 110, O = 111, P = 112, Q = 113, R = 114, S = 115, T = 116,
        U = 117, V = 118, W = 119, X = 120, Y = 121, Z = 122,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306,
        RightAlt = 307, LeftAlt = 308, RightCommand = 309, RightApple = 309,
        LeftCommand = 310, LeftApple = 310, LeftWindows = 311, RightWindows = 312
    }

    public class Input
    {
        public static bool GetKey(KeyCode key) => throw null;
        public static bool GetKeyDown(KeyCode key) => throw null;
        public static bool GetMouseButton(int button) => throw null;
        public static bool GetMouseButtonDown(int button) => throw null;
        public static bool GetMouseButtonUp(int button) => throw null;
        public static Vector3 mousePosition => throw null;
        public static Vector2 mouseScrollDelta => throw null;
    }
}
