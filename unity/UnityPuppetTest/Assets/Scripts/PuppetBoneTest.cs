using System;
using UnityEngine;

[Serializable]
public sealed class PuppetControlState
{
    public int version;
    public bool handDetected;
    public bool calibrated;
    public float calibrationProgress;
    public int sequenceId;
    public float sampleDt;
    public float leftArmVelocity;
    public float rightArmVelocity;

    // Legacy scalar controls remain readable for older senders.
    public float headNod;
    public float leftArm;
    public float rightArm;
    public float bodyTurn;
    public float moveX;

    // V2 continuous glove-puppet controls.
    public float leftHandX;
    public float leftHandY;
    public float leftHandZ;
    public float rightHandX;
    public float rightHandY;
    public float rightHandZ;
    public float headPitch;
    public float headYaw;
    public float headRoll;
    public float bodyX;
    public float bodyY;
    public float bodyDepth;
    public float bodyLean;
    public string controlMode;
    public int gesture;
    public float gestureHold;
    public string detectedAction;
    public string action;
    public int actionSequence;
}

public sealed class PuppetBoneTest : MonoBehaviour
{
    [Header("Rig")]
    public Transform head;
    public Transform spine;
    public Transform leftShoulder;
    public Transform leftArm;
    public Transform leftForeArm;
    public Transform leftHand;
    public Transform rightShoulder;
    public Transform rightArm;
    public Transform rightForeArm;
    public Transform rightHand;

    [Header("Continuous IK Range")]
    [Range(0f, 1f)] public float handHorizontalRange = 0.55f;
    [Range(0f, 1.5f)] public float handVerticalRange = 0.95f;
    [Range(0f, 1.5f)] public float handDepthRange = 1.15f;
    [Range(-1f, 1f)] public float leftHandDepthDirection = -1f;
    [Range(-1f, 1f)] public float rightHandDepthDirection = 1f;
    [Range(0.5f, 0.999f)] public float maximumReach = 0.95f;
    [Range(1f, 180f)] public float shoulderLimitDegrees = 110f;
    [Range(1f, 180f)] public float elbowLimitDegrees = 155f;

    [Header("Head And Body Range")]
    public float headPitchDegrees = 42f;
    public float headYawDegrees = 48f;
    public float headRollDegrees = 30f;
    public float bodyTurnDegrees = 30f;
    public float bodyLeanDegrees = 18f;
    public float horizontalMove = 1.25f;
    [Min(0f)] public float gestureHorizontalMove = 3.0f;
    public float verticalMove = 0.55f;
    public float depthMove = 0.55f;
    public float responseSpeed = 14f;
    [Tooltip("Keeps the puppet root at its calibrated floor height. Disable only when intentional jumping or hovering is needed.")]
    public bool lockFeetToGround = true;

    [Header("Puppet Animation Layer")]
    [Min(0.01f)] public float handSmoothTime = 0.10f;
    [Min(0.01f)] public float bodySmoothTime = 0.14f;
    [Min(0.01f)] public float headSmoothTime = 0.20f;
    [Min(0f)] public float maximumInputSpeed = 8f;
    [Min(0f)] public float maximumBodySpeed = 4f;

    [Header("Basic Puppet Arms")]
    [Range(10f, 20f)] public float idleArmDropDegrees = 15f;
    [Range(0.86f, 0.96f)] public float idleArmReach = 0.90f;
    [Range(0f, 0.10f)] public float forwardReachExtension = 0.05f;
    [Range(0f, 1f)] public float preferredElbowPoleWeight = 0.70f;
    [Range(0f, 1f)] public float elbowDownBias = 0.80f;
    [Range(0f, 1f)] public float elbowBackBias = 0.25f;
    [Range(0f, 1f)] public float palmArmHorizontalRange = 0.35f;
    [Range(0f, 1f)] public float palmArmVerticalRange = 0.45f;
    [Range(0f, 1f)] public float palmArmDepthRange = 0.40f;
    [Range(0f, 0.08f)] public float palmForwardReachExtension = 0.03f;

    [Header("Palm-Center Forward Gesture")]
    [Range(0f, 0.8f)] public float centerGestureStart = 0.42f;
    [Range(0f, 0.8f)] public float centerGestureExit = 0.30f;
    [Range(0.2f, 1f)] public float centerGestureFull = 0.95f;
    [Range(1f, 20f)] public float centerSpringFrequency = 6f;
    [Range(1f, 20f)] public float centerFastSpringFrequency = 10.5f;
    [Range(0.5f, 1.5f)] public float centerSpringDamping = 1.02f;
    [Range(0.5f, 1.5f)] public float centerFastSpringDamping = 0.92f;
    [Range(0f, 1f)] public float centerVelocityFeedForward = 0.20f;
    [Range(0f, 0.08f)] public float centerPredictionTime = 0.04f;
    [Range(0f, 0.25f)] public float centerMaximumPrediction = 0.10f;
    [Range(0.1f, 2f)] public float centerMinimumEnterSpeed = 0.65f;
    [Range(0.1f, 2f)] public float centerMinimumExitSpeed = 0.80f;
    [Range(0.5f, 5f)] public float centerMaximumEnterSpeed = 2.4f;
    [Range(0.5f, 5f)] public float centerMaximumExitSpeed = 2.6f;
    [Range(1f, 40f)] public float centerMaximumEnterAcceleration = 16f;
    [Range(1f, 40f)] public float centerMaximumExitAcceleration = 20f;
    [Range(10f, 300f)] public float centerMaximumJerk = 140f;
    [Range(0.2f, 10f)] public float centerFastInputSpeed = 7f;
    [Range(0.2f, 6f)] public float centerMotionBoostDecay = 1.3f;
    [Range(0f, 0.3f)] public float centerForwardHandSeparation = 0.07f;
    [Range(0f, 0.2f)] public float centerForwardDrop = 0.03f;
    [Range(0.9f, 0.999f)] public float centerForwardReach = 0.98f;
    [Range(0f, 0.25f)] public float headMotionHandDeadZone = 0.045f;
    public bool holdLastPoseWhenHandLost = true;

