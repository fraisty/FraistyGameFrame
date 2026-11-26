using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using FRFrame.Network.Client;
using FRFrame.Network.Protocol;
using FRFrame.Network.Replication;
using FRFrame.Network.Shared;
using UnityEngine;

namespace FRFrame.Network.Unity
{
    public sealed partial class NetworkMultiplayerDemo
    {
        private bool _checkChatReceived;

        // The files only coordinate three local test participants. All requests go through the normal client pump.
        internal void StartCapabilityCheck(int port, string identity, string directory)
        {
            if (_client != null || identity is not ("amber" or "fern" or "oak"))
                throw new InvalidOperationException("F5 requires a fresh demo and identity amber, fern or oak.");
            _capabilityCheck = true;
            _host = "127.0.0.1";
            _port = port.ToString();
            _identity = identity;
            RunCapabilityCheck(directory).Forget();
        }

        private async UniTask RunCapabilityCheck(string directory)
        {
            string role = _identity;
            bool owner = role == "amber";
            bool returning = role == "fern";
            var cancellation = destroyCancellationToken;
            void Require(bool ok, string stage)
            {
                if (!ok) throw new InvalidOperationException($"{role}: {stage}");
            }
            async UniTask Wait(Func<bool> ready, string stage)
            {
                float deadline = Time.realtimeSinceStartup + 35f;
                while (!ready())
                {
                    cancellation.ThrowIfCancellationRequested();
                    Require(Time.realtimeSinceStartup < deadline, stage);
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                }
            }
            void Signal(string name, string contents = "ok")
            {
                string file = Path.Combine(directory, name);
                File.WriteAllText(file + ".pending", contents);
                File.Move(file + ".pending", file);
            }
            UniTask AwaitFile(string name) => Wait(() => File.Exists(Path.Combine(directory, name)), name);

            int exit = 1;
            try
            {
                Require(Directory.Exists(directory), "coordination directory missing");
                Connect();
                Require(_client != null, "could not connect");
                var client = _client;
                var runtime = client.Runtime;
                async UniTask<ClientRequestResult> Reply(uint id, ErrorCode expected = ErrorCode.None)
                {
                    Require(id != 0, $"request rejected locally {client.LastSendError}");
                    await Wait(() => _capabilityResults.ContainsKey(id), $"request #{id} did not finish");
                    var result = _capabilityResults[id];
                    _capabilityResults.Remove(id);
                    Require(result.Status == ClientRequestStatus.Response && result.Error == expected,
                        $"{result.Operation} #{id}: {result.Status}/{result.Error}, expected {expected}");
                    return result;
                }
                async UniTask<int> Create(string definition, RoomConfiguration configuration, GameInitialization options = null)
                {
                    var before = runtime.Rooms.Keys.ToArray();
                    await Reply(runtime.CreateRoom(definition, configuration, options));
                    return runtime.Rooms.Keys.Single(id => !before.Contains(id));
                }

                await Wait(() => client.Authenticated, "login");
                int player = owner ? 17 : returning ? 401 : 809;
                Require(client.Player == player && runtime.Rooms.Count == 0, "configured identity and roomless session");
                var profile = await Reply(GetProfile());
                Require(profile.BusinessMessageId == ProfileProtocol.RequestId &&
                    ProfileProtocol.TryReadSnapshot(profile.BusinessPayload, out var identity) && identity == (player, 1u), "typed roomless Profile");
                await Wait(() => _profilePushes == 1, "directed Profile push");
                Require(_profileNotice == (player, 1u), "Profile push belongs to authenticated recipient");
                Log($"F5 PROFILE PASS identity={role} player={player} request={profile.RequestId} pushes=1");
                await CheckHttp(_httpBase, cancellation);

                int counter, plain, rpc;
                if (owner)
                {
                    plain = await Create(SliceMessage.PlainRoom, new RoomConfiguration("F5 Plain"));
                    rpc = await Create(DemoPulse.DefinitionId, new RoomConfiguration("F5 Native RPC"));
                    counter = await Create(CounterProtocol.DefinitionId,
                        new RoomConfiguration("F5 Counter", 3, RoomVisibility.Hidden, RoomJoinPolicy.InviteOnly),
                        CounterProtocol.CreateOptions(3, 17));
                    Signal("rooms", $"{plain}|{rpc}|{counter}");
                    await AwaitFile("browse-fern");
                    await AwaitFile("browse-oak");
                    await Reply(runtime.InvitePlayer(counter, 401));
                    await Reply(runtime.InvitePlayer(counter, 809));
                    await AwaitFile("oak-saw-invite");
                    await Reply(runtime.RevokeInvitation(counter, 809));
                    Signal("oak-revoked");
                }
                else
                {
                    await AwaitFile("rooms");
                    var rooms = File.ReadAllText(Path.Combine(directory, "rooms")).Split('|');
                    plain = int.Parse(rooms[0]);
                    rpc = int.Parse(rooms[1]);
                    counter = int.Parse(rooms[2]);
                    var first = (RoomDirectoryPage)(await Reply(runtime.ListRooms(namePrefix: "f5", pageSize: 1))).RoomData;
                    Require(first.HasMore && first.Rooms.Single().RoomId == plain, "directory first page");
                    var next = (RoomDirectoryPage)(await Reply(runtime.ListRooms(first.NextAfterId, namePrefix: "F5", pageSize: 1))).RoomData;
                    Require(!next.HasMore && next.Rooms.Single().RoomId == rpc && runtime.Rooms.Count == 0, "directory second page excludes Hidden room");
                    await Reply(runtime.GetRoomInfo(counter), ErrorCode.RoomUnavailable);
                    Signal("browse-" + role);
                    await Wait(() => _invitation.Room == counter && _invitation.Owner == 17, "directed invitation push");
                    if (returning)
                    {
                        var list = (RoomDirectoryPage)(await Reply(runtime.ListInvitations())).RoomData;
                        Require(list.Rooms.Single().RoomId == counter, "invitation list");
                        var detail = (RoomInfo)(await Reply(runtime.GetRoomInfo(counter))).RoomData;
                        Require(detail.Summary.Name == "F5 Counter" && detail.Visibility == RoomVisibility.Hidden, "invited room detail");
                        await Reply(runtime.JoinRoom(counter));
                        Require(((RoomDirectoryPage)(await Reply(runtime.ListInvitations())).RoomData).Rooms.Count == 0, "invite consumed on join");
                    }
                    else
                    {
                        Signal("oak-saw-invite");
                        await AwaitFile("oak-revoked");
                        await Reply(runtime.JoinRoom(counter), ErrorCode.Forbidden);
                        Require(((RoomDirectoryPage)(await Reply(runtime.ListInvitations())).RoomData).Rooms.Count == 0, "revocation visible");
                        await Reply(runtime.JoinRoom(plain));
                    }
                }
                Log($"F5 ROOMS PASS role={role} plain={plain} rpc={rpc} counter={counter}");

                if (owner || returning)
                {
                    client.SelectRoom(counter);
                    await Wait(() => _counterView is { IsCurrent: true } && _counterView.Snapshot is { Value: 3, Limit: 17, Revision: 1 }, "Counter initial view");
                    if (owner)
                    {
                        var original = await Reply(runtime.SendGameQuery(counter, CounterProtocol.GetState, ReadOnlySpan<byte>.Empty));
                        await AwaitFile("counter-ready-fern");
                        await Reply(runtime.ConfigureRoom(counter, new RoomConfiguration("F5 Revised", 2, RoomVisibility.Hidden, RoomJoinPolicy.InviteOnly)));
                        await Wait(() => runtime.Rooms[counter].Name == "F5 Revised" && runtime.Rooms[counter].Capacity == 2, "new room configuration");
                        Signal("configured");
                        await AwaitFile("counter-disconnected");
                        await Wait(() => runtime.Rooms[counter].OnlineMask == 1, "offline retained member");
                        await Reply(runtime.SendGameCommand(counter, CounterProtocol.Add, CounterProtocol.EncodeAdd(4)));
                        await Wait(() => _counterView.Snapshot is { Value: 7, Limit: 17, Revision: 2 }, "versioned Counter Add");
                        var old = await Reply(client.Send(OpCode.GameQuery, new SliceMessage { Room = counter, RequestId = original.RequestId, GameMessageId = CounterProtocol.GetState }));
                        Require(CounterProtocol.TryReadState(old.GameQueryPayload, out var previous) && previous.Revision == 1 &&
                            _counterView.Snapshot is { Revision: 2 }, "historical query cannot roll back view");
                        Signal("counter-changed");
                    }
                    else
                    {
                        Signal("counter-ready-fern");
                        await AwaitFile("configured");
                        await Wait(() => runtime.Rooms[counter].Name == "F5 Revised" && runtime.Rooms[counter].Capacity == 2, "member configuration updated");
                        int session = client.Session;
                        client.Disconnect();
                        Signal("counter-disconnected");
                        await AwaitFile("counter-changed");
                        Require(!_counterView.IsCurrent && _counterView.Snapshot is { Revision: 1 }, "stale view while disconnected");
                        Require(client.Reconnect(true), "explicit Counter Resume");
                        await Wait(() => client.Authenticated && client.Session == session && _counterView is { IsCurrent: true } &&
                            _counterView.Snapshot is { Value: 7, Limit: 17, Revision: 2 }, "new Current Members triggers Counter recovery");
                        Signal("counter-restored");
                    }
                    Log($"F5 COUNTER PASS role={role} value=7 limit=17 revision=2");
                }
                await AwaitFile("counter-restored");

                if (!owner) await Reply(runtime.JoinRoom(rpc));
                client.SelectRoom(rpc);
                await Wait(() => client.Ready && client.Baselines > 0 && client.Members.Length == 3, "three LES baselines");
                Signal("les-ready-" + role);
                if (owner)
                {
                    await AwaitFile("les-ready-fern");
                    await AwaitFile("les-ready-oak");
                    _scriptMove = 1;
                }
                await Wait(() => client.Diffs > 0 && client.Rooms[rpc].RpcPulse is { Count: > 0, Player: 17 }, "native RPC delivered to this replica");
                _scriptMove = 0;
                Log($"F5 RPC PASS role={role} player=17 baseline={client.Baselines} diffs={client.Diffs}");
                Signal("rpc-" + role);

                if (returning)
                {
                    int session = client.Session;
                    uint originalStream = client.Rooms[rpc].StreamId;
                    client.Disconnect();
                    Signal("les-disconnected");
                    await AwaitFile("resume-les");
                    Require(client.Reconnect(true), "explicit LES Resume");
                    await Wait(() => client.Authenticated && client.Session == session && client.Ready && client.Baselines > 0 &&
                        client.Rooms[rpc].StreamId != originalStream, "resumed LES stream baseline");
                    Signal("les-resumed");
                    await Wait(() => client.Diffs > 0, "resumed native LES diff");
                    await Reply(runtime.LeaveRoom(counter));
                    Log($"F5 LES RESUME PASS role={role} session={session} stream={client.Rooms[rpc].StreamId}");
                    Signal("les-diff");
                }
                else if (owner)
                {
                    await AwaitFile("rpc-oak");
                    await AwaitFile("les-disconnected");
                    await Wait(() => client.Rooms[rpc].OnlineMask != 7, "offline LES membership");
                    Signal("resume-les");
                    await AwaitFile("les-resumed");
                    _scriptMove = -1;
                    await AwaitFile("les-diff");
                    _scriptMove = 0;
                    await Wait(() => _checkChatReceived, "plain room shared chat");
                    await Reply(runtime.Kick(rpc, 809));
                    await Reply(runtime.TransferOwner(plain, 809));
                    await Wait(() => !runtime.Rooms.ContainsKey(plain), "new owner closed plain room");
                    Signal("finish");
                }
                else
                {
                    await Reply(runtime.SendRoomMessage(plain, "F5 plain chat"));
                    await Wait(() => !runtime.Rooms.ContainsKey(rpc) && runtime.Rooms[plain].Owner == 809, "Kick and ownership transfer");
                    await Reply(runtime.CloseRoom(plain));
                }
                await AwaitFile("finish");
                Require(_profilePushes == 1, "no Profile push for either other player");
                Disconnect();
                Signal("pass-" + role);
                Log($"F5 PASS role={role}");
                exit = 0;
            }
            catch (Exception error)
            {
                Debug.LogError($"F5 FAIL role={role}: {error}");
            }
            if (!Application.isEditor)
                Application.Quit(exit);
        }
    }
}
