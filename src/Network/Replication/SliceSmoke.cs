using System;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Replication
{
    /// <summary>Drives the real client input and checks results from its normal receive and view paths.</summary>
    public sealed class SliceSmoke
    {
        private const int AllPlayers = 0b111;
        private readonly SliceClient _client;
        private readonly bool _create;
        private readonly int _room;
        private readonly Action<string> _log;
        private readonly int[] _messages = new int[3];
        private int _seen, _moved, _owned, _replies, _conflicts, _markers;
        private uint _message;
        private float _elapsed;
        private float _startAt = -1;
        private float _joinAt = -1;
        private int _joinAttempts;
        private bool _sentControl;
        private bool _controlReported;

        public bool Passed { get; private set; }
        public bool TimedOut => !Passed && _elapsed > 45;

        public SliceSmoke(SliceClient client, bool create, int room, Action<string> log)
        {
            _client = client;
            _create = create;
            _room = room;
            _log = log;
            client.Received += OnMessage;
        }

        public void Tick(float milliseconds, Action<PawnSample> view)
        {
            _elapsed += milliseconds / 1000;
            if (_joinAt >= 0 && _elapsed >= _joinAt)
            {
                _joinAt = -1;
                _joinAttempts++;
                _client.JoinRoom(_room);
            }

            if (!_sentControl && _client.OnlineMask == AllPlayers)
            {
                _sentControl = true;
                var text = $"poc2-hello-{_client.Player}";
                _message = _client.SendText(text);
                if (_message != 0 && _client.SendText("different-payload", _message) == 0 &&
                    _client.LastSendError == ErrorCode.RequestIdConflict)
                    _conflicts++;
                if (_create)
                    _client.SetMarker(7);
            }

            if (_startAt < 0 && _client.Ready && _client.OnlineMask == AllPlayers && _seen == AllPlayers)
                _startAt = _elapsed;

            var moving = _startAt >= 0 && _elapsed < _startAt + 2.5f;
            var x = moving ? (_client.Player == 2 ? (sbyte)0 : (sbyte)-1) : (sbyte)0;
            var y = moving ? (_client.Player == 1 ? (sbyte)0 : (sbyte)1) : (sbyte)0;
            _client.Tick(milliseconds, x, y, pawn =>
            {
                view(pawn);
                if (pawn.Player is < 1 or > 3)
                    return;
                var bit = 1 << (pawn.Player - 1);
                _seen |= bit;
                if (pawn.Owned)
                    _owned |= bit;
                if ((_moved & bit) == 0 && (Math.Abs(pawn.X - pawn.Player * 2) > 0.45f || Math.Abs(pawn.Y) > 0.45f))
                {
                    _moved |= bit;
                    _log($"POC-2 view local={_client.Player} player={pawn.Player} owner={pawn.Owned} x={pawn.X:F2} y={pawn.Y:F2}");
                }
            });

            var controlPassed = _replies == 2 && _conflicts == 1 && _markers == 1 &&
                                _messages[0] == 1 && _messages[1] == 1 && _messages[2] == 1;
            if (!_controlReported && controlPassed)
            {
                _controlReported = true;
                _log($"POC-2 control PASS player={_client.Player} session={_client.Session} room={_client.Room} " +
                     $"owner={_client.Owner} broadcasts=1/1/1 response-replay={_replies} conflicts={_conflicts} marker={_client.Marker}");
            }

            if (!Passed && controlPassed && _startAt >= 0 && _elapsed > _startAt + 2.7f &&
                _client.Diffs > 0 && _owned == 1 << (_client.Player - 1) && _moved == AllPlayers)
            {
                Passed = true;
                _log($"POC-2 PASS player={_client.Player} session={_client.Session} room={_client.Room} " +
                     $"owner={_client.Owner} moved={_moved} baseline={_client.Baselines} diffs={_client.Diffs} " +
                     $"broadcasts=1/1/1 response-replay={_replies} conflicts={_conflicts} marker={_client.Marker}");
            }
        }

        private void OnMessage(OpCode op, SliceMessage message)
        {
            if (op == OpCode.Response)
            {
                if (message.ReplyTo == OpCode.Login && message.Error == ErrorCode.None)
                {
                    if (_create)
                        _client.CreateRoom();
                    else
                        _joinAt = _elapsed;
                }
                if (message.ReplyTo == OpCode.JoinRoom && message.Error == ErrorCode.RoomUnavailable && _joinAttempts < 24)
                    _joinAt = _elapsed + 0.5f;
                if (message.ReplyTo == OpCode.RoomMessage && message.RequestId == _message)
                {
                    if (message.Error == ErrorCode.None)
                    {
                        _replies++;
                        if (_replies == 1)
                            _client.SendText($"poc2-hello-{_client.Player}", _message);
                    }
                }
            }
            else if (op == OpCode.RoomBroadcast && message.Player is >= 1 and <= 3 &&
                     message.Text == $"poc2-hello-{message.Player}")
                _messages[message.Player - 1]++;
            else if (op == OpCode.MarkerChanged && message.Player == _client.Owner && message.Marker == 7)
                _markers++;
        }
    }
}
