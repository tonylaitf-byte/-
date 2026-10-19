using UnityEngine;

[DisallowMultipleComponent]
public sealed class ActionDebugPanel : MonoBehaviour
{
    [SerializeField] private ActionManager actionManager;

    public void Configure(ActionManager manager)
    {
        actionManager = manager;
    }

    private void OnGUI()
    {
        if (actionManager == null)
            return;

        GUILayout.BeginArea(new Rect(18f, 18f, 230f, 250f), GUI.skin.box);
        GUILayout.Label("Puppet Action Test");
        GUILayout.Label("Last: " + actionManager.LastAction);

        if (GUILayout.Button("DRINK"))
            actionManager.TestDrink();
        if (GUILayout.Button("ATTACK"))
            actionManager.TestAttack();
        if (GUILayout.Button("BOW / 鞠躬"))
            actionManager.TestBow();
        if (GUILayout.Button("LANDSCAPE / 賞月"))
            actionManager.TestLandscape();
        if (GUILayout.Button("ALERT / 警惕"))
            actionManager.TestAlert();

        GUILayout.EndArea();
    }
}
