using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UnityBridge : MonoBehaviour
{
    [Serializable]
    private sealed class BrowserCommand
    {
        public string type;
        public int puppet;
        public string action;
    }

    [SerializeField] private ActionManager actionManager;

    public void Configure(ActionManager actions)
    {
        actionManager = actions;
    }

    public void ReceiveCommand(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        BrowserCommand command;
        try
        {
            command = JsonUtility.FromJson<BrowserCommand>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Invalid browser command: {exception.Message}", this);
            return;
        }

        if (command == null ||
            !string.Equals(command.type, "ACTION", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(command.action))
            return;

        actionManager ??= FindAnyObjectByType<ActionManager>();
        if (actionManager == null || !actionManager.QueueAction(command.action))
            Debug.LogWarning($"Could not queue browser action: {command.action}", this);
    }
}
