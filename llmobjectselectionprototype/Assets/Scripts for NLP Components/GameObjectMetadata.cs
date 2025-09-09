#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class GameObjectMetadata : MonoBehaviour
{
    // Unique identifier for this GameObject
    public string id;

    // Core Attributes
    public string objectName;   // Default: GameObject name
    public string description;  // Custom description for NLP (manual or auto-generated)

    // Called in the Editor (and at runtime if needed) when values change
    void OnValidate()
    {
        // Generate a unique GUID if one hasn't been assigned already.
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();

            // Mark the object as dirty so Unity knows to save the change
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        // Optionally auto-fill attributes in edit mode as well
        AutoFillAttributes();
    }

    // Fallback for runtime if OnValidate was not executed
    void Awake()
    {
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
        }
    }

    void Start()
    {
        AutoFillAttributes();
    }

    void AutoFillAttributes()
    {
        // 1. Assign object name if not set
        if (string.IsNullOrEmpty(objectName))
            objectName = gameObject.name;

        // 8. Generate a fallback description
        if (string.IsNullOrEmpty(description))
            description = GenerateDescription();
    }


    // Generates a basic description using known attributes
    string GenerateDescription()
    {
        // This is a stub for now, but ideally we would ask the llama model to generate a description based on it's name.
        return $"{objectName}";
    }

    // Returns a dictionary of all key attributes (for NLP processing)
    public Dictionary<string, object> GetAttributes()
    {
        return new Dictionary<string, object>
        {
            { "id", id },
            { "name", objectName },
            { "description", description }
        };
    }

    public Transform GetTransform()
    {
        return transform;
    }
}
