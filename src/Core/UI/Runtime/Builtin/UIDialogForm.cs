using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    /// <summary>
    /// 内置通用弹窗。
    /// </summary>
    public partial class UIDialogForm : UIForm
    {
        private UIDialogParams _param;

        protected override void OnInit(object userData)
        {
            if (_txtTitle != null)
                return;

            RectTransform root = UIBuiltinViewFactory.Stretch(gameObject);
            RectTransform panel = UIBuiltinViewFactory.CreatePanel("Panel", root, new Color(0.08f, 0.09f, 0.12f, 0.98f));
            UIBuiltinViewFactory.SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-360f, -210f), new Vector2(360f, 210f));

            _txtTitle = UIBuiltinViewFactory.CreateText("Title", panel, string.Empty, 30f, TMPro.TextAlignmentOptions.Center, Color.white);
            UIBuiltinViewFactory.SetRect(_txtTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -76f), new Vector2(-32f, -20f));

            _txtContent = UIBuiltinViewFactory.CreateText("Content", panel, string.Empty, 23f, TMPro.TextAlignmentOptions.Center, new Color(0.86f, 0.88f, 0.92f, 1f));
            UIBuiltinViewFactory.SetRect(_txtContent.rectTransform, Vector2.zero, Vector2.one, new Vector2(44f, 104f), new Vector2(-44f, -92f));

            _btnConfirm = UIBuiltinViewFactory.CreateButton("ConfirmButton", panel, "确定");
            UIBuiltinViewFactory.SetRect(_btnConfirm.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(24f, 30f), new Vector2(224f, 86f));
            _txtConfirm = _btnConfirm.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);

            _btnCancel = UIBuiltinViewFactory.CreateButton("CancelButton", panel, "取消");
            UIBuiltinViewFactory.SetRect(_btnCancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-224f, 30f), new Vector2(-24f, 86f));
            _txtCancel = _btnCancel.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            _goCancel = _btnCancel.gameObject;
        }

        protected override void OnOpen(object userData)
        {
            _param = userData as UIDialogParams;
            if (_param == null)
                _param = new UIDialogParams();

            _param.Form = this;

            if (_txtTitle != null) _txtTitle.text = _param.Title ?? string.Empty;
            if (_txtContent != null) _txtContent.text = _param.DisplayContent ?? string.Empty;
            if (_txtConfirm != null) _txtConfirm.text = _param.ConfirmText ?? "确定";
            if (_txtCancel != null) _txtCancel.text = _param.CancelText ?? "取消";
            if (_goCancel != null) _goCancel.SetActive(_param.ShowCancel);

            if (_btnConfirm != null)
            {
                _btnConfirm.onClick.RemoveAllListeners();
                _btnConfirm.onClick.AddListener(OnClickConfirm);
            }

            if (_btnCancel != null)
            {
                _btnCancel.onClick.RemoveAllListeners();
                _btnCancel.onClick.AddListener(OnClickCancel);
            }
        }

        private void OnClickConfirm()
        {
            _param?.OnConfirm?.Invoke();
            _param?.ResultSource?.TrySetResult(UIDialogResult.Confirm);
            Close();
        }

        private void OnClickCancel()
        {
            _param?.OnCancel?.Invoke();
            _param?.ResultSource?.TrySetResult(UIDialogResult.Cancel);
            Close();
        }

        protected override void OnClose(object userData)
        {
            _param?.ResultSource?.TrySetResult(UIDialogResult.Close);
            if (_param != null && _param.Form == this)
                _param.Form = null;
        }
    }
}
