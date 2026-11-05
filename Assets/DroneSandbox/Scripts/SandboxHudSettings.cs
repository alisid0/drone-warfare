using UnityEngine;

namespace DroneSandbox
{
    // The settings screen, from the main menu: a row of choices per setting, the current one lit.
    public partial class SandboxHud
    {
        // Draw difficulty, comfort settings and the entry to keyboard controls using the same menu layout.
        void DrawSettings()
        {
            float s = Scale;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.3f));
            Rect r = Centered(560f, 646f);
            Fill(r, ModalColor);
            Fill(new Rect(r.x, r.y, r.width, 3f * s), Teal);
            GUI.Label(new Rect(r.x, r.y + 16f * s, r.width, 34f * s), "SETTINGS", Tinted(new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter }, label.normal.textColor));

            float y = r.y + 72f * s;
            int difficulty = Choice(r, ref y, "Difficulty", "Reports, warnings and stock", Difficulty.Names, (int)Settings.Difficulty, 80f);
            if (difficulty >= 0) Settings.Difficulty = (DifficultyLevel)difficulty;
            int quality = Choice(r, ref y, "Graphics", "Lower it if the game stutters", Settings.QualityNames, Settings.Quality);
            if (quality >= 0) Settings.Quality = quality;
            int upscaling = Choice(r, ref y, "Upscaling", "DLSS on NVIDIA RTX: faster, a touch softer", new[] { "On", "Off" }, Settings.Upscaling ? 0 : 1);
            if (upscaling >= 0) Settings.Upscaling = upscaling == 0;
            int scale = Choice(r, ref y, "Interface size", "Text, panels and markers", new[] { "80%", "90%", "100%", "120%", "140%" },
                System.Array.IndexOf(Settings.UiScales, Settings.UiScale));
            if (scale >= 0) Settings.UiScale = Settings.UiScales[scale];
            int shake = Choice(r, ref y, "Camera shake", "From blasts close by", new[] { "On", "Off" }, Settings.Shake ? 0 : 1);
            if (shake >= 0) Settings.Shake = shake == 0;
            int flashes = Choice(r, ref y, "Bright flashes", "Explosions light the ground", new[] { "Full", "Soft" }, Settings.Flashes ? 0 : 1);
            if (flashes >= 0) Settings.Flashes = flashes == 0;
            int alerts = Choice(r, ref y, "Alert sounds", "Warnings and radio calls", new[] { "On", "Off" }, Settings.AlertSounds ? 0 : 1);
            if (alerts >= 0) Settings.AlertSounds = alerts == 0;
            int tips = Choice(r, ref y, "Tips", "In the first two Operations", new[] { "On", "Off" }, Settings.Tips ? 0 : 1);
            if (tips >= 0) Settings.Tips = tips == 0;
            int volume = Choice(r, ref y, "Volume", "Everything", new[] { "0", "25", "50", "75", "100" },
                System.Array.IndexOf(Settings.Volumes, Settings.Volume));
            if (volume >= 0) Settings.Volume = Settings.Volumes[volume];

            if (GUI.Button(new Rect(r.x + 30f * s, r.yMax - 60f * s, 200f * s, 42f * s), "<b>Controls</b>", button))
                OpenControls();
            var back = new Rect(r.xMax - 230f * s, r.yMax - 60f * s, 200f * s, 42f * s);
            bool escape = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (GUI.Button(back, "<b>Back</b>  (Esc)", button) || escape)
            {
                if (escape) Event.current.Use();
                SettingsOpen = false;
            }
        }

        // One setting: its name and a hint on the left, a button per option on the right. Returns the option
        // clicked this frame, or -1.
        int Choice(Rect panel, ref float y, string name, string hint, string[] options, int current, float optionWidth = 56f)
        {
            float s = Scale;
            GUI.Label(new Rect(panel.x + 30f * s, y, 200f * s, 22f * s), $"<b>{name}</b>", label);
            GUI.Label(new Rect(panel.x + 30f * s, y + 19f * s, 260f * s, 18f * s + 6f), hint, small);
            float width = optionWidth * s, gap = 4f * s;
            float x = panel.xMax - 30f * s - options.Length * width - (options.Length - 1) * gap;
            int clicked = -1;
            for (int i = 0; i < options.Length; i++)
            {
                var b = new Rect(x + i * (width + gap), y + 4f * s, width, 32f * s);
                if (GUI.Button(b, options[i], i == current ? chosenButton : button) && i != current)
                {
                    clicked = i;
                    Sound.Ui("click");
                }
            }
            y += 58f * s;
            return clicked;
        }
    }
}
