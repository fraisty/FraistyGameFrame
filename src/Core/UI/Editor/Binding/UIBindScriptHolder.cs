#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// 仅编辑器使用的脚本引用持有器。
    /// </summary>
    public sealed class UIBindScriptHolder : MonoBehaviour
    {
        public MonoScript targetScript;
    }
}
#endif
