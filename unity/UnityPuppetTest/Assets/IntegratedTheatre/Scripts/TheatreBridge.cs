using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.DuetGestures;
using PalmTheatre.DuetMapping;

namespace PalmTheatre.Integration
{
    // One browser owns the camera, polling, and session lifetime. Scene receivers
    // are disabled in the generated scenes, avoiding duplicate input paths.
    public sealed class TheatreBridge : MonoBehaviour
    {
        // Keep shaders looked up by the legacy runtime material passes in WebGL.
        public Shader[] retainedShaders;
        [Serializable] public sealed class Configuration
        {
            public string sessionId;
            public int players;
            public string mode;
        }
        [Serializable] public sealed class InputEnvelope
        {
            public string sessionId;
            public string state;
            public int sequence;
        }
        [Serializable] public sealed class Status
        {
            public string sessionId, mode, scene, subtitle, action, error;
            public int players, actors, sequence, queued;
            public bool ready, paused, speaking;
            public float leftX, rightX;
            public bool actionActive;
            public int actionSequence, inputEpoch;
            public HybridActorStatus leftHybrid, rightHybrid;
        }
        public Configuration Config { get; private set; }
        public bool Ready { get; private set; }
        public bool Paused { get; private set; }
        public string Error { get; private set; } = "";
        public int LastSequence { get; private set; }
        PuppetBoneTest solo;
        PuppetController soloActor;
        ActionManager actions;
        ActionVoicePresenter voice;
        DuetGestureSession duet;
        DuetMappingSession mapping;
        HybridTheatreSession hybrid;
        Coroutine loading;
        int actionSequence;
        float lastInput = float.NegativeInfinity, nextStatus;
        bool disconnected = true;
        [DllImport("__Internal")] static extern void TheatreStatus(string json);

