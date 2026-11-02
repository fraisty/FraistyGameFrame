using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 单个UI层组。
    /// 负责排序、遮挡、暂停。
    /// </summary>
    public sealed class UIGroup
    {
        public UIGroupLayer Layer { get; }
        public string Name { get; }
        public Transform Root { get; }
        public int SortingOrderBase { get; }

        private readonly List<UIForm> _forms = new List<UIForm>();

        public UIGroup(UIGroupLayer layer, string name, Transform root, int sortingOrderBase)
        {
            Layer = layer;
            Name = name;
            Root = root;
            SortingOrderBase = sortingOrderBase;
        }

        public IReadOnlyList<UIForm> Forms => _forms;

        public void Add(UIForm form)
        {
            if (form == null)
                return;

            if (!_forms.Contains(form))
            {
                _forms.Add(form);
            }

            RefreshDepth();
            RefreshCoverState();
        }

        public void Remove(UIForm form)
        {
            if (form == null)
                return;

            _forms.Remove(form);
            RefreshDepth();
            RefreshCoverState();
        }

        public void BringToFront(UIForm form)
        {
            if (form == null)
                return;

            if (_forms.Remove(form))
            {
                _forms.Add(form);
                RefreshDepth();
                RefreshCoverState();
            }
        }

        public int GetTopSortingOrder()
        {
            return SortingOrderBase + (_forms.Count * 100);
        }

        public UIForm GetTopForm()
        {
            for (int i = _forms.Count - 1; i >= 0; i--)
            {
                UIForm form = _forms[i];
                if (form != null && form.State == UIFormState.Opened)
                    return form;
            }

            return null;
        }

        public bool Contains(UIForm form)
        {
            return form != null && _forms.Contains(form);
        }

        public int GetDepth(UIForm form)
        {
            return form == null ? -1 : _forms.IndexOf(form);
        }

        public void RefreshDepth()
        {
            for (int i = 0; i < _forms.Count; i++)
            {
                int sortingOrder = SortingOrderBase + i * 100;
                _forms[i].InternalSetDepth(sortingOrder);
            }
        }

        public void RefreshCoverState()
        {
            ApplyCoverState(false);
        }

        public bool ApplyCoverState(bool coveredByHigherGroup)
        {
            bool covered = coveredByHigherGroup;
            bool hasOpenedFullScreen = false;

            for (int i = _forms.Count - 1; i >= 0; i--)
            {
                UIForm form = _forms[i];
                if (form == null || form.State != UIFormState.Opened)
                    continue;

                if (!covered)
                {
                    form.InternalReveal();
                    form.InternalResume();

                    if (BlocksUnderlying(form))
                    {
                        covered = true;
                        hasOpenedFullScreen = true;
                    }
                }
                else
                {
                    form.InternalCover();
                    if (form.Config != null && form.Config.pauseCovered)
                        form.InternalPause();
                }
            }

            return coveredByHigherGroup || hasOpenedFullScreen;
        }

        public UIForm GetTopBackCloseForm()
        {
            for (int i = _forms.Count - 1; i >= 0; i--)
            {
                UIForm form = _forms[i];
                if (form == null || form.State != UIFormState.Opened)
                    continue;

                if (form.Config != null && form.Config.allowBackClose)
                    return form;
            }

            return null;
        }

        public UIForm GetTopMaskForm()
        {
            for (int i = _forms.Count - 1; i >= 0; i--)
            {
                UIForm form = _forms[i];
                if (form == null || form.State != UIFormState.Opened || !form.IsVisible)
                    continue;

                if (form.Config != null && form.UseMask && form.MaskMode != UIMaskMode.None)
                    return form;
            }

            return null;
        }

        private static bool BlocksUnderlying(UIForm form)
        {
            if (form?.Config == null)
                return false;

            switch (form.Config.blockingMode)
            {
                case UIBlockingMode.None:
                    return false;
                case UIBlockingMode.Always:
                    return true;
                default:
                    return form.Config.fullScreen;
            }
        }
    }
}