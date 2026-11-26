using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FRFrame.Network.Client;
using FRFrame.Network.Protocol;
using FRFrame.Network.Replication;
using FRFrame.Network.Shared;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Network.Unity
{
    public sealed partial class NetworkMultiplayerDemo
    {
        private readonly Dictionary<uint, ClientRequestResult> _capabilityResults = new();
        private readonly HttpManager _http = new();
        private CounterRoomState _counterView;
        private RoomDirectoryPage _directory;
        private bool _listingInvitations;
        private RoomInfo _detail;
        private string _roomName = "Demo", _capacity = "3", _initial = "3", _limit = "17", _prefix = "";
        private string _httpBase = "http://127.0.0.1:18952", _httpStatus = "";
        private bool _hidden, _inviteOnly, _showCapabilities, _httpBusy;
        private bool _capabilityCheck;
        private sbyte _scriptMove;
        private int _pulseRoom, _pulseCount, _profilePushes;
        private (int Player, uint Count) _profileNotice;
        private (int Room, int Owner, int RemainingMilliseconds) _invitation;

        private uint GetProfile() => _client.Runtime.SendBusinessRequest(ProfileProtocol.RequestId, ProfileProtocol.EncodeRequest(1));

        private void OnProfilePush(ClientBusinessPush push)
        {
            if (push.MessageId != ProfileProtocol.PushId || !ProfileProtocol.TryReadSnapshot(push.Payload, out var profile))
                return;
            _profilePushes++;
            _profileNotice = profile;
            Log($"Profile push P{profile.Player}: {profile.Count}");
        }

        private void OnRoomInvitation((int Room, int Owner, int RemainingMilliseconds) invitation)
        {
            _invitation = invitation;
            Log($"Invite: room {invitation.Room} from P{invitation.Owner}");
        }

        private void OnCapabilityResult(ClientRequestResult result)
        {
            if (_capabilityCheck)
                _capabilityResults[result.RequestId] = result;
            if (result.RoomData is RoomDirectoryPage page)
            {
                _directory = page;
                _listingInvitations = result.Operation == OpCode.ListInvitations;
            }
            if (result.RoomData is RoomInfo detail)
                _detail = detail;
            if (result.BusinessMessageId == ProfileProtocol.RequestId && ProfileProtocol.TryReadSnapshot(result.BusinessPayload, out var profile))
                Log($"Profile result #{result.RequestId} P{profile.Player}: {profile.Count}");
        }

        private void UpdateCapabilityView()
        {
            _client.Rooms.TryGetValue(_client.Room, out var room);
            if (room?.Definition == CounterProtocol.DefinitionId)
            {
                if (_counterView == null || _counterView.RoomId != room.Id)
                {
                    _counterView?.Dispose();
                    _counterView = new CounterRoomState(_client.Runtime, room.Id);
                }
            }
            else if (_counterView != null)
            {
                _counterView.Dispose();
                _counterView = null;
            }

            var pulse = room?.RpcPulse ?? default;
            if (_pulseRoom != _client.Room)
                _pulseCount = 0;
            _pulseRoom = _client.Room;
            if (pulse.Count > _pulseCount)
                Log($"LES native RPC received room={_pulseRoom} moved=P{pulse.Player} count={pulse.Count}");
            _pulseCount = pulse.Count;
        }

        private void DrawRoomCapabilities(SliceClient client)
        {
            _showCapabilities = GUILayout.Toggle(_showCapabilities, "Room directory, invitations and sample games");
            if (!_showCapabilities)
                return;

            var runtime = client.Runtime;
            if (GUILayout.Button("Profile + directed push")) GetProfile();
            if (GUILayout.Button("Native LES RPC room")) runtime.CreateRoom(DemoPulse.DefinitionId);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name");
            GUI.SetNextControlName("f5-name");
            _roomName = GUILayout.TextField(_roomName, 32);
            GUILayout.Label("Seats");
            GUI.SetNextControlName("f5-seats");
            _capacity = GUILayout.TextField(_capacity, 2, GUILayout.Width(35));
            GUILayout.EndHorizontal();
            _hidden = GUILayout.Toggle(_hidden, "Hidden");
            _inviteOnly = GUILayout.Toggle(_inviteOnly, "Invite only");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Counter initial / limit");
            GUI.SetNextControlName("f5-initial");
            _initial = GUILayout.TextField(_initial, 3, GUILayout.Width(42));
            GUI.SetNextControlName("f5-limit");
            _limit = GUILayout.TextField(_limit, 3, GUILayout.Width(42));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create configured Counter")) ApplyRoomConfiguration(runtime, true);
            if (client.Room != 0 && GUILayout.Button("Configure selected")) ApplyRoomConfiguration(runtime, false);
            GUILayout.EndHorizontal();

            if (runtime.Rooms.TryGetValue(client.Room, out var room) && room.IsCurrent)
                GUILayout.Label($"Members: {room.Name} · {room.Members.Count}/{room.Capacity} · {room.Visibility} / {room.JoinPolicy}");
            if (client.Rooms.TryGetValue(client.Room, out var view) && view.RpcPulse is { Count: > 0 } pulse)
                GUILayout.Label($"Native RPC · P{pulse.Player} moved · {pulse.Count} announcements");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name prefix");
            GUI.SetNextControlName("f5-prefix");
            _prefix = GUILayout.TextField(_prefix, 32);
            if (GUILayout.Button("List rooms")) runtime.ListRooms(namePrefix: _prefix);
            if (GUILayout.Button("Invitations")) runtime.ListInvitations();
            GUILayout.EndHorizontal();
            if (_directory != null)
            {
                foreach (var item in _directory.Rooms)
                    if (GUILayout.Button($"#{item.RoomId} {item.Name} ({item.Definition}) {item.MemberCount}/{item.Capacity}"))
                        _room = item.RoomId.ToString();
                if (_directory.HasMore && GUILayout.Button("Next page"))
                {
                    if (_listingInvitations) runtime.ListInvitations(_directory.NextAfterId);
                    else runtime.ListRooms(_directory.NextAfterId, namePrefix: _prefix);
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Inspect room ID") && int.TryParse(_room, out int id)) runtime.GetRoomInfo(id);
            if (GUILayout.Button("Invite target") && int.TryParse(_target, out int target)) runtime.InvitePlayer(client.Room, target);
            if (GUILayout.Button("Revoke") && int.TryParse(_target, out int revoke)) runtime.RevokeInvitation(client.Room, revoke);
            GUILayout.EndHorizontal();
            if (_detail != null)
                GUILayout.Label($"Query: #{_detail.Summary.RoomId} {_detail.Summary.Name}, owner P{_detail.Owner}, {_detail.Visibility}");
            if (_invitation.Room > 0 && GUILayout.Button($"Use invite room #{_invitation.Room} from P{_invitation.Owner}"))
                _room = _invitation.Room.ToString();
        }

        private void ApplyRoomConfiguration(ClientRuntime runtime, bool create)
        {
            if (!int.TryParse(_capacity, out int capacity) || capacity is < 1 or > 3 ||
                (create && (!uint.TryParse(_initial, out _) || !uint.TryParse(_limit, out _))))
            {
                Log("Enter seats 1–3 and numeric Counter options.");
                return;
            }
            try
            {
                var config = new RoomConfiguration(_roomName, capacity, _hidden ? RoomVisibility.Hidden : RoomVisibility.Discoverable,
                    _inviteOnly ? RoomJoinPolicy.InviteOnly : RoomJoinPolicy.Open);
                if (create) runtime.CreateRoom(CounterProtocol.DefinitionId, config, CounterProtocol.CreateOptions(uint.Parse(_initial), uint.Parse(_limit)));
                else runtime.ConfigureRoom(_client.Room, config);
            }
            catch (ArgumentException error) { Log(error.Message); }
        }

        private void DrawHttpCapability()
        {
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("f5-http");
            _httpBase = GUILayout.TextField(_httpBase, 160);
            if (!_httpBusy && GUILayout.Button("Check HTTP")) RunHttpButton().Forget();
            GUILayout.EndHorizontal();
            if (_httpStatus.Length != 0)
                GUILayout.Label(_httpStatus);
        }

        private async UniTask RunHttpButton()
        {
            _httpBusy = true;
            try { await CheckHttp(_httpBase, destroyCancellationToken); }
            catch (Exception error) { _httpStatus = error.Message; }
            finally { _httpBusy = false; }
        }

        [Preserve]
        private sealed class FixturePayload
        {
            [Preserve] public int value = 0;
            [Preserve] public FixturePayload() { }
        }

        private async UniTask CheckHttp(string url, CancellationToken cancellation)
        {
            url = url.TrimEnd('/');
            await _http.InitializeAsync();
            var json = await _http.GetJsonAsync<FixturePayload>(url + "/json", cancellationToken: cancellation);
            var bytes = await _http.SendBytesAsync(FRFrame.HttpMethod.Post, url + "/bytes", new byte[] { 0, 42, 255 }, cancellationToken: cancellation);
            var missing = await _http.GetBytesAsync(url + "/missing", cancellationToken: cancellation);
            _httpStatus = $"HTTP JSON={json.StatusCode}/{json.ErrorKind}, bytes={bytes.StatusCode}/{bytes.ErrorKind}, missing={missing.StatusCode}/{missing.ErrorKind}";
            if (!json.IsSuccess || json.Data?.value != 42 || !bytes.IsSuccess || bytes.Data.Length != 3 ||
                bytes.Data[0] != 0 || bytes.Data[1] != 42 || bytes.Data[2] != 255 || missing.StatusCode != 404 || missing.ErrorKind != HttpErrorKind.HttpStatus)
                throw new InvalidOperationException(_httpStatus);
            Log($"F5 HTTP PASS JSON=42 bytes=00-2A-FF status=404 player={_identity}");
        }
    }
}
