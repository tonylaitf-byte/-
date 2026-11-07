using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class ProductionTheatreArt
{
    const string Assets = "Assets/Environment/CourtyardProduction";
    static readonly string[] Roots = { "MoonlitTempleEnvironment", "MoonlitMountainBackdrop", "Courtyard Banquet Blender Assets", "MoonlitEnvironmentLights", "Directional Light" };
    sealed class Surface { public Mesh mesh; public Material[] original, refined; }
    [Serializable] sealed class Check { public string scene; public float tableHeight; public int refinedRenderers, cameras; public bool rigsUnchanged; }
    [Serializable] sealed class Report { public Check[] scenes; }

    public static void Apply()
    {
        var preview = EditorSceneManager.OpenScene(HeroRefinementPreview.Scene);
        var source = UnityEngine.Object.FindAnyObjectByType<HeroSurfaceRefinement>();
        if (!source) throw new InvalidOperationException("Approved hero materials are missing.");
        var heroFacing = source.bindings.First(b => b.renderer is SkinnedMeshRenderer).renderer.transform.root.rotation;
        var surfaces = source.bindings.Select(b => new Surface {
            mesh = MeshOf(b.renderer), original = b.renderer.sharedMaterials, refined = b.refined }).ToArray();
        if (!AssetDatabase.IsValidFolder(Assets)) AssetDatabase.CreateFolder("Assets/Environment", "CourtyardProduction");
        var prefabs = new List<GameObject>();
        var rootNames = new HashSet<string>();
        foreach (string name in Roots)
        {
            var root = GameObject.Find(name);
            if (!root) throw new InvalidOperationException("Missing approved art: " + name);
            root = root.transform.root.gameObject;
            if (!rootNames.Add(root.name)) continue;
            prefabs.Add(PrefabUtility.SaveAsPrefabAsset(root, Assets + "/" + root.name.Replace(" ", "_") + ".prefab"));
        }
        rootNames.UnionWith(prefabs.Select(p => p.name));
        var checks = new List<Check>();
        foreach (string path in IntegratedTheatreBuilder.Scenes.Skip(1))
        {
            var scene = EditorSceneManager.OpenScene(path);
            var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var meshes = skins.Select(s => s.sharedMesh).ToArray();
            var bones = skins.Select(s => s.bones).ToArray();
            foreach (string name in rootNames.Concat(new[] { "Production Hero Surfaces" }))
            {
                var old = GameObject.Find(name);
                if (old) UnityEngine.Object.DestroyImmediate(old);
            }
            foreach (var prefab in prefabs) PrefabUtility.InstantiatePrefab(prefab, scene);
            var bindings = new List<HeroSurfaceRefinement.Binding>();
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var surface = surfaces.FirstOrDefault(s => s.mesh && s.mesh == MeshOf(renderer) &&
                    renderer.sharedMaterials.SequenceEqual(s.original));
                if (surface != null) bindings.Add(new HeroSurfaceRefinement.Binding { renderer = renderer, refined = surface.refined });
            }
            if (bindings.Count != surfaces.Length) throw new InvalidOperationException("Hero surface binding mismatch in " + path + ": " + bindings.Count);
            if (Path.GetFileName(path).StartsWith("Solo"))
                bindings.First(b => b.renderer is SkinnedMeshRenderer).renderer.transform.root.rotation = heroFacing;
            var refinement = new GameObject("Production Hero Surfaces").AddComponent<HeroSurfaceRefinement>();
            refinement.bindings = bindings.ToArray();
            float floor = GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
            var table = GameObject.Find("Low Table Mesh").GetComponent<Renderer>();
            var check = new Check { scene = path, tableHeight = table.bounds.max.y - floor, refinedRenderers = bindings.Count,
                cameras = UnityEngine.Object.FindObjectsByType<Camera>().Length,
                rigsUnchanged = skins.Select((s, i) => s.sharedMesh == meshes[i] && s.bones.SequenceEqual(bones[i])).All(v => v) };
            if (Mathf.Abs(check.tableHeight - .875f) > .001f || !check.rigsUnchanged || check.cameras != 1)
                throw new InvalidOperationException("Production art validation failed in " + path);
            checks.Add(check);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../../reports/production-art");
        File.WriteAllText("../../reports/production-art/Integration.json", JsonUtility.ToJson(new Report { scenes = checks.ToArray() }, true));
        Debug.Log("PRODUCTION_THEATRE_ART_PASS");
    }
    static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
}
