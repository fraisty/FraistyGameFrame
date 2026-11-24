using FRFrame.Network.Client;
using UnityEngine;
using UnityEngine.Scripting;

// The startup callback has no scene references.
[assembly: AlwaysLinkAssembly]

namespace FRFrame.Network.Unity
{
    internal static class NetworkStartupDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void LogProtocolVersion()
        {
            Debug.Log($"[FRFrame.Network] Client protocol version {ClientProtocol.Version.Value}.");
        }
    }
}
