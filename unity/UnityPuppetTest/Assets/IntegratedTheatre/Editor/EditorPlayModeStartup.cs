using System;
using UnityEditor;
using UnityEngine;

// Let editor startup callbacks (including first-use Search indexing) finish in edit mode.
public static class EditorPlayModeStartup
{
    static double deadline;
    static bool scheduled;

    public static void EnterAfterInitialization()
    {
        if (scheduled || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("A Play Mode transition is already pending.");
        scheduled = true;
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.delayCall += DeferOneEditorCycle;
    }

    static void DeferOneEditorCycle()
    {
        EditorApplication.delayCall += EnterWhenReady;
    }

    static void EnterWhenReady()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            scheduled = false;
            Debug.LogError("Editor initialization timed out before Play Mode.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnterWhenReady;
            return;
        }
        scheduled = false;
        EditorApplication.EnterPlaymode();
    }
}
