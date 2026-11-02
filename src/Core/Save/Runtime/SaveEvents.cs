using System;

namespace FRFrame
{
    public sealed class SaveEvents
    {
        public event Action<SaveKey> SaveStarted;
        public event Action<SaveKey> SaveCompleted;
        public event Action<SaveKey, string> SaveFailed;
        public event Action<SaveKey> LoadStarted;
        public event Action<SaveKey> LoadCompleted;
        public event Action<SaveKey, string> LoadFailed;
        public event Action<SaveSlotInfo> SlotCreated;
        public event Action<SaveSlotInfo> SlotDeleted;
        public event Action<SaveKey, bool> DirtyChanged;

        internal void RaiseSaveStarted(SaveKey key) { SaveStarted?.Invoke(key); }
        internal void RaiseSaveCompleted(SaveKey key) { SaveCompleted?.Invoke(key); }
        internal void RaiseSaveFailed(SaveKey key, string error) { SaveFailed?.Invoke(key, error); }
        internal void RaiseLoadStarted(SaveKey key) { LoadStarted?.Invoke(key); }
        internal void RaiseLoadCompleted(SaveKey key) { LoadCompleted?.Invoke(key); }
        internal void RaiseLoadFailed(SaveKey key, string error) { LoadFailed?.Invoke(key, error); }
        internal void RaiseSlotCreated(SaveSlotInfo slot) { SlotCreated?.Invoke(slot); }
        internal void RaiseSlotDeleted(SaveSlotInfo slot) { SlotDeleted?.Invoke(slot); }
        internal void RaiseDirtyChanged(SaveKey key, bool dirty) { DirtyChanged?.Invoke(key, dirty); }
    }
}
