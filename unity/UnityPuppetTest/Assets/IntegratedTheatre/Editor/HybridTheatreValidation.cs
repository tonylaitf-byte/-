using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.Integration;
using PalmTheatre.DuetMapping;
using PalmTheatre.DuetGestures;

public static class HybridTheatreValidation
{
    const string Pending = "HybridTheatreValidation.Pending";
    static IEnumerator routine;
    static int lastFrame = -1, errors;
    static Report report;
    [Serializable] sealed class Report
    {
        public bool passed, eventGate, solo, duet, liveUnmaskedArm, latestTargetReturn,
            busyDrop, pause, epochRelease, musicianBounded, masks;
        public int errors;
        public string failure;
    }

    // Parent runs after scene generation, in a separate batch Editor process (without -quit).
    public static void Run()
    {
        if (!File.Exists(HybridTheatreBuilder.SoloScene) || !File.Exists(HybridTheatreBuilder.DuetScene))
            throw new InvalidOperationException("Generate hybrid scenes before running validation.");
        EditorSceneManager.OpenScene(HybridTheatreBuilder.SoloScene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        routine = null;
        report = new Report();
        errors = 0;
        lastFrame = -1;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }

    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (routine == null) routine = Check();
            if (routine.MoveNext()) return;
            report.passed = errors == 0;
        }
        catch (Exception exception) { report.failure = exception.ToString(); }
        Application.logMessageReceived -= Log;
        EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        report.errors = errors;
        Directory.CreateDirectory("IntegrationReports");
        File.WriteAllText("IntegrationReports/HybridValidation.json", JsonUtility.ToJson(report, true));
        Debug.Log(report.passed ? "HYBRID_VALIDATION_PASS" : "HYBRID_VALIDATION_FAIL: " + report.failure);
        EditorApplication.Exit(report.passed ? 0 : 1);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static PuppetControlState Control(int sequence = 0, string action = "", int epoch = 1)
    {
        return new PuppetControlState {
            version = 2, controlMode = "hybrid", inputEpoch = epoch,
            handDetected = true, calibrated = true, calibrationProgress = 1,
            sequenceId = sequence + 1, sampleDt = 1f / 60,
            actionSequence = sequence, action = action
        };
    }

    static DuetMappingHand Slot(int owner, PuppetControlState controls)
    {
        return new DuetMappingHand { ownerId = owner, ownerHeld = true,
            handDetected = controls.handDetected, controls = controls };
    }

    static HybridDuetState Duet(PuppetControlState left, PuppetControlState right)
    {
        return new HybridDuetState { version = 1, controlMode = "duet_hybrid", left = Slot(11, left), right = Slot(22, right) };
    }

    static void Advance(HybridTheatreSession session, float seconds)
    {
        for (int i = 0; i < Mathf.CeilToInt(seconds * 60); i++) session.Tick(1f / 60);
    }

    static HybridTheatreSession Session()
    {
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        Require(session && session.Initialize(), "Hybrid session failed to initialize.");
        session.automaticUpdate = false;
        session.enableAudio = false;
        AudioListener.volume = 0;
        Require(UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>().All(r => !r.enabled), "A legacy poller is enabled.");
        Require(!UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>(), "A legacy gesture session is enabled.");
        HybridTheatreBuilder.Audit(session.leftActor);
        if (session.rightActor) HybridTheatreBuilder.Audit(session.rightActor);
        return session;
    }

    static void CheckGate()
    {
        var gate = new HybridEventGate();
        Require(gate.Accept(11, true, Control(1, "ATTACK"), out _, out bool fresh) && fresh, "First event was lost.");
        Require(gate.Accept(11, true, Control(1, "ATTACK"), out _, out fresh) && !fresh, "Duplicate replayed.");
        Require(gate.Accept(11, true, Control(0), out _, out fresh) && !fresh, "Reverse sequence replayed.");
        Require(gate.Accept(22, true, Control(1, "BOW"), out bool changed, out fresh) && changed && fresh, "Independent owner sequence failed.");
        Require(!gate.Accept(11, true, Control(9, "ATTACK"), out _, out _), "Retired owner returned in the same epoch.");
        Require(gate.Accept(11, true, Control(0, "", 2), out changed, out fresh) && changed && !fresh, "New epoch failed.");
        Require(!gate.Accept(11, true, Control(10, "ATTACK"), out _, out _), "Old epoch was accepted.");
        report.eventGate = true;
    }

