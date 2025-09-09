/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.Networking;

public class NLPServerCommunicator : MonoBehaviour
{
    private static NLPServerCommunicator _instance;
    public static NLPServerCommunicator Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject singletonObject = new GameObject(typeof(NLPServerCommunicator).Name);
                _instance = singletonObject.AddComponent<NLPServerCommunicator>();
                DontDestroyOnLoad(singletonObject);
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public string apiUrl = "http://127.0.0.1:8000/"; // Local Python server

    /// <summary>
    /// General-purpose helper for sending a JSON POST request.
    /// Calls 'onCompleted' with the raw JSON response string (or null on error).
    /// </summary>
    private IEnumerator PostJson(string endpoint, string jsonPayload, System.Action<string> callback)
    {
        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                callback?.Invoke(request.downloadHandler.text);
            else
                callback?.Invoke(null);
        }
    }


    //===========================================================================
    // 1) /create_faiss_embeddings
    //===========================================================================

    /// <summary>
    /// Sends a list of Unity objects to the NLP server to create FAISS embeddings.
    /// Corresponds to the /create_faiss_embeddings endpoint.
    /// </summary>
    public void CreateFaissEmbeddings(List<UnityObject> objects, Action<CreateFaissEmbeddingsResponse> onCompleted)
    {
        string endpointURL = apiUrl + "create_faiss_embeddings";

        // Prepare the payload
        UnityObjectList payloadObj = new UnityObjectList { objects = objects };
        string jsonPayload = JsonUtility.ToJson(payloadObj);

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            // Parse JSON
            CreateFaissEmbeddingsResponse response = JsonUtility.FromJson<CreateFaissEmbeddingsResponse>(jsonResponse);
            onCompleted?.Invoke(response);
        }));
    }

    //===========================================================================
    // 2) /search_name_in_faiss
    //===========================================================================

    /// <summary>
    /// Perform a FAISS search in the names embeddings.
    /// Already implemented. Calls /search_name_in_faiss.
    /// </summary>
    public void SearchObjectByNameInFAISSEmbedding(
                    string query, 
                    int topK, 
                    Action<FAISSResponse> onCompleted, 
                    float maxDistanceJump = 0.5f)
    {
        string endpointURL = apiUrl + "search_name_in_faiss";
        SearchQueryPayload payloadObj = new SearchQueryPayload { query = query, top_k = topK };
        // maxDistanceJump = 0.5f;       // If distance between matches jumps more than this, we stop adding matches.

        string jsonPayload = JsonUtility.ToJson(payloadObj);

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            FAISSResponse response = JsonUtility.FromJson<FAISSResponse>(jsonResponse);
            if (response == null || response.matches == null)
            {
                onCompleted?.Invoke(null);
                return;
            }

            // Optional: filter based on distance jump
            List<FAISSMatch> filteredMatches = new List<FAISSMatch>();
            float lastDistance = -1.0f;
            foreach (var match in response.matches)
            {
                if (filteredMatches.Count == 0 || lastDistance < 0.0f
                    || (match.distance - lastDistance) < maxDistanceJump)
                {
                    filteredMatches.Add(match);
                    lastDistance = match.distance;
                }
                else
                {
                    break;
                }
            }

            FAISSResponse filteredResponse = new FAISSResponse
            {
                matches = filteredMatches.ToArray()
            };
            onCompleted?.Invoke(filteredResponse);
        }));
    }

    //===========================================================================
    // 3) /search_description_in_faiss
    //===========================================================================

    /// <summary>
    /// Perform a FAISS search in the descriptions embeddings.
    /// Already implemented. Calls /search_description_in_faiss.
    /// </summary>
    public void SearchObjectByDescriptionInFAISSEmbedding(string query, int topK, Action<FAISSResponse> onCompleted)
    {
        string endpointURL = apiUrl + "search_description_in_faiss";
        SearchQueryPayload payloadObj = new SearchQueryPayload { query = query, top_k = topK };
        float maxDistanceJump = 0.5f; // example threshold

        string jsonPayload = JsonUtility.ToJson(payloadObj);

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            FAISSResponse response = JsonUtility.FromJson<FAISSResponse>(jsonResponse);
            if (response == null || response.matches == null)
            {
                onCompleted?.Invoke(null);
                return;
            }

            // Optional: filter based on distance jump
            List<FAISSMatch> filteredMatches = new List<FAISSMatch>();
            float lastDistance = -1.0f;
            foreach (var match in response.matches)
            {
                if (filteredMatches.Count == 0 || lastDistance < 0.0f
                    || (match.distance - lastDistance) < maxDistanceJump)
                {
                    filteredMatches.Add(match);
                    lastDistance = match.distance;
                }
                else
                {
                    break;
                }
            }

            FAISSResponse filteredResponse = new FAISSResponse
            {
                matches = filteredMatches.ToArray()
            };
            onCompleted?.Invoke(filteredResponse);
        }));
    }


    //===========================================================================
    // 4) /get_command_complexity_score
    //===========================================================================

    /// <summary>
    /// Example usage:
    /// Determines a complexity score for a user command using the /get_command_complexity_score endpoint.
    /// Example "command": "the smallest tree. The green one."
    /// Expected response:
    /// {
    ///  "complexity": 2
    /// }
    /// </summary>
    public void GetCommandComplexityScore(string command, Action<CompexityScoreResponse> onCompleted)
    {
        string endpointURL = apiUrl + "command_complexity";
        string jsonPayload = "{\"command\": \"" + command + "\"}";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            CompexityScoreResponse response = JsonUtility.FromJson<CompexityScoreResponse>(jsonResponse);
            if (response == null)
            {
                onCompleted?.Invoke(null);
                return;
            }

            onCompleted?.Invoke(response);
        }));
    }

    //===========================================================================
    // 5a) /get_main_object_and_descriptors_using_spacy
    //===========================================================================

    /// <summary>
    /// Example usage:
    /// Extract the main object and any descriptors in a users command with /get_main_object_and_descriptors_using_spacy
    /// Example command: "Show me the shiny blue car parked beside the old red truck"
    /// Expected response:
    /// {
    ///  "main_object": "car",
    ///  "descriptors": [
    ///    "shiny",
    ///    "blue"
    ///  ]
    /// }
    /// </summary>
    public void GetMainObjectAndDescriptorsInCommand(string command, Action<MainObjectWithDescriptorsResponse> onCompleted)
    {
        string endpointURL = apiUrl + "get_main_object_and_descriptors_using_spacy";
        string jsonPayload = "{\"command\": \"" + command + "\"}";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            MainObjectWithDescriptorsResponse response = JsonUtility.FromJson<MainObjectWithDescriptorsResponse>(jsonResponse);
            if (response == null)
            {
                onCompleted?.Invoke(null);
                return;
            }

            onCompleted?.Invoke(response);
        }));
    }


    //===========================================================================
    // 5b) /openai_get_main_object_and_descriptors_using_chatgpt
    //===========================================================================

    /// <summary>
    /// Example usage:
    /// Extract the main object and any descriptors in a users command with /get_main_object_and_descriptors_using_spacy
    /// Example command: "Show me the shiny blue car parked beside the old red truck"
    /// Expected response:
    /// {
    ///  "main_object": "car",
    ///  "descriptors": [
    ///    "shiny",
    ///    "blue"
    ///  ]
    /// }
    /// </summary>
    public void GetMainObjectAndDescriptorsInCommandUsingOpenAI(string command, Action<MainObjectWithDescriptorsResponse> onCompleted)
    {
        // --- 1) build the request payload ------------------------
        var namedEntities = GameObjectManager.Instance.GetSceneSpecificNamedEntities();

        CommandRequestOpenAI payload = new CommandRequestOpenAI
        {
            command = command,
            named_entities = namedEntities.ToArray()   // array for JsonUtility
        };
        string jsonPayload = JsonUtility.ToJson(payload);

        // --- 2) POST it ------------------------------------------
        string endpointURL = apiUrl + "openai_get_main_object_and_descriptors_using_chatgpt";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            MyLogger.Log($"NLPServerCommunicator: GetMainObjectAndDescriptorsInCommandUsingOpenAI response: {jsonResponse}");
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            MainObjWrapper responseWrapped = JsonUtility.FromJson<MainObjWrapper>(jsonResponse);
            MainObjectWithDescriptorsResponse responseUnwrapped = responseWrapped.mainObj;

            onCompleted?.Invoke(responseUnwrapped);
        }));
    }


    //===========================================================================
    // 6a) /get_objects_with_attributes_using_spacy
    //===========================================================================

    /// <summary>
    /// Calls /get_objects_with_attributes_using_spacy to extract each object (noun),
    /// any descriptors (adjectives, compounds), and any spatial or prepositional relations.
    /// </summary>
    public void GetObjectsWithAttributesUsingSpacy(string command, Action<ObjectsWithAttributesResponse> onCompleted)
    {
        string endpointURL = apiUrl + "get_objects_with_attributes_using_spacy";
        string jsonPayload = "{\"command\": \"" + command + "\"}";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            ObjectsWithAttributesResponse response = JsonUtility.FromJson<ObjectsWithAttributesResponse>(jsonResponse);
            onCompleted?.Invoke(response);
        }));
    }



    //===========================================================================
    // 6.b) /get_objects_with_attributes_using_openai
    //===========================================================================

    /// <summary>
    /// Calls /get_objects_with_attributes_using_spacy to extract each object (noun),
    /// any descriptors (adjectives, compounds), and any spatial or prepositional relations.
    /// </summary>
    public void GetObjectsWithAttributesUsingOpenAI(string command, Action<ObjectsWithAttributesResponse> onCompleted)
    {
        // --- 1) build the request payload ------------------------
        var namedEntities = GameObjectManager.Instance.GetSceneSpecificNamedEntities();

        CommandRequestOpenAI payload = new CommandRequestOpenAI
        {
            command = command,
            named_entities = namedEntities.ToArray()   // array for JsonUtility
        };
        string jsonPayload = JsonUtility.ToJson(payload);

        // --- 2) POST it ------------------------------------------
        string endpointURL = apiUrl + "openai_get_objects_and_attributes_using_chatgpt";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            ObjectsWithAttributesResponse response = JsonUtility.FromJson<ObjectsWithAttributesResponse>(jsonResponse);
            onCompleted?.Invoke(response);
        }));
    }

    //===========================================================================
    // 7) /extract_spatial_relationships_using_stanza
    //===========================================================================

    /// <summary>
    /// Calls /extract_spatial_relationships_using_stanza to get any spatial relations info.
    /// We simply return the raw JSON, as the shape may vary.
    /// </summary>
    //public void ExtractSpatialRelationshipsUsingStanza(string command, Action<SpatialRelationshipContainer> onCompleted)
    //{
    //    string endpointURL = apiUrl + "extract_spatial_relationships_using_stanza";
    //    string jsonPayload = "{\"command\": \"" + command + "\"}";

    //    StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
    //    {
    //        // Wrap the JSON array with a property name so that it matches the container class.
    //        string wrappedJson = "{\"relationships\":" + jsonResponse + "}";
    //        SpatialRelationshipContainer response = JsonUtility.FromJson<SpatialRelationshipContainer>(wrappedJson);
    //        onCompleted?.Invoke(response);
    //    }));
    //}

    //===========================================================================
    // 7b) /extract_spatial_relationships_using_stanza_ver2
    //===========================================================================

    /// <summary>
    /// Calls /extract_spatial_relationships_using_stanza to get any spatial relations info.
    /// We simply return the raw JSON, as the shape may vary.
    /// </summary>
    public void ExtractSpatialRelationshipsUsingStanzaVer2(string command, Action<SpatialRelationshipContainer> onCompleted)
    {
        string endpointURL = apiUrl + "extract_spatial_relationships_using_stanza_ver2";
        string jsonPayload = "{\"command\": \"" + command + "\"}";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            // Wrap the JSON array with a property name so that it matches the container class.
            string wrappedJson = "{\"relationships\":" + jsonResponse + "}";
            SpatialRelationshipContainer response = JsonUtility.FromJson<SpatialRelationshipContainer>(wrappedJson);
            onCompleted?.Invoke(response);
        }));
    }

    //===========================================================================
    // 7c) /openai_get_spatial_relationships_using_chatgpt
    //===========================================================================

    /// <summary>
    /// Calls /extract_spatial_relationships_using_stanza to get any spatial relations info.
    /// We simply return the raw JSON, as the shape may vary.
    /// </summary>
    public void ExtractSpatialRelationshipsUsingOpenAI(string command, Action<SpatialRelationshipContainer> onCompleted)
    {
        // --- 1) build the request payload ------------------------
        var namedEntities = GameObjectManager.Instance.GetSceneSpecificNamedEntities();

        CommandRequestOpenAI payload = new CommandRequestOpenAI
        {
            command = command,
            named_entities = namedEntities.ToArray()   // array for JsonUtility
        };
        string jsonPayload = JsonUtility.ToJson(payload);

        // --- 2) POST it ------------------------------------------
        string endpointURL = apiUrl + "openai_get_spatial_relationships_using_chatgpt";

        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            // No wrapping needed - server JSON already matches the container
            SpatialRelationshipContainer response =
                JsonUtility.FromJson<SpatialRelationshipContainer>(jsonResponse);

            onCompleted?.Invoke(response);
        }));
    }

    // -------------------------------------------------------------------
    //  7.1) /openai_get_object_matches
    // -------------------------------------------------------------------
    public void GetObjectNameMatchesUsingOpenAI(
        string command,
        string mainObject,
        Action<ObjectMatchContainer> onCompleted)
    {
        // ----- 1) gather inventory -------------------------------------
        Dictionary<string, string> objectDict =
            GameObjectManager.Instance.BuildSceneObjectNameDictionary();

        // ----- 2) build request payload --------------------------------
        ObjectMatchRequestOpenAI payload = new()
        {
            command = command,
            main_object = mainObject,
            object_dict = objectDict
        };

        // We rely on Newtonsoft because JsonUtility can't handle Dictionary<>
        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        MyLogger.Log($"NLPServerCommunicator: GetObjectNameMatchesUsingOpenAI payload: {jsonPayload}");

        // ----- 3) POST it ----------------------------------------------
        string endpointURL = apiUrl + "openai_get_object_matches_using_chatgpt";

        MyLogger.Log($"NLPServerCommunicator: GetObjectNameMatchesUsingOpenAI command: {command}, mainObject: {mainObject}");
        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            ObjectMatchContainer response =
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectMatchContainer>(jsonResponse);

            onCompleted?.Invoke(response);
        }));
    }

    // -------------------------------------------------------------------
    //  7.2) /openai_get_object_description_matches_using_chatgpt
    // -------------------------------------------------------------------
    public void GetObjectDescMatchesUsingOpenAI(
        string command,
        string mainObjectChunk,
        Action<ObjectDescriptionMatchContainer> onCompleted)
    {
        // ----- 1) gather inventory -------------------------------------
        Dictionary<string, string> objectDict =
            GameObjectManager.Instance.BuildSceneObjectDescriptionDictionary();

        // ----- 2) build request payload --------------------------------
        ObjectMatchRequestOpenAI payload = new()
        {
            command = command,
            main_object = mainObjectChunk,
            object_dict = objectDict
        };

        // We rely on Newtonsoft because JsonUtility can't handle Dictionary<>
        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        MyLogger.Log($"NLPServerCommunicator: GetObjectDescrMatchesUsingOpenAI payload: {jsonPayload}");

        // ----- 3) POST it ----------------------------------------------
        string endpointURL = apiUrl + "openai_get_object_description_matches_using_chatgpt";

        MyLogger.Log($"NLPServerCommunicator: GetObjectDescrMatchesUsingOpenAI command: {command}, mainObject: {mainObjectChunk}");
        StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                onCompleted?.Invoke(null);
                return;
            }

            ObjectDescriptionMatchContainer response =
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectDescriptionMatchContainer>(jsonResponse);

            onCompleted?.Invoke(response);
        }));
    }

    ////===========================================================================
    //// 8) /lmstudio_get_objects_and_attributes_using_llama
    ////===========================================================================

    ///// <summary>
    ///// Calls /lmstudio_get_objects_and_attributes_using_llama.
    ///// Returns the entire JSON from LM-Studio (OpenAI-like format).
    ///// </summary>
    //public void LmStudioGetObjectsAndAttributesUsingLlama(string command, Action<string> onCompleted)
    //{
    //    string endpointURL = apiUrl + "lmstudio_get_objects_and_attributes_using_llama";
    //    string jsonPayload = "{\"command\": \"" + command + "\"}";

    //    StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
    //    {
    //        // Return the raw JSON to the caller
    //        onCompleted?.Invoke(jsonResponse);
    //    }));
    //}

    ////===========================================================================
    //// 9) /lmstudio_get_main_object_using_llama
    ////===========================================================================

    ///// <summary>
    ///// Calls /lmstudio_get_main_object_using_llama.
    ///// Returns the entire JSON from LM-Studio (OpenAI-like format).
    ///// </summary>
    //public void LmStudioGetMainObjectUsingLlama(string command, Action<string> onCompleted)
    //{
    //    string endpointURL = apiUrl + "lmstudio_get_main_object_using_llama";
    //    string jsonPayload = "{\"command\": \"" + command + "\"}";

    //    StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
    //    {
    //        onCompleted?.Invoke(jsonResponse);
    //    }));
    //}

    ////===========================================================================
    //// 10) /lmstudio_get_main_object_and_spatial_relations_using_llama
    ////===========================================================================

    ///// <summary>
    ///// Calls /lmstudio_get_main_object_and_spatial_relations_using_llama
    ///// Returns raw JSON.
    ///// </summary>
    //public void LmStudioGetMainObjectAndSpatialRelationsUsingLlama(string command, Action<string> onCompleted)
    //{
    //    string endpointURL = apiUrl + "lmstudio_get_main_object_and_spatial_relations_using_llama";
    //    string jsonPayload = "{\"command\": \"" + command + "\"}";

    //    StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
    //    {
    //        onCompleted?.Invoke(jsonResponse);
    //    }));
    //}

    ////===========================================================================
    //// 11) /openai_get_objects_and_attributes_using_chatgpt
    ////     (Likely requires command, main_object, and spatial_info)
    ////===========================================================================

    ///// <summary>
    ///// Calls /openai_get_objects_and_attributes_using_chatgpt, 
    ///// which requires CommandInputSpatial: { "command": ..., "main_object": ..., "spatial_info": ...}
    ///// Returns raw JSON from the OpenAI chat completion.
    ///// </summary>
    //public void OpenaiGetObjectsAndAttributesUsingChatgpt(CommandInputSpatial inputPayload, Action<string> onCompleted)
    //{
    //    string endpointURL = apiUrl + "openai_get_objects_and_attributes_using_chatgpt";

    //    // Convert the input into JSON
    //    string jsonPayload = JsonUtility.ToJson(inputPayload);

    //    StartCoroutine(PostJson(endpointURL, jsonPayload, (jsonResponse) =>
    //    {
    //        onCompleted?.Invoke(jsonResponse);
    //    }));
    //}




    //===========================================================================
    // HELPER CLASSES FOR JSON SERIALIZATION
    //===========================================================================

    [Serializable]
    public class SearchQueryPayload
    {
        public string query;
        public int top_k;
    }

    [Serializable]
    public class UnityObject
    {
        public string id;
        public string name;
        public string description;
    }

    [Serializable]
    public class UnityObjectList
    {
        public List<UnityObject> objects;
    }

    [Serializable]
    public class CreateFaissEmbeddingsResponse
    {
        public string message;
        public int total_objects;
    }
    [Serializable]
    public class CompexityScoreResponse
    {
        public int complexity;
    }

    [Serializable]
    public class GetAllObjectsResponse
    {
        public List<string> object_names;
    }

    [Serializable]
    public class MainObjectWithDescriptorsResponse
    {
        public string main_object;
        public List<string> descriptors;
        public float cost_usd;  // used for cost tracking of openAI calls
    }
    [Serializable]
    public class MainObjWrapper
    {
        public MainObjectWithDescriptorsResponse mainObj;
    }

    [Serializable]
    public class FAISSMatch
    {
        public string id;
        public string name;
        public float distance;
    }

    [Serializable]
    public class FAISSResponse
    {
        public FAISSMatch[] matches;
    }

    // For /get_objects_with_attributes_using_spacy
    [Serializable]
    public class ObjectsWithAttributesResponse
    {
        public List<SpacyObject> objects;
        public float cost_usd;  // used for cost tracking of openAI calls
    }

    [Serializable]
    public class SpacyObject
    {
        public string object_text;
        public string head;
        public List<string> descriptors;
        public List<Relation> relations;
    }

    [Serializable]
    public class Relation
    {
        public string relation_phrase;
        public string related_object;
        public List<string> related_descriptors;
    }

    // For /openai_get_objects_and_attributes_using_chatgpt
    [Serializable]
    public class CommandInputSpatial
    {
        public string command;
        public string main_object;
        public string spatial_info;
    }

    [System.Serializable]
    public class SpatialRelationship
    {
        public string main_object;
        public string spatial_relation;
        public string related_object;
    }

    // Note: Unity's JsonUtility cannot directly deserialize a top-level JSON array.
    // To work around this, you can create a container class:
    [System.Serializable]
    public class SpatialRelationshipContainer
    {
        public SpatialRelationship[] relationships;
        public float cost_usd;  // used for cost tracking of openAI calls
    }

    [Serializable]              // <- required for JsonUtility
    public class CommandRequestOpenAI
    {
        public string command;
        public string[] named_entities;   // JsonUtility prefers arrays
    }

    // ---------- request ----------
    [Serializable]
    public class ObjectMatchRequestOpenAI
    {
        public string command;                      // raw user utterance
        public string main_object;                  // e.g.  "laptop"
        public Dictionary<string, string> object_dict;
    }

    // ---------- response ----------
    [Serializable]
    public class ObjectMatch
    {
        public string query_name;
        public string named_entity;
        public string id;           
    }

    [Serializable]
    public class ObjectMatchContainer
    {
        public List<ObjectMatch> object_matches;
    }


    [Serializable]
    public class ObjectDescriptionMatch
    {
        public string query_name;
        public string id;
    }

    [Serializable]
    public class ObjectDescriptionMatchContainer
    {
        public List<ObjectDescriptionMatch> object_matches;
    }
}
