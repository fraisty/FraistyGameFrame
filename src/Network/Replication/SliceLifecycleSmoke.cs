#nullable enable
using System;
using System.Collections.Generic;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Replication
{
    /// <summary>Drives the existing network path from three independent Player processes.</summary>
    public sealed class SliceLifecycleSmoke : IDisposable
    {
        private readonly SliceClient _initial;
        private SliceClient _client;
        private SliceClient? _retiring;
        private readonly Action<string> _log;
        private readonly int _replacementPort;
        private readonly int[] _moved = new int[3];
        private readonly int[] _own = new int[3];
        private readonly int[] _original = new int[3];
        private readonly uint[] _stream = new uint[3];
        private readonly HashSet<int> _observedOffline = new();
        private readonly bool _short;
        private float _time, _moveAt = -1, _nextJoin, _resumeAt, _rebindStarted;
        private bool _started, _secondCreated, _secondMessage, _secondMarker, _sentSecond;
        private bool _recovering, _rejoined, _markerComplete, _recoveryPublished, _replacementPublished;
        private bool _handoff, _closing, _finalSignal;
        private bool _onlineReplacementObserved;
        private int _oldSession, _oldEpoch, _oldGeneration;
        private uint _delayed;
        public bool Passed { get; private set; }
        public string Failure { get; private set; } = "";
        public bool TimedOut => !Passed && _time > 40;
        public SliceClient CurrentClient => _client;

        public SliceLifecycleSmoke(SliceClient client, Action<string> log, bool shortRun = false, int replacementPort = 0)
        {
            _initial = _client = client;
            _log = log;
            _short = shortRun;
            _replacementPort = replacementPort;
            client.Received += OnMessage;
        }

        public void Tick(float milliseconds, Action<PawnSample> view)
        {
            _time += milliseconds / 1000;
            if (_retiring != null)
            {
                _retiring.Tick(milliseconds, 0, 0, _ => { });
                if (_retiring.Replaced)
                {
                    _onlineReplacementObserved = true;
                    _log($"POC-3 online replacement observed on old connection session={_oldSession} generation={_oldGeneration}");
                    _retiring = null;
                }
            }
            if (_started && _client.Player != 1 && !_recovering && _client.Connected && _time >= _nextJoin)
            {
                _nextJoin = _time + 0.6f;
                if (!_client.Rooms.ContainsKey(1))
                    _client.JoinRoom(1);
                if (_client.Player == 2 && !_client.Rooms.ContainsKey(2) && !_closing)
                    _client.JoinRoom(2);
            }

            var move = false;
            if (_moveAt < 0 && World(1, 7) && (_client.Player == 3 || World(2, 3)))
                _moveAt = _time;
            if (_moveAt >= 0)
            {
                var age = _time - _moveAt;
                var duration = _short ? 0.75f : 1.15f;
                _client.SelectRoom(age < duration || _client.Player == 3 || age >= duration * 2 ? 1 : 2);
                move = age < duration * 2;
            }
            var x = move && _client.Player != 2 ? (sbyte)-1 : (sbyte)0;
            var y = move && _client.Player != 1 ? (sbyte)1 : (sbyte)0;
            _client.Tick(milliseconds, x, y, sample =>
            {
                view(sample);
                if (sample.Room is < 1 or > 2 || sample.Player is < 1 or > 3)
                    return;
                if (sample.Owned)
                    _own[sample.Room] = sample.Entity;
                var bit = 1 << (sample.Player - 1);
                if ((_moved[sample.Room] & bit) == 0 &&
                    (Math.Abs(sample.X - sample.Player * 2) > 0.4f || Math.Abs(sample.Y) > 0.4f))
                {
                    _moved[sample.Room] |= bit;
                    _log($"POC-3 view local={_client.Player} room={sample.Room} player={sample.Player} entity={sample.Entity} owned={sample.Owned} x={sample.X:F2} y={sample.Y:F2}");
                }
            });
            if (Passed || Failure.Length != 0)
                return;

            if (_client.Player == 1 && !_sentSecond && _moved[1] == 7 && _moved[2] == 3 && World(2, 3))
            {
                _sentSecond = true;
                _client.SendText(2, "poc3-room-two");
                _client.SetMarker(2, 4);
            }
            if (_client.Player == 1 && !_recovering && _sentSecond && _secondMarker && _secondMessage)
            {
                _recovering = true;
                RememberBinding();
                _delayed = _client.SetMarker(1, 8, 2200);
                _client.SendText(1, "poc3-accepted-before-loss");
            }
            if (_resumeAt != 0 && _time >= _resumeAt)
            {
                _resumeAt = 0;
                if (!_client.Reconnect(recover: true))
                    Failure = "Could not start the requested connection.";
            }
            if (_rejoined && World(1, 7) && World(2, 3) && _own[1] == _original[1] && _own[2] == _original[2] &&
                _client.Rooms[1].StreamId != _stream[1] && _client.Rooms[2].StreamId != _stream[2])
            {
                if (_client.Player == 1 && _markerComplete && !_recoveryPublished)
                {
                    _recoveryPublished = true;
                    _client.SendText(1, "poc3-recovered");
                    _log($"POC-3 recovery replay and rebind session={_client.Session} epoch={_client.Epoch} generation={_client.ConnectionGeneration} entities={_own[1]}/{_own[2]} elapsedMs={(_time - _rebindStarted) * 1000:F0}");
                }
                if (_client.Player == 2 && _onlineReplacementObserved && !_replacementPublished)
                {
                    _replacementPublished = true;
                    _client.SendText(1, "poc3-replaced");
                    _log($"POC-3 replacement rebind session={_client.Session} epoch={_client.Epoch} generation={_client.ConnectionGeneration} entities={_own[1]}/{_own[2]} elapsedMs={(_time - _rebindStarted) * 1000:F0}");
                }
            }
            if (_client.Player == 2 && _replacementPublished && !_closing &&
                _client.Rooms.TryGetValue(2, out var second) && second.Owner == 2 && second.Members.Length == 1)
            {
                _closing = true;
                _client.CloseRoom(2);
            }
            if (_finalSignal && World(1, 7) && !_client.Rooms.ContainsKey(2) && _moved[1] == 7 &&
                (_client.Player == 3 ? !_secondMessage && !_secondMarker && _observedOffline.Count == 1 && _observedOffline.Contains(1) :
                    _moved[2] == 3 && _secondMessage && _secondMarker && (_recoveryPublished || _replacementPublished)))
            {
                Passed = true;
                _log($"POC-3 PASS player={_client.Player} session={_client.Session} room1Moved={_moved[1]} room2Moved={_moved[2]} room1Diffs={_client.Rooms[1].Diffs} rooms={_client.Rooms.Count}");
            }
        }

        private bool World(int room, int online) => _client.Rooms.TryGetValue(room, out var state) &&
            state.State == SliceStreamState.Ready && state.Baselines > 0 && state.Diffs > 0 && state.OnlineMask == online;

        private void RememberBinding()
        {
            _oldSession = _client.Session;
            _oldEpoch = _client.Epoch;
            _oldGeneration = _client.ConnectionGeneration;
            for (var id = 1; id <= 2; id++)
            {
                _original[id] = _own[id];
                _own[id] = 0;
                _stream[id] = _client.Rooms[id].StreamId;
            }
        }

        private void OnMessage(OpCode op, SliceMessage message)
        {
            if (op == OpCode.Response && message.ReplyTo is OpCode.Login or OpCode.Recover &&
                message.Error != ErrorCode.None)
            {
                Failure = $"{message.ReplyTo} returned {message.Error}.";
                return;
            }
            if (op == OpCode.Response && message.Error == ErrorCode.None)
            {
                if (message.ReplyTo == OpCode.Login && !_started)
                {
                    _started = true;
                    if (_client.Player == 1)
                        _client.CreateRoom(SliceMessage.SharedDefinitionId);
                }
                else if (message.ReplyTo == OpCode.CreateRoom && !_secondCreated)
                {
                    _secondCreated = true;
                    _client.CreateRoom(SliceMessage.SharedDefinitionId);
                }
                else if (message.ReplyTo == OpCode.Recover || (message.ReplyTo == OpCode.Login && _recovering))
                {
                    var same = _client.Session == _oldSession && _client.Epoch == _oldEpoch;
                    if ((_client.Player == 1 ? !same : _client.Session == _oldSession || _client.Epoch <= _oldEpoch) ||
                        _client.ConnectionGeneration <= _oldGeneration)
                        Failure = "Session identity did not follow the requested lifecycle.";
                    _rejoined = true;
                    if (_client.Player == 1)
                        _client.SetMarker(1, 8, 2200, _delayed);
                }
                else if (message.ReplyTo == OpCode.SetMarker && message.RequestId == _delayed && _rejoined)
                    _markerComplete = message.Marker == 8;
                else if (message.ReplyTo == OpCode.TransferOwner && _handoff)
                    _client.LeaveRoom(2);
            }
            else if (op == OpCode.Members && message.Room == 1 && _client.Player == 3)
            {
                for (var i = 0; i < message.MemberIds.Length; i++)
                    if (message.MemberIds[i] != 3 && (message.Online & (1 << i)) == 0)
                        _observedOffline.Add(message.MemberIds[i]);
            }
            else if (op == OpCode.MarkerChanged && message.Room == 2 && message.Marker == 4)
                _secondMarker = true;
            else if (op == OpCode.RoomBroadcast)
            {
                if (message.Room == 2 && message.Text == "poc3-room-two")
                    _secondMessage = true;
                if (_client.Player == 1 && message.Text == "poc3-accepted-before-loss" && !_rejoined)
                {
                    _rebindStarted = _time;
                    _client.Disconnect();
                    _resumeAt = _time + 0.35f;
                }
                if (_client.Player == 2 && message.Text == "poc3-recovered" && !_recovering)
                {
                    _recovering = true;
                    RememberBinding();
                    BeginOnlineReplacement();
                }
                if (_client.Player == 1 && message.Text == "poc3-replaced" && !_handoff)
                {
                    _handoff = true;
                    _client.TransferOwner(2, 2);
                }
                if (message.Room == 1 && message.Text == "poc3-room-two-closed")
                    _finalSignal = true;
            }
            else if (op == OpCode.RoomClosed && message.Room == 2 && _client.Player == 2)
                _client.SendText(1, "poc3-room-two-closed");
        }

        private void BeginOnlineReplacement()
        {
            if (!_client.Connected)
            {
                Failure = "The old connection was not online when replacement started.";
                return;
            }
            try
            {
                _rebindStarted = _time;
                var replacement = _client.StartOnlineReplacement(_replacementPort);
                _retiring = _client;
                _retiring.Received -= OnMessage;
                _client = replacement;
                _client.Received += OnMessage;
                _log($"POC-3 replacing still-online session={_oldSession} generation={_oldGeneration}");
            }
            catch (InvalidOperationException error)
            {
                Failure = error.Message;
            }
        }

        public void Dispose()
        {
            if (_client != _initial)
                _client.Dispose();
        }
    }

    /// <summary>One sampled control round trip at a time, shared by the optional POC observers.</summary>
    public sealed class SliceObservation : IDisposable
    {
        private readonly SliceClient _client;
        private readonly Action<string> _log;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long _next, _sent;
        private uint _request;

        public SliceObservation(SliceClient client, Action<string> log)
        {
            _client = client;
            _log = log;
            _client.Received += Result;
        }

        public void Tick()
        {
            var now = _clock.ElapsedMilliseconds;
            if (_request != 0 && now - _sent >= 4000)
            {
                _log($"POC-4 RTT timeout player={_client.Player}");
                _request = 0;
            }
            if (_request != 0 || now < _next || !_client.Authenticated || !_client.Ready)
                return;
            _sent = now;
            _next = now + 1000;
            _request = _client.SendText("poc4-rt");
        }

        private void Result(OpCode op, SliceMessage message)
        {
            if (op != OpCode.Response || message.ReplyTo != OpCode.RoomMessage || _request == 0 || message.RequestId != _request)
                return;
            _log($"POC-4 RTT player={_client.Player} ms={_clock.ElapsedMilliseconds - _sent} result={message.Error}");
            _request = 0;
        }

        public void Dispose() => _client.Received -= Result;
    }
}
