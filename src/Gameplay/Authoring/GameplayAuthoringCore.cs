using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    public enum GameplayAuthoringIssueSeverity { Error, Warning }

    /// <summary>A location-aware content diagnostic, independent of UnityEditor.</summary>
    public readonly struct GameplayAuthoringValidationIssue
    {
        public readonly GameplayAuthoringIssueSeverity Severity;
        public readonly string Path;
        public readonly string Message;
        public GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity severity, string path, string message)
        { Severity = severity; Path = path; Message = message; }
        public override string ToString() => $"{Severity} at {Path}: {Message}";
    }

    /// <summary>Collects path-aware errors for an asset graph; Asset detects recursive references without building runtime state.</summary>
    public sealed class GameplayAuthoringValidationContext
    {
        internal Func<GameplayDefinitionAsset, string, bool> GraphAssetValidation;
        private readonly List<GameplayAuthoringValidationIssue> _issues = new List<GameplayAuthoringValidationIssue>();
        private readonly HashSet<GameplayDefinitionAsset> _active = new HashSet<GameplayDefinitionAsset>();
        private readonly HashSet<(GameplayDefinitionAsset, string)> _validated = new HashSet<(GameplayDefinitionAsset, string)>();
        private readonly HashSet<(string, string)> _reported = new HashSet<(string, string)>();
        public IReadOnlyList<GameplayAuthoringValidationIssue> Issues => _issues;
        public bool HasErrors => _issues.Exists(issue => issue.Severity == GameplayAuthoringIssueSeverity.Error);
        /// <summary>Coalesces identical diagnostics at the same path, retaining diagnostics at every distinct source location.</summary>
        public void Error(string path, string message)
        {
            if (_reported.Add((path, message)))
                _issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error, path, message));
        }
        /// <summary>Non-blocking author guidance, displayed beside content errors in the workbench.</summary>
        public void Warning(string path,string message)
        {
            if(_reported.Add((path,message)))_issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning,path,message));
        }
        public void Asset(GameplayDefinitionAsset asset, string path)
        {
            if (GraphAssetValidation != null && GraphAssetValidation(asset, path)) return;
            if (asset == null) { Error(path, "Missing asset reference."); return; }
            if (_active.Contains(asset)) { Error(path, "Circular asset reference."); return; }
            if (!_validated.Add((asset, path))) return;
            _active.Add(asset);
            try { asset.Validate(this, path); }
            finally { _active.Remove(asset); }
        }
        public void Tag(GameplayTagReference tag, string path)
        {
            try { tag.Build(); } catch (ArgumentException ex) { Error(path, ex.Message); }
        }
        public void Attribute(GameplayAttributeReference attribute, string path)
        {
            try { attribute.Build(); } catch (ArgumentException ex) { Error(path, ex.Message); }
        }
        public void Enum<T>(T value, string path) where T : struct
        { if (!System.Enum.IsDefined(typeof(T), value)) Error(path, $"Unsupported {typeof(T).Name}: {value}."); }
        public void Finite(float value, string path)
        { if (!float.IsFinite(value)) Error(path, "Value must be finite."); }
    }

    /// <summary>Static serialized definition; runtime state and built definitions live outside the asset.</summary>
    public abstract class GameplayDefinitionAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        [SerializeField] private string definitionId;
        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        public string DefinitionId => definitionId;
        /// <summary>Optional author-facing capability limits; not serialized gameplay state.</summary>
        public virtual string CapabilityNote=>null;
        public int SchemaVersion => schemaVersion;
        [SerializeField, HideInInspector] private bool migrationHistoryOnly;
        /// <summary>Preserved migration material is inspectable but cannot be edited as a
        /// second skill source. Actual shared runtime resources never receive this marker.</summary>
        public bool MigrationHistoryOnly => migrationHistoryOnly;
#if UNITY_EDITOR
        public void MarkMigrationHistoryOnly(bool historyOnly=true) => migrationHistoryOnly = historyOnly;
