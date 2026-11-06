using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SwordDancePreview
{
    public static void BuildAndRender()
    {
        SwordDanceAuthor.Build();
        EditorSceneManager.OpenScene("Assets/IntegratedTheatre/Scenes/SoloGesture.unity");
        var actor=UnityEngine.Object.FindAnyObjectByType<PuppetController>().gameObject;
        foreach(var behavior in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())behavior.enabled=false;
        var head=actor.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Head");
        var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.fieldOfView=33;
        var focus=new Vector3(actor.transform.position.x,head.position.y-.35f,0);
        camera.transform.position=focus+new Vector3(0,.12f,6.2f);camera.transform.LookAt(focus);
        var fill=new GameObject("Preview soft key").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.65f;fill.transform.rotation=Quaternion.Euler(22,160,0);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordDanceAuthor.ClipPath);
        var samples=new System.Collections.Generic.List<string>();
        var hands=actor.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="LeftHand").ToArray();
        foreach(var animator in actor.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var baked=skins.Select(skin=>{
            var item=new GameObject("Preview CPU skin");item.transform.SetParent(skin.transform,false);
            var filter=item.AddComponent<MeshFilter>();filter.sharedMesh=new Mesh();item.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;return filter;
        }).ToArray();
        Directory.CreateDirectory("SwordDanceReports/Frames");
        for(int i=0;i<=60;i++)
        {
            clip.SampleAnimation(actor,i/20f);
            for(int j=0;j<skins.Length;j++){skins[j].BakeMesh(baked[j].sharedMesh);skins[j].enabled=false;}
            if(i%10==0)samples.Add(i+": "+string.Join(";",hands.Select(h=>h.position+" local="+h.localRotation)));
            Render(camera,"SwordDanceReports/Frames/"+i.ToString("000")+".png",720,800);
        }
        File.WriteAllLines("SwordDanceReports/PreviewPoses.txt",samples);
        Debug.Log("SWORD_DANCE_PREVIEW_PASS");EditorApplication.Exit(0);
    }
    public static void Render(Camera camera,string path,int w,int h)
    {
        var target=new RenderTexture(w,h,24){antiAliasing=4};var pixels=new Texture2D(w,h,TextureFormat.RGB24,false);var old=camera.targetTexture;var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,w,h),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
