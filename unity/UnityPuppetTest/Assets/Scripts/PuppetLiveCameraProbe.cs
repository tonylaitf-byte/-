using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public sealed class PuppetLiveCameraProbe : MonoBehaviour
{
    [Serializable]
    private sealed class CameraHealth
    {
        public string status = "";
        public bool cameraOpened = false;
        public bool calibrated = false;
        public float calibrationProgress = 0f;
    }

    [Serializable]
    private sealed class LiveReport
    {
        public bool success;
        public bool cameraOpened;
        public bool channelConnected;
        public bool v2PayloadReceived;
        public bool handObserved;
        public bool calibrationCompleted;
        public int handFrames;
        public float maximumCalibrationProgress;
        public float maximumLeftHandInput;
        public float maximumRightHandInput;
        public float maximumLeftForwardInput;
        public float maximumRightForwardInput;
        public float maximumHeadInput;
        public float maximumBodyInput;
        public float leftHandDisplacement;
        public float rightHandDisplacement;
        public float leftForwardReach;
        public float rightForwardReach;
        public float leftForwardLift;
        public float rightForwardLift;
        public float headAngle;
        public float bodyDisplacement;
        public float bodyAngle;
        public string[] errors;
    }

    private const string HealthEndpoint = "http://127.0.0.1:5000/health";
    private const float HandWaitTimeout = 90f;
    private const float CaptureDuration = 40f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWhenRequested()
    {
        string requestPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "RunPuppetLiveCamera.request"
        );
        if (!File.Exists(requestPath))
            return;

        File.Delete(requestPath);
        new GameObject("Puppet Live Camera Probe").AddComponent<PuppetLiveCameraProbe>();
    }

    private IEnumerator Start()
    {
        List<string> errors = new List<string>();
        CameraHealth health = null;
        using (UnityWebRequest request = UnityWebRequest.Get(HealthEndpoint))
        {
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                health = JsonUtility.FromJson<CameraHealth>(request.downloadHandler.text);
        }

        PuppetBoneTest puppet = FindAnyObjectByType<PuppetBoneTest>();
        PuppetHttpReceiver receiver = puppet != null
            ? puppet.GetComponent<PuppetHttpReceiver>()
            : null;
        if (puppet == null || receiver == null)
        {
            WriteReportAndStop(new LiveReport
            {
                success = false,
                cameraOpened = health != null && health.cameraOpened,
                errors = new[] { "Unity could not find the puppet or HTTP receiver." }
            });
            yield break;
        }

        Vector3 neutralLeftHand = puppet.leftHand.position;
        Vector3 neutralRightHand = puppet.rightHand.position;
        Vector3 neutralLeftHandLocal = puppet.transform.InverseTransformPoint(
            puppet.leftHand.position
        );
        Vector3 neutralRightHandLocal = puppet.transform.InverseTransformPoint(
            puppet.rightHand.position
        );
        Quaternion neutralHead = puppet.head.localRotation;
        Vector3 neutralRoot = puppet.transform.localPosition;
        Quaternion neutralSpine = puppet.spine.localRotation;

        bool channelConnected = false;
        bool v2PayloadReceived = false;
        bool handObserved = false;
        bool calibrationCompleted = false;
        int handFrames = 0;
        float maximumCalibrationProgress = 0f;
        float maximumLeftHandInput = 0f;
        float maximumRightHandInput = 0f;
        float maximumLeftForwardInput = 0f;
        float maximumRightForwardInput = 0f;
        float maximumHeadInput = 0f;
        float maximumBodyInput = 0f;
        float leftHandDisplacement = 0f;
        float rightHandDisplacement = 0f;
        float leftForwardReach = 0f;
        float rightForwardReach = 0f;
        float leftForwardLift = 0f;
        float rightForwardLift = 0f;
        float headAngle = 0f;
        float bodyDisplacement = 0f;
        float bodyAngle = 0f;
        float waitStartTime = Time.realtimeSinceStartup;
        float captureStartTime = -1f;

        while (true)
        {
            float currentTime = Time.realtimeSinceStartup;
            channelConnected |= receiver.IsConnected;
            PuppetControlState state = receiver.LatestState;
            if (state != null)
            {
                v2PayloadReceived |= state.version >= 2;
                maximumCalibrationProgress = Mathf.Max(
                    maximumCalibrationProgress,
                    state.calibrationProgress
                );
                if (state.handDetected)
                {
                    handObserved = true;
                    handFrames++;
                    if (captureStartTime < 0f)
                        captureStartTime = currentTime;
                }
                calibrationCompleted |= state.calibrated;
                if (state.calibrated && state.handDetected)
                {
                    maximumLeftHandInput = Mathf.Max(
                        maximumLeftHandInput,
                        Magnitude(state.leftHandX, state.leftHandY, state.leftHandZ)
                    );
                    maximumRightHandInput = Mathf.Max(
                        maximumRightHandInput,
                        Magnitude(state.rightHandX, state.rightHandY, state.rightHandZ)
                    );
                    maximumLeftForwardInput = Mathf.Max(
                        maximumLeftForwardInput,
                        state.leftHandZ
                    );
                    maximumRightForwardInput = Mathf.Max(
                        maximumRightForwardInput,
                        state.rightHandZ
                    );
                    maximumHeadInput = Mathf.Max(
                        maximumHeadInput,
                        Magnitude(state.headPitch, state.headYaw, state.headRoll)
                    );
                    maximumBodyInput = Mathf.Max(
                        maximumBodyInput,
                        Magnitude(state.bodyX, state.bodyY, state.bodyDepth),
                        Mathf.Abs(state.bodyTurn),
                        Mathf.Abs(state.bodyLean)
                    );
                }
            }

            leftHandDisplacement = Mathf.Max(
                leftHandDisplacement,
                Vector3.Distance(neutralLeftHand, puppet.leftHand.position)
            );
            rightHandDisplacement = Mathf.Max(
                rightHandDisplacement,
                Vector3.Distance(neutralRightHand, puppet.rightHand.position)
            );
            Vector3 leftHandLocalDelta =
                puppet.transform.InverseTransformPoint(puppet.leftHand.position) -
                neutralLeftHandLocal;
            Vector3 rightHandLocalDelta =
                puppet.transform.InverseTransformPoint(puppet.rightHand.position) -
                neutralRightHandLocal;
            leftForwardReach = Mathf.Max(leftForwardReach, leftHandLocalDelta.z);
            rightForwardReach = Mathf.Max(rightForwardReach, rightHandLocalDelta.z);
            leftForwardLift = Mathf.Max(leftForwardLift, leftHandLocalDelta.y);
            rightForwardLift = Mathf.Max(rightForwardLift, rightHandLocalDelta.y);
            headAngle = Mathf.Max(
                headAngle,
                Quaternion.Angle(neutralHead, puppet.head.localRotation)
            );
            bodyDisplacement = Mathf.Max(
                bodyDisplacement,
                Vector3.Distance(neutralRoot, puppet.transform.localPosition)
            );
            bodyAngle = Mathf.Max(
                bodyAngle,
                Quaternion.Angle(neutralSpine, puppet.spine.localRotation)
            );

            if (
                captureStartTime < 0f &&
                currentTime - waitStartTime >= HandWaitTimeout
            )
            {
                break;
            }
            if (
                captureStartTime >= 0f &&
                currentTime - captureStartTime >= CaptureDuration
            )
            {
                break;
            }
            yield return null;
        }

        bool cameraOpened = health != null && health.cameraOpened;
        if (!cameraOpened)
            errors.Add("The physical camera did not open.");
        if (!channelConnected)
            errors.Add("Unity never connected to the live /puppet_state endpoint.");
        if (!v2PayloadReceived)
            errors.Add("Unity did not receive a V2 control payload.");
        if (!handObserved)
            errors.Add("No hand was observed by the physical camera.");
        if (!calibrationCompleted)
            errors.Add("The neutral-pose calibration did not complete.");
        if (maximumLeftHandInput < 0.08f || leftHandDisplacement < 0.01f)
            errors.Add("Live thumb movement did not drive the puppet's left hand.");
        if (maximumRightHandInput < 0.08f || rightHandDisplacement < 0.01f)
            errors.Add("Live pinky movement did not drive the puppet's right hand.");
        if (maximumLeftForwardInput < 0.08f || leftForwardReach < 0.03f)
            errors.Add("Live thumb depth did not move the puppet's left arm forward.");
        if (maximumRightForwardInput < 0.08f || rightForwardReach < 0.03f)
            errors.Add("Live pinky depth did not move the puppet's right arm forward.");
        if (leftForwardLift < 0.02f)
            errors.Add("The left forward gesture did not visibly lift the arm.");
        if (rightForwardLift < 0.02f)
            errors.Add("The right forward gesture did not visibly lift the arm.");
        if (maximumHeadInput < 0.08f || headAngle < 2f)
            errors.Add("Live middle-finger movement did not drive the puppet's head.");
        if (
            maximumBodyInput < 0.08f ||
            (bodyDisplacement < 0.01f && bodyAngle < 2f)
        )
        {
            errors.Add("Live palm movement did not drive the puppet's body.");
        }

        WriteReportAndStop(new LiveReport
        {
            success = errors.Count == 0,
            cameraOpened = cameraOpened,
            channelConnected = channelConnected,
            v2PayloadReceived = v2PayloadReceived,
            handObserved = handObserved,
            calibrationCompleted = calibrationCompleted,
            handFrames = handFrames,
            maximumCalibrationProgress = maximumCalibrationProgress,
            maximumLeftHandInput = maximumLeftHandInput,
            maximumRightHandInput = maximumRightHandInput,
            maximumLeftForwardInput = maximumLeftForwardInput,
            maximumRightForwardInput = maximumRightForwardInput,
            maximumHeadInput = maximumHeadInput,
            maximumBodyInput = maximumBodyInput,
            leftHandDisplacement = leftHandDisplacement,
            rightHandDisplacement = rightHandDisplacement,
            leftForwardReach = leftForwardReach,
            rightForwardReach = rightForwardReach,
            leftForwardLift = leftForwardLift,
            rightForwardLift = rightForwardLift,
            headAngle = headAngle,
            bodyDisplacement = bodyDisplacement,
            bodyAngle = bodyAngle,
            errors = errors.ToArray()
        });
    }

    private static float Magnitude(float x, float y, float z)
    {
        return Mathf.Sqrt(x * x + y * y + z * z);
    }

    private static void WriteReportAndStop(LiveReport report)
    {
        string reportPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "PuppetLiveCameraReport.json"
        );
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        Debug.Log(
            $"Puppet live camera test: {(report.success ? "PASS" : "FAIL")} - {reportPath}"
        );

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(report.success ? 0 : 2);
#endif
    }
}