    static IEnumerator Check()
    {
        yield return null;
        CheckGate();
        var session = Session();
        var actor = session.leftActor;
        Require(session.ActorCount == 1, "Solo actor count changed.");
        session.AcceptSolo(Control());
        Advance(session, 1);
        var attack = Control(1, "ATTACK");
        session.AcceptSolo(attack);
        Advance(session, .4f);
        Vector3 hand = actor.mapping.leftHand.position;
        attack.leftHandY = .9f;
        session.AcceptSolo(attack);
        Advance(session, .4f);
        Require(actor.ActionActive && actor.StartedCount == 1 && Vector3.Distance(hand, actor.mapping.leftHand.position) > .02f,
            "ATTACK blocked unmasked left-arm mapping or replayed.");
        report.liveUnmaskedArm = true;
        var busy = Control(2, "BOW");
        busy.leftHandY = .9f;
        session.AcceptSolo(busy);
        Require(actor.DroppedCount == 1, "Busy event was not consumed.");
        Advance(session, 40);
        Require(!actor.ActionActive && actor.StartedCount == 1, "Busy event was queued or action never released.");
        report.busyDrop = true;
        hand = actor.mapping.leftHand.position;
        busy.leftHandY = -.4f;
        session.AcceptSolo(busy);
        Advance(session, 1);
        Require(actor.StartedCount == 1 && Vector3.Distance(hand, actor.mapping.leftHand.position) > .02f, "Did not return to latest mapping.");
        report.latestTargetReturn = true;
        session.AcceptSolo(Control(3, "BOW"));
        Advance(session, .5f);
        hand = actor.mapping.rightHand.position;
        float elapsed = actor.Elapsed;
        session.SetPaused(true);
        int dropped = actor.DroppedCount;
        session.AcceptSolo(Control(4, "DRINK"));
        Advance(session, 2);
        Require(actor.Elapsed == elapsed && Vector3.Distance(hand, actor.mapping.rightHand.position) < .00001f &&
            actor.Events.Sequence == 4 && actor.DroppedCount == dropped + 1, "Pause advanced pose or failed to consume an event.");
        session.SetPaused(false);
        session.AcceptSolo(Control(4, "DRINK"));
        Require(actor.DroppedCount == dropped + 1, "A paused event was replayed after resume.");
        report.pause = true;
        Quaternion before = actor.mapping.rightArm.localRotation;
        session.AcceptSolo(Control(0, "", 2));
        Require(actor.Releasing && Quaternion.Angle(before, actor.mapping.rightArm.localRotation) < .001f, "Epoch change snapped instead of releasing.");
        Advance(session, .1f);
        Require(actor.ActionActive && actor.Releasing, "Release was immediate.");
        var latest = Control(0, "", 2);
        latest.rightHandY = .9f;
        session.AcceptSolo(latest);
        Vector3 targetBefore = actor.mapping.CurrentRightHandTarget;
        for (int i = 0; i < Mathf.CeilToInt((actor.ReleaseDuration + 4) * 60) && actor.ActionActive; i++)
            session.Tick(1f / 60);
        Require(!actor.ActionActive && Vector3.Distance(targetBefore, actor.mapping.CurrentRightHandTarget) > .02f,
            "Release failed to settle toward the latest masked-arm mapping.");
        report.epochRelease = report.solo = true;

        var loading = EditorSceneManager.LoadSceneAsyncInPlayMode(HybridTheatreBuilder.DuetScene, new LoadSceneParameters(LoadSceneMode.Single));
        while (!loading.isDone) yield return null;
        yield return null;
        session = Session();
        Require(session.ActorCount == 2, "Duet actor count changed.");
        session.AcceptDuet(Duet(Control(), Control()));
        Advance(session, 1);
        session.AcceptDuet(Duet(Control(1, "ATTACK"), Control(1, "BOW")));
        Advance(session, .2f);
        Require(session.leftActor.StartedCount == 1 && session.rightActor.StartedCount == 1 &&
            session.Speaker == session.leftActor && session.rightActor.VoiceWaiting, "Duet owner/voice isolation failed.");
        session.AcceptDuet(Duet(Control(1, "ATTACK"), Control(0, "", 2)));
        Require(session.leftActor.ActionActive && !session.leftActor.Releasing && session.rightActor.Releasing,
            "Right recalibration cancelled the left actor.");
        Advance(session, 40);
        int sequence = 0;
        foreach (string id in new[] { "BOW", "LANDSCAPE", "DRINK", "ALERT", "ATTACK" })
        {
            session.AcceptDuet(Duet(Control(1, "ATTACK"), Control(++sequence, id, 2)));
            Require(session.rightActor.ActionActive, "Musician action failed: " + id);
            Advance(session, 45);
            Require(!session.rightActor.ActionActive && session.rightActor.musician.IsIdle,
                "Musician action did not exit: " + id);
        }
        report.duet = report.musicianBounded = report.masks = true;
        session.AcceptDuet(Duet(Control(0, "", 2), Control(sequence, "", 2)));
        int rightStarted = session.rightActor.StartedCount;
        session.AcceptDuet(Duet(Control(99, "ATTACK", 1), Control(++sequence, "BOW", 2)));
        Require(session.leftActor.Events.Epoch == 2 && session.rightActor.StartedCount == rightStarted + 1,
            "A stale left epoch blocked the right actor.");
        var lost = Control(sequence, "", 2);
        lost.handDetected = false;
        session.AcceptDuet(Duet(Control(0, "", 2), lost));
        Require(session.rightActor.Releasing && !session.rightActor.VoiceWaiting,
            "Tracking loss did not release the actor and cancel its waiting voice.");
        session.StopAll();
    }
}
