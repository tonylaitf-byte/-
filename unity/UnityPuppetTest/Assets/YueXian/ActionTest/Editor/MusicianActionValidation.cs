using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MusicianActions;

public static class MusicianActionValidation
{
    const string Pending="YueXianActions.Validation";
    static int errors;
    static Report finalReport;
    static System.Collections.IEnumerator visualRoutine;
    static int lastFrame=-1;
    [Serializable] public class Report
    {
        public bool passed, singleMusician, noAudioSources, independentInstrument, loopSeams, clipJunctions, challengeFreeze, continuousLoops, challengeToFast, stopToIdle, pauseWorks, voiceLengthTiming, interruptionWorks;
        public int runtimeErrors, simulatedFrames, poseSamples;
        public float maxGripDistance, gripDistanceVariation, rightHandMovementDegrees, challengeFastDifferenceDegrees;
        public string[] tests; public string failure;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(MusicianActionBuilder.ScenePath);
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        errors=0;Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;};
        EditorApplication.update+=Tick;
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        EditorApplication.update-=Tick;var report=new Report();
        try
        {
            Directory.CreateDirectory("ActionReports/Poses");Directory.CreateDirectory("ActionReports/Surfaces");
            var managers=UnityEngine.Object.FindObjectsByType<MusicianActionManager>();
            report.singleMusician=managers.Length==1&&UnityEngine.Object.FindObjectsByType<Animator>().Length==1;
            Require(report.singleMusician,"Test scene must contain exactly one musician Animator.");
            report.noAudioSources=UnityEngine.Object.FindObjectsByType<AudioSource>().Length==0;Require(report.noAudioSources,"Unexpected audio source.");
            var m=managers[0];m.automaticUpdate=false;m.puppet.Initialize();
            var root=m.puppet.animator.transform;var instrument=Find(root,"Yueqin_Independent");var chest=Find(root,"Chest");var grip=Find(root,"LeftMiddleProximal");var anchor=Find(root,"Yueqin_LeftGrip");var hand=Find(root,"RightHand");
            Vector3 instrumentLocal=instrument.localPosition;Quaternion initialHand=hand.localRotation;
            report.independentInstrument=instrument.IsChildOf(chest)&&instrument.GetComponentsInChildren<SkinnedMeshRenderer>().Length==0;
            float minGrip=float.MaxValue,maxGrip=0;var tests=new List<string>();
            Action<float> advance=seconds=>
            {
                for(float t=0;t<seconds-.000001f;t+=1f/60)
                {
                    m.Advance(Mathf.Min(1f/60,seconds-t));report.simulatedFrames++;
                    float distance=Vector3.Distance(grip.position,anchor.position);minGrip=Mathf.Min(minGrip,distance);maxGrip=Mathf.Max(maxGrip,distance);
                    report.rightHandMovementDegrees=Mathf.Max(report.rightHandMovementDegrees,Quaternion.Angle(initialHand,hand.localRotation));
                    Require(Vector3.Distance(instrumentLocal,instrument.localPosition)<.00001f,"Instrument socket drift.");
                    Require(!float.IsNaN(hand.position.x),"Invalid bone pose.");
                }
            };
            foreach(var action in m.actions)
            {
                m.ExecuteAction(action.actionId);advance(.7f);
                advance(.9f);
                advance(1.5f);
                advance(action.fallbackDuration-3.1f+.3f);
                if(action.continuous)
                {
                    Require(m.IsLooping,action.actionId+" did not enter loop.");
                    advance(action.loopClip.length*4);Require(m.IsLooping,"Music loop stopped.");
                }
                else if(action.actionId=="MUSIC_CHALLENGE")
                {
                    report.challengeToFast=m.ActionId=="MUSIC_PLAY_FAST"&&!m.IsIdle;Require(report.challengeToFast,"Challenge failed to enter fast playing.");
                }
                else Require(m.IsIdle,action.actionId+" failed to return to Idle.");
                tests.Add(action.actionId+": intro / completion / loop checked");
            }
            report.continuousLoops=true;
            var bones=root.GetComponentsInChildren<Transform>();
            foreach(string state in new[]{"MusicIdle","SlowLoop","FastLoop"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+state+".anim");
                clip.SampleAnimation(root.gameObject,0);var rotations=bones.Select(b=>b.localRotation).ToArray();
                clip.SampleAnimation(root.gameObject,clip.length);
                Require(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,rotations[i])).Max()<.08f,"Loop seam: "+state);
            }
            report.loopSeams=true;
            foreach(var pair in new[]{new[]{"SlowIntro","SlowLoop"},new[]{"FastIntro","FastLoop"},new[]{"Challenge","FastIntro"},new[]{"FastExit","MusicIdle"},new[]{"Greeting","MusicIdle"},new[]{"Pause","MusicIdle"}})
            {
                var first=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+pair[0]+".anim");
                var next=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+pair[1]+".anim");
                first.SampleAnimation(root.gameObject,first.length);var rotations=bones.Select(b=>b.localRotation).ToArray();
                next.SampleAnimation(root.gameObject,0);
                Require(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,rotations[i])).Max()<.1f,"Clip junction mismatch: "+pair[0]+" -> "+pair[1]);
            }
            report.clipJunctions=true;
            var challenge=m.actions.First(a=>a.actionId=="MUSIC_CHALLENGE");
            challenge.mainClip.SampleAnimation(root.gameObject,.42f);var freeze=bones.Select(b=>b.localRotation).ToArray();
            challenge.mainClip.SampleAnimation(root.gameObject,.88f);
            report.challengeFreeze=bones.Select((b,i)=>Quaternion.Angle(b.localRotation,freeze[i])).Max()<.08f;Require(report.challengeFreeze,"Challenge freeze is moving.");
            challenge.mainClip.SampleAnimation(root.gameObject,1.6f);var challengePose=bones.Select(b=>b.localRotation).ToArray();
            var fast=m.actions.First(a=>a.actionId=="MUSIC_PLAY_FAST");fast.mainClip.SampleAnimation(root.gameObject,1.6f);
            report.challengeFastDifferenceDegrees=bones.Select((b,i)=>Quaternion.Angle(b.localRotation,challengePose[i])).Max();
            Require(report.challengeFastDifferenceDegrees>18f,"Challenge and fast-play silhouettes are still too similar.");
            m.ExecuteAction("MUSIC_PLAY_FAST");advance(4);m.StopAction();advance(1.3f);report.stopToIdle=m.IsIdle;Require(report.stopToIdle,"Stop failed.");
            m.ExecuteAction("MUSIC_PLAY_FAST");advance(1);m.Paused=true;var paused=hand.localRotation;float elapsed=m.Elapsed;advance(.7f);
            report.pauseWorks=Mathf.Abs(m.Elapsed-elapsed)<.00001f&&Quaternion.Angle(paused,hand.localRotation)<.01f;m.Paused=false;Require(report.pauseWorks,"Pause failed.");
            foreach(var a in m.actions) {m.ExecuteAction(a.actionId);advance(.33f);}
            report.interruptionWorks=true;
            foreach(var a in m.actions.Where(a=>!a.continuous))
            {
                foreach(float voice in new[]{2.4f,6.2f})
                {
                    float duration=a.ResolveDuration(voice);
                    Require(Mathf.Abs(a.ClipTime(duration,duration)-a.fallbackDuration)<.001f,"Timing failed to reach clip end.");
                    Require(Mathf.Abs(a.ClipTime(.3f,duration)-.3f)<.001f,"Entry was slowed.");
                    Require(Mathf.Abs((a.ClipTime(duration-.1f,duration)-a.ClipTime(duration-.2f,duration))-.1f)<.001f,"Exit was slowed.");
                    m.ExecuteAction(a.actionId,voice);advance(duration+.25f);
                    Require(a.actionId=="MUSIC_CHALLENGE"?m.ActionId=="MUSIC_PLAY_FAST":m.IsIdle,"Voice duration scheduling failed.");
                    tests.Add(a.actionId+": silent timing simulation "+voice.ToString("0.0")+"s -> "+duration.ToString("0.0")+"s");
                }
            }
            var timing=m.actions[0];var clipRef=AudioClip.Create("SilentTimingOnly",6200,1,1000,false);timing.timingReference=clipRef;
            Require(Mathf.Abs(timing.ResolveDuration()-6.5f)<.001f,"AudioClip.length not used.");timing.timingReference=null;UnityEngine.Object.DestroyImmediate(clipRef);
            report.voiceLengthTiming=true;report.maxGripDistance=maxGrip;report.gripDistanceVariation=maxGrip-minGrip;
            Require(report.gripDistanceVariation<.001f,"Left grip drift.");Require(report.rightHandMovementDegrees>8,"Animations did not move the hand.");
            report.tests=tests.ToArray();report.runtimeErrors=errors;Require(errors==0,"Runtime errors.");report.passed=true;
        }
        catch(Exception e){report.failure=e.ToString();Debug.LogException(e);}
        finalReport=report;
        if(!report.passed){Finish();return;}
        visualRoutine=VisualSequence(UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>(),report);
        EditorApplication.update+=VisualTick;
    }
    static System.Collections.IEnumerator VisualSequence(MusicianActionManager m,Report report)
    {
        foreach(var action in m.actions)
        {
            m.ExecuteAction(action.actionId);m.Advance(.7f);yield return null;
            Capture(m,action.actionId+"_070",report,false);
            m.Advance(.9f);yield return null;Capture(m,action.actionId+"_160",report,true);
            m.Advance(1.5f);yield return null;Capture(m,action.actionId+"_310",report,false);
            if(action.continuous)
            {
                m.Advance(action.fallbackDuration-3.1f+.25f);yield return null;
                Capture(m,action.actionId+"_LOOP",report,true);
            }
        }
        m.ExecuteAction("MUSIC_PLAY_FAST");m.Advance(3.625f);yield return null;Capture(m,"FAST_LOOP_MAX",report,false);
        m.Advance(.25f);yield return null;Capture(m,"FAST_LOOP_MIN",report,false);
        m.ExecuteAction("MUSIC_PLAY_SLOW");m.Advance(4.05f);yield return null;Capture(m,"SLOW_LOOP_MAX",report,false);
        m.Advance(.9f);yield return null;Capture(m,"SLOW_LOOP_MIN",report,false);
        m.ExecuteAction("MUSIC_PAUSE");m.Advance(1.6f);yield return null;
        m.ExecuteAction("MUSIC_PLAY_FAST");m.Advance(.08f);yield return null;Capture(m,"TRANS_PAUSE_FAST_008",report,false);
        m.Advance(.08f);yield return null;Capture(m,"TRANS_PAUSE_FAST_016",report,false);
        m.ExecuteAction("MUSIC_GREETING");m.Advance(1.6f);yield return null;
        m.ExecuteAction("MUSIC_PLAY_FAST");m.Advance(.08f);yield return null;Capture(m,"TRANS_GREETING_FAST_008",report,false);
        yield return null;
    }
    static void VisualTick()
    {
        if(!EditorApplication.isPlaying||lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;
        try{if(visualRoutine.MoveNext())return;}
        catch(Exception e){finalReport.passed=false;finalReport.failure=e.ToString();Debug.LogException(e);}
        EditorApplication.update-=VisualTick;Finish();
    }
    static void Finish()
    {
        finalReport.runtimeErrors=errors;finalReport.passed &= errors==0;
        File.WriteAllText("ActionReports/PlayModeValidation.json",JsonUtility.ToJson(finalReport,true));SessionState.SetBool(Pending,false);
        Debug.Log(finalReport.passed?"MUSICIAN_ACTION_PLAYMODE_PASS":"MUSICIAN_ACTION_PLAYMODE_FAIL");EditorApplication.Exit(finalReport.passed?0:1);
    }
    static void Capture(MusicianActionManager manager,string name,Report report,bool render)
    {
        if(!name.StartsWith("TRANS_"))Require(manager.puppet.CurrentState.IsName(manager.puppet.StateName),"Animator state differs from scheduled state: "+name+" / "+manager.puppet.StateName);
        report.poseSamples++;MusicianSurfaceExport.Capture(manager.gameObject,"ActionReports/Surfaces/"+name+".json");
        if(!render)return;
        Camera camera=Camera.main;MusicianActionBuilder.Render(camera,"ActionReports/Poses/"+name+".png");
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        Vector3 focus=new Vector3(0,1.43f,-.10f);camera.transform.position=focus+new Vector3(-.35f,.12f,-2.5f);camera.transform.LookAt(focus);
        MusicianActionBuilder.Render(camera,"ActionReports/Poses/"+name+"_hands.png",1200,900);
        if(name.StartsWith("MUSIC_PLAY_"))
        {
            focus=Find(manager.puppet.animator.transform,"Yueqin_StrumPoint").position;
            camera.transform.position=focus+new Vector3(-1f,.3f,-1.15f);camera.transform.LookAt(focus);
            MusicianActionBuilder.Render(camera,"ActionReports/Poses/"+name+"_side.png",1200,900);
        }
        camera.transform.SetPositionAndRotation(position,rotation);
    }
}