    [Header("Legacy And Fallback")]
    public float headNodDegrees = 18f;
    public float armRaiseDegrees = 45f;
    public bool useAutomaticDemo;
    public float demoSpeed = 1.5f;

    public bool ContinuousIkMode { get; private set; }
    public Vector3 CurrentLeftHandTarget { get; private set; }
    public Vector3 CurrentRightHandTarget { get; private set; }
    public float CurrentLeftCenterForward => currentLeftCenterForward;
    public float CurrentRightCenterForward => currentRightCenterForward;
    public float CurrentLeftCenterVelocity => currentLeftCenterVelocity;
    public float CurrentRightCenterVelocity => currentRightCenterVelocity;

    private Quaternion headRest;
    private Quaternion spineRest;
    private Quaternion leftShoulderRest;
    private Quaternion leftArmRest;
    private Quaternion leftForeArmRest;
    private Quaternion leftHandRest;
    private Quaternion rightArmRest;
    private Quaternion rightForeArmRest;
    private Quaternion rightHandRest;
    private Quaternion rightShoulderRest;
    private Vector3 rootRestPosition;
    private Vector3 leftPoleInParent;
    private Vector3 rightPoleInParent;
    private float leftArmLength;
    private float rightArmLength;

    private Vector3 targetLeftHand;
    private Vector3 targetRightHand;
    private Vector3 currentLeftHand;
    private Vector3 currentRightHand;
    private Vector3 leftHandVelocity;
    private Vector3 rightHandVelocity;
    private Vector3 targetBodyPosition;
    private Vector3 currentBodyPosition;
    private Vector3 bodyPositionVelocity;
    private Vector3 targetHead;
    private Vector3 currentHead;
    private Vector3 headVelocity;
    private float targetBodyTurn;
    private float currentBodyTurn;
    private float bodyTurnVelocity;
    private float targetBodyLean;
    private float currentBodyLean;
    private float bodyLeanVelocity;

    private float targetLegacyHeadNod;
    private float targetLegacyLeftArm;
    private float targetLegacyRightArm;
    private float currentLegacyHeadNod;
    private float currentLegacyLeftArm;
    private float currentLegacyRightArm;
    private float legacyHeadVelocity;
    private float legacyLeftArmVelocity;
    private float legacyRightArmVelocity;
    private float targetLeftCenterForward;
    private float targetRightCenterForward;
    private float currentLeftCenterForward;
    private float currentRightCenterForward;
    private float targetLeftCenterVelocity;
    private float targetRightCenterVelocity;
    private float currentLeftCenterVelocity;
    private float currentRightCenterVelocity;
    private float currentLeftCenterAcceleration;
    private float currentRightCenterAcceleration;
    private float leftCenterMotionBoost;
    private float rightCenterMotionBoost;
    private bool leftCenterGestureActive;
    private bool rightCenterGestureActive;
    private int lastGestureSequenceId = -1;
    private float lastGestureSampleTime = float.NegativeInfinity;
    private float lastGestureSampleDt = 1f / 30f;
    private bool hasExternalInput;
    private bool hasActivePuppetPose;
    private Vector3 previousLeftHandInput;
    private Vector3 previousRightHandInput;
    private bool hasPreviousInputSample;
    private bool gestureModeActive;
    private bool suspendedForAnimation;
    private bool animationHandoffActive;
    private float animationHandoffStartTime;
    private float animationHandoffDuration;
    private Vector3 animationHandoffRootPosition;
    private Quaternion animationHandoffHead;
    private Quaternion animationHandoffSpine;
    private Quaternion animationHandoffLeftShoulder;
    private Quaternion animationHandoffLeftArm;
    private Quaternion animationHandoffLeftForeArm;
    private Quaternion animationHandoffLeftHand;
    private Quaternion animationHandoffRightShoulder;
    private Quaternion animationHandoffRightArm;
    private Quaternion animationHandoffRightForeArm;
    private Quaternion animationHandoffRightHand;

    private void Awake()
    {
        Application.runInBackground = true;
        ResolveRig();
        CacheRestPose();
        ContinuousIkMode = true;

        if (GetComponent<PuppetHttpReceiver>() == null)
            gameObject.AddComponent<PuppetHttpReceiver>();
    }

