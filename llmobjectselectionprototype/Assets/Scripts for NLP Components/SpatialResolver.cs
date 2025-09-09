/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static NLPServerCommunicator;

/// <summary>
/// Resolves the subset of <see cref="GameObject"/>s that satisfy a chain-style set of
/// spatial constraints (e.g. "laptop on desk beside chalkboard").
/// Every major step is instrumented with <c>MyLogger.Log("[SPATREL] ...")</c> so you can
/// filter the Unity Console for "[SPATREL]" and watch the algorithm work in real time.
/// </summary>
/// 
/// <summary>
/// <para>
/// <b>SpatialResolver</b> narrows an initial set of focus-objects (e.g. all laptops)
/// down to those that satisfy an entire <i>chain</i> of spatial
/// relationships extracted from natural-language input.
///
/// The algorithm works depth-first from the leaves of the relationship graph
/// towards the overall focus object so that **each predicate is applied only to
/// already-filtered candidates**.  
/// In practice that means a sentence such as
///
/// <code>"the laptop on the desk beside the chalkboard"</code>
///
/// is evaluated in the intuitive order:
///
/// 1. <code>desk <c>beside</c> chalkboard</code> &nbsp;->&nbsp; subset of desks  
/// 2. <code>laptop <c>on</c> (subset-of-desks)</code> &nbsp;->&nbsp; final subset of laptops
///
/// At every significant step the class emits a
/// <c>MyLogger.Log("[SPATREL] ...")</c> line so that developers can
/// filter the Unity Console by the tag <c>[SPATREL]</c>.
/// </para>
///
/// <remarks>
/// <para>
/// **Key design points**
/// <list type="bullet">
///   <item><description>
///   Each object-name is fetched from the scene **once** and then cached in
///   <see cref="rawCandidates"/> so repeated visits are free.
///   </description></item>
///   <item><description>
///   A node is never filtered until every <i>related</i> node beneath it has
///   already been resolved and cached in <see cref="finalCandidates"/>.
///   </description></item>
///   <item><description>
///   The focus object's starting candidates are injected from outside and are
///   <b>never</b> re-queried with <see cref="GetRelatedObjects.Instance"/>.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
///
/// <example>
/// <code>
/// // Example set-up for the sentence
/// //   "the laptop on the desk beside the chalkboard"
///
/// var container = new SpatialRelationshipContainer {
///     relationships = new[] {
///         new SpatialRelationship { main_object = "laptop",
///                                   spatial_relation = "on",
///                                   related_object = "desk" },
///         new SpatialRelationship { main_object = "desk",
///                                   spatial_relation = "beside",
///                                   related_object = "chalkboard" }
///     }
/// };
///
/// // Suppose we have already located every laptop:
/// List&lt;GameObject&gt; initialLaptops = FindAllLaptops();   // returns 4 items
///
/// // Kick off the resolver coroutine:
/// StartCoroutine(
///     spatialResolver.ResolveFocusObject(
///         container,
///         focusObjectName : "laptop",
///         focusObjectInitialSet : initialLaptops,
///         onDone : finalLaptops =>
///         {
///             Debug.Log($"Filtered down to {finalLaptops.Count} laptop(s).");
///         }));
/// </code>
/// </example>
/// </summary>
public class SpatialResolver : MonoBehaviour
{
    // ----------------------------------------------------------
    // PUBLIC ENTRY POINT
    // ----------------------------------------------------------
    /// <summary>
    /// Launches the recursive filtering coroutine.
    /// </summary>
    /// <param name="relationships">
    /// A container whose <c>.relationships</c> array encodes one directed edge
    /// per spatial predicate in the sentence.
    /// </param>
    /// <param name="focusObjectName">The object-name that should remain after all filters.</param>
    /// <param name="focusObjectInitialSet">
    /// The starting <see cref="GameObject"/>s for the focus, typically fetched
    /// earlier by a semantic search.
    /// </param>
    /// <param name="onDone">
    /// Callback invoked with the final filtered list when the coroutine finishes.
    /// </param>
    /// <returns>A Unity <see cref="Coroutine"/> handle.</returns>
    public IEnumerator ResolveFocusObject(
        NLPServerCommunicator.SpatialRelationshipContainer spatRelContainer,
        string focusObjectName,
        List<GameObject> focusObjectInitialSet,
        Action<List<GameObject>> onDone)
    {
        MyLogger.Log($"[SPATREL] -> ResolveFocusObject  | focus='{focusObjectName}'  | " +
                     $"initialSet={focusObjectInitialSet.Count}  | relContainerNull?={spatRelContainer == null}");

        /*----------------------------------------------------------------------
         * 1.  Build quick-lookup maps so we can ask:
         *     - "What edge(s) leave this node?"
         *     - "Have I already resolved this node?"
         *----------------------------------------------------------------------*/
        var relArray = spatRelContainer?.relationships
                       ?? Array.Empty<NLPServerCommunicator.SpatialRelationship>();

        /*  outgoingByMain :  key = full main_object string
         *                    val = all edges that leave that string
         */
        var outgoingByMain = relArray
            .GroupBy(r => r.main_object, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,                   // key
                g => g.ToList(),              // value
                StringComparer.OrdinalIgnoreCase);

        MyLogger.Log($"[SPATREL]   Built outgoingByMain  | nodes={outgoingByMain.Count}");

        /*  Cache dictionaries  */
        var rawCandidates = new Dictionary<string, List<GameObject>>();     // before filtering
        var finalCandidates = new Dictionary<string, List<GameObject>>();   // after filtering

        /*  keeps track of nodes currently on the recursion stack */
        var activeStack = new HashSet<string>();

        /*  Pre-seed cache with the focus object's candidates so we never
         *  re-query it in EnsureRawCandidates().
         */
        rawCandidates[focusObjectName] = new List<GameObject>(focusObjectInitialSet);

        /* -------------------------------------------------------------------
         *  A)  HELPER - robust outgoing lookup
         * ------------------------------------------------------------------- */
        bool TryGetOutgoing(string objName,
                            out List<NLPServerCommunicator.SpatialRelationship> outgoing)
        {
            /* 1. exact match -------------------------------------------------- */
            if (outgoingByMain.TryGetValue(objName, out outgoing))
                return true;

            /* 2. head-noun fallback: use the last word in a multi-word phrase  */
            // e.g. "ceiling light" ? "light"
            var tokens = objName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                string head = tokens[^1];                         // last token
                if (outgoingByMain.TryGetValue(head, out outgoing))
                {
                    MyLogger.Log($"[SPATREL]     Head-noun match '{objName}' ? '{head}' "
                               + $"(edges={outgoing.Count})");
                    return true;
                }
            }

            /* 3. descriptor+objName ("blue cube" ? "cube") -------------------- */
            string altKey = outgoingByMain.Keys
                .Where(k =>
                       k.Length > objName.Length &&               // really an extension
                       k.EndsWith(" " + objName,
                                StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Length)
                .FirstOrDefault();

            if (altKey != null)
            {
                outgoing = outgoingByMain[altKey];
                MyLogger.Log($"[SPATREL]     Alias-match '{objName}' ? '{altKey}' "
                           + $"(edges={outgoing.Count})");
                return true;
            }

            /* 4. no luck ------------------------------------------------------ */
            outgoing = null;
            return false;
        }


        /*----------------------------------------------------------------------
         * 2.  Local coroutine: fetch raw candidates for any object-name once,
         *     caching the result so repeated visits are cheap.
         *----------------------------------------------------------------------*/
        IEnumerator EnsureRawCandidates(string objName, Action onFetched)
        {
            if (rawCandidates.ContainsKey(objName))
            {
                MyLogger.Log($"[SPATREL]     EnsureRawCandidates  | '{objName}' cached ({rawCandidates[objName].Count})");
                onFetched();
                yield break;
            }

            MyLogger.Log($"[SPATREL]     EnsureRawCandidates  | '{objName}' -> ProcessCommand()");
            List<GameObject> fetched = null;

            if (objName == "*user")
            {
                // Get reference to the main camera
                fetched = new List<GameObject> { Camera.main.gameObject };
                rawCandidates[objName] = fetched;
            }
            else
            {
                yield return StartCoroutine(
                    GetRelatedObjects.Instance.ProcessCommand(
                        objName,
                        matches => fetched = matches));
            }

            fetched ??= new List<GameObject>();

            MyLogger.Log($"[SPATREL]     EnsureRawCandidates  | '{objName}' fetched={fetched.Count}");
            rawCandidates[objName] = fetched;
            onFetched();
        }

        /*----------------------------------------------------------------------
         * 3.  Local coroutine: resolve (i.e. filter) one node and cache the
         *     result in finalCandidates.  Uses DFS so all dependencies
         *     (edges further down the chain) are resolved first.
         *----------------------------------------------------------------------*/
        IEnumerator ResolveNode(string objName, Action onResolved)
        {
            if (finalCandidates.ContainsKey(objName))
            {
                MyLogger.Log($"[SPATREL]   ResolveNode '{objName}' already finalised ({finalCandidates[objName].Count})");
                onResolved();
                yield break;
            }

            /* ------------------- 2. CYCLE GUARD -------------------------- */
            if (activeStack.Contains(objName))
            {
                MyLogger.LogWarning($"[SPATREL] CYCLE detected at '{objName}'. "
                           + "Treating as leaf to avoid infinite recursion.");
                // make sure node exists in finalCandidates, even if empty
                finalCandidates[objName] = rawCandidates.ContainsKey(objName)
                                         ? rawCandidates[objName]
                                         : new List<GameObject>();
                onResolved();
                yield break;
            }

            activeStack.Add(objName);
            MyLogger.Log($"[SPATREL]   ResolveNode -> '{objName}'");

            /*  3a) Ensure we have raw (unfiltered) candidates */
            yield return StartCoroutine(EnsureRawCandidates(objName, () => { }));

            /*  3b) Leaf node?  (No outgoing edges)  
             *       Its raw set IS its final set.
             *   (use robust lookup) ------------ */
            if (!TryGetOutgoing(objName, out var outgoing))
            {
                finalCandidates[objName] = rawCandidates[objName];
                MyLogger.Log($"[SPATREL]   ResolveNode | '{objName}' is leaf  | "
                           + $"final={finalCandidates[objName].Count}");
                activeStack.Remove(objName);
                onResolved();
                yield break;
            }

             //*  LOCAL HELPER  -> returns the two 'between' edges
            IEnumerable<NLPServerCommunicator.SpatialRelationship>
            GetBetweenGroup(NLPServerCommunicator.SpatialRelationship e) =>
                outgoing.Where(x =>
                    x.spatial_relation.Equals("between", StringComparison.OrdinalIgnoreCase) &&
                    x.main_object.Equals(e.main_object, StringComparison.OrdinalIgnoreCase));

            // Resolve every related node first
            foreach (var edge in outgoing)
            {
                MyLogger.Log($"[SPATREL]     ResolveNode '{objName}' -> recurse on related '{edge.related_object}'");
                yield return StartCoroutine(ResolveNode(edge.related_object, () => { }));
            }

            // Filter this node's raw set against each edge's predicate
            List<GameObject> current = rawCandidates[objName];
            MyLogger.Log($"[SPATREL]     ResolveNode '{objName}' | starting candidates={current.Count}");

            foreach (var edge in outgoing)
            {
                if (edge.spatial_relation.Equals("between",
                          StringComparison.OrdinalIgnoreCase))
                {
                    // collect BOTH sides ???
                    var group = GetBetweenGroup(edge).ToList();
                    if (group.Count != 2)
                    {
                        Debug.LogWarning($"[SPATREL] Expected exactly 2 related objects "
                                       + $"for 'between' but got {group.Count}.");
                        continue;   // or treat as failure
                    }

                    var setA = finalCandidates[group[0].related_object];
                    var setB = finalCandidates[group[1].related_object];

                    current = SpatialRelationEvaluator.Evaluate(
                                  "between",
                                  current,
                                  setA,
                                  setB);
                }
                else
                {
                    var relatedSet = finalCandidates[edge.related_object];
                    current = SpatialRelationEvaluator.Evaluate(
                                  edge.spatial_relation,
                                  current,
                                  relatedSet);
                }
            }

            finalCandidates[objName] = current;
            MyLogger.Log($"[SPATREL]   ResolveNode <- '{objName}' done  | final={current.Count}");
            onResolved();
        }

        /*----------------------------------------------------------------------
         * 4.  Kick off the recursion starting from the focus object.
         *----------------------------------------------------------------------*/
        yield return StartCoroutine(ResolveNode(focusObjectName, () => { }));

        MyLogger.Log($"[SPATREL] <- ResolveFocusObject finished  | result={finalCandidates[focusObjectName].Count}");
        onDone(finalCandidates[focusObjectName]);
    }
}
