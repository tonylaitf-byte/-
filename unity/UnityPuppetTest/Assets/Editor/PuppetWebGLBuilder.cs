using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PuppetWebGLBuilder
{
    private const string DefaultScene = "Assets/Scenes/PuppetActionTest.unity";

    [MenuItem("Puppet/Build Phase 3 WebGL")]
    public static void BuildFromMenu()
    {
        Build();
    }

    public static void BuildFromCommandLine()
    {
        Build();
        Debug.Log("PUPPET_WEBGL_BUILD_SUCCESS");
    }

    private static void Build()
    {
        // Keep generated animation/controller/scene assets in sync before packaging.
        PuppetProjectBootstrap.BuildFromCommandLine();

        string outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "..",
            "..",
            "hand-tracking",
            "static",
            "unity"
        ));
        Directory.CreateDirectory(outputDirectory);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
            scenes = new[] { DefaultScene };

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputDirectory,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"WebGL build failed: {report.summary.result}, " +
                $"errors={report.summary.totalErrors}"
            );
    }
}
