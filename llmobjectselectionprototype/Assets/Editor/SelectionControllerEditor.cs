/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 */


using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SelectionController))]
public class SelectionControllerEditor : Editor
{
    private GUIStyle _wrapStyle;
    private GUIStyle _bigStatusStyle;

    private static GUIContent ErrorIcon =>
        EditorGUIUtility.IconContent("console.erroricon") ?? new GUIContent("?");
    private static GUIContent WarningIcon =>
    EditorGUIUtility.IconContent("console.warnicon") ?? new GUIContent("?");

    private static GUIContent PassIcon =>
        EditorGUIUtility.IconContent("TestPassed") ?? new GUIContent("?");

    public override void OnInspectorGUI()
    {
        var ctrl = (SelectionController)target;

        if (_wrapStyle == null)
            _wrapStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };

        if (_bigStatusStyle == null)
        {
            _bigStatusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                fontSize = 12
            };
        }

        // ------------------ Quick Start ------------------
        EditorGUILayout.LabelField("Quick Start", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("HelpBox"))
        {
            EditorGUILayout.LabelField(
                "1) Ensure the Python FastAPI server is running on localhost.\n" +
                "2) Update the FAISS vector on first launch (and after any server restart).\n" +
                "3) On Quest 3, say 'Hey Quest' to activate voice input.\n" +
                "4) Without a headset, type a command in the text field and press the Process Command button.",
                _wrapStyle);
        }

        EditorGUILayout.Space(6);

        // ------------------ FAISS Status ------------------
        EditorGUILayout.LabelField("FAISS Status", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("HelpBox"))
        {
            var status = ctrl.LastFaissStatus;
            bool hasRun = status != null && status.hasResult;
            bool ok = hasRun && status.ok && status.objectsAdded > 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!ok)
                {
                    var prev = GUI.contentColor;
                    GUI.contentColor = new Color(1.0f, 0.75f, 0.0f);
                    GUILayout.Label(WarningIcon, GUILayout.Width(20), GUILayout.Height(20));
                    GUILayout.Label("If the Python API server has been reset, FAISS Vector must be reupdated", _bigStatusStyle);
                    GUI.contentColor = prev;
                }
                else
                {
                    var prev = GUI.contentColor;
                    GUI.contentColor = new Color(0.2f, 0.7f, 0.3f);
                    GUILayout.Label(PassIcon, GUILayout.Width(20), GUILayout.Height(20));
                    GUILayout.Label($"FAISS Updated: {status.objectsAdded} objects", _bigStatusStyle);
                    GUI.contentColor = prev;
                }
            }

            // Only the Update button - no timestamp / message lines
            if (Application.isPlaying)
            {
                if (GUILayout.Button("? Update FAISS"))
                    ctrl.TriggerUpdateFAISS();
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use Update FAISS.", MessageType.Info);
            }
        }

        EditorGUILayout.Space(6);

        // ------------------ Default inspector fields ------------------
        serializedObject.Update();
        var prop = serializedObject.GetIterator();
        bool expanded = true;

        while (prop.NextVisible(expanded))
        {
            expanded = false;

            // Skip script reference
            if (prop.propertyPath == "m_Script") continue;

            // Detect the searchString property to inject Process Command button
            if (prop.propertyPath == "searchString")
            {
                EditorGUILayout.PropertyField(prop, true);

                if (Application.isPlaying)
                {
                    if (GUILayout.Button("? Process Command"))
                        ctrl.TriggerProcessCommand();
                }
                else
                {
                    EditorGUILayout.HelpBox("Enter Play Mode to process commands.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);

        // ------------------ Extra Debug Buttons ------------------
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Debug / Extra Actions", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("? Debug Forward")) ctrl.TriggerDebugForward();
                if (GUILayout.Button("? Temp Function")) ctrl.TriggerTempFunction();
            }
        }
    }
}
