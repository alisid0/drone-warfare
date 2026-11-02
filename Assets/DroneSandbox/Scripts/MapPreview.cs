using UnityEngine;

namespace DroneSandbox
{
    // Shows the map in the editor's Scene view without pressing Play. The map is built from code, so the scene
    // itself holds almost nothing; this builds a copy under itself that is never saved, whenever the scene opens
    // or scripts recompile. In Play mode it does nothing (SandboxGame builds the real map).
    // Drone Sandbox > Configure Project adds it to the main scene. Untick "Show Map" to hide the preview.
    [ExecuteAlways]
    public class MapPreview : MonoBehaviour
    {
        const string PreviewName = "Map Preview (not saved)";

        public bool showMap = true;
        bool pending;

        void OnEnable()
        {
            if (Application.isPlaying)
            {
                Clear();
                return;
            }
#if UNITY_EDITOR
            if (Application.isBatchMode) return;
            Schedule();
#endif
        }

        void OnDisable() => Clear();

        void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && isActiveAndEnabled) Schedule();
#endif
        }

        // Building in the middle of a scene load or recompile isn't allowed; wait for the editor to settle.
        void Schedule()
        {
#if UNITY_EDITOR
            if (pending) return;
            pending = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                pending = false;
                Rebuild();
            };
#endif
        }

        void Rebuild()
        {
#if UNITY_EDITOR
            if (this == null || !isActiveAndEnabled || Application.isPlaying || Application.isBatchMode
                || UnityEditor.BuildPipeline.isBuildingPlayer) return;
            Clear();
            if (!showMap) return;
            var root = new GameObject(PreviewName);
            root.transform.SetParent(transform, false);
            World.Build(root.transform, preview: true);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            if (Ground.Current != null) Ground.Current.terrainData.hideFlags = HideFlags.DontSave;
#endif
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != PreviewName) continue;
#if UNITY_EDITOR
                // Terrain data, meshes and materials made in code outlive their objects; free them too, or every
                // rebuild (each recompile) would leak a few megabytes.
                foreach (var terrain in child.GetComponentsInChildren<Terrain>(true)) Art.Discard(terrain.terrainData);
                foreach (var filter in child.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && !UnityEditor.AssetDatabase.Contains(filter.sharedMesh)) Art.Discard(filter.sharedMesh);
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && !UnityEditor.AssetDatabase.Contains(material)) Art.Discard(material);
#endif
                Art.Discard(child.gameObject);
            }
        }
    }
}
