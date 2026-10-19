using System;

[Serializable]
public sealed class PuppetActionDefinition
{
    public string actionId;
    public string animatorTrigger;
    public float duration;
    public float cooldown;

    public PuppetActionDefinition(
        string actionId,
        string animatorTrigger,
        float duration,
        float cooldown
    )
    {
        this.actionId = actionId;
        this.animatorTrigger = animatorTrigger;
        this.duration = duration;
        this.cooldown = cooldown;
    }
}
