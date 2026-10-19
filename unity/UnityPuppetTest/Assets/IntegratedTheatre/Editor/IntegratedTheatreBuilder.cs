using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;
using PalmTheatre.DuetGestures;
using PalmTheatre.DuetMapping;

public static class IntegratedTheatreBuilder
{
    public const string Root = "Assets/IntegratedTheatre";
    public static readonly string[] Scenes = {
        Root + "/Scenes/TheatreEntry.unity", Root + "/Scenes/SoloGesture.unity",
        Root + "/Scenes/SoloMapping.unity", Root + "/Scenes/DuetGesture.unity", Root + "/Scenes/DuetMapping.unity"
    };
    [MenuItem("掌中智戲/整合舞台/建立整合場景")]
    public static void Prepare()
    {
        Directory.CreateDirectory(Root + "/Scenes");
        AssetDatabase.Refresh();
        string[] sources = { "Assets/Scenes/PuppetActionTest.unity", "Assets/Scenes/PuppetActionTest.unity",
            DuetGestureBuilder.ScenePath, DuetMappingBuilder.ScenePath };
        for (int i = 0; i < sources.Length; i++)
        {
            var scene = EditorSceneManager.OpenScene(sources[i]);
            foreach (var receiver in UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>()) receiver.enabled = false;
            foreach (var view in UnityEngine.Object.FindObjectsByType<ActionDebugPanel>()) view.enabled = false;
            foreach (var view in UnityEngine.Object.FindObjectsByType<DuetGestureView>()) view.enabled = false;
            foreach (var view in UnityEngine.Object.FindObjectsByType<DuetMappingView>()) view.enabled = false;
            foreach (var voice in UnityEngine.Object.FindObjectsByType<ActionVoicePresenter>()) voice.showSubtitles = false;
            foreach (var duet in UnityEngine.Object.FindObjectsByType<DuetGestureSession>()) duet.pollServer = false;
            foreach (var mapping in UnityEngine.Object.FindObjectsByType<DuetMappingSession>()) mapping.pollServer = false;
            var visuals = new GameObject("Integrated scene visual passes");
            visuals.AddComponent<PuppetVisualEnhancer>();
            visuals.AddComponent<PuppetArchitectureMaterialPass>();
            visuals.AddComponent<PuppetSkinLightingPass>();
            visuals.AddComponent<PuppetColorPass>();
            if (Camera.main) Camera.main.rect = new Rect(0, 0, 1, 1);
            EditorSceneManager.SaveScene(scene, Scenes[i + 1]);
        }
        var entry = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bridge = new GameObject("TheatreBridge").AddComponent<TheatreBridge>();
        bridge.retainedShaders = new[] {
            Shader.Find("Puppet/Procedural Wet Courtyard Stone"),
            Shader.Find("Puppet/Moonlit Architecture PBR"),
            Shader.Find("Puppet/Opera Costume PBR"), Shader.Find("Standard")
        };
        EditorSceneManager.SaveScene(entry, Scenes[0]);
        AssetDatabase.SaveAssets();
        Debug.Log("INTEGRATED_THEATRE_PREPARE_PASS");
    }
    [MenuItem("掌中智戲/整合舞台/建置 WebGL")]
    public static void Build()
    {
        Prepare();
        string output = Environment.GetEnvironmentVariable("THEATRE_BUILD_OUTPUT");
        if (string.IsNullOrEmpty(output)) {
            string mainBackend = Path.GetFullPath("../../hand-tracking");
            output = Directory.Exists(mainBackend) ? Path.Combine(mainBackend, "static/theatre") : Path.GetFullPath("../IntegratedTheatre/hand-tracking/static/theatre");
        }
        Directory.CreateDirectory(output);
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.runInBackground = true;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = Scenes, locationPathName = output, target = BuildTarget.WebGL, options = BuildOptions.None
        });
        Directory.CreateDirectory("IntegrationReports");
        File.WriteAllText("IntegrationReports/Build.json", JsonUtility.ToJson(new BuildRecord {
            passed = report.summary.result == BuildResult.Succeeded, errors = report.summary.totalErrors,
            bytes = report.summary.totalSize, output = output, scenes = Scenes
        }, true));
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Integrated WebGL build failed.");
        Debug.Log("INTEGRATED_THEATRE_BUILD_PASS");
    }
    [Serializable] class BuildRecord { public bool passed; public int errors; public ulong bytes; public string output; public string[] scenes; }
}
