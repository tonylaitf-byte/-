using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PuppetBackgroundIntegrationTest
{
    private const string SourceScene = "Assets/Scenes/PuppetRigTest.unity";
    private const string OutputScene = "Assets/Scenes/PuppetBackgroundTest.unity";
    private const string EnvironmentFolder = "Assets/Environment/MoonlitTemple";
    private const string ModelPath = EnvironmentFolder + "/moonlit_temple_v8_architecture.fbx";
    private const string BackdropPath = EnvironmentFolder + "/moonlit_mountain_backplate_v2.png";

    [Serializable]
    private sealed class IntegrationReport
    {
        public bool success;
        public string sourceScene;
        public string outputScene;
        public string modelAsset;
        public string backdropAsset;
        public int environmentRendererCount;
        public int environmentMeshCount;
        public int environmentVertexCount;
        public int puppetRendererCount;
        public int backdropWidth;
        public int backdropHeight;
        public float environmentWidth;
        public float environmentHeight;
        public float environmentDepth;
        public float puppetHeight;
        public float averageLuminance;
        public float luminanceDeviation;
        public float nonBlackRatio;
        public float moonRegionPeakLuminance;
        public float moonRegionBrightRatio;
        public bool puppetControllerPreserved;
        public bool httpReceiverPreserved;
        public bool swordPreserved;
        public string previewPath;
        public string[] errors;
    }

    [MenuItem("Tools/Puppet/Run Background Integration Test")]
    public static void Run()
    {
        var errors = new List<string>();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string reportPath = Path.Combine(projectRoot, "PuppetBackgroundIntegrationReport.json");
        string previewPath = Path.Combine(projectRoot, "PuppetBackgroundIntegrationPreview.png");

        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporters();

            GameObject environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Texture2D backdropTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackdropPath);
            if (environmentPrefab == null)
                throw new InvalidOperationException("Unity could not import the moonlit temple FBX.");
            if (backdropTexture == null)
                throw new InvalidOperationException("Unity could not import the mountain backdrop PNG.");
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene))
                throw new InvalidOperationException("The original PuppetRigTest scene is missing.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OutputScene) != null)
                AssetDatabase.DeleteAsset(OutputScene);
            if (!AssetDatabase.CopyAsset(SourceScene, OutputScene))
                throw new InvalidOperationException("Unity could not copy PuppetRigTest to an independent test scene.");

            Scene scene = EditorSceneManager.OpenScene(OutputScene, OpenSceneMode.Single);
            PuppetBoneTest controller = FindInScene<PuppetBoneTest>(scene);
            PuppetHttpReceiver receiver = FindInScene<PuppetHttpReceiver>(scene);
            Camera camera = FindInScene<Camera>(scene);
            if (controller == null)
                throw new InvalidOperationException("PuppetBoneTest was not preserved in the copied scene.");
            if (receiver == null || receiver.target != controller)
                errors.Add("PuppetHttpReceiver or its PuppetBoneTest target was not preserved.");
            if (camera == null)
                throw new InvalidOperationException("The copied scene has no camera.");

            RemoveExistingEnvironment(scene);
            GameObject environment = (GameObject)PrefabUtility.InstantiatePrefab(environmentPrefab, scene);
            environment.name = "MoonlitTempleEnvironment";
            environment.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            environment.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(
                environment,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction
            );

            ApplyEnvironmentMaterials(environment);
            ScaleRearArchitecture(environment, 0.84f, 2.5f);
            GameObject backdrop = CreateBackdrop(camera, backdropTexture, scene);
            AddEnvironmentLights(scene);
            ConfigureCamera(camera);

            Bounds environmentBounds = CalculateBounds(environment);
            Bounds puppetBounds = CalculateBounds(controller.gameObject);
            Renderer[] environmentRenderers = environment.GetComponentsInChildren<Renderer>(true);
            MeshFilter[] environmentMeshes = environment.GetComponentsInChildren<MeshFilter>(true);
            int environmentVertices = environmentMeshes.Sum(
                mesh => mesh.sharedMesh != null ? mesh.sharedMesh.vertexCount : 0
            );
            Renderer[] puppetRenderers = controller.GetComponentsInChildren<Renderer>(true);

            AlignStage(environment, environmentBounds, puppetBounds);
            environmentBounds = CalculateBounds(environment);
            FrameScene(camera, puppetBounds, environmentBounds);

            RenderMetrics metrics = RenderPreview(camera, previewPath);
            bool swordPreserved = controller
                .GetComponentsInChildren<Transform>(true)
                .Any(item => item.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0);

            if (environmentRenderers.Length < 100)
                errors.Add("Too few environment renderers were imported from the FBX.");
            if (environmentVertices < 1000)
                errors.Add("The environment FBX contains too little mesh geometry.");
            if (!File.Exists(previewPath) || new FileInfo(previewPath).Length < 10000)
                errors.Add("Unity did not produce a usable integration preview.");
            if (metrics.nonBlackRatio < 0.20f || metrics.deviation < 0.015f)
                errors.Add("The rendered preview appears blank or lacks visible detail.");
            if (metrics.moonRegionPeak < 0.65f || metrics.moonRegionBrightRatio < 0.002f)
                errors.Add("The moon is not clearly visible above and left of the temple roof.");
            if (!swordPreserved)
                errors.Add("The sword object was not found in the copied puppet hierarchy.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, OutputScene);
            AssetDatabase.SaveAssets();

            var report = new IntegrationReport
            {
                success = errors.Count == 0,
                sourceScene = SourceScene,
                outputScene = OutputScene,
                modelAsset = ModelPath,
                backdropAsset = BackdropPath,
                environmentRendererCount = environmentRenderers.Length,
                environmentMeshCount = environmentMeshes.Length,
                environmentVertexCount = environmentVertices,
                puppetRendererCount = puppetRenderers.Length,
                backdropWidth = backdropTexture.width,
                backdropHeight = backdropTexture.height,
                environmentWidth = environmentBounds.size.x,
                environmentHeight = environmentBounds.size.y,
                environmentDepth = environmentBounds.size.z,
                puppetHeight = puppetBounds.size.y,
                averageLuminance = metrics.average,
                luminanceDeviation = metrics.deviation,
                nonBlackRatio = metrics.nonBlackRatio,
                moonRegionPeakLuminance = metrics.moonRegionPeak,
                moonRegionBrightRatio = metrics.moonRegionBrightRatio,
                puppetControllerPreserved = controller != null,
                httpReceiverPreserved = receiver != null && receiver.target == controller,
                swordPreserved = swordPreserved,
                previewPath = previewPath,
                errors = errors.ToArray()
            };
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log(
                $"Puppet background integration test: {(report.success ? "PASS" : "FAIL")} - {reportPath}"
            );

            if (!report.success)
            {
                if (Application.isBatchMode)
                    EditorApplication.Exit(2);
                throw new InvalidOperationException(string.Join(" ", errors));
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            var failed = new IntegrationReport
            {
                success = false,
                sourceScene = SourceScene,
                outputScene = OutputScene,
                modelAsset = ModelPath,
                backdropAsset = BackdropPath,
                previewPath = previewPath,
                errors = errors.Distinct().ToArray()
            };
            File.WriteAllText(reportPath, JsonUtility.ToJson(failed, true));
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(2);
            throw;
        }
    }

    private static void ConfigureImporters()
    {
        if (AssetImporter.GetAtPath(ModelPath) is ModelImporter modelImporter)
        {
            bool changed = modelImporter.materialImportMode != ModelImporterMaterialImportMode.None ||
                           modelImporter.importAnimation ||
                           modelImporter.importCameras ||
                           modelImporter.importLights;
            modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            modelImporter.importAnimation = false;
            modelImporter.importCameras = false;
            modelImporter.importLights = false;
            modelImporter.isReadable = true;
            if (changed)
                modelImporter.SaveAndReimport();
        }

        if (AssetImporter.GetAtPath(BackdropPath) is TextureImporter textureImporter)
        {
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.sRGBTexture = true;
            textureImporter.mipmapEnabled = false;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.maxTextureSize = 4096;
            textureImporter.SaveAndReimport();
        }
    }

    private static void ApplyEnvironmentMaterials(GameObject environment)
    {
        Material stone = GetOrCreateMaterial("MoonlitStone", new Color(0.23f, 0.25f, 0.28f), 0.05f, 0.22f);
        Material roof = GetOrCreateMaterial("MoonlitRoof", new Color(0.075f, 0.095f, 0.12f), 0.12f, 0.32f);
        Material wood = GetOrCreateMaterial("MoonlitWood", new Color(0.16f, 0.075f, 0.045f), 0.04f, 0.24f);
        Material wall = GetOrCreateMaterial("MoonlitWall", new Color(0.34f, 0.31f, 0.27f), 0.02f, 0.18f);
        Material gold = GetOrCreateMaterial("MoonlitGold", new Color(0.58f, 0.38f, 0.09f), 0.52f, 0.48f);
        Material foliage = GetOrCreateMaterial("MoonlitFoliage", new Color(0.055f, 0.14f, 0.12f), 0.01f, 0.14f);
        Material warm = GetOrCreateMaterial("MoonlitLantern", new Color(0.82f, 0.31f, 0.08f), 0.02f, 0.55f, true);

        foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
        {
            string name = renderer.gameObject.name.ToLowerInvariant();
            Material material;
            if (ContainsAny(name, "gold", "gild", "plaque", "finial"))
                material = gold;
            else if (ContainsAny(name, "lantern", "flame", "warm"))
                material = warm;
            else if (ContainsAny(name, "roof", "tile", "eave", "fascia", "ridge"))
                material = roof;
            else if (ContainsAny(name, "tree", "leaf", "foliage", "canopy", "bush"))
                material = foliage;
            else if (ContainsAny(name, "courtyard", "stone", "step", "plinth", "foundation", "foot", "paving", "base"))
                material = stone;
            else if (ContainsAny(name, "wall", "body"))
                material = wall;
            else
                material = wood;

            int count = Mathf.Max(1, renderer.sharedMaterials.Length);
            renderer.sharedMaterials = Enumerable.Repeat(material, count).ToArray();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static Material GetOrCreateMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness,
        bool emission = false
    )
    {
        string path = $"{EnvironmentFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Standard");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        if (emission)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.4f);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreateBackdrop(Camera camera, Texture2D texture, Scene scene)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "MoonlitMountainBackdrop";
        SceneManager.MoveGameObjectToScene(quad, scene);
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(camera.transform, false);

        float distance = 80f;
        float viewHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float viewWidth = viewHeight * (16f / 9f);

        quad.transform.localPosition = new Vector3(0f, 0f, distance);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(viewWidth * 1.02f, viewHeight * 1.02f, 1f);

        string materialPath = EnvironmentFolder + "/MoonlitBackdrop.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        Shader shader = Shader.Find("Unlit/Texture");
        if (material == null)
        {
            material = new Material(shader) { name = "MoonlitBackdrop" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.mainTexture = texture;
        // Crop the 2:1 source to 16:9 and place the moon outside the temple roof.
        material.mainTextureScale = new Vector2(0.66f, 0.75f);
        material.mainTextureOffset = new Vector2(0.325f, 0.23f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(material);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        return quad;
    }

    private static void AddEnvironmentLights(Scene scene)
    {
        GameObject root = new GameObject("MoonlitEnvironmentLights");
        SceneManager.MoveGameObjectToScene(root, scene);

        GameObject moonObject = new GameObject("Moon Key Light");
        moonObject.transform.SetParent(root.transform, false);
        Light moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.48f, 0.62f, 0.82f);
        moon.intensity = 0.58f;
        moon.shadows = LightShadows.Soft;
        moonObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

        AddPointLight(root.transform, "Left Lantern Fill", new Vector3(-5.2f, 1.6f, -7.5f));
        AddPointLight(root.transform, "Right Lantern Fill", new Vector3(5.2f, 1.6f, -7.5f));
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.14f, 0.17f, 0.23f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.12f, 0.16f, 0.22f);
        RenderSettings.fogDensity = 0.006f;
    }

    private static void AddPointLight(Transform parent, string name, Vector3 position)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.47f, 0.18f);
        light.intensity = 2.2f;
        light.range = 7f;
        light.shadows = LightShadows.Soft;
    }

    private static void ConfigureCamera(Camera camera)
    {
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
        camera.fieldOfView = 32f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 200f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
    }

    private static void AlignStage(GameObject environment, Bounds environmentBounds, Bounds puppetBounds)
    {
        float heightRatio = environmentBounds.size.y / Mathf.Max(puppetBounds.size.y, 0.01f);
        float scale = heightRatio < 1.5f || heightRatio > 12f
            ? Mathf.Clamp((puppetBounds.size.y * 4.8f) / Mathf.Max(environmentBounds.size.y, 0.01f), 0.1f, 4f)
            : 1f;
        environment.transform.localScale = Vector3.one * scale;
        environmentBounds = CalculateBounds(environment);
        environment.transform.position += new Vector3(
            puppetBounds.center.x - environmentBounds.center.x,
            puppetBounds.min.y - environmentBounds.min.y,
            0f
        );
    }

    private static void ScaleRearArchitecture(GameObject environment, float scale, float moveBack)
    {
        Transform[] candidates = environment
            .GetComponentsInChildren<Renderer>(true)
            .Select(renderer => renderer.transform)
            .Where(transform => IsRearArchitecture(transform.name))
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
            return;

        var candidateSet = new HashSet<Transform>(candidates);
        Transform[] roots = candidates
            .Where(transform => !HasAncestorInSet(transform.parent, candidateSet, environment.transform))
            .ToArray();
        Bounds bounds = CalculateBounds(candidates.Select(item => item.GetComponent<Renderer>()).Where(item => item != null));

        GameObject group = new GameObject("DistantTempleGroup");
        group.transform.SetParent(environment.transform, false);
        group.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        foreach (Transform root in roots)
            root.SetParent(group.transform, true);

        group.transform.localScale = Vector3.one * Mathf.Clamp(scale, 0.5f, 1f);
        group.transform.position -= environment.transform.forward * moveBack;
    }

    private static bool IsRearArchitecture(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        return ContainsAny(
            name,
            "gate",
            "hall",
            "temple",
            "roof",
            "column",
            "bracket",
            "wall",
            "door",
            "window",
            "lattice",
            "beam",
            "eave",
            "fascia",
            "ridge",
            "tile",
            "plinth",
            "foundation",
            "step"
        );
    }

    private static bool HasAncestorInSet(Transform current, HashSet<Transform> set, Transform stopAt)
    {
        while (current != null && current != stopAt)
        {
            if (set.Contains(current))
                return true;
            current = current.parent;
        }
        return false;
    }

    private static void FrameScene(Camera camera, Bounds puppetBounds, Bounds environmentBounds)
    {
        Vector3 puppetTarget = puppetBounds.center + Vector3.up * puppetBounds.size.y * 0.04f;
        float halfHeight = Mathf.Max(puppetBounds.extents.y, 0.5f);
        float distance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.32f;
        camera.transform.position = puppetTarget + new Vector3(0f, puppetBounds.size.y * 0.02f, distance);
        camera.transform.LookAt(puppetTarget);
        camera.farClipPlane = Mathf.Max(200f, environmentBounds.size.z * 4f);
    }

    private static RenderMetrics RenderPreview(Camera camera, string path)
    {
        const int width = 1280;
        const int height = 720;
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());

            Color32[] pixels = image.GetPixels32();
            double sum = 0d;
            double sumSquared = 0d;
            int nonBlack = 0;
            int moonRegionSamples = 0;
            int moonRegionBright = 0;
            float moonRegionPeak = 0f;
            int samples = 0;
            for (int index = 0; index < pixels.Length; index += 16)
            {
                Color32 pixel = pixels[index];
                float luminance = (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
                sum += luminance;
                sumSquared += luminance * luminance;
                if (luminance > 0.025f)
                    nonBlack++;
                int x = index % width;
                int y = index / width;
                if (x >= width * 0.04f && x <= width * 0.32f && y >= height * 0.68f)
                {
                    moonRegionSamples++;
                    moonRegionPeak = Mathf.Max(moonRegionPeak, luminance);
                    if (luminance > 0.55f)
                        moonRegionBright++;
                }
                samples++;
            }

            float average = (float)(sum / Math.Max(samples, 1));
            float variance = (float)Math.Max(sumSquared / Math.Max(samples, 1) - average * average, 0d);
            return new RenderMetrics
            {
                average = average,
                deviation = Mathf.Sqrt(variance),
                nonBlackRatio = nonBlack / (float)Math.Max(samples, 1),
                moonRegionPeak = moonRegionPeak,
                moonRegionBrightRatio = moonRegionBright / (float)Math.Max(moonRegionSamples, 1)
            };
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void RemoveExistingEnvironment(Scene scene)
    {
        string[] names =
        {
            "MoonlitTempleEnvironment",
            "MoonlitMountainBackdrop",
            "MoonlitEnvironmentLights"
        };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (names.Contains(root.name))
                UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T result = root.GetComponentInChildren<T>(true);
            if (result != null)
                return result;
        }
        return null;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        return CalculateBounds(renderers);
    }

    private static Bounds CalculateBounds(IEnumerable<Renderer> rendererSource)
    {
        Renderer[] renderers = rendererSource.Where(renderer => renderer != null).ToArray();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(value.Contains);
    }

    private struct RenderMetrics
    {
        public float average;
        public float deviation;
        public float nonBlackRatio;
        public float moonRegionPeak;
        public float moonRegionBrightRatio;
    }
}
