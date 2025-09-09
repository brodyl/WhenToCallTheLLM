using UnityEngine;
using UnityEngine.Events;
using TMPro;
using Oculus.Voice;
using System.Reflection;
using Meta.WitAi.CallbackHandlers;
using System.Collections;

public class VoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;
    [SerializeField] private WitResponseMatcher responseMatcher;
    [SerializeField] private TextMeshProUGUI transcriptionText;

    [Header("Voice Events")]
    [SerializeField] private UnityEvent wakeWordDetected;
    [SerializeField] private UnityEvent<string> completeTranscription;

    [Tooltip("Seconds to keep the final command visible")]
    [SerializeField] private float holdDuration = 5.0f;

    private Coroutine _clearRoutine;
    private readonly Color _partialCol = Color.red;
    private readonly Color _finalCol = Color.green;
    private readonly Color _clearCol = new Color(1, 1, 1, 0);   // invisible

    private bool _voiceCommandReady;

    private void Awake()
    {
        // Setup event listeners
        appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(ReactivateVoice);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscription);

        var eventField = typeof(WitResponseMatcher).GetField("onMultiValueEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent multiValueEvent)
        {
            multiValueEvent.AddListener(WakeWordDetected);
        }
    }

    private IEnumerator Start()
    {
        // Request mic permission if needed
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            // Give Quest a moment to initialize the mic
            yield return new WaitForSeconds(0.5f);
            appVoiceExperience.Activate();
        }
        else
        {
            Debug.LogWarning("Microphone permission not granted!");
        }
    }

    private void OnDestroy()
    {
        // Remove event listeners
        appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(ReactivateVoice);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscription);

        // Deactivate voice to free mic
        appVoiceExperience.Deactivate();

        var eventField = typeof(WitResponseMatcher).GetField("onMultiValueEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent multiValueEvent)
        {
            multiValueEvent.RemoveListener(WakeWordDetected);
        }
        if (appVoiceExperience != null)
        {
            appVoiceExperience.Deactivate();
        }
    }

    private void ReactivateVoice()
    {
        // Find the Mic in the scene (if it exists)
        var micObject = FindObjectOfType<Meta.WitAi.Lib.Mic>();
        if (micObject != null)
        {
            // Clear the cached device list via reflection
            typeof(Meta.WitAi.Lib.Mic)
                .GetField("_micDevices", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(micObject, null);
        }
        appVoiceExperience.Activate();
    }

    private void WakeWordDetected(string[] arg0)
    {
        _voiceCommandReady = true;

        CancelClearTimer();                   // stop any pending fadeout
        transcriptionText.color = _partialCol;   // reset colour in case
        transcriptionText.text = string.Empty;
        wakeWordDetected?.Invoke();
    }

    private void OnPartialTranscription(string transcription)
    {
        if (!_voiceCommandReady) return;

        CancelClearTimer();                   // allow live updates
        transcriptionText.color = _partialCol;
        transcriptionText.text = transcription;
    }

    private void OnFullTranscription(string transcription)
    {
        if (!_voiceCommandReady) return;
        _voiceCommandReady = false;

        transcriptionText.color = _finalCol;  // switch to green
        transcriptionText.text = transcription;
        // restart the fade?out timer
        CancelClearTimer();
        _clearRoutine = StartCoroutine(ClearAfterDelay());

        if (transcription.ToLower() != "hey quest")
        {
            SelectionController.Instance.searchString = transcription;
            Debug.Log("Processing command: " + transcription);
            SelectionController.Instance.StartCoroutine(SelectionController.Instance.ProcessCommand(transcription));
        }
        else
        {
            Debug.Log("Ignoring wake word \"Hey quest\"");
        }

        completeTranscription?.Invoke(transcription);
    }

    // ---------- helpers ----------
    private IEnumerator ClearAfterDelay()
    {
        yield return new WaitForSeconds(holdDuration);
        transcriptionText.color = _clearCol;
        transcriptionText.text = string.Empty;
        _clearRoutine = null;                 // mark as finished
    }

    private void CancelClearTimer()
    {
        if (_clearRoutine != null)
        {
            StopCoroutine(_clearRoutine);
            _clearRoutine = null;
        }
    }
}
