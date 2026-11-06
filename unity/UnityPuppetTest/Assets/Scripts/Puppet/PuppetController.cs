using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PuppetController : MonoBehaviour
{
    private const float IdleBlendDuration = 0.35f;
    private const float LiveControlBlendDuration = 0.35f;

    [SerializeField] private Animator animator;
    [SerializeField] private PuppetBoneTest mappingController;

    public bool IsPerforming { get; private set; }
    public string CurrentAction { get; private set; } = "IDLE";

    private Coroutine actionRoutine;

    private void Awake()
    {
        animator ??= GetComponent<Animator>();
        mappingController ??= GetComponent<PuppetBoneTest>();
    }

    public void Configure(Animator targetAnimator, PuppetBoneTest targetMappingController)
    {
        animator = targetAnimator;
        mappingController = targetMappingController;
    }

    public bool PlayAction(string actionId, string animatorTrigger, float duration)
    {
        if (animator == null || string.IsNullOrWhiteSpace(animatorTrigger))
            return false;

        if (actionRoutine != null)
            StopCoroutine(actionRoutine);

        actionRoutine = StartCoroutine(PlayActionRoutine(
            actionId,
            animatorTrigger,
            Mathf.Max(0.05f, duration)
        ));
        return true;
    }

    private IEnumerator PlayActionRoutine(string actionId, string animatorTrigger, float duration)
    {
        IsPerforming = true;
        CurrentAction = actionId;

        if (mappingController != null)
            mappingController.SuspendForAnimation();

        animator.ResetTrigger("Drink");
        animator.ResetTrigger("Attack");
        animator.ResetTrigger("Bow");
        animator.ResetTrigger("Landscape");
        animator.ResetTrigger("Alert");
        // Start near the action's target pose and let Mecanim blend from the
        // currently evaluated pose. This also makes mid-action switching smooth.
        float startOffset = animatorTrigger switch
        {
            "Attack" => 0f,
            "Landscape" => 0.03f,
            "Alert" => 0.04f,
            _ => 0.22f
        };
        // Compensate for the skipped lead-in so the visible action still lasts
        // for its full configured duration (Drink remains a full two seconds).
        animator.speed = Mathf.Max(0.05f, 1f - startOffset);
        float offsetSeconds = 0f;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            if (clip.name == animatorTrigger) { offsetSeconds = clip.length * startOffset; break; }
        // A slower breathing loop must not lengthen the transition into an
        // action. Keep the original normalized lead-in, with a fixed blend time.
        animator.CrossFadeInFixedTime(animatorTrigger, 0.24f, 0, offsetSeconds);

        yield return new WaitForSeconds(duration);

        animator.speed = 1f;
        animator.CrossFadeInFixedTime("Idle", IdleBlendDuration, 0, 0f);
        yield return new WaitForSeconds(IdleBlendDuration);

        if (mappingController != null)
            mappingController.ResumeFromAnimation(LiveControlBlendDuration);

        CurrentAction = "IDLE";
        IsPerforming = false;
        actionRoutine = null;
    }
}
