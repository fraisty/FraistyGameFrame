using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public sealed class SaveManager : IManagerBase
    {
        private sealed class SaveCacheEntry
        {
            public object Value;
            public Type ValueType;
            public bool Dirty;
        }

        private readonly Dictionary<SaveKey, SaveCacheEntry> _cache = new Dictionary<SaveKey, SaveCacheEntry>();
        private readonly Dictionary<string, SaveScope> _scopes = new Dictionary<string, SaveScope>();
        private ISaveSerializer _serializer;
        private SaveFileStorage _storage;
        private int _saveFailureCount;
        private int _loadFailureCount;
        private int _backupRestoreCount;

        public override int Priority => 30;
        public SaveEvents Events { get; } = new SaveEvents();
        public string RootPath => _storage?.RootPath;

        public override UniTask InitializeAsync()
        {
            _serializer = new NewtonsoftJsonSaveSerializer();
            _storage = new SaveFileStorage(_serializer);
            FRLog.Info("SaveManager initialized.");
            return UniTask.CompletedTask;
        }

        public override void Destroy()
        {
            foreach (SaveScope scope in new List<SaveScope>(_scopes.Values))
                scope.Dispose();
            _scopes.Clear();
            Flush();
            _cache.Clear();
            FRLog.Info("SaveManager destroyed.");
        }

        public SaveScope CreateScope(string name, SaveScopeOptions options = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                FRLog.Error("Create SaveScope failed, name is null or empty.");
                return null;
            }

            options ??= new SaveScopeOptions();
            if (_scopes.TryGetValue(name, out SaveScope existing))
            {
                if (existing.PackageName != (options.PackageName ?? string.Empty) || existing.UserId != (options.UserId ?? string.Empty) || existing.SlotId != (options.SlotId ?? string.Empty))
                    FRLog.Warning($"Create SaveScope got existing scope with different context. name:{name}");
                return existing;
            }

            SaveScope scope = new SaveScope(this, name, options);
            _scopes.Add(name, scope);
            return scope;
        }

        public async UniTask SaveAsync<T>(SaveKey key, T data, SaveWriteOptions options = null)
        {
            options ??= new SaveWriteOptions();
            if (!ValidateKey(key))
                return;

            Events.RaiseSaveStarted(key);
            try
            {
                byte[] bytes = _serializer.Serialize(data);
                await _storage.WriteAsync(key, bytes, options.Backup);
                if (!options.SkipCache)
                    SetCache(key, data, typeof(T), false);
                Events.RaiseSaveCompleted(key);
            }
            catch (Exception e)
            {
                _saveFailureCount++;
                FRLog.Error($"SaveAsync failed : {key}, {e}");
                Events.RaiseSaveFailed(key, e.ToString());
            }
        }

        public async UniTask<SaveLoadResult<T>> LoadAsync<T>(SaveKey key, SaveLoadOptions options = null)
        {
            options ??= new SaveLoadOptions();
            if (!ValidateKey(key))
                return new SaveLoadResult<T>(false, default, error: "Invalid key");

            if (!options.IgnoreCache && _cache.TryGetValue(key, out SaveCacheEntry entry) && entry.Value is T cached)
                return new SaveLoadResult<T>(true, cached, true);

            Events.RaiseLoadStarted(key);
            try
            {
                var result = await _storage.ReadAsync(key, options.AllowBackup);
                if (!result.success)
                {
                    _loadFailureCount++;
                    Events.RaiseLoadFailed(key, result.error);
                    return new SaveLoadResult<T>(false, default, error: result.error);
                }

                T data = _serializer.Deserialize<T>(result.bytes);
                SetCache(key, data, typeof(T), false);
                if (result.fromBackup)
                    _backupRestoreCount++;
                Events.RaiseLoadCompleted(key);
                return new SaveLoadResult<T>(true, data, false, result.fromBackup);
            }
            catch (Exception e)
            {
                _loadFailureCount++;
                FRLog.Error($"LoadAsync failed : {key}, {e}");
                Events.RaiseLoadFailed(key, e.ToString());
                return new SaveLoadResult<T>(false, default, error: e.ToString());
            }
        }

        public async UniTask<T> LoadOrDefaultAsync<T>(SaveKey key, T defaultValue = default, SaveLoadOptions options = null)
        {
            SaveLoadResult<T> result = await LoadAsync<T>(key, options);
            return result.Success ? result.Data : defaultValue;
        }

        public UniTask<bool> ExistsAsync(SaveKey key)
        {
            return _storage.ExistsAsync(key);
        }

        public async UniTask<bool> DeleteAsync(SaveKey key)
        {
            bool deleted = await _storage.DeleteAsync(key);
            if (_cache.Remove(key))
                Events.RaiseDirtyChanged(key, false);
            return deleted;
        }

        public UniTask SaveSettingAsync<T>(string name, T data)
        {
            return SaveAsync(new SaveKey(SaveDomain.GlobalSettings, name), data);
        }

        public UniTask<T> LoadSettingAsync<T>(string name, T defaultValue = default)
        {
            return LoadOrDefaultAsync(new SaveKey(SaveDomain.GlobalSettings, name), defaultValue);
        }

        public UniTask SaveUserAsync<T>(string userId, string name, T data, string packageName = null)
        {
            return SaveAsync(new SaveKey(SaveDomain.User, name, packageName, userId), data);
        }

        public UniTask<T> LoadUserAsync<T>(string userId, string name, T defaultValue = default, string packageName = null)
        {
            return LoadOrDefaultAsync(new SaveKey(SaveDomain.User, name, packageName, userId), defaultValue);
        }

        public void SetDirty<T>(SaveKey key, T data)
        {
            if (!ValidateKey(key))
                return;
            SetCache(key, data, typeof(T), true);
            Events.RaiseDirtyChanged(key, true);
        }

        public async UniTask FlushAsync()
        {
            List<KeyValuePair<SaveKey, SaveCacheEntry>> dirtyEntries = CollectDirtyEntries();
            for (int i = 0; i < dirtyEntries.Count; i++)
            {
                SaveKey key = dirtyEntries[i].Key;
                SaveCacheEntry entry = dirtyEntries[i].Value;
                bool success = await SaveObjectAsync(key, entry.Value, entry.ValueType);
                if (success)
                {
                    entry.Dirty = false;
                    Events.RaiseDirtyChanged(key, false);
                }
            }
        }

        public void Flush()
        {
            List<KeyValuePair<SaveKey, SaveCacheEntry>> dirtyEntries = CollectDirtyEntries();
            for (int i = 0; i < dirtyEntries.Count; i++)
            {
                SaveKey key = dirtyEntries[i].Key;
                SaveCacheEntry entry = dirtyEntries[i].Value;
                bool success = SaveObject(key, entry.Value, entry.ValueType);
                if (success)
                {
                    entry.Dirty = false;
                    Events.RaiseDirtyChanged(key, false);
                }
            }
        }

        public async UniTask<SaveSlotInfo> CreateSlotAsync(string packageName, string userId, string displayName = null)
        {
            string slotId = Guid.NewGuid().ToString("N");
            DateTime now = DateTime.UtcNow;
            SaveSlotInfo slot = new SaveSlotInfo
            {
                SlotId = slotId,
                DisplayName = string.IsNullOrEmpty(displayName) ? slotId : displayName,
                PackageName = packageName ?? string.Empty,
                UserId = userId ?? string.Empty,
                AppVersion = Application.version,
                DataVersion = 1,
                CreateTime = now,
                UpdateTime = now
            };

            await SaveSlotMetaAsync(slot);
            Events.RaiseSlotCreated(slot);
            return slot;
        }

        public async UniTask<IReadOnlyList<SaveSlotInfo>> GetSlotsAsync(string packageName, string userId)
        {
            List<SaveSlotInfo> result = new List<SaveSlotInfo>();
            string root = _storage.GetSlotRootDirectory(packageName, userId);
            if (!Directory.Exists(root))
                return result;

            string[] directories = await UniTask.RunOnThreadPool(() => Directory.GetDirectories(root));
            for (int i = 0; i < directories.Length; i++)
            {
                string slotId = Path.GetFileName(directories[i]);
                SaveKey key = _storage.CreateSlotMetaKey(packageName, userId, slotId);
                SaveLoadResult<SaveSlotInfo> loaded = await LoadAsync<SaveSlotInfo>(key);
                if (loaded.Success && loaded.Data != null)
                    result.Add(loaded.Data);
            }

            result.Sort((left, right) => right.UpdateTime.CompareTo(left.UpdateTime));
            return result;
        }

        public async UniTask<bool> DeleteSlotAsync(string packageName, string userId, string slotId)
        {
            SaveKey metaKey = _storage.CreateSlotMetaKey(packageName, userId, slotId);
            SaveSlotInfo slot = await LoadOrDefaultAsync<SaveSlotInfo>(metaKey);
            string directory = _storage.GetSlotDirectory(packageName, userId, slotId);
            await _storage.DeleteDirectoryAsync(directory);
            RemoveCacheForSlot(packageName, userId, slotId);
            if (slot != null)
                Events.RaiseSlotDeleted(slot);
            return slot != null;
        }

        public async UniTask<bool> CopySlotAsync(string packageName, string userId, string sourceSlotId, string targetSlotId)
        {
            string source = _storage.GetSlotDirectory(packageName, userId, sourceSlotId);
            if (!Directory.Exists(source))
                return false;

            string target = _storage.GetSlotDirectory(packageName, userId, targetSlotId);
            await _storage.CopyDirectoryAsync(source, target);

            SaveKey targetMetaKey = _storage.CreateSlotMetaKey(packageName, userId, targetSlotId);
            SaveSlotInfo meta = await LoadOrDefaultAsync<SaveSlotInfo>(targetMetaKey, null, new SaveLoadOptions { IgnoreCache = true });
            if (meta != null)
            {
                meta.SlotId = targetSlotId;
                meta.UpdateTime = DateTime.UtcNow;
                await SaveSlotMetaAsync(meta);
            }
            return true;
        }

        public SaveDiagnosticsSnapshot CreateSnapshot()
        {
            SaveDiagnosticsSnapshot snapshot = new SaveDiagnosticsSnapshot
            {
                RootPath = RootPath,
                CacheCount = _cache.Count,
                SaveFailureCount = _saveFailureCount,
                LoadFailureCount = _loadFailureCount,
                BackupRestoreCount = _backupRestoreCount
            };

            foreach (KeyValuePair<SaveKey, SaveCacheEntry> pair in _cache)
            {
                if (!pair.Value.Dirty)
                    continue;
                snapshot.DirtyCount++;
                snapshot.DirtyItems.Add(new SaveDirtySnapshot
                {
                    Key = pair.Key,
                    TypeName = pair.Value.ValueType?.FullName ?? string.Empty
                });
            }
            return snapshot;
        }

        internal void RemoveScope(SaveScope scope)
        {
            if (scope != null && _scopes.TryGetValue(scope.Name, out SaveScope existing) && existing == scope)
                _scopes.Remove(scope.Name);
        }

        private async UniTask<bool> SaveObjectAsync(SaveKey key, object value, Type type)
        {
            Events.RaiseSaveStarted(key);
            try
            {
                byte[] bytes = SerializeObject(value, type);
                await _storage.WriteAsync(key, bytes, true);
                Events.RaiseSaveCompleted(key);
                return true;
            }
            catch (Exception e)
            {
                _saveFailureCount++;
                FRLog.Error($"SaveObjectAsync failed : {key}, {e}");
                Events.RaiseSaveFailed(key, e.ToString());
                return false;
            }
        }

        private bool SaveObject(SaveKey key, object value, Type type)
        {
            Events.RaiseSaveStarted(key);
            try
            {
                byte[] bytes = SerializeObject(value, type);
                _storage.Write(key, bytes, true);
                Events.RaiseSaveCompleted(key);
                return true;
            }
            catch (Exception e)
            {
                _saveFailureCount++;
                FRLog.Error($"SaveObject failed : {key}, {e}");
                Events.RaiseSaveFailed(key, e.ToString());
                return false;
            }
        }

        private byte[] SerializeObject(object value, Type type)
        {
            return _serializer.Serialize(value);
        }

        private List<KeyValuePair<SaveKey, SaveCacheEntry>> CollectDirtyEntries()
        {
            List<KeyValuePair<SaveKey, SaveCacheEntry>> dirtyEntries = new List<KeyValuePair<SaveKey, SaveCacheEntry>>();
            foreach (KeyValuePair<SaveKey, SaveCacheEntry> pair in _cache)
            {
                if (pair.Value.Dirty)
                    dirtyEntries.Add(pair);
            }
            return dirtyEntries;
        }

        private UniTask SaveSlotMetaAsync(SaveSlotInfo slot)
        {
            SaveKey key = _storage.CreateSlotMetaKey(slot.PackageName, slot.UserId, slot.SlotId);
            return SaveAsync(key, slot);
        }

        private void SetCache(SaveKey key, object value, Type valueType, bool dirty)
        {
            _cache[key] = new SaveCacheEntry
            {
                Value = value,
                ValueType = valueType,
                Dirty = dirty
            };
        }

        private void RemoveCacheForSlot(string packageName, string userId, string slotId)
        {
            List<SaveKey> removeKeys = new List<SaveKey>();
            foreach (SaveKey key in _cache.Keys)
            {
                if (key.Domain == SaveDomain.Slot && key.PackageName == (packageName ?? string.Empty) && key.UserId == (userId ?? string.Empty) && key.SlotId == (slotId ?? string.Empty))
                    removeKeys.Add(key);
            }
            for (int i = 0; i < removeKeys.Count; i++)
                _cache.Remove(removeKeys[i]);
        }

        private bool ValidateKey(SaveKey key)
        {
            if (string.IsNullOrEmpty(key.DataName))
            {
                FRLog.Error("Save key data name is null or empty.");
                return false;
            }

            if (key.Domain == SaveDomain.Slot && (string.IsNullOrEmpty(key.PackageName) || string.IsNullOrEmpty(key.UserId) || string.IsNullOrEmpty(key.SlotId)))
            {
                FRLog.Error($"Slot save key needs packageName, userId and slotId : {key}");
                return false;
            }
            return true;
        }
    }
}
