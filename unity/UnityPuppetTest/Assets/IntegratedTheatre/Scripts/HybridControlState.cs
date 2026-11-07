using System;
using System.Collections.Generic;
using UnityEngine;
using PalmTheatre.DuetMapping;
using PalmTheatre.DuetGestures;

namespace PalmTheatre.Integration
{
    [Serializable]
    public sealed class HybridDuetState
    {
        public int version;
        public string controlMode;
        public DuetMappingHand left, right;
    }

    [Flags]
    public enum HybridBodyPart
    {
        None = 0, Pelvis = 1, Torso = 2, Head = 4,
        LeftArm = 8, RightArm = 16, Legs = 32,
        UpperBody = Torso | Head | LeftArm | RightArm,
        FullBody = Pelvis | UpperBody | Legs
    }

    [Serializable]
    public sealed class HybridBone
    {
        public Transform bone;
        public string path;
        public HybridBodyPart part;
        public Quaternion restRotation;
        public Vector3 restPosition;
    }

    [Serializable]
    public sealed class HybridAction
    {
        public string actionId;
        public DuetGestureLine line;
        public HybridBodyPart mask;
        public float cooldown = 3f;
        public float blendIn = .3f, blendOut = .5f, loopBlend = .2f;
        public float poseSmoothing = .025f;
    }

    [Serializable]
    public sealed class HybridPoseStatus
    {
        public Vector3 root, headEuler, spineEuler, leftHand, rightHand, leftTarget, rightTarget;
        public float inputBodyX, inputHeadYaw, inputLeftHandY, inputRightHandY;
    }

    [Serializable]
    public sealed class HybridActorStatus
    {
        public string role, action, mask, phase, error;
        public int ownerId, inputEpoch, actionSequence, startedActionSequence, startedCount, droppedCount;
        public bool ownerHeld, handDetected, calibrated, actionActive, voiceWaiting;
        public float blendWeight;
        public HybridPoseStatus mappingPose;
    }

    // This is event admission, not hand assignment or gesture recognition.
    public sealed class HybridEventGate
    {
        readonly Dictionary<int, int> sequences = new Dictionary<int, int>();
        readonly HashSet<int> retiredOwners = new HashSet<int>();
        public int Epoch { get; private set; } = -1;
        public int Owner { get; private set; }
        public int Sequence { get; private set; }

        public bool Accept(int owner, bool held, PuppetControlState state,
            out bool identityChanged, out bool newEvent)
        {
            identityChanged = false;
            newEvent = false;
            if (!ValidControls(state) || state.inputEpoch < Epoch || (held && owner <= 0)) return false;
            if (state.inputEpoch > Epoch)
            {
                Epoch = state.inputEpoch;
                sequences.Clear();
                retiredOwners.Clear();
                Owner = 0;
                Sequence = 0;
                identityChanged = true;
            }
            if (held && retiredOwners.Contains(owner)) return false;
            int nextOwner = held ? owner : 0;
            if (nextOwner != Owner)
            {
                if (Owner > 0) retiredOwners.Add(Owner);
                Owner = nextOwner;
                identityChanged = true;
            }
            if (!held) { Sequence = 0; return true; }
            sequences.TryGetValue(owner, out int consumed);
            Sequence = consumed;
            if (state.actionSequence > consumed)
            {
                Sequence = state.actionSequence;
                sequences[owner] = Sequence;
                newEvent = true;
            }
            return true;
        }

        public static bool ValidControls(PuppetControlState state)
        {
            return state != null && state.version == 2 && state.controlMode == "hybrid" &&
                state.inputEpoch > 0 && state.actionSequence >= 0;
        }
    }
}
