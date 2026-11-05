using UnityEngine;

namespace DroneSandbox
{
    // The controls page edits saved keyboard bindings from the menu, where gameplay input is already blocked.
    public partial class SandboxHud
    {
        public bool ControlsOpen;
        Vector2 controlsScroll;
        int bindingRow = -1;
        string controlsMessage = "";

        static readonly (GameKey key, string name)[] ControlRows =
        {
            (GameKey.Up, "Move camera up"),
            (GameKey.Down, "Move camera down"),
            (GameKey.Left, "Move camera left"),
            (GameKey.Right, "Move camera right"),
            (GameKey.RotateLeft, "Rotate camera left"),
            (GameKey.RotateRight, "Rotate camera right"),
            (GameKey.Focus, "Focus on selection"),
            (GameKey.SelectAll, "Select all drones"),
            (GameKey.Deselect, "Deselect drones"),
            (GameKey.Stop, "Stop drones"),
            (GameKey.Recall, "Recall drones"),
            (GameKey.Pause, "Tactical pause"),
            (GameKey.Sync, "Coordinate strike"),
            (GameKey.Combat, "Look at the fight"),
            (GameKey.Feed, "Toggle drone feed"),
            (GameKey.Inspect, "Inspect drone feed (hold)"),
            (GameKey.Launch1, "Launch Scout"),
            (GameKey.Launch2, "Launch Strike"),
            (GameKey.Launch3, "Launch Kamikaze"),
            (GameKey.Launch4, "Launch Nightjar"),
            (GameKey.Thermal, "Toggle thermal view"),
            (GameKey.SkipTime, "Skip time (free play)"),
            (GameKey.Help, "Toggle help"),
            (GameKey.Telemetry, "Toggle telemetry"),
            (GameKey.Photo, "Toggle photo mode"),
            (GameKey.Rise, "Photo: rise"),
            (GameKey.Sink, "Photo: descend"),
            (GameKey.TimeBack, "Slower time (photo: time backward)"),
            (GameKey.TimeForward, "Faster time (photo: time forward)"),
            (GameKey.Screenshot, "Photo: save screenshot")
        };

        // Start a fresh visit from Settings so an abandoned capture cannot carry over.
        void OpenControls()
        {
            ControlsOpen = true;
            bindingRow = -1;
            controlsScroll = Vector2.zero;
            controlsMessage = "";
            GUI.FocusControl(null);
        }

        // Keep the list scrollable at larger interface scales, with capture feedback and navigation always visible.
        void DrawControls()
        {
            ReadControlKey();
            float s = Scale;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.3f));
            Rect r = Centered(Mathf.Min(720f, Screen.width / s - 32f), Mathf.Min(740f, Screen.height / s - 32f));
            Fill(r, ModalColor);
            Fill(new Rect(r.x, r.y, r.width, 3f * s), Teal);
            GUI.Label(new Rect(r.x, r.y + 16f * s, r.width, 34f * s), "CONTROLS",
                Tinted(new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter }, label.normal.textColor));
            GUI.Label(new Rect(r.x + 24f * s, r.y + 58f * s, r.width - 48f * s, 44f * s),
                "Click a key, then press its replacement. Esc cancels. Extra default keys stay unless reassigned. Occupied keys swap.", smallWrap);

            var viewport = new Rect(r.x + 24f * s, r.y + 108f * s, r.width - 48f * s, r.height - 250f * s);
            float rowHeight = 40f * s;
            var content = new Rect(0f, 0f, viewport.width - 24f * s, ControlRows.Length * rowHeight);
            controlsScroll = GUI.BeginScrollView(viewport, controlsScroll, content);
            for (int i = 0; i < ControlRows.Length; i++)
            {
                float y = i * rowHeight;
                float keyWidth = content.width * 0.46f;
                GUI.Label(new Rect(0f, y + 7f * s, content.width - keyWidth - 8f * s, 26f * s), ControlRows[i].name, label);
                string text = bindingRow == i ? "Press a key... (Esc cancels)" : GameInput.BindingLabel(ControlRows[i].key);
                if (GUI.Button(new Rect(content.width - keyWidth, y + 2f * s, keyWidth, 34f * s), text,
                    bindingRow == i ? chosenButton : button))
                {
                    bindingRow = i;
                    controlsMessage = "Waiting for a key for " + ControlRows[i].name + ".";
                    GUI.FocusControl(null);
                    Sound.Ui("click");
                }
            }
            GUI.EndScrollView();

            GUI.Label(new Rect(r.x + 24f * s, r.yMax - 132f * s, r.width - 48f * s, 64f * s),
                controlsMessage, Tinted(smallWrap, Teal));
            float footerWidth = (r.width - 60f * s) / 2f;
            if (GUI.Button(new Rect(r.x + 24f * s, r.yMax - 60f * s, footerWidth, 42f * s), "Reset to defaults", button))
            {
                GameInput.ResetBindings();
                bindingRow = -1;
                controlsMessage = "Default controls restored.";
                GUI.FocusControl(null);
                Sound.Ui("click");
            }
            if (GUI.Button(new Rect(r.xMax - 24f * s - footerWidth, r.yMax - 60f * s, footerWidth, 42f * s),
                "<b>Back</b>  (Esc)", button)) CloseControls();
        }

        // Capture IMGUI keyboard events before buttons see them; Escape cancels capture before it can leave the page.
        void ReadControlKey()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape)
            {
                if (bindingRow >= 0)
                {
                    bindingRow = -1;
                    controlsMessage = "Binding cancelled.";
                }
                else CloseControls();
                e.Use();
                return;
            }
            if (bindingRow < 0 || e.keyCode == KeyCode.None) return;

            var row = ControlRows[bindingRow];
            if (GameInput.Rebind(row.key, e.keyCode, out var swapped))
            {
                controlsMessage = row.name + ": " + GameInput.BindingLabel(row.key) + ".";
                if (swapped.Length > 0)
                {
                    controlsMessage += " Key already in use; swapped: ";
                    for (int i = 0; i < swapped.Length; i++)
                        controlsMessage += (i > 0 ? "; " : "") + ControlName(swapped[i]) + " -> " + GameInput.BindingLabel(swapped[i]);
                    controlsMessage += ".";
                }
                bindingRow = -1;
                GUI.FocusControl(null);
                Sound.Ui("click");
            }
            else controlsMessage = "That key is not supported. Choose another key, or press Esc to cancel.";
            e.Use();
        }

        // Use the same friendly action names in swap messages as in the list.
        static string ControlName(GameKey key)
        {
            foreach (var row in ControlRows)
                if (row.key == key) return row.name;
            return key.ToString();
        }

        // Return to Settings and discard any pending capture without changing its binding.
        void CloseControls()
        {
            ControlsOpen = false;
            bindingRow = -1;
            GUI.FocusControl(null);
        }
    }
}
