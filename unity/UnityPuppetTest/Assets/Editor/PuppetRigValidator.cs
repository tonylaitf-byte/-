using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PuppetRigValidator
{
    [Serializable]
    private sealed class RigReport
    {
        public bool success;
        public string modelAsset;
        public int skinnedMeshCount;
        public int vertexCount;
        public string[] requiredBones;
        public string[] foundBones;
        public string[] missingBones;
        public string scenePath;
        public string message;
    }

    [MenuItem("Tools/Puppet/Run Rig Validator")]
    public static void Run()
    {
        const string scenePath = "Assets/Scenes/PuppetRigTest.unity";
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" });
        if (modelGuids.Length == 0)
            Fail("No FBX model was imported from Assets/Models.");

        string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelPrefab == null)
            Fail($"Unity could not load the model at {modelPath}.");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject puppet = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        puppet.name = "MoonlitImperialPuppet";
        puppet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        string[] required =
        {
            "Hips",
            "Spine",
            "Head",
            "LeftShoulder",
            "LeftArm",
            "LeftForeArm",
            "LeftHand",
            "RightShoulder",
            "RightArm",
            "RightForeArm",
            "RightHand"
        };
        Dictionary<string, Transform> transforms = puppet
            .GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string[] found = required.Where(transforms.ContainsKey).ToArray();
        string[] missing = required.Where(name => !transforms.ContainsKey(name)).ToArray();
        SkinnedMeshRenderer[] renderers = puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int vertexCount = renderers.Sum(renderer => renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0);

        ApplyReadablePreviewMaterial(renderers);

        PuppetBoneTest test = puppet.AddComponent<PuppetBoneTest>();
        transforms.TryGetValue("Head", out test.head);
        transforms.TryGetValue("Spine", out test.spine);
        transforms.TryGetValue("LeftShoulder", out test.leftShoulder);
        transforms.TryGetValue("LeftArm", out test.leftArm);
        transforms.TryGetValue("LeftForeArm", out test.leftForeArm);
        transforms.TryGetValue("LeftHand", out test.leftHand);
        transforms.TryGetValue("RightShoulder", out test.rightShoulder);
        transforms.TryGetValue("RightArm", out test.rightArm);
        transforms.TryGetValue("RightForeArm", out test.rightForeArm);
        transforms.TryGetValue("RightHand", out test.rightHand);

        PuppetHttpReceiver receiver = puppet.AddComponent<PuppetHttpReceiver>();
        receiver.target = test;

        PuppetControlState sampleState = JsonUtility.FromJson<PuppetControlState>(
            "{\"version\":2,\"handDetected\":true,\"calibrated\":true," +
            "\"leftHandX\":0.25,\"rightHandY\":-0.5,\"headYaw\":0.75," +
            "\"bodyTurn\":-0.2,\"bodyX\":0.1}"
        );
        if (
            sampleState == null ||
            sampleState.version != 2 ||
            !sampleState.handDetected ||
            !sampleState.calibrated ||
            !Mathf.Approximately(sampleState.headYaw, 0.75f) ||
            !Mathf.Approximately(sampleState.leftHandX, 0.25f)
        )
        {
            Fail("Unity could not parse the V2 continuous puppet payload.");
        }

        AddLightingAndCamera(puppet);
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
        EditorSceneManager.SaveScene(scene, scenePath);

        bool success = missing.Length == 0 && renderers.Length > 0 && vertexCount > 0;
        RigReport report = new RigReport
        {
            success = success,
            modelAsset = modelPath,
            skinnedMeshCount = renderers.Length,
            vertexCount = vertexCount,
            requiredBones = required,
            foundBones = found,
            missingBones = missing,
            scenePath = scenePath,
            message = success
                ? "PASS: Unity found the puppet mesh and all control bones."
                : "FAIL: The model is missing a skinned mesh or one or more required bones."
        };

        string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PuppetRigTestReport.json");
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        AssetDatabase.SaveAssets();
        Debug.Log(report.message + " Report: " + reportPath);

        if (!success)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(2);
            throw new InvalidOperationException(report.message);
        }
    }

    private static void AddLightingAndCamera(GameObject puppet)
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.24f, 0.28f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.88f);
        light.intensity = 0.65f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

        GameObject cameraObject = new GameObject("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.fieldOfView = 32f;

        Bounds bounds = CalculateBounds(puppet);
        float halfHeight = Mathf.Max(bounds.extents.y, 0.5f);
        float distance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;

        // Meshy characters face -Z, so the front camera belongs on +Z.
        cameraObject.transform.position = bounds.center + new Vector3(0f, bounds.size.y * 0.03f, distance);
        cameraObject.transform.LookAt(bounds.center + Vector3.up * bounds.size.y * 0.03f);
    }

    private static void ApplyReadablePreviewMaterial(SkinnedMeshRenderer[] renderers)
    {
        const string materialPath = "Assets/Models/PuppetPreview.mat";
        Material preview = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (preview == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                return;

            preview = new Material(shader) { name = "PuppetPreview" };
            preview.color = new Color(0.38f, 0.46f, 0.55f, 1f);
            preview.SetFloat("_Metallic", 0.05f);
            preview.SetFloat("_Glossiness", 0.22f);

            string[] normalGuids = AssetDatabase.FindAssets("t:Texture2D normal", new[] { "Assets/Models" });
            if (normalGuids.Length > 0)
            {
                string normalPath = AssetDatabase.GUIDToAssetPath(normalGuids[0]);
                TextureImporter importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }

                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                preview.SetTexture("_BumpMap", normal);
                preview.EnableKeyword("_NORMALMAP");
            }

            AssetDatabase.CreateAsset(preview, materialPath);
        }

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            int slotCount = Mathf.Max(renderer.sharedMaterials.Length, 1);
            renderer.sharedMaterials = Enumerable.Repeat(preview, slotCount).ToArray();
        }
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(Vector3.up, Vector3.one * 2f);

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void Fail(string message)
    {
        string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PuppetRigTestReport.json");
        File.WriteAllText(reportPath, JsonUtility.ToJson(new RigReport { success = false, message = message }, true));
        Debug.LogError(message);
        if (Application.isBatchMode)
            EditorApplication.Exit(2);
        throw new InvalidOperationException(message);
    }
}
