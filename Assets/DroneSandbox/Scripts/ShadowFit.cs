using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Fits the sun's shadows to the high RTS camera's view, with four cascades from Atmosphere. Distance is
    // already scaled by the camera controller: the bounded fit reaches the far ground at 300-640 m zoom without
    // a 1.5 km shadow pass. Photo mode uses the environment profile instead.
    public class ShadowFit : MonoBehaviour
    {
        RtsController rts;
        Volume volume;
        HDShadowSettings shadows;

        // Creates a distance-only override; cascade count and splits come from the environment profile.
        public void Init(RtsController controller)
        {
            rts = controller;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            shadows = profile.Add<HDShadowSettings>(); // overrides only the distance
            volume = new GameObject("Shadow Fit").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.sharedProfile = profile;
        }

        // Keeps the last cascade just beyond the visible ground as the camera zooms.
        void LateUpdate()
        {
            if (volume == null) return;
            volume.weight = PhotoMode.Active ? 0f : 1f;
            // To the screen's top corners, with a margin.
            if (!PhotoMode.Active) shadows.maxShadowDistance.Override(Mathf.Clamp(rts.Distance * 1.35f + 80f, 200f, 1000f));
        }
    }
}
