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
        Photo, Rise, Sink, TimeBack, TimeForward, Screenshot, Ctrl, Pause, Focus, Recall, Feed, Telemetry, Sync, Combat, Inspect
    }

    // Reads mouse and keyboard through whichever input backend the project uses
    // (the new Input System package or the old Input Manager), so the scripts
    // work in any Unity 6 project without changing Player Settings.
    public static class GameInput
    {
        const string BindingPrefix = "settings.key.";
        static readonly GameKey[] Actions = (GameKey[])System.Enum.GetValues(typeof(GameKey));
        static readonly KeyCode[][] Defaults = BuildDefaults();
        static readonly KeyCode[] Primary = new KeyCode[Actions.Length];
        static readonly KeyCode[][] ResolvedCodes = new KeyCode[Actions.Length][];
        static readonly string[] BindingLabels = new string[Actions.Length];
        static bool bindingsLoaded;

        // Matching entries describe the same physical key in both backends. Preferences always store KeyCode.
        static readonly KeyCode[] SupportedCodes =
        {
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G,
            KeyCode.H, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N,
            KeyCode.O, KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T, KeyCode.U,
            KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z,
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12,
            KeyCode.F13, KeyCode.F14, KeyCode.F15,
            KeyCode.F16, KeyCode.F17, KeyCode.F18, KeyCode.F19, KeyCode.F20,
            KeyCode.F21, KeyCode.F22, KeyCode.F23, KeyCode.F24,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Space, KeyCode.Tab, KeyCode.Return, KeyCode.Escape, KeyCode.Backspace,
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
            KeyCode.KeypadPeriod, KeyCode.KeypadDivide, KeyCode.KeypadMultiply, KeyCode.KeypadMinus,
            KeyCode.KeypadPlus, KeyCode.KeypadEnter, KeyCode.KeypadEquals,
            KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Minus, KeyCode.Equals,
            KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Backslash, KeyCode.Semicolon,
            KeyCode.Quote, KeyCode.BackQuote, KeyCode.Pause, KeyCode.Insert, KeyCode.Delete,
            KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.CapsLock, KeyCode.Numlock, KeyCode.ScrollLock, KeyCode.Print
        };

#if ENABLE_INPUT_SYSTEM
        static readonly Key[][] ResolvedKeys = new Key[Actions.Length][];
        static readonly Key[] SupportedKeys =
        {
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G,
            Key.H, Key.I, Key.J, Key.K, Key.L, Key.M, Key.N,
            Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U,
            Key.V, Key.W, Key.X, Key.Y, Key.Z,
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
            Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6,
            Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
            Key.F13, Key.F14, Key.F15,
            Key.F16, Key.F17, Key.F18, Key.F19, Key.F20,
            Key.F21, Key.F22, Key.F23, Key.F24,
            Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow,
            Key.Space, Key.Tab, Key.Enter, Key.Escape, Key.Backspace,
            Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl,
            Key.LeftAlt, Key.RightAlt,
            Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
            Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9,
            Key.NumpadPeriod, Key.NumpadDivide, Key.NumpadMultiply, Key.NumpadMinus,
            Key.NumpadPlus, Key.NumpadEnter, Key.NumpadEquals,
            Key.LeftBracket, Key.RightBracket, Key.Minus, Key.Equals,
            Key.Comma, Key.Period, Key.Slash, Key.Backslash, Key.Semicolon,
            Key.Quote, Key.Backquote, Key.Pause, Key.Insert, Key.Delete,
            Key.Home, Key.End, Key.PageUp, Key.PageDown,
            Key.CapsLock, Key.NumLock, Key.ScrollLock, Key.PrintScreen
        };
#endif

        // The UI shows the cached primary and any remaining secondary without allocating every GUI pass.
        public static string BindingLabel(GameKey key)
        {
            LoadBindings();
            return BindingLabels[(int)key];
        }

        // Keep names readable in the controls page, including keys whose enum names differ between backends.
        public static string KeyName(KeyCode code)
        {
            string name = code.ToString();
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9) return name.Substring(5);
            if (name.StartsWith("Keypad")) return "Keypad " + name.Substring(6);
            switch (code)
            {
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.UpArrow: return "Up arrow";
                case KeyCode.DownArrow: return "Down arrow";
                case KeyCode.LeftArrow: return "Left arrow";
                case KeyCode.RightArrow: return "Right arrow";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Print: return "Print Screen";
                default: return name;
            }
        }

        // Replace the primary and swap occupied keys so a rebind cannot silently steal another action.
        // Shift/Ctrl remain independent modifiers. Existing shared primaries move together during a swap.
        public static bool Rebind(GameKey key, KeyCode code, out GameKey[] swapped)
        {
            LoadBindings();
            swapped = System.Array.Empty<GameKey>();
            if ((int)key < 0 || (int)key >= Actions.Length || !Supported(code)) return false;
            int index = (int)key;
            KeyCode previous = Primary[index];
            if (previous == code) return true;
            var conflicts = new System.Collections.Generic.List<GameKey>();
            for (int i = 0; i < Actions.Length; i++)
                if (i != index && !Modifier(Actions[i]) && System.Array.IndexOf(ResolvedCodes[i], code) >= 0)
                    conflicts.Add(Actions[i]);

            foreach (var other in conflicts) Primary[(int)other] = previous;
            if (conflicts.Count > 0)
            {
                // Focus and photo descent share F by default; preserve that sharing without creating a new clash.
                for (int i = 0; i < Actions.Length; i++)
                    if (i != index && !Modifier(Actions[i]) && !conflicts.Contains(Actions[i]) && Primary[i] == previous)
                    {
                        Primary[i] = code;
                        conflicts.Add(Actions[i]);
                    }
            }
            Primary[index] = code;
            swapped = conflicts.ToArray();
            SaveBindings();
            RebuildBindings();
            return true;
        }

        // Clear only control overrides, leaving the player's other settings intact.
        public static void ResetBindings()
        {
            LoadBindings();
            for (int i = 0; i < Actions.Length; i++)
            {
                Primary[i] = Defaults[i][0];
                PlayerPrefs.DeleteKey(BindingPrefix + Actions[i]);
            }
            PlayerPrefs.Save();
            RebuildBindings();
        }

        // Internal modifier actions are not swapped when a player assigns Shift or Ctrl to another action.
        static bool Modifier(GameKey key) => key == GameKey.Shift || key == GameKey.Ctrl;

        // Accept only keyboard keys that have a counterpart in both input backends.
        static bool Supported(KeyCode code) => System.Array.IndexOf(SupportedCodes, code) >= 0;

        // Read preferences once, rejecting unknown or corrupt values before they reach Keyboard's indexer.
        static void LoadBindings()
        {
            if (bindingsLoaded) return;
            for (int i = 0; i < Actions.Length; i++)
            {
                var code = (KeyCode)PlayerPrefs.GetInt(BindingPrefix + Actions[i], (int)Defaults[i][0]);
                Primary[i] = Supported(code) ? code : Defaults[i][0];
            }
            RebuildBindings();
            bindingsLoaded = true;
        }

        // Save the portable KeyCode value rather than the Input System enum, whose numbers differ.
        static void SaveBindings()
        {
            for (int i = 0; i < Actions.Length; i++)
            {
                string pref = BindingPrefix + Actions[i];
                if (Primary[i] == Defaults[i][0]) PlayerPrefs.DeleteKey(pref);
                else PlayerPrefs.SetInt(pref, (int)Primary[i]);
            }
            PlayerPrefs.Save();
        }

        // Resolve secondary defaults only when bindings change. A primary takes precedence over an alias.
        static void RebuildBindings()
        {
            for (int i = 0; i < Actions.Length; i++)
            {
                KeyCode secondary = Defaults[i].Length > 1 ? Defaults[i][1] : KeyCode.None;
                bool keepSecondary = secondary != KeyCode.None && System.Array.IndexOf(Primary, secondary) < 0;
                ResolvedCodes[i] = keepSecondary ? new[] { Primary[i], secondary } : new[] { Primary[i] };
                BindingLabels[i] = KeyName(Primary[i]) + (keepSecondary ? " / " + KeyName(secondary) : "");
#if ENABLE_INPUT_SYSTEM
                var codes = ResolvedCodes[i];
                var keys = new Key[codes.Length];
                for (int k = 0; k < codes.Length; k++)
                    keys[k] = SupportedKeys[System.Array.IndexOf(SupportedCodes, codes[k])];
                ResolvedKeys[i] = keys;
#endif
            }
        }

        // Build the original defaults once; both backends now share this single source of bindings.
        static KeyCode[][] BuildDefaults()
        {
            var defaults = new KeyCode[Actions.Length][];
            for (int i = 0; i < Actions.Length; i++) defaults[i] = DefaultCodes(Actions[i]);
            return defaults;
        }

