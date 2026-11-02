using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Builds the current serialized search definition, then calls its exact runtime request
    /// constructor. Preview inputs are explicit, transient test parameters, not a second skill source.</summary>
    public sealed class GameplayStandardRangePreview : IGameplayRangePreviewProvider
    {
        public bool Supports(GameplayRangePreviewContext context, SerializedProperty property)=>
            property?.propertyType==SerializedPropertyType.ManagedReference && property.managedReferenceValue is GameplaySearchTargetActionAuthoring;
        public GameplayRangePreviewShape Read(GameplayRangePreviewContext context, SerializedProperty property)
        {
            var source=(GameplaySearchTargetActionAuthoring)property.managedReferenceValue;
            using(var world=new GameplayStandardWorld())
            {
                var actor=world.CreateActor(default,1);
                if(context.Profile!=null)context.Profile.Build(new GameplayAuthoringBuildContext()).Initialize(actor);
                else{
                    actor.Attributes.Register(new GameplayAttributeId("Attack"),100);
                    actor.Attributes.Register(new GameplayAttributeId("Health"),10000);
                    actor.Attributes.Register(new GameplayAttributeId("MaxHealth"),10000);
                    actor.Attributes.Register(new GameplayAttributeId("Mana"),100);
                    actor.Attributes.Register(new GameplayAttributeId("Shield"),0);
                    actor.Attributes.Register(new GameplayAttributeId("Defense"),0);
                }
                var target=world.CreateActor(new GameplayPoint(3,0,3),2);
                var entry=AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(context.Asset)) as GameplaySkillDocumentAsset;
                var scalarDefaults=new List<KeyValuePair<GameplayTag,float>>();
                if(entry?.Defaults!=null)foreach(var item in entry.Defaults)if(item!=null)scalarDefaults.Add(new KeyValuePair<GameplayTag,float>(item.Key.Build(),item.Value));
                var defaults=new GameplayContextValues(scalarDefaults);
                var input=new GameplayContext(actor.Id,new GameplayTargetData(target.Id,target.Position),values:defaults,abilityLevel:Math.Max(1,context.AbilityLevel));
                var action=(GameplaySearchTargetAction)source.Build(new GameplayAuthoringBuildContext());
                if(!action.TryCreateRequest(input,actor,out var request))throw new InvalidOperationException("当前预览输入缺少合法搜索原点/方向，或两个端点重合。");
                var geometry=request.Geometry;
                return new GameplayRangePreviewShape(GameplayRangePreviewShapeKind.Sector,"正式范围 / "+geometry.Shape,geometry.Radius,
                    geometry.Angle,geometry.Length,geometry.Width,geometry:geometry);
            }
        }
    }
}
