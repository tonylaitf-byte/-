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
        Root + "/Scenes/SoloMapping.unity", Root + "/Scenes/DuetGesture.unity", Root + "/Scenes/DuetMapping.unity",
        HybridTheatreBuilder.SoloScene, HybridTheatreBuilder.DuetScene
    };
    [MenuItem("掌中智戲/整合舞台/建立整合場景")]
    public static void Prepare()
    {
        // Original performance scenes and entry are authored inputs. Only hybrid
        // copies are regenerated, preserving their source assets and metadata.
        for (int i = 0; i < 5; i++)
            if (!File.Exists(Scenes[i]))
                throw new FileNotFoundException("Existing integrated scene is missing.", Scenes[i]);
        HybridTheatreBuilder.Prepare();
        AssetDatabase.SaveAssets();
        Debug.Log("INTEGRATED_THEATRE_PREPARE_PASS");
    }
    [MenuItem("掌中智戲/整合舞台/建置 WebGL")]
    public static void Build()
    {
        // Existing stage assets are inputs, not generated output of a normal build.
        for (int i = 0; i < 5; i++)
            if (!File.Exists(Scenes[i]))
                throw new FileNotFoundException("Existing integrated scene is missing. Run IntegratedTheatreBuilder.Prepare explicitly first.", Scenes[i]);
        HybridTheatreBuilder.Prepare();
        AssetDatabase.SaveAssets();
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
