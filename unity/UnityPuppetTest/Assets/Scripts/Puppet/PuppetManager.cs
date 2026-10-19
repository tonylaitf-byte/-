using UnityEngine;

[DisallowMultipleComponent]
public sealed class PuppetManager : MonoBehaviour
{
    [SerializeField] private PuppetController primaryPuppet;

    public PuppetController PrimaryPuppet => primaryPuppet;
    public bool IsPerforming => primaryPuppet != null && primaryPuppet.IsPerforming;

    public void Configure(PuppetController puppet)
    {
        primaryPuppet = puppet;
    }

    public bool PlayAction(string actionId, string animatorTrigger, float duration)
    {
        return primaryPuppet != null &&
            primaryPuppet.PlayAction(actionId, animatorTrigger, duration);
    }
}
