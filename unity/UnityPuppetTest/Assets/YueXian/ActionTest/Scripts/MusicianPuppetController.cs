using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PalmTheatre.MusicianActions
{
    // The dedicated controller preserves the wrist helper bones and precise prop pose.
    // The original model's Humanoid avatar and the warrior controller remain untouched.
    public sealed class MusicianPuppetController : MonoBehaviour
    {
        public Animator animator;
        public RuntimeAnimatorController controller;
        PlayableGraph graph;
        AnimatorControllerPlayable playable;
        public bool Ready => graph.IsValid();
        public string StateName { get; private set; } = "MusicIdle";

        public void Initialize()
        {
            if (Ready) return;
            animator.runtimeAnimatorController = null;
            graph = PlayableGraph.Create("YueXian motion test");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimatorControllerPlayable.Create(graph, controller);
            AnimationPlayableOutput.Create(graph, "Musician", animator).SetSourcePlayable(playable);
            graph.Play();
            playable.Play("MusicIdle", 0, 0);
            graph.Evaluate(0);
        }

        public void Switch(string state, float blend = .16f)
        {
            Initialize();
            StateName = state;
            playable.CrossFadeInFixedTime(state, blend, 0, 0);
        }
        public void Evaluate(float delta) { Initialize(); graph.Evaluate(Mathf.Max(0, delta)); }
        public AnimatorStateInfo CurrentState => playable.GetCurrentAnimatorStateInfo(0);
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
