using System;
using FRFrame.Gameplay;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Serializable static expression node; evaluation remains in Gameplay Runtime.</summary>
    [Serializable]
    public abstract class GameplayFloatValueAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplayFloatValue Build();
    }

    [Serializable, Preserve]
    public sealed class GameplayConstantFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public float Value;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => v.Finite(Value, p);
        public override IGameplayFloatValue Build() => new GameplayConstantFloatValue(Value);
    }

    [Serializable, Preserve]
    public sealed class GameplaySetByCallerFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public GameplayTagReference Key;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => v.Tag(Key, p + ".Key");
        public override IGameplayFloatValue Build() => new GameplaySetByCallerFloatValue(Key.Build());
    }

    [Serializable, Preserve]
    public sealed class GameplayContextFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public GameplayTagReference Key;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => v.Tag(Key, p + ".Key");
        public override IGameplayFloatValue Build() => new GameplayContextFloatValue(Key.Build());
    }

    [Serializable, Preserve]
    public sealed class GameplayAttributeFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public GameplayValueAttributeSource Source;
        public GameplayAttributeReference Attribute;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        { v.Enum(Source, p + ".Source"); v.Attribute(Attribute, p + ".Attribute"); }
        public override IGameplayFloatValue Build() => new GameplayAttributeFloatValue(Source, Attribute.Build());
    }

    [Serializable, Preserve]
    public sealed class GameplayStackCountFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public override void Validate(GameplayAuthoringValidationContext v, string p) { }
        public override IGameplayFloatValue Build() => new GameplayStackCountFloatValue();
    }

    [Serializable, Preserve]
    public sealed class GameplayBinaryFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public GameplayBinaryFloatOperation Operation;
        [SerializeReference] public GameplayFloatValueAuthoring Left;
        [SerializeReference] public GameplayFloatValueAuthoring Right;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(Operation, p + ".Operation");
            if (Left == null) v.Error(p + ".Left", "Missing left expression."); else Left.Validate(v, p + ".Left");
            if (Right == null) v.Error(p + ".Right", "Missing right expression."); else Right.Validate(v, p + ".Right");
        }
        public override IGameplayFloatValue Build() => new GameplayBinaryFloatValue(Operation, Left.Build(), Right.Build());
    }
}
