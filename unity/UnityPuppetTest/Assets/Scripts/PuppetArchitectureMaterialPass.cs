using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces the temple's flat-color source materials with detailed PBR variants
/// while keeping the imported FBX and scene hierarchy intact.
/// </summary>
public sealed class PuppetArchitectureMaterialPass : MonoBehaviour
{
    private static readonly Dictionary<string, Material> Materials =
        new Dictionary<string, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForCurrentScene()
    {
        if (FindAnyObjectByType<PuppetArchitectureMaterialPass>() == null)
            new GameObject("Puppet Architecture Material Pass").AddComponent<PuppetArchitectureMaterialPass>();
    }

    private void Start()
    {
        Shader shader = Shader.Find("Puppet/Moonlit Architecture PBR");
        if (shader == null)
        {
            Debug.LogWarning("Temple PBR material shader is unavailable.");
            return;
        }

        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            Material[] source = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                    continue;

                Material replacement = GetReplacement(shader, source[i].name);
                if (replacement == null)
                    continue;

                source[i] = replacement;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = source;
        }
    }

    private static Material GetReplacement(Shader shader, string materialName)
    {
        if (Materials.TryGetValue(materialName, out Material cached) && cached != null)
            return cached;
        Materials.Remove(materialName);

        Material material = null;
        switch (materialName)
        {
            case "MoonlitWood":
                material = Create(shader, materialName, 0, new Color(0.19f, 0.085f, 0.045f), new Color(0.035f, 0.015f, 0.009f), 1.6f, 0.3f, 0.0f, 0.0f);
                break;
            case "MoonlitRoof":
                material = Create(shader, materialName, 1, new Color(0.075f, 0.12f, 0.16f), new Color(0.015f, 0.028f, 0.045f), 1.05f, 0.38f, 0.05f, 0.0f);
                break;
            case "MoonlitWall":
            case "MoonlitStone":
                material = Create(shader, materialName, 2, new Color(0.35f, 0.34f, 0.31f), new Color(0.075f, 0.082f, 0.09f), 1.35f, 0.2f, 0.0f, 0.0f);
                break;
            case "MoonlitGold":
                material = Create(shader, materialName, 3, new Color(0.68f, 0.43f, 0.10f), new Color(0.07f, 0.19f, 0.14f), 1.4f, 0.58f, 0.88f, 0.0f);
                break;
            case "MoonlitFoliage":
                material = Create(shader, materialName, 4, new Color(0.045f, 0.16f, 0.13f), new Color(0.015f, 0.055f, 0.065f), 0.9f, 0.16f, 0.0f, 0.0f);
                break;
            // Lantern meshes use a separate glow frame and paper surfaces.
            // Keep the authored material here: the procedural detail is applied
            // in world space and creates an unwanted striped light-column on
            // these small meshes. Their warm point lights remain active.
            case "MoonlitLantern":
                return null;
        }

        if (material != null)
            Materials.Add(materialName, material);
        return material;
    }

    private static Material Create(
        Shader shader,
        string sourceName,
        float mode,
        Color baseColor,
        Color accentColor,
        float tiling,
        float smoothness,
        float metallic,
        float emission
    )
    {
        Material material = new Material(shader) { name = "PBR " + sourceName };
        material.SetFloat("_Mode", mode);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_AccentColor", accentColor);
        material.SetFloat("_Tiling", tiling);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_EmissionStrength", emission);
        return material;
    }
}
