using UnityEngine;
namespace PalmTheatre.MusicianGesture
{
    public sealed class MusicianGestureView : MonoBehaviour
    {
        public MusicianGestureSession session;
        public Camera stageCamera;
        float distance=4.8f,yaw=-10,pitch=6;
        Font font;GUIStyle heading,label,button,subtitle;Texture2D panel;
        public void ResetView(){distance=4.8f;yaw=-10;pitch=6;PositionCamera();}
        void Start(){ResetView();}
        void PositionCamera(){var focus=new Vector3(0,1.08f,0);transform.position=focus+Quaternion.Euler(pitch,yaw,0)*(Vector3.back*distance);transform.LookAt(focus);}
        void Update()
        {
            for(int i=0;i<5;i++)if(Input.GetKeyDown(KeyCode.Alpha1+i))session.StartGesture(i+1);
            if(Input.GetKeyDown(KeyCode.Alpha0)||Input.GetKeyDown(KeyCode.Escape))session.StopPerformance();
            if(Input.GetKeyDown(KeyCode.Space))session.SetPaused(!session.Paused);
            if(Input.GetKeyDown(KeyCode.R))ResetView();
            if(Input.mousePosition.x<Screen.width*.72f)
            {
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,-15,45);}
                distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*.18f,1.6f,7);PositionCamera();
            }
        }
        void Styles()
        {
            if(font)return;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial"},20);
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,normal={textColor=new Color(.86f,.91f,.94f)}};
            heading=new GUIStyle(label){fontSize=25,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=17,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,8,4,4)};
            subtitle=new GUIStyle(label){fontSize=26,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            panel=new Texture2D(1,1);panel.SetPixel(0,0,new Color(.03f,.065f,.09f));panel.Apply();
        }
        void OnGUI()
        {
            Styles();var old=GUI.matrix;float scale=Mathf.Max(.6f,Screen.height/900f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale,pw=Mathf.Max(340,w*.28f),x=w-pw;
            stageCamera.rect=new Rect(0,0,x/w,1);GUI.DrawTexture(new Rect(x,0,pw,h),panel);
            GUILayout.BeginArea(new Rect(x+20,25,pw-40,h-45));
            GUILayout.Label("月下琴劍 · 月弦公子",heading);GUILayout.Label("五手勢 × 琴師動作 × 台語語音",label);GUILayout.Space(14);
            GUILayout.Label(session.Message,label);
            var state=session.LatestState;
            GUILayout.Label("手勢："+(state==null||state.gesture==0?"—":state.gesture.ToString())+"　確認："+((state?.gestureHold??0)*100).ToString("0")+"%",label);
            GUILayout.Label("左右平移："+session.HorizontalOffset.ToString("+0.00;-0.00;0.00")+(session.Paused?"　已暫停":""),label);GUILayout.Space(15);
            for(int i=0;i<session.lines.Length;i++)
            {
                var line=session.lines[i];var action=session.manager.actions[i];
                GUI.enabled=!session.Busy&&!session.Paused;
                if(GUILayout.Button((i+1)+"　"+action.displayName+"　"+line.clip.length.ToString("0.0")+"s",button,GUILayout.Height(46)))session.StartGesture(i+1);
                GUI.enabled=true;
                GUILayout.Space(5);
            }
            GUILayout.Label(session.Busy?"目前動作與台詞播放中。完成後依鏡頭當下的手勢自動觸發。":"攝影機辨識到穩定手勢即自動觸發；下方按鈕僅供測試。",label);GUILayout.Space(12);
            if(GUILayout.Button("開啟琴師手勢網頁",button,GUILayout.Height(40)))Application.OpenURL("http://127.0.0.1:5000/gesture?character=musician");
            if(GUILayout.Button(session.Paused?"繼續 [Space]":"暫停動作與語音 [Space]",button,GUILayout.Height(38)))session.SetPaused(!session.Paused);
            if(GUILayout.Button("停止語音並收琴 [0]",button,GUILayout.Height(38)))session.StopPerformance();
            GUILayout.Space(14);
            GUILayout.Label("1 食指 → 行禮\n2 食指＋中指 → 慢奏\n3 拇指＋食指＋中指 → 回應\n4 拇指收掌、其餘四指 → 挑戰\n5 五指攤開 → 快奏\n\n手勢維持 0.5 秒即自動觸發。播放中保持本次動作與台詞；完成後讀取當下手勢，不累積待播。相同手勢持續維持不會重播。手掌左右移動可平移角色，0 收琴。",label);
            GUILayout.EndArea();
            if(!string.IsNullOrEmpty(session.Subtitle))
            {
                var box=new Rect(18,h-112,x-36,88);GUI.DrawTexture(box,panel);
                GUI.Label(new Rect(box.x+12,box.y+4,box.width-24,box.height-8),session.Subtitle,subtitle);
            }
            GUI.matrix=old;
        }
        void OnDestroy(){if(panel)Destroy(panel);if(font)Destroy(font);}
    }
}
