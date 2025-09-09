/*
 * Outline.cs (Unity 6 ready, MaterialPropertyBlock variant)
 * Based on QuickOutline by Chris Nolet - refactored for shared materials & per-object MPB.
 * 2025-05-29
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class Outline : MonoBehaviour
{
    public enum Mode
    {
        OutlineAll,
        OutlineVisible,
        OutlineHidden,
        OutlineAndSilhouette,
        SilhouetteOnly,
        HaloThrough
    }

    #region ---- Public API ----

    private Mode outlineMode = Mode.OutlineHidden;
    private Color outlineColor = Color.white;
    private float outlineWidth = 0f;

    public Mode OutlineMode
    {
        get => outlineMode;
        set { outlineMode = value; _needsUpdate = true; }
    }

    public Color OutlineColor
    {
        get => outlineColor;
        set { outlineColor = value; _needsUpdate = true; }
    }

    public float OutlineWidth
    {
        get => outlineWidth;
        set { outlineWidth = value; _needsUpdate = true; }
    }

    #endregion

    #region ---- Static shared data ----

    private static Material s_MaskMat;
    private static Material s_FillMat;
    private static readonly HashSet<Mesh> s_RegisteredMeshes = new();

    #endregion

    private Renderer[] _renderers;
    private bool _needsUpdate;
    private MaterialPropertyBlock _mpb;

    // smooth-normal bake support
    [Serializable] private class ListVector3 { public List<Vector3> data; }
    [SerializeField] private bool precomputeOutline = false;
    [SerializeField, HideInInspector] private List<Mesh> bakeKeys = new();
    [SerializeField, HideInInspector] private List<ListVector3> bakeValues = new();

    /*---- Unity events ----*/
    private void Awake()
    {
        EnsureSharedMaterials();
        _mpb = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>();

        LoadSmoothNormals();
        _needsUpdate = true;
    }

    private static void EnsureSharedMaterials()
    {
        if (s_MaskMat && s_FillMat) return;

        s_MaskMat = Instantiate(Resources.Load<Material>("Materials/OutlineMask"));
        s_MaskMat.name = "OutlineMask (Shared)";

        s_FillMat = Instantiate(Resources.Load<Material>("Materials/OutlineFill"));
        s_FillMat.name = "OutlineFill (Shared)";

        const int kOverlayQueue = 5000;          // ensure highlights are not overdrawn by other objects

        s_MaskMat.renderQueue = kOverlayQueue;
        s_FillMat.renderQueue = kOverlayQueue;
    }

    private void OnEnable()
    {
        foreach (var r in _renderers)
        {
            var mats = r.sharedMaterials.ToList();
            if (!mats.Contains(s_MaskMat)) mats.Add(s_MaskMat);
            if (!mats.Contains(s_FillMat)) mats.Add(s_FillMat);
            r.sharedMaterials = mats.ToArray();
        }
    }

    private void OnDisable()
    {
        foreach (var r in _renderers)
        {
            var mats = r.sharedMaterials.ToList();
            mats.Remove(s_MaskMat);
            mats.Remove(s_FillMat);
            r.sharedMaterials = mats.ToArray();
            r.SetPropertyBlock(null);
        }
    }

    private void Update()
    {
        if (_needsUpdate)
        {
            _needsUpdate = false;
            UpdateMaterialProperties();
        }
    }

    /*---- Material updates ----*/
    private void UpdateMaterialProperties()
    {
        // per-object settings via MPB
        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_OutlineColor", outlineColor);
            _mpb.SetFloat("_OutlineWidth", outlineWidth);
            r.SetPropertyBlock(_mpb);
        }

        var maskZ = (float)UnityEngine.Rendering.CompareFunction.Always;
        float fillZ;
        float fillWidth = outlineWidth;
        float maskWidth = outlineWidth;

        switch (outlineMode)
        {
            case Mode.OutlineAll:
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Always;
                break;
            case Mode.OutlineVisible:
                fillZ = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                break;
            case Mode.OutlineHidden:
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Greater;
                break;
            case Mode.OutlineAndSilhouette:
                maskZ = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Always;
                break;
            case Mode.SilhouetteOnly:
                maskZ = (float)UnityEngine.Rendering.CompareFunction.LessEqual;
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Greater;
                fillWidth = 0f;
                break;
            case Mode.HaloThrough:
                maskZ = (float)UnityEngine.Rendering.CompareFunction.Always;
                s_MaskMat.SetInt("_ZWrite", 0);
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Always;
                s_FillMat.SetInt("_ZWrite", 0);
                break;
            default:
                fillZ = (float)UnityEngine.Rendering.CompareFunction.Always;
                break;
        }

        s_MaskMat.SetFloat("_ZTest", maskZ);
        s_FillMat.SetFloat("_ZTest", fillZ);
        s_MaskMat.SetFloat("_OutlineWidth", maskWidth);
        s_FillMat.SetFloat("_OutlineWidth", fillWidth);
    }

    #region ---- Smooth-normal helpers (unchanged from QuickOutline) ----

    private void LoadSmoothNormals()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (!s_RegisteredMeshes.Add(mf.sharedMesh)) continue;
            var idx = bakeKeys.IndexOf(mf.sharedMesh);
            var smooth = idx >= 0 ? bakeValues[idx].data : SmoothNormals(mf.sharedMesh);
            mf.sharedMesh.SetUVs(3, smooth);
            CombineSubmeshes(mf.sharedMesh, mf.GetComponent<Renderer>().sharedMaterials);
        }

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!s_RegisteredMeshes.Add(smr.sharedMesh)) continue;
            smr.sharedMesh.uv4 = new Vector2[smr.sharedMesh.vertexCount];
            CombineSubmeshes(smr.sharedMesh, smr.sharedMaterials);
        }
    }

    private static List<Vector3> SmoothNormals(Mesh mesh)
    {
        var groups = mesh.vertices
            .Select((v, i) => new KeyValuePair<Vector3, int>(v, i))
            .GroupBy(p => p.Key);

        var smooth = new List<Vector3>(mesh.normals);
        foreach (var g in groups)
        {
            if (g.Count() == 1) continue;
            var avg = Vector3.zero;
            foreach (var p in g) avg += smooth[p.Value];
            avg.Normalize();
            foreach (var p in g) smooth[p.Value] = avg;
        }
        return smooth;
    }

    private static void CombineSubmeshes(Mesh mesh, Material[] mats)
    {
        if (mesh.subMeshCount == 1 || mesh.subMeshCount > mats.Length) return;
        mesh.subMeshCount++;
        mesh.SetTriangles(mesh.triangles, mesh.subMeshCount - 1);
    }

    #endregion
}