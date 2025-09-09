/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static NLPServerCommunicator;
using static MyLogger;
using System.Linq;
using System;

public class GetRelatedObjects : MonoBehaviour
{
    private static GetRelatedObjects _instance;
    public static GetRelatedObjects Instance
    {
        get
        {
            if (_instance == null)
            {
                // Look for one that is already alive first
                _instance = FindFirstObjectByType<GetRelatedObjects>();

                // If still null create one automatically.
                // Safer: just warn so the dev knows the singleton is missing.
                if (_instance == null)
                {
                    Debug.LogError(
                        "No GetRelatedObjects in the scene. " +
                        "Add one to a GameObject before accessing Instance."
                    );
                }
            }

            return _instance;
        }
    }

    public string relatedObjectText;

    public float nameDistanceThreshold = 0.2f;              // Distance threshold for name matches
    public float descDistanceThreshold = 0.2f;              // Distance threshold for desc matches
    public float intersectionDistanceThreshold = 0.2f;      // Distance threshold for intersection matches
    public float maxCombinedDistanceAccepted = 2.0f;        // Maximum distance accepted for a match in FAISS searches
    public float maxDistanceForSpatialCandidates = 1.0f;    // max distance threshold when finding candidate objects for spatial relationships
    public int topK = 50;                                   // Number of top matches to return from FAISS


    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning(
                $"Duplicate GetRelatedObjects on {gameObject.name}. Destroying this copy."
            );
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>
    /// High-level method that:
    /// 1) Counts how many objects appear in the user's command.
    /// 2) Decides if there is one or multiple objects.
    /// 3) Delegates the actual selection logic to separate methods.
    /// TODO update this summary based on new logic
    /// </summary>
    public IEnumerator ProcessCommand(
        string objectToSelect,
        Action<List<GameObject>> onComplete)
    {
        // I'm just copying the same logic from SElectionController algorithm, which assumes a command input
        // so if the object to select is "the big shiny red chair" we need to change it to a command format like
        // "Select the big shiny red chair" so that NLPServerCommunicator can process it correctly.

        relatedObjectText = "Select the " + objectToSelect; // This is the command we will use to get the objects and attributes

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1a: Get all the objects names (text) with their descriptions from the command 
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var (allObjectsAndAttrributes, objectCount) = (default(ObjectsWithAttributesResponse), 0);
        yield return StartCoroutine(
            GetAllObjectsAndAttributesCoroutine(relatedObjectText, (resp, count) =>
            {
                allObjectsAndAttrributes = resp;
                objectCount = count;
            })
        );

        if (allObjectsAndAttrributes == null)
        {
            MyLogger.LogWarning($"No objects found in text: '{relatedObjectText}'");
            yield break;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1b: Try get the main object and descriptors in the command
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var (mainObjectAndDescriptors, mainObject) = (default(MainObjectWithDescriptorsResponse), (string)null);
        yield return StartCoroutine(
            GetMainObjectAndDescriptorsCoroutine(relatedObjectText, (resp, objName) =>
            {
                mainObjectAndDescriptors = resp;
                mainObject = objName;
            })
        );

        if (mainObjectAndDescriptors == null || string.IsNullOrEmpty(mainObject))
        {
            MyLogger.LogWarning($"No main object found in command: '{objectToSelect}'");
            yield break;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1c: Run FAISS searches to try resolve the main object into an actual GameObject
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        List<GameObject> gameObjectsToSelect = null;

        if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Traditional_NLP ||
            SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.LLM_Assisted)
        {
            yield return StartCoroutine(
                FindMatchForMainObjectCoroutine(
                    mainObject,
                    mainObjectAndDescriptors,
                    allObjectsAndAttrributes,
                    (selectedObjs) =>
                    {
                        gameObjectsToSelect = selectedObjs;
                    })
            );
        }
        else if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Full_LLM)
        {
            yield return StartCoroutine(
                FindMatchForMainObjectCoroutineUsingOpenAI(
                    mainObject,
                    mainObjectAndDescriptors,
                    allObjectsAndAttrributes,
                    (selectedObjs) =>
                    {
                        gameObjectsToSelect = selectedObjs;
                    },
                    relatedObjectText)
            );
        }

        
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 2: If still no match, we can check if there's spatial relationships between objects that 
        ////////         can refine our results
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        // TODO
        // Check if there's any relational attirubtes such as 'rightmost', 'leftmost', 'largest', etc.
        //  if not, or if they don't result in a main object match, then we can move on with other queries
        // NOTE: This block is only checking relational attributes for the main object, not all objects in the command.
        //       Future improvement could be to check all objects and attributes in the command.
        if (allObjectsAndAttrributes != null)
        {
            foreach (SpacyObject obj in allObjectsAndAttrributes.objects)
            {
                if (obj.head == mainObject)
                {
                    // If the object has descriptors, we can use them to refine the search
                    if (obj.descriptors != null && obj.descriptors.Count > 0)
                    {
                        List<GameObject> validMainsFromDescriptiveRels = DescriptiveRelationEvaluator.Evaluate(
                                                                            obj.descriptors,
                                                                            gameObjectsToSelect);

                        // If we found any valid main objects from the descriptive relations, we can use them
                        if (validMainsFromDescriptiveRels.Count > 0)
                        {
                            gameObjectsToSelect = validMainsFromDescriptiveRels;
                        }
                    }
                    else
                    {
                        MyLogger.Log($"Object '{mainObject}' has no descriptors.");
                    }
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 2: Return the references to all the objects found
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///

        onComplete?.Invoke(gameObjectsToSelect);
    }




    /// <summary>
    /// Coroutine that attempts to find a single clear match for the main object
    /// using FAISS name and description searches. Calls back with the selected GameObject (or null if not found).
    /// </summary>
    public IEnumerator FindMatchForMainObjectCoroutineUsingOpenAI(
        string mainObject,
        MainObjectWithDescriptorsResponse mainObjectAndDescriptors,
        ObjectsWithAttributesResponse allObjectsAndAttrributes,
        Action<List<GameObject>> onComplete,
        string command = "" // Optional command to use for name matches, if not provided will use mainObject
    )
    {
        // 1) First, search by name
        // Run OpenAI version of FAISS searches to try resolve the main object into an actual GameObject
        List<GameObject> gameObjectsToSelect = new List<GameObject>();
        if (mainObject != "none")
        {
            ObjectMatchContainer matchesNames = null;
            yield return StartCoroutine(
                GetObjectNameMatchesCoroutine(command, mainObject, (resp) => matchesNames = resp));

            // Act on the matches
            if (matchesNames == null || matchesNames.object_matches.Count == 0)
            {
                Debug.Log("Nothing in the scene matches that request.");
            }
            else
            {
                foreach (ObjectMatch match in matchesNames.object_matches)
                {
                    MyLogger.Log($"Found match: {match.query_name} (ID: {match.id})");
                    GameObject foundGO = GameObjectManager.Instance.GetObjectById(match.id)?.gameObject;
                    if (foundGO != null)
                    {
                        gameObjectsToSelect.Add(foundGO);
                    }
                }
            }
        }


        SelectionController.Instance?.MarkStage(ProcStage.NameSearch);

        // If there is exactly one match, it's a clear result.
        if (gameObjectsToSelect.Count == 1)
        {
            MyLogger.Log("Found a clear match by name with object name: " + gameObjectsToSelect[0].name);
            onComplete?.Invoke(gameObjectsToSelect);
            yield break; // Return the single match
        }


        // If still no clear match, we attempt to refine using description
        if (mainObjectAndDescriptors.descriptors != null || mainObject == "none")
        {
            // If the difference is not significant, we have an ambiguous case.
            // Perform a FAISS search on the object description.
            // If there's multiple matches on description we can check for overlap between object id's
            // Best way to get teh descriptors is by getting the full phrase used in the command which is object_text var in SpacyObject
            yield return StartCoroutine(
                TryFaissDescriptionSearchCoroutineUsingOpenAI(
                    mainObject,
                    allObjectsAndAttrributes,
                    (foundObjs) =>
                    {
                        if (foundObjs != null && foundObjs.Count > 0)
                        {
                            gameObjectsToSelect = foundObjs;
                        }
                    },
                    command
                )
            );
        }
        else
        {
            // The top name-based match was ambiguous
            if (gameObjectsToSelect.Count == 0)
            {
                MyLogger.Log("Name was ambiguous with other objects, and no object descriptors were given for a more detailed query. No clear match found.");
            }
        }

        // Callback with the selected object
        onComplete?.Invoke(gameObjectsToSelect);
    }

    /// <summary>
    /// Attempts to refine an ambiguous FAISS name search using a description search and intersection checks.
    /// </summary>
    private IEnumerator TryFaissDescriptionSearchCoroutineUsingOpenAI(
        string mainObject,
        ObjectsWithAttributesResponse allObjectsAndAttrributes,
        Action<List<GameObject>> onComplete,
        string command = "" // Optional command to use for description matches, if not provided will use mainObject
    )
    {
        bool isDone = false;
        List<GameObject> foundObjs = new List<GameObject>();    // For storing either the single match or all possible matches

        // Gather the full chunk of text used for the main object from allObjectsAndAttributes
        string mainObjectChunk = "";
        foreach (SpacyObject obj in allObjectsAndAttrributes.objects)
        {
            if (obj.head == mainObject)
            {
                mainObjectChunk = obj.object_text;
                break;
            }
        }

        // If there's no match let's assume the first object in allObjectsAndAttrributes is the main object
        //if (mainObjectChunk == "")
        //{
        //    if (allObjectsAndAttrributes != null && allObjectsAndAttrributes.objects != null && allObjectsAndAttrributes.objects.Count > 0)
        //    {
        //        // Use the first object as a fallback
        //        List<SpacyObject> objs = allObjectsAndAttrributes.objects;
        //        mainObjectChunk = objs[0].object_text;
        //    }
        //}
        if (mainObjectChunk == "")
        {
            mainObjectChunk = mainObject; // If no chunk found, use the main object as a fallback
        }

        ObjectDescriptionMatchContainer matchesDescs = null;

        // Call the communicator.  It *itself* starts a coroutine that
        // posts JSON; we just need to wait until its callback fires.
        MyLogger.Log($"[GetObjectDescrMatchesCoroutine] Searching openai for desc matches for: {mainObjectChunk} in command: {command}");
        NLPServerCommunicator.Instance.GetObjectDescMatchesUsingOpenAI(
            command,
            mainObjectChunk,
            (resp) =>
            {
                matchesDescs = resp;
                isDone = true;
            });

        // Yield until the HTTP call comes back
        yield return new WaitUntil(() => isDone);


        // Act on the matches
        if (matchesDescs == null || matchesDescs.object_matches.Count == 0)
        {
            Debug.Log("Nothing in the scene matches that request.");
        }
        else
        {
            foreach (ObjectDescriptionMatch match in matchesDescs.object_matches)
            {
                MyLogger.Log($"Found match: {match.query_name} (ID: {match.id})");
                GameObject foundGO = GameObjectManager.Instance.GetObjectById(match.id)?.gameObject;
                if (foundGO != null)
                {
                    foundObjs.Add(foundGO);
                }
            }
        }

        MyLogger.Log($"[TryFaissDescriptionSearchCoroutine] Found {foundObjs.Count} objects matching description '{mainObjectChunk}'");
        onComplete?.Invoke(foundObjs);                                                              // Return the whole list with single match
        yield break;
    }


    /// <summary>
    /// Coroutine to get all objects and attributes in the command.
    /// </summary>
    private IEnumerator GetAllObjectsAndAttributesCoroutine(
        string command,
        Action<ObjectsWithAttributesResponse, int> onComplete
    )
    {
        bool isDone = false;
        ObjectsWithAttributesResponse allObjectsAndAttrributes = null;
        int objectCount = 0;

        // Call the NLP server to get all object names and attributes from command
        if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Traditional_NLP ||
            SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.LLM_Assisted)
        {
            NLPServerCommunicator.Instance.GetObjectsWithAttributesUsingSpacy(command, (response) => {
                allObjectsAndAttrributes = response;
                isDone = true;
            });
        }
        else if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Full_LLM)
        {
            NLPServerCommunicator.Instance.GetObjectsWithAttributesUsingOpenAI(command, (response) => {
                allObjectsAndAttrributes = response;
                SelectionController.Instance.AddToProcessingCost(response.cost_usd);
                isDone = true;
            });
        }

        yield return new WaitUntil(() => isDone);

        // Count the number of objects and log them
        if (allObjectsAndAttrributes != null)
        {
            foreach (SpacyObject obj in allObjectsAndAttrributes.objects)
            {
                if (!string.IsNullOrEmpty(obj.head))
                {
                    obj.head = obj.head.ToLower();
                }

                if (obj.descriptors != null)
                {
                    for (int i = 0; i < obj.descriptors.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(obj.descriptors[i]))
                        {
                            obj.descriptors[i] = obj.descriptors[i].ToLower();
                        }
                    }
                }
            }

            foreach (SpacyObject obj in allObjectsAndAttrributes.objects)
            {
                // Handle null list by using an empty list if needed.
                string attributes = obj.descriptors != null
                    ? string.Join(", ", obj.descriptors)
                    : "None";
                // Convert all atributes to lowercase for consistency
                attributes = attributes.ToLower();

                MyLogger.Log($"Found object: {obj.head} with attributes: {attributes}");
                objectCount++;
            }
        }

        onComplete?.Invoke(allObjectsAndAttrributes, objectCount);
    }

