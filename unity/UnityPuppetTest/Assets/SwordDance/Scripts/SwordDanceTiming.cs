using UnityEngine;

public static class SwordDanceTiming
{
    public const float GuardTime=.36f;

    // Extend the preparation pose when dialogue takes longer than the clip.
    // The two cuts and recovery then retain their authored speed and order.
    public static float SampleTime(float elapsed,float clipLength,float duration)
    {
        float extra=Mathf.Max(0,duration-clipLength);
        float time=elapsed<=GuardTime?elapsed:elapsed<=GuardTime+extra?GuardTime:elapsed-extra;
        return Mathf.Clamp(time,0,clipLength);
    }
}
