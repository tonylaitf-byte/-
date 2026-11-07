using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using PalmTheatre.MusicianActions;

namespace PalmTheatre.Integration
{
    [DefaultExecutionOrder(-200)]
    public sealed class HybridPuppetActor : MonoBehaviour
    {
        public string role;
        public PuppetBoneTest mapping;
        public Animator animator;
        public MusicianActionManager musician;
        public HybridBone[] bones;
        public HybridAction[] actions;
        public GameObject instrument;
        public bool showInstrument;
        public float minimumX, maximumX, maximumDepth = .2f;
        public float blendIn = .18f, blendOut = .35f;
        [Range(60f, 360f)] public float handoffAngularSpeed = 180f;

        public readonly HybridEventGate Events = new HybridEventGate();
        public bool ActionActive => active != null;
        public bool VoiceWaiting { get; private set; }
        public bool VoiceFinished { get; private set; }
        public long VoiceOrder { get; private set; }
        public HybridAction ActiveAction => active;
        public string Error { get; private set; } = "";
        public int StartedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int StartedActionSequence { get; private set; }
        public float Elapsed { get; private set; }
        public float Duration { get; private set; }
        public bool Releasing => releasing;
        public float ReleaseDuration => active != null ? exitDuration : blendOut;

        readonly Dictionary<string, float> nextAllowed = new Dictionary<string, float>();
        HybridAction active;
        PuppetControlState input;
        Quaternion[] animated, entryPose, releasePose, visiblePose, loopPose;
        Vector3[] visibleVelocity, releaseVelocity;
        bool[] entrySettling;
        float releaseError, releaseStep, releaseSettledTime;
        Vector3 grounded;
        bool initialized, releasing, ownerHeld;
        float releaseElapsed, clock, stepDelta;
        float entryDuration, exitDuration, loopDuration, loopElapsed;
        int loopCycle;
        bool blendingLoop;
        PlayableGraph graph;
        AnimationPlayableOutput output;
        AnimationClipPlayable clip;
        AnimationClip sampledClip;
        MusicianActionDefinition musicianAction;
        Action composeBody;

        void Awake()
        {
            if (mapping) mapping.externallyDriven = true;
            if (musician)
            {
                musician.automaticUpdate = false;
                musician.enabled = false;
                if (musician.puppet) musician.puppet.enabled = false;
            }
        }

        public bool Initialize()
        {
            if (initialized) return string.IsNullOrEmpty(Error);
            initialized = true;
            if (!mapping || !animator || bones == null || bones.Length == 0 || actions == null)
            {
                Error = "Hybrid actor has incomplete rig bindings.";
                return false;
            }
            foreach (var item in bones)
                if (item == null || !item.bone || item.bone == transform)
                {
                    Error = "Hybrid bone binding is missing or includes StageRoot.";
                    return false;
                }
            animated = new Quaternion[bones.Length];
            entryPose = new Quaternion[bones.Length];
            releasePose = new Quaternion[bones.Length];
            visiblePose = new Quaternion[bones.Length];
            loopPose = new Quaternion[bones.Length];
            visibleVelocity = new Vector3[bones.Length];
            releaseVelocity = new Vector3[bones.Length];
            entrySettling = new bool[bones.Length];
            grounded = transform.localPosition;
            RestoreRestPose();
            mapping.useAutomaticDemo = false;
            mapping.InitializeHybridDrive();
            foreach (var receiver in GetComponents<PuppetHttpReceiver>()) receiver.enabled = false;
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            graph = PlayableGraph.Create(role + " hybrid actions");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            output = AnimationPlayableOutput.Create(graph, role, animator);
            graph.Play();
            RestoreRestPose();
            Capture(visiblePose);
            composeBody = () => Compose(true);
            ApplyInstrumentVisibility();
            return true;
        }

        public bool Accept(int owner, bool held, PuppetControlState state,
            out bool identityChanged, out bool newEvent)
        {
            identityChanged = newEvent = false;
            if (!Initialize() || !Events.Accept(owner, held, state, out identityChanged, out newEvent)) return false;
            if (identityChanged)
            {
                CancelAction();
                nextAllowed.Clear();
                mapping.ClearExternalInput();
            }
            input = state;
            ownerHeld = held;
            if (!held || !state.handDetected || !state.calibrated) CancelAction();
            if (held) mapping.SetControlState(state);
            else mapping.ClearExternalInput();
            return true;
        }

