using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Globalization;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Presentation-only formatting/resource binding for committed combat feedback.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayFloatingTextData:GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayFloatingTextDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameObject Prefab;
        public Vector3 Offset=new Vector3(0,1.5f,0);
        public float Duration=1,RiseSpeed=.6f;
        public Vector3 Acceleration,VelocitySpread;
        [Range(0,.99f)]public float FadeStartRatio;
        public bool Follow=true;
        public string Binding;
        [Tooltip("伤害数值前的文字，例如 - 或 处决。")] public string DamagePrefix="";
        [Tooltip("0 显示整数，0.## 最多显示两位小数。只影响飘字。")] public string NumberFormat="0.##";
        public bool RequestedHealing,SuppressEmptyHealing,FixedPoint;
        public Vector3 Point;
        public string CriticalPrefix="CRIT ",MissText="MISS",ImmuneText="IMMUNE",ShieldPrefix="盾 ";
        public Color DamageColor=new Color(1,.35f,.3f),CriticalColor=new Color(1,.8f,.2f),SupportColor=new Color(.3f,1,.6f),MissColor=Color.gray;
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {
            if(Prefab==null||Prefab.GetComponent<GameplayFloatingTextWidget>()==null)validation.Error(path+".Prefab","Prefab requires GameplayFloatingTextWidget.");
            validation.Finite(Duration,path+".Duration");validation.Finite(RiseSpeed,path+".RiseSpeed");
            if(Duration<=0)validation.Error(path+".Duration","Duration must be positive.");
            if(!float.IsFinite(Offset.x)||!float.IsFinite(Offset.y)||!float.IsFinite(Offset.z))validation.Error(path+".Offset","Offset must be finite.");
            if(!float.IsFinite(Point.x)||!float.IsFinite(Point.y)||!float.IsFinite(Point.z))validation.Error(path+".Point","Point must be finite.");
            foreach(var motion in new[]{Acceleration,VelocitySpread})if(!float.IsFinite(motion.x)||!float.IsFinite(motion.y)||!float.IsFinite(motion.z))validation.Error(path,"飘字运动参数必须有限。");
            if(VelocitySpread.x<0||VelocitySpread.y<0||VelocitySpread.z<0||!float.IsFinite(FadeStartRatio)||FadeStartRatio<0||FadeStartRatio>=1)validation.Error(path,"运动随机范围必须非负，淡出起点必须在0～1之间。");
            try{if(string.IsNullOrWhiteSpace(NumberFormat))throw new FormatException();1f.ToString(NumberFormat,CultureInfo.InvariantCulture);}catch(FormatException){validation.Error(path+".NumberFormat","数值显示格式无效。");}
        }
        public GameplayFloatingTextDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>new GameplayFloatingTextDefinition(
            Prefab,Offset,Duration,RiseSpeed,Follow,CriticalPrefix,MissText,ImmuneText,DamageColor,CriticalColor,SupportColor,MissColor,ShieldPrefix,
            Binding,DamagePrefix,NumberFormat,RequestedHealing,SuppressEmptyHealing,FixedPoint?(Vector3?)Point:null,Acceleration,VelocitySpread,FadeStartRatio));
    
    }
}
