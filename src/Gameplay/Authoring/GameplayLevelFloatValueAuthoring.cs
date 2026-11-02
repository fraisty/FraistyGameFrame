using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Authorable finite rule curve. Runtime owns a detached copy and has no Unity dependency.</summary>
    [Serializable,Preserve]
    public sealed class GameplayCurveFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        [Serializable] public struct Point {public float Input,Output;public Point(float input,float output){Input=input;Output=output;}}
        [SerializeReference] public GameplayFloatValueAuthoring Input=new GameplayAbilityLevelFloatValueAuthoring();
        [Tooltip("分段线性插值；输入必须严格递增，范围外保持首尾值。")]
        public Point[] Keys={new Point(1,1),new Point(10,10)};
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {
            if(Input==null){validation.Error(path+".Input","Missing curve input.");return;}Input.Validate(validation,path+".Input");
            try{new GameplayCurveFloatValue(new GameplayConstantFloatValue(0),KeySnapshot());}
            catch(ArgumentException ex){validation.Error(path+".Keys",ex.Message);}
        }
        private System.Collections.Generic.KeyValuePair<float,float>[] KeySnapshot()=>
            Keys==null?null:Array.ConvertAll(Keys,p=>new System.Collections.Generic.KeyValuePair<float,float>(p.Input,p.Output));
        public override IGameplayFloatValue Build()=>new GameplayCurveFloatValue(Input.Build(),KeySnapshot());
    }
    [Serializable, Preserve]
    public sealed class GameplayAbilityLevelFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public override void Validate(GameplayAuthoringValidationContext validation, string path) { }
        public override IGameplayFloatValue Build() => new GameplayAbilityLevelFloatValue();
    }

    [Serializable, Preserve]
    public sealed class GameplayLevelTableFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        [Tooltip("Lv1 从第一个元素开始；超过表长度时保持最后一项。")]
        public float[] Values = new[] { 1f };
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (Values == null || Values.Length == 0) { validation.Error(path, "Level table cannot be empty."); return; }
            for (int i = 0; i < Values.Length; i++) validation.Finite(Values[i], path + $".Values[{i}]");
        }
        public override IGameplayFloatValue Build() => new GameplayLevelTableFloatValue(Values);
    }

    /// <summary>Formula text is the serialized authority. Build compiles it into the same value AST used
    /// by structured authoring; no second formula runtime or evaluation by the Editor.</summary>
    [Serializable, Preserve]
    public sealed class GameplayFormulaFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        [Tooltip("支持 Level、StackCount、Caster.Attack、Target.Health、Resource.Mana、TargetResource.Shield、Context.Key、Caller.Key 和 min/max/clamp。Resource读取实际可消费余额。")]
        public string Expression = "1.45 + Level * 0.15";
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            try { GameplayFormula.Compile(Expression); }
            catch (Exception ex) when (ex is FormatException || ex is ArithmeticException || ex is ArgumentException)
            { validation.Error(path + ".Expression", ex.Message); }
        }
        public override IGameplayFloatValue Build() => GameplayFormula.Compile(Expression);
    }
}
