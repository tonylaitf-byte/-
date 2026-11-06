using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class HybridHandoffValidation
{
    const string Pending = "HybridHandoffValidation.Pending";
    static IEnumerator routine;
    static int lastFrame = -1, epoch = 90000, sequence;
    static Report report;
    [Serializable] sealed class Report
    {
        public bool passed;
        public string failure = "";
        public List<string> engineDiagnostics = new List<string>();
        public List<Row> results = new List<Row>();
    }
    [Serializable] sealed class Row
    {
        public string role, action, scenario;
        public int fps;
        public float entryPeakDegreesPerSecond, releasePeakDegreesPerSecond;
        public float firstMappingStepDegrees, releaseSeconds, nominalReleaseSeconds;
        public bool completed;
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
        report = new Report(); routine = null; lastFrame = -1;
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
        Directory.CreateDirectory("../../reports/hybrid-handoff");
        File.WriteAllText("../../reports/hybrid-handoff/Validation.json", JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.passed ? 0 : 1);
    }
    static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    static PuppetControlState Control(float y, int eventId = 0, string action = "") => new PuppetControlState {
        version = 2, controlMode = "hybrid", inputEpoch = epoch, sequenceId = ++sequence,
        handDetected = true, calibrated = true, sampleDt = 1f / 60,
        actionSequence = eventId, action = action,
        leftHandY = y, rightHandY = -y, leftHandZ = -y, rightHandZ = y,
        headYaw = y * .7f, headPitch = y * .5f
    };
    static void Send(HybridPuppetActor actor, PuppetControlState state)
    { Require(actor.Accept(11, true, state, out _, out _), "Input rejected"); }
    static Quaternion[] Pose(HybridPuppetActor actor)
    {
        var result = new Quaternion[actor.bones.Length];
        for (int i = 0; i < result.Length; i++) result[i] = actor.bones[i].bone.localRotation;
        return result;
    }
    static float Difference(HybridPuppetActor actor, Quaternion[] previous, HybridBodyPart mask)
    {
        float maximum = 0;
        for (int i = 0; i < previous.Length; i++)
            if ((actor.bones[i].part & mask) != 0)
                maximum = Mathf.Max(maximum, Quaternion.Angle(previous[i], actor.bones[i].bone.localRotation));
        return maximum;
    }
    static IEnumerator Check()
    {
        yield return null;
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        Require(session && session.Initialize(), "Scene failed to initialize");
        session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
        foreach (var actor in new[] { session.leftActor, session.rightActor })
        foreach (int fps in new[] { 30, 60, 120 })
        foreach (var action in actor.actions)
        foreach (bool cancel in new[] { false, true })
        {
            float dt = 1f / fps;
            epoch++;
            actor.StopImmediately();
            Send(actor, Control(-.9f));
            for (int i = 0; i < fps * 3; i++) actor.Step(dt);
            Send(actor, Control(-.9f, 1, action.actionId));
            Require(actor.TryStart(0, 1), "Action failed: " + action.actionId);
            actor.VoiceEnded();
            var row = new Row { role = actor.role, action = action.actionId, fps = fps,
                scenario = cancel ? "cancel-moving-target" : "natural-moving-target",
                nominalReleaseSeconds = actor.ReleaseDuration };
            report.results.Add(row);
            float releaseTime = 0;
            bool sawRelease = false, reversed = false;
            for (int frame = 0; frame < fps * 50 && actor.ActionActive; frame++)
            {
                if (cancel && frame == fps) actor.CancelAction();
                bool releasing = actor.Releasing;
                if (releasing)
                {
                    sawRelease = true;
                    releaseTime += dt;
                    // Reverse the live target immediately before the original timeout.
                    if (!reversed && releaseTime >= actor.ReleaseDuration - dt * 1.5f)
                    { Send(actor, Control(.9f, 1)); reversed = true; }
                }
                else if (frame == fps / 2) Send(actor, Control(-.4f, 1));
                var previous = Pose(actor);
                float elapsed = actor.Elapsed;
                actor.Step(dt);
                float speed = Difference(actor, previous, action.mask) / dt;
                if (elapsed + dt <= Mathf.Max(.08f, action.blendIn))
                    row.entryPeakDegreesPerSecond = Mathf.Max(row.entryPeakDegreesPerSecond, speed);
                if (releasing || actor.Releasing)
                    row.releasePeakDegreesPerSecond = Mathf.Max(row.releasePeakDegreesPerSecond, speed);
            }
            row.releaseSeconds = releaseTime;
            row.completed = !actor.ActionActive && sawRelease && reversed;
            var end = Pose(actor);
            actor.Step(dt);
            row.firstMappingStepDegrees = Difference(actor, end, action.mask);
            Require(row.completed, "Release did not complete: " + JsonUtility.ToJson(row));
            Require(row.entryPeakDegreesPerSecond <= actor.handoffAngularSpeed + 1,
                "Entry exceeded speed bound: " + JsonUtility.ToJson(row));
            Require(row.releasePeakDegreesPerSecond <= actor.handoffAngularSpeed + 1,
                "Release exceeded speed bound: " + JsonUtility.ToJson(row));
            Require(row.firstMappingStepDegrees <= actor.handoffAngularSpeed * dt + .1f,
                "First mapping frame snapped: " + JsonUtility.ToJson(row));
            Require(releaseTime < actor.ReleaseDuration + 4, "Release did not settle promptly");
            yield return null;
        }
    }
}
