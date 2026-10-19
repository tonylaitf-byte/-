using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.DuetGestures;
using PalmTheatre.DuetMapping;
using PalmTheatre.MusicianMapping;

public static class DuetMappingBuilder
{
    public const string Base="Assets/MoonlitDuetMapping";
    public const string ScenePath=Base+"/Scenes/MoonlitDuetMappingTest.unity";
    [MenuItem("掌中智戲/月下琴劍/建立雙人手部映射場景")]
    public static void Build()
    {
        Directory.CreateDirectory(Base+"/Scenes");Directory.CreateDirectory(Base+"/Prefabs");Directory.CreateDirectory("DuetMappingReports");AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene(DuetGestureBuilder.ScenePath);
        foreach(var actor in UnityEngine.Object.FindObjectsByType<DuetGestureActor>())UnityEngine.Object.DestroyImmediate(actor.gameObject);
        foreach(var session in UnityEngine.Object.FindObjectsByType<DuetGestureSession>())UnityEngine.Object.DestroyImmediate(session.gameObject);
        foreach(var view in UnityEngine.Object.FindObjectsByType<DuetGestureView>())UnityEngine.Object.DestroyImmediate(view);
        float floorY=GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        var hero=Spawn("Assets/Puppets/Puppet01.prefab");
        var musician=Spawn("Assets/YueXian/Mapping/Prefabs/YueXianMappedPuppet.prefab");
        foreach(var old in musician.GetComponents<MusicianMappingSession>())UnityEngine.Object.DestroyImmediate(old);
        foreach(var item in new[]{hero,musician})
        {
            foreach(var receiver in item.GetComponentsInChildren<PuppetHttpReceiver>(true))receiver.enabled=false;
            foreach(var behavior in item.GetComponentsInChildren<MonoBehaviour>(true))
                if(!(behavior is PuppetBoneTest)&&!(behavior is PuppetHttpReceiver))UnityEngine.Object.DestroyImmediate(behavior);
            foreach(var audio in item.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(audio);
            foreach(var animator in item.GetComponentsInChildren<Animator>(true)){animator.enabled=false;animator.runtimeAnimatorController=null;}
        }
        hero.name="俠客 · 左區手部映射";hero.transform.position=new Vector3(-1.6f,0,0);
        var head=hero.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Head");
        var nose=hero.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="headfront");
        hero.transform.rotation=Quaternion.FromToRotation(Vector3.ProjectOnPlane(nose.position-head.position,Vector3.up).normalized,Vector3.back)*hero.transform.rotation;
        musician.name="月弦公子 · 右區手部映射";musician.transform.position=new Vector3(1.6f,0,0);
        hero.transform.position+=Vector3.up*(floorY-DuetGestureBuilder.SkinBounds(hero).min.y);
        musician.transform.position+=Vector3.up*(floorY-DuetGestureBuilder.SkinBounds(musician).min.y);
        var left=Configure(hero,-2.55f,-.85f,"俠客 · 左側控制");
        var right=Configure(musician,.85f,2.55f,"琴師 · 月弦公子");
        right.instrument=musician.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Yueqin_Independent").gameObject;
        right.SetInstrumentVisible(false);
        var controller=new GameObject("月下琴劍 · 雙人映射控制").AddComponent<DuetMappingSession>();
        controller.leftWarrior=left;controller.rightMusician=right;controller.backgroundMusic=UnityEngine.Object.FindAnyObjectByType<BackgroundMusicPlayer>();
        Camera.main.rect=new Rect(0,0,1,1);Camera.main.gameObject.AddComponent<DuetMappingView>().session=controller;
        PrefabUtility.SaveAsPrefabAsset(hero,Base+"/Prefabs/DuetMappedWarrior.prefab");
        PrefabUtility.SaveAsPrefabAsset(musician,Base+"/Prefabs/DuetMappedMusician.prefab");
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        File.WriteAllText("DuetMappingReports/Build.json",JsonUtility.ToJson(new BuildReport{scene=ScenePath,sourceScene=DuetGestureBuilder.ScenePath,floorY=floorY},true));
        Debug.Log("DUET_MAPPING_BUILD_PASS");
    }
    static GameObject Spawn(string path)
    {
        var item=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        PrefabUtility.UnpackPrefabInstance(item,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);return item;
    }
    static DuetMappingActor Configure(GameObject item,float min,float max,string name)
    {
        var actor=item.AddComponent<DuetMappingActor>();actor.characterName=name;actor.mapping=item.GetComponent<PuppetBoneTest>();actor.characterAnimator=item.GetComponentInChildren<Animator>(true);
        actor.minimumX=min;actor.maximumX=max;actor.mapping.horizontalMove=.95f;actor.mapping.depthMove=.15f;
        actor.mapping.useAutomaticDemo=false;actor.mapping.lockFeetToGround=true;
        foreach(var skin in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.updateWhenOffscreen=true;
        return actor;
    }
    [Serializable]class BuildReport{public string scene,sourceScene;public float floorY;}
    public static void BuildAndTest(){Build();DuetMappingValidation.Run();}
}
