using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public class FsmManager : IManagerBase
    {
        public override int Priority => 2;

        private sealed class FsmRuntime
        {
            public FsmController Fsm;
            public FsmCreateOption Option;
        }

        private readonly List<FsmRuntime> _runtimes = new List<FsmRuntime>(16);
        private readonly FsmManagerStatistics _statistics = new FsmManagerStatistics();

        public int Count => _runtimes.Count;

        public override UniTask InitializeAsync()
        {
            return UniTask.CompletedTask;
        }

        public T CreateFsm<T>(FsmCreateOption option = null) where T : FsmController, new()
        {
            T fsm = new T();
            SetupFsm(fsm, option);
            return fsm;
        }

        public FsmController CreateFsm(Type type, FsmCreateOption option = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!typeof(FsmController).IsAssignableFrom(type))
                throw new Exception($"{type.FullName} 不是 FsmController 类型");

            FsmController fsm = (FsmController)Activator.CreateInstance(type);
            SetupFsm(fsm, option);
            return fsm;
        }

        public bool RemoveFsm(FsmController fsm)
        {
            if (fsm == null) return false;

            for (int i = _runtimes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_runtimes[i].Fsm, fsm))
                {
                    if (fsm.EnableLog)
                        FRLog.InfoT("FSM", $"[FSMManager] RemoveFsm => {fsm.Name}");

                    _runtimes[i].Fsm.Dispose();
                    _runtimes.RemoveAt(i);
                    _statistics.TotalDisposed++;
                    return true;
                }
            }

            return false;
        }

        public int RemoveByOwner(object owner)
        {
            if (owner == null) return 0;

            int count = 0;
            for (int i = _runtimes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_runtimes[i].Fsm.Owner, owner))
                {
                    var fsm = _runtimes[i].Fsm;
                    if (fsm.EnableLog)
                        FRLog.InfoT("FSM", $"[FSMManager] RemoveByOwner => {fsm.Name}");

                    fsm.Dispose();
                    _runtimes.RemoveAt(i);
                    _statistics.TotalDisposed++;
                    count++;
                }
            }

            return count;
        }

        public int RemoveByType<T>() where T : FsmController
        {
            int count = 0;
            for (int i = _runtimes.Count - 1; i >= 0; i--)
            {
                if (_runtimes[i].Fsm is T)
                {
                    var fsm = _runtimes[i].Fsm;
                    if (fsm.EnableLog)
                        FRLog.InfoT("FSM", $"[FSMManager] RemoveByType => {fsm.Name}");

                    fsm.Dispose();
                    _runtimes.RemoveAt(i);
                    _statistics.TotalDisposed++;
                    count++;
                }
            }

            return count;
        }

        public T GetFirstFsm<T>() where T : FsmController
        {
            for (int i = 0; i < _runtimes.Count; i++)
                if (_runtimes[i].Fsm is T t) return t;
            return null;
        }

        public List<T> GetFsms<T>() where T : FsmController
        {
            var result = new List<T>();
            GetFsms(result);
            return result;
        }

        public void GetFsms<T>(List<T> results) where T : FsmController
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _runtimes.Count; i++)
                if (_runtimes[i].Fsm is T t) results.Add(t);
        }

        public int FillFsms<T>(IList<T> results) where T : FsmController
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _runtimes.Count; i++)
                if (_runtimes[i].Fsm is T t) results.Add(t);
            return results.Count;
        }

        public List<FsmDebugInfo> GetAllDebugInfo()
        {
            var result = new List<FsmDebugInfo>(_runtimes.Count);
            GetAllDebugInfo(result);
            return result;
        }

        public void GetAllDebugInfo(List<FsmDebugInfo> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _runtimes.Count; i++)
            {
                var fsm = _runtimes[i].Fsm;
                if (fsm != null)
                    results.Add(fsm.GetDebugInfo());
            }
        }

        public FsmManagerStatistics GetStatistics()
        {
            int taskCount = 0;
            for (int i = 0; i < _runtimes.Count; i++)
                if (_runtimes[i].Fsm is FsmTaskController) taskCount++;

            return new FsmManagerStatistics
            {
                ActiveCount = _runtimes.Count,
                TotalCreated = _statistics.TotalCreated,
                TotalDisposed = _statistics.TotalDisposed,
                TotalAutoRemoved = _statistics.TotalAutoRemoved,
                TaskFsmCount = taskCount
            };
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = _runtimes.Count - 1; i >= 0; i--)
            {
                var runtime = _runtimes[i];
                var fsm = runtime.Fsm;

                if (fsm == null || fsm.IsDisposed)
                {
                    _runtimes.RemoveAt(i);
                    continue;
                }

                fsm.Update(deltaTime, unscaledDeltaTime);

                if (runtime.Option.AutoRemoveWhenTaskDone && fsm is FsmTaskController taskFsm && taskFsm.IsDone)
                {
                    if (fsm.EnableLog)
                        FRLog.InfoT("FSM", $"[FSMManager] AutoRemove TaskFsm => {fsm.Name}, TaskState: {taskFsm.TaskState}");

                    taskFsm.Dispose();
                    _runtimes.RemoveAt(i);
                    _statistics.TotalAutoRemoved++;
                    _statistics.TotalDisposed++;
                }
            }
        }

        public override void Dispose()
        {
            for (int i = _runtimes.Count - 1; i >= 0; i--)
            {
                if (_runtimes[i].Fsm != null && !_runtimes[i].Fsm.IsDisposed)
                    _statistics.TotalDisposed++;
                _runtimes[i].Fsm?.Dispose();
            }

            _runtimes.Clear();
        }

        private void SetupFsm(FsmController fsm, FsmCreateOption option)
        {
            option ??= new FsmCreateOption();
            fsm.Owner = option.Owner;
            fsm.EnableLog = option.EnableLog;
            fsm.Initialize();
            _runtimes.Add(new FsmRuntime { Fsm = fsm, Option = option });
            _statistics.TotalCreated++;

            if (fsm.EnableLog)
                FRLog.InfoT("FSM", $"[FSMManager] CreateFsm => {fsm.Name}, Owner: {fsm.Owner?.GetType().Name ?? "None"}");
        }
    }
}
