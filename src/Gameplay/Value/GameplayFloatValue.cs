using System;

namespace FRFrame.Gameplay
{
    /// <summary>不可变 float 规则值；实现不得保存每次应用的运行态。</summary>
    public interface IGameplayFloatValue
    {
        bool RequiresApplicationContext { get; }
        float Evaluate(GameplayValueEvaluationContext context);
    }

    public readonly struct GameplayValueEvaluationContext
    {
        public GameplayEffectApplicationContext Application { get; }
        public int StackCount => Application?.StackCount ?? 1;
        public GameplayValueEvaluationContext(GameplayEffectApplicationContext application) => Application = application;
        public GameplayEffectApplicationContext RequireApplication() => Application ??
            throw new InvalidOperationException("This value requires an Effect application context.");
    }

    /// <summary>统一进行 null 和有限值校验的规则数值入口。</summary>
    public static class GameplayValueEvaluator
    {
        public static float Evaluate(IGameplayFloatValue value, GameplayEffectApplicationContext application = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.RequiresApplicationContext && application == null)
                throw new InvalidOperationException("Value requires an Effect application context.");
            float result = value.Evaluate(new GameplayValueEvaluationContext(application));
            if (!float.IsFinite(result)) throw new ArithmeticException("Gameplay value must be finite.");
            return result;
        }
    }

    public sealed class GameplayConstantFloatValue : IGameplayFloatValue
    {
        public float Value { get; }
        public bool RequiresApplicationContext => false;
        public GameplayConstantFloatValue(float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }
        public float Evaluate(GameplayValueEvaluationContext context) => Value;
    }

    public sealed class GameplaySetByCallerFloatValue : IGameplayFloatValue
    {
        public GameplayTag Key { get; }
        public bool RequiresApplicationContext => true;
        public GameplaySetByCallerFloatValue(GameplayTag key)
        { if (!key.IsValid) throw new ArgumentException("Key must be valid.", nameof(key)); Key = key; }
        public float Evaluate(GameplayValueEvaluationContext context) =>
            context.RequireApplication().SetByCaller.GetRequiredValue(Key);
    }

    public sealed class GameplayContextFloatValue : IGameplayFloatValue
    {
        public GameplayTag Key { get; }
        public bool RequiresApplicationContext => true;
        public GameplayContextFloatValue(GameplayTag key)
        { if (!key.IsValid) throw new ArgumentException("Key must be valid.", nameof(key)); Key = key; }
        public float Evaluate(GameplayValueEvaluationContext context) =>
            context.RequireApplication().Context.Values.GetRequiredValue(Key);
    }

    public enum GameplayValueAttributeSource { Instigator, Target }

    public sealed class GameplayAttributeFloatValue : IGameplayFloatValue
    {
        public GameplayValueAttributeSource Source { get; }
        public GameplayAttributeId AttributeId { get; }
        public bool RequiresApplicationContext => true;
        public GameplayAttributeFloatValue(GameplayValueAttributeSource source, GameplayAttributeId attributeId)
        {
            if (source != GameplayValueAttributeSource.Instigator && source != GameplayValueAttributeSource.Target)
                throw new ArgumentOutOfRangeException(nameof(source));
            if (!attributeId.IsValid) throw new ArgumentException("Attribute ID must be valid.", nameof(attributeId));
            Source = source; AttributeId = attributeId;
        }
        public float Evaluate(GameplayValueEvaluationContext context)
        {
            var application = context.RequireApplication();
            AttributeSet attributes = Source == GameplayValueAttributeSource.Target ?
                application.Target.Attributes : application.InstigatorAttributes;
            if (attributes == null) throw new InvalidOperationException("Instigator attribute view is unavailable.");
            if (!attributes.Contains(AttributeId))
            {
                var actor = Source == GameplayValueAttributeSource.Target ? application.Target : application.InstigatorTarget;
                if (actor is IGameplayResourceTarget resources && resources.Resources.TryGetBalance(AttributeId, out float balance)) return balance;
            }
            return attributes.GetCurrentValue(AttributeId);
        }
    }

    public sealed class GameplayStackCountFloatValue : IGameplayFloatValue
    {
        public bool RequiresApplicationContext => true;
        public float Evaluate(GameplayValueEvaluationContext context) => context.RequireApplication().StackCount;
    }

    public enum GameplayBinaryFloatOperation { Add, Subtract, Multiply, Divide, Min, Max }

    public sealed class GameplayBinaryFloatValue : IGameplayFloatValue
    {
        public GameplayBinaryFloatOperation Operation { get; }
        public IGameplayFloatValue Left { get; }
        public IGameplayFloatValue Right { get; }
        public bool RequiresApplicationContext => Left.RequiresApplicationContext || Right.RequiresApplicationContext;
        public GameplayBinaryFloatValue(GameplayBinaryFloatOperation operation,
            IGameplayFloatValue left, IGameplayFloatValue right)
        {
            if (!Enum.IsDefined(typeof(GameplayBinaryFloatOperation), operation))
                throw new ArgumentOutOfRangeException(nameof(operation));
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
            Operation = operation;
        }
        public float Evaluate(GameplayValueEvaluationContext context)
        {
            float a = GameplayValueEvaluator.Evaluate(Left, context.Application);
            float b = GameplayValueEvaluator.Evaluate(Right, context.Application);
            if (Operation == GameplayBinaryFloatOperation.Divide && b == 0f)
                throw new DivideByZeroException("Gameplay value division by zero.");
            float result;
            switch (Operation)
            {
                case GameplayBinaryFloatOperation.Add: result = a + b; break;
                case GameplayBinaryFloatOperation.Subtract: result = a - b; break;
                case GameplayBinaryFloatOperation.Multiply: result = a * b; break;
                case GameplayBinaryFloatOperation.Divide: result = a / b; break;
                case GameplayBinaryFloatOperation.Min: result = Math.Min(a, b); break;
                default: result = Math.Max(a, b); break;
            }
            if (!float.IsFinite(result)) throw new ArithmeticException("Gameplay formula must be finite.");
            return result;
        }
    }

    /// <summary>Stateless game-specific calculation; receives the current application snapshot and must return a finite value.</summary>
    public interface IGameplayCustomFloatCalculation
    {
        float Calculate(GameplayValueEvaluationContext context);
    }

    public sealed class GameplayCustomFloatValue : IGameplayFloatValue
    {
        public IGameplayCustomFloatCalculation Calculation { get; }
        public bool RequiresApplicationContext => true;
        public GameplayCustomFloatValue(IGameplayCustomFloatCalculation calculation) =>
            Calculation = calculation ?? throw new ArgumentNullException(nameof(calculation));
        public float Evaluate(GameplayValueEvaluationContext context) => Calculation.Calculate(context);
    }
}
