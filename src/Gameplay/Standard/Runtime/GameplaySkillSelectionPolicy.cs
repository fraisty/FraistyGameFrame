using System;
using System.Linq;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Small priority-based automatic cast policy. Uses actual installed grants and their
    /// eligibility; no cooldown mirror, state machine or timer service. Host/FSM supplies each delta.
    /// At most one cast per decision, stable installed order breaks ties, missed decisions do not burst.</summary>
    public sealed class GameplaySkillSelectionPolicy:IGameplayBehaviourDriver
    {
        private readonly GameplayStandardActor _actor;
        private readonly Func<GameplayInstalledSkill,int> _priority;
        private readonly Func<GameplayInstalledSkill,GameplayTargetData> _target;
        private readonly float _interval;
        private double _elapsed;
        private bool _disposed;
        public string CurrentState=>"AutoCast policy";
        public bool TryChangeState(string state)=>false; // State transitions belong to the existing host FSM.
        void IGameplayBehaviourDriver.Advance(float amount)=>Advance(amount);
        public void Dispose()=>_disposed=true;
        public int LastSelectedSkillId { get; private set; }
        public GameplaySkillSelectionPolicy(GameplayStandardActor actor,Func<GameplayInstalledSkill,int> priority,
            Func<GameplayInstalledSkill,GameplayTargetData> target,float interval=1)
        {
            _actor=actor??throw new ArgumentNullException(nameof(actor));
            _priority=priority??throw new ArgumentNullException(nameof(priority));_target=target??throw new ArgumentNullException(nameof(target));
            if(!float.IsFinite(interval)||interval<=0)throw new ArgumentOutOfRangeException(nameof(interval));_interval=interval;
        }
        public bool Advance(float amount)
        {
            if(!float.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            if(_disposed||_actor.IsDisposed||!_actor.IsTargetable)return false;
            _elapsed=Math.Min(_interval,_elapsed+amount);if(_elapsed<_interval)return false;
            var skills=_actor.InstalledSkills.Where(s=>!s.IsDisposed).ToArray();
            if(skills.Any(s=>s.Status.Phase==GameplayAbilityPhase.Active))return false;
            _elapsed=0;
            foreach(var skill in skills.Where(s=>s.Definition.Mode==GameplayStandardActivationMode.Active).OrderByDescending(_priority))
            {
                var target=_target(skill);if(target==null||!skill.TryCast(target))continue;
                LastSelectedSkillId=skill.Definition.SkillId;return true;
            }
            return false;
        }
    }
}
