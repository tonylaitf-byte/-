using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;
using PalmTheatre.DuetMapping;

public static class HybridMotionReview
{
    const string Pending = "HybridMotionReview.Pending";
    const float Dt = 1f / 60;
    static IEnumerator routine;
    static string folder;
    static readonly List<Result> results = new List<Result>();
    static readonly List<string> diagnostics = new List<string>();
    static int lastFrame;

    [Serializable] sealed class Report
    {
        public bool completed, timingChecks;
        public string unityVersion, failure;
        public Result[] actions;
        public string[] engineDiagnostics;
    }
    [Serializable] sealed class Result
    {
        public string action, role;
        public string peakBone, accelerationBone, accelerationPhase;
        public float peakTime, accelerationTime, actionSeconds;
        public float seconds, peakDegreesPerSecond, p95DegreesPerSecond,
            peakAcceleration, entryPeak, actionPeak, releasePeak, peakWristSpeed;
        public int frames;
    }
    sealed class Track
    {
        readonly HybridPuppetActor actor;
        readonly Quaternion[] previous;
        readonly float[] speeds;
        readonly List<float> frameSpeeds = new List<float>();
        Vector3 left, right;
        public readonly Result result;
        public Track(HybridPuppetActor value, string id)
        {
            actor = value;
            previous = actor.bones.Select(b => b.bone.localRotation).ToArray();
            speeds = new float[previous.Length];
            left = actor.mapping.leftHand.position;
            right = actor.mapping.rightHand.position;
            result = new Result { action = id, role = actor.role };
        }
        public void Sample(float time)
        {
            float peak = 0;
            for (int i = 0; i < previous.Length; i++)
            {
                Quaternion current = actor.bones[i].bone.localRotation;
                float speed = Quaternion.Angle(previous[i], current) / Dt;
                if (float.IsNaN(speed)) throw new InvalidOperationException("Nonfinite bone motion.");
                float acceleration = Mathf.Abs(speed - speeds[i]) / Dt;
                if (acceleration > result.peakAcceleration)
                {
                    result.peakAcceleration = acceleration;
                    result.accelerationBone = actor.bones[i].bone.name;
                    result.accelerationTime = time;
                    result.accelerationPhase = actor.Releasing ? "Release" : actor.ActionActive ? "Action" : "Mapping";
                }
                if (speed > result.peakDegreesPerSecond)
                {
                    result.peakDegreesPerSecond = speed;
                    result.peakBone = actor.bones[i].bone.name;
                    result.peakTime = time;
                }
                previous[i] = current;
                speeds[i] = speed;
                peak = Mathf.Max(peak, speed);
            }
            result.peakDegreesPerSecond = Mathf.Max(result.peakDegreesPerSecond, peak);
            if (actor.Releasing || !actor.ActionActive) result.releasePeak = Mathf.Max(result.releasePeak, peak);
            else if (time < .6f) result.entryPeak = Mathf.Max(result.entryPeak, peak);
            else result.actionPeak = Mathf.Max(result.actionPeak, peak);
            result.peakWristSpeed = Mathf.Max(result.peakWristSpeed,
                Vector3.Distance(left, actor.mapping.leftHand.position) / Dt,
                Vector3.Distance(right, actor.mapping.rightHand.position) / Dt);
            left = actor.mapping.leftHand.position;
            right = actor.mapping.rightHand.position;
            frameSpeeds.Add(peak);
            result.frames++;
            result.seconds = time;
            result.actionSeconds = actor.Duration;
        }
        public Result Finish()
        {
            frameSpeeds.Sort();
            result.p95DegreesPerSecond = frameSpeeds[Mathf.Min(frameSpeeds.Count - 1, Mathf.FloorToInt(frameSpeeds.Count * .95f))];
            return result;
        }
    }

