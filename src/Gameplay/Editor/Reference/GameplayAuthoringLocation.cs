using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>A source location, never execution data. Embedded identities survive array edits.</summary>
    public sealed class GameplayAuthoringLocation
    {
        private Object _source;
        private string _path;
        private GameplaySourceHandle _handle;
        public Object Source { get => _source ? _source : _handle?.Owner; set { if(_source!=value){_source=value;_handle=null;} } }
        public string Path { get => Handle.ResolvePath(); set { _path=value??"";_handle=null; } }
        public GameplaySourceHandle Handle => _handle ?? (_handle=GameplaySourceHandle.Capture(_source,_path??""));
        public string Title, Summary, Lane;
        /// <summary>Transient scope/terminal explanation; never an authored action or serialized graph state.</summary>
        public string FlowHint;
        public bool Animation;
        /// <summary>Canonical marker time for display only; null for non-marker locations.</summary>
        public float? Time;
        public GameplayNodeVisualKind Kind;
        public GameplaySequenceDefinitionAsset Sequence;
        /// <summary>Exact canonical marker path; time is display data, never marker identity. Null means no unique marker.</summary>
        private string _originMarkerPath;
        private GameplaySourceHandle _originMarker;
        public string OriginMarkerPath {
            get {if(_originMarkerPath==null)return null;return (_originMarker??(_originMarker=GameplaySourceHandle.Capture(Sequence,_originMarkerPath))).ResolvePath();}
            set {_originMarkerPath=value;_originMarker=null;}
        }
        public string Key => Handle.Key;
    }
}
