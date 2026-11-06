using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;
using PalmTheatre.DuetMapping;

public static class LivePuppetMotionAudit
{
    const string Pending = "LivePuppetMotionAudit.Pending";
    const float Dt = 1f / 60;
    static IEnumerator routine;
    static int lastFrame, sequence;
    static string folder;
    static Report report;
    [Serializable] sealed class Report
    {
        public string failure = "";
        public List<string> engineDiagnostics = new List<string>();
        public List<Row> results = new List<Row>();
    }
    [Serializable] sealed class Row
    {
        public string phase, role;
        public bool avoidance;
        public int frames, correctedFrames, intersectionFrames, releaseIntersectionFrames;
        public float maxCorrection, maxPenetrationInShoulderSpans, maxWristStepInShoulderSpans;
        public float maxBoneStepDegrees, finalLeftForward, finalRightForward, finalLeftReach, finalRightReach;
        public Vector3 finalLeftLocal, finalRightLocal;
    }
    sealed class Track
    {
        public Row row;
        HybridPuppetActor actor;
        PuppetBodyAvoidance proxy = new PuppetBodyAvoidance();
        Vector3 left, right;
        Quaternion[] rotations;
        public Track(HybridPuppetActor value, string phase, bool enabled)
        {
            actor = value;
            row = new Row { role = value.role, phase = phase, avoidance = enabled };
            proxy.Bind(value.mapping);
            left = value.mapping.leftHand.position; right = value.mapping.rightHand.position;
            rotations = new Quaternion[value.bones.Length];
            for (int i = 0; i < rotations.Length; i++) rotations[i] = value.bones[i].bone.localRotation;
        }
        public void Sample()
        {
            var m = actor.mapping;
            proxy.Refresh(m);
            float span = proxy.span;
            float overlap = Mathf.Max(proxy.Penetration(m.leftArm, m.leftForeArm, m.leftHand, true, m.avoidancePadding),
                proxy.Penetration(m.rightArm, m.rightForeArm, m.rightHand, false, m.avoidancePadding)) / span;
            row.maxPenetrationInShoulderSpans = Mathf.Max(row.maxPenetrationInShoulderSpans, overlap);
            if (overlap > .0002f) { row.intersectionFrames++; if (actor.Releasing) row.releaseIntersectionFrames++; }
            float correction = Mathf.Max(m.LeftAvoidanceBlend, m.RightAvoidanceBlend);
            if (correction > .001f) row.correctedFrames++;
            row.maxCorrection = Mathf.Max(row.maxCorrection, correction);
            row.maxWristStepInShoulderSpans = Mathf.Max(row.maxWristStepInShoulderSpans,
                Mathf.Max(Vector3.Distance(left, m.leftHand.position), Vector3.Distance(right, m.rightHand.position)) / span);
            for (int i = 0; i < rotations.Length; i++)
            {
                Quaternion rotation = actor.bones[i].bone.localRotation;
                row.maxBoneStepDegrees = Mathf.Max(row.maxBoneStepDegrees, Quaternion.Angle(rotations[i], rotation));
                rotations[i] = rotation;
            }
            left = m.leftHand.position; right = m.rightHand.position;
            float length = Vector3.Distance(m.leftArm.position, m.leftForeArm.position) + Vector3.Distance(m.leftForeArm.position, left);
            row.finalLeftForward = Vector3.Dot(left - m.leftArm.position, m.transform.forward) / length;
            row.finalLeftReach = Vector3.Distance(left, m.leftArm.position) / length;
            length = Vector3.Distance(m.rightArm.position, m.rightForeArm.position) + Vector3.Distance(m.rightForeArm.position, right);
            row.finalRightForward = Vector3.Dot(right - m.rightArm.position, m.transform.forward) / length;
            row.finalRightReach = Vector3.Distance(right, m.rightArm.position) / length;
            row.finalLeftLocal = m.transform.InverseTransformPoint(left);
            row.finalRightLocal = m.transform.InverseTransformPoint(right);
            row.frames++;
        }
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(HybridTheatreBuilder.DuetScene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        report = new Report(); routine = null; lastFrame = -1; sequence = 0;
        folder = Path.GetFullPath(Environment.GetEnvironmentVariable("PUPPET_AUDIT_OUTPUT") ?? "../../reports/current-motion-audit");
        Directory.CreateDirectory(folder);
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            report.engineDiagnostics.Add(message + "\n" + trace);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (routine == null) routine = Review(); if (routine.MoveNext()) return; }
        catch (Exception error) { report.failure = error.ToString(); }
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        File.WriteAllText(Path.Combine(folder, "Audit.json"), JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.failure == "" ? 0 : 1);
    }
    static PuppetControlState Control(string action = "", int eventId = 0) => new PuppetControlState {
        version = 2, controlMode = "hybrid", handDetected = true, calibrated = true,
        inputEpoch = 43210, sequenceId = ++sequence, sampleDt = Dt, action = action, actionSequence = eventId
    };
    static void Send(HybridTheatreSession session, PuppetControlState controls)
    {
        if (!session.AcceptDuet(new HybridDuetState {
            version = 1, controlMode = "duet_hybrid",
            left = new DuetMappingHand { ownerHeld = true, ownerId = 11, handDetected = true, controls = controls },
            right = new DuetMappingHand { ownerHeld = true, ownerId = 22, handDetected = true, controls = controls }
        })) throw new InvalidOperationException("Synthetic input was rejected");
    }
    static IEnumerator Review()
    {
        yield return null; yield return null;
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        if (!session || !session.Initialize()) throw new InvalidOperationException("Scene initialization failed");
        session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
        foreach (bool enabled in new[] { false, true })
        foreach (string phase in new[] { "forward", "sweep", "head-only" })
        {
            session.leftActor.mapping.avoidBody = session.rightActor.mapping.avoidBody = enabled;
            var neutral = Control();
            if (phase == "head-only") neutral.leftArm = neutral.rightArm = 1;
            Send(session, neutral);
            for (int i = 0; i < 300; i++) session.Tick(Dt);
            var left = new Track(session.leftActor, phase, enabled);
            var right = new Track(session.rightActor, phase, enabled);
            var sheet = new Texture2D(1440, 720, TextureFormat.RGB24, false);
            for (int frame = 0; frame < 360; frame++)
            {
                float u = frame / 359f;
                var controls = Control();
                if (phase == "forward") controls.leftArm = controls.rightArm = Mathf.Min(1, u * 1.5f);
                if (phase == "sweep")
                {
                    controls.leftHandY = controls.rightHandY = Mathf.Sin(u * 2 * Mathf.PI) * .8f;
                    controls.leftHandZ = -Mathf.Sin(u * Mathf.PI);
                    controls.rightHandZ = -controls.leftHandZ;
                }
                if (phase == "head-only")
                {
                    controls.leftArm = controls.rightArm = 1;
                    controls.headPitch = Mathf.Sin(u * 2 * Mathf.PI);
                    controls.headYaw = Mathf.Sin(u * 2 * Mathf.PI) * .6f;
                }
                Send(session, controls); session.Tick(Dt); left.Sample(); right.Sample();
                if (frame % 60 == 0) { yield return null; Capture(sheet, frame / 60, session.leftActor); }
            }
            report.results.Add(left.row); report.results.Add(right.row);
            File.WriteAllBytes(Path.Combine(folder, phase + (enabled ? "-protected" : "-original") + ".png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }
        int eventId = 0;
        foreach (string action in new[] { "BOW", "LANDSCAPE", "DRINK", "ALERT", "ATTACK" })
        {
            Send(session, Control("", eventId));
            for (int i = 0; i < 300; i++) session.Tick(Dt);
            var left = new Track(session.leftActor, action, true);
            var right = new Track(session.rightActor, action, true);
            Send(session, Control(action, ++eventId));
            var sheet = new Texture2D(1440, 720, TextureFormat.RGB24, false);
            int shot = 0;
            for (int frame = 0; frame < 3600; frame++)
            {
                var controls = Control(action, eventId);
                controls.leftArm = controls.rightArm = .85f;
                Send(session, controls); session.Tick(Dt); left.Sample(); right.Sample();
                if (frame % 60 == 0 && shot < 5) { yield return null; Capture(sheet, shot++, session.leftActor); }
                if (frame % 180 == 0) yield return null;
                if (!session.leftActor.ActionActive && !session.rightActor.ActionActive)
                { yield return null; Capture(sheet, 5, session.leftActor); break; }
                if (frame == 3599) throw new InvalidOperationException("Action did not finish: " + action);
            }
            report.results.Add(left.row); report.results.Add(right.row);
            File.WriteAllBytes(Path.Combine(folder, action + ".png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }
        session.StopAll();
    }
    static void Capture(Texture2D sheet, int shot, HybridPuppetActor actor)
    {
        Camera camera = Camera.main;
        var rt = RenderTexture.GetTemporary(480, 360, 24);
        var previous = camera.targetTexture; var active = RenderTexture.active;
        Vector3 position = camera.transform.position; Rect rect = camera.rect; float fov = camera.fieldOfView;
        var image = new Texture2D(480, 360, TextureFormat.RGB24, false);
        try
        {
            var m = actor.mapping;
            float span = Vector3.Distance(m.leftArm.position, m.rightArm.position);
            camera.transform.position = (m.leftArm.position + m.rightArm.position) * .5f - camera.transform.forward * span * 6;
            camera.rect = new Rect(0, 0, 1, 1); camera.fieldOfView = 40;
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 480, 360), 0, 0); image.Apply();
            sheet.SetPixels(shot % 3 * 480, (1 - shot / 3) * 360, 480, 360, image.GetPixels()); sheet.Apply();
        }
        finally
        {
            camera.transform.position = position; camera.rect = rect; camera.fieldOfView = fov;
            camera.targetTexture = previous; RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
