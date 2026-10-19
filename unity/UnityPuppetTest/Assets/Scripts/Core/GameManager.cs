using UnityEngine;

public enum PuppetGameState
{
    Loading,
    Ready,
    CameraOff,
    Tracking,
    Performing,
    Paused
}

[DisallowMultipleComponent]
public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private PuppetManager puppetManager;
    [SerializeField] private ActionManager actionManager;

    public PuppetGameState State { get; private set; } = PuppetGameState.Loading;
    public PuppetManager Puppets => puppetManager;
    public ActionManager Actions => actionManager;

    private void Start()
    {
        State = PuppetGameState.Ready;
    }

    public void Configure(PuppetManager puppets, ActionManager actions)
    {
        puppetManager = puppets;
        actionManager = actions;
    }
}
