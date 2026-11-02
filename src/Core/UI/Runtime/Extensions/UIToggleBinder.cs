using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FRFrame
{
    [Serializable]
    public sealed class UIToggleIndexChangedEvent : UnityEvent<int, bool>
    {
    }

    [Serializable]
    public sealed class UIToggleGroupIndexChangedEvent : UnityEvent<int>
    {
    }

    public sealed class UIToggleBinder : MonoBehaviour
    {
        public Toggle toggle;
        public int index;
        public GameObject[] activeWhenOn;
        public GameObject[] activeWhenOff;
        public UIToggleIndexChangedEvent onValueChanged = new UIToggleIndexChangedEvent();

        private void Awake()
        {
            if (toggle == null)
                toggle = GetComponent<Toggle>();
        }

        private void OnEnable()
        {
            if (toggle == null)
                toggle = GetComponent<Toggle>();
            if (toggle != null)
                toggle.onValueChanged.AddListener(OnToggleChanged);
            Refresh();
        }

        private void OnDisable()
        {
            if (toggle != null)
                toggle.onValueChanged.RemoveListener(OnToggleChanged);
        }

        public void SetIsOn(bool isOn, bool notify = true)
        {
            if (toggle == null)
                return;

            if (notify)
                toggle.isOn = isOn;
            else
                toggle.SetIsOnWithoutNotify(isOn);
            Refresh();
        }

        public void Refresh()
        {
            bool isOn = toggle != null && toggle.isOn;
            SetActive(activeWhenOn, isOn);
            SetActive(activeWhenOff, !isOn);
        }

        private void OnToggleChanged(bool isOn)
        {
            Refresh();
            onValueChanged.Invoke(index, isOn);
        }

        private static void SetActive(GameObject[] targets, bool active)
        {
            if (targets == null)
                return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    targets[i].SetActive(active);
            }
        }
    }
}
