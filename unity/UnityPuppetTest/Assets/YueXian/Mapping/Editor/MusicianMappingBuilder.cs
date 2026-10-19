using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MoonlitDuet;
using PalmTheatre.MusicianMapping;

public static class MusicianMappingBuilder
{
    public const string Base="Assets/YueXian/Mapping";
    public const string ScenePath=Base+"/Scenes/YueXianMappingTest.unity";
    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    [MenuItem("掌中智戲/琴師映射/建立雙手映射場景")]
    public static void Build()
    {
        Directory.CreateDirectory(Base+"/Scenes");Directory.CreateDirectory(Base+"/Prefabs");Directory.CreateDirectory(Base+"/Meshes");Directory.CreateDirectory("MappingReports");AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene("Assets/YueXian/Scenes/YueXianPreview.unity");
        var performance=UnityEngine.Object.FindAnyObjectByType<YueXianPerformance>();var actor=performance.gameObject;
        PrefabUtility.UnpackPrefabInstance(actor,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        var animator=performance.humanoidAnimator;UnityEngine.Object.DestroyImmediate(performance);
        animator.runtimeAnimatorController=null;animator.enabled=false;
        foreach(var view in UnityEngine.Object.FindObjectsByType<MoonlitStageView>())UnityEngine.Object.DestroyImmediate(view);
        foreach(var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())UnityEngine.Object.DestroyImmediate(listener);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/YueXian/Models/YueXian.fbx");
        var originals=source.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        // Restore the authored rest skeleton before the existing mapper caches it.
        var skeleton=Find(actor.transform,"Root");
        foreach(var bone in skeleton.GetComponentsInChildren<Transform>(true))
        {
            if(originals.TryGetValue(bone.name,out Transform rest)){bone.localPosition=rest.localPosition;bone.localRotation=rest.localRotation;}
        }
        foreach(string side in new[]{"Left","Right"})
            foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                foreach(string joint in new[]{"Proximal","Intermediate","Distal"})
                    Find(actor.transform,side+finger+joint).localRotation*=Quaternion.Euler(joint=="Intermediate"?7:4,0,0);
        var frame=new GameObject("月弦公子 · 手部映射");frame.transform.SetPositionAndRotation(actor.transform.position,Quaternion.Euler(0,180,0));
        actor.transform.SetParent(frame.transform,true);actor.name="YueXian_Visual";
        AddSleeveLiner(actor,"Left");AddSleeveLiner(actor,"Right");
        var mapping=frame.AddComponent<PuppetBoneTest>();
        mapping.head=Find(actor.transform,"Head");mapping.spine=Find(actor.transform,"Spine");
        mapping.leftShoulder=Find(actor.transform,"LeftShoulder");mapping.leftArm=Find(actor.transform,"LeftUpperArm");mapping.leftForeArm=Find(actor.transform,"LeftForearm");mapping.leftHand=Find(actor.transform,"LeftHand");
        mapping.rightShoulder=Find(actor.transform,"RightShoulder");mapping.rightArm=Find(actor.transform,"RightUpperArm");mapping.rightForeArm=Find(actor.transform,"RightForearm");mapping.rightHand=Find(actor.transform,"RightHand");
        mapping.useAutomaticDemo=false;mapping.lockFeetToGround=true;mapping.horizontalMove=.7f;mapping.depthMove=.25f;
        mapping.shoulderLimitDegrees=110;mapping.elbowLimitDegrees=155;
        var receiver=frame.AddComponent<PuppetHttpReceiver>();receiver.target=mapping;
        var session=frame.AddComponent<MusicianMappingSession>();session.mapping=mapping;session.receiver=receiver;session.characterAnimator=animator;session.instrument=Find(actor.transform,"Yueqin_Independent").gameObject;session.SetInstrumentVisible(false);
        foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)){renderer.updateWhenOffscreen=true;renderer.quality=SkinQuality.Bone4;}
        PrefabUtility.SaveAsPrefabAsset(frame,Base+"/Prefabs/YueXianMappedPuppet.prefab");
        var camera=Camera.main;camera.rect=new Rect(0,0,.72f,1);
        var controlView=camera.gameObject.AddComponent<MusicianMappingView>();controlView.session=session;controlView.stageCamera=camera;controlView.ResetView();
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("MUSICIAN_MAPPING_BUILD_PASS");
    }
    public static void BuildAndTest(){Build();MusicianMappingValidation.Run();}
    static void AddSleeveLiner(GameObject actor,string side)
    {
        Transform fore=Find(actor.transform,side+"Forearm"),hand=Find(actor.transform,side+"Hand");
        Vector3 axis=(hand.position-fore.position).normalized,u=Vector3.Cross(axis,Vector3.up).normalized,v=Vector3.Cross(axis,u).normalized;
        var item=new GameObject(side+"_Mapping_InnerSleeve");item.transform.SetParent(actor.transform,false);
        // Cover the skin interval exposed when the formerly folded arms extend.
        float[] along={-.205f,-.175f,-.095f,-.052f,-.052f,-.095f,-.175f,-.205f};
        float[] radii={.039f,.036f,.033f,.026f,.023f,.030f,.033f,.036f};
        const int sides=48;var vertices=new List<Vector3>();var triangles=new List<int>();var weights=new List<BoneWeight>();
        for(int ring=0;ring<along.Length;ring++)for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.PI*2/sides;Vector3 world=hand.position+axis*along[ring]+radii[ring]*(u*Mathf.Cos(angle)+v*Mathf.Sin(angle));
            vertices.Add(item.transform.InverseTransformPoint(world));weights.Add(new BoneWeight{boneIndex0=0,weight0=1});
        }
        for(int ring=0;ring<along.Length;ring++)for(int i=0;i<sides;i++)
        {
            int a=ring*sides+i,b=ring*sides+(i+1)%sides,c=((ring+1)%along.Length)*sides+(i+1)%sides,d=((ring+1)%along.Length)*sides+i;
            triangles.AddRange(new[]{a,c,b,a,d,c});
        }
        var mesh=new Mesh{name=side+" Mapping Inner Sleeve"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.boneWeights=weights.ToArray();mesh.bindposes=new[]{fore.worldToLocalMatrix*item.transform.localToWorldMatrix};mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Base+"/Meshes/"+side+"InnerSleeve.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(existing);}else AssetDatabase.CreateAsset(mesh,path);
        var renderer=item.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.bones=new[]{fore};renderer.rootBone=fore;renderer.updateWhenOffscreen=true;
        renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/YueXian/Materials/Character/YX_Silk_MoonWhite.mat");
    }
}
