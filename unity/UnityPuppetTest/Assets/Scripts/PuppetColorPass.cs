using UnityEngine;

/// <summary>
/// Gives the untextured FBX puppet a traditional coloured costume at runtime.
/// The largest mesh receives the costume; the smaller mesh is treated as the sword.
/// </summary>
public sealed class PuppetColorPass : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForCurrentScene()
    {
        if (FindAnyObjectByType<PuppetColorPass>() == null)
            new GameObject("Puppet Costume Color Pass").AddComponent<PuppetColorPass>();
    }

    private void Start()
    {
        PuppetBoneTest puppet = FindAnyObjectByType<PuppetBoneTest>();
        if (puppet == null)
            return;

        Renderer[] renderers = puppet.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        // The UV-coloured FBX has deliberately separated robe, embroidery,
        // face and hair submeshes.  Leave those authored materials intact;
        // this fallback is only for the original one-slot source model.
        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 1)
                return;
        }

        Renderer body = FindLargestRenderer(renderers);
        Shader costumeShader = Shader.Find("Puppet/Opera Costume PBR");
        Shader standardShader = Shader.Find("Standard");
        if (body == null || costumeShader == null || standardShader == null)
        {
            Debug.LogWarning("Puppet colour pass could not find its required mesh or shaders.");
            return;
        }

        Material costume = CreateCostumeMaterial(costumeShader, body);
        Material sword = CreateSwordMaterial(standardShader);

        foreach (Renderer renderer in renderers)
            renderer.sharedMaterial = renderer == body ? costume : sword;
    }

    private static Renderer FindLargestRenderer(Renderer[] renderers)
    {
        Renderer result = null;
        float largest = -1f;
        foreach (Renderer renderer in renderers)
        {
            float size = renderer.bounds.size.sqrMagnitude;
            if (size > largest)
            {
                largest = size;
                result = renderer;
            }
        }
        return result;
    }

    private static Material CreateCostumeMaterial(Shader shader, Renderer renderer)
    {
        Bounds bounds = GetLocalBounds(renderer);
        Material material = new Material(shader) { name = "Moonlit Opera Costume" };
        material.SetColor("_RobeColor", new Color(0.025f, 0.085f, 0.24f));
        material.SetColor("_TrimColor", new Color(0.045f, 0.13f, 0.29f));
        material.SetColor("_GoldColor", new Color(0.80f, 0.48f, 0.10f));
        material.SetColor("_SkinColor", new Color(0.68f, 0.38f, 0.25f));
        material.SetFloat("_MinY", bounds.min.y);
        material.SetFloat("_MaxY", bounds.max.y);
        return material;
    }

    private static Material CreateSwordMaterial(Shader shader)
    {
        Material material = new Material(shader) { name = "Moonlit Steel Sword" };
        material.SetColor("_Color", new Color(0.34f, 0.46f, 0.58f));
        material.SetFloat("_Metallic", 0.92f);
        material.SetFloat("_Glossiness", 0.7f);
        return material;
    }

    private static Bounds GetLocalBounds(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            return skinned.sharedMesh.bounds;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);
    }
}
