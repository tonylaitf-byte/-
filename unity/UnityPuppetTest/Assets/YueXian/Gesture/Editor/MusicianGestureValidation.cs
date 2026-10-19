using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using PalmTheatre.MusicianGesture;

public static class MusicianGestureValidation
{
    const string Pending="MusicianGestureValidation.Pending";
    static IEnumerator routine;static int frame=-1,errors;static Report report;
    [Serializable] class Report
    {
        public bool passed,automaticCaptureTrigger,duplicateIgnored,noInterruption,noQueue,latestPoseAfterCompletion;
        public bool modeGuard,reconnectGuard,voiceTiming,continuousLoops,pauseWorks,stopWorks,httpTransport,realAudio,subtitles;
        public bool horizontalMovement,movementDuringActions,movementBounds,movementRecovery,audioProtected,backgroundPolling;
        public int simulatedFrames,errors;public string failure;public float[] voiceSeconds,actionSeconds;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(MusicianGestureBuilder.ScenePath);
        UnityEngine.Object.FindAnyObjectByType<MusicianGestureSession>().pollServer=false;
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        errors=0;Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;};
        report=new Report();EditorApplication.update+=Tick;
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;
        try
        {
            if(routine==null)routine=Check();
            if(routine.MoveNext())return;
            report.passed=errors==0;
        }
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        EditorApplication.update-=Tick;report.errors=errors;
        Directory.CreateDirectory("GestureReports");File.WriteAllText("GestureReports/Validation.json",JsonUtility.ToJson(report,true));
        SessionState.SetBool(Pending,false);Debug.Log(report.passed?"MUSICIAN_GESTURE_PASS":"MUSICIAN_GESTURE_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static PuppetControlState State(int gesture,int sequence=70,string mode="gesture",bool hand=true,float hold=1)
    {
        string[] actions={"BOW","LANDSCAPE","DRINK","ALERT","ATTACK"};
        return new PuppetControlState{version=2,handDetected=hand,controlMode=mode,actionSequence=sequence,
            gesture=gesture,gestureHold=hold,action=gesture>=1&&gesture<=5?actions[gesture-1]:"BOW"};
    }
    static IEnumerator Check()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<MusicianGestureSession>();
        s.automaticUpdate=false;s.enableAudio=false;s.manager.puppet.Initialize();
        Require(UnityEngine.Object.FindObjectsByType<Animator>().Length==1,"Expected only the second character.");
        Require(UnityEngine.Object.FindObjectsByType<AudioListener>().Length==1,"Expected one listener.");
        Require(UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>().Length==0,"Legacy receiver would write the wrong action system.");
        Require(Application.runInBackground,"Background polling is disabled.");report.backgroundPolling=true;
        Require(s.lines.Length==5&&s.lines.All(l=>l.clip&&l.clip.length>3&&l.clip.length<6),"Missing voice clips.");
        report.voiceSeconds=s.lines.Select(l=>l.clip.length).ToArray();
        report.actionSeconds=s.manager.actions.Select(a=>a.ResolveDuration()).ToArray();
        Action<int> advance=n=>{for(int f=0;f<n;f++){s.Tick(1f/60);report.simulatedFrames++;}};
        Action neutral=()=>s.AcceptState(State(0,hand:false,hold:0));
        Action stop=()=>{neutral();s.StopPerformance();advance(100);};

        neutral();Require(s.StartedCount==0,"Replayed stale action without a visible hand.");
        s.AcceptState(State(1,hold:.3f));Require(s.StartedCount==0,"Unconfirmed pose started an action.");
        // No action sequence increment or manual button is required for a live confirmed pose.
        s.AcceptState(State(1));Require(s.StartedCount==1&&s.CurrentAction==s.lines[0].actionId,"Camera pose did not trigger automatically.");report.automaticCaptureTrigger=true;
        s.AcceptState(State(1,71));s.AcceptState(State(1,72));Require(s.StartedCount==1,"Held pose restarted the action.");
        for(int i=2;i<=5;i++)
        {
            s.AcceptState(State(i,72+i));advance(3);
            Require(s.StartedCount==1&&s.CurrentAction==s.lines[0].actionId&&s.Subtitle==s.lines[0].subtitle,"Gesture interrupted the current action.");
        }
        Require(!s.StartGesture(2),"Manual button interrupted the current action.");report.noInterruption=true;
        int guard=0;while(s.StartedCount==1&&guard++<500)advance(1);
        Require(s.StartedCount==2&&s.CurrentAction==s.lines[4].actionId,"Latest current pose was not read after completion.");report.latestPoseAfterCompletion=true;
        advance(600);Require(s.StartedCount==2,"Held pose or intermediate poses were replayed.");report.duplicateIgnored=report.noQueue=true;
        stop();s.StartGesture(1);int before=s.StartedCount;
        s.AcceptState(State(4));neutral();advance(600);
        Require(s.StartedCount==before&&!s.Busy,"A gesture no longer in the camera was queued.");

        stop();s.StartGesture(1);s.SetPaused(true);float elapsed=s.manager.Elapsed;float offset=s.HorizontalOffset;
        s.AcceptState(State(5));advance(60);
        Require(s.Paused&&s.StartedCount==before+1&&Mathf.Abs(s.manager.Elapsed-elapsed)<.001f&&Mathf.Abs(s.HorizontalOffset-offset)<.001f,"Gesture bypassed pause.");
        neutral();s.SetPaused(false);report.pauseWorks=true;stop();

        var rest=s.transform.localPosition;
        for(int i=0;i<5;i++)
        {
            stop();Require(s.StartGesture(i+1),"Manual idle test action did not start.");int started=s.StartedCount;
            foreach(float direction in new[]{-1f,1f})
            {
                var moving=State(i+1);moving.bodyX=direction;s.AcceptState(moving);advance(60);
                Require(Mathf.Abs(s.HorizontalOffset-direction*s.horizontalRange)<.01f,"Horizontal movement failed "+i);
                Require(Mathf.Abs(s.transform.localPosition.x-rest.x-s.HorizontalOffset)<.001f,"Root did not follow movement.");
                Require(Mathf.Abs(s.transform.localPosition.y-rest.y)<.001f&&Mathf.Abs(s.transform.localPosition.z-rest.z)<.001f,"Movement changed ground height/depth.");
                Require(s.StartedCount==started&&s.Busy,"Movement restarted/stopped the action.");
            }
        }
        report.horizontalMovement=report.movementDuringActions=true;
        var extreme=State(5);extreme.bodyX=99;s.AcceptState(extreme);advance(90);
        Require(Mathf.Abs(s.HorizontalOffset-s.horizontalRange)<.001f,"Movement exceeded bounds.");
        extreme.bodyX=float.NaN;s.AcceptState(extreme);advance(90);Require(Mathf.Abs(s.HorizontalOffset)<.001f,"Invalid movement corrupted transform.");report.movementBounds=true;
        extreme.bodyX=-1;s.AcceptState(extreme);advance(60);extreme.handDetected=false;s.AcceptState(extreme);advance(90);
        Require(Mathf.Abs(s.HorizontalOffset)<.001f,"Hand loss did not return to center.");
        extreme.handDetected=true;extreme.bodyX=1;s.AcceptState(extreme);advance(60);s.MarkDisconnected();advance(90);
        Require(Mathf.Abs(s.HorizontalOffset)<.001f,"Disconnect did not return to center.");report.movementRecovery=true;

        for(int i=0;i<5;i++)
        {
            stop();s.StartGesture(i+1);
            int started=s.StartedCount;float duration=s.manager.Duration;
            if(!s.manager.actions[i].continuous)Require(duration>=s.lines[i].clip.length+.29f,"Animation finished before voice.");
            advance(Mathf.FloorToInt(s.lines[i].clip.length*60)-4);
            Require(s.StartedCount==started&&s.Subtitle==s.lines[i].subtitle,"Voice/subtitle interrupted early "+i);
            if(s.manager.actions[i].continuous)
            {
                int loopWait=0;while(!s.manager.IsLooping&&loopWait++<60)advance(1);
                Require(s.manager.IsLooping,"Playing action failed to loop.");
            }
            int wait=0;while(s.Busy&&wait++<180)advance(1);
            Require(!s.Busy,"Voice timing did not release the action.");
        }
        report.voiceTiming=report.continuousLoops=report.subtitles=true;
        stop();before=s.StartedCount;s.AcceptState(State(1,mode:"mapping"));advance(60);
        Require(s.StartedCount==before,"Mapping mode triggered an action.");report.modeGuard=true;
        s.MarkDisconnected();s.AcceptState(State(0,hand:false,hold:0));advance(30);
        Require(s.StartedCount==before,"Reconnect replayed a stale action.");
        s.AcceptState(State(3));Require(s.StartedCount==before+1,"Reconnect ignored a live confirmed hand.");report.reconnectGuard=true;
        s.StopPerformance();advance(600);Require(s.manager.IsIdle&&!s.Busy&&s.Subtitle==""&&s.StartedCount==before+1,"Stop retriggered the held gesture.");report.stopWorks=true;neutral();

        // Production polling receives a live hand at the first connection without any button click.
        s.enabled=false;s.endpoint="http://127.0.0.1:5129/puppet_state";s.pollServer=true;s.enabled=true;
        before=s.StartedCount;float deadline=Time.realtimeSinceStartup+5;
        while(!s.IsConnected&&Time.realtimeSinceStartup<deadline)yield return null;
        Require(s.IsConnected,"HTTP fixture not connected.");
        Require(s.StartedCount==before+1&&s.CurrentAction==s.lines[0].actionId,"First camera pose did not trigger through HTTP.");
        using(var request=UnityWebRequest.Get("http://127.0.0.1:5129/emit/5"))
        {var op=request.SendWebRequest();while(!op.isDone)yield return null;Require(request.result==UnityWebRequest.Result.Success,"Fixture emit failed.");}
        deadline=Time.realtimeSinceStartup+3;
        while(s.LatestState.gesture!=5&&Time.realtimeSinceStartup<deadline)yield return null;
        Require(s.StartedCount==before+1&&s.CurrentAction==s.lines[0].actionId,"HTTP gesture interrupted the current action.");
        using(var request=UnityWebRequest.Get("http://127.0.0.1:5129/move/-1"))
        {var op=request.SendWebRequest();while(!op.isDone)yield return null;Require(request.result==UnityWebRequest.Result.Success,"Fixture movement failed.");}
        deadline=Time.realtimeSinceStartup+3;
        while(s.LatestState.bodyX>-.9f&&Time.realtimeSinceStartup<deadline)yield return null;
        advance(60);Require(s.HorizontalOffset<-.8f&&s.StartedCount==before+1,"HTTP movement interrupted the action.");
        advance(300);Require(s.StartedCount==before+2&&s.CurrentAction=="MUSIC_PLAY_FAST","Current camera pose did not trigger after completion.");report.httpTransport=true;
        s.enabled=false;s.pollServer=false;s.enabled=true;stop();

        // Protect each actual WAV and subtitle while other camera gestures arrive.
        s.enableAudio=true;
        for(int i=0;i<5;i++)
        {
            stop();s.StartGesture(i+1);s.Tick(1f/60);yield return null;
            Require(s.voice.isPlaying&&s.voice.clip==s.lines[i].clip,"Audio did not start "+i);
            Require(s.Subtitle==s.lines[i].subtitle,"Real audio subtitle mismatch "+i);
            int started=s.StartedCount;
            s.AcceptState(State((i+1)%5+1));s.SetPaused(true);yield return null;
            Require(s.Paused&&!s.StartGesture((i+1)%5+1)&&s.voice.clip==s.lines[i].clip,"Gesture interrupted paused audio.");
            s.SetPaused(false);neutral();
            float start=Time.realtimeSinceStartup;
            while(s.Busy&&Time.realtimeSinceStartup-start<12)
            {
                if(Time.realtimeSinceStartup-start<1)
                {
                    s.AcceptState(State((i+1)%5+1));
                    Require(s.voice.clip==s.lines[i].clip&&s.Subtitle==s.lines[i].subtitle&&s.StartedCount==started,"Gesture interrupted live voice/subtitle.");
                }
                else neutral();
                s.Tick(Time.unscaledDeltaTime);yield return null;
            }
            Require(!s.Busy&&!s.voice.isPlaying&&s.Subtitle==""&&s.StartedCount==started,"Voice/subtitle did not finish alone "+i);
        }
        stop();report.realAudio=report.audioProtected=true;yield return null;
    }
}
