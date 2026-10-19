using UnityEngine;

namespace PalmTheatre.MusicianActions
{
    public sealed class MusicianActionTestView : MonoBehaviour
    {
        public MusicianActionManager manager;
        public Camera stageCamera;
        public Vector3 focus = new Vector3(0, 1.08f, 0);
        public float distance = 4.8f, yaw = -10, pitch = 6;
        public Font chineseFont;
        float simulatedVoice = 3.6f;
        bool useSimulatedVoice;
        int selected;
        GUIStyle heading, label, button, small;
        Texture2D panel;

        public void ResetView() { focus = new Vector3(0, 1.08f, 0); distance = 4.8f; yaw = -10; pitch = 6; PositionCamera(); }
        void Start() { PositionCamera(); }
        void PositionCamera()
        {
            transform.position = focus + Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * distance);
            transform.LookAt(focus);
        }
        void Update()
        {
            for (int i = 0; i < 5; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Trigger(i);
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Escape)) manager.StopAction();
            if (Input.GetKeyDown(KeyCode.Space)) manager.Paused = !manager.Paused;
            if (Input.GetKeyDown(KeyCode.R)) ResetView();
            if (Input.GetKeyDown(KeyCode.F)) { focus = new Vector3(0, 1.36f, 0); distance = 2.15f; PositionCamera(); }
            if (Input.mousePosition.x > Screen.width * .72f) return;
            if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * 3; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, -15, 45); }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .18f, 1.45f, 7);
            PositionCamera();
        }
        void Trigger(int index)
        {
            selected = index; manager.CancelShowcase(); manager.Paused = false;
            manager.ExecuteAction(manager.actions[index].actionId, useSimulatedVoice ? simulatedVoice : -1);
        }
        void Styles()
        {
            if (heading != null) return;
            if (!chineseFont) chineseFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei", "Microsoft YaHei", "Arial" }, 18);
            heading = new GUIStyle(GUI.skin.label) { font = chineseFont, fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.80f,.91f,.94f) } };
            label = new GUIStyle(GUI.skin.label) { font = chineseFont, fontSize = 16, wordWrap = true, normal = { textColor = new Color(.82f,.86f,.87f) } };
            small = new GUIStyle(label) { fontSize = 13 };
            button = new GUIStyle(GUI.skin.button) { font = chineseFont, fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(15,10,5,5) };
            panel = new Texture2D(1,1); panel.SetPixel(0,0,new Color(.035f,.065f,.092f)); panel.Apply();
        }
        void OnGUI()
        {
            Styles();
            Font previousFont = GUI.skin.font;
            GUI.skin.font = chineseFont;
            float scale = Mathf.Max(.6f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            float panelWidth = Mathf.Max(300, width * .28f), x = width - panelWidth;
            stageCamera.rect = new Rect(0, 0, x / width, 1);
            GUI.DrawTexture(new Rect(x,0,panelWidth,height), panel);
            GUILayout.BeginArea(new Rect(x+22,28,panelWidth-44,height-45));
            GUILayout.Label("月下琴劍 · 月弦公子", heading);
            GUILayout.Label("琴師單人動作測試  /  純動作", label);
            GUILayout.Space(20);
            for (int i = 0; i < manager.actions.Length; i++)
            {
                var a = manager.actions[i];
                GUI.backgroundColor = manager.ActionId == a.actionId ? new Color(.35f,.70f,.76f) : new Color(.65f,.72f,.78f);
                if (GUILayout.Button((i+1)+"  "+a.displayName+"   "+a.fallbackDuration.ToString("0.0")+"s", button, GUILayout.Height(52))) Trigger(i);
                GUILayout.Space(5);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.Label(manager.IsIdle ? "持琴待機" : manager.ActionId, label);
            string phase = manager.Phase == "Main" ? "主動作" : manager.Phase == "Loop" ? "持續循環" : manager.Phase == "Exit" ? "收琴" : "待機";
            GUILayout.Label((manager.Paused ? "已暫停 · " : "")+phase+"   "+manager.Elapsed.ToString("0.0")+" / "+(manager.Duration>0?manager.Duration.ToString("0.0"):"—")+" 秒", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(manager.Paused ? "繼續 [Space]" : "暫停 [Space]", GUILayout.Height(34))) manager.Paused = !manager.Paused;
            if (GUILayout.Button("收琴 [0]", GUILayout.Height(34))) manager.StopAction();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重播選取動作", GUILayout.Height(32))) Trigger(selected);
            if (GUILayout.Button("五動作連播", GUILayout.Height(32))) manager.StartShowcase();
            GUILayout.EndHorizontal();
            GUILayout.Space(16);
            useSimulatedVoice = GUILayout.Toggle(useSimulatedVoice, "模擬未來台詞時長（無聲）");
            simulatedVoice = GUILayout.HorizontalSlider(simulatedVoice, 1.5f, 9);
            GUILayout.Label(simulatedVoice.ToString("0.0")+" 秒 · 下次按動作時套用", small);
            GUILayout.Label("僅調整行禮、回應、挑戰的停留時間。\n慢奏與急奏保留速度並持續循環。", small);
            GUILayout.Space(15);
            GUILayout.Label("1–5：切換動作   0：收琴\n右鍵拖曳：環視   滾輪：縮放\nF：手部近看   R：全身視角\n挑戰完成後會直接進入急奏。", small);
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
            GUI.skin.font = previousFont;
        }
        void OnDestroy() { if (panel) Destroy(panel); }
    }
}
