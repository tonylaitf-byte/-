using UnityEngine;

namespace PalmTheatre.MusicianMapping
{
    public sealed class MusicianMappingView : MonoBehaviour
    {
        public MusicianMappingSession session;
        public Camera stageCamera;
        float distance=5.7f,yaw=-4,pitch=5;
        Font font;GUIStyle title,label,small,button;Texture2D background;
        public void ResetView(){distance=5.7f;yaw=-4;pitch=5;PositionCamera();}
        void Start(){ResetView();}
        void PositionCamera()
        {
            Vector3 focus=session.transform.position+Vector3.up*1.02f;
            transform.position=focus+Quaternion.Euler(pitch,yaw,0)*(Vector3.back*distance);transform.LookAt(focus);
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.I))session.SetInstrumentVisible(!session.showInstrument);
            if(Input.GetKeyDown(KeyCode.Space))session.SetFrozen(!session.Frozen);
            if(Input.GetKeyDown(KeyCode.R))ResetView();
            if(Input.mousePosition.x<Screen.width*.72f)
            {
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,-15,45);}
                distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.2f,1.8f,8);
            }
            PositionCamera();
        }
        void CreateStyles()
        {
            if(title!=null)return;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial"},18);
            title=new GUIStyle(GUI.skin.label){font=font,fontSize=25,fontStyle=FontStyle.Bold,normal={textColor=new Color(.8f,.93f,.94f)}};
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,normal={textColor=new Color(.82f,.87f,.9f)}};
            small=new GUIStyle(label){fontSize=14};button=new GUIStyle(GUI.skin.button){font=font,fontSize=17};
            background=new Texture2D(1,1);background.SetPixel(0,0,new Color(.03f,.065f,.09f));background.Apply();
        }
        void OnGUI()
        {
            CreateStyles();Font oldFont=GUI.skin.font;GUI.skin.font=font;
            float scale=Mathf.Max(.6f,Screen.height/900f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale,height=Screen.height/scale,panel=Mathf.Max(320,width*.28f),x=width-panel;
            stageCamera.rect=new Rect(0,0,x/width,1);GUI.DrawTexture(new Rect(x,0,panel,height),background);
            GUILayout.BeginArea(new Rect(x+22,30,panel-44,height-45));
            GUILayout.Label("月弦公子 · 手部映射",title);GUILayout.Label("雙手自由控制  /  即時 IK",label);GUILayout.Space(24);
            var state=session.receiver.LatestState;
            string status=session.DemoMode?"離線示範中":!session.receiver.IsConnected?"等待手部追蹤服務":state==null?"等待資料":state.controlMode!="mapping"?"請在網頁選擇手部映射模式":!state.handDetected?"已連線，等待鏡頭中的手":!state.calibrated?"校正中… "+(state.calibrationProgress*100).ToString("0")+"%":"手部映射已連線";
            GUI.color=session.receiver.IsConnected&&!session.DemoMode?new Color(.5f,1,.75f):new Color(1,.82f,.45f);GUILayout.Label(status,label);GUI.color=Color.white;
            if(session.Frozen)GUILayout.Label("姿勢已暫停",label);
            GUILayout.Space(20);
            if(GUILayout.Button(session.showInstrument?"隱藏月琴 [I]":"顯示月琴 [I]",button,GUILayout.Height(44)))session.SetInstrumentVisible(!session.showInstrument);
            GUILayout.Label("月琴隨胸部移動，雙手仍可自由映射。",small);GUILayout.Space(15);
            if(GUILayout.Button(session.Frozen?"恢復映射 [Space]":"暫停姿勢 [Space]",button,GUILayout.Height(40)))session.SetFrozen(!session.Frozen);
            if(GUILayout.Button("開啟手部映射網頁",button,GUILayout.Height(40)))Application.OpenURL("http://127.0.0.1:5000/mapping");
            if(GUILayout.Button("重新校正",button,GUILayout.Height(40)))session.Calibrate();
            if(!string.IsNullOrEmpty(session.CalibrationMessage))GUILayout.Label(session.CalibrationMessage,small);
            GUILayout.Space(15);
            bool demo=GUILayout.Toggle(session.DemoMode,"離線示範（暫停攝影機控制）");if(demo!=session.DemoMode)session.SetDemo(demo);
            GUILayout.Space(24);
            GUILayout.Label("使用方式",label);
            GUILayout.Label("1. 在網頁啟動攝影機。\n2. 手掌保持自然姿勢完成校正。\n3. 拇指與小指控制兩側手臂，中指控制頭部；移動手掌可移動及轉動人偶。",small);
            GUILayout.Space(18);
            GUILayout.Label("右鍵拖曳：環視\n滾輪：縮放   R：重設視角\nI：月琴顯示   Space：暫停姿勢",small);
            GUILayout.Space(14);GUILayout.Label("手暫時離開鏡頭時保留姿勢；\n服務斷線時平順回到中立姿勢。",small);
            GUILayout.EndArea();GUI.matrix=Matrix4x4.identity;GUI.skin.font=oldFont;
        }
        void OnDestroy(){if(background)Destroy(background);if(font)Destroy(font);}
    }
}
