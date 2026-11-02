using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Reads the actual spendable resource balance, never the modified attribute aggregate.
    /// A resource capability is authoritative; hosts without it use the existing AttributeSet BaseValue.</summary>
    public sealed class GameplayResourceFloatValue : IGameplayFloatValue
    {
        public GameplayValueAttributeSource Source {get;}
        public GameplayAttributeId Resource {get;}
        public bool RequiresApplicationContext=>true;
        public GameplayResourceFloatValue(GameplayValueAttributeSource source,GameplayAttributeId resource)
        {
            if(!Enum.IsDefined(typeof(GameplayValueAttributeSource),source))throw new ArgumentOutOfRangeException(nameof(source));
            if(!resource.IsValid)throw new ArgumentException("Resource key is required.",nameof(resource));Source=source;Resource=resource;
        }
        public float Evaluate(GameplayValueEvaluationContext context)
        {
            var application=context.RequireApplication();
            var actor=Source==GameplayValueAttributeSource.Target?application.Target:application.InstigatorTarget;
            IGameplayResourceStore store=actor is IGameplayResourceTarget resources?resources.Resources:
                Source==GameplayValueAttributeSource.Target?application.Target.Attributes:application.InstigatorAttributes;
            if(store==null)throw new InvalidOperationException("Resource owner view is unavailable.");
            if(!store.TryGetBalance(Resource,out float balance))throw new KeyNotFoundException("Resource balance is unavailable: "+Resource);
            return balance;
        }
    }
}
