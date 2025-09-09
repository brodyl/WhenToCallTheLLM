/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using System.Collections.Generic;
using System.Linq;
using UnityEngine;



/// <summary>
/// Manages all GameObjectMetadata instances in the scene using a Singleton pattern.
/// Provides methods to initialize, retrieve, and manage GameObjectMetadata objects by their unique IDs.
/// The gameobjects will be stored in a FAISS index, which can easily retrieve the object id's of the objects that are similar to the users voice command
///     which can then be used to get the gameobject reference.
/// </summary>
public class GameObjectManager : MonoBehaviour
{
    // The Singleton instance
    public static GameObjectManager Instance { get; private set; }

    // Dictionary mapping unique IDs to GameObjectMetadata instances
    public Dictionary<string, GameObjectMetadata> objectDictionary = new Dictionary<string, GameObjectMetadata>();
    public List<string> sceneSpecificNamedEntities = new List<string>();

    void Awake()
    {
        // Implement the Singleton pattern: if an instance already exists and it's not this, destroy this.
        if (Instance == null)
        {
            Instance = this;
            // Optionally, persist across scene loads:
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // Populate the dictionary at start-up
        InitializeObjectDictionary();
        InitializeNamedEntities();
    }

    // Initializes the dictionary by finding all GameObjectMetadata components in the scene
    public void InitializeObjectDictionary()
    {
        objectDictionary.Clear();
        GameObjectMetadata[] objects = FindObjectsByType<GameObjectMetadata>(FindObjectsSortMode.None);
        foreach (var obj in objects)
        {
            //Check is obj has an id that already exists in the dictionary
            if (!objectDictionary.ContainsKey(obj.id))
            {
                objectDictionary[obj.id] = obj;
            }
            else
            {
                // Give an error to console since we shouldn't have duplicate id's
                Debug.LogError("ERROR - " + obj.name + " has a duplicate id " + obj.id);
            }

            // Check if obj has a Highlighable component
            if (!obj.TryGetComponent<Highlightable>(out Highlightable highlightable))
            {
                // If not add one and display error for userr
                Highlightable tempHighlightable = new Highlightable();
                obj.gameObject.AddComponent<Highlightable>();
                Debug.LogWarning("WARNING - " + obj.name + " does not have a Highlightable component. Adding one automatically.");
            }

        }

        // Create a new sorted dictionary based on objectName
        objectDictionary = objectDictionary
            .OrderBy(pair => pair.Value.objectName)  // Sort by objectName
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }    // Initializes the dictionary by finding all GameObjectMetadata components in the scene
    public void InitializeNamedEntities()
    {
        HashSet<string> uniqueNames = new HashSet<string>();

        // Loop through each unique GameObjectMetadata value in the objectDictionary and add the name to the sceneSpecificNamedEntities
        foreach (GameObjectMetadata metadata in objectDictionary.Values)
        {
            uniqueNames.Add(metadata.objectName);
        }
        sceneSpecificNamedEntities = uniqueNames.ToArray().ToList();
        sceneSpecificNamedEntities.Sort();
    }

    // Retrieves a GameObjectMetadata by its unique id
    public GameObjectMetadata GetObjectById(string id)
    {
        objectDictionary.TryGetValue(id, out GameObjectMetadata result);
        return result;
    }

    // Retrieves a GameObjectMetadata by its unique id
    public List<string> GetSceneSpecificNamedEntities()
    {
        return sceneSpecificNamedEntities;
    }

    public Dictionary<string, string> BuildSceneObjectNameDictionary()
        => objectDictionary
           .OrderBy(pair => pair.Value.objectName)
           .ToDictionary(
               pair => pair.Key,
               pair => pair.Value.objectName);

    public Dictionary<string, string> BuildSceneObjectDescriptionDictionary()
        => objectDictionary.ToDictionary(
               pair => pair.Key,          // id
               pair => pair.Value.description);
}
