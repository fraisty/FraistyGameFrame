#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// UI 绑定批量重命名弹窗。
    ///
    /// 设计说明：
    /// 1. 只负责根据选中的绑定生成"新名字方案"并预览，真正写回由回调交给调用方，
    ///    这样 Undo、SetDirty、列表刷新都由管理窗口统一处理；
    /// 2. 支持加前缀 / 加后缀 / 查找替换 / 正则替换 / 按节点名+类型规则重建；
    /// 3. 应用前会对整批目标名做去重，避免批量操作制造出新的重复字段名。
    ///
    /// 关于"批量是否有意义"：
    /// 如果字段名彼此没有共同规律，前缀/替换类操作确实帮助有限，
    /// 这时更推荐使用"按规则重建"生成语义化默认名，再逐个微调；
    /// 逐个微调请使用管理窗口表格里的行内编辑。
    /// </summary>
    public sealed class UIBindBatchRenameWindow : EditorWindow
    {
        /// <summary>
        /// 一条待重命名的目标。
        /// </summary>
        public sealed class RenameTarget
        {
            public UIBindInfo info;
            public string originalName;
        }

        private readonly List<RenameTarget> _targets = new List<RenameTarget>();
        private Action<List<RenameTarget>> _applyCallback;

        private UIBindEditorUtility.BatchRenameMode _mode = UIBindEditorUtility.BatchRenameMode.AddPrefix;
        private string _argA = string.Empty;
        private string _argB = string.Empty;

        private Vector2 _scroll;

        // 预览结果：与 _targets 一一对应。
        private readonly List<string> _preview = new List<string>();
        private bool _hasConflictWithOutside;

        /// <summary>
        /// 打开批量重命名弹窗。
        /// </summary>
        /// <param name="targets">被选中的绑定及其原名。</param>
        /// <param name="allFieldNamesExcludingTargets">除选中项外，其余绑定已经占用的字段名，用于跨批次判重。</param>
        /// <param name="applyCallback">用户点击应用后回调，参数为写入了新名字的目标列表。</param>
        public static void Open(
            List<RenameTarget> targets,
            HashSet<string> allFieldNamesExcludingTargets,
            Action<List<RenameTarget>> applyCallback)
        {
            if (targets == null || targets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在列表中选择要批量重命名的绑定。", "确定");
                return;
            }

            UIBindBatchRenameWindow window = CreateInstance<UIBindBatchRenameWindow>();
            window.titleContent = new GUIContent("批量重命名");
            window.minSize = new Vector2(520f, 420f);

            window._targets.Clear();
            window._targets.AddRange(targets);
            window._outsideNames = allFieldNamesExcludingTargets ?? new HashSet<string>();
            window._applyCallback = applyCallback;

            window.RecomputePreview();
            window.ShowUtility();
        }

        private HashSet<string> _outsideNames = new HashSet<string>();

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"选中 {_targets.Count} 条绑定", EditorStyles.boldLabel);

            DrawModeArea();

            EditorGUILayout.Space();
            DrawPreviewArea();

            EditorGUILayout.Space();
            DrawButtons();
        }

        private void DrawModeArea()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();

            _mode = (UIBindEditorUtility.BatchRenameMode)EditorGUILayout.EnumPopup("操作方式", _mode);

            switch (_mode)
            {
                case UIBindEditorUtility.BatchRenameMode.AddPrefix:
                    _argA = EditorGUILayout.TextField("前缀", _argA);
                    break;

                case UIBindEditorUtility.BatchRenameMode.AddSuffix:
                    _argA = EditorGUILayout.TextField("后缀", _argA);
                    break;

                case UIBindEditorUtility.BatchRenameMode.Replace:
                    _argA = EditorGUILayout.TextField("查找", _argA);
                    _argB = EditorGUILayout.TextField("替换为", _argB);
                    break;

                case UIBindEditorUtility.BatchRenameMode.RegexReplace:
                    _argA = EditorGUILayout.TextField("正则", _argA);
                    _argB = EditorGUILayout.TextField("替换为", _argB);
                    if (!string.IsNullOrEmpty(_argA) && !UIBindEditorUtility.IsValidRegex(_argA))
                    {
                        EditorGUILayout.HelpBox("正则表达式无法编译，将保持原名。", MessageType.Warning);
                    }
                    EditorGUILayout.LabelField("替换串支持 $1 $2 分组引用。", EditorStyles.miniLabel);
                    break;

                case UIBindEditorUtility.BatchRenameMode.RebuildByRule:
                    EditorGUILayout.HelpBox(
                        "按每个节点的名字 + 组件类型规则重新生成字段名（如 LoginBtn 节点 → _btnLogin）。\n" +
                        "适合字段名彼此没有共同规律、想要一个语义化默认名再逐个微调的场景。",
                        MessageType.Info);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                RecomputePreview();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewArea()
        {
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);

            if (_hasConflictWithOutside)
            {
                EditorGUILayout.HelpBox("部分新名字与未选中的绑定重名，应用时会自动追加序号消歧。", MessageType.Warning);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, "box");

            for (int i = 0; i < _targets.Count; i++)
            {
                string oldName = _targets[i].originalName;
                string newName = i < _preview.Count ? _preview[i] : oldName;

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(oldName, GUILayout.MinWidth(160f));
                GUILayout.Label("→", GUILayout.Width(20f));

                Color old = GUI.contentColor;
                if (newName != oldName)
                    GUI.contentColor = new Color(0.55f, 0.9f, 0.55f);

                EditorGUILayout.LabelField(newName, GUILayout.MinWidth(160f));
                GUI.contentColor = old;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("应用", GUILayout.Height(28)))
            {
                Apply();
            }

            if (GUILayout.Button("取消", GUILayout.Height(28)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 重新计算预览。这里已经把整批目标名的相互去重也算进去，
        /// 让预览结果和真正应用后的结果保持一致。
        /// </summary>
        private void RecomputePreview()
        {
            _preview.Clear();
            _hasConflictWithOutside = false;

            // used 先放入未选中项已占用的名字，保证批量结果不会撞到它们。
            HashSet<string> used = new HashSet<string>(_outsideNames);

            for (int i = 0; i < _targets.Count; i++)
            {
                RenameTarget target = _targets[i];
                string original = target.originalName;

                string raw = UIBindEditorUtility.EvaluateBatchRename(
                    original, _mode, _argA, _argB, target.info, used);

                if (string.IsNullOrEmpty(raw))
                    raw = original;

                // 与已有名字去重。
                string unique = UIBindEditorUtility.MakeUniqueAgainst(raw, used);
                if (unique != raw && _outsideNames.Contains(raw))
                    _hasConflictWithOutside = true;

                used.Add(unique);
                _preview.Add(unique);
            }
        }

        private void Apply()
        {
            RecomputePreview();

            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].info.fieldName = _preview[i];
            }

            _applyCallback?.Invoke(_targets);
            Close();
        }
    }
}
#endif
