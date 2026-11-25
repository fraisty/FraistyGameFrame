using System;
using System.Collections.Generic;
using FRFrame.Network.Replication;
using FRFrame.Network.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FRFrame.Network.Unity
{
    /// <summary>Reusable POC-2 scene for manual or scripted tests through the same client.</summary>
    public sealed class NetworkMultiplayerDemo : MonoBehaviour
    {
        private static readonly Color[] PawnColors = { Color.cyan, new Color(1, 0.65f, 0.25f), new Color(0.83f, 0.60f, 1) };
        private readonly Dictionary<int, (GameObject Object, Material Material)> _views = new();
        private readonly Queue<string> _events = new(8);
        private SliceClient _client;
        private SliceSmoke _smoke;
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
        private int _joinOnLogin;

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
                    if (Array.IndexOf(arguments, "-frNetworkAuto") >= 0)
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
            if (_smoke != null)
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
            if (!_client.Ready && _views.Count != 0)
                ClearViews();
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
            }
            catch (InvalidOperationException error)
            {
                Log(error.Message);
            }
        }

        private void OnMessage(ProbeOp op, SliceMessage message)
        {
            if (op == ProbeOp.Response)
            {
                Log($"POC-2 reply {message.ReplyTo} request={message.RequestId} result={message.Error} room={message.Room} player={message.Player}");
                if (message.ReplyTo == ProbeOp.Login && message.Error == SliceError.None)
                {
                    if (_createOnLogin)
                        _client.CreateRoom();
                    else if (_joinOnLogin > 0)
                        _client.JoinRoom(_joinOnLogin);
                    _createOnLogin = false;
                    _joinOnLogin = 0;
                }
            }
            else if (op == ProbeOp.RoomBroadcast)
                Log($"POC-2 room={message.Room} from={message.Player} {message.Text}");
            else if (op == ProbeOp.MarkerChanged)
                Log($"POC-2 marker={message.Marker} from={message.Player}");
            else if (op == ProbeOp.Members)
                Log($"POC-2 members={string.Join(",", message.MemberIds)} online-mask={message.Online} owner={message.Player}");
        }

        private void UpdateView(PawnSample sample)
        {
            if (Application.isBatchMode || sample.Player is < 1 or > 8)
                return;
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

            GUILayout.BeginArea(new Rect(12, 12, 355, 415), GUI.skin.box);
            if (_client == null)
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
                GUILayout.Label(_client.Disconnected ? "Disconnected" : _client.Connected ? "Connected" : "Connecting…");
                GUILayout.Label($"Player {_client.Player}    Session {_client.Session}    Room {_client.Room}    Owner {_client.Owner}");
                if (_client.Session != 0 && _client.Room == 0 && _client.Connected)
                {
                    if (GUILayout.Button("Create movement room"))
                        _client.CreateRoom();
                    GUILayout.BeginHorizontal();
                    _room = GUILayout.TextField(_room, 6);
                    if (GUILayout.Button("Join room") && int.TryParse(_room, out var room))
                        _client.JoinRoom(room);
                    GUILayout.EndHorizontal();
                }
                else if (_client.Room != 0)
                {
                    var members = "";
                    for (var i = 0; i < _client.Members.Length; i++)
                        members += $"  P{_client.Members[i]}:{((_client.OnlineMask & (1 << i)) != 0 ? "online" : "offline")}";
                    GUILayout.Label(members);
                    GUILayout.Label(_client.StreamState switch
                    {
                        SliceStreamState.Ready => "LES ready. Click Move, then use WASD.",
                        SliceStreamState.Pending => "Negotiating LES stream…",
                        _ => $"LES {_client.StreamState}: {_client.StreamError}"
                    });
                    GUILayout.BeginHorizontal();
                    GUI.SetNextControlName("room message");
                    _message = GUILayout.TextField(_message, 80);
                    if (GUILayout.Button("Send") && !string.IsNullOrWhiteSpace(_message))
                        _client.SendText(_message);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Move"))
                        GUI.FocusControl(null);
                    if (GUILayout.Button($"Advance marker ({_client.Marker})"))
                        _client.SetMarker((_client.Marker + 1) % 10);
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Disconnect"))
                    Disconnect();
            }
            foreach (var message in _events)
                GUILayout.Label(message);
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
            _client?.Dispose();
            _client = null;
            _smoke = null;
            _reportedTimeout = false;
            _createOnLogin = false;
            _joinOnLogin = 0;
            ClearViews();
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
