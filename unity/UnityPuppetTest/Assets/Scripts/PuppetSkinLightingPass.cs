using UnityEngine;

/// <summary>
/// Gives the face and hands a subtle warm lift in the moonlit scene without
/// flattening the rest of the puppet's lighting.
/// </summary>
public sealed class PuppetSkinLightingPass : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForCurrentScene()
    {
        if (FindAnyObjectByType<PuppetSkinLightingPass>() == null)
            new GameObject("Puppet Skin Lighting Pass").AddComponent<PuppetSkinLightingPass>();
    }

    private void Start()
    {
        PuppetBoneTest puppet = FindAnyObjectByType<PuppetBoneTest>();
        if (puppet == null)
            return;

        foreach (Renderer renderer in puppet.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.materials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material source = materials[index];
                if (source == null || !source.name.ToLowerInvariant().Contains("skin"))
                    continue;

                Material skin = new Material(source) { name = "Moonlit Puppet Skin" };
                if (skin.HasProperty("_Color"))
                    skin.SetColor("_Color", new Color(0.98f, 0.64f, 0.47f, 1f));
                if (skin.HasProperty("_EmissionColor"))
                {
                    skin.EnableKeyword("_EMISSION");
                    skin.SetColor("_EmissionColor", new Color(0.055f, 0.018f, 0.008f, 1f));
                }
                materials[index] = skin;
                changed = true;
            }
            if (changed)
                renderer.materials = materials;
        }
    }
}
