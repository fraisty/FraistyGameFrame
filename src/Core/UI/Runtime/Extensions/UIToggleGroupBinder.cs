using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIToggleGroupBinder : MonoBehaviour
    {
        public ToggleGroup toggleGroup;
        public bool autoCollectChildren = true;
        public bool allowSwitchOff;
        public int defaultIndex;
        public List<Toggle> toggles = new List<Toggle>();
        public UIToggleGroupIndexChangedEvent onSelectedIndexChanged = new UIToggleGroupIndexChangedEvent();
        public UnityEvent<Toggle> onSelectedToggleChanged = new UnityEvent<Toggle>();

        private readonly List<Toggle> _boundToggles = new List<Toggle>();
        private readonly List<UnityAction<bool>> _listeners = new List<UnityAction<bool>>();
        private int _selectedIndex = -1;

        public int SelectedIndex => _selectedIndex;
        public Toggle SelectedToggle => _selectedIndex >= 0 && _selectedIndex < toggles.Count ? toggles[_selectedIndex] : null;

        private void Awake()
        {
            if (toggleGroup == null)
                toggleGroup = GetComponent<ToggleGroup>();
            RefreshToggles();
        }

        private void OnEnable()
        {
            RefreshToggles();
            BindEvents();
            if (SelectedToggle == null && defaultIndex >= 0 && defaultIndex < toggles.Count)
                Select(defaultIndex, false);
            RefreshSelected(true);
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        public void RefreshToggles()
        {
            Toggle selectedToggle = SelectedToggle;
            bool wasBound = _listeners.Count > 0;
            UnbindEvents();

            if (autoCollectChildren)
            {
                toggles.Clear();
                GetComponentsInChildren(true, toggles);
            }

            for (int i = toggles.Count - 1; i >= 0; i--)
            {
                if (toggles[i] == null)
                    toggles.RemoveAt(i);
            }

            if (toggleGroup == null)
                toggleGroup = GetComponent<ToggleGroup>();

            for (int i = 0; i < toggles.Count; i++)
            {
                toggles[i].group = toggleGroup;
                UIToggleBinder binder = toggles[i].GetComponent<UIToggleBinder>();
                if (binder != null)
                    binder.index = i;
            }

            if (toggleGroup != null)
                toggleGroup.allowSwitchOff = allowSwitchOff;

            int selectedIndex = selectedToggle != null ? toggles.IndexOf(selectedToggle) : -1;
            if (selectedIndex < 0)
                selectedIndex = FindSelectedIndex();
            if (selectedIndex < 0 && defaultIndex >= 0 && defaultIndex < toggles.Count)
                selectedIndex = defaultIndex;

            if (selectedIndex >= 0)
                Select(selectedIndex, false);
            else
                _selectedIndex = -1;

            if (wasBound || isActiveAndEnabled)
                BindEvents();
        }

        public void Select(int index, bool notify = true)
        {
            if (index < 0 || index >= toggles.Count)
                return;

            for (int i = 0; i < toggles.Count; i++)
            {
                if (notify)
                    toggles[i].isOn = i == index;
                else
                    toggles[i].SetIsOnWithoutNotify(i == index);
            }

            RefreshSelected(!notify);
        }

        private void BindEvents()
        {
            UnbindEvents();
            for (int i = 0; i < toggles.Count; i++)
            {
                Toggle toggle = toggles[i];
                UnityAction<bool> listener = _ => RefreshSelected(false);
                _boundToggles.Add(toggle);
                _listeners.Add(listener);
                toggle.onValueChanged.AddListener(listener);
            }
        }

        private void UnbindEvents()
        {
            for (int i = 0; i < _listeners.Count && i < _boundToggles.Count; i++)
            {
                if (_boundToggles[i] != null)
                    _boundToggles[i].onValueChanged.RemoveListener(_listeners[i]);
            }
            _boundToggles.Clear();
            _listeners.Clear();
        }

        private int FindSelectedIndex()
        {
            for (int i = 0; i < toggles.Count; i++)
            {
                if (toggles[i] != null && toggles[i].isOn)
                    return i;
            }
            return -1;
        }

        private void RefreshSelected(bool forceNotify)
        {
            int selected = FindSelectedIndex();

            if (!forceNotify && selected == _selectedIndex)
                return;

            _selectedIndex = selected;
            onSelectedIndexChanged.Invoke(_selectedIndex);
            onSelectedToggleChanged.Invoke(SelectedToggle);
        }
    }
}
