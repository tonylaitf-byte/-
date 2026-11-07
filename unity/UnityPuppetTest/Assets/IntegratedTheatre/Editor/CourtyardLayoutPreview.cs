using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class CourtyardLayoutPreview
{
    const string Folder = "../../reports/courtyard-layout";
    public const string PreviewScene = "Assets/IntegratedTheatre/Scenes/CourtyardLayoutPreview.unity";
    const string Pending = "CourtyardLayoutPreview.Pending";
    static string OutputFolder => SessionState.GetString("CourtyardLayoutPreview.Output", Folder);
    static IEnumerator routine;
    static int lastFrame;
    static readonly List<string> diagnostics = new List<string>();
    [Serializable] sealed class Item
    {
        public string name, parent;
        public Vector3 position, size, scale;
    }
    [Serializable] sealed class Inspection
    {
        public Vector3 cameraPosition, cameraAngles;
        public float fieldOfView;
        public Item[] objects, actors;
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene(HybridTheatreBuilder.DuetScene);
        var environment = GameObject.Find("MoonlitTempleEnvironment");
        var camera = Camera.main;
        var report = new Inspection {
            cameraPosition = camera.transform.position, cameraAngles = camera.transform.eulerAngles,
            fieldOfView = camera.fieldOfView,
            objects = environment.GetComponentsInChildren<Renderer>().Select(r => new Item {
                name = r.name, parent = r.transform.parent.name,
                position = r.bounds.center, size = r.bounds.size, scale = r.transform.lossyScale
            }).ToArray(),
            actors = UnityEngine.Object.FindObjectsByType<HybridPuppetActor>(FindObjectsSortMode.None)
                .Select(a => new Item { name = a.name, position = a.transform.position,
                    size = BoundsOf(a.gameObject).size }).ToArray()
        };
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "inspection.json"), JsonUtility.ToJson(report, true));
    }
    static Bounds BoundsOf(GameObject target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return bounds;
    }
    public static void Create()
    {
        var scene = EditorSceneManager.OpenScene(HybridTheatreBuilder.DuetScene);
        EditorSceneManager.SaveScene(scene, PreviewScene, true);
        scene = EditorSceneManager.OpenScene(PreviewScene);
        var environment = GameObject.Find("MoonlitTempleEnvironment");
        var renderers = environment.GetComponentsInChildren<Renderer>();
        var left = renderers.Where(r => r.name.StartsWith("left hall") || r.name.StartsWith("left side hall")).ToArray();
        var right = renderers.Where(r => r.name.StartsWith("right hall") || r.name.StartsWith("right side hall")).ToArray();
        var hall = renderers.Where(r => r.name.Contains("gate") || r.name.StartsWith("inner temple")).ToArray();
        if (left.Length == 0 || right.Length == 0 || hall.Length == 0)
            throw new InvalidOperationException("Missing independent architectural groups.");
        Group(environment.transform, left, "Layout Left House").position += Vector3.right * 5.3f;
        Group(environment.transform, right, "Layout Right House").position -= Vector3.right * 5.3f;
        Group(environment.transform, hall, "Layout Central Hall").localScale = new Vector3(1.45f, 1, 1);
        // Expose the existing columns/lattice instead of burying them in the solid wall proxy.
        foreach (var r in left.Where(r => r.name == "left side hall body")) r.transform.position -= Vector3.right * .35f;
        foreach (var r in right.Where(r => r.name == "right side hall body")) r.transform.position += Vector3.right * .35f;
        foreach (int index in new[] { 2, 3 })
        {
            var lamps = renderers.Where(r => r.name.StartsWith("courtyard lantern " + index + " ")).ToArray();
            Group(environment.transform, lamps, "Layout Existing Lantern " + index).position += Vector3.right * (index == 2 ? 3.5f : -3.5f);
        }

        // Plain sizing proxies only. No detailed props, textures or rig edits.
        const string assets = "Assets/Environment/CourtyardLayoutBlockout";
        if (!AssetDatabase.IsValidFolder(assets)) AssetDatabase.CreateFolder("Assets/Environment", "CourtyardLayoutBlockout");
        var wood = FlatMaterial(assets + "/BlockoutWood.mat", new Color(.23f, .105f, .045f));
        var reed = FlatMaterial(assets + "/BlockoutMat.mat", new Color(.48f, .36f, .19f));
        var backdrop = GameObject.Find("MoonlitMountainBackdrop").GetComponent<Renderer>();
        string backdropPath = assets + "/LayoutBackdrop.mat";
        var backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(backdropPath);
        if (!backdropMaterial) { backdropMaterial = new Material(backdrop.sharedMaterial); AssetDatabase.CreateAsset(backdropMaterial, backdropPath); }
        backdropMaterial.mainTextureOffset = new Vector2(.285f, .15f);
        EditorUtility.SetDirty(backdropMaterial);
        backdrop.sharedMaterial = backdropMaterial;
        var furniture = new GameObject("Layout Banquet Size Proxies").transform;
        float floor = renderers.First(r => r.name == "wet stone courtyard").bounds.max.y;
        Box(furniture, "Large Floor Mat Placeholder", new Vector3(0, floor + .018f, -.9f), new Vector3(4.25f, .025f, 1.8f), reed);
        Box(furniture, "Low Banquet Table Placeholder", new Vector3(0, floor + .33f, -.9f), new Vector3(2.15f, .09f, 1.3f), wood);
        foreach (float x in new[] { -.9f, .9f })
        foreach (float z in new[] { -1.4f, -.4f })
            Box(furniture, "Low Table Leg", new Vector3(x, floor + .17f, z), new Vector3(.10f, .29f, .10f), wood);
        foreach (float x in new[] { -1.6f, 1.6f })
            Box(furniture, "Kneeling Mat Placeholder", new Vector3(x, floor + .045f, -.9f), new Vector3(.75f, .04f, 1.25f), reed);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "layout.txt"),
            "Independent preview scene: " + PreviewScene + "\nSide houses: 5.3 units inward each\nCentral architecture width: 1.45x\nExisting rear lanterns: 3.5 units inward each\nSeparate backdrop material reframes moonlight\nTable: 2.15 x 1.30, top height 0.375\nMain mat: 4.25 x 1.80\nTwo sitting mats: 0.75 x 1.25\nOriginal scenes and WebGL unchanged.\n");
        RenderScene(PreviewScene, Folder);
    }
    public static void RenderScene(string path, string outputFolder)
    {
        EditorSceneManager.OpenScene(path);
        Directory.CreateDirectory(outputFolder);
        SessionState.SetString("CourtyardLayoutPreview.Output", outputFolder);
        SessionState.SetBool(Pending, true);
        EditorPlayModeStartup.EnterAfterInitialization();
    }
    static Transform Group(Transform parent, Renderer[] renderers, string name)
    {
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.position = bounds.center;
        foreach (var r in renderers) r.transform.SetParent(group, true);
        return group;
    }
    static Material FlatMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.color = color; material.SetFloat("_Glossiness", .12f);
        return material;
    }
    static void Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name; item.transform.SetParent(parent, false);
        item.transform.position = center; item.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        item.GetComponent<Renderer>().sharedMaterial = material;
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        routine = null; lastFrame = -1; diagnostics.Clear();
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }
    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) diagnostics.Add(message + "\n" + trace); }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        string failure = "";
        try { if (routine == null) routine = Capture(); if (routine.MoveNext()) return; }
        catch (Exception error) { failure = error.ToString(); }
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        SessionState.SetBool(Pending, false);
        File.WriteAllText(Path.Combine(OutputFolder, "render-status.txt"), "Failure: " + failure + "\n" + string.Join("\n", diagnostics));
        EditorApplication.Exit(failure == "" ? 0 : 1);
    }
    static IEnumerator Capture()
    {
        yield return null;
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        if (!session || !session.Initialize()) throw new InvalidOperationException("Preview rig initialization failed.");
        session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
        foreach (var actor in new[] { session.leftActor, session.rightActor })
        {
            actor.showInstrument = true; actor.SetInstrumentVisible(true);
            actor.Accept(11, true, new PuppetControlState { version = 2, controlMode = "hybrid",
                inputEpoch = 1, handDetected = true, calibrated = true, sequenceId = 1 }, out _, out _);
        }
        for (int i = 0; i < 180; i++) session.Tick(1f / 60);
        yield return null; yield return null;
        SaveCamera("layout-front.png", 1600, 900);
        SaveCamera("layout-4x3.png", 1200, 900);
        var banquet = GameObject.Find("Courtyard Banquet Blender Assets");
        if (banquet)
        {
            var camera = Camera.main;
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            camera.transform.position = new Vector3(2.7f, 2.2f, -4.1f);
            camera.transform.LookAt(new Vector3(0, -.31f, -.9f));
            camera.fieldOfView = 43;
            SaveCamera("banquet-detail.png", 1400, 1000);
            camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov;
        }
    }
    static void SaveCamera(string filename, int width, int height)
    {
        var camera = Camera.main;
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var target = camera.targetTexture; var current = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        float aspect = camera.aspect;
        try
        {
            camera.aspect = (float)width / height; camera.targetTexture = rt; camera.Render();
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(OutputFolder, filename), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = target; camera.aspect = aspect; RenderTexture.active = current;
            RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
