using UnityEngine;

public static class MyLogger
{
    public static bool DebugEnabled = true; // Toggle this flag

    public static void Log(string message)
    {
        if (DebugEnabled)
        {
            Debug.Log(message);
        }
    }

    public static void LogWarning(string message)
    {
        if (DebugEnabled)
        {
            Debug.LogWarning(message);
        }
    }

    public static void LogError(string message)
    {
        if (DebugEnabled)
        {
            Debug.LogError(message);
        }
    }
}
