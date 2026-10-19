using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime visual pass for the moonlit temple scene. It upgrades the courtyard
/// material and lighting without touching the puppet or camera-input scripts.
/// </summary>
public sealed class PuppetVisualEnhancer : MonoBehaviour
{
    private const string CourtyardName = "wet stone courtyard";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForCurrentScene()
    {
        if (GameObject.Find(CourtyardName) == null) return;
        if (FindAnyObjectByType<PuppetVisualEnhancer>() == null)
            new GameObject("Puppet Visual Enhancer").AddComponent<PuppetVisualEnhancer>();
    }

    private void Start()
    {
        ConfigureEnvironment();
        UpgradeCourtyard();
        AddLanternLightPool();
        AddReflectionProbe();
    }

    private static void ConfigureEnvironment()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.075f, 0.11f, 0.19f);
        RenderSettings.ambientEquatorColor = new Color(0.055f, 0.075f, 0.105f);
        RenderSettings.ambientGroundColor = new Color(0.018f, 0.024f, 0.035f);
        RenderSettings.reflectionIntensity = 0.75f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.035f, 0.06f, 0.11f);
        RenderSettings.fogDensity = 0.0065f;

        QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 90f);
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowCascades = 4;
        QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
    }

    private static void UpgradeCourtyard()
    {
        GameObject courtyard = GameObject.Find(CourtyardName);
        if (courtyard == null)
        {
            Debug.LogWarning("Puppet visual pass: courtyard object was not found.");
            return;
        }

        Shader shader = Shader.Find("Puppet/Procedural Wet Courtyard Stone");
        if (shader == null)
        {
            Debug.LogWarning("Puppet visual pass: wet-stone shader was not found.");
            return;
        }

        Material wetStone = new Material(shader)
        {
            name = "Runtime Wet Courtyard Stone"
        };
        wetStone.SetColor("_StoneColor", new Color(0.18f, 0.22f, 0.27f));
        wetStone.SetColor("_MortarColor", new Color(0.028f, 0.036f, 0.05f));
        wetStone.SetFloat("_Tiling", 0.72f);
        wetStone.SetFloat("_Wetness", 0.7f);
        wetStone.SetFloat("_Smoothness", 0.54f);

        foreach (Renderer renderer in courtyard.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = wetStone;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    private static void AddLanternLightPool()
    {
        List<Transform> lanterns = new List<Transform>();
        foreach (Transform transform in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            string name = transform.name.ToLowerInvariant();
            if (name.Contains("lantern") && name.EndsWith(" glow"))
                lanterns.Add(transform);
        }

        foreach (Transform lantern in lanterns)
        {
            GameObject glow = new GameObject("Warm lantern light");
            glow.transform.SetParent(lantern, false);
            glow.transform.localPosition = new Vector3(0, 0.3f, 0);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.34f, 0.12f);
            light.intensity = 1.55f;
            light.range = 5.5f;
            light.shadows = LightShadows.None;
        }
    }

    private static void AddReflectionProbe()
    {
        GameObject probeObject = new GameObject("Courtyard Reflection Probe");
        probeObject.transform.position = new Vector3(0, 3.5f, 1.5f);
        ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        probe.resolution = 256;
        probe.intensity = 0.65f;
        probe.size = new Vector3(28f, 10f, 26f);
        probe.RenderProbe();
    }
}
