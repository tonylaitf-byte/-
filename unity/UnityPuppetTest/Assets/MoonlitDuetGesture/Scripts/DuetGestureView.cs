using UnityEngine;
namespace PalmTheatre.DuetGestures
{
    public sealed class DuetGestureView : MonoBehaviour
    {
        public DuetGestureSession session;
        GUIStyle text,title,button,subtitle;
        Font font;
        void Styles()
        {
            if(font)return;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial"},20);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,normal={textColor=new Color(.86f,.94f,.96f)}};
            title=new GUIStyle(text){fontSize=27,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=16};
            subtitle=new GUIStyle(text){fontSize=27,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Space))session.SetPaused(!session.Paused);
            if(Input.GetKeyDown(KeyCode.Escape)||Input.GetKeyDown(KeyCode.Alpha0))session.StopAll();
        }
        void OnGUI()
        {
            Styles();var old=GUI.matrix;float scale=Mathf.Max(.65f,Screen.height/900f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;
            GUI.Box(new Rect(12,12,w-24,94),GUIContent.none);
            GUI.Label(new Rect(20,18,w-40,36),"月下琴劍 · 雙人手勢小劇場",title);
            GUI.Label(new Rect(28,57,w-56,40),session.Message+"　｜　中央 45%–55% 緩衝區 · 台詞輪流 · 動作不中斷",text);
            DrawActor(session.leftActor,new Rect(20,118,275,280),"攝影機左側 · 任一隻手");
            DrawActor(session.rightActor,new Rect(w-295,118,275,280),"攝影機右側 · 任一隻手");
            if(GUI.Button(new Rect(w/2-235,h-63,200,40),"開啟雙人手勢網頁",button))Application.OpenURL("http://127.0.0.1:5000/duet");
            if(GUI.Button(new Rect(w/2-25,h-63,130,40),session.Paused?"繼續 [Space]":"暫停 [Space]",button))session.SetPaused(!session.Paused);
            if(GUI.Button(new Rect(w/2+115,h-63,120,40),"收勢 [0]",button))session.StopAll();
            if(session.Speaker!=null)
            {
                var box=new Rect(w*.16f,h-166,w*.68f,88);GUI.Box(box,GUIContent.none);
                GUI.Label(new Rect(box.x+15,box.y+5,box.width-30,box.height-10),session.Speaker.characterName+"："+session.Subtitle,subtitle);
            }
            GUI.matrix=old;
        }
        void DrawActor(DuetGestureActor actor,Rect rect,string hint)
        {
            GUI.Box(rect,GUIContent.none);GUILayout.BeginArea(new Rect(rect.x+12,rect.y+8,rect.width-24,rect.height-16));
            GUILayout.Label(actor.characterName,text);GUILayout.Label(hint,text);
            var state=actor.InputState;
            GUILayout.Label("手勢 "+(state?.gesture??0)+"　確認 "+((state?.gestureHold??0)*100).ToString("0")+"%",text);
            GUILayout.Label(actor.Busy?(actor.VoiceWaiting?"動作進行中 · 台詞輪流播放":"演出中 · "+actor.CurrentLine.displayName):"等待穩定手勢",text);
            for(int i=0;i<5;i++)
            {
                GUI.enabled=!actor.Busy&&!session.Paused;
                if(GUILayout.Button((i+1)+"　"+actor.lines[i].displayName,button,GUILayout.Height(29)))session.StartGesture(actor,i+1);
            }
            GUI.enabled=true;GUILayout.EndArea();
        }
        void OnDestroy(){if(font)Destroy(font);}
    }
}
