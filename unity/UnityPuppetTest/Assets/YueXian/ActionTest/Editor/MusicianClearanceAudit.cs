using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MusicianActions;

// Dense CPU skin samples, including both hands and the wrist surfaces.
// All coordinates are in the instrument frame so its geometry is written once.
public static class MusicianClearanceAudit
{
    const string Folder="ActionReports/Clearance";
    sealed class Skin
    {
        public SkinnedMeshRenderer renderer;
        public Mesh baked=new Mesh();
        public int[] indices,triangles;
        public string name;
    }
    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    static bool HandWeight(BoneWeight w,Transform[] bones)
    {
        Func<int,float,bool> hit=(i,v)=>v>.01f&&(bones[i].name.Contains("Hand")||bones[i].name.Contains("Wrist")||new[]{"Thumb","Index","Middle","Ring","Little"}.Any(bones[i].name.Contains));
        return hit(w.boneIndex0,w.weight0)||hit(w.boneIndex1,w.weight1)||hit(w.boneIndex2,w.weight2)||hit(w.boneIndex3,w.weight3);
    }
    static void Vec(BinaryWriter w,Vector3 p){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        EditorSceneManager.OpenScene(MusicianActionBuilder.ScenePath);
        var m=UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>();
        var actor=m.puppet.animator.gameObject; m.puppet.animator.enabled=false;
        var instrument=Find(actor.transform,"Yueqin_Independent");
        var skins=new List<Skin>();
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=r.sharedMesh;var weights=mesh.boneWeights;var triangles=new List<int>();
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                if(!r.sharedMaterials[sub].name.Contains("Porcelain"))continue;
                var t=mesh.GetTriangles(sub);
                for(int i=0;i<t.Length;i+=3)
                    if(HandWeight(weights[t[i]],r.bones)||HandWeight(weights[t[i+1]],r.bones)||HandWeight(weights[t[i+2]],r.bones))triangles.AddRange(new[]{t[i],t[i+1],t[i+2]});
            }
            if(triangles.Count==0)continue;
            var ids=triangles.Distinct().OrderBy(i=>i).ToArray();var index=ids.Select((v,i)=>new{v,i}).ToDictionary(p=>p.v,p=>p.i);
            skins.Add(new Skin{renderer=r,name=r.name,indices=ids,triangles=triangles.Select(i=>index[i]).ToArray()});
        }
        using(var w=new BinaryWriter(File.Create(Folder+"/Hands.bin")))
        {
            var filters=instrument.GetComponentsInChildren<MeshFilter>();w.Write(filters.Length);
            foreach(var f in filters)
            {
                w.Write(f.name);var v=f.sharedMesh.vertices;var t=f.sharedMesh.triangles;w.Write(v.Length);w.Write(t.Length);
                foreach(var p in v)Vec(w,instrument.InverseTransformPoint(f.transform.TransformPoint(p)));
                foreach(int i in t)w.Write(i);
            }
            w.Write(skins.Count);
            foreach(var s in skins){w.Write(s.name);w.Write(s.indices.Length);w.Write(s.triangles.Length);foreach(int i in s.triangles)w.Write(i);}
            Action<string,float> sample=(state,time)=>
            {
                w.Write(state);w.Write(time);
                foreach(var s in skins)
                {
                    s.renderer.BakeMesh(s.baked,true);var v=s.baked.vertices;
                    foreach(int i in s.indices)Vec(w,instrument.InverseTransformPoint(s.renderer.transform.TransformPoint(v[i])));
                }
            };
            foreach(string state in new[]{"MusicIdle","SlowIntro","SlowLoop","FastIntro","FastLoop","FastExit","Challenge"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+state+".anim");
                int frames=Mathf.CeilToInt(clip.length*60);
                for(int frame=0;frame<=frames;frame++)
                {
                    float time=Mathf.Min(clip.length,frame/60f);clip.SampleAnimation(actor,time);sample(state,time);
                }
                Debug.Log("CLEARANCE_EXPORTED "+state+" "+(frames+1));
            }
            // Exercise the actual controller's blends, loop entry and stop sequence.
            m.puppet.animator.enabled=true;m.automaticUpdate=false;m.puppet.Initialize();
            Action<string,int> advance=(name,count)=>
            {
                for(int frame=0;frame<count;frame++){m.Advance(1f/60);sample(name,(frame+1)/60f);}
                Debug.Log("CLEARANCE_EXPORTED "+name+" "+count);
            };
            m.ExecuteAction("MUSIC_PLAY_SLOW");advance("Runtime_Idle_Slow_Loop",360);
            m.ExecuteAction("MUSIC_PLAY_FAST");advance("Runtime_Slow_Fast_Loop",360);
            m.StopAction();advance("Runtime_Stop",90);
            m.ExecuteAction("MUSIC_CHALLENGE");advance("Runtime_Challenge_Fast",330);
            m.ExecuteAction("MUSIC_PAUSE");m.Advance(1.6f);
            m.ExecuteAction("MUSIC_PLAY_FAST");advance("Runtime_Pause_Fast",60);
            m.ExecuteAction("MUSIC_GREETING");m.Advance(1.6f);
            m.ExecuteAction("MUSIC_PLAY_SLOW");advance("Runtime_Greeting_Slow",60);
        }
        foreach(var s in skins)UnityEngine.Object.DestroyImmediate(s.baked);
        Debug.Log("CLEARANCE_EXPORT_COMPLETE");EditorApplication.Exit(0);
    }
    [Serializable] class Shot { public string state;public float time; }
    [Serializable] class Shots { public Shot[] shots; }
    public static void ValidateInstallation()
    {
        EditorSceneManager.OpenScene(MusicianActionBuilder.ScenePath);
        var m=UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>();
        var prop=Find(m.puppet.animator.transform,"Yueqin_Independent");
        var filters=prop.GetComponentsInChildren<MeshFilter>();
        if(filters.Length!=15||filters.Any(f=>!f.sharedMesh))throw new Exception("Missing instrument mesh reference.");
        var rim=filters.First(f=>f.name=="Yueqin_Round_Rosewood_Frame");
        float radius=rim.sharedMesh.vertices.Select(v=>prop.InverseTransformPoint(rim.transform.TransformPoint(v))).Max(v=>new Vector2(v.x,v.z).magnitude)*100;
        if(radius<.17f||radius>.19f)throw new Exception("Compact instrument was not installed.");
        m.automaticUpdate=false;m.puppet.Initialize();
        foreach(var id in new[]{"MUSIC_PLAY_SLOW","MUSIC_PLAY_FAST"})
        {
            m.ExecuteAction(id);m.Advance(4.5f);
            if(!m.IsLooping||!m.puppet.CurrentState.IsName(id=="MUSIC_PLAY_SLOW"?"SlowLoop":"FastLoop"))throw new Exception("Installed animation failed: "+id);
        }
        m.StopAction();m.Advance(1.5f);if(!m.IsIdle)throw new Exception("Installed stop failed.");
        Directory.CreateDirectory("ActionReports");
        File.WriteAllText("ActionReports/ClearanceReferenceValidation.json","{\"passed\":true,\"instrument_meshes\":15,\"slow_fast_loops\":true,\"stop_to_idle\":true}");
        Debug.Log("INSTALLATION_REFERENCE_PASS");EditorApplication.Exit(0);
    }
    public static void RenderSelected()
    {
        EditorSceneManager.OpenScene(MusicianActionBuilder.ScenePath);
        var m=UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>();var actor=m.puppet.animator.gameObject;m.puppet.animator.enabled=false;
        var shots=JsonUtility.FromJson<Shots>(File.ReadAllText(Folder+"/Shots.json"));
        foreach(var shot in shots.shots)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(MusicianActionBuilder.Base+"/Animations/"+shot.state+".anim");clip.SampleAnimation(actor,shot.time);
            var camera=Camera.main;Vector3 focus=Find(actor.transform,"Yueqin_StrumPoint").position;
            camera.transform.position=focus+new Vector3(-.7f,.22f,-1.25f);camera.transform.LookAt(focus);
            MusicianActionBuilder.Render(camera,Folder+"/"+shot.state+"_"+shot.time.ToString("F2")+".png",1200,1000);
        }
        EditorApplication.Exit(0);
    }
}
