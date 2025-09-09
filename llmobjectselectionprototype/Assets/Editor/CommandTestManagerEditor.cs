#if UNITY_EDITOR
using System.Runtime.Remoting.Messaging;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(CommandTestManager))]
public class CommandTestManagerEditor : Editor
{
    private ReorderableList _list;
    private int _pickIndex = -1;                     // current “Pick Objects” index
    private CommandTestManager M => (CommandTestManager)target;


    // ----------------------------------------------
    //  initialisation
    // ----------------------------------------------
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;

        _list = new ReorderableList(
            serializedObject,
            serializedObject.FindProperty("tests"),
            draggable: true,
            displayHeader: true,
            displayAddButton: true,
            displayRemoveButton: true
        );

        _list.drawHeaderCallback = DrawHeader;
        _list.drawElementCallback = DrawElement;
        _list.elementHeightCallback = GetElementHeight;
        _list.onAddCallback = list => AddNewTest();
        _list.onRemoveCallback = list => RemoveTest(list.index);
        _list.onReorderCallback = list => _pickIndex = -1;   // cancel pick mode
    }

    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    // ----------------------------------------------
    //  Inspector GUI
    // ----------------------------------------------
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        /* draw everything except the tests list */
        DrawPropertiesExcluding(serializedObject, "tests");
        EditorGUILayout.Space(4);

        _list.DoLayoutList();                      // the fancy list

        EditorGUILayout.Space(6);

        /* global buttons */
        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Run Tests")) M.Run();
        GUI.enabled = true;

        if (GUILayout.Button("Add Predefined Tests"))
        {
            M.AddPredefinedTests();
            Repaint();
        }

        if (_pickIndex >= 0)
            EditorGUILayout.HelpBox(
                $"Picking mode ON – click Scene objects to add to test #{_pickIndex}.",
                MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    // ----------------------------------------------
    //  ReorderableList callbacks
    // ----------------------------------------------
    private void DrawHeader(Rect rect)
    {
        const float TOGGLE_W = 18f;
        const float LABEL_W = 70f;
        float line = EditorGUIUtility.singleLineHeight;

        /* 1) label */
        Rect titleRect = new Rect(rect.x, rect.y, rect.width - TOGGLE_W - 4, line);
        EditorGUI.LabelField(titleRect, "Test Cases", EditorStyles.boldLabel);

        /* 2) label beside the toggle */
        Rect labelRect = new Rect(rect.xMax - TOGGLE_W - LABEL_W - 4, rect.y, LABEL_W, line);
        EditorGUI.LabelField(labelRect, "Toggle All");

        /* 2) aggregate state of all tests */
        SerializedProperty arr = _list.serializedProperty;
        bool anyTrue = false, anyFalse = false;

        for (int i = 0; i < arr.arraySize; i++)
        {
            bool v = arr.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("run").boolValue;
            anyTrue |= v;
            anyFalse |= !v;
        }
        bool allTrue = anyTrue && !anyFalse;   // every test ON?

        /* 3) draw the master toggle */
        EditorGUI.showMixedValue = anyTrue && anyFalse;   // show dash if mixed

        Rect chkRect = new Rect(rect.xMax - TOGGLE_W, rect.y, TOGGLE_W, line);
        bool clickedVal = EditorGUI.Toggle(chkRect, allTrue);

        EditorGUI.showMixedValue = false;

        /* 4) only change array if user actually clicked */
        if (clickedVal != allTrue)
        {
            for (int i = 0; i < arr.arraySize; i++)
                arr.GetArrayElementAtIndex(i)
                   .FindPropertyRelative("run").boolValue = clickedVal;
        }
    }

    private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty el = _list.serializedProperty.GetArrayElementAtIndex(index);
        float line = EditorGUIUtility.singleLineHeight;
        float pad = 2f;

        /* -- 1) Foldout toggle ----------------------- */
        Rect foldRect = new Rect(rect.x, rect.y, 15, line);
        el.isExpanded = EditorGUI.Foldout(foldRect, el.isExpanded, GUIContent.none, true);

        /* -- 2) Individual 'Run' checkbox ------------ */
        Rect chkRect = new Rect(rect.x + 18, rect.y, 18, line);
        SerializedProperty runProp = el.FindPropertyRelative("run");
        runProp.boolValue = EditorGUI.Toggle(chkRect, runProp.boolValue);

        /* -- 3) Command name label ------------------- */
        Rect labelRect = new Rect(rect.x + 40, rect.y, rect.width - 120, line);
        EditorGUI.LabelField(labelRect, el.FindPropertyRelative("command").stringValue);

        /* -- 4) Run Button --------------------------- */
        GUI.enabled = Application.isPlaying;
        Rect btnRect = new Rect(rect.xMax - 40, rect.y, 35, line);
        if (GUI.Button(btnRect, "Run"))
        {
            string cmd = el.FindPropertyRelative("command").stringValue;
            RunSingleTest(cmd);
        }
        GUI.enabled = true;

        if (!el.isExpanded) return;

        /* indent block */
        Rect r = new Rect(rect.x + 15, rect.y + line + pad, rect.width - 15, line);

        // command
        EditorGUI.PropertyField(r, el.FindPropertyRelative("command"));
        r.y += line + pad;

        // difficulty
        EditorGUI.PropertyField(r, el.FindPropertyRelative("level"));
        r.y += line + pad;

        // expected list
        SerializedProperty exp = el.FindPropertyRelative("expected");
        if (!exp.isExpanded) exp.isExpanded = true;
        float expHeight = EditorGUI.GetPropertyHeight(exp);
        r.height = expHeight;
        EditorGUI.PropertyField(r, exp, true);
        r.y += expHeight + pad;

        // ambiguousCount
        r.height = line;
        EditorGUI.PropertyField(r, el.FindPropertyRelative("ambiguousCount"));
        r.y += line + pad;

        // override cam toggle
        SerializedProperty ovr = el.FindPropertyRelative("overrideCamera");
        EditorGUI.PropertyField(r, ovr, new GUIContent("Override Camera"));
        r.y += line + pad;

        if (ovr.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(r, el.FindPropertyRelative("camPosition"));
            r.y += line + pad;
            EditorGUI.PropertyField(r, el.FindPropertyRelative("camEuler"));
            r.y += line + pad;
            EditorGUI.indentLevel--;
        }

        /* --- custom buttons row --------------------------------------- */
        float w = 90;
        r.height = line;
        Rect pickRect = new Rect(r.x, r.y, w, line);
        Rect setRect = new Rect(r.x + w + 5, r.y, 70, line);
        Rect gotoRect = new Rect(r.x + w + 5 + 70 + 5, r.y, 70, line);

        bool pick = GUI.Toggle(pickRect, _pickIndex == index, "Pick Objects", "Button");
        if (pick && _pickIndex != index) _pickIndex = index;
        else if (!pick && _pickIndex == index) _pickIndex = -1;

        GUI.enabled = SceneView.lastActiveSceneView != null;
        if (GUI.Button(setRect, "Set Cam")) CaptureSceneViewPose(index);
        if (GUI.Button(gotoRect, "Goto Cam")) MoveSceneViewToPose(index);
        GUI.enabled = true;
    }

    private void RunSingleTest(string command)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Can't run test outside of Play mode.");
            return;
        }

        if (SelectionController.Instance != null)
        {
            M.StartCoroutine(SelectionController.Instance.ProcessCommand(command));
        }
        else
        {
            Debug.LogError("SelectionController.Instance is null.");
        }
    }



    /* dynamic element height */
    private float GetElementHeight(int index)
    {
        SerializedProperty el = _list.serializedProperty.GetArrayElementAtIndex(index);
        float line = EditorGUIUtility.singleLineHeight;
        float pad = 2f;
        float h = line + pad;                  // header line

        if (!el.isExpanded) return h;

        h += (line + pad) * 3;                 // command, level, ambiguous
        h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("expected")) + pad;

        h += line + pad;                       // override toggle
        if (el.FindPropertyRelative("overrideCamera").boolValue)
            h += (line + pad) * 2;             // pos + euler

        h += line + pad;                       // buttons row
        return h;
    }

    /* add / remove helpers keep Undo + serializedObject happy */
    private void AddNewTest()
    {
        var arr = _list.serializedProperty;
        arr.arraySize++;
        var el = arr.GetArrayElementAtIndex(arr.arraySize - 1);
        el.isExpanded = true;                       // open the test itself
        el.FindPropertyRelative("expected").isExpanded = true;   // open list
    }
    private void RemoveTest(int idx)
    {
        _pickIndex = -1;
        _list.serializedProperty.DeleteArrayElementAtIndex(idx);
    }


    // ----------------------------------------------
    //  scene-view camera helpers
    // ----------------------------------------------
    private void CaptureSceneViewPose(int idx)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        Undo.RecordObject(M, "Set Camera Pose");
        var tc = M.tests[idx];
        tc.overrideCamera = true;
        tc.camPosition = sv.camera.transform.position;
        tc.camEuler = sv.camera.transform.eulerAngles;

        EditorUtility.SetDirty(M);
    }

    private void MoveSceneViewToPose(int idx)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        var tc = M.tests[idx];
        if (sv == null || !tc.overrideCamera) return;

        sv.pivot = tc.camPosition;
        sv.rotation = Quaternion.Euler(tc.camEuler);
        sv.size = 0.5f;
        sv.Repaint();

        GameObject OVRRig = GameObject.Find("OVRCameraRigInteraction"); ;                        // reference to the OVR rig, used for camera pose capture
        if (OVRRig != null)
        {
            // also move the OVR rig to the same position
            OVRRig.transform.position = tc.camPosition;
            OVRRig.transform.eulerAngles = tc.camEuler;
        }
    }


    // ----------------------------------------------
    //  object-picking
    // ----------------------------------------------
    private void OnSceneGUI(SceneView sv)
    {
        if (_pickIndex < 0) return;

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 &&
            !e.alt && !e.shift && !e.control)
        {
            GameObject g = HandleUtility.PickGameObject(e.mousePosition, false);
            if (g != null && g.TryGetComponent<GameObjectMetadata>(out _))
            {
                Undo.RecordObject(M, "Add Expected Object");
                var list = M.tests[_pickIndex].expected;
                if (!list.Contains(g)) list.Add(g);
                EditorUtility.SetDirty(M);
            }
            e.Use();
        }
    }
}
#endif
