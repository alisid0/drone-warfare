using System.Collections.Generic;
using UnityEngine;

namespace DroneSandbox
{
    // A scrollable service record and queued notices share the existing HUD styles and unscaled menu clock.
    public partial class SandboxHud
    {
        public bool AchievementsOpen;
        Vector2 achievementsScroll;
        readonly Queue<Achievement> achievementNotices = new Queue<Achievement>();
        Achievement achievementNotice;
        float achievementUntil;

        // Subscribe for this HUD's lifetime so one unlock produces one notice.
        void OnEnable() => Achievements.Unlocked += QueueAchievement;

        // Release the static event when the HUD goes away, including scene reloads.
        void OnDisable() => Achievements.Unlocked -= QueueAchievement;

        // Queue simultaneous awards instead of overwriting a notice that has not been read yet.
        void QueueAchievement(Achievement entry) => achievementNotices.Enqueue(entry);

        // Show saved progress at every interface scale, concealing both name and description of hidden entries.
        void DrawAchievements()
        {
            float s = Scale;
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.3f));
            Rect r = Centered(Mathf.Min(760f, Screen.width / s - 32f), Mathf.Min(740f, Screen.height / s - 32f));
            Fill(r, ModalColor);
            Fill(new Rect(r.x, r.y, r.width, 3f * s), Teal);
            int earned = 0;
            foreach (var entry in Achievements.All) if (Achievements.Earned(entry.id)) earned++;
            GUI.Label(new Rect(r.x, r.y + 16f * s, r.width, 34f * s), "ACHIEVEMENTS",
                new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(r.x, r.y + 54f * s, r.width, 26f * s), earned + " / " + Achievements.All.Length + " earned", centered);

            var viewport = new Rect(r.x + 24f * s, r.y + 94f * s, r.width - 48f * s, r.height - 170f * s);
            float rowHeight = 86f * s;
            var content = new Rect(0f, 0f, viewport.width - 24f * s, Achievements.All.Length * rowHeight);
            achievementsScroll = GUI.BeginScrollView(viewport, achievementsScroll, content);
            for (int i = 0; i < Achievements.All.Length; i++)
            {
                var entry = Achievements.All[i];
                bool done = Achievements.Earned(entry.id), hidden = entry.hidden && !done;
                float y = i * rowHeight;
                Color colour = done ? Teal : small.normal.textColor;
                Fill(new Rect(0f, y, content.width, rowHeight - 6f * s), done ? new Color(0.2f, 0.8f, 0.7f, 0.12f) : new Color(1f, 1f, 1f, 0.03f));
                if (done) Fill(new Rect(0f, y, 3f * s, rowHeight - 6f * s), Teal);
                GUI.Label(new Rect(12f * s, y + 6f * s, content.width - 110f * s, 26f * s), hidden ? "???" : entry.name, Tinted(heading, colour));
                string status = done ? "Earned" : hidden ? "???" : "Unearned";
                if (!hidden && entry.target > 1) status = Achievements.Current(entry.id) + "/" + entry.target;
                GUI.Label(new Rect(content.width - 94f * s, y + 8f * s, 82f * s, 24f * s), status,
                    Tinted(new GUIStyle(small) { alignment = TextAnchor.MiddleRight }, colour));
                GUI.Label(new Rect(12f * s, y + 34f * s, content.width - 24f * s, 42f * s), hidden ? "???" : entry.description, Tinted(smallWrap, colour));
            }
            GUI.EndScrollView();

            bool escape = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (GUI.Button(new Rect(r.x + 24f * s, r.yMax - 60f * s, r.width - 48f * s, 42f * s), "<b>Back</b>  (Esc)", button) || escape)
            {
                if (escape) Event.current.Use();
                AchievementsOpen = false;
                GUI.FocusControl(null);
            }
        }

        // Give each queued award four visible seconds, below the feed and its reserved critical-alert strip.
        void DrawAchievementToast()
        {
            if (achievementNotice != null && Time.unscaledTime >= achievementUntil) achievementNotice = null;
            if (achievementNotice == null && achievementNotices.Count > 0)
            {
                achievementNotice = achievementNotices.Dequeue();
                achievementUntil = Time.unscaledTime + 4f;
            }
            if (achievementNotice == null) return;
            float s = Scale, y = 16f * s;
            if (game.Mode != GameMode.Menu)
            {
                y += 36f * s;
                for (int i = 0; i < game.Feed.Count; i++)
                    if (Time.unscaledTime <= game.Feed[i].until) y += (i == 0 ? 33f : 25f) * s;
            }
            float width = Mathf.Min(420f * s, Screen.width - 24f * s);
            var r = new Rect((Screen.width - width) / 2f, y, width, 32f * s);
            Fill(r, ModalColor);
            Fill(new Rect(r.x, r.y, 3f * s, r.height), Teal);
            GUI.Label(r, "Achievement: " + achievementNotice.name, Tinted(centered, Teal));
        }
    }
}
