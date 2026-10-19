using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
[RequireComponent(typeof(PuppetBoneTest))]
public sealed class PuppetHttpReceiver : MonoBehaviour
{
    [SerializeField] private string endpoint = "http://127.0.0.1:5000/puppet_state";
    [SerializeField, Min(0.02f)] private float pollInterval = 0.033f;
    [SerializeField, Min(0.1f)] private float failureRetryInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float disconnectAfter = 0.5f;
    [SerializeField] private bool logConnectionChanges = true;

    public PuppetBoneTest target;
    [SerializeField] private ActionManager actionManager;
    public bool IsConnected { get; private set; }
    public PuppetControlState LatestState { get; private set; }

    private float lastSuccessTime = float.NegativeInfinity;
    private Coroutine pollingRoutine;
    private int lastActionSequence;
    private bool hasInitialActionSequence;

    private void Awake()
    {
        target ??= GetComponent<PuppetBoneTest>();
        actionManager ??= FindAnyObjectByType<ActionManager>();
    }

    private void OnEnable()
    {
        hasInitialActionSequence = false;
        pollingRoutine = StartCoroutine(PollState());
    }

    private void OnDisable()
    {
        if (pollingRoutine != null)
            StopCoroutine(pollingRoutine);
        pollingRoutine = null;
        SetConnected(false);
        target?.ClearExternalInput();
    }

    private IEnumerator PollState()
    {
        WaitForSecondsRealtime connectedDelay = new WaitForSecondsRealtime(pollInterval);
        WaitForSecondsRealtime failedDelay = new WaitForSecondsRealtime(failureRetryInterval);

        while (enabled)
        {
            bool receivedValidState = false;
            using UnityWebRequest request = UnityWebRequest.Get(endpoint);
            request.timeout = 1;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                PuppetControlState state = JsonUtility.FromJson<PuppetControlState>(request.downloadHandler.text);
                if (state != null)
                {
                    LatestState = state;
                    lastSuccessTime = Time.realtimeSinceStartup;
                    SetConnected(true);
                    target?.SetControlState(state);
                    // The server intentionally keeps its most recent action so
                    // reconnecting clients can inspect state. Treat the first
                    // response as a baseline, not as a newly triggered action.
                    if (!hasInitialActionSequence)
                    {
                        lastActionSequence = state.actionSequence;
                        hasInitialActionSequence = true;
                    }
                    else if (state.actionSequence != lastActionSequence)
                    {
                        lastActionSequence = state.actionSequence;
                        if (state.actionSequence > 0 && !string.IsNullOrWhiteSpace(state.action))
                            actionManager?.QueueAction(state.action);
                    }
                    receivedValidState = true;
                }
            }

            if (Time.realtimeSinceStartup - lastSuccessTime > disconnectAfter)
            {
                SetConnected(false);
                target?.ClearExternalInput();
            }

            yield return receivedValidState ? connectedDelay : failedDelay;
        }
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value)
            return;

        IsConnected = value;
        if (logConnectionChanges)
            Debug.Log(value ? $"Puppet channel connected: {endpoint}" : "Puppet channel disconnected.", this);
    }
}
