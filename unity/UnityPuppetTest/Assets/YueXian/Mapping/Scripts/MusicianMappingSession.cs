using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace PalmTheatre.MusicianMapping
{
    [DefaultExecutionOrder(-100)]
    public sealed class MusicianMappingSession : MonoBehaviour
    {
        public PuppetBoneTest mapping;
        public PuppetHttpReceiver receiver;
        public Animator characterAnimator;
        public GameObject instrument;
        public bool showInstrument;
        public bool DemoMode { get; private set; }
        public bool Frozen { get; private set; }
        public string CalibrationMessage { get; private set; } = "";
        float demoStarted;
        bool calibrating;

        void Awake()
        {
            // Realtime IK is the only writer in this scene.
            characterAnimator.enabled = false;
            characterAnimator.runtimeAnimatorController = null;
            instrument.SetActive(showInstrument);
        }
        public void SetInstrumentVisible(bool value) { showInstrument=value;instrument.SetActive(value); }
        public void SetFrozen(bool value) { Frozen=value;mapping.enabled=!value; }
        public void SetDemo(bool value)
        {
            DemoMode=value;receiver.enabled=!value;demoStarted=Time.unscaledTime;
            mapping.ClearExternalInput();
        }
        void Update()
        {
            if(!DemoMode)return;
            float t=Time.unscaledTime-demoStarted;
            mapping.SetControlState(new PuppetControlState{
                version=2,handDetected=true,calibrated=true,controlMode="mapping",sequenceId=Time.frameCount,
                leftHandY=Mathf.Sin(t*1.3f)*.55f,rightHandY=Mathf.Sin(t*1.3f+1.3f)*.55f,
                leftHandZ=-.28f*(1+Mathf.Sin(t*.9f)),rightHandZ=.28f*(1+Mathf.Sin(t*.9f+1)),
                headYaw=Mathf.Sin(t*.6f)*.35f,headPitch=Mathf.Sin(t*.8f)*.20f,bodyTurn=Mathf.Sin(t*.5f)*.20f,
                sampleDt=Time.unscaledDeltaTime
            });
        }
        public void Calibrate() { if(!calibrating&&!DemoMode)StartCoroutine(CalibrateRoutine()); }
        IEnumerator CalibrateRoutine()
        {
            calibrating=true;CalibrationMessage="正在重新校正…";
            using(var request=UnityWebRequest.PostWwwForm("http://127.0.0.1:5000/calibrate",""))
            {
                request.timeout=3;yield return request.SendWebRequest();
                CalibrationMessage=request.result==UnityWebRequest.Result.Success?"請將手保持自然姿勢，等待校正完成。":"無法連接手部追蹤服務。";
            }
            calibrating=false;
        }
    }
}
