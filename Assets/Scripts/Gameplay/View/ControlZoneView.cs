using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The two zones painted on the sand around the player's gladiator while he is being given his
    /// orders: the green arc of ground he can be sent onto, and the red wedge his weapon covers.
    ///
    /// They are a picture of rules the simulation enforces, not a helper drawn beside them. The
    /// green arc is MoveEnvelope, which GameManager clamps every order through - including the
    /// bot's - and the red wedge is the swing arc that decides whether a blow lands at all. Neither
    /// is advice: a point outside the green is somewhere he will not go, and a man outside the red
    /// is a man who does not get hit.
    ///
    /// Both are rebuilt only when their shape actually changes. The green one moves with him and
    /// changes size with his speed and with an armed ability; the red one changes when he picks a
    /// different weapon off the sand, which during a planning phase is never - so in the ordinary
    /// case this allocates nothing per frame.
    /// </summary>
    public class ControlZoneView : MonoBehaviour
    {
        /// <summary>How high off the sand each layer sits. Under the trajectory, over the hazard.</summary>
        private const float MoveZoneHeight = 0.033f;
        private const float MoveEdgeHeight = 0.038f;
        private const float StrikeZoneHeight = 0.043f;

        /// <summary>Thickness of the bright rim on the far edge of the green arc, as a fraction.</summary>
        private const float EdgeThickness = 0.055f;

        private ArenaView _arena;
        private Transform _root;
        private MeshFilter _move, _moveEdge, _strike;
        private MeshRenderer _moveRenderer, _moveEdgeRenderer, _strikeRenderer;

        private float _builtOuter = -1f, _builtArc = -1f;
        private float _builtReach = -1f, _builtSwing = -1f;

        public void Bind(ArenaView arena)
        {
            _arena = arena;
            if (_root != null) return;

            var palette = arena != null ? arena.Palette : null;

            var root = new GameObject("ControlZone");
            root.transform.SetParent(arena != null ? arena.transform : transform, false);
            _root = root.transform;

            _move = Layer("MoveZone", palette != null ? palette.MoveZone : null,
                MoveZoneHeight, out _moveRenderer);
            _moveEdge = Layer("MoveZoneEdge", palette != null ? palette.MoveZoneEdge : null,
                MoveEdgeHeight, out _moveEdgeRenderer);
            _strike = Layer("StrikeZone", palette != null ? palette.StrikeZone : null,
                StrikeZoneHeight, out _strikeRenderer);

            SetVisible(false);
        }

        private MeshFilter Layer(string name, Material material, float height, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);

            var filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        /// <summary>
        /// Shows the zones for this gladiator, or hides them when there is nobody to show them for.
        ///
        /// Called every frame of planning and nothing else: the zones describe a decision being
        /// made, and left up while the gladiators run they would describe one already carried out.
        /// </summary>
        public void Sync(GladiatorInstance g, bool visible)
        {
            if (_root == null || _arena == null) return;

            if (!visible || g == null || !g.Alive)
            {
                SetVisible(false);
                return;
            }

            var envelope = MoveEnvelope.For(g);
            Rebuild(envelope, g.WeaponDef);

            // The whole assembly is placed and turned once; the three meshes are all built about
            // +Z, so pointing the root at his heading points all of them.
            _root.position = _arena.ToWorld(g.Pos);
            _root.rotation = Quaternion.LookRotation(ToWorldDirection(envelope.Facing), Vector3.up);
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

        private void Rebuild(MoveEnvelope envelope, WeaponDef weapon)
        {
            float outer = _arena.ScaleLength(envelope.OuterRadius);
            float inner = _arena.ScaleLength(envelope.InnerRadius);
            float arc = envelope.ArcDegrees;

            if (!Mathf.Approximately(outer, _builtOuter) || !Mathf.Approximately(arc, _builtArc))
            {
                // The far edge draws in towards the sides, because a dash spent bending round
                // covers less ground than a dash spent going straight. Sampled off the envelope
                // itself rather than reproduced here, so the picture and the rule cannot disagree.
                float scale = outer / Mathf.Max(envelope.OuterRadius, 0.0001f);
                System.Func<float, float> edge = turn => envelope.ReachAtTurn(turn) * scale;

                _move.sharedMesh = ViewPrimitives.CreateAnnulusSector(inner, edge, arc);
                _moveEdge.sharedMesh = ViewPrimitives.CreateAnnulusSector(
                    turn => edge(turn) * (1f - EdgeThickness), edge, arc);
                _builtOuter = outer;
                _builtArc = arc;
            }

            float reach = _arena.ScaleLength(weapon.Reach);
            if (!Mathf.Approximately(reach, _builtReach)
                || !Mathf.Approximately(weapon.SwingArcDegrees, _builtSwing))
            {
                // From his own edge rather than from his feet: the middle of it is where he is
                // standing, and a wedge drawn through him reads as a stain rather than as reach.
                _strike.sharedMesh = ViewPrimitives.CreateAnnulusSector(
                    _arena.ScaleLength(GameConstants.GladiatorRadius * 0.6f), reach,
                    weapon.SwingArcDegrees);
                _builtReach = reach;
                _builtSwing = weapon.SwingArcDegrees;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_moveRenderer != null) _moveRenderer.enabled = visible;
            if (_moveEdgeRenderer != null) _moveEdgeRenderer.enabled = visible;
            if (_strikeRenderer != null) _strikeRenderer.enabled = visible;
        }

        /// <summary>Whether the zones are currently painted. For tests and for the screenshot rig.</summary>
        public bool IsShowing => _moveRenderer != null && _moveRenderer.enabled;
    }
}
