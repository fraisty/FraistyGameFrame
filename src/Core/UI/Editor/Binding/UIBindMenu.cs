#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public static class UIBindMenu
    {
        [MenuItem("GameObject/FRFrame/UI/Create UIBindData Root", false, 10)]
        public static void CreateBindRoot(MenuCommand command)
        {
            GameObject go = command.context as GameObject;
            if (go == null)
            {
                go = Selection.activeGameObject;
            }

            if (go == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选中一个UI根节点。", "确定");
                return;
            }

            if (go.GetComponent<UIBindData>() == null)
            {
                Undo.AddComponent<UIBindData>(go);
            }

            if (go.GetComponent<UIBindScriptHolder>() == null)
            {
                Undo.AddComponent<UIBindScriptHolder>(go);
            }

            EditorGUIUtility.PingObject(go);
        }

        [MenuItem("FRFrame/UI/Generate Bind Code For Selected")]
        public static void GenerateForSelected()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("提示", "请选择带 UIBindData 的对象。", "确定");
                return;
            }

            UIBindData bindData = go.GetComponent<UIBindData>();
            if (bindData == null)
            {
                EditorUtility.DisplayDialog("提示", "选中对象上没有 UIBindData。", "确定");
                return;
            }

            UIBindScriptHolder holder = go.GetComponent<UIBindScriptHolder>();
            UICodeGenerator.Generate(bindData, holder != null ? holder.targetScript : null);
        }
    }
}
#endif
