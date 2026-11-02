using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public sealed class UIManagerSnapshot
    {
        public int OpenedCount;
        public int HiddenCount;
        public int PreloadedCount;
        public int LoadingCount;
        public int NavigationCount;
        public UIToastSnapshot Toast;
        public UIDialogSnapshot Dialog;
        public UILoadingSnapshot Loading;
        public List<UIGroupSnapshot> Groups = new List<UIGroupSnapshot>();
        public List<UIFormSnapshot> Forms = new List<UIFormSnapshot>();
        public List<UILoadingFormSnapshot> LoadingForms = new List<UILoadingFormSnapshot>();
        public List<UIPreloadSnapshot> Preloads = new List<UIPreloadSnapshot>();
        public List<UIMaskSnapshot> Masks = new List<UIMaskSnapshot>();
        public List<string> Warnings = new List<string>();
    }

    public sealed class UIGroupSnapshot
    {
        public UIGroupLayer Layer;
        public string Name;
        public int FormCount;
        public int SortingOrderBase;
    }

    public sealed class UIFormSnapshot
    {
        public int SerialId;
        public string FormId;
        public string LogicType;
        public string AssetPath;
        public string ConfigGroup;
        public string PackageName;
        public UIGroupLayer Layer;
        public UIFormState State;
        public int SortingOrder;
        public int Depth;
        public bool IsTopOfFormId;
        public bool IsVisible;
        public bool IsPaused;
        public bool IsCovered;
        public bool UseMask;
        public UIMaskMode MaskMode;
        public bool CloseOnMaskClick;
    }

    public sealed class UILoadingFormSnapshot
    {
        public string FormId;
        public int SerialId;
        public bool CancelRequested;
        public bool HasFormInstance;
    }

    public sealed class UIPreloadSnapshot
    {
        public string FormId;
        public UIPreloadMode Mode;
        public bool AssetPreloaded;
        public bool InstanceWarmed;
        public string PackageName;
        public string AssetPath;
        public float LastPreloadTime;
    }

    public sealed class UIMaskSnapshot
    {
        public UIGroupLayer Layer;
        public bool Active;
        public int SortingOrder;
        public string TargetFormId;
        public int TargetSerialId;
        public UIMaskMode MaskMode;
        public Color Color;
        public bool CloseOnMaskClick;
    }

    public sealed class UIToastSnapshot
    {
        public int QueueCount;
        public bool Showing;
        public string CurrentMessage;
        public UIToastPosition CurrentPosition;
        public int CurrentPriority;
    }

    public sealed class UIDialogSnapshot
    {
        public int QueueCount;
        public bool QueueRunning;
        public string CurrentTitle;
        public string CurrentContent;
    }

    public sealed class UILoadingSnapshot
    {
        public int RefCount;
        public bool Visible;
        public string Message;
        public float Progress;
        public bool ShowProgress;
    }
}
