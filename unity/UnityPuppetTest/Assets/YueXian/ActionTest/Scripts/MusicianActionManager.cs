using System;
using UnityEngine;

namespace PalmTheatre.MusicianActions
{
    public sealed class MusicianActionManager : MonoBehaviour
    {
        public MusicianPuppetController puppet;
        public MusicianActionDefinition[] actions;
        public bool automaticUpdate = true;
        public bool Paused { get; set; }
        public string ActionId { get; private set; } = "MUSIC_IDLE";
        public string Phase { get; private set; } = "Idle";
        public float Elapsed { get; private set; }
        public float Duration { get; private set; }
        public float TotalElapsed { get; private set; }
        public bool IsLooping => Phase == "Loop";
        public bool IsIdle => Phase == "Idle";
        public bool Showcase { get; private set; }
        MusicianActionDefinition active;
        int showcaseIndex;
        float showcaseTime;

        void Start() { puppet.Initialize(); }
        void Update() { if (automaticUpdate) Advance(Time.deltaTime); }

        public bool ExecuteAction(string id, float previewVoiceSeconds = -1)
        {
            var action = Array.Find(actions, a => a && a.actionId == id);
            if (!action) return false;
            active = action; ActionId = id; Phase = "Main";
            Elapsed = TotalElapsed = 0; Duration = action.ResolveDuration(previewVoiceSeconds);
            puppet.Switch(action.stateName);
            return true;
        }
        public void StopAction()
        {
            Showcase = false; Paused = false;
            if (IsIdle || Phase == "Exit") return;
            Phase = "Exit"; Elapsed = 0; Duration = 1;
            puppet.Switch("FastExit", .18f);
        }
        public void StartShowcase()
        {
            Paused = false; Showcase = true; showcaseIndex = 0; showcaseTime = 0;
            ExecuteAction(actions[0].actionId);
        }
        public void CancelShowcase() { Showcase = false; }

        public void Advance(float seconds)
        {
            if (Paused || seconds <= 0) return;
            // Bounded substeps keep transitions deterministic at low frame rates.
            while (seconds > .000001f)
            {
                float dt = Mathf.Min(seconds, 1f / 60f); seconds -= dt;
                Tick(dt);
                if (!Showcase) continue;
                showcaseTime += dt;
                float limit = actions[showcaseIndex].fallbackDuration + (actions[showcaseIndex].continuous ? 4f : .7f);
                if (showcaseTime < limit) continue;
                showcaseTime = 0;
                if (++showcaseIndex >= actions.Length) { StopAction(); continue; }
                ExecuteAction(actions[showcaseIndex].actionId);
            }
        }
        void Tick(float dt)
        {
            float before = Elapsed;
            Elapsed += dt; TotalElapsed += dt;
            if (Phase == "Main")
                puppet.Evaluate(active.ClipTime(Mathf.Min(Elapsed, Duration), Duration) - active.ClipTime(before, Duration));
            else puppet.Evaluate(dt);
            if (IsIdle) return;
            if (IsLooping) { if (Elapsed >= Duration) Elapsed %= Duration; return; }
            if (Elapsed + .00001f < Duration) return;
            if (Phase == "Exit") { GoIdle(); return; }
            if (ActionId == "MUSIC_CHALLENGE") { ExecuteAction("MUSIC_PLAY_FAST"); return; }
            if (active.continuous)
            {
                Phase = "Loop"; Elapsed = 0; Duration = active.loopClip.length;
                puppet.Switch(ActionId == "MUSIC_PLAY_SLOW" ? "SlowLoop" : "FastLoop", .1f);
            }
            else GoIdle();
        }
        void GoIdle()
        {
            ActionId = "MUSIC_IDLE"; Phase = "Idle"; Elapsed = 0; Duration = 0;
            puppet.Switch("MusicIdle", .2f);
        }
    }
}
