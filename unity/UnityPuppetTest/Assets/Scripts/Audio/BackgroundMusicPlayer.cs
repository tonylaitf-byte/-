using UnityEngine;

[DisallowMultipleComponent]
public sealed class BackgroundMusicPlayer : MonoBehaviour
{
    private const string MusicResourcePath =
        "Audio/Music/Chinese Music Preview - Marnix Wyns (youtube)";
    private const float NormalVolume = 0.16f;
    private const float VoiceVolume = 0.07f;
    private const float VolumeChangeSpeed = 0.35f;

    public static BackgroundMusicPlayer Instance { get; private set; }

    private AudioSource musicSource;
    private float targetVolume = NormalVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        GameObject musicObject = new GameObject("BackgroundMusicAudio");
        musicObject.transform.SetParent(transform, false);
        musicSource = musicObject.AddComponent<AudioSource>();
        musicSource.clip = Resources.Load<AudioClip>(MusicResourcePath);
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.priority = 192;
        musicSource.volume = NormalVolume;

        if (musicSource.clip == null)
        {
            Debug.LogWarning(
                $"Could not load background music: Resources/{MusicResourcePath}",
                this
            );
            return;
        }

        musicSource.Play();
    }

    public void SetVoiceActive(bool active)
    {
        targetVolume = active ? VoiceVolume : NormalVolume;
    }

    private void Update()
    {
        if (musicSource == null)
            return;

        musicSource.volume = Mathf.MoveTowards(
            musicSource.volume,
            targetVolume,
            VolumeChangeSpeed * Time.unscaledDeltaTime
        );
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
