using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The single place where the simulation's virtual 2D space is converted to world space, plus
    /// the visuals that belong to the arena itself (the shrinking danger rings).
    ///
    /// The simulation is a top-down plane, so virtual (x, y) maps to world (x, 0, y) - the same
    /// convention the original web build used. Nothing else in the project should do this
    /// conversion by hand.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        [Tooltip("World radius of the arena floor. GameConstants.ArenaRadius virtual units map onto this.")]
        public float WorldArenaRadius = 8f;

        public ViewPalette Palette;

        [Tooltip("Height above the floor at which the danger rings are drawn, to avoid z-fighting.")]
        public float HazardRingHeight = 0.03f;

        public float VirtualToWorld => WorldArenaRadius / GameConstants.ArenaRadius;

        public Vector3 ToWorld(Vector2 virtualPos, float height = 0f)
            => new Vector3(virtualPos.x * VirtualToWorld, height, virtualPos.y * VirtualToWorld);

        public Vector2 ToVirtual(Vector3 worldPos)
            => new Vector2(worldPos.x, worldPos.z) / VirtualToWorld;

        /// <summary>Converts a virtual length (a radius, a distance) to world units.</summary>
        public float ScaleLength(float virtualLength) => virtualLength * VirtualToWorld;

        private readonly List<Renderer> _hazardRings = new List<Renderer>();

        public void BuildHazardRings()
        {
            foreach (var ring in _hazardRings)
                if (ring != null) Destroy(ring.gameObject);
            _hazardRings.Clear();

            var root = new GameObject("HazardRings");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, HazardRingHeight, 0f);

            foreach (var stage in HazardSystem.Schedule)
            {
                var go = new GameObject($"Ring_{stage.InnerFraction:0.00}-{stage.OuterFraction:0.00}");
                go.transform.SetParent(root.transform, false);

                // A stage reaching the centre is a disc, not a ring; a tiny inner radius keeps the
                // same triangle-strip mesh working for both without a special case.
                float inner = Mathf.Max(stage.InnerFraction * WorldArenaRadius, 0.001f);
                float outer = stage.OuterFraction * WorldArenaRadius;

                go.AddComponent<MeshFilter>().sharedMesh = ViewPrimitives.CreateAnnulus(inner, outer);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _hazardRings.Add(renderer);
            }
        }

        /// <summary>
        /// Shows the rings that are dealing damage right now, and - during Planning only - the one
        /// that will light up next cycle. The design calls for that stage to be telegraphed a cycle
        /// ahead so the player can plan a move out of it.
        /// </summary>
        public void Sync(MatchState state)
        {
            if (_hazardRings.Count == 0 || Palette == null) return;

            var upcoming = HazardSystem.UpcomingStage(state.Cycle);
            bool telegraphing = state.Phase == MatchPhase.Planning && upcoming.HasValue;

            for (int i = 0; i < _hazardRings.Count && i < HazardSystem.Schedule.Count; i++)
            {
                var stage = HazardSystem.Schedule[i];
                var renderer = _hazardRings[i];

                bool active = state.Cycle >= stage.ActivateCycle;
                bool warned = telegraphing && stage.ActivateCycle == upcoming.Value.ActivateCycle;

                renderer.enabled = active || warned;
                if (renderer.enabled)
                    renderer.sharedMaterial = active ? Palette.HazardActive : Palette.HazardTelegraph;
            }
        }
    }
}