        public bool TryStart(float voiceDelay, long order)
        {
            string id = (input?.action ?? "").Trim().ToUpperInvariant();
            HybridAction definition = Array.Find(actions, item => item != null && item.actionId == id);
            if (!ownerHeld || input == null || !input.handDetected || !input.calibrated ||
                ActionActive || definition == null || definition.mask == HybridBodyPart.None ||
                definition.line == null || !definition.line.voice ||
                (nextAllowed.TryGetValue(id, out float allowed) && clock < allowed))
            {
                DroppedCount++;
                return false;
            }
            AnimationClip animation = definition.line.animation;
            if (musician)
            {
                musicianAction = Array.Find(musician.actions, item => item && item.actionId == definition.line.actionId);
                if (!musicianAction || !musicianAction.mainClip ||
                    (musicianAction.continuous && !musicianAction.loopClip))
                { DroppedCount++; return false; }
                animation = musicianAction.mainClip;
                Duration = Mathf.Max(musicianAction.mainClip.length, musicianAction.ResolveDuration(definition.line.voice.length + voiceDelay),
                    definition.line.voice.length + voiceDelay + .3f);
            }
            else
            {
                if (!animation) { DroppedCount++; return false; }
                Duration = Mathf.Max(definition.line.animation.length, definition.line.voice.length + voiceDelay + .3f);
            }
            Array.Copy(visiblePose, entryPose, bones.Length);
            Array.Clear(entrySettling, 0, entrySettling.Length);
            SetClip(animation);
            active = definition;
            entryDuration = Mathf.Max(.08f, definition.blendIn > 0 ? definition.blendIn : blendIn);
            exitDuration = Mathf.Max(.08f, definition.blendOut > 0 ? definition.blendOut : blendOut);
            loopDuration = Mathf.Clamp(definition.loopBlend, .08f, .4f);
            loopCycle = -1;
            blendingLoop = false;
            Elapsed = 0;
            releasing = false;
            releaseElapsed = 0;
            VoiceWaiting = true;
            VoiceFinished = false;
            VoiceOrder = order;
            StartedActionSequence = Events.Sequence;
            StartedCount++;
            nextAllowed[id] = clock + Mathf.Max(0, definition.cooldown);
            ApplyInstrumentVisibility();
            return true;
        }

        public void VoiceStarted() { VoiceWaiting = false; }
        public void VoiceEnded() { VoiceWaiting = false; VoiceFinished = true; }
        public void DropEvent() { DroppedCount++; }

        public void Disconnect()
        {
            if (!mapping) return;
            input = null;
            ownerHeld = false;
            mapping.ClearExternalInput();
            CancelAction();
        }

        public void CancelAction()
        {
            VoiceWaiting = false;
            VoiceFinished = true;
            if (active == null || releasing) return;
            BeginRelease();
        }

        void BeginRelease()
        {
            releasing = true;
            releaseElapsed = 0;
            releaseSettledTime = 0;
            Array.Copy(visiblePose, releasePose, bones.Length);
            // u*(1-u)^3 peaks at 27/256: cap the carried motion at 15 degrees.
            float limit = 15f * 256f / (27f * Mathf.Max(.08f, exitDuration));
            for (int i = 0; i < bones.Length; i++) releaseVelocity[i] = Vector3.ClampMagnitude(visibleVelocity[i], limit);
        }

