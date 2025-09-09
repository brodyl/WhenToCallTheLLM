/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using Meta.WitAi.Lib;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.VersionControl;
using UnityEngine;

/// <summary>
/// Central registry + evaluators for all supported spatial relations.
/// Each evaluator receives the candidate mains / related objects and
/// returns the mains that satisfy <paramref name="relation"/>.
/// </summary>
public static class SpatialRelationEvaluator
{
    /// Which side of a reference object we care about.
    public enum AxisRelation
    {
        Above,
        Below,
        InFront,   // closer to the camera (-Z)   ???
        Behind,    // farther away   (+Z)         ???
        LeftOf,    // smaller X (-X)              ???
        RightOf    // larger  X (+X)              ???
    }

    // Delegate type every rule must follow.
    private delegate List<GameObject> RelationEval(
        List<GameObject> mains,
        List<GameObject> related);

    /* --------------------------------------------------------------------
 *  1.  delegate type for two-list relations
 * ------------------------------------------------------------------*/
    private delegate List<GameObject> RelationEvalDual(
        List<GameObject> mains,
        List<GameObject> relatedA,
        List<GameObject> relatedB);

    // ------------------------------------------------------------
    // Registry -- map keyword -> two-list relation evaluator
    // ------------------------------------------------------------
    private static readonly Dictionary<string, RelationEvalDual> _rulesDual =
    new(StringComparer.OrdinalIgnoreCase)
    {
        { "between", EvalBetween },
        { "in between", EvalBetween }
    };

    // ------------------------------------------------------------
    // Registry -- map keyword -> one-list relation evaluator
    // ------------------------------------------------------------
    private static readonly Dictionary<string, RelationEval> _rules =
        new(StringComparer.OrdinalIgnoreCase)        // "Above" == "above"
        {
            { "on",             EvalOn  },      ////////
            { "on top",         EvalOnTop  },   ////////
            { "on top of",      EvalOnTop  },
            { "sitting on",     EvalOnTop  },
            { "placed on",      EvalOnTop  },
            { "resting on",     EvalOnTop  },
            { "positioned on",  EvalOnTop  },
            { "left on",        EvalOnTop  },
            { "atop",           EvalOnTop  },
            { "above",          (m, r) => EvalAxisRelation(m, r, AxisRelation.Above)  },   ////////       
            { "over",           EvalAbove  },
            { "below",          EvalBelow  },   ////////
            { "under",          EvalBelow  },
            { "underneath",     EvalBelow  },
            { "beneath",        EvalBelow  },
            { "lower than",     EvalBelow  },
            { "in",             EvalInside },   ////////
            { "inside",         EvalInside },
            { "within",         EvalInside },
            { "outside",        EvalOutside },  ////////
            { "outside of",     EvalOutside },
            { "out of",         EvalOutside },
            { "behind",         (m, r) => EvalAxisRelation(m, r, AxisRelation.Behind) },
            { "in front",       (m, r) => EvalAxisRelation(m, r, AxisRelation.InFront) },
            { "ahead of",       (m, r) => EvalAxisRelation(m, r, AxisRelation.InFront) },
            { "in front of",    (m, r) => EvalAxisRelation(m, r, AxisRelation.InFront) },
            //{ "left",        (m, r) => EvalAxisRelation(m, r, AxisRelation.LeftOf) },
            //{ "right",       (m, r) => EvalAxisRelation(m, r, AxisRelation.RightOf) },
            { "left",           EvalLeft   },   ////////
            { "right",          EvalRight  },   ////////
            { "near",           EvalNear },     ////////
            { "beside",         EvalNear },
            { "right beside",   EvalNear },
            { "next to",        EvalNear },
            { "right next to",  EvalNear },
            { "at",             EvalNear },
            { "close to",       EvalNear },
            { "closest",        EvalClosestPerRelated },   // for more strict: EvalClosestTo
            { "closest to",     EvalClosestPerRelated },   // for more strict: EvalClosestTo
            { "closer to",      EvalClosestPerRelated },   // for more strict: EvalClosestTo
            { "nearest",        EvalClosestPerRelated },   // for more strict: EvalClosestTo
            { "nearest to",     EvalClosestPerRelated },   // for more strict: EvalClosestTo
            { "nearer to",      EvalClosestPerRelated },   // for more strict: EvalClosestTo
            // add more rules here, or auto-register via reflection
        };

    /// <summary>
    /// Public entry point. Returns the set of mains that fulfil the relation.
    /// An empty list == "no matches / unknown relation".
    /// </summary>
    public static List<GameObject> Evaluate(
        string relation,
        List<GameObject> mains,
        List<GameObject> related)
    {
        if (string.IsNullOrEmpty(relation) ||
            !_rules.TryGetValue(relation, out var eval))
        {
            Debug.LogWarning($"[SpatialRelationEvaluator] No rule for '{relation}' spatial relationship.");
            return new();
        }

        return eval(mains, related);
    }

    /// <summary>
    /// Public entry point. Returns the set of mains that fulfil the relation.
    /// An empty list == "no matches / unknown relation".
    /// </summary>
    public static List<GameObject> Evaluate(
        string relation,
        List<GameObject> mains,
        List<GameObject> relatedA,
        List<GameObject> relatedB)
    {
        if (string.IsNullOrEmpty(relation) ||
            !_rulesDual.TryGetValue(relation, out var eval))
        {
            Debug.LogWarning($"[SpatialRelationEvaluator] No dual-list rule for '{relation}'.");
            return new();
        }

        return eval(mains, relatedA ?? new(), relatedB ?? new());
    }


    // ------------------------------------------------------------
    // ------------------------------------------------------------
    //                         Helpers
    // ------------------------------------------------------------
    // ------------------------------------------------------------

