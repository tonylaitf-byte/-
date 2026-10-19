using UnityEngine;

namespace PalmTheatre.MoonlitDuet
{
    public sealed class MoonlitStageView : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0,1,0);
        public float distance = 5, yaw = -9, pitch = 7;
        public string title = "月下琴劍";
        public string subtitle = "月弦公子 · 清弦入月";
        public bool showInterface = true;
        float currentDistance, currentYaw, currentPitch;
        Vector3 mouse;
        GUIStyle titleStyle, bodyStyle;
        Font font;
        void OnEnable() { ResetView(); }
        public void ResetView() { currentDistance=distance; currentYaw=yaw; currentPitch=pitch; Apply(); }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) ResetView();
            if (Input.GetKeyDown(KeyCode.H)) showInterface=!showInterface;
            if (Input.GetKeyDown(KeyCode.Space))
                foreach (var player in Object.FindObjectsByType<YueXianPerformance>(FindObjectsSortMode.None)) player.SetPlaying(!player.playMusic);
            if (Input.GetMouseButtonDown(0)) mouse=Input.mousePosition;
            if (Input.GetMouseButton(0))
            {
                Vector3 delta=Input.mousePosition-mouse;
                currentYaw+=delta.x*.22f;currentPitch=Mathf.Clamp(currentPitch-delta.y*.16f,-8,45);mouse=Input.mousePosition;
            }
            currentDistance=Mathf.Clamp(currentDistance*Mathf.Exp(-Input.mouseScrollDelta.y*.09f),distance*.42f,distance*1.8f);
            Apply();
        }
        void Apply() { transform.rotation=Quaternion.Euler(currentPitch,currentYaw,0);transform.position=focus+transform.rotation*(Vector3.back*currentDistance); }
        void OnGUI()
        {
            if (!showInterface) return;
            if (titleStyle==null)
            {
                font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft JhengHei","Microsoft YaHei","Arial"},30);
                titleStyle=new GUIStyle(GUI.skin.label){font=font,fontSize=30};titleStyle.normal.textColor=new Color(.79f,.89f,.89f);
                bodyStyle=new GUIStyle(GUI.skin.label){font=font,fontSize=13};bodyStyle.normal.textColor=new Color(.56f,.70f,.74f);
            }
            GUI.Label(new Rect(35,Screen.height-108,700,44),title,titleStyle);
            GUI.Label(new Rect(38,Screen.height-62,700,28),subtitle,bodyStyle);
            GUI.Label(new Rect(Screen.width-365,Screen.height-60,340,30),"拖曳環視 · 滾輪縮放 · 空白鍵撥弦 · R 重設",bodyStyle);
        }
        void OnDestroy() { if(font) Destroy(font); }
    }
}
