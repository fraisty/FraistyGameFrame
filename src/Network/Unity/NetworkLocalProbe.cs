using System;
using FRFrame.Network.Protocol;
using FRFrame.Network.Replication;
using UnityEngine;

namespace FRFrame.Network.Unity
{
    internal sealed class NetworkLocalProbe : MonoBehaviour
    {
        private NetworkProbeClient _probe;
        private float _seconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void FromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-frNetworkProbe");
            if (index >= 0 && index + 2 < args.Length && int.TryParse(args[index + 2], out var port))
                Run(args[index + 1], port);
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("FRFrame/Network/Run Local P1 Protocol Probe")]
        private static void RunEditorProbe()
        {
            if (Application.isPlaying)
                Run("127.0.0.1", 9050);
            else
                Debug.Log("[FRFrame.Network] Enter Play mode with the local Host running first.");
        }
#endif

        private static void Run(string host, int port)
        {
            void Log(string value) => Debug.Log($"[FRFrame.Network] {value}");
            ProtocolChecks.Run(Log);
            var probe = new NetworkProbeClient(host, port, Log);
            var owner = new GameObject("FRFrame Network P1 Protocol Probe");
            DontDestroyOnLoad(owner);
            owner.AddComponent<NetworkLocalProbe>()._probe = probe;
        }

        private void Update()
        {
            try
            {
                _seconds += Time.unscaledDeltaTime;
                _probe.Tick(Time.unscaledDeltaTime * 1000);
                if (_probe.Passed)
                    Destroy(gameObject);
                else if (_seconds >= 15)
                    throw new TimeoutException("P1 protocol probe timed out after 15 seconds.");
            }
            catch (Exception error)
            {
                Debug.LogError($"[FRFrame.Network] {error}");
                Destroy(gameObject);
            }
        }

        private void OnDestroy() => _probe?.Dispose();
    }
}
