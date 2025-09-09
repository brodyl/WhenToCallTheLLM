/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static UnityEditor.PlayerSettings;

/// <summary>
/// Central registry + evaluators for all supported descriptive relations.
/// Each evaluator receives the candidate main objects and
/// returns the mains that satisfy <paramref name="relation"/>.
/// </summary>
public static class DescriptiveRelationEvaluator
{
    // Delegate type every rule must follow.
    private delegate List<GameObject> DescriptiveEval(
        List<GameObject> mains, List<GameObject> related = null);

    // ------------------------------------------------------------
    // Registry -- map keyword -> evaluator
    // ------------------------------------------------------------
    private static readonly Dictionary<string, DescriptiveEval> _rules =
    new(StringComparer.OrdinalIgnoreCase)
    {
        // Edge
        { "left",               (m,r) => EvalEdge(m, Edge.Left) },
        { "leftmost",           (m,r) => EvalEdge(m, Edge.Left) },
        { "furthest left",      (m,r) => EvalEdge(m, Edge.Left) },
        { "most left",          (m,r) => EvalEdge(m, Edge.Left) },
        { "right",              (m,r) => EvalEdge(m, Edge.Right) },
        { "rightmost",          (m,r) => EvalEdge(m, Edge.Right) },
        { "furthest right",     (m,r) => EvalEdge(m, Edge.Right) },
        { "most right",         (m,r) => EvalEdge(m, Edge.Right) },
        { "top",                (m,r) => EvalEdge(m, Edge.Top) },
        { "topmost",            (m,r) => EvalEdge(m, Edge.Top) },
        { "most high",          (m,r) => EvalEdge(m, Edge.Top) },
        { "upper",              (m,r) => EvalEdge(m, Edge.Top) },
        { "uppermost",          (m,r) => EvalEdge(m, Edge.Top) },
        { "bottom",             (m,r) => EvalEdge(m, Edge.Bottom) },
        { "bottommost",         (m,r) => EvalEdge(m, Edge.Bottom) },
        { "most bottom",        (m,r) => EvalEdge(m, Edge.Bottom) },
        { "lower",              (m,r) => EvalEdge(m, Edge.Bottom) },
        { "lowest",             (m,r) => EvalEdge(m, Edge.Bottom) },
        { "most low",           (m,r) => EvalEdge(m, Edge.Bottom) },

        // Centre/Middle
        { "middle",             (m,r) => EvalMiddle(m) },
        { "center",             (m,r) => EvalMiddle(m) },
        { "centre",             (m,r) => EvalMiddle(m) },

        // Extrema
        { "large",              (m,r) => EvalLargest(m) },
        { "largest",            (m,r) => EvalLargest(m) },
        { "most large",         (m,r) => EvalLargest(m) },
        { "most big",           (m,r) => EvalLargest(m) },
        { "smallest",           (m,r) => EvalSmallest(m) },
        { "small",              (m,r) => EvalSmallest(m) },
        { "most small",         (m,r) => EvalSmallest(m) },
        { "tiny",               (m,r) => EvalSmallest(m) },
        { "most tiny",          (m,r) => EvalSmallest(m) },
        { "petite",             (m,r) => EvalSmallest(m) },
        { "most petite",        (m,r) => EvalSmallest(m) },
        { "tall",               (m,r) => EvalTallest(m) },
        { "tallest",            (m,r) => EvalTallest(m) },
        { "most tall",          (m,r) => EvalTallest(m) },
        { "short",              (m,r) => EvalShortest(m) },
        { "shortest",           (m,r) => EvalShortest(m) },
        { "most short",         (m,r) => EvalShortest(m) },
        { "widest",             (m,r) => EvalWidest(m) },
        { "wide",               (m,r) => EvalWidest(m) },
        { "most wide",          (m,r) => EvalWidest(m) },

        // Proximity
        { "close",              (m,r) => EvalProximity(m, r, true) },
        { "closest",            (m,r) => EvalProximity(m, r, true) },
        { "nearest",            (m,r) => EvalProximity(m, r, true) },
        { "most near",          (m,r) => EvalProximity(m, r, true) },
        { "furthest",           (m,r) => EvalProximity(m, r, false) },
        { "far",                (m,r) => EvalProximity(m, r, false) },
        { "farthest",           (m,r) => EvalProximity(m, r, false) },
        { "most far",           (m,r) => EvalProximity(m, r, false) },

        // Quantitative
        //{ "most red",           (m,r) => EvalMostColor(m, Color.red) },
        //{ "reddest",            (m,r) => EvalMostColor(m, Color.red) },
        //{ "brightest",          (m,r) => EvalBrightest(m) },
        { "empty",              (m,r) => EvalEmpty(m) },

        // Fractional
        { "left half",       (m,r) => EvalFraction(m, 0f, 0.5f) },
        { "right half",      (m,r) => EvalFraction(m, 0.5f, 1f) },

        // (Ordinals handled dynamically in Evaluate)
};