    public static void Run()
    {
        if (Environment.GetEnvironmentVariable("HYBRID_REVIEW_LABEL") == "after") HybridTheatreBuilder.Prepare();
        EditorSceneManager.OpenScene(HybridTheatreBuilder.DuetScene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        results.Clear(); diagnostics.Clear(); routine = null; lastFrame = -1;
        string label = Environment.GetEnvironmentVariable("HYBRID_REVIEW_LABEL") ?? "review";
        if (label != "before" && label != "after") label = "review";
        folder = Path.GetFullPath("../../reports/motion-" + label);
        Directory.CreateDirectory(folder);
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            diagnostics.Add(message + "\n" + trace);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        string failure = "";
        try
        {
            if (routine == null) routine = Review();
            if (routine.MoveNext()) return;
        }
        catch (Exception exception) { failure = exception.ToString(); }
        Application.logMessageReceived -= Log;
        EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        File.WriteAllText(Path.Combine(folder, "MotionReview.json"), JsonUtility.ToJson(new Report {
            completed = failure == "" && results.Count == 10, timingChecks = failure == "", failure = failure,
            unityVersion = Application.unityVersion, actions = results.ToArray(), engineDiagnostics = diagnostics.ToArray()
        }, true));
        Debug.Log("HYBRID_MOTION_REVIEW_" + (failure == "" ? "COMPLETE" : "FAIL: " + failure));
        EditorApplication.Exit(failure == "" ? 0 : 1);
    }
    static PuppetControlState Control(int sequence = 0, string action = "")
    {
        return new PuppetControlState { version = 2, controlMode = "hybrid", inputEpoch = 9000,
            sequenceId = sequence + 1, sampleDt = Dt, calibrated = true, calibrationProgress = 1,
            handDetected = true, actionSequence = sequence, action = action };
    }
    static HybridDuetState State(int sequence = 0, string action = "")
    {
        return new HybridDuetState { version = 1, controlMode = "duet_hybrid",
            left = new DuetMappingHand { ownerHeld = true, ownerId = 11, handDetected = true, controls = Control(sequence, action) },
            right = new DuetMappingHand { ownerHeld = true, ownerId = 22, handDetected = true, controls = Control(sequence, action) } };
    }
    static IEnumerator Review()
    {
        CheckTiming();
        yield return null;
        yield return null;
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        if (!session || !session.Initialize()) throw new InvalidOperationException("Hybrid scene failed to initialize.");
        session.automaticUpdate = false;
        session.enableAudio = false;
        AudioListener.volume = 0;
        session.AcceptDuet(State());
        for (int i = 0; i < 180; i++) session.Tick(Dt);
        string[] ids = { "BOW", "LANDSCAPE", "DRINK", "ALERT", "ATTACK" };
        for (int index = 0; index < ids.Length; index++)
        {
            string id = ids[index];
            var left = new Track(session.leftActor, id);
            var right = new Track(session.rightActor, id);
            session.AcceptDuet(State(index + 1, id));
            if (!session.leftActor.ActionActive || !session.rightActor.ActionActive)
                throw new InvalidOperationException("Action failed to start: " + id);
            float duration = Mathf.Max(session.leftActor.Duration, session.rightActor.Duration);
            float[] samples = { 0, .2f, .5f, 1, duration * .5f, duration, duration + .18f, duration + 1 };
            int shot = 0, frame = 0;
            var sheet = new Texture2D(1600, 450, TextureFormat.RGB24, false);
            while (frame < 3600)
            {
                float time = frame * Dt;
                if (shot < samples.Length && time + .001f >= samples[shot])
                {
                    yield return null;
                    Capture(sheet, shot);
                    shot++;
                }
                session.Tick(Dt);
                left.Sample(time); right.Sample(time);
                frame++;
                if (!session.leftActor.ActionActive && !session.rightActor.ActionActive && time > duration + 1) break;
                if (frame % 180 == 0) yield return null;
            }
            if (frame >= 3600) throw new InvalidOperationException("Action failed to finish: " + id);
            while (shot < samples.Length) { yield return null; Capture(sheet, shot); shot++; }
            File.WriteAllBytes(Path.Combine(folder, id + ".png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            results.Add(left.Finish()); results.Add(right.Finish());
            for (int i = 0; i < 180; i++) session.Tick(Dt);
            yield return null;
        }
        session.StopAll();
    }
    static void CheckTiming()
    {
        foreach (float length in new[] { 1f, 2.2f, 3.6f, 5f })
        foreach (float multiplier in new[] { 1f, 1.5f, 3f, 8f })
        {
            float duration = length * multiplier, start = length * .25f, end = length * .7f;
            float previous = -1;
            for (int i = 0; i <= 2000; i++)
            {
                float value = HybridMotionTiming.ClipTime(duration * i / 2000, duration, length, start, end);
                if (float.IsNaN(value) || value < previous - .00001f || value < 0 || value > length + .00001f)
                    throw new InvalidOperationException("Retiming is nonfinite, reverses or overshoots.");
                previous = value;
            }
            if (Mathf.Abs(previous - length) > .00001f) throw new InvalidOperationException("Retiming lost its endpoint.");
            if (multiplier == 1) continue;
            foreach (float boundary in new[] { start, start + duration - length + end - start })
            {
                const float h = .001f;
                float center = HybridMotionTiming.ClipTime(boundary, duration, length, start, end);
                float before = (center - HybridMotionTiming.ClipTime(boundary - h, duration, length, start, end)) / h;
                float after = (HybridMotionTiming.ClipTime(boundary + h, duration, length, start, end) - center) / h;
                if (Mathf.Abs(before - after) > .025f) throw new InvalidOperationException("Retiming speed jumps at its join.");
            }
        }
        if (HybridMotionTiming.ClipTime(1, 1, 0, 0, 0) != 0 || HybridMotionTiming.Ease(0) != 0 || HybridMotionTiming.Ease(1) != 1)
            throw new InvalidOperationException("Degenerate timing or easing endpoint failed.");
    }
    static void Capture(Texture2D sheet, int index)
    {
        var camera = Camera.main;
        if (!camera) throw new InvalidOperationException("Stage camera missing.");
        var target = RenderTexture.GetTemporary(800, 450, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var frame = new Texture2D(800, 450, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            frame.ReadPixels(new Rect(0, 0, 800, 450), 0, 0);
            frame.Apply();
            // Eight successive poses: two rows, four columns.
            var pixels = frame.GetPixels(0, 0, 800, 450);
            var reduced = new Color[400 * 225];
            for (int y = 0; y < 225; y++) for (int x = 0; x < 400; x++) reduced[y * 400 + x] = pixels[y * 2 * 800 + x * 2];
            sheet.SetPixels(index % 4 * 400, (1 - index / 4) * 225, 400, 225, reduced);
            sheet.Apply();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(frame);
        }
    }
}
