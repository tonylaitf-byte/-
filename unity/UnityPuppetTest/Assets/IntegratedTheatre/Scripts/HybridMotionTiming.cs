using UnityEngine;

namespace PalmTheatre.Integration
{
    public static class HybridMotionTiming
    {
        public static float Ease(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        public static float ClipTime(float elapsed, float duration, float length, float holdStart, float holdEnd)
        {
            if (length <= 0) return 0;
            float t = Mathf.Clamp(elapsed, 0, Mathf.Max(duration, length));
            float start = Mathf.Clamp(holdStart, 0, length);
            float end = Mathf.Clamp(holdEnd, start, length);
            if (duration <= length || end - start < .001f) return Mathf.Min(t, length);
            float hold = duration - length + end - start;
            float rate = (end - start) / hold;
            float radius = Mathf.Min(Mathf.Min(.16f, start * .45f), Mathf.Min((length - end) * .45f, hold * .2f));
            float exit = start + hold;
            if (radius > .001f)
            {
                if (t >= start - radius && t <= start + radius)
                    return Ramp(t - start + radius, radius * 2, start - radius, 1, rate);
                if (t >= exit - radius && t <= exit + radius)
                    return Ramp(t - exit + radius, radius * 2, end - rate * radius, rate, 1);
            }
            if (t <= start) return t;
            if (t < exit) return start + (t - start) * rate;
            return Mathf.Min(length, end + t - exit);
        }

        // Integrating a smooth velocity ramp preserves the old timing outside its window.
        static float Ramp(float elapsed, float seconds, float origin, float fromSpeed, float toSpeed)
        {
            float u = Mathf.Clamp01(elapsed / seconds);
            float integral = u * u * u - .5f * u * u * u * u;
            return origin + seconds * (fromSpeed * u + (toSpeed - fromSpeed) * integral);
        }
    }
}
