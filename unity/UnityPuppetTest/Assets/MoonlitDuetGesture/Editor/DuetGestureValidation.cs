using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using PalmTheatre.DuetGestures;

public static class DuetGestureValidation
{
    const string Pending="DuetGestureValidation.Pending";
    static IEnumerator routine;static int frame=-1,errors;static Report report;
    [Serializable] class Report
    {
        public bool passed,twoActors,tenLines,automaticTrigger,noInterruption,noActionQueue,voiceTurns,subtitles;
        public bool stageBounds,independentMovement,lostGrace,pause,stop,httpTransport,realAudio,groundedFacing;
        public bool soloEnvironment,backgroundMusic,musicDucking,musicPause,musicRecovery;
        public int simulatedFrames,errors;public string failure;public float[] voiceSeconds;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(DuetGestureBuilder.ScenePath);
        UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>().pollServer=false;
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        errors=0;Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;};
        report=new Report();EditorApplication.update+=Tick;
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(routine==null)routine=Check();if(routine.MoveNext())return;report.passed=errors==0;}
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        EditorApplication.update-=Tick;report.errors=errors;
        Directory.CreateDirectory("DuetReports");File.WriteAllText("DuetReports/Validation.json",JsonUtility.ToJson(report,true));
        SessionState.SetBool(Pending,false);Debug.Log(report.passed?"DUET_GESTURE_PASS":"DUET_GESTURE_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static DuetHandState Hand(int gesture=0,float movement=0,bool detected=true,bool held=true)=>new DuetHandState
        {gesture=gesture,gestureHold=gesture>0?1:0,bodyX=movement,handDetected=detected,ownerHeld=held,handedness="Right",ownerId=held?1:0};
    static DuetControlState State(int left=0,int right=0,float lx=0,float rx=0)=>new DuetControlState
        {version=1,controlMode="duet",left=Hand(left,lx),right=Hand(right,rx)};
    static IEnumerator Check()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();s.automaticUpdate=false;s.enableAudio=false;
        var left=s.leftActor;var right=s.rightActor;left.Initialize();right.Initialize();
        Require(UnityEngine.Object.FindObjectsByType<Animator>().Length==2,"Expected both models.");
        Require(UnityEngine.Object.FindObjectsByType<AudioSource>().Length==2&&UnityEngine.Object.FindObjectsByType<AudioListener>().Length==1,"Voice/music source or listener conflict: sources="+UnityEngine.Object.FindObjectsByType<AudioSource>().Length+", listeners="+UnityEngine.Object.FindObjectsByType<AudioListener>().Length);
        Require(s.MusicSource&&s.MusicSource.clip&&s.MusicSource.clip.name=="Chinese Music Preview - Marnix Wyns (youtube)"&&s.MusicSource.loop&&s.MusicSource.isPlaying,"Original solo BGM did not start.");report.backgroundMusic=true;
        Require(GameObject.Find("MoonlitTempleEnvironment").GetComponentsInChildren<Renderer>().Length>100&&GameObject.Find("MoonlitMountainBackdrop"),"Solo environment/backdrop missing.");report.soloEnvironment=true;
        Require(UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>().Length==0&&UnityEngine.Object.FindObjectsByType<PuppetBoneTest>().Length==0,"Legacy input conflicts with duet control.");
        Require(!left.musician&&right.musician&&left.transform.position.x<0&&right.transform.position.x>0,"Wrong swapped default side.");
        Require(left.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null))&&right.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null)),"Missing character materials.");report.twoActors=true;
        Require(left.lines.Length==5&&right.lines.Length==5&&left.lines.Concat(right.lines).All(l=>l.voice&&l.voice.length>2&&!string.IsNullOrEmpty(l.subtitle)),"Missing ten voice/subtitle lines.");
        report.voiceSeconds=Enumerable.Range(0,5).SelectMany(i=>new[]{left.lines[i].voice.length,right.lines[i].voice.length}).ToArray();report.tenLines=true;
        Action<int> advance=n=>{for(int f=0;f<n;f++){s.Tick(1f/60);report.simulatedFrames++;}};
        Action reset=()=>{s.AcceptState(State());s.StopAll();advance(100);};
        reset();Render(Camera.main,"DuetReports/Stage.png");
        var heroSkin=DuetGestureBuilder.SkinBounds(left.gameObject);
        var head=left.GetComponentsInChildren<Transform>().Single(t=>t.name=="Head");
        var nose=left.GetComponentsInChildren<Transform>().Single(t=>t.name=="headfront");
        float floorY=GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        Require(Mathf.Abs(heroSkin.min.y-floorY)<.02f&&Mathf.Abs(DuetGestureBuilder.SkinBounds(right.gameObject).min.y-floorY)<.02f,"Feet do not meet the courtyard.");
        Require(Vector3.Dot(Vector3.ProjectOnPlane(nose.position-head.position,Vector3.up).normalized,Vector3.back)>.8f,"Warrior faces away from the audience.");report.groundedFacing=true;
        var leftGround=left.transform.position;var rightGround=right.transform.position;
        s.AcceptState(State(1,1));advance(1);
        Require(left.Busy&&right.Busy&&s.Speaker==left,"Automatic simultaneous action / first voice failed.");report.automaticTrigger=true;
        int lc=left.StartedCount,rc=right.StartedCount;
        s.AcceptState(State(1,1,-99,99));advance(90);
        Require(Mathf.Abs(left.transform.position.x-left.minimumX)<.01f&&Mathf.Abs(right.transform.position.x-right.maximumX)<.01f,"Outer limits failed.");
        s.AcceptState(State(1,1,99,-99));advance(90);
        Require(Mathf.Abs(left.transform.position.x-left.maximumX)<.01f&&Mathf.Abs(right.transform.position.x-right.minimumX)<.01f,"Center limits failed.");
        Require(left.transform.position.x<0&&right.transform.position.x>0&&left.transform.position.y==leftGround.y&&right.transform.position.y==rightGround.y,"Crossed center or floated.");report.stageBounds=true;
        float rightX=right.transform.position.x;s.AcceptState(State(1,1,-.7f,-99));advance(30);
        Require(left.transform.position.x<-1.8f&&Mathf.Abs(right.transform.position.x-rightX)<.01f,"Movement channels not independent.");report.independentMovement=true;
        var occluded=State();occluded.left=Hand(0,-.7f,false,true);occluded.right=Hand(0,-1,false,true);s.AcceptState(occluded);advance(30);
        Require(left.transform.position.x<-1.8f&&right.transform.position.x<1,"Short loss did not preserve position.");
        occluded.left.ownerHeld=occluded.right.ownerHeld=false;s.AcceptState(occluded);advance(90);
        Require(Mathf.Abs(left.transform.position.x-left.homeX)<.01f&&Mathf.Abs(right.transform.position.x-right.homeX)<.01f,"Released hands did not return home.");report.lostGrace=true;
        s.SetPaused(true);float time=left.Elapsed;var speaker=s.Speaker;advance(30);
        Require(Mathf.Abs(left.Elapsed-time)<.001f&&s.Speaker==speaker&&!s.StartGesture(right,2),"Pause failed.");
        Require(!s.MusicSource.isPlaying,"Music did not pause.");s.SetPaused(false);Require(s.MusicSource.isPlaying,"Music did not resume.");report.pause=report.musicPause=true;
        reset();
        for(int i=1;i<=5;i++)
        {
            lc=left.StartedCount;rc=right.StartedCount;int spoken=s.SpokenCount;
            s.AcceptState(State(i,i));advance(1);
            Require(left.StartedCount==lc+1&&right.StartedCount==rc+1&&s.Speaker==left,"Gesture mapping failed "+i);
            Require(s.Subtitle==left.lines[i-1].subtitle,"Warrior subtitle mismatch.");
            s.AcceptState(State(i%5+1,i%5+1));advance(4);
            Require(left.StartedCount==lc+1&&right.StartedCount==rc+1&&left.CurrentLine==left.lines[i-1]&&right.CurrentLine==right.lines[i-1],"Gesture interrupted action "+i);
            if(i==5){advance(40);Render(Camera.main,"DuetReports/SwordAndMusic.png");}
            s.AcceptState(State());int guard=0;bool musicianSpoke=false;
            while((left.Busy||right.Busy||s.Speaker!=null)&&guard++<1500)
            {
                advance(1);
                if(s.Speaker==right){Require(s.Subtitle==right.lines[i-1].subtitle,"Musician subtitle mismatch.");musicianSpoke=true;}
            }
            Require(guard<1500&&musicianSpoke&&s.SpokenCount==spoken+2,"Voice turns failed "+i);
            Require(left.StartedCount==lc+1&&right.StartedCount==rc+1,"Intermediate gesture was queued.");reset();
        }
        report.noInterruption=report.noActionQueue=report.voiceTurns=report.subtitles=true;
        s.AcceptState(State(1,1));advance(1);lc=left.StartedCount;rc=right.StartedCount;
        s.StopAll();advance(1000);Require(left.StartedCount==lc&&right.StartedCount==rc&&s.Speaker==null,"Stop retriggered held poses.");report.stop=true;reset();

        s.enabled=false;s.endpoint="http://127.0.0.1:5130/duet_state";s.pollServer=true;s.enabled=true;
        float deadline=Time.realtimeSinceStartup+5;
        while(!s.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
        Require(s.Connected,"Duet HTTP fixture failed.");advance(1);
        Require(left.CurrentLine==left.lines[1]&&right.CurrentLine==right.lines[3]&&left.Busy&&right.Busy,"Independent HTTP poses did not trigger.");report.httpTransport=true;
        s.enabled=false;s.pollServer=false;s.enabled=true;reset();

        // All ten real recordings play on one shared AudioSource, in complete alternating turns.
        s.enableAudio=true;
        for(int i=1;i<=5;i++)
        {
            reset();int spoken=s.SpokenCount;s.AcceptState(State(i,i));advance(1);yield return null;
            Require(s.voice.isPlaying&&s.voice.clip==left.lines[i-1].voice,"Warrior WAV failed "+i);
            s.AcceptState(State());float start=Time.realtimeSinceStartup;bool heardMusician=false;
            while((left.Busy||right.Busy||s.Speaker!=null)&&Time.realtimeSinceStartup-start<18)
            {
                s.Tick(Time.unscaledDeltaTime);
                if(s.Speaker==right){heardMusician=true;Require(s.voice.clip==right.lines[i-1].voice,"Wrong musician WAV.");}
                if(s.Speaker!=null&&Time.realtimeSinceStartup-start>.6f)
                {Require(s.MusicSource.volume<.08f&&s.MusicSource.isPlaying,"Music did not duck under dialogue.");report.musicDucking=true;}
                yield return null;
            }
            Require(heardMusician&&s.SpokenCount==spoken+2&&s.Speaker==null&&!s.voice.isPlaying&&!left.Busy&&!right.Busy,"Real voice turns did not finish "+i);
        }
        reset();report.realAudio=true;
        deadline=Time.realtimeSinceStartup+2;
        while(s.MusicSource.volume<.159f&&Time.realtimeSinceStartup<deadline)yield return null;
        Require(s.MusicSource.volume>=.159f&&s.MusicSource.isPlaying,"BGM did not recover after speech.");report.musicRecovery=true;yield return null;
    }
    static void Render(Camera camera,string path)
    {
        var target=new RenderTexture(1600,1000,24){antiAliasing=4};var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var old=camera.targetTexture;var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
