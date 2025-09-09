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
using System.Linq;
using System;
using static OVRInput;
using System.Text.RegularExpressions;

public class SelectionController : MonoBehaviour
{
    private static SelectionController _instance;
    public static SelectionController Instance
    {
        get
        {
            if (_instance == null)
            {
                // Look for one that is already alive first
                _instance = FindFirstObjectByType<SelectionController>();

                // If still null create one automatically.
                // Safer: would be just warn so the dev knows the singleton is missing.
                if (_instance == null)
                {
                    Debug.LogError(
                        "No SelectionController in the scene. " +
                        "Add one to a GameObject before accessing Instance."
                    );
                }
            }

            return _instance;
        }
    }


#if UNITY_EDITOR
    public void MarkStage(ProcStage stage) => CommandTestManager.Instance?.OnStageReached(stage);
    public void ReportResult(List<GameObject> found, float cost_usd) => CommandTestManager.Instance?.OnResult(found, cost_usd);
#endif

    public enum SelectionPipelineVersion
    {
        Traditional_NLP,
        LLM_Assisted,
        Full_LLM,
        Hybrid
    }

    [SerializeField]
    private SelectionPipelineVersion pipelineVersion = SelectionPipelineVersion.Traditional_NLP;

    public SelectionPipelineVersion PipelineVersion => pipelineVersion;

    // I'm breaking down individual steps into keypresses for initial testing and debugging
    [Header("Keyboard Bindings")]
    [Tooltip("Key to trigger a full FAISS re-index from scene GameObjects.")]
    public KeyCode updateFAISSKey               = KeyCode.F;
    [Tooltip("Key to process the text command in 'searchString'.")]
    public KeyCode KeyPress_processCommand      = KeyCode.Alpha0;
    public KeyCode Debug_ForwardStep            = KeyCode.Alpha1;
    public KeyCode temp_functionCommand         = KeyCode.Alpha2;

    [Header("OVR Touch Bindings (Meta SDK v78)")]
    [Tooltip("OVR button to trigger FAISS update.")]
    public Button updateFAISS_OVRButton = Button.One;        // A/X
    [Tooltip("OVR button to trigger processing of 'searchString'.")]
    public Button processCommand_OVRButton = Button.Two;      // B/Y
    [Tooltip("OVR button for debug forward step.")]
    public Button debugForward_OVRButton = Button.PrimaryThumbstick;
    [Tooltip("OVR button for temp/extra function.")]
    public Button tempFunction_OVRButton = Button.SecondaryThumbstick;

    [Header("Command / Query")]
    [Tooltip("Type a command here (useful without a headset). Press the bound key/OVR button to process.")]
    [TextArea(1, 3)]
    public string searchString;

    // Settings for FAISS querys
    [Header("FAISS / Matching Settings")]
    public float nameDistanceThreshold = 0.2f;              // Distance threshold for name matches
    public float descDistanceThreshold = 0.2f;              // Distance threshold for desc matches
    public float intersectionDistanceThreshold = 0.5f;      // Distance threshold for intersection matches
    public float maxCombinedDistanceAccepted = 0.8f;        // Maximum distance accepted for a match in FAISS searches
    public float maxDistanceForSpatialCandidates = 1.0f;    // max distance threshold when finding candidate objects for spatial relationships
    public int topK = 50;                                   // Number of top matches to return from FAISS

    private SpatialResolver spatialResolver;                // This is used to recursively resolve spatial relationships between objects in the scene.

    private float processingCost = 0f;                      // This is used to track the processing cost to process a command when API calls are made.// Call this from sub-functions after OpenAI call completes

    public float GetProcessingCost() => processingCost;
    public void AddToProcessingCost(float cost)
    {
        processingCost += cost;
        MyLogger.Log($"Processing cost updated: {processingCost}");
    }

