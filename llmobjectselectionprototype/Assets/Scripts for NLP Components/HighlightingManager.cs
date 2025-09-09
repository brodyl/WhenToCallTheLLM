using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class HighlightingManager : MonoBehaviour
{
    private static HighlightingManager _instance;
    public static HighlightingManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Look for one that is already alive first
                _instance = FindFirstObjectByType<HighlightingManager>();

                // If still null create one automatically.
                // Safer: just warn so the dev knows the singleton is missing.
                if (_instance == null)
                {
                    Debug.LogError(
                        "No SelectionControllerVer3 in the scene. " +
                        "Add one to a GameObject before accessing Instance."
                    );
                }
            }

            return _instance;
        }
    }


    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning(
                $"Duplicate HighlightingManager on {gameObject.name}. Destroying this copy."
            );
            Destroy(gameObject);
            return;
        }

        _instance = this;
        //DontDestroyOnLoad(gameObject);   // This is if we need it in other scenes, but our gaurd will prevent dups anyways...
    }



    [SerializeField] private float outlineWidth = 4f;
    [SerializeField] public Color outlineColor = Color.cyan;

    private readonly HashSet<Highlightable> _active = new();   // track multiple highlighted objects

    public void AddHighlight(Highlightable h)
    {
        if (h == null || _active.Contains(h)) return;
        _active.Add(h);
        Show(h);
    }

    public void RemoveHighlight(Highlightable h)
    {
        if (h == null || !_active.Remove(h)) return;
        Hide(h);
    }

    public void ClearHighlights()
    {
        foreach (var h in _active) Hide(h);
        _active.Clear();
    }

    private void Show(Highlightable h)
    {
        foreach (var o in h.Outlines)
        {
            // Bring the passes back
            if (!o.enabled) o.enabled = true;

            o.OutlineColor = outlineColor;
            o.OutlineWidth = outlineWidth;
            o.OutlineMode = Outline.Mode.OutlineAll;
        }
    }

    private void Hide(Highlightable h)
    {
        foreach (var o in h.Outlines)
        {
            o.enabled = false;
            o.OutlineWidth = 0f;
            o.OutlineMode = Outline.Mode.OutlineHidden;
        }
    }
}
