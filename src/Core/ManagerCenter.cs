using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 所有框架模块的入口
    /// </summary>
    public static class FRFM
    {
        public static FRLogManager Log => ManagerCenter.Instance.GetModule<FRLogManager>();
        public static EventManager Event => ManagerCenter.Instance.GetModule<EventManager>();
        public static PoolManager Pool => ManagerCenter.Instance.GetModule<PoolManager>();
        public static FsmManager Fsm => ManagerCenter.Instance.GetModule<FsmManager>();
        public static ResourceManager Resource => ManagerCenter.Instance.GetModule<ResourceManager>();
        public static AudioManager Audio => ManagerCenter.Instance.GetModule<AudioManager>();
        public static SaveManager Save => ManagerCenter.Instance.GetModule<SaveManager>();
        public static UIManager UI => ManagerCenter.Instance.GetModule<UIManager>();


        /// <summary>
        /// 注册所有模块
        /// </summary>
        public static void RegisterAllManagers()
        {
            ManagerCenter.Instance.Register(new FRLogManager());
            ManagerCenter.Instance.Register(new EventManager());
            ManagerCenter.Instance.Register(new PoolManager());
            ManagerCenter.Instance.Register(new FsmManager());
            ManagerCenter.Instance.Register(new ResourceManager());
            ManagerCenter.Instance.Register(new AudioManager());
            ManagerCenter.Instance.Register(new SaveManager());
            ManagerCenter.Instance.Register(new UIManager());
        }
        
        /// <summary>
        /// 异步初始化
        /// </summary>
        public static async UniTask InitializeAsync()
        {
            await ManagerCenter.Instance.InitializeAsync();
        }
        
        /// <summary>
        /// 等待某个模块初始化完成
        /// </summary>
        public static async UniTask WaitReady<T>() where T : IManagerBase
        {
            await ManagerCenter.Instance.WaitReady<T>();
        }
        
        /// <summary>
        /// 更新框架。
        /// 建议由一个 MonoBehaviour 驱动。
        /// </summary>
        public static void Update(float deltaTime, float unscaledDeltaTime)
        {
            ManagerCenter.Instance.UpdateAll(deltaTime, unscaledDeltaTime);
        }
        
        /// <summary>
        /// 销毁框架。
        /// </summary>
        public static void Shutdown()
        {
            ManagerCenter.Instance.DestroyAll();
        }
    }

    internal class ManagerCenter : Singleton<ManagerCenter>
    {
        /// <summary>
        /// 存储所有注册的模块实例，Type -> IManagerBase
        /// </summary>
        private readonly Dictionary<Type, IManagerBase> _registeredManagers = new Dictionary<Type, IManagerBase>();
        private List<IManagerBase> _orderedManagers = new List<IManagerBase>();
        
        /// <summary>
        /// 存储每个模块的就绪信号源，当模块初始化完成后，触发此信号，唤醒所有等待该模块的代码
        /// </summary>
        private readonly Dictionary<Type, UniTaskCompletionSource> _managerReadySignals = new Dictionary<Type, UniTaskCompletionSource>();

        /// <summary>
        /// 是否初始化完成
        /// </summary>
        private bool _initialized = false;
        private bool _initializing;
        private int _generation;
        
        /// <summary>
        /// 注册模块
        /// </summary>
        public void Register<T>(T manager) where T : IManagerBase
        {
            Type type = typeof(T);
            if (_registeredManagers.ContainsKey(type))
            {
                Debug.LogWarning($"模块 {type.Name} 已被注册，忽略重复注册。");
                return;
            }

            _registeredManagers[type] = manager;
            // 为每个模块创建一个“未完成”的信号源
            _managerReadySignals[type] = new UniTaskCompletionSource();
        }
        
        /// <summary>
        /// 启动框架：按优先级排序并依次初始化所有模块
        /// </summary>
        public async UniTask InitializeAsync()
        {
            if (_initialized) return;
            if (_initializing) throw new InvalidOperationException("Framework initialization is already running.");
            _initializing = true;
            int generation = ++_generation;
            Debug.Log("=== 框架开始初始化 ===");

            // 【核心逻辑】根据 Priority 对模块进行升序排序
            // 确保 ResourceManager (Priority 10) 在 UIManager (Priority 20) 之前运行
            List<IManagerBase> sortedManagers = _registeredManagers.Values.OrderBy(m => m.Priority).ToList();
            _orderedManagers = new List<IManagerBase>();
            try
            {
                foreach (IManagerBase manager in sortedManagers)
                {
                    if (generation != _generation) return;
                    var ready = _managerReadySignals[manager.GetType()];
                    _orderedManagers.Add(manager); // Shutdown cleans attempted modules, including partial initialization.
                    try
                    {
                        Debug.Log($"正在初始化: {manager.GetType().Name} (优先级: {manager.Priority})");
                        await manager.InitializeAsync();
                        if (generation != _generation) return;
                    
                        // 初始化成功后，触发信号，唤醒所有 await WaitReady<T> 的协程
                        ready.TrySetResult();
                        Debug.Log($"[OK] {manager.GetType().Name} 初始化完成");
                    }
                    catch (Exception e)
                    {
                        if (generation != _generation) return;
                        ready.TrySetException(e);
                        Debug.LogError($"[CRITICAL] 模块 {manager.GetType().Name} 初始化失败: {e}");
                        // Dependent modules receive this failure from WaitReady; independent modules may continue.
                    }
                }
    
                _initialized = true;
                Debug.Log("=== 框架初始化阶段结束；模块可用状态以 WaitReady 为准 ===");
            }
            finally { if (generation == _generation) _initializing = false; }
        }
        
        /// <summary>
        /// 异步等待指定模块就绪
        /// 用法: await FRFM.WaitReady&lt;ResourceManager&gt;();
        /// 初始化失败传播原异常；Shutdown取消尚未就绪的等待，不把失败当成就绪。
        /// </summary>
        public UniTask WaitReady<T>() where T : IManagerBase
        {
            Type type = typeof(T);
            if (!_managerReadySignals.ContainsKey(type))
            {
                return UniTask.FromException(new InvalidOperationException($"未注册的模块: {type.Name}"));
            }

            // 如果模块已经初始化完成，Task.IsCompleted 为 true，await 会立即返回，无性能损耗
            // 如果未完成，代码会挂起，直到 InitializeAsync 触发 TrySetResult
            return _managerReadySignals[type].Task;
        }
        
        /// <summary>
        /// 获取模块实例 (同步)
        /// 注意：请确保在调用前模块已初始化，或者配合 WaitReady 使用
        /// </summary>
        public T GetModule<T>() where T : IManagerBase
        {
            Type type = typeof(T);
            if (_registeredManagers.TryGetValue(type, out IManagerBase module))
            {
                return (T)module;
            }
            Debug.LogError($"模块 {type.Name} 未找到");
            return default;
        }
        
        /// <summary>
        /// 更新全部管理器。
        /// </summary>
        public void UpdateAll(float deltaTime, float unscaledDeltaTime)
        {
            if (!_initialized) return;
            for (int i = 0; i < _orderedManagers.Count; i++)
            {
                var manager = _orderedManagers[i];
                if (_managerReadySignals.TryGetValue(manager.GetType(), out var ready) && ready.Task.Status == UniTaskStatus.Succeeded)
                    manager.Update(deltaTime, unscaledDeltaTime);
            }
        }
        
        /// <summary>
        /// 销毁全部管理器。
        /// 取消就绪等待并隔离旧初始化轮次；不能代替模块自身取消其异步资源加载。
        /// </summary>
        public void DestroyAll()
        {
            ++_generation;
            _initialized = _initializing = false;
            foreach (var ready in _managerReadySignals.Values) ready.TrySetCanceled();
            for (int i = _orderedManagers.Count - 1; i >= 0; i--)
            {
                try { _orderedManagers[i].Destroy(); }
                catch (Exception error) { Debug.LogError($"模块 {_orderedManagers[i].GetType().Name} 销毁失败: {error}"); }
            }
            _registeredManagers.Clear();
            _orderedManagers.Clear();
            _managerReadySignals.Clear();
        }
    }
    
}
