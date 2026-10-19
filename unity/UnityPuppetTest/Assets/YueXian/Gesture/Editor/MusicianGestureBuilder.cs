using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MusicianActions;
using PalmTheatre.MusicianGesture;

public static class MusicianGestureBuilder
{
    public const string Base="Assets/YueXian/Gesture";
    public const string ScenePath=Base+"/Scenes/YueXianGestureTest.unity";
    [MenuItem("掌中智戲/琴師手勢/建立五手勢語音測試")]
    public static void Build()
    {
        Directory.CreateDirectory(Base+"/Scenes");Directory.CreateDirectory(Base+"/Data");Directory.CreateDirectory(Base+"/Prefabs");AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene(MusicianActionBuilder.ScenePath);
        var manager=UnityEngine.Object.FindAnyObjectByType<MusicianActionManager>();
        string[] sources={"BOW","LANDSCAPE","DRINK","ALERT","ATTACK"};
        string[] prefixes={"02","04","06","08","10"};
        string[] taiwanese={"一曲行江湖，今暗會著少俠，有禮矣。","月色遮爾好，且聽我撥弦一曲。","哈哈！你飲好酒，我用琴聲共你鬥鬧熱！","莫急！我欲看你的劍，敢追會著我的琴聲！","好劍法！弦聲催劍，劍走親像風！"};
        string[] subtitles={"一曲行走江湖，今晚遇見少俠，有禮了。","如此美好的月色，且聽我彈奏一曲。","哈哈！你喝你的好酒，我用琴聲陪你助興！","別急！我只是想看看，你的劍能不能追上我的琴聲！","好劍法！琴弦催動劍勢，劍走如風！"};
        var session=manager.gameObject.AddComponent<MusicianGestureSession>();session.manager=manager;session.voice=manager.gameObject.AddComponent<AudioSource>();
        session.lines=new MusicianVoiceLine[5];
        for(int i=0;i<5;i++)
        {
            var path=Directory.GetFiles(Base+"/Audio",prefixes[i]+"*.wav").Single().Replace('\\','/');
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);importer.forceToMono=true;
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var definition=UnityEngine.Object.Instantiate(manager.actions[i]);definition.timingReference=clip;definition.futureSubtitle=subtitles[i];
            string target=Base+"/Data/"+definition.actionId+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<MusicianActionDefinition>(target);
            if(existing){EditorUtility.CopySerialized(definition,existing);UnityEngine.Object.DestroyImmediate(definition);EditorUtility.SetDirty(existing);definition=existing;}
            else AssetDatabase.CreateAsset(definition,target);
            manager.actions[i]=definition;
            session.lines[i]=new MusicianVoiceLine{sourceAction=sources[i],actionId=definition.actionId,clip=clip,taiwanese=taiwanese[i],subtitle=subtitles[i]};
        }
        manager.automaticUpdate=false;session.voice.playOnAwake=false;session.voice.spatialBlend=0;
        var camera=Camera.main;
        var old=UnityEngine.Object.FindAnyObjectByType<MusicianActionTestView>();UnityEngine.Object.DestroyImmediate(old);
        if(!UnityEngine.Object.FindAnyObjectByType<AudioListener>())camera.gameObject.AddComponent<AudioListener>();
        var view=camera.gameObject.AddComponent<MusicianGestureView>();view.session=session;view.stageCamera=camera;view.ResetView();
        manager.gameObject.name="月弦公子 · 五手勢語音測試";
        PrefabUtility.SaveAsPrefabAsset(manager.gameObject,Base+"/Prefabs/YueXianGestureActor.prefab");
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("MUSICIAN_GESTURE_BUILD_PASS");
    }
    public static void BuildAndTest(){Build();MusicianGestureValidation.Run();}
}
