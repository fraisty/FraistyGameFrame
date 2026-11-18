using System;
using System.Collections.Generic;
using System.Globalization;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Builds the current serialized search definition, then calls its exact runtime request
    /// constructor. Preview inputs are explicit, transient test parameters, not a second skill source.</summary>
    public sealed class GameplayStandardRangePreview : IGameplayRangePreviewProvider
    {
        public bool Supports(GameplayRangePreviewContext context, SerializedProperty property)=>
            (property==null && context.Asset is GameplayAreaDefinitionAsset) ||
            (context.Asset is GameplayHitWindowDefinitionAsset && property?.propertyPath=="values") ||
            (property?.propertyType==SerializedPropertyType.ManagedReference &&
                property.managedReferenceValue is GameplaySearchTargetActionAuthoring or GameplayHitWindowData or GameplayAreaDefinitionData);
        public GameplayRangePreviewShape Read(GameplayRangePreviewContext context, SerializedProperty property) =>
            ReadSource(context, property?.propertyType==SerializedPropertyType.ManagedReference ?
                property.managedReferenceValue : (context.Asset as GameplayDefinitionAsset)?.CaptureGraphValues());

        /// <summary>Reads a standard search supplied by an external action without building its callbacks.</summary>
        public static GameplayRangePreviewShape ReadSearch(GameplayRangePreviewContext context, GameplaySearchTargetActionAuthoring search) =>
            ReadSource(context, search);

        private static GameplayRangePreviewShape ReadSource(GameplayRangePreviewContext context, object source)
        {
            var simulation=context.Simulation??new GameplayRangeSimulation();
            var direction=new Extensions.GameplayDirection(simulation.Facing.x,simulation.Facing.y,simulation.Facing.z);
            var owner=Point(simulation.Owner);
            if(source is GameplayHitWindowData window)
            {
                var definition=window.PreviewDefinition();
                if(definition.Anchor.Kind!=GameplayHitAnchorKind.OwnerRoot&&!context.HitPose.HasValue)
                    throw new InvalidOperationException("当前姿态缺少所选挂点或骨骼。请选择匹配的动画模型并移动编排光标。");
                var pose=context.HitPose??new GameplayHitPose(owner,direction);
                if(context.HitPose.HasValue)
                {
                    var basis=Quaternion.FromToRotation(Vector3.right,simulation.Facing);
                    var p=pose.Position;
                    pose=new GameplayHitPose(Point(simulation.Owner+basis*new Vector3(p.X,p.Y,p.Z)),
                        Rotate(pose.Forward,basis),Rotate(pose.Up,basis),pose.Revision);
                }
                if (!definition.AcceptsPose(pose))
                    throw new InvalidOperationException("所选锚点的前向无法投影到窗口平面。");
                var hit=definition.Geometry(pose);
                string label=context.HitPose.HasValue?"光标处模型姿态 · 单帧体积":"角色根节点 · 静态体积";
                return new GameplayRangePreviewShape(GameplayRangePreviewShapeKind.Sector,
                    $"有效窗 {definition.Duration:0.###}s · {label} · {hit.Plane}", geometry:hit);
            }
            using(var world=new GameplayStandardWorld())
            {
                var actor=world.CreateActor(owner,1);
                actor.Facing=direction;
                if(context.Profile!=null)context.Profile.Build(new GameplayAuthoringBuildContext()).Initialize(actor);
                else{
                    actor.Attributes.Register(new GameplayAttributeId("Attack"),100);
                    actor.Attributes.Register(new GameplayAttributeId("Health"),10000);
                    actor.Attributes.Register(new GameplayAttributeId("MaxHealth"),10000);
                    actor.Attributes.Register(new GameplayAttributeId("Mana"),100);
                    actor.Attributes.Register(new GameplayAttributeId("Shield"),0);
                    actor.Attributes.Register(new GameplayAttributeId("Defense"),0);
                }
                var target=simulation.HasTarget?world.CreateActor(Point(simulation.Target),2):null;
                var entry=context.Document??context.Asset as GameplaySkillDocumentAsset;
                var scalarDefaults=new Dictionary<GameplayTag,float>();
                if(entry?.Defaults!=null)foreach(var item in entry.Defaults)if(item!=null)scalarDefaults.Add(item.Key.Build(),item.Value);
                foreach(string line in simulation.Caller.Split('\n'))
                {
                    if(string.IsNullOrWhiteSpace(line))continue;
                    int equals=line.IndexOf('=');
                    if(equals<1||!float.TryParse(line.Substring(equals+1),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))
                        throw new FormatException("Caller 每行需要一个有效的 标签=数值。");
                    scalarDefaults[new GameplayTag(line.Substring(0,equals).Trim())]=value;
                }
                var defaults=new GameplayContextValues(scalarDefaults);
                var input=new GameplayContext(actor.Id,new GameplayTargetData(target?.Id??default,simulation.HasPoint?Point(simulation.Point):(GameplayPoint?)null),
                    values:defaults,abilityLevel:Math.Max(1,context.AbilityLevel));
                GameplaySearchGeometry geometry;
                string label;
                if(source is GameplayAreaDefinitionData area)
                {
                    geometry=area.PreviewGeometry(actor,input);
                    label=area.FollowOwner?"区域 · 初始范围 · 施放后跟随持有者":"区域 · 初始范围";
                }
                else
                {
                    var search=(GameplaySearchTargetActionAuthoring)source;
                    if(!search.TryPreviewRequest(input,actor,out var request))
                        throw new InvalidOperationException("当前输入缺少有效原点、目标或方向，或两个端点重合。");
                    geometry=request.Geometry;
                    label=search.RepeatFromResult?"搜索 · 首次查询 · 后续命中后重选":"搜索 · 查询范围";
                if (search.Faction is GameplayFactionFilter.Neutrals or GameplayFactionFilter.NonAllied)
                    label+=" · 阵营关系由运行宿主提供";
                }
                return new GameplayRangePreviewShape(GameplayRangePreviewShapeKind.Sector,label,geometry.Radius,
                    geometry.Angle,geometry.Length,geometry.Width,geometry:geometry);
            }
        }
        private static GameplayPoint Point(Vector3 value)=>new GameplayPoint(value.x,value.y,value.z);
        private static Extensions.GameplayDirection Rotate(Extensions.GameplayDirection value,Quaternion rotation)
        {
            if(!value.IsValid)return default;
            var direction=rotation*new Vector3(value.X,value.Y,value.Z);
            return new Extensions.GameplayDirection(direction.x,direction.y,direction.z);
        }
    }
}
