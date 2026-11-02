using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public interface IGameplayAnimationSourceInspector
    {
        bool Supports(UnityEngine.Object source);
        void DrawCore(SerializedObject source);
        void DrawAdvanced(SerializedObject source);
        string Summary(UnityEngine.Object source);
        float ClipDuration(UnityEngine.Object source);
        string BindingWarning(UnityEngine.Object source,GameObject subject);
        string[] SourceFields(UnityEngine.Object source);
    }
    /// <summary>Optional compact asset/clip picker for the shared animation card.
    /// Backend playback options remain in the right Inspector, without duplicate core controls.</summary>
    public interface IGameplayCompactAnimationSourceInspector
    {
        void DrawCompact(SerializedObject source);
        void DrawPlayback(SerializedObject source);
    }
    public static class GameplayAnimationSourceInspectors
    {
        private static readonly IGameplayAnimationSourceInspector[] Providers=TypeCache.GetTypesDerivedFrom<IGameplayAnimationSourceInspector>()
            .Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).Select(t=>(IGameplayAnimationSourceInspector)Activator.CreateInstance(t)).ToArray();
        public static IGameplayAnimationSourceInspector For(UnityEngine.Object source)=>Providers.FirstOrDefault(p=>p.Supports(source));
        /// <summary>The native Inspector and Workbench use the identical SDK controls, common
        /// playback fields and advanced section; no parallel animation parameter surface.</summary>
        public static void DrawShared(SerializedObject source)
        {
            var provider=For(source.targetObject);if(provider==null)return;source.UpdateIfRequiredOrScript();
            GUILayout.Label("动画资源 / 已安装后端",EditorStyles.boldLabel);provider.DrawCore(source);
            GUILayout.Label("播放 / 生命周期",EditorStyles.boldLabel);
            string[] common={"Speed","Priority","Channel","Loop","RestorePrevious"};
            foreach(var name in common){
                if((name=="Speed"||name=="Loop")&&provider.SourceFields(source.targetObject).Contains(name))continue;
                var property=source.FindProperty(name);
                if(name=="Loop")GameplayAuthoringInspector.Field(property,new GUIContent("循环播放"));
                else GameplayAuthoringInspector.Field(property);
            }
            string key="FRFrame.Animation.Fields."+GameplaySourceHandle.Capture(source.targetObject).Key;
            bool advanced=EditorGUILayout.Foldout(SessionState.GetBool(key,false),"变体 / 混合 / 高级动画参数",true);SessionState.SetBool(key,advanced);
            if(advanced)provider.DrawAdvanced(source);
            var covered=new System.Collections.Generic.HashSet<string>(provider.SourceFields(source.targetObject).Concat(common));
            var extra=source.GetIterator();var remaining=new System.Collections.Generic.List<string>();
            if(extra.NextVisible(true))do{if(extra.name!="m_Script"&&!covered.Contains(extra.name))remaining.Add(extra.propertyPath);}while(extra.NextVisible(false));
            bool details=EditorGUILayout.Foldout(SessionState.GetBool(key+".source",false),"高级来源 / 扩展字段",true);SessionState.SetBool(key+".source",details);
            if(details)foreach(var path in remaining)GameplayAuthoringInspector.Field(source.FindProperty(path));
            if(source.ApplyModifiedProperties())GameplayAuthoringSourceChanges.Notify(source.targetObject);
        }
        public static UnityEngine.Object Resolve(UnityEngine.Object source,string path)
        {
            if(source==null)return null;
            if(For(source)!=null)return source;
            using(var so=new SerializedObject(source)){
                var property=so.FindProperty(path??"");
                var animation=property?.propertyType==SerializedPropertyType.ManagedReference?property.FindPropertyRelative("Animation"):null;
                return animation?.propertyType==SerializedPropertyType.ObjectReference?animation.objectReferenceValue:null;
            }
        }
    }
}
