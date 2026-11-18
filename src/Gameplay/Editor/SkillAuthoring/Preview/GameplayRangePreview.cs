using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public enum GameplayRangePreviewShapeKind { Circle, Sector, Line }
    /// <summary>Transient editor projection of current rule fields; never serialized or executed.</summary>
    public readonly struct GameplayRangePreviewShape
    {
        public readonly GameplayRangePreviewShapeKind Kind;
        public readonly string Label;
        public readonly float Radius, Angle, Length, Width, OffsetAngle;
        public readonly GameplaySearchGeometry Geometry;
        public GameplayRangePreviewShape(GameplayRangePreviewShapeKind kind, string label, float radius = 0,
            float angle = 0, float length = 0, float width = 0, float offsetAngle = 0, GameplaySearchGeometry geometry = null)
        { Kind = kind; Label = label; Radius = radius; Angle = angle; Length = length; Width = width; OffsetAngle = offsetAngle; Geometry = geometry; }
    }
    /// <summary>Author-only test inputs in world coordinates. They never modify a source asset.</summary>
    public sealed class GameplayRangeSimulation
    {
        public Vector3 Owner, Facing = Vector3.right, Target = new Vector3(2.2f, .6f, 1.4f), Point = new Vector3(1.4f, -.65f, .9f);
        public bool HasTarget = true, HasPoint = true;
        public string Caller = "";
    }
    /// <summary>Stable source identity. Providers receive a fresh serialized source on every read, including unsaved Undo changes.</summary>
    public readonly struct GameplayRangePreviewContext
    {
        public readonly Object Asset;
        public readonly string PropertyPath;
        public readonly int AbilityLevel;
        public readonly GameplayActorProfileAsset Profile;
        public readonly GameplayHitPose? HitPose;
        public readonly GameplayRangeSimulation Simulation;
        public readonly GameplaySkillDocumentAsset Document;
        public GameplayRangePreviewContext(Object asset, string propertyPath, int abilityLevel = 1, GameplayActorProfileAsset profile = null,
            GameplayHitPose? hitPose = null, GameplayRangeSimulation simulation = null, GameplaySkillDocumentAsset document = null)
        { Asset = asset; PropertyPath = propertyPath; AbilityLevel = abilityLevel; Profile = profile; HitPose = hitPose; Simulation = simulation; Document = document; }
    }
    public interface IGameplayRangePreviewProvider
    {
        bool Supports(GameplayRangePreviewContext context, SerializedProperty property);
        GameplayRangePreviewShape Read(GameplayRangePreviewContext context, SerializedProperty property);
    }
    /// <summary>Optional access to the already-evaluated model at the existing Timeline preview cursor.</summary>
    public interface IGameplayHitPosePreview
    {
        bool TryReadHitPose(GameplayHitAnchor anchor, out GameplayHitPose pose);
    }
    public static class GameplayRangePreview
    {
        public static IReadOnlyList<GameplayRangePreviewContext> Sources(Object asset)
        {
            var result = new List<GameplayRangePreviewContext>();
            if (asset == null) return result;
            using (var serialized = new SerializedObject(asset))
            {
                var root = new GameplayRangePreviewContext(asset, null);
                if (GameplayAuthoringProviders.Ranges.Any(p => p.Supports(root, null))) result.Add(root);
                foreach (var property in GameplayAuthoringProperties.Walk(serialized))
                {
                    var context = new GameplayRangePreviewContext(asset, property.propertyPath);
                    if (GameplayAuthoringProviders.Ranges.Any(p => p.Supports(context, property))) result.Add(context);
                }
            }
            return result;
        }
        public static bool TryRead(GameplayRangePreviewContext context, out GameplayRangePreviewShape shape)
            => TryRead(context, out shape, out _);
        public static bool TryRead(GameplayRangePreviewContext context, out GameplayRangePreviewShape shape, out string error)
        {
            error = null;
            shape = default;
            if (context.Asset == null) return false;
            using (var serialized = new SerializedObject(context.Asset))
            {
                var property = context.PropertyPath == null ? null : serialized.FindProperty(context.PropertyPath);
                if (context.PropertyPath != null && property == null) return false;
                foreach (var provider in GameplayAuthoringProviders.Ranges)
                    try
                    {
                        if (!provider.Supports(context, property)) continue;
                        shape = provider.Read(context, property);
                        return true;
                    }
                    catch (Exception ex) { error = context.PropertyPath + " · " + ex.Message; return false; }
            }
            return false;
        }
        /// <summary>+X schematic only; no Physics, targeting query or gameplay clock.</summary>
        public static void Draw(Rect rect, GameplayRangePreviewShape shape)
        {
            if (Event.current.type != EventType.Repaint) return;
            if(shape.Geometry!=null){CanonicalViews.GetValue(shape.Geometry, g => new GameplayRangeViewport()).Draw(rect, new[]{(shape.Geometry, new Color(.12f,.65f,.8f,.72f))}, shape.Geometry.Plane==GameplaySpatialPlane.XZ, null, null);return;}
            EditorGUI.DrawRect(rect, new Color(.1f, .12f, .14f));
            Vector2 origin = new Vector2(rect.x + rect.width * .35f, rect.center.y);
            float extent = Mathf.Max(.01f, shape.Kind == GameplayRangePreviewShapeKind.Line ? Mathf.Max(shape.Length, shape.Width) : shape.Radius);
            float scale = Mathf.Min(rect.width * .42f, rect.height * .42f) / extent;
            Vector3 Point(float distance, float degrees) => origin + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), -Mathf.Sin(degrees * Mathf.Deg2Rad)) * distance * scale;
            Color old = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = new Color(.2f, .8f, 1f);
                if (shape.Kind == GameplayRangePreviewShapeKind.Line)
                {
                    float a = shape.OffsetAngle;
                    Vector3 side = (Point(shape.Width / 2, a + 90) - (Vector3)origin);
                    Vector3 end = Point(shape.Length, a);
                    Handles.DrawAAPolyLine(2f, (Vector3)origin + side, end + side, end - side, (Vector3)origin - side, (Vector3)origin + side);
                }
                else
                {
                    float angle = shape.Kind == GameplayRangePreviewShapeKind.Circle ? 360 : shape.Angle;
                    var points = new Vector3[65];
                    for (int i = 0; i < points.Length; i++) points[i] = Point(shape.Radius, shape.OffsetAngle - angle / 2 + angle * i / 64f);
                    Handles.DrawAAPolyLine(2f, points);
                    if (angle < 360) Handles.DrawAAPolyLine(2f, points[0], origin, points[64]);
                }
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, origin, Point(extent, 0));
                Handles.Label(Point(extent, 0), "+X");
                Handles.Label(origin, "0");
            }
            finally { Handles.color = old; Handles.EndGUI(); }
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameplaySearchGeometry,GameplayRangeViewport> CanonicalViews
            = new System.Runtime.CompilerServices.ConditionalWeakTable<GameplaySearchGeometry,GameplayRangeViewport>();
    }

    /// <summary>One world-coordinate viewport and a cached, approximate raster of the exact Contains rule.</summary>
    internal sealed class GameplayRangeViewport
    {
        private const int Columns=52, Rows=32;
        private (GameplaySearchGeometry geometry, Color color)[] _items;
        private readonly List<(Rect rect, Color color)> _runs=new List<(Rect,Color)>();
        private bool _xz;
        private float _aspect, _slice, _halfWidth, _halfHeight;
        private Vector2 _center;

        public void Draw(Rect rect, (GameplaySearchGeometry geometry,Color color)[] items, bool xz, float? slice, GameplayRangeSimulation simulation)
        {
            if(Event.current.type!=EventType.Repaint||items.Length==0||rect.width<=0||rect.height<=0)return;
            var sample=items[0].geometry.Origin;
            float depth=slice??(xz?sample.Y:sample.Z);
            float aspect=rect.width/rect.height;
            if(_items==null||!_items.SequenceEqual(items)||_xz!=xz||_slice!=depth||!Mathf.Approximately(_aspect,aspect))
                Rebuild(items,xz,depth,aspect);
            EditorGUI.DrawRect(rect,new Color(.09f,.12f,.15f));
            foreach(var run in _runs)
                EditorGUI.DrawRect(new Rect(rect.x+run.rect.x*rect.width,rect.y+run.rect.y*rect.height,
                    run.rect.width*rect.width+.1f,run.rect.height*rect.height+.1f),run.color);
            foreach(var item in items)
            {
                var g=item.geometry;
                Mark(g.Origin,item.color,"");
                if(g.Shape!=GameplaySearchShape.Circle&&g.Shape!=GameplaySearchShape.GridCells&&g.Shape!=GameplaySearchShape.Single)
                {
                    var d=g.Direction;
                    float length=Mathf.Min(1,g.BoundsRadius*.35f);
                    Mark(new GameplayPoint(g.Origin.X+d.X*length,g.Origin.Y+d.Y*length,g.Origin.Z+d.Z*length),item.color,"→");
                }
            }
            if(simulation!=null)
            {
                var owner=simulation.Owner;
                Mark(new GameplayPoint(owner.x,owner.y,owner.z),Color.white,"自身");
                Mark(new GameplayPoint(owner.x+simulation.Facing.x,owner.y+simulation.Facing.y,owner.z+simulation.Facing.z),Color.white,"→");
                if(simulation.HasTarget) Mark(new GameplayPoint(simulation.Target.x,simulation.Target.y,simulation.Target.z),Color.yellow,"目标");
                if(simulation.HasPoint) Mark(new GameplayPoint(simulation.Point.x,simulation.Point.y,simulation.Point.z),Color.cyan,"地点");
            }
            string note=items.All(i=>i.geometry.Shape==GameplaySearchShape.Single)?"Single · 目标身份查询，无面积":
                $"{(xz?"XZ":"XY")} 切片 · {(xz?"Y":"Z")} = {depth:0.##} · 横向 {_halfWidth*2:0.##} 单位";
            GUI.Label(new Rect(rect.x+4,rect.y+4,rect.width-8,19),note,EditorStyles.whiteMiniLabel);

            void Mark(GameplayPoint point,Color color,string label)
            {
                Vector2 p=Project(point);
                p=new Vector2(rect.center.x+(p.x-_center.x)/_halfWidth*rect.width*.5f,
                    rect.center.y-(p.y-_center.y)/_halfHeight*rect.height*.5f);
                if(!rect.Contains(p))return;
                EditorGUI.DrawRect(new Rect(p.x-2,p.y-2,5,5),color);
                if(label.Length>0) GUI.Label(new Rect(Mathf.Min(p.x+4,rect.xMax-32),p.y-11,30,17),label,EditorStyles.whiteMiniLabel);
            }
        }

        private Vector2 Project(GameplayPoint point)=>new Vector2(point.X,_xz?point.Z:point.Y);

        private void Rebuild((GameplaySearchGeometry geometry,Color color)[] items,bool xz,float depth,float aspect)
        {
            _items=items;_xz=xz;_slice=depth;_aspect=aspect;
            _runs.Clear();
            var minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var maximum=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var item in items)
            {
                var geometry=item.geometry;
                float extent=Mathf.Max(.5f,geometry.BoundsRadius);
                Vector2 center=Project(geometry.Origin);
                minimum=Vector2.Min(minimum,center-Vector2.one*extent);
                maximum=Vector2.Max(maximum,center+Vector2.one*extent);
            }
            _center=(minimum+maximum)*.5f;
            Vector2 size=(maximum-minimum)*.55f;
            _halfHeight=Mathf.Max(size.y,size.x/aspect);
            _halfWidth=_halfHeight*aspect;
            foreach(var item in items)
            {
                if(item.geometry.Shape==GameplaySearchShape.Single)continue;
                for(int y=0;y<Rows;y++)
                {
                    float second=_center.y+(1-2*(y+.5f)/Rows)*_halfHeight;
                    int start=-1;
                    for(int x=0;x<=Columns;x++)
                    {
                        float first=_center.x+(2*(x+.5f)/Columns-1)*_halfWidth;
                        var point=xz?new GameplayPoint(first,depth,second):new GameplayPoint(first,second,depth);
                        bool inside=x<Columns&&item.geometry.Contains(item.geometry.SingleTarget,point);
                        if(inside&&start<0)start=x;
                        if(inside||start<0)continue;
                        _runs.Add((new Rect((float)start/Columns,(float)y/Rows,(float)(x-start)/Columns,1f/Rows),item.color));
                        start=-1;
                    }
                }
            }
        }
    }
}
