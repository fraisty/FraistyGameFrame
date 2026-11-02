using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Static initial attributes plus combat resource mapping; mutable values only enter the actor at creation.</summary>
    public sealed class GameplayActorProfile
    {
        private readonly IReadOnlyList<KeyValuePair<GameplayAttributeId, float>> _attributes;
        public GameplayCombatAttributeMap Combat { get; }
        private readonly IReadOnlyList<(GameplaySkillDocumentDefinition skill,int level)> _skills;
        public GameplayActorProfile(IEnumerable<KeyValuePair<GameplayAttributeId, float>> attributes, GameplayCombatAttributeMap combat,
            IEnumerable<(GameplaySkillDocumentDefinition skill,int level)> startingSkills=null)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            var copy = new List<KeyValuePair<GameplayAttributeId, float>>(); var keys = new HashSet<GameplayAttributeId>();
            foreach (var pair in attributes ?? throw new ArgumentNullException(nameof(attributes)))
            {
                if (!pair.Key.IsValid || !float.IsFinite(pair.Value) || !keys.Add(pair.Key)) throw new ArgumentException("Initial attributes require unique valid IDs and finite values.");
                copy.Add(pair);
            }
            if (!keys.Contains(combat.Health) || !keys.Contains(combat.MaxHealth)) throw new ArgumentException("Health/MaxHealth must be initialized.");
            float health = copy.Find(p => p.Key == combat.Health).Value, max = copy.Find(p => p.Key == combat.MaxHealth).Value;
            if (max <= 0 || health < 0 || health > max) throw new ArgumentException("Initial Health must be within positive MaxHealth.");
            _attributes = copy.AsReadOnly();
            var skills=new List<(GameplaySkillDocumentDefinition skill,int level)>(startingSkills??Array.Empty<(GameplaySkillDocumentDefinition skill,int level)>());
            var ids=new HashSet<int>();
            foreach(var entry in skills)if(entry.skill==null||entry.level<1||entry.level>entry.skill.Ability.MaxLevel||!ids.Add(entry.skill.SkillId))throw new ArgumentException("Profile skills require distinct IDs and valid levels.");
            _skills=skills.AsReadOnly();
        }
        public void Initialize(GameplayStandardActor actor)
        {
            if (actor == null || actor.IsDisposed || actor.BorrowsContainers) throw new ArgumentException("Initialize a new standard actor; borrowed games retain their existing state.");
            foreach (var pair in _attributes) if (actor.Attributes.Contains(pair.Key)) throw new InvalidOperationException("Profile cannot overwrite existing attributes.");
            foreach (var pair in _attributes) actor.Attributes.Register(pair.Key, pair.Value);
            actor.CombatAttributes = Combat;
            // Static defaults are resolved before battle. Installation still uses the actor's
            // unique containers; Profile never retains mutable grants.
            try{foreach(var entry in _skills)actor.InstallDocument(entry.skill,entry.level);}
            catch{actor.Dispose();throw;}
        }
    }
}
