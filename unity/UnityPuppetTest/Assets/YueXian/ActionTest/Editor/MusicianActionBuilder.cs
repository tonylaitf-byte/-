using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MoonlitDuet;
using PalmTheatre.MusicianActions;

public static class MusicianActionBuilder
{
    public const string Base="Assets/YueXian/ActionTest";
    public const string ScenePath=Base+"/Scenes/YueXianActionTest.unity";
    static readonly string[] States={"MusicIdle","Greeting","SlowIntro","SlowLoop","Pause","Challenge","FastIntro","FastLoop","FastExit"};
    static readonly float[] Lengths={2,3.6f,3.6f,1.8f,4.2f,4.5f,3.5f,1,1};
    [MenuItem("掌中智戲/琴師動作/建立五動作測試場景")]
    public static void Build()
    {
        foreach(string folder in new[]{"Animations","Data","Prefabs","Scenes"})Directory.CreateDirectory(Base+"/"+folder);
        Directory.CreateDirectory("ActionReports"); AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene("Assets/YueXian/Scenes/YueXianPreview.unity");
        var actor=UnityEngine.Object.FindAnyObjectByType<YueXianPerformance>();
        GameObject character=actor.gameObject;
        PrefabUtility.UnpackPrefabInstance(character,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        Animator animator=actor.humanoidAnimator;
        UnityEngine.Object.DestroyImmediate(actor);
        foreach(var old in UnityEngine.Object.FindObjectsByType<MoonlitStageView>())UnityEngine.Object.DestroyImmediate(old);
        foreach(var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())UnityEngine.Object.DestroyImmediate(listener);
        foreach(var source in UnityEngine.Object.FindObjectsByType<AudioSource>())UnityEngine.Object.DestroyImmediate(source);
        AdjustPlayingInstrument(animator.transform);
        var author=new MusicianPoseAuthor(animator.transform);
        var clips=new Dictionary<string,AnimationClip>();
        for(int i=0;i<States.Length;i++)clips[States[i]]=Bake(author,animator.transform,States[i],Lengths[i],States[i].EndsWith("Loop")||States[i]=="MusicIdle");
        author.Reset();
        string avatarPath=Base+"/Animations/YueXianPrecisionAvatar.asset";
        Avatar avatar=AvatarBuilder.BuildGenericAvatar(animator.gameObject,"");avatar.name="YueXian Precision Skeleton";
        Save(avatar,avatarPath);animator.avatar=AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
        string controllerPath=Base+"/Animations/YueXianActions.controller";
        if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerPath))AssetDatabase.DeleteAsset(controllerPath);
        var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine=controller.layers[0].stateMachine;
        var states=new Dictionary<string,AnimatorState>();
        for(int i=0;i<States.Length;i++)
        {
            var state=machine.AddState(States[i],new Vector3(260+(i%3)*230,50+(i/3)*100,0));
            state.motion=clips[States[i]];state.writeDefaultValues=false;states[States[i]]=state;
        }
        machine.defaultState=states["MusicIdle"];
        // The manager schedules transitions using elapsed time and warped hold time.
        // These preview triggers also make the controller usable directly in Animator.
        foreach(string state in States)
        {
            controller.AddParameter("Preview_"+state,AnimatorControllerParameterType.Trigger);
            var transition=machine.AddAnyStateTransition(states[state]);transition.duration=.16f;transition.hasExitTime=false;transition.canTransitionToSelf=true;
            transition.AddCondition(AnimatorConditionMode.If,0,"Preview_"+state);
        }
        animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var puppet=character.AddComponent<MusicianPuppetController>();puppet.animator=animator;puppet.controller=controller;
        string[] ids={"MUSIC_GREETING","MUSIC_PLAY_SLOW","MUSIC_PAUSE","MUSIC_CHALLENGE","MUSIC_PLAY_FAST"};
        string[] titles={"抱琴行禮","悠然慢奏","停琴回應","急弦挑戰","急奏助劍"};
        string[] main={"Greeting","SlowIntro","Pause","Challenge","FastIntro"};
        float[] hs={1.2f,0,1.2f,1.5f,0},he={2.6f,0,2.8f,3.2f,0};
        var actions=new MusicianActionDefinition[5];
        for(int i=0;i<5;i++)
        {
            var definition=ScriptableObject.CreateInstance<MusicianActionDefinition>();
            definition.name=ids[i];definition.actionId=ids[i];definition.displayName=titles[i];definition.stateName=main[i];
            definition.mainClip=clips[main[i]];definition.fallbackDuration=clips[main[i]].length;definition.holdStart=hs[i];definition.holdEnd=he[i];
            definition.continuous=i==1||i==4;definition.loopClip=i==1?clips["SlowLoop"]:i==4?clips["FastLoop"]:null;definition.exitClip=clips["FastExit"];
            string path=Base+"/Data/"+ids[i]+".asset";Save(definition,path);actions[i]=AssetDatabase.LoadAssetAtPath<MusicianActionDefinition>(path);
        }
        var manager=character.AddComponent<MusicianActionManager>();manager.puppet=puppet;manager.actions=actions;
        character.name="月弦公子 · 五動作測試";
        PrefabUtility.SaveAsPrefabAsset(character,Base+"/Prefabs/YueXianActionActor.prefab");
        Camera camera=Camera.main;camera.rect=new Rect(0,0,.72f,1);
        var view=camera.gameObject.AddComponent<MusicianActionTestView>();view.manager=manager;view.stageCamera=camera;view.ResetView();
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Render(camera,"ActionReports/ActionTestStage.png");
        File.WriteAllText("ActionReports/ClipInventory.json",JsonUtility.ToJson(new Inventory{clips=States.Select((s,i)=>new ClipInfo{name=s,duration=clips[s].length,fps=clips[s].frameRate,loop=clips[s].isLooping}).ToArray(),boneCount=author.bones.Length,precisionAvatarValid=animator.avatar.isValid},true));
        Debug.Log("MUSICIAN_ACTION_BUILD_PASS");
    }
    static void Save(UnityEngine.Object value,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if(existing){EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);}
        else AssetDatabase.CreateAsset(value,path);
    }
    static void AdjustPlayingInstrument(Transform model)
    {
        var transforms=model.GetComponentsInChildren<Transform>(true);
        var prop=transforms.First(t=>t.name=="Yueqin_Independent");
        var grip=transforms.First(t=>t.name=="Yueqin_LeftGrip");
        // Tilt around the supporting fingers, keeping the neck anchored.
        prop.RotateAround(grip.position,Vector3.right,-4f);
        prop.position+=Vector3.forward*.002f;
        Directory.CreateDirectory(Base+"/Meshes");AssetDatabase.Refresh();
        foreach(var filter in prop.GetComponentsInChildren<MeshFilter>())
        {
            string name=filter.name;
            bool body=new[]{"Bridge","Circular_Binding","HalfMoon_Inlay","Round_Rosewood_Frame","Round_Soundboard","Short_Tassel","Soundboard_Grain","Tailpiece","Tassel_JadeBead"}.Any(name.EndsWith);
            bool strings=name.EndsWith("Four_Strings")||name.EndsWith("Raised_Frets");
            if(!body&&!strings)continue;
            var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name=name+"_Playing";
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=prop.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                if(body){p.x*=.86f;p.z*=.86f;}
                else if(p.z<.0016f)p.z*=.86f;
                vertices[i]=filter.transform.InverseTransformPoint(prop.TransformPoint(p));
            }
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Base+"/Meshes/"+mesh.name+".asset";Save(mesh,path);
            filter.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
    }
    public static void RebuildPlayingClearance()
    {
        // Author from the original resting skeleton, never from a sampled clip.
        EditorSceneManager.OpenScene("Assets/YueXian/Scenes/YueXianPreview.unity");
        var animator=UnityEngine.Object.FindAnyObjectByType<YueXianPerformance>().humanoidAnimator;
        AdjustPlayingInstrument(animator.transform);
        var reference=animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Yueqin_Independent");
        var propPosition=reference.localPosition;var propRotation=reference.localRotation;
        var meshes=reference.GetComponentsInChildren<MeshFilter>().ToDictionary(f=>f.name,f=>f.sharedMesh);
        var author=new MusicianPoseAuthor(animator.transform);
        for(int i=0;i<States.Length;i++)Bake(author,animator.transform,States[i],Lengths[i],States[i].EndsWith("Loop")||States[i]=="MusicIdle");
        author.Reset();
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var manager=UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>();
        var prop=manager.puppet.animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Yueqin_Independent");
        prop.SetLocalPositionAndRotation(propPosition,propRotation);
        foreach(var filter in prop.GetComponentsInChildren<MeshFilter>())filter.sharedMesh=meshes[filter.name];
        PrefabUtility.SaveAsPrefabAsset(manager.gameObject,Base+"/Prefabs/YueXianActionActor.prefab");
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("PLAYING_CLEARANCE_REBUILT");
        MusicianClearanceAudit.Run();
    }
    static AnimationClip Bake(MusicianPoseAuthor author,Transform model,string state,float length,bool loop)
    {
        var clip=new AnimationClip{name=state,frameRate=30,legacy=false};
        int count=Mathf.RoundToInt(length*30)+1;
        var curves=author.bones.ToDictionary(b=>b,b=>new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()});
        var last=new Dictionary<Transform,Quaternion>();
        for(int frame=0;frame<count;frame++)
        {
            float time=Mathf.Min(length,frame/30f);author.Pose(state,time);
            foreach(var bone in author.bones)
            {
                Quaternion q=bone.localRotation;
                if(last.TryGetValue(bone,out Quaternion prev)&&Quaternion.Dot(prev,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                last[bone]=q;var c=curves[bone];c[0].AddKey(time,q.x);c[1].AddKey(time,q.y);c[2].AddKey(time,q.z);c[3].AddKey(time,q.w);
            }
        }
        foreach(var pair in curves)
        {
            string path=AnimationUtility.CalculateTransformPath(pair.Key,model);
            for(int axis=0;axis<4;axis++)
            {
                var c=pair.Value[axis];
                for(int key=0;key<c.length;key++){AnimationUtility.SetKeyLeftTangentMode(c,key,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(c,key,AnimationUtility.TangentMode.ClampedAuto);}
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),c);
            }
        }
        clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        string assetPath=Base+"/Animations/"+state+".anim";Save(clip,assetPath);return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
    }
    public static void Render(Camera camera,string path,int width=1300,int height=1200)
    {
        var previous=camera.targetTexture;var active=RenderTexture.active;var rect=camera.rect;
        var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        camera.rect=new Rect(0,0,1,1);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=previous;camera.rect=rect;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
    }
    public static void BuildAndTest() { Build(); MusicianActionValidation.Run(); }
    [Serializable] class Inventory { public ClipInfo[] clips;public int boneCount;public bool precisionAvatarValid; }
    [Serializable] class ClipInfo { public string name;public float duration,fps;public bool loop; }
}
