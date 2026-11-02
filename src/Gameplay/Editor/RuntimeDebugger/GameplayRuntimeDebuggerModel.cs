using System;
using FRFrame.Gameplay.Debugging;

namespace FRFrame.Gameplay.Editor.RuntimeDebugger
{
    /// <summary>Pure selection and Freeze state for the Editor window; it owns no gameplay runtime object.</summary>
    public sealed class GameplayRuntimeDebuggerModel
    {
        public GameplayRuntimeDebugSnapshot Snapshot { get; private set; }
        public GameplaySourceId SelectedUnitSourceId { get; private set; }
        public string SelectedUnitGroup {get;private set;}
        public bool Frozen { get; set; }

        public void SelectUnit(GameplaySourceId sourceId,string group=null){SelectedUnitSourceId=sourceId;SelectedUnitGroup=group;}

        public void Refresh(IGameplayRuntimeDebugSource source)
        {
            if (source == null)
            {
                Clear();
                return;
            }
            if (Frozen) return;
            Snapshot = source.CaptureSnapshot();
            if (Snapshot == null || Snapshot.Units.Count == 0)
            {
                SelectedUnitSourceId = default;SelectedUnitGroup=null;
                return;
            }
            foreach (GameplayDebugUnitSnapshot unit in Snapshot.Units)
                if (unit.SourceId == SelectedUnitSourceId&&(SelectedUnitGroup==null||unit.Group==SelectedUnitGroup)){SelectedUnitGroup=unit.Group;return;}
            SelectedUnitSourceId = Snapshot.Units[0].SourceId;SelectedUnitGroup=Snapshot.Units[0].Group;
        }

        public GameplayDebugUnitSnapshot SelectedUnit()
        {
            if (Snapshot == null) return null;
            foreach (GameplayDebugUnitSnapshot unit in Snapshot.Units)
                if (unit.SourceId == SelectedUnitSourceId&&(SelectedUnitGroup==null||unit.Group==SelectedUnitGroup)) return unit;
            return null;
        }

        public void Clear()
        {
            Snapshot = null;
            SelectedUnitSourceId = default;
            SelectedUnitGroup=null;
            Frozen = false;
        }

        public static bool Matches(string text, string filter) =>
            string.IsNullOrWhiteSpace(filter) ||
            (text?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

        public static bool MatchesUnit(GameplayDebugUnitSnapshot unit, string filter)
        {
            if (Matches(unit.DisplayName, filter) || Matches(unit.Group, filter)) return true;
            foreach (GameplayDebugSection section in unit.Sections)
            {
                if (Matches(section.Name, filter)) return true;
                foreach (string line in section.Lines)
                    if (Matches(line, filter)) return true;
            }
            return false;
        }
    }
}
