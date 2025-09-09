using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach this script to an empty GameObject. On Start(), it will
/// sequentially test each of the NLPServerCommunicator's methods.
/// </summary>
public class NLPServerCommunicatorTester : MonoBehaviour
{
    private NLPServerCommunicator _communicator;

    private void Start()
    {
        // Get reference to the NLPServerCommunicator singleton
        _communicator = NLPServerCommunicator.Instance;

        // Run tests in a coroutine
        StartCoroutine(RunAllTests());
    }

    private IEnumerator RunAllTests()
    {
        Debug.Log("Starting NLPServerCommunicator tests...");

        // 1) create_faiss_embeddings
        yield return StartCoroutine(Test_CreateFaissEmbeddings());

        // 2) search_name_in_faiss
        yield return StartCoroutine(Test_SearchNameInFAISSEmbedding());

        // 3) search_description_in_faiss
        yield return StartCoroutine(Test_SearchDescriptionInFAISSEmbedding());

        // 5) get_main_object_and_descriptors_using_spacy
        yield return StartCoroutine(Test_GetMainObjectAndDescriptorsInCommand());

        // 6) get_objects_with_attributes_using_spacy
        yield return StartCoroutine(Test_GetObjectsWithAttributesUsingSpacy());

        Debug.Log("All tests complete!");
    }

    //----------------------------------------------------------------------------
    // 1) /create_faiss_embeddings
    //----------------------------------------------------------------------------
    private IEnumerator Test_CreateFaissEmbeddings()
    {
        Debug.Log("[Test] create_faiss_embeddings...");

        // Prepare test data
        List<NLPServerCommunicator.UnityObject> sampleObjects = new List<NLPServerCommunicator.UnityObject>
        {
            new NLPServerCommunicator.UnityObject
            {
                id = "obj_1",
                name = "Cube",
                description = "A small red cube used as a dice"
            },
            new NLPServerCommunicator.UnityObject
            {
                id = "obj_2",
                name = "Sphere",
                description = "A shiny blue sphere for decoration"
            }
        };

        // Call the method
        _communicator.CreateFaissEmbeddings(sampleObjects, (response) =>
        {
            if (response == null)
            {
                Debug.LogError("create_faiss_embeddings returned null.");
            }
            else
            {
                Debug.Log($"[Test] create_faiss_embeddings success: {response.message}, total={response.total_objects}");
            }
        });

        // Wait a short time for async callback
        yield return new WaitForSeconds(1f);
    }

    //----------------------------------------------------------------------------
    // 2) /search_name_in_faiss
    //----------------------------------------------------------------------------
    private IEnumerator Test_SearchNameInFAISSEmbedding()
    {
        Debug.Log("[Test] search_name_in_faiss...");

        _communicator.SearchObjectByNameInFAISSEmbedding("cube", 3, (faissResponse) =>
        {
            if (faissResponse == null || faissResponse.matches == null)
            {
                Debug.LogError("search_name_in_faiss returned null or empty.");
            }
            else
            {
                Debug.Log($"[Test] search_name_in_faiss results:");
                foreach (var match in faissResponse.matches)
                {
                    Debug.Log($" - ID={match.id}, name={match.name}, distance={match.distance}");
                }
            }
        });

        yield return new WaitForSeconds(1f);
    }

    //----------------------------------------------------------------------------
    // 3) /search_description_in_faiss
    //----------------------------------------------------------------------------
    private IEnumerator Test_SearchDescriptionInFAISSEmbedding()
    {
        Debug.Log("[Test] search_description_in_faiss...");

        _communicator.SearchObjectByDescriptionInFAISSEmbedding("red dice", 3, (faissResponse) =>
        {
            if (faissResponse == null || faissResponse.matches == null)
            {
                Debug.LogError("search_description_in_faiss returned null or empty.");
            }
            else
            {
                Debug.Log($"[Test] search_description_in_faiss results:");
                foreach (var match in faissResponse.matches)
                {
                    Debug.Log($" - ID={match.id}, name={match.name}, distance={match.distance}");
                }
            }
        });

        yield return new WaitForSeconds(1f);
    }

    //----------------------------------------------------------------------------
    // 5) /get_main_object_and_descriptors_using_spacy
    //----------------------------------------------------------------------------
    private IEnumerator Test_GetMainObjectAndDescriptorsInCommand()
    {
        Debug.Log("[Test] get_main_object_and_descriptors_using_spacy...");

        string testCommand = "Place the bright green cylinder beside the tall red building";
        _communicator.GetMainObjectAndDescriptorsInCommand(testCommand, (response) =>
        {
            if (response == null)
            {
                Debug.LogError("get_main_object_and_descriptors_using_spacy returned null.");
            }
            else
            {
                Debug.Log("[Test] main_object: " + response.main_object);
                if (response.descriptors != null)
                {
                    Debug.Log("[Test] descriptors: " + string.Join(", ", response.descriptors));
                }
            }
        });

        yield return new WaitForSeconds(1f);
    }

    //----------------------------------------------------------------------------
    // 6) /get_objects_with_attributes_using_spacy
    //----------------------------------------------------------------------------
    private IEnumerator Test_GetObjectsWithAttributesUsingSpacy()
    {
        Debug.Log("[Test] get_objects_with_attributes_using_spacy...");

        string testCommand = "Show me the big red pillow on top of the small couch";
        _communicator.GetObjectsWithAttributesUsingSpacy(testCommand, (response) =>
        {
            if (response == null || response.objects == null)
            {
                Debug.LogError("get_objects_with_attributes_using_spacy returned null.");
            }
            else
            {
                Debug.Log("[Test] Objects with attributes:");
                foreach (var objInfo in response.objects)
                {
                    Debug.Log($"Object Text: {objInfo.object_text}, Head: {objInfo.head}");
                    Debug.Log("   Descriptors: " + string.Join(", ", objInfo.descriptors ?? new List<string>()));

                    if (objInfo.relations != null)
                    {
                        foreach (var rel in objInfo.relations)
                        {
                            Debug.Log($"   Relation Phrase: {rel.relation_phrase}");
                            Debug.Log($"   Related Object:  {rel.related_object}");
                            Debug.Log($"   Related Descriptors: {string.Join(", ", rel.related_descriptors ?? new List<string>())}");
                        }
                    }
                }
            }
        });

        yield return new WaitForSeconds(1f);
    }
}
