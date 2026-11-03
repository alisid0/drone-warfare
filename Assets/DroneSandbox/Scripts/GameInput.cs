using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DroneSandbox
{
    public enum GameKey
    {
        Up, Down, Left, Right, RotateLeft, RotateRight, Shift,
        SelectAll, Deselect, Stop, Help, Launch1, Launch2, Launch3, Launch4, Thermal, SkipTime,
        Photo, Rise, Sink, TimeBack, TimeForward, Screenshot, Ctrl, Pause, Focus, Recall, Feed, Telemetry
    }

    // Reads mouse and keyboard through whichever input backend the project uses
    // (the new Input System package or the old Input Manager), so the scripts
    // work in any Unity 6 project without changing Player Settings.
    public static class GameInput
    {
#if ENABLE_INPUT_SYSTEM
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool LeftUp => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
        static float RawScroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        public static bool Held(GameKey key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var k in Keys(key))
                if (keyboard[k].isPressed) return true;
            return false;
        }

        public static bool Pressed(GameKey key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var k in Keys(key))
                if (keyboard[k].wasPressedThisFrame) return true;
            return false;
        }

        static Key[] Keys(GameKey key)
        {
            switch (key)
            {
                case GameKey.Up: return new[] { Key.W, Key.UpArrow };
                case GameKey.Down: return new[] { Key.S, Key.DownArrow };
                case GameKey.Left: return new[] { Key.A, Key.LeftArrow };
                case GameKey.Right: return new[] { Key.D, Key.RightArrow };
                case GameKey.RotateLeft: return new[] { Key.Q };
                case GameKey.RotateRight: return new[] { Key.E };
                case GameKey.Shift: return new[] { Key.LeftShift, Key.RightShift };
                case GameKey.SelectAll: return new[] { Key.Space };
                case GameKey.Deselect: return new[] { Key.Escape };
                case GameKey.Stop: return new[] { Key.X };
                case GameKey.Help: return new[] { Key.H };
                case GameKey.Launch1: return new[] { Key.Digit1, Key.Numpad1 };
                case GameKey.Launch2: return new[] { Key.Digit2, Key.Numpad2 };
                case GameKey.Launch3: return new[] { Key.Digit3, Key.Numpad3 };
                case GameKey.Launch4: return new[] { Key.Digit4, Key.Numpad4 };
                case GameKey.Thermal: return new[] { Key.T };
                case GameKey.SkipTime: return new[] { Key.N };
                case GameKey.Photo: return new[] { Key.P };
                case GameKey.Rise: return new[] { Key.R };
                case GameKey.Sink: return new[] { Key.F };
                case GameKey.TimeBack: return new[] { Key.LeftBracket, Key.Comma };
                case GameKey.TimeForward: return new[] { Key.RightBracket, Key.Period };
                case GameKey.Screenshot: return new[] { Key.F12, Key.K };
                case GameKey.Ctrl: return new[] { Key.LeftCtrl, Key.RightCtrl };
                case GameKey.Pause: return new[] { Key.Tab, Key.Pause };
                case GameKey.Focus: return new[] { Key.F };
                case GameKey.Recall: return new[] { Key.B };
                case GameKey.Feed: return new[] { Key.V };
                case GameKey.Telemetry: return new[] { Key.F3 };
                default: return new Key[0];
            }
        }
#else
        public static Vector2 MousePosition => Input.mousePosition;
        public static bool LeftDown => Input.GetMouseButtonDown(0);
        public static bool LeftHeld => Input.GetMouseButton(0);
        public static bool LeftUp => Input.GetMouseButtonUp(0);
        public static bool RightDown => Input.GetMouseButtonDown(1);
        public static bool RightHeld => Input.GetMouseButton(1);
        static float RawScroll => Input.mouseScrollDelta.y;

        public static bool Held(GameKey key)
        {
            foreach (var k in Codes(key))
                if (Input.GetKey(k)) return true;
            return false;
        }

        public static bool Pressed(GameKey key)
        {
            foreach (var k in Codes(key))
                if (Input.GetKeyDown(k)) return true;
            return false;
        }

        static KeyCode[] Codes(GameKey key)
        {
            switch (key)
            {
                case GameKey.Up: return new[] { KeyCode.W, KeyCode.UpArrow };
                case GameKey.Down: return new[] { KeyCode.S, KeyCode.DownArrow };
                case GameKey.Left: return new[] { KeyCode.A, KeyCode.LeftArrow };
                case GameKey.Right: return new[] { KeyCode.D, KeyCode.RightArrow };
                case GameKey.RotateLeft: return new[] { KeyCode.Q };
                case GameKey.RotateRight: return new[] { KeyCode.E };
                case GameKey.Shift: return new[] { KeyCode.LeftShift, KeyCode.RightShift };
                case GameKey.SelectAll: return new[] { KeyCode.Space };
                case GameKey.Deselect: return new[] { KeyCode.Escape };
                case GameKey.Stop: return new[] { KeyCode.X };
                case GameKey.Help: return new[] { KeyCode.H };
                case GameKey.Launch1: return new[] { KeyCode.Alpha1, KeyCode.Keypad1 };
                case GameKey.Launch2: return new[] { KeyCode.Alpha2, KeyCode.Keypad2 };
                case GameKey.Launch3: return new[] { KeyCode.Alpha3, KeyCode.Keypad3 };
                case GameKey.Launch4: return new[] { KeyCode.Alpha4, KeyCode.Keypad4 };
                case GameKey.Thermal: return new[] { KeyCode.T };
                case GameKey.SkipTime: return new[] { KeyCode.N };
                case GameKey.Photo: return new[] { KeyCode.P };
                case GameKey.Rise: return new[] { KeyCode.R };
                case GameKey.Sink: return new[] { KeyCode.F };
                case GameKey.TimeBack: return new[] { KeyCode.LeftBracket, KeyCode.Comma };
                case GameKey.TimeForward: return new[] { KeyCode.RightBracket, KeyCode.Period };
                case GameKey.Screenshot: return new[] { KeyCode.F12, KeyCode.K };
                case GameKey.Ctrl: return new[] { KeyCode.LeftControl, KeyCode.RightControl };
                case GameKey.Pause: return new[] { KeyCode.Tab, KeyCode.Pause };
                case GameKey.Focus: return new[] { KeyCode.F };
                case GameKey.Recall: return new[] { KeyCode.B };
                case GameKey.Feed: return new[] { KeyCode.V };
                case GameKey.Telemetry: return new[] { KeyCode.F3 };
                default: return new KeyCode[0];
            }
        }
#endif

        // +1 for a wheel notch up, -1 for down, 0 otherwise (the two backends use different step sizes).
        public static float Scroll => Mathf.Abs(RawScroll) < 0.01f ? 0f : Mathf.Sign(RawScroll);
    }
}
