using System;
using UnityEngine;

namespace PalmTheatre.DuetMapping
{
    [Serializable]public sealed class DuetMappingHand
    {
        public bool ownerHeld,handDetected;
        public int ownerId;
        public string handedness;
        public float palmX;
        public PuppetControlState controls;
    }
    [Serializable]public sealed class DuetMappingState
    {
        public int version;
        public string controlMode;
        public DuetMappingHand left,right;
    }

    [DefaultExecutionOrder(250)]
    public sealed class DuetMappingActor:MonoBehaviour
    {
        public string characterName;
        public PuppetBoneTest mapping;
        public Animator characterAnimator;
        public GameObject instrument;
        public bool showInstrument;
        public float minimumX,maximumX;
        public DuetMappingHand LatestState{get;private set;}
        public bool Frozen{get;private set;}
        Vector3 grounded;
        void Awake()
        {
            grounded=transform.localPosition;
            characterAnimator.enabled=false;characterAnimator.runtimeAnimatorController=null;
            // Keep the disabled receiver so PuppetBoneTest.Awake does not create
            // its single-character poller. The duet session is the only input.
            foreach(var receiver in GetComponents<PuppetHttpReceiver>())receiver.enabled=false;
            mapping.useAutomaticDemo=false;mapping.lockFeetToGround=true;
            SetInstrumentVisible(showInstrument);
        }
        public void Accept(DuetMappingHand state)
        {
            LatestState=state;if(Frozen)return;
            if(state!=null&&state.ownerHeld&&state.controls!=null&&state.controls.version==2&&state.controls.controlMode=="mapping")
                mapping.SetControlState(state.controls);
            else mapping.ClearExternalInput();
        }
        public void SetFrozen(bool value){Frozen=value;mapping.enabled=!value;if(!value)Accept(LatestState);}
        public void SetInstrumentVisible(bool value){showInstrument=value;if(instrument)instrument.SetActive(value);}
        void LateUpdate()
        {
            var position=transform.localPosition;
            position.x=Mathf.Clamp(position.x,minimumX,maximumX);position.y=grounded.y;
            position.z=Mathf.Clamp(position.z,grounded.z-.2f,grounded.z+.2f);transform.localPosition=position;
        }
    }
}
