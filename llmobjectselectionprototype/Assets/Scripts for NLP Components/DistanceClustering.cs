using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DistanceClustering
{
    /* CONFIG */
    private const int K_NEAREST = 3;
    private const float MIN_EPS = 0.01f;
    private const float MAX_EPS = 3.00f;
    private const float ELBOW_CUSHION = 1.05f;
    private const bool IGNORE_Y_AXIS = true;

    public static List<List<GameObject>> ClusterByDistance(List<GameObject> items)
    {
        Debug.Log($"[Cluster] Starting clustering for {items.Count} items.");

        if (items == null || items.Count == 0)
            return new List<List<GameObject>>();

        // — no more collapsing overlaps —
        var objects = new List<GameObject>(items);

        // STEP 1: compute k-th nearest gaps (zeros allowed)
        var kthGaps = ComputeKthNearestGaps(objects, K_NEAREST);
        kthGaps.Sort();
        string gapListStr = string.Join(", ", kthGaps.Select(g => g.ToString("F3")));
        Debug.Log($"[Cluster] Sorted {K_NEAREST}-th nearest gaps: {gapListStr}");

        // STEP 2: pick epsilon via elbow
        float eps = PickEpsilon(kthGaps);
        Debug.Log($"[Cluster] Selected epsilon = {eps:F3} m");

        // STEP 3: flood-fill clustering
        var clusters = FloodFillClusters(objects, eps);
        Debug.Log($"[Cluster] Finished clustering: {clusters.Count} clusters found.");

        return clusters;
    }

    private static (Vector3 min, Vector3 max) GetWorldAABB(GameObject go)
    {
        if (go == null) return (Vector3.zero, Vector3.zero);
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return (Vector3.zero, Vector3.zero);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return (b.min, b.max);
    }

    private static float AabbGap((Vector3 min, Vector3 max) a, (Vector3 min, Vector3 max) b)
    {
        float dx = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float dy = IGNORE_Y_AXIS
                   ? 0
                   : Mathf.Max(0, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        float dz = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static List<float> ComputeKthNearestGaps(List<GameObject> objs, int k)
    {
        var gaps = new List<float>(objs.Count);

        for (int i = 0; i < objs.Count; i++)
        {
            var aabbI = GetWorldAABB(objs[i]);
            var dists = new List<float>(objs.Count - 1);

            for (int j = 0; j < objs.Count; j++)
            {
                if (i == j) continue;
                dists.Add(AabbGap(aabbI, GetWorldAABB(objs[j])));
            }

            dists.Sort();
            float kth = dists[Mathf.Clamp(k - 1, 0, dists.Count - 1)];
            gaps.Add(kth);

            Debug.Log($"[Gaps] Object {GetId(objs[i])} ? {k}-th nearest gap = {kth:F3} m");
        }

        return gaps;
    }

    private static float PickEpsilon(List<float> gaps)
    {
        float eps = MAX_EPS;
        float bestRatio = 1f;
        bool foundElbow = false;

        for (int i = 1; i < gaps.Count; i++)
        {
            float prev = Mathf.Max(gaps[i - 1], 1e-4f);
            float curr = gaps[i];
            float ratio = curr / prev;
            Debug.Log($"[Elbow] gap[{i - 1}]={prev:F3}, gap[{i}]={curr:F3}, ratio={ratio:F3}");

            if (ratio > bestRatio && curr > MIN_EPS)
            {
                bestRatio = ratio;
                float mid = (prev + curr) * 0.5f;
                eps = Mathf.Clamp(mid, MIN_EPS, MAX_EPS);
                foundElbow = true;
                Debug.Log($"[Elbow] Detected elbow — eps set to midpoint {eps:F3}");
            }
        }

        if (!foundElbow)
        {
            float median = gaps[gaps.Count / 2];
            eps = Mathf.Clamp(median, MIN_EPS, MAX_EPS);
            Debug.Log($"[Elbow] No elbow found — falling back to median {eps:F3}");
        }

        return eps;
    }

    private static List<List<GameObject>> FloodFillClusters(List<GameObject> items, float eps)
    {
        var clusters = new List<List<GameObject>>();
        var unvisited = new HashSet<GameObject>(items);

        int clusterIdx = 0;
        while (unvisited.Count > 0)
        {
            var seed = unvisited.First();
            unvisited.Remove(seed);
            Debug.Log($"[Cluster] Starting cluster #{clusterIdx} with seed {GetId(seed)}");

            var cluster = new List<GameObject> { seed };
            var queue = new Queue<GameObject>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var aabbCur = GetWorldAABB(cur);

                foreach (var other in unvisited.ToArray())
                {
                    float gap = AabbGap(aabbCur, GetWorldAABB(other));
                    if (gap <= eps)
                    {
                        unvisited.Remove(other);
                        cluster.Add(other);
                        queue.Enqueue(other);
                        Debug.Log($"[Cluster]   Adding {GetId(other)} (gap={gap:F3}) to cluster #{clusterIdx}");
                    }
                }
            }

            clusters.Add(cluster);
            clusterIdx++;
        }

        return clusters;
    }

    private static string GetId(GameObject obj)
    {
        var md = obj.GetComponent<GameObjectMetadata>();
        return (md != null && !string.IsNullOrEmpty(md.id))
            ? md.id
            : obj.name;
    }
}
