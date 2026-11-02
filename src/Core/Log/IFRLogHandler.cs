namespace FRFrame
{
    public interface IFRLogHandler
    {
        void Handle(in FRLogEntry entry);
        void Flush();
        void Shutdown();
    }
}
