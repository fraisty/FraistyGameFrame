using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Canonical creation commands shared by the button palette and GraphView context
    /// menu. First-party/Common and game-owned creators populate both without nested popup windows.</summary>
    public interface IGameplaySkillCreationMenu
    {
        void AddItem(GUIContent caption,bool selected,GenericMenu.MenuFunction command);
        void AddDisabledItem(GUIContent caption);
    }
    internal sealed class GameplaySkillCreationMenu:IGameplaySkillCreationMenu
    {
        private readonly GenericMenu _generic;
        private readonly DropdownMenu _context;
        private readonly System.Action<GUIContent,GenericMenu.MenuFunction> _inline;
        internal GameplaySkillCreationMenu(GenericMenu menu)=>_generic=menu;
        internal GameplaySkillCreationMenu(DropdownMenu menu)=>_context=menu;
        private GameplaySkillCreationMenu(System.Action<GUIContent,GenericMenu.MenuFunction> add)=>_inline=add;
        public void AddItem(GUIContent caption,bool selected,GenericMenu.MenuFunction command)
        {
            if(_generic!=null)_generic.AddItem(caption,selected,command);
            else if(_context!=null)_context.AppendAction(caption.text,_=>command(),selected?DropdownMenuAction.Status.Checked:DropdownMenuAction.Status.Normal);
            else _inline(caption,command);
        }
        public void AddDisabledItem(GUIContent caption){if(_generic!=null)_generic.AddDisabledItem(caption);else if(_context!=null)_context.AppendAction(caption.text,null,DropdownMenuAction.Status.Disabled);else _inline(caption,null);}
        /// <summary>A temporary, searchable creation menu in the existing canvas. It survives
        /// native window focus transitions and shares exactly the context menu's commands.</summary>
        internal static void Show(VisualElement view,string title,System.Action<IGameplaySkillCreationMenu> build,Vector2? worldAnchor=null)
        {
            view.Q<VisualElement>("canvas-action-menu")?.RemoveFromHierarchy();
            var panel=new VisualElement{name="canvas-action-menu",focusable=true};
            panel.style.maxHeight=Mathf.Max(100,view.layout.height-44);
            panel.style.position=Position.Absolute;panel.style.width=350;panel.style.maxWidth=Length.Percent(95);panel.style.backgroundColor=new Color(.18f,.2f,.24f);panel.style.paddingLeft=6;panel.style.paddingRight=6;
            panel.style.paddingTop=6;panel.style.paddingBottom=6;panel.style.borderTopWidth=1;panel.style.borderTopColor=new Color(.4f,.6f,.8f);
            var anchor=worldAnchor.HasValue?view.WorldToLocal(worldAnchor.Value):new Vector2(12,40);
            panel.style.left=Mathf.Clamp(anchor.x,4,Mathf.Max(4,view.layout.width-362));panel.style.top=Mathf.Clamp(anchor.y,36,Mathf.Max(36,view.layout.height-440));
            var header=new VisualElement();header.style.flexDirection=FlexDirection.Row;panel.Add(header);
            var caption=new Label(title);caption.style.flexGrow=1;caption.style.whiteSpace=WhiteSpace.Normal;header.Add(caption);header.Add(new Button(()=>panel.RemoveFromHierarchy()){text="×"});
            var filter=new TextField{tooltip="按中文名称筛选当前 Scope 可创建的正式配置"};panel.Add(filter);
            var note=new Label("仅写入本入口说明的真实配置 · ↑↓ 选择 / Enter 执行 / Esc 关闭 · ☆ 收藏");note.style.fontSize=10;note.style.whiteSpace=WhiteSpace.Normal;panel.Add(note);
            var list=new ScrollView();list.style.maxHeight=Mathf.Min(330,Mathf.Max(40,view.layout.height-190));list.style.minHeight=40;panel.Add(list);
            var rows=new List<(string name,VisualElement row,Button button,GenericMenu.MenuFunction command)>();
            string recentKey="FRFrame.Authoring."+Application.dataPath+".RecentChoices";
            var recents=EditorPrefs.GetString(recentKey,"").Split('|').Where(n=>n.Length>0).ToList();string recent=recents.FirstOrDefault()??"";int focused=0;
            build(new GameplaySkillCreationMenu((label,command)=>{
                var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;
                string key="FRFrame.Authoring.Favorite."+label.text;
                var star=new Button();star.text=EditorPrefs.GetBool(key,false)?"★":"☆";star.style.width=24;
                star.clicked+=()=>{bool value=!EditorPrefs.GetBool(key,false);EditorPrefs.SetBool(key,value);star.text=value?"★":"☆";};row.Add(star);
                var button=new Button(()=>Pick(label.text,command)){text=label.text.Replace("/"," · "),tooltip=label.tooltip};
                button.userData=(Action)(()=>Pick(label.text,command));
                button.style.height=30;button.style.flexGrow=1;button.style.unityTextAlign=TextAnchor.MiddleLeft;row.Add(button);
                button.SetEnabled(command!=null);
                rows.Add((label.text,row,button,command));
            }));
            rows=rows.OrderByDescending(r=>EditorPrefs.GetBool("FRFrame.Authoring.Favorite."+r.name,false)).ThenByDescending(r=>r.name==recent).ToList();
            foreach(var row in rows)list.Add(row.row);
            string Category(string name)=>name.Split('/')[0].Trim();
            var categories=new[]{"全部","收藏","最近"}.Concat(rows.Select(r=>Category(r.name)).Distinct().OrderBy(n=>n,StringComparer.Ordinal)).ToList();
            var category=new PopupField<string>("业务分类",categories,0);panel.Insert(1,category);
            var help=new Label();help.style.whiteSpace=WhiteSpace.Normal;help.style.fontSize=11;panel.Add(help);
            void Highlight(){var visible=rows.Where(r=>r.row.style.display.value!=DisplayStyle.None).ToArray();focused=Mathf.Clamp(focused,0,Math.Max(0,visible.Length-1));
                foreach(var row in rows)row.row.style.backgroundColor=Color.clear;
                if(visible.Length>0){visible[focused].row.style.backgroundColor=new Color(.25f,.32f,.4f);help.text=visible[focused].button.tooltip;list.ScrollTo(visible[focused].row);}else help.text="当前 Scope 没有匹配项。";
            }
            void Pick(string name,GenericMenu.MenuFunction command){if(command==null)return;recents.Remove(name);recents.Insert(0,name);EditorPrefs.SetString(recentKey,string.Join("|",recents.Take(6)));panel.RemoveFromHierarchy();command();}
            void Filter(){
                list.Clear();var groups=new Dictionary<string,Foldout>();
                foreach(var row in rows){
                    bool favorite=EditorPrefs.GetBool("FRFrame.Authoring.Favorite."+row.name,false);
                    bool categoryMatch=category.value=="全部"||category.value=="收藏"&&favorite||category.value=="最近"&&recents.Contains(row.name)||Category(row.name)==category.value;
                    bool match=categoryMatch&&(row.name+" "+row.button.tooltip).IndexOf(filter.value??"",StringComparison.OrdinalIgnoreCase)>=0;
                    row.row.style.display=match?DisplayStyle.Flex:DisplayStyle.None;
                    if(!match)continue;
                    if(string.IsNullOrWhiteSpace(filter.value)&&category.value!="收藏"&&category.value!="最近"){
                        var parts=row.name.Split('/');VisualElement parent=list;string path="";
                        for(int i=0;i<parts.Length-1;i++){
                            path+=(i>0?"/":"")+parts[i];
                            if(!groups.TryGetValue(path,out var group)){group=new Foldout{text=parts[i],value=true};groups.Add(path,group);parent.Add(group);}parent=group;
                        }
                        row.button.text=parts.Last();parent.Add(row.row);
                    }else{row.button.text=row.name.Replace("/"," · ");list.Add(row.row);}
                }
                focused=0;Highlight();
            }
            filter.RegisterValueChangedCallback(_=>Filter());category.RegisterValueChangedCallback(_=>Filter());
            EventCallback<PointerDownEvent> dismiss=e=>{if(!panel.Contains(e.target as VisualElement))panel.RemoveFromHierarchy();};
            view.RegisterCallback(dismiss,TrickleDown.TrickleDown);
            panel.RegisterCallback<DetachFromPanelEvent>(_=>view.UnregisterCallback(dismiss,TrickleDown.TrickleDown));
            panel.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());panel.RegisterCallback<KeyDownEvent>(e=>{
                if(e.keyCode==KeyCode.Escape){panel.RemoveFromHierarchy();e.StopImmediatePropagation();}
                else if(e.keyCode==KeyCode.DownArrow||e.keyCode==KeyCode.UpArrow){focused+=e.keyCode==KeyCode.DownArrow?1:-1;Highlight();e.StopImmediatePropagation();}
                else if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter){var visible=rows.Where(r=>r.row.style.display.value!=DisplayStyle.None).ToArray();if(visible.Length>0)Pick(visible[Mathf.Clamp(focused,0,visible.Length-1)].name,visible[Mathf.Clamp(focused,0,visible.Length-1)].command);e.StopImmediatePropagation();}
            },TrickleDown.TrickleDown);
            view.hierarchy.Add(panel);panel.BringToFront();Filter();filter.Focus();
            panel.RegisterCallback<GeometryChangedEvent>(_=>{
                panel.style.left=Mathf.Clamp(anchor.x,4,Mathf.Max(4,view.layout.width-panel.layout.width-4));
                panel.style.top=Mathf.Clamp(anchor.y,36,Mathf.Max(36,view.layout.height-panel.layout.height-4));
            });
        }
    }
}