    /// <summary>
    /// Calculates the world-space axis-aligned bounding box (AABB) of the given GameObject, 
    /// including all Renderer components in its children, regardless of hierarchy depth.
    /// </summary>
    /// <param name="go">The root GameObject whose bounds are to be calculated.</param>
    /// <returns>A tuple containing the minimum and maximum world-space coordinates of the AABB.</returns>
    private static (Vector3 min, Vector3 max) GetWorldSpaceAABB(GameObject go)
    {
        if (go == null) return (Vector3.zero, Vector3.zero);

        // Try renderers first
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }
            return (combinedBounds.min, combinedBounds.max);
        }

        // Fallback to colliders
        Collider[] colliders = go.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds combinedBounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                combinedBounds.Encapsulate(colliders[i].bounds);
            }
            return (combinedBounds.min, combinedBounds.max);
        }

        // No bounds found
        return (Vector3.zero, Vector3.zero);
    }


    private static Vector3 GetAABBCentre(GameObject go)
    {
        var (min, max) = GetWorldSpaceAABB(go);
        return (min + max) * 0.5f;
    }


    // Returns % of XZ area overlap between two AABBs (0-1)
    private static float XZOverlapPercent(
        (Vector3 min, Vector3 max) a,
        (Vector3 min, Vector3 max) b)
    {
        float xMin = Mathf.Max(a.min.x, b.min.x);
        float xMax = Mathf.Min(a.max.x, b.max.x);
        float zMin = Mathf.Max(a.min.z, b.min.z);
        float zMax = Mathf.Min(a.max.z, b.max.z);

        float overlapX = Mathf.Max(0, xMax - xMin);
        float overlapZ = Mathf.Max(0, zMax - zMin);
        float overlapArea = overlapX * overlapZ;

        float areaA = (a.max.x - a.min.x) * (a.max.z - a.min.z);
        float areaB = (b.max.x - b.min.x) * (b.max.z - b.min.z);

        return overlapArea / Mathf.Min(areaA, areaB);   // use smaller area as reference
    }


    private static bool NearlyEqual(float a, float b) =>
        Mathf.Abs(a - b) <= TouchEpsilon;

    // Overlap helpers (interval intersection in 2D planes)
    private static bool OverlapXZ(
        Vector3 minA, Vector3 maxA,
        Vector3 minB, Vector3 maxB) =>
            maxA.x >= minB.x && minA.x <= maxB.x &&
            maxA.z >= minB.z && minA.z <= maxB.z;

    private static bool OverlapYZ(
        Vector3 minA, Vector3 maxA,
        Vector3 minB, Vector3 maxB) =>
            maxA.y >= minB.y && minA.y <= maxB.y &&
            maxA.z >= minB.z && minA.z <= maxB.z;

    private static bool OverlapXY(
        Vector3 minA, Vector3 maxA,
        Vector3 minB, Vector3 maxB) =>
            maxA.x >= minB.x && minA.x <= maxB.x &&
            maxA.y >= minB.y && minA.y <= maxB.y;

    /// <summary>
    /// True if the bounds overlap (or are within <paramref name="slack"/>) in *all three axes*.
    /// </summary>
    private static bool BoundsIntersect(Bounds a, Bounds b, float slack = 0f)
    {
        return a.max.x + slack >= b.min.x && a.min.x - slack <= b.max.x &&
                a.max.y + slack >= b.min.y && a.min.y - slack <= b.max.y &&
                a.max.z + slack >= b.min.z && a.min.z - slack <= b.max.z;
    }

    /// <summary>
    /// Uses Physics.ComputePenetration to check if colliders are overlapping or
    /// separated by <= TouchEpsilon. Works for any collider shapes.
    /// </summary>
    private static bool PreciseTouch(Collider colMain, Collider colRel)
    {
        bool hit = Physics.ComputePenetration(
            colMain, colMain.transform.position, colMain.transform.rotation,
            colRel, colRel.transform.position, colRel.transform.rotation,
            out _, out float distance);

        // When colliders are *overlapping* the function returns "true" with
        // distance ~ 0.  When they're separated it returns "false", but the
        // distance still contains the gap.  We treat both as "touch" if
        // the gap/penetration is within TouchEpsilon.
        return distance <= TouchEpsilon;
    }

    private static MeshCollider EnsureMeshCollider(GameObject go,
                                               bool wantTrigger = false)
    {
        MeshCollider mc = go.GetComponent<MeshCollider>();
        if (!mc)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return null;

            mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
        }

        // Decide how to configure the collider
        if (wantTrigger)
        {
            // Triggers MUST be convex.  If too many tris, fall back to concave-non-trigger.
            const int MaxConvexTris = 255;
            bool canBeConvex = mc.sharedMesh.triangles.Length / 3 <= MaxConvexTris;

            if (canBeConvex)
            {
                mc.convex    = true;
                mc.isTrigger = true;
            }
            else
            {
                Debug.LogWarning(
                    $"{go.name}: mesh has >{MaxConvexTris} tris; using concave collider (non-trigger).");
                mc.convex    = false;
                mc.isTrigger = false;
            }
        }
        else
        {
            mc.convex    = false;   // full-accuracy concave
            mc.isTrigger = false;
        }

        return mc;
    }

    private static bool AABBTouch((Vector3 min, Vector3 max) a,
                              (Vector3 min, Vector3 max) b,
                              float eps)
    {
        bool xTouch = Mathf.Abs(a.min.x - b.max.x) <= eps ||
                      Mathf.Abs(a.max.x - b.min.x) <= eps;
        bool yTouch = Mathf.Abs(a.min.y - b.max.y) <= eps ||
                      Mathf.Abs(a.max.y - b.min.y) <= eps;
        bool zTouch = Mathf.Abs(a.min.z - b.max.z) <= eps ||
                      Mathf.Abs(a.max.z - b.min.z) <= eps;

        // For each axis where faces touch, require overlap in the other two
        bool overlapXY = a.max.x >= b.min.x && a.min.x <= b.max.x &&
                         a.max.y >= b.min.y && a.min.y <= b.max.y;

        bool overlapXZ = a.max.x >= b.min.x && a.min.x <= b.max.x &&
                         a.max.z >= b.min.z && a.min.z <= b.max.z;

        bool overlapYZ = a.max.y >= b.min.y && a.min.y <= b.max.y &&
                         a.max.z >= b.min.z && a.min.z <= b.max.z;

        return (xTouch && overlapYZ) ||
               (yTouch && overlapXZ) ||
               (zTouch && overlapXY);
    }

    /// True iff point lies *strictly* inside collider volume.
    /// Works for any collider type, concave or convex.
    private static bool PointInsideCollider(Collider col, Vector3 point)
    {
        // ClosestPoint returns the same point when the supplied point is inside.
        Vector3 closest = col.ClosestPoint(point);
        return (closest - point).sqrMagnitude < 1e-8f;
    }

    /// 9 sample points for a bounds (centre + 8 corners)
    private static IEnumerable<Vector3> SampleBoundsPoints(Bounds b)
    {
        yield return b.center;
        Vector3 min = b.min, max = b.max;
        yield return new Vector3(min.x, min.y, min.z);
        yield return new Vector3(max.x, min.y, min.z);
        yield return new Vector3(min.x, max.y, min.z);
        yield return new Vector3(min.x, min.y, max.z);
        yield return new Vector3(max.x, max.y, min.z);
        yield return new Vector3(min.x, max.y, max.z);
        yield return new Vector3(max.x, min.y, max.z);
        yield return new Vector3(max.x, max.y, max.z);
    }

    /// Euclidean distance between two AABBs in world space (0 if they overlap).
    private static float BoundsDistance(Bounds a, Bounds b)
    {
        float dx = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float dy = Mathf.Max(0, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        float dz = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool BoundsSeparate(Bounds a, Bounds b, float gap = 0f)
    {
        return a.min.x > b.max.x + gap || a.max.x < b.min.x - gap ||
                a.min.y > b.max.y + gap || a.max.y < b.min.y - gap ||
                a.min.z > b.max.z + gap || a.max.z < b.min.z - gap;
    }

    private static float CollidersGap(Collider a, Collider b)
    {
        Vector3 pA = a.ClosestPoint(b.transform.position);
        Vector3 pB = b.ClosestPoint(pA);             // symmetric
        return Vector3.Distance(pA, pB);
    }

    /// Surface-to-surface distance between two objects (0 if overlapping).
    private static float DistanceBetween(GameObject a, GameObject b)
    {
        Collider colA = EnsureMeshCollider(a);
        Collider colB = EnsureMeshCollider(b);

        if (colA && colB)
        {
            // Physics path: use ClosestPoint on each collider
            Vector3 pA = colA.ClosestPoint(colB.transform.position);
            Vector3 pB = colB.ClosestPoint(colA.transform.position);
            return Vector3.Distance(pA, pB);        // 0 if touching / inside
        }

        /* --- Fallback: centre-to-centre distance ------------------------ */
        var (minA, maxA) = GetWorldSpaceAABB(a);
        var (minB, maxB) = GetWorldSpaceAABB(b);
        Vector3 centreA = (minA + maxA) * 0.5f;
        Vector3 centreB = (minB + maxB) * 0.5f;
        return Vector3.Distance(centreA, centreB);
    }



    // ------------------------------------------------------------
    // ------------------------------------------------------------
    //              Spatial Relation Evaluators
    // ------------------------------------------------------------
    // ------------------------------------------------------------


    // ------------------------------------------------------------
    // CONFIGS
    // ------------------------------------------------------------
    private const float TouchEpsilon = 0.02f;   // ~ 2 cm "touch" tolerance
    private const float AABBSlack = 0.05f;   // broad-phase slack (avoids false negatives)
    private const float MinOverlap = 0.50f;  // 50 %
    private static float _nearThreshold = 2.0f;    // default 50 cm

    public static float NearThreshold
    {
        get => _nearThreshold;
        set => _nearThreshold = Mathf.Max(0f, value);   // prevent negatives
    }



    /// Mapping Main -> its single closest Related (public getter if you need it)
    public static readonly Dictionary<GameObject, GameObject> ClosestMap =
        new();

    public static readonly Dictionary<GameObject, GameObject> ClosestPerRelated =
    new();   // key = related, value = chosen main

    // ------------------------------------------------------------
    // MAIN RULE
    // ------------------------------------------------------------
    //private static List<GameObject> EvalOn(
    //    List<GameObject> mains,
    //    List<GameObject> related)
    //{
    //    var valid = new HashSet<GameObject>();      // dedupe cheaply

    //    foreach (var main in mains)
    //    {
    //        var (minM, maxM) = GetWorldSpaceAABB(main);

    //        foreach (var rel in related)
    //        {
    //            var (minR, maxR) = GetWorldSpaceAABB(rel);

    //            // Check each of the 6 possible touch-faces
    //            bool touchTop = NearlyEqual(minM.y, maxR.y) &&
    //                                OverlapXZ(minM, maxM, minR, maxR);

    //            bool touchBottom = NearlyEqual(maxM.y, minR.y) &&
    //                                OverlapXZ(minM, maxM, minR, maxR);

    //            bool touchPosX = NearlyEqual(minM.x, maxR.x) &&
    //                                OverlapYZ(minM, maxM, minR, maxR);

    //            bool touchNegX = NearlyEqual(maxM.x, minR.x) &&
    //                                OverlapYZ(minM, maxM, minR, maxR);

    //            bool touchPosZ = NearlyEqual(minM.z, maxR.z) &&
    //                                OverlapXY(minM, maxM, minR, maxR);

    //            bool touchNegZ = NearlyEqual(maxM.z, minR.z) &&
    //                                OverlapXY(minM, maxM, minR, maxR);

    //            if (touchTop || touchBottom ||
    //                touchPosX || touchNegX ||
    //                touchPosZ || touchNegZ)
    //            {
    //                valid.Add(main);
    //                break;                      // one surface match is enough
    //            }
    //        }
    //    }

    //    return valid.ToList();
    //}

    private static List<GameObject> EvalOn(
    List<GameObject> mains,
    List<GameObject> related)
    {
        var valid = new HashSet<GameObject>();

        foreach (var main in mains)
        {
            // Try collider path first
            Collider colMain = EnsureMeshCollider(main);  // or EnsureMeshCollider
            bool usedBroadPhase = false;

            foreach (var rel in related)
            {
                Collider colRel = EnsureMeshCollider(rel);
                if (colMain && colRel)
                {
                    // Broad-phase bounds test
                    if (!BoundsIntersect(colMain.bounds, colRel.bounds, 0.05f))
                        continue;
                    usedBroadPhase = true;

                    if (PreciseTouch(colMain, colRel))
                    {
                        valid.Add(main);
                        break;
                    }
                }
                else
                {
                    // Fallback to pure AABB when either object lacks a collider
                    var aabbMain = GetWorldSpaceAABB(main);
                    var aabbRel = GetWorldSpaceAABB(rel);

                    if (AABBTouch(aabbMain, aabbRel, 0.02f))
                    {
                        valid.Add(main);
                        break;
                    }
                }
            }

            // Optional: log if we had to fall back
            if (!usedBroadPhase)
                Debug.LogWarning($"[{main.name}] evaluated with AABB fallback.");
        }

        return valid.ToList();
    }



    private static List<GameObject> EvalOnTop(
        List<GameObject> mains,
        List<GameObject> related)
    {
        var valid = new List<GameObject>();

        foreach (var main in mains)
        {
            var aabbMain = GetWorldSpaceAABB(main);

            foreach (var rel in related)
            {
                var aabbRel = GetWorldSpaceAABB(rel);

                // Height check
                bool closeVertically =
                    (aabbMain.min.y - aabbRel.max.y) <= TouchEpsilon;

                if (!closeVertically) continue;

                // Planar overlap check
                float overlap = XZOverlapPercent(aabbMain, aabbRel);
                if (overlap < MinOverlap) continue;

                // Optional ray confirmation (uncomment for stricter rule)
                //   Proof that rel is the first solid surface directly below main.
                /*
                Vector3 probe = new Vector3(
                    (aabbMain.min.x + aabbMain.max.x) * 0.5f,
                     aabbMain.min.y + 0.01f,                              // just under main
                    (aabbMain.min.z + aabbMain.max.z) * 0.5f);

                if (!Physics.Raycast(probe, Vector3.down, out var hit, 0.1f) ||
                    hit.collider.gameObject != rel)
                    continue;
                */

                valid.Add(main);
                break;              // one supporting surface is enough
            }
        }

        return valid.Distinct().ToList();
    }



    /// <summary>
    /// Returns mains that are spatially **above** at least one related object.
    ///
    /// Rule:
    ///   1. main.maxY >= rel.maxY                                  (higher)
    ///   2. Project both AABBs to X-Z plane.
    ///      Grow rel's projection by <see cref="SlopeFactor"/> * deltaY in all
    ///      directions, then check for any overlap.
    ///      deltaY = main.minY - rel.maxY  (vertical gap between surfaces)
    ///
    /// Intuition: the higher main is, the looser our notion of "above".
    ///           At ground-contact (deltaY ~ 0) it behaves like a tight overlap test.
    /// </summary>
    private const float SlopeFactor = 0.25f;   // metres of allowed drift per metre of height
    //private const float MaxTolerance = 3.0f;    // (optional) cap the growth in metres

    private static List<GameObject> EvalAbove(
        List<GameObject> mains,
        List<GameObject> related)
    {
        float SlopeFactor = 0.50f;   // tune this based on your scene scale
        float MaxTolerance = 2.0f;   // optional: prevent excessive growth

        var valid = new HashSet<GameObject>(); // deduplicated output

        foreach (var main in mains)
        {
            var (minMain, maxMain) = GetWorldSpaceAABB(main);

            foreach (var rel in related)
            {
                var (minRel, maxRel) = GetWorldSpaceAABB(rel);

                // Must be vertically above
                if (maxMain.y < maxRel.y)
                    continue;

                // Vertical gap: bottom of main above top of related
                float deltaY = Mathf.Max(0f, minMain.y - maxRel.y);

                // Tolerance grows with vertical gap (sloped pyramid model)
                float tolerance = Mathf.Min(MaxTolerance, deltaY * SlopeFactor);

                // Expand rel object's XZ footprint by tolerance
                float relMinX = minRel.x - tolerance;
                float relMaxX = maxRel.x + tolerance;
                float relMinZ = minRel.z - tolerance;
                float relMaxZ = maxRel.z + tolerance;

                // Check if main's XZ footprint overlaps the expanded rel area
                bool overlapX = maxMain.x >= relMinX && minMain.x <= relMaxX;
                bool overlapZ = maxMain.z >= relMinZ && minMain.z <= relMaxZ;

                if (overlapX && overlapZ)
                {
                    valid.Add(main); // only need one hit
                    break;
                }
            }
        }

        return valid.ToList();
    }



    ///// <summary>
    ///// Returns every <paramref name="mains"/> object that is spatially **above** at least
    ///// one object in <paramref name="related"/>.
    /////
    ///// "Above" is defined as:
    /////   - main.maxY >= related.maxY               (higher in world Y)
    /////   - main.center.XZ lies inside related AABB (rough X-Z overlap)
    ///// The test is intentionally loose to handle phrases like
    ///// "the lamp above the coffee table" even if the lamp's base touches the floor.
    ///// </summary>
    //private static List<GameObject> EvalAbove(
    //    List<GameObject> mains,
    //    List<GameObject> related)
    //{
    //    var valid = new List<GameObject>();

    //    foreach (var main in mains)
    //    {
    //        var (minMain, maxMain) = GetWorldSpaceAABB(main);
    //        var centerMainXZ        = new Vector2(
    //            (minMain.x + maxMain.x) * 0.5f,
    //            (minMain.z + maxMain.z) * 0.5f);

    //        foreach (var rel in related)
    //        {
    //            var (minRel, maxRel) = GetWorldSpaceAABB(rel);

    //            bool higherY  = maxMain.y >= maxRel.y;
    //            bool insideXZ =
    //                centerMainXZ.x >= minRel.x && centerMainXZ.x <= maxRel.x &&
    //                centerMainXZ.y >= minRel.z && centerMainXZ.y <= maxRel.z;

    //            if (higherY && insideXZ)
    //                valid.Add(main);
    //        }
    //    }

    //    return valid.Distinct().ToList();   // remove dupes caused by multiple matches
    //}

    /// <summary>
    /// A variant of <see cref="EvalAbove"/> that uses pre-computed AABBs for compute savings.
    /// This may be required if the number of mains and related objects is large. 
    ///     Extra tips if you ever need more speed
    ///     -Cache AABBs per GameObject
    ///     -Store them in a Dictionary<GameObject, (Vector3 min, Vector3 max)> and update only 
    ///     when the object's Renderer list or transform changes.
    /// </summary>
    //private static List<GameObject> EvalAbove(
    //List<GameObject> mains,
    //List<GameObject> related)
    //{
    //    // --------------------------------------------------
    //    //  Pre-compute AABBs (O(N)) instead of inside
    //    //     the nested loop (O(N*M)).
    //    // --------------------------------------------------
    //    var mainData = mains.Select(main =>
    //    {
    //        var (min, max) = GetWorldSpaceAABB(main);
    //        var centerXZ = new Vector2((min.x + max.x) * 0.5f,
    //                                     (min.z + max.z) * 0.5f);
    //        return (go: main, min, max, centerXZ);
    //    }).ToList();

    //    var relData = related.Select(rel =>
    //    {
    //        var (min, max) = GetWorldSpaceAABB(rel);
    //        return (go: rel, min, max);
    //    }).ToList();

    //    // --------------------------------------------------
    //    // Evaluate
    //    // --------------------------------------------------
    //    const float XZMargin = 0f;             // expand / shrink horizontal test if wanted
    //    var valid = new HashSet<GameObject>(); // hash set avoids duplicates cheaply

    //    foreach (var (main, _, maxMain, centerMainXZ) in mainData)
    //    {
    //        foreach (var (_, minRel, maxRel) in relData)
    //        {
    //            bool higherY = maxMain.y >= maxRel.y;
    //            bool insideXZ =
    //                centerMainXZ.x >= (minRel.x - XZMargin) &&
    //                centerMainXZ.x <= (maxRel.x + XZMargin) &&
    //                centerMainXZ.y >= (minRel.z - XZMargin) &&
    //                centerMainXZ.y <= (maxRel.z + XZMargin);

    //            if (higherY && insideXZ)
    //            {
    //                valid.Add(main);
    //                break;              // one supporting rel is enough
    //            }
    //        }
    //    }

    //    return valid.ToList();
    //}




    private static List<GameObject> EvalBelow(
        List<GameObject> mains,
        List<GameObject> related)
    {
        var valid = new List<GameObject>();

        foreach (var main in mains)
        {
            var (minMain, maxMain) = GetWorldSpaceAABB(main);
            var centerMainXZ = new Vector2(
                (minMain.x + maxMain.x) * 0.5f,
                (minMain.z + maxMain.z) * 0.5f);

            foreach (var rel in related)
            {
                var (minRel, maxRel) = GetWorldSpaceAABB(rel);

                bool higherY = maxMain.y <= maxRel.y;
                bool insideXZ =
                    centerMainXZ.x >= minRel.x && centerMainXZ.x <= maxRel.x &&
                    centerMainXZ.y >= minRel.z && centerMainXZ.y <= maxRel.z;

                if (higherY && insideXZ)
                    valid.Add(main);
            }
        }

        return valid.Distinct().ToList();
    }

    private static List<GameObject> EvalLeft(
        List<GameObject> mains,
        List<GameObject> related)
    {
        var valid = new List<GameObject>();

        //foreach (var main in mains)
        //{
        //    var (minMain, maxMain) = AABB(main);
        //    var (_, maxRel) = AABB(related[0]);   // simplistic: compare to first

        //    if (maxMain.x < maxRel.x)
        //        valid.Add(main);
        //}

        return valid.Distinct().ToList();
    }

    private static List<GameObject> EvalRight(
        List<GameObject> mains,
        List<GameObject> related)
    {
        // mirror of EvalLeft
        var valid = new List<GameObject>();

        //foreach (var main in mains)
        //{
        //    var (minMain, _) = AABB(main);
        //    var (minRel, _) = AABB(related[0]);

        //    if (minMain.x > minRel.x)
        //        valid.Add(main);
        //}

        return valid.Distinct().ToList();
    }


    private static List<GameObject> EvalInside(
    List<GameObject> mains,
    List<GameObject> related)
    {
        var valid = new HashSet<GameObject>();

        foreach (var main in mains)
        {
            // Cache main's world-space bounds + sample points once
            var aabbMain = GetWorldSpaceAABB(main);
            Bounds boundsMain = new Bounds();
            boundsMain.SetMinMax(aabbMain.min, aabbMain.max);
            var samplePts = SampleBoundsPoints(boundsMain).ToArray();

            foreach (var rel in related)
            {
                // Prefer collider-accurate test
                Collider colRel = EnsureMeshCollider(rel); // concave OK, not trigger
                bool insideFound = false;

                if (colRel)
                {
                    // Broad-phase: bounds check to skip far objects
                    if (!BoundsIntersect(colRel.bounds, boundsMain, AABBSlack))
                        continue;

                    // Narrow-phase: any sample pt strictly inside?
                    foreach (var p in samplePts)
                    {
                        if (PointInsideCollider(colRel, p))
                        {
                            insideFound = true;
                            break;
                        }
                    }
                }
                else
                {
                    /* --- Fallback: pure AABB containment -------------------- */
                    var aabbRel = GetWorldSpaceAABB(rel);

                    bool CentreInside =
                        aabbMain.min.x >= aabbRel.min.x && aabbMain.max.x <= aabbRel.max.x &&
                        aabbMain.min.y >= aabbRel.min.y && aabbMain.max.y <= aabbRel.max.y &&
                        aabbMain.min.z >= aabbRel.min.z && aabbMain.max.z <= aabbRel.max.z;

                    // "Partially inside" ~= any overlap of the two AABBs
                    bool AnyOverlap =
                        aabbMain.max.x >= aabbRel.min.x && aabbMain.min.x <= aabbRel.max.x &&
                        aabbMain.max.y >= aabbRel.min.y && aabbMain.min.y <= aabbRel.max.y &&
                        aabbMain.max.z >= aabbRel.min.z && aabbMain.min.z <= aabbRel.max.z;

                    insideFound = CentreInside || AnyOverlap;
                }

                if (insideFound)
                {
                    valid.Add(main);
                    break;                      // one related object is enough
                }
            }
        }

        return valid.ToList();
    }


    private static List<GameObject> EvalAxisRelation(
        List<GameObject> mains,
        List<GameObject> related,
        AxisRelation relation,
        float slopeFactor = 0.50f,   // still used for Above / Below / Left / Right
        float maxTolerance = 2.0f)   // hard cap for any tolerance we compute
    {
        /* ??? 0. Acquire camera info (needed only for depth tests) ????????????? */
        Camera cam = null;

        if (relation is AxisRelation.InFront or AxisRelation.Behind)
        {
            // 1??  Try OVRCameraRig first (Quest / VR)
            var rig = UnityEngine.Object.FindObjectOfType<OVRCameraRig>();
            if (rig != null) cam = rig.GetComponentInChildren<Camera>();

            // 2??  Fallback to Camera.main
            if (cam == null) cam = Camera.main;

            if (cam == null)
            {
                Debug.LogWarning("[EvalAxisRelation] No camera found - "
                               + "falling back to world-Z slope model for InFront/Behind.");
            }
        }

        /* Cache frustum half-angles if we have a camera */
        float halfVertRad = 0f;
        float halfHorzRad = 0f;
        if (cam != null)
        {
            halfVertRad = cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
            halfHorzRad = Mathf.Atan(Mathf.Tan(halfVertRad) * cam.aspect);
        }

        // ???????????????????????????????????????????????????????????????????
        var valid = new HashSet<GameObject>();

        foreach (var main in mains)
        {
            var (minMain, maxMain) = GetWorldSpaceAABB(main);

            foreach (var rel in related)
            {
                var (minRel, maxRel) = GetWorldSpaceAABB(rel);

                /* 1??  Side test + primary-axis gap */
                bool correctSide;
                float delta;                    // unsigned gap along primary axis

                switch (relation)
                {
                    /* ?? Vertical (world Y) ?? */
                    case AxisRelation.Above:
                        correctSide = maxMain.y >= maxRel.y;
                        delta = Mathf.Max(0f, minMain.y - maxRel.y);
                        break;

                    case AxisRelation.Below:
                        correctSide = minMain.y <= minRel.y;
                        delta = Mathf.Max(0f, minRel.y - maxMain.y);
                        break;

                    /* ?? Depth (camera-relative) ?? */
                    case AxisRelation.InFront when cam != null:
                    case AxisRelation.Behind when cam != null:
                        {
                            Vector3 fwd = cam.transform.forward.normalized;
                            GetMinMaxAlongAxis(minMain, maxMain, fwd,
                                               out float minMF, out float maxMF);
                            GetMinMaxAlongAxis(minRel, maxRel, fwd,
                                               out float minRF, out float maxRF);

                            if (relation == AxisRelation.InFront)
                            {
                                correctSide = maxMF >= maxRF;
                                delta = Mathf.Max(0f, minMF - maxRF);
                            }
                            else /* Behind */
                            {
                                correctSide = maxMF <= minRF;
                                delta = Mathf.Max(0f, minRF - maxMF);
                            }
                            break;
                        }

                    /* ?? Depth fallback (world Z) ?? */
                    case AxisRelation.InFront:
                        correctSide = minMain.z <= minRel.z;
                        delta = Mathf.Max(0f, minRel.z - maxMain.z);
                        break;

                    case AxisRelation.Behind:
                        correctSide = maxMain.z >= maxRel.z;
                        delta = Mathf.Max(0f, minMain.z - maxRel.z);
                        break;

                    /* ?? Lateral (world X) ?? */
                    case AxisRelation.RightOf:
                        correctSide = maxMain.x >= maxRel.x;
                        delta = Mathf.Max(0f, minMain.x - maxRel.x);
                        break;

                    case AxisRelation.LeftOf:
                        correctSide = minMain.x <= minRel.x;
                        delta = Mathf.Max(0f, minRel.x - maxMain.x);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(relation), relation, null);
                }

                if (!correctSide) continue;

                /* 2??  Compute tolerance */
                float tolX, tolY, tolZ;   // we'll only need two of these each time

                if (cam != null && (relation == AxisRelation.InFront || relation == AxisRelation.Behind))
                {
                    // Match the camera frustum
                    //tolX = Mathf.Min(maxTolerance, delta * Mathf.Tan(halfHorzRad));
                    //tolY = Mathf.Min(maxTolerance, delta * Mathf.Tan(halfVertRad));
                    tolX = delta * Mathf.Tan(halfHorzRad);
                    tolY = delta * Mathf.Tan(halfVertRad);
                }
                else
                {
                    // Fixed slope factor for non-camera axes
                    float tol = Mathf.Min(maxTolerance, delta * slopeFactor);
                    tolX = tolY = tol;
                }

                /* 3??  Overlap in the plane perpendicular to primary axis */
                bool overlap1, overlap2;

                switch (relation)
                {
                    /* Vertical ? X-Z plane */
                    case AxisRelation.Above:
                    case AxisRelation.Below:
                        overlap1 = maxMain.x >= (minRel.x - tolX) && minMain.x <= (maxRel.x + tolX);
                        overlap2 = maxMain.z >= (minRel.z - tolY) && minMain.z <= (maxRel.z + tolY);
                        break;

                    /* Depth ? camera-right / camera-up plane (or X-Y fallback) */
                    case AxisRelation.InFront when cam != null:
                    case AxisRelation.Behind when cam != null:
                        Vector3 right = cam.transform.right.normalized;
                        Vector3 up = cam.transform.up.normalized;
                        overlap1 = OverlapAlongAxis(minMain, maxMain, minRel, maxRel, right, tolX);
                        overlap2 = OverlapAlongAxis(minMain, maxMain, minRel, maxRel, up, tolY);
                        break;

                    case AxisRelation.InFront:
                    case AxisRelation.Behind:
                        overlap1 = maxMain.x >= (minRel.x - tolX) && minMain.x <= (maxRel.x + tolX);
                        overlap2 = maxMain.y >= (minRel.y - tolY) && minMain.y <= (maxRel.y + tolY);
                        break;

                    /* Lateral ? Y-Z plane */
                    case AxisRelation.LeftOf:
                    case AxisRelation.RightOf:
                        overlap1 = maxMain.y >= (minRel.y - tolX) && minMain.y <= (maxRel.y + tolX);
                        overlap2 = maxMain.z >= (minRel.z - tolY) && minMain.z <= (maxRel.z + tolY);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(relation), relation, null);
                }

                if (overlap1 && overlap2)
                {
                    valid.Add(main);
                    break;  // one hit is enough
                }
            }
        }

        return new List<GameObject>(valid);
    }

    /* ?????????????????????????????????????????????????????????????????????????????
     *  Utility helpers
     * ???????????????????????????????????????????????????????????????????????????*/

    /* Return the min / max projection of an AABB onto an arbitrary axis. */
    private static void GetMinMaxAlongAxis(
        Vector3 min, Vector3 max, Vector3 axis,
        out float minProj, out float maxProj)
    {
        minProj = float.PositiveInfinity;
        maxProj = float.NegativeInfinity;

        // enumerate the 8 corners (bit-trick)
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z);

            float proj = Vector3.Dot(corner, axis);
            if (proj < minProj) minProj = proj;
            if (proj > maxProj) maxProj = proj;
        }
    }

    /* Axis-interval overlap with tolerance */
    private static bool OverlapAlongAxis(
        Vector3 minMain, Vector3 maxMain,
        Vector3 minRel, Vector3 maxRel,
        Vector3 axis, float tol)
    {
        GetMinMaxAlongAxis(minMain, maxMain, axis, out float minM, out float maxM);
        GetMinMaxAlongAxis(minRel, maxRel, axis, out float minR, out float maxR);

        return maxM >= (minR - tol) && minM <= (maxR + tol);
    }


    //// This variation assumes that if a main candidate is "outside" of ANY related candiates
    ////  then it gets added to the list of valid objects to return
    //private static List<GameObject> EvalOutside(
    //List<GameObject> mains,
    //List<GameObject> related)
    //{
    //    float OutsideGap = TouchEpsilon;  // gap to consider "outside"
    //    var valid = new HashSet<GameObject>();

    //    foreach (var main in mains)
    //    {
    //        /* Gather sample points for "inside" test */
    //        var aabbMain = GetWorldSpaceAABB(main);
    //        Bounds bMain = new Bounds();
    //        bMain.SetMinMax(aabbMain.min, aabbMain.max);
    //        var samples = SampleBoundsPoints(bMain).ToArray();

    //        foreach (var rel in related)
    //        {
    //            bool anyPointInside = false;

    //            Collider colRel = EnsureMeshCollider(rel);
    //            if (colRel)
    //            {
    //                // Broad skip: bounds overlap? then MAYBE inside
    //                if (BoundsIntersect(colRel.bounds, bMain, AABBSlack))
    //                {
    //                    foreach (var p in samples)
    //                    {
    //                        if (PointInsideCollider(colRel, p))
    //                        {
    //                            anyPointInside = true;
    //                            break;
    //                        }
    //                    }
    //                }
    //            }
    //            else    /* fallback: AABB "inside" test */
    //            {
    //                var aabbRel = GetWorldSpaceAABB(rel);
    //                anyPointInside =
    //                    aabbMain.min.x >= aabbRel.min.x && aabbMain.max.x <= aabbRel.max.x &&
    //                    aabbMain.min.y >= aabbRel.min.y && aabbMain.max.y <= aabbRel.max.y &&
    //                    aabbMain.min.z >= aabbRel.min.z && aabbMain.max.z <= aabbRel.max.z;
    //            }

    //            /* If *main* is even partly inside, it cannot be "outside" */
    //            if (anyPointInside)
    //                continue;

    //            /* Check for clear separation (no touching / overlap) */
    //            bool separated;
    //            if (colRel && EnsureMeshCollider(main))
    //            {
    //                float gap = CollidersGap(EnsureMeshCollider(main), colRel);
    //                separated = gap > OutsideGap;
    //            }
    //            else
    //            {
    //                var aabbRel = GetWorldSpaceAABB(rel);
    //                Bounds bRel = new Bounds();
    //                bRel.SetMinMax(aabbRel.min, aabbRel.max);
    //                separated = BoundsSeparate(bMain, bRel, OutsideGap);
    //            }

    //            if (separated)
    //            {
    //                valid.Add(main);
    //                break;              // one "outside" relation is enough
    //            }
    //        }
    //    }

    //    return valid.ToList();
    //}


    private static List<GameObject> EvalOutside(
    List<GameObject> mains,
    List<GameObject> related)
    {
        float OutsideGap = TouchEpsilon;  // gap to consider "outside"
        var valid = new HashSet<GameObject>();

        foreach (var main in mains)
        {
            bool insideAny = false;   // becomes true the moment one rel contains main
            bool separatedEvery = true;    // becomes false if main touches / overlaps a rel

            // Cache main's data once
            var aabbMain = GetWorldSpaceAABB(main);
            Bounds bMain = new Bounds(); bMain.SetMinMax(aabbMain.min, aabbMain.max);
            var samples = SampleBoundsPoints(bMain).ToArray();
            Collider colMain = EnsureMeshCollider(main);

            foreach (var rel in related)
            {
                // "Inside?" check
                bool insideThisRel = false;

                Collider colRel = EnsureMeshCollider(rel);
                if (colRel)
                {
                    if (BoundsIntersect(colRel.bounds, bMain, AABBSlack))
                    {
                        foreach (var p in samples)
                        {
                            if (PointInsideCollider(colRel, p))
                            {
                                insideThisRel = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var aabbRel = GetWorldSpaceAABB(rel);
                    insideThisRel =
                        aabbMain.min.x >= aabbRel.min.x && aabbMain.max.x <= aabbRel.max.x &&
                        aabbMain.min.y >= aabbRel.min.y && aabbMain.max.y <= aabbRel.max.y &&
                        aabbMain.min.z >= aabbRel.min.z && aabbMain.max.z <= aabbRel.max.z;
                }

                if (insideThisRel)
                {
                    insideAny = true;               // immediately disqualifies "outside"
                    break;                          // no need to check other rels
                }

                // Separation check (must hold for **all** related objects)
                bool separatedThisRel;
                if (colRel && colMain)
                    separatedThisRel = CollidersGap(colMain, colRel) > OutsideGap;
                else
                {
                    var aabbRel = GetWorldSpaceAABB(rel);
                    Bounds bRel = new Bounds(); bRel.SetMinMax(aabbRel.min, aabbRel.max);
                    separatedThisRel = BoundsSeparate(bMain, bRel, OutsideGap);
                }

                if (!separatedThisRel)
                {
                    separatedEvery = false;         // touching / overlapping
                                                    // keep looping-main might still be inside another rel
                }
            }

            /* Final decision for this main */
            if (!insideAny && separatedEvery)
                valid.Add(main);
        }

        return valid.ToList();
    }


    private static List<GameObject> EvalNear(
        List<GameObject> mains,
        List<GameObject> related)
    {
        //var (a, b) = GetWorldSpaceAABB(mains[0]);
        //// Fin the min of width, height, and depth of the first main object
        //Vector3 size = a - b;  // gives width (x), height (y), depth (z)
        //float minDimension = Math.Abs(Mathf.Min(size.x, size.y, size.z));
        //float maxDistance = minDimension * 2;
        //MyLogger.Log($"EvalNear: using maxDistance = {maxDistance} (2 * min dimension of first main object).");

        float maxDistance = NearThreshold;
        MyLogger.Log($"EvalNear: using maxDistance = {maxDistance} (NearThreshold).");
        var valid = new HashSet<GameObject>();

        foreach (var main in mains)
        {
            Collider colMain = EnsureMeshCollider(main);
            Bounds aabbMain = colMain ? colMain.bounds :
                                new Bounds(); // will set later if no collider

            if (!colMain)                                   // fallback aabb
            {
                var (min, max) = GetWorldSpaceAABB(main);
                aabbMain.SetMinMax(min, max);
            }

            foreach (var rel in related)
            {
                Collider colRel = EnsureMeshCollider(rel);
                bool isNear = false;

                if (colMain && colRel)
                {
                    // PhysX gap test (uses convex hulls even for concave colliders)
                    Physics.ComputePenetration(
                        colMain, colMain.transform.position, colMain.transform.rotation,
                        colRel, colRel.transform.position, colRel.transform.rotation,
                        out _, out float dist);
                    // dist = 0 when overlapping; otherwise gap
                    isNear = dist <= maxDistance;
                }
                else
                {
                    // AABB distance fallback
                    Bounds aabbRel;
                    if (colRel)
                        aabbRel = colRel.bounds;
                    else
                    {
                        var (min, max) = GetWorldSpaceAABB(rel);
                        aabbRel = new Bounds();
                        aabbRel.SetMinMax(min, max);
                    }

                    float dist = BoundsDistance(aabbMain, aabbRel);
                    isNear = dist <= maxDistance;
                }

                if (isNear)
                {
                    valid.Add(main);
                    break;          // one "near" related object is enough
                }
            }
        }

        return valid.ToList();
    }


    /// Public field exposes the winning related object, in case you need it.
    public static GameObject ClosestTo_Related { get; private set; }

    private static List<GameObject> EvalClosestTo(
        List<GameObject> mains,
        List<GameObject> related)
    {
        if (mains.Count == 0 || related.Count == 0) return new();

        GameObject bestMain = null;
        GameObject bestRel = null;
        float bestDist = float.PositiveInfinity;

        foreach (var rel in related)
        {
            foreach (var main in mains)
            {
                float d = DistanceBetween(main, rel);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestMain = main;
                    bestRel = rel;
                }
            }
        }

        ClosestTo_Related = bestRel;          // expose for inspection
        return bestMain ? new List<GameObject> { bestMain } : new List<GameObject>();
    }


    private static List<GameObject> EvalClosestPerRelated(
    List<GameObject> mains,
    List<GameObject> related)
    {
        ClosestPerRelated.Clear();
        var winners = new HashSet<GameObject>();

        foreach (var rel in related)
        {
            GameObject bestMain = null;
            float bestDist = float.PositiveInfinity;

            foreach (var main in mains)
            {
                float d = DistanceBetween(main, rel);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestMain = main;
                }
            }

            if (bestMain != null)
            {
                ClosestPerRelated[rel] = bestMain;
                winners.Add(bestMain);          // keep unique mains
            }
        }

        return winners.ToList();                // 1 - R mains
    }


    private static List<GameObject> EvalBetween(
    List<GameObject> mains,
    List<GameObject> relatedA,
    List<GameObject> relatedB)
    {
        const float LateralSlope = 0.25f;   // m tolerance per 1 m AB separation
        const float MaxLateralTol = 2.0f;    // hard cap

        if (relatedA.Count == 0 || relatedB.Count == 0) return new();

        /* Pre-compute centres for speed */
        var centresA = relatedA.Select(GetAABBCentre).ToArray();
        var centresB = relatedB.Select(GetAABBCentre).ToArray();

        var valid = new List<GameObject>();

        foreach (var main in mains)
        {
            Vector3 cM = GetAABBCentre(main);
            bool hit = false;

            /* Test against every (A,B) pair */
            foreach (Vector3 a in centresA)
            {
                foreach (Vector3 b in centresB)
                {
                    Vector3 ab = b - a;
                    float lenAB = ab.magnitude;
                    if (lenAB < 1e-4f) continue;          // degenerate pair

                    /* 1 - projection of AM onto AB */
                    float t = Vector3.Dot(cM - a, ab) / (lenAB * lenAB);
                    if (t < 0f || t > 1f) continue;       // outside segment

                    Vector3 proj = a + t * ab;

                    /* 2 - lateral distance to AB */
                    float lateralTol = Mathf.Min(MaxLateralTol, lenAB * LateralSlope);
                    float distToLine = Vector3.Cross(ab.normalized, cM - a).magnitude;

                    if (distToLine <= lateralTol)
                    {
                        hit = true;
                        break;                            // break out of B-loop
                    }
                }
                if (hit) break;                           // break out of A-loop
            }

            if (hit) valid.Add(main);
        }

        return valid;
    }


}
