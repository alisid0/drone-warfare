using UnityEngine;

namespace DroneSandbox
{
    // Starts the sandbox automatically when you press Play in any scene, so there is
    // no scene, prefab or asset to set up. Delete this file once the game has real scenes.
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Object.FindAnyObjectByType<SandboxGame>() != null) return;
            new GameObject("Drone Sandbox").AddComponent<SandboxGame>();
        }
    }
}
