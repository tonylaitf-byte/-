using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CourtyardBanquetPreview
{
    public const string Scene = "Assets/IntegratedTheatre/Scenes/CourtyardBanquetPreview.unity";
    const string Assets = "Assets/Environment/CourtyardBanquet";
    const string Folder = "../../reports/courtyard-banquet";
    [Serializable] sealed class Surface { public string name; public float[] color; public float roughness, metallic; }
    [Serializable] sealed class Manifest { public Surface[] materials; }
    [Serializable] sealed class Report
    {
        public string scene, model, sourceBlend;
        public Vector3 boundsCenter, boundsSize;
        public int meshRenderers, materialSlots, triangles;
        public float additionalHouseOutward = .55f;
        public bool originalScenesUnchanged = true, webGLRebuilt = false;
    }
    public static void Create()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var scene = EditorSceneManager.OpenScene(CourtyardLayoutPreview.PreviewScene);
        EditorSceneManager.SaveScene(scene, Scene, true);
        scene = EditorSceneManager.OpenScene(Scene);
        GameObject.Find("Layout Left House").transform.position -= Vector3.right * .55f;
        GameObject.Find("Layout Right House").transform.position += Vector3.right * .55f;
        // Give each roof joint a little sky, and let the moon peek past the left eave.
        var backdrop = GameObject.Find("MoonlitMountainBackdrop").GetComponent<Renderer>();
        string backdropPath = Assets + "/BanquetBackdrop.mat";
        var background = AssetDatabase.LoadAssetAtPath<Material>(backdropPath);
        if (!background) { background = new Material(backdrop.sharedMaterial); AssetDatabase.CreateAsset(background, backdropPath); }
        background.mainTextureOffset = new Vector2(.25f, .18f);
        backdrop.sharedMaterial = background; EditorUtility.SetDirty(background);

        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Assets + "/banquet_manifest.json"));
        var surfaces = manifest.materials.ToDictionary(s => s.name, MaterialFor);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Assets + "/courtyard_banquet_v1.fbx");
        if (!model) throw new InvalidOperationException("Blender banquet FBX import failed.");
        var proxies = GameObject.Find("Layout Banquet Size Proxies");
        if (proxies) UnityEngine.Object.DestroyImmediate(proxies);
        var instance = new GameObject("Courtyard Banquet Blender Assets");
        var imported = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        imported.transform.SetParent(instance.transform, false);
        imported.transform.localRotation = Quaternion.Euler(0, 180, 0) * imported.transform.localRotation;
        float floor = GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        instance.transform.SetPositionAndRotation(new Vector3(0, floor, -.9f), Quaternion.identity);
        var renderers = instance.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in renderers)
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => surfaces.TryGetValue(m.name, out var material) ? material : m).ToArray();
            if (r.sharedMaterials.Any(m => !m || !m.shader || m.shader.name == "Hidden/InternalErrorShader"))
                throw new InvalidOperationException("Missing banquet material: " + r.name);
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        }
        foreach (float x in new[] { -2.45f, 2.45f })
        {
            var item = new GameObject("Banquet floor lantern light");
            item.transform.SetParent(instance.transform, false);
            item.transform.localPosition = new Vector3(x, .34f, -.84f);
            var light = item.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1, .55f, .21f);
            light.intensity = 1.05f; light.range = 2.2f; light.shadows = LightShadows.None;
        }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(Folder + "/import-diagnostic.txt", renderers.Select(r => r.name +
            " position=" + r.transform.position + " scale=" + r.transform.lossyScale +
            " worldBounds=" + r.bounds + " meshBounds=" + r.GetComponent<MeshFilter>().sharedMesh.bounds));
        // Reject centimeter-scale, flat-on-edge or axis-flipped imports.
        if (bounds.size.x < 5 || bounds.size.x > 9 || bounds.size.y < .7f || bounds.size.y > 2)
            throw new InvalidOperationException("Unexpected Blender import bounds: " + bounds.size);
        if (renderers.Length > 64 || instance.GetComponentsInChildren<Collider>().Length != 0)
            throw new InvalidOperationException("Unexpected banquet render/collider configuration.");
        var report = new Report { scene = Scene, model = Assets + "/courtyard_banquet_v1.fbx",
            sourceBlend = "ArtSources/CourtyardBanquet/courtyard_banquet_v1.blend",
            boundsCenter = bounds.center, boundsSize = bounds.size,
            meshRenderers = renderers.Length, materialSlots = renderers.Sum(r => r.sharedMaterials.Length),
            triangles = instance.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3) };
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/Integration.json", JsonUtility.ToJson(report, true));
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        CourtyardLayoutPreview.RenderScene(Scene, Folder);
    }
    static Material MaterialFor(Surface surface)
    {
        string path = Assets + "/" + surface.name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.color = new Color(surface.color[0], surface.color[1], surface.color[2], 1).gamma;
        material.SetFloat("_Glossiness", 1 - surface.roughness); material.SetFloat("_Metallic", surface.metallic);
        string albedo = Assets + "/Textures/" + surface.name + "_albedo.png";
        if (File.Exists(albedo))
        {
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo); material.color = Color.white;
            string normal = Assets + "/Textures/" + surface.name + "_normal.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(normal);
            if (importer.textureType != TextureImporterType.NormalMap)
            { importer.textureType = TextureImporterType.NormalMap; importer.maxTextureSize = 512; importer.SaveAndReimport(); }
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            material.SetFloat("_BumpScale", surface.name == "BanquetWood" ? .18f : .45f); material.EnableKeyword("_NORMALMAP");
        }
        if (surface.name == "BanquetGlow")
        { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(1, .30f, .035f) * 2.5f); }
        EditorUtility.SetDirty(material);
        return material;
    }
}
