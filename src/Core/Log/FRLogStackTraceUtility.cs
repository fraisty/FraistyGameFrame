using System.Diagnostics;

namespace FRFrame
{
    public static class FRLogStackTraceUtility
    {
        public static string Capture(int skipFrames = 3)
        {
            return new StackTrace(skipFrames, true).ToString();
        }
    }
}
