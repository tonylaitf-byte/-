using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PuppetProjectBootstrap
{
    private const string SourceScenePath = "Assets/Scenes/PuppetBackgroundTest.unity";
    private const string StageScenePath = "Assets/Scenes/Stage01.unity";
    private const string ActionScenePath = "Assets/Scenes/PuppetActionTest.unity";
    private const string PuppetPrefabPath = "Assets/Puppets/Puppet01.prefab";
    private const string ControllerPath = "Assets/Animations/Puppet01.controller";
    private const int BootstrapRevision = 30;
    private const string BootstrapRevisionKey = "UnityPuppetTest.BootstrapRevision";

    [InitializeOnLoadMethod]
    private static void BuildAutomaticallyWhenReady()
    {
        if (Application.isBatchMode ||
            (EditorPrefs.GetInt(BootstrapRevisionKey, 0) >= BootstrapRevision &&
             AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null))
            return;

        EditorApplication.delayCall += TryAutomaticBuild;
    }

    private static void TryAutomaticBuild()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutomaticBuild;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += TryAutomaticBuild;
            return;
        }

        try
        {
            Build();
            Debug.Log("PUPPET_AUTOMATIC_BOOTSTRAP_SUCCESS");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Puppet/Build Phase 1 And 2 Baseline")]
    public static void BuildFromMenu()
    {
        Build();
    }

    public static void BuildFromCommandLine()
    {
        Build();
        Debug.Log("PUPPET_BOOTSTRAP_SUCCESS");
    }

    private static void Build()
    {
        EnsureFolder("Assets/Animations");
        EnsureFolder("Assets/Puppets");

        Scene sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        PuppetBoneTest mapping = UnityEngine.Object.FindAnyObjectByType<PuppetBoneTest>();
        if (mapping == null)
            throw new InvalidOperationException("PuppetBackgroundTest has no PuppetBoneTest character.");

        GameObject puppet = mapping.gameObject;
        GameObject outermostRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(puppet);
        if (outermostRoot != null)
        {
            puppet = outermostRoot;
            mapping = puppet.GetComponent<PuppetBoneTest>() ?? mapping;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(puppet))
            PrefabUtility.UnpackPrefabInstance(
                puppet,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction
            );

        Animator animator = puppet.GetComponent<Animator>();
        if (animator == null)
            animator = puppet.AddComponent<Animator>();

        Dictionary<string, Transform> bones = FindRequiredBones(puppet.transform);
        AnimationClip idle = CreateIdleClip(puppet.transform, bones);
        AnimationClip drink = CreateDrinkClip(puppet.transform, bones);
        AnimationClip attack = CreateAttackClip(puppet.transform, bones);
        AnimationClip bow = CreateBowClip(puppet.transform, bones);
        AnimationClip landscape = CreateLandscapeClip(puppet.transform, bones);
        AnimationClip alert = CreateAlertClip(puppet.transform, bones);
        ValidateForwardExtension(
            puppet.transform, bones, bones["RightHand"], landscape, 3.0f,
            shoulderFraction: 0.34f
        );
        ValidateElbowBend(
            puppet.transform, bones["RightArm"], bones["RightForeArm"],
            bones["RightHand"], landscape, 0.80f, maximumDegrees: 8f
        );
        ValidateElbowBend(
            puppet.transform, bones["RightArm"], bones["RightForeArm"],
            bones["RightHand"], landscape, 3.0f, maximumDegrees: 6f
        );
        ValidateProjectedElbowBend(
            puppet.transform, bones, bones["RightArm"], bones["RightForeArm"],
            bones["RightHand"], landscape, 3.0f, maximumDegrees: 5f
        );
        ValidateForwardExtension(
            puppet.transform, bones, bones["LeftHand"], alert, 1.15f,
            shoulderFraction: 0.28f
        );
        ValidateChestClearance(
            puppet.transform, bones, bones["LeftHand"], alert, 0.26f,
            shoulderFraction: 0.28f
        );
        ValidateElbowBend(
            puppet.transform, bones["LeftArm"], bones["LeftForeArm"],
            bones["LeftHand"], alert, 1.15f, maximumDegrees: 24f
        );
        ValidateElbowBend(
            puppet.transform, bones["LeftArm"], bones["LeftForeArm"],
            bones["LeftHand"], alert, 1.55f, maximumDegrees: 24f
        );
        AssetDatabase.DeleteAsset("Assets/Animations/Wave.anim");
        AssetDatabase.DeleteAsset("Assets/Animations/Salute.anim");
        AnimatorController controller = CreateController(
            idle, drink, attack, bow, landscape, alert
        );
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        PuppetController puppetController = puppet.GetComponent<PuppetController>();
        if (puppetController == null)
            puppetController = puppet.AddComponent<PuppetController>();
        puppetController.Configure(animator, mapping);

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            puppet,
            PuppetPrefabPath,
            InteractionMode.AutomatedAction
        );
        if (prefab == null)
            throw new InvalidOperationException("Could not save Puppet01 prefab.");

        AddGameSystems(puppetController);
        EditorSceneManager.MarkSceneDirty(sourceScene);
        EditorSceneManager.SaveScene(sourceScene, ActionScenePath, true);

        Scene stageScene = EditorSceneManager.OpenScene(ActionScenePath, OpenSceneMode.Single);
        PuppetController stagePuppet = UnityEngine.Object.FindAnyObjectByType<PuppetController>();
        if (stagePuppet != null)
            UnityEngine.Object.DestroyImmediate(stagePuppet.gameObject);

        GameManager stageSystems = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (stageSystems != null)
            UnityEngine.Object.DestroyImmediate(stageSystems.gameObject);

        EditorSceneManager.MarkSceneDirty(stageScene);
        EditorSceneManager.SaveScene(stageScene, StageScenePath, true);

        EditorSceneManager.OpenScene(ActionScenePath, OpenSceneMode.Single);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ActionScenePath, true),
            new EditorBuildSettingsScene(StageScenePath, true)
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateOutput();
        EditorPrefs.SetInt(BootstrapRevisionKey, BootstrapRevision);
    }

    private static void AddGameSystems(PuppetController puppet)
    {
        GameManager existing = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        GameObject systems = new GameObject("GameSystems");
        PuppetManager puppetManager = systems.AddComponent<PuppetManager>();
        ActionManager actionManager = systems.AddComponent<ActionManager>();
        GameManager gameManager = systems.AddComponent<GameManager>();
        UnityBridge unityBridge = systems.AddComponent<UnityBridge>();
        ActionDebugPanel debugPanel = systems.AddComponent<ActionDebugPanel>();
        systems.AddComponent<BackgroundMusicPlayer>();

        puppetManager.Configure(puppet);
        actionManager.Configure(puppetManager);
        gameManager.Configure(puppetManager, actionManager);
        unityBridge.Configure(actionManager);
        debugPanel.Configure(actionManager);
    }

    private static AnimatorController CreateController(
        AnimationClip idle,
        AnimationClip drink,
        AnimationClip attack,
        AnimationClip bow,
        AnimationClip landscape,
        AnimationClip alert
    )
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Drink", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Bow", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Landscape", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Alert", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = machine.AddState("Idle");
        idleState.motion = idle;
        machine.defaultState = idleState;

        AddActionState(machine, idleState, "Drink", drink, 0.08f);
        AddActionState(machine, idleState, "Attack", attack, 0.04f);
        AddActionState(machine, idleState, "Bow", bow, 0.08f);
        AddActionState(machine, idleState, "Landscape", landscape, 0.10f);
        AddActionState(machine, idleState, "Alert", alert, 0.05f);
        return controller;
    }

    private static void AddActionState(
        AnimatorStateMachine machine,
        AnimatorState idleState,
        string name,
        AnimationClip clip,
        float transitionDuration
    )
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.duration = transitionDuration;
        enter.AddCondition(AnimatorConditionMode.If, 0f, name);

        // PuppetController owns the exit timing. A second state-machine exit
        // would begin blending early and fight the live-control handoff.
    }

    private static AnimationClip CreateIdleClip(Transform root, Dictionary<string, Transform> bones)
    {
        AnimationClip clip = NewClip("Idle", true);
        SetRotationCurve(clip, root, bones["Spine"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(1f, new Vector3(0f, 0f, 1.5f)),
            Pose(2f, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["Head"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(1f, new Vector3(1.5f, -1f, 0f)),
            Pose(2f, Vector3.zero)
        });
        return SaveClip(clip, "Assets/Animations/Idle.anim");
    }

    private static AnimationClip CreateDrinkClip(Transform root, Dictionary<string, Transform> bones)
    {
        const float duration = 3.3f;
        AnimationClip clip = NewClip("Drink", false);
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        Vector3 side = (
            bones["RightShoulder"].position - bones["LeftShoulder"].position
        ).normalized;
        Vector3 towardViewer = ViewerDirection(root, bones["Head"]);
        Vector3 mouthTarget = bones["Head"].position +
            Vector3.up * shoulderSpan * 0.02f +
            towardViewer * shoulderSpan * 1.48f +
            side * shoulderSpan * 0.62f;
        Vector3 approachTarget = mouthTarget +
            side * shoulderSpan * 0.20f +
            Vector3.down * shoulderSpan * 0.34f;
        Vector3 elbowPole = bones["RightShoulder"].position +
            side * shoulderSpan * 1.25f +
            Vector3.down * shoulderSpan * 0.65f +
            towardViewer * shoulderSpan * 0.52f;
        ArmPose approachPose = SolveArmPose(
            bones["RightArm"],
            bones["RightForeArm"],
            bones["RightHand"],
            approachTarget,
            elbowPole
        );
        ArmPose sipPose = SolveArmPose(
            bones["RightArm"],
            bones["RightForeArm"],
            bones["RightHand"],
            mouthTarget,
            elbowPole
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["RightArm"],
            DrinkRotationPoses(
                sipPose.UpperRest,
                approachPose.UpperTarget,
                sipPose.UpperTarget
            )
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["RightForeArm"],
            DrinkRotationPoses(
                sipPose.LowerRest,
                approachPose.LowerTarget,
                sipPose.LowerTarget
            )
        );
        SetRotationCurve(clip, root, bones["Head"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.48f, new Vector3(-4f, 1.5f, 0f)),
            Pose(0.82f, new Vector3(-8f, 2.5f, 0f)),
            Pose(1.28f, new Vector3(-11f, 2f, 0.5f)),
            Pose(1.82f, new Vector3(-8.5f, 2.8f, -0.4f)),
            Pose(2.28f, new Vector3(-12f, 2f, 0.6f)),
            Pose(2.62f, new Vector3(-9f, 1.5f, 0f)),
            Pose(2.95f, new Vector3(-5f, 0.8f, 0f)),
            Pose(duration, Vector3.zero)
        });
        Vector3 desiredFacing = (towardViewer + side * 0.48f).normalized;
        float turnTowardRightHand = Vector3.SignedAngle(
            towardViewer,
            desiredFacing,
            Vector3.up
        );
        Quaternion hipsRest = bones["Hips"].localRotation;
        Quaternion hipsTurned = WorldYawLocalRotation(
            bones["Hips"],
            turnTowardRightHand
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["Hips"],
            HeldRotationPoses(hipsRest, hipsTurned, duration)
        );
        return SaveClip(clip, "Assets/Animations/Drink.anim");
    }

    private static AnimationClip CreateAttackClip(Transform root, Dictionary<string, Transform> bones)
    {
        const float duration = 3.0f;
        AnimationClip clip = NewClip("Attack", false);
        SetRotationCurve(clip, root, bones["Spine"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.38f, new Vector3(0f, -28f, 0f)),
            Pose(0.82f, new Vector3(0f, 38f, -5f)),
            Pose(1.18f, new Vector3(0f, 24f, -4f)),
            Pose(1.55f, new Vector3(0f, -24f, 0f)),
            Pose(1.95f, new Vector3(0f, 34f, -5f)),
            Pose(2.35f, new Vector3(0f, 16f, -3f)),
            Pose(2.68f, new Vector3(0f, 6f, -1f)),
            Pose(duration, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["RightArm"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.38f, new Vector3(-25f, -55f, -55f)),
            Pose(0.82f, new Vector3(18f, 60f, 28f)),
            Pose(1.18f, new Vector3(-18f, 46f, 22f)),
            Pose(1.55f, new Vector3(12f, -44f, -38f)),
            Pose(1.95f, new Vector3(-30f, 52f, 24f)),
            Pose(2.35f, new Vector3(24f, 30f, 18f)),
            Pose(2.68f, new Vector3(-10f, 12f, -8f)),
            Pose(duration, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["RightForeArm"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.38f, new Vector3(0f, 0f, -8f)),
            Pose(0.82f, new Vector3(-10f, 0f, 10f)),
            Pose(1.18f, new Vector3(-20f, 0f, 12f)),
            Pose(1.55f, new Vector3(8f, 0f, -8f)),
            Pose(1.95f, new Vector3(-20f, 0f, 10f)),
            Pose(2.35f, new Vector3(12f, 0f, 5f)),
            Pose(2.68f, new Vector3(-5f, 0f, 0f)),
            Pose(duration, Vector3.zero)
        });
        return SaveClip(clip, "Assets/Animations/Attack.anim");
    }

    private static AnimationClip CreateBowClip(Transform root, Dictionary<string, Transform> bones)
    {
        const float duration = 2.2f;
        AnimationClip clip = NewClip("Bow", false);
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        Vector3 side = (
            bones["RightShoulder"].position - bones["LeftShoulder"].position
        ).normalized;
        Vector3 towardViewer = ViewerDirection(root, bones["Head"]);
        Vector3 handCenter = bones["Hips"].position +
            Vector3.up * shoulderSpan * 0.10f +
            towardViewer * shoulderSpan * 0.24f;
        ArmPose leftPose = SolveArmPose(
            bones["LeftArm"],
            bones["LeftForeArm"],
            bones["LeftHand"],
            handCenter - side * shoulderSpan * 0.30f,
            bones["LeftShoulder"].position - side * shoulderSpan * 0.72f +
                Vector3.down * shoulderSpan * 0.72f +
                towardViewer * shoulderSpan * 0.20f
        );
        ArmPose rightPose = SolveArmPose(
            bones["RightArm"],
            bones["RightForeArm"],
            bones["RightHand"],
            handCenter + side * shoulderSpan * 0.30f,
            bones["RightShoulder"].position + side * shoulderSpan * 0.72f +
                Vector3.down * shoulderSpan * 0.72f +
                towardViewer * shoulderSpan * 0.20f
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["LeftArm"],
            HeldRotationPoses(
                leftPose.UpperRest,
                Quaternion.Slerp(leftPose.UpperRest, leftPose.UpperTarget, 0.5f),
                duration
            )
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["LeftForeArm"],
            HeldRotationPoses(
                leftPose.LowerRest,
                Quaternion.Slerp(leftPose.LowerRest, leftPose.LowerTarget, 0.5f),
                duration
            )
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["RightArm"],
            HeldRotationPoses(
                rightPose.UpperRest,
                Quaternion.Slerp(rightPose.UpperRest, rightPose.UpperTarget, 0.5f),
                duration
            )
        );
        SetAbsoluteRotationCurve(
            clip, root, bones["RightForeArm"],
            HeldRotationPoses(
                rightPose.LowerRest,
                Quaternion.Slerp(rightPose.LowerRest, rightPose.LowerTarget, 0.5f),
                duration
            )
        );
        SetRotationCurve(clip, root, bones["Spine"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.55f, new Vector3(24f, 0f, 0f)),
            Pose(1.55f, new Vector3(24f, 0f, 0f)),
            Pose(duration, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["Head"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.55f, new Vector3(16f, 0f, 0f)),
            Pose(1.55f, new Vector3(16f, 0f, 0f)),
            Pose(duration, Vector3.zero)
        });
        return SaveClip(clip, "Assets/Animations/Bow.anim");
    }

    private static AnimationClip CreateLandscapeClip(
        Transform root,
        Dictionary<string, Transform> bones
    )
    {
        const float duration = 5.6f;
        AnimationClip clip = NewClip("Landscape", false);
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        Vector3 side = (
            bones["RightShoulder"].position - bones["LeftShoulder"].position
        ).normalized;
        Vector3 towardViewer = ViewerDirection(root, bones["Head"]);
        float rightReach = Vector3.Distance(
            bones["RightArm"].position, bones["RightForeArm"].position
        ) + Vector3.Distance(
            bones["RightForeArm"].position, bones["RightHand"].position
        );
        Vector3 moonDirection = (
            towardViewer * 0.58f + Vector3.up * 0.50f + side * 0.64f
        ).normalized;
        Vector3 forwardGuideDirection = (
            towardViewer * 0.98f + Vector3.up * 0.05f + side * 0.12f
        ).normalized;
        Vector3 forwardGuideTarget = bones["RightArm"].position +
            forwardGuideDirection * rightReach * 0.999f;
        Vector3 moonTarget = bones["RightArm"].position + moonDirection * rightReach * 0.9995f;
        Vector3 moonDriftDirection = (
            moonDirection + Vector3.up * 0.045f + side * 0.018f
        ).normalized;
        Vector3 moonDriftTarget = bones["RightArm"].position +
            moonDriftDirection * rightReach * 0.9995f;
        // Bend mainly along camera depth so the remaining elbow angle is
        // hidden in the frontal projection instead of forming a visible kink.
        Vector3 hiddenBendDirection = Vector3.ProjectOnPlane(
            towardViewer,
            moonDirection
        );
        if (hiddenBendDirection.sqrMagnitude < 0.0001f)
            hiddenBendDirection = Vector3.ProjectOnPlane(Vector3.down, moonDirection);
        hiddenBendDirection.Normalize();
        Vector3 elbowPole = bones["RightShoulder"].position +
            hiddenBendDirection * shoulderSpan * 1.35f;
        ArmPose forwardGuidePose = SolveArmPose(
            bones["RightArm"], bones["RightForeArm"], bones["RightHand"],
            forwardGuideTarget, elbowPole
        );
        ArmPose moonPose = SolveArmPose(
            bones["RightArm"], bones["RightForeArm"], bones["RightHand"],
            moonTarget, elbowPole
        );
        ArmPose moonDriftPose = SolveArmPose(
            bones["RightArm"], bones["RightForeArm"], bones["RightHand"],
            moonDriftTarget, elbowPole
        );
        SetAbsoluteRotationCurve(clip, root, bones["RightArm"], new[]
        {
            new TimedRotation(0f, moonPose.UpperRest),
            new TimedRotation(0.80f, forwardGuidePose.UpperTarget),
            new TimedRotation(2.05f, moonPose.UpperTarget),
            new TimedRotation(2.85f, moonDriftPose.UpperTarget),
            new TimedRotation(3.65f, moonPose.UpperTarget),
            new TimedRotation(4.45f, moonDriftPose.UpperTarget),
            new TimedRotation(5.05f, moonPose.UpperTarget),
            new TimedRotation(duration, moonPose.UpperRest)
        });
        SetAbsoluteRotationCurve(clip, root, bones["RightForeArm"], new[]
        {
            new TimedRotation(0f, moonPose.LowerRest),
            new TimedRotation(0.80f, forwardGuidePose.LowerTarget),
            new TimedRotation(2.05f, moonPose.LowerTarget),
            new TimedRotation(2.85f, moonDriftPose.LowerTarget),
            new TimedRotation(3.65f, moonPose.LowerTarget),
            new TimedRotation(4.45f, moonDriftPose.LowerTarget),
            new TimedRotation(5.05f, moonPose.LowerTarget),
            new TimedRotation(duration, moonPose.LowerRest)
        });
        SetRotationCurve(clip, root, bones["Spine"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(1.55f, new Vector3(-4f, 8f, -2f)),
            Pose(2.75f, new Vector3(-4.8f, 8.8f, -2.5f)),
            Pose(3.85f, new Vector3(-3.6f, 7.5f, -1.6f)),
            Pose(5.05f, new Vector3(-4f, 8f, -2f)),
            Pose(duration, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["Head"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(1.45f, new Vector3(-18f, 9f, 0f)),
            Pose(2.70f, new Vector3(-20f, 7.5f, 1f)),
            Pose(3.85f, new Vector3(-17f, 10.5f, -1f)),
            Pose(5.08f, new Vector3(-18f, 9f, 0f)),
            Pose(duration, Vector3.zero)
        });
        return SaveClip(clip, "Assets/Animations/Landscape.anim");
    }

    private static AnimationClip CreateAlertClip(
        Transform root,
        Dictionary<string, Transform> bones
    )
    {
        const float duration = 4.2f;
        AnimationClip clip = NewClip("Alert", false);
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        Vector3 side = (
            bones["RightShoulder"].position - bones["LeftShoulder"].position
        ).normalized;
        Vector3 towardViewer = ViewerDirection(root, bones["Head"]);
        Vector3 swordSide = -side;
        float swordReach = Vector3.Distance(
            bones["LeftArm"].position, bones["LeftForeArm"].position
        ) + Vector3.Distance(
            bones["LeftForeArm"].position, bones["LeftHand"].position
        );
        Vector3 guardDirection = (
            towardViewer * 0.94f + Vector3.up * 0.08f + swordSide * 0.22f
        ).normalized;
        Vector3 guardTarget = bones["LeftArm"].position +
            guardDirection * swordReach * 0.985f;
        Vector3 guardDriftDirection = (
            guardDirection + Vector3.up * 0.045f + swordSide * 0.018f
        ).normalized;
        Vector3 guardDriftTarget = bones["LeftArm"].position +
            guardDriftDirection * swordReach * 0.985f;
        Vector3 elbowPole = bones["LeftShoulder"].position +
            swordSide * shoulderSpan * 1.05f +
            Vector3.down * shoulderSpan * 0.42f +
            towardViewer * shoulderSpan * 0.42f;
        ArmPose guardPose = SolveArmPose(
            bones["LeftArm"], bones["LeftForeArm"], bones["LeftHand"],
            guardTarget, elbowPole
        );
        ArmPose guardDriftPose = SolveArmPose(
            bones["LeftArm"], bones["LeftForeArm"], bones["LeftHand"],
            guardDriftTarget, elbowPole
        );
        SetAbsoluteRotationCurve(clip, root, bones["LeftArm"], new[]
        {
            new TimedRotation(0f, guardPose.UpperRest),
            new TimedRotation(0.30f, Quaternion.Slerp(
                guardPose.UpperRest, guardPose.UpperTarget, 0.55f
            )),
            new TimedRotation(0.82f, guardPose.UpperTarget),
            new TimedRotation(1.55f, guardDriftPose.UpperTarget),
            new TimedRotation(2.25f, guardPose.UpperTarget),
            new TimedRotation(2.95f, guardDriftPose.UpperTarget),
            new TimedRotation(3.68f, guardPose.UpperTarget),
            new TimedRotation(duration, guardPose.UpperRest)
        });
        SetAbsoluteRotationCurve(clip, root, bones["LeftForeArm"], new[]
        {
            new TimedRotation(0f, guardPose.LowerRest),
            new TimedRotation(0.30f, Quaternion.Slerp(
                guardPose.LowerRest, guardPose.LowerTarget, 0.55f
            )),
            new TimedRotation(0.82f, guardPose.LowerTarget),
            new TimedRotation(1.55f, guardDriftPose.LowerTarget),
            new TimedRotation(2.25f, guardPose.LowerTarget),
            new TimedRotation(2.95f, guardDriftPose.LowerTarget),
            new TimedRotation(3.68f, guardPose.LowerTarget),
            new TimedRotation(duration, guardPose.LowerRest)
        });
        SetRotationCurve(clip, root, bones["Spine"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.22f, new Vector3(0f, -13f, -3f)),
            Pose(1.45f, new Vector3(-0.8f, -12.2f, -3.5f)),
            Pose(2.45f, new Vector3(0.6f, -13.8f, -2.5f)),
            Pose(3.68f, new Vector3(0f, -13f, -3f)),
            Pose(duration, Vector3.zero)
        });
        SetRotationCurve(clip, root, bones["Head"], new[]
        {
            Pose(0f, Vector3.zero),
            Pose(0.16f, new Vector3(-2f, -18f, 2f)),
            Pose(0.38f, new Vector3(1f, -11f, 0f)),
            Pose(1.45f, new Vector3(0f, -15f, 1f)),
            Pose(2.45f, new Vector3(1.5f, -9f, -0.5f)),
            Pose(3.10f, new Vector3(0f, -13f, 0.5f)),
            Pose(3.68f, new Vector3(1f, -11f, 0f)),
            Pose(duration, Vector3.zero)
        });
        return SaveClip(clip, "Assets/Animations/Alert.anim");
    }

    private static AnimationClip NewClip(string name, bool loop)
    {
        AnimationClip clip = new AnimationClip { name = name, frameRate = 30f };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    private static TimedPose[] ActionPoses(Vector3 target, float duration)
    {
        return new[]
        {
            Pose(0f, Vector3.zero),
            Pose(duration * 0.25f, target),
            Pose(duration * 0.68f, target),
            Pose(duration, Vector3.zero)
        };
    }

    private static TimedPose Pose(float time, Vector3 eulerOffset)
    {
        return new TimedPose(time, eulerOffset);
    }

    private static TimedRotation[] HeldRotationPoses(
        Quaternion rest,
        Quaternion target,
        float duration
    )
    {
        return new[]
        {
            new TimedRotation(0f, rest),
            new TimedRotation(duration * 0.25f, target),
            new TimedRotation(duration * 0.72f, target),
            new TimedRotation(duration, rest)
        };
    }

    private static TimedRotation[] DrinkRotationPoses(
        Quaternion rest,
        Quaternion approach,
        Quaternion sip
    )
    {
        return new[]
        {
            new TimedRotation(0f, rest),
            new TimedRotation(0.32f, approach),
            new TimedRotation(0.72f, sip),
            new TimedRotation(2.58f, sip),
            new TimedRotation(2.95f, approach),
            new TimedRotation(3.3f, rest)
        };
    }

    private static Vector3 ViewerDirection(Transform root, Transform head)
    {
        Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        Vector3 direction = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.position - head.position, Vector3.up)
            : Vector3.ProjectOnPlane(-root.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            direction = -root.forward;
        return direction.normalized;
    }

    private static void ValidateForwardExtension(
        Transform root,
        Dictionary<string, Transform> bones,
        Transform hand,
        AnimationClip clip,
        float sampleTime,
        float shoulderFraction
    )
    {
        Vector3 restPosition = hand.position;
        Vector3 forward = ViewerDirection(root, bones["Head"]);
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        float forwardDistance = 0f;
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.SampleAnimationClip(root.gameObject, clip, sampleTime);
            forwardDistance = Vector3.Dot(hand.position - restPosition, forward);
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        float minimum = shoulderSpan * shoulderFraction;
        if (forwardDistance < minimum)
        {
            throw new InvalidOperationException(
                $"{clip.name} does not extend {hand.name} far enough forward: " +
                $"{forwardDistance:F3} < {minimum:F3}"
            );
        }
        Debug.Log(
            $"{clip.name} forward reach validated for {hand.name}: " +
            $"{forwardDistance:F3} (minimum {minimum:F3})"
        );
    }

    private static void ValidateChestClearance(
        Transform root,
        Dictionary<string, Transform> bones,
        Transform hand,
        AnimationClip clip,
        float sampleTime,
        float shoulderFraction
    )
    {
        Vector3 forward = ViewerDirection(root, bones["Head"]);
        Vector3 chestCenter = Vector3.Lerp(
            bones["Hips"].position,
            bones["Head"].position,
            0.59f
        );
        float shoulderSpan = Vector3.Distance(
            bones["LeftShoulder"].position,
            bones["RightShoulder"].position
        );
        float clearance = 0f;
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.SampleAnimationClip(root.gameObject, clip, sampleTime);
            clearance = Vector3.Dot(hand.position - chestCenter, forward);
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        float minimum = shoulderSpan * shoulderFraction;
        if (clearance < minimum)
        {
            throw new InvalidOperationException(
                $"{clip.name} chest clearance is too small for {hand.name}: " +
                $"{clearance:F3} < {minimum:F3}"
            );
        }
        Debug.Log(
            $"{clip.name} chest clearance validated for {hand.name}: " +
            $"{clearance:F3} (minimum {minimum:F3})"
        );
    }

    private static void ValidateElbowBend(
        Transform root,
        Transform upperArm,
        Transform forearm,
        Transform hand,
        AnimationClip clip,
        float sampleTime,
        float maximumDegrees
    )
    {
        float bendDegrees = 180f;
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.SampleAnimationClip(root.gameObject, clip, sampleTime);
            Vector3 upperDirection = forearm.position - upperArm.position;
            Vector3 lowerDirection = hand.position - forearm.position;
            bendDegrees = Vector3.Angle(upperDirection, lowerDirection);
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        if (bendDegrees > maximumDegrees)
        {
            throw new InvalidOperationException(
                $"{clip.name} bends {forearm.name} too far at {sampleTime:F2}s: " +
                $"{bendDegrees:F1} degrees > {maximumDegrees:F1} degrees"
            );
        }
        Debug.Log(
            $"{clip.name} elbow bend validated at {sampleTime:F2}s: " +
            $"{bendDegrees:F1} degrees (maximum {maximumDegrees:F1})"
        );
    }

    private static void ValidateProjectedElbowBend(
        Transform root,
        Dictionary<string, Transform> bones,
        Transform upperArm,
        Transform forearm,
        Transform hand,
        AnimationClip clip,
        float sampleTime,
        float maximumDegrees
    )
    {
        float projectedDegrees = 180f;
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.SampleAnimationClip(root.gameObject, clip, sampleTime);
            Vector3 cameraDepth = ViewerDirection(root, bones["Head"]);
            Vector3 upperScreenDirection = Vector3.ProjectOnPlane(
                forearm.position - upperArm.position,
                cameraDepth
            );
            Vector3 lowerScreenDirection = Vector3.ProjectOnPlane(
                hand.position - forearm.position,
                cameraDepth
            );
            projectedDegrees = Vector3.Angle(upperScreenDirection, lowerScreenDirection);
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        if (projectedDegrees > maximumDegrees)
        {
            throw new InvalidOperationException(
                $"{clip.name} projected elbow bend is visible at {sampleTime:F2}s: " +
                $"{projectedDegrees:F1} degrees > {maximumDegrees:F1} degrees"
            );
        }
        Debug.Log(
            $"{clip.name} projected elbow bend validated at {sampleTime:F2}s: " +
            $"{projectedDegrees:F1} degrees (maximum {maximumDegrees:F1})"
        );
    }

    private static Quaternion WorldYawLocalRotation(Transform bone, float angle)
    {
        Quaternion targetWorld = Quaternion.AngleAxis(angle, Vector3.up) * bone.rotation;
        return bone.parent != null
            ? Quaternion.Inverse(bone.parent.rotation) * targetWorld
            : targetWorld;
    }

    private static ArmPose SolveArmPose(
        Transform upperArm,
        Transform lowerArm,
        Transform hand,
        Vector3 requestedTarget,
        Vector3 polePoint
    )
    {
        Quaternion upperRest = upperArm.localRotation;
        Quaternion lowerRest = lowerArm.localRotation;
        Vector3 shoulder = upperArm.position;
        float upperLength = Vector3.Distance(shoulder, lowerArm.position);
        float lowerLength = Vector3.Distance(lowerArm.position, hand.position);
        Vector3 direction = requestedTarget - shoulder;
        float requestedDistance = Mathf.Max(direction.magnitude, 0.0001f);
        direction /= requestedDistance;
        float minimumDistance = Mathf.Abs(upperLength - lowerLength) + 0.0001f;
        float maximumDistance = upperLength + lowerLength - 0.0001f;
        float distance = Mathf.Clamp(requestedDistance, minimumDistance, maximumDistance);
        Vector3 target = shoulder + direction * distance;

        Vector3 poleDirection = Vector3.ProjectOnPlane(polePoint - shoulder, direction);
        if (poleDirection.sqrMagnitude < 0.0001f)
            poleDirection = Vector3.ProjectOnPlane(Vector3.down, direction);
        if (poleDirection.sqrMagnitude < 0.0001f)
            poleDirection = Vector3.Cross(direction, Vector3.right);
        poleDirection.Normalize();

        float along = (
            upperLength * upperLength - lowerLength * lowerLength + distance * distance
        ) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 elbowTarget = shoulder + direction * along + poleDirection * height;

        upperArm.rotation = Quaternion.FromToRotation(
            lowerArm.position - shoulder,
            elbowTarget - shoulder
        ) * upperArm.rotation;
        lowerArm.rotation = Quaternion.FromToRotation(
            hand.position - lowerArm.position,
            target - lowerArm.position
        ) * lowerArm.rotation;

        ArmPose pose = new ArmPose(
            upperRest,
            lowerRest,
            upperArm.localRotation,
            lowerArm.localRotation
        );
        upperArm.localRotation = upperRest;
        lowerArm.localRotation = lowerRest;
        return pose;
    }

    private static void SetRotationCurve(
        AnimationClip clip,
        Transform root,
        Transform bone,
        IReadOnlyList<TimedPose> poses
    )
    {
        string path = AnimationUtility.CalculateTransformPath(bone, root);
        Quaternion rest = bone.localRotation;
        AnimationCurve x = new AnimationCurve();
        AnimationCurve y = new AnimationCurve();
        AnimationCurve z = new AnimationCurve();
        AnimationCurve w = new AnimationCurve();

        for (int index = 0; index < poses.Count; index++)
        {
            Quaternion rotation = rest * Quaternion.Euler(poses[index].EulerOffset);
            x.AddKey(poses[index].Time, rotation.x);
            y.AddKey(poses[index].Time, rotation.y);
            z.AddKey(poses[index].Time, rotation.z);
            w.AddKey(poses[index].Time, rotation.w);
        }

        SetQuaternionContinuity(x, y, z, w);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.x"), x);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.y"), y);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.z"), z);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.w"), w);
        clip.EnsureQuaternionContinuity();
    }

    private static void SetAbsoluteRotationCurve(
        AnimationClip clip,
        Transform root,
        Transform bone,
        IReadOnlyList<TimedRotation> poses
    )
    {
        string path = AnimationUtility.CalculateTransformPath(bone, root);
        AnimationCurve x = new AnimationCurve();
        AnimationCurve y = new AnimationCurve();
        AnimationCurve z = new AnimationCurve();
        AnimationCurve w = new AnimationCurve();

        for (int index = 0; index < poses.Count; index++)
        {
            Quaternion rotation = poses[index].Rotation;
            x.AddKey(poses[index].Time, rotation.x);
            y.AddKey(poses[index].Time, rotation.y);
            z.AddKey(poses[index].Time, rotation.z);
            w.AddKey(poses[index].Time, rotation.w);
        }

        SetQuaternionContinuity(x, y, z, w);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.x"), x);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.y"), y);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.z"), z);
        AnimationUtility.SetEditorCurve(clip, Binding(path, "m_LocalRotation.w"), w);
        clip.EnsureQuaternionContinuity();
    }

    private static EditorCurveBinding Binding(string path, string property)
    {
        return EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
    }

    private static void SetQuaternionContinuity(params AnimationCurve[] curves)
    {
        foreach (AnimationCurve curve in curves)
        {
            for (int index = 0; index < curve.length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
            }
        }
    }

    private static Dictionary<string, Transform> FindRequiredBones(Transform root)
    {
        string[] names =
        {
            "Head", "Spine", "Hips",
            "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
            "RightShoulder", "RightArm", "RightForeArm", "RightHand"
        };
        Dictionary<string, Transform> result = new Dictionary<string, Transform>();
        foreach (string name in names)
        {
            Transform bone = FindRecursive(root, name);
            if (bone == null)
                throw new InvalidOperationException("Required puppet bone is missing: " + name);
            result[name] = bone;
        }
        return result;
    }

    private static Transform FindRecursive(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent ?? "Assets", name);
    }

    private static void ValidateOutput()
    {
        string[] requiredAssets =
        {
            StageScenePath,
            ActionScenePath,
            PuppetPrefabPath,
            ControllerPath,
            "Assets/Animations/Idle.anim",
            "Assets/Animations/Drink.anim",
            "Assets/Animations/Attack.anim",
            "Assets/Animations/Bow.anim",
            "Assets/Animations/Landscape.anim",
            "Assets/Animations/Alert.anim"
        };

        foreach (string path in requiredAssets)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException("Bootstrap output is missing: " + path);
        }
    }

    private readonly struct TimedPose
    {
        public readonly float Time;
        public readonly Vector3 EulerOffset;

        public TimedPose(float time, Vector3 eulerOffset)
        {
            Time = time;
            EulerOffset = eulerOffset;
        }
    }

    private readonly struct TimedRotation
    {
        public readonly float Time;
        public readonly Quaternion Rotation;

        public TimedRotation(float time, Quaternion rotation)
        {
            Time = time;
            Rotation = rotation;
        }
    }

    private readonly struct ArmPose
    {
        public readonly Quaternion UpperRest;
        public readonly Quaternion LowerRest;
        public readonly Quaternion UpperTarget;
        public readonly Quaternion LowerTarget;

        public ArmPose(
            Quaternion upperRest,
            Quaternion lowerRest,
            Quaternion upperTarget,
            Quaternion lowerTarget
        )
        {
            UpperRest = upperRest;
            LowerRest = lowerRest;
            UpperTarget = upperTarget;
            LowerTarget = lowerTarget;
        }
    }
}
