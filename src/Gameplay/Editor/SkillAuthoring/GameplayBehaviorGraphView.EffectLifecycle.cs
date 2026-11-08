using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Node = UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        private void AddEffectLifecycle(Node card, GameplayLocalDefinition definition, GameplayEffectDefinitionData effect)
        {
            if (effect.DurationPolicy == GameplayEffectDurationPolicy.Instant) return;
            string owner = definition.Id;
            var section = new VisualElement { name = "EffectLifecycle", style = { marginLeft = 9, marginRight = 5, marginBottom = 5 } };
            card.extensionContainer.Add(section);
            section.Add(new Label("生命周期") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            Row("首次成功应用", GameplayGraphEffectCallbacks.Applied, "新的一组效果提交后执行。免疫或失败不会触发。");
            if (effect.Stacking != null)
            {
                Row("层数变化", GameplayGraphEffectCallbacks.StackChanged, "仅该组真实增减层。持续外观自动读取层数。");
                Row("进入满层", GameplayGraphEffectCallbacks.EnterMax, "从低于上限变为满层。满层外观自动更新；再次申请超限才是溢出。");
            }
            if (effect.Period > 0)
                Row(effect.Executions.Length == 0 ? "每次周期" : "每次周期 · 先结算", GameplayGraphEffectCallbacks.Period,
                    "使用 Effect 时钟的实际周期；原结算在同一周期先执行。");
            if (effect.Stacking != null)
            {
                var overflow = Row("叠层溢出", GameplayGraphEffectCallbacks.AddOverflowActions,
                    "再次申请超限时按下列顺序执行；可添加普通动作或引用后续 Effect 定义。", "+ 行为");
                if (effect.Stacking.Overflow == GameplayEffectStackOverflowPolicy.ExecuteOverflow)
                    Dock(overflow, GameplayGraphEffectCallbacks.Overflow, "+ 效果", "添加仅消费定义的后续 Effect。");
                for (int i = 0; i < effect.Stacking.OverflowExecutions.Length; i++)
                {
                    var item = effect.Stacking.OverflowExecutions[i];
                    string step = (i + 1) + " · ";
                    if (item is GameplayApplyEffectOnOverflowAuthoring)
                        Row(step + "施加效果", "Overflow/" + item.AuthoringId, "只消费所连接的 Effect 定义。", "");
                    else if (item is GameplayEffectOverflowActionsAuthoring)
                        Row(step + "行为", GameplayGraphEffectCallbacks.OverflowActionPrefix + item.AuthoringId,
                            "在同一溢出列表的这个位置执行普通动作。", "");
                    else if (item is GameplayRemoveCurrentOnOverflowAuthoring)
                        Row(step + "移除当前组", null, "精确清理这次溢出的组。");
                    else if (item != null) Row(step + GameplaySkillNodeDescriptors.Get(item.GetType()).DisplayName, null, "由此溢出执行项直接消费。");
                }
            }
            if (effect.DurationPolicy == GameplayEffectDurationPolicy.Duration)
            {
                var expiry = Row("自然到期", GameplayGraphEffectCallbacks.AddCompletionActions,
                    "先清理效果，再按顺序运行后续；驱散和取消不触发。", "+ 行为");
                Dock(expiry, GameplayGraphEffectCallbacks.Completion, "+ 效果", "添加仅消费定义的后续 Effect。");
                for (int i = 0; i < effect.CompletionExecutions.Length; i++)
                {
                    var item = effect.CompletionExecutions[i];
                    string step = (i + 1) + " · ";
                    if (item is GameplayApplyEffectOnCompletionAuthoring)
                        Row(step + "施加效果", "Completion/" + item.AuthoringId, "只消费所连接的 Effect 定义。", "");
                    else if (item is GameplayEffectCompletionActionsAuthoring)
                        Row(step + "行为", GameplayGraphEffectCallbacks.CompletionActionPrefix + item.AuthoringId,
                            "在自然到期列表的这个位置运行。", "");
                }
            }
            Row("移除 · 自动回收状态外观", null, "驱散、取消和溢出清理具体 Effect；目标死亡时释放视图。规则保留的状态在复活时可恢复。");

            VisualElement Row(string caption, string key, string hint, string portCaption = "")
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, minHeight = 20 }, tooltip = hint };
                var label = new Label(caption) { style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis, whiteSpace = WhiteSpace.NoWrap } };
                if (key != null)
                {
                    label.AddToClassList("effect-lifecycle-row");
                    label.userData = (owner, key, caption, hint);
                    Dock(row, key, portCaption, hint);
                }
                row.Insert(0, label);
                section.Add(row);
                return row;
            }

            void Dock(VisualElement row, string key, string caption, string hint)
            {
                var port = AddPort(card, owner, key);
                port.RemoveFromHierarchy();
                port.portName = caption.Length == 0 && !_document.AuthoringGraph.Connections.Any(e => e.FromNodeId == owner && e.OutputPortKey == key) ? "+" : caption;
                port.tooltip = hint;
                port.style.flexShrink = 0;
                if (GameplayGraphEffectCallbacks.IsActionPort(key)) port.portColor = new Color(.35f, .83f, .81f);
                row.Add(port);
            }
        }

        private void RefreshEffectLifecycleRows(Node card)
        {
            var graph = _document.AuthoringGraph;
            foreach (var label in card.extensionContainer.Query<Label>(className: "effect-lifecycle-row").ToList())
            {
                var (owner, key, caption, hint) = ((string, string, string, string))label.userData;
                string steps = GameplaySkillNodeDisplay.EffectActionSteps(graph, owner, key);
                string text = string.IsNullOrEmpty(steps) ? caption : caption + " · " + steps;
                if (label.text != text) label.text = text;
                label.tooltip = hint + "\n" + text;
            }
        }

        private void ShowExistingEffectActions(Socket source, Vector2 anchor)
        {
            var graph = _document.AuthoringGraph;
            var check = GameplayGraphContextPolicy.ConnectionChecker(graph);
            GameplaySkillCreationMenu.Show(this, "连接已有行为", menu => {
                foreach (var node in graph.Nodes.Where(n => n.Payload is GameplayActionNodePayload))
                {
                    if (check(source.Owner, source.Port, node.Id, "Execute", false) != null ||
                        graph.Connections.Any(e => e.FromNodeId == source.Owner && e.OutputPortKey == source.Port && e.ToNodeId == node.Id)) continue;
                    string id = node.Id;
                    menu.AddItem(new GUIContent(GameplaySkillNodeDisplay.Name(node, graph) + " · " + id.Substring(0, 5)), false,
                        () => Try(() => GameplaySkillAuthoringCommands.Connect(_document, source.Owner, source.Port, id)));
                }
            }, anchor);
        }
    }
}
