using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.DuetGestures;

public static class RelaxedIdlePreview
{
    public static void BuildAndRender()
    {
        Directory.CreateDirectory("IdleReports");
        if(!File.Exists("IdleReports/Before.png"))RenderPair("Before",false);
        var warrior=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Puppets/Puppet01.prefab"));
        RelaxedIdleAuthor.CreateWarriorIdle(warrior.transform);UnityEngine.Object.DestroyImmediate(warrior);
        MusicianActionBuilder.RebuildRelaxedIdleTransitions();
        IntegratedTheatreBuilder.Prepare();
        RenderPair("After",true);
        Debug.Log("RELAXED_IDLE_PREVIEW_PASS");EditorApplication.Exit(0);
    }
    static void RenderPair(string label,bool sequence)
    {
        EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/DuetGesture.unity");
        var session=UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();
        var warrior=session.leftActor.gameObject;var musician=session.rightActor.music.puppet.animator.gameObject;
        foreach(var behavior in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())behavior.enabled=false;
        foreach(var animator in UnityEngine.Object.FindObjectsByType<Animator>())animator.enabled=false;
        var warriorClip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Idle.anim");
        var musicianClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/MusicIdle.anim");
        var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.fieldOfView=31;
        camera.transform.position=new Vector3(0,2.1f,-8.2f);camera.transform.LookAt(new Vector3(0,1.04f,0));
        var light=new GameObject("Idle review fill").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.55f;light.transform.rotation=Quaternion.Euler(24,25,0);
        var skins=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>();
        var baked=skins.Select(s=>{var go=new GameObject("CPU preview");go.transform.SetParent(s.transform,false);var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=new Mesh();go.AddComponent<MeshRenderer>().sharedMaterials=s.sharedMaterials;return filter;}).ToArray();
        if(sequence)Directory.CreateDirectory("IdleReports/Frames");
        for(int frame=0;frame<(sequence?49:1);frame++)
        {
            warriorClip.SampleAnimation(warrior,frame/10f);musicianClip.SampleAnimation(musician,frame/10f);
            for(int i=0;i<skins.Length;i++){skins[i].BakeMesh(baked[i].sharedMesh,true);baked[i].sharedMesh.RecalculateBounds();skins[i].enabled=false;}
            if(frame==0)File.WriteAllLines("IdleReports/"+label+"SkinDiagnostics.txt",skins.Select((s,i)=>s.name+" vertices="+baked[i].sharedMesh.vertexCount+" meshBounds="+baked[i].sharedMesh.bounds+" world="+baked[i].transform.position+" scale="+baked[i].transform.lossyScale+" renderer="+baked[i].GetComponent<Renderer>().bounds));
            SwordDancePreview.Render(camera,sequence?"IdleReports/Frames/"+frame.ToString("000")+".png":"IdleReports/Before.png",1200,800);
            if(frame==0&&sequence)SwordDancePreview.Render(camera,"IdleReports/After.png",1200,800);
        }
    }
    public static void RenderOnly(){RenderPair("After",true);EditorApplication.Exit(0);}
    public static void PrepareAndRender(){IntegratedTheatreBuilder.Prepare();RenderOnly();}
}
