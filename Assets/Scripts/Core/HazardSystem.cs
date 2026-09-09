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
    /// The arena is safe for HazardSafeCycles cycles, then shrinks inward in telegraphed stages,
    /// one every HazardRingInterval cycles until there is nowhere left to stand.
    ///
    /// The schedule is built from those two numbers rather than written out, because the two are
    /// what the pacing actually is and a hand-written list drifts from them the first time either
    /// moves - which it did: the list still said "6 safe cycles" in its own comment while the first
    /// ring landed on the seventh.
    /// </summary>
    public static class HazardSystem
    {
        /// <summary>
        /// The rings, from the wall inwards. Each one is a quarter of the way to the middle, and
        /// the last is the middle itself - so once the whole schedule has run there is nowhere on
        /// the sand that does not burn.
        /// </summary>
        private static readonly float[] RingEdges = { 1.00f, 0.75f, 0.50f, 0.25f, 0.00f };

        public static readonly IReadOnlyList<HazardStage> Schedule = BuildSchedule();

        private static List<HazardStage> BuildSchedule()
        {
            var stages = new List<HazardStage>();
            for (int i = 0; i + 1 < RingEdges.Length; i++)
            {
                stages.Add(new HazardStage
                {
                    OuterFraction = RingEdges[i],
                    InnerFraction = RingEdges[i + 1],

                    // The first ring lands on the cycle after the safe ones run out, and the rest
                    // follow it at a fixed interval.
                    ActivateCycle = GameConstants.HazardSafeCycles + 1
                                    + i * GameConstants.HazardRingInterval,
                });
            }
            return stages;
        }

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
            // Fraction of the way to the wall, measured on the ellipse - so a ring is a ring, not
            // a circle sitting inside an oval.
            float r = ArenaShape.NormalizedDistance(pos);
            foreach (var stage in Schedule)
            {
                if (cycle < stage.ActivateCycle) continue;
                if (r >= stage.InnerFraction && r <= stage.OuterFraction) return true;
            }
            return false;
        }
    }
}
