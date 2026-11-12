using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        private sealed class PreviewChoice {internal GameplayAuthoringNode Node;internal object Values;internal float Start;internal string Key,Label,Owner,EventId;internal GameplayDefinitionAsset Shared;}
        // Seconds are absolute within Root's timeline. ChoiceKey includes the occurrence path,
        // so reusing one animation action never aliases two scheduled previews.
        private sealed class PreviewSeekRequest {internal string ChoiceKey;internal float Seconds;}
        private sealed class PreviewState {internal IGameplaySkillPreviewSession Session;internal string Key,ChoiceKey;internal float Seconds;internal bool HasClock;internal PreviewSeekRequest Pending;}
        private readonly Dictionary<string,PreviewState> _previews=new Dictionary<string,PreviewState>();
        private PreviewState State(string root){if(!_previews.TryGetValue(root,out var state)){var cursor=Canvas.PreviewCursor(root);_previews[root]=state=new PreviewState{Seconds=cursor??0,HasClock=cursor.HasValue};}return state;}
        private void DisposePreview(){_previewChoices.Clear();_rangeKey=null;_previewPlaying=false;foreach(var state in _previews.Values){state.Session?.Dispose();state.Session=null;state.Key=null;}}
        private string _detailsPreviewRoot;
        private void DisposeDetailsPreview()
        {
            string root = _detailsPreviewRoot;
            _detailsPreviewRoot = null;
            if (root == null || root == _inlinePreviewNode || !_previews.TryGetValue(root, out var state)) return;
            state.Session?.Dispose();state.Session=null;state.Key=null;
            if (_previewClockOwner == root) _previewPlaying = false;
        }
        private void OnLostFocus()
        {
            foreach (var state in _previews.Values)
                (state.Session as IGameplaySkillPreviewSource)?.CancelInteraction();
        }
        private string _previewClockOwner;
        private readonly Dictionary<string,List<PreviewChoice>> _previewChoices=new Dictionary<string,List<PreviewChoice>>();
        private string _inlinePreviewNode;
        private bool PreviewAffected(GameplaySkillAuthoringChange change)
        {
            if(change.Unknown||change.Structural)return true;
            if(change.Layout)return false;
            var graph=_document.AuthoringGraph;
            return change.Owners.Any(owner=>owner==graph.AbilityNodeId||graph.Timelines.Any(t=>t.Id==owner)||
                _previewChoices.Values.SelectMany(c=>c).Any(c=>c.Node.Id==owner||c.Owner==owner));
        }
        private List<PreviewChoice> PreviewChoices(string root)
        {
            if(root==null||!_document||_document.AuthoringGraph==null)return new List<PreviewChoice>();
            if(_previewChoices.TryGetValue(root,out var cached))return cached;
            var graph=_document.AuthoringGraph;var result=new List<PreviewChoice>();var seen=new HashSet<string>();
            Action<string,float,string,string,bool> visit=null;
            visit=(id,time,origin,eventId,effectOnly)=>{
                if(string.IsNullOrEmpty(id)||!seen.Add(id+":"+origin+":"+effectOnly))return;
                var node=graph.Nodes.Find(n=>n.Id==id);var local=graph.LocalDefinitions.Find(d=>d.Id==id);var timeline=graph.Timelines.Find(t=>t.Id==id);
                if(timeline!=null){foreach(var entry in timeline.Events.OrderBy(e=>e.TimeSeconds).ThenBy(e=>e.Order))visit(entry.EntryNodeId,time+entry.TimeSeconds,origin+"/"+entry.Id,entry.Id,false);return;}
                foreach(var binding in node?.Payload.Definitions??local?.Definitions??new List<GameplayDefinitionBinding>()){
                    object values=graph.LocalDefinitions.Find(d=>d.Id==binding.LocalId)?.Payload;
                    if(values==null&&binding.External)try{values=binding.External.CaptureGraphValues();}catch(NotSupportedException){}
                    if(!effectOnly&&node!=null&&values!=null&&PreviewFactories.Any(p=>p.Supports(values)))result.Add(new PreviewChoice{Node=node,Values=values,Start=time,Owner=binding.LocalId,Shared=binding.External,EventId=eventId,Key=origin+":"+id+":"+binding.PropertyPath,Label=time.ToString("0.###")+"s · "+GameplaySkillFieldSchema.NodeName(node)+" · "+string.Join("/",origin.Split('/').Select(part=>part.Substring(0,Math.Min(4,part.Length))))});
                    if(!string.IsNullOrEmpty(binding.LocalId))visit(binding.LocalId,time,origin,eventId,false);
                }
                if(effectOnly || local?.Payload is GameplayEffectDefinitionData)return;
                foreach(var edge in graph.Connections.Where(e=>e.FromNodeId==id).OrderBy(e=>e.Order))visit(edge.ToNodeId,time,origin,eventId,edge.InputPortKey==GameplayGraphEffectCallbacks.Input);
            };
            visit(root,0,root,null,false);_previewChoices[root]=result;return result;
        }
        private bool HasPreview(string id)=>PreviewChoices(id).Any(c=>GameplaySkillNodeDescriptors.Get(((GameplayActionNodePayload)c.Node.Payload).Action.GetType()).IsAnimation);
        private void SetInlinePreview(string id,bool open)
        {
            _inlinePreviewNode=open?id:null;if(!open)DisposePreview();Repaint();
        }
        private void RequestPreviewSeek(string root,string eventId,float seconds)
        {
            Canvas.SetPreviewCursor(root,seconds);
            if(!HasPreview(root))return;
            var choices=PreviewChoices(root).Where(c=>GameplaySkillNodeDescriptors.Get(((GameplayActionNodePayload)c.Node.Payload).Action.GetType()).IsAnimation).ToList();
            var matches=choices.Where(c=>c.EventId==eventId).ToArray();
            var state=State(root);var choice=matches.Length==1?matches[0]:choices.Find(c=>c.Key==state.ChoiceKey);
            if(choice==null&&choices.Count==1)choice=choices[0];
            SetInlinePreview(root,true);_previewPlaying=false;_previewClockOwner=root;state.Seconds=seconds;state.HasClock=true;
            if(choice!=null){state.ChoiceKey=choice.Key;state.Pending=new PreviewSeekRequest{ChoiceKey=choice.Key,Seconds=seconds};}
            Repaint();
        }
        // Actual SDK creation is kept after event selection. A first-frame/configuration reload
        // cannot overwrite an earlier seek request with the animation's start offset.
        private void EnsurePreview(string root,PreviewChoice choice)
        {
            var state=State(root);string key=_document.GetInstanceID()+":"+choice.Key;
            if(state.Key!=key){
                state.Session?.Dispose();state.Session=null;state.Key=key;
                if(!state.HasClock)state.Seconds=choice.Start;
                try{
                    state.Session=PreviewFactories.First(p=>p.Supports(choice.Values)).Create(_document,choice.Values,choice.Start);
                    if (state.Session is IGameplaySkillPreviewSource source)
                    {
                        int index = _document.AuthoringGraph.LocalDefinitions.FindIndex(d => d.Id == choice.Owner);
                        source.BindSource(choice.Shared ? GameplaySourceHandle.Capture(choice.Shared) :
                            index >= 0 ? GameplaySourceHandle.Capture(_document, $"authoringGraph.LocalDefinitions.Array.data[{index}].Payload") : null, choice.Shared);
                    }
                }
                catch(Exception error){_feedback=error.Message;RecordPreviewError(choice.Node.Id,error.Message);}
                state.HasClock=true;
            }
            if(state.Pending!=null&&state.Pending.ChoiceKey==choice.Key){state.Seconds=state.Pending.Seconds;state.Pending=null;}
            state.Session?.Seek(state.Seconds);Canvas.SetPreviewCursor(root,state.Seconds);
        }
        private float PreviewClockDuration(string root)
        {
            var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==root);
            var timeline=_document.AuthoringGraph.Timelines.FirstOrDefault(t=>node!=null&&node.Payload.Definitions.Any(b=>b.LocalId==t.Id));
            return timeline?.DurationSeconds??State(root).Session?.Duration??0;
        }
        private void TogglePreviewPlayback(string root)
        {
            var choices=PreviewChoices(root).Where(c=>GameplaySkillNodeDescriptors.Get(((GameplayActionNodePayload)c.Node.Payload).Action.GetType()).IsAnimation).ToList();
            var state=State(root);var choice=choices.Find(c=>c.Key==state.ChoiceKey)??(choices.Count==1?choices[0]:null);
            if(choice==null){_feedback="请先选择要预览的确切动画事件。";Repaint();return;}
            state.ChoiceKey=choice.Key;EnsurePreview(root,choice);_previewClockOwner=root;_previewPlaying=!_previewPlaying;_previewLast=EditorApplication.timeSinceStartup;
        }
        private void DrawInlinePreview(string id)=>DrawPreview(id,true);
        private void DrawPreview(string root=null,bool inline=false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){EditorGUILayout.HelpBox("编辑模式预览已暂停；请在真实 Scene / Runtime 调试中观察施放。",MessageType.Info);return;}
            var graph=_document.AuthoringGraph;if(graph==null)return;root=root??_selected;
            if (!inline && _detailsPreviewRoot != root) { DisposeDetailsPreview(); _detailsPreviewRoot = root; }
            if(!inline&&root==_inlinePreviewNode&&root!=null){
                GUILayout.Label("编排卡使用同一时间光标；此处调整预览专用输入。",EditorStyles.wordWrappedMiniLabel);
                State(_inlinePreviewNode).Session?.DrawControls();return;
            }
            var choices=PreviewChoices(root);
            if(inline)choices=choices.Where(c=>GameplaySkillNodeDescriptors.Get(((GameplayActionNodePayload)c.Node.Payload).Action.GetType()).IsAnimation).ToList();
            if(choices.Count==0){EditorGUILayout.HelpBox("当前行为没有可用预览；请检查实际资源与后端。",MessageType.Info);return;}
            var state=State(root);int at=choices.FindIndex(c=>c.Key==state.ChoiceKey);
            if(choices.Count==1)at=0;
            else{
                var options=new[]{"选择动画事件 / 载体"}.Concat(choices.Select(c=>c.Label)).ToArray();
                int next=EditorGUILayout.Popup("预览事件",at+1,options)-1;
                if(next!=at&&next>=0){at=next;_previewPlaying=false;}
            }
            if(at<0){EditorGUILayout.HelpBox("多个动画事件；选择上方载体或时间轴中的确切动画事件。",MessageType.Info);return;}
            var choice=choices[at];state.ChoiceKey=choice.Key;EnsurePreview(root,choice);var preview=state.Session;
            if(preview==null){EditorGUILayout.HelpBox(_feedback??"请配置实际资源。",MessageType.Warning);if(GUILayout.Button("配置修复后重试"))DisposePreview();return;}
            if(preview is IGameplaySkillPreviewDiagnostics diagnostic&&!string.IsNullOrEmpty(diagnostic.Error)){
                RecordPreviewError(choice.Node.Id,diagnostic.Error);EditorGUILayout.HelpBox(diagnostic.Error,MessageType.Error);
                if(GUILayout.Button("重试此预览"))diagnostic.Retry();return;
            }
            if(!inline){
                int fps=30;GUILayout.BeginHorizontal();
                if(GUILayout.Button(_previewPlaying&&_previewClockOwner==root?"暂停":"播放",GUILayout.Width(46))){_previewClockOwner=root;_previewPlaying=!_previewPlaying;_previewLast=EditorApplication.timeSinceStartup;}
                if(GUILayout.Button("停止",GUILayout.Width(44))){_previewPlaying=false;state.Seconds=0;}
                if(GUILayout.Button("◀",GUILayout.Width(28))){_previewPlaying=false;state.Seconds-=1f/fps;}
                if(GUILayout.Button("▶",GUILayout.Width(28))){_previewPlaying=false;state.Seconds+=1f/fps;}
                state.Seconds=Mathf.Clamp(EditorGUILayout.FloatField(state.Seconds*fps)/fps,0,PreviewClockDuration(root));GUILayout.EndHorizontal();preview.Seek(state.Seconds);
            }
            float width=inline?Canvas.PreviewWidth(root):_inspector.resolvedStyle.width-35;
            var rect=GUILayoutUtility.GetRect(100,inline?Canvas.PreviewHeight(root):Mathf.Clamp(width*.46f,150,220),GUILayout.ExpandWidth(true));
            preview.Render(rect);
            if(!inline)preview.DrawControls();
        }
    }
}