    // ---------- FAISS Status (shown in custom inspector) ----------
    [System.Serializable]
    public class FaissStatus
    {
        public bool hasResult;
        public bool ok;
        public int objectsAdded;
        [TextArea(1, 6)] public string message;
        public System.DateTime timestampUtc;
    }

    [SerializeField, Tooltip("Last FAISS update status (read-only, for Inspector).")]
    private FaissStatus lastFaissStatus = new FaissStatus();

    public FaissStatus LastFaissStatus => lastFaissStatus;


    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning(
                $"Duplicate SelectionController on {gameObject.name}. Destroying this copy."
            );
            Destroy(gameObject);
            return;
        }

        _instance = this;
        //DontDestroyOnLoad(gameObject);   // This is if we need it in other scenes, but our gaurd will prevent dups anyways...


        if (spatialResolver != null)
        {
            Debug.LogWarning("SpatialResolver already exists on this GameObject. Reusing it.");
            return; // Avoid adding another SpatialResolver if one already exists
        }
        else
        {
            spatialResolver = gameObject.AddComponent<SpatialResolver>();
        }
    }

    private void Update()
    {
        // Keyboard
        if (Input.GetKeyDown(updateFAISSKey)) TriggerUpdateFAISS();
        else if (Input.GetKeyDown(KeyPress_processCommand)) TriggerProcessCommand();
        else if (Input.GetKeyDown(Debug_ForwardStep)) TriggerDebugForward();
        else if (Input.GetKeyDown(temp_functionCommand)) TriggerTempFunction();

        // OVR Touch
        if (OVRInput.GetDown(updateFAISS_OVRButton)) TriggerUpdateFAISS();
        if (OVRInput.GetDown(processCommand_OVRButton)) TriggerProcessCommand();
        if (OVRInput.GetDown(debugForward_OVRButton)) TriggerDebugForward();
        if (OVRInput.GetDown(tempFunction_OVRButton)) TriggerTempFunction();
    }



    // ---------- Public triggers (callable from Inspector buttons) ----------
    public void TriggerUpdateFAISS()
    {
        StartCoroutine(CreateFAISSFromGameObjects());
    }

    public void TriggerProcessCommand()
    {
        if (string.IsNullOrWhiteSpace(searchString))
        {
            MyLogger.LogWarning("No command in 'searchString'. Type something first.");
            return;
        }
        StartCoroutine(ProcessCommand(searchString));
    }

    public void TriggerDebugForward()
    {
        // Put your debug step here
        MyLogger.Log("Debug forward step triggered.");
    }

    public void TriggerTempFunction()
    {
        // Put your extra function here
        MyLogger.Log("Temp function triggered.");
    }


    private IEnumerator WaitForDebugStep()
    {
        Debug.Log("Waiting for debug key...");
        yield return new WaitUntil(() => Input.GetKeyDown(Debug_ForwardStep));
    }

    /// <summary>
    /// High-level method that:
    /// 1) Counts how many objects appear in the user's command.
    /// 2) Decides if there is one or multiple objects.
    /// 3) Delegates the actual selection logic to separate methods.
    /// ... TODO - update this docstring to reflect the new logic and algorithm
    /// </summary>
    public IEnumerator ProcessCommand(string command)
    {
        // Reset any highlighted objects and cost of processing
        HighlightingManager.Instance.ClearHighlights();
        processingCost = 0f;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 00: Get a compexity score for the command, which will determine which selectioncontroller version to use.
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        MyLogger.Log($"[COMPLEXITY] - Processing command: '{command}'");

        if (PipelineVersion == SelectionPipelineVersion.Hybrid)
        {
            yield return StartCoroutine(
                GetCommandComplexityScore(command, (complexityScore) =>
                {
                    MyLogger.Log($"[COMPLEXITY] - Command complexity score is {complexityScore}.");
                    if (complexityScore <= 1)
                    {
                        pipelineVersion = SelectionPipelineVersion.Traditional_NLP;
                        MyLogger.Log($"Command complexity score is {complexityScore}. Using Traditional_NLP.");
                    }
                    else if (complexityScore > 1)   // For now we want to use the LLM enhanced version for both complexity 2 and 3, but this can be changed later
                    {
                        pipelineVersion = SelectionPipelineVersion.LLM_Assisted;
                        MyLogger.Log($"Command complexity score is {complexityScore}. Using LLM_Assisted.");
                    }
                    else
                    {
                        pipelineVersion = SelectionPipelineVersion.Full_LLM;
                        MyLogger.Log($"Command complexity score is {complexityScore}. Using Full_LLM.");
                    }
                })
            );
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 0: First check if the command uses an relationship statement, in which case we will not allow results to be returned
        ////////         the don't match in the spatial relationship evaluators. 
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        SpatialRelationshipContainer allSpatialRelationships = default;
        yield return StartCoroutine(
            GetSpatialRelationshipsCoroutine(command, (resp) =>
            {
                allSpatialRelationships = resp;
            })
        );

        if (allSpatialRelationships == null || allSpatialRelationships.relationships.Count() == 0)
        {
            MyLogger.LogWarning($"No spatial relationships found in command: '{command}'");
        }

#if UNITY_EDITOR
        MarkStage(ProcStage.CheckRelationship);
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1a: Get all the objects names (text) with their descriptions from the command 
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var (allObjectsAndAttrributes, objectCount) = (default(ObjectsWithAttributesResponse), 0);
        yield return StartCoroutine(
            GetAllObjectsAndAttributesCoroutine(command, (resp, count) =>
            {
                allObjectsAndAttrributes = resp;
                objectCount = count;
            })
        );

#if UNITY_EDITOR
        MarkStage(ProcStage.ExtractMentions);
#endif

        if (allObjectsAndAttrributes == null)
        {
#if UNITY_EDITOR
            MarkStage(ProcStage.Done);
            ReportResult(new List<GameObject>(), processingCost); // can be empty
#endif
            MyLogger.LogWarning($"No objects found in command: '{command}'");
            MyLogger.Log($"Total cost of processing command: ${processingCost:F6} USD");
            yield break;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1b: Try get the main object and descriptors in the command
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var (mainObjectAndDescriptors, mainObject) = (default(MainObjectWithDescriptorsResponse), (string)null);
        yield return StartCoroutine(
            GetMainObjectAndDescriptorsCoroutine(command, (resp, objName) =>
            {
                mainObjectAndDescriptors = resp;
                mainObject = objName;
            })
        );

#if UNITY_EDITOR
        MarkStage(ProcStage.ParseMainObject);
#endif

        if (mainObjectAndDescriptors == null || string.IsNullOrEmpty(mainObject))
        {
#if UNITY_EDITOR
            MarkStage(ProcStage.Done);
            ReportResult(new List<GameObject>(), processingCost); // can be empty
#endif
            MyLogger.LogWarning($"No main object of focus was identified in command: '{command}'");
            MyLogger.Log($"Total cost of processing command: ${processingCost:F6} USD");
            yield break;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 1c: Run FAISS searches to try resolve the main object into an actual GameObject
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        List<GameObject> gameObjectsToSelect = null;

        if (PipelineVersion == SelectionPipelineVersion.Traditional_NLP || 
            PipelineVersion == SelectionPipelineVersion.LLM_Assisted)
        {
            yield return StartCoroutine(
                GetRelatedObjects.Instance.FindMatchForMainObjectCoroutine(
                    mainObject,
                    mainObjectAndDescriptors,
                    allObjectsAndAttrributes,
                    (selectedObjs) =>
                    {
                        gameObjectsToSelect = selectedObjs;
                    })
            );
        }
        else if (PipelineVersion == SelectionPipelineVersion.Full_LLM)
        {
            yield return StartCoroutine(
                GetRelatedObjects.Instance.FindMatchForMainObjectCoroutineUsingOpenAI(
                    mainObject,
                    mainObjectAndDescriptors,
                    allObjectsAndAttrributes,
                    (selectedObjs) =>
                    {
                        gameObjectsToSelect = selectedObjs;
                    },
                    command)
            );
        }

        // If SINGLE MATCH found -> Highlight it
        if (gameObjectsToSelect != null && gameObjectsToSelect.Count == 1)
        {
            // Call the HighlightingManager.Instance to highlight the selected object(s)
            Highlightable highlightable = gameObjectsToSelect[0].GetComponent<Highlightable>(); // Needs to exist to be highlightable
            if (highlightable == null)
            {
                MyLogger.LogWarning($"GameObject '{gameObjectsToSelect[0].name}' does not have a Highlightable component.");
            }
            else
            {
                HighlightingManager.Instance.outlineColor = Color.green;
                HighlightingManager.Instance.AddHighlight(highlightable);
            }
            // Log result to console
            MyLogger.Log($"Final selection: {gameObjectsToSelect[0].name}");
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

        if (allSpatialRelationships != null && allSpatialRelationships.relationships.Count() > 0 && gameObjectsToSelect.Count > 1)
        {
            // We'll store all discovered candidates in some data structure for further logic
            List<(SpatialRelationship relationship,
            List<GameObject> possibleMainObjects,
            List<GameObject> possibleRelatedObjects)> relationshipCandidates
                = new List<(SpatialRelationship, List<GameObject>, List<GameObject>)>();

            yield return StartCoroutine(
                spatialResolver.ResolveFocusObject(
                    allSpatialRelationships,
                    mainObject,
                    gameObjectsToSelect,
                    result =>
                    {
                        foreach (var gameobject in result)
                        {
                            var metadata = gameobject.GetComponent<GameObjectMetadata>();
                            MyLogger.Log($"[STEP 2] Candidate after spatial filter: {metadata.objectName} (ID: {metadata?.id})");
                        }
                        gameObjectsToSelect = result;
                    }));
        }


#if UNITY_EDITOR
    MarkStage(ProcStage.RelationSearch);
#endif

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////// STEP 3: gameObjectsToSelect contains either:
        ///                 1. No gameobjcts
        ///                 2. All objects found before spatial relationship search (assuming no metnion made of any relationships
        ///                 3. All objects found after spatial relationship search
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        if (gameObjectsToSelect != null && gameObjectsToSelect.Count >= 1)
        {
            // Call the HighlightingManager.Instance to highlight the selected object(s)
            foreach (var gameObjectToSelect in gameObjectsToSelect)
            {
                MyLogger.Log($"Highlighting object: {gameObjectToSelect.name}");
                Highlightable highlightable = gameObjectToSelect.GetComponent<Highlightable>(); // Needs to exist to be highlightable
                if (highlightable == null)
                {
                    MyLogger.LogWarning($"GameObject '{gameObjectToSelect.name}' does not have a Highlightable component.");
                }
                HighlightingManager.Instance.AddHighlight(highlightable);
            }


            // Early exit the coroutine if we found a match
#if UNITY_EDITOR
            MarkStage(ProcStage.Done);
            ReportResult(gameObjectsToSelect, processingCost); // can be empty
#endif
            MyLogger.Log($"Total cost of processing command: ${processingCost:F6} USD");
            yield break;
        }
        else
        {
            MyLogger.LogWarning("Ensure you've created the FAISS index of all game objects before searchin! \nNo valid objects found after processing command.");
            MyLogger.Log($"Total cost of processing command: ${processingCost:F6} USD");
        }

#if UNITY_EDITOR
        MarkStage(ProcStage.Done);
        ReportResult(gameObjectsToSelect, processingCost); // can be empty
#endif
    }


    /// <summary>
    /// Coroutine to get all objects and attributes in the command.
    /// </summary>
    private IEnumerator GetCommandComplexityScore(
        string command,
        Action<int> onComplete
    )
    {
        bool isDone = false;
        CompexityScoreResponse complexityResponse = null;

        // Call the NLP server to get all object names and attributes from command
        NLPServerCommunicator.Instance.GetCommandComplexityScore(command, (response) => {
            complexityResponse = response;
            isDone = true;
        });

        yield return new WaitUntil(() => isDone);

        onComplete?.Invoke(complexityResponse.complexity);
    }


    /// <summary>
    /// Coroutine to get all objects and attributes in the command.
    /// </summary>
    private IEnumerator GetSpatialRelationshipsCoroutine(
        string command,
        Action<SpatialRelationshipContainer> onComplete
    )
    {
        bool isDone = false;
        SpatialRelationshipContainer allSpatialRelationships = null;

        // Call the NLP server to get all object names and attributes from command
        if (PipelineVersion == SelectionPipelineVersion.Traditional_NLP)
        {
            NLPServerCommunicator.Instance.ExtractSpatialRelationshipsUsingStanzaVer2(command, (response) =>  {
                allSpatialRelationships = response;
                isDone = true; });
        } 
        else if (PipelineVersion == SelectionPipelineVersion.LLM_Assisted ||
                   PipelineVersion == SelectionPipelineVersion.Full_LLM)
        {
            NLPServerCommunicator.Instance.ExtractSpatialRelationshipsUsingOpenAI(command, (response) => {
                allSpatialRelationships = response;
                AddToProcessingCost(response.cost_usd);
                isDone = true; });
        }

        yield return new WaitUntil(() => isDone);

        // Log all the sptatial relationships
        if (allSpatialRelationships != null)
        {
            foreach (SpatialRelationship spatialRelationship in allSpatialRelationships.relationships)
            {
                string mainObject   = spatialRelationship.main_object.ToLower() ?? "None";
                string relation = spatialRelationship.spatial_relation.ToLower() ?? "None";
                string relatedObject = spatialRelationship.related_object.ToLower() ?? "None";

                MyLogger.Log($"Spatial relationship: {mainObject} {relation} {relatedObject}");
            }
        }

        onComplete?.Invoke(allSpatialRelationships);
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
        if (PipelineVersion == SelectionPipelineVersion.Traditional_NLP)
        {
            NLPServerCommunicator.Instance.GetObjectsWithAttributesUsingSpacy(command, (response) => {
                allObjectsAndAttrributes = response;
                isDone = true; });
        }
        else if (PipelineVersion == SelectionPipelineVersion.LLM_Assisted ||
                   PipelineVersion == SelectionPipelineVersion.Full_LLM)
        {
            NLPServerCommunicator.Instance.GetObjectsWithAttributesUsingOpenAI(command, (response) => {
                allObjectsAndAttrributes = response;
                AddToProcessingCost(response.cost_usd);
                isDone = true; });
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


        if (PipelineVersion == SelectionPipelineVersion.Traditional_NLP)
        {
            NLPServerCommunicator.Instance.GetMainObjectAndDescriptorsInCommand(command, (response) => {
                mainObjectAndDescriptors = response;
                isDone = true; });
        }
        else if (PipelineVersion == SelectionPipelineVersion.LLM_Assisted ||
                   PipelineVersion == SelectionPipelineVersion.Full_LLM)
        {
            NLPServerCommunicator.Instance.GetMainObjectAndDescriptorsInCommandUsingOpenAI(command, (response) => {
                mainObjectAndDescriptors = response;
                AddToProcessingCost(response.cost_usd);
                isDone = true; });
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
    /// Finds all objects with a GameObjectMetaData component, then sends them in a JSON payload to the python NLP server
    ///     which Creates two FAISS indices. One for the object names and one for the object descriptions.
    /// </summary>
    /// <returns>An IEnumerator for coroutine handling.</returns>
    public IEnumerator CreateFAISSFromGameObjects()
    {
        // Endpoint for FastAPI endpoint
        string endpointURL = NLPServerCommunicator.Instance.apiUrl + "create_faiss_embeddings";

        Dictionary<string, GameObjectMetadata> objects = GameObjectManager.Instance.objectDictionary;

        if (objects.Count == 0)
        {
            var msg = "No GameObjects with GameObjectMetadata found!";
            Debug.LogWarning(msg);
            UpdateFaissStatus(ok: false, added: 0, message: msg);
            yield break;
        }

        // Use the UnityObject class for JSON payload
        List<UnityObject> unityObjects = new List<UnityObject>();
        foreach (var obj in objects)
        {
            var meta = obj.Value;
            unityObjects.Add(new UnityObject
            {
                id = meta.id,
                name = meta.objectName.ToLower(),
                description = meta.description.ToLower()
            });
        }

        // Create the payload using the UnityObjectList class
        UnityObjectList payload = new UnityObjectList { objects = unityObjects };

        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(endpointURL, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler.text ?? string.Empty;
                MyLogger.Log("FAISS indices updated: " + body);

                // Parse {"message":"Objects updated!","total_objects":196}
                int added = 0;
                string serverMsg = "";
                try
                {
                    var resp = JsonUtility.FromJson<FaissServerResponse>(body);
                    if (resp != null)
                    {
                        added = Mathf.Max(0, resp.total_objects);
                        serverMsg = resp.message ?? "";
                    }
                }
                catch { /* fallback handled below */ }

                bool ok = added > 0;
                string msg = ok
                    ? $"? FAISS updated. Objects indexed: {added}."
                    : "No objects were indexed. Check server logs or payload.";

                // Include server message for context
                if (!string.IsNullOrEmpty(serverMsg))
                    msg += $"\nServer: {serverMsg}";
                else if (!string.IsNullOrEmpty(body))
                    msg += $"\nServer: {body}";

                UpdateFaissStatus(ok: ok, added: added, message: msg);
            }
            else
            {
                // Example: "Cannot connect to destination host"
                string errDetail = request.error ?? "Unknown network error";
                string err = $"Error updating FAISS indices: {errDetail}";
                MyLogger.LogError(err);
                UpdateFaissStatus(ok: false, added: 0, message: err);
            }
        }
    }

    // ---------- Helpers for FAISS status ----------
    private void UpdateFaissStatus(bool ok, int added, string message)
    {
        lastFaissStatus ??= new FaissStatus();
        lastFaissStatus.hasResult = true;
        lastFaissStatus.ok = ok;
        lastFaissStatus.objectsAdded = added;
        lastFaissStatus.message = message;
        lastFaissStatus.timestampUtc = System.DateTime.UtcNow;
#if UNITY_EDITOR
        // Repaint the inspector so status appears immediately
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
    }

    private bool TryParseAddedCount(string body, out int count)
    {
        // Very tolerant: look for "added": <int> (with or without quotes)
        var m = Regex.Match(body, @"(?i)""?added""?\s*:\s*(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out count)) return true;

        // Other common keys we might see:
        m = Regex.Match(body, @"(?i)""?objects(?:Indexed|Added)?""?\s*:\s*(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out count)) return true;

        count = 0;
        return false;
    }

    private int GuessFirstInteger(string s)
    {
        var m = Regex.Match(s ?? "", @"\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }



    [System.Serializable]
    public class UnityObjectData
    {
        public string id;
        public string name;
        public string description;

        public UnityObjectData(string id, string name, string description)
        {
            this.id = id;
            this.name = name;
            this.description = description;
        }
    }

    [System.Serializable]
    public class FAISSPayload
    {
        public List<UnityObjectData> objects;

        public FAISSPayload(List<UnityObjectData> objects)
        {
            this.objects = objects;
        }
    }

    [System.Serializable]
    public class FaissServerResponse
    {
        public string message;
        public int total_objects;
    }
}