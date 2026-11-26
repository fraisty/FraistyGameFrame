using System;
using LiteEntitySystem;

namespace FRFrame.Network.Replication
{
    internal enum MovementEntity : byte { Pawn, Controller, DemoPulse }

    // Both builds register these exact constructors in the same order.
    internal sealed class PocPlayerPawn : PawnLogic
    {
#if FRFRAME_POC2_LAYOUT_NEGATIVE
        private SyncVar<short> _player;
#else
        private SyncVar<int> _player;
#endif
        [SyncVarFlags(SyncFlags.Interpolated)] private SyncVar<float> _x;
        [SyncVarFlags(SyncFlags.Interpolated)] private SyncVar<float> _y;
        private float _moveX;
        private float _moveY;
#if !UNITY_5_3_OR_NEWER
        internal Action? BeforeTestUpdate;
#endif

        public PocPlayerPawn(EntityParams parameters) : base(parameters) { }

        public static EntityTypesMap CreateTypes() => MovementTypes();

        public static EntityTypesMap CreateRpcTypes() => MovementTypes()
            .Register(MovementEntity.DemoPulse, parameters => new DemoPulse(parameters));

        private static EntityTypesMap<MovementEntity> MovementTypes() => new EntityTypesMap<MovementEntity>()
            .Register(MovementEntity.Pawn, parameters => new PocPlayerPawn(parameters))
            .Register(MovementEntity.Controller, parameters => new PocPlayerController(parameters));

        public int Player => _player.Value;
        public float X => _x.Value;
        public float Y => _y.Value;
        public float VisualX => _x.InterpolatedValue;
        public float VisualY => _y.InterpolatedValue;

        public void Spawn(int player)
        {
#if FRFRAME_POC2_LAYOUT_NEGATIVE
            _player.Value = (short)player;
#else
            _player.Value = player;
#endif
            _x.Value = SpawnX(player);
        }

        internal static float SpawnX(int player) => player <= 8 ? player * 2 : ((player - 1) % 5 + 1) * 2;

        public void SetMove(PocPlayerInput direction)
        {
            var x = Math.Clamp((int)direction.X, -1, 1);
            var y = Math.Clamp((int)direction.Y, -1, 1);
            var scale = x != 0 && y != 0 ? 0.70710678f : 1;
            _moveX = x * scale;
            _moveY = y * scale;
        }

        protected override void Update()
        {
#if !UNITY_5_3_OR_NEWER
            BeforeTestUpdate?.Invoke();
#endif
            base.Update();
            var step = 4 * EntityManager.DeltaTimeF;
            _x.Value = Math.Clamp(_x.Value + _moveX * step, -12, 12);
            _y.Value = Math.Clamp(_y.Value + _moveY * step, -8, 8);
        }
    }

    /// <summary>Sample-only native LES RPC: announce the first authoritative movement of a pawn.</summary>
    internal sealed class DemoPulse : EntityLogic
    {
        internal const string DefinitionId = "movement.rpc";
        private static RemoteCall<int> _moved;
        public int Count { get; private set; }
        public int LastPlayer { get; private set; }

        public DemoPulse(EntityParams parameters) : base(parameters) { }

        protected override void RegisterRPC(ref RPCRegistrator registrator)
        {
            base.RegisterRPC(ref registrator);
            registrator.CreateRPCAction(this, OnMoved, ref _moved, ExecuteFlags.SendToAll);
        }

        public void Announce(int player) => ExecuteRPC(_moved, player);

        private void OnMoved(int player)
        {
            LastPlayer = player;
            Count++;
        }
    }

    internal struct PocPlayerInput
    {
        public sbyte X;
        public sbyte Y;
    }

    internal sealed class PocPlayerController : HumanControllerLogic<PocPlayerInput, PocPlayerPawn>
    {
        public PocPlayerController(EntityParams parameters) : base(parameters) { }

        public void SetInput(sbyte x, sbyte y) => ModifyPendingInput() = new PocPlayerInput { X = x, Y = y };

        protected override void BeforeControlledUpdate() => ControlledEntity?.SetMove(CurrentInput);
    }
}
