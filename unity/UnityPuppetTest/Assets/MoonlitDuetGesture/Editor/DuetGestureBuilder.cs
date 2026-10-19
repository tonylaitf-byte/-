using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.MusicianActions;
using PalmTheatre.MusicianGesture;
using PalmTheatre.MoonlitDuet;
using PalmTheatre.DuetGestures;

public static class DuetGestureBuilder
{
    public const string Base="Assets/MoonlitDuetGesture";
    public const string ScenePath=Base+"/Scenes/MoonlitDuetGestureTest.unity";
    [MenuItem("掌中智戲/月下琴劍/建立雙人手勢場景")]
    public static void Build()
    {
        Directory.CreateDirectory(Base+"/Scenes");Directory.CreateDirectory(Base+"/Prefabs");Directory.CreateDirectory("DuetReports");AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/PuppetActionTest.unity");
        foreach(var root in scene.GetRootGameObjects())
        {
            if(root.name=="GameSystems"||root.GetComponentInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(root);
        }
        // Preserve the solo scene's courtyard, mountain backdrop, materials and
        // lighting. Rotate the complete set so screen-left remains negative X.
        var setRotation=Quaternion.Euler(0,180,0);
        foreach(var root in scene.GetRootGameObjects())
            root.transform.SetPositionAndRotation(setRotation*root.transform.position,setRotation*root.transform.rotation);
        float floorY=GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        var musician=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/YueXian/Gesture/Prefabs/YueXianGestureActor.prefab"));
        PrefabUtility.UnpackPrefabInstance(musician,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(var c in musician.GetComponents<MusicianGestureSession>())UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in musician.GetComponents<AudioSource>())UnityEngine.Object.DestroyImmediate(c);
        musician.name="月弦公子 · 右側控制";musician.transform.position=new Vector3(1.6f,0,0);musician.transform.rotation=Quaternion.identity;
        musician.transform.position+=Vector3.up*(floorY-SkinBounds(musician).min.y);
        var right=musician.AddComponent<DuetGestureActor>();right.musician=true;right.characterName="琴師 · 月弦公子";
        right.music=musician.GetComponent<MusicianActionManager>();right.music.automaticUpdate=false;right.animator=right.music.puppet.animator;
        right.minimumX=.85f;right.maximumX=2.55f;right.homeX=1.6f;
        var hero=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Puppets/Puppet01.prefab"));
        PrefabUtility.UnpackPrefabInstance(hero,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(var c in hero.GetComponentsInChildren<PuppetHttpReceiver>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in hero.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in hero.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(c);
        hero.name="俠客 · 左側控制";hero.transform.position=new Vector3(-1.6f,0,0);
        var left=hero.AddComponent<DuetGestureActor>();left.characterName="俠客";left.animator=hero.GetComponentInChildren<Animator>();
        left.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;left.minimumX=-2.55f;left.maximumX=-.85f;left.homeX=-1.6f;
        left.idleClip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Idle.anim");
        left.idleClip.SampleAnimation(hero,0);
        var head=hero.GetComponentsInChildren<Transform>().Single(t=>t.name=="Head");
        var nose=hero.GetComponentsInChildren<Transform>().Single(t=>t.name=="headfront");
        var facing=Vector3.ProjectOnPlane(nose.position-head.position,Vector3.up).normalized;
        hero.transform.rotation=Quaternion.FromToRotation(facing,Vector3.back)*hero.transform.rotation;
        // The first puppet has an offset model origin. Ground its evaluated skin,
        // rather than carrying the solo scene's root height into this stage.
        hero.transform.position+=Vector3.up*(floorY-SkinBounds(hero).min.y);
        var names=new[]{"抱琴行禮","慢速撥琴","停琴回應","急弦挑戰","快速演奏","鞠躬","賞月","喝酒","警惕","舞劍"};
        var tw=new[]{"一曲行江湖，今暗會著少俠，有禮矣。","月色遮爾好，且聽我撥弦一曲。","哈哈！你飲好酒，我用琴聲共你鬥鬧熱！","莫急！我欲看你的劍，敢追會著我的琴聲！","好劍法！弦聲催劍，劍走親像風！","月下相逢就是緣，朋友，幸會矣！","今暝月娘真媠，予人看甲袂放。","有好月、有好琴，哪通會使無好酒！","嗯？你想欲創啥物？","哈哈！琴聲既起，就看我這一劍！"};
        var subtitles=new[]{"一曲行走江湖，今晚遇見少俠，有禮了。","如此美好的月色，且聽我彈奏一曲。","哈哈！你喝你的好酒，我用琴聲陪你助興！","別急！我只是想看看，你的劍能不能追上我的琴聲！","好劍法！琴弦催動劍勢，劍走如風！","月下相逢便是有緣，朋友，幸會了！","今晚的月亮真美，讓人捨不得移開目光。","有好月色、有好琴聲，怎麼能沒有好酒！","嗯？你想做什麼？","哈哈！琴聲既然響起，就看我這一劍！"};
        string[] musicIds={"MUSIC_GREETING","MUSIC_PLAY_SLOW","MUSIC_PAUSE","MUSIC_CHALLENGE","MUSIC_PLAY_FAST"};
        string[] heroIds={"Bow","Landscape","Drink","Alert","Attack"};
        foreach(var actor in new[]{left,right})
        {
            actor.lines=new DuetGestureLine[5];
            for(int i=0;i<5;i++)
            {
                int index=actor.musician?i:i+5;int audioNumber=2*i+(actor.musician?2:1);
                var path=Directory.GetFiles(Base+"/Audio",audioNumber.ToString("00")+"*.wav").Single().Replace('\\','/');
                var importer=(AudioImporter)AssetImporter.GetAtPath(path);importer.forceToMono=true;
                var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
                actor.lines[i]=new DuetGestureLine{actionId=actor.musician?musicIds[i]:heroIds[i],displayName=names[index],taiwanese=tw[index],subtitle=subtitles[index],
                    voice=AssetDatabase.LoadAssetAtPath<AudioClip>(path),animation=actor.musician?null:AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/"+heroIds[i]+".anim")};
            }
        }
        var session=new GameObject("月下琴劍 · 雙人手勢控制").AddComponent<DuetGestureSession>();
        session.leftActor=left;session.rightActor=right;session.voice=session.gameObject.AddComponent<AudioSource>();session.voice.playOnAwake=false;
        session.backgroundMusic=new GameObject("單人戲偶原背景音樂").AddComponent<BackgroundMusicPlayer>();
        var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.fieldOfView=32;camera.transform.position=new Vector3(0,floorY+2.1f,-9.4f);camera.transform.LookAt(new Vector3(0,floorY+1.0f,0));
        if(!camera.GetComponent<AudioListener>())camera.gameObject.AddComponent<AudioListener>();
        camera.gameObject.AddComponent<DuetGestureView>().session=session;
        PrefabUtility.SaveAsPrefabAsset(musician,Base+"/Prefabs/DuetMusician.prefab");PrefabUtility.SaveAsPrefabAsset(hero,Base+"/Prefabs/DuetWarrior.prefab");
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        File.WriteAllText("DuetReports/Build.json",JsonUtility.ToJson(new BuildReport{scene=ScenePath,sourceScene="Assets/Scenes/PuppetActionTest.unity",backgroundMusic="Chinese Music Preview - Marnix Wyns (youtube)",floorY=floorY,voiceCount=10,leftRange=new[]{left.minimumX,left.maximumX},rightRange=new[]{right.minimumX,right.maximumX}},true));
        Debug.Log("DUET_GESTURE_BUILD_PASS");
    }
    [Serializable] class BuildReport{public string scene,sourceScene,backgroundMusic;public float floorY;public int voiceCount;public float[] leftRange,rightRange;}
    public static Bounds SkinBounds(GameObject actor)
    {
        Bounds bounds=default;bool first=true;
        foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();skin.BakeMesh(mesh);
            foreach(var vertex in mesh.vertices)
            {
                var world=skin.transform.TransformPoint(vertex);
                if(first){bounds=new Bounds(world,Vector3.zero);first=false;}else bounds.Encapsulate(world);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        if(first)throw new Exception("Missing character skin.");return bounds;
    }
    public static void BuildAndTest(){Build();DuetGestureValidation.Run();}
}
