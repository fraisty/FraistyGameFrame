using System;
using Cysharp.Threading.Tasks;

namespace FRFrame
{
    public sealed class UIDialogParams
    {
        public string Title;
        public string Content;
        public string Message;
        public string ConfirmText = "确定";
        public string CancelText = "取消";
        public bool ShowCancel = true;
        public bool CloseOnMaskClick = true;
        public float Timeout;
        public Action OnConfirm;
        public Action OnCancel;
        internal UniTaskCompletionSource<UIDialogResult> ResultSource;
        internal UIDialogForm Form;

        public string DisplayContent => string.IsNullOrEmpty(Message) ? Content : Message;
    }
}
