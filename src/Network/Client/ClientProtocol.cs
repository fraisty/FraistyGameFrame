using FRFrame.Network.Shared;

namespace FRFrame.Network.Client
{
    /// <summary>Exposes the protocol revision implemented by the client.</summary>
    public static class ClientProtocol
    {
        /// <summary>Gets the protocol revision for client startup diagnostics.</summary>
        public static ProtocolVersion Version => ProtocolVersion.Current;
    }
}
