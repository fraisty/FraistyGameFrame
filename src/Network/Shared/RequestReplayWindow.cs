namespace FRFrame.Network.Shared
{
    /// <summary>Local configuration contract for the multiplayer Host and client. It is independent of the wire format.</summary>
    public static class RequestReplayWindow
    {
        public const int MinimumHostMilliseconds = 60_000;

        // Reserve nominal transport margin. Deduplication only holds while the server retains the result.
        public const int ClientRetryMilliseconds = MinimumHostMilliseconds - 5_000;
    }
}
