using UnityEngine;
namespace PalmTheatre.DuetMapping
{
    public sealed class DuetMappingView:MonoBehaviour
    {
        public DuetMappingSession session;
        Font font;GUIStyle label,title,button;
        void Styles()
        {
            if(font)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial"},18);
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,normal={textColor=new Color(.86f,.94f,.96f)}};
            title=new GUIStyle(label){fontSize=26,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=16};
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Space))session.SetFrozen(!session.Frozen);
            if(Input.GetKeyDown(KeyCode.I))session.rightMusician.SetInstrumentVisible(!session.rightMusician.showInstrument);
        }
        void OnGUI()
        {
            Styles();var old=GUI.matrix;float scale=Mathf.Max(.65f,Screen.height/900f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;
            GUI.Box(new Rect(12,12,w-24,90),GUIContent.none);GUI.Label(new Rect(20,18,w-40,36),"月下琴劍 · 雙人手部映射",title);
            GUI.Label(new Rect(28,57,w-56,40),session.Message+"　｜　任一隻手 · 獨立校正 · 中央不越界",label);
            DrawActor(session.leftWarrior,new Rect(20,115,270,230),"left");DrawActor(session.rightMusician,new Rect(w-290,115,270,230),"right");
            GUI.Box(new Rect(w*.22f,h-174,w*.56f,90),GUIContent.none);
            GUI.Label(new Rect(w*.22f+15,h-165,w*.56f-30,75),"拇指：左臂　小指：右臂　中指：頭部\n手掌：平移、轉身與傾斜。各自保持自然手勢完成校正。"+(session.Frozen?"\n姿勢已暫停":""),label);
            if(GUI.Button(new Rect(w/2-290,h-66,190,42),"開啟雙人映射網頁",button))Application.OpenURL("http://127.0.0.1:5000/duet-mapping");
            if(GUI.Button(new Rect(w/2-90,h-66,175,42),session.rightMusician.showInstrument?"隱藏月琴 [I]":"顯示月琴 [I]",button))session.rightMusician.SetInstrumentVisible(!session.rightMusician.showInstrument);
            if(GUI.Button(new Rect(w/2+95,h-66,195,42),session.Frozen?"恢復映射 [Space]":"暫停姿勢 [Space]",button))session.SetFrozen(!session.Frozen);
            GUI.matrix=old;
        }
        void DrawActor(DuetMappingActor actor,Rect rect,string side)
        {
            GUI.Box(rect,GUIContent.none);GUILayout.BeginArea(new Rect(rect.x+12,rect.y+8,rect.width-24,rect.height-16));
            GUILayout.Label(actor.characterName,label);var state=actor.LatestState;var controls=state?.controls;
            GUILayout.Label(state?.handDetected==true?"控制中 · "+(state.handedness=="Left"?"左手":"右手"):state?.ownerHeld==true?"暫失追蹤 · 保留姿勢":"等待區域內的手",label);
            GUILayout.Label(controls?.calibrated==true?"校正完成": "自然姿勢校正 "+((controls?.calibrationProgress??0)*100).ToString("0")+"%",label);
            GUILayout.Label("平移 "+(controls?.bodyX??0).ToString("0.00"),label);
            if(GUILayout.Button("重新校正此角色",button,GUILayout.Height(34)))session.Calibrate(side);
            if(!string.IsNullOrEmpty(session.CalibrationMessage))GUILayout.Label(session.CalibrationMessage,label);
            GUILayout.EndArea();
        }
        void OnDestroy(){if(font)Destroy(font);}
    }
}
