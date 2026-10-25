using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PuppetAvoidanceValidation
{
    [Serializable] sealed class Result
    {
        public string scene, rig;
        public bool swordProxy;
        public int samples, correctedFrames, unsafeFrames, baselineUnsafeFrames;
        public int intrusionProbes;
        public float maxPenetration, baselineMaxPenetration, maxWristStep, maxBlendStep;
        public float span, idleCorrection, releaseCorrection, headOnlyWristMovement;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public string failure, unityVersion;
        public List<Result> rigs = new List<Result>();
    }
    private static readonly MethodInfo ApplyIk = typeof(PuppetBoneTest).GetMethod("ApplyContinuousIk", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Run()
    {
        var report = new Report { unityVersion = Application.unityVersion };
        try
        {
            Check(Mathf.Abs(PuppetBodyAvoidance.SegmentDistance(Vector3.zero, Vector3.right,
                Vector3.up, Vector3.up + Vector3.right) - 1) < .0001f, "Parallel segment distance");
            Check(PuppetBodyAvoidance.SegmentDistance(-Vector3.right, Vector3.right,
                -Vector3.up, Vector3.up) < .0001f, "Crossing segments");
            Check(Mathf.Abs(PuppetBodyAvoidance.SegmentDistance(Vector3.zero, Vector3.zero,
                Vector3.up, Vector3.up) - 1) < .0001f, "Degenerate segments");
            foreach (int index in new[] { 2, 4, 5, 6 })
            {
                EditorSceneManager.OpenScene(IntegratedTheatreBuilder.Scenes[index]);
                foreach (var rig in UnityEngine.Object.FindObjectsByType<PuppetBoneTest>(FindObjectsSortMode.None))
                {
                    rig.InitializeHybridDrive();
                    var result = new Result { scene = Path.GetFileNameWithoutExtension(IntegratedTheatreBuilder.Scenes[index]),
                        rig = rig.name, swordProxy = rig.HasSwordAvoidance,
                        span = Vector3.Distance(rig.leftArm.position, rig.rightArm.position) };
                    report.rigs.Add(result);
                    Review(rig, result);
                }
            }
            Check(report.rigs.Count == 6, "Expected six rigs in four scenes");
            foreach (var result in report.rigs)
            {
                Check(result.unsafeFrames == 0, result.scene + ": unresolved body intersections");
                Check(result.idleCorrection < .001f, result.scene + ": neutral pose unexpectedly corrected");
                Check(result.releaseCorrection < .001f, result.scene + ": correction did not release");
                Check(result.headOnlyWristMovement < .005f, result.scene + ": head motion moved neutral wrists");
            }
            report.passed = true;
        }
        catch (Exception error) { report.failure = error.ToString(); }
        string directory = Path.GetFullPath("../../reports/arm-avoidance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Validation.json"), JsonUtility.ToJson(report, true));
        Debug.Log("PUPPET_AVOIDANCE_" + (report.passed ? "PASS" : report.failure));
        EditorApplication.Exit(report.passed ? 0 : 1);
    }

    private static PuppetControlState Control() => new PuppetControlState {
        version = 2, calibrated = true, handDetected = true, controlMode = "mapping", sampleDt = 1f / 60f
    };

    private static void Review(PuppetBoneTest rig, Result result)
    {
        var proxy = new PuppetBodyAvoidance(); proxy.Bind(rig);
        var state = Control();
        for (int frame = 0; frame < 120; frame++) { rig.SetControlState(state); rig.EvaluateHybridPose(1f / 60, null); }
        result.idleCorrection = Mathf.Max(rig.LeftAvoidanceBlend, rig.RightAvoidanceBlend);
        Vector3 neutralLeft = rig.leftHand.position, neutralRight = rig.rightHand.position;
        state.headPitch = 1; state.headYaw = 1; state.headRoll = 1;
        for (int frame = 0; frame < 120; frame++) { rig.SetControlState(state); rig.EvaluateHybridPose(1f / 60, null); }
        result.headOnlyWristMovement = Mathf.Max(Vector3.Distance(neutralLeft, rig.leftHand.position),
            Vector3.Distance(neutralRight, rig.rightHand.position)) / result.span;
        Vector3 previousLeft = rig.leftHand.position, previousRight = rig.rightHand.position;
        float previousBlend = rig.LeftAvoidanceBlend;
        for (int frame = 0; frame < 1800; frame++)
        {
            float t = frame / 60f;
            state.sequenceId = frame + 1;
            state.leftHandX = Mathf.Sin(t * .71f);
            state.leftHandY = Mathf.Sin(t * 1.3f);
            state.leftHandZ = Mathf.Sin(t * .93f);
            state.rightHandX = -state.leftHandX;
            state.rightHandY = Mathf.Sin(t * 1.1f + 1);
            state.rightHandZ = -Mathf.Sin(t * .8f);
            state.leftArm = (Mathf.Sin(t * .6f) + 1) * .5f;
            state.rightArm = (Mathf.Cos(t * .7f) + 1) * .5f;
            state.bodyTurn = Mathf.Sin(t * .5f);
            state.bodyLean = Mathf.Sin(t * .4f);
            state.bodyX = Mathf.Sin(t * .3f);
            state.headPitch = Mathf.Sin(t);
            state.headYaw = Mathf.Cos(t * .6f);
            rig.SetControlState(state);
            rig.EvaluateHybridPose(1f / 60, null);
            float penetration = Mathf.Max(rig.LeftBodyPenetration, rig.RightBodyPenetration) / result.span;
            Check(!float.IsNaN(penetration), "Nonfinite solver result");
            result.samples++;
            if (penetration > .0002f) result.unsafeFrames++;
            result.maxPenetration = Mathf.Max(result.maxPenetration, penetration);
            if (rig.LeftAvoidanceBlend > .001f || rig.RightAvoidanceBlend > .001f) result.correctedFrames++;
            result.maxWristStep = Mathf.Max(result.maxWristStep, Mathf.Max(Vector3.Distance(previousLeft, rig.leftHand.position),
                Vector3.Distance(previousRight, rig.rightHand.position)) / result.span);
            result.maxBlendStep = Mathf.Max(result.maxBlendStep, Mathf.Abs(previousBlend - rig.LeftAvoidanceBlend));
            previousLeft = rig.leftHand.position; previousRight = rig.rightHand.position;
            previousBlend = rig.LeftAvoidanceBlend;
            bool capture = result.scene == "SoloMapping" && (frame == 300 || frame == 900 || frame == 1300)
                && Environment.GetEnvironmentVariable("AVOIDANCE_RENDER") == "1";
            if (capture) Capture(rig, frame + "-protected");
            Quaternion lu = rig.leftArm.localRotation, ll = rig.leftForeArm.localRotation,
                ru = rig.rightArm.localRotation, rl = rig.rightForeArm.localRotation;
            float leftBlend = rig.LeftAvoidanceBlend, rightBlend = rig.RightAvoidanceBlend;
            rig.avoidBody = false;
            ApplyIk.Invoke(rig, new object[] { true, true });
            proxy.Refresh(rig);
            float baseline = Mathf.Max(proxy.Penetration(rig.leftArm, rig.leftForeArm, rig.leftHand, true, rig.avoidancePadding),
                proxy.Penetration(rig.rightArm, rig.rightForeArm, rig.rightHand, false, rig.avoidancePadding)) / result.span;
            result.baselineMaxPenetration = Mathf.Max(result.baselineMaxPenetration, baseline);
            if (baseline > .0002f) result.baselineUnsafeFrames++;
            if (capture) Capture(rig, frame + "-original");
            rig.avoidBody = true;
            // Restore the real output; the baseline must not be the next frame's pose.
            rig.leftArm.localRotation = lu; rig.leftForeArm.localRotation = ll;
            rig.rightArm.localRotation = ru; rig.rightForeArm.localRotation = rl;
            typeof(PuppetBoneTest).GetProperty("LeftAvoidanceBlend").SetValue(rig, leftBlend);
            typeof(PuppetBoneTest).GetProperty("RightAvoidanceBlend").SetValue(rig, rightBlend);
        }
        state = Control();
        for (int frame = 0; frame < 240; frame++) { rig.SetControlState(state); rig.EvaluateHybridPose(1f / 60, null); }
        result.releaseCorrection = Mathf.Max(rig.LeftAvoidanceBlend, rig.RightAvoidanceBlend);
        // An animation-owned arm must remain completely untouched by live IK.
        Quaternion leftUpper = rig.leftArm.localRotation, leftLower = rig.leftForeArm.localRotation;
        state.leftHandY = 1; rig.SetControlState(state);
        rig.EvaluateHybridPose(1f / 60, null, false, true);
        Check(Quaternion.Angle(leftUpper, rig.leftArm.localRotation) < .001f &&
            Quaternion.Angle(leftLower, rig.leftForeArm.localRotation) < .001f, "Animation ownership regression");
        proxy.Refresh(rig);
        var solve = typeof(PuppetBoneTest).GetMethod("SolveProtectedArm", BindingFlags.Instance | BindingFlags.NonPublic);
        object Field(string name) => typeof(PuppetBoneTest).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
        foreach (bool left in new[] { true, false })
        foreach (Vector3 target in new[] { proxy.torso.a, proxy.torso.b, proxy.skull.a })
        {
            string prefix = left ? "left" : "right";
            Transform upper = left ? rig.leftArm : rig.rightArm, lower = left ? rig.leftForeArm : rig.rightForeArm,
                hand = left ? rig.leftHand : rig.rightHand;
            solve.Invoke(rig, new object[] { upper, lower, hand, Field(prefix + "ArmRest"), Field(prefix + "ForeArmRest"),
                Field(prefix + "PoleInParent"), target, rig.maximumReach, left });
            Check(proxy.Penetration(upper, lower, hand, left, rig.avoidancePadding) / result.span <= .0002f,
                result.scene + ": direct intrusion probe failed");
            result.intrusionProbes++;
        }
    }

    private static void Capture(PuppetBoneTest rig, string name)
    {
        Camera camera = Camera.main;
        if (!camera) throw new InvalidOperationException("No render camera");
        var rt = RenderTexture.GetTemporary(960, 720, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        Vector3 position = camera.transform.position;
        float fieldOfView = camera.fieldOfView;
        Rect rect = camera.rect;
        var texture = new Texture2D(960, 720, TextureFormat.RGB24, false);
        var bakedObjects = new List<GameObject>();
        var bakedMeshes = new List<Mesh>();
        var skins = new List<SkinnedMeshRenderer>();
        try
        {
            // Edit-mode rendering can reuse stale GPU skinning matrices. Bake the
            // evaluated bones for this diagnostic image, without altering assets.
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled) continue;
                var mesh = new Mesh(); skin.BakeMesh(mesh);
                var visual = new GameObject("Avoidance diagnostic skin");
                visual.transform.SetParent(skin.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                skin.enabled = false;
                skins.Add(skin); bakedObjects.Add(visual); bakedMeshes.Add(mesh);
            }
            float span = Vector3.Distance(rig.leftArm.position, rig.rightArm.position);
            Vector3 center = (rig.leftArm.position + rig.rightArm.position) * .5f;
            camera.transform.position = center - camera.transform.forward * span * 6;
            camera.fieldOfView = 40;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); texture.Apply();
            string directory = Path.GetFullPath("../../reports/arm-avoidance");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.position = position; camera.fieldOfView = fieldOfView; camera.rect = rect;
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(texture);
            foreach (var skin in skins) skin.enabled = true;
            foreach (var visual in bakedObjects) UnityEngine.Object.DestroyImmediate(visual);
            foreach (var mesh in bakedMeshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
