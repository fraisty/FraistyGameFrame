namespace FRFrame.Network.Shared
{
    /// <summary>Identifies the protocol revision used by the client and server.</summary>
    public readonly struct ProtocolVersion
    {
        private ProtocolVersion(int value)
        {
            Value = value;
        }

        /// <summary>Gets the revision identifier.</summary>
        public int Value { get; }

        /// <summary>Gets the protocol revision implemented by this build.</summary>
        public static ProtocolVersion Current => new ProtocolVersion(1);
    }
}
