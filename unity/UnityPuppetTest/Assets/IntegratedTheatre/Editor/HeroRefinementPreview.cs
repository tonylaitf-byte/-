using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class HeroRefinementPreview
{
    const string Folder = "../../reports/hero-refinement";
    public const string Scene = "Assets/IntegratedTheatre/Scenes/HeroRefinementPreview.unity";
    const string Pending = "HeroRefinementPreview.Pending";
    static IEnumerator routine;
    static int lastFrame;
    static Report report;
    [Serializable] sealed class Report
    {
        public bool passed, sameMeshes, sameBoneBindings, materialSwapPreservesPose, liveControlMoves, headControlMoves;
        public int materials, testedFrames;
        public string failure = "", scene = Scene;
        public List<string> engineDiagnostics = new List<string>();
    }
    [Serializable] sealed class Surface
    {
        public string renderer, material, shader, texture, normal, mesh;
        public Color color;
        public float metallic, smoothness;
        public int vertices, slots;
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene(CourtyardBanquetPreview.Scene);
        var actor = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>().leftActor;
        var surfaces = actor.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials.Select(m => {
            var mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            return new Surface { renderer = r.name, material = m.name, shader = m.shader.name,
                texture = m.mainTexture ? AssetDatabase.GetAssetPath(m.mainTexture) : "",
                normal = m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") ? AssetDatabase.GetAssetPath(m.GetTexture("_BumpMap")) : "",
                color = m.HasProperty("_Color") ? m.color : Color.white,
                metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0,
                smoothness = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0,
                vertices = mesh ? mesh.vertexCount : 0, slots = r.sharedMaterials.Length,
                mesh = AssetDatabase.GetAssetPath(mesh) };
        })).ToArray();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/inspection.txt", string.Join("\n", surfaces.Select(s => JsonUtility.ToJson(s))));
    }
    public static void Create()
    {
        var scene = EditorSceneManager.OpenScene(CourtyardBanquetPreview.Scene);
        EditorSceneManager.SaveScene(scene, Scene, true);
        scene = EditorSceneManager.OpenScene(Scene);
        var actor = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>().leftActor;
        const string assets = "Assets/Environment/HeroRefinement";
        if (!AssetDatabase.IsValidFolder(assets)) AssetDatabase.CreateFolder("Assets/Environment", "HeroRefinement");
        var shader = Shader.Find("Puppet/Refined Surface Preview");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Refined shader compilation failed.");
        var refinement = actor.gameObject.AddComponent<HeroSurfaceRefinement>();
        var cache = new Dictionary<string, Material>();
        refinement.bindings = actor.GetComponentsInChildren<Renderer>(true).Select(r => new HeroSurfaceRefinement.Binding {
            renderer = r, refined = r.sharedMaterials.Select(m => {
                if (cache.TryGetValue(m.name, out var existing)) return existing;
                var material = SurfaceMaterial(m, shader, assets);
                cache[m.name] = material;
                return material;
            }).ToArray()
        }).ToArray();
        refinement.applyOnStart = true;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/surfaces.txt", string.Join("\n", cache.Select(kv => kv.Key + " -> " + AssetDatabase.GetAssetPath(kv.Value))));
        SessionState.SetBool(Pending, true);
        EditorPlayModeStartup.EnterAfterInitialization();
    }
    static Material SurfaceMaterial(Material source, Shader shader, string assets)
    {
        string key = source.name;
        string filename = "Hero_" + (key == "古金" ? "AntiqueGold" : key.Replace(" ", "_"));
        string path = assets + "/" + filename + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        Color color = source.color;
        float profile = 0, metal = 0, smoothness = .32f, detail = .3f;
        switch (key)
        {
            case "Royal_Blue": color = new Color(.045f, .14f, .31f); smoothness = .34f; break;
            case "Base Color": color = new Color(.03f, .085f, .21f); smoothness = .3f; break;
            case "base_shadow": color = new Color(.018f, .035f, .075f); smoothness = .25f; break;
            case "Red": color = new Color(.42f, .032f, .022f); smoothness = .3f; break;
            case "White_Hair": color = new Color(.79f, .80f, .77f); profile = 3; smoothness = .26f; detail = .22f; break;
            case "Royal_Gold": color = new Color(.74f, .49f, .17f); profile = 1; metal = .52f; smoothness = .43f; break;
            case "Crown_Gold": color = new Color(.80f, .57f, .22f); profile = 1; metal = .78f; smoothness = .51f; break;
            case "古金": color = new Color(.53f, .35f, .14f); profile = 1; metal = .62f; smoothness = .38f; break;
            case "Skin": color = new Color(.84f, .60f, .46f); profile = 2; smoothness = .31f; detail = .18f; break;
            case "Sword_ColdSteel": color = new Color(.61f, .68f, .75f); profile = 5; metal = .85f; smoothness = .74f; detail = .22f; break;
            case "Jug_Lacquered_Red": color = new Color(.22f, .018f, .012f); profile = 4; smoothness = .67f; break;
            default: throw new InvalidOperationException("Unreviewed hero surface: " + key);
        }
        material.SetColor("_Color", color);
        material.mainTexture = source.mainTexture;
        material.SetFloat("_Profile", profile); material.SetFloat("_Metallic", metal);
        material.SetFloat("_Glossiness", smoothness); material.SetFloat("_DetailStrength", detail);
        material.SetFloat("_DetailScale", profile == 3 ? 120 : 90);
        EditorUtility.SetDirty(material);
        return material;
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        routine = null; lastFrame = -1; report = new Report();
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) report.engineDiagnostics.Add(message + "\n" + trace); }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (routine == null) routine = Review(); if (routine.MoveNext()) return; report.passed = true; }
        catch (Exception error) { report.failure = error.ToString(); }
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        File.WriteAllText(Folder + "/Validation.json", JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.passed ? 0 : 1);
    }
    static IEnumerator Review()
    {
        yield return null; yield return null;
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        if (!session || !session.Initialize()) throw new InvalidOperationException("Hero scene failed to initialize.");
        session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
        var actor = session.leftActor;
        var refinement = actor.GetComponent<HeroSurfaceRefinement>();
        report.materials = refinement.bindings.Sum(b => b.refined.Length);
        var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>();
        var meshes = skins.Select(s => s.sharedMesh).ToArray();
        var bones = skins.Select(s => s.bones).ToArray();
        var rootBones = skins.Select(s => s.rootBone).ToArray();
        for (int frame = 0; frame < 180; frame++) session.Tick(1f / 60);
        yield return null;
        refinement.SetRefined(false);
        CaptureViews("before", actor);
        var pose = actor.bones.Select(b => b.bone.localRotation).ToArray();
        refinement.SetRefined(true);
        report.materialSwapPreservesPose = actor.bones.Select((b, i) => Quaternion.Angle(b.bone.localRotation, pose[i]) < .001f).All(v => v);
        yield return null;
        CaptureViews("after", actor);
        report.sameMeshes = skins.Select((s, i) => s.sharedMesh == meshes[i]).All(v => v);
        report.sameBoneBindings = skins.Select((s, i) => s.bones.SequenceEqual(bones[i]) && s.rootBone == rootBones[i]).All(v => v);
        Vector3 start = actor.mapping.rightHand.position;
        Quaternion initialHead = actor.mapping.head.localRotation;
        float motion = 0;
        float headMotion = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            float t = frame / 60f;
            // Exercise arms and head independently: small per-frame arm changes
            // during head motion are intentionally held by the existing head-only gate.
            bool headPhase = frame >= 180;
            bool accepted = actor.Accept(11, true, new PuppetControlState { version = 2, controlMode = "hybrid", inputEpoch = 12345,
                sequenceId = frame + 1, sampleDt = 1f / 60, handDetected = true, calibrated = true,
                leftHandY = headPhase ? 0 : Mathf.Sin(t * 2) * .6f,
                rightHandY = headPhase ? 0 : -Mathf.Sin(t * 2) * .6f,
                headPitch = headPhase ? Mathf.Sin(t) * .5f : 0,
                headYaw = headPhase ? Mathf.Cos(t) * .4f : 0 }, out _, out _);
            if (!accepted) throw new InvalidOperationException("Synthetic control was rejected.");
            session.Tick(1f / 60);
            motion = Mathf.Max(motion, Vector3.Distance(start, actor.mapping.rightHand.position));
            headMotion = Mathf.Max(headMotion, Quaternion.Angle(initialHead, actor.mapping.head.localRotation));
            report.testedFrames++;
        }
        report.liveControlMoves = motion > .03f;
        report.headControlMoves = headMotion > 1;
        if (!report.sameMeshes || !report.sameBoneBindings || !report.materialSwapPreservesPose || !report.liveControlMoves || !report.headControlMoves)
            throw new InvalidOperationException("Hero refinement changed a rig invariant or blocked mapping.");
    }
    static void CaptureViews(string label, HybridPuppetActor actor)
    {
        var camera = Camera.main;
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        float fov = camera.fieldOfView;
        Save(camera, label + "-stage.png", 1600, 900);
        Vector3 focus = actor.mapping.head.position + Vector3.down * .65f;
        camera.transform.position = focus + new Vector3(0, .12f, -3.5f);
        camera.transform.LookAt(focus); camera.fieldOfView = 38;
        Save(camera, label + "-hero.png", 1400, 1400);
        focus = actor.mapping.head.position + Vector3.up * .055f;
        camera.transform.position = focus + new Vector3(.08f, .015f, -1.6f);
        camera.transform.LookAt(focus); camera.fieldOfView = 31;
        Save(camera, label + "-head.png", 1200, 1200);
        camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov;
    }
    static void Save(Camera camera, string name, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var target = camera.targetTexture; var active = RenderTexture.active;
        float aspect = camera.aspect;
        var backdrop = GameObject.Find("MoonlitMountainBackdrop").transform;
        Vector3 backdropScale = backdrop.localScale;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = (float)width / height; camera.targetTexture = rt;
            float viewHeight = 2 * Mathf.Abs(backdrop.localPosition.z) * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            backdrop.localScale = new Vector3(Mathf.Max(backdropScale.x, viewHeight * camera.aspect * 1.02f),
                Mathf.Max(backdropScale.y, viewHeight * 1.02f), backdropScale.z);
            camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "/" + name, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = target; camera.aspect = aspect; RenderTexture.active = active;
            backdrop.localScale = backdropScale;
            RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
