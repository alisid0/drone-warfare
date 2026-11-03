using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // The live video feed from a selected drone's own camera, in a window at the bottom right (V hides and shows
    // it): what the drone sees, looking ahead and down, with its telemetry over the picture (SandboxHud.DrawFeed).
    // A diving Kamikaze's feed rides the dive to the end, then shows the static of a lost signal. The feed
    // renders small (384 x 216), every other frame, without shadows, clouds, fog volumes or screen-space
    // effects, so it costs little.
    public class DroneFeed : MonoBehaviour
    {
        public const int Width = 384, Height = 216;
        public static DroneFeed Instance { get; private set; }

        public bool Visible = true;            // V toggles
        public Drone Source { get; private set; }
        public RenderTexture Picture { get; private set; }
        public float LostAt { get; private set; } = -100f; // when the drone we were watching went (static for a moment)
        public string LostName { get; private set; }

        Camera feed;
        RtsController rts;

        public static DroneFeed Create(GameObject host, RtsController controller)
        {
            var f = host.AddComponent<DroneFeed>();
            f.rts = controller;
            Instance = f;
            return f;
        }

        void Start()
        {
            Picture = new RenderTexture(Width, Height, 24) { name = "Drone Feed" };
            var go = new GameObject("Drone Feed Camera");
            feed = go.AddComponent<Camera>();
            feed.fieldOfView = 48f;
            feed.nearClipPlane = 0.2f;
            feed.farClipPlane = 600f;
            feed.targetTexture = Picture;
            feed.enabled = false;
            var data = go.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing;
            data.customRenderingSettings = true;
            foreach (var field in new[] { FrameSettingsField.VolumetricClouds, FrameSettingsField.Volumetrics, FrameSettingsField.SSR,
                         FrameSettingsField.SSAO, FrameSettingsField.ContactShadows, FrameSettingsField.SSGI, FrameSettingsField.Water,
                         FrameSettingsField.MotionVectors, FrameSettingsField.Decals, FrameSettingsField.ShadowMaps })
            {
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
            }
        }

        void LateUpdate()
        {
            var game = SandboxGame.Instance;
            // Watch the first selected drone; a kamikaze that's diving keeps the feed even if it isn't selected now.
            Drone pick = null;
            if (rts.selected.Count > 0) pick = rts.selected[0];
            if (Source != null && Source.IsKamikaze && Source.AttackTarget != null) pick = Source;
            Source = pick;
            bool on = Visible && Source != null && !PhotoMode.Active && game != null && !game.UiBlocking;
            feed.enabled = on && Time.frameCount % 2 == 0; // the picture keeps between renders
            if (!on) return;

            // Looking ahead and down from just under the drone, the way it's facing (a Kamikaze looks along its dive).
            Transform t = Source.transform;
            Vector3 eye = t.position + Vector3.down * 0.4f + t.forward * 0.6f;
            Vector3 look = Source.IsKamikaze && Source.AttackTarget != null
                ? (Source.AttackTarget.AimPoint - eye).normalized
                : (Quaternion.AngleAxis(38f, t.right) * t.forward).normalized;
            feed.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look, Vector3.up));
        }

        // The drone we were watching is gone (shot down or detonated): show static for a moment.
        public void SignalLost(Drone drone)
        {
            if (drone != Source) return;
            LostAt = Time.unscaledTime;
            LostName = drone.unitName;
        }

        void OnDestroy()
        {
            if (Picture != null) Picture.Release();
        }
    }
}
