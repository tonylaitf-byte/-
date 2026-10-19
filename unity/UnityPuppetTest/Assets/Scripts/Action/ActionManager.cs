using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ActionManager : MonoBehaviour
{
    [SerializeField] private PuppetManager puppetManager;
    [SerializeField] private ActionVoicePresenter voicePresenter;
    [SerializeField] private List<PuppetActionDefinition> actions = new List<PuppetActionDefinition>();
    [SerializeField, Range(1, 8)] private int maximumQueuedActions = 4;

    private readonly Dictionary<string, float> nextAllowedTime = new Dictionary<string, float>();
    private readonly Queue<string> queuedActions = new Queue<string>();
    private Coroutine queueRoutine;

    public string LastAction { get; private set; } = "IDLE";
    public int PendingActionCount => queuedActions.Count;

    private void Awake()
    {
        puppetManager ??= GetComponent<PuppetManager>();
        EnsureVoicePresenter();
        EnsureDefaultActions();
    }

    public void Configure(PuppetManager manager)
    {
        puppetManager = manager;
        EnsureVoicePresenter();
        EnsureDefaultActions();
    }

    public bool ExecuteAction(string actionId)
    {
        string normalized = (actionId ?? string.Empty).Trim().ToUpperInvariant();
        PuppetActionDefinition definition = actions.Find(
            item => string.Equals(item.actionId, normalized, StringComparison.OrdinalIgnoreCase)
        );

        if (definition == null || puppetManager == null)
            return false;

        if (nextAllowedTime.TryGetValue(normalized, out float allowedAt) &&
            Time.unscaledTime < allowedAt)
            return false;

        if (!puppetManager.PlayAction(
            definition.actionId,
            definition.animatorTrigger,
            definition.duration
        ))
            return false;

        LastAction = definition.actionId;
        voicePresenter?.PlayActionVoice(definition.actionId);
        nextAllowedTime[normalized] = Time.unscaledTime + Mathf.Max(0f, definition.cooldown);
        return true;
    }

    public bool QueueAction(string actionId)
    {
        string normalized = (actionId ?? string.Empty).Trim().ToUpperInvariant();
        if (FindDefinition(normalized) == null || queuedActions.Count >= maximumQueuedActions)
            return false;

        queuedActions.Enqueue(normalized);
        queueRoutine ??= StartCoroutine(ProcessQueuedActions());
        return true;
    }

    private IEnumerator ProcessQueuedActions()
    {
        while (queuedActions.Count > 0)
        {
            while (IsActionOrVoicePlaying())
                yield return null;

            string nextAction = queuedActions.Peek();
            if (nextAllowedTime.TryGetValue(nextAction, out float allowedAt) &&
                Time.unscaledTime < allowedAt)
            {
                yield return null;
                continue;
            }

            queuedActions.Dequeue();
            if (!ExecuteAction(nextAction))
                continue;

            while (IsActionOrVoicePlaying())
                yield return null;
        }

        queueRoutine = null;
    }

    private bool IsActionOrVoicePlaying()
    {
        return (puppetManager != null && puppetManager.IsPerforming) ||
            (voicePresenter != null && voicePresenter.IsPlaying);
    }

    private PuppetActionDefinition FindDefinition(string actionId)
    {
        return actions.Find(
            item => string.Equals(item.actionId, actionId, StringComparison.OrdinalIgnoreCase)
        );
    }

    public void TestDrink() => ExecuteAction("DRINK");
    public void TestAttack() => ExecuteAction("ATTACK");
    public void TestBow() => ExecuteAction("BOW");
    public void TestLandscape() => ExecuteAction("LANDSCAPE");
    public void TestAlert() => ExecuteAction("ALERT");

    private void EnsureDefaultActions()
    {
        EnsureAction("DRINK", "Drink", 3.3f, 3.6f);
        EnsureAction("ATTACK", "Attack", 3.0f, 3.5f);
        EnsureAction("BOW", "Bow", 2.2f, 2.5f);
        EnsureAction("LANDSCAPE", "Landscape", 5.6f, 6.0f);
        EnsureAction("ALERT", "Alert", 4.2f, 4.6f);
    }

    private void EnsureAction(
        string actionId,
        string animatorTrigger,
        float duration,
        float cooldown
    )
    {
        if (FindDefinition(actionId) == null)
            actions.Add(new PuppetActionDefinition(actionId, animatorTrigger, duration, cooldown));
    }

    private void EnsureVoicePresenter()
    {
        voicePresenter ??= GetComponent<ActionVoicePresenter>();
        if (voicePresenter == null)
            voicePresenter = gameObject.AddComponent<ActionVoicePresenter>();
    }
}