    private void Start()
    {
        // Imported FBX hierarchies can finish their activation after Awake.
        // Bind a second time here so a replaced visual model cannot leave the
        // live arm IK pointing at missing transforms.
        ResolveRig();
        CacheRestPose();
    }

    public void SetControlState(PuppetControlState state)
    {
        hasExternalInput = true;
        if (state == null)
        {
            SetNeutralTargets();
            return;
        }

        gestureModeActive = string.Equals(
            state.controlMode,
            "gesture",
            StringComparison.OrdinalIgnoreCase
        );

        ContinuousIkMode = state.version >= 2;
        bool active = state.handDetected && (!ContinuousIkMode || state.calibrated);
        if (!active)
        {
            if (ContinuousIkMode && holdLastPoseWhenHandLost &&
                hasActivePuppetPose && !state.handDetected)
                return;

            SetNeutralTargets();
            return;
        }

        hasActivePuppetPose = true;
        if (ContinuousIkMode)
        {
            Vector3 incomingLeftHand = ClampVector(
                state.leftHandX,
                state.leftHandY,
                state.leftHandZ
            );
            Vector3 incomingRightHand = ClampVector(
                state.rightHandX,
                state.rightHandY,
                state.rightHandZ
            );
            Vector3 incomingHead = ClampVector(
                state.headPitch,
                state.headYaw,
                state.headRoll
            );
            bool freshGestureSample = state.sequenceId <= 0 ||
                state.sequenceId != lastGestureSequenceId;
            if (freshGestureSample)
            {
                targetLeftCenterForward = CenterGestureTarget(
                    state.leftArm,
                    ref leftCenterGestureActive
                );
                targetRightCenterForward = CenterGestureTarget(
                    state.rightArm,
                    ref rightCenterGestureActive
                );
                targetLeftCenterVelocity = CenterGestureVelocity(
                    state.leftArmVelocity,
                    leftCenterGestureActive
                );
                targetRightCenterVelocity = CenterGestureVelocity(
                    state.rightArmVelocity,
                    rightCenterGestureActive
                );
                leftCenterMotionBoost = Mathf.Max(
                    leftCenterMotionBoost,
                    Mathf.Clamp01(
                        Mathf.Abs(targetLeftCenterVelocity) /
                        Mathf.Max(centerFastInputSpeed, 0.01f)
                    )
                );
                rightCenterMotionBoost = Mathf.Max(
                    rightCenterMotionBoost,
                    Mathf.Clamp01(
                        Mathf.Abs(targetRightCenterVelocity) /
                        Mathf.Max(centerFastInputSpeed, 0.01f)
                    )
                );
                lastGestureSequenceId = state.sequenceId;
                lastGestureSampleTime = Time.realtimeSinceStartup;
                lastGestureSampleDt = Mathf.Clamp(state.sampleDt, 1f / 240f, 0.25f);
            }
            bool headOnlyMotion = hasPreviousInputSample &&
                incomingHead.magnitude > 0.06f &&
                Vector3.Distance(incomingLeftHand, previousLeftHandInput) < headMotionHandDeadZone &&
                Vector3.Distance(incomingRightHand, previousRightHandInput) < headMotionHandDeadZone;
            if (!headOnlyMotion)
            {
                targetLeftHand = incomingLeftHand;
                targetRightHand = incomingRightHand;
                targetBodyTurn = SafeClamp(state.bodyTurn, -1f, 1f);
                targetBodyPosition = ClampVector(state.bodyX, state.bodyY, state.bodyDepth);
                targetBodyLean = SafeClamp(state.bodyLean, -1f, 1f);
            }

            targetHead = incomingHead;
            targetLegacyHeadNod = 0f;
            targetLegacyLeftArm = 0f;
            targetLegacyRightArm = 0f;
            previousLeftHandInput = incomingLeftHand;
            previousRightHandInput = incomingRightHand;
            hasPreviousInputSample = true;
        }
        else
        {
            targetLegacyHeadNod = SafeClamp01(state.headNod);
            targetLegacyLeftArm = SafeClamp01(state.leftArm);
            targetLegacyRightArm = SafeClamp01(state.rightArm);
            targetLeftCenterForward = 0f;
            targetRightCenterForward = 0f;
            targetLeftCenterVelocity = 0f;
            targetRightCenterVelocity = 0f;
            targetBodyPosition = new Vector3(SafeClamp(state.moveX, -1f, 1f), 0f, 0f);
            targetLeftHand = Vector3.zero;
            targetRightHand = Vector3.zero;
            targetHead = Vector3.zero;
            targetBodyLean = 0f;
            hasPreviousInputSample = false;
        }
    }

    public void ClearExternalInput()
    {
        hasExternalInput = false;
        SetNeutralTargets();
    }

    public void SuspendForAnimation()
    {
        animationHandoffActive = false;
        suspendedForAnimation = true;
    }

