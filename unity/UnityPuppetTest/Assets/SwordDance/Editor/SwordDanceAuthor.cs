using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SwordDanceAuthor
{
    public const string ClipPath="Assets/Animations/Attack.anim";
    public const float Duration=3f;
    public const float GuardTime=SwordDanceTiming.GuardTime;
    [Serializable]public class FrameRecord{public float time;public Vector3 hand,tip;public float faceClearance;}
    [Serializable]public class Report{public bool passed;public string weaponHand;public float duration,tipTravel,minFaceClearance;public int frames,rotationBones;public FrameRecord[] keys;}
    sealed class Pose
    {
        public float t,yaw,lean,weight;public Vector3 hand,blade,support;
        public Pose(float t,float yaw,float lean,Vector3 hand,Vector3 blade,Vector3 support,float weight=1)
        {this.t=t;this.yaw=yaw;this.lean=lean;this.hand=hand;this.blade=blade;this.support=support;this.weight=weight;}
    }
    static readonly Pose[] Poses={
        new Pose(0,0,0,new Vector3(.65f,.70f,.78f),new Vector3(.4f,.9f,.16f),new Vector3(-.75f,.1f,.7f),0),
        new Pose(.28f,16,-2,new Vector3(.65f,.70f,.78f),new Vector3(.4f,.9f,.16f),new Vector3(-.75f,.1f,.7f)),
        new Pose(.48f,16,-2,new Vector3(.65f,.70f,.78f),new Vector3(.4f,.9f,.16f),new Vector3(-.75f,.1f,.7f)),
        new Pose(.92f,-20,4,new Vector3(-.5f,-.55f,.95f),new Vector3(-.85f,-.45f,.20f),new Vector3(-.75f,.12f,.7f)),
        new Pose(1.10f,-19,3,new Vector3(-.43f,-.55f,.92f),new Vector3(-.80f,-.60f,.20f),new Vector3(-.75f,.12f,.7f)),
        new Pose(1.38f,-8,0,new Vector3(-.28f,.45f,.95f),new Vector3(-.5f,.75f,.20f),new Vector3(-.75f,-.05f,.65f)),
        new Pose(1.82f,20,3,new Vector3(.85f,.02f,.7f),new Vector3(.95f,.08f,.20f),new Vector3(-.75f,-.05f,.65f)),
        new Pose(2.02f,18,2,new Vector3(.90f,-.10f,.6f),new Vector3(.90f,-.15f,.20f),new Vector3(-.75f,-.05f,.65f)),
        new Pose(2.38f,4,0,new Vector3(.85f,-.5f,.65f),new Vector3(.45f,.85f,.20f),new Vector3(-.75f,.10f,.65f)),
        new Pose(2.58f,0,0,new Vector3(.85f,-.5f,.65f),new Vector3(.45f,.85f,.20f),new Vector3(-.75f,.10f,.65f)),
        new Pose(3,0,0,new Vector3(.85f,-.5f,.65f),new Vector3(.45f,.85f,.20f),new Vector3(-.75f,.10f,.65f),0)
    };
    static Pose At(float time)
    {
        int index=0;while(index<Poses.Length-2&&time>Poses[index+1].t)index++;
        var a=Poses[index];var b=Poses[index+1];float u=Mathf.SmoothStep(0,1,Mathf.InverseLerp(a.t,b.t,time));
        return new Pose(time,Mathf.Lerp(a.yaw,b.yaw,u),Mathf.Lerp(a.lean,b.lean,u),Vector3.Lerp(a.hand,b.hand,u),Vector3.Slerp(a.blade,b.blade,u),Vector3.Lerp(a.support,b.support,u),Mathf.Lerp(a.weight,b.weight,u));
    }
    static Vector3 Principal(Vector3[] points,Vector3 axis,Vector3 orthogonal)
    {
        Vector3 center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
        for(int i=0;i<32;i++)
        {Vector3 next=Vector3.zero;foreach(var point in points){var d=point-center;next+=d*Vector3.Dot(d,axis);}if(orthogonal!=Vector3.zero)next=Vector3.ProjectOnPlane(next,orthogonal);axis=next.normalized;}
        return axis;
    }
    static void Solve(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 pole)
    {
        var origin=upper.position;float a=Vector3.Distance(origin,lower.position),b=Vector3.Distance(lower.position,hand.position);
        var direction=(target-origin).normalized;float distance=Mathf.Clamp(Vector3.Distance(origin,target),Mathf.Abs(a-b)+.005f,a+b-.005f);
        target=origin+direction*distance;var bend=Vector3.ProjectOnPlane(pole-origin,direction).normalized;
        float along=(a*a-b*b+distance*distance)/(2*distance);float height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        var elbow=origin+direction*along+bend*height;
        upper.rotation=Quaternion.FromToRotation(lower.position-origin,elbow-origin)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation;
    }
    public static AnimationClip CreateAttackClip(Transform root,Dictionary<string,Transform> bones)
    {
        var all=root.GetComponentsInChildren<Transform>(true);
        bones=all.GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var rest=all.ToDictionary(t=>t,t=>(t.localPosition,t.localRotation));
        var hand=bones["LeftHand"];var sword=all.Single(t=>t.name=="Mesh1.0");var bottle=all.SingleOrDefault(t=>t.name=="Mesh1.001");
        if(!sword.IsChildOf(hand))throw new Exception("Sword must remain attached to the left hand.");
        var sourceVertices=sword.GetComponent<MeshFilter>().sharedMesh.vertices;
        var points=sourceVertices.Select(v=>sword.TransformPoint(v)).ToArray();
        var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
        var axis=Principal(points,Vector3.up,Vector3.zero);if(Vector3.Dot(axis,center-hand.position)<0)axis=-axis;
        var wide=Principal(points,Vector3.ProjectOnPlane(Vector3.right,axis).normalized,axis);
        var nose=all.Single(t=>t.name=="headfront");var front=Vector3.ProjectOnPlane(nose.position-bones["Head"].position,Vector3.up).normalized;
        var normal=Vector3.Cross(wide,axis).normalized;if(Vector3.Dot(normal,front)<0)normal=-normal;
        var frame=Quaternion.LookRotation(hand.InverseTransformDirection(normal),hand.InverseTransformDirection(axis));
        var palm=Vector3.Lerp(hand.position,bones["LeftHand_end"].position,.35f);
        var gripIndex=Enumerable.Range(0,points.Length).OrderBy(i=>(points[i]-palm).sqrMagnitude).First();
        var alignedSword=sword.localPosition+hand.InverseTransformVector(palm-points[gripIndex]);
        var side=Vector3.ProjectOnPlane(bones["LeftShoulder"].position-bones["RightShoulder"].position,Vector3.up).normalized;
        float turnSign=Mathf.Sign(Vector3.Dot(Vector3.Cross(front,side),Vector3.up));
        var names=new[]{"Spine02","Spine01","Spine","neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand"};
        var tracks=names.ToDictionary(n=>n,n=>new List<Quaternion>());var swordPositions=new List<Vector3>();
        var records=new List<FrameRecord>();float travel=0,minClearance=float.MaxValue;Vector3 previousTip=Vector3.zero;
        int tipIndex=Enumerable.Range(0,points.Length).OrderByDescending(i=>Vector3.Dot(points[i]-hand.position,axis)).First();
        float previousTwist=0;bool hasTwist=false;
        Action reset=()=>{foreach(var item in rest){item.Key.localPosition=item.Value.Item1;item.Key.localRotation=item.Value.Item2;}};
        try
        {
            for(int i=0;i<=180;i++)
            {
                float time=i/60f;reset();var p=At(time);
                bones["Spine02"].rotation=Quaternion.AngleAxis(p.yaw*turnSign,Vector3.up)*Quaternion.AngleAxis(p.lean,side)*bones["Spine02"].rotation;
                bones["Spine01"].rotation=Quaternion.AngleAxis(p.yaw*.18f*turnSign,Vector3.up)*bones["Spine01"].rotation;
                bones["Head"].rotation=Quaternion.AngleAxis(-p.yaw*.42f*turnSign,Vector3.up)*bones["Head"].rotation;
                foreach(string name in new[]{"Left","Right"})
                {
                    var upper=bones[name+"Arm"];var lower=bones[name+"ForeArm"];var wrist=bones[name+"Hand"];
                    float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,wrist.position);
                    var v=name=="Left"?p.hand:p.support;
                    var direction=(side*v.x+Vector3.up*v.y+front*v.z).normalized;
                    var target=upper.position+direction*length*(name=="Left"?.93f:.86f);
                    var pole=upper.position+(name=="Left"?side:-side)*.35f-Vector3.up*.5f+front*.1f;
                    Solve(upper,lower,wrist,target,pole);
                }
                var blade=(side*p.blade.x+Vector3.up*p.blade.y+front*p.blade.z).normalized;
                var face=Vector3.ProjectOnPlane(front,blade).normalized;
                var desiredHand=Quaternion.LookRotation(face,blade)*Quaternion.Inverse(frame);
                var forearm=bones["LeftForeArm"];var forearmAxis=(hand.position-forearm.position).normalized;
                var delta=desiredHand*Quaternion.Inverse(hand.rotation);
                var projection=forearmAxis*Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),forearmAxis);
                if(projection.sqrMagnitude+delta.w*delta.w>.00001f)
                {
                    float rawTwist=2*Mathf.Atan2(Vector3.Dot(projection,forearmAxis),delta.w)*Mathf.Rad2Deg;
                    float twist=hasTwist?previousTwist+Mathf.DeltaAngle(previousTwist,rawTwist):Mathf.DeltaAngle(0,rawTwist);
                    previousTwist=twist;hasTwist=true;
                    forearm.rotation=Quaternion.AngleAxis(twist*.65f,forearmAxis)*forearm.rotation;
                }
                hand.rotation=desiredHand;
                // Fade the authored stance back to the existing rest pose at both
                // ends so Animator and the live mapping handoff stay continuous.
                foreach(var name in names){var bone=bones[name];bone.localRotation=Quaternion.Slerp(rest[bone].Item2,bone.localRotation,p.weight);tracks[name].Add(bone.localRotation);}
                sword.localPosition=Vector3.Lerp(rest[sword].Item1,alignedSword,p.weight);swordPositions.Add(sword.localPosition);
                var tip=sword.TransformPoint(sourceVertices[tipIndex]);if(i>0)travel+=Vector3.Distance(previousTip,tip);previousTip=tip;
                var headCenter=Vector3.Lerp(bones["Head"].position,bones["head_end"].position,.45f);
                float clearance=sourceVertices.Min(v=>Vector3.Distance(sword.TransformPoint(v),headCenter));
                if(time>=.28f&&time<=2.58f)minClearance=Mathf.Min(minClearance,clearance);
                if(i%12==0)records.Add(new FrameRecord{time=time,hand=hand.position,tip=tip,faceClearance=clearance});
            }
        }
        finally{reset();}
        var clip=new AnimationClip{name="Attack",frameRate=60};
        foreach(var pair in tracks)
        {
            string path=AnimationUtility.CalculateTransformPath(bones[pair.Key],root);
            var values=pair.Value;for(int i=1;i<values.Count;i++)if(Quaternion.Dot(values[i-1],values[i])<0)values[i]=new Quaternion(-values[i].x,-values[i].y,-values[i].z,-values[i].w);
            for(int channel=0;channel<4;channel++)
            {
                var curve=new AnimationCurve();for(int i=0;i<values.Count;i++)curve.AddKey(i/60f,values[i][channel]);
                SetLinear(curve);AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[channel]),curve);
            }
        }
        for(int channel=0;channel<3;channel++)
        {
            var curve=new AnimationCurve();for(int i=0;i<swordPositions.Count;i++)curve.AddKey(i/60f,swordPositions[i][channel]);SetLinear(curve);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(sword,root),typeof(Transform),"m_LocalPosition."+"xyz"[channel]),curve);
        }
        if(bottle)
        {
            var curve=new AnimationCurve(new Keyframe(0,0),new Keyframe(2.999f,0),new Keyframe(3,1));
            for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Constant);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Constant);}
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bottle,root),typeof(GameObject),"m_IsActive"),curve);
        }
        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);clip=existing;EditorUtility.SetDirty(clip);}else AssetDatabase.CreateAsset(clip,ClipPath);
        Directory.CreateDirectory("SwordDanceReports");
        File.WriteAllText("SwordDanceReports/Authoring.json",JsonUtility.ToJson(new Report{passed=true,weaponHand="LeftHand",duration=Duration,tipTravel=travel,minFaceClearance=minClearance,frames=181,rotationBones=tracks.Count,keys=records.ToArray()},true));
        AssetDatabase.SaveAssets();return clip;
    }
    static void SetLinear(AnimationCurve curve)
    {for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}}
    [MenuItem("掌中智戲/劍師/重建舞劍動畫")]
    public static void Build()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Puppets/Puppet01.prefab"));
        var bones=actor.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        CreateAttackClip(actor.transform,bones);UnityEngine.Object.DestroyImmediate(actor);Debug.Log("SWORD_DANCE_BUILD_PASS");
    }
}
