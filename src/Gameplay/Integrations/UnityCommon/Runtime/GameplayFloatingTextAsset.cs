using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Globalization;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Presentation-only formatting/resource binding for committed combat feedback.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Presentation/Combat Floating Text")]
    public sealed class GameplayFloatingTextAsset:GameplayDefinitionAsset
    {
        public GameObject Prefab;

        public Vector3 Offset=new Vector3(0,1.5f,0);

        public float Duration=1,RiseSpeed=.6f;

        public Vector3 Acceleration,VelocitySpread;

        [Range(0,.99f)]public float FadeStartRatio;

        public bool Follow=true;

        public string Binding,DamagePrefix="",NumberFormat="0.##";

        public bool RequestedHealing,SuppressEmptyHealing,FixedPoint;

        public Vector3 Point;

        public string CriticalPrefix="CRIT ",MissText="MISS",ImmuneText="IMMUNE",ShieldPrefix="盾 ";

        public Color DamageColor=new Color(1,.35f,.3f),CriticalColor=new Color(1,.8f,.2f),SupportColor=new Color(.3f,1,.6f),MissColor=Color.gray;

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayFloatingTextData CaptureValues() => new GameplayFloatingTextData { Prefab = Prefab, Offset = Offset, Duration = Duration, RiseSpeed = RiseSpeed, Acceleration = Acceleration, VelocitySpread = VelocitySpread, FadeStartRatio = FadeStartRatio, Follow = Follow, Binding = Binding, DamagePrefix = DamagePrefix, NumberFormat = NumberFormat, RequestedHealing = RequestedHealing, SuppressEmptyHealing = SuppressEmptyHealing, FixedPoint = FixedPoint, Point = Point, CriticalPrefix = CriticalPrefix, MissText = MissText, ImmuneText = ImmuneText, ShieldPrefix = ShieldPrefix, DamageColor = DamageColor, CriticalColor = CriticalColor, SupportColor = SupportColor, MissColor = MissColor };
        public GameplayFloatingTextDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
    public sealed class GameplayFloatingTextDefinition
    {
        public GameObject Prefab {get;}public Vector3 Offset {get;}public float Duration {get;}public float RiseSpeed {get;}public bool Follow {get;}
        public string Binding {get;}
        public Vector3? Point {get;}
        public Vector3 Acceleration {get;}public Vector3 VelocitySpread {get;}public float FadeStartRatio {get;}
        public bool SuppressEmptyHealing {get;}
        private readonly bool _requestedHealing;
        private readonly string _damagePrefix,_numberFormat;
        private readonly string _critical,_miss,_immune,_shield;private readonly Color _damageColor,_criticalColor,_supportColor,_missColor;
        public GameplayFloatingTextDefinition(GameObject prefab,Vector3 offset,float duration,float riseSpeed,bool follow,string critical,string miss,string immune,Color damageColor,Color criticalColor,Color supportColor,Color missColor,string shieldPrefix="盾 ",
            string binding=null,string damagePrefix="",string numberFormat="0.##",bool requestedHealing=false,bool suppressEmptyHealing=false,Vector3? point=null,Vector3? acceleration=null,Vector3? velocitySpread=null,float fadeStartRatio=0)
        {
            if(prefab==null||prefab.GetComponent<GameplayFloatingTextWidget>()==null||!float.IsFinite(duration)||duration<=0||!float.IsFinite(riseSpeed))throw new ArgumentException("Invalid floating text source.");
            Prefab=prefab;Offset=offset;Duration=duration;RiseSpeed=riseSpeed;Follow=follow;_critical=critical;_miss=miss;_immune=immune;
            _damageColor=damageColor;_criticalColor=criticalColor;_supportColor=supportColor;_missColor=missColor;
            _shield=shieldPrefix;
            Binding=binding;Point=point;SuppressEmptyHealing=suppressEmptyHealing;_requestedHealing=requestedHealing;_damagePrefix=damagePrefix;_numberFormat=numberFormat;
            Acceleration=acceleration??Vector3.zero;VelocitySpread=velocitySpread??Vector3.zero;FadeStartRatio=fadeStartRatio;
            foreach(var value in new[]{offset,Acceleration,VelocitySpread,point??Vector3.zero})if(!float.IsFinite(value.x)||!float.IsFinite(value.y)||!float.IsFinite(value.z))throw new ArgumentException("Floating positions/motion must be finite.");
            if(VelocitySpread.x<0||VelocitySpread.y<0||VelocitySpread.z<0||!float.IsFinite(fadeStartRatio)||fadeStartRatio<0||fadeStartRatio>=1)throw new ArgumentException("Invalid floating motion/fade policy.");
            if(string.IsNullOrWhiteSpace(numberFormat))throw new ArgumentException("Number format is required.");1f.ToString(numberFormat,CultureInfo.InvariantCulture);
        }
        public GameplayFloatingTextContent Format(GameplayCombatOutcome outcome)
        {
            if(outcome.Kind==GameplayCombatOutcomeKind.Damage)
            {
                if(outcome.Damage.IsMiss)return new GameplayFloatingTextContent(_miss,_missColor);
                if(outcome.Damage.IsImmune)return new GameplayFloatingTextContent(_immune,_missColor);
                if(outcome.Applied==0&&outcome.Damage.AbsorbedDamage>0)return new GameplayFloatingTextContent(_shield+"-"+Number(outcome.Damage.AbsorbedDamage),_supportColor);
                return new GameplayFloatingTextContent((outcome.Damage.IsCritical?_critical:"")+_damagePrefix+Number(outcome.Applied),outcome.Damage.IsCritical?_criticalColor:_damageColor);
            }
            return new GameplayFloatingTextContent((outcome.Kind==GameplayCombatOutcomeKind.Shield?_shield+"+":"+")+Number(outcome.Applied+(outcome.Kind==GameplayCombatOutcomeKind.Heal&&_requestedHealing?outcome.Overflow:0)),_supportColor);
        }
        private string Number(float value)=>value.ToString(_numberFormat,CultureInfo.InvariantCulture);
    }
}