    /// <summary>
    /// Coroutine to get the main object and descriptors in the command.
    /// </summary>
    private IEnumerator GetMainObjectAndDescriptorsCoroutine(
        string command,
        Action<MainObjectWithDescriptorsResponse, string> onComplete
    )
    {
        bool isDone = false;
        MainObjectWithDescriptorsResponse mainObjectAndDescriptors = null;
        string mainObject = null;


        if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Traditional_NLP ||
            SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.LLM_Assisted)
        {
            NLPServerCommunicator.Instance.GetMainObjectAndDescriptorsInCommand(command, (response) => {
                mainObjectAndDescriptors = response;
                isDone = true;
            });
        }
        else if (SelectionController.Instance.PipelineVersion == SelectionController.SelectionPipelineVersion.Full_LLM)
        {
            NLPServerCommunicator.Instance.GetMainObjectAndDescriptorsInCommandUsingOpenAI(command, (response) => {
                mainObjectAndDescriptors = response;
                SelectionController.Instance.AddToProcessingCost(response.cost_usd);
                isDone = true;
            });
        }

        yield return new WaitUntil(() => isDone);

        // Log the main object and descriptors
        if (mainObjectAndDescriptors != null)
        {
            mainObject = mainObjectAndDescriptors.main_object.ToLower();
            string allDescriptors = "";
            foreach (string descriptor in mainObjectAndDescriptors.descriptors)     // loop through the descriptors and print them
            {
                allDescriptors = descriptor.ToLower() ?? "None";
            }
            MyLogger.Log($"Found main object: {mainObject} with attributes: {allDescriptors}");
        }

