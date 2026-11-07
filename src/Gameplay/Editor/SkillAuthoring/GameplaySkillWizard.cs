using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>V2's single-main-asset creation workflow. All choices are reviewed before the
    /// path is written; profile/clock choices are editor preferences, not a second runtime source.</summary>
    internal sealed class GameplaySkillWizard:EditorWindow
    {
        private GameplaySkillWorkbench _owner;
        private GameplaySkillDocumentAsset _template;
        private bool _share;
        private List<GameplaySkillCopyReferences.Reference> _references;
        private readonly HashSet<string> _preserveReferences=new HashSet<string>();
        private Vector2 _referenceScroll;
        private string[] _scopes;
        private int _scope,_fps=30,_id,_start;private string _name="我的技能",_error;private bool _allowDuplicate,_turns;
        private GameplayActorProfileAsset _profile;
        internal static void Open(GameplaySkillWorkbench owner,GameplaySkillDocumentAsset template=null)
        {
            var wizard=CreateInstance<GameplaySkillWizard>();wizard._owner=owner;wizard._template=template;if(template)wizard._name=template.DisplayName+" 副本";wizard._id=GameplaySkillWorkbench.Documents().Select(d=>d.SkillId).DefaultIfEmpty(2009).Max()+1;
            string current=template?template.AuthoringGraph.Scope:owner.Document?.AuthoringGraph?.Scope??GameplayReferenceCatalogProvider.Framework;
            wizard._scopes=GameplayReferenceCatalogProvider.Catalogs.SelectMany(catalog=>catalog.ScopeAliases.Prepend(GameplayReferenceCatalogProvider.OwnerScope(catalog)))
                .Append(current).Distinct(StringComparer.Ordinal).ToArray();
            if(template)wizard._references=GameplaySkillCopyReferences.Audit(template);
            wizard._scope=Mathf.Max(0,Array.IndexOf(wizard._scopes,template?template.AuthoringGraph.Scope:owner.Document?.AuthoringGraph?.Scope??"FRFrame"));wizard.titleContent=new GUIContent("新建正式技能");wizard.minSize=template?new Vector2(540,650):new Vector2(440,400);wizard.ShowUtility();
        }
        private void OnGUI()
        {
            GUILayout.Label("一个主文件保存全部技能行为",EditorStyles.boldLabel);using(new EditorGUI.DisabledScope(_template))_scope=EditorGUILayout.Popup("玩法 Scope",_scope,_scopes);
            _name=EditorGUILayout.TextField("显示名称",_name);_id=EditorGUILayout.IntField("技能 ID",_id);
            using(new EditorGUI.DisabledScope(_template))_start=EditorGUILayout.Popup("创作起点",_start,new[]{"空白技能（施放 → 结束）","即时伤害（搜索 → 伤害 → 结束）","时间编排技能（可不设动画）"});
            EditorGUILayout.HelpBox(_template?"沿用源范例的真实行为、时间与FPS；此处不会另加起点节点。":_start==0?"只创建施放和结束。下一步从施放执行端口拖向空白选择行为。":_start==1?"创建可编辑的敌方搜索、即时伤害与结束；请配置数值和真实目标。":"明确创建 1 秒时间编排和结束；主动画可选，逻辑时长独立。",MessageType.Info);using(new EditorGUI.DisabledScope(_template))_fps=Mathf.Max(1,EditorGUILayout.IntField("创作显示 / 吸附 FPS",_fps));
            _profile=(GameplayActorProfileAsset)EditorGUILayout.ObjectField("预览属性（可选）",_profile,typeof(GameplayActorProfileAsset),false);
            bool logical=_scopes[_scope]=="LogicGrid"||_scopes[_scope]=="TurnBased";
            using(new EditorGUI.DisabledScope(!logical))_turns=EditorGUILayout.Toggle("试装使用 Lab 手动回合",_turns&&logical);
            EditorGUILayout.HelpBox(logical?"规则时间由真实 Lab 推进；手动回合使用现有 AdvanceSpatialLabTurn。FPS 只负责显示。":"规则时钟由已有宿主负责。预览属性不自动装备，也不改变 Scene/Prefab。",MessageType.Info);
            if(_scopes[_scope]=="AzurLane")EditorGUILayout.HelpBox("可创作通用规则；当前工作台未注册 AzurLane 试装 Host，需要游戏现有安装链。",MessageType.Info);
            bool duplicate=GameplaySkillWorkbench.Documents().Any(d=>d.SkillId==_id);
            if(duplicate){EditorGUILayout.HelpBox("已有同 ID 技能；不同 Host 中复用 ID 需要作者明确确认。",MessageType.Warning);_allowDuplicate=EditorGUILayout.Toggle("明确允许跨 Host 重号",_allowDuplicate);}
            if(_template){
                string tagScope=GameplayReferenceCatalogProvider.Scope(_template);
                EditorGUILayout.HelpBox("新GUID/ID/局部StableIds；保持原Scope与Host限制。逐项确认自身身份：勾选重映射至新ID；取消勾选表示有意引用源技能（可能共同冷却/续接归属源技能）。其它ID、普通数值与外部SO保持原引用，外部SO不会被修改。",MessageType.Info);
                _referenceScroll=EditorGUILayout.BeginScrollView(_referenceScroll,GUILayout.MinHeight(100),GUILayout.MaxHeight(230));
                var drawnCooldowns=new HashSet<string>();
                foreach(var reference in _references){
                    if(reference.CooldownGroup!=null){
                        if(!drawnCooldowns.Add(reference.CooldownGroup))continue;
                        var group=_references.Where(r=>r.CooldownGroup==reference.CooldownGroup).ToArray();
                        bool independent=!group.Any(r=>_preserveReferences.Contains(r.Path));
                        bool next=EditorGUILayout.ToggleLeft(new GUIContent("独立冷却："+reference.CooldownGroup+" → "+reference.TargetTag(_id,tagScope)+"（"+group.Length+"处）",string.Join("\n",group.Select(r=>r.Path))+"\n取消勾选：有意与源技能共享这组冷却及业务标签。"),independent);
                        foreach(var item in group)if(next)_preserveReferences.Remove(item.Path);else _preserveReferences.Add(item.Path);
                    }
                    else if(reference.IsSelf){string target=reference.IsTag?reference.TargetTag(_id,tagScope):_id.ToString();bool remap=!_preserveReferences.Contains(reference.Path);bool next=EditorGUILayout.ToggleLeft(new GUIContent("重映射："+reference.Description+" → "+target,reference.Path),remap);if(next)_preserveReferences.Remove(reference.Path);else _preserveReferences.Add(reference.Path);}
                    else EditorGUILayout.LabelField(new GUIContent("保留："+reference.Description,reference.Path));
                }
                EditorGUILayout.EndScrollView();
                _share=EditorGUILayout.ToggleLeft("已审核引用并保留外部 SO 共享",_share);
            }
            bool valid=(!_template||_share)&&_id>0&&!string.IsNullOrWhiteSpace(_name)&&(!duplicate||_allowDuplicate);
            if(_error!=null)EditorGUILayout.HelpBox(_error,MessageType.Error);
            EditorGUILayout.Space();using(new EditorGUI.DisabledScope(!valid||EditorApplication.isPlayingOrWillChangePlaymode))if(GUILayout.Button("选择位置并创建",GUILayout.Height(30))){
                string path=EditorUtility.SaveFilePanelInProject("保存正式技能",_id.ToString(),"asset","只创建一个主 SkillData 文件，不覆盖已有文件。");if(string.IsNullOrEmpty(path))return;
                try{var doc=GameplaySkillAuthoringCommands.CreateDocument(path,_id,_name,_scopes[_scope],_start==2,_fps,_start==1,_template,_preserveReferences);GameplaySkillEditorState.CreationPreferences(doc,_profile,_turns);_owner.SelectDocument(doc);Selection.activeObject=doc;Close();}catch(Exception e){_error=e.Message;}
            }
            if(GUILayout.Button("取消"))Close();
        }
    }
}
