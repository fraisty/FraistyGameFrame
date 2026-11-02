using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Unity;
using NUnit.Framework;

namespace FRFrame.Gameplay.Tests.UnityIntegration
{
    public sealed class GameplayFsmOwnershipTests
    {
        /// <summary>Protects actual host FSMs against double ticking and accidental disposal when borrowed.</summary>
        [TestCase(false)] [TestCase(true)]
        public void FsmAdapterHonoursClockOwnershipAndExplicitStateAliases(bool owned)
        {
            using(var fsm=new HostFsm())using(var world=new GameplayStandardWorld())
            {
                fsm.Initialize();var actor=world.CreateActor(new GameplayPoint(0,0));
                var adapter=new GameplayFsmBehaviourAdapter(fsm,new Dictionary<string,Type>{{"Idle",typeof(Idle)},{"Guard",typeof(Guard)}},owned,owned);
                actor.Behaviour=adapter;
                Assert.That(adapter.TryChangeState("Idle"),Is.True);
                Assert.That(new GameplayChangeStateAction("Missing").Execute(default,default,actor),Is.EqualTo(GameplaySkillActionResult.Rejected));
                Assert.That(fsm.CurrentStateName,Is.EqualTo(nameof(Idle)));
                Assert.That(new GameplayChangeStateAction("Guard").Execute(default,default,actor),Is.EqualTo(GameplaySkillActionResult.Succeeded));
                world.Advance(.5f);Assert.That(fsm.RunTime,Is.EqualTo(owned?.5f:0));
                actor.Dispose();Assert.That(fsm.IsDisposed,Is.EqualTo(owned));
                Assert.That(adapter.TryChangeState("Idle"),Is.False);
                if(!owned){fsm.Update(.5f,.5f);Assert.That(fsm.RunTime,Is.EqualTo(.5f));}
            }
        }
        private sealed class HostFsm:FsmController
        {protected override void OnInitialize(){AddState<Idle>();AddState<Guard>();}}
        public sealed class Idle:FsmState { }
        public sealed class Guard:FsmState { }
    }
}
