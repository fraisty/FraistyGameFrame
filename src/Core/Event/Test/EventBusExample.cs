using UnityEngine;
using FRFrame;

public struct PlayerDeadEvent
{
    public int PlayerId;

    public PlayerDeadEvent(int playerId)
    {
        PlayerId = playerId;
    }
}

public struct GoldChangedEvent
{
    public int NewValue;

    public GoldChangedEvent(int newValue)
    {
        NewValue = newValue;
    }
}

public class EventBusExample : MonoBehaviour
{
    private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

    private void OnEnable()
    {
        FRFM.Event.Subscribe<PlayerDeadEvent>(OnPlayerDead, owner: this).AddTo(_scope);
        FRFM.Event.Subscribe<GoldChangedEvent>(OnGoldChanged, owner: this, priority: 10).AddTo(_scope);
    }

    private void Start()
    {
        FRFM.Event.Publish(new PlayerDeadEvent(7));
        FRFM.Event.Post(new GoldChangedEvent(100));
    }

    private void OnDisable()
    {
        _scope.Clear();
    }

    private void OnDestroy()
    {
        _scope.Dispose();
    }

    private void OnPlayerDead(PlayerDeadEvent evt)
    {
        FRLog.InfoT("Event", $"Player dead: {evt.PlayerId}");
    }

    private void OnGoldChanged(GoldChangedEvent evt)
    {
        FRLog.InfoT("Event", $"Gold changed: {evt.NewValue}");
    }
}
