using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MoonlitDuet;

public static class YueXianPlayModeValidation
{
    const string Pending = "MoonlitDuet.PlayModeValidation";
    static double started;
    static int frames, errors;
    static float movement, gripDistance;
    static Quaternion initialHand;
    static Vector3 socketPosition;
    static YueXianPerformance player;
    static Transform instrument, grip, support;
    [Serializable] sealed class Report
    {
        public bool passed, avatarValid, avatarHuman, humanoidPoseWorks, independentInstrument,
            instrumentAttachmentStable, companionPresent, cameraResetWorks, shadersSupported, completeHandsPresent, fingerSkinningWorks, thumbOrientationCorrect;
        public int framesObserved, runtimeErrors;
        public float pluckMotionDegrees, maximumGripDistance;
        public string message;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/YueXian/Scenes/MoonlitDuet.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        started = EditorApplication.timeSinceStartup;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }
    static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        try
        {
            if (player == null)
            {
                player = UnityEngine.Object.FindFirstObjectByType<YueXianPerformance>();
                if (!player) throw new Exception("YueXian performance component is missing.");
                instrument = Find(player.transform, "Yueqin_Independent");
                grip = Find(instrument, "Yueqin_LeftGrip");
                support = Find(player.transform, "LeftMiddleProximal");
                socketPosition = instrument.localPosition;
                initialHand = player.rightHand.localRotation;
                player.SetPlaying(true);
            }
            frames++;
            movement = Mathf.Max(movement, Quaternion.Angle(initialHand, player.rightHand.localRotation));
            gripDistance = Mathf.Max(gripDistance, Vector3.Distance(support.position, grip.position));
            if (frames < 50 || EditorApplication.timeSinceStartup - started < 4) return;
            EditorApplication.update -= Tick;
            var view = UnityEngine.Object.FindFirstObjectByType<MoonlitStageView>();
            view.ResetView();
            Vector3 expected = view.transform.position;
            view.transform.position += Vector3.one;
            view.ResetView();
            bool reset = Vector3.Distance(expected, view.transform.position) < .00001f;
            bool shaders = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported));
            var avatar = player.humanoidAnimator.avatar;
            bool separate = instrument.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0;
            bool attached = instrument.IsChildOf(player.chest) && Vector3.Distance(socketPosition, instrument.localPosition) < .00001f;
            bool companion = GameObject.Find("原角色 俠客") != null;
            bool poseWorks = ValidateHumanPose(player);
            bool completeHands = new[] { "Left", "Right" }.All(side => player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.name == side + "_Closed_Carved_Hand"));
            bool fingersWork = completeHands && ValidateFingerSkinning(player);
            bool thumbsCorrect = ValidateThumbOrientation(player);
            bool passed = avatar.isValid && avatar.isHuman && reset && shaders && separate && attached && companion && poseWorks && fingersWork && thumbsCorrect && movement > 2 && gripDistance < .14f && errors == 0;
            Capture(Camera.main, "YueXianReports/MoonlitDuet_PlayMode.png");
            YueXianSurfaceValidation.Capture(player);
            CaptureDetails(player);
            var report = new Report { passed = passed, avatarValid = avatar.isValid, avatarHuman = avatar.isHuman,
                humanoidPoseWorks = poseWorks, independentInstrument = separate, instrumentAttachmentStable = attached,
                companionPresent = companion, cameraResetWorks = reset, shadersSupported = shaders,
                completeHandsPresent = completeHands, fingerSkinningWorks = fingersWork, thumbOrientationCorrect = thumbsCorrect,
                framesObserved = frames, runtimeErrors = errors, pluckMotionDegrees = movement,
                maximumGripDistance = gripDistance, message = passed ? "Humanoid pose, plucking motion, prop attachment and duet scene passed in Play mode." : "Play mode validation failed." };
            File.WriteAllText("YueXianReports/PlayModeValidation.json", JsonUtility.ToJson(report, true));
            SessionState.SetBool(Pending, false);
            Debug.Log(passed ? "YUEXIAN_PLAYMODE_PASS" : "YUEXIAN_PLAYMODE_FAIL");
            EditorApplication.Exit(passed ? 0 : 1);
        }
        catch (Exception ex)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory("YueXianReports");
            File.WriteAllText("YueXianReports/PlayModeValidation.json", JsonUtility.ToJson(new Report { passed = false, message = ex.ToString() }, true));
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }
    static bool ValidateHumanPose(YueXianPerformance original)
    {
        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        try
        {
            clone.transform.position += Vector3.right * 20;
            var performance = clone.GetComponent<YueXianPerformance>();
            performance.enabled = false;
            var animator = performance.humanoidAnimator;
            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
            {
                HumanPose pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                int muscle = Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up");
                if (muscle < 0) throw new Exception("Left arm humanoid muscle was not found.");
                var bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                pose.muscles[muscle] = -.45f;
                handler.SetHumanPose(ref pose);
                Quaternion before = bone.localRotation;
                pose.muscles[muscle] = .45f;
                handler.SetHumanPose(ref pose);
                return Quaternion.Angle(before, bone.localRotation) > 5f &&
                    !float.IsNaN(bone.position.x) && !float.IsNaN(bone.position.y);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
    static bool ValidateFingerSkinning(YueXianPerformance original)
    {
        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        try
        {
            clone.transform.position += Vector3.right * 20;
            clone.GetComponent<YueXianPerformance>().enabled = false;
            foreach (string side in new[] { "Left", "Right" })
            {
                var renderer = clone.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == side + "_Closed_Carved_Hand");
                Transform finger = Find(clone.transform, side + "IndexProximal");
                var before = new Mesh(); var after = new Mesh();
                Quaternion rotation = finger.localRotation;
                try
                {
                    renderer.BakeMesh(before);
                    finger.localRotation = rotation * Quaternion.Euler(22, 0, 0);
                    renderer.BakeMesh(after);
                    Vector3[] a = before.vertices, b = after.vertices;
                    var weights = renderer.sharedMesh.boneWeights;
                    int index = Array.FindIndex(renderer.bones, t => t.name == side + "IndexDistal");
                    int middle = Array.FindIndex(renderer.bones, t => t.name == side + "MiddleDistal");
                    float moving = 0, neighbor = 0; int count = 0, other = 0;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        float distance = Vector3.Distance(renderer.transform.TransformPoint(a[i]), renderer.transform.TransformPoint(b[i]));
                        if (weights[i].boneIndex0 == index && weights[i].weight0 > .8f) { moving = Mathf.Max(moving, distance); count++; }
                        if (weights[i].boneIndex0 == middle && weights[i].weight0 > .8f) { neighbor = Mathf.Max(neighbor, distance); other++; }
                    }
                    if (count < 5 || other < 5 || moving < .015f || neighbor > .001f) return false;
                }
                finally { finger.localRotation = rotation; UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
            }
            return true;
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }
    static bool ValidateThumbOrientation(YueXianPerformance performer)
    {
        foreach(string side in new[]{"Left","Right"})
        {
            var wrist=Find(performer.transform,side+"Hand");
            Vector3 middle=(Find(performer.transform,side+"IndexProximal").position+Find(performer.transform,side+"LittleProximal").position)*.5f;
            Vector3 direction=(middle-wrist.position).normalized;
            Vector3 radial=Vector3.ProjectOnPlane(Find(performer.transform,side+"ThumbProximal").position-wrist.position,direction).normalized;
            if(Vector3.Dot(radial,performer.transform.up)<.35f)return false;
        }
        return true;
    }
    static void CaptureDetails(YueXianPerformance performer)
    {
        var camera = Camera.main;
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        bool ortho = camera.orthographic; float size = camera.orthographicSize;
        try
        {
            camera.orthographic = true;
            var target = performer.transform.TransformPoint(new Vector3(0, 1.44f, -.14f));
            foreach (var view in new[] { ("Front", new Vector3(0, .16f, -3)), ("Side", new Vector3(-3, .2f, -.5f)), ("Back", new Vector3(.3f, .2f, 3)) })
            {
                camera.transform.position = target + performer.transform.TransformDirection(view.Item2);
                camera.transform.LookAt(target);
                camera.orthographicSize = .36f;
                Capture(camera, "YueXianReports/HeadSleeve_" + view.Item1 + ".png");
            }
            foreach (string side in new[] { "Left", "Right" })
            {
                var wrist = Find(performer.transform, side + "Hand");
                var center = (Find(performer.transform, side + "IndexProximal").position + Find(performer.transform, side + "LittleProximal").position) * .5f;
                target = Vector3.Lerp(wrist.position, center, .6f);
                camera.transform.position = target + performer.transform.TransformDirection(new Vector3(-.08f, .12f, -1f));
                camera.transform.LookAt(target); camera.orthographicSize = .21f;
                Capture(camera, "YueXianReports/Hand_" + side + "_Unity.png");
                camera.transform.position = target + performer.transform.TransformDirection(new Vector3(side=="Left"?.75f:-.75f, -.10f, -.75f));
                camera.transform.LookAt(target);
                Capture(camera, "YueXianReports/Wrist_" + side + "_Unity.png");
            }
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.orthographic = ortho; camera.orthographicSize = size; }
    }
    static void Capture(Camera camera, string path)
    {
        var target = new RenderTexture(1800, 1400, 24) { antiAliasing = 8 };
        var pixels = new Texture2D(1800, 1400, TextureFormat.RGB24, false);
        var active = RenderTexture.active; var old = camera.targetTexture; float aspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = 1800f / 1400; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1800, 1400), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = old; camera.aspect = aspect; RenderTexture.active = active;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