    public void ResumeFromAnimation(float blendDuration)
    {
        animationHandoffRootPosition = transform.localPosition;
        animationHandoffHead = LocalRotationOrIdentity(head);
        animationHandoffSpine = LocalRotationOrIdentity(spine);
        animationHandoffLeftShoulder = LocalRotationOrIdentity(leftShoulder);
        animationHandoffLeftArm = LocalRotationOrIdentity(leftArm);
        animationHandoffLeftForeArm = LocalRotationOrIdentity(leftForeArm);
        animationHandoffLeftHand = LocalRotationOrIdentity(leftHand);
        animationHandoffRightShoulder = LocalRotationOrIdentity(rightShoulder);
        animationHandoffRightArm = LocalRotationOrIdentity(rightArm);
        animationHandoffRightForeArm = LocalRotationOrIdentity(rightForeArm);
        animationHandoffRightHand = LocalRotationOrIdentity(rightHand);
        animationHandoffDuration = Mathf.Max(0.01f, blendDuration);
        animationHandoffStartTime = Time.unscaledTime;
        animationHandoffActive = true;
        suspendedForAnimation = false;
    }

    private void LateUpdate()
    {
        if (!hasExternalInput && useAutomaticDemo)
        {
            ContinuousIkMode = false;
            float wave = (Mathf.Sin(Time.time * demoSpeed) + 1f) * 0.5f;
            targetLegacyHeadNod = wave;
            targetLegacyLeftArm = wave;
            targetLegacyRightArm = 1f - wave;
            targetBodyTurn = Mathf.Sin(Time.time * demoSpeed) * 0.4f;
        }

        float responseTime = 1f / Mathf.Max(responseSpeed, 0.01f);
        float handTime = Mathf.Max(handSmoothTime, responseTime * 0.5f);
        float bodyTime = Mathf.Max(bodySmoothTime, responseTime);
        float headTime = Mathf.Max(headSmoothTime, responseTime * 1.25f);

        // Mecanim owns the animated pose while an action is playing, but the
        // gesture-controlled root translation must remain live. Updating only
        // the horizontal channel avoids fighting the action's bone animation.
        if (suspendedForAnimation)
        {
            float targetHorizontal = targetBodyPosition.x;
            float currentHorizontal = Mathf.SmoothDamp(
                currentBodyPosition.x,
                targetHorizontal,
                ref bodyPositionVelocity.x,
                bodyTime,
                Mathf.Max(maximumBodySpeed, 0.01f),
                Time.deltaTime
            );
            currentBodyPosition.x = currentHorizontal;
            ApplyHorizontalMovementOnly();
            return;
        }

        float gestureSampleTimeout = Mathf.Max(lastGestureSampleDt * 2.5f, 0.08f);
        if (Time.realtimeSinceStartup - lastGestureSampleTime > gestureSampleTimeout)
        {
            targetLeftCenterVelocity = Mathf.MoveTowards(
                targetLeftCenterVelocity,
                0f,
                Time.unscaledDeltaTime * 12f
            );
            targetRightCenterVelocity = Mathf.MoveTowards(
                targetRightCenterVelocity,
                0f,
                Time.unscaledDeltaTime * 12f
            );
        }
        currentLeftHand = Vector3.SmoothDamp(
            currentLeftHand, targetLeftHand, ref leftHandVelocity, handTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );
        currentRightHand = Vector3.SmoothDamp(
            currentRightHand, targetRightHand, ref rightHandVelocity, handTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );
        UpdateCenterGestureServo(
            ref currentLeftCenterForward,
            ref currentLeftCenterVelocity,
            ref currentLeftCenterAcceleration,
            ref leftCenterMotionBoost,
            targetLeftCenterForward,
            targetLeftCenterVelocity
        );
        UpdateCenterGestureServo(
            ref currentRightCenterForward,
            ref currentRightCenterVelocity,
            ref currentRightCenterAcceleration,
            ref rightCenterMotionBoost,
            targetRightCenterForward,
            targetRightCenterVelocity
        );
        currentHead = Vector3.SmoothDamp(
            currentHead, targetHead, ref headVelocity, headTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );
        currentBodyPosition = Vector3.SmoothDamp(
            currentBodyPosition, targetBodyPosition, ref bodyPositionVelocity, bodyTime,
            Mathf.Max(maximumBodySpeed, 0.01f), Time.deltaTime
        );
        currentBodyTurn = Mathf.SmoothDamp(
            currentBodyTurn, targetBodyTurn, ref bodyTurnVelocity, bodyTime,
            Mathf.Max(maximumBodySpeed, 0.01f), Time.deltaTime
        );
        currentBodyLean = Mathf.SmoothDamp(
            currentBodyLean, targetBodyLean, ref bodyLeanVelocity, bodyTime,
            Mathf.Max(maximumBodySpeed, 0.01f), Time.deltaTime
        );
        currentLegacyHeadNod = Mathf.SmoothDamp(
            currentLegacyHeadNod, targetLegacyHeadNod, ref legacyHeadVelocity, headTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );
        currentLegacyLeftArm = Mathf.SmoothDamp(
            currentLegacyLeftArm, targetLegacyLeftArm, ref legacyLeftArmVelocity, handTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );
        currentLegacyRightArm = Mathf.SmoothDamp(
            currentLegacyRightArm, targetLegacyRightArm, ref legacyRightArmVelocity, handTime,
            Mathf.Max(maximumInputSpeed, 0.01f), Time.deltaTime
        );

        ApplyBodyAndHead();
        ResetArmPose();

        if (ContinuousIkMode)
            ApplyContinuousIk();
        else
            ApplyLegacyArms();

        ApplyAnimationHandoff();
    }

