using TMPro;

namespace FRFrame
{
    public partial class UILoadingForm
    {
        protected TextMeshProUGUI _txtMessage;

        protected override void BindInit()
        {
            if (_bindData == null) return;
            _txtMessage = _bindData.GetComponentByIndex<TextMeshProUGUI>(0);
        }
    }
}
