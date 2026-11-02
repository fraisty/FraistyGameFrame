using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Highest eligible level band, then stable authored weights within that band.
    /// Preview/SDK sampling consumes only a captured scalar; it never advances a random stream.</summary>
    public static class GameplayAnimationVariantSelector
    {
        public static readonly GameplayTag RollKey=new GameplayTag("Data.Presentation.Animation.VariantRoll");
        public static GameplayContext Capture(GameplayContext context,IGameplayRandomSource random)
        {
            if(context.Values.TryGetValue(RollKey,out var existing)){ValidateRoll(existing);return context;}
            double roll=(random??throw new ArgumentNullException(nameof(random))).NextUnitInterval();
            if(!double.IsFinite(roll)||roll<0||roll>=1)throw new ArithmeticException("Animation variant roll must be in [0,1).");
            // Conversion to float can round a valid double up to 1.
            return context.WithValues(context.Values.WithValue(RollKey,Math.Min((float)roll,.99999994f)));
        }
        public static int Select<T>(IReadOnlyList<T> variants,GameplayContext context,Func<T,int> minimum,Func<T,float> weight,bool weighted)
        {
            int band=0,first=-1;
            for(int i=0;i<variants.Count;i++)if(variants[i]!=null&&minimum(variants[i])<=context.AbilityLevel&&minimum(variants[i])>band)
            {band=minimum(variants[i]);first=i;}
            if(first<0||!weighted)return first;
            context.Values.TryGetValue(RollKey,out var roll);ValidateRoll(roll);
            double total=0;int last=first;
            for(int i=0;i<variants.Count;i++)if(variants[i]!=null&&minimum(variants[i])==band)
            {float value=weight(variants[i]);if(!float.IsFinite(value)||value<=0)throw new ArithmeticException("Animation weights must be positive and finite.");total+=value;last=i;}
            double threshold=roll*total;
            for(int i=0;i<variants.Count;i++)if(variants[i]!=null&&minimum(variants[i])==band)
            {threshold-=weight(variants[i]);if(threshold<0)return i;}
            return last;
        }
        private static void ValidateRoll(float roll){if(!float.IsFinite(roll)||roll<0||roll>=1)throw new ArithmeticException("Animation variant roll must be in [0,1).");}
    }
}
