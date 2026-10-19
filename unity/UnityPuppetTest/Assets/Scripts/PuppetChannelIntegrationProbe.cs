using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public sealed class PuppetChannelIntegrationProbe : MonoBehaviour
{
    [Serializable]
    private sealed class IntegrationReport
    {
        public bool success;
        public bool channelConnected;
        public bool continuousIkMode;
        public bool embeddedMockServer;
        public float headDirectionAngle;
        public float headLeftHandDrift;
        public float headRightHandDrift;
        public float leftHandDisplacement;
        public float rightHandDisplacement;
        public float leftForwardReach;
        public float rightForwardReach;
        public float leftForwardLift;
        public float rightForwardLift;
        public float leftIdleDropAngle;
        public float rightIdleDropAngle;
        public float leftIdleReachRatio;
        public float rightIdleReachRatio;
        public float leftCenterForwardReach;
        public float rightCenterForwardReach;
        public float leftCenterInwardReach;
        public float rightCenterInwardReach;
        public float leftCenterElbowStraightening;
        public float rightCenterElbowStraightening;
        public float leftCenterCrossTalk;
        public float rightCenterCrossTalk;
        public float centerThresholdLeak;
        public float leftCenterEarlyDisplacement;
        public float rightCenterEarlyDisplacement;
        public float leftCenterEarlyProgress;
        public float rightCenterEarlyProgress;
        public float leftCenterFinalProgress;
        public float rightCenterFinalProgress;
        public float leftCenterLivePalmDisplacement;
        public float rightCenterLivePalmDisplacement;
        public float leftForwardShoulderAngle;
        public float rightForwardShoulderAngle;
        public float leftForwardElbowStraightening;
        public float rightForwardElbowStraightening;
        public float leftToRightCrossTalk;
        public float rightToLeftCrossTalk;
        public float bodyPositionDisplacement;
        public float bodyTurnAngle;
        public float leftPalmDrivenHandDisplacement;
        public float rightPalmDrivenHandDisplacement;
        public float maximumLeftShoulderAngle;
        public float maximumRightShoulderAngle;
        public float neutralReturnError;
        public float uncalibratedMovement;
        public float flexibleExtensionRatio;
        public float flexibleExtensionOvershoot;
        public float flexibleExtensionPeakSpeed;
        public float shoulderSpineSynergy;
        public float actionSettleTime;
        public float elbowFlipCount;
        public float minimumLeftElbowPoleDot;
        public float minimumRightElbowPoleDot;
        public float maximumLeftToRightCrossTalk;
        public float maximumRightToLeftCrossTalk;
        public float fastReverseJitter;
        public float fastReversePeakSpeed;
        public float fastReverseResidual;
        public float disconnectionReturnTime;
        public float disconnectionReturnError;
        public float handLossHoldError;
        public string[] errors;
    }

    private struct PoseSnapshot
    {
        public Vector3 root;
        public Vector3 leftHand;
        public Vector3 rightHand;
        public Quaternion head;
        public Quaternion spine;
        public Quaternion leftShoulder;
        public Quaternion rightShoulder;
        public Quaternion leftArm;
        public Quaternion rightArm;
    }

    private sealed class MotionObservation
    {
        public float settleTime = -1f;
        public float maxLinearSpeed;
        public float maxAngularSpeed;
        public float maxLeftDisplacement;
        public float maxRightDisplacement;
        public int leftVelocityReversals;
        public int rightVelocityReversals;
        public float maximumForward;
        public float minimumForward;
        public float lastForward;
    }

    private sealed class ElbowObservation
    {
        public int flipCount;
        public float minimumLeftPoleDot = 1f;
        public float minimumRightPoleDot = 1f;
        private Vector3 previousLeftBend;
        private Vector3 previousRightBend;
        private bool hasPrevious;

        public void Sample(PuppetBoneTest puppet)
        {
            Vector3 leftBend = ElbowBendDirection(
                puppet.leftArm,
                puppet.leftForeArm,
                puppet.leftHand
            );
            Vector3 rightBend = ElbowBendDirection(
                puppet.rightArm,
                puppet.rightForeArm,
                puppet.rightHand
            );
            if (leftBend.sqrMagnitude < 0.000001f || rightBend.sqrMagnitude < 0.000001f)
                return;

            if (hasPrevious)
            {
                if (Vector3.Dot(previousLeftBend, leftBend) < -0.25f ||
                    Vector3.Dot(previousRightBend, rightBend) < -0.25f)
                    flipCount++;
            }

            Vector3 leftReference = previousLeftBend.sqrMagnitude > 0.000001f
                ? previousLeftBend
                : leftBend;
            Vector3 rightReference = previousRightBend.sqrMagnitude > 0.000001f
                ? previousRightBend
                : rightBend;
            minimumLeftPoleDot = Mathf.Min(minimumLeftPoleDot, Vector3.Dot(leftReference, leftBend));
            minimumRightPoleDot = Mathf.Min(minimumRightPoleDot, Vector3.Dot(rightReference, rightBend));
            previousLeftBend = leftBend;
            previousRightBend = rightBend;
            hasPrevious = true;
        }
    }

    private const string MockBaseUrl = "http://127.0.0.1:5000";
    private bool lastRequestSucceeded;
    private EmbeddedMockPuppetServer embeddedServer;
    private bool embeddedServerStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWhenRequested()
    {
        string requestPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "RunPuppetChannelIntegration.request"
        );
        bool commandLineRequested = Array.Exists(
            Environment.GetCommandLineArgs(),
            argument => argument == "-puppetChannelIntegrationTest"
        );
        bool fileRequested = File.Exists(requestPath);
        if (commandLineRequested || fileRequested)
        {
            if (fileRequested)
                File.Delete(requestPath);
            new GameObject("Puppet Channel Integration Probe").AddComponent<PuppetChannelIntegrationProbe>();
        }
    }

    private IEnumerator Start()
    {
        List<string> errors = new List<string>();
        embeddedServer = new EmbeddedMockPuppetServer();
        embeddedServerStarted = embeddedServer.TryStart();

        PuppetBoneTest puppet = FindAnyObjectByType<PuppetBoneTest>();
        if (puppet == null)
        {
            WriteReportAndExit(new IntegrationReport
            {
                success = false,
                errors = new[] { "PuppetBoneTest was not found in the scene." }
            });
            yield break;
        }

        if (!HasCompleteRig(puppet))
        {
            WriteReportAndExit(new IntegrationReport
            {
                success = false,
                errors = new[] { "The arm chain is incomplete; IK requires upper arm, forearm, and hand bones." }
            });
            yield break;
        }

        PuppetHttpReceiver receiver = puppet.GetComponent<PuppetHttpReceiver>();
        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(1f);

        if (!lastRequestSucceeded)
            errors.Add("Unity could not reach the mock /preset endpoint.");
        if (receiver == null || !receiver.IsConnected)
            errors.Add("PuppetHttpReceiver did not connect to /puppet_state.");
        if (!puppet.ContinuousIkMode)
            errors.Add("Unity did not enter V2 continuous IK mode.");

        Quaternion neutralHead = puppet.head.localRotation;
        Quaternion neutralLeftArm = puppet.leftArm.localRotation;
        Quaternion neutralRightArm = puppet.rightArm.localRotation;
        Quaternion neutralLeftShoulder = puppet.leftShoulder.localRotation;
        Quaternion neutralRightShoulder = puppet.rightShoulder.localRotation;
        Quaternion neutralSpine = puppet.spine.localRotation;
        Vector3 neutralRoot = puppet.transform.localPosition;
        Vector3 neutralLeftHand = puppet.leftHand.position;
        Vector3 neutralRightHand = puppet.rightHand.position;
        float leftLength = ArmLength(puppet.leftArm, puppet.leftForeArm, puppet.leftHand);
        float rightLength = ArmLength(puppet.rightArm, puppet.rightForeArm, puppet.rightHand);
        float leftIdleReachRatio = Vector3.Distance(puppet.leftArm.position, neutralLeftHand) / leftLength;
        float rightIdleReachRatio = Vector3.Distance(puppet.rightArm.position, neutralRightHand) / rightLength;
        float neutralLeftElbowFlex = ElbowFlexDegrees(
            puppet.leftArm,
            puppet.leftForeArm,
            puppet.leftHand
        );
        float neutralRightElbowFlex = ElbowFlexDegrees(
            puppet.rightArm,
            puppet.rightForeArm,
            puppet.rightHand
        );
        float leftIdleDropAngle = IdleDropAngle(puppet, puppet.leftArm, neutralLeftHand);
        float rightIdleDropAngle = IdleDropAngle(puppet, puppet.rightArm, neutralRightHand);
        if (leftIdleDropAngle < 8f || leftIdleDropAngle > 24f ||
            rightIdleDropAngle < 8f || rightIdleDropAngle > 24f)
            errors.Add(
                $"Idle arm drop was outside the puppet range: " +
                $"{leftIdleDropAngle:F1}/{rightIdleDropAngle:F1} degrees."
            );
        if (leftIdleReachRatio < 0.88f || leftIdleReachRatio > 0.96f ||
            rightIdleReachRatio < 0.88f || rightIdleReachRatio > 0.96f)
            errors.Add(
                $"Idle elbows were not lightly bent: " +
                $"reach {leftIdleReachRatio:F2}/{rightIdleReachRatio:F2}."
            );

        yield return ApplyPreset("uncalibrated");
        yield return new WaitForSecondsRealtime(0.8f);
        float uncalibratedMovement = MaximumMovement(
            puppet,
            neutralRoot,
            neutralLeftHand,
            neutralRightHand
        );
        if (uncalibratedMovement > Mathf.Max(leftLength, rightLength) * 0.03f)
            errors.Add($"Uncalibrated input moved the puppet: {uncalibratedMovement:F4} units.");

        yield return ApplyPreset("head-direction");
        yield return new WaitForSecondsRealtime(1f);
        float headDirectionAngle = Quaternion.Angle(neutralHead, puppet.head.localRotation);
        float headLeftHandDrift = Vector3.Distance(neutralLeftHand, puppet.leftHand.position);
        float headRightHandDrift = Vector3.Distance(neutralRightHand, puppet.rightHand.position);
        if (headDirectionAngle < 25f)
            errors.Add($"Head did not follow the middle-finger direction enough: {headDirectionAngle:F2} degrees.");
        if (headLeftHandDrift > leftLength * 0.02f ||
            headRightHandDrift > rightLength * 0.02f)
            errors.Add(
                $"Head input moved an arm: {headLeftHandDrift:F4}/{headRightHandDrift:F4} units."
            );

        yield return ApplyPreset("left-hand");
        yield return new WaitForSecondsRealtime(1f);
        float leftHandDisplacement = Vector3.Distance(neutralLeftHand, puppet.leftHand.position);
        float leftToRightCrossTalk = Vector3.Distance(neutralRightHand, puppet.rightHand.position);
        float maximumLeftShoulderAngle = Quaternion.Angle(
            neutralLeftArm,
            puppet.leftArm.localRotation
        );
        if (leftHandDisplacement < leftLength * 0.08f)
            errors.Add($"Thumb input did not move the left hand enough: {leftHandDisplacement:F4} units.");
        if (leftToRightCrossTalk > rightLength * 0.04f)
            errors.Add($"Thumb input also moved the right hand: {leftToRightCrossTalk:F4} units.");
        if (maximumLeftShoulderAngle > puppet.shoulderLimitDegrees + 1f)
            errors.Add($"Left shoulder exceeded its joint limit: {maximumLeftShoulderAngle:F2} degrees.");

        yield return ApplyPreset("right-hand");
        yield return new WaitForSecondsRealtime(1f);
        float rightHandDisplacement = Vector3.Distance(neutralRightHand, puppet.rightHand.position);
        float rightToLeftCrossTalk = Vector3.Distance(neutralLeftHand, puppet.leftHand.position);
        float maximumRightShoulderAngle = Quaternion.Angle(
            neutralRightArm,
            puppet.rightArm.localRotation
        );
        if (rightHandDisplacement < rightLength * 0.08f)
            errors.Add($"Pinky input did not move the right hand enough: {rightHandDisplacement:F4} units.");
        if (rightToLeftCrossTalk > leftLength * 0.04f)
            errors.Add($"Pinky input also moved the left hand: {rightToLeftCrossTalk:F4} units.");
        if (maximumRightShoulderAngle > puppet.shoulderLimitDegrees + 1f)
            errors.Add($"Right shoulder exceeded its joint limit: {maximumRightShoulderAngle:F2} degrees.");

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        Vector3 thresholdLeftHand = puppet.leftHand.position;
        Vector3 thresholdRightHand = puppet.rightHand.position;
        yield return ApplyPreset("center-below-threshold");
        yield return new WaitForSecondsRealtime(0.75f);
        float centerThresholdLeak = Mathf.Max(
            Vector3.Distance(thresholdLeftHand, puppet.leftHand.position),
            Vector3.Distance(thresholdRightHand, puppet.rightHand.position)
        );
        if (centerThresholdLeak > Mathf.Max(leftLength, rightLength) * 0.02f)
            errors.Add(
                $"Palm-center gesture activated before the fingertip crossed the threshold: " +
                $"{centerThresholdLeak:F4} units."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        Vector3 neutralLeftCurlHand = puppet.leftHand.position;
        Vector3 neutralRightDuringLeftCurl = puppet.rightHand.position - puppet.rightArm.position;
        float neutralLeftCurlElbow = ElbowFlexDegrees(
            puppet.leftArm,
            puppet.leftForeArm,
            puppet.leftHand
        );
        yield return ApplyPreset("left-curl");
        yield return new WaitForSecondsRealtime(0.12f);
        float leftCenterEarlyDisplacement = Vector3.Distance(
            neutralLeftCurlHand,
            puppet.leftHand.position
        );
        float leftCenterEarlyProgress = puppet.CurrentLeftCenterForward;
        if (leftCenterEarlyProgress < 0.01f || leftCenterEarlyProgress > 0.45f)
            errors.Add(
                $"Thumb palm-center servo was not gradual during the first 120 ms: " +
                $"{leftCenterEarlyProgress:F3}."
            );
        if (leftCenterEarlyDisplacement > leftLength * 0.35f)
            errors.Add(
                $"Thumb palm-center gesture moved too far in the first 120 ms: " +
                $"{leftCenterEarlyDisplacement:F4} units."
            );
        yield return new WaitForSecondsRealtime(0.9f);
        float leftCenterFinalProgress = puppet.CurrentLeftCenterForward;
        if (leftCenterFinalProgress < 0.85f)
            errors.Add(
                $"Thumb palm-center servo did not reach the commanded pose: " +
                $"{leftCenterFinalProgress:F3}."
            );
        Vector3 leftCenterDelta = puppet.leftHand.position - neutralLeftCurlHand;
        float leftCenterForwardReach = Vector3.Dot(leftCenterDelta, puppet.transform.forward);
        float leftCenterInwardReach = Vector3.Dot(leftCenterDelta, puppet.transform.right);
        float leftCenterElbowStraightening = neutralLeftCurlElbow - ElbowFlexDegrees(
            puppet.leftArm,
            puppet.leftForeArm,
            puppet.leftHand
        );
        float leftCenterCrossTalk = Vector3.Distance(
            neutralRightDuringLeftCurl,
            puppet.rightHand.position - puppet.rightArm.position
        );
        if (leftCenterForwardReach < leftLength * 0.30f ||
            leftCenterInwardReach < leftLength * 0.20f)
            errors.Add(
                $"Thumb palm-center gesture did not send the left hand forward and inward: " +
                $"{leftCenterForwardReach:F4}/{leftCenterInwardReach:F4} units."
            );
        if (leftCenterElbowStraightening < 12f)
            errors.Add(
                $"Thumb palm-center gesture did not straighten the left elbow enough: " +
                $"{leftCenterElbowStraightening:F2} degrees."
            );
        if (leftCenterCrossTalk > rightLength * 0.04f)
            errors.Add($"Thumb palm-center gesture moved the right hand: {leftCenterCrossTalk:F4} units.");

        Vector3 leftCenterLiveOffset = puppet.leftHand.position - puppet.leftArm.position;
        yield return ApplyPreset("left-center-palm-shift");
        yield return new WaitForSecondsRealtime(0.65f);
        float leftCenterLivePalmDisplacement = Vector3.Distance(
            leftCenterLiveOffset,
            puppet.leftHand.position - puppet.leftArm.position
        );
        if (leftCenterLivePalmDisplacement < leftLength * 0.04f)
            errors.Add(
                $"Live palm input stopped affecting the fully extended left arm: " +
                $"{leftCenterLivePalmDisplacement:F4} units."
            );

        yield return ApplyPreset("neutral");
        yield return WaitForArmChainsToSettle(puppet, 1.8f);
        Vector3 neutralRightCurlHand = puppet.rightHand.position;
        Vector3 neutralLeftDuringRightCurl = puppet.leftHand.position - puppet.leftArm.position;
        float neutralRightCurlElbow = ElbowFlexDegrees(
            puppet.rightArm,
            puppet.rightForeArm,
            puppet.rightHand
        );
        yield return ApplyPreset("right-curl");
        yield return new WaitForSecondsRealtime(0.12f);
        float rightCenterEarlyDisplacement = Vector3.Distance(
            neutralRightCurlHand,
            puppet.rightHand.position
        );
        float rightCenterEarlyProgress = puppet.CurrentRightCenterForward;
        if (rightCenterEarlyProgress < 0.01f || rightCenterEarlyProgress > 0.45f)
            errors.Add(
                $"Pinky palm-center servo was not gradual during the first 120 ms: " +
                $"{rightCenterEarlyProgress:F3}."
            );
        if (rightCenterEarlyDisplacement > rightLength * 0.35f)
            errors.Add(
                $"Pinky palm-center gesture moved too far in the first 120 ms: " +
                $"{rightCenterEarlyDisplacement:F4} units."
            );
        yield return new WaitForSecondsRealtime(0.9f);
        float rightCenterFinalProgress = puppet.CurrentRightCenterForward;
        if (rightCenterFinalProgress < 0.85f)
            errors.Add(
                $"Pinky palm-center servo did not reach the commanded pose: " +
                $"{rightCenterFinalProgress:F3}."
            );
        Vector3 rightCenterDelta = puppet.rightHand.position - neutralRightCurlHand;
        float rightCenterForwardReach = Vector3.Dot(rightCenterDelta, puppet.transform.forward);
        float rightCenterInwardReach = Vector3.Dot(rightCenterDelta, -puppet.transform.right);
        float rightCenterElbowStraightening = neutralRightCurlElbow - ElbowFlexDegrees(
            puppet.rightArm,
            puppet.rightForeArm,
            puppet.rightHand
        );
        float rightCenterCrossTalk = Vector3.Distance(
            neutralLeftDuringRightCurl,
            puppet.leftHand.position - puppet.leftArm.position
        );
        if (rightCenterForwardReach < rightLength * 0.30f ||
            rightCenterInwardReach < rightLength * 0.20f)
            errors.Add(
                $"Pinky palm-center gesture did not send the right hand forward and inward: " +
                $"{rightCenterForwardReach:F4}/{rightCenterInwardReach:F4} units."
            );
        if (rightCenterElbowStraightening < 12f)
            errors.Add(
                $"Pinky palm-center gesture did not straighten the right elbow enough: " +
                $"{rightCenterElbowStraightening:F2} degrees."
            );
        if (rightCenterCrossTalk > leftLength * 0.04f)
            errors.Add($"Pinky palm-center gesture moved the left hand: {rightCenterCrossTalk:F4} units.");

        Vector3 rightCenterLiveOffset = puppet.rightHand.position - puppet.rightArm.position;
        yield return ApplyPreset("right-center-palm-shift");
        yield return new WaitForSecondsRealtime(0.65f);
        float rightCenterLivePalmDisplacement = Vector3.Distance(
            rightCenterLiveOffset,
            puppet.rightHand.position - puppet.rightArm.position
        );
        if (rightCenterLivePalmDisplacement < rightLength * 0.04f)
            errors.Add(
                $"Live palm input stopped affecting the fully extended right arm: " +
                $"{rightCenterLivePalmDisplacement:F4} units."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(1f);
        yield return ApplyPreset("forward-hands");
        yield return new WaitForSecondsRealtime(1f);
        Vector3 leftForwardDelta = puppet.leftHand.position - neutralLeftHand;
        Vector3 rightForwardDelta = puppet.rightHand.position - neutralRightHand;
        float leftForwardReach = Vector3.Dot(leftForwardDelta, puppet.transform.forward);
        float rightForwardReach = Vector3.Dot(rightForwardDelta, puppet.transform.forward);
        float leftForwardLift = Vector3.Dot(leftForwardDelta, puppet.transform.up);
        float rightForwardLift = Vector3.Dot(rightForwardDelta, puppet.transform.up);
        float leftForwardShoulderAngle = Quaternion.Angle(
            neutralLeftShoulder,
            puppet.leftShoulder.localRotation
        );
        float rightForwardShoulderAngle = Quaternion.Angle(
            neutralRightShoulder,
            puppet.rightShoulder.localRotation
        );
        float leftForwardElbowStraightening = neutralLeftElbowFlex - ElbowFlexDegrees(
            puppet.leftArm,
            puppet.leftForeArm,
            puppet.leftHand
        );
        float rightForwardElbowStraightening = neutralRightElbowFlex - ElbowFlexDegrees(
            puppet.rightArm,
            puppet.rightForeArm,
            puppet.rightHand
        );
        if (leftForwardReach < leftLength * 0.05f)
            errors.Add($"Thumb depth did not move the left arm forward enough: {leftForwardReach:F4} units.");
        if (rightForwardReach < rightLength * 0.05f)
            errors.Add($"Pinky depth did not move the right arm forward enough: {rightForwardReach:F4} units.");
        if (leftForwardElbowStraightening < 4f || rightForwardElbowStraightening < 4f)
            errors.Add(
                $"Forward reach did not straighten the elbows enough: " +
                $"{leftForwardElbowStraightening:F2}/{rightForwardElbowStraightening:F2} degrees."
            );
        if (leftForwardShoulderAngle > 2f || rightForwardShoulderAngle > 2f)
            errors.Add(
                $"Forward input still added shoulder follow: " +
                $"{leftForwardShoulderAngle:F2}/{rightForwardShoulderAngle:F2} degrees."
            );

        yield return ApplyPreset("body");
        yield return new WaitForSecondsRealtime(1f);
        float bodyPositionDisplacement = Vector3.Distance(neutralRoot, puppet.transform.localPosition);
        float bodyTurnAngle = Quaternion.Angle(neutralSpine, puppet.spine.localRotation);
        if (bodyPositionDisplacement < 0.15f)
            errors.Add($"Palm translation did not move the body enough: {bodyPositionDisplacement:F4} units.");
        if (bodyTurnAngle < 10f)
            errors.Add($"Palm orientation did not turn or lean the body enough: {bodyTurnAngle:F2} degrees.");

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);

        Vector3 palmNeutralLeftOffset = puppet.leftHand.position - puppet.leftArm.position;
        Vector3 palmNeutralRightOffset = puppet.rightHand.position - puppet.rightArm.position;
        yield return ApplyPreset("palm-arms");
        yield return new WaitForSecondsRealtime(1f);
        float leftPalmDrivenHandDisplacement = Vector3.Distance(
            palmNeutralLeftOffset,
            puppet.leftHand.position - puppet.leftArm.position
        );
        float rightPalmDrivenHandDisplacement = Vector3.Distance(
            palmNeutralRightOffset,
            puppet.rightHand.position - puppet.rightArm.position
        );
        if (leftPalmDrivenHandDisplacement < leftLength * 0.06f ||
            rightPalmDrivenHandDisplacement < rightLength * 0.06f)
            errors.Add(
                $"Palm translation did not change both arm poses enough: " +
                $"{leftPalmDrivenHandDisplacement:F4}/{rightPalmDrivenHandDisplacement:F4} units."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);

        PoseSnapshot neutralPose = CapturePose(puppet);
        float maximumArmLength = Mathf.Max(leftLength, rightLength);

        yield return ApplyPreset("flexible-left-forward");
        MotionObservation extensionObservation = new MotionObservation();
        yield return ObserveForSeconds(
            puppet,
            neutralPose,
            1.1f,
            true,
            extensionObservation,
            puppet.transform.forward
        );
        float flexibleForward = Vector3.Dot(
            puppet.leftHand.position - neutralPose.leftHand,
            puppet.transform.forward
        );
        float requestedForward = Vector3.Dot(
            puppet.CurrentLeftHandTarget - neutralPose.leftHand,
            puppet.transform.forward
        );
        float flexibleExtensionRatio = requestedForward > 0.0001f
            ? flexibleForward / requestedForward
            : 0f;
        float flexibleExtensionOvershoot = Mathf.Max(
            0f,
            extensionObservation.maximumForward - requestedForward
        );
        if (flexibleExtensionRatio < 0.72f)
            errors.Add($"Flexible extension reached only {flexibleExtensionRatio:P0} of its requested forward target.");
        if (flexibleExtensionOvershoot > maximumArmLength * 0.08f)
            errors.Add($"Flexible extension overshot its target by {flexibleExtensionOvershoot:F4} units.");
        if (extensionObservation.maxLinearSpeed > maximumArmLength * 18f)
            errors.Add($"Flexible extension moved too abruptly: {extensionObservation.maxLinearSpeed:F4} units/s.");

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        PoseSnapshot synergyNeutral = CapturePose(puppet);
        yield return ApplyPreset("synergy-left");
        MotionObservation synergyObservation = new MotionObservation();
        yield return ObserveForSeconds(
            puppet,
            synergyNeutral,
            1.1f,
            true,
            synergyObservation,
            Vector3.zero
        );
        float synergyShoulderAngle = Quaternion.Angle(
            synergyNeutral.leftShoulder,
            puppet.leftShoulder.localRotation
        );
        float synergySpineAngle = Quaternion.Angle(
            synergyNeutral.spine,
            puppet.spine.localRotation
        );
        float shoulderSpineSynergy = 1f - Mathf.Clamp01(Mathf.Max(
            synergyShoulderAngle / 2f,
            synergySpineAngle / 2f
        ));
        if (synergyShoulderAngle > 2f || synergySpineAngle > 2f)
            errors.Add(
                $"Arm input leaked into shoulder/spine: shoulder {synergyShoulderAngle:F2}, " +
                $"spine {synergySpineAngle:F2} degrees."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        PoseSnapshot settleNeutral = CapturePose(puppet);
        yield return ApplyPreset("forward-hands");
        MotionObservation settleObservation = new MotionObservation();
        yield return ObserveForSeconds(
            puppet,
            settleNeutral,
            1.25f,
            true,
            settleObservation,
            Vector3.zero
        );
        float actionSettleTime = settleObservation.settleTime < 0f
            ? 1.25f
            : settleObservation.settleTime;
        if (settleObservation.settleTime < 0f || actionSettleTime > 0.95f)
            errors.Add($"Action did not settle within 0.95 seconds: {actionSettleTime:F3} seconds.");

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        PoseSnapshot crossTalkNeutral = CapturePose(puppet);
        MotionObservation leftIsolation = new MotionObservation();
        yield return ApplyPreset("left-hand");
        yield return ObserveForSeconds(
            puppet,
            crossTalkNeutral,
            0.75f,
            false,
            leftIsolation,
            Vector3.zero
        );
        float maximumLeftToRightCrossTalk = leftIsolation.maxRightDisplacement;

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        crossTalkNeutral = CapturePose(puppet);
        MotionObservation rightIsolation = new MotionObservation();
        yield return ApplyPreset("right-hand");
        yield return ObserveForSeconds(
            puppet,
            crossTalkNeutral,
            0.75f,
            false,
            rightIsolation,
            Vector3.zero
        );
        float maximumRightToLeftCrossTalk = rightIsolation.maxLeftDisplacement;
        if (maximumLeftToRightCrossTalk > rightLength * 0.04f)
            errors.Add(
                $"Left-hand input caused too much right-hand cross-talk: " +
                $"{maximumLeftToRightCrossTalk:F4} units."
            );
        if (maximumRightToLeftCrossTalk > leftLength * 0.04f)
            errors.Add(
                $"Right-hand input caused too much left-hand cross-talk: " +
                $"{maximumRightToLeftCrossTalk:F4} units."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        ElbowObservation elbowObservation = new ElbowObservation();
        MotionObservation elbowMotion = new MotionObservation();
        yield return ApplyPreset("elbow-sweep-left");
        yield return ObserveForSeconds(
            puppet,
            CapturePose(puppet),
            0.45f,
            false,
            elbowMotion,
            Vector3.zero,
            elbowObservation
        );
        yield return ApplyPreset("elbow-sweep-forward");
        yield return ObserveForSeconds(
            puppet,
            CapturePose(puppet),
            0.45f,
            false,
            elbowMotion,
            Vector3.zero,
            elbowObservation
        );
        yield return ApplyPreset("synergy-left");
        yield return ObserveForSeconds(
            puppet,
            CapturePose(puppet),
            0.45f,
            false,
            elbowMotion,
            Vector3.zero,
            elbowObservation
        );
        if (elbowObservation.flipCount > 0 ||
            elbowObservation.minimumLeftPoleDot < -0.25f ||
            elbowObservation.minimumRightPoleDot < -0.25f)
            errors.Add(
                $"Elbow bend direction flipped: {elbowObservation.flipCount} flip(s), " +
                $"pole dots {elbowObservation.minimumLeftPoleDot:F2}/{elbowObservation.minimumRightPoleDot:F2}."
            );

        yield return ApplyPreset("neutral");
        yield return new WaitForSecondsRealtime(0.65f);
        PoseSnapshot reverseNeutral = CapturePose(puppet);
        MotionObservation reverseObservation = new MotionObservation();
        yield return ApplyPreset("left-hand");
        yield return ObserveForSeconds(puppet, reverseNeutral, 0.22f, false, reverseObservation, puppet.transform.right);
        yield return ApplyPreset("right-hand");
        yield return ObserveForSeconds(puppet, reverseNeutral, 0.22f, false, reverseObservation, puppet.transform.right);
        yield return ApplyPreset("left-hand");
        yield return ObserveForSeconds(puppet, reverseNeutral, 0.22f, false, reverseObservation, puppet.transform.right);
        yield return ApplyPreset("neutral");
        yield return ObserveForSeconds(puppet, reverseNeutral, 0.85f, true, reverseObservation, puppet.transform.right);
        float fastReverseJitter = reverseObservation.leftVelocityReversals + reverseObservation.rightVelocityReversals;
        float fastReverseResidual = MaximumMovement(
            puppet,
            reverseNeutral.root,
            reverseNeutral.leftHand,
            reverseNeutral.rightHand
        );
        if (fastReverseJitter > 4)
            errors.Add($"Fast reverse input oscillated {fastReverseJitter} time(s).");
        if (reverseObservation.maxLinearSpeed > maximumArmLength * 22f)
            errors.Add($"Fast reverse input exceeded the speed limit: {reverseObservation.maxLinearSpeed:F4} units/s.");
        if (fastReverseResidual > maximumArmLength * 0.06f)
            errors.Add($"Fast reverse input left a residual pose error: {fastReverseResidual:F4} units.");

        yield return ApplyPreset("left-hand");
        yield return new WaitForSecondsRealtime(0.65f);
        PoseSnapshot heldPose = CapturePose(puppet);
        yield return ApplyPreset("hand-lost");
        MotionObservation disconnectionObservation = new MotionObservation();
        yield return ObserveForSeconds(
            puppet,
            heldPose,
            0.8f,
            false,
            disconnectionObservation,
            Vector3.zero
        );
        float handLossHoldError = MaximumMovement(
            puppet,
            heldPose.root,
            heldPose.leftHand,
            heldPose.rightHand
        );
        if (handLossHoldError > maximumArmLength * 0.02f)
            errors.Add($"Puppet did not hold its pose after hand loss: {handLossHoldError:F4} units.");
        if (Quaternion.Angle(heldPose.head, puppet.head.localRotation) > 2f ||
            Quaternion.Angle(heldPose.spine, puppet.spine.localRotation) > 2f)
            errors.Add("Head or spine changed after hand loss.");

        float disconnectionReturnTime = 0f;
        float disconnectionReturnError = handLossHoldError;
        float neutralReturnError = handLossHoldError;

        WriteReportAndExit(new IntegrationReport
        {
            success = errors.Count == 0,
            channelConnected = receiver != null && receiver.IsConnected,
            continuousIkMode = puppet.ContinuousIkMode,
            embeddedMockServer = embeddedServerStarted,
            headDirectionAngle = headDirectionAngle,
            headLeftHandDrift = headLeftHandDrift,
            headRightHandDrift = headRightHandDrift,
            leftHandDisplacement = leftHandDisplacement,
            rightHandDisplacement = rightHandDisplacement,
            leftForwardReach = leftForwardReach,
            rightForwardReach = rightForwardReach,
            leftForwardLift = leftForwardLift,
            rightForwardLift = rightForwardLift,
            leftIdleDropAngle = leftIdleDropAngle,
            rightIdleDropAngle = rightIdleDropAngle,
            leftIdleReachRatio = leftIdleReachRatio,
            rightIdleReachRatio = rightIdleReachRatio,
            leftCenterForwardReach = leftCenterForwardReach,
            rightCenterForwardReach = rightCenterForwardReach,
            leftCenterInwardReach = leftCenterInwardReach,
            rightCenterInwardReach = rightCenterInwardReach,
            leftCenterElbowStraightening = leftCenterElbowStraightening,
            rightCenterElbowStraightening = rightCenterElbowStraightening,
            leftCenterCrossTalk = leftCenterCrossTalk,
            rightCenterCrossTalk = rightCenterCrossTalk,
            centerThresholdLeak = centerThresholdLeak,
            leftCenterEarlyDisplacement = leftCenterEarlyDisplacement,
            rightCenterEarlyDisplacement = rightCenterEarlyDisplacement,
            leftCenterEarlyProgress = leftCenterEarlyProgress,
            rightCenterEarlyProgress = rightCenterEarlyProgress,
            leftCenterFinalProgress = leftCenterFinalProgress,
            rightCenterFinalProgress = rightCenterFinalProgress,
            leftCenterLivePalmDisplacement = leftCenterLivePalmDisplacement,
            rightCenterLivePalmDisplacement = rightCenterLivePalmDisplacement,
            leftForwardShoulderAngle = leftForwardShoulderAngle,
            rightForwardShoulderAngle = rightForwardShoulderAngle,
            leftForwardElbowStraightening = leftForwardElbowStraightening,
            rightForwardElbowStraightening = rightForwardElbowStraightening,
            leftToRightCrossTalk = leftToRightCrossTalk,
            rightToLeftCrossTalk = rightToLeftCrossTalk,
            bodyPositionDisplacement = bodyPositionDisplacement,
            bodyTurnAngle = bodyTurnAngle,
            leftPalmDrivenHandDisplacement = leftPalmDrivenHandDisplacement,
            rightPalmDrivenHandDisplacement = rightPalmDrivenHandDisplacement,
            maximumLeftShoulderAngle = maximumLeftShoulderAngle,
            maximumRightShoulderAngle = maximumRightShoulderAngle,
            neutralReturnError = neutralReturnError,
            uncalibratedMovement = uncalibratedMovement,
            flexibleExtensionRatio = flexibleExtensionRatio,
            flexibleExtensionOvershoot = flexibleExtensionOvershoot,
            flexibleExtensionPeakSpeed = extensionObservation.maxLinearSpeed,
            shoulderSpineSynergy = shoulderSpineSynergy,
            actionSettleTime = actionSettleTime,
            elbowFlipCount = elbowObservation.flipCount,
            minimumLeftElbowPoleDot = elbowObservation.minimumLeftPoleDot,
            minimumRightElbowPoleDot = elbowObservation.minimumRightPoleDot,
            maximumLeftToRightCrossTalk = maximumLeftToRightCrossTalk,
            maximumRightToLeftCrossTalk = maximumRightToLeftCrossTalk,
            fastReverseJitter = fastReverseJitter,
            fastReversePeakSpeed = reverseObservation.maxLinearSpeed,
            fastReverseResidual = fastReverseResidual,
            disconnectionReturnTime = disconnectionReturnTime,
            disconnectionReturnError = disconnectionReturnError,
            handLossHoldError = handLossHoldError,
            errors = errors.ToArray()
        });
    }

    private void OnDestroy()
    {
        embeddedServer?.Dispose();
        embeddedServer = null;
    }

    private IEnumerator ApplyPreset(string presetName)
    {
        using UnityWebRequest request = UnityWebRequest.Get($"{MockBaseUrl}/preset/{presetName}");
        request.timeout = 2;
        yield return request.SendWebRequest();
        lastRequestSucceeded = request.result == UnityWebRequest.Result.Success;
    }

    private static bool HasCompleteRig(PuppetBoneTest puppet)
    {
        return puppet.head != null &&
               puppet.spine != null &&
               puppet.leftShoulder != null &&
               puppet.rightShoulder != null &&
               puppet.leftArm != null &&
               puppet.leftForeArm != null &&
               puppet.leftHand != null &&
               puppet.rightArm != null &&
               puppet.rightForeArm != null &&
               puppet.rightHand != null;
    }

    private static float ArmLength(Transform upper, Transform lower, Transform hand)
    {
        return Vector3.Distance(upper.position, lower.position) +
               Vector3.Distance(lower.position, hand.position);
    }

    private static float ElbowFlexDegrees(
        Transform upper,
        Transform lower,
        Transform hand
    )
    {
        return Vector3.Angle(
            lower.position - upper.position,
            hand.position - lower.position
        );
    }

    private static float IdleDropAngle(
        PuppetBoneTest puppet,
        Transform upper,
        Vector3 handPosition
    )
    {
        Vector3 direction = handPosition - upper.position;
        if (direction.sqrMagnitude < 0.000001f)
            return 0f;
        float vertical = Vector3.Dot(direction.normalized, puppet.transform.up);
        return Mathf.Asin(Mathf.Clamp(-vertical, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private static float MaximumMovement(
        PuppetBoneTest puppet,
        Vector3 neutralRoot,
        Vector3 neutralLeftHand,
        Vector3 neutralRightHand
    )
    {
        return Mathf.Max(
            Vector3.Distance(neutralRoot, puppet.transform.localPosition),
            Vector3.Distance(neutralLeftHand, puppet.leftHand.position),
            Vector3.Distance(neutralRightHand, puppet.rightHand.position)
        );
    }

    private static PoseSnapshot CapturePose(PuppetBoneTest puppet)
    {
        return new PoseSnapshot
        {
            root = puppet.transform.localPosition,
            leftHand = puppet.leftHand.position,
            rightHand = puppet.rightHand.position,
            head = puppet.head.localRotation,
            spine = puppet.spine.localRotation,
            leftShoulder = puppet.leftShoulder.localRotation,
            rightShoulder = puppet.rightShoulder.localRotation,
            leftArm = puppet.leftArm.localRotation,
            rightArm = puppet.rightArm.localRotation
        };
    }

    private static IEnumerator ObserveForSeconds(
        PuppetBoneTest puppet,
        PoseSnapshot baseline,
        float maxSeconds,
        bool stopWhenSettled,
        MotionObservation observation,
        Vector3 projectionAxis,
        ElbowObservation elbowObservation = null
    )
    {
        float referenceLength = Mathf.Max(
            ArmLength(puppet.leftArm, puppet.leftForeArm, puppet.leftHand),
            ArmLength(puppet.rightArm, puppet.rightForeArm, puppet.rightHand)
        );
        float elapsed = 0f;
        float stableFor = 0f;
        int leftDirection = 0;
        int rightDirection = 0;
        int leftSegmentReversals = 0;
        int rightSegmentReversals = 0;
        PoseSnapshot previous = CapturePose(puppet);

        observation.maximumForward = 0f;
        observation.minimumForward = 0f;
        while (elapsed < maxSeconds)
        {
            yield return null;
            PoseSnapshot current = CapturePose(puppet);
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            elapsed += deltaTime;

            float linearDelta = Mathf.Max(
                Vector3.Distance(previous.root, current.root),
                Vector3.Distance(previous.leftHand, current.leftHand),
                Vector3.Distance(previous.rightHand, current.rightHand)
            );
            float linearSpeed = linearDelta / deltaTime;
            observation.maxLinearSpeed = Mathf.Max(observation.maxLinearSpeed, linearSpeed);
            observation.maxLeftDisplacement = Mathf.Max(
                observation.maxLeftDisplacement,
                Vector3.Distance(baseline.leftHand, current.leftHand)
            );
            observation.maxRightDisplacement = Mathf.Max(
                observation.maxRightDisplacement,
                Vector3.Distance(baseline.rightHand, current.rightHand)
            );

            float angularDelta = Mathf.Max(
                Quaternion.Angle(previous.head, current.head),
                Quaternion.Angle(previous.spine, current.spine),
                Quaternion.Angle(previous.leftArm, current.leftArm),
                Quaternion.Angle(previous.rightArm, current.rightArm)
            );
            float angularSpeed = angularDelta / deltaTime;
            observation.maxAngularSpeed = Mathf.Max(observation.maxAngularSpeed, angularSpeed);

            if (projectionAxis.sqrMagnitude > 0.000001f)
            {
                Vector3 axis = projectionAxis.normalized;
                float leftVelocity = Vector3.Dot(current.leftHand - previous.leftHand, axis) / deltaTime;
                float rightVelocity = Vector3.Dot(current.rightHand - previous.rightHand, axis) / deltaTime;
                float reversalSpeedThreshold = referenceLength * 0.04f;

                if (Mathf.Abs(leftVelocity) > reversalSpeedThreshold)
                {
                    int direction = leftVelocity > 0f ? 1 : -1;
                    if (leftDirection != 0 && direction != leftDirection)
                        leftSegmentReversals++;
                    leftDirection = direction;
                }

                if (Mathf.Abs(rightVelocity) > reversalSpeedThreshold)
                {
                    int direction = rightVelocity > 0f ? 1 : -1;
                    if (rightDirection != 0 && direction != rightDirection)
                        rightSegmentReversals++;
                    rightDirection = direction;
                }

                float forward = Vector3.Dot(current.leftHand - baseline.leftHand, axis);
                observation.maximumForward = Mathf.Max(observation.maximumForward, forward);
                observation.minimumForward = Mathf.Min(observation.minimumForward, forward);
                observation.lastForward = forward;
            }

            elbowObservation?.Sample(puppet);

            bool isStable = linearSpeed <= referenceLength * 0.08f && angularSpeed <= 8f;
            stableFor = isStable ? stableFor + deltaTime : 0f;
            if (stopWhenSettled && stableFor >= 0.12f)
            {
                AddExcessReversals(
                    observation,
                    leftSegmentReversals,
                    rightSegmentReversals
                );
                observation.settleTime = Mathf.Max(0f, elapsed - stableFor);
                yield break;
            }

            previous = current;
        }

        AddExcessReversals(observation, leftSegmentReversals, rightSegmentReversals);
        if (stopWhenSettled)
            observation.settleTime = -1f;
    }

    private static void AddExcessReversals(
        MotionObservation observation,
        int leftSegmentReversals,
        int rightSegmentReversals
    )
    {
        // A two-bone IK chain can cross one projected axis while following a
        // smooth arc. Only repeated reversals in the same command are jitter.
        observation.leftVelocityReversals += Mathf.Max(0, leftSegmentReversals - 1);
        observation.rightVelocityReversals += Mathf.Max(0, rightSegmentReversals - 1);
    }

    private static IEnumerator WaitForArmChainsToSettle(
        PuppetBoneTest puppet,
        float maximumWait
    )
    {
        float referenceLength = Mathf.Max(
            ArmLength(puppet.leftArm, puppet.leftForeArm, puppet.leftHand),
            ArmLength(puppet.rightArm, puppet.rightForeArm, puppet.rightHand)
        );
        float speedThreshold = referenceLength * 0.01f;
        float elapsed = 0f;
        float stableFor = 0f;
        Vector3 previousLeft = puppet.leftHand.position - puppet.leftArm.position;
        Vector3 previousRight = puppet.rightHand.position - puppet.rightArm.position;

        while (elapsed < maximumWait)
        {
            yield return null;
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            elapsed += deltaTime;
            Vector3 currentLeft = puppet.leftHand.position - puppet.leftArm.position;
            Vector3 currentRight = puppet.rightHand.position - puppet.rightArm.position;
            float speed = Mathf.Max(
                Vector3.Distance(previousLeft, currentLeft),
                Vector3.Distance(previousRight, currentRight)
            ) / deltaTime;

            stableFor = speed <= speedThreshold ? stableFor + deltaTime : 0f;
            if (stableFor >= 0.2f)
                yield break;

            previousLeft = currentLeft;
            previousRight = currentRight;
        }
    }

    private static Vector3 ElbowBendDirection(
        Transform upper,
        Transform lower,
        Transform end
    )
    {
        if (upper == null || lower == null || end == null)
            return Vector3.zero;

        Vector3 chain = end.position - upper.position;
        Vector3 bend = Vector3.ProjectOnPlane(lower.position - upper.position, chain);
        if (bend.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        Vector3 localBend = upper.parent != null
            ? upper.parent.InverseTransformDirection(bend)
            : bend;
        return localBend.normalized;
    }

    private static void WriteReportAndExit(IntegrationReport report)
    {
        string reportPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "PuppetChannelIntegrationReport.json"
        );
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        Debug.Log($"Puppet channel integration test: {(report.success ? "PASS" : "FAIL")} - {reportPath}");

#if UNITY_EDITOR
        bool commandLineRun = Array.Exists(
            Environment.GetCommandLineArgs(),
            argument => argument == "-puppetChannelIntegrationTest"
        );
        if (commandLineRun)
            UnityEditor.EditorApplication.Exit(report.success ? 0 : 2);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(report.success ? 0 : 2);
#endif
    }

    private sealed class EmbeddedMockPuppetServer : IDisposable
    {
        private static readonly Dictionary<string, string> Presets =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["neutral"] = StateJson(),
                ["head-direction"] = StateJson(
                    "\"headPitch\":0.8,\"headYaw\":-0.65,\"headRoll\":0.4"
                ),
                ["left-hand"] = StateJson(
                    "\"leftHandX\":0.55,\"leftHandY\":0.8,\"leftHandZ\":-0.45"
                ),
                ["right-hand"] = StateJson(
                    "\"rightHandX\":-0.55,\"rightHandY\":0.8,\"rightHandZ\":-0.45"
                ),
                ["center-below-threshold"] = StateJson(
                    "\"leftArm\":0.4,\"rightArm\":0.4"
                ),
                ["left-curl"] = StateJson(
                    "\"leftArm\":1.0,\"leftArmVelocity\":6.0"
                ),
                ["right-curl"] = StateJson(
                    "\"rightArm\":1.0,\"rightArmVelocity\":6.0"
                ),
                ["left-center-palm-shift"] = StateJson(
                    "\"leftArm\":1.0,\"bodyX\":0.45,\"bodyY\":0.35,\"bodyDepth\":0.2"
                ),
                ["right-center-palm-shift"] = StateJson(
                    "\"rightArm\":1.0,\"bodyX\":-0.45,\"bodyY\":0.35,\"bodyDepth\":0.2"
                ),
                ["forward-hands"] = StateJson(
                    "\"leftHandZ\":-0.8,\"rightHandZ\":0.8"
                ),
                ["flexible-left-forward"] = StateJson(
                    "\"leftHandX\":0.35,\"leftHandY\":0.6,\"leftHandZ\":-0.85"
                ),
                ["synergy-left"] = StateJson(
                    "\"leftHandX\":0.5,\"leftHandY\":0.7,\"leftHandZ\":-0.55"
                ),
                ["elbow-sweep-left"] = StateJson(
                    "\"leftHandX\":-0.45,\"leftHandY\":0.35,\"leftHandZ\":-0.2"
                ),
                ["elbow-sweep-forward"] = StateJson(
                    "\"leftHandX\":0.1,\"leftHandY\":0.85,\"leftHandZ\":-0.85"
                ),
                ["body"] = StateJson(
                    "\"bodyX\":0.6,\"bodyY\":0.5,\"bodyDepth\":0.35," +
                    "\"bodyTurn\":0.8,\"bodyLean\":-0.6"
                ),
                ["palm-arms"] = StateJson(
                    "\"bodyX\":0.65,\"bodyY\":0.55,\"bodyDepth\":0.45"
                ),
                ["uncalibrated"] = StateJson(
                    "\"calibrated\":false,\"calibrationProgress\":0.5," +
                    "\"leftHandX\":1.0,\"rightHandY\":1.0,\"headPitch\":1.0,\"bodyX\":1.0"
                ),
                ["hand-lost"] = StateJson("\"handDetected\":false"),
            };

        private readonly object stateLock = new object();
        private TcpListener listener;
        private Thread thread;
        private volatile bool running;
        private string currentState = Presets["neutral"];

        public bool TryStart()
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 5000);
                listener.Start();
                running = true;
                thread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "Puppet Integration Mock HTTP"
                };
                thread.Start();
                return true;
            }
            catch (SocketException)
            {
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            running = false;
            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
            }
            listener = null;
            if (thread != null && thread.IsAlive)
                thread.Join(500);
            thread = null;
        }

        private void ListenLoop()
        {
            while (running)
            {
                try
                {
                    using TcpClient client = listener.AcceptTcpClient();
                    Handle(client);
                }
                catch (SocketException)
                {
                    if (running)
                        Thread.Sleep(20);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 1000;
            client.SendTimeout = 1000;
            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new StreamReader(
                stream,
                Encoding.ASCII,
                false,
                1024,
                true
            );

            string requestLine = reader.ReadLine();
            string header;
            do
            {
                header = reader.ReadLine();
            }
            while (!string.IsNullOrEmpty(header));

            string path = RequestPath(requestLine);
            string json;
            int status;
            if (path == "/health")
            {
                json = "{\"status\":\"ok\"}";
                status = 200;
            }
            else if (path == "/puppet_state")
            {
                lock (stateLock)
                    json = currentState;
                status = 200;
            }
            else if (path.StartsWith("/preset/", StringComparison.Ordinal))
            {
                string presetName = path.Substring("/preset/".Length);
                if (Presets.TryGetValue(presetName, out string preset))
                {
                    lock (stateLock)
                        currentState = preset;
                    json = preset;
                    status = 200;
                }
                else
                {
                    json = "{\"error\":\"unknown preset\"}";
                    status = 404;
                }
            }
            else
            {
                json = "{\"error\":\"not found\"}";
                status = 404;
            }

            byte[] body = Encoding.UTF8.GetBytes(json);
            string statusText = status == 200 ? "OK" : "Not Found";
            byte[] responseHeader = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {statusText}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n"
            );
            stream.Write(responseHeader, 0, responseHeader.Length);
            stream.Write(body, 0, body.Length);
        }

        private static string RequestPath(string requestLine)
        {
            if (string.IsNullOrWhiteSpace(requestLine))
                return "/";
            string[] parts = requestLine.Split(' ');
            return parts.Length >= 2 ? parts[1] : "/";
        }

        private static string StateJson(string overrides = null)
        {
            const string defaults =
                "\"version\":2,\"handDetected\":true,\"calibrated\":true," +
                "\"calibrationProgress\":1.0,\"sequenceId\":0,\"sampleDt\":0.033," +
                "\"leftArmVelocity\":0.0,\"rightArmVelocity\":0.0," +
                "\"headNod\":0.0,\"leftArm\":0.0," +
                "\"rightArm\":0.0,\"bodyTurn\":0.0,\"moveX\":0.0," +
                "\"leftHandX\":0.0,\"leftHandY\":0.0,\"leftHandZ\":0.0," +
                "\"rightHandX\":0.0,\"rightHandY\":0.0,\"rightHandZ\":0.0," +
                "\"headPitch\":0.0,\"headYaw\":0.0,\"headRoll\":0.0," +
                "\"bodyX\":0.0,\"bodyY\":0.0,\"bodyDepth\":0.0,\"bodyLean\":0.0";
            if (string.IsNullOrEmpty(overrides))
                return "{" + defaults + "}";

            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (string field in defaults.Split(','))
            {
                int separator = field.IndexOf(':');
                values[field.Substring(0, separator)] = field.Substring(separator + 1);
            }
            foreach (string field in overrides.Split(','))
            {
                int separator = field.IndexOf(':');
                values[field.Substring(0, separator)] = field.Substring(separator + 1);
            }

            StringBuilder result = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!first)
                    result.Append(',');
                result.Append(pair.Key).Append(':').Append(pair.Value);
                first = false;
            }
            return result.Append('}').ToString();
        }
    }
}
