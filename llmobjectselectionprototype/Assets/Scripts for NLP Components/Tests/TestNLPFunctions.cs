using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestNLPFunctions : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        // Run the tests sequentially as a coroutine.
        StartCoroutine(RunTests());
    }

    IEnumerator RunTests()
    {
        Debug.Log("Starting NLP functions tests...");

        // 1. Test /create_faiss_embeddings
        List<NLPServerCommunicator.UnityObject> testObjects = new List<NLPServerCommunicator.UnityObject>()
        {
            new NLPServerCommunicator.UnityObject() { name = "TestObject1", id = "1", description = "This is a test object" },
            new NLPServerCommunicator.UnityObject() { name = "TestObject2", id = "2", description = "Another test object for embedding" }
        };
        NLPServerCommunicator.Instance.CreateFaissEmbeddings(testObjects, (response) =>
        {
            if (response != null)
                Debug.Log("CreateFaissEmbeddings response: " + response.message + " | Total Objects: " + response.total_objects);
            else
                Debug.LogError("CreateFaissEmbeddings failed.");
        });
        yield return new WaitForSeconds(5f);

        // 2. Test /search_name_in_faiss
        NLPServerCommunicator.Instance.SearchObjectByNameInFAISSEmbedding("Test", 3, (faissResponse) =>
        {
            if (faissResponse != null)
                Debug.Log("SearchObjectByNameInFAISSEmbedding response: " + JsonUtility.ToJson(faissResponse));
            else
                Debug.LogError("SearchObjectByNameInFAISSEmbedding failed.");
        });
        yield return new WaitForSeconds(5f);

        // 3. Test /search_description_in_faiss
        NLPServerCommunicator.Instance.SearchObjectByDescriptionInFAISSEmbedding("test", 3, (faissResponse) =>
        {
            if (faissResponse != null)
                Debug.Log("SearchObjectByDescriptionInFAISSEmbedding response: " + JsonUtility.ToJson(faissResponse));
            else
                Debug.LogError("SearchObjectByDescriptionInFAISSEmbedding failed.");
        });
        yield return new WaitForSeconds(5f);

        // 5. Test /get_main_object_and_descriptors_using_spacy (simplified version)
        NLPServerCommunicator.Instance.GetMainObjectAndDescriptorsInCommand("Show me the red car", (response) =>
        {
            if (response != null)
                Debug.Log("GetMainObjectAndDescriptorsInCommand response: " + JsonUtility.ToJson(response));
            else
                Debug.LogError("GetMainObjectAndDescriptorsInCommand failed.");
        });
        yield return new WaitForSeconds(5f);

        // 6. Test /get_objects_with_attributes_using_spacy
        NLPServerCommunicator.Instance.GetObjectsWithAttributesUsingSpacy("Show me the big red box on top of the small blue container", (response) =>
        {
            if (response != null)
                Debug.Log("GetObjectsWithAttributesUsingSpacy response: " + JsonUtility.ToJson(response));
            else
                Debug.LogError("GetObjectsWithAttributesUsingSpacy failed.");
        });
        yield return new WaitForSeconds(5f);

        // 11. Test /openai_get_objects_and_attributes_using_chatgpt
        NLPServerCommunicator.CommandInputSpatial inputPayload = new NLPServerCommunicator.CommandInputSpatial
        {
            command = "Find the red object on top of the blue one",
            main_object = "red object",
            spatial_info = "{\"sample\": \"spatial info\"}"
        };

        Debug.Log("Finished NLP functions tests.");
    }
}
