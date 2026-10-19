using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using PalmTheatre.MusicianActions;

namespace PalmTheatre.DuetGestures
{
    [Serializable] public sealed class DuetGestureLine
    {
        public string actionId, displayName, taiwanese, subtitle;
        public AudioClip voice;
        public AnimationClip animation;
    }
    [Serializable] public sealed class DuetHandState
    {
        public bool handDetected, ownerHeld;
        public int ownerId, gesture, actionSequence;
        public float gestureHold, bodyX, palmX;
        public string handedness;
    }
    [Serializable] public sealed class DuetControlState
    {
        public int version;
        public string controlMode;
        public DuetHandState left, right;
    }

    public sealed class DuetGestureActor : MonoBehaviour
    {
        public string characterName;
        public bool musician;
        public Animator animator;
        public AnimationClip idleClip;
        public MusicianActionManager music;
        public DuetGestureLine[] lines;
        public float minimumX, maximumX, homeX;
        public DuetHandState InputState {get;private set;}
        public bool Busy {get;private set;}
        public bool VoiceWaiting {get;private set;}
        public bool VoiceFinished {get;private set;}
        public long VoiceOrder {get;private set;}
        public DuetGestureLine CurrentLine {get;private set;}
        public int StartedCount {get;private set;}
        public float Elapsed {get;private set;}
        public float Duration {get;private set;}
        public int ReadyGesture => InputState!=null&&InputState.handDetected&&InputState.gesture>=1&&InputState.gesture<=5&&
            InputState.gestureHold>=.999f&&InputState.gesture!=latchedGesture&&!Busy?InputState.gesture:0;
        int latchedGesture;
        float targetX, velocity, idleTime;
        Vector3 groundedPosition;
        PlayableGraph graph;
        bool initialized;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idle, action;

        void Awake(){Initialize();}
        public void Initialize()
        {
            if(initialized)return;initialized=true;
            groundedPosition=transform.localPosition;targetX=homeX;
            if(musician){music.automaticUpdate=false;music.puppet.Initialize();return;}
            animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
            graph=PlayableGraph.Create(characterName+" duet actions");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,2);
            idle=AnimationClipPlayable.Create(graph,idleClip);idle.SetSpeed(0);
            graph.Connect(idle,0,mixer,0);mixer.SetInputWeight(0,1);
            AnimationPlayableOutput.Create(graph,"Duet warrior",animator).SetSourcePlayable(mixer);
            graph.Play();graph.Evaluate(0);
        }
        public void Accept(DuetHandState state)
        {
            InputState=state;
            if(state==null||!state.handDetected||state.gesture!=latchedGesture)latchedGesture=0;
            float value=state!=null&&state.ownerHeld&&!float.IsNaN(state.bodyX)&&!float.IsInfinity(state.bodyX)?Mathf.Clamp(state.bodyX,-1,1):0;
            targetX=state!=null&&state.ownerHeld?Mathf.Lerp(minimumX,maximumX,(value+1)*.5f):homeX;
        }
        public bool StartGesture(int gesture,float voiceDelay,long order)
        {
            Initialize();
            if(Busy||gesture<1||gesture>lines.Length)return false;
            var line=lines[gesture-1];if(line==null||!line.voice)return false;
            if(musician)
            {
                if(!music.ExecuteAction(line.actionId,line.voice.length+voiceDelay))return false;
                Duration=Mathf.Max(music.Duration,line.voice.length+voiceDelay+.3f);
            }
            else
            {
                if(!line.animation)return false;
                if(action.IsValid()){graph.Disconnect(mixer,1);graph.DestroyPlayable(action);}
                action=AnimationClipPlayable.Create(graph,line.animation);action.SetSpeed(0);action.SetTime(0);
                graph.Connect(action,0,mixer,1);
                Duration=Mathf.Max(line.animation.length,line.voice.length+voiceDelay+.3f);
            }
            CurrentLine=line;Elapsed=0;Busy=true;VoiceWaiting=true;VoiceFinished=false;VoiceOrder=order;
            latchedGesture=gesture;StartedCount++;return true;
        }
        public void VoiceStarted(){VoiceWaiting=false;}
        public void VoiceEnded(){VoiceWaiting=false;VoiceFinished=true;}
        public void Advance(float dt)
        {
            if(dt<=0)return;
            Initialize();idleTime+=dt;
            if(Busy)Elapsed+=dt;
            if(musician)music.Advance(dt);
            else
            {
                idle.SetTime(idleTime%Mathf.Max(.01f,idleClip.length));
                float weight=0;
                if(action.IsValid()&&CurrentLine!=null)
                {
                    float length=CurrentLine.animation.length;
                    float holdStart=length*.25f,holdEnd=length*.7f;
                    float hold=Mathf.Max(.01f,Duration-(length-(holdEnd-holdStart)));
                    float t=Elapsed<=holdStart?Elapsed:Elapsed<holdStart+hold?
                        holdStart+(Elapsed-holdStart)*(holdEnd-holdStart)/hold:holdEnd+Elapsed-holdStart-hold;
                    action.SetTime(Mathf.Clamp(t,0,length));
                    weight=Busy?Mathf.Min(Mathf.Clamp01(Elapsed/.18f),Mathf.Clamp01((Duration-Elapsed)/.25f)):0;
                }
                mixer.SetInputWeight(0,1-weight);mixer.SetInputWeight(1,weight);graph.Evaluate(0);
            }
            if(Busy&&Elapsed>=Duration&&VoiceFinished)Busy=false;
            var position=transform.localPosition;
            position.x=Mathf.Clamp(Mathf.SmoothDamp(position.x,targetX,ref velocity,.14f,6,dt),minimumX,maximumX);
            position.y=groundedPosition.y;position.z=groundedPosition.z;transform.localPosition=position;
        }
        public void StopPerformance()
        {
            Busy=false;VoiceWaiting=false;VoiceFinished=true;CurrentLine=null;
            latchedGesture=InputState?.gesture??0;
            if(musician)music.StopAction();
        }
        void OnDestroy(){if(graph.IsValid())graph.Destroy();}
    }
}
