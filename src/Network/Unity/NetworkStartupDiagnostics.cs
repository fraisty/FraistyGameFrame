using FRFrame.Network.Client;
using UnityEngine;

namespace FRFrame.Network.Unity
{
    internal static class NetworkStartupDiagnostics
    {
        internal static void LogProtocolVersion()
        {
            Debug.Log($"[FRFrame.Network] Client protocol version {ClientProtocol.Version.Value}.");
        }
    }
}
