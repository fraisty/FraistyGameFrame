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
    /// <summary>Stable source identity. Providers receive a fresh serialized source on every read, including unsaved Undo changes.</summary>
    public readonly struct GameplayRangePreviewContext
    {
        public readonly Object Asset;
        public readonly string PropertyPath;
        public readonly int AbilityLevel;
        public readonly GameplayActorProfileAsset Profile;
        public GameplayRangePreviewContext(Object asset, string propertyPath, int abilityLevel = 1, GameplayActorProfileAsset profile = null)
        { Asset = asset; PropertyPath = propertyPath; AbilityLevel = abilityLevel; Profile = profile; }
    }
    public interface IGameplayRangePreviewProvider
    {
        bool Supports(GameplayRangePreviewContext context, SerializedProperty property);
        GameplayRangePreviewShape Read(GameplayRangePreviewContext context, SerializedProperty property);
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
                    if (provider.Supports(context, property)) {
                        try { shape = provider.Read(context, property); return true; }
                        catch (Exception ex) { error = ex.Message; return false; }
                    }
            }
            return false;
        }
        /// <summary>+X schematic only; no Physics, targeting query or gameplay clock.</summary>
        public static void Draw(Rect rect, GameplayRangePreviewShape shape)
        {
            if (Event.current.type != EventType.Repaint) return;
            if(shape.Geometry!=null){DrawCanonicalGeometry(rect,shape.Geometry);return;}
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
        private static void DrawCanonicalGeometry(Rect rect, GameplaySearchGeometry geometry)
        {
            EditorGUI.DrawRect(rect,new Color(.1f,.12f,.14f));
            if(geometry.Shape==GameplaySearchShape.Single){GUI.Label(rect,"Single：查询指定目标身份，不定义面积。");return;}
            float extent=Mathf.Max(1,geometry.BoundsRadius)*1.05f;
            const int columns=64,rows=40;
            float cellWidth=rect.width/columns,cellHeight=rect.height/rows;
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++){
                float px=((x+.5f)/columns*2-1)*extent,py=(1-(y+.5f)/rows*2)*extent;
                var point=geometry.Plane==GameplaySpatialPlane.XZ?new GameplayPoint(geometry.Origin.X+px,geometry.Origin.Y,geometry.Origin.Z+py):
                    new GameplayPoint(geometry.Origin.X+px,geometry.Origin.Y+py,geometry.Origin.Z);
                if(geometry.Contains(geometry.SingleTarget,point))EditorGUI.DrawRect(new Rect(rect.x+x*cellWidth,rect.y+y*cellHeight,cellWidth+.1f,cellHeight+.1f),new Color(.12f,.65f,.8f,.75f));
            }
            GUI.Label(new Rect(rect.x+4,rect.y+4,rect.width-8,20),geometry.Plane==GameplaySpatialPlane.ThreeDimensional?"3D / XY 中心切片（非整个立体）":geometry.Plane+" / 同一 Contains 规则");
            EditorGUI.DrawRect(new Rect(rect.center.x-1,rect.center.y-5,2,10),Color.white);
            EditorGUI.DrawRect(new Rect(rect.center.x-5,rect.center.y-1,10,2),Color.white);
        }
    }
}
