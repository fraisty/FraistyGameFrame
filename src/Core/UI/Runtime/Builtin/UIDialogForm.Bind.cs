using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public partial class UIDialogForm
    {
        protected TextMeshProUGUI _txtTitle;
        protected TextMeshProUGUI _txtContent;
        protected TextMeshProUGUI _txtConfirm;
        protected TextMeshProUGUI _txtCancel;
        protected Button _btnConfirm;
        protected Button _btnCancel;
        protected GameObject _goCancel;

        protected override void BindInit()
        {
            if (_bindData == null) return;
            _txtTitle = _bindData.GetComponentByIndex<TextMeshProUGUI>(0);
            _txtContent = _bindData.GetComponentByIndex<TextMeshProUGUI>(1);
            _txtConfirm = _bindData.GetComponentByIndex<TextMeshProUGUI>(2);
            _txtCancel = _bindData.GetComponentByIndex<TextMeshProUGUI>(3);
            _btnConfirm = _bindData.GetComponentByIndex<Button>(4);
            _btnCancel = _bindData.GetComponentByIndex<Button>(5);
            _goCancel = _bindData.GetGameObjectByIndex(6);
        }
    }
}
