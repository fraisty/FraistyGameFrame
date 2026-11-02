using System;
using System.Collections.Generic;
using System.Linq;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Structured compilation diagnostics keep stable source paths available to the
    /// Workbench; callers can navigate the actual payload instead of parsing a stack trace.</summary>
    public sealed class GameplayGraphCompilationException:InvalidOperationException
    {
        public IReadOnlyList<GameplayAuthoringValidationIssue> Issues {get;}
        public GameplayGraphCompilationException(IEnumerable<GameplayAuthoringValidationIssue> issues):this(issues.ToArray()){}
        private GameplayGraphCompilationException(GameplayAuthoringValidationIssue[] issues):base(string.Join("\n",issues.Select(i=>i.ToString()))){Issues=issues;}
    }
}
