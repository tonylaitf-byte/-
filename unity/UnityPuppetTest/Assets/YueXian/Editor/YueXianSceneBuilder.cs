using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using PalmTheatre.MoonlitDuet;

public static class YueXianSceneBuilder
{
    const string Base="Assets/YueXian";
    const string Character=Base+"/Models/YueXian.fbx";
    const string Instrument=Base+"/Models/Yueqin.fbx";
    const string Prefab=Base+"/Prefabs/YueXianGongZi.prefab";
    const string StageFolder=Base+"/Materials/Stage";
    const string MeshFolder=Base+"/Models/Stage";
    const string Reports="YueXianReports";
    static readonly Dictionary<HumanBodyBones,string> Map=HumanMap();
    static Dictionary<HumanBodyBones,string> HumanMap()
    {
        var d=new Dictionary<HumanBodyBones,string>{{HumanBodyBones.Hips,"Pelvis"},{HumanBodyBones.Spine,"Spine"},{HumanBodyBones.Chest,"Chest"},{HumanBodyBones.Neck,"Neck"},{HumanBodyBones.Head,"Head"}};
        foreach(string side in new[]{"Left","Right"})
        {
            foreach(var pair in new[]{("Shoulder","Shoulder"),("UpperArm","UpperArm"),("LowerArm","Forearm"),("Hand","Hand"),("UpperLeg","UpperLeg"),("LowerLeg","LowerLeg"),("Foot","Foot"),("Toes","Toes")})
                d[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+pair.Item1)]=side+pair.Item2;
            foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                foreach(string joint in new[]{"Proximal","Intermediate","Distal"})
                    d[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+finger+joint)]=side+finger+joint;
        }
        return d;
    }

    [MenuItem("掌中智戲/月下琴劍/建立月弦公子與雙角色場景")]
    public static void Build()
    {
        foreach(string dir in new[]{Base+"/Prefabs",Base+"/Scenes",Base+"/Materials/Character",Base+"/Materials/Instrument",StageFolder,MeshFolder,Reports})Directory.CreateDirectory(dir);
        AssetDatabase.Refresh();
        ConfigureHumanoid();
        ConfigureProp();
        Materials(Character,Base+"/Materials/Character");Materials(Instrument,Base+"/Materials/Instrument");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        GameObject root=CreateCharacter();
        PrefabUtility.SaveAsPrefabAsset(root,Prefab);
        Validate(root);
        UnityEngine.Object.DestroyImmediate(root);
        BuildScene(false);BuildScene(true);
        AssetDatabase.SaveAssets();
        Debug.Log("YUEXIAN_BUILD_PASS: Humanoid avatar, independent yueqin, solo and duet scenes saved.");
    }

    static void ConfigureHumanoid()
    {
        ModelImporter importer=(ModelImporter)AssetImporter.GetAtPath(Character);
        importer.animationType=ModelImporterAnimationType.Generic;importer.importAnimation=false;importer.isReadable=true;importer.optimizeGameObjects=false;importer.preserveHierarchy=true;
        importer.importCameras=false;importer.importLights=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.materialLocation=ModelImporterMaterialLocation.InPrefab;importer.SaveAndReimport();
        GameObject asset=AssetDatabase.LoadAssetAtPath<GameObject>(Character);
        Transform[] transforms=asset.GetComponentsInChildren<Transform>(true);
        foreach(var name in Map.Values)if(!transforms.Any(t=>t.name==name))throw new Exception("Missing humanoid bone "+name);
        HumanDescription description=new HumanDescription{
            human=Map.Select(p=>new HumanBone{humanName=HumanTrait.BoneName[(int)p.Key],boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
            skeleton=transforms.Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
            upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0,hasTranslationDoF=false
        };
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.humanDescription=description;importer.SaveAndReimport();
        Avatar avatar=AssetDatabase.LoadAllAssetsAtPath(Character).OfType<Avatar>().FirstOrDefault();
        if(avatar==null||!avatar.isValid||!avatar.isHuman)throw new Exception("Unity did not validate the YueXian Humanoid avatar.");
    }
    static void ConfigureProp()
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(Instrument);importer.animationType=ModelImporterAnimationType.None;importer.importAnimation=false;importer.isReadable=true;importer.importCameras=false;importer.importLights=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.materialLocation=ModelImporterMaterialLocation.InPrefab;importer.SaveAndReimport();
    }
    static void Materials(string assetPath,string destination)
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(assetPath);
        // Read the current FBX palette rather than a material remapped by an earlier build.
        foreach(var key in importer.GetExternalObjectMap().Keys.Where(k=>k.type==typeof(Material)).ToArray())importer.RemoveRemap(key);
        importer.SaveAndReimport();
        var materials=AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().Concat(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath).GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)).Where(m=>m!=null).GroupBy(m=>m.name).Select(g=>g.First());
        foreach(var source in materials)
        {
            string path=destination+"/"+source.name+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Standard")){name=source.name};AssetDatabase.CreateAsset(m,path);}
            m.color=source.color;string key=source.name.ToLowerInvariant();float metal=0,smooth=.31f;
            if(key.Contains("silver")||key.Contains("gold")||key.Contains("brass")){metal=.68f;smooth=.52f;}
            else if(key.Contains("jade")){metal=.18f;smooth=.63f;}
            else if(key.Contains("hair")){metal=.04f;smooth=.32f;}
            else if(key.Contains("eye_dark")){smooth=.54f;}
            else if(key.Contains("rosewood")){metal=.12f;smooth=.4f;}
            m.SetFloat("_Metallic",metal);m.SetFloat("_Glossiness",smooth);m.enableInstancing=true;EditorUtility.SetDirty(m);importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source.name),m);
        }
        importer.SaveAndReimport();
    }

    static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    static Vector3 left,forward,origin;
    static Vector3 P(float x,float y,float z)=>origin+left*x-forward*y+Vector3.up*z;
    static Vector3 D(float x,float y,float z)=>left*x-forward*y+Vector3.up*z;
    static GameObject CreateCharacter()
    {
        GameObject root=new GameObject("月弦公子 YueXianGongZi");
        GameObject model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Character));model.name="YueXian_Humanoid";model.transform.SetParent(root.transform,false);
        var head=Find(model.transform,"Head");
        Vector3 faceDirection=Vector3.ProjectOnPlane(Find(model.transform,"YueXian_FaceForward").position-Find(model.transform,"Root").position,Vector3.up).normalized;
        model.transform.rotation=Quaternion.FromToRotation(faceDirection,Vector3.back)*model.transform.rotation;
        origin=Find(model.transform,"Root").position;left=Vector3.ProjectOnPlane(Find(model.transform,"LeftUpperArm").position-Find(model.transform,"RightUpperArm").position,Vector3.up).normalized;forward=Vector3.back;
        SolveArm(model.transform,"Left",P(.245f,-.490f,1.424f),P(.47f,-.15f,1.12f));
        SolveArm(model.transform,"Right",P(-.210f,-.530f,1.19f),P(-.45f,-.15f,1.12f));
        AimHand(model.transform,"Left",D(-.60f,-.58f,.55f));
        AimHand(model.transform,"Right",D(.88f,-.25f,-.39f));
        foreach(string side in new[]{"Left","Right"})
            foreach(string finger in new[]{"Index","Middle","Ring","Little","Thumb"})
            {
                float[] angles=FingerPose(side,finger);int j=0;
                foreach(string joint in new[]{"Proximal","Intermediate","Distal"})
                {
                    Transform b=Find(model.transform,side+finger+joint);b.localRotation*=Quaternion.Euler(angles[j++],0,0);
                }
            }
        GameObject instrument=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Instrument));instrument.name="Yueqin_Independent";
        Transform f=Find(instrument.transform,"Yueqin_FaceForward"),tip=Find(instrument.transform,"Yueqin_NeckTip");
        Vector3 currentForward=(f.position-instrument.transform.position).normalized,currentUp=(tip.position-instrument.transform.position).normalized;
        Vector3 desiredUp=Quaternion.AngleAxis(23,forward)*Vector3.up;
        instrument.transform.rotation=Quaternion.LookRotation(forward,desiredUp)*Quaternion.Inverse(Quaternion.LookRotation(currentForward,currentUp))*instrument.transform.rotation;
        instrument.transform.position=P(.015f,-.46f,1.10f);
        var chest=Find(model.transform,"Chest");GameObject socket=new GameObject("Yueqin_Socket_Adjustable");socket.transform.SetParent(chest,false);socket.transform.SetPositionAndRotation(instrument.transform.position,instrument.transform.rotation);
        instrument.transform.SetParent(socket.transform,true);
        PrefabUtility.SaveAsPrefabAsset(instrument,Base+"/Prefabs/Yueqin.prefab");
        // The standalone prop prefab has a clean origin; the character socket carries the placement.
        var propContents=PrefabUtility.LoadPrefabContents(Base+"/Prefabs/Yueqin.prefab");propContents.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);propContents.transform.localScale=Vector3.one;PrefabUtility.SaveAsPrefabAsset(propContents,Base+"/Prefabs/Yueqin.prefab");PrefabUtility.UnloadPrefabContents(propContents);
        Animator animator=model.GetComponent<Animator>();if(!animator)animator=model.AddComponent<Animator>();animator.avatar=AssetDatabase.LoadAllAssetsAtPath(Character).OfType<Avatar>().First();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var performance=root.AddComponent<YueXianPerformance>();performance.humanoidAnimator=animator;performance.chest=chest;performance.head=head;performance.rightHand=Find(model.transform,"RightHand");performance.rightIndex=Find(model.transform,"RightIndexProximal");
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()){renderer.updateWhenOffscreen=true;renderer.quality=SkinQuality.Bone4;}
        return root;
    }
    static void Aim(Transform bone,Transform child,Vector3 direction){bone.rotation=Quaternion.FromToRotation(child.position-bone.position,direction)*bone.rotation;}
    static void AimHand(Transform model,string side,Vector3 direction)
    {
        Transform hand=Find(model,side+"Hand");
        direction.Normalize();Vector3 radial=Vector3.ProjectOnPlane(Vector3.up,direction).normalized;
        Transform twist=Find(model,side+"WristTwist");Vector3 axis=(hand.position-twist.position).normalized;
        Vector3 old=Find(model,side+"IndexProximal").position-Find(model,side+"LittleProximal").position;
        float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(old,axis),Vector3.ProjectOnPlane(radial,axis),axis);
        twist.rotation=Quaternion.AngleAxis(angle,axis)*twist.rotation;
        Vector3 center=(Find(model,side+"IndexProximal").position+Find(model,side+"LittleProximal").position)*.5f;
        Vector3 current=(center-hand.position).normalized;
        Vector3 across=Vector3.ProjectOnPlane(Find(model,side+"IndexProximal").position-Find(model,side+"LittleProximal").position,current).normalized;
        hand.rotation=Quaternion.LookRotation(direction,radial)*Quaternion.Inverse(Quaternion.LookRotation(current,across))*hand.rotation;
    }
    // Batch entry point: validate the exact prefabs and scenes just generated.
    public static void BuildAndValidate()
    {
        Build();
        YueXianPlayModeValidation.Run();
    }
    static float[] FingerPose(string side,string finger)
    {
        if(side=="Left")switch(finger){case "Index":return new[]{14f,24f,12f};case "Middle":return new[]{18f,29f,15f};case "Ring":return new[]{21f,32f,17f};case "Little":return new[]{25f,36f,20f};default:return new[]{8f,12f,8f};}
        switch(finger){case "Index":return new[]{10f,23f,12f};case "Middle":return new[]{18f,30f,17f};case "Ring":return new[]{25f,37f,20f};case "Little":return new[]{30f,40f,23f};default:return new[]{7f,14f,9f};}
    }
    static void SolveArm(Transform root,string stem,Vector3 target,Vector3 pole)
    {
        Transform upper=Find(root,stem+"UpperArm"),fore=Find(root,stem+"Forearm"),hand=Find(root,stem+"Hand");Vector3 shoulder=upper.position;float a=Vector3.Distance(shoulder,fore.position),b=Vector3.Distance(fore.position,hand.position);Vector3 delta=target-shoulder;float distance=Mathf.Min(delta.magnitude,a+b-.0001f);Vector3 direction=delta.normalized;float along=(a*a-b*b+distance*distance)/(2*distance);float height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));Vector3 bend=Vector3.ProjectOnPlane(pole-shoulder,direction).normalized;Vector3 elbow=shoulder+direction*along+bend*height;Aim(upper,fore,elbow-shoulder);Aim(fore,hand,target-fore.position);
    }

    [Serializable] public class Report
    {
        public bool passed,avatarValid,avatarHuman,instrumentSeparate,instrumentParentedToChest;
        public int mappedHumanBones,fingerBones,skinnedMeshes,instrumentMeshes;
        public float leftGripDistance,rightStringDistance;
        public string[] errors;
    }
    static void Validate(GameObject root)
    {
        var player=root.GetComponent<YueXianPerformance>();var instrument=Find(root.transform,"Yueqin_Independent");var leftHand=Find(root.transform,"LeftMiddleProximal");var rightHand=Find(root.transform,"RightMiddleDistal");
        float ld=Vector3.Distance(leftHand.position,Find(instrument,"Yueqin_LeftGrip").position),rd=Vector3.Distance(rightHand.position,Find(instrument,"Yueqin_StrumPoint").position);
        var errors=new List<string>();Avatar avatar=player.humanoidAnimator.avatar;
        if(!avatar||!avatar.isValid||!avatar.isHuman)errors.Add("Humanoid avatar invalid.");
        if(!instrument.IsChildOf(player.chest))errors.Add("Independent yueqin is not on Chest socket.");
        if(instrument.GetComponentsInChildren<SkinnedMeshRenderer>().Length!=0)errors.Add("Yueqin should be an independent static prop.");
        if(ld>.14f||rd>.14f)errors.Add("Hands are too far from instrument anchors.");
        if(root.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterials.Any(m=>m==null)))errors.Add("Missing material.");
        var report=new Report{passed=errors.Count==0,avatarValid=avatar&&avatar.isValid,avatarHuman=avatar&&avatar.isHuman,mappedHumanBones=Map.Count,fingerBones=30,instrumentSeparate=true,instrumentParentedToChest=instrument.IsChildOf(player.chest),skinnedMeshes=root.GetComponentsInChildren<SkinnedMeshRenderer>().Length,instrumentMeshes=instrument.GetComponentsInChildren<MeshRenderer>().Length,leftGripDistance=ld,rightStringDistance=rd,errors=errors.ToArray()};
        File.WriteAllText(Reports+"/UnityValidation.json",JsonUtility.ToJson(report,true));if(!report.passed)throw new Exception(string.Join("; ",errors));
    }

    static void BuildScene(bool duet)
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Environment();CreateStage(duet);
        GameObject musician=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));musician.transform.position=new Vector3(duet?-.66f:0,.095f,0);musician.transform.rotation=Quaternion.Euler(0,duet?-8:0,0);
        if(duet)CreateCompanion();
        var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();cameraObject.AddComponent<AudioListener>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.043f,.066f);camera.fieldOfView=32;camera.nearClipPlane=.03f;camera.farClipPlane=80;
        var view=cameraObject.AddComponent<MoonlitStageView>();view.focus=new Vector3(0,1.01f,0);view.distance=duet?6.6f:4.8f;view.yaw=duet?-3:-10;view.pitch=6;view.title=duet?"月下琴劍":"月弦公子";view.subtitle=duet?"一弦清月 · 一劍江湖":"清雅月琴樂師 · 文人俠客";view.ResetView();
        string path=Base+"/Scenes/"+(duet?"MoonlitDuet":"YueXianPreview")+".unity";EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        Render(Camera.main,Reports+(duet?"/MoonlitDuet.png":"/YueXianUnity.png"),duet?1800:1300,1400);
    }

    static void CreateCompanion()
    {
        const string source="Assets/Models/SwordCharacter/sword_01_testblend_colored.fbx";
        GameObject asset=AssetDatabase.LoadAssetAtPath<GameObject>(source);if(!asset)throw new Exception("The original sword puppet reference is missing.");
        GameObject hero=(GameObject)PrefabUtility.InstantiatePrefab(asset);hero.name="原角色 俠客";PrefabUtility.UnpackPrefabInstance(hero,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(var animator in hero.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(animator);
        Transform head=Find(hero.transform,"Head"),nose=Find(hero.transform,"headfront");hero.transform.rotation=Quaternion.FromToRotation(Vector3.ProjectOnPlane(nose.position-head.position,Vector3.up).normalized,Vector3.back)*hero.transform.rotation;
        foreach(string side in new[]{"Left","Right"})
        {
            Transform upper=Find(hero.transform,side+"Arm"),fore=Find(hero.transform,side+"ForeArm");Vector3 sign=side=="Left"?Vector3.right:Vector3.left;
            Aim(upper,fore,(sign*.55f+Vector3.down*.80f+Vector3.back*.13f).normalized);
        }
        foreach(var transform in hero.GetComponentsInChildren<Transform>())if(transform.name=="Mesh1.001")transform.gameObject.SetActive(false);
        PresentSword(hero.transform);
        foreach(var renderer in hero.GetComponentsInChildren<Renderer>())
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                string name=materials[i]?materials[i].name:"Reference cloth";string lower=name.ToLowerInvariant();Color c=materials[i]?materials[i].color:Color.gray;float metal=.1f;
                if(lower.Contains("gold")||name.Contains("金")){c=new Color(.53f,.32f,.08f);metal=.7f;}
                else if(lower.Contains("blue"))c=new Color(.035f,.10f,.24f);
                else if(lower.Contains("hair"))c=new Color(.65f,.72f,.79f);
                else if(lower.Contains("skin"))c=new Color(.7f,.45f,.32f);
                materials[i]=MaterialAsset("Reference_"+name,c,metal,.38f);
            }
            renderer.sharedMaterials=materials;if(renderer is SkinnedMeshRenderer s)s.updateWhenOffscreen=true;
        }
        hero.transform.position=new Vector3(.72f,.16f,.08f);hero.transform.Rotate(0,10,0,Space.World);
    }
    static void PresentSword(Transform hero)
    {
        Transform sword=Find(hero,"Mesh1.0"),hand=Find(hero,"LeftHand"),tip=Find(hero,"LeftHand_end");
        Vector3[] points=sword.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>sword.TransformPoint(v)).ToArray();
        Vector3 center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
        Vector3 main=Vector3.up;
        for(int i=0;i<24;i++){Vector3 next=Vector3.zero;foreach(var p in points){Vector3 d=p-center;next+=d*Vector3.Dot(d,main);}main=next.normalized;}
        if(Vector3.Dot(main,center-hand.position)<0)main=-main;
        Vector3 wide=Vector3.ProjectOnPlane(Vector3.right,main).normalized;
        for(int i=0;i<24;i++){Vector3 next=Vector3.zero;foreach(var p in points){Vector3 d=p-center;next+=d*Vector3.Dot(d,wide);}wide=Vector3.ProjectOnPlane(next,main).normalized;}
        Vector3 normal=Vector3.Cross(main,wide).normalized;
        Vector3 grip=points.OrderBy(p=>(p-hand.position).sqrMagnitude).First();
        Vector3 localGrip=sword.InverseTransformPoint(grip);
        Vector3 desiredTip=new Vector3(.23f,1,-.06f).normalized;
        sword.rotation=Quaternion.LookRotation(Vector3.back,desiredTip)*Quaternion.Inverse(Quaternion.LookRotation(normal,main))*sword.rotation;
        Vector3 palm=Vector3.Lerp(hand.position,tip.position,.40f);
        sword.position+=palm-sword.TransformPoint(localGrip);
    }
    static void Environment()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.26f,.35f,.43f);RenderSettings.ambientEquatorColor=new Color(.13f,.18f,.23f);RenderSettings.ambientGroundColor=new Color(.05f,.07f,.10f);RenderSettings.skybox=null;
        RenderSettings.fog=true;RenderSettings.fogColor=new Color(.025f,.043f,.066f);RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=9;RenderSettings.fogEndDistance=24;
        Light key=Light("Moon soft key",new Vector3(36,-33,0),new Color(.78f,.87f,1),1.3f);key.shadows=LightShadows.Soft;key.shadowStrength=.62f;
        Light("Warm stage fill",new Vector3(18,38,0),new Color(1,.89f,.73f),.55f);Light("Silver rim",new Vector3(30,150,0),new Color(.57f,.80f,1),1.05f);
        QualitySettings.antiAliasing=8;QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=20;
    }
    static Light Light(string name,Vector3 angles,Color color,float intensity){var obj=new GameObject(name);obj.transform.rotation=Quaternion.Euler(angles);var light=obj.AddComponent<Light>();light.type=LightType.Directional;light.color=color;light.intensity=intensity;return light;}
    static void CreateStage(bool duet)
    {
        Transform stage=new GameObject("月下小劇場 Moonlit Stage").transform;
        Material stone=MaterialAsset("MoonSlate",new Color(.018f,.029f,.043f),.1f,.3f),silver=MaterialAsset("MoonSilver",new Color(.37f,.52f,.58f),.55f,.45f),dark=MaterialAsset("InkWood",new Color(.009f,.020f,.035f),.08f,.36f);
        MeshObject("StageFloor",BoxMesh(new Vector3(24,.05f,24)),new Vector3(0,-.07f,0),stone,stage);
        string variant=duet?"Duet":"Solo";
        float radius=duet?1.65f:.9f;MeshObject(variant+"_RoundStage",CylinderMesh(radius,.09f,128),Vector3.zero,stone,stage);MeshObject(variant+"_StageSilverRing",TorusMesh(radius-.03f,.003f,128,8),new Vector3(0,.092f,0),silver,stage);
        GameObject gate=MeshObject(variant+"_MoonGate",TorusMesh(duet?1.70f:1.27f,.014f,160,10),new Vector3(0,1.27f,1.35f),silver,stage);gate.transform.rotation=Quaternion.Euler(90,0,0);
        Material moon=MaterialAsset("MoonDisc",new Color(.27f,.43f,.50f),0,.7f);
        GameObject disc=MeshObject("DistantMoon",CylinderMesh(.26f,.01f,96),new Vector3(duet?0:-.5f,2.45f,3.0f),moon,stage);disc.transform.rotation=Quaternion.Euler(90,0,0);
        foreach(int side in new[]{-1,1})
            for(int i=0;i<4;i++)MeshObject("SideLattice_"+side+"_"+i,BoxMesh(new Vector3(.022f,2.25f,.04f)),new Vector3(side*(1.8f+i*.18f),1.12f,1.7f),dark,stage);
    }
    static void Render(Camera camera,string path,int width,int height)
    {
        var target=new RenderTexture(width,height,24){antiAliasing=8};var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);var active=RenderTexture.active;var old=camera.targetTexture;float aspect=camera.aspect;
        try{camera.targetTexture=target;camera.aspect=(float)width/height;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=old;camera.aspect=aspect;RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
    }

    static Material MaterialAsset(string name, Color color, float metallic, float smoothness)
    {
        string path = StageFolder + "/" + name.Replace(' ', '_') + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Standard")) { name = name }; AssetDatabase.CreateAsset(material, path); }
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject MeshObject(string name, Mesh mesh, Vector3 position, Material material, Transform parent)
    {
        string path = MeshFolder + "/" + name.Replace(' ', '_') + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        mesh.name = name;
        if (existing == null) AssetDatabase.CreateAsset(mesh, path);
        else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = material;
        return obj;
    }

    static Mesh CylinderMesh(float radius, float height, int segments)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
            Vector3 p = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            Vector3 q = new Vector3(Mathf.Cos(b) * radius, 0, Mathf.Sin(b) * radius);
            AddQuad(vertices, triangles, p, p + Vector3.up * height, q + Vector3.up * height, q);
            AddTriangle(vertices, triangles, Vector3.up * height, q + Vector3.up * height, p + Vector3.up * height);
            AddTriangle(vertices, triangles, Vector3.zero, p, q);
        }
        return Finish(vertices, triangles);
    }

    static Mesh TorusMesh(float radius, float tube, int segments, int sides)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        for (int j = 0; j <= sides; j++)
        {
            float u = i * Mathf.PI * 2 / segments, v = j * Mathf.PI * 2 / sides;
            float r = radius + Mathf.Cos(v) * tube;
            vertices.Add(new Vector3(Mathf.Cos(u) * r, Mathf.Sin(v) * tube, Mathf.Sin(u) * r));
        }
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = i * (sides + 1) + j, b = (i + 1) * (sides + 1) + j;
            triangles.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
        }
        return Finish(vertices, triangles);
    }

    static Mesh BoxMesh(Vector3 size)
    {
        Vector3 h = size * 0.5f;
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        AddQuad(vertices, triangles, new Vector3(-h.x,-h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(h.x,-h.y,-h.z));
        AddQuad(vertices, triangles, new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z),new Vector3(-h.x,-h.y,h.z));
        AddQuad(vertices, triangles, new Vector3(-h.x,-h.y,h.z),new Vector3(-h.x,h.y,h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,-h.y,-h.z));
        AddQuad(vertices, triangles, new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(h.x,h.y,h.z),new Vector3(h.x,-h.y,h.z));
        AddQuad(vertices, triangles, new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(h.x,h.y,-h.z));
        AddQuad(vertices, triangles, new Vector3(-h.x,-h.y,h.z),new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,-h.y,h.z));
        return Finish(vertices, triangles);
    }

    static Mesh MountainMesh()
    {
        Vector2[] ridge = { new Vector2(-9,0.7f),new Vector2(-7,1.7f),new Vector2(-5.3f,1.02f),new Vector2(-4.2f,2.15f),new Vector2(-3.2f,1.35f),new Vector2(-1.8f,1.8f),new Vector2(0,0.65f),new Vector2(1.4f,1.24f),new Vector2(2.5f,0.8f),new Vector2(3.8f,1.92f),new Vector2(5.4f,1.2f),new Vector2(7,2.0f),new Vector2(9,1.0f) };
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int i = 0; i < ridge.Length - 1; i++)
            AddQuad(vertices, triangles, new Vector3(ridge[i].x,0,0), new Vector3(ridge[i].x,ridge[i].y,0), new Vector3(ridge[i+1].x,ridge[i+1].y,0), new Vector3(ridge[i+1].x,0,0));
        return Finish(vertices, triangles);
    }

    static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = vertices.Count;
        vertices.AddRange(new[] { a, b, c });
        triangles.AddRange(new[] { i, i + 1, i + 2 });
    }

    static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = vertices.Count;
        vertices.AddRange(new[] { a, b, c, d });
        triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    static Mesh Finish(List<Vector3> vertices, List<int> triangles)
    {
        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