    /// <summary>
    /// Public entry point. Returns the set of mains that fulfil the relation.
    /// An empty list == "no matches". Null == none evaluated.
    /// </summary>
    public static List<GameObject> Evaluate(
        List<string> descriptors,
        List<GameObject> mains,
        List<GameObject> related = null)        // Most relations don't use this as the are self-referential
    {
        foreach (var desc in descriptors)
        {
            if (string.IsNullOrWhiteSpace(desc))
                continue;

            // 1) Direct registry lookup
            if (_rules.TryGetValue(desc, out var eval))
                return eval(mains, related);

            // 2) Dynamic ordinal parser: e.g. "4th", "second", "third from the right"
            var ordMatch = Regex.Match(desc,
              @"^(?<ord>\d+)(st|nd|rd|th)\s*(from\s+the\s+(?<dir>left|right))?$",
              RegexOptions.IgnoreCase);
            if (!ordMatch.Success)
            {
                // also spelled-out words: first, second, third
                var wordOrd = ParseOrdinalWord(desc, out bool hasDir, out bool fromLeft);
                if (wordOrd > 0)
                {
                    // if phrase contains "from the right", override direction:
                    if (hasDir && !fromLeft) fromLeft = false;
                    return EvalNth(mains, wordOrd, fromLeft);
                }
            }
            else
            {
                int n = int.Parse(ordMatch.Groups["ord"].Value);
                bool fromLeft = !ordMatch.Groups["dir"].Success ||
                                ordMatch.Groups["dir"].Value.Equals("left", StringComparison.OrdinalIgnoreCase);
                return EvalNth(mains, n, fromLeft);
            }
        }

        Debug.LogWarning("[DescriptiveRelationEvaluator] no valid descriptor matched.");
        return new List<GameObject>();
    }


    // ------------------------------------------------------------
    // ------------------------------------------------------------
    //                         Helpers
    // ------------------------------------------------------------
    // ------------------------------------------------------------

    private static float GetBoundsVolume(GameObject go)
    {
        if (go == null) return 0f;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 size = bounds.size;
        return size.x * size.y * size.z;
    }


    // ------------------------------------------------------------
    // ------------------------------------------------------------
    //              Comparative Relation Evaluators
    // ------------------------------------------------------------
    // ------------------------------------------------------------


    // ------------------------------------------------------------
    // CONFIGS
    // ------------------------------------------------------------



    //------------------ ENUMS / TINY UTILS ------------------
    private enum Edge { Left, Right, Top, Bottom }

    private static Vector3 ViewRight =>
        Camera.main ? Camera.main.transform.right : Vector3.right;
    private static Vector3 ViewUp =>
        Camera.main ? Camera.main.transform.up : Vector3.up;

    private static Vector3 CenterOf(GameObject go)
    {
        if (!go) return Vector3.zero;
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return go.transform.position;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.center;
    }

    /// <summary>
    /// Calculates the world-space axis-aligned bounding box (AABB) of the given GameObject, 
    /// including all Renderer components in its children, regardless of hierarchy depth.
    /// </summary>
    /// <param name="go">The root GameObject whose bounds are to be calculated.</param>
    /// <returns>A tuple containing the minimum and maximum world-space coordinates of the AABB.</returns>
    private static (Vector3 min, Vector3 max) GetWorldSpaceAABB(GameObject go)
    {
        if (go == null) return (Vector3.zero, Vector3.zero);

        // Get all renderers in this object and its children
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return (Vector3.zero, Vector3.zero); // No visible mesh
        }

        // Initialize with the bounds of the first renderer
        Bounds combinedBounds = renderers[0].bounds;

