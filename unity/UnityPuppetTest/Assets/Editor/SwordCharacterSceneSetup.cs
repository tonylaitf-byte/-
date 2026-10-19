using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SwordCharacterSceneSetup
{
    private const string ModelPath = "Assets/Models/SwordCharacter/sword_01.fbx";
    private const string ScenePath = "Assets/Scenes/PuppetRigTest.unity";
    private const string PendingFileName = "SwordCharacterSetup.pending";

    private static readonly string[] RequiredBones =
    {
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

    [Serializable]
    private sealed class SetupReport
    {
        public bool success;
        public string modelAsset;
        public string scenePath;
        public string puppetName;
        public int rendererCount;
        public int vertexCount;
        public string swordObject;
        public string[] missingBones;
        public string message;
    }

    [MenuItem("Tools/Puppet/Replace With Sword Character")]
    public static void Run()
    {
        try
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
            );

            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelPrefab == null)
                throw new InvalidOperationException($"Unity could not load {ModelPath}.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PuppetBoneTest oldController = UnityEngine.Object.FindAnyObjectByType<PuppetBoneTest>(
                FindObjectsInactive.Include
            );
            if (oldController == null)
                throw new InvalidOperationException("The current scene has no PuppetBoneTest component.");

            GameObject oldPuppet = oldController.gameObject;
            PuppetHttpReceiver oldReceiver = oldPuppet.GetComponent<PuppetHttpReceiver>();
            if (oldReceiver == null)
                throw new InvalidOperationException("The current puppet has no PuppetHttpReceiver component.");

            GameObject newPuppet = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, scene);
            newPuppet.name = "MoonlitImperialSwordPuppet";
            newPuppet.transform.SetPositionAndRotation(
                oldPuppet.transform.position,
                oldPuppet.transform.rotation
            );
            newPuppet.transform.localScale = oldPuppet.transform.localScale;
            newPuppet.SetActive(oldPuppet.activeSelf);

            Dictionary<string, Transform> bones = newPuppet
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            string[] missingBones = RequiredBones.Where(name => !bones.ContainsKey(name)).ToArray();
            if (missingBones.Length > 0)
            {
                UnityEngine.Object.DestroyImmediate(newPuppet);
                throw new InvalidOperationException(
                    "The sword model is missing control bones: " + string.Join(", ", missingBones)
                );
            }

            Transform sword = FindSwordObject(newPuppet);
            if (sword == null)
            {
                UnityEngine.Object.DestroyImmediate(newPuppet);
                throw new InvalidOperationException("The imported character does not contain the sword mesh.");
            }

            PuppetBoneTest newController = newPuppet.AddComponent<PuppetBoneTest>();
            EditorUtility.CopySerialized(oldController, newController);
            newController.head = bones["Head"];
            newController.spine = bones["Spine"];
            newController.leftShoulder = bones["LeftShoulder"];
            newController.leftArm = bones["LeftArm"];
            newController.leftForeArm = bones["LeftForeArm"];
            newController.leftHand = bones["LeftHand"];
            newController.rightShoulder = bones["RightShoulder"];
            newController.rightArm = bones["RightArm"];
            newController.rightForeArm = bones["RightForeArm"];
            newController.rightHand = bones["RightHand"];

            PuppetHttpReceiver newReceiver = newPuppet.AddComponent<PuppetHttpReceiver>();
            EditorUtility.CopySerialized(oldReceiver, newReceiver);
            newReceiver.target = newController;

            Renderer[] renderers = newPuppet.GetComponentsInChildren<Renderer>(true);
            ApplyPreviewMaterial(renderers);
            int vertexCount = renderers.Sum(GetVertexCount);

            UnityEngine.Object.DestroyImmediate(oldPuppet);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            WriteReport(new SetupReport
            {
                success = true,
                modelAsset = ModelPath,
                scenePath = ScenePath,
                puppetName = newPuppet.name,
                rendererCount = renderers.Length,
                vertexCount = vertexCount,
                swordObject = GetTransformPath(sword, newPuppet.transform),
                missingBones = Array.Empty<string>(),
                message = "PASS: Replaced the puppet with sword_01 and rebound all control bones."
            });
            Debug.Log("PASS: sword_01 is now the controlled puppet in PuppetRigTest.");
        }
        catch (Exception exception)
        {
            WriteReport(new SetupReport
            {
                success = false,
                modelAsset = ModelPath,
                scenePath = ScenePath,
                missingBones = Array.Empty<string>(),
                message = exception.Message
            });
            Debug.LogException(exception);
            throw;
        }
    }

    [InitializeOnLoadMethod]
    private static void RegisterPendingSetup()
    {
        EditorApplication.delayCall += RunPendingSetup;
    }

    private static void RunPendingSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(PendingFilePath))
            return;

        try
        {
            Run();
            File.Delete(PendingFilePath);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Automatic sword character setup failed. The pending marker was kept for retry. " +
                exception.Message
            );
        }
    }

    private static Transform FindSwordObject(GameObject puppet)
    {
        Transform[] transforms = puppet.GetComponentsInChildren<Transform>(true);
        Transform knownSwordMesh = transforms.FirstOrDefault(transform =>
            transform != puppet.transform &&
            string.Equals(transform.name, "Mesh1.0", StringComparison.OrdinalIgnoreCase)
        );
        if (knownSwordMesh != null)
            return knownSwordMesh;

        return transforms.FirstOrDefault(transform =>
            transform != puppet.transform &&
            transform.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0
        );
    }

    private static void ApplyPreviewMaterial(IEnumerable<Renderer> renderers)
    {
        Material preview = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/PuppetPreview.mat");
        if (preview == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            int slotCount = Mathf.Max(renderer.sharedMaterials.Length, 1);
            renderer.sharedMaterials = Enumerable.Repeat(preview, slotCount).ToArray();
        }
    }

    private static int GetVertexCount(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            return skinned.sharedMesh.vertexCount;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : 0;
    }

    private static string GetTransformPath(Transform target, Transform root)
    {
        List<string> names = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static void WriteReport(SetupReport report)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        File.WriteAllText(
            Path.Combine(projectRoot, "SwordCharacterSetupReport.json"),
            JsonUtility.ToJson(report, true)
        );
    }

    private static string PendingFilePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, PendingFileName);
}
