using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class FRLogManager : IManagerBase
    {
        public override int Priority => -10000;

        public override UniTask InitializeAsync()
        {
            FRLogger.Initialize(FRLogger.CreateDefaultConfig());
            return UniTask.CompletedTask;
        }

        public void Flush()
        {
            FRLogger.Flush();
        }

        public override void Destroy()
        {
            FRLogger.Shutdown();
        }
    }
}
