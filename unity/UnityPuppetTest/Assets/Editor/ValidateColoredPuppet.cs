using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

public static class ValidateColoredPuppet
{
    [InitializeOnLoadMethod]
    private static void ScheduleRigBindingRepair()
    {
        EditorApplication.delayCall += BindRigIfCurrentSceneNeedsIt;
    }

    private static void BindRigIfCurrentSceneNeedsIt()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/PuppetBackgroundTest.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        PuppetBoneTest controller = Object.FindAnyObjectByType<PuppetBoneTest>();
        if (controller == null || CountBoundRigBones(controller) == 10)
            return;

        BindRigBones(controller);
        if (CountBoundRigBones(controller) != 10)
            return;

        PuppetHttpReceiver receiver = controller.GetComponent<PuppetHttpReceiver>();
        if (receiver != null)
        {
            receiver.target = controller;
            EditorUtility.SetDirty(receiver);
        }
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("PUPPET_RIG_AUTO_REPAIRED bones=10");
    }

    public static void BindScenePuppetRig()
    {
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/PuppetBackgroundTest.unity",
            OpenSceneMode.Single
        );
        PuppetBoneTest controller = Object.FindAnyObjectByType<PuppetBoneTest>();
        if (controller == null)
            throw new System.InvalidOperationException("PuppetBoneTest is missing.");

        BindRigBones(controller);

        int bound = CountBoundRigBones(controller);
        if (bound != 10)
            throw new System.InvalidOperationException($"Could only bind {bound}/10 required rig bones.");

        PuppetHttpReceiver receiver = controller.GetComponent<PuppetHttpReceiver>();
        if (receiver != null)
            receiver.target = controller;

        EditorUtility.SetDirty(controller);
        if (receiver != null)
            EditorUtility.SetDirty(receiver);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"PUPPET_RIG_BOUND bones={bound}");
    }

    public static void ValidateScenePuppetRig()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PuppetBackgroundTest.unity", OpenSceneMode.Single);
        PuppetBoneTest controller = Object.FindAnyObjectByType<PuppetBoneTest>();
        int bound = controller == null ? 0 : CountBoundRigBones(controller);
        if (bound != 10)
            throw new System.InvalidOperationException($"Puppet rig binding invalid: {bound}/10 bones.");
        Debug.Log($"PUPPET_RIG_BINDING_OK bones={bound}");
    }

    public static void ReportPuppetGroundGap()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PuppetBackgroundTest.unity", OpenSceneMode.Single);
        PuppetBoneTest controller = Object.FindAnyObjectByType<PuppetBoneTest>();
        if (controller == null)
            throw new System.InvalidOperationException("Puppet is missing from the scene.");

        float puppetBottom = float.PositiveInfinity;
        foreach (Renderer renderer in controller.GetComponentsInChildren<Renderer>(true))
            puppetBottom = Mathf.Min(puppetBottom, renderer.bounds.min.y);

        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer.transform.IsChildOf(controller.transform))
                continue;
            string rendererName = renderer.name.ToLowerInvariant();
            if (rendererName.Contains("courtyard") || rendererName.Contains("floor") || rendererName.Contains("ground"))
                Debug.Log($"GROUND_CANDIDATE name={renderer.name} minY={renderer.bounds.min.y:F4} maxY={renderer.bounds.max.y:F4}");
        }
        Debug.Log($"PUPPET_GROUND_GAP rootY={controller.transform.position.y:F4} bottomY={puppetBottom:F4}");
    }

    public static void ForceReimportColoredPuppet()
    {
        AssetDatabase.ImportAsset(
            "Assets/Models/SwordCharacter/sword_01_colored_uv.fbx",
            ImportAssetOptions.ForceUpdate
        );
        AssetDatabase.SaveAssets();
        Debug.Log("COLORED_PUPPET_FORCED_REIMPORT");
    }

    public static void CompareSourceHierarchy()
    {
        const string originalPath = "Assets/Models/SwordCharacter/sword_01.fbx";
        const string coloredPath = "Assets/Models/SwordCharacter/sword_01_colored_uv.fbx";

        GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(originalPath);
        GameObject colored = AssetDatabase.LoadAssetAtPath<GameObject>(coloredPath);
        if (original == null || colored == null)
            throw new System.InvalidOperationException("Both puppet FBX assets must be imported.");

        var originalIds = SourceIdsByPath(original.transform);
        var coloredIds = SourceIdsByPath(colored.transform);
        int shared = 0;
        foreach (var pair in originalIds)
        {
            if (coloredIds.TryGetValue(pair.Key, out long coloredId) && coloredId == pair.Value)
                shared++;
        }

        Debug.Log(
            $"COLORED_PUPPET_VALIDATION originalNodes={originalIds.Count} " +
            $"coloredNodes={coloredIds.Count} stableSourceIds={shared}"
        );
    }

    /// <summary>
    /// Replaces the old untextured model instance while retaining its authored
    /// transform and live-control settings.  Rig references are deliberately
    /// reset so PuppetBoneTest resolves the matching bones in the new FBX.
    /// </summary>
    public static void ReplaceScenePuppet()
    {
        ReplaceScenePuppetWithAsset("Assets/Models/SwordCharacter/sword_01_colored_uv.fbx");
    }

    public static void ReplaceScenePuppetWithTestBlendColors()
    {
        ReplaceScenePuppetWithAsset("Assets/Models/SwordCharacter/sword_01_testblend_colored.fbx");
    }

    private static void ReplaceScenePuppetWithAsset(string coloredPath)
    {
        const string scenePath = "Assets/Scenes/PuppetBackgroundTest.unity";

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        PuppetBoneTest oldController = Object.FindAnyObjectByType<PuppetBoneTest>();
        GameObject coloredAsset = AssetDatabase.LoadAssetAtPath<GameObject>(coloredPath);
        if (oldController == null || coloredAsset == null)
            throw new System.InvalidOperationException("Scene puppet or coloured FBX could not be found.");

        GameObject oldRoot = oldController.gameObject;
        Transform oldTransform = oldRoot.transform;
        Transform oldParent = oldTransform.parent;
        int siblingIndex = oldTransform.GetSiblingIndex();
        Vector3 position = oldTransform.position;
        Quaternion rotation = oldTransform.rotation;
        Vector3 scale = oldTransform.localScale;
        string displayName = oldRoot.name;

        GameObject newRoot = (GameObject)PrefabUtility.InstantiatePrefab(coloredAsset, scene);
        newRoot.name = displayName;
        newRoot.transform.SetParent(oldParent, true);
        newRoot.transform.position = position;
        newRoot.transform.rotation = rotation;
        newRoot.transform.localScale = scale;
        newRoot.transform.SetSiblingIndex(siblingIndex);

        PuppetBoneTest newController = newRoot.AddComponent<PuppetBoneTest>();
        EditorUtility.CopySerialized(oldController, newController);
        ClearRigReferences(newController);

        PuppetHttpReceiver oldReceiver = oldRoot.GetComponent<PuppetHttpReceiver>();
        PuppetHttpReceiver newReceiver = newRoot.AddComponent<PuppetHttpReceiver>();
        if (oldReceiver != null)
            EditorUtility.CopySerialized(oldReceiver, newReceiver);
        newReceiver.target = newController;

        int multiMaterialRenderers = 0;
        foreach (Renderer renderer in newRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterials.Length > 1)
                multiMaterialRenderers++;
        }
        if (multiMaterialRenderers == 0)
            throw new System.InvalidOperationException("The coloured FBX has no separated material slots after import.");

        Object.DestroyImmediate(oldRoot);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"COLORED_PUPPET_REPLACED multiMaterialRenderers={multiMaterialRenderers}");
    }

    public static void ValidateScenePuppet()
    {
        const string scenePath = "Assets/Scenes/PuppetBackgroundTest.unity";
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        PuppetBoneTest controller = Object.FindAnyObjectByType<PuppetBoneTest>();
        if (controller == null || controller.GetComponent<PuppetHttpReceiver>() == null)
            throw new System.InvalidOperationException("Puppet control components are missing.");
        if (!controller.lockFeetToGround)
            throw new System.InvalidOperationException("Feet must remain floor-locked.");

        int renderers = 0;
        int materialSlots = 0;
        foreach (Renderer renderer in controller.GetComponentsInChildren<Renderer>(true))
        {
            renderers++;
            materialSlots += renderer.sharedMaterials.Length;
        }
        if (renderers < 2 || materialSlots < 10)
            throw new System.InvalidOperationException("Expected the test.blend character material slots and sword slot.");

        string rootGuid = AssetDatabase.AssetPathToGUID(
            AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(controller.gameObject))
        );
        if (rootGuid != "0eb8b29e65177e546a14bd65525aa034")
            throw new System.InvalidOperationException("The scene is not using the test.blend-coloured FBX.");

        Renderer characterRenderer = null;
        foreach (Renderer renderer in controller.GetComponentsInChildren<Renderer>(true))
        {
            if (characterRenderer == null || renderer.sharedMaterials.Length > characterRenderer.sharedMaterials.Length)
                characterRenderer = renderer;
        }
        string[] materialNames = System.Array.ConvertAll(
            characterRenderer.sharedMaterials,
            material => material != null ? material.name : "<empty>"
        );
        Debug.Log(
            $"TEST_BLEND_PUPPET_SCENE_OK renderers={renderers} materialSlots={materialSlots} " +
            $"floorLock={controller.lockFeetToGround} materials={string.Join(",", materialNames)}"
        );
    }

    private static void ClearRigReferences(PuppetBoneTest controller)
    {
        controller.head = null;
        controller.spine = null;
        controller.leftShoulder = null;
        controller.leftArm = null;
        controller.leftForeArm = null;
        controller.leftHand = null;
        controller.rightShoulder = null;
        controller.rightArm = null;
        controller.rightForeArm = null;
        controller.rightHand = null;
    }

    private static Transform FindRigBone(PuppetBoneTest controller, string boneName)
    {
        foreach (Transform child in controller.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, boneName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static void BindRigBones(PuppetBoneTest controller)
    {
        controller.head = FindRigBone(controller, "Head");
        controller.spine = FindRigBone(controller, "Spine");
        controller.leftShoulder = FindRigBone(controller, "LeftShoulder");
        controller.leftArm = FindRigBone(controller, "LeftArm");
        controller.leftForeArm = FindRigBone(controller, "LeftForeArm");
        controller.leftHand = FindRigBone(controller, "LeftHand");
        controller.rightShoulder = FindRigBone(controller, "RightShoulder");
        controller.rightArm = FindRigBone(controller, "RightArm");
        controller.rightForeArm = FindRigBone(controller, "RightForeArm");
        controller.rightHand = FindRigBone(controller, "RightHand");
    }

    private static int CountBoundRigBones(PuppetBoneTest controller)
    {
        Transform[] bones =
        {
            controller.head, controller.spine,
            controller.leftShoulder, controller.leftArm, controller.leftForeArm, controller.leftHand,
            controller.rightShoulder, controller.rightArm, controller.rightForeArm, controller.rightHand,
        };
        int count = 0;
        foreach (Transform bone in bones)
        {
            if (bone != null)
                count++;
        }
        return count;
    }

    private static Dictionary<string, long> SourceIdsByPath(Transform root)
    {
        var result = new Dictionary<string, long>();
        AddRecursive(root, root.name, result);
        return result;
    }

    private static void AddRecursive(Transform node, string path, Dictionary<string, long> result)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(node.gameObject, out _, out long fileId);
        result[path] = fileId;
        for (int index = 0; index < node.childCount; index++)
        {
            Transform child = node.GetChild(index);
            AddRecursive(child, path + "/" + child.name, result);
        }
    }
}
