using Cysharp.Threading.Tasks;

namespace FRFrame
{
    /// <summary>
    /// 框架管理器接口。
    /// 所有 Manager 都可以统一注册到 ManagerCenter。
    /// </summary>
    public abstract class IManagerBase
    {
        /// <summary>
        /// 优先级，数字越小越先初始化。
        /// </summary>
        public virtual int Priority => 0;
        
        public abstract UniTask InitializeAsync();
        
        public virtual void Update(float deltaTime, float unscaledDeltaTime)
        {
            
        }
        
        public virtual void Dispose()
        {
            
        }

        public virtual void Destroy()
        {
            
        }
    }
}
