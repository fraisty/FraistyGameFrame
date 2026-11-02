namespace FRFrame
{
    public sealed class SaveScopeOptions
    {
        public string PackageName;
        public string UserId;
        public string SlotId;
        public SaveDomain DefaultDomain = SaveDomain.Slot;
    }
}