        void Awake()
        {
            gameObject.name = "TheatreBridge";
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }
        void Start() { PublishStatus(); }
        public void Configure(string json)
        {
            Configuration config;
            try { config = JsonUtility.FromJson<Configuration>(json); }
            catch (ArgumentException) { return; }
            if (config == null || string.IsNullOrEmpty(config.sessionId) ||
                (config.players != 1 && config.players != 2) ||
                (config.mode != "gesture" && config.mode != "mapping" && config.mode != "hybrid") || loading != null) return;
            loading = StartCoroutine(Load(config));
        }
        IEnumerator Load(Configuration config)
        {
            Ready = false; Error = "";
            SetPaused("false");
            StopScene();
            Config = config; LastSequence = 0; actionSequence = 0;
            lastInput = float.NegativeInfinity; disconnected = true;
            PublishStatus();
            string scene = (config.players == 1 ? "Solo" : "Duet") +
                (config.mode == "gesture" ? "Gesture" : config.mode == "hybrid" ? "Hybrid" : "Mapping");
            yield return SceneManager.LoadSceneAsync(scene);
            // Let each existing controller complete its Start-time rig binding.
            yield return null;
            solo = null; soloActor = null; actions = null; voice = null; duet = null; mapping = null; hybrid = null;
            if (config.mode == "hybrid")
            {
                hybrid = FindAnyObjectByType<HybridTheatreSession>();
                if (!hybrid || hybrid.ActorCount != config.players || !hybrid.Initialize())
                    Error = hybrid && !string.IsNullOrEmpty(hybrid.Error) ? hybrid.Error : "Hybrid stage is incomplete.";
            }
            else if (config.players == 1)
            {
                solo = FindAnyObjectByType<PuppetBoneTest>();
                soloActor = FindAnyObjectByType<PuppetController>();
                actions = FindAnyObjectByType<ActionManager>();
                voice = FindAnyObjectByType<ActionVoicePresenter>();
                if (!solo || !soloActor || !actions || !voice) Error = "單人舞台元件不完整";
                else { solo.useAutomaticDemo = false; voice.showSubtitles = false; }
            }
            else if (config.mode == "gesture")
            {
                duet = FindAnyObjectByType<DuetGestureSession>();
                if (!duet) Error = "雙人手勢舞台元件不完整";
            }
            else
            {
                mapping = FindAnyObjectByType<DuetMappingSession>();
                if (!mapping) Error = "雙人映射舞台元件不完整";
            }
            Ready = string.IsNullOrEmpty(Error); loading = null;
            PublishStatus();
        }
        public void ReceiveState(string json)
        {
            if (!Ready || (Paused && !hybrid)) return;
            try
            {
                var packet = JsonUtility.FromJson<InputEnvelope>(json);
                if (packet == null || packet.sessionId != Config.sessionId || packet.sequence <= LastSequence) return;
                if (hybrid)
                {
                    bool accepted = Config.players == 1 ?
                        hybrid.AcceptSolo(JsonUtility.FromJson<PuppetControlState>(packet.state)) :
                        hybrid.AcceptDuet(JsonUtility.FromJson<HybridDuetState>(packet.state));
                    if (!accepted) return;
                }
                else if (solo)
                {
                    var state = JsonUtility.FromJson<PuppetControlState>(packet.state);
                    if (state == null || state.version != 2 || state.controlMode != Config.mode) return;
                    solo.SetControlState(state);
                    if (state.actionSequence != actionSequence)
                    {
                        actionSequence = state.actionSequence;
                        if (Config.mode == "gesture" && state.handDetected && state.actionSequence > 0 &&
                            !string.IsNullOrEmpty(state.action)) actions.QueueAction(state.action);
                    }
                }
                else if (duet)
                {
                    var state = JsonUtility.FromJson<DuetControlState>(packet.state);
                    if (state == null || state.version != 1 || state.controlMode != "duet") return;
                    duet.AcceptState(state);
                }
                else if (mapping)
                {
                    var state = JsonUtility.FromJson<DuetMappingState>(packet.state);
                    if (state == null || state.version != 1 || state.controlMode != "duet_mapping") return;
                    mapping.AcceptState(state);
                }
                LastSequence = packet.sequence; lastInput = Time.realtimeSinceStartup; disconnected = false;
            }
            catch (ArgumentException) { /* Reject malformed input without replacing the live pose. */ }
        }
        public void Disconnect(string unused)
        {
            disconnected = true;
            if (solo) solo.ClearExternalInput();
            if (duet) duet.MarkDisconnected();
            if (mapping) mapping.MarkDisconnected();
            if (hybrid) hybrid.MarkDisconnected();
        }
        public void SetPaused(string value)
        {
            Paused = value == "true";
            if (duet) duet.SetPaused(Paused);
            if (mapping) mapping.SetFrozen(Paused);
            if (hybrid) hybrid.SetPaused(Paused);
            if (solo) solo.enabled = !Paused;
            if (voice) voice.SetPaused(Paused);
            Time.timeScale = Paused ? 0 : 1;
            AudioListener.pause = Paused;
            PublishStatus();
        }
        void StopScene()
        {
            // Disable sessions before their actors/audio are destroyed. Their
            // existing OnDisable methods restore poses and need live children.
            if (duet) duet.enabled = false;
            if (mapping) mapping.enabled = false;
            if (hybrid) hybrid.enabled = false;
            if (actions) actions.StopAllCoroutines();
            if (voice) voice.enabled = false;
            solo = null; soloActor = null; actions = null; voice = null; duet = null; mapping = null; hybrid = null;
        }
        public void EndPerformance(string unused)
        {
            Ready = false; StopScene(); AudioListener.pause = true;
        }
        public void SetVolume(string value)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float volume)) AudioListener.volume = Mathf.Clamp01(volume);
        }
        public void ToggleInstrument(string unused)
        {
            if (mapping) mapping.rightMusician.SetInstrumentVisible(!mapping.rightMusician.showInstrument);
            if (hybrid && hybrid.rightActor) hybrid.rightActor.SetInstrumentVisible(!hybrid.rightActor.showInstrument);
        }
        public Status ReadStatus()
        {
            string subtitle = voice ? voice.CurrentSubtitle : duet ? duet.Subtitle : hybrid ? hybrid.Subtitle : "";
            if (!string.IsNullOrEmpty(subtitle)) subtitle = (voice ? "俠客" : duet ? duet.Speaker.characterName : hybrid.SpeakerName) + "：" + subtitle;
            var leftHybrid = hybrid && hybrid.leftActor ? hybrid.leftActor.ReadStatus() : null;
            var rightHybrid = hybrid && hybrid.rightActor ? hybrid.rightActor.ReadStatus() : null;
            return new Status {
                sessionId = Config?.sessionId ?? "", players = Config?.players ?? 0, mode = Config?.mode ?? "",
                scene = SceneManager.GetActiveScene().name, ready = Ready, paused = Paused, error = Error,
                subtitle = subtitle, speaking = voice ? voice.IsPlaying : duet ? duet.Speaker != null : hybrid && hybrid.Speaker != null,
                action = soloActor ? soloActor.CurrentAction : duet && duet.Speaker != null ? duet.Speaker.CurrentLine.displayName :
                    hybrid && hybrid.Speaker ? hybrid.Speaker.ActiveAction?.actionId ?? "" : leftHybrid?.action ?? "",
                actors = hybrid ? hybrid.ActorCount : solo ? 1 : duet || mapping ? 2 : 0, sequence = LastSequence,
                queued = actions ? actions.PendingActionCount : 0,
                leftX = hybrid ? hybrid.leftActor.transform.position.x : solo ? solo.transform.position.x : duet ? duet.leftActor.transform.position.x : mapping ? mapping.leftWarrior.transform.position.x : 0,
                rightX = hybrid && hybrid.rightActor ? hybrid.rightActor.transform.position.x : duet ? duet.rightActor.transform.position.x : mapping ? mapping.rightMusician.transform.position.x : 0,
                actionActive = (leftHybrid?.actionActive ?? false) || (rightHybrid?.actionActive ?? false),
                actionSequence = leftHybrid?.actionSequence ?? actionSequence, inputEpoch = leftHybrid?.inputEpoch ?? 0,
                leftHybrid = leftHybrid, rightHybrid = rightHybrid
            };
        }
        void Update()
        {
            if (Ready && !Paused && !disconnected && Time.realtimeSinceStartup - lastInput > .8f) Disconnect("");
            if (Time.realtimeSinceStartup >= nextStatus) { nextStatus = Time.realtimeSinceStartup + .1f; PublishStatus(); }
        }
        void PublishStatus()
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
            TheatreStatus(JsonUtility.ToJson(ReadStatus()));
            #endif
        }
        void OnDestroy() { Time.timeScale = 1; AudioListener.pause = false; }
    }
}
