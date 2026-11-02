namespace FRFrame
{
    public abstract class UIItem : UIWidget
    {
        public int Index { get; private set; } = -1;
        public long Key { get; private set; }
        public string TemplateId { get; private set; }
        public object Data { get; private set; }
        public bool IsSelected { get; private set; }

        public void RefreshItem(int index, object data)
        {
            SetItemData(index, data);
        }

        public void SetItemData(int index, object data)
        {
            Index = index;
            Data = data;
            OnSetData(index, data);
            OnRefreshItem(index, data);
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected)
                return;

            IsSelected = selected;
            OnSelectedChanged(selected);
        }

        internal void InternalItemVisible(int index, object data)
        {
            InternalItemVisible(index, index, null, data);
        }

        internal void InternalItemVisible(int index, long key, string templateId, object data)
        {
            Key = key;
            TemplateId = templateId;
            SetItemData(index, data);
            OnItemVisible(index, data);
        }

        internal void InternalSetIndex(int index)
        {
            Index = index;
        }

        internal void InternalItemRecycled()
        {
            int oldIndex = Index;
            object oldData = Data;
            OnItemRecycled(oldIndex, oldData);
            SetSelected(false);
            Index = -1;
            Data = null;
            ClearTypedData();
            Key = 0L;
            TemplateId = null;
        }

        protected virtual void OnSetData(int index, object data) { }
        protected virtual void OnSelectedChanged(bool selected) { }
        protected virtual void OnRefreshItem(int index, object data) { }
        protected virtual void OnItemVisible(int index, object data) { }
        protected virtual void OnItemRecycled(int index, object data) { }
        protected virtual void ClearTypedData() { }
    }

    public abstract class UIItem<TData> : UIItem
    {
        public TData TypedData { get; private set; }

        protected sealed override void OnSetData(int index, object data)
        {
            if (data is TData typedData)
            {
                TypedData = typedData;
                OnSetData(index, typedData);
            }
            else
            {
                TypedData = default;
                OnSetData(index, default);
            }
        }

        protected sealed override void OnItemVisible(int index, object data)
        {
            OnItemVisible(index, TypedData);
        }

        protected sealed override void OnItemRecycled(int index, object data)
        {
            OnItemRecycled(index, TypedData);
        }

        protected sealed override void ClearTypedData()
        {
            TypedData = default;
        }

        protected abstract void OnSetData(int index, TData data);
        protected virtual void OnItemVisible(int index, TData data) { }
        protected virtual void OnItemRecycled(int index, TData data) { }
    }
}
