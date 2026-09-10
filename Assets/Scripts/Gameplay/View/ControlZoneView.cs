using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The red wedge painted on the sand in front of the player's gladiator while he is being given
    /// his orders: the ground his weapon covers.
    ///
    /// A picture of a rule the simulation enforces rather than a helper drawn beside it - the swing
    /// arc that decides whether a blow lands at all. A man outside the red is a man who does not get
    /// hit.
    ///
    /// There used to be a green arc under it as well, the ground he could be sent onto. It went
    /// when the arena got obstacles and a tap became an order to go anywhere: with no limit on where
    /// he can run there is nothing for it to draw.
    ///
    /// Rebuilt only when the wedge's shape changes, which is when he picks a different weapon off
    /// the sand - during a planning phase, never - so in the ordinary case this allocates nothing.
    /// </summary>
    public class ControlZoneView : MonoBehaviour
    {
        /// <summary>How high off the sand the wedge sits. Under the trajectory, over the hazard.</summary>
        private const float StrikeZoneHeight = 0.043f;

        private ArenaView _arena;
        private Transform _root;
        private MeshFilter _strike;
        private MeshRenderer _strikeRenderer;

        private float _builtReach = -1f, _builtSwing = -1f;

        public void Bind(ArenaView arena)
        {
            _arena = arena;
            if (_root != null) return;

            var palette = arena != null ? arena.Palette : null;

            var root = new GameObject("ControlZone");
            root.transform.SetParent(arena != null ? arena.transform : transform, false);
            _root = root.transform;

            var go = new GameObject("StrikeZone");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, StrikeZoneHeight, 0f);

            _strike = go.AddComponent<MeshFilter>();
            _strikeRenderer = go.AddComponent<MeshRenderer>();
            _strikeRenderer.sharedMaterial = palette != null ? palette.StrikeZone : null;
            _strikeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _strikeRenderer.receiveShadows = false;

            SetVisible(false);
        }

        /// <summary>
        /// Shows the wedge for this gladiator, or hides it when there is nobody to show it for.
        ///
        /// Called every frame of planning and nothing else: it describes a decision being made, and
        /// left up while the gladiators run it would describe one already carried out.
        /// </summary>
        public void Sync(GladiatorInstance g, bool visible)
        {
            if (_root == null || _arena == null) return;

            if (!visible || g == null || !g.Alive)
            {
                SetVisible(false);
                return;
            }

            Rebuild(g.WeaponDef);

            // Built about +Z, so pointing the root at his heading points the wedge.
            _root.position = _arena.ToWorld(g.Pos);
            _root.rotation = Quaternion.LookRotation(ToWorldDirection(g.Facing), Vector3.up);
            SetVisible(true);
        }

        /// <summary>
        /// A heading on the simulation's plane as a direction in the world. The scale between the
        /// two is uniform - the arena's elongation lives in the simulation's own coordinates rather
        /// than in the conversion - so a heading is the same heading in both.
        /// </summary>
        private static Vector3 ToWorldDirection(Vector2 facing)
        {
            var direction = new Vector3(facing.x, 0f, facing.y);
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
        }

        private void Rebuild(WeaponDef weapon)
        {
            float reach = _arena.ScaleLength(weapon.Reach);
            if (Mathf.Approximately(reach, _builtReach)
                && Mathf.Approximately(weapon.SwingArcDegrees, _builtSwing))
                return;

            // From his own edge rather than from his feet: the middle of it is where he is standing,
            // and a wedge drawn through him reads as a stain rather than as reach.
            _strike.sharedMesh = ViewPrimitives.CreateAnnulusSector(
                _arena.ScaleLength(GameConstants.GladiatorRadius * 0.6f), reach, weapon.SwingArcDegrees);
            _builtReach = reach;
            _builtSwing = weapon.SwingArcDegrees;
        }

        private void SetVisible(bool visible)
        {
            if (_strikeRenderer != null) _strikeRenderer.enabled = visible;
        }

        /// <summary>Whether the wedge is currently painted. For tests and for the screenshot rig.</summary>
        public bool IsShowing => _strikeRenderer != null && _strikeRenderer.enabled;
    }
}
