using UnityEngine;

namespace PalmTheatre.MusicianActions
{
    [CreateAssetMenu(menuName = "掌中智戲/琴師動作")]
    public sealed class MusicianActionDefinition : ScriptableObject
    {
        public string actionId, displayName, stateName;
        public AnimationClip mainClip, loopClip, exitClip;
        [Tooltip("僅提供時長參考；本測試不播放語音。")]
        public AudioClip timingReference;
        [TextArea] public string futureSubtitle;
        public float fallbackDuration, holdStart, holdEnd, voiceTail = .3f;
        public bool continuous;

        public float ResolveDuration(float previewVoiceSeconds = -1)
        {
            float voice = previewVoiceSeconds >= 0 ? previewVoiceSeconds : timingReference ? timingReference.length : -1;
            if (voice < 0) return fallbackDuration;
            // Loop actions keep their authored introduction speed and continue after the line ends.
            if (continuous) return fallbackDuration;
            return Mathf.Max(fallbackDuration - (holdEnd - holdStart) + .12f, voice + voiceTail);
        }

        public float ClipTime(float elapsed, float duration)
        {
            if (continuous || holdEnd <= holdStart) return Mathf.Min(elapsed, fallbackDuration);
            float hold = Mathf.Max(.12f, duration - (fallbackDuration - (holdEnd - holdStart)));
            if (elapsed <= holdStart) return elapsed;
            if (elapsed < holdStart + hold) return holdStart + (elapsed - holdStart) / hold * (holdEnd - holdStart);
            return Mathf.Min(fallbackDuration, holdEnd + elapsed - holdStart - hold);
        }
    }
}
