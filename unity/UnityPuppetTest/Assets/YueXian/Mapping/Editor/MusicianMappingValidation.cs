using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MusicianMapping;

public static class MusicianMappingValidation
{
    const string Pending="YueXianMapping.Validate";
    static IEnumerator routine;static float nextTime,simulationClock;static int lastFrame=-1,errors;
    static Report report;static LoopbackServer server;
    [Serializable] public class Report
    {
        public bool passed,completeRig,animatorDisabled,noAudio,singlePuppet,uncalibratedStable,leftIndependent,rightIndependent,headIndependent,groundLocked,handLossHolds,disconnectReturns,pauseWorks,instrumentToggleWorks,httpChannel;
        public int runtimeErrors,sampledPoses,httpRequests;
        public float leftDisplacement,rightDisplacement,leftCrossTalk,rightCrossTalk,headRotationDegrees,bodyDisplacement,neutralReturnError,handLossDrift;
        public string failure;public List<string> poses=new List<string>();
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(MusicianMappingBuilder.ScenePath);
        // Isolate the test from the real camera server; the saved scene remains enabled.
        UnityEngine.Object.FindAnyObjectByType<PuppetHttpReceiver>().enabled=false;
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if(!SessionState.GetBool(Pending,false))return;
        errors=0;lastFrame=-1;nextTime=0;simulationClock=0;routine=null;report=new Report();
        Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
    }
    static void OnLog(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors++;}
    static void Require(bool value,string error){if(!value)throw new Exception(error);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;
        lastFrame=Time.frameCount;
        // The center servo caps each step at 0.05 s. Slow batch rendering must
        // still provide enough actual LateUpdate steps, including after exports.
        simulationClock+=Mathf.Min(Time.deltaTime,1f/60f);
        if(simulationClock<nextTime)return;
        try
        {
            if(routine==null)routine=Sequence();
            if(routine.MoveNext())
            {
                float delay=routine.Current switch { float f=>f,int i=>i,_=>0 };
                // Bone smoothing uses scaled, maximum-delta-clamped simulation time.
                // Exporting a large mesh must not consume the next pose's settling time.
                nextTime=simulationClock+delay;return;
            }
            report.passed=errors==0;
        }
        catch(Exception ex){report.failure=ex.ToString();Debug.LogException(ex);report.passed=false;}
        Finish();
    }
    static void Finish()
    {
        server?.Dispose();EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;SessionState.SetBool(Pending,false);
        report.runtimeErrors=errors;Directory.CreateDirectory("MappingReports");File.WriteAllText("MappingReports/PlayModeValidation.json",JsonUtility.ToJson(report,true));
        Debug.Log(report.passed?"MUSICIAN_MAPPING_PLAYMODE_PASS":"MUSICIAN_MAPPING_PLAYMODE_FAIL");EditorApplication.Exit(report.passed?0:1);
    }
    static PuppetControlState State()=>new PuppetControlState{version=2,handDetected=true,calibrated=true,controlMode="mapping",sampleDt=1f/30,sequenceId=Time.frameCount};
    static IEnumerator Sequence()
    {
        Directory.CreateDirectory("MappingReports/Poses");Directory.CreateDirectory("MappingReports/Surfaces");
        var session=UnityEngine.Object.FindAnyObjectByType<MusicianMappingSession>();Require(session,"Mapping session missing.");
        var map=session.mapping;var receiver=session.receiver;receiver.enabled=false;
        var bones=new[]{map.head,map.spine,map.leftShoulder,map.leftArm,map.leftForeArm,map.leftHand,map.rightShoulder,map.rightArm,map.rightForeArm,map.rightHand};
        report.completeRig=bones.All(b=>b);Require(report.completeRig,"Incomplete musician rig binding.");
        report.animatorDisabled=!session.characterAnimator.enabled&&session.characterAnimator.runtimeAnimatorController==null;
        Require(report.animatorDisabled,"Animator would overwrite mapping.");
        report.singlePuppet=UnityEngine.Object.FindObjectsByType<PuppetBoneTest>().Length==1;Require(report.singlePuppet,"Unexpected second puppet.");
        report.noAudio=UnityEngine.Object.FindObjectsByType<AudioSource>().Length==0;Require(report.noAudio,"Unexpected audio.");
        yield return .5f;
        Vector3 neutralLeft=map.leftHand.position,neutralRight=map.rightHand.position,neutralRoot=map.transform.localPosition;
        Quaternion neutralHead=map.head.localRotation;
        Capture(session,"Neutral",true);
        var s=State();s.calibrated=false;s.leftHandY=1;s.headYaw=1;map.SetControlState(s);yield return .5f;
        report.uncalibratedStable=Vector3.Distance(neutralLeft,map.leftHand.position)<.005f&&Quaternion.Angle(neutralHead,map.head.localRotation)<.1f;Require(report.uncalibratedStable,"Uncalibrated hand moved the character.");
        s=State();s.leftHandY=.7f;s.leftHandZ=-.5f;map.SetControlState(s);yield return .8f;
        report.leftDisplacement=Vector3.Distance(neutralLeft,map.leftHand.position);report.leftCrossTalk=Vector3.Distance(neutralRight,map.rightHand.position);
        report.leftIndependent=report.leftDisplacement>.08f&&report.leftCrossTalk<.01f;Require(report.leftIndependent,"Left input or independent control failed.");Capture(session,"Left_Raise_Forward",true);
        map.SetControlState(State());yield return .7f;
        s=State();s.rightHandY=.7f;s.rightHandZ=.5f;map.SetControlState(s);yield return .8f;
        report.rightDisplacement=Vector3.Distance(neutralRight,map.rightHand.position);report.rightCrossTalk=Vector3.Distance(neutralLeft,map.leftHand.position);
        report.rightIndependent=report.rightDisplacement>.08f&&report.rightCrossTalk<.01f;Require(report.rightIndependent,"Right input or independent control failed.");Capture(session,"Right_Raise_Forward",true);
        map.SetControlState(State());yield return .7f;
        s=State();s.headPitch=.55f;s.headYaw=.45f;map.SetControlState(s);yield return .8f;
        report.headRotationDegrees=Quaternion.Angle(neutralHead,map.head.localRotation);
        report.headIndependent=report.headRotationDegrees>8&&Vector3.Distance(neutralLeft,map.leftHand.position)<.01f&&Vector3.Distance(neutralRight,map.rightHand.position)<.01f;
        Require(report.headIndependent,"Head-only movement disturbed the arms.");Capture(session,"Head_Only",true);
        s=State();s.bodyX=.5f;s.bodyY=.8f;s.bodyTurn=.65f;s.bodyLean=.4f;map.SetControlState(s);yield return .9f;
        report.bodyDisplacement=Vector3.Distance(neutralRoot,map.transform.localPosition);report.groundLocked=Mathf.Abs(map.transform.localPosition.y-neutralRoot.y)<.00001f;
        Require(report.bodyDisplacement>.1f&&report.groundLocked,"Body mapping or floor lock failed.");Capture(session,"Body_Turn_Move",true);
        map.SetControlState(State());yield return .7f;
        foreach(var pair in new[]{new[]{1f,1f},new[]{-1f,-1f}})
        {
            s=State();s.leftHandY=pair[0];s.rightHandY=pair[1];s.leftHandZ=-.7f;s.rightHandZ=.7f;map.SetControlState(s);yield return .9f;
            Require(bones.All(b=>!float.IsNaN(b.position.x)),"Invalid bone positions.");Capture(session,pair[0]>0?"Both_High":"Both_Low",true);
        }
        s=State();s.leftArm=1;s.rightArm=1;map.SetControlState(s);yield return 2.5f;Capture(session,"Both_Center_Forward",true);
        // Full center travel has a deliberate 0.8 units/s exit speed limit.
        // Test a held settled pose, rather than interrupting that transition.
        s=State();s.rightHandY=.6f;s.rightHandZ=.3f;map.SetControlState(s);yield return 2.5f;
        Vector3 heldLeft=map.leftHand.position,heldRight=map.rightHand.position;
        s.handDetected=false;map.SetControlState(s);yield return .6f;
        report.handLossDrift=Mathf.Max(Vector3.Distance(heldRight,map.rightHand.position),Vector3.Distance(heldLeft,map.leftHand.position));
        report.handLossHolds=report.handLossDrift<.008f;Require(report.handLossHolds,"Lost-hand hold failed: drift "+report.handLossDrift+"m; center "+map.CurrentLeftCenterForward+"/"+map.CurrentRightCenterForward);
        session.SetFrozen(true);var frozen=map.rightHand.position;map.SetControlState(State());yield return .4f;
        report.pauseWorks=Vector3.Distance(frozen,map.rightHand.position)<.00001f;Require(report.pauseWorks,"Pose pause failed.");session.SetFrozen(false);yield return .7f;
        session.SetInstrumentVisible(true);yield return .2f;Require(session.instrument.activeInHierarchy,"Instrument failed to show.");
        var instrumentParent=session.instrument.transform.parent;var localPos=session.instrument.transform.localPosition;
        Capture(session,"Instrument_Shown_Neutral",true);
        session.SetInstrumentVisible(false);report.instrumentToggleWorks=!session.instrument.activeSelf&&session.instrument.transform.parent==instrumentParent&&Vector3.Distance(localPos,session.instrument.transform.localPosition)<.00001f;
        Require(report.instrumentToggleWorks,"Instrument toggle altered attachment.");
        // Exercise the existing HTTP receiver through a private ephemeral loopback port.
        server=new LoopbackServer();var serialized=new SerializedObject(receiver);serialized.FindProperty("endpoint").stringValue=server.Endpoint;serialized.ApplyModifiedPropertiesWithoutUndo();
        server.Payload=JsonUtility.ToJson(State());receiver.enabled=true;yield return .6f;
        Require(receiver.IsConnected,"HTTP receiver did not connect to the test endpoint.");
        s=State();s.leftHandY=.7f;s.rightHandZ=.5f;server.Payload=JsonUtility.ToJson(s);yield return .9f;
        report.httpChannel=receiver.IsConnected&&receiver.LatestState!=null&&Vector3.Distance(neutralLeft,map.leftHand.position)>.08f;
        report.httpRequests=server.Requests;Require(report.httpChannel&&report.httpRequests>3,"HTTP mapping channel failed.");Capture(session,"HTTP_Mapped",true);
        server.Dispose();server=null;yield return 1.9f;
        report.neutralReturnError=Mathf.Max(Vector3.Distance(neutralLeft,map.leftHand.position),Vector3.Distance(neutralRight,map.rightHand.position));
        report.disconnectReturns=!receiver.IsConnected&&report.neutralReturnError<.025f;Require(report.disconnectReturns,"Disconnected channel did not return to neutral.");
        receiver.enabled=false;Capture(session,"Disconnected_Neutral",false);
    }
    static void Capture(MusicianMappingSession session,string name,bool render)
    {
        bool visible=session.instrument.activeSelf;session.instrument.SetActive(true);
        MusicianSurfaceExport.Capture(session.gameObject,"MappingReports/Surfaces/"+name+".json");session.instrument.SetActive(visible);
        report.sampledPoses++;report.poses.Add(name+" (instrument "+(visible?"shown":"hidden")+")");
        if(render)
        {
            Camera camera=Camera.main;MusicianActionBuilder.Render(camera,"MappingReports/Poses/"+name+".png",1400,1100);
            if(name=="Neutral"||name=="Both_Center_Forward")
            {
                Vector3 oldPosition=camera.transform.position;Quaternion oldRotation=camera.transform.rotation;
                Vector3 focus=(session.mapping.rightForeArm.position+session.mapping.rightHand.position)*.5f;
                camera.transform.position=focus+new Vector3(-.15f,.25f,-1.4f);camera.transform.LookAt(focus);
                MusicianActionBuilder.Render(camera,"MappingReports/Poses/"+name+"_wrist.png",1200,900);
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
            }
        }
    }
    sealed class LoopbackServer : IDisposable
    {
        readonly TcpListener listener;readonly Thread thread;volatile bool running=true;volatile string payload="{}";int requests;
        public string Payload {set=>payload=value;}public int Requests=>requests;public string Endpoint{get;}
        public LoopbackServer()
        {
            listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Endpoint="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/puppet_state";
            thread=new Thread(Serve){IsBackground=true,Name="Musician mapping validation"};thread.Start();
        }
        void Serve()
        {
            while(running)
            {
                try
                {
                    using(var client=listener.AcceptTcpClient())
                    using(var stream=client.GetStream())
                    {
                        client.ReceiveTimeout=1000;byte[] buffer=new byte[8192];stream.Read(buffer,0,buffer.Length);
                        byte[] body=Encoding.UTF8.GetBytes(payload),header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");
                        stream.Write(header,0,header.Length);stream.Write(body,0,body.Length);Interlocked.Increment(ref requests);
                    }
                }
                catch(Exception){if(!running)break;}
            }
        }
        public void Dispose(){running=false;listener.Stop();thread.Join(1000);}
    }
}
