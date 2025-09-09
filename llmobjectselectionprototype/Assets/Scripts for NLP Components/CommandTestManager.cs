/*
 * Script Name: CommandTestManager.cs
 * Author: Brody Wells, SEER Lab
 * Date: 2025
 *
 * Project Context:
 * This script is part of the Unity project accompanying the research paper:
 *   "When to Call the LLM: Adaptive Speech Interfaces for Natural 
 *      Language Input in eXtended Reality Environments" - published in VRST 2025
 * Please cite the paper if you use this code in academic work.
 *
 * Overview:
 * CommandTestManager runs all tests defined in its "tests" list and records, per test:
 *   - Command        : The text command that is processed.
 *   - Difficulty     : A subjective rating (Easy, Moderate, Hard).
 *                      **Not used in our analysis; safe to ignore.**
 *   - Expected       : The number of objects expected to be returned.
 *   - Found          : The number of objects actually returned.
 *   - Correct        : The number of correctly returned objects.
 *   - Score          : An F1-style score computed from expected/found/correct.
 *   - StageCount     : The number of processing stages reached.
 *   - TotalTime      : Total time to process the command (seconds).
 *   - TotalCost      : Total USD cost of any OpenAI API calls made.
 *   - CheckRelationship : Time spent in stage: CheckRelationship.
 *   - ExtractMentions   : Time spent in stage: ExtractMentions.
 *   - ParseMainObject   : Time spent in stage: ParseMainObject.
 *   - NameSearch        : Time spent in stage: NameSearch.
 *   - DescSearch        : Time spent in stage: DescSearch.
 *   - Intersection      : Time spent in stage: Intersection.
 *   - RelationSearch    : Time spent in stage: RelationSearch.
 *
 * What it does:
 * - Maintains a list of test commands (command, difficulty, ambiguity).
 * - Tracks progress through a processing pipeline (ProcStage enum).
 * - Measures per-stage durations and total runtime, plus optional API cost.
 * - Computes an F1-style score from expected/found/correct counts.
 * - Exports results to CSV under Assets/Test Results/.
 * - Provides convenience menu actions: "Run Tests" and "Add Predefined Tests".
 *
 * How to run (Editor):
 * 1) Add this component to a GameObject in your scene (Editor only).
 * 2) Populate the "tests" list in the Inspector (or use "Add Predefined Tests").
 * 3) (Optional) For any test, enable "overrideCamera" and set cam pose. Required if commands are ego-centric.
 * 4) From the component context menu, choose "Run Tests" in play-mode.
 * 5) CSV will be written to: Assets/Test Results/<csvFileName>_<pipeline>_<timestamp>.csv
 *
 * Dependencies & Assumptions:
 * - Unity Editor only: the file is wrapped in #if UNITY_EDITOR.
 * - A GameObject named "OVRCameraRigInteraction" exists if camera overrides are used.
 * - SelectionController.Instance is present and exposes:
 *      - IEnumerator ProcessCommand(string)
 *      - SelectionPipelineVersion PipelineVersion (enum*
 *      
 * License:
 *   MIT License — SPDX-License-Identifier: MIT
 *   Copyright (c) 2025 Brody Wells, SEER Lab
 *
 *   Permission is hereby granted, free of charge, to any person obtaining a copy
 *   of this software and associated documentation files (the "Software"), to deal
 *   in the Software without restriction, including without limitation the rights
 *   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *   copies of the Software, and to permit persons to whom the Software is
 *   furnished to do so, subject to the following conditions:
 *   The above copyright notice and this permission notice shall be included in
 *   all copies or substantial portions of the Software.
 *   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 *   THE SOFTWARE.
 *
 * Suggested Citation (example):
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using Debug = UnityEngine.Debug;

public enum ProcStage
{
    None,
    CheckRelationship,
    ExtractMentions,
    ParseMainObject,
    NameSearch,
    DescSearch,
    Intersection,
    RelationSearch,
    Done
}

public class CommandTestManager : MonoBehaviour
{
    // ---------- Singleton ----------
    private static CommandTestManager _instance;
    public static CommandTestManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<CommandTestManager>();
            return _instance;
        }
    }

    // ---------- Test-case model ----------
    [System.Serializable]
    public class CommandCase
    {
        [Tooltip("Run this test when the suite executes")]
        public bool run = true;                 // default ON

        public string command;
        public Difficulty level;
        public List<GameObject> expected;
        public int ambiguousCount;

        [Tooltip("If true, the rig will be moved/rotated before the test starts")]
        public bool overrideCamera;

        [Tooltip("World-space position the rig should move to")]
        public Vector3 camPosition;

        [Tooltip("Yaw / pitch / roll in degrees (Euler angles)")]
        public Vector3 camEuler;

        public enum Difficulty { Easy, Moderate, Hard }
    }

    [Header("Test Script")]
    public List<CommandCase> tests = new();

    [Header("Output")]
    public string csvFileName = "CommandTestResults";

    // ---------- Runtime tracking ----------
    private readonly Dictionary<ProcStage, float> _stageStart = new();
    private CommandCase _current;
    private readonly List<Row> _rows = new();

    private class Row
    {
        public string cmd;
        public CommandCase.Difficulty diff;
        public int expected;
        public int found;
        public int correct;
        public int stageCount;
        public float totalTime;
        public float cost_usd;      // relevant when calling openai API
        public Dictionary<ProcStage, float> times = new();
    }

    private GameObject _rig;

    private void Awake()
    {
        _rig = GameObject.Find("OVRCameraRigInteraction");
        if (_rig == null)
            Debug.LogError("OVRCameraRig not found - camera poses won't be applied");
    }

    public void OnStageReached(ProcStage stage)
    {
        _stageStart[stage] = Time.realtimeSinceStartup;
    }

    public void OnResult(List<GameObject> found, float cost_usd)
    {
        if (_current == null)
        {
            MyLogger.Log("No current command set - cannot log results.");
            return;
        }

        // Ensure Done is always captured
        if (!_stageStart.ContainsKey(ProcStage.Done))
            _stageStart[ProcStage.Done] = Time.realtimeSinceStartup;

        var row = new Row
        {
            cmd = _current.command,
            diff = _current.level,
            expected = _current.expected?.Count ?? _current.ambiguousCount,
            found = found.Count,
            correct = _current.expected?.Intersect(found).Count() ?? 0,
            stageCount = _stageStart.Count,
            cost_usd = cost_usd
        };

        // Calculate time between each stage and Done
        foreach (var kvp in _stageStart)
        {
            var next = _stageStart
                .Where(p => p.Key > kvp.Key)
                .OrderBy(p => p.Key)
                .Select(p => (float?)p.Value)
                .FirstOrDefault() ?? _stageStart[ProcStage.Done];

            row.times[kvp.Key] = next - kvp.Value;
        }

        // Calculate total time (from first to Done)
        row.totalTime = _stageStart[ProcStage.Done] - _stageStart.OrderBy(kvp => kvp.Value).First().Value;

        _rows.Add(row);
    }

    // ---------- Test runner ----------
    [ContextMenu("Run Tests")]
    public void Run()
    {
        StartCoroutine(RunAll());
    }

    private IEnumerator RunAll()
    {
        // Reset all old data before running new test suite
        _rows.Clear();
        _stageStart.Clear();
        _current = null;
        foreach (var t in tests.Where(tc => tc.run))
        {
            _current = t;
            _stageStart.Clear();    // reset timing map

            ApplyCameraPose(t);
            yield return null;      // give the rig one frame to settle

                SelectionController.Instance.StartCoroutine(
                SelectionController.Instance.ProcessCommand(t.command)
            );

            float startTime = Time.realtimeSinceStartup;
            float timeout = 400f;   // Needs quite a large timeout for hard cases using full LLM

            while (_rows.All(r => r.cmd != t.command) && Time.realtimeSinceStartup - startTime < timeout)
                yield return null;

            if (Time.realtimeSinceStartup - startTime >= timeout)
                Debug.LogError($"[CommandTestManager] Timeout on command: {t.command}");

            yield return null; // small buffer
        }

        DumpCsv();
        MyLogger.Log($"<color=lime>Command tests complete. CSV saved to Assets/{csvFileName}</color>");
    }

    private void DumpCsv()
    {
        var stages = System.Enum.GetValues(typeof(ProcStage)).Cast<ProcStage>();
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        string subfolder = "Test Results";
        string controllerTag = SelectionController.Instance.PipelineVersion switch
        {
            SelectionController.SelectionPipelineVersion.Traditional_NLP           => "NLP",
            SelectionController.SelectionPipelineVersion.LLM_Assisted => "LLM_enhanced",
            SelectionController.SelectionPipelineVersion.Full_LLM => "LLM_full",
            _ => "Unknown"
        };
        string fullFilename = $"{csvFileName}_{controllerTag}_{timestamp}.csv";
        string folderPath = Path.Combine(Application.dataPath, subfolder);

        // Ensure the directory exists
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string path = Path.Combine(folderPath, fullFilename);

        using var sw = new StreamWriter(path);

        // header
        sw.Write("Command,Difficulty,Expected,Found,Correct,Score,StageCount,TotalTime,TotalCost");
        foreach (var s in stages) sw.Write($",{s}");
        sw.WriteLine();

        // rows
        foreach (var r in _rows)
        {
            float score = ComputeF1Score(r.expected, r.found, r.correct);
            sw.Write($"\"{r.cmd}\",{r.diff},{r.expected},{r.found},{r.correct},{score},{r.stageCount},{r.totalTime:F4},{r.cost_usd:F6}");

            foreach (var s in stages)
            {
                if (r.times.TryGetValue(s, out float time))
                    sw.Write($",{time:F4}");
                else
                    sw.Write(","); // blank if stage not hit
            }

            sw.WriteLine();
        }
    }

    float ComputeF1Score(int expected, int result, int correct)
    {
        if (expected == 0 && result == 0)
            return 1f;
        if (result == 0 || expected == 0)
            return 0f;

        float precision = correct / (float)result;
        float recall = correct / (float)expected;
        if (precision + recall == 0)
            return 0f;

        return 2f * (precision * recall) / (precision + recall);
    }


    [ContextMenu("Add Predefined Tests")]
    public void AddPredefinedTests()
    {
        // 1) Preset table
        (string cmd, CommandCase.Difficulty diff, int amb)[] presets =
        {
            // "user command",          difficulty rating,          abmiguity is how many objects would match their command. 0 = not ambiguous
            ("Whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("A whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("The whiteboard",      
                                    CommandCase.Difficulty.Easy,     0),
            ("Show me the whiteboard",       
                                    CommandCase.Difficulty.Easy,     0),
            ("Where's the whiteboard",      
                                    CommandCase.Difficulty.Easy,     0),
            ("Select the whiteboard",      
                                    CommandCase.Difficulty.Easy,     0),
            ("I want to see the whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("Highlight the whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("Can I see the whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("Can I use the whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            // ----------------- Search using descriptions and names ---------------------
            ("The red laptop",
                                    CommandCase.Difficulty.Easy,     0),
            ("The apple laptop",
                                    CommandCase.Difficulty.Easy,     0),
            ("The silver iPhone",
                                    CommandCase.Difficulty.Easy,     0),
            ("The musical keyboard",
                                    CommandCase.Difficulty.Moderate,     0),
            ("The yellow filing cabinet",
                                    CommandCase.Difficulty.Easy,     3),
            ("The blue office desk",
                                    CommandCase.Difficulty.Easy,     6),
            ("The table that is round and wooden",
                                    CommandCase.Difficulty.Moderate,     0),
            ("The blue magnet that is blue",
                                    CommandCase.Difficulty.Moderate,     1),
            ("The laptop with a Nvidia graphics card",
                                    CommandCase.Difficulty.Moderate,     0),
            ("The Laptop. Not the windows laptops, but the apple laptop",
                                    CommandCase.Difficulty.Hard,     0),
            ("The thing you draw on, I think it's called a whiteboard",
                                    CommandCase.Difficulty.Moderate,     0),
            // ----------------- Search using discriptions only ---------------------
            ("A large board I can write on, but not a chalkboard",
                                    CommandCase.Difficulty.Moderate,     0),
            ("A large board to write on",
                                    CommandCase.Difficulty.Moderate,     3),
            ("A convient place to store all my paper documents",
                                    CommandCase.Difficulty.Moderate,     11),
            ("A device I can use to browse the internet",
                                    CommandCase.Difficulty.Moderate,     11),
            ("A device I can connect to my computer to help in music production",
                                    CommandCase.Difficulty.Moderate,     1),
            ("A wired peripheral device I can connect to my computer to type on",
                                    CommandCase.Difficulty.Moderate,     5),
            ("A tool to stick pieces of paper together",
                                    CommandCase.Difficulty.Moderate,     2),
            ("Something I can use to make a phone call",
                                    CommandCase.Difficulty.Moderate,     0),
            ("Something I can throw my garbage into",
                                    CommandCase.Difficulty.Moderate,     2),

            // ----------------- Search using comparative or context dependent descriptions ---------------------
            ("The middle chalkboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("The largest poster",
                                    CommandCase.Difficulty.Easy,     0),
            ("The widest poster",
                                    CommandCase.Difficulty.Easy,     0),
            ("The poster that is most short",
                                    CommandCase.Difficulty.Easy,     0),
            ("The empty trash bin",
                                    CommandCase.Difficulty.Easy,     0),
            ("The topmost magnet",
                                    CommandCase.Difficulty.Easy,     1),
            ("The third cube",
                                    CommandCase.Difficulty.Easy,     0),
            ("The closest Oculus Quest Headset",
                                    CommandCase.Difficulty.Easy,     0),
            ("The door that is furthest away",
                                    CommandCase.Difficulty.Easy,     0),
            ("The door that is closest",
                                    CommandCase.Difficulty.Easy,     0),
            ("The poster furthest on the right",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("The chalkboard on the left",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            
            // ----------------- Search using single spatial relationship ---------------------
            ("The stapler on top of the round table",
                                    CommandCase.Difficulty.Easy,     0),
            ("The phone on the corner desk",
                                    CommandCase.Difficulty.Easy,     0),
            ("The office desk under the ceiling fan",
                                    CommandCase.Difficulty.Easy,     3),
            ("The garbage under the blue office desk",
                                    CommandCase.Difficulty.Hard,     1),        // This requires the full collider/mesh to be completely under and a desks collider might extend below the table surface (ie: the trash bin is not low enough)
            ("The garbage bin beside the office desk",
                                    CommandCase.Difficulty.Easy,     2),
            ("The trash in the bin beside the chalkboard",
                                    CommandCase.Difficulty.Easy,     4),
            ("The light directly above me",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("The door behind me",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("The Oculus Quest in the middle cabinet",
                                    CommandCase.Difficulty.Hard,     1),        // EGO-CENTRIC
            ("The trash bin with nothing in it",
                                    CommandCase.Difficulty.Easy,     0),
            ("The Oculus Quest 3 on the yellow office desk",
                                    CommandCase.Difficulty.Moderate,     0),        // Troubles distinguishing between quest 2 and quest 3
            
            // ----------------- Search using multiple spatial relationships ---------------------
            ("The blue office desk closest to the door beside the posters",
                                    CommandCase.Difficulty.Hard,     0),        // 'closest to' wants to evaluate in relation to the users position, not relative to the other object.
            ("The blue magnet on the board beside the sofa",
                                    CommandCase.Difficulty.Hard,     0),
            ("The trash in the bin beside the apple laptop",
                                    CommandCase.Difficulty.Hard,     4),
            ("The stapler on the desk closest to me",
                                    CommandCase.Difficulty.Hard,     0),
            ("The laptop near the blue office desk with an oculus quest on top of it",
                                    CommandCase.Difficulty.Hard,     0),
            ("The chair beside the lamp and the iphone",
                                    CommandCase.Difficulty.Hard,     0),
            ("The oculus quest in the cabinet closest to the whiteboard",
                                    CommandCase.Difficulty.Hard,     0),
            ("The chair nearest the yellow office desk closest to the door next to the posters",
                                    CommandCase.Difficulty.Hard,     0),
            ("The device, that can be used to staple paper, that inside the filing cabinet. The filing cabinet under the office desk with an oculus quest on it.",              // Requires intersection threshold of 0.085 as not to exclude blue desks
                                    CommandCase.Difficulty.Hard,     0),
            ("The thing on the blue office desk. Its a light, but not the light on the ceiling",
                                    CommandCase.Difficulty.Hard,     0),
            ("The Oculus Quest second from the left in the cabinets in front of me",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            

            // -----------------------------------------------------------
            // ----------------- Adapted from RefEgo ---------------------
            // -----------------------------------------------------------
            //("A bakery tray that goes in and out of the bottom shelf of a cooker",
            //                        CommandCase.Difficulty.Hard,     0),
            ("The chalk of the chalkboard",
                                    CommandCase.Difficulty.Easy,     2),
            ("The chalk of the middle chalkboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("the laptop placed on the desk near the wall shelves", 
                                    CommandCase.Difficulty.Hard,     0),                // Corner wooden desk is not found in FAISS search by quering 'desk'
            ("the laptop placed on the corner office desk near the wall shelves",
                                    CommandCase.Difficulty.Moderate,     0),
            ("The lamp on the blue office desk next to the door",
                                    CommandCase.Difficulty.Easy,     0),
            ("The chalkboard on the left side of the wall",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("The MIDI Keyboard to the right of the monitor",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("the laptop on the desk near the chalkboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("A stapler on the table close to the whiteboard",
                                    CommandCase.Difficulty.Easy,     0),
            ("Blue cube on the top shelf of the wooden shelves",
                                    CommandCase.Difficulty.Easy,     0),
            ("Cube on the top right of the wooden shelves",
                                    CommandCase.Difficulty.Easy,     1),        // EGO-CENTRIC
            ("The black laptop sitting on the blue desk to the right of the other black laptop",
                                    CommandCase.Difficulty.Easy,     0),        // EGO-CENTRIC
            ("the red magnet in the center of the circular table",
                                    CommandCase.Difficulty.Easy,     0),
            ("Select the cube on the top shelf",
                                    CommandCase.Difficulty.Hard,     3),        // Whats to pick the top cube, rather than cube on the top shelf
             //----------------- Adapted from RefEgo extra long ---------------------
            ("The black chair that serves as a seat. It is to the right of the macbook pro and has some levers on it, as well as swivel mechanism so it can be turned.",
                                    CommandCase.Difficulty.Easy,     0),
            ("Red cube on the wooden shelf right next to another yellow cube and green cube",
                                    CommandCase.Difficulty.Easy,     0),
            ("the monitor connected to the mouse and the keyboard placed on the office desk",
                                    CommandCase.Difficulty.Easy,     4),
            ("The cube on the bottom shelf in front of a red cube, between a red cube and a green cube",
                                    CommandCase.Difficulty.Easy,     0),
            ("Where's a spot I can take a nap?",
                                    CommandCase.Difficulty.Hard,     0),
            ("Is there some sort of device I can use to send a few emails",
                                    CommandCase.Difficulty.Hard,     11),
            ("What can I use to check my online bank statement?",
                                    CommandCase.Difficulty.Hard,     11),
            ("Where can I plug my USB stick from my camera into so i can upload my photos",
                                    CommandCase.Difficulty.Hard,     11),
            ("Where am I most likely to find the hard copies of last years tax reports",
                                    CommandCase.Difficulty.Hard,     11),
        };

        int added = 0;

        // 2) For each preset, check for an existing test with same command
        foreach (var p in presets)
        {
            bool exists = tests.Any(t =>
                string.Equals(t.command, p.cmd, StringComparison.OrdinalIgnoreCase));

            if (exists)
                continue;                        // skip duplicates

            tests.Add(new CommandCase
            {
                command = p.cmd,
                level = p.diff,
                expected = new List<GameObject>(),
                ambiguousCount = p.amb
            });

            added++;
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(this);
            Debug.Log($"Added {added} new predefined test(s).");
        }
        else
        {
            Debug.Log("No predefined tests added - all presets already exist.");
        }
    }



    private void ApplyCameraPose(CommandCase tc)
    {
        if (!tc.overrideCamera || _rig == null) return;

        // -- move & rotate the *root* of the rig.
        // The HMD tracking still adds on top of this,
        // so we only need to set a base pose.
        _rig.transform.SetPositionAndRotation(
            tc.camPosition,
            Quaternion.Euler(tc.camEuler)
        );
    }

}
#endif