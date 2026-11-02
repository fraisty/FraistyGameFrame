using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UINavEntry
    {
        public string FormId;
        public UIForm Form;
        public object UserData;
        public UIGroupLayer Layer;
        public UINavClosePolicy ClosePolicy;
    }
}
