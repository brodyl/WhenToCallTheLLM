using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;                          // for prefab-saving utilities
#endif

[ExecuteAlways]                             // run in Edit Mode too
[DisallowMultipleComponent]
public class Highlightable : MonoBehaviour
{
    public IReadOnlyList<Outline> Outlines => _outlines;
    [SerializeField] private bool includeSelfIfRenderer = true;
    [SerializeField] public bool autoAddCollider = true;   // toggle if desired

    private readonly List<Outline> _outlines = new();
    
    // ------------------------------------------------------------
    //                  Outline set-up (unchanged)
    // ------------------------------------------------------------
    private void Awake() => BuildOutlines();
#if UNITY_EDITOR
    private void OnValidate() => BuildOutlines();                // updates live in editor
#endif

    private void BuildOutlines()
    {
        _outlines.Clear();

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == GetComponent<Renderer>() && !includeSelfIfRenderer) continue;
            if (!r.TryGetComponent(out Outline o))
                o = r.gameObject.AddComponent<Outline>();

            o.OutlineMode = Outline.Mode.OutlineHidden;
            o.OutlineWidth = 0f;
            o.enabled = false;
            _outlines.Add(o);
        }

        if (autoAddCollider)
            EnsureCollider();
    }
    
    // ------------------------------------------------------------
    //                      Collider helper
    // ------------------------------------------------------------
    private void EnsureCollider()
    {
        if (TryGetComponent<Collider>(out _)) return;            // we're good

        // Try a MeshCollider first
        if (TryAddMeshCollider()) return;

        // Fallback: box sized to renderer bounds
        var rend = GetComponentInChildren<Renderer>();
        if (!rend) return;

        var box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = transform.InverseTransformPoint(rend.bounds.center);
        box.size = rend.bounds.size;
    }

    private bool TryAddMeshCollider()
    {
        var mf = GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return false;

        var mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        mc.convex = false;      // highest accuracy; not a Trigger
        return true;
    }
}
