using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PalmTheatre.Integration;

// Promote the approved art scene into every performance scene without copying
// its actors, controllers, camera or preview-only validation behaviours.
public static class IntegratedStageAssets
{
    [Serializable] sealed class Row { public string scene; public int listeners, banquetRenderers, heroBindings; }
    [Serializable] sealed class Report { public bool passed; public Row[] scenes; }

    public static void Prepare()
    {
        var rows = IntegratedTheatreBuilder.Scenes.Skip(1).Select(Apply).ToArray();
        var entry = EditorSceneManager.OpenScene(IntegratedTheatreBuilder.Scenes[0]);
        var bridge = UnityEngine.Object.FindAnyObjectByType<TheatreBridge>();
        var shader = Shader.Find("Puppet/Refined Surface Preview");
        if (!bridge || !shader) throw new InvalidOperationException("The integrated bridge/refined shader is missing.");
        bridge.retainedShaders = bridge.retainedShaders.Concat(new[] { shader }).Where(s => s).Distinct().ToArray();
        EditorSceneManager.SaveScene(entry);
        Directory.CreateDirectory("../../reports");
        File.WriteAllText("../../reports/IntegrationSceneValidation.json", JsonUtility.ToJson(new Report { passed = true, scenes = rows }, true));
    }

    static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    static GameObject Named(Scene scene, string name) => Components<Transform>(scene).FirstOrDefault(t => t.name == name)?.gameObject;
    static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

    static Row Apply(string path)
    {
        var target = EditorSceneManager.OpenScene(path);
        var source = EditorSceneManager.OpenScene(HeroRefinementPreview.Scene, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(target);
        try
        {
            foreach (string name in new[] { "MoonlitTempleEnvironment", "MoonlitMountainBackdrop", "Courtyard Banquet Blender Assets" })
            {
                var input = Named(source, name);
                if (!input) throw new InvalidOperationException("Art scene object missing: " + name);
                var existing = Named(target, name);
                if (existing) UnityEngine.Object.DestroyImmediate(existing);
                var copy = UnityEngine.Object.Instantiate(input);
                copy.name = name;
                SceneManager.MoveGameObjectToScene(copy, target);
            }
            var old = Named(target, "Integrated hero surfaces");
            if (old) UnityEngine.Object.DestroyImmediate(old);
            var template = Components<HeroSurfaceRefinement>(source).Single();
            var renderers = Components<Renderer>(target);
            var driver = new GameObject("Integrated hero surfaces").AddComponent<HeroSurfaceRefinement>();
            driver.bindings = template.bindings.Select(binding => new HeroSurfaceRefinement.Binding {
                renderer = renderers.Single(r => r.name == binding.renderer.name && MeshOf(r) == MeshOf(binding.renderer)),
                refined = binding.refined.ToArray()
            }).ToArray();
            driver.applyOnStart = true;
            var listeners = Components<AudioListener>(target);
            if (listeners.Length == 0)
            {
                var camera = Components<Camera>(target).First(c => c.CompareTag("MainCamera"));
                camera.gameObject.AddComponent<AudioListener>();
            }
            listeners = Components<AudioListener>(target);
            if (listeners.Length != 1 || !listeners[0].enabled) throw new InvalidOperationException("A stage must have exactly one enabled AudioListener: " + path);
            var banquet = Named(target, "Courtyard Banquet Blender Assets");
            var row = new Row { scene = path, listeners = listeners.Length,
                banquetRenderers = banquet.GetComponentsInChildren<Renderer>().Length, heroBindings = driver.bindings.Length };
            if (row.banquetRenderers == 0 || row.heroBindings == 0) throw new InvalidOperationException("Integrated art bindings are empty.");
            EditorSceneManager.SaveScene(target);
            return row;
        }
        finally { EditorSceneManager.CloseScene(source, true); }
    }
}
