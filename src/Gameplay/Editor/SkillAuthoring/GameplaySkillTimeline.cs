using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Node = UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>One retained row per occurrence. The middle time surface alone scrolls;
    /// fixed sibling labels and native ports cannot cover its duration handle.</summary>
    internal sealed class GameplaySkillTimeline : VisualElement
    {
        [Serializable] internal sealed class ViewState {
            public Vector2 Scroll;public float Zoom=1,Cursor;public bool Seconds,FitDuration=true;
            public string SelectedEventId;
            [NonSerialized] public string SelectedNodeId;
            [NonSerialized] internal string Key;private Vector2 _savedScroll;private float _savedZoom=-1,_savedCursor;private bool _savedSeconds,_savedFit;private string _savedSelection;
            internal static ViewState Load(string key){var json=SessionState.GetString(key,"");var state=json.Length==0?new ViewState():JsonUtility.FromJson<ViewState>(json);state.Key=key;if(!json.Contains("\"FitDuration\""))state.FitDuration=true;return state;}
            internal void Save(){if(Key==null||Scroll==_savedScroll&&Zoom==_savedZoom&&Cursor==_savedCursor&&Seconds==_savedSeconds&&FitDuration==_savedFit&&SelectedEventId==_savedSelection)return;SessionState.SetString(Key,JsonUtility.ToJson(this));_savedScroll=Scroll;_savedZoom=Zoom;_savedCursor=Cursor;_savedSeconds=Seconds;_savedFit=FitDuration;_savedSelection=SelectedEventId;}
        }

        private sealed class Row {
            internal GameplayScheduledEntry Entry;
            internal VisualElement Element,PortArea;
            internal Label Label;
            internal IMGUIContainer Content;
            internal string Local,Path;
            internal float Duration;
            internal bool Region;
        }
        private readonly ViewState _state;
        private readonly GameplaySkillDocumentAsset _document;
        private readonly string _node,_timeline;
        private readonly Action<float> _add;
        private readonly Action<string> _select,_reveal;
        private readonly Dictionary<string,Row> _rows=new Dictionary<string,Row>();
        private readonly Scroller _horizontal;
        private readonly IMGUIContainer _ruler;
        private float _pixels=1,_extent,_contentWidth;
        private GUIStyle _eventStyle;
        private string _drag,_durationOwner,_durationPath;
        private float _dragTime,_originalTime,_dragDuration,_resizeStartDuration,_startMouseX,_startTime;
        private bool _resizing,_scrubbing;
        private int _hotId;
        internal int RowCount=>_rows.Count;
        internal Action<float> CursorChanged;
        internal string SelectedEventId=>_state.SelectedEventId;
        internal bool HasAnimation;
        internal Func<string,bool> IsMainAnimation;
        internal Func<bool> IsPlaying;
        internal Action TogglePlaying;
        internal readonly Dictionary<string,Rect> VisibleEventScreenRects=new Dictionary<string,Rect>();
        internal readonly Dictionary<string,Rect> DurationHandleScreenRects=new Dictionary<string,Rect>();
        private float _cursor {get=>_state.Cursor;set=>_state.Cursor=value;}
        private float _zoom {get=>_state.Zoom;set=>_state.Zoom=value;}
        private Vector2 _scroll {get=>_state.Scroll;set=>_state.Scroll=new Vector2(value.x,0);}
        public GameplaySkillTimeline(GameplaySkillDocumentAsset document,string node,string timeline,ViewState state,Action<float> add,Action<string> select,Action<string> reveal)
        {
            _document=document;_node=node;_timeline=timeline;_state=state;_add=add;_select=select;_reveal=reveal;
            name="TimelineRows";style.flexShrink=0;
            var rulerRow=new VisualElement{name="TimelineRuler",style={flexDirection=FlexDirection.Row,height=26}};
            rulerRow.Add(FixedLabel("时间 / 顺序"));
            _ruler=new IMGUIContainer(DrawRuler){style={flexGrow=1,minWidth=140,overflow=Overflow.Hidden}};
            rulerRow.Add(_ruler);rulerRow.Add(new VisualElement{style={width=36,flexShrink=0}});Add(rulerRow);
            _horizontal=new Scroller(0,0,value=>{_scroll=new Vector2(value,0);_state.Save();RepaintTime();},SliderDirection.Horizontal);
            _horizontal.name="TimelineHorizontalScroll";_horizontal.style.marginLeft=160;_horizontal.style.marginRight=36;
            RegisterCallback<DetachFromPanelEvent>(_=>CancelDrag());
            RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape||(e.ctrlKey||e.commandKey)&&(e.keyCode==KeyCode.Z||e.keyCode==KeyCode.Y))CancelDrag();},TrickleDown.TrickleDown);
            _ruler.RegisterCallback<GeometryChangedEvent>(_=>RefreshGeometry());
            InvalidatePresentation();
        }
        private static Label FixedLabel(string text)=>new Label(text){style={width=160,flexShrink=0,fontSize=11,unityTextAlign=TextAnchor.MiddleLeft,paddingLeft=5,overflow=Overflow.Hidden,textOverflow=TextOverflow.Ellipsis}};
        internal void AttachPort(string id,Port port)
        {
            var row=_rows[id];row.PortArea.Add(port);port.portName="";port.name="Occurrence-"+id;
            port.style.position=Position.Relative;port.style.width=30;port.style.minWidth=30;port.style.height=26;port.style.flexShrink=0;port.style.paddingLeft=0;port.style.paddingRight=0;
            var connector=port.Q("connector");connector.style.width=12;connector.style.minWidth=12;connector.style.height=12;
            port.tooltip=row.Label.tooltip+"\n从行尾拖动真实调度连接";
            port.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0)FocusEvent(id);},TrickleDown.TrickleDown);
        }
        internal void InvalidatePresentation()
        {
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);if(timeline==null)return;
            var entries=timeline.Events.Where(e=>IsMainAnimation?.Invoke(e.EntryNodeId)!=true).OrderBy(e=>e.TimeSeconds).ThenBy(e=>e.Order).ThenBy(e=>e.Id,StringComparer.Ordinal).ToArray();
            for(int index=0;index<entries.Length;index++){
                var entry=entries[index];
                if(!_rows.TryGetValue(entry.Id,out var row)){
                    row=new Row{Element=new VisualElement{name="TimelineRow-"+entry.Id,style={flexDirection=FlexDirection.Row,height=30,flexShrink=0,backgroundColor=new Color(.16f,.17f,.2f)}}};
                    row.Label=FixedLabel("");row.Label.name="EventLabel-"+entry.Id;
                    var captured=row;row.Content=new IMGUIContainer(()=>DrawRow(captured)){cullingEnabled=true,name="TimeContent-"+entry.Id,style={flexGrow=1,minWidth=140,overflow=Overflow.Hidden}};
                    row.PortArea=new VisualElement{name="PortArea-"+entry.Id,style={width=36,flexShrink=0,alignItems=Align.Center,justifyContent=Justify.Center}};
                    row.Element.Add(row.Label);row.Element.Add(row.Content);row.Element.Add(row.PortArea);_rows.Add(entry.Id,row);
                    string id=entry.Id;row.Label.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0){FocusEvent(id);e.StopPropagation();}});
                    row.Label.AddManipulator(new ContextualMenuManipulator(e=>PopulateMenu(e.menu,id)));
                }
                row.Entry=entry;var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==entry.EntryNodeId);
                row.Region=GameplaySkillFieldSchema.TryDuration(_document.AuthoringGraph,entry.EntryNodeId,out row.Local,out row.Path,out row.Duration);
                var action=(node?.Payload as GameplayActionNodePayload)?.Action;
                row.Label.text="#"+(index+1)+" "+(action!=null&&GameplaySkillNodeDescriptors.Get(action.GetType()).IsAnimation?GameplaySkillNodeDisplay.EventLabel(node,_document.AuthoringGraph).Split('\n')[0]:GameplaySkillNodeDisplay.Name(node,_document.AuthoringGraph));
                row.Label.tooltip=GameplaySkillNodeDisplay.EventLabel(node,_document.AuthoringGraph)+"\n"+(entry.TimeSeconds*timeline.FramesPerSecond).ToString("0.##")+"f · Order "+entry.Order+"\n事件 "+entry.Id+"\n行为 "+entry.EntryNodeId;
                if(IndexOf(row.Element)!=index+1)Insert(index+1,row.Element);
            }
            foreach(var id in _rows.Keys.Where(id=>!entries.Any(e=>e.Id==id)).ToArray()){_rows[id].Element.RemoveFromHierarchy();_rows.Remove(id);VisibleEventScreenRects.Remove(id);DurationHandleScreenRects.Remove(id);}
            Add(_horizontal);RefreshGeometry();RepaintTime();
        }
        private void RepaintTime(){_ruler.MarkDirtyRepaint();foreach(var row in _rows.Values)row.Content.MarkDirtyRepaint();}
        private void RefreshGeometry()
        {
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);if(timeline==null)return;
            float previousExtent=_extent,previousPixels=_pixels,previousWidth=_contentWidth;
            _extent=timeline.DurationSeconds;
            foreach(var row in _rows.Values)_extent=Mathf.Max(_extent,row.Entry.TimeSeconds+(row.Region?row.Duration:0));
            _contentWidth=Mathf.Max(140,_ruler.contentRect.width);
            _pixels=Mathf.Max(1,(_contentWidth-46)/Mathf.Max(.1f,_extent))*(_state.FitDuration?1:_zoom);
            float maximum=Mathf.Max(0,24+_extent*_pixels+22-_contentWidth);
            _horizontal.highValue=maximum;_horizontal.style.display=maximum>.5f?DisplayStyle.Flex:DisplayStyle.None;
            _horizontal.value=Mathf.Clamp(_scroll.x,0,maximum);_scroll=new Vector2(_horizontal.value,0);
            if(_extent!=previousExtent||_pixels!=previousPixels||_contentWidth!=previousWidth)RepaintTime();
        }
        private void CancelDrag(){_scrubbing=false;_drag=null;_resizing=false;_durationOwner=null;_durationPath=null;if(_hotId!=0&&GUIUtility.hotControl==_hotId)GUIUtility.hotControl=0;_hotId=0;}
        private void Attempt(Action action){try{action();}catch(Exception error){Debug.LogWarning("时间编排拒绝修改："+error.Message);}}
        internal void DrawControls()
        {
            float previousCursor=_cursor;bool previousSeconds=_state.Seconds;
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);if(timeline==null)return;
            GUILayout.BeginHorizontal();
            if(HasAnimation){
                if(GUILayout.Button(IsPlaying?.Invoke()==true?"暂停":"播放",GUILayout.Width(40)))TogglePlaying?.Invoke();
                if(GUILayout.Button("停止",GUILayout.Width(38))){_cursor=0;CursorChanged?.Invoke(_cursor);}
                if(GUILayout.Button("◀",GUILayout.Width(22))){_cursor=Mathf.Max(0,_cursor-1f/timeline.FramesPerSecond);CursorChanged?.Invoke(_cursor);}
                if(GUILayout.Button("▶",GUILayout.Width(22))){_cursor=Mathf.Min(timeline.DurationSeconds,_cursor+1f/timeline.FramesPerSecond);CursorChanged?.Invoke(_cursor);}
            }
            float shown=_state.Seconds?_cursor:_cursor*timeline.FramesPerSecond;
            float next=EditorGUILayout.FloatField(shown,GUILayout.Width(52));
            if(next!=shown){_cursor=Mathf.Clamp(_state.Seconds?next:next/timeline.FramesPerSecond,0,timeline.DurationSeconds);CursorChanged?.Invoke(_cursor);}
            _state.Seconds=GUILayout.Toggle(_state.Seconds,new GUIContent(_state.Seconds?"秒":"帧","只改变显示单位"),EditorStyles.miniButton,GUILayout.Width(28));
            GUILayout.Label((timeline.DurationSeconds*timeline.FramesPerSecond).ToString("0.#")+"f · "+timeline.FramesPerSecond+"fps",EditorStyles.miniLabel,GUILayout.Width(88));
            bool narrow=resolvedStyle.width<510;if(narrow){GUILayout.EndHorizontal();GUILayout.BeginHorizontal();}
            float zoom=GUILayout.HorizontalSlider(_state.FitDuration?1:_zoom,.5f,6,GUILayout.MinWidth(35),GUILayout.MaxWidth(85));
            if(zoom!=(_state.FitDuration?1:_zoom)){_zoom=zoom;_state.FitDuration=false;}
            if(GUILayout.Button("全时长",GUILayout.Width(48))){_zoom=1;_state.FitDuration=true;_scroll=Vector2.zero;}
            if(GUILayout.Button("+ 事件",GUILayout.Width(55)))_add?.Invoke(_cursor);
            GUILayout.EndHorizontal();RefreshGeometry();if(previousCursor!=_cursor||previousSeconds!=_state.Seconds)RepaintTime();_state.Save();
        }

        internal void FocusEvent(string id)
        {
            _state.SelectedEventId=id;
            if(_rows.TryGetValue(id,out var row)){
                float x=24+row.Entry.TimeSeconds*_pixels;
                if(x<_scroll.x+24||x>_scroll.x+_contentWidth-22)_horizontal.value=Mathf.Clamp(x-32,0,_horizontal.highValue);
            }
            _select?.Invoke(id);_state.Save();RepaintTime();
        }
        private void PopulateMenu(DropdownMenu menu,string id)
        {
            var row=_rows[id];
            menu.AppendAction("此帧添加行为…",_=>_add?.Invoke(row.Entry.TimeSeconds));
            menu.AppendAction("同帧提前",_=>Attempt(()=>GameplaySkillAuthoringCommands.ReorderEvent(_document,id,-1)));
            menu.AppendAction("同帧后移",_=>Attempt(()=>GameplaySkillAuthoringCommands.ReorderEvent(_document,id,1)));
            menu.AppendAction("在图中定位消费者",_=>_reveal?.Invoke(row.Entry.EntryNodeId));
            menu.AppendAction("移除此调度",_=>Attempt(()=>GameplaySkillAuthoringCommands.Disconnect(_document,id,true)));
            menu.AppendAction("删除整个行为",_=>Attempt(()=>GameplaySkillAuthoringCommands.Delete(_document,new[]{row.Entry.EntryNodeId})));
        }
        private void DrawGrid(Rect rect,bool ruler)
        {
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);
            EditorGUI.DrawRect(rect,new Color(.11f,.12f,.15f));
            int step=Mathf.Max(1,Mathf.CeilToInt(40/(_pixels/timeline.FramesPerSecond)));
            int first=Mathf.Max(0,Mathf.FloorToInt((_scroll.x-24)/_pixels*timeline.FramesPerSecond/step)*step);
            int last=Mathf.Min(Mathf.CeilToInt(_extent*timeline.FramesPerSecond),Mathf.CeilToInt((_scroll.x+rect.width)/_pixels*timeline.FramesPerSecond));
            for(int frame=first;frame<=last;frame+=step){
                float x=24+frame/(float)timeline.FramesPerSecond*_pixels-_scroll.x;
                EditorGUI.DrawRect(new Rect(x,ruler?19:0,1,ruler?7:rect.height),new Color(.25f,.27f,.31f));
                if(ruler)GUI.Label(new Rect(x,0,50,20),_state.Seconds?(frame/(float)timeline.FramesPerSecond).ToString("0.##")+"s":frame+"f",EditorStyles.miniLabel);
            }
            float end=24+timeline.DurationSeconds*_pixels-_scroll.x;
            EditorGUI.DrawRect(new Rect(end,0,1,rect.height),new Color(.8f,.4f,.45f));
            EditorGUI.DrawRect(new Rect(24+_cursor*_pixels-_scroll.x,0,1,rect.height),new Color(.3f,.75f,.95f));
        }
        private void DrawRuler()
        {
            var rect=GUILayoutUtility.GetRect(0,26,GUILayout.ExpandWidth(true));DrawGrid(rect,true);
            var evt=Event.current;var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);
            if(evt.type==EventType.MouseDown&&evt.button==0&&rect.Contains(evt.mousePosition)){_scrubbing=true;_hotId=GUIUtility.GetControlID(FocusType.Passive);GUIUtility.hotControl=_hotId;}
            if(_scrubbing&&(evt.type==EventType.MouseDown||evt.type==EventType.MouseDrag)){_cursor=Snap((evt.mousePosition.x+_scroll.x-24)/_pixels);CursorChanged?.Invoke(_cursor);_state.Save();RepaintTime();evt.Use();}
            if(_scrubbing&&evt.type==EventType.MouseUp){CancelDrag();evt.Use();}
        }
        private float Snap(float seconds)
        {
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);
            return Mathf.Clamp(Mathf.Round(seconds*timeline.FramesPerSecond)/timeline.FramesPerSecond,0,timeline.DurationSeconds);
        }
        private void DrawRow(Row row)
        {
            if(_eventStyle==null)_eventStyle=new GUIStyle(EditorStyles.miniButton){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(4,4,0,0)};
            var bounds=GUILayoutUtility.GetRect(0,30,GUILayout.ExpandWidth(true));DrawGrid(bounds,false);
            var timeline=_document.AuthoringGraph.Timelines.Find(t=>t.Id==_timeline);var evt=Event.current;
            if(evt.type==EventType.KeyDown&&(evt.keyCode==KeyCode.Escape||evt.control&&evt.keyCode==KeyCode.Z))CancelDrag();
            float time=_drag==row.Entry.Id?_dragTime:row.Entry.TimeSeconds;
            float length=_drag==row.Entry.Id&&_resizing?_dragDuration:row.Duration;
            var rect=new Rect(24+time*_pixels-_scroll.x,4,row.Region?Mathf.Max(18,length*_pixels):38,22);
            bool selected=_state.SelectedEventId==row.Entry.Id||_state.SelectedNodeId==row.Entry.EntryNodeId;
            var color=GUI.backgroundColor;GUI.backgroundColor=selected?new Color(.25f,.65f,.85f):row.Region?new Color(.4f,.7f,.5f):new Color(.65f,.7f,.8f);
            string clock=_state.Seconds?time.ToString("0.###")+"s":(time*timeline.FramesPerSecond).ToString("0.#")+"f";
            GUI.Box(rect,new GUIContent(clock,row.Label.tooltip+(row.Region?"\n持续 "+length.ToString("0.###")+"s；"+(row.Local!=null&&row.Path!=null?"拖动绿色右端修改本地常量":"参考/共享时长只读"):"")),_eventStyle);GUI.backgroundColor=color;
            var handle=new Rect(rect.xMax-8,rect.y,8,rect.height);
            bool editable=row.Region&&row.Local!=null&&row.Path!=null;
            if(editable)EditorGUI.DrawRect(new Rect(handle.x+2,handle.y+3,4,handle.height-6),new Color(.3f,.9f,.5f));
            if(evt.type==EventType.Repaint){
                VisibleEventScreenRects.Remove(row.Entry.Id);DurationHandleScreenRects.Remove(row.Entry.Id);
                var clipped=Rect.MinMaxRect(Mathf.Max(0,rect.xMin),rect.yMin,Mathf.Min(bounds.width,rect.xMax),rect.yMax);
                if(clipped.width>0)VisibleEventScreenRects[row.Entry.Id]=new Rect(GUIUtility.GUIToScreenPoint(clipped.position),clipped.size);
                if(editable&&handle.xMin>=0&&handle.xMax<=bounds.width)DurationHandleScreenRects[row.Entry.Id]=new Rect(GUIUtility.GUIToScreenPoint(handle.position),handle.size);
            }
            if(evt.type==EventType.MouseDown&&evt.button==0&&bounds.Contains(evt.mousePosition)&&rect.Contains(evt.mousePosition)){
                FocusEvent(row.Entry.Id);_drag=row.Entry.Id;_dragTime=_originalTime=_startTime=row.Entry.TimeSeconds;
                _resizing=editable&&handle.Contains(evt.mousePosition);_dragDuration=_resizeStartDuration=length;_durationOwner=row.Local;_durationPath=row.Path;
                _startMouseX=evt.mousePosition.x;_hotId=GUIUtility.GetControlID(FocusType.Passive);GUIUtility.hotControl=_hotId;evt.Use();
            }
            if(evt.type==EventType.ContextClick&&rect.Contains(evt.mousePosition)){
                var menu=new GenericMenu();string id=row.Entry.Id;
                menu.AddItem(new GUIContent("在图中定位消费者"),false,()=>{FocusEvent(id);_reveal?.Invoke(row.Entry.EntryNodeId);});
                menu.AddItem(new GUIContent("移除此调度"),false,()=>Attempt(()=>GameplaySkillAuthoringCommands.Disconnect(_document,id,true)));menu.ShowAsContext();evt.Use();
            }
            if(_drag==row.Entry.Id){
                if(evt.type==EventType.MouseDrag){
                    float delta=(evt.mousePosition.x-_startMouseX)/_pixels;
                    if(_resizing)_dragDuration=Mathf.Max(1f/timeline.FramesPerSecond,Mathf.Round((_resizeStartDuration+delta)*timeline.FramesPerSecond)/timeline.FramesPerSecond);
                    else _dragTime=Snap(_startTime+delta);
                    RepaintTime();evt.Use();
                }
                if(evt.type==EventType.MouseUp){
                    string id=_drag,owner=_durationOwner,path=_durationPath;float changedTime=_dragTime,changedDuration=_dragDuration;bool resize=_resizing;CancelDrag();
                    if(resize){if(changedDuration!=_resizeStartDuration)Attempt(()=>GameplaySkillAuthoringCommands.ResizeConstantDuration(_document,owner,path,changedDuration));}
                    else if(changedTime!=_originalTime)Attempt(()=>GameplaySkillAuthoringCommands.MoveEvent(_document,id,changedTime));
                    evt.Use();RepaintTime();
                }
                if(GUIUtility.hotControl!=_hotId)CancelDrag();
            }
            if(evt.type==EventType.ScrollWheel&&bounds.Contains(evt.mousePosition)){_horizontal.value=Mathf.Clamp(_horizontal.value+evt.delta.y*20+evt.delta.x*20,0,_horizontal.highValue);evt.Use();}
        }
    }
}
