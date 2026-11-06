using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.DuetGestures;

public static class SwordDanceValidation
{
    const string Pending="SwordDanceValidation.Pending";static IEnumerator routine;static int frame=-1,errors;static float next;static Report report;
    [Serializable]class Report
    {
        public bool passed,leftHandWeapon,soloPlayback,soloVoice,fullSequence,footLock,propRestored,duetGuardHold,duetCuts,independentMovement,restContinuity;
        public int errors;public float soloHandTravel,duetHandTravel,duetDuration;public string failure;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/SoloGesture.unity");
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;routine=null;frame=-1;errors=0;next=0;report=new Report();
        Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||frame==Time.frameCount||Time.time<next)return;frame=Time.frameCount;
        try{if(routine==null)routine=Check();if(routine.MoveNext()){next=Time.time+(routine.Current is float f?f:0);return;}report.passed=errors==0;}
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        Application.logMessageReceived-=Log;EditorApplication.update-=Tick;SessionState.SetBool(Pending,false);report.errors=errors;
        File.WriteAllText("SwordDanceReports/Validation.json",JsonUtility.ToJson(report,true));Debug.Log(report.passed?"SWORD_DANCE_PASS":"SWORD_DANCE_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static Transform Bone(GameObject actor,string name)=>actor.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    static IEnumerator Check()
    {
        var actor=UnityEngine.Object.FindAnyObjectByType<PuppetController>();var manager=UnityEngine.Object.FindAnyObjectByType<ActionManager>();var voice=UnityEngine.Object.FindAnyObjectByType<ActionVoicePresenter>();
        var hand=Bone(actor.gameObject,"LeftHand");var sword=Bone(actor.gameObject,"Mesh1.0");var bottle=Bone(actor.gameObject,"Mesh1.001");
        Require(sword.IsChildOf(hand),"Wrong weapon hand.");report.leftHandWeapon=true;
        yield return .3f;var root=actor.transform.position;var foot=Bone(actor.gameObject,"LeftFoot").position;var initial=hand.position;
        Require(manager.ExecuteAction("ATTACK"),"Solo ATTACK did not execute.");yield return .4f;
        Require(voice.IsPlaying&&voice.GetComponent<AudioSource>().clip.name=="sword","Original solo recording changed.");report.soloVoice=true;
        Require(!bottle.gameObject.activeSelf,"Bottle should be hidden during sword dance.");var guard=hand.position;
        yield return .6f;report.soloHandTravel=Vector3.Distance(guard,hand.position);Require(report.soloHandTravel>.15f,"Sword hand did not make the first cut.");
        Require(Mathf.Abs(actor.transform.position.y-root.y)<.001f&&Mathf.Abs(Bone(actor.gameObject,"LeftFoot").position.y-foot.y)<.001f,"Feet floated during solo action.");
        yield return .9f;Require(Vector3.Distance(guard,hand.position)>.15f,"Second cut did not move the sword hand.");
        yield return 1.8f;Require(!actor.IsPerforming&&!voice.IsPlaying,"Solo action/voice did not finish.");Require(bottle.gameObject.activeSelf,"Bottle did not restore after action.");
        report.soloPlayback=report.fullSequence=report.footLock=report.propRestored=true;
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordDanceAuthor.ClipPath);
        Require(Mathf.Abs(clip.length-3)<.001f,"Unexpected clip duration.");
        var bindings=AnimationUtility.GetCurveBindings(clip);Require(!bindings.Any(b=>b.path==""&&b.propertyName.StartsWith("m_LocalPosition")),"Clip animates the stage root.");
        foreach(var binding in bindings.Where(b=>b.propertyName.StartsWith("m_LocalRotation")))
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding);Require(Mathf.Abs(curve.Evaluate(0)-curve.Evaluate(3))<.001f,"Clip does not return to rest.");
        }
        report.restContinuity=true;
        EditorSceneManager.LoadSceneInPlayMode("Assets/IntegratedTheatre/Scenes/DuetGesture.unity",new LoadSceneParameters(LoadSceneMode.Single));yield return .2f;
        var session=UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();session.automaticUpdate=false;session.pollServer=false;session.enableAudio=false;
        var left=session.leftActor;var right=session.rightActor;left.Initialize();right.Initialize();
        var rightHand=Bone(right.gameObject,"RightHand").position;hand=Bone(left.gameObject,"LeftHand");sword=Bone(left.gameObject,"Mesh1.0");
        Require(left.StartGesture(5,0,1),"Duet sword dance did not execute.");left.VoiceStarted();left.VoiceEnded();report.duetDuration=left.Duration;
        float elapsed=0;Action<float> advance=until=>{while(elapsed<until){left.Advance(1f/120);elapsed+=1f/120;}};
        advance(.6f);guard=hand.position;advance(1.6f);Require(Vector3.Distance(guard,hand.position)<.003f,"Long dialogue slowed the cut instead of extending guard.");report.duetGuardHold=true;
        float extra=left.Duration-clip.length;advance(extra+.94f);report.duetHandTravel=Vector3.Distance(guard,hand.position);Require(report.duetHandTravel>.15f,"Duet diagonal cut missing.");
        advance(extra+1.84f);Require(Vector3.Distance(guard,hand.position)>.15f,"Duet reverse cut missing.");report.duetCuts=true;
        left.Accept(new DuetHandState{ownerHeld=true,handDetected=true,bodyX=-1});advance(left.Duration+.2f);
        Require(left.transform.position.x>=left.minimumX&&left.transform.position.x<left.homeX-.1f&&Vector3.Distance(rightHand,Bone(right.gameObject,"RightHand").position)<.001f,"Independent stage movement failed.");report.independentMovement=true;
        Require(!left.Busy,"Duet action did not complete.");session.enabled=false;
    }
}
