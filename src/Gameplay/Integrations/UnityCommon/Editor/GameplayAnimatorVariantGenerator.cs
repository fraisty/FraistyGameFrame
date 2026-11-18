using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FRFrame.Gameplay.Unity.Editor
{
    /// <summary>Creates or updates standard Animator sources from intentionally selected controller states.</summary>
    public sealed class GameplayAnimatorVariantGenerator : EditorWindow
    {
        private sealed class Row
        {
            internal string State, Reason, Group = "动作";
            internal AnimationClip Clip;
            internal bool Selected;
            internal int Level = 1;
            internal float Weight = 1;
        }

        private sealed class GroupPolicy
        {
            internal string Channel = "Body";
            internal int Priority = 10;
            internal float Blend;
            internal bool Loop, Restore = true, Weighted = true;

            internal GroupPolicy Copy() => (GroupPolicy)MemberwiseClone();
            internal string Description =>
                $"{Channel} · P{Priority} · 混合 {Blend:R}s · 循环 {Loop} · 恢复 {Restore} · 权重 {Weighted}";
        }

        private GameplayUnityAnimationAsset _source;
        private GameObject _prefab, _scannedPrefab;
        private AnimatorController _controller;
        private DefaultAsset _folder;
        private Row[] _rows;
        private int _layer, _scannedLayer, _bulkLevel = 1;
        private float _bulkWeight = 1, _seconds;
        private readonly Dictionary<string, GroupPolicy> _policies = new(StringComparer.Ordinal);
        private GroupPolicy _defaults = new();
        private string _filter = "", _group = "动作", _error, _approvedPlan;
        private Vector2 _scroll;
        private GameplayAnimationPreviewSession _preview;

        [MenuItem("FRFrame/Gameplay/Animation/Animator Variant Generator")]
        public static void Open() => GetWindow<GameplayAnimatorVariantGenerator>("动画变体生成");

        private static AnimatorController Controller(GameObject prefab)
        {
            var animator = prefab ? prefab.GetComponentInChildren<Animator>(true) : null;
            if (!animator || animator.runtimeAnimatorController is not AnimatorController controller)
                throw new InvalidOperationException("请选择带实际 AnimatorController 的模型。OverrideController 无法可靠扫描。");
            return controller;
        }

        private static Row[] ScanLayer(AnimatorController controller, int layer)
        {
            if (layer < 0 || layer >= controller.layers.Length) throw new InvalidOperationException("所选层已不存在。");
            var rows = new List<Row>();
            var actual = controller.layers[layer];
            if (actual.syncedLayerIndex >= 0) throw new InvalidOperationException("同步层不能直接推断每个状态的有效 Motion。");
            Collect(actual.stateMachine, actual.name);
            return rows.ToArray();

            void Collect(AnimatorStateMachine machine, string prefix)
            {
                foreach (var entry in machine.states)
                {
                    var motion = entry.state.motion;
                    var clip = motion as AnimationClip;
                    rows.Add(new Row { State = prefix + "." + entry.state.name, Clip = clip,
                        Reason = motion is BlendTree ? "BlendTree 需手动配置" : !clip || clip.length <= 0 ? "缺少有效直接剪辑" : null });
                }
                foreach (var nested in machine.stateMachines) Collect(nested.stateMachine, prefix + "." + nested.stateMachine.name);
            }
        }

        /// <summary>Legacy scan API returns eligible direct-clip states for the source layer.</summary>
        public static GameplayUnityAnimationAsset.LevelVariant[] Scan(GameplayUnityAnimationAsset source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            var rows = ScanLayer(Controller(source.PreviewPrefab), source.Layer).Where(r => r.Reason == null).Select(Variant).ToArray();
            if (rows.Length == 0) throw new InvalidOperationException("本层没有可用的直接剪辑。");
            return rows;
        }

        private static GameplayUnityAnimationAsset.LevelVariant Variant(Row row) => new()
        { StateName = row.State, Clip = row.Clip, MinimumLevel = row.Level, Weight = row.Weight };

        private static void ValidateSelection(Row[] actual, GameplayUnityAnimationAsset.LevelVariant[] selected)
        {
            if (selected == null || selected.Length == 0) throw new InvalidOperationException("请勾选至少一个状态。");
            foreach (var variant in selected)
                if (variant == null || variant.MinimumLevel < 1 || !float.IsFinite(variant.Weight) || variant.Weight <= 0 ||
                    !actual.Any(r => r.Reason == null && r.State == variant.StateName && r.Clip == variant.Clip))
                    throw new InvalidOperationException("状态、剪辑、等级或权重已无效。请重新扫描或修正参数。");
        }

        /// <summary>Applies the reviewed selection with its edited levels and weights in one Undo.</summary>
        public static void Apply(GameplayUnityAnimationAsset source, GameplayUnityAnimationAsset.LevelVariant[] selected)
        {
            EnsureEditable();
            if (!source) throw new ArgumentNullException(nameof(source));
            if (source.Backend != GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState || !AssetDatabase.IsOpenForEdit(source))
                throw new InvalidOperationException("请选择可编辑的 Animator State Source。");
            ValidateSelection(ScanLayer(Controller(source.PreviewPrefab), source.Layer), selected);
            SetVariants(source, selected, null);
        }

        private static void SetVariants(GameplayUnityAnimationAsset source, GameplayUnityAnimationAsset.LevelVariant[] selected, GroupPolicy policy)
        {
            Undo.RecordObject(source, "更新动画变体");
            source.Variants = selected.Select(v => new GameplayUnityAnimationAsset.LevelVariant
            { StateName = v.StateName, Clip = v.Clip, MinimumLevel = v.MinimumLevel, Weight = v.Weight }).ToArray();
            source.WeightedVariants = true;
            if (policy != null) UsePolicy(source, policy);
            EditorUtility.SetDirty(source);
            GameplayAuthoringSourceChanges.Notify(source);
        }

        private static void EnsureEditable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("退出 Play 后再修改正式动画。");
        }

        private void OnDisable() => _preview?.Dispose();

        private void OnGUI()
        {
            var selected = (GameplayUnityAnimationAsset)EditorGUILayout.ObjectField("现有 Source", _source,typeof(GameplayUnityAnimationAsset),false);
            if (selected != _source)
            {
                _source=selected;
                if (_source)
                {
                    _prefab=_source.PreviewPrefab;_layer=_source.Layer;
                    _defaults=new GroupPolicy { Channel=_source.Channel, Priority=_source.Priority, Blend=_source.BlendDuration,
                        Loop=_source.Loop, Restore=_source.RestorePrevious, Weighted=_source.WeightedVariants };
                }
                _rows=null;_policies.Clear();_approvedPlan=null;
            }
            _prefab=(GameObject)EditorGUILayout.ObjectField("真实模型",_prefab,typeof(GameObject),false);
            if (_prefab && _prefab.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController is AnimatorController controller)
                _layer=EditorGUILayout.Popup("扫描层",Mathf.Clamp(_layer,0,controller.layers.Length-1),controller.layers.Select(l=>l.name).ToArray());
            if (GUILayout.Button("扫描直接剪辑")) Attempt(() =>
            {
                _controller=Controller(_prefab);_rows=ScanLayer(_controller,_layer);_scannedPrefab=_prefab;_scannedLayer=_layer;
                _policies.Clear();_approvedPlan=null;
            });
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            if (_rows != null)
            {
                _filter=EditorGUILayout.TextField("状态筛选",_filter);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _group=EditorGUILayout.TextField(_group);
                    if (GUILayout.Button("选中行设组")) foreach (var row in _rows.Where(r=>r.Selected)) row.Group=_group;
                    if (GUILayout.Button("按状态名称前缀分组"))
                        foreach (var row in _rows.Where(r=>r.Selected))
                        {
                            string name=row.State.Split('.').Last();
                            row.Group=name.Split('_','-',' ').First();
                        }
                }
                foreach (var row in _rows.Where(r=>r.State.IndexOf(_filter,StringComparison.OrdinalIgnoreCase)>=0))
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUI.DisabledScope(row.Reason!=null))
                            row.Selected=EditorGUILayout.ToggleLeft(row.State,row.Selected);
                        EditorGUILayout.LabelField(row.Reason??$"{row.Clip.name} · {row.Clip.length:0.###}s",EditorStyles.miniLabel);
                        if (!row.Selected) continue;
                        row.Group=EditorGUILayout.TextField("组",row.Group);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            row.Level=EditorGUILayout.IntField("等级",row.Level);
                            row.Weight=EditorGUILayout.FloatField("权重",row.Weight);
                            if (GUILayout.Button("预览",GUILayout.Width(52))) Attempt(()=>ShowPreview(row));
                        }
                    }
                _bulkLevel=EditorGUILayout.IntField("批量等级",_bulkLevel);
                _bulkWeight=EditorGUILayout.FloatField("批量权重",_bulkWeight);
                if (GUILayout.Button("将等级 / 权重应用于勾选行"))
                    foreach (var row in _rows.Where(r=>r.Selected)){row.Level=_bulkLevel;row.Weight=_bulkWeight;}
                string[] names=_rows.Where(r=>r.Selected).Select(r=>r.Group).Distinct(StringComparer.Ordinal).ToArray();
                EditorGUILayout.LabelField("批量播放策略", EditorStyles.boldLabel);
                DrawPolicy(_defaults);
                if (names.Length>0 && GUILayout.Button("将批量策略填入所选组"))
                    foreach (var name in names) _policies[name]=_defaults.Copy();
                foreach (var name in names)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(name) ? "未命名组" : name, EditorStyles.boldLabel);
                        DrawPolicy(Policy(name));
                    }
                if(_preview!=null)
                {
                    _seconds=EditorGUILayout.Slider("实际剪辑时间",_seconds,0,Mathf.Max(.01f,_preview.Duration));
                    _preview.Seek(_seconds);_preview.Render(GUILayoutUtility.GetRect(100,155,GUILayout.ExpandWidth(true)));
                    if(!string.IsNullOrEmpty(_preview.Error))EditorGUILayout.HelpBox(_preview.Error,MessageType.Warning);
                }
                _folder=(DefaultAsset)EditorGUILayout.ObjectField("新 Source 文件夹",_folder,typeof(DefaultAsset),false);
                if(GUILayout.Button("Dry-run · 显示目标和参数"))
                    Attempt(()=>_approvedPlan=Plan(out _,out _));
                if(_approvedPlan!=null)
                {
                    EditorGUILayout.LabelField(_approvedPlan,EditorStyles.wordWrappedMiniLabel);
                    using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        if(_source&&GUILayout.Button("应用单组到现有 Source"))Attempt(ApplyPlan);
                        if(_folder&&GUILayout.Button("创建正式 Source"))Attempt(CreatePlan);
                    }
                }
            }
            if(_error!=null)EditorGUILayout.HelpBox(_error,MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private GroupPolicy Policy(string group)
        {
            if (!_policies.TryGetValue(group, out var policy))
                _policies.Add(group, policy=_defaults.Copy());
            return policy;
        }

        private static void DrawPolicy(GroupPolicy policy)
        {
            policy.Channel=EditorGUILayout.TextField("通道",policy.Channel);
            policy.Priority=EditorGUILayout.IntField("优先级",policy.Priority);
            policy.Blend=EditorGUILayout.FloatField("混合秒数",policy.Blend);
            using (new EditorGUILayout.HorizontalScope())
            {
                policy.Loop=EditorGUILayout.ToggleLeft("循环",policy.Loop);
                policy.Restore=EditorGUILayout.ToggleLeft("恢复之前",policy.Restore);
                policy.Weighted=EditorGUILayout.ToggleLeft("组内权重",policy.Weighted);
            }
        }

        private void Attempt(Action action)
        {
            try { action();_error=null; }
            catch(Exception error){_error=error.Message;}
        }

        private string Plan(out IGrouping<string,Row>[] groups,out string folder)
        {
            if(_prefab!=_scannedPrefab||_layer!=_scannedLayer||Controller(_prefab)!=_controller)
                throw new InvalidOperationException("模型、Controller 或层已变化，请重新扫描。");
            var chosen=_rows.Where(r=>r.Selected).ToArray();
            ValidateSelection(ScanLayer(_controller,_scannedLayer),chosen.Select(Variant).ToArray());
            if(chosen.Any(r=>string.IsNullOrWhiteSpace(r.Group)||r.Group.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0))
                throw new InvalidOperationException("组名不能为空或含路径字符。");
            groups=chosen.GroupBy(r=>r.Group,StringComparer.Ordinal).ToArray();
            foreach (var group in groups)
            {
                var policy=Policy(group.Key);
                if (string.IsNullOrWhiteSpace(policy.Channel)||!float.IsFinite(policy.Blend)||policy.Blend<0)
                    throw new InvalidOperationException($"「{group.Key}」需要通道和有效混合秒数。");
            }
            folder=_folder ? AssetDatabase.GetAssetPath(_folder) : "";
            var destination=folder;
            var plan = groups.Select(g=>
                (string.IsNullOrEmpty(destination)?"":(AssetDatabase.LoadMainAssetAtPath(SourcePath(destination,g.Key))||System.IO.File.Exists(SourcePath(destination,g.Key))?"冲突（整批取消） ":"创建 ")+SourcePath(destination,g.Key)+"\n")+
                $"{g.Key} · L{_layer} / {Policy(g.Key).Description}\n"+
                string.Join(" / ",g.Select(r=>$"{r.State} {r.Clip.name} Lv{r.Level} 权重{r.Weight:R}")));
            var oldVariants=_source?.Variants??Array.Empty<GameplayUnityAnimationAsset.LevelVariant>();
            return (_source?$"更新 {AssetDatabase.GetAssetPath(_source)} [{_source.DefinitionId}] · 旧 L{_source.Layer}/{_source.Channel}/P{_source.Priority}/混合{_source.BlendDuration:R}s · 循环{_source.Loop} · 恢复{_source.RestorePrevious} · 权重{_source.WeightedVariants}\n"+
                string.Join(" / ",oldVariants.Where(v=>v!=null).Select(v=>$"{v.StateName} {v.Clip?.name} Lv{v.MinimumLevel} 权重{v.Weight:R}"))+"\n":"")+
                $"来源 {AssetDatabase.GetAssetPath(_controller)} · L{_layer} {_controller.layers[_layer].name}\n"+
                string.Join("\n",plan);
        }

        private static string SourcePath(string folder,string name)=>folder+"/"+name+".asset";

        private IGrouping<string,Row>[] Approved(out string folder)
        {
            EnsureEditable();
            if(Plan(out var groups,out folder)!=_approvedPlan)
                throw new InvalidOperationException("配置或目标文件已变化，请重新 Dry-run。");
            return groups;
        }

        private void ApplyPlan()
        {
            var groups=Approved(out _);
            if(!_source||groups.Length!=1||_source.PreviewPrefab!=_prefab||_source.Layer!=_layer||
                _source.Backend!=GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState||!AssetDatabase.IsOpenForEdit(_source))
                throw new InvalidOperationException("请使用同一模型及层的可编辑 Animator Source，且只选择一组。");
            Undo.IncrementCurrentGroup();
            SetVariants(_source,groups[0].Select(Variant).ToArray(),Policy(groups[0].Key));
            AssetDatabase.SaveAssetIfDirty(_source);
            _approvedPlan=null;
        }

        private static void UsePolicy(GameplayUnityAnimationAsset asset, GroupPolicy policy)
        {
            asset.Channel=policy.Channel;asset.Priority=policy.Priority;asset.BlendDuration=policy.Blend;
            asset.Loop=policy.Loop;asset.RestorePrevious=policy.Restore;asset.WeightedVariants=policy.Weighted;
        }

        private void ShowPreview(Row row)
        {
            _preview?.Dispose();
            var policy=Policy(row.Group);
            _preview=new GameplayAnimationPreviewSession();
            _preview.UpdateValues(null,new GameplayUnityAnimationData
            {
                PreviewPrefab=_prefab,Clip=row.Clip,StateName=row.State,Layer=_scannedLayer,
                Channel=policy.Channel,Priority=policy.Priority,BlendDuration=policy.Blend,Loop=policy.Loop,RestorePrevious=policy.Restore
            },0);
            _seconds=0;
        }

        private void CreatePlan()
        {
            var groups=Approved(out string folder);
            if(!EditorUtility.DisplayDialog("创建正式动画资源","将一次创建 "+groups.Length+" 个新 Source。新文件请在 Project 中管理，Undo 用于已有 Source 的修改。","创建","取消"))return;
            var created=WriteSources(groups,folder);
            _approvedPlan=null;
            Selection.activeObject=AssetDatabase.LoadMainAssetAtPath(created[0]);
        }

        private List<string> WriteSources(IGrouping<string,Row>[] groups,string folder)
        {
            if(!(folder=="Assets"||folder.StartsWith("Assets/",StringComparison.Ordinal))||!AssetDatabase.IsValidFolder(folder)||
                !AssetDatabase.IsOpenForEdit(folder, StatusQueryOptions.ForceUpdate))
                throw new InvalidOperationException("请选择 Assets 内可写的文件夹。");
            var candidates=groups.Select(group=>(Group: group, Path: SourcePath(folder,group.Key))).ToArray();
            if(candidates.Select(c=>c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=candidates.Length||
                candidates.Any(c=>AssetDatabase.LoadMainAssetAtPath(c.Path)||System.IO.File.Exists(c.Path)))
                throw new InvalidOperationException("至少一个目标已存在。请更换组名或目录后重新 Dry-run。");
            var created=new List<string>();
            try
            {
                foreach(var candidate in candidates)
                {
                    var baseRow=candidate.Group.First();
                    var asset=CreateInstance<GameplayUnityAnimationAsset>();
                    asset.PreviewPrefab=_prefab;asset.Layer=_layer;asset.Clip=baseRow.Clip;asset.StateName=baseRow.State;
                    asset.Variants=candidate.Group.Select(Variant).ToArray();
                    UsePolicy(asset,Policy(candidate.Group.Key));
                    using(var values=new SerializedObject(asset))
                    {
                        values.FindProperty("definitionId").stringValue="Animation.Unity."+Guid.NewGuid().ToString("N");
                        values.ApplyModifiedPropertiesWithoutUndo();
                    }
                    asset.Build(new GameplayAuthoringBuildContext());
                    AssetDatabase.CreateAsset(asset,candidate.Path);
                    if(AssetDatabase.GetAssetPath(asset)!=candidate.Path)throw new InvalidOperationException("未能创建 "+candidate.Path);
                    created.Add(candidate.Path);
                    GameplayAuthoringSourceChanges.Notify(asset);
                }
            }
            catch
            {
                foreach(string path in created)AssetDatabase.DeleteAsset(path);
                throw;
            }
            return created;
        }
    }
}
