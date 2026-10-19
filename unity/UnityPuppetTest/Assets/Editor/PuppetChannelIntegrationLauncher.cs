using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PuppetChannelIntegrationLauncher
{
    private const string RequestFileName = "RunPuppetChannelIntegration.request";
    private const string LiveRequestFileName = "RunPuppetLiveCamera.request";
    private static double nextRequestCheck;

    [InitializeOnLoadMethod]
    private static void RegisterRequestWatcher()
    {
        EditorApplication.update -= WatchForRequests;
        EditorApplication.update += WatchForRequests;
        EditorApplication.delayCall += WatchForRequests;
    }

    private static void WatchForRequests()
    {
        if (
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < nextRequestCheck
        )
        {
            return;
        }

        nextRequestCheck = EditorApplication.timeSinceStartup + 0.5d;
        if (File.Exists(RequestPath))
            RunInteractive();
        else if (File.Exists(LiveRequestPath))
            RunLiveInteractive();
    }

    public static void Run()
    {
        string scenePath = Array.Exists(
            Environment.GetCommandLineArgs(),
            argument => argument == "-puppetBackgroundScene"
        )
            ? "Assets/Scenes/PuppetBackgroundTest.unity"
            : "Assets/Scenes/PuppetRigTest.unity";
        EditorSceneManager.OpenScene(scenePath);
        EditorApplication.EnterPlaymode();
    }

    private static void RunInteractive()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EditorSceneManager.OpenScene("Assets/Scenes/PuppetRigTest.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void RunLiveInteractive()
    {
        if (!File.Exists(LiveRequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EditorSceneManager.OpenScene("Assets/Scenes/PuppetRigTest.unity");
        EditorApplication.EnterPlaymode();
    }

    private static string RequestPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, RequestFileName);

    private static string LiveRequestPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, LiveRequestFileName);
}