    private void ApplyAnimationHandoff()
    {
        if (!animationHandoffActive)
            return;

        float elapsed = Time.unscaledTime - animationHandoffStartTime;
        float blend = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.Clamp01(elapsed / animationHandoffDuration)
        );
        transform.localPosition = Vector3.Lerp(
            animationHandoffRootPosition,
            transform.localPosition,
            blend
        );
        BlendLocalRotation(head, animationHandoffHead, blend);
        BlendLocalRotation(spine, animationHandoffSpine, blend);
        BlendLocalRotation(leftShoulder, animationHandoffLeftShoulder, blend);
        BlendLocalRotation(leftArm, animationHandoffLeftArm, blend);
        BlendLocalRotation(leftForeArm, animationHandoffLeftForeArm, blend);
        BlendLocalRotation(leftHand, animationHandoffLeftHand, blend);
        BlendLocalRotation(rightShoulder, animationHandoffRightShoulder, blend);
        BlendLocalRotation(rightArm, animationHandoffRightArm, blend);
        BlendLocalRotation(rightForeArm, animationHandoffRightForeArm, blend);
        BlendLocalRotation(rightHand, animationHandoffRightHand, blend);

        if (blend >= 1f)
            animationHandoffActive = false;
    }

    private void ResolveRig()
    {
        head ??= FindBone("Head");
        spine ??= FindBone("Spine");
        leftShoulder ??= FindBone("LeftShoulder");
        leftArm ??= FindBone("LeftArm");
        leftForeArm ??= FindBone("LeftForeArm");
        leftHand ??= FindBone("LeftHand");
        rightShoulder ??= FindBone("RightShoulder");
        rightArm ??= FindBone("RightArm");
        rightForeArm ??= FindBone("RightForeArm");
        rightHand ??= FindBone("RightHand");
    }

    private void CacheRestPose()
    {
        headRest = LocalRotationOrIdentity(head);
        spineRest = LocalRotationOrIdentity(spine);
        leftShoulderRest = LocalRotationOrIdentity(leftShoulder);
        leftArmRest = LocalRotationOrIdentity(leftArm);
        leftForeArmRest = LocalRotationOrIdentity(leftForeArm);
        leftHandRest = LocalRotationOrIdentity(leftHand);
        rightArmRest = LocalRotationOrIdentity(rightArm);
        rightForeArmRest = LocalRotationOrIdentity(rightForeArm);
        rightHandRest = LocalRotationOrIdentity(rightHand);
        rightShoulderRest = LocalRotationOrIdentity(rightShoulder);
        rootRestPosition = transform.localPosition;

        leftArmLength = ChainLength(leftArm, leftForeArm, leftHand);
        rightArmLength = ChainLength(rightArm, rightForeArm, rightHand);
        leftPoleInParent = CalculatePoleInParent(leftArm, leftForeArm, leftHand);
        rightPoleInParent = CalculatePoleInParent(rightArm, rightForeArm, rightHand);
    }

    private void SetNeutralTargets()
    {
        targetLeftHand = Vector3.zero;
        targetRightHand = Vector3.zero;
        targetHead = Vector3.zero;
        targetBodyPosition = Vector3.zero;
        targetBodyTurn = 0f;
        targetBodyLean = 0f;
        targetLegacyHeadNod = 0f;
        targetLegacyLeftArm = 0f;
        targetLegacyRightArm = 0f;
        targetLeftCenterForward = 0f;
        targetRightCenterForward = 0f;
        targetLeftCenterVelocity = 0f;
        targetRightCenterVelocity = 0f;
        leftCenterMotionBoost = 0f;
        rightCenterMotionBoost = 0f;
        leftCenterGestureActive = false;
        rightCenterGestureActive = false;
    }

    private void ApplyBodyAndHead()
    {
        // The resting pose is authored with the puppet's feet on the courtyard.
        // Browser camera height is useful for gestures, but must not translate
        // the whole rig vertically or the feet will float/sink through the floor.
        float verticalOffset = lockFeetToGround
            ? 0f
            : currentBodyPosition.y * verticalMove;
        float activeHorizontalMove = gestureModeActive
            ? gestureHorizontalMove
            : horizontalMove;
        transform.localPosition = rootRestPosition + new Vector3(
            currentBodyPosition.x * activeHorizontalMove,
            verticalOffset,
            currentBodyPosition.z * depthMove
        );

        if (spine != null)
            spine.localRotation = spineRest * Quaternion.Euler(
                0f,
                currentBodyTurn * bodyTurnDegrees,
                -currentBodyLean * bodyLeanDegrees
            );

        if (head == null)
            return;

        head.localRotation = ContinuousIkMode
            ? headRest * Quaternion.Euler(
                currentHead.x * headPitchDegrees,
                currentHead.y * headYawDegrees,
                currentHead.z * headRollDegrees
            )
            : headRest * Quaternion.Euler(currentLegacyHeadNod * headNodDegrees, 0f, 0f);
    }

    private void ApplyHorizontalMovementOnly()
    {
        float activeHorizontalMove = gestureModeActive
            ? gestureHorizontalMove
            : horizontalMove;
        Vector3 animatedPosition = transform.localPosition;
        animatedPosition.x = rootRestPosition.x + currentBodyPosition.x * activeHorizontalMove;
        transform.localPosition = animatedPosition;
    }

    private void ResetArmPose()
    {
        SetLocalRotation(leftShoulder, leftShoulderRest);
        SetLocalRotation(leftArm, leftArmRest);
        SetLocalRotation(leftForeArm, leftForeArmRest);
        SetLocalRotation(leftHand, leftHandRest);
        SetLocalRotation(rightShoulder, rightShoulderRest);
        SetLocalRotation(rightArm, rightArmRest);
        SetLocalRotation(rightForeArm, rightForeArmRest);
        SetLocalRotation(rightHand, rightHandRest);
    }

    private void ApplyContinuousIk()
    {
        CurrentLeftHandTarget = BuildHandTarget(
            leftArm,
            currentLeftHand,
            currentBodyPosition,
            currentLeftCenterForward,
            leftArmLength,
            -1f,
            leftHandDepthDirection,
            out float leftSolveReach
        );
        CurrentRightHandTarget = BuildHandTarget(
            rightArm,
            currentRightHand,
            currentBodyPosition,
            currentRightCenterForward,
            rightArmLength,
            1f,
            rightHandDepthDirection,
            out float rightSolveReach
        );

        SolveTwoBone(
            leftArm,
            leftForeArm,
            leftHand,
            leftArmRest,
            leftForeArmRest,
            leftPoleInParent,
            CurrentLeftHandTarget,
            leftSolveReach
        );

        SolveTwoBone(
            rightArm,
            rightForeArm,
            rightHand,
            rightArmRest,
            rightForeArmRest,
            rightPoleInParent,
            CurrentRightHandTarget,
            rightSolveReach
        );
    }

    private void ApplyLegacyArms()
    {
        if (leftArm != null)
            leftArm.localRotation = leftArmRest * Quaternion.Euler(
                0f,
                0f,
                currentLegacyLeftArm * armRaiseDegrees
            );
        if (rightArm != null)
            rightArm.localRotation = rightArmRest * Quaternion.Euler(
                0f,
                0f,
                -currentLegacyRightArm * armRaiseDegrees
            );
    }

    private Vector3 BuildHandTarget(
        Transform upper,
        Vector3 input,
        Vector3 palmInput,
        float centerForwardInput,
        float armLength,
        float sideSign,
        float depthDirection,
        out float solveReach
    )
    {
        solveReach = maximumReach;
        if (upper == null)
            return transform.position;

        Vector3 outwardAxis = transform.right * sideSign;
        float idleDropRadians = idleArmDropDegrees * Mathf.Deg2Rad;
        Vector3 direction =
            outwardAxis * Mathf.Cos(idleDropRadians) -
            transform.up * Mathf.Sin(idleDropRadians);

        // Finger motion shapes each sleeve independently; palm translation shifts
        // both targets so broad hand movement remains visible on the puppet.
        direction += transform.up * (input.y * handVerticalRange);
        float depth = DirectedDepth(input, depthDirection);
        direction += transform.forward * (depth * handDepthRange);
        direction += transform.right * (palmInput.x * palmArmHorizontalRange);
        direction += transform.up * (palmInput.y * palmArmVerticalRange);
        direction += transform.forward * (palmInput.z * palmArmDepthRange);
        if (direction.sqrMagnitude < 0.000001f)
            direction = outwardAxis;
        direction.Normalize();

        float extension = Mathf.Clamp(-sideSign * input.x, -1f, 1f);
        float forwardExtension =
            Mathf.Max(depth, 0f) * forwardReachExtension +
            Mathf.Max(palmInput.z, 0f) * palmForwardReachExtension;
        float reach = Mathf.Clamp(
            idleArmReach + extension * handHorizontalRange * 0.18f + forwardExtension,
            0.78f,
            Mathf.Clamp(maximumReach, 0.82f, 0.999f)
        );

        Vector3 baseTarget = upper.position + direction * armLength * reach;
        float centerBlend = Mathf.Clamp01(centerForwardInput);
        if (centerBlend > 0f)
        {
            float shoulderSpan = leftArm != null && rightArm != null
                ? Vector3.Distance(leftArm.position, rightArm.position)
                : armLength * 0.4f;
            Vector3 shoulderCenter = upper.position - outwardAxis * (
                shoulderSpan * 0.5f
            );
            Vector3 centerTarget =
                shoulderCenter +
                transform.forward * armLength +
                transform.right * (sideSign * armLength * centerForwardHandSeparation) -
                transform.up * (armLength * centerForwardDrop);
            Vector3 neutralDirection =
                outwardAxis * Mathf.Cos(idleDropRadians) -
                transform.up * Mathf.Sin(idleDropRadians);
            Vector3 neutralTarget =
                upper.position + neutralDirection * armLength * idleArmReach;
            baseTarget += (centerTarget - neutralTarget) * centerBlend;
        }

        solveReach = Mathf.Lerp(maximumReach, centerForwardReach, centerBlend);
        return baseTarget;
    }

    private float CenterGestureTarget(float input, ref bool active)
    {
        input = SafeClamp01(input);
        float enter = Mathf.Max(centerGestureStart, centerGestureExit + 0.01f);
        if (active)
        {
            if (input <= centerGestureExit)
                active = false;
        }
        else if (input >= enter)
        {
            active = true;
        }

        if (!active)
            return 0f;

        float range = Mathf.Max(centerGestureFull - centerGestureExit, 0.01f);
        return Mathf.Clamp01((input - centerGestureExit) / range);
    }

    private float CenterGestureVelocity(float inputVelocity, bool active)
    {
        if (!active && inputVelocity >= 0f)
            return 0f;
        float range = Mathf.Max(centerGestureFull - centerGestureExit, 0.01f);
        return Mathf.Clamp(inputVelocity / range, -8f, 8f);
    }

    private void UpdateCenterGestureServo(
        ref float position,
        ref float velocity,
        ref float acceleration,
        ref float motionBoost,
        float target,
        float inputVelocity
    )
    {
        float frameDelta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(frameDelta * 120f));
        float deltaTime = frameDelta / steps;

        for (int step = 0; step < steps; step++)
        {
            float speedFactor = Mathf.Max(
                Mathf.Clamp01(
                    Mathf.Abs(inputVelocity) /
                    Mathf.Max(centerFastInputSpeed, 0.01f)
                ),
                motionBoost
            );
            float frequency = Mathf.Lerp(
                centerSpringFrequency,
                centerFastSpringFrequency,
                speedFactor
            );
            float damping = Mathf.Lerp(
                centerSpringDamping,
                centerFastSpringDamping,
                speedFactor
            );
            float prediction = Mathf.Clamp(
                inputVelocity * centerPredictionTime,
                -centerMaximumPrediction,
                centerMaximumPrediction
            );
            float predictedTarget = Mathf.Clamp01(target + prediction);
            float enterSpeedLimit = Mathf.Lerp(
                centerMinimumEnterSpeed,
                centerMaximumEnterSpeed,
                speedFactor
            );
            float exitSpeedLimit = Mathf.Lerp(
                centerMinimumExitSpeed,
                centerMaximumExitSpeed,
                speedFactor
            );
            float referenceVelocity = Mathf.Clamp(
                inputVelocity * centerVelocityFeedForward,
                -exitSpeedLimit,
                enterSpeedLimit
            );
            float commandedAcceleration =
                frequency * frequency * (predictedTarget - position) +
                2f * damping * frequency * (referenceVelocity - velocity);
            bool entering = predictedTarget >= position;
            float accelerationLimit = entering
                ? centerMaximumEnterAcceleration
                : centerMaximumExitAcceleration;
            commandedAcceleration = Mathf.Clamp(
                commandedAcceleration,
                -accelerationLimit,
                accelerationLimit
            );
            acceleration = Mathf.MoveTowards(
                acceleration,
                commandedAcceleration,
                centerMaximumJerk * deltaTime
            );
            velocity += acceleration * deltaTime;
            velocity = Mathf.Clamp(
                velocity,
                -exitSpeedLimit,
                enterSpeedLimit
            );
            position += velocity * deltaTime;
            motionBoost = Mathf.MoveTowards(
                motionBoost,
                0f,
                centerMotionBoostDecay * deltaTime
            );

            if (position <= 0f)
            {
                position = 0f;
                if (velocity < 0f)
                    velocity = 0f;
                if (acceleration < 0f)
                    acceleration = 0f;
            }
            else if (position >= 1f)
            {
                position = 1f;
                if (velocity > 0f)
                    velocity = 0f;
                if (acceleration > 0f)
                    acceleration = 0f;
            }
        }

        if (Mathf.Abs(target - position) < 0.008f &&
            Mathf.Abs(inputVelocity) < 0.03f &&
            Mathf.Abs(velocity) < 0.03f)
        {
            position = target;
            velocity = 0f;
            acceleration = 0f;
        }
    }

    private void SolveTwoBone(
        Transform upper,
        Transform lower,
        Transform end,
        Quaternion upperRest,
        Quaternion lowerRest,
        Vector3 poleInParent,
        Vector3 requestedTarget,
        float reachLimit
    )
    {
        if (upper == null || lower == null || end == null)
            return;

        Vector3 upperPosition = upper.position;
        Vector3 lowerPosition = lower.position;
        Vector3 endPosition = end.position;
        float upperLength = Vector3.Distance(upperPosition, lowerPosition);
        float lowerLength = Vector3.Distance(lowerPosition, endPosition);
        if (upperLength < 0.00001f || lowerLength < 0.00001f)
            return;

        Vector3 toTarget = requestedTarget - upperPosition;
        if (toTarget.sqrMagnitude < 0.0000001f)
            return;

        float minimumDistance = Mathf.Abs(upperLength - lowerLength) + 0.0001f;
        float maximumDistance = Mathf.Max(
            minimumDistance + 0.0001f,
            (upperLength + lowerLength) * Mathf.Clamp(reachLimit, 0.5f, 0.999f)
        );
        float targetDistance = Mathf.Clamp(toTarget.magnitude, minimumDistance, maximumDistance);
        Vector3 targetDirection = toTarget.normalized;
        Vector3 target = upperPosition + targetDirection * targetDistance;

        Vector3 basePoleWorld = upper.parent != null
            ? upper.parent.TransformDirection(poleInParent)
            : transform.TransformDirection(poleInParent);
        Vector3 bendDirection = Vector3.ProjectOnPlane(basePoleWorld, targetDirection);
        Vector3 preferredPoleWorld =
            -transform.up * elbowDownBias -
            transform.forward * elbowBackBias;
        Vector3 preferredBend = Vector3.ProjectOnPlane(
            preferredPoleWorld,
            targetDirection
        );
        if (preferredBend.sqrMagnitude > 0.000001f)
        {
            preferredBend.Normalize();
            if (bendDirection.sqrMagnitude < 0.000001f)
            {
                bendDirection = preferredBend;
            }
            else
            {
                bendDirection.Normalize();
                if (Vector3.Dot(bendDirection, preferredBend) < 0f)
                    bendDirection = -bendDirection;
                bendDirection = Vector3.Slerp(
                    bendDirection,
                    preferredBend,
                    Mathf.Clamp01(preferredElbowPoleWeight)
                );
            }
        }
        if (bendDirection.sqrMagnitude < 0.000001f)
            bendDirection = Vector3.ProjectOnPlane(transform.forward, targetDirection);
        if (bendDirection.sqrMagnitude < 0.000001f)
            bendDirection = Vector3.ProjectOnPlane(transform.up, targetDirection);
        bendDirection.Normalize();

        float along = (
            upperLength * upperLength -
            lowerLength * lowerLength +
            targetDistance * targetDistance
        ) / (2f * targetDistance);
        float bend = Mathf.Sqrt(Mathf.Max(upperLength * upperLength - along * along, 0f));
        Vector3 desiredElbow = upperPosition + targetDirection * along + bendDirection * bend;

        Quaternion upperDelta = Quaternion.FromToRotation(
            lowerPosition - upperPosition,
            desiredElbow - upperPosition
        );
        upper.rotation = upperDelta * upper.rotation;
        ClampLocalRotation(upper, upperRest, shoulderLimitDegrees);

        lowerPosition = lower.position;
        endPosition = end.position;
        Quaternion lowerDelta = Quaternion.FromToRotation(
            endPosition - lowerPosition,
            target - lowerPosition
        );
        lower.rotation = lowerDelta * lower.rotation;
        ClampLocalRotation(lower, lowerRest, elbowLimitDegrees);
    }

    private Vector3 CalculatePoleInParent(Transform upper, Transform lower, Transform end)
    {
        if (upper == null || lower == null || end == null)
            return Vector3.forward;

        Vector3 chainDirection = (end.position - upper.position).normalized;
        Vector3 pole = Vector3.ProjectOnPlane(lower.position - upper.position, chainDirection);
        if (pole.sqrMagnitude < 0.000001f)
            pole = Vector3.ProjectOnPlane(transform.forward, chainDirection);
        if (pole.sqrMagnitude < 0.000001f)
            pole = Vector3.ProjectOnPlane(transform.up, chainDirection);
        pole.Normalize();

        return upper.parent != null
            ? upper.parent.InverseTransformDirection(pole)
            : transform.InverseTransformDirection(pole);
    }

    private static void ClampLocalRotation(
        Transform bone,
        Quaternion rest,
        float maximumAngle
    )
    {
        Quaternion delta = Quaternion.Inverse(rest) * bone.localRotation;
        float angle = Quaternion.Angle(Quaternion.identity, delta);
        if (angle > maximumAngle && angle > 0.0001f)
        {
            bone.localRotation = rest * Quaternion.Slerp(
                Quaternion.identity,
                delta,
                maximumAngle / angle
            );
        }
    }

    private static float ChainLength(Transform upper, Transform lower, Transform end)
    {
        if (upper == null || lower == null || end == null)
            return 1f;
        return Vector3.Distance(upper.position, lower.position) +
               Vector3.Distance(lower.position, end.position);
    }

    private static Vector3 ClampVector(float x, float y, float z)
    {
        return new Vector3(
            SafeClamp(x, -1f, 1f),
            SafeClamp(y, -1f, 1f),
            SafeClamp(z, -1f, 1f)
        );
    }

    private static float DirectedDepth(Vector3 input, float depthDirection)
    {
        float direction = Mathf.Approximately(depthDirection, 0f)
            ? 1f
            : depthDirection;
        return input.z * direction;
    }

    private static float SafeClamp01(float value)
    {
        return SafeClamp(value, 0f, 1f);
    }

    private static float SafeClamp(float value, float minimum, float maximum)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;
        return Mathf.Clamp(value, minimum, maximum);
    }

    private static Quaternion LocalRotationOrIdentity(Transform bone)
    {
        return bone != null ? bone.localRotation : Quaternion.identity;
    }

    private static void SetLocalRotation(Transform bone, Quaternion rotation)
    {
        if (bone != null)
            bone.localRotation = rotation;
    }

    private static void BlendLocalRotation(
        Transform bone,
        Quaternion from,
        float blend
    )
    {
        if (bone != null)
            bone.localRotation = Quaternion.Slerp(from, bone.localRotation, blend);
    }

    private Transform FindBone(string boneName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, boneName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }
}
