using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DroneSandbox
{
    // Fits the sun's shadows to what the RTS camera can see. It looks down from 15-110 m away, so the farthest
    // ground in view is a couple of hundred metres off at most: the shadows end just past the top of the screen
    // instead of at the 350 m the photo views need. That skips drawing shadows nobody sees, and the last cascade,
    // which covers most of the ground in view, gets its resolution spread over less of it, so they're sharper
    // too. (Spreading the cascades over the visible ground instead of the default split cost more, as more of
    // them then have trees in them, and looked no better.) In photo mode this steps aside.
    public class ShadowFit : MonoBehaviour
    {
        RtsController rts;
        Volume volume;
        HDShadowSettings shadows;

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

        void LateUpdate()
        {
            if (volume == null) return;
            volume.weight = PhotoMode.Active ? 0f : 1f;
            // To the screen's top corners, with a margin.
            if (!PhotoMode.Active) shadows.maxShadowDistance.Override(Mathf.Clamp(rts.Distance * 1.8f + 70f, 120f, 350f));
        }
    }
}
