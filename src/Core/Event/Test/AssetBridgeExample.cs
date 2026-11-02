using UnityEngine;
using FRFrame;

[CreateAssetMenu(menuName = "Events/Void Game Event")]
public sealed class VoidGameEventAsset : GameEventAsset
{
}

[CreateAssetMenu(menuName = "Events/Int Game Event")]
public sealed class IntGameEventAsset : GameEventAsset<int>
{
}

public class AssetBridgeExample : MonoBehaviour
{
    [SerializeField] private VoidGameEventAsset playerDeadAsset;
    [SerializeField] private IntGameEventAsset goldChangedAsset;

    private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

    private void OnEnable()
    {
        FRFM.Event.Bridge.Bind(playerDeadAsset, () => new PlayerDeadEvent(1)).AddTo(_scope);
        FRFM.Event.Bridge.Bind<int, GoldChangedEvent>(goldChangedAsset, value => new GoldChangedEvent(value)).AddTo(_scope);

        FRFM.Event.Subscribe<PlayerDeadEvent>(OnPlayerDead, owner: this).AddTo(_scope);
        FRFM.Event.Subscribe<GoldChangedEvent>(OnGoldChanged, owner: this).AddTo(_scope);
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
        FRLog.InfoT("Event", $"Bridge dead {evt.PlayerId}");
    }

    private void OnGoldChanged(GoldChangedEvent evt)
    {
        FRLog.InfoT("Event", $"Bridge gold {evt.NewValue}");
    }
}
