using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Author poses in the existing puppet's coordinate frame, then bake native Unity clips.
public sealed class MusicianPoseAuthor
{
    readonly Transform root, chest, head;
    public readonly Transform[] bones;
    readonly Quaternion[] rest;
    readonly Vector3[] positions;
    readonly Vector3 origin, left, front;
    readonly Vector3 wrist, pole, fingerDirection, thumbDirection;
    public Transform Find(string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    public Vector3 P(float x, float y, float z) => origin + D(x,y,z);
    public Vector3 D(float x, float y, float z) => left*x-front*y+Vector3.up*z;
    public MusicianPoseAuthor(Transform model)
    {
        root = model; chest = Find("Chest"); head = Find("Head");
        var skeleton = Find("Root");
        bones = skeleton.GetComponentsInChildren<Transform>().Where(t => !t.name.StartsWith("Yueqin") && !t.GetComponent<Renderer>() && !t.GetComponent<MeshFilter>() && !t.IsChildOf(Find("Yueqin_Independent"))).ToArray();
        rest = bones.Select(b=>b.localRotation).ToArray(); positions = bones.Select(b=>b.localPosition).ToArray();
        origin = skeleton.position; front = Vector3.back; left = Vector3.right;
        wrist = chest.InverseTransformPoint(Find("RightHand").position);
        pole = chest.InverseTransformPoint(P(-.45f,-.15f,1.12f));
        var h = Find("RightHand"); var i = Find("RightIndexProximal"); var l = Find("RightLittleProximal");
        fingerDirection = chest.InverseTransformDirection(((i.position+l.position)*.5f-h.position).normalized);
        thumbDirection = chest.InverseTransformDirection(Vector3.ProjectOnPlane(i.position-l.position, chest.TransformDirection(fingerDirection)).normalized);
    }
    public void Reset()
    {
        for (int i=0;i<bones.Length;i++) { bones[i].localRotation=rest[i]; bones[i].localPosition=positions[i]; }
    }
    static float Ease(float v) { v=Mathf.Clamp01(v); return v*v*(3-2*v); }
    static float Ramp(float t,float a,float b) => Ease((t-a)/(b-a));
    static float Envelope(float t,float start,float peak,float release,float end) => Ramp(t,start,peak)*(1-Ramp(t,release,end));
    void Rotate(Transform bone, Vector3 axis, float degrees) { bone.rotation=Quaternion.AngleAxis(degrees,axis)*bone.rotation; }
    public void Pose(string state, float t)
    {
        Reset();
        var supportHand=Find("LeftHand");var supportTwist=Find("LeftWristTwist");
        var supportRotation=supportHand.rotation;var supportTwistRotation=supportTwist.rotation;
        SolveArm(supportHand.position+Find("Yueqin_Independent").TransformVector(Vector3.down*.00034f),P(.47f,-.15f,1.12f),"Left");
        supportTwist.rotation=supportTwistRotation;supportHand.rotation=supportRotation;
        Vector3 targetLocal=wrist, dirLocal=fingerDirection, radialLocal=thumbDirection;
        Vector3 desiredTarget=P(-.20f,-.548f,1.13f), desiredDir=D(.94f,-.30f,-.33f).normalized, desiredRadial=Vector3.up;
        float gesture=0, play=0, stroke=0, bow=0, yaw=0, nod=0, sway=0, open=0;
        if(state=="MusicIdle") { sway=.28f*Mathf.Sin(t*Mathf.PI); nod=.2f*Mathf.Sin(t*Mathf.PI); }
        if(state=="Greeting")
        {
            gesture=Envelope(t,.45f,1.2f,2.6f,3.6f);
            desiredTarget=P(-.22f,-.59f,1.32f); desiredDir=D(.90f,-.23f,.22f).normalized;
            bow=12*gesture; nod=4*gesture; yaw=10*Envelope(t,0,.45f,2.6f,3.6f);
        }
        if(state=="Pause")
        {
            float reach=Envelope(t,.45f,1.2f,2.8f,4.2f);
            gesture=reach; desiredTarget=P(-.34f,-.52f,1.34f); desiredDir=D(-.45f,-.82f,.23f).normalized;
            desiredRadial=Vector3.Cross(Vector3.up,desiredDir).normalized;
            yaw=14*Envelope(t,0,.6f,2.8f,4.2f); open=reach;
            nod=3*Envelope(t,2.8f,3.3f,3.35f,3.9f);
        }
        if(state=="SlowIntro")
        {
            play=Ramp(t,0,.5f); stroke=Mathf.Sin((t-.5f)/1.55f*Mathf.PI*2)*play;
            // Settle exactly to the loop's initial pose.
            stroke*=1-Ramp(t,3.2f,3.6f); nod=2.5f*play; sway=1.4f*Mathf.Sin(t/3.6f*Mathf.PI*2);
        }
        if(state=="SlowLoop") { play=1; stroke=Mathf.Sin(t/1.8f*Mathf.PI*2); sway=1.4f*Mathf.Sin(t/1.8f*Mathf.PI*2); nod=2.5f; }
        if(state=="FastIntro")
        {
            // 「好劍法！琴弦催動劍勢，劍走如風！」
            // A forward-driving diagonal sweep reads as support for the swordsman,
            // rather than the open, convivial silhouette of Challenge.
            float drive=Envelope(t,0,.32f,2.75f,3.5f);
            play=1; stroke=Mathf.Sin(t/.46f*Mathf.PI*2)*(1-Ramp(t,3.2f,3.5f));
            if(t<.35f)stroke=-Mathf.Sin(t/.35f*Mathf.PI);
            desiredTarget+=D(stroke*.020f,stroke*.016f,stroke*.062f);
            bow=7.5f*drive; yaw=4.5f*Mathf.Sin(t/.92f*Mathf.PI*2)*drive;
            sway=4.2f*Mathf.Sin(t/.92f*Mathf.PI*2)*(1-Ramp(t,3.2f,3.5f)); nod=1.2f+2.4f*drive;
        }
        if(state=="FastLoop")
        {
            play=1; stroke=Mathf.Sin(t*Mathf.PI*4);
            desiredTarget+=D(stroke*.018f,stroke*.012f,stroke*.052f);
            bow=4.5f*(.5f-.5f*Mathf.Cos(t*Mathf.PI*2));
            sway=3.6f*Mathf.Sin(t*Mathf.PI*2); yaw=3.2f*Mathf.Sin(t*Mathf.PI*2); nod=1.2f+1.5f*(.5f-.5f*Mathf.Cos(t*Mathf.PI*2));
        }
        if(state=="FastExit") { play=1-Ramp(t,.3f,1); stroke=-.65f*Mathf.Sin(Mathf.Clamp01(t/.3f)*Mathf.PI)*(1-Ramp(t,.28f,.3f)); nod=1.2f*play; }
        if(state=="Challenge")
        {
            // 「哈哈！你喝你的好酒，我用琴聲陪你助興！」
            // Sweep away from the instrument and hold an open-palm invitation.
            float invite=Ramp(t,0,.38f)*(1-Ramp(t,3.2f,4.48f));
            play=1; gesture=invite; open=invite;
            desiredTarget=Vector3.Lerp(desiredTarget,P(-.58f,-.39f,1.49f),invite);
            desiredDir=Vector3.Slerp(desiredDir,D(.28f,-.58f,.76f).normalized,invite);
            desiredRadial=Vector3.Slerp(desiredRadial,Vector3.up,invite);
            float settle=Ramp(t,4.15f,4.48f);
            bow=-5.5f*invite; yaw=-20f*invite; nod=-5f*invite+1.2f*settle; sway=-3.5f*invite;
            // A final broad downstroke returns cleanly to FastIntro's first pose.
            if(t>=3.2f)
            {
                float flourish=Mathf.Sin(Ramp(t,3.2f,4.35f)*Mathf.PI);
                stroke=-1.35f*flourish;
                desiredTarget+=D(-.020f*flourish,.010f*flourish,.055f*flourish);
            }
        }
        float amount=Mathf.Max(gesture,play);
        if(play>0)
        {
            bool fast=state.StartsWith("Fast")||state=="Challenge";
            desiredTarget+=D(0,0,stroke*(fast?.019f:.010f));
        }
        Vector3 target=chest.InverseTransformPoint(desiredTarget), direction=chest.InverseTransformDirection(desiredDir), radial=chest.InverseTransformDirection(desiredRadial);
        targetLocal=Vector3.Lerp(wrist,target,amount);
        dirLocal=Vector3.Slerp(fingerDirection,direction,amount);
        radialLocal=Vector3.Slerp(thumbDirection,radial,amount);
        Rotate(Find("Spine"),Vector3.left,bow*.42f);
        Rotate(chest,Vector3.left,bow*.58f);
        Rotate(chest,Vector3.up,-yaw*.4f);
        Rotate(chest,front,sway);
        Rotate(head,Vector3.up,-yaw*.6f);
        Rotate(head,Vector3.left,nod);
        SolveArm(chest.TransformPoint(targetLocal),chest.TransformPoint(pole));
        AimHand(chest.TransformDirection(dirLocal),chest.TransformDirection(radialLocal));
        float wristStroke=state.StartsWith("Fast")?12f:state=="Challenge"?8f:3f;
        // Fan across the strings instead of turning the fingertips into the soundboard.
        Rotate(Find("RightHand"),Find("Yueqin_Independent").TransformDirection(Vector3.down),stroke*wristStroke);
        foreach(string finger in new[]{"Index","Middle","Ring","Little"})
        {
            foreach(string joint in new[]{"Proximal","Intermediate","Distal"})
                Find("Right"+finger+joint).localRotation*=Quaternion.Euler(-open*(joint=="Intermediate"?15:6)+(finger=="Index"?stroke*3:0),0,0);
        }
        ClearPlayingHand();
    }
    void ClearPlayingHand()
    {
        var prop=Find("Yueqin_Independent");
        var skin=root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Right_Closed_Carved_Hand");
        var mesh=new Mesh();
        // Author-time correction only: bake safe native curves, with no runtime mesh work.
        // FBX instrument units are 1/100 m. Include rim, frets and raised strings.
        for(int iteration=0;iteration<4;iteration++)
        {
            skin.BakeMesh(mesh,true);float push=0;
            foreach(var vertex in mesh.vertices)
            {
                var p=prop.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                bool body=p.x*p.x+p.z*p.z<.00187f*.00187f;
                bool neck=Mathf.Abs(p.x)<.00046f&&p.z>0&&p.z<.0053f;
                if(body||neck)push=Mathf.Max(push,p.y+.00058f);
            }
            if(push<.000001f)break;
            var hand=Find("RightHand");var twist=Find("RightWristTwist");
            var handRotation=hand.rotation;var twistRotation=twist.rotation;
            SolveArm(hand.position+prop.TransformVector(Vector3.down*(push+.000015f)),chest.TransformPoint(pole));
            twist.rotation=twistRotation;hand.rotation=handRotation;
        }
        UnityEngine.Object.DestroyImmediate(mesh);
    }
    void Aim(Transform bone,Transform child,Vector3 direction) { bone.rotation=Quaternion.FromToRotation(child.position-bone.position,direction)*bone.rotation; }
    void SolveArm(Vector3 target,Vector3 poleWorld,string side="Right")
    {
        Transform upper=Find(side+"UpperArm"), fore=Find(side+"Forearm"), hand=Find(side+"Hand");
        Vector3 shoulder=upper.position; float a=Vector3.Distance(shoulder,fore.position),b=Vector3.Distance(fore.position,hand.position);
        Vector3 delta=target-shoulder; float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
        Vector3 direction=delta.normalized; float along=(a*a-b*b+distance*distance)/(2*distance),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        Vector3 elbow=shoulder+direction*along+Vector3.ProjectOnPlane(poleWorld-shoulder,direction).normalized*height;
        Aim(upper,fore,elbow-shoulder); Aim(fore,hand,shoulder+direction*distance-fore.position);
    }
    void AimHand(Vector3 direction,Vector3 radial)
    {
        Transform hand=Find("RightHand"),twist=Find("RightWristTwist"),i=Find("RightIndexProximal"),l=Find("RightLittleProximal");
        direction.Normalize(); radial=Vector3.ProjectOnPlane(radial,direction).normalized;
        Vector3 axis=(hand.position-twist.position).normalized,old=i.position-l.position;
        float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(old,axis),Vector3.ProjectOnPlane(radial,axis),axis);
        twist.rotation=Quaternion.AngleAxis(angle,axis)*twist.rotation;
        Vector3 current=((i.position+l.position)*.5f-hand.position).normalized;
        Vector3 across=Vector3.ProjectOnPlane(i.position-l.position,current).normalized;
        hand.rotation=Quaternion.LookRotation(direction,radial)*Quaternion.Inverse(Quaternion.LookRotation(current,across))*hand.rotation;
    }
}
