using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PalmTheatre.DuetMapping
{
    [DefaultExecutionOrder(-120)]
    public sealed class DuetMappingSession:MonoBehaviour
    {
        public DuetMappingActor leftWarrior,rightMusician;
        public BackgroundMusicPlayer backgroundMusic;
        public string endpoint="http://127.0.0.1:5000/duet_mapping_state";
        public bool pollServer=true;
        public bool Connected{get;private set;}
        public bool Frozen{get;private set;}
        public string Message{get;private set;}="等待雙人映射服務";
        public string CalibrationMessage{get;private set;}="";
        bool previousBackground,calibrating;
        Coroutine polling;
        AudioSource Music=>backgroundMusic?backgroundMusic.GetComponentInChildren<AudioSource>():null;
        void Awake(){previousBackground=Application.runInBackground;Application.runInBackground=true;}
        void OnEnable(){if(pollServer)polling=StartCoroutine(Poll());}
        void OnDisable(){if(polling!=null)StopCoroutine(polling);polling=null;SetFrozen(false);MarkDisconnected();}
        void OnDestroy(){Application.runInBackground=previousBackground;}
        public void AcceptState(DuetMappingState state)
        {
            Connected=state!=null&&state.version==1&&state.controlMode=="duet_mapping";
            leftWarrior.Accept(Connected?state.left:null);rightMusician.Accept(Connected?state.right:null);
            Message=Connected?"雙人映射已連線 · 左俠客／右琴師":"請開啟雙人手部映射網頁";
        }
        public void MarkDisconnected(){Connected=false;leftWarrior.Accept(null);rightMusician.Accept(null);Message="服務未連線 · 平順回到中立姿勢";}
        public void SetFrozen(bool value)
        {
            Frozen=value;leftWarrior.SetFrozen(value);rightMusician.SetFrozen(value);
            var music=Music;if(music){if(value)music.Pause();else music.UnPause();}
        }
        public void Calibrate(string side){if(!calibrating)StartCoroutine(Calibration(side));}
        IEnumerator Calibration(string side)
        {
            if(side!="left"&&side!="right"&&side!="both")yield break;
            calibrating=true;CalibrationMessage="請各自保持自然手勢，重新校正中…";
            using(var request=new UnityWebRequest("http://127.0.0.1:5000/duet_mapping/calibrate","POST"))
            {
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"side\":\""+side+"\"}"));
                request.downloadHandler=new DownloadHandlerBuffer();request.SetRequestHeader("Content-Type","application/json");request.timeout=3;
                yield return request.SendWebRequest();
                CalibrationMessage=request.result==UnityWebRequest.Result.Success?"請保持自然姿勢約 1.5 秒。":"無法連線校正服務。";
            }
            calibrating=false;
        }
        IEnumerator Poll()
        {
            while(enabled&&pollServer)
            {
                using(var request=UnityWebRequest.Get(endpoint))
                {
                    request.timeout=1;yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        DuetMappingState state=null;try{state=JsonUtility.FromJson<DuetMappingState>(request.downloadHandler.text);}catch(System.ArgumentException){}
                        if(state!=null)AcceptState(state);else MarkDisconnected();
                    }
                    else MarkDisconnected();
                }
                yield return new WaitForSecondsRealtime(Connected?.033f:.25f);
            }
        }
    }
}
