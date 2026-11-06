using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.Integration;

public static class HybridIdleValidation
{
    const string Pending = "HybridIdleValidation.Pending";
    const float Dt = 1f / 60;
    static IEnumerator routine;
    static int lastFrame = -1, sequence;
    static Report report;
    [Serializable] sealed class Report
    {
        public bool passed;
        public string failure = "";
        public List<Row> cases = new List<Row>();
        public List<string> engineDiagnostics = new List<string>();
    }
    [Serializable] sealed class Row
    {
        public string scene, role, idleClip;
        public bool initialIdle, liveMapping, uncalibratedIdle, lostHandIdle, actionReturn;
        public float maximumIdleError, breathingDegrees;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(HybridTheatreBuilder.SoloScene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        routine = null; lastFrame = -1; sequence = 0; report = new Report();
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            report.engineDiagnostics.Add(message + "\n" + trace);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (routine == null) routine = Check(); if (routine.MoveNext()) return; report.passed = true; }
        catch (Exception error) { report.failure = error.ToString(); }
        Application.logMessageReceived -= Log;
        EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        Directory.CreateDirectory("IntegrationReports");
        File.WriteAllText("IntegrationReports/HybridIdleValidation.json", JsonUtility.ToJson(report, true));
        Debug.Log(report.passed ? "HYBRID_IDLE_PASS" : "HYBRID_IDLE_FAIL");
        EditorApplication.Exit(report.passed ? 0 : 1);
    }
    static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    static PuppetControlState Control(bool detected = true, bool calibrated = true, int action = 0) => new PuppetControlState {
        version = 2, controlMode = "hybrid", inputEpoch = 99001, sequenceId = ++sequence,
        handDetected = detected, calibrated = calibrated, sampleDt = Dt,
        leftHandY = .8f, rightHandY = -.6f, headYaw = .4f,
        actionSequence = action, action = action > 0 ? "ATTACK" : ""
    };
    static void Send(HybridPuppetActor actor, PuppetControlState input)
    { Require(actor.Accept(11, true, input, out _, out _), "Input rejected"); }
    static void Advance(HybridPuppetActor actor, int frames)
    { for (int i = 0; i < frames; i++) actor.Step(Dt); }
    static float IdleError(HybridPuppetActor actor)
    {
        var status = actor.ReadStatus();
        Require(status.phase == "Idle" && status.idleWeight > .999f, "Authored idle did not take ownership");
        var actual = actor.bones.Select(b => b.bone.localRotation).ToArray();
        actor.idleClip.SampleAnimation(actor.animator.gameObject, status.idleTime % actor.idleClip.length);
        float error = actor.bones.Select((b, i) => Quaternion.Angle(b.bone.localRotation, actual[i])).Max();
        Require(error < .1f, "Idle differs from the current authored clip: " + error);
        return error;
    }
    static IEnumerator Check()
    {
        yield return null;
        foreach (var scene in new[] { HybridTheatreBuilder.SoloScene, HybridTheatreBuilder.DuetScene })
        {
            if (SceneManager.GetActiveScene().path != scene) {
                var loading = EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
                Require(loading != null, "Scene load did not start");
                while (!loading.isDone) yield return null;
                yield return null;
            }
            Require(SceneManager.GetActiveScene().path == scene, "Wrong hybrid scene loaded");
            var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
            Require(session && session.Initialize(), "Hybrid scene did not initialize");
            session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
            foreach (var actor in new[] { session.leftActor, session.rightActor }.Where(a => a))
            {
                var row = new Row { scene = scene, role = actor.role, idleClip = AssetDatabase.GetAssetPath(actor.idleClip) };
                report.cases.Add(row);
                Require(row.idleClip == (actor.musician ? "Assets/YueXian/ActionTest/Animations/MusicIdle.anim" :
                    "Assets/Animations/Idle.anim"), "Wrong idle clip");
                Advance(actor, 60); row.maximumIdleError = IdleError(actor); row.initialIdle = true;
                var before = actor.bones.Select(b => b.bone.localRotation).ToArray();
                Advance(actor, 144);
                row.breathingDegrees = actor.bones.Select((b, i) => Quaternion.Angle(b.bone.localRotation, before[i])).Max();
                Require(row.breathingDegrees > .1f, "Idle breathing is frozen");
                Send(actor, Control()); Advance(actor, 90);
                Require(actor.ReadStatus().phase == "Mapping", "Idle overrides calibrated mapping");
                Require(Vector3.Distance(actor.mapping.leftHand.position, actor.mapping.rightHand.position) > .01f,
                    "Invalid live mapping pose"); row.liveMapping = true;
                Send(actor, Control(calibrated: false)); Advance(actor, 60);
                row.maximumIdleError = Mathf.Max(row.maximumIdleError, IdleError(actor)); row.uncalibratedIdle = true;
                Send(actor, Control()); Advance(actor, 60);
                Send(actor, Control(action: 1)); Require(actor.TryStart(0, 1), "Action did not start");
                actor.VoiceEnded();
                for (int i = 0; i < 60 * 50 && actor.ActionActive; i++) actor.Step(Dt);
                Require(!actor.ActionActive && actor.ReadStatus().phase == "Mapping", "Action did not return to live mapping");
                row.actionReturn = true;
                actor.Disconnect(); Advance(actor, 60);
                row.maximumIdleError = Mathf.Max(row.maximumIdleError, IdleError(actor)); row.lostHandIdle = true;
            }
        }
        Require(report.cases.Count == 3, "Expected solo warrior and both duet actors");
        Require(report.engineDiagnostics.All(d => d.Contains("UnityEditor.Search.SearchDatabase")),
            "Unexpected Unity diagnostic");
    }
}
