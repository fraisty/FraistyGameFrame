using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class SaveScope : System.IDisposable
    {
        private readonly SaveManager _manager;
        private readonly SaveDomain _defaultDomain;

        internal SaveScope(SaveManager manager, string name, SaveScopeOptions options)
        {
            _manager = manager;
            Name = name;
            PackageName = options.PackageName ?? string.Empty;
            UserId = options.UserId ?? string.Empty;
            SlotId = options.SlotId ?? string.Empty;
            _defaultDomain = options.DefaultDomain;
        }

        public string Name { get; }
        public string PackageName { get; }
        public string UserId { get; }
        public string SlotId { get; }
        public bool IsDisposed { get; private set; }

        public UniTask SaveAsync<T>(string dataName, T data, SaveDomain? domain = null)
        {
            if (IsDisposed)
            {
                FRLog.Error($"SaveScope is disposed : {Name}");
                return UniTask.CompletedTask;
            }
            return _manager.SaveAsync(CreateKey(dataName, domain), data);
        }

        public UniTask<T> LoadAsync<T>(string dataName, T defaultValue = default, SaveDomain? domain = null)
        {
            if (IsDisposed)
            {
                FRLog.Error($"SaveScope is disposed : {Name}");
                return UniTask.FromResult(defaultValue);
            }
            return _manager.LoadOrDefaultAsync(CreateKey(dataName, domain), defaultValue);
        }

        public void SetDirty<T>(string dataName, T data, SaveDomain? domain = null)
        {
            if (IsDisposed)
            {
                FRLog.Error($"SaveScope is disposed : {Name}");
                return;
            }
            _manager.SetDirty(CreateKey(dataName, domain), data);
        }

        public UniTask FlushAsync()
        {
            return _manager.FlushAsync();
        }

        public UniTask<bool> DeleteAsync(string dataName, SaveDomain? domain = null)
        {
            if (IsDisposed)
            {
                FRLog.Error($"SaveScope is disposed : {Name}");
                return UniTask.FromResult(false);
            }
            return _manager.DeleteAsync(CreateKey(dataName, domain));
        }

        public SaveKey CreateKey(string dataName, SaveDomain? domain = null)
        {
            SaveDomain targetDomain = domain ?? _defaultDomain;
            return new SaveKey(targetDomain, dataName, PackageName, UserId, SlotId);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            _manager.RemoveScope(this);
        }
    }
}
