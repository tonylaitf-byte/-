using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace PalmTheatre.DuetGestures
{
    [DefaultExecutionOrder(-120)]
    public sealed class DuetGestureSession : MonoBehaviour
    {
        [FormerlySerializedAs("leftMusician")]public DuetGestureActor leftActor;
        [FormerlySerializedAs("rightWarrior")]public DuetGestureActor rightActor;
        public BackgroundMusicPlayer backgroundMusic;
        public AudioSource voice;
        public string endpoint="http://127.0.0.1:5000/duet_state";
        public bool pollServer=true,automaticUpdate=true,enableAudio=true;
        public bool Connected {get;private set;}
        public bool Paused {get;private set;}
        public DuetControlState LatestState {get;private set;}
        public DuetGestureActor Speaker {get;private set;}
        public string Subtitle => Speaker!=null?Speaker.CurrentLine?.subtitle??"":"";
        public string Message {get;private set;}="等待雙人手勢服務";
        public int SpokenCount {get;private set;}
        public AudioSource MusicSource => backgroundMusic?backgroundMusic.GetComponentInChildren<AudioSource>():null;
        DuetGestureActor Warrior => leftActor.musician?rightActor:leftActor;
        DuetGestureActor Musician => leftActor.musician?leftActor:rightActor;
        long order;
        float voiceElapsed;
        bool previousBackground;
        Coroutine polling;
        public float VoiceRemaining => Speaker==null?0:Mathf.Max(0,Speaker.CurrentLine.voice.length-voiceElapsed);
        void Awake(){previousBackground=Application.runInBackground;Application.runInBackground=true;voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=0;}
        void OnEnable(){if(pollServer)polling=StartCoroutine(Poll());}
        void OnDisable(){if(polling!=null)StopCoroutine(polling);polling=null;Connected=false;LatestState=null;StopAll();leftActor.Accept(null);rightActor.Accept(null);}
        void OnDestroy(){Application.runInBackground=previousBackground;}
        void Update(){if(automaticUpdate)Tick(Time.deltaTime);}
        public void AcceptState(DuetControlState state)
        {
            LatestState=state;Connected=state!=null&&state.version==1&&state.controlMode=="duet";
            leftActor.Accept(Connected?state.left:null);rightActor.Accept(Connected?state.right:null);
            Message=Connected?"雙人手勢已連線 · 左俠客／右琴師":"請開啟雙人手勢網頁";
        }
        public void MarkDisconnected(){Connected=false;LatestState=null;leftActor.Accept(null);rightActor.Accept(null);Message="雙人服務未連線；本次動作與台詞繼續完成";}
        float EstimatedVoiceDelay()
        {
            float delay=VoiceRemaining;
            foreach(var actor in new[]{Warrior,Musician})if(actor.VoiceWaiting)delay+=actor.CurrentLine.voice.length;
            return delay;
        }
        public bool StartGesture(DuetGestureActor actor,int gesture)
        {
            if(Paused||actor==null)return false;
            return actor.StartGesture(gesture,EstimatedVoiceDelay(),++order);
        }
        public void Tick(float dt)
        {
            if(Paused||dt<=0)return;
            if(Speaker!=null)
            {
                voiceElapsed+=dt;
                if(voiceElapsed>=Speaker.CurrentLine.voice.length&&(!enableAudio||!voice.isPlaying))
                {Speaker.VoiceEnded();Speaker=null;voice.Stop();}
            }
            leftActor.Advance(dt);rightActor.Advance(dt);
            if(Connected)
            {
                // If both poses arrive together, the warrior's opening line precedes the musician.
                foreach(var actor in new[]{Warrior,Musician})
                    if(actor.ReadyGesture>0)StartGesture(actor,actor.ReadyGesture);
            }
            if(Speaker==null)
            {
                foreach(var actor in new[]{Warrior,Musician})
                    if(actor.VoiceWaiting&&(Speaker==null||actor.VoiceOrder<Speaker.VoiceOrder))Speaker=actor;
                if(Speaker!=null)
                {
                    voiceElapsed=0;Speaker.VoiceStarted();SpokenCount++;
                    voice.clip=Speaker.CurrentLine.voice;if(enableAudio)voice.Play();
                }
            }
            if(backgroundMusic)backgroundMusic.SetVoiceActive(Speaker!=null);
        }
        public void SetPaused(bool paused)
        {
            Paused=paused;var music=MusicSource;
            if(paused){voice.Pause();if(music)music.Pause();}else{voice.UnPause();if(music)music.UnPause();}
        }
        public void StopAll(){voice.Stop();Speaker=null;SetPaused(false);leftActor.StopPerformance();rightActor.StopPerformance();if(backgroundMusic)backgroundMusic.SetVoiceActive(false);}
        IEnumerator Poll()
        {
            while(enabled&&pollServer)
            {
                using(var request=UnityWebRequest.Get(endpoint))
                {
                    request.timeout=2;yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        DuetControlState state=null;try{state=JsonUtility.FromJson<DuetControlState>(request.downloadHandler.text);}catch(ArgumentException){}
                        if(state!=null)AcceptState(state);else MarkDisconnected();
                    }
                    else MarkDisconnected();
                }
                yield return new WaitForSecondsRealtime(Connected?.033f:.25f);
            }
        }
    }
}