        void SetClip(AnimationClip animation)
        {
            if (clip.IsValid()) graph.DestroyPlayable(clip);
            sampledClip = animation;
            clip = AnimationClipPlayable.Create(graph, animation);
            clip.SetSpeed(0);
            output.SetSourcePlayable(clip);
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= 0 || !Initialize()) return;
            stepDelta = deltaTime;
            clock += deltaTime;
            RestoreRestPose();
            if (active != null && !releasing)
            {
                Elapsed += deltaTime;
                if (musician)
                {
                    // Sample action/loop clips only; the legacy controller's exit and Idle
                    // transitions must never become the source of the mapping release.
                    float time;
                    if (musicianAction.continuous && Elapsed >= musicianAction.fallbackDuration)
                    {
                        int cycle = Mathf.FloorToInt((Elapsed - musicianAction.fallbackDuration) / musicianAction.loopClip.length);
                        if (sampledClip != musicianAction.loopClip || cycle != loopCycle)
                        {
                            Array.Copy(visiblePose, loopPose, bones.Length);
                            loopElapsed = 0;
                            blendingLoop = true;
                            loopCycle = cycle;
                            if (sampledClip != musicianAction.loopClip) SetClip(musicianAction.loopClip);
                        }
                        time = (Elapsed - musicianAction.fallbackDuration) % sampledClip.length;
                    }
                    else time = musicianAction.continuous ? Mathf.Min(Elapsed, sampledClip.length) :
                        HybridMotionTiming.ClipTime(Elapsed, Duration, sampledClip.length, musicianAction.holdStart, musicianAction.holdEnd);
                    clip.SetTime(Mathf.Clamp(time, 0, sampledClip.length));
                    graph.Evaluate(0);
                }
                else
                {
                    float length = active.line.animation.length;
                    float time = HybridMotionTiming.ClipTime(Elapsed, Duration, length, length * .25f, length * .7f);
                    clip.SetTime(Mathf.Clamp(time, 0, length));
                    graph.Evaluate(0);
                }
                Capture(animated);
                if (blendingLoop)
                {
                    loopElapsed += deltaTime;
                    float weight = HybridMotionTiming.Ease(loopElapsed / loopDuration);
                    for (int i = 0; i < bones.Length; i++) animated[i] = Quaternion.Slerp(loopPose[i], animated[i], weight);
                    if (loopElapsed >= loopDuration) blendingLoop = false;
                }
                if (Elapsed >= Duration && VoiceFinished) BeginRelease();
            }
            if (releasing) releaseElapsed += deltaTime;
            // No animation curve outside the explicit mask reaches the visible rig.
            RestoreRestPose();
            releaseError = 0;
            releaseStep = 0;
            mapping.EvaluateHybridPose(deltaTime, composeBody,
                active == null || releasing || (active.mask & HybridBodyPart.LeftArm) == 0,
                active == null || releasing || (active.mask & HybridBodyPart.RightArm) == 0);
            Compose(false);
            Vector3 position = transform.localPosition;
            position.x = Mathf.Clamp(position.x, minimumX, maximumX);
            position.y = grounded.y;
            position.z = Mathf.Clamp(position.z, grounded.z - maximumDepth, grounded.z + maximumDepth);
            transform.localPosition = position;
            CaptureVisible(deltaTime);
            if (releasing)
            {
                bool following = releaseElapsed >= exitDuration && releaseError <= .05f &&
                    releaseStep <= Mathf.Max(60f, handoffAngularSpeed) * deltaTime * .75f;
                releaseSettledTime = following ? releaseSettledTime + deltaTime : 0;
            }
            if (releasing && releaseSettledTime >= .15f)
            {
                active = null;
                releasing = false;
                ApplyInstrumentVisibility();
            }
        }

        void Compose(bool body)
        {
            if (active == null) return;
            float t = HybridMotionTiming.Ease(releasing ? releaseElapsed / exitDuration : Elapsed / entryDuration);
            for (int i = 0; i < bones.Length; i++)
            {
                var item = bones[i];
                bool isBody = (item.part & (HybridBodyPart.Pelvis | HybridBodyPart.Torso | HybridBodyPart.Head)) != 0;
                if (body != isBody || (active.mask & item.part) == 0) continue;
                Quaternion target = releasing ?
                    Quaternion.Slerp(releasePose[i], item.bone.localRotation, t) :
                    Quaternion.Slerp(entryPose[i], animated[i], t);
                if (releasing)
                {
                    float remaining = 1 - Mathf.Clamp01(releaseElapsed / exitDuration);
                    Vector3 drift = releaseVelocity[i] * (releaseElapsed * remaining * remaining * remaining);
                    if (drift.sqrMagnitude > .000001f) target = Quaternion.AngleAxis(drift.magnitude, drift.normalized) * target;
                }
                // Filter only animation-owned bones; finish exactly on the current mapping pose.
                float seconds = releasing ? 0 : Mathf.Clamp(active.poseSmoothing, 0, .06f);
                float gain = seconds > .0001f ? 1 - Mathf.Exp(-stepDelta / seconds) : 1;
                Quaternion composed = Quaternion.Slerp(visiblePose[i], target, gain);
                Quaternion filteredTarget = composed;
                // A moving live target can outrun the timed crossfade. Keep ownership
                // until the visible pose catches up, instead of snapping on timeout.
                if (releasing || Elapsed <= entryDuration || entrySettling[i])
                {
                    composed = Quaternion.RotateTowards(visiblePose[i], composed,
                        Mathf.Max(60f, handoffAngularSpeed) * stepDelta);
                    float error = Quaternion.Angle(composed, releasing ? target : filteredTarget);
                    if (releasing)
                    {
                        releaseError = Mathf.Max(releaseError, error);
                        releaseStep = Mathf.Max(releaseStep, Quaternion.Angle(visiblePose[i], composed));
                    }
                    else entrySettling[i] = error > .05f;
                }
                item.bone.localRotation = composed;
            }
        }

