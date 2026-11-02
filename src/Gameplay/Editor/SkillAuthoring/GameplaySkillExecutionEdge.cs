using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>GraphView's native hit-tested curve, with distinct scheduled/inline business
    /// connections. Source ports and the existing GraphView deletion/selection still own it.</summary>
    internal sealed class GameplaySkillExecutionEdge : Edge
    {
        internal bool Scheduled,Config;
        // Curves remain visible across a card, but its fields, markers and native sockets own input there.
        private bool OverCard(Vector2 point){var graph=GetFirstAncestorOfType<GameplayBehaviorGraphView>();return graph!=null&&graph.IsCardPoint(this.ChangeCoordinatesTo(graph.contentViewContainer,point));}
        public override bool ContainsPoint(Vector2 localPoint)=>!OverCard(localPoint)&&base.ContainsPoint(localPoint);
        protected override EdgeControl CreateEdgeControl()=>new BusinessCurve(this);
        private sealed class BusinessCurve:EdgeControl
        {
            private readonly GameplaySkillExecutionEdge _owner;
            public override bool ContainsPoint(Vector2 localPoint)=>!_owner.OverCard(this.ChangeCoordinatesTo(_owner,localPoint))&&base.ContainsPoint(localPoint);
            internal BusinessCurve(GameplaySkillExecutionEdge owner)
            {
                _owner=owner;generateVisualContent+=ctx=>{
                    var points=controlPoints;if(points==null||points.Length<4)return;
                    if(owner.Config){
                        var painter=ctx.painter2D;painter.strokeColor=inputColor;painter.lineWidth=4;
                        for(int i=0;i<40;i+=2){
                            Vector2 At(float t){float u=1-t;return u*u*u*points[0]+3*u*u*t*points[1]+3*u*t*t*points[2]+t*t*t*points[3]-layout.position;}
                            painter.BeginPath();painter.MoveTo(At(i/40f));painter.LineTo(At((i+1)/40f));painter.Stroke();
                        }
                    }
                    var direction=(points[3]-points[2]).normalized;var tip=points[3]-layout.position-direction*9;var normal=new Vector2(-direction.y,direction.x);
                    var paint=ctx.painter2D;paint.fillColor=owner.Scheduled?new Color(.94f,.68f,.3f):inputColor;paint.BeginPath();paint.MoveTo(tip);
                    paint.LineTo(tip-direction*(owner.Scheduled?6:9)+normal*4);
                    if(owner.Scheduled)paint.LineTo(tip-direction*12);
                    paint.LineTo(tip-direction*(owner.Scheduled?6:9)-normal*4);paint.ClosePath();paint.Fill();
                };
            }
        }
    }
}
