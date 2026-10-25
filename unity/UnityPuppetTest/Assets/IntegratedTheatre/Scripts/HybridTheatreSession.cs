using UnityEngine;
using PalmTheatre.DuetMapping;

namespace PalmTheatre.Integration
{
    [DefaultExecutionOrder(200)]
    public sealed class HybridTheatreSession : MonoBehaviour
    {
        public HybridPuppetActor leftActor, rightActor;
        public AudioSource voice;
        public BackgroundMusicPlayer backgroundMusic;
        public bool automaticUpdate = true, enableAudio = true;
        public bool Paused { get; private set; }
        public HybridPuppetActor Speaker { get; private set; }
        public int SpokenCount { get; private set; }
        public int ActorCount => rightActor ? 2 : 1;
        public string Subtitle => Speaker && Speaker.ActiveAction != null ? Speaker.ActiveAction.line.subtitle : "";
        public string SpeakerName => Speaker ? Speaker.role : "";
        public float VoiceRemaining => Speaker && Speaker.ActiveAction != null ?
            Mathf.Max(0, Speaker.ActiveAction.line.voice.length - voiceElapsed) : 0;
        public string Error => !leftActor ? "Hybrid left actor is missing." :
            !string.IsNullOrEmpty(leftActor.Error) ? leftActor.Error : rightActor ? rightActor.Error : "";
        long order;
        float voiceElapsed;

        void Start() { Initialize(); }

        public bool Initialize()
        {
            if (!voice || !leftActor || !leftActor.Initialize()) return false;
            if (rightActor && !rightActor.Initialize()) return false;
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0;
            return true;
        }

        public bool AcceptSolo(PuppetControlState state)
        {
            if (rightActor || !HybridEventGate.ValidControls(state)) return false;
            return Accept(leftActor, 1, true, state);
        }

        public bool AcceptDuet(HybridDuetState state)
        {
            if (!rightActor || state == null || state.version != 1 ||
                state.controlMode != "duet_hybrid" || !ValidSlot(state.left) || !ValidSlot(state.right)) return false;
            // Each actor rejects its own stale identity without blocking its partner.
            bool left = Accept(leftActor, state.left.ownerId, state.left.ownerHeld, state.left.controls);
            bool right = Accept(rightActor, state.right.ownerId, state.right.ownerHeld, state.right.controls);
            return left || right;
        }

        static bool ValidSlot(DuetMappingHand slot)
        {
            return slot != null && HybridEventGate.ValidControls(slot.controls) &&
                (!slot.ownerHeld || slot.ownerId > 0) && slot.handDetected == slot.controls.handDetected;
        }

        bool Accept(HybridPuppetActor actor, int owner, bool held, PuppetControlState controls)
        {
            if (!actor.Accept(owner, held, controls, out bool changed, out bool newEvent)) return false;
            if ((changed || !held || !controls.handDetected || !controls.calibrated) && Speaker == actor) StopVoice();
            if (newEvent)
            {
                if (Paused) actor.DropEvent();
                else if (actor.TryStart(EstimatedVoiceDelay(), order + 1)) order++;
            }
            return true;
        }

        float EstimatedVoiceDelay()
        {
            float delay = VoiceRemaining;
            if (leftActor.VoiceWaiting) delay += leftActor.ActiveAction.line.voice.length;
            if (rightActor && rightActor.VoiceWaiting) delay += rightActor.ActiveAction.line.voice.length;
            return delay;
        }

        void LateUpdate() { if (automaticUpdate) Tick(Time.deltaTime); }

        public void Tick(float deltaTime)
        {
            if (Paused || deltaTime <= 0) return;
            if (Speaker)
            {
                voiceElapsed += deltaTime;
                if (Speaker.ActiveAction == null ||
                    (voiceElapsed >= Speaker.ActiveAction.line.voice.length && (!enableAudio || !voice.isPlaying)))
                {
                    Speaker.VoiceEnded();
                    StopVoice();
                }
            }
            leftActor.Step(deltaTime);
            if (rightActor) rightActor.Step(deltaTime);
            if (!Speaker)
            {
                HybridPuppetActor next = leftActor.VoiceWaiting ? leftActor : null;
                if (rightActor && rightActor.VoiceWaiting && (!next || rightActor.VoiceOrder < next.VoiceOrder)) next = rightActor;
                if (next)
                {
                    Speaker = next;
                    voiceElapsed = 0;
                    Speaker.VoiceStarted();
                    SpokenCount++;
                    voice.clip = Speaker.ActiveAction.line.voice;
                    if (enableAudio) voice.Play();
                }
            }
            if (backgroundMusic) backgroundMusic.SetVoiceActive(Speaker != null);
        }

        void StopVoice()
        {
            if (voice) voice.Stop();
            Speaker = null;
            voiceElapsed = 0;
        }

        public void SetPaused(bool value)
        {
            Paused = value;
            if (voice) { if (value) voice.Pause(); else voice.UnPause(); }
        }

        public void MarkDisconnected()
        {
            if (Speaker) Speaker.VoiceEnded();
            StopVoice();
            if (leftActor) leftActor.Disconnect();
            if (rightActor) rightActor.Disconnect();
            if (backgroundMusic) backgroundMusic.SetVoiceActive(false);
        }

        public void StopAll()
        {
            StopVoice();
            if (leftActor) leftActor.StopImmediately();
            if (rightActor) rightActor.StopImmediately();
            if (backgroundMusic) backgroundMusic.SetVoiceActive(false);
        }

        void OnDisable() { StopAll(); }
    }
}
