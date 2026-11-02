using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Optional integration-owned availability checks used before a creation/picker
    /// write. Missing runtime/editor resource adapters are unavailable, not empty saved shells.</summary>
    public interface IGameplaySkillTypeAvailability {string Unavailable(GameplaySkillDocumentAsset document,Type type);}
    public static class GameplaySkillTypeAvailability
    {
        private static readonly IGameplaySkillTypeAvailability[] Providers=TypeCache.GetTypesDerivedFrom<IGameplaySkillTypeAvailability>().Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).Select(t=>(IGameplaySkillTypeAvailability)Activator.CreateInstance(t)).ToArray();
        public static string Reason(GameplaySkillDocumentAsset document,Type type)
        {
            if(type==null)return null;
            foreach(var provider in Providers){var reason=provider.Unavailable(document,type);if(reason!=null)return reason;}
            if(typeof(GameplaySkillActionAuthoring).IsAssignableFrom(type)){
                var action=Activator.CreateInstance(type);foreach(var slot in GameplayGraphFields.ResourceSlots(action))
                    if(!GameplaySkillFieldSchema.Values(slot.ValueType).Any()&&slot.ValueType!=typeof(GameplaySkillDocumentAsset)&&slot.ValueType!=typeof(GameplayActorProfileAsset)&&slot.ValueType!=typeof(GameplaySkillDefinitionAsset))return "此资源没有已安装的配置 / 运行后端："+slot.ValueType.Name;
            }return null;
        }
        internal static void Add(IGameplaySkillCreationMenu menu,GameplaySkillDocumentAsset doc,Type type,UnityEngine.GUIContent label,GenericMenu.MenuFunction action)
        {var reason=Reason(doc,type);if(reason!=null)menu.AddDisabledItem(new UnityEngine.GUIContent(label.text+" · 不可用",reason));else menu.AddItem(label,false,()=>{var current=Reason(doc,type);if(current!=null)throw new InvalidOperationException(current);action();});}
    }
}
