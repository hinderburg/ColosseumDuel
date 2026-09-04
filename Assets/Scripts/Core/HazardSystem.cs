using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public struct HazardStage
    {
        public float InnerFraction; // of ArenaRadius
        public float OuterFraction;
        public int ActivateCycle;   // cycle number (1-based) this ring starts dealing damage
    }

    /// <summary>
    /// The arena is safe for the first HazardSafeCycles cycles, then shrinks inward in telegraphed
    /// stages. Tune ActivateCycle per stage to match the exact pacing of the original prototype if
    /// you have it on hand - these defaults follow the written design (6 safe cycles, fully
    /// realized by cycle 10, core dangerous ~2 cycles after that).
    /// </summary>
    public static class HazardSystem
    {
        public static readonly IReadOnlyList<HazardStage> Schedule = new List<HazardStage>
        {
            new HazardStage { InnerFraction = 0.75f, OuterFraction = 1.00f, ActivateCycle = 7 },
            new HazardStage { InnerFraction = 0.50f, OuterFraction = 0.75f, ActivateCycle = 8 },
            new HazardStage { InnerFraction = 0.25f, OuterFraction = 0.50f, ActivateCycle = 9 },
            new HazardStage { InnerFraction = 0.00f, OuterFraction = 0.25f, ActivateCycle = 11 }, // core, 2 cycles after the last ring
        };

        public static List<HazardStage> ActiveStagesAt(int cycle)
        {
            var active = new List<HazardStage>();
            foreach (var stage in Schedule)
                if (cycle >= stage.ActivateCycle) active.Add(stage);
            return active;
        }

        /// <summary>The stage that will activate NEXT cycle, for telegraphing during Planning. Null if none.</summary>
        public static HazardStage? UpcomingStage(int currentCycle)
        {
            foreach (var stage in Schedule)
                if (stage.ActivateCycle == currentCycle + 1) return stage;
            return null;
        }

        public static bool IsInActiveHazard(Vector2 pos, int cycle)
        {
            float r = pos.magnitude / GameConstants.ArenaRadius;
            foreach (var stage in Schedule)
            {
                if (cycle < stage.ActivateCycle) continue;
                if (r >= stage.InnerFraction && r <= stage.OuterFraction) return true;
            }
            return false;
        }
    }
}
