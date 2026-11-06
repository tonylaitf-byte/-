using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RelaxedIdleAuthor
{
    public const float Period=4.8f;
    static readonly string[] Names={"Spine02","Spine01","Spine","neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand"};
    static Vector3 Principal(Vector3[] points,Vector3 axis,Vector3 orthogonal)
    {
        var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
        for(int i=0;i<32;i++)
        {
            var next=Vector3.zero;foreach(var point in points){var d=point-center;next+=d*Vector3.Dot(d,axis);}
            if(orthogonal!=Vector3.zero)next=Vector3.ProjectOnPlane(next,orthogonal);axis=next.normalized;
        }
        return axis;
    }
    static void Solve(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 pole)
    {
        var origin=upper.position;float a=Vector3.Distance(origin,lower.position),b=Vector3.Distance(lower.position,hand.position);
        var direction=(target-origin).normalized;float distance=Mathf.Clamp(Vector3.Distance(origin,target),Mathf.Abs(a-b)+.005f,a+b-.005f);
        float along=(a*a-b*b+distance*distance)/(2*distance),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        var elbow=origin+direction*along+Vector3.ProjectOnPlane(pole-origin,direction).normalized*height;
        upper.rotation=Quaternion.FromToRotation(lower.position-origin,elbow-origin)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,origin+direction*distance-lower.position)*lower.rotation;
    }
    public static AnimationClip CreateWarriorIdle(Transform root)
    {
        var all=root.GetComponentsInChildren<Transform>(true);var bones=all.GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var rest=all.ToDictionary(t=>t,t=>t.localRotation);
        var hand=bones["LeftHand"];var sword=bones["Mesh1.0"];
        var points=sword.GetComponent<MeshFilter>().sharedMesh.vertices.Select(sword.TransformPoint).ToArray();
        var axis=Principal(points,Vector3.up,Vector3.zero);
        var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;if(Vector3.Dot(axis,center-hand.position)<0)axis=-axis;
        var wide=Principal(points,Vector3.ProjectOnPlane(Vector3.right,axis).normalized,axis);
        var front=Vector3.ProjectOnPlane(bones["headfront"].position-bones["Head"].position,Vector3.up).normalized;
        var normal=Vector3.Cross(wide,axis).normalized;if(Vector3.Dot(normal,front)<0)normal=-normal;
        var swordFrame=Quaternion.LookRotation(hand.InverseTransformDirection(normal),hand.InverseTransformDirection(axis));
        var side=Vector3.ProjectOnPlane(bones["LeftShoulder"].position-bones["RightShoulder"].position,Vector3.up).normalized;
        var bottle=bones["Mesh1.001"];
        var bottlePoints=bottle.GetComponent<MeshFilter>().sharedMesh.vertices.Select(bottle.TransformPoint).ToArray();
        var bottleAxis=Principal(bottlePoints,Vector3.up,Vector3.zero);if(Vector3.Dot(bottleAxis,Vector3.up)<0)bottleAxis=-bottleAxis;
        var bottleLocalAxis=bones["RightHand"].InverseTransformDirection(bottleAxis);
        var tracks=Names.ToDictionary(n=>n,n=>new List<Quaternion>());
        Action reset=()=>{foreach(var pair in rest)pair.Key.localRotation=pair.Value;};
        try
        {
            for(int frame=0;frame<=288;frame++)
            {
                reset();float phase=frame/60f/Period*Mathf.PI*2,breath=.5f-.5f*Mathf.Cos(phase);
                bones["Spine02"].rotation=Quaternion.AngleAxis(.35f*Mathf.Sin(phase),front)*bones["Spine02"].rotation;
                bones["Spine01"].rotation=Quaternion.AngleAxis(-.65f*breath,side)*bones["Spine01"].rotation;
                bones["Head"].rotation=Quaternion.AngleAxis(-2f+.35f*breath,side)*bones["Head"].rotation;
                foreach(string name in new[]{"Left","Right"})
                {
                    bool left=name=="Left";var upper=bones[name+"Arm"];var lower=bones[name+"ForeArm"];var wrist=bones[name+"Hand"];
                    var shoulder=bones[name+"Shoulder"];
                    shoulder.rotation=Quaternion.AngleAxis((left?-2.5f:2f),front)*shoulder.rotation;
                    float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,wrist.position);
                    var direction=(side*(left?.38f:-.40f)+Vector3.down*(left?.78f:.72f)+front*(left?.42f:.48f)).normalized;
                    var target=upper.position+direction*length*(left?.86f:.78f)+Vector3.up*(.0025f*breath);
                    var pole=upper.position+side*(left?.26f:-.26f)+Vector3.down*.65f+front*.04f;
                    Solve(upper,lower,wrist,target,pole);
                }
                var blade=(side*.70f+Vector3.down*.70f+front*.22f).normalized;
                var desired=Quaternion.LookRotation(Vector3.ProjectOnPlane(front,blade).normalized,blade)*Quaternion.Inverse(swordFrame);
                var forearm=bones["LeftForeArm"];var forearmAxis=(hand.position-forearm.position).normalized;
                var delta=desired*Quaternion.Inverse(hand.rotation);
                float twist=2*Mathf.Atan2(Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),forearmAxis),delta.w)*Mathf.Rad2Deg;
                forearm.rotation=Quaternion.AngleAxis(Mathf.DeltaAngle(0,twist)*.65f,forearmAxis)*forearm.rotation;hand.rotation=desired;
                var right=bones["RightHand"];
                right.rotation=Quaternion.FromToRotation(right.TransformDirection(bottleLocalAxis),Vector3.up)*right.rotation;
                foreach(var name in Names)tracks[name].Add(bones[name].localRotation);
            }
        }
        finally{reset();}
        var clip=new AnimationClip{name="Idle",frameRate=60};
        foreach(var pair in tracks)
        {
            var values=pair.Value;
            for(int i=1;i<values.Count;i++)if(Quaternion.Dot(values[i-1],values[i])<0)values[i]=new Quaternion(-values[i].x,-values[i].y,-values[i].z,-values[i].w);
            for(int channel=0;channel<4;channel++)
            {
                var curve=new AnimationCurve();for(int i=0;i<values.Count;i++)curve.AddKey(i/60f,values[i][channel]);
                for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);}
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[pair.Key],root),typeof(Transform),"m_LocalRotation."+"xyzw"[channel]),curve);
            }
        }
        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        const string path="Assets/Animations/Idle.anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);EditorUtility.SetDirty(existing);clip=existing;}else AssetDatabase.CreateAsset(clip,path);
        AssetDatabase.SaveAssets();return clip;
    }
}
