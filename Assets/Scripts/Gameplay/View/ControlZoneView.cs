using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The wedge painted on the sand in front of the player's gladiator while he is being given his
    /// orders - the ground his weapon covers - and the dashed circle round him at the length of his
    /// reach, which stays up while he carries the orders out as well.
    ///
    /// Both white and see-through. The wedge was a flat red; it is a white light fading in from the
    /// man towards the edge of his reach now, and the circle is a white dashed line, so the sand and
    /// the fight both show through them.
    ///
    /// A picture of a rule the simulation enforces rather than a helper drawn beside it - the swing
    /// arc that decides whether a blow lands at all. A man outside the red is a man who does not get
    /// hit.
    ///
    /// There used to be a green arc under it as well, the ground he could be sent onto. It went
    /// when the arena got obstacles and a tap became an order to go anywhere: with no limit on where
    /// he can run there is nothing for it to draw.
    ///
    /// Rebuilt only when the wedge's shape changes - a different man, or Lunge or Trident Throw
    /// armed or wearing off - so in the ordinary case this allocates nothing.
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

        /// <summary>How high off the sand the circle sits: just over the wedge, so the two never fight for a pixel.</summary>
        private const float RingHeight = 0.047f;

        /// <summary>How thick the circle's line is, in virtual units.</summary>
        private const float RingWidth = 3f;

        /// <summary>One dash and the gap after it, in virtual units along the circle.</summary>
        private const float DashPeriod = 20f;

        /// <summary>How much of each period is dash.</summary>
        private const float DashFill = 0.55f;

        private Transform _ringRoot;
        private MeshFilter _ring;
        private MeshRenderer _ringRenderer;
        private float _builtRingReach = -1f;

        /// <summary>How many dashes the circle is drawn in. For tests.</summary>
        public int RingDashes { get; private set; }

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

            // Its own root, not the wedge's: the wedge is turned to where he looks, and dashes turned
            // with him would crawl round the circle every time he did.
            var ringRoot = new GameObject("ReachRingRoot");
            ringRoot.transform.SetParent(arena != null ? arena.transform : transform, false);
            _ringRoot = ringRoot.transform;

            var ring = new GameObject("ReachRing");
            ring.transform.SetParent(_ringRoot, false);
            ring.transform.localPosition = new Vector3(0f, RingHeight, 0f);
            _ring = ring.AddComponent<MeshFilter>();
            _ringRenderer = ring.AddComponent<MeshRenderer>();
            _ringRenderer.sharedMaterial = palette != null ? palette.ReachRing : null;
            _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ringRenderer.receiveShadows = false;
            _ringRenderer.enabled = false;

            SetVisible(false);
        }

        /// <summary>
        /// Shows the wedge for this gladiator, or hides it when there is nobody to show it for.
        ///
        /// Called every frame of planning and nothing else: it describes a decision being made, and
        /// left up while the gladiators run it would describe one already carried out.
        /// </summary>
        public void Sync(GladiatorInstance g, bool visible, bool ringVisible = false)
        {
            if (_root == null || _arena == null) return;

            bool alive = g != null && g.Alive;
            SyncRing(g, ringVisible && alive);

            if (!visible || !alive)
            {
                SetVisible(false);
                return;
            }

            Rebuild(g.PlannedWeaponReach(), g.WeaponDef.SwingArcDegrees);

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

        /// <summary>
        /// To his reach rather than his weapon's: Lunge and Trident Throw lengthen it, and the wedge
        /// was drawn the weapon's length through both - through the rounds they were working and
        /// through the planning phase they were armed in, which is when it matters.
        /// </summary>
        private void Rebuild(float weaponReach, float swingDegrees)
        {
            float reach = _arena.ScaleLength(weaponReach);
            if (Mathf.Approximately(reach, _builtReach)
                && Mathf.Approximately(swingDegrees, _builtSwing))
                return;

            // From his own edge rather than from his feet: the middle of it is where he is standing,
            // and a wedge drawn through him reads as a stain rather than as reach.
            _strike.sharedMesh = ViewPrimitives.CreateAnnulusSector(
                _arena.ScaleLength(GameConstants.GladiatorRadius * 0.6f), reach, swingDegrees);
            _builtReach = reach;
            _builtSwing = swingDegrees;
        }

        private void SetVisible(bool visible)
        {
            if (_strikeRenderer != null) _strikeRenderer.enabled = visible;
        }

        /// <summary>Whether the wedge is currently painted. For tests and for the screenshot rig.</summary>
        public bool IsShowing => _strikeRenderer != null && _strikeRenderer.enabled;

        /// <summary>Whether the reach circle is drawn. For tests and for the screenshot rig.</summary>
        public bool IsRingShowing => _ringRenderer != null && _ringRenderer.enabled;

        /// <summary>
        /// The circle round him at the length of his reach - the reach his armed ability will give
        /// him while it is armed, and what he has while it works - kept on him wherever he runs.
        /// </summary>
        private void SyncRing(GladiatorInstance g, bool visible)
        {
            if (_ringRenderer == null) return;
            if (_ringRenderer.enabled != visible) _ringRenderer.enabled = visible;
            if (!visible) return;

            float reach = _arena.ScaleLength(g.PlannedWeaponReach());
            if (!Mathf.Approximately(reach, _builtRingReach))
            {
                _ring.sharedMesh = CreateDashedRing(reach, _arena.ScaleLength(RingWidth), _arena.ScaleLength(DashPeriod), out int dashes);
                RingDashes = dashes;
                _builtRingReach = reach;
            }
            _ringRoot.position = _arena.ToWorld(g.Pos);
        }

        /// <summary>
        /// A circle of this radius drawn as a dashed line this wide: the dashes an even length
        /// whatever the radius, a whole number of them so the join does not show, each a short arc.
        /// </summary>
        private static Mesh CreateDashedRing(float radius, float width, float period, out int dashes)
        {
            const int segmentsPerDash = 3;
            dashes = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / Mathf.Max(0.0001f, period)), 12, 120);

            int ringsPerDash = segmentsPerDash + 1;
            var vertices = new Vector3[dashes * ringsPerDash * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[dashes * segmentsPerDash * 6];
            float inner = Mathf.Max(0f, radius - width * 0.5f), outer = radius + width * 0.5f;
            float step = Mathf.PI * 2f / dashes;

            int v = 0, t = 0;
            for (int d = 0; d < dashes; d++)
            {
                int first = v;
                for (int s = 0; s < ringsPerDash; s++)
                {
                    float angle = step * (d + DashFill * s / segmentsPerDash);
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    vertices[v] = new Vector3(cos * inner, 0f, sin * inner);
                    vertices[v + 1] = new Vector3(cos * outer, 0f, sin * outer);
                    normals[v] = normals[v + 1] = Vector3.up;
                    uvs[v] = new Vector2(s / (float)segmentsPerDash, 0f);
                    uvs[v + 1] = new Vector2(s / (float)segmentsPerDash, 1f);
                    v += 2;
                }
                for (int s = 0; s < segmentsPerDash; s++)
                {
                    int a = first + s * 2, b = a + 1, c = a + 2, e = a + 3;
                    // Wound as the full ring is (see ViewPrimitives.CreateAnnulus): angles run from +X
                    // towards +Z, and this order faces up at the camera.
                    triangles[t++] = a; triangles[t++] = e; triangles[t++] = b;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = e;
                }
            }

            var mesh = new Mesh { name = $"ReachRing_{radius:0.00}" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