#endif
        /// <summary>Exports plain authoring values for the explicit V1 migrator or conversion
        /// between a local definition and a deliberately shared library definition. It does not
        /// create an SO or mutate this source; unsupported definition families fail explicitly.</summary>
        public virtual Graph.GameplayLocalDefinitionPayload CaptureGraphValues() =>
            throw new NotSupportedException("No V2 value builder for " + GetType().FullName);
        /// <summary>Explicit shared-resource compilation seam used by V2 reference bindings.
        /// Concrete sources delegate to their existing identity-cached immutable builder.</summary>
        public virtual object BuildSharedDefinition(GameplayAuthoringBuildContext context) =>
            throw new NotSupportedException("No shared definition builder for " + GetType().FullName);

        public void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            if (string.IsNullOrWhiteSpace(definitionId)) validation.Error(path + ".DefinitionId", "DefinitionId is required.");
            if (schemaVersion != CurrentSchemaVersion) validation.Error(path + ".SchemaVersion", $"Unsupported schema {schemaVersion}; expected {CurrentSchemaVersion}.");
            ValidateContent(validation, path);
        }
        protected abstract void ValidateContent(GameplayAuthoringValidationContext validation, string path);

        /// <summary>Explicit editor-callable seam. No older schema exists yet, so no asset is mutated.</summary>
        public virtual bool TryMigrateOneVersion() => false;

        /// <summary>
        /// Completes exactly one schema migration step after a derived asset has migrated its own fields.
        /// Keeps the serialized version private while making the migration seam usable without UnityEditor/reflection.
        /// </summary>
        protected void CompleteOneVersionMigration(int expectedFromVersion)
        {
            if (schemaVersion != expectedFromVersion)
                throw new InvalidOperationException(
                    $"Cannot migrate schema {schemaVersion}; expected {expectedFromVersion}.");
            if (expectedFromVersion < 0 || expectedFromVersion >= CurrentSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(expectedFromVersion),
                    $"Migration target {expectedFromVersion + 1} must not exceed current schema {CurrentSchemaVersion}.");
            schemaVersion = expectedFromVersion + 1;
        }
    }

    /// <summary>One build session: preserves asset identity, caches each runtime Definition, and rejects recursive builds.</summary>
    public sealed partial class GameplayAuthoringBuildContext
    {
        private readonly Dictionary<GameplayDefinitionAsset, object> _built = new Dictionary<GameplayDefinitionAsset, object>();
        private readonly HashSet<GameplayDefinitionAsset> _active = new HashSet<GameplayDefinitionAsset>();
        private GameplayDefinitionAsset _current;
        private readonly Dictionary<IGameplaySkillAction,GameplaySkillTraceSource> _actionSources=new Dictionary<IGameplaySkillAction,GameplaySkillTraceSource>();
        /// <summary>Builds the original action unchanged and records its native serialized identity.
        /// Unsaved/transient nodes without an assigned identity remain explicitly unlocatable.</summary>
        public IGameplaySkillAction BuildAction(GameplaySkillActionAuthoring authoring,Func<IGameplaySkillAction> factory=null)
        {
            var owner=_current;var result=factory!=null?factory():authoring.Build(this);
            if(owner!=null){long id=UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(owner,authoring);
                if(id>=0)_actionSources[result]=new GameplaySkillTraceSource(owner.DefinitionId,id);}
            return result;
        }
        public IReadOnlyDictionary<IGameplaySkillAction,GameplaySkillTraceSource> GetActionSourcesSnapshot()=>
            new System.Collections.ObjectModel.ReadOnlyDictionary<IGameplaySkillAction,GameplaySkillTraceSource>(new Dictionary<IGameplaySkillAction,GameplaySkillTraceSource>(_actionSources));
        public T Build<T>(GameplayDefinitionAsset asset, Func<T> factory) where T : class
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_built.TryGetValue(asset, out object existing)) return (T)existing;
            if (!_active.Add(asset)) throw new InvalidOperationException($"Circular authoring asset: {asset.name} ({asset.DefinitionId}).");
            var previous=_current;_current=asset;
            try
            {
                var validation = new GameplayAuthoringValidationContext();
                validation.Asset(asset, asset.name);
                if (validation.HasErrors) throw new InvalidOperationException(string.Join(" | ", validation.Issues));
                T result;
                try { result = factory(); }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                { throw new InvalidOperationException($"Failed building {asset.name} ({asset.DefinitionId}): {ex.Message}", ex); }
                _built.Add(asset, result);
                return result;
            }
            finally { _current=previous;_active.Remove(asset); }
        }
    }

    [Serializable]
    public struct GameplayTagReference
    {
        public string Name;
        public GameplayTagReference(string name) { Name = name; }
        public GameplayTag Build() => new GameplayTag(Name);
    }

    [Serializable]
    public struct GameplayAttributeReference
    {
        public string Value;
        public GameplayAttributeReference(string value) { Value = value; }
        public GameplayAttributeId Build() => new GameplayAttributeId(Value);
    }
}
