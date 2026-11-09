using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    public sealed partial class GameplayStandardActor
    {
        private GameplayContext _presentationCallback;
        private GameplayPoint? _presentationSegmentStart;

        /// <summary>Reads a committed Standard callback only while its exact context is executing.
        /// Search callbacks can also carry their actual query origin for a two-point view.</summary>
        public bool TryReadPresentationCallback(GameplayContext context, out GameplayPoint? segmentStart)
        {
            bool current = _presentationCallback != null && ReferenceEquals(_presentationCallback, context);
            segmentStart = current ? _presentationSegmentStart : null;
            return current;
        }

        internal GameplaySkillActionExecution RunPresentationCallback(IReadOnlyList<IGameplaySkillAction> actions,
            GameplayContext context, GameplayEventData trigger, Func<bool> canContinue = null, GameplayPoint? segmentStart = null)
        {
            var previous = _presentationCallback;
            var previousStart = _presentationSegmentStart;
            _presentationCallback = context;
            _presentationSegmentStart = segmentStart;
            try { return GameplaySkillActionRunner.Execute(actions, context, trigger, this, canContinue); }
            finally { _presentationCallback = previous; _presentationSegmentStart = previousStart; }
        }
    }
}
