using UnityEditor;
using UnityEngine;

public static class PbrBuildingMaterialSetup
{
    private const string TextureRoot = "Assets/Environment/PBR/";

    [MenuItem("Tools/Puppet/Apply CC0 PBR Building Materials")]
    public static void ApplyPbrBuildingMaterials()
    {
        AssetDatabase.Refresh();

        ConfigureTexture(TextureRoot + "WoodenPlanks/wooden_planks_nor_dx_1k.jpg", true);
        ConfigureTexture(TextureRoot + "WoodenPlanks/wooden_planks_rough_1k.jpg", false);
        ConfigureTexture(TextureRoot + "Roof3/roof_3_nor_dx_1k.jpg", true);
        ConfigureTexture(TextureRoot + "Roof3/roof_3_rough_1k.jpg", false);
        ConfigureTexture(TextureRoot + "GraniteWall/granite_wall_nor_dx_1k.jpg", true);
        ConfigureTexture(TextureRoot + "GraniteWall/granite_wall_rough_1k.jpg", false);

        Shader shader = Shader.Find("Puppet/PBR Texture Surface");
        if (shader == null)
            throw new System.InvalidOperationException("PBR texture shader was not found.");

        ConfigureMaterial(
            "Assets/Environment/MoonlitTemple/MoonlitWood.mat",
            shader,
            "WoodenPlanks/wooden_planks_diff_1k.jpg",
            "WoodenPlanks/wooden_planks_nor_dx_1k.jpg",
            "WoodenPlanks/wooden_planks_rough_1k.jpg",
            new Color(0.55f, 0.43f, 0.31f),
            new Vector2(2.6f, 2.6f),
            0.0f,
            0.74f,
            0.75f
        );
        ConfigureMaterial(
            "Assets/Environment/MoonlitTemple/MoonlitRoof.mat",
            shader,
            "Roof3/roof_3_diff_1k.jpg",
            "Roof3/roof_3_nor_dx_1k.jpg",
            "Roof3/roof_3_rough_1k.jpg",
            new Color(0.46f, 0.58f, 0.72f),
            new Vector2(3.0f, 3.0f),
            0.0f,
            0.62f,
            0.85f
        );
        ConfigureMaterial(
            "Assets/Environment/MoonlitTemple/MoonlitStone.mat",
            shader,
            "GraniteWall/granite_wall_diff_1k.jpg",
            "GraniteWall/granite_wall_nor_dx_1k.jpg",
            "GraniteWall/granite_wall_rough_1k.jpg",
            new Color(0.42f, 0.49f, 0.58f),
            new Vector2(1.8f, 1.8f),
            0.0f,
            0.55f,
            0.9f
        );
        ConfigureMaterial(
            "Assets/Environment/MoonlitTemple/MoonlitWall.mat",
            shader,
            "GraniteWall/granite_wall_diff_1k.jpg",
            "GraniteWall/granite_wall_nor_dx_1k.jpg",
            "GraniteWall/granite_wall_rough_1k.jpg",
            new Color(0.5f, 0.54f, 0.58f),
            new Vector2(1.25f, 1.25f),
            0.0f,
            0.48f,
            0.65f
        );

        Material gold = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Environment/MoonlitTemple/MoonlitGold.mat"
        );
        if (gold != null)
        {
            gold.SetFloat("_Metallic", 0.82f);
            gold.SetFloat("_Glossiness", 0.7f);
            EditorUtility.SetDirty(gold);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Applied CC0 PBR textures to temple wood, roof, stone, and wall materials.");
    }

    private static void ConfigureTexture(string path, bool normalMap)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new System.IO.FileNotFoundException("Texture import failed.", path);

        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normalMap;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
    }

    private static void ConfigureMaterial(
        string materialPath,
        Shader shader,
        string albedoPath,
        string normalPath,
        string roughnessPath,
        Color tint,
        Vector2 tiling,
        float metallic,
        float smoothness,
        float normalStrength
    )
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
            throw new System.IO.FileNotFoundException("Material was not found.", materialPath);

        material.shader = shader;
        material.SetColor("_Color", tint);
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + albedoPath));
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + normalPath));
        material.SetTexture("_RoughnessMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + roughnessPath));
        material.SetTextureScale("_MainTex", tiling);
        material.SetTextureScale("_NormalMap", tiling);
        material.SetTextureScale("_RoughnessMap", tiling);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_BumpScale", normalStrength);
        EditorUtility.SetDirty(material);
    }
}