        // Expand bounds to include all child renderers
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        return (combinedBounds.min, combinedBounds.max);
    }


    private static bool TryGetHierarchyBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;

        // Prefer colliders (they already match the physics broad-phase).
        Collider[] cols = go.GetComponentsInChildren<Collider>();
        if (cols.Length > 0)
        {
            bounds = cols[0].bounds;
            for (int i = 1; i < cols.Length; ++i)
                bounds.Encapsulate(cols[i].bounds);
            return true;
        }

        // Fallback: use renderers so we still get something.
        Renderer[] rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; ++i)
                bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        return false;                // nothing visible / collidable
    }



    //------------------ EDGE-to-EDGE DISTANCE ------------------
    private static float AabbGap((Vector3 min, Vector3 max) a,
                                 (Vector3 min, Vector3 max) b)
    {
        // Gap along each axis (0 if overlapping)
        float dx = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float dy = Mathf.Max(0, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        float dz = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    //------------------ NEAREST-NEIGHBOUR DIST LIST ------------------
    private static List<float> ComputeNearestGaps(List<GameObject> objs)
    {
        int n = objs.Count;
        var gaps = new List<float>(n);

        for (int i = 0; i < n; i++)
        {
            float best = float.MaxValue;
            var aabbI = GetWorldSpaceAABB(objs[i]);

            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var gap = AabbGap(aabbI, GetWorldSpaceAABB(objs[j]));
                if (gap < best) best = gap;
            }
            gaps.Add(best);
        }
        return gaps;
    }


    //------------------ CLUSTERING ------------------
    /* No manual parameter; adapts to shelves, rows, or scattered groups.	O(N^2) pair-wise checks; fine for dozens, but not hundreds of objects per query.
    Handles mixed object sizes via edge-gap metric.	If gaps form a perfect arithmetic progression (rare), elbow detection may pick a large epsilon. Clamp caps it at 5 m.
    Identifies touching/overlapping items as one cluster (gap = 0).	If clusters overlap (two rows within epsilon), they'll merge. This is inherent to single-link.

    If you need industrial-scale grouping (1000+ items) or sophisticated shape
    analysis, switch to DBSCAN or HDBSCAN offline, cache the cluster IDs,
    then reuse them at runtime.*/
    private static List<List<GameObject>> ClusterByDistance(
    List<GameObject> items)
    {
        const float MinEps = 0.01f;   // 1 cm - lower bound
        const float MaxEps = 3.0f;    // 5 m  - sanity cap
        if (items.Count == 0) return new();

        /* Build nearest-neighbour gap array */
    var nnGaps = ComputeNearestGaps(items);          // O(N^2)
        nnGaps.Sort();                                   // O(N log N)

        /* Detect "elbow" - largest multiplicative jump */
        float eps = MaxEps;
        float bestRatio = 1f;
        for (int i = 1; i < nnGaps.Count; i++)
        {
            float ratio = nnGaps[i] / Mathf.Max(nnGaps[i - 1], 1e-4f);
            if (ratio > bestRatio && nnGaps[i] > MinEps)
            {
                bestRatio = ratio;
                eps = Mathf.Clamp((nnGaps[i] + nnGaps[i - 1]) * 0.5f, MinEps, MaxEps);
            }
        }
        // Fallback if list is monotonic (objects evenly spaced)
        eps = Mathf.Clamp(eps, MinEps, MaxEps);

        /* Single-link flood-fill with chosen epsilon */
        var clusters = new List<List<GameObject>>();
        var unvisited = new HashSet<GameObject>(items);

        while (unvisited.Count > 0)
        {
            var seed = unvisited.First();
            unvisited.Remove(seed);

            var cluster = new List<GameObject> { seed };
            var queue = new Queue<GameObject>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var aabbCur = GetWorldSpaceAABB(cur);

                foreach (var other in unvisited.ToArray())
                {
                    if (AabbGap(aabbCur, GetWorldSpaceAABB(other)) <= eps)
                    {
                        unvisited.Remove(other);
                        cluster.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }
            clusters.Add(cluster);
            int counter = 0;
            HighlightingManager.Instance.outlineColor = new Color(0.5f, 0.5f, 1f, 0.8f); // Set a distinct color for debugging
            foreach (var singleCluster in clusters)
            {
                // HIGHLIGHT POSSIBLE RELATED OBJECTS FOR THIS RELATIONSHIP AND YIELD FOR DEBUGGING PURPOSES
                foreach (GameObject obj in singleCluster)
                {
                    Highlightable highlightableRelMain = obj.GetComponent<Highlightable>();
                    HighlightingManager.Instance.AddHighlight(highlightableRelMain);
                }
                // pick  a new random highlight color for the next cluster
                HighlightingManager.Instance.outlineColor = new Color(
                    UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 0.8f);
                MyLogger.Log("CLUSTER " + counter);
                counter++;
            }
        }

        /* Optional: debug */
        // Debug.Log($"[Cluster] auto-eps = {eps:F2}  clusters = {clusters.Count}");
        return clusters;
    }

    private static List<List<GameObject>> ClusterByDistanceMagicNumbers(
        List<GameObject> items, float maxDist)
    {
        var clusters = new List<List<GameObject>>();
        var unvisited = new HashSet<GameObject>(items);

        while (unvisited.Count > 0)
        {
            var seed = unvisited.First();
            unvisited.Remove(seed);

            var cluster = new List<GameObject> { seed };
            var queue = new Queue<GameObject>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                Vector3 cPos = CenterOf(current);

                foreach (var other in unvisited.ToArray())
                {
                    if (Vector3.Distance(cPos, CenterOf(other)) <= maxDist)
                    {
                        unvisited.Remove(other);
                        cluster.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }
            clusters.Add(cluster);
        }
        return clusters;
    }

    //------------------ EVALUATORS ------------------

    /// <summary>leftmost / rightmost</summary>
    private static List<GameObject> EvalEdge(
         List<GameObject> mains, Edge whichEdge)
    {
        var result = new List<GameObject>();
        Vector3 axis;
        bool findMin;

        switch (whichEdge)
        {
            case Edge.Left: axis = ViewRight; findMin = true; break;
            case Edge.Right: axis = ViewRight; findMin = false; break;
            case Edge.Bottom: axis = ViewUp; findMin = true; break;
            case Edge.Top: axis = ViewUp; findMin = false; break;
            default: axis = ViewRight; findMin = true; break;
        }
        axis = axis.normalized;

        float best = findMin ? float.MaxValue : float.MinValue;
        GameObject pick = null;

        foreach (var go in mains)
        {
            float proj = Vector3.Dot(CenterOf(go), axis);
            if ((findMin && proj < best) || (!findMin && proj > best))
            {
                best = proj;
                pick = go;
            }
        }
        if (pick != null) result.Add(pick);
        return result;
    }

    // -------------- EXAMPLE OF CLUSTERING GROUPS OF SIMILAR OBJECTS --------------------
    ///// <summary>middle / center</summary>
    //private static List<GameObject> EvalMiddle(List<GameObject> mains)
    //{
    //    var valid = new List<GameObject>();
    //    var clusters = DistanceClustering.ClusterByDistance(mains);
    //    Vector3 axis = ViewRight.normalized;

    //    foreach (var group in clusters)
    //    {
    //        var sorted = group.OrderBy(g => Vector3.Dot(CenterOf(g), axis)).ToList();
    //        int count = sorted.Count;
    //        if (count == 0) continue;

    //        if (count % 2 == 1)       // odd => single middle
    //        {
    //            valid.Add(sorted[count / 2]);
    //        }
    //        else                      // even => two centres
    //        {
    //            valid.Add(sorted[count / 2 - 1]);
    //            valid.Add(sorted[count / 2]);
    //        }
    //    }
    //    return valid.Distinct().ToList();
    //}

    /// <summary>middle / center</summary>
    private static List<GameObject> EvalMiddle(List<GameObject> mains)
    {
        var valid = new List<GameObject>();
        Vector3 axis = ViewRight.normalized;

        var sorted = mains.OrderBy(g => Vector3.Dot(CenterOf(g), axis)).ToList();
        int count = sorted.Count;

        if (count % 2 == 1)       // odd => single middle
        {
            valid.Add(sorted[count / 2]);
        }
        else                      // even => two centres
        {
            valid.Add(sorted[count / 2 - 1]);
            valid.Add(sorted[count / 2]);
        }
        return valid.Distinct().ToList();
    }

    /// <summary>nth from left / right</summary>
    private static List<GameObject> EvalNth(
        List<GameObject> mains, int n, bool fromLeft)
    {
        var valid = new List<GameObject>();
        if (n <= 0) return valid;

        Vector3 axis = ViewRight.normalized;

        var sorted = mains.OrderBy(g => Vector3.Dot(CenterOf(g), axis)).ToList();
        if (!fromLeft) sorted.Reverse();               // now "left" is desired edge

        if (sorted.Count >= n)
            valid.Add(sorted[n - 1]);

        return valid;
    }

    //------------------ EXISTING EvalLargest stays here ------------------
    //private static List<GameObject> EvalLargest(List<GameObject> mains)
    //{
    //    if (mains == null || mains.Count == 0) return new();

    //    float maxVol = float.MinValue;
    //    var largest = new List<GameObject>();

    //    foreach (var go in mains)
    //    {
    //        float v = GetBoundsVolume(go);
    //        if (v > maxVol)
    //        {
    //            maxVol = v;
    //            largest.Clear();
    //            largest.Add(go);
    //        }
    //        else if (Mathf.Approximately(v, maxVol))
    //        {
    //            largest.Add(go);
    //        }
    //    }
    //    return largest;
    //}

    private static List<GameObject> EvalLargest(List<GameObject> mains)
    {
        float maxV = mains.Select(GetBoundsVolume).DefaultIfEmpty(0).Max();
        return mains.Where(g => Mathf.Approximately(GetBoundsVolume(g), maxV))
                    .ToList();
    }

    private static List<GameObject> EvalSmallest(List<GameObject> mains)
    {
        float minV = mains.Select(GetBoundsVolume).DefaultIfEmpty(0).Min();
        return mains.Where(g => Mathf.Approximately(GetBoundsVolume(g), minV))
                    .ToList();
    }

    private static List<GameObject> EvalTallest(List<GameObject> mains)
    {
        float maxH = mains.Select(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return max.y - min.y;
        }).DefaultIfEmpty(0).Max();
        return mains.Where(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return Mathf.Approximately(max.y - min.y, maxH);
        }).ToList();
    }

    private static List<GameObject> EvalShortest(List<GameObject> mains)
    {
        float minH = mains.Select(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return max.y - min.y;
        }).DefaultIfEmpty(0).Min();
        return mains.Where(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return Mathf.Approximately(max.y - min.y, minH);
        }).ToList();
    }

    private static List<GameObject> EvalWidest(List<GameObject> mains)
    {
        float maxW = mains.Select(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return max.x - min.x;
        }).DefaultIfEmpty(0).Max();
        return mains.Where(g => {
            var (min, max) = GetWorldSpaceAABB(g);
            return Mathf.Approximately(max.x - min.x, maxW);
        }).ToList();
    }

    // ------------------------------------------------------------
    //                    Proximity Evaluators
    // ------------------------------------------------------------
    private static List<GameObject> EvalProximity(
        List<GameObject> mains,
        List<GameObject> related,
        bool closest)
    {
        Vector3 reference = Camera.main ? Camera.main.transform.position : Vector3.zero;
        if (related != null && related.Count > 0)
            reference = CenterOf(related[0]);

        float best = closest ? float.MaxValue : float.MinValue;
        GameObject pick = null;

        foreach (var go in mains)
        {
            float d = Vector3.Distance(CenterOf(go), reference);
            if ((closest && d < best) || (!closest && d > best))
            {
                best = d;
                pick = go;
            }
        }
        return pick != null ? new() { pick } : new();
    }

    // ------------------------------------------------------------
    //                  Quantitative Scorers
    // ------------------------------------------------------------
    private static List<GameObject> EvalMostColor(
        List<GameObject> mains, Color target)
    {
        float bestScore = float.MinValue;
        var winners = new List<GameObject>();

        foreach (var go in mains)
        {
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend == null || !rend.material.HasProperty("_Color")) continue;
            Color c = rend.material.color;
            // simple "redness" = red channel minus green/blue
            float score = c.r - (c.g + c.b) * 0.5f;
            if (score > bestScore + 1e-6f)
            {
                bestScore = score;
                winners.Clear();
                winners.Add(go);
            }
            else if (Mathf.Approximately(score, bestScore))
                winners.Add(go);
        }
        return winners;
    }

    private static List<GameObject> EvalBrightest(List<GameObject> mains)
    {
        float best = float.MinValue;
        var winners = new List<GameObject>();

        foreach (var go in mains)
        {
            var light = go.GetComponent<Light>();
            if (light == null) continue;
            float intensity = light.intensity;
            if (intensity > best + 1e-6f)
            {
                best = intensity;
                winners.Clear();
                winners.Add(go);
            }
            else if (Mathf.Approximately(intensity, best))
                winners.Add(go);
        }
        return winners;
    }


    // Returns true if 'child' is the container itself or any of its descendants
    static bool IsSelfOrChild(Transform child, Transform container)
    {
        return child == container || child.IsChildOf(container);
    }

    static readonly Collider[] _scratch = new Collider[32];
    static bool HasForeignContent(GameObject container,
                              int contentsMask = ~0,
                              float epsilon = 0.0001f,
                              bool debug = false)
    {
        if (!TryGetHierarchyBounds(container, out Bounds canBounds))
            return false;
        
        int hitCount = Physics.OverlapBoxNonAlloc(
                           canBounds.center, canBounds.extents,
                           _scratch, Quaternion.identity,
                           contentsMask, QueryTriggerInteraction.Ignore);

        Transform canRoot = container.transform;

        if (debug)
            Debug.Log($"[Check] '{container.name}' saw {hitCount} raw hits");

        Transform canXform = container.transform;

        for (int i = 0; i < hitCount; ++i)
        {
            Collider col = _scratch[i];

            /* 1 ? skip colliders that are part of THIS trash-can prefab                */
            if (IsSelfOrChild(col.transform, canXform))
            {
                if (debug) Debug.Log($"  . Skipped self/child collider: '{col.name}'");
                continue;
            }

            /* 2 ? full-containment check, as before                                    */
            Bounds b = col.bounds;
            bool fullyInside =
                canBounds.Contains(b.min + Vector3.one * epsilon) &&
                canBounds.Contains(b.max - Vector3.one * epsilon);

            if (debug)
                Debug.Log($"  . Hit '{col.name}' (fullyInside={fullyInside})");

            if (!fullyInside) continue;

            return true;            // found a foreign object completely inside
        }

        return false;               // nothing but air
    }



    /// <summary>
    /// Returns all objects in <paramref name="mains"/> that do **not** enclose
    /// any other collider except their own hierarchy.
    /// </summary>
    private static List<GameObject> EvalEmpty(List<GameObject> mains, int contentsLayerMask = ~0)
    {
        var empties = new List<GameObject>();

        foreach (var main in mains)
        {
            bool isEmpty = !HasForeignContent(main,
                                  contentsMask: ~0,
                                  debug: true);          // turn on logging
            if (isEmpty)
                empties.Add(main);
        }

        return empties;
    }

    // ------------------------------------------------------------
    //                  Fractional Splitting
    // ------------------------------------------------------------
    private static List<GameObject> EvalFraction(
        List<GameObject> mains,
        float startFrac, float endFrac)
    {
        int c = mains.Count;
        if (c == 0) return new();

        // sort along camera right-axis
        var sorted = mains
            .OrderBy(g => Vector3.Dot(CenterOf(g), ViewRight))
            .ToList();

        int startIndex = Mathf.FloorToInt(startFrac * c);
        int endIndex = Mathf.CeilToInt(endFrac * c) - 1;
        startIndex = Mathf.Clamp(startIndex, 0, c - 1);
        endIndex = Mathf.Clamp(endIndex, 0, c - 1);

        return sorted.GetRange(startIndex, endIndex - startIndex + 1);
    }

    // ------------------------------------------------------------
    //                  Ordinal Word Parser
    // ------------------------------------------------------------
    private static readonly Dictionary<string, int> _wordOrdinals =
        new(StringComparer.OrdinalIgnoreCase)
    {
            {"first",1},{"second",2},{"third",3},{"fourth",4},{"fifth",5},
            {"sixth",6},{"seventh",7},{"eighth",8},{"ninth",9},{"tenth",10}
    };

    /// <summary>
    /// If desc contains a spelled-out ordinal, returns its number.
    /// Also detects "from the right" or "from the left".
    /// </summary>
    private static int ParseOrdinalWord(
        string desc, out bool hasDir, out bool fromLeft)
    {
        hasDir = false;
        fromLeft = true;

        foreach (var kv in _wordOrdinals)
        {
            var pattern = $@"\b{kv.Key}\b(?:.*\bfrom\s+the\s+(left|right)\b)?";
            var m = Regex.Match(desc, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            if (m.Groups[1].Success)
            {
                hasDir = true;
                fromLeft = m.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase);
            }
            return kv.Value;
        }
        return 0;
    }
}
