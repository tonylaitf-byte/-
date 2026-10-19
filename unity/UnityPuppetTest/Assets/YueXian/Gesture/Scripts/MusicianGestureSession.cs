using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using PalmTheatre.MusicianActions;

namespace PalmTheatre.MusicianGesture
{
    [Serializable] public sealed class MusicianVoiceLine
    {
        public string sourceAction, actionId;
        public AudioClip clip;
        [TextArea] public string taiwanese, subtitle;
    }

    [DefaultExecutionOrder(-100)]
    public sealed class MusicianGestureSession : MonoBehaviour
    {
        public MusicianActionManager manager;
        public AudioSource voice;
        public MusicianVoiceLine[] lines;
        public string endpoint="http://127.0.0.1:5000/puppet_state";
        public bool pollServer=true, automaticUpdate=true, enableAudio=true;
        [Header("Gesture movement")]
        [Min(0f)] public float horizontalRange=.9f;
        [Min(.01f)] public float movementSmoothTime=.14f;
        [Min(.01f)] public float maximumMovementSpeed=6f;
        public bool IsConnected {get;private set;}
        public bool Paused {get;private set;}
        public bool Busy {get;private set;}
        public float HorizontalOffset {get;private set;}
        public string Subtitle {get;private set;}="";
        public string CurrentAction {get;private set;}="";
        public int StartedCount {get;private set;}
        public PuppetControlState LatestState {get;private set;}
        public string Message {get;private set;}="等待手勢辨識服務";
        Vector3 restPosition;
        float movementTarget, movementVelocity;
        bool previousBackgroundSetting;
        int latchedGesture;
        float elapsed, minimumDuration, voiceDuration;
        Coroutine polling;

        void Awake()
        {
            manager.automaticUpdate=false;
            voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=0;voice.pitch=1;
            restPosition=transform.localPosition;
            previousBackgroundSetting=Application.runInBackground;
            Application.runInBackground=true;
        }
        void OnEnable(){latchedGesture=0;LatestState=null;if(pollServer)polling=StartCoroutine(Poll());}
        void OnDisable()
        {
            if(polling!=null)StopCoroutine(polling);
            polling=null;IsConnected=false;LatestState=null;latchedGesture=0;
            voice.Stop();Subtitle="";Busy=false;CurrentAction="";
            Paused=false;manager.Paused=false;
            movementTarget=movementVelocity=0;HorizontalOffset=0;
            var position=transform.localPosition;position.x=restPosition.x;transform.localPosition=position;
        }
        void OnDestroy(){Application.runInBackground=previousBackgroundSetting;}
        void Update(){if(automaticUpdate)Tick(Time.deltaTime);}
        public bool StartGesture(int number)
        {
            if(Busy||Paused||number<1||number>lines.Length)return false;
            if(!Begin(number-1))return false;
            latchedGesture=number;return true;
        }
        public void AcceptState(PuppetControlState state)
        {
            LatestState=state;IsConnected=true;
            if(!string.Equals(state.controlMode,"gesture",StringComparison.OrdinalIgnoreCase))
            {latchedGesture=0;movementTarget=0;Message="等待網頁切換至手勢模式";return;}
            // Movement is continuous and independent of actionSequence/voice playback.
            movementTarget=state.handDetected&&!float.IsNaN(state.bodyX)&&!float.IsInfinity(state.bodyX)
                ?Mathf.Clamp(state.bodyX,-1,1)*horizontalRange:0;
            Message=state.handDetected?"手勢辨識已連線":"已連線，等待鏡頭中的手";
            if(!state.handDetected||state.gesture!=latchedGesture)latchedGesture=0;
            TryStartTrackedGesture();
        }
        void TryStartTrackedGesture()
        {
            var state=LatestState;
            // Read the current confirmed pose when ready. Never store gestures for later playback.
            if(Busy||Paused||!IsConnected||state==null||!state.handDetected||
                !string.Equals(state.controlMode,"gesture",StringComparison.OrdinalIgnoreCase)||
                state.gesture<1||state.gesture>lines.Length||!(state.gestureHold>=.999f)||
                state.gesture==latchedGesture)return;
            if(StartGesture(state.gesture))latchedGesture=state.gesture;
        }
        public void MarkDisconnected(){IsConnected=false;LatestState=null;latchedGesture=0;movementTarget=0;Message="辨識服務未連線；目前動作會繼續播放";}
        public void Tick(float delta)
        {
            if(Paused||delta<=0)return;
            manager.Advance(delta);
            HorizontalOffset=Mathf.SmoothDamp(HorizontalOffset,movementTarget,ref movementVelocity,
                movementSmoothTime,maximumMovementSpeed,delta);
            var position=transform.localPosition;position.x=restPosition.x+HorizontalOffset;transform.localPosition=position;
            if(Busy)
            {
                elapsed+=delta;
                bool audioPlaying=enableAudio&&voice.isPlaying;
                if(elapsed>=voiceDuration&&!audioPlaying)Subtitle="";
                if(elapsed>=minimumDuration&&!audioPlaying){Busy=false;CurrentAction="";}
            }
            TryStartTrackedGesture();
        }
        bool Begin(int index)
        {
            var line=lines[index];
            if(line==null||!line.clip)return false;
            manager.CancelShowcase();manager.Paused=false;
            if(!manager.ExecuteAction(line.actionId,line.clip.length))return false;
            Paused=false;voice.Stop();
            CurrentAction=line.actionId;Subtitle=line.subtitle;StartedCount++;
            elapsed=0;voiceDuration=line.clip.length;
            minimumDuration=Mathf.Max(manager.Duration,voiceDuration+.3f);
            Busy=true;voice.clip=line.clip;if(enableAudio)voice.Play();
            return true;
        }
        public void SetPaused(bool value)
        {
            if(Paused==value)return;
            Paused=value;manager.Paused=value;
            if(value)voice.Pause();else voice.UnPause();
        }
        public void StopPerformance()
        {
            SetPaused(false);voice.Stop();Subtitle="";
            Busy=false;CurrentAction="";manager.StopAction();
            latchedGesture=LatestState?.gesture??0;
        }
        IEnumerator Poll()
        {
            while(enabled&&pollServer)
            {
                using(var request=UnityWebRequest.Get(endpoint))
                {
                    request.timeout=2;yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        PuppetControlState state=null;
                        try{state=JsonUtility.FromJson<PuppetControlState>(request.downloadHandler.text);}catch(ArgumentException){}
                        if(state!=null&&state.version>=2)AcceptState(state);else MarkDisconnected();
                    }
                    else MarkDisconnected();
                }
                yield return new WaitForSecondsRealtime(IsConnected?.033f:.3f);
            }
        }
    }
}