        onComplete?.Invoke(mainObjectAndDescriptors, mainObject);
    }

    /// <summary>
    /// Coroutine that attempts to find a single clear match for the main object
    /// using FAISS name and description searches. Calls back with the selected GameObject (or null if not found).
    /// </summary>
    public IEnumerator FindMatchForMainObjectCoroutine(
        string mainObject,
        MainObjectWithDescriptorsResponse mainObjectAndDescriptors,
        ObjectsWithAttributesResponse allObjectsAndAttrributes,
        Action<List<GameObject>> onComplete
    )
    {
        bool isDone = false;
        int topK = this.topK;                                                       // how many matches to fetch
        float nameDistanceThreshold = this.nameDistanceThreshold;                   // distance threshold for a "clear" match
        float descDistanceThreshold = this.descDistanceThreshold;                   // distance threshold for a "clear" match
        float intersectionDistanceThreshold = this.intersectionDistanceThreshold;   // distance threshold for a "clear" match
        FAISSResponse faissRespNames = null;

        // 1) First, search by name
        NLPServerCommunicator.Instance.SearchObjectByNameInFAISSEmbedding(
            mainObject,
            topK,
            (response) =>
            {
                faissRespNames = response;
                isDone = true;
            }
        );

        yield return new WaitUntil(() => isDone);
        isDone = false;

        List<GameObject> selectedObjects = new List<GameObject>();
        string mainObjectID = null;

        if (faissRespNames != null && faissRespNames.matches != null && faissRespNames.matches.Length > 0)
        {
            // --------------------------------------------------------------------
            // Debug Logging: Print out all FAISS name matches & distances
            // --------------------------------------------------------------------
            PrintFAISSMatches("Name", faissRespNames.matches);

            // If there is exactly one match, it's a clear result.
            if (faissRespNames.matches.Length == 1)
            {
                mainObjectID = faissRespNames.matches[0].id;
                // HIGHLIGHT OBJ AND YIELD FOR DEBUGGING PURPOSES
                GameObject obj = GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject;
                selectedObjects.Add(GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject);
                MyLogger.Log("Found a clear match by name with object id: " + mainObjectID);
            }
            else // more than 1 match
            {
                // Check the top two matches to see if there's a "clear" winner
                var first = faissRespNames.matches[0];
                var second = faissRespNames.matches[1];
                // note, that NLPServerCommunicator.Instance.SearchObjectByNameInFAISSEmbedding also has a DistanceThreshold
                //   which is used to stop the search short. That threshold needs to be bigger than this threshold. Might want 
                //   to rethink this in the future. However I have them seperate so that the distances between searches can be visualized
                //   in the debug log of Unity rather than all the matches being filtered out on the python server side first.
                if (second.distance - first.distance > nameDistanceThreshold)
                {
                    mainObjectID = first.id;
                    // HIGHLIGHT OBJ AND YIELD FOR DEBUGGING PURPOSES
                    GameObject obj = GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject;
                    selectedObjects.Add(GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject);
                    MyLogger.Log("Found a clear match by name with object id: " + mainObjectID);
                }
            }

            SelectionController.Instance?.MarkStage(ProcStage.NameSearch);

            // If still no clear match (mainObjetID still null), we attempt to refine using description
            if (mainObjectID == null && mainObjectAndDescriptors.descriptors != null)
            {
                // If the difference is not significant, we have an ambiguous case.
                // Perform a FAISS search on the object description.
                // If there's multiple matches on description we can check for overlap between object id's
                // Best way to get teh descriptors is by getting the full phrase used in the command which is object_text var in SpacyObject
                yield return StartCoroutine(
                    TryFaissDescriptionSearchCoroutine(
                        mainObject,
                        descDistanceThreshold,
                        intersectionDistanceThreshold,
                        allObjectsAndAttrributes,
                        faissRespNames,
                        mainObjectAndDescriptors,
                        (foundObjs) =>
                        {
                            if (foundObjs != null && foundObjs.Count > 0)
                            {
                                selectedObjects = foundObjs;
                            }
                        }
                    )
                );
            }
            else
            {
                // The top name-based match was ambiguous
                if (selectedObjects.Count == 0)
                {
                    MyLogger.Log("Name was ambiguous with other objects, and no object descriptors were given for a more detailed query. No clear match found.");
                }
            }
        }
        else
        {
            MyLogger.LogWarning("FAISS did not return any name matches.");
        }

        // Callback with the selected object
        onComplete?.Invoke(selectedObjects);
    }

    /// <summary>
    /// Attempts to refine an ambiguous FAISS name search using a description search and intersection checks.
    /// </summary>
    private IEnumerator TryFaissDescriptionSearchCoroutine(
        string mainObject,
        float descDistanceThreshold,
        float intersectionDistanceThreshold,
        ObjectsWithAttributesResponse allObjectsAndAttrributes,
        FAISSResponse faissRespNames,
        MainObjectWithDescriptorsResponse mainObjectAndDescritors,
        Action<List<GameObject>> onComplete
    )
    {
        bool isDone = false;
        FAISSResponse faissRespDescriptions = null;
        List<GameObject> foundObjs = new List<GameObject>();    // For storing either the single match or all possible matches

        // Gather the full chunk of text used for the main object from allObjectsAndAttributes
        string mainObjectChunk = "";
        foreach (SpacyObject obj in allObjectsAndAttrributes.objects)
        {
            if (obj.head == mainObject)
            {
                mainObjectChunk = obj.object_text;
                break;
            }
        }

        // If there's no match let's assuming the first object in allObjectsAndAttrributes is the main object
        if (mainObjectChunk == "")
        {
            List<SpacyObject> objs = allObjectsAndAttrributes.objects;
            if (objs != null && objs.Count > 0)
            {
                mainObjectChunk = objs[0].object_text;
            }
        }

        if (mainObjectChunk != "")      // This is a quick fix, but should come up with a proper solution TODO
        {
            NLPServerCommunicator.Instance.SearchObjectByDescriptionInFAISSEmbedding(
                mainObjectChunk,
                50,
                (response) =>
                {
                    faissRespDescriptions = response;
                    isDone = true;
                }
            );

            yield return new WaitUntil(() => isDone);
        }

        if (faissRespDescriptions != null && faissRespDescriptions.matches != null && faissRespDescriptions.matches.Length > 0)
        {
            // --------------------------------------------------------------------
            // Debug Logging: Print out all FAISS description matches & distances
            // --------------------------------------------------------------------
            MyLogger.Log($"[FAISS:Description] matches for '{mainObjectChunk}':");
            PrintFAISSMatches("Description", faissRespDescriptions.matches);

            // If exactly one match from description, it's a clear result.
            if (faissRespDescriptions.matches.Length == 1)
            {
                string mainObjectID = faissRespDescriptions.matches[0].id;
                MyLogger.Log("Found a clear match by description with object id: " + mainObjectID);
                foundObjs.Add(GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject);          // Add the single match
                onComplete?.Invoke(foundObjs);                                                              // Return the whole list with single match
                yield break;
            }
            else
            {
                // Assume the matches are sorted by increasing distance (lower is better)
                // Compare top two for a "clear" winner
                var bestDesc = faissRespDescriptions.matches[0];
                var secondDesc = faissRespDescriptions.matches[1];
                if (secondDesc.distance - bestDesc.distance > descDistanceThreshold)
                {
                    string mainObjectID = bestDesc.id;
                    MyLogger.Log("Found a clear match by description with object id: " + mainObjectID);
                    foundObjs.Add(GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject);      // Add the single match
                    onComplete?.Invoke(foundObjs);                                                          // Return the whole list with single match
                    yield break;
                }
            }

            // Don't do Intersection check if the main object is "none"
            if (mainObject == "none")
            {
                float previousDistance = faissRespDescriptions.matches[0].distance; // Track the last added distance
                string previousName = faissRespDescriptions.matches[0].name; // Track the last added name
                MyLogger.Log("Main object is 'none', skipping intersection check.");
                foreach (var match in faissRespDescriptions.matches)
                {
                    MyLogger.Log($"Checking description match '{match.name}' with distance={match.distance:F3} against previous={previousDistance:F3}");
                    if (match.distance != previousDistance && match.name != previousName)
                    {
                        MyLogger.Log($"Ending Loop");
                        break;
                    }
                    previousDistance = match.distance; // Update the last added distance

                    GameObject foundGO = GameObjectManager.Instance.GetObjectById(match.id)?.gameObject;
                    if (foundGO != null)
                    {
                        MyLogger.Log($"Adding description match '{foundGO.name}' to found objects.");
                        foundObjs.Add(foundGO);
                    }
                }
                MyLogger.Log($"Returning {foundObjs.Count} objects from description matches.");
                onComplete?.Invoke(foundObjs);                  // Return the whole list with matches                                                        // Return the whole list with single match
                yield break;
            }


            SelectionController.Instance?.MarkStage(ProcStage.DescSearch);


            // If still no single match, check for intersection with name-based matches based on object id's
            // We will check every objectID from the faissRespDescriptions and see if it exists in the faissRespNames and if exactly one objectID intersects that's the one we want
            List<GameObject> intersectionResult = null;
            yield return StartCoroutine(
                    FindIntersectionCandidate(
                        faissRespNames,
                        faissRespDescriptions,
                        intersectionDistanceThreshold,
                        list => intersectionResult = list));
            if (intersectionResult != null && intersectionResult.Count > 0)
            {
                foundObjs = intersectionResult;                          // Here we might have multiple gameobjects in the list returned
            }
            onComplete?.Invoke(foundObjs);                  // Return the whole list with matches


            SelectionController.Instance?.MarkStage(ProcStage.Intersection);


            yield break;
        }
        else
        {
            MyLogger.Log("No clear FAISS description matches or they are null.");
        }

        // If no result
        onComplete?.Invoke(null);
    }

    /// <summary>
    /// Logs all FAISS matches for debugging.
    /// </summary>
    private void PrintFAISSMatches(string label, FAISSMatch[] matches)
    {
        MyLogger.Log($"[FAISS:{label}] Received {matches.Length} matches:");
        for (int i = 0; i < matches.Length; i++)
        {
            FAISSMatch match = matches[i];
            MyLogger.Log($"  -> Match {i,-5} | " +
                         $"ID='{match.id,-15}' | " +
                         $"distance={match.distance,-10:F3} | " +
                         $"name='{match.name,-15}");
        }
    }

    /// <summary>
    /// Returns a single best intersection candidate (if any) between name-matches and description-matches,
    /// based on combined distance. Returns (id, gameObject) or (null, null) if no single best found.
    /// </summary>
    private IEnumerator FindIntersectionCandidate(
        FAISSResponse faissRespNames,
        FAISSResponse faissRespDescriptions,
        float intersectionDistanceThreshold,
        Action<List<GameObject>> onComplete)
    {
        List<string> objectIDsFromNames = faissRespNames.matches.Select(m => m.id).ToList();
        List<string> objectIDsFromDescriptions = faissRespDescriptions.matches.Select(m => m.id).ToList();
        List<GameObject> foundObjs = new List<GameObject>();    // For storing either the single match or all possible matches

        var intersection = new List<string>(objectIDsFromNames);
        intersection.RemoveAll(id => !objectIDsFromDescriptions.Contains(id));

        if (intersection.Count == 1)
        {
            string mainObjectID = intersection[0];
            MyLogger.Log("Found a clear single intersection with object id: " + mainObjectID);
            foundObjs.Add(GameObjectManager.Instance.GetObjectById(mainObjectID)?.gameObject);          // Add the single match
            onComplete(foundObjs);
            yield break;                                                                     // Return the whole list with single match
        }// ~~~~~~~~~~~~~~~ This else if can be removed it it is forcing incorrect matches
         // ~~~~~~~~~~~~~~~ It's picking a match based on lowest COMBINED faiss distances under a THRESHOLD limit
         // ~~~~~~~~~~~~~~~ Sort intersections by lowest average FAISS distance
        else if (intersection.Count > 1)
        {
            // ----------------------------------------------------
            //  MULTIPLE candidates in the intersection.
            //  Let's combine their name-distance & description-distance
            //  to pick the best one. 
            // ----------------------------------------------------
            MyLogger.Log(
                $"We have {intersection.Count} intersecting matches. Attempting to pick the best by combined distance."
            );

            // Combine name-distance & description-distance to pick the best
            Dictionary<string, float> combinedDistances = new Dictionary<string, float>();

            foreach (string id in intersection)
            {
                // Find the FAISSMatch in names that has this id
                FAISSMatch nameMatch = faissRespNames.matches.First(m => m.id == id);
                // Find the FAISSMatch in descriptions that has this id
                FAISSMatch descMatch = faissRespDescriptions.matches.First(m => m.id == id);

                float combined = nameMatch.distance + descMatch.distance;
                combinedDistances[id] = combined;

                MyLogger.Log(
                    $"Intersection candidate '{id}' '{nameMatch.name}' => NameDist={nameMatch.distance}, DescDist={descMatch.distance}, Combined={combined}"
                );
            }

            // Sort the dictionary by the combined distance (ascending = best match first)
            var sorted = combinedDistances.OrderBy(kvp => kvp.Value).ToList();
            // We'll check the best and second-best to see if there's a big difference
            var best = sorted[0];
            float bestDistance = best.Value;
            string bestID = best.Key;

            if (sorted.Count > 1)
            {
                var secondBest = sorted[1];
                float secondBestDistance = secondBest.Value;

                if (secondBestDistance - bestDistance > intersectionDistanceThreshold)
                {
                    // If the difference to #2 is large enough, pick the best
                    MyLogger.Log($"Picked '{bestID}' with combined distance={bestDistance}. " +
                                 $"Second-best is {secondBestDistance} => difference " +
                                 $"{(secondBestDistance - bestDistance):F2} > {intersectionDistanceThreshold}.");
                    foundObjs.Add(GameObjectManager.Instance.GetObjectById(bestID)?.gameObject);          // Add the single match
                    onComplete(foundObjs);
                    yield break;                                                                     // Return the whole list with single match
                }
                else
                {
                    // It's still ambiguous (they're close)
                    MyLogger.Log("Multiple intersection candidates are too close. No clear single winner. Returning ALL possible matches");

                    // Add all candidates to the found objects until we hit a distance threshold between the current GO being added and the last GO added
                    float previousDistance = sorted[0].Value; // Track the last added distance
                    foreach (var kvp in sorted)
                    {
                        string id = kvp.Key;
                        float distance = kvp.Value;

                        // Stop if we exceed the max accepted distance
                        if (distance > maxCombinedDistanceAccepted)
                        {
                            break; // No need to check further, as the list is sorted by distance
                        }

                        // Only add if the distance is below the threshold
                        MyLogger.Log($"Checking intersection candidate '{id}' with distance={distance:F3} against previous={previousDistance:F3}");
                        if (distance - previousDistance < intersectionDistanceThreshold)
                        {
                            MyLogger.Log($"Adding intersection candidate '{id}' to found objects.");
                            GameObject foundGO = GameObjectManager.Instance.GetObjectById(id)?.gameObject;
                            if (foundGO != null)
                            {
                                foundObjs.Add(foundGO);
                            }
                        }
                        else
                        {
                            break; // Stop adding if the distance exceeds the threshold
                        }
                        previousDistance = distance; // Update the last added distance
                    }

                    MyLogger.Log($"Returning {foundObjs.Count} objects from intersection candidates.");
                    onComplete(foundObjs);
                    yield break;                                                                     // Return the whole list with multiple matches
                }
            }
        }
        else
        {
            MyLogger.Log("No intersection. Still ambiguous. No match found.");
        }
        // ~~~~~~~~~~~~~~~ End of the else if finding lowest faiss distance winner

        onComplete(null); // No result was found
        yield break;
    }

    // ------------------------------------------------------------
    //  Waits for /openai_get_object_matches_using_chatgpt and returns the reply
    // ------------------------------------------------------------
    private IEnumerator GetObjectNameMatchesCoroutine(
        string command,          // entire utterance
        string mainObject,       // e.g. "laptop"
        Action<ObjectMatchContainer> onDone)
    {
        bool finished = false;
        ObjectMatchContainer result = null;

        // Call the communicator.  It *itself* starts a coroutine that
        // posts JSON; we just need to wait until its callback fires.
        MyLogger.Log($"[GetObjectNameMatchesCoroutine] Searching openai for name matches for: {mainObject} in command: {command}");
        NLPServerCommunicator.Instance.GetObjectNameMatchesUsingOpenAI(
            command,
            mainObject,
            (resp) =>
            {
                result = resp;
                finished = true;
            });

        // Yield until the HTTP call comes back
        yield return new WaitUntil(() => finished);

        MyLogger.Log($"[GetObjectNameMatchesCoroutine] Received {result?.object_matches.Count} matches for '{mainObject}' in command: '{command}'");

        onDone?.Invoke(result);
    }
}