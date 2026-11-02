using System;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>Editor operations on the Sequence asset itself; tracks are only type projections.</summary>
    public static class GameplaySequenceTimelineModel
    {
        public const int PreviewFramesPerSecond = 60;
        public const int TrackCount = 3;

        /// <summary>One mouse gesture owns one Undo record. Movement is a transient preview;
        /// only MouseUp writes the source, so an intervening Undo can restore it unambiguously.</summary>
        public sealed class MarkerDrag : IDisposable
        {
            private readonly GameplaySequenceDefinitionAsset _asset;
            private readonly int _fps;
            private readonly GameplaySourceHandle _marker;
            private readonly float _original;
            private bool _ended;
            private float _previewTime;

            /// <summary>Time presented by the active gesture; not a serialized authoring value.</summary>
            public float PreviewTime => _previewTime;

            internal MarkerDrag(GameplaySequenceDefinitionAsset asset, int index, int fps)
            {
                _asset = asset; _fps=fps;
                _marker = GameplaySourceHandle.Capture(asset,"Markers.Array.data["+index+"]");
                _original = asset.Markers[index].Time;
                _previewTime = _original;
            }

            public void Update(float seconds, bool snap)
            {
                if (_ended) return;
                int index=CurrentIndex;
                if(index<0){Cancel();return;}
                _previewTime = Mathf.Clamp(Snap(seconds, snap, _fps), 0f, Mathf.Max(0f, _asset.Duration));
            }

            /// <summary>Current slot of the original marker, or -1 after its identity was removed.</summary>
            public int CurrentIndex {get{
                if(!_marker.TryResolve(out string path))return -1;
                int start=path.LastIndexOf('['),end=path.LastIndexOf(']');
                return start>=0&&end>start&&int.TryParse(path.Substring(start+1,end-start-1),out int index)?index:-1;
            }}

            public void Cancel() { _ended=true; }
            public void End()
            {
                if (_ended) return;
                _ended = true;
                int index=CurrentIndex;if(index<0||!_asset)return;
                // Another editor/Undo may already have changed the same field. Never overwrite
                // its restored value with a gesture that has not been committed.
                if(!Mathf.Approximately(_asset.Markers[index].Time,_original)||Mathf.Approximately(_previewTime,_original))return;
                Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(_asset,"移动序列标记");
                WriteTime(_asset,index,_previewTime,false,false,_fps);
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();
            }
            public void Dispose() => End();
        }

        public static int Track(GameplaySequenceMarkerAuthoring marker) =>
            marker is GameplaySequenceApplyEffectMarkerAuthoring ? 0 :
            marker is GameplaySequenceCueMarkerAuthoring ? 1 :
            marker is GameplaySequenceSkillStepMarkerAuthoring ? 2 : -1;

        public static float Snap(float seconds, bool enabled, int fps=PreviewFramesPerSecond) =>
            enabled ? Mathf.Round(seconds * Math.Max(1,fps)) / (float)Math.Max(1,fps) : seconds;

        public static void SetTime(GameplaySequenceDefinitionAsset asset, int index, float seconds, bool snap, int fps=PreviewFramesPerSecond)
        {
            WriteTime(asset, index, seconds, snap, true, fps);
        }

        public static MarkerDrag BeginDrag(GameplaySequenceDefinitionAsset asset, int index, int fps=PreviewFramesPerSecond)
        {
            ValidateIndex(asset, index);
            return new MarkerDrag(asset, index, fps);
        }

        private static void ValidateIndex(GameplaySequenceDefinitionAsset asset, int index)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (index < 0 || index >= asset.Markers.Length) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void WriteTime(GameplaySequenceDefinitionAsset asset, int index, float seconds, bool snap, bool recordUndo, int fps)
        {
            ValidateIndex(asset, index);
            float value = Mathf.Clamp(Snap(seconds, snap, fps), 0f, Mathf.Max(0f, asset.Duration));
            if (Mathf.Approximately(asset.Markers[index].Time, value)) return;
            if (recordUndo) Undo.RecordObject(asset, "移动序列标记");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("Markers").GetArrayElementAtIndex(index).FindPropertyRelative("Time").floatValue = value;
            if(serialized.ApplyModifiedPropertiesWithoutUndo()) GameplayAuthoringSourceChanges.Notify(asset,"Markers.Array.data["+index+"].Time");
            EditorUtility.SetDirty(asset);
        }

        public static int Add(GameplaySequenceDefinitionAsset asset, GameplaySequenceMarkerAuthoring marker)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            Undo.RecordObject(asset, "添加序列标记");
            var source = asset.Markers ?? Array.Empty<GameplaySequenceMarkerAuthoring>();
            var result = new GameplaySequenceMarkerAuthoring[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = marker;
            asset.Markers = result;
            GameplayAuthoringSourceChanges.Notify(asset,"Markers");
            EditorUtility.SetDirty(asset);
            return source.Length;
        }

        public static int Duplicate(GameplaySequenceDefinitionAsset asset, int index)
        {
            if (asset == null || index < 0 || index >= asset.Markers.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var serialized = new SerializedObject(asset);
            var markers = serialized.FindProperty("Markers");
            var source = markers.GetArrayElementAtIndex(index).managedReferenceValue;
            var copy = Activator.CreateInstance(source.GetType());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), copy);
            Undo.RecordObject(asset, "复制序列标记");
            markers.InsertArrayElementAtIndex(index + 1);
            markers.GetArrayElementAtIndex(index + 1).managedReferenceValue = copy;
            if(serialized.ApplyModifiedProperties()) GameplayAuthoringSourceChanges.Notify(asset,"Markers");
            EditorUtility.SetDirty(asset);
            return index + 1;
        }

        public static void Delete(GameplaySequenceDefinitionAsset asset, int index)
        {
            if (asset == null || index < 0 || index >= asset.Markers.Length) throw new ArgumentOutOfRangeException(nameof(index));
            GameplaySharedAuthoringCommands.Remove(asset,"Markers.Array.data["+index+"]");
        }
    }
}
