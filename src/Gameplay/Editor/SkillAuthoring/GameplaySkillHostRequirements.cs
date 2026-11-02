using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Cheap typed host metadata over current values and explicitly shared resources.
    /// It neither discovers project assets nor creates a spatial backend.</summary>
    public static class GameplaySkillHostRequirements
    {
        public static IEnumerable<GameplaySpatialPlane> Planes(GameplaySkillDocumentAsset document)
        {
            foreach(var action in document.AuthoringGraph.Nodes.Select(n=>(n.Payload as GameplayActionNodePayload)?.Action).OfType<GameplaySearchTargetActionAuthoring>())yield return action.Plane;
            foreach(var value in document.AuthoringGraph.LocalDefinitions.Select(d=>d.Payload)){
                if(value is GameplayProjectileDefinitionData projectile)yield return projectile.Plane;
                if(value is GameplayAreaDefinitionData area)yield return area.Plane;
            }
            foreach(var value in document.AuthoringGraph.Nodes.SelectMany(n=>n.Payload.Definitions).Concat(document.AuthoringGraph.LocalDefinitions.SelectMany(d=>d.Definitions)).Select(b=>b.External)){
                if(value is GameplayProjectileDefinitionAsset projectile)yield return projectile.Plane;
                if(value is GameplayAreaDefinitionAsset area)yield return area.Plane;
            }
        }
        public static string Physics2DReason(GameplaySkillDocumentAsset document)=>Planes(document).Any(p=>p!=GameplaySpatialPlane.XY)?"此技能使用 XZ / 3D 范围；Physics2D 仅支持 XY，请选择 Logical / Physics3D Host。":null;
    }
}
