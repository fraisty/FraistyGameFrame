using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Immutable mapping to a game's actual resources; no unit names, inventory or save objects.</summary>
    public sealed class GameplayCombatAttributeMap
    {
        public GameplayAttributeId Health { get; }
        public GameplayAttributeId MaxHealth { get; }
        public GameplayAttributeId Shield { get; }
        public GameplayAttributeId Defense { get; }
        public GameplayAttributeId MagicDefense {get;}
        public GameplayCombatAttributeMap(GameplayAttributeId health, GameplayAttributeId maxHealth, GameplayAttributeId shield, GameplayAttributeId defense,GameplayAttributeId magicDefense=default)
        {
            if (!health.IsValid || !maxHealth.IsValid) throw new ArgumentException("Health and MaxHealth mappings are required.");
            if (health == maxHealth || (shield.IsValid && (shield == health || shield == maxHealth))) throw new ArgumentException("Resource mappings must be distinct.");
            Health = health; MaxHealth = maxHealth; Shield = shield; Defense = defense;MagicDefense=magicDefense;
        }
    }
    public enum GameplayCombatOutcomeKind { Damage, Heal, Shield }
    /// <summary>Actual post-commit rule producers. Damage includes miss/immunity/absorption;
    /// Hit excludes miss and immunity but includes full shield absorption. Magnitude is applied
    /// resource change. Subject filtering uses the original instigator and actual target.</summary>
    public static class GameplayCombatEvents
    {
        public static readonly GameplayTag Damage = new GameplayTag("Event.Standard.Combat.Damage");
        public static readonly GameplayTag Hit = new GameplayTag("Event.Standard.Combat.Hit");
        public static readonly GameplayTag Heal = new GameplayTag("Event.Standard.Combat.Heal");
        public static readonly GameplayTag Shield = new GameplayTag("Event.Standard.Combat.Shield");
        /// <summary>Authored scalar policy (1 = suppress retaliation). Outcomes still publish;
        /// retaliation rules check this captured value, ordinary hit chains remain observable.</summary>
        public static readonly GameplayTag NoRetaliate = new GameplayTag("Data.Standard.Combat.NoRetaliate");
        /// <summary>Optional root-cast scalar: maximum reaction hops, integer 0..16. Default 16.
        /// Set in SkillDocument Defaults or captured input; cannot raise the World safety limit.</summary>
        public static readonly GameplayTag MaxChainDepth = new GameplayTag("Data.Standard.Combat.MaxChainDepth");
    }
    /// <summary>Defense routing: physical uses Defense, magical uses MagicDefense, true bypasses
    /// only Defense stages. Authored immunity/miss/shield/minimum ordering still applies.</summary>
    public enum GameplayDamageType { Physical, Magical, True }
    /// <summary>Committed feedback. Applied is actual resource change, distinct from calculated damage/overheal.</summary>
    public readonly struct GameplayCombatOutcome
    {
        public GameplayContext Context { get; }
        /// <summary>Input of the action that committed this result. Per-target effect application
        /// may derive a separate Context for the actual recipient.</summary>
        public GameplayContext ExecutionContext { get; }
        public GameplayCombatOutcomeKind Kind { get; }
        public DamageResult Damage { get; }
        public float Applied { get; }
        public float Overflow { get; }
        public float Before { get; }
        public float After { get; }
        public GameplayDamageType DamageType {get;}
        public GameplayCombatOutcome(GameplayContext context, GameplayCombatOutcomeKind kind, DamageResult damage, float applied, float overflow, float before, float after,GameplayDamageType damageType=GameplayDamageType.Physical)
            : this(context, kind, damage, applied, overflow, before, after, damageType, context) { }

        private GameplayCombatOutcome(GameplayContext context, GameplayCombatOutcomeKind kind, DamageResult damage,
            float applied, float overflow, float before, float after, GameplayDamageType damageType, GameplayContext executionContext)
        {
            Context = context; ExecutionContext = executionContext; Kind = kind; Damage = damage;
            Applied = applied; Overflow = overflow; Before = before; After = after; DamageType = damageType;
        }

        internal GameplayCombatOutcome WithExecutionContext(GameplayContext executionContext) =>
            new GameplayCombatOutcome(Context, Kind, Damage, Applied, Overflow, Before, After, DamageType, executionContext);
    }
    public enum GameplayDamageStageKind { Immunity, Miss, Defense, Critical, Shield, Minimum, Scale }
    public sealed class GameplayDamageStage
    {
        public GameplayDamageStageKind Kind { get; }
        public IGameplayFloatValue Value { get; }
        public GameplayDamageStage(GameplayDamageStageKind kind, IGameplayFloatValue value = null)
        {
            if (!Enum.IsDefined(typeof(GameplayDamageStageKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; Value = value ?? new GameplayConstantFloatValue(kind == GameplayDamageStageKind.Critical ? 2 : 1);
        }
    }
    /// <summary>Official damage Effect execution on the existing DamagePipeline. Stages are pure;
    /// final HP/Shield commit is atomic. Explicit captured Critical/Miss values give deterministic
    /// preview/runtime inputs. A fully absorbed hit stops Minimum, avoiding damage through a full shield.</summary>
    public sealed class GameplayDamageExecution : IGameplayEffectExecution
    {
        public static readonly GameplayTag CriticalKey = new GameplayTag("Data.Standard.Critical");
        public static readonly GameplayTag MissKey = new GameplayTag("Data.Standard.Miss");
        private readonly IGameplayFloatValue _amount;
        private readonly IReadOnlyList<GameplayDamageStage> _stages;
        private readonly GameplayTagQuery _immunity;
        public GameplayDamageType DamageType {get;}
        public GameplayDamageExecution(IGameplayFloatValue amount, IEnumerable<GameplayDamageStage> stages, GameplayTagQuery immunity = null,GameplayDamageType damageType=GameplayDamageType.Physical)
        {
            _amount = amount ?? throw new ArgumentNullException(nameof(amount));
            if(!Enum.IsDefined(typeof(GameplayDamageType),damageType))throw new ArgumentOutOfRangeException(nameof(damageType));DamageType=damageType;
            var copy = new List<GameplayDamageStage>(stages ?? throw new ArgumentNullException(nameof(stages)));
            if (copy.Contains(null)) throw new ArgumentException("Damage stage is null.");
            int shields = 0; foreach (var stage in copy) if (stage.Kind == GameplayDamageStageKind.Shield) shields++;
            if (shields > 1) throw new ArgumentException("Only one shield stage may consume the captured capacity.");
            _stages = copy.AsReadOnly(); _immunity = immunity ?? GameplayTagQuery.Empty;
        }
        public void Execute(GameplayEffectApplicationContext application)
        {
            if (!(application.Target is GameplayStandardActor target) || target.CombatAttributes == null) throw new InvalidOperationException("Damage requires a standard target with a combat profile.");
            var map = target.CombatAttributes; float initial = GameplayValueEvaluator.Evaluate(_amount, application);
            if (initial < 0) throw new ArithmeticException("Damage must be nonnegative; use Heal for restoration.");
            var pipeline = new DamagePipeline();
            foreach (var stage in _stages)
            {
                if (stage.Kind == GameplayDamageStageKind.Defense)
                {
                    if(DamageType==GameplayDamageType.True)continue;
                    var defense=DamageType==GameplayDamageType.Magical?map.MagicDefense:map.Defense;
                    if (!defense.IsValid) throw new InvalidOperationException("Damage-type defense mapping is missing.");
                    pipeline.AddModifier(new DefenseDamageModifier(defense)); continue;
                }
                float value = GameplayValueEvaluator.Evaluate(stage.Value, application);
                if (value < 0) throw new ArithmeticException("Damage stage value must be nonnegative.");
                float shield = 0;
                if (stage.Kind == GameplayDamageStageKind.Shield && (!map.Shield.IsValid || !target.Resources.TryGetBalance(map.Shield, out shield)))
                    throw new InvalidOperationException("Shield mapping is unavailable.");
                bool immune = !_immunity.IsEmpty && _immunity.Matches(target.Tags);
                bool critical = application.Context.Values.TryGetValue(CriticalKey, out float crit) && crit != 0;
                bool miss = application.Context.Values.TryGetValue(MissKey, out float missed) && missed != 0;
                pipeline.AddModifier(new Stage(stage.Kind, value, Math.Max(0, shield), immune, critical, miss));
            }
            var result = pipeline.Calculate(new DamageContext(application.Context, initial, target.Attributes, application.InstigatorAttributes));
            if (!target.Resources.TryGetBalance(map.Health, out float health)) throw new InvalidOperationException("Health resource is missing.");
            float applied = Math.Min(Math.Max(0, health), result.FinalDamage);
            Action commitShieldOwnership=null;
            var balances = new Dictionary<GameplayAttributeId, float> { [map.Health] = Math.Max(0, health - applied) };
            if (result.AbsorbedDamage > 0)
            {
                if (!target.Resources.TryGetBalance(map.Shield, out float shield)) throw new InvalidOperationException("Shield resource is missing.");
                balances.Add(map.Shield, Math.Max(0, shield - result.AbsorbedDamage));
                commitShieldOwnership=target.Shields.PrepareConsumption(result.AbsorbedDamage,shield);
            }
            target.Resources.SetBalancesAtomic(balances);
            commitShieldOwnership?.Invoke();
            target.ReportCombat(new GameplayCombatOutcome(application.Context, GameplayCombatOutcomeKind.Damage, result,
                applied, Math.Max(0, result.FinalDamage - applied), health, balances[map.Health],DamageType));
        }
        private sealed class Stage : IDamageResultModifier
        {
            private readonly GameplayDamageStageKind _kind; private readonly float _value, _shield;
            private readonly bool _immune, _critical, _miss;
            public Stage(GameplayDamageStageKind kind, float value, float shield, bool immune, bool critical, bool miss)
            { _kind = kind; _value = value; _shield = shield; _immune = immune; _critical = critical; _miss = miss; }
            public float Modify(DamageContext context, float currentDamage) => Calculate(context, currentDamage).Damage;
            public DamageStageResult Calculate(DamageContext context, float damage)
            {
                if (_kind == GameplayDamageStageKind.Immunity && _immune) return new DamageStageResult(0, flags: DamageResultFlags.Immune, stop: true);
                if (_kind == GameplayDamageStageKind.Miss && _miss) return new DamageStageResult(0, flags: DamageResultFlags.Miss, stop: true);
                if (_kind == GameplayDamageStageKind.Critical && _critical) return new DamageStageResult(damage * _value, flags: DamageResultFlags.Critical);
                if (_kind == GameplayDamageStageKind.Minimum) return new DamageStageResult(Math.Max(damage, _value));
                if (_kind == GameplayDamageStageKind.Scale) return new DamageStageResult(damage * _value);
                if (_kind == GameplayDamageStageKind.Shield)
                {
                    float absorbed = Math.Min(damage, _shield);
                    return new DamageStageResult(damage - absorbed, absorbed,
                        _shield > 0 && absorbed >= _shield ? DamageResultFlags.ShieldBroken : DamageResultFlags.None,
                        absorbed > 0 && absorbed >= damage);
                }
                return new DamageStageResult(damage);
            }
        }
    }
    /// <summary>Standard heal on the mapped actual resource; bounded by MaxHealth, with explicit overheal feedback.</summary>
    public sealed class GameplayHealExecution : IGameplayEffectExecution
    {
        private readonly IGameplayFloatValue _amount;
        public GameplayHealExecution(IGameplayFloatValue amount) => _amount = amount ?? throw new ArgumentNullException(nameof(amount));
        public void Execute(GameplayEffectApplicationContext application)
        {
            if (!(application.Target is GameplayStandardActor target) || target.CombatAttributes == null) throw new InvalidOperationException("Heal requires a combat profile.");
            var map = target.CombatAttributes; float amount = GameplayValueEvaluator.Evaluate(_amount, application);
            if (amount < 0) throw new ArithmeticException("Heal must be nonnegative.");
            if (!target.Resources.TryGetBalance(map.Health, out float before)) throw new InvalidOperationException("Health resource is missing.");
            float maximum = target.Attributes.GetCurrentValue(map.MaxHealth);
            float applied = Math.Min(amount, Math.Max(0, maximum - before));
            target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId, float> { [map.Health] = before + applied });
            target.ReportCombat(new GameplayCombatOutcome(application.Context, GameplayCombatOutcomeKind.Heal, default, applied, amount - applied, before, before + applied));
        }
    }
    /// <summary>Instant shield capacity addition. Timed/owned contributions are a separate Effect lease operation.</summary>
    public sealed class GameplayShieldExecution : IGameplayEffectExecution
    {
        private readonly IGameplayFloatValue _amount;
        public GameplayShieldExecution(IGameplayFloatValue amount) => _amount = amount ?? throw new ArgumentNullException(nameof(amount));
        public void Execute(GameplayEffectApplicationContext application)
        {
            if (!(application.Target is GameplayStandardActor target) || target.CombatAttributes == null) throw new InvalidOperationException("Shield requires a combat profile.");
            var map = target.CombatAttributes;
            if (!map.Shield.IsValid || !target.Resources.TryGetBalance(map.Shield, out float before)) throw new InvalidOperationException("Shield resource is missing.");
            float amount = GameplayValueEvaluator.Evaluate(_amount, application);
            if (amount < 0) throw new ArithmeticException("Shield must be nonnegative.");
            target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId, float> { [map.Shield] = before + amount });
            target.ReportCombat(new GameplayCombatOutcome(application.Context, GameplayCombatOutcomeKind.Shield, default, amount, 0, before, before + amount));
        }
    }
}
