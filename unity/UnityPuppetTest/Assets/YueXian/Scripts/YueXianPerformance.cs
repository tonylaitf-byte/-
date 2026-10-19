using UnityEngine;

namespace PalmTheatre.MoonlitDuet
{
    /// <summary>Quiet holding pose and a small optional plucking demonstration.</summary>
    public sealed class YueXianPerformance : MonoBehaviour
    {
        public Animator humanoidAnimator;
        public Transform chest, head, rightHand, rightIndex;
        public bool playMusic;
        public bool gentleBreathing = true;
        public float pluckAmplitude = 4.5f;
        Quaternion chestPose, headPose, handPose, fingerPose;
        bool captured;

        void Awake() { CapturePose(); }
        public void CapturePose()
        {
            if (chest) chestPose = chest.localRotation;
            if (head) headPose = head.localRotation;
            if (rightHand) handPose = rightHand.localRotation;
            if (rightIndex) fingerPose = rightIndex.localRotation;
            captured = true;
        }
        public void SetPlaying(bool playing) { playMusic = playing; }
        void LateUpdate()
        {
            // Assigning a retargeted Animator Controller gives it full control.
            if (humanoidAnimator && humanoidAnimator.runtimeAnimatorController != null) return;
            if (!captured) CapturePose();
            float breathe = gentleBreathing ? Mathf.Sin(Time.time * 1.1f) : 0;
            if (chest) chest.localRotation = chestPose * Quaternion.Euler(0.35f * breathe, 0, 0);
            if (head) head.localRotation = headPose * Quaternion.Euler(0, 0.6f * breathe, 0);
            float pluck = playMusic ? Mathf.Sin(Time.time * 8.5f) : 0;
            if (rightHand) rightHand.localRotation = handPose * Quaternion.Euler(0, 0, pluck * pluckAmplitude);
            if (rightIndex) rightIndex.localRotation = fingerPose * Quaternion.Euler(pluck * 7f, 0, 0);
        }
        void OnDisable()
        {
            if (!captured) return;
            if (chest) chest.localRotation = chestPose;
            if (head) head.localRotation = headPose;
            if (rightHand) rightHand.localRotation = handPose;
            if (rightIndex) rightIndex.localRotation = fingerPose;
        }
    }
}
