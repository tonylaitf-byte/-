using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class ActionVoicePresenter : MonoBehaviour
{
    public bool IsPlaying => audioSource != null && (audioSource.isPlaying ||
        (paused && !string.IsNullOrEmpty(currentSubtitle)));
    public string CurrentSubtitle => currentSubtitle;
    public bool showSubtitles = true;

    private sealed class VoiceLine
    {
        public readonly AudioClip Clip;
        public readonly string Subtitle;

        public VoiceLine(string resourcePath, string subtitle)
        {
            Clip = Resources.Load<AudioClip>(resourcePath);
            Subtitle = subtitle;
        }
    }

    private readonly Dictionary<string, VoiceLine[]> lines =
        new Dictionary<string, VoiceLine[]>(StringComparer.OrdinalIgnoreCase);
    private AudioSource audioSource;
    private Coroutine subtitleRoutine;
    private string currentSubtitle = string.Empty;
    private int playbackVersion;
    private bool paused;
    private GUIStyle subtitleStyle;
    private GUIStyle subtitleShadowStyle;

    private void Awake()
    {
        EnsureAudioListener();
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        audioSource.priority = 64;

        lines["DRINK"] = new[]
        {
            LoadLine("Audio/Voice/SinglePuppetDenoised/drink", "來！我們一起喝一杯！")
        };
        lines["BOW"] = new[]
        {
            LoadLine("Audio/Voice/SinglePuppetDenoised/greeting", "久仰久仰，很高興見到你！")
        };
        lines["ATTACK"] = new[]
        {
            LoadLine("Audio/Voice/SinglePuppetDenoised/sword", "劍出無情。接招！")
        };
        lines["LANDSCAPE"] = new[]
        {
            LoadLine("Audio/Voice/SinglePuppetDenoised/landscape", "明月高掛天際，今晚真是一番好景色。")
        };
        lines["ALERT"] = new[]
        {
            LoadLine("Audio/Voice/SinglePuppetDenoised/alert", "嗯？你想做什麼？")
        };
    }

    private void EnsureAudioListener()
    {
        if (FindAnyObjectByType<AudioListener>() != null)
            return;

        Camera targetCamera = Camera.main;
        GameObject listenerObject = targetCamera != null
            ? targetCamera.gameObject
            : gameObject;
        listenerObject.AddComponent<AudioListener>();
        Debug.Log($"Added missing AudioListener to {listenerObject.name}.", listenerObject);
    }

    public void PlayActionVoice(string actionId)
    {
        StopVoice();

        string normalized = (actionId ?? string.Empty).Trim().ToUpperInvariant();
        if (!lines.TryGetValue(normalized, out VoiceLine[] options) || options.Length == 0)
            return;

        int index = ChooseIndex(options.Length);
        VoiceLine line = options[index];
        if (line.Clip == null)
        {
            Debug.LogWarning($"Action voice clip is missing for {normalized} index {index}.", this);
            return;
        }

        currentSubtitle = line.Subtitle;
        audioSource.clip = line.Clip;
        BackgroundMusicPlayer.Instance?.SetVoiceActive(true);
        audioSource.Play();
        int version = playbackVersion;
        subtitleRoutine = StartCoroutine(ClearSubtitleWhenVoiceEnds(version));
    }

    public void StopVoice()
    {
        paused = false;
        playbackVersion++;
        if (subtitleRoutine != null)
        {
            StopCoroutine(subtitleRoutine);
            subtitleRoutine = null;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        BackgroundMusicPlayer.Instance?.SetVoiceActive(false);
        currentSubtitle = string.Empty;
    }

    public void SetPaused(bool value) { paused = value; }

    private VoiceLine LoadLine(string resourcePath, string subtitle)
    {
        VoiceLine line = new VoiceLine(resourcePath, subtitle);
        if (line.Clip == null)
            Debug.LogWarning($"Could not load action voice: Resources/{resourcePath}", this);
        return line;
    }

    private int ChooseIndex(int count)
    {
        if (count <= 1)
            return 0;
        return UnityEngine.Random.Range(0, count);
    }

    private IEnumerator ClearSubtitleWhenVoiceEnds(int version)
    {
        while (version == playbackVersion && audioSource != null && (paused || audioSource.isPlaying))
            yield return null;

        if (version == playbackVersion)
        {
            BackgroundMusicPlayer.Instance?.SetVoiceActive(false);
            currentSubtitle = string.Empty;
            subtitleRoutine = null;
        }
    }

    private void OnDisable()
    {
        StopVoice();
    }

    private void OnGUI()
    {
        if (!showSubtitles || string.IsNullOrEmpty(currentSubtitle))
            return;

        EnsureSubtitleStyles();
        float width = Mathf.Min(Screen.width - 40f, 1200f);
        float height = Mathf.Clamp(Screen.height * 0.09f, 64f, 104f);
        Rect boxRect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - height - Mathf.Max(22f, Screen.height * 0.025f),
            width,
            height
        );

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(boxRect, GUIContent.none);
        GUI.color = previousColor;

        Rect textRect = new Rect(boxRect.x + 18f, boxRect.y, boxRect.width - 36f, boxRect.height);
        Rect shadowRect = new Rect(textRect.x + 2f, textRect.y + 2f, textRect.width, textRect.height);
        GUI.Label(shadowRect, currentSubtitle, subtitleShadowStyle);
        GUI.Label(textRect, currentSubtitle, subtitleStyle);
    }

    private void EnsureSubtitleStyles()
    {
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.038f), 26, 48);
        if (subtitleStyle != null && subtitleStyle.fontSize == fontSize)
            return;

        Font font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft JhengHei", "Noto Sans CJK TC", "Arial Unicode MS" },
            fontSize
        );
        subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            wordWrap = true,
            font = font
        };
        subtitleShadowStyle = new GUIStyle(subtitleStyle)
        {
            normal = { textColor = Color.black }
        };
    }
}
