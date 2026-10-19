using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.DuetMapping;

public static class DuetMappingValidation
{
    const string Pending="DuetMappingValidation.Pending";
    static IEnumerator routine;static float clock,next;static int frame=-1,errors;static Report report;
    [Serializable]class Report
    {
        public bool passed,completeRigs,isolatedWriters,independentActors,headAndArms,groundedBounds,lossGrace,releaseReturns,pause,instrumentToggle,httpTransport,disconnectReturns,backgroundMusic;
        public int errors;public string failure;public float warriorTravel,musicianTravel;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(DuetMappingBuilder.ScenePath);UnityEngine.Object.FindAnyObjectByType<DuetMappingSession>().pollServer=false;
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        routine=null;clock=next=0;frame=-1;errors=0;report=new Report();Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string m,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;clock+=Mathf.Min(Time.deltaTime,1f/60);
        if(clock<next)return;
        try
        {
            if(routine==null)routine=Check();
            if(routine.MoveNext()){next=clock+(routine.Current is float delay?delay:0);return;}
            report.passed=errors==0;
        }
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        Application.logMessageReceived-=Log;EditorApplication.update-=Tick;SessionState.SetBool(Pending,false);report.errors=errors;
        Directory.CreateDirectory("DuetMappingReports");File.WriteAllText("DuetMappingReports/Validation.json",JsonUtility.ToJson(report,true));
        Debug.Log(report.passed?"DUET_MAPPING_PASS":"DUET_MAPPING_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static DuetMappingHand Hand()=>new DuetMappingHand{ownerHeld=true,handDetected=true,ownerId=1,handedness="Right",controls=new PuppetControlState{version=2,handDetected=true,calibrated=true,calibrationProgress=1,sequenceId=Time.frameCount,sampleDt=1f/30,controlMode="mapping"}};
    static DuetMappingState State()=>new DuetMappingState{version=1,controlMode="duet_mapping",left=Hand(),right=Hand()};
    static bool Rig(PuppetBoneTest m)=>new[]{m.head,m.spine,m.leftArm,m.leftForeArm,m.leftHand,m.rightArm,m.rightForeArm,m.rightHand}.All(t=>t);
    static IEnumerator Check()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<DuetMappingSession>();var left=s.leftWarrior;var right=s.rightMusician;
        Require(Rig(left.mapping)&&Rig(right.mapping),"Incomplete mapping rigs.");report.completeRigs=true;
        Require(left.transform.position.x<0&&right.transform.position.x>0,"Wrong character placement.");
        Require(UnityEngine.Object.FindObjectsByType<PuppetBoneTest>().Length==2&&UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>().All(r=>!r.enabled)&&UnityEngine.Object.FindObjectsByType<Animator>().All(a=>!a.enabled),"Competing animator/receiver.");report.isolatedWriters=true;
        Require(UnityEngine.Object.FindObjectsByType<AudioSource>().Length==1&&s.backgroundMusic&&s.backgroundMusic.GetComponentInChildren<AudioSource>().isPlaying,"BGM missing or unwanted voice source.");report.backgroundMusic=true;
        var music=s.backgroundMusic.GetComponentInChildren<AudioSource>();
        s.AcceptState(State());yield return 1f;
        var neutralL=left.mapping.leftHand.position;var neutralR=right.mapping.leftHand.position;
        var leftRoot=left.transform.position;var rightRoot=right.transform.position;var head=right.mapping.head.localRotation;
        Render("Neutral");
        var state=State();state.left.controls.calibrated=false;state.left.controls.leftHandY=1;s.AcceptState(state);yield return .6f;
        Require(Vector3.Distance(neutralL,left.mapping.leftHand.position)<.008f,"Uncalibrated input moved warrior.");
        state=State();state.left.controls.leftHandY=.8f;s.AcceptState(state);yield return 1f;
        report.warriorTravel=Vector3.Distance(neutralL,left.mapping.leftHand.position);
        Require(report.warriorTravel>.08f&&Vector3.Distance(neutralR,right.mapping.leftHand.position)<.008f,"Warrior input leaked into musician.");
        s.AcceptState(State());yield return .9f;
        var neutralRightHand=right.mapping.rightHand.position;
        state=State();state.right.controls.rightHandY=.8f;state.right.controls.headYaw=.6f;s.AcceptState(state);yield return 1f;
        report.musicianTravel=Vector3.Distance(neutralRightHand,right.mapping.rightHand.position);
        Require(report.musicianTravel>.08f&&Vector3.Distance(neutralL,left.mapping.leftHand.position)<.008f,"Musician input leaked into warrior.");
        Require(Quaternion.Angle(head,right.mapping.head.localRotation)>12,"Head mapping did not respond.");report.independentActors=report.headAndArms=true;Render("IndependentArms");
        state=State();state.left.controls.bodyX=-99;state.right.controls.bodyX=99;state.left.controls.bodyY=state.right.controls.bodyY=1;s.AcceptState(state);yield return 1.3f;
        Require(Mathf.Abs(left.transform.position.x-left.minimumX)<.01f&&Mathf.Abs(right.transform.position.x-right.maximumX)<.01f,"Outer bounds failed.");
        state.left.controls.bodyX=99;state.right.controls.bodyX=-99;s.AcceptState(state);yield return 1.3f;
        Require(Mathf.Abs(left.transform.position.x-left.maximumX)<.01f&&Mathf.Abs(right.transform.position.x-right.minimumX)<.01f,"Center bounds failed.");
        Require(left.transform.position.y==leftRoot.y&&right.transform.position.y==rightRoot.y,"Root floated.");report.groundedBounds=true;
        state=State();state.left.controls.leftHandY=.6f;state.right.controls.rightHandY=.6f;s.AcceptState(state);yield return 1.5f;
        var heldL=left.mapping.leftHand.position;var heldR=right.mapping.rightHand.position;
        state.left.handDetected=state.right.handDetected=false;state.left.controls.handDetected=state.right.controls.handDetected=false;s.AcceptState(state);yield return .6f;
        Require(Vector3.Distance(heldL,left.mapping.leftHand.position)<.008f&&Vector3.Distance(heldR,right.mapping.rightHand.position)<.008f,"Short loss did not hold pose.");report.lossGrace=true;
        state.left.ownerHeld=state.right.ownerHeld=false;s.AcceptState(state);yield return 1.1f;
        Require(Vector3.Distance(neutralL,left.mapping.leftHand.position)<.015f&&Vector3.Distance(neutralR,right.mapping.leftHand.position)<.015f,"Released owner did not return to neutral.");report.releaseReturns=true;
        s.SetFrozen(true);heldL=left.mapping.leftHand.position;state=State();state.left.controls.leftHandY=1;s.AcceptState(state);yield return .4f;
        Require(Vector3.Distance(heldL,left.mapping.leftHand.position)<.00001f&&!music.isPlaying,"Pause failed.");s.SetFrozen(false);yield return .8f;
        Require(Vector3.Distance(heldL,left.mapping.leftHand.position)>.08f&&music.isPlaying,"Resume failed.");report.pause=true;
        right.SetInstrumentVisible(true);var parent=right.instrument.transform.parent;var local=right.instrument.transform.localPosition;yield return .2f;Render("InstrumentShown");
        right.SetInstrumentVisible(false);Require(!right.instrument.activeSelf&&right.instrument.transform.parent==parent&&right.instrument.transform.localPosition==local,"Instrument attachment changed.");report.instrumentToggle=true;
        s.enabled=false;s.endpoint="http://127.0.0.1:5140/duet_mapping_state";s.pollServer=true;s.enabled=true;yield return 1.2f;
        Require(s.Connected&&left.LatestState.controls.leftHandY>.5f&&right.LatestState.controls.headYaw>.3f,"Independent HTTP channels failed.");report.httpTransport=true;
        s.enabled=false;s.endpoint="http://127.0.0.1:1/duet_mapping_state";s.enabled=true;yield return 1.8f;
        Require(!s.Connected&&Vector3.Distance(neutralL,left.mapping.leftHand.position)<.02f&&Vector3.Distance(neutralR,right.mapping.leftHand.position)<.02f,"Disconnected inputs did not return to neutral.");report.disconnectReturns=true;
        s.enabled=false;
    }
    static void Render(string name)
    {
        var camera=Camera.main;var target=new RenderTexture(1600,1000,24){antiAliasing=4};var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var old=camera.targetTexture;var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();File.WriteAllBytes("DuetMappingReports/"+name+".png",pixels.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