        void RestoreRestPose()
        {
            foreach (var item in bones)
            {
                item.bone.localRotation = item.restRotation;
                item.bone.localPosition = item.restPosition;
            }
        }

        void Capture(Quaternion[] target)
        {
            for (int i = 0; i < bones.Length; i++) target[i] = bones[i].bone.localRotation;
        }

        void CaptureVisible(float deltaTime)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion current = bones[i].bone.localRotation;
                Quaternion change = current * Quaternion.Inverse(visiblePose[i]);
                if (change.w < 0) change = new Quaternion(-change.x, -change.y, -change.z, -change.w);
                change.ToAngleAxis(out float angle, out Vector3 axis);
                visibleVelocity[i] = angle > .001f && axis.sqrMagnitude > .5f ? axis * (angle / deltaTime) : Vector3.zero;
                visiblePose[i] = current;
            }
        }

        public void SetInstrumentVisible(bool visible)
        {
            showInstrument = visible;
            ApplyInstrumentVisibility();
        }

        void ApplyInstrumentVisibility()
        {
            if (instrument) instrument.SetActive(showInstrument || ActionActive);
        }

        public void StopImmediately()
        {
            active = null;
            releasing = false;
            VoiceWaiting = false;
            VoiceFinished = true;
            Disconnect();
            ApplyInstrumentVisibility();
        }

        public HybridActorStatus ReadStatus()
        {
            return new HybridActorStatus {
                role = role, ownerId = Events.Owner, inputEpoch = Mathf.Max(0, Events.Epoch),
                actionSequence = Events.Sequence, startedActionSequence = StartedActionSequence,
                startedCount = StartedCount, droppedCount = DroppedCount,
                ownerHeld = ownerHeld, handDetected = input != null && input.handDetected,
                calibrated = input != null && input.calibrated, actionActive = ActionActive,
                voiceWaiting = VoiceWaiting, action = active?.actionId ?? "IDLE",
                mask = active?.mask.ToString() ?? "None", error = Error,
                phase = releasing ? "Release" : active == null ? "Mapping" : "Action",
                blendWeight = active == null ? 0 : releasing ?
                    1 - HybridMotionTiming.Ease(releaseElapsed / exitDuration) :
                    HybridMotionTiming.Ease(Elapsed / entryDuration),
                mappingPose = new HybridPoseStatus {
                    root = transform.localPosition,
                    headEuler = mapping && mapping.head ? mapping.head.localEulerAngles : Vector3.zero,
                    spineEuler = mapping && mapping.spine ? mapping.spine.localEulerAngles : Vector3.zero,
                    leftHand = mapping && mapping.leftHand ? mapping.leftHand.position : Vector3.zero,
                    rightHand = mapping && mapping.rightHand ? mapping.rightHand.position : Vector3.zero,
                    leftTarget = mapping ? mapping.CurrentLeftHandTarget : Vector3.zero,
                    rightTarget = mapping ? mapping.CurrentRightHandTarget : Vector3.zero,
                    inputBodyX = input?.bodyX ?? 0, inputHeadYaw = input?.headYaw ?? 0,
                    inputLeftHandY = input?.leftHandY ?? 0, inputRightHandY = input?.rightHandY ?? 0
                }
            };
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
