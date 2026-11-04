using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        private GameplaySkillDocumentBrowser _documentBrowser;
        private VisualElement _browserHandle,_inspectorHandle;
        private float _browserWidth=260,_inspectorWidth=370;
        private bool _browserOpen=true,_inspectorOpen=true;
        private int _browserTab,_detailTab,_rangeLevel=1;
        private bool _browserForced,_previewPlaying;
        private double _previewLast,_hostRepaintLast;
        private string _rangeKey;
        private FRFrame.Gameplay.Editor.SkillAuthoring.GameplayRangePreviewShape _rangeShape;
        private string _rangeError;
        private bool _rangeValid;
        private void PreviewTick()
        {
            double hostNow=EditorApplication.timeSinceStartup;
            if(_installation!=null&&EditorApplication.isPlaying&&_detailTab==2&&hostNow-_hostRepaintLast>=.1){_hostRepaintLast=hostNow;_details?.MarkDirtyRepaint();Repaint();}
            if(!_previewPlaying||_previewClockOwner==null||!_previews.TryGetValue(_previewClockOwner,out var state)||state.Session==null)return;
            double now=EditorApplication.timeSinceStartup;state.Seconds+=(float)(now-_previewLast);_previewLast=now;
            float duration=PreviewClockDuration(_previewClockOwner);if(state.Seconds>duration){state.Seconds=duration;_previewPlaying=false;}
            state.Session.Seek(state.Seconds);Canvas.SetPreviewCursor(_previewClockOwner,state.Seconds);_details?.MarkDirtyRepaint();Canvas?.MarkDirtyRepaint();
        }
        private void DrawRangePreview(){
            var graph=_document.AuthoringGraph;int index=graph.Nodes.FindIndex(n=>n.Id==_selected);
            if(index<0||!((graph.Nodes[index].Payload as FRFrame.Gameplay.Authoring.Graph.GameplayActionNodePayload)?.Action is GameplaySearchTargetActionAuthoring))return;
            _rangeLevel=Mathf.Clamp(EditorGUILayout.IntField("范围预览等级",_rangeLevel),1,_document.EffectiveMaxLevel);
            var context=new FRFrame.Gameplay.Editor.SkillAuthoring.GameplayRangePreviewContext(_document,"authoringGraph.Nodes.Array.data["+index+"].Payload.Action",_rangeLevel);
            string key=_document.GetInstanceID()+":"+_selected+":"+_rangeLevel;
            if(_rangeKey!=key){_rangeKey=key;_rangeValid=FRFrame.Gameplay.Editor.SkillAuthoring.GameplayRangePreview.TryRead(context,out _rangeShape,out _rangeError);}
            if(_rangeValid){GUILayout.Label(_rangeShape.Label,EditorStyles.boldLabel);FRFrame.Gameplay.Editor.SkillAuthoring.GameplayRangePreview.Draw(GUILayoutUtility.GetRect(100,210,GUILayout.ExpandWidth(true)),_rangeShape);}
            else if(_rangeError!=null)EditorGUILayout.HelpBox(_rangeError,MessageType.Warning);
        }
        // V1's native captured-pointer splitter: resizing a pane changes view geometry only.
        private static VisualElement PaneHandle(Action<float> move,Action save)
        {
            var handle=new VisualElement{style={width=5,flexShrink=0,backgroundColor=GameplayEditorStyles.Surface}};float previous=0;
            handle.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;previous=e.position.x;handle.CapturePointer(e.pointerId);e.StopPropagation();});
            handle.RegisterCallback<PointerMoveEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;move(e.position.x-previous);previous=e.position.x;e.StopPropagation();});
            handle.RegisterCallback<PointerUpEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;handle.ReleasePointer(e.pointerId);save();e.StopPropagation();});return handle;
        }
        internal void OpenDetails(int tab=0)
        {
            _detailTab=tab;
            _inspectorScroll=Vector2.zero;
            if(!_inspectorOpen){
                _inspectorOpen=true;
                ApplyPaneWidths();
                SavePanePreferences();
            }
            _details.MarkDirtyRepaint();
        }

        private void ApplyPaneWidths()
        {
            if(_browser==null||_inspector==null)return;
            bool browser=_browserOpen&&(position.width>=1100||_browserForced);
            _browser.style.display=browser?DisplayStyle.Flex:DisplayStyle.None;
            _browserHandle.style.display=browser?DisplayStyle.Flex:DisplayStyle.None;
            _inspector.style.display=_inspectorOpen?DisplayStyle.Flex:DisplayStyle.None;
            _inspectorHandle.style.display=_inspectorOpen?DisplayStyle.Flex:DisplayStyle.None;
            float right=_inspectorOpen?Mathf.Clamp(_inspectorWidth,300,Mathf.Max(300,position.width-400)):0;
            _inspector.style.width=right;
            _browser.style.width=Mathf.Clamp(_browserWidth,200,Mathf.Max(200,position.width-right-360));
        }
        private void DrawSourceBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if(_document){
                GUILayout.Label(new GUIContent(_document.DisplayName+"  #"+_document.SkillId+(EditorUtility.IsDirty(_document)?"  • 未保存":""),AssetDatabase.GetAssetPath(_document)),EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();GUILayout.Label(new GUIContent(System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(_document)),AssetDatabase.GetAssetPath(_document)),EditorStyles.miniLabel);
                if(GUILayout.Button("定位源文件",EditorStyles.toolbarButton,GUILayout.Width(78))){Selection.activeObject=_document;EditorGUIUtility.PingObject(_document);}
            }else GUILayout.Label("选择技能或新建正式技能 · 单击节点直接编辑业务配置",EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
        private void ExternalChanged(IReadOnlyList<GameplayAuthoringSourceChanges.Change> changes)
        {
            if(!_document)return;
            bool documentChanged=changes.Any(c=>c.Source==_document)&&EditorUtility.GetDirtyCount(_document)!=_knownDocumentDirtyCount;
            bool sharedChanged=changes.Any(c=>Canvas?.UsesExternal(c.Source)==true);
            // Native Apply also emits a deferred object notification. Its already-handled
            // dirty revision must not trigger a second full refresh. Unknown revisions and
            // all shared-source changes retain conservative topology/context fallback.
            if(!documentChanged&&!sharedChanged)return;
            _diagnosticsStale=true;_rangeKey=null;DisposePreview();_documentBrowser?.Changed(_document);RefreshUnknown();RememberSourceVersion();_details?.MarkDirtyRepaint();Repaint();
        }

    }
}
