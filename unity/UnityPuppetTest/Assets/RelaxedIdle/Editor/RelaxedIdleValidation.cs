using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.DuetGestures;

public static class RelaxedIdleValidation
{
    const string Pending="RelaxedIdle.Validation";
    static IEnumerator routine;static int lastFrame=-1,errors;static float next;static Report report;
    [Serializable]class Report
    {
        public bool passed,loopSeams,grounded,relaxedArms,subtleBreathing,instrumentGrip,handClearance,musicianJunctions,soloIdle,soloActionReturn,horizontalMovement,duetActions,mappingUnaffected;
        public float maxIdleDegreesPerFrame,minBladeFloorClearance=100,maxGripVariation,soloMovement;public int completedDuetActions,errors;public string failure;
    }
    static Transform Find(GameObject actor,string name)=>actor.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    public static void Run()
    {
        report=new Report();StaticChecks();File.WriteAllText("IdleReports/Geometry.json",JsonUtility.ToJson(report,true));
        EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/SoloGesture.unity");
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    static void StaticChecks()
    {
        EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/DuetGesture.unity");
        var session=UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();
        session.automaticUpdate=false;session.pollServer=false;
        var warrior=session.leftActor.gameObject;var musician=session.rightActor.music.puppet.animator.gameObject;
        foreach(var animator in UnityEngine.Object.FindObjectsByType<Animator>())animator.enabled=false;
        var floor=GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        var prop=Find(musician,"Yueqin_Independent");var skin=musician.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="Right_Closed_Carved_Hand");
        var baked=new Mesh();float minGrip=100,maxGrip=0,breathing=0;
        foreach(var actor in new[]{warrior,musician})
        {
            bool hero=actor==warrior;var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(hero?"Assets/Animations/Idle.anim":MusicianActionBuilder.Base+"/Animations/MusicIdle.anim");
            Require(clip.isLooping&&Mathf.Abs(clip.length-4.8f)<.001f,"Idle loop period/flag incorrect.");
            var transforms=actor.GetComponentsInChildren<Transform>(true);clip.SampleAnimation(actor,0);var initial=transforms.Select(t=>t.localRotation).ToArray();
            var feet=new[]{Find(actor,"LeftFoot").position,Find(actor,"RightFoot").position};var root=actor.transform.localPosition;
            var previous=initial.ToArray();
            for(int frame=0;frame<=288;frame++)
            {
                clip.SampleAnimation(actor,frame/60f);
                Require(Vector3.Distance(actor.transform.localPosition,root)<.00001f,"Idle moved the stage root.");
                Require(Vector3.Distance(Find(actor,"LeftFoot").position,feet[0])<.00001f&&Vector3.Distance(Find(actor,"RightFoot").position,feet[1])<.00001f,"Idle moved the feet.");
                for(int i=0;i<transforms.Length;i++)
                {
                    float angle=Quaternion.Angle(previous[i],transforms[i].localRotation);report.maxIdleDegreesPerFrame=Mathf.Max(report.maxIdleDegreesPerFrame,angle);
                    breathing=Mathf.Max(breathing,Quaternion.Angle(initial[i],transforms[i].localRotation));previous[i]=transforms[i].localRotation;
                }
                if(hero)
                {
                    foreach(string side in new[]{"Left","Right"})Require(Find(actor,side+"Hand").position.y<Find(actor,side+"Shoulder").position.y-.18f,"Warrior arm remains raised.");
                    var sword=Find(actor,"Mesh1.0");float clearance=sword.GetComponent<MeshFilter>().sharedMesh.vertices.Min(v=>sword.TransformPoint(v).y-floor);
                    report.minBladeFloorClearance=Mathf.Min(report.minBladeFloorClearance,clearance);Require(clearance>.08f,"Resting blade approaches the floor.");
                }
                else
                {
                    float grip=Vector3.Distance(Find(actor,"LeftMiddleProximal").position,Find(actor,"Yueqin_LeftGrip").position);minGrip=Mathf.Min(minGrip,grip);maxGrip=Mathf.Max(maxGrip,grip);
                    if(frame%2==0)
                    {
                        skin.BakeMesh(baked,true);
                        foreach(var vertex in baked.vertices)
                        {
                            var p=prop.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                            bool overFace=p.x*p.x+p.z*p.z<.00187f*.00187f;
                            bool overNeck=Mathf.Abs(p.x)<.00046f&&p.z>0&&p.z<.0053f;
                            Require(!(overFace||overNeck)||p.y<-.000578f,"Idle right hand enters the instrument safety envelope.");
                        }
                    }
                }
            }
            Require(transforms.Select((t,i)=>Quaternion.Angle(t.localRotation,initial[i])).Max()<.06f,"Idle loop seam.");
        }
        UnityEngine.Object.DestroyImmediate(baked);
        report.maxGripVariation=maxGrip-minGrip;Require(report.maxGripVariation<.001f,"Supporting hand drifted from the neck.");
        Require(report.maxIdleDegreesPerFrame<1.5f&&breathing>.2f&&breathing<5,"Breathing is static or too large.");
        report.loopSeams=report.grounded=report.relaxedArms=report.subtleBreathing=report.instrumentGrip=report.handClearance=true;
        var bones=musician.GetComponentsInChildren<Transform>(true);
        foreach(string state in new[]{"Greeting","Pause","FastExit"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+state+".anim");
            clip.SampleAnimation(musician,clip.length);var end=bones.Select(t=>t.localRotation).ToArray();
            AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/MusicIdle.anim").SampleAnimation(musician,0);
            Require(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,end[i])).Max()<.1f,"Musician idle junction: "+state);
        }
        report.musicianJunctions=true;
        foreach(string scene in new[]{"SoloMapping","DuetMapping"})
        {
            EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/"+scene+".unity");
            Require(UnityEngine.Object.FindObjectsByType<PuppetBoneTest>().All(m=>m.gestureIdleClip==null),"Idle overrides live mapping.");
        }
        report.mappingUnaffected=true;
    }
    [InitializeOnLoadMethod]static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        report=JsonUtility.FromJson<Report>(File.ReadAllText("IdleReports/Geometry.json"));routine=null;lastFrame=-1;errors=0;next=0;
        Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||lastFrame==Time.frameCount||Time.time<next)return;lastFrame=Time.frameCount;
        try{if(routine==null)routine=Check();if(routine.MoveNext()){next=Time.time+(routine.Current is float f?f:0);return;}report.passed=errors==0;}
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        Application.logMessageReceived-=Log;EditorApplication.update-=Tick;SessionState.SetBool(Pending,false);report.errors=errors;
        File.WriteAllText("IdleReports/Validation.json",JsonUtility.ToJson(report,true));Debug.Log(report.passed?"RELAXED_IDLE_PASS":"RELAXED_IDLE_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static IEnumerator Check()
    {
        yield return .6f;
        var mapper=UnityEngine.Object.FindAnyObjectByType<PuppetBoneTest>();var actor=UnityEngine.Object.FindAnyObjectByType<PuppetController>();
        Require(mapper.gestureIdleClip!=null,"Solo mapper did not receive the authored idle.");
        Require(mapper.leftHand.position.y<mapper.leftShoulder.position.y-.18f&&mapper.rightHand.position.y<mapper.rightShoulder.position.y-.18f,"Solo idle is overwritten.");report.soloIdle=true;
        float originalX=mapper.transform.position.x,originalY=mapper.transform.position.y;
        mapper.SetControlState(new PuppetControlState{version=2,controlMode="gesture",handDetected=true,calibrated=true,bodyX=.4f});yield return .8f;
        report.soloMovement=Mathf.Abs(mapper.transform.position.x-originalX);Require(report.soloMovement>.2f&&Mathf.Abs(mapper.transform.position.y-originalY)<.001f,"Idle blocked stage translation.");report.horizontalMovement=true;
        Require(UnityEngine.Object.FindAnyObjectByType<ActionManager>().ExecuteAction("ATTACK"),"Solo sword action did not start.");yield return .35f;
        Debug.Log("IDLE_ACTION_HANDOFF performing="+actor.IsPerforming+" handY="+mapper.leftHand.position.y+" shoulderY="+mapper.leftShoulder.position.y+" state="+actor.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime);
        Require(actor.IsPerforming&&mapper.leftHand.position.y>mapper.leftShoulder.position.y-.15f,"Idle overrode the sword action.");yield return 3.45f;
        Require(!actor.IsPerforming&&mapper.leftHand.position.y<mapper.leftShoulder.position.y-.18f,"Action did not return to the relaxed pose.");report.soloActionReturn=true;
        EditorSceneManager.LoadSceneInPlayMode("Assets/IntegratedTheatre/Scenes/DuetGesture.unity",new LoadSceneParameters(LoadSceneMode.Single));yield return .2f;
        var session=UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();session.automaticUpdate=false;session.pollServer=false;session.enableAudio=false;
        foreach(var person in new[]{session.leftActor,session.rightActor})person.Initialize();
        Action<float> advance=seconds=>{for(float t=0;t<seconds;t+=1f/60)session.Tick(Mathf.Min(1f/60,seconds-t));};
        for(int gesture=1;gesture<=5;gesture++)
        {
            Require(session.StartGesture(session.leftActor,gesture)&&session.StartGesture(session.rightActor,gesture),"Duet gesture rejected: "+gesture);
            advance(Mathf.Max(session.leftActor.Duration,session.rightActor.Duration)+.8f);
            Require(!session.leftActor.Busy&&!session.rightActor.Busy&&session.Speaker==null,"Duet action/dialogue did not finish: "+gesture);report.completedDuetActions+=2;
            session.StopAll();advance(1.5f);Require(session.rightActor.music.IsIdle,"Musician failed to settle to idle.");
        }
        report.duetActions=true;
    }
}
