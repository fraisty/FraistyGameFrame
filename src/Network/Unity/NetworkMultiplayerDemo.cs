using System;
using System.Collections.Generic;
using FRFrame.Network.Client;
using FRFrame.Network.Protocol;
using FRFrame.Network.Replication;
using FRFrame.Network.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FRFrame.Network.Unity
{
    /// <summary>Reusable network scene for manual or scripted tests through the same client.</summary>
    public sealed class NetworkMultiplayerDemo : MonoBehaviour
    {
        private static readonly Color[] PawnColors = { Color.cyan, new Color(1, 0.65f, 0.25f), new Color(0.83f, 0.60f, 1) };
        private readonly Dictionary<int, (GameObject Object, Material Material)> _views = new();
        private readonly HashSet<int> _seenViews = new();
        private readonly List<int> _missingViews = new();
        private readonly Queue<string> _events = new(8);
        private SliceClient _client;
        private SliceSmoke _smoke;
        private SliceLifecycleSmoke _lifecycle;
        private SliceObservation _observation;
        private bool _observe;
        private string _host = "127.0.0.1";
        private string _port = "9050";
        private string _identity = "A";
        private string _room = "1";
        private string _message = "Hello";
        private Camera _camera;
        private Material _floorMaterial;
        private bool _typing;
        private bool _reportedTimeout;
        private bool _createOnLogin;
        private bool _counterProbe;
        private bool _counterProbeSent;
        private int _joinOnLogin;
        private int _viewRoom;
        private string _target = "2";
        private Vector2 _scroll;
        private uint _retryRequest;
        private int _retrySession;
        private string _requestStatus = "";

        private void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            SliceClient.SetReplicationLog(Debug.Log, Debug.LogWarning, Debug.LogError);
            _camera = Camera.main;

            // A small floor provides visual context for the replicated directions.
            if (!Application.isBatchMode)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Movement bounds";
                floor.transform.SetParent(transform);
                floor.transform.SetPositionAndRotation(new Vector3(0, -0.2f, 0), Quaternion.identity);
                floor.transform.localScale = new Vector3(24, 0.1f, 16);
                _floorMaterial = floor.GetComponent<Renderer>().material;
                _floorMaterial.color = new Color(0.22f, 0.28f, 0.33f);
            }

            var arguments = Environment.GetCommandLineArgs();
            _observe = Array.IndexOf(arguments, "-frNetworkObserve") >= 0;
            _counterProbe = Array.IndexOf(arguments, "-frNetworkCounter") >= 0;
            if (Array.IndexOf(arguments, "-frNetworkLayoutCheck") >= 0)
            {
                SliceLayoutCheck.Run(Debug.Log);
                Application.Quit();
                return;
            }
            var start = Array.IndexOf(arguments, "-frNetworkDemo");
            if (start >= 0 && start + 4 < arguments.Length)
            {
                _host = arguments[start + 1];
                _port = arguments[start + 2];
                _identity = arguments[start + 3];
                var create = arguments[start + 4] == "create";
                if (!create)
                    _room = arguments[start + 4];
                Connect();
                if (_client != null && int.TryParse(_room, out var room))
                {
                    if (Array.IndexOf(arguments, "-frNetworkLifecycle") >= 0)
                    {
                        var option = Array.IndexOf(arguments, "-frNetworkReplacementPort");
                        var replacementPort = 0;
                        if (option >= 0 && option + 1 < arguments.Length)
                            int.TryParse(arguments[option + 1], out replacementPort);
                        _lifecycle = new SliceLifecycleSmoke(_client, Log,
                            Array.IndexOf(arguments, "-frNetworkShort") >= 0, replacementPort);
                    }
                    else if (Array.IndexOf(arguments, "-frNetworkAuto") >= 0)
                        _smoke = new SliceSmoke(_client, create, room, Log);
                    else
                    {
                        _createOnLogin = create;
                        _joinOnLogin = create ? 0 : room;
                    }
                }
            }
        }

        private void Update()
        {
            if (_client == null)
                return;
            _seenViews.Clear();
            if (_lifecycle != null)
            {
                _lifecycle.Tick(Time.unscaledDeltaTime * 1000, UpdateView);
                if (_observe && _lifecycle.Passed)
                {
                    _observation ??= new SliceObservation(_lifecycle.CurrentClient, Log);
                    _observation.Tick();
                }
                if (!_reportedTimeout && (_lifecycle.TimedOut || _lifecycle.Failure.Length != 0))
                {
                    _reportedTimeout = true;
                    var current = _lifecycle.CurrentClient;
                    Debug.LogError($"POC-3 FAIL identity={_identity} reason={_lifecycle.Failure} room={current.Room} epoch={current.Epoch}");
                }
            }
            else if (_smoke != null)
            {
                _smoke.Tick(Time.unscaledDeltaTime * 1000, UpdateView);
                if (!_reportedTimeout && (_smoke.TimedOut || (!_smoke.Passed && _client.Disconnected)))
                {
                    _reportedTimeout = true;
                    Debug.LogError($"POC-2 FAIL identity={_identity} ready={_client.Ready} " +
                                   $"baseline={_client.Baselines} diffs={_client.Diffs} online={_client.OnlineMask}");
                }
            }
            else
            {
                var keyboard = Keyboard.current;
                var x = 0;
                var y = 0;
                if (keyboard != null && !_typing)
                {
                    x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                    y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
                }
                _client.Tick(Time.unscaledDeltaTime * 1000, (sbyte)x, (sbyte)y, UpdateView);
            }
            _missingViews.Clear();
            foreach (var player in _views.Keys)
                if (!_seenViews.Contains(player))
                    _missingViews.Add(player);
            foreach (var player in _missingViews)
            {
                var removed = _views[player];
                _views.Remove(player);
                Destroy(removed.Material);
                Destroy(removed.Object);
            }
        }

        private void Connect()
        {
            if (!int.TryParse(_port, out var port) || port is < 1 or > 65535 ||
                _identity.Length != 1 || _identity[0] is < 'A' or > 'H' || string.IsNullOrWhiteSpace(_host))
            {
                Log("Enter a host, a port between 1 and 65535, and an identity from A to H.");
                return;
            }
            try
            {
                _client = new SliceClient(_host, port, _identity, Log);
                _client.Received += OnMessage;
                _client.GameEventReceived += OnGameEvent;
                _client.RequestFinished += OnRequestFinished;
            }
            catch (InvalidOperationException error)
            {
                Log(error.Message);
            }
        }

        private void OnMessage(OpCode op, SliceMessage message)
        {
            if (op == OpCode.Response)
            {
                Log($"POC-2 reply {message.ReplyTo} request={message.RequestId} result={message.Error} room={message.Room} player={message.Player}");
                if (message.ReplyTo == OpCode.Login && message.Error == ErrorCode.None)
                {
                    if (_createOnLogin)
                        _client.CreateRoom(_counterProbe ? CounterProtocol.DefinitionId : SliceMessage.DefinitionId);
                    else if (_joinOnLogin > 0)
                        _client.JoinRoom(_joinOnLogin);
                    _createOnLogin = false;
                    _joinOnLogin = 0;
                }
            }
            else if (op == OpCode.RoomBroadcast)
                Log($"POC-2 room={message.Room} from={message.Player} {message.Text}");
            else if (op == OpCode.MarkerChanged)
                Log($"POC-2 marker={message.Marker} from={message.Player}");
            else if (op == OpCode.Members)
            {
                Log($"POC-2 members={string.Join(",", message.MemberIds)} online-mask={message.Online} owner={message.Player} room={message.Room}");
                if (_counterProbe && !_counterProbeSent && message.Text == CounterProtocol.DefinitionId)
                {
                    _counterProbeSent = true;
                    AddCounter(message.Room);
                }
            }
        }

        private void AddCounter(int room) => _client.SendGameCommand(room, CounterProtocol.Add, CounterProtocol.EncodeAdd(1));

        private void OnGameEvent(ClientGameEvent message)
        {
            if (message.Id != CounterProtocol.Changed || !_client.Rooms.TryGetValue(message.Room, out var room) ||
                room.Definition != CounterProtocol.DefinitionId || !CounterProtocol.TryReadChanged(message.Payload, out var value))
                return;
            Log($"Counter room {message.Room}: {value}");
            if (_counterProbe && value == 1 && room.StreamId == 0 && room.Baselines == 0 && room.State == SliceStreamState.Idle)
                Log($"P5 counter PASS room={message.Room} value={value} no LES stream");
        }

        private void UpdateView(PawnSample sample)
        {
            if (Application.isBatchMode || sample.Player is < 1 or > 8 || sample.Room != (_lifecycle?.CurrentClient ?? _client).Room)
                return;
            if (_viewRoom != sample.Room)
            {
                ClearViews();
                _viewRoom = sample.Room;
            }
            if (!_views.TryGetValue(sample.Player, out var view))
            {
                var pawn = GameObject.CreatePrimitive(sample.Owned ? PrimitiveType.Capsule : PrimitiveType.Sphere);
                pawn.name = $"Network player {sample.Player}";
                pawn.transform.SetParent(transform);
                pawn.transform.localScale = Vector3.one * 0.8f;
                var material = pawn.GetComponent<Renderer>().material;
                material.color = PawnColors[(sample.Player - 1) % PawnColors.Length];
                view = (pawn, material);
                _views.Add(sample.Player, view);
            }
            _seenViews.Add(sample.Player);
            view.Object.transform.position = new Vector3(sample.X, 0.65f, sample.Y);
        }

        private void OnGUI()
        {
            if (Application.isBatchMode)
                return;
            if (_camera != null)
            {
                foreach (var view in _views)
                {
                    var position = _camera.WorldToScreenPoint(view.Value.Object.transform.position);
                    GUI.Label(new Rect(position.x - 10, Screen.height - position.y - 31, 40, 21), $"P{view.Key}");
                }
            }

            GUILayout.BeginArea(new Rect(12, 12, 365, Mathf.Min(590, Screen.height - 24)), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            var client = _lifecycle?.CurrentClient ?? _client;
            if (client == null)
            {
                GUILayout.Label("Host");
                _host = GUILayout.TextField(_host, 253);
                GUILayout.Label("Port");
                _port = GUILayout.TextField(_port, 5);
                GUILayout.Label("Test identity (A–H)");
                _identity = GUILayout.TextField(_identity, 1).ToUpperInvariant();
                if (GUILayout.Button("Connect"))
                    Connect();
            }
            else
            {
                GUILayout.Label(client.AuthenticationError != ErrorCode.None ? $"Authentication failed: {client.AuthenticationError}" :
                    client.ConnectionError != ErrorCode.None ? $"Connection failed: {client.ConnectionError}" : client.State.ToString());
                if (_requestStatus.Length != 0)
                    GUILayout.Label(_requestStatus);
                if (client.LastSendError != ErrorCode.None)
                    GUILayout.Label($"Request rejected locally: {client.LastSendError}");
                if (client.Authenticated)
                    GUILayout.Label($"Player {client.Player} · Session {client.Session} · Epoch {client.Epoch} · Generation {client.ConnectionGeneration}");
                if (client.Authenticated)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Shared room")) client.CreateRoom(SliceMessage.SharedDefinitionId);
                    if (GUILayout.Button("Per-room")) client.CreateRoom();
                    if (GUILayout.Button("Chat room")) client.CreateRoom(SliceMessage.PlainRoom);
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button("Counter room")) client.CreateRoom(CounterProtocol.DefinitionId);
                    GUILayout.BeginHorizontal();
                    _room = GUILayout.TextField(_room, 6);
                    if (GUILayout.Button("Join room") && int.TryParse(_room, out var room))
                        client.JoinRoom(room);
                    GUILayout.EndHorizontal();
                }
                foreach (var entry in client.Rooms.Values)
                    if (GUILayout.Button($"{(entry.Id == client.Room ? "Selected" : "Open")} room {entry.Id} · owner P{entry.Owner} · {(entry.IsCurrent ? entry.State.ToString() : "stale")}"))
                        client.SelectRoom(entry.Id);
                if (client.Room != 0 && client.Authenticated)
                {
                    var definition = client.Rooms[client.Room].Definition;
                    var counter = definition == CounterProtocol.DefinitionId;
                    var hasMovement = definition is SliceMessage.DefinitionId or SliceMessage.SharedDefinitionId;
                    var members = "";
                    var roomMembers = client.Members;
                    for (var i = 0; i < roomMembers.Length; i++)
                        members += $"  P{roomMembers[i]}:{((client.OnlineMask & (1 << i)) != 0 ? "online" : "offline")}";
                    GUILayout.Label(members);
                    if (hasMovement)
                        GUILayout.Label(client.StreamState switch
                    {
                        SliceStreamState.Ready => "LES ready. Click Move, then use WASD.",
                        SliceStreamState.Pending => "Negotiating LES stream…",
                        _ => $"LES {client.StreamState}: {client.StreamError}"
                    });
                    GUILayout.BeginHorizontal();
                    GUI.SetNextControlName("room message");
                    _message = GUILayout.TextField(_message, 80);
                    if (GUILayout.Button("Send") && !string.IsNullOrWhiteSpace(_message))
                        client.SendText(_message);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (hasMovement && GUILayout.Button("Move"))
                        GUI.FocusControl(null);
                    if (hasMovement && GUILayout.Button($"Advance marker ({client.Marker})"))
                        client.SetMarker((client.Marker + 1) % 10);
                    if (counter && GUILayout.Button("Add 1")) AddCounter(client.Room);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    _target = GUILayout.TextField(_target, 1, GUILayout.Width(24));
                    int.TryParse(_target, out var target);
                    if (GUILayout.Button("Leave")) client.LeaveRoom(client.Room);
                    if (GUILayout.Button("Kick")) client.Kick(client.Room, target);
                    if (GUILayout.Button("Transfer")) client.TransferOwner(client.Room, target);
                    if (GUILayout.Button("Close room")) client.CloseRoom(client.Room);
                    GUILayout.EndHorizontal();
                    if (client.StreamState is SliceStreamState.Failed or SliceStreamState.Closed && GUILayout.Button("Open stream"))
                        client.OpenRoomStream(client.Room);
                }
                if (client.Authenticated)
                {
                    if (_retryRequest != 0 && client.Session == _retrySession && GUILayout.Button($"Retry request {_retryRequest}"))
                        client.Retry(_retryRequest);
                    if (GUILayout.Button("Disconnect")) client.Disconnect();
                }
                else if (client.Disconnected)
                {
                    if (client.CanRecover && GUILayout.Button("Recover session")) client.Reconnect(true);
                    if (GUILayout.Button("New login")) client.Reconnect(false);
                    if (GUILayout.Button("Back to connection settings")) Disconnect();
                }
            }
            foreach (var message in _events)
                GUILayout.Label(message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            _typing = GUI.GetNameOfFocusedControl() == "room message";
        }

        private void Log(string message)
        {
            Debug.Log(message);
            if (_events.Count == 8)
                _events.Dequeue();
            _events.Enqueue(message);
        }

        private void Disconnect()
        {
            _observation?.Dispose();
            _observation = null;
            _lifecycle?.Dispose();
            if (_client != null)
            {
                _client.Received -= OnMessage;
                _client.GameEventReceived -= OnGameEvent;
                _client.RequestFinished -= OnRequestFinished;
                _client.Dispose();
            }
            _client = null;
            _smoke = null;
            _lifecycle = null;
            _reportedTimeout = false;
            _createOnLogin = false;
            _counterProbeSent = false;
            _joinOnLogin = 0;
            _requestStatus = "";
            _retryRequest = 0;
            _retrySession = 0;
            ClearViews();
        }

        private void OnRequestFinished(ClientRequestResult result)
        {
            _requestStatus = $"Request {result.RequestId} · {result.Status} · {result.Error}";
            if (result.MayHaveExecuted || (result.Status == ClientRequestStatus.Response && result.Error == ErrorCode.Overloaded))
            {
                _retryRequest = result.RequestId;
                _retrySession = _client.Session;
            }
            else if (_retryRequest == result.RequestId)
                _retryRequest = 0;
        }

        private void ClearViews()
        {
            foreach (var view in _views.Values)
            {
                Destroy(view.Material);
                Destroy(view.Object);
            }
            _views.Clear();
        }

        private void OnDestroy()
        {
            Disconnect();
            Destroy(_floorMaterial);
        }
    }
}