#if ENABLE_INPUT_SYSTEM
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool LeftUp => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
        static float RawScroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        // Poll the cached keys without creating arrays or consulting preferences during gameplay.
        public static bool Held(GameKey key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var k in Keys(key))
                if (keyboard[k].isPressed) return true;
            return false;
        }

        // Report a press from either the primary key or its remaining default alias.
        public static bool Pressed(GameKey key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            foreach (var k in Keys(key))
                if (keyboard[k].wasPressedThisFrame) return true;
            return false;
        }

        // Resolve on first use and return the same array until a binding changes.
        static Key[] Keys(GameKey key)
        {
            LoadBindings();
            return ResolvedKeys[(int)key];
        }
#else
        public static Vector2 MousePosition => Input.mousePosition;
        public static bool LeftDown => Input.GetMouseButtonDown(0);
        public static bool LeftHeld => Input.GetMouseButton(0);
        public static bool LeftUp => Input.GetMouseButtonUp(0);
        public static bool RightDown => Input.GetMouseButtonDown(1);
        public static bool RightHeld => Input.GetMouseButton(1);
        static float RawScroll => Input.mouseScrollDelta.y;

        // Poll the cached legacy keys, with the same overrides as the Input System backend.
        public static bool Held(GameKey key)
        {
            foreach (var k in Codes(key))
                if (Input.GetKey(k)) return true;
            return false;
        }

        // Report presses through the old Input Manager without per-frame allocations.
        public static bool Pressed(GameKey key)
        {
            foreach (var k in Codes(key))
                if (Input.GetKeyDown(k)) return true;
            return false;
        }

        // Resolve on first use and return the same array until a binding changes.
        static KeyCode[] Codes(GameKey key)
        {
            LoadBindings();
            return ResolvedCodes[(int)key];
        }
#endif

        // Preserve every original primary and secondary shortcut for reset and first launch.
        static KeyCode[] DefaultCodes(GameKey key)
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
                case GameKey.Sync: return new[] { KeyCode.G };
                case GameKey.Combat: return new[] { KeyCode.C };
                case GameKey.Inspect: return new[] { KeyCode.Z };
                default: return new KeyCode[0];
            }
        }

        // +1 for a wheel notch up, -1 for down, 0 otherwise (the two backends use different step sizes).
        public static float Scroll => Mathf.Abs(RawScroll) < 0.01f ? 0f : Mathf.Sign(RawScroll);
    }
}
